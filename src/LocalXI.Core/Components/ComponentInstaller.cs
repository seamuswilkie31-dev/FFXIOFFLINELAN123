using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using LocalXI.Core.Util;

namespace LocalXI.Core.Components;

public sealed record InstallProgress(string ComponentId, string Message, double? Fraction = null);

public sealed class ComponentUnavailableException(string message) : Exception(message);

/// <summary>Downloads pinned components, verifies their SHA-256, and installs them under the runtime folder.</summary>
public sealed class ComponentInstaller(AppPaths paths, HttpClient http, string repository)
{
    private sealed record Marker(string Id, string Version, string Sha256);

    public string TargetPath(ComponentSpec spec) => Path.GetFullPath(Path.Combine(paths.Runtime, spec.InstallDir));

    public bool IsInstalled(ComponentSpec spec)
    {
        if (spec.Kind == ComponentKind.Installer)
        {
            return Prerequisites.IsSatisfied(spec.Detect);
        }

        var target = TargetPath(spec);
        if (spec.Kind == ComponentKind.File)
        {
            var file = Path.Combine(target, spec.FileName!);
            return File.Exists(file) && ReadMarker(spec) is { } fileMarker && Hashing.HashEquals(fileMarker.Sha256, spec.Sha256);
        }

        if (ReadMarker(spec) is not { } marker || !Hashing.HashEquals(marker.Sha256, spec.Sha256) || marker.Version != spec.Version)
        {
            return false;
        }

        var probe = spec.Probe is null ? target : Path.Combine(target, spec.Probe);
        return File.Exists(probe) || Directory.Exists(probe);
    }

    public async Task InstallAsync(ComponentSpec spec, IProgress<InstallProgress>? progress, CancellationToken cancellationToken)
    {
        if (!spec.IsPublished)
        {
            throw new ComponentUnavailableException(
                $"{spec.Name} has not been published yet, so there is nothing to download. " +
                "See docs/RELEASING.md, or point Settings > Developer at a local LandSandBoat build.");
        }

        var download = await DownloadAsync(spec, progress, cancellationToken);
        progress?.Report(new InstallProgress(spec.Id, $"Installing {spec.Name}..."));

        if (spec.Kind == ComponentKind.Installer)
        {
            await RunInstallerAsync(spec, download);
            WriteMarker(spec);
            progress?.Report(new InstallProgress(spec.Id, $"{spec.Name} installed.", 1));
            return;
        }

        await Task.Run(() =>
        {
            switch (spec.Kind)
            {
                case ComponentKind.File:
                    var target = TargetPath(spec);
                    Directory.CreateDirectory(target);
                    File.Copy(download, Path.Combine(target, spec.FileName!), overwrite: true);
                    break;
                case ComponentKind.Zip:
                    ExtractReplacing(spec, download);
                    break;
                case ComponentKind.ZipOverlay:
                    ExtractOverlay(spec, download);
                    break;
                default:
                    throw new UnreachableException();
            }
        }, cancellationToken);

        WriteMarker(spec);
        progress?.Report(new InstallProgress(spec.Id, $"{spec.Name} ready.", 1));
    }

    private async Task<string> DownloadAsync(ComponentSpec spec, IProgress<InstallProgress>? progress, CancellationToken cancellationToken)
    {
        var url = spec.ResolveUrl(repository);
        var extension = spec.FileName is not null ? Path.GetExtension(spec.FileName) : ".zip";
        var destination = Path.Combine(paths.Downloads, $"{spec.Id}-{spec.Version}{extension}");

        if (File.Exists(destination) && Hashing.HashEquals(await Hashing.Sha256FileAsync(destination, cancellationToken), spec.Sha256))
        {
            return destination;
        }

        Directory.CreateDirectory(paths.Downloads);
        var partial = destination + ".part";
        progress?.Report(new InstallProgress(spec.Id, $"Downloading {spec.Name}...", 0));

        using (var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
        {
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength;

            await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var file = File.Create(partial);
            using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

            var buffer = new byte[1 << 16];
            long received = 0;
            var lastReport = Stopwatch.StartNew();
            int read;
            while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
            {
                await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                sha.AppendData(buffer, 0, read);
                received += read;

                if (lastReport.ElapsedMilliseconds > 250)
                {
                    lastReport.Restart();
                    var fraction = total > 0 ? (double)received / total.Value : (double?)null;
                    progress?.Report(new InstallProgress(spec.Id, $"Downloading {spec.Name}... {FormatSize(received)}{(total > 0 ? " of " + FormatSize(total.Value) : "")}", fraction));
                }
            }

            var actual = Convert.ToHexStringLower(sha.GetHashAndReset());
            if (!Hashing.HashEquals(actual, spec.Sha256))
            {
                file.Close();
                File.Delete(partial);
                throw new InvalidDataException(
                    $"The download of {spec.Name} did not match its expected fingerprint, so it was discarded.\n" +
                    $"Expected {spec.Sha256}\nReceived {actual}\nURL {url}");
            }
        }

        File.Move(partial, destination, overwrite: true);
        return destination;
    }

    private static async Task RunInstallerAsync(ComponentSpec spec, string installer)
    {
        // These installers write to Program Files, so they run elevated; Windows shows its permission prompt.
        var startInfo = new ProcessStartInfo(installer)
        {
            UseShellExecute = true,
            Verb = "runas",
            Arguments = spec.InstallArguments ?? "",
        };

        int exitCode;
        try
        {
            using var process = Process.Start(startInfo) ?? throw new InvalidOperationException($"Could not start the {spec.Name} installer.");
            await process.WaitForExitAsync();
            exitCode = process.ExitCode;
        }
        catch (System.ComponentModel.Win32Exception e) when (e.NativeErrorCode == 1223)
        {
            throw new OperationCanceledException($"Installing {spec.Name} was cancelled at the Windows permission prompt.");
        }

        // 3010/1641: installed but wants a restart. 1638: a newer version is already installed.
        if (exitCode is not (0 or 3010 or 1641 or 1638))
        {
            throw new InvalidOperationException($"The {spec.Name} installer failed (exit code {exitCode}).");
        }

        if (!Prerequisites.IsSatisfied(spec.Detect))
        {
            throw new InvalidOperationException($"{spec.Name} still isn't detected after installing. Restart Windows and try again.");
        }
    }

    private void ExtractReplacing(ComponentSpec spec, string zipPath)
    {
        var target = TargetPath(spec);
        var staging = target + ".new";
        var previous = target + ".old";
        DeleteDirectory(staging);
        DeleteDirectory(previous);

        ZipFile.ExtractToDirectory(zipPath, staging);
        var root = spec.StripTopFolder ? SingleTopFolder(staging) : staging;

        // Carry folders such as logs, meshes and user config across the upgrade.
        if (Directory.Exists(target))
        {
            foreach (var keep in spec.Preserve)
            {
                var from = Path.Combine(target, keep);
                if (!Directory.Exists(from))
                {
                    continue;
                }

                var to = Path.Combine(root, keep);
                DeleteDirectory(to);
                Directory.Move(from, to);
            }

            Directory.Move(target, previous);
        }

        Directory.Move(root, target);
        DeleteDirectory(staging);
        DeleteDirectory(previous);
    }

    private void ExtractOverlay(ComponentSpec spec, string zipPath)
    {
        var target = TargetPath(spec);
        Directory.CreateDirectory(target);
        if (!spec.StripTopFolder)
        {
            ZipFile.ExtractToDirectory(zipPath, target, overwriteFiles: true);
            return;
        }

        var staging = target + "." + spec.Id + ".new";
        DeleteDirectory(staging);
        ZipFile.ExtractToDirectory(zipPath, staging);
        CopyDirectory(SingleTopFolder(staging), target);
        DeleteDirectory(staging);
    }

    private static string SingleTopFolder(string directory)
    {
        var entries = Directory.GetFileSystemEntries(directory);
        return entries.Length == 1 && Directory.Exists(entries[0])
            ? entries[0]
            : throw new InvalidDataException($"Expected a single top-level folder in the archive extracted to {directory}.");
    }

    private static void CopyDirectory(string source, string destination)
    {
        foreach (var dir in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, dir)));
        }

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            File.Copy(file, Path.Combine(destination, Path.GetRelativePath(source, file)), overwrite: true);
        }
    }

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private string MarkerPath(ComponentSpec spec) => Path.Combine(paths.ComponentMarkers, spec.Id + ".json");

    private Marker? ReadMarker(ComponentSpec spec)
    {
        try
        {
            var path = MarkerPath(spec);
            return File.Exists(path) ? JsonSerializer.Deserialize<Marker>(File.ReadAllText(path), JsonDefaults.Options) : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private void WriteMarker(ComponentSpec spec)
    {
        Directory.CreateDirectory(paths.ComponentMarkers);
        File.WriteAllText(MarkerPath(spec), JsonSerializer.Serialize(new Marker(spec.Id, spec.Version, spec.Sha256!), JsonDefaults.Options));
    }

    private static string FormatSize(long bytes) => bytes switch
    {
        >= 1L << 30 => $"{bytes / (double)(1L << 30):0.0} GB",
        >= 1L << 20 => $"{bytes / (double)(1L << 20):0.0} MB",
        _ => $"{bytes / 1024.0:0} KB",
    };
}
