using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;

namespace Lumo
{
    /// <summary>Verze 4: okno Nastavení a použití nastavení vzhledu / záložek.</summary>
    public partial class MainWindow
    {
        private const double BaseWindowW = 560, BaseWindowH = 260;
        private SettingsWindow? _settingsWin;

        public void OpenSettings()
        {
            if (_settingsWin != null)
            {
                _settingsWin.Activate();
                return;
            }
            _settingsWin = new SettingsWindow(this);
            _settingsWin.Closed += (_, _) => _settingsWin = null;
            _settingsWin.Show();
            _settingsWin.Activate();
        }

        /// <summary>Po změně v Nastavení – použije všechno najednou.</summary>
        public void ApplySettings()
        {
            ApplyAppearance();
            ApplyTabs();
            ApplyPosition();
            ApplyLayer();
            UpdateMedia();
            UpdateCompactWeather();
            if (_expanded) ShowPage(_page, animate: false);
            else AnimateIsland(_media.HasMedia ? CompactWMedia : CompactWIdle, CompactH, CompactH / 2, false);
        }

        private Color CurrentAccentColor()
        {
            if (_s.AccentMode == "Fixed")
            {
                try { return (Color)ColorConverter.ConvertFromString(_s.FixedColor); }
                catch { }
            }
            return _media.ArtColor ?? DefaultAccent;
        }

        private void ApplyAppearance()
        {
            double op = Math.Clamp(_s.BackgroundOpacity, 0.5, 1.0);
            Island.Background = new SolidColorBrush(Color.FromArgb((byte)Math.Round(op * 255), 5, 5, 7));

            double sc = Math.Clamp(_s.Scale, 0.7, 1.4);
            Root.LayoutTransform = Math.Abs(sc - 1) < 0.001 ? Transform.Identity : new ScaleTransform(sc, sc);
            Width = BaseWindowW * sc;
            Height = BaseWindowH * sc;
        }

        private void ApplyTabs()
        {
            var map = new (string Key, FrameworkElement Tab, Page Page)[]
            {
                ("Music", TabMusic, Page.Music), ("System", TabSystem, Page.System), ("Weather", TabWeather, Page.Weather),
                ("Widgets", TabWidgets, Page.Widgets), ("Tools", TabTools, Page.Tools)
            };
            Page? firstVisible = null;
            bool currentHidden = false;
            foreach (var (key, tab, page) in map)
            {
                bool hidden = _s.HiddenTabs.Contains(key);
                tab.Visibility = hidden ? Visibility.Collapsed : Visibility.Visible;
                if (!hidden && firstVisible == null) firstVisible = page;
                if (hidden && page == _page) currentHidden = true;
            }
            if (currentHidden && firstVisible != null) _page = firstVisible.Value;
        }

        public async Task RefreshWeatherNow() => await RefreshWeatherAsync();

        public IReadOnlyList<BatteryService.Device> BatteryDevices => _battery.Devices;

        /// <summary>Krátký náhled pro Nastavení: dnes / zítra / další směna.</summary>
        public string ShiftPreview()
        {
            var today = DateTime.Today;
            string t = ShiftName(ShiftOn(today)), z = ShiftName(ShiftOn(today.AddDays(1)));
            var next = CurrentOrNextShift(DateTime.Now);
            string n = next == null ? "" : $" · nejbližší: {(next.Value.Kind == 'R' ? "denní" : "noční")} {next.Value.Start.ToString("ddd d. M. H:mm", CsCz)}";
            return $"Dnes: {t} · zítra: {z}{n}";
        }
    }
}
