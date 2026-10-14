using System.Windows;
using System.Windows.Threading;
using LocalXI.App.ViewModels;
using LocalXI.Core;

namespace LocalXI.App;

public partial class App : Application
{
    private Mutex? _singleInstance;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // An elevated copy started for one admin task (registry, firewall): do it and exit without any UI.
        if (e.Args.Length > 0 && e.Args[0] == Elevation.Flag)
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            Shutdown(await Elevation.ExecuteAsync(e.Args));
            return;
        }

        // Two launchers driving one database would fight; bring the user back to the first one instead.
        _singleInstance = new Mutex(initiallyOwned: true, @"Local\LocalXI-Launcher", out var isFirst);
        if (!isFirst)
        {
            MessageBox.Show("LocalXI is already running.", "LocalXI", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        DispatcherUnhandledException += OnUnhandledException;

        var launcher = new LauncherService(AppPaths.Resolve());
        var window = new MainWindow { DataContext = new MainViewModel(launcher) };
        MainWindow = window;
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _singleInstance?.Dispose();
        base.OnExit(e);
    }

    private static void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(e.Exception.Message, "LocalXI - something went wrong", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
