using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace LocalXI.Core.Util;

public static partial class Secrets
{
    // No look-alike characters, so a password read aloud to a friend survives.
    private const string Letters = "abcdefghijkmnopqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ";
    private const string LettersAndDigits = Letters + "23456789";

    /// <summary>
    /// Always starts with a letter: LandSandBoat reads settings from environment variables and turns
    /// all-digit values into numbers, which would corrupt a numeric password.
    /// </summary>
    public static string NewPassword(int length = 24) =>
        RandomNumberGenerator.GetString(Letters, 1) + RandomNumberGenerator.GetString(LettersAndDigits, length - 1);

    /// <summary>Server accounts allow 1-16 printable characters; we also keep them safe to pass on a command line.</summary>
    public static bool IsValidUsername(string username) => UsernamePattern().IsMatch(username);

    /// <summary>
    /// Server passwords allow up to 32 printable characters. We stay narrower: spaces and quotes would break the
    /// loader's command line, and ';' or '#' could be read as a comment in Ashita's INI file.
    /// </summary>
    public static bool IsValidPassword(string password) => PasswordPattern().IsMatch(password);

    [GeneratedRegex("^[A-Za-z0-9_]{3,16}$")]
    private static partial Regex UsernamePattern();

    [GeneratedRegex(@"^[A-Za-z0-9!@$^*_+=.,?~-]{6,32}$")]
    private static partial Regex PasswordPattern();
}
