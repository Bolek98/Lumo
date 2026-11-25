using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Microsoft.Win32;

namespace Lumo
{
    public enum IslandPosition { Top, Bottom }
    public enum LayerMode { Desktop, AlwaysOnTop }

    public class Settings
    {
        public IslandPosition Position { get; set; } = IslandPosition.Top;
        /// <summary>Desktop = jen na ploše (okna programů ho překryjí), AlwaysOnTop = vždy navrchu.</summary>
        public LayerMode Layer { get; set; } = LayerMode.Desktop;
        public bool HideInFullscreen { get; set; } = true;
        public bool PopOnNewTrack { get; set; } = true;
        /// <summary>Posun ostrůvku do strany od středu obrazovky (po přetažení myší).</summary>
        public double OffsetX { get; set; } = 0;

        /// <summary>Den, kdy začíná cyklus směn (první denní). R = denní, N = noční, _ = volno.</summary>
        public string ShiftAnchor { get; set; } = "2026-11-24";
        public string ShiftPattern { get; set; } = "RRNN____";

        /// <summary>Začátek denní a noční směny (pro odpočet a připomínku).</summary>
        public string ShiftDayStart { get; set; } = "07:00";
        public string ShiftNightStart { get; set; } = "19:00";
        public int ShiftTimesVersion { get; set; } = 0;
        /// <summary>Kolik minut předem připomenout směnu (0 = vypnuto).</summary>
        public int ShiftRemindMinutes { get; set; } = 60;

        /// <summary>Počasí – výchozí Kladno.</summary>
        public string WeatherPlace { get; set; } = "Kladno";
        public double WeatherLat { get; set; } = 50.1473;
        public double WeatherLon { get; set; } = 14.1029;
        public bool WeatherInCompact { get; set; } = true;

        /// <summary>Upozornit, když baterie zařízení klesne pod tuto hodnotu.</summary>
        public int BatteryWarnPercent { get; set; } = 20;

        /// <summary>Poslední otevřená záložka (Music / System / Weather / Widgets / Tools).</summary>
        public string LastPage { get; set; } = "Music";

        // ---- vzhled ----
        /// <summary>Album = barva z obalu alba, Fixed = vlastní pevná barva.</summary>
        public string AccentMode { get; set; } = "Album";
        public string FixedColor { get; set; } = "#8C9CFF";
        /// <summary>Krytí pozadí ostrůvku 0.5–1.0.</summary>
        public double BackgroundOpacity { get; set; } = 0.96;
        /// <summary>Velikost: 0.85 malý, 1.0 normální, 1.15 velký.</summary>
        public double Scale { get; set; } = 1.0;

        /// <summary>Skryté záložky (Music, System, Weather, Widgets, Tools).</summary>
        public List<string> HiddenTabs { get; set; } = new();

        public static string Folder =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Lumo");
        private static string OldFolder =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Ostrovek");

        /// <summary>Převod dat ze starého Ostrůvku (nastavení, poznámky, přihrádka) a autostartu.</summary>
        public static void MigrateFromOstrovek()
        {
            try
            {
                if (!Directory.Exists(Folder) && Directory.Exists(OldFolder))
                {
                    Directory.CreateDirectory(Folder);
                    foreach (var f in new[] { "settings.json", "poznamky.txt", "nastroje.json" })
                    {
                        var src = Path.Combine(OldFolder, f);
                        if (File.Exists(src)) File.Copy(src, Path.Combine(Folder, f), false);
                    }
                    Log.Write("Data převedena z Ostrůvku.");
                }
                if (Packaging.IsPackaged) return;
                using var key = Registry.CurrentUser.CreateSubKey(RunKey);
                if (key.GetValue("Ostrovek") is string)
                {
                    key.DeleteValue("Ostrovek", false);
                    key.SetValue(RunName, "\"" + Environment.ProcessPath + "\"");
                }
            }
            catch (Exception ex) { Log.Write("Převod z Ostrůvku: " + ex.Message); }
        }
        private static string FilePath => Path.Combine(Folder, "settings.json");

        public static Settings Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    var st = JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new Settings();
                    // v3.0 měla omylem 6:00/18:00 – směny jsou 7–19 a 19–7
                    if (st.ShiftTimesVersion < 2 && st.ShiftDayStart == "06:00" && st.ShiftNightStart == "18:00")
                    {
                        st.ShiftDayStart = "07:00";
                        st.ShiftNightStart = "19:00";
                    }
                    st.ShiftTimesVersion = 2;
                    return st;
                }
            }
            catch (Exception ex) { Log.Write("Nastavení nešlo načíst: " + ex.Message); }
            return new Settings();
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Folder);
                File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception ex) { Log.Write("Nastavení nešlo uložit: " + ex.Message); }
        }

        // ---- Spouštění s Windows (HKCU\...\Run) ----
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string RunName = "Lumo";

        public static bool StartWithWindows
        {
            get
            {
                if (Packaging.IsPackaged) return Packaging.StartupEnabled;
                using var key = Registry.CurrentUser.OpenSubKey(RunKey);
                return key?.GetValue(RunName) is string;
            }
            set
            {
                if (Packaging.IsPackaged) { Packaging.StartupEnabled = value; return; }
                using var key = Registry.CurrentUser.CreateSubKey(RunKey);
                if (value)
                    key.SetValue(RunName, "\"" + Environment.ProcessPath + "\"");
                else
                    key.DeleteValue(RunName, false);
            }
        }
    }

    public static class Log
    {
        public static void Write(string msg)
        {
            try
            {
                Directory.CreateDirectory(Settings.Folder);
                File.AppendAllText(Path.Combine(Settings.Folder, "log.txt"),
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {msg}{Environment.NewLine}");
            }
            catch { }
        }
    }
}
