using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace Lumo
{
    /// <summary>
    /// Rozpozná, jestli Lumo běží jako balíček MSIX (Microsoft Store), a obslouží autostart přes StartupTask.
    /// V balíčku nefunguje klíč Run v registru ani spouštění jako správce.
    /// </summary>
    public static class Packaging
    {
        private const string StartupTaskId = "LumoStartup";

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetCurrentPackageFullName(ref int length, System.Text.StringBuilder? name);

        private static bool? _isPackaged;

        public static bool IsPackaged
        {
            get
            {
                if (_isPackaged.HasValue) return _isPackaged.Value;
                try
                {
                    int len = 0;
                    int rc = GetCurrentPackageFullName(ref len, null);
                    _isPackaged = rc != 15700; // APPMODEL_ERROR_NO_PACKAGE
                }
                catch { _isPackaged = false; }
                return _isPackaged.Value;
            }
        }

        public static bool StartupEnabled
        {
            get
            {
                try
                {
                    return Task.Run(async () =>
                    {
                        var t = await Windows.ApplicationModel.StartupTask.GetAsync(StartupTaskId);
                        return t.State is Windows.ApplicationModel.StartupTaskState.Enabled
                                       or Windows.ApplicationModel.StartupTaskState.EnabledByPolicy;
                    }).GetAwaiter().GetResult();
                }
                catch (Exception ex) { Log.Write("StartupTask (čtení): " + ex.Message); return false; }
            }
            set
            {
                try
                {
                    Task.Run(async () =>
                    {
                        var t = await Windows.ApplicationModel.StartupTask.GetAsync(StartupTaskId);
                        if (value)
                        {
                            var st = await t.RequestEnableAsync();
                            if (st == Windows.ApplicationModel.StartupTaskState.DisabledByUser)
                                Log.Write("Autostart vypnul uživatel ve Správci úloh – zapnout jde jen tam.");
                        }
                        else t.Disable();
                    }).GetAwaiter().GetResult();
                }
                catch (Exception ex) { Log.Write("StartupTask (změna): " + ex.Message); }
            }
        }
    }
}
