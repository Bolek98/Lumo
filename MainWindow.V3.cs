using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Media;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Lumo
{
    /// <summary>Verze 3: počasí, baterie, nástroje (přihrádka, schránka, spouštění), odpočet směny.</summary>
    public partial class MainWindow
    {
        private readonly WeatherService _weather = new();
        private readonly BatteryService _battery = new();
        private readonly ToolsStore _tools = ToolsStore.Load();
        private readonly List<string> _clips = new();
        private readonly HashSet<string> _batteryWarned = new();
        private readonly DispatcherTimer _weatherTimer = new() { Interval = TimeSpan.FromMinutes(15) };
        private readonly DispatcherTimer _batteryTimer = new() { Interval = TimeSpan.FromMinutes(2) };
        private DateTime _ignoreClipUntil = DateTime.MinValue;
        private DateTime _remindedFor = DateTime.MinValue;
        private DateTime _staleSince = DateTime.MinValue;

        private void InitV3()
        {
            // ---- počasí ----
            _weatherTimer.Tick += async (_, _) => await RefreshWeatherAsync();
            // ---- baterie ----
            _batteryTimer.Tick += async (_, _) => await RefreshBatteryAsync();

            Loaded += async (_, _) =>
            {
                try
                {
                    if (_hwnd != IntPtr.Zero) Native.AddClipboardFormatListener(_hwnd);
                }
                catch { }
                _weatherTimer.Start();
                _batteryTimer.Start();
                RebuildLaunchers();
                RebuildShelf();
                RebuildClips();
                await RefreshWeatherAsync();
                await RefreshBatteryAsync();
            };

            // ---- přetahování souborů na ostrůvek ----
            Island.DragEnter += (_, e) =>
            {
                if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
                _hover = true;
                _collapseDelay.Stop();
                if (!_expanded || _page != Page.Tools) { _page = Page.Tools; Expand(); }
            };
            Island.DragOver += (_, e) =>
            {
                e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
                e.Handled = true;
            };
            Island.Drop += OnFilesDropped;
        }

        // ================= počasí =================

        private async System.Threading.Tasks.Task RefreshWeatherAsync()
        {
            if (await _weather.RefreshAsync(_s.WeatherLat, _s.WeatherLon))
            {
                UpdateCompactWeather();
                if (!_expanded) AnimateIsland(_media.HasMedia ? CompactWMedia : CompactWIdle, CompactH, CompactH / 2, false);
                if (_expanded && _page == Page.Weather) RefreshWeatherPage();
            }
        }

        private void UpdateCompactWeather()
        {
            bool show = _weather.HasData && _s.WeatherInCompact && !_media.HasMedia;
            if (show)
            {
                var (icon, _) = WeatherService.Describe(_weather.Code, _weather.IsDay);
                CompactWeather.Text = $"{icon} {Math.Round(_weather.Temp):0}°";
            }
            CompactWeather.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        }

        private void RefreshWeatherPage()
        {
            WPlace.Text = (_s.WeatherPlace ?? "Počasí").ToUpper(CsCz);
            if (!_weather.HasData)
            {
                WIcon.Text = "";
                WTemp.Text = "…";
                WDesc.Text = "Načítám počasí";
                WDetail.Text = WRain.Text = "";
                WDays.Children.Clear();
                return;
            }
            var (icon, text) = WeatherService.Describe(_weather.Code, _weather.IsDay);
            WIcon.Text = icon;
            WTemp.Text = $"{Math.Round(_weather.Temp):0}°";
            WDesc.Text = text;
            WDetail.Text = $"Pocitově {Math.Round(_weather.Feels):0}° · vítr {Math.Round(_weather.Wind):0} km/h";
            WRain.Text = _weather.RainHint ?? "";
            WRain.Foreground = _accent;

            WDays.Children.Clear();
            foreach (var d in _weather.Days.Skip(1).Take(3))
            {
                var row = new Grid { Margin = new Thickness(0, 3, 0, 3) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                string day = d.Date.Date == DateTime.Today.AddDays(1) ? "Zítra" : d.Date.ToString("ddd", CsCz);
                day = char.ToUpper(day[0], CsCz) + day[1..];
                row.Children.Add(new TextBlock { Text = day, Foreground = Brushes.White, FontSize = 12, VerticalAlignment = VerticalAlignment.Center });

                var ic = new TextBlock
                {
                    Text = WeatherService.Describe(d.Code).Icon,
                    FontFamily = new FontFamily("Segoe UI Emoji, Segoe UI Symbol"),
                    Foreground = Brushes.White, FontSize = 15, VerticalAlignment = VerticalAlignment.Center,
                    ToolTip = WeatherService.Describe(d.Code).Text
                };
                Grid.SetColumn(ic, 1); row.Children.Add(ic);

                if (d.RainChance >= 30)
                {
                    var rain = new TextBlock
                    {
                        Text = d.RainChance + " %", Foreground = _accent, FontSize = 10.5,
                        VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 0, 0)
                    };
                    Grid.SetColumn(rain, 2); row.Children.Add(rain);
                }

                var temps = new TextBlock { FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
                temps.Inlines.Add(new System.Windows.Documents.Run($"{Math.Round(d.Max):0}°") { Foreground = Brushes.White, FontWeight = FontWeights.SemiBold });
                temps.Inlines.Add(new System.Windows.Documents.Run($"  {Math.Round(d.Min):0}°") { Foreground = new SolidColorBrush(Color.FromArgb(0x88, 255, 255, 255)) });
                Grid.SetColumn(temps, 3); row.Children.Add(temps);

                WDays.Children.Add(row);
            }
        }

        // ================= baterie =================

        private async System.Threading.Tasks.Task RefreshBatteryAsync()
        {
            await _battery.RefreshAsync();
            if (_expanded && _page == Page.System) RefreshBatteryStrip();

            foreach (var d in _battery.Devices)
            {
                if (d.Percent <= _s.BatteryWarnPercent && _batteryWarned.Add(d.Name))
                {
                    if (_hover || NotesBox.IsKeyboardFocusWithin || _hiddenForFullscreen) continue;
                    _page = Page.System;
                    Expand();
                    _popHide.Stop(); _popHide.Start();
                }
                else if (d.Percent > _s.BatteryWarnPercent + 10)
                {
                    _batteryWarned.Remove(d.Name); // po nabití může upozornit znovu
                }
            }
        }

        private void RefreshBatteryStrip()
        {
            BatteryPanel.Children.Clear();
            foreach (var d in _battery.Devices)
            {
                bool low = d.Percent <= _s.BatteryWarnPercent;
                var color = low ? new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x6B)) : (Brush)Brushes.White;
                var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 14, 0), ToolTip = d.Name };
                var icons = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets");
                sp.Children.Add(new TextBlock { Text = d.Glyph, FontFamily = icons, FontSize = 12, Foreground = new SolidColorBrush(Color.FromArgb(0xAA, 255, 255, 255)), VerticalAlignment = VerticalAlignment.Center });
                sp.Children.Add(new TextBlock { Text = ShortName(d.Name), FontSize = 11, Foreground = new SolidColorBrush(Color.FromArgb(0xAA, 255, 255, 255)), Margin = new Thickness(5, 0, 5, 0), VerticalAlignment = VerticalAlignment.Center, MaxWidth = 110, TextTrimming = TextTrimming.CharacterEllipsis });
                sp.Children.Add(new TextBlock { Text = BatteryService.BatteryGlyph(d.Percent), FontFamily = icons, FontSize = 12, Foreground = color, VerticalAlignment = VerticalAlignment.Center });
                sp.Children.Add(new TextBlock { Text = $" {d.Percent} %", FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = color, VerticalAlignment = VerticalAlignment.Center });
                BatteryPanel.Children.Add(sp);
            }
            BatteryPanel.Visibility = _battery.Devices.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private static string ShortName(string n) => n.Length > 22 ? n[..21] + "…" : n;

        // ================= odpočet směny + připomínka =================

        private (DateTime Start, DateTime End, char Kind)? CurrentOrNextShift(DateTime now)
        {
            TimeSpan dayStart = ParseTime(_s.ShiftDayStart, 6), nightStart = ParseTime(_s.ShiftNightStart, 18);
            for (int i = -1; i <= 14; i++)
            {
                var day = now.Date.AddDays(i);
                char c = ShiftOn(day);
                if (c is not ('R' or 'N')) continue;
                var start = day + (c == 'R' ? dayStart : nightStart);
                var end = start.AddHours(12);
                if (end > now) return (start, end, c);
            }
            return null;
        }

        private static TimeSpan ParseTime(string? s, int fallbackHour) =>
            TimeSpan.TryParse(s, CultureInfo.InvariantCulture, out var t) ? t : TimeSpan.FromHours(fallbackHour);

        private void UpdateShiftCountdown()
        {
            var now = DateTime.Now;
            var sh = CurrentOrNextShift(now);
            ShiftCountdown.Foreground = _accent;
            if (sh == null) { ShiftCountdown.Text = ""; return; }
            var (start, end, kind) = sh.Value;
            if (start <= now)
            {
                ShiftCountdown.Text = $"Ve směně do {end:H:mm}";
                return;
            }
            var left = start - now;
            string name = kind == 'R' ? "Denní" : "Noční";
            ShiftCountdown.Text = left.TotalHours < 24
                ? $"{name} za {FmtLeft(left)}"
                : $"{name} {start.ToString("ddd", CsCz)} v {start:H:mm}";
        }

        private static string FmtLeft(TimeSpan t)
        {
            if (t.TotalMinutes < 60) return $"{Math.Ceiling(t.TotalMinutes):0} min";
            int h = (int)t.TotalHours, m = t.Minutes;
            return m == 0 ? $"{h} h" : $"{h} h {m} min";
        }

        /// <summary>Volá se každých 500 ms z hodin.</summary>
        private void CheckShiftReminder()
        {
            if (_s.ShiftRemindMinutes <= 0) return;
            var now = DateTime.Now;
            var sh = CurrentOrNextShift(now);
            if (sh == null) return;
            var (start, _, kind) = sh.Value;
            if (start <= now || _remindedFor == start) return;
            if ((start - now).TotalMinutes > _s.ShiftRemindMinutes) return;

            _remindedFor = start;
            SystemSounds.Asterisk.Play();
            _forceRevealUntil = now.AddSeconds(10);
            if (_s.Layer == LayerMode.Desktop) Reveal();
            _page = Page.Widgets;
            Expand();
            var hide = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
            hide.Tick += (_, _) => { hide.Stop(); if (!_hover && !NotesBox.IsKeyboardFocusWithin) Collapse(); };
            hide.Start();
        }

        // ================= přihrádka a spouštění =================

        private void OnFilesDropped(object sender, DragEventArgs e)
        {
            if (e.Data.GetData(DataFormats.FileDrop) is not string[] files || files.Length == 0) return;
            var p = e.GetPosition(LaunchBar);
            bool toLauncher = PageTools.Visibility == Visibility.Visible &&
                              p.X >= 0 && p.Y >= 0 && p.X <= LaunchBar.ActualWidth && p.Y <= LaunchBar.ActualHeight;

            if (toLauncher)
            {
                foreach (var f in files)
                    if (!_tools.Launchers.Contains(f, StringComparer.OrdinalIgnoreCase) && _tools.Launchers.Count < 8)
                        _tools.Launchers.Add(f);
                RebuildLaunchers();
            }
            else
            {
                foreach (var f in files)
                {
                    _tools.Shelf.RemoveAll(x => string.Equals(x, f, StringComparison.OrdinalIgnoreCase));
                    _tools.Shelf.Insert(0, f);
                }
                if (_tools.Shelf.Count > 30) _tools.Shelf.RemoveRange(30, _tools.Shelf.Count - 30);
                RebuildShelf();
            }
            _tools.Save();
            e.Handled = true;
            _hover = true; // kurzor je nad ostrůvkem, CheckStaleHover to případně opraví
        }

        private static ImageSource? IconFor(string path)
        {
            try
            {
                using var ic = System.Drawing.Icon.ExtractAssociatedIcon(path);
                if (ic == null) return null;
                var src = Imaging.CreateBitmapSourceFromHIcon(ic.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                src.Freeze();
                return src;
            }
            catch { return null; }
        }

        private static void OpenPath(string path)
        {
            try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
            catch (Exception ex) { Log.Write("Nejde otevřít " + path + ": " + ex.Message); }
        }

        private void RebuildLaunchers()
        {
            LaunchItems.Children.Clear();
            foreach (var path in _tools.Launchers.ToList())
            {
                var img = new Image { Width = 24, Height = 24, Source = IconFor(path) };
                var btn = new Button
                {
                    Style = (Style)FindResource("IconButton"),
                    Width = 36, Height = 36, Margin = new Thickness(1, 0, 1, 0),
                    Content = img,
                    ToolTip = Path.GetFileNameWithoutExtension(path) + "\n(pravým tlačítkem odebrat)"
                };
                btn.Click += (_, _) => OpenPath(path);
                btn.MouseRightButtonUp += (_, e) =>
                {
                    _tools.Launchers.Remove(path);
                    _tools.Save();
                    RebuildLaunchers();
                    e.Handled = true;
                };
                LaunchItems.Children.Add(btn);
            }
            LaunchHint.Visibility = _tools.Launchers.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void RebuildShelf()
        {
            ShelfItems.Children.Clear();
            foreach (var path in _tools.Shelf.ToList())
            {
                bool exists = File.Exists(path) || Directory.Exists(path);
                var row = ListRow(
                    Directory.Exists(path) ? "" : "",
                    Path.GetFileName(path.TrimEnd('\\')) is { Length: > 0 } n ? n : path,
                    path + "\nKlik = otevřít, táhni ven = přesunout/zkopírovat",
                    exists ? 1.0 : 0.4,
                    onClick: () => OpenPath(path),
                    onRemove: () => { _tools.Shelf.Remove(path); _tools.Save(); RebuildShelf(); });

                // vytáhnout soubor zpátky ven (na plochu, do Discordu…)
                Point? down = null;
                row.PreviewMouseLeftButtonDown += (_, e) => down = e.GetPosition(row);
                row.PreviewMouseMove += (_, e) =>
                {
                    if (down == null || e.LeftButton != MouseButtonState.Pressed || !exists) return;
                    var d = e.GetPosition(row) - down.Value;
                    if (Math.Abs(d.X) + Math.Abs(d.Y) < 6) return;
                    down = null;
                    DragDrop.DoDragDrop(row, new DataObject(DataFormats.FileDrop, new[] { path }), DragDropEffects.Copy | DragDropEffects.Move);
                };
                row.PreviewMouseLeftButtonUp += (_, _) => down = null;
                ShelfItems.Children.Add(row);
            }
            ShelfHint.Visibility = _tools.Shelf.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private Border ListRow(string glyph, string text, string tooltip, double opacity, Action onClick, Action onRemove)
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var icons = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets");
            if (glyph.Length > 0)
                grid.Children.Add(new TextBlock { Text = glyph, FontFamily = icons, FontSize = 11, Foreground = new SolidColorBrush(Color.FromArgb(0x99, 255, 255, 255)), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });

            var label = new TextBlock { Text = text, Foreground = Brushes.White, FontSize = 11.5, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(label, 1);
            grid.Children.Add(label);

            var x = new Button { Style = (Style)FindResource("IconButton"), Content = "", FontSize = 8, Width = 18, Height = 18, Foreground = new SolidColorBrush(Color.FromArgb(0x88, 255, 255, 255)), Visibility = Visibility.Hidden, ToolTip = "Odebrat" };
            x.Click += (_, e) => { onRemove(); e.Handled = true; };
            Grid.SetColumn(x, 2);
            grid.Children.Add(x);

            var row = new Border { Child = grid, Padding = new Thickness(4, 2, 2, 2), CornerRadius = new CornerRadius(6), Background = Brushes.Transparent, Cursor = Cursors.Hand, ToolTip = tooltip, Opacity = opacity, Tag = "nodrag" };
            row.MouseEnter += (_, _) => { row.Background = new SolidColorBrush(Color.FromArgb(0x1F, 255, 255, 255)); x.Visibility = Visibility.Visible; };
            row.MouseLeave += (_, _) => { row.Background = Brushes.Transparent; x.Visibility = Visibility.Hidden; };
            Point? press = null;
            row.MouseLeftButtonDown += (_, e) => press = e.GetPosition(row);
            row.MouseLeftButtonUp += (_, e) =>
            {
                if (press == null) return;
                var d = e.GetPosition(row) - press.Value;
                press = null;
                if (Math.Abs(d.X) + Math.Abs(d.Y) < 6) onClick();
            };
            return row;
        }

        // ================= historie schránky (jen v paměti) =================

        private void OnClipboardChanged()
        {
            if (DateTime.Now < _ignoreClipUntil) return;
            try
            {
                // správci hesel tímhle formátem říkají "neukládat"
                if (Clipboard.ContainsData("ExcludeClipboardContentFromMonitorProcessing")) return;
                if (!Clipboard.ContainsText()) return;
                string t = Clipboard.GetText();
                if (string.IsNullOrWhiteSpace(t)) return;
                if (t.Length > 5000) t = t[..5000];
                _clips.Remove(t);
                _clips.Insert(0, t);
                if (_clips.Count > 10) _clips.RemoveAt(_clips.Count - 1);
                RebuildClips();
            }
            catch { /* schránku zrovna drží jiný program */ }
        }

        private void RebuildClips()
        {
            ClipItems.Children.Clear();
            foreach (var text in _clips.ToList())
            {
                string oneLine = string.Join(" ", text.Split(new[] { '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries)).Trim();
                var row = ListRow("", oneLine, (text.Length > 300 ? text[..300] + "…" : text) + "\n\nKlik = zkopírovat znovu",
                    1.0,
                    onClick: () => CopyBack(text),
                    onRemove: () => { _clips.Remove(text); RebuildClips(); });
                ClipItems.Children.Add(row);
            }
            ClipHint.Visibility = _clips.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void CopyBack(string text)
        {
            for (int i = 0; i < 5; i++)
            {
                try
                {
                    _ignoreClipUntil = DateTime.Now.AddMilliseconds(500);
                    Clipboard.SetText(text);
                    _clips.Remove(text);
                    _clips.Insert(0, text);
                    RebuildClips();
                    ClipLabel.Text = "ZKOPÍROVÁNO ✓";
                    var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.2) };
                    t.Tick += (_, _) => { t.Stop(); ClipLabel.Text = "SCHRÁNKA"; };
                    t.Start();
                    return;
                }
                catch { System.Threading.Thread.Sleep(40); }
            }
        }

        // ================= kolečko nad seznamy =================

        private System.Windows.Controls.ScrollViewer? ScrollViewerAt(Native.POINT pt)
        {
            if (!_expanded || !(_page is Page.Tools or Page.Widgets)) return null;
            var local = Island.PointFromScreen(new Point(pt.X, pt.Y));
            var hit = Island.InputHitTest(local) as DependencyObject;
            while (hit != null && hit != Island)
            {
                if (hit is ScrollViewer sv && sv.ScrollableHeight > 0) return sv;
                hit = hit is Visual ? VisualTreeHelper.GetParent(hit) : LogicalTreeHelper.GetParent(hit);
            }
            return null;
        }

        private static bool IsNoDrag(object? source)
        {
            var d = source as DependencyObject;
            while (d != null)
            {
                if (d is FrameworkElement fe && fe.Tag is "nodrag") return true;
                d = d is Visual ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
            }
            return false;
        }

        // ================= pojistka: zaseknuté rozbalení =================

        /// <summary>Po přetažení souboru WPF někdy nepošle MouseLeave – sbalíme podle skutečné polohy kurzoru.</summary>
        private void CheckStaleHover()
        {
            if (!_expanded || !_hover || _typing || _dragPending) { _staleSince = DateTime.MinValue; return; }
            if (!Native.GetCursorPos(out var p)) return;
            if (Island.ActualWidth <= 0) return;
            var tl = Island.PointToScreen(new Point(-20, -20));
            var br = Island.PointToScreen(new Point(Island.ActualWidth + 20, Island.ActualHeight + 20));
            bool inside = p.X >= tl.X && p.X <= br.X && p.Y >= tl.Y && p.Y <= br.Y;
            if (inside) { _staleSince = DateTime.MinValue; return; }
            if (_staleSince == DateTime.MinValue) { _staleSince = DateTime.Now; return; }
            if ((DateTime.Now - _staleSince).TotalMilliseconds > 900)
            {
                _staleSince = DateTime.MinValue;
                _hover = false;
                Collapse();
            }
        }
    }
}
