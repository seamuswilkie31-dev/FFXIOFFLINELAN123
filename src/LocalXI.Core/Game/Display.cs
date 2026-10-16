using System.Runtime.InteropServices;

namespace LocalXI.Core.Game;

public static partial class Display
{
    private const int ScreenWidth = 0;  // SM_CXSCREEN
    private const int ScreenHeight = 1; // SM_CYSCREEN

    public static readonly IReadOnlyList<int> UiScales = [100, 125, 150, 175, 200, 250, 300];

    // The game's menus were laid out for a 640x480 screen; below 480 lines they no longer fit.
    private const int MinMenuHeight = 480;

    /// <summary>The primary monitor's resolution in physical pixels (the launcher is per-monitor DPI aware).</summary>
    public static (int Width, int Height) PrimaryResolution() => (GetSystemMetrics(ScreenWidth), GetSystemMetrics(ScreenHeight));

    /// <summary>
    /// The game draws its menus at the "menu resolution" (ffxi.registry 0037/0038) and stretches them to the window,
    /// so a menu resolution smaller than the window makes the interface bigger. Keeps the window's shape, never goes
    /// below 480 lines, and stays even.
    /// </summary>
    public static (int Width, int Height) MenuResolution(int windowWidth, int windowHeight, int uiScalePercent)
    {
        var scale = Math.Clamp(uiScalePercent, 100, 300) / 100.0;
        var height = Math.Max(windowHeight / scale, Math.Min(windowHeight, MinMenuHeight));
        var width = windowWidth * height / windowHeight;
        return ((int)Math.Round(width / 2) * 2, (int)Math.Round(height / 2) * 2);
    }

    [LibraryImport("user32.dll")]
    private static partial int GetSystemMetrics(int index);
}
