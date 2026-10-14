using System.Security.Cryptography;

namespace LocalXI.Core.Util;

public static class Hashing
{
    public static async Task<string> Sha256FileAsync(string path, CancellationToken cancellationToken = default)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexStringLower(hash);
    }

    public static bool HashEquals(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
