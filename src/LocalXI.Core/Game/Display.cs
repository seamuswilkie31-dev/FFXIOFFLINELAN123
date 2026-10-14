using System.Runtime.InteropServices;

namespace LocalXI.Core.Game;

public static partial class Display
{
    private const int ScreenWidth = 0;  // SM_CXSCREEN
    private const int ScreenHeight = 1; // SM_CYSCREEN

    /// <summary>The primary monitor's resolution in physical pixels (the launcher is per-monitor DPI aware).</summary>
    public static (int Width, int Height) PrimaryResolution() => (GetSystemMetrics(ScreenWidth), GetSystemMetrics(ScreenHeight));

    [LibraryImport("user32.dll")]
    private static partial int GetSystemMetrics(int index);
}
