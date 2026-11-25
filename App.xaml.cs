using System;
using System.Linq;
using System.Threading;
using System.Windows;

namespace Lumo
{
    public partial class App : Application
    {
        private const string MutexName = "Lumo_Bolek98_SingleInstance";
        private static Mutex? _mutex;
        private TrayIcon? _tray;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            bool restart = e.Args.Contains("--restart");
            _mutex = new Mutex(true, MutexName, out bool isNew);
            if (!isNew)
            {
                bool got = false;
                if (restart)
                {
                    // čekáme, až se stará instance ukončí
                    try { got = _mutex.WaitOne(TimeSpan.FromSeconds(10)); }
                    catch (AbandonedMutexException) { got = true; }
                }
                if (!got)
                {
                    _mutex.Dispose();
                    _mutex = null;
                    Shutdown();
                    return;
                }
            }

            DispatcherUnhandledException += (_, args) =>
            {
                Log.Write("Neošetřená chyba: " + args.Exception);
                args.Handled = true;
            };

            Settings.MigrateFromOstrovek();
            var settings = Settings.Load();
            var window = new MainWindow(settings);
            _tray = new TrayIcon(window, settings);
            window.Show();
        }

        public static void ReleaseSingleInstance()
        {
            try { _mutex?.ReleaseMutex(); } catch { }
            _mutex?.Dispose();
            _mutex = null;
        }

        public static void ReacquireSingleInstance()
        {
            try { _mutex ??= new Mutex(true, MutexName, out _); } catch { }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _tray?.Dispose();
            ReleaseSingleInstance();
            base.OnExit(e);
        }
    }
}
