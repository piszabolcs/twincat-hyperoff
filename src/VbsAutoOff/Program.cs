using System;
using System.ServiceProcess;
using System.Windows;
using VbsAutoOff.Core;
using VbsAutoOff.Service;
namespace VbsAutoOff
{
    public static class Program
    {
        [STAThread]
        public static int Main(string[] args)
        {
            if (HasArg(args, "--service"))
            {
                ServiceBase.Run(new ServiceBase[] { new CleanupWorker() });
                return 0;
            }
            if (HasArg(args, "--watchdog"))
            {
                try
                {
                    VbsCleaner.RunPhase1(true);
                    if (!ServiceControl.IsInstalled() || ServiceControl.CurrentStatus() != "Running")
                    {
                        Logger.Log("Watchdog: the service was not running, automatic reactivation...");
                        ServiceControl.InstallAndStart();
                    }
                }
                catch (Exception ex)
                {
                    Logger.Log("Watchdog error: " + ex.Message);
                }
                return 0;
            }
            if (HasArg(args, "--run-once"))
            {
                VbsCleaner.RunPhase1(true);
                return 0;
            }
            if (HasArg(args, "--uninstall"))
            {
                var r = ServiceControl.Uninstall();
                return r.Ok ? 0 : 1;
            }

            // The helper window created with XamlReader during UI building (and never shown) kept the
            // process running after the main window was closed under the default OnLastWindowClose mode.
            // OnMainWindowClose + an explicit MainWindow fixes this.
            var app = new Application();
            app.ShutdownMode = ShutdownMode.OnMainWindowClose;
            app.DispatcherUnhandledException += delegate(object s, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
            {
                Logger.Log("UI error: " + e.Exception);
                MessageBox.Show("Unexpected error:\n\n" + e.Exception.Message + "\n\nDetails in the log: " + Logger.LogPath,
                    "VBS Auto Off", MessageBoxButton.OK, MessageBoxImage.Error);
                e.Handled = true;
            };
            Ui.MainWindow win;
            try
            {
                win = new Ui.MainWindow();
            }
            catch (Exception ex)
            {
                Logger.Log("Window creation error: " + ex);
                MessageBox.Show("The window failed to start:\n\n" + ex.Message + "\n\nDetails: " + Logger.LogPath,
                    "VBS Auto Off", MessageBoxButton.OK, MessageBoxImage.Error);
                return 1;
            }
            app.MainWindow = win;
            return app.Run(win);
        }

        private static bool HasArg(string[] args, string name)
        {
            if (args == null) return false;
            foreach (var a in args)
            {
                if (string.Equals(a, name, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }
    }
}

