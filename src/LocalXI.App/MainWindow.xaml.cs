using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using LocalXI.App.ViewModels;

namespace LocalXI.App;

public partial class MainWindow : Window
{
    private bool _shutdownComplete;

    public MainWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => UseDarkTitleBar();
    }

    protected override async void OnClosing(CancelEventArgs e)
    {
        if (_shutdownComplete || DataContext is not MainViewModel vm)
        {
            base.OnClosing(e);
            return;
        }

        var launcher = vm.Launcher;
        if (launcher.Game.IsGameRunning && launcher.Config.RunsLocalServer && launcher.Server.IsRunning &&
            MessageBox.Show(
                "The game is still running. Closing LocalXI stops the server and disconnects you. Close anyway?",
                "LocalXI", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            e.Cancel = true;
            return;
        }

        // Stop the server and database cleanly (with a backup) before the window goes away.
        e.Cancel = true;
        IsEnabled = false;
        vm.Status = "Saving and shutting down...";
        try
        {
            await launcher.DisposeAsync();
        }
        finally
        {
            _shutdownComplete = true;
            // DisposeAsync can finish synchronously (nothing was running), and WPF ignores Close() from inside the
            // Closing event, so close once this event has returned.
            _ = Dispatcher.BeginInvoke(new Action(Close));
        }
    }

    private void UseDarkTitleBar()
    {
        const int DwmUseImmersiveDarkMode = 20;
        var enabled = 1;
        DwmSetWindowAttribute(new WindowInteropHelper(this).Handle, DwmUseImmersiveDarkMode, ref enabled, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
