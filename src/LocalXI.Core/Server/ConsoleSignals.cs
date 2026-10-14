using System.Runtime.InteropServices;

namespace LocalXI.Core.Server;

/// <summary>
/// Sends Ctrl+C to a console program we started with a hidden console, which is how LandSandBoat expects to be
/// asked to shut down (saving characters on the way out). Killing it outright would skip that.
/// </summary>
internal static partial class ConsoleSignals
{
    private const uint CtrlCEvent = 0;
    private static readonly Lock Gate = new();
    private static bool _ignoringOwnCtrlC;

    public static bool TrySendCtrlC(int processId)
    {
        lock (Gate)
        {
            // The launcher has no console of its own; borrow the child's long enough to signal it.
            FreeConsole();
            if (!AttachConsole((uint)processId))
            {
                return false;
            }

            try
            {
                if (!_ignoringOwnCtrlC)
                {
                    // The signal goes to every process on the console, including us while attached. Ignore it for good.
                    SetConsoleCtrlHandler(IntPtr.Zero, true);
                    _ignoringOwnCtrlC = true;
                }

                return GenerateConsoleCtrlEvent(CtrlCEvent, 0);
            }
            finally
            {
                FreeConsole();
            }
        }
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AttachConsole(uint processId);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool FreeConsole();

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetConsoleCtrlHandler(IntPtr handler, [MarshalAs(UnmanagedType.Bool)] bool add);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GenerateConsoleCtrlEvent(uint ctrlEvent, uint processGroupId);
}
