using System.Windows;

namespace OrbitTweak;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += (_, ex) =>
        {
            MessageBox.Show(ex.Exception.Message, "OrbitTweak", MessageBoxButton.OK, MessageBoxImage.Warning);
            ex.Handled = true;
        };
        // after an in-app update give the old instance a moment to exit
        if (e.Args.Contains("--updated")) Thread.Sleep(1500);
        base.OnStartup(e);
    }
}
