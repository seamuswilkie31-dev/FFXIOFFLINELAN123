using System.Globalization;
using System.Text.RegularExpressions;

namespace LocalXI.Core.Server;

/// <summary>
/// Settings that can't travel as environment variables. LandSandBoat only types numeric environment overrides when
/// every character is a digit, so a rate like 2.5 has to go in a settings/&lt;name&gt;.lua override file instead.
/// </summary>
public static partial class ServerSettingsFiles
{
    public static readonly IReadOnlyList<double> ExpRates = [1.0, 1.5, 2.0, 2.5, 3.0];

    private const string Marker = "-- Written by LocalXI";

    /// <summary>
    /// Writes settings/map.lua as a copy of the defaults with EXP_RATE (combat experience) changed, or removes
    /// LocalXI's copy at 1x. A map.lua someone wrote by hand is left alone. Returns false if it couldn't apply.
    /// </summary>
    public static bool ApplyExpRate(string serverDir, double rate)
    {
        var target = Path.Combine(serverDir, "settings", "map.lua");
        var ours = !File.Exists(target) || File.ReadAllText(target).StartsWith(Marker, StringComparison.Ordinal);
        if (!ours)
        {
            return false;
        }

        if (Math.Abs(rate - 1.0) < 0.001)
        {
            File.Delete(target);
            return true;
        }

        var source = Path.Combine(serverDir, "settings", "default", "map.lua");
        if (!File.Exists(source))
        {
            return false;
        }

        var defaults = File.ReadAllText(source);
        if (!ExpRateLine().IsMatch(defaults))
        {
            return false;
        }

        var value = rate.ToString("0.0##", CultureInfo.InvariantCulture);
        var text = ExpRateLine().Replace(defaults, m => m.Groups[1].Value + value, 1);
        File.WriteAllText(target, $"{Marker}: combat EXP x{value}. Removed again when the rate is set back to 1x.\n{text}");
        return true;
    }

    [GeneratedRegex(@"^(\s*EXP_RATE\s*=\s*)[0-9.]+", RegexOptions.Multiline)]
    private static partial Regex ExpRateLine();
}
