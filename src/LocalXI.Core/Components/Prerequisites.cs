using Microsoft.Win32;

namespace LocalXI.Core.Components;

/// <summary>System-wide software the launcher can't ship inside its own folder, detected the way Microsoft documents.</summary>
public static class Prerequisites
{
    /// <summary>
    /// Evaluates a check such as "vcruntime-x86:14.44": the Visual C++ 2015-2026 runtime for that architecture,
    /// at least that version. xiloader and Ashita are 32-bit; the LandSandBoat server is 64-bit.
    /// </summary>
    public static bool IsSatisfied(string? check)
    {
        if (string.IsNullOrWhiteSpace(check))
        {
            return false;
        }

        var parts = check.Split(':', 2);
        var minimum = Version.Parse(parts.Length > 1 ? parts[1] : "14.0");
        return parts[0] switch
        {
            "vcruntime-x86" => VcRuntimeVersion("x86", RegistryView.Registry32) >= minimum,
            "vcruntime-x64" => VcRuntimeVersion("x64", RegistryView.Registry64) >= minimum,
            _ => false,
        };
    }

    // https://learn.microsoft.com/cpp/windows/redistributing-visual-cpp-files#install-the-redistributable-packages
    private static Version? VcRuntimeVersion(string architecture, RegistryView view)
    {
        using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
        using var key = hklm.OpenSubKey($@"SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\{architecture}");
        if (key?.GetValue("Installed") is not int installed || installed != 1 ||
            key.GetValue("Major") is not int major || key.GetValue("Minor") is not int minor)
        {
            return null;
        }

        return new Version(major, minor);
    }
}
