using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Lumo
{
    /// <summary>Okno Nastavení – postavené v kódu, styly jsou v App.xaml.</summary>
    public class SettingsWindow : Window
    {
        private readonly MainWindow _win;
        private readonly Settings _s;
        private readonly StackPanel _content = new() { Margin = new Thickness(26, 6, 26, 24) };
        private readonly StackPanel _nav = new() { Margin = new Thickness(12, 4, 12, 12) };
        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };

        private static readonly Brush Dim = new SolidColorBrush(Color.FromArgb(0x99, 255, 255, 255));
        private static readonly Brush Faint = new SolidColorBrush(Color.FromArgb(0x14, 255, 255, 255));
        private static readonly FontFamily Icons = new("Segoe Fluent Icons, Segoe MDL2 Assets");

        private static readonly (string Key, string Glyph, string Title)[] Sections =
        {
            ("general", "", "Obecné"),
            ("look", "", "Vzhled"),
            ("weather", "", "Počasí"),
            ("shifts", "", "Směny"),
            ("battery", "", "Baterie"),
            ("tabs", "", "Záložky"),
            ("about", "", "O aplikaci"),
        };

        public SettingsWindow(MainWindow win)
        {
            _win = win;
            _s = win.Settings;

            Title = "Lumo – Nastavení";
            Width = 700; Height = 520;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
            Foreground = Brushes.White;
            UseLayoutRounding = true;
            try { Icon = BitmapFrame.Create(new Uri("pack://application:,,,/lumo.ico")); } catch { }

            var root = new Grid();
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(52) });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            // ---- titulek ----
            var title = new DockPanel { Background = Brushes.Transparent, Margin = new Thickness(18, 0, 10, 0) };
            title.MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); };
            var close = new Button { Style = (Style)FindResource("IconButton"), Content = "", FontSize = 11, Width = 34, Height = 34, ToolTip = "Zavřít" };
            close.Click += (_, _) => Close();
            DockPanel.SetDock(close, Dock.Right);
            title.Children.Add(close);
            var logo = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            try { logo.Children.Add(new Image { Source = BitmapFrame.Create(new Uri("pack://application:,,,/lumo.ico")), Width = 22, Height = 22, Margin = new Thickness(0, 0, 10, 0) }); } catch { }
            logo.Children.Add(new TextBlock { Text = "Lumo", FontSize = 15, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
            logo.Children.Add(new TextBlock { Text = "  Nastavení", FontSize = 15, Foreground = Dim, VerticalAlignment = VerticalAlignment.Center });
            title.Children.Add(logo);
            Grid.SetColumnSpan(title, 2);
            root.Children.Add(title);

            // ---- navigace ----
            foreach (var (key, glyph, text) in Sections)
            {
                var sp = new StackPanel { Orientation = Orientation.Horizontal };
                sp.Children.Add(new TextBlock { Text = glyph, FontFamily = Icons, FontSize = 14, Width = 26, VerticalAlignment = VerticalAlignment.Center });
                sp.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
                var rb = new RadioButton { Style = (Style)FindResource("NavItem"), Content = sp, GroupName = "nav", Tag = key };
                rb.Checked += (_, _) => Show(key);
                _nav.Children.Add(rb);
            }
            Grid.SetRow(_nav, 1);
            root.Children.Add(_nav);

            var scroll = new ScrollViewer { Content = _content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            var right = new Border { Background = Faint, CornerRadius = new CornerRadius(12), Margin = new Thickness(0, 0, 14, 14), Child = scroll };
            Grid.SetRow(right, 1); Grid.SetColumn(right, 1);
            root.Children.Add(right);

            Content = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0xF7, 0x12, 0x12, 0x18)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x33, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(16),
                Child = root
            };

            ((RadioButton)_nav.Children[0]).IsChecked = true;
        }

        // ================= sekce =================

        private void Show(string key)
        {
            _content.Children.Clear();
            switch (key)
            {
                case "general": General(); break;
                case "look": Look(); break;
                case "weather": Weather(); break;
                case "shifts": Shifts(); break;
                case "battery": Battery(); break;
                case "tabs": Tabs(); break;
                case "about": About(); break;
            }
        }

        private void General()
        {
            Header("Obecné");
            Chips("Pozice", new[] { ("Nahoře", "Top"), ("Dole", "Bottom") }, _s.Position.ToString(),
                v => _win.SetPosition(Enum.Parse<IslandPosition>(v)));
            ButtonRow(("Vycentrovat", () => _win.CenterIsland()));
            Hint("Lumo jde i přetáhnout myší. Pustíš-li ho v horní půlce obrazovky, přichytí se nahoru.");

            Chips("Kdy je vidět", new[] { ("Jen na ploše", "Desktop"), ("Vždy navrchu", "AlwaysOnTop") }, _s.Layer.ToString(),
                v => _win.SetLayer(Enum.Parse<LayerMode>(v)));
            Hint("„Jen na ploše“: okna ho překryjí, vyjede, když najedeš myší k okraji obrazovky.");

            Toggle("Spouštět s Windows", Settings.StartWithWindows, v => Settings.StartWithWindows = v);
            Toggle("Schovat ve hrách přes celou obrazovku", _s.HideInFullscreen, v => _win.SetHideInFullscreen(v));
            Toggle("Ukázat novou skladbu", _s.PopOnNewTrack, v => _win.SetPopOnNewTrack(v));

            if (!HardwareService.IsAdmin && !Packaging.IsPackaged)
            {
                Label("Teplota CPU");
                ButtonRow(("Spustit jako správce", () => _win.RestartAsAdmin()));
                Hint("Pro teplotu procesoru je potřeba spuštění jako správce a ovladač PawnIO (pawnio.eu).");
            }
        }

        private void Look()
        {
            Header("Vzhled");
            var swatches = new WrapPanel { Margin = new Thickness(0, 2, 0, 6) };
            Chips("Barva", new[] { ("Podle obalu alba", "Album"), ("Vlastní", "Fixed") }, _s.AccentMode, v =>
            {
                _s.AccentMode = v; Apply();
                swatches.IsEnabled = v == "Fixed";
            });

            string[] colors = { "#8C9CFF", "#B48CFF", "#FF7AB6", "#FF6B6B", "#FFB454", "#FFD866", "#6EE7A0", "#5CC8FF" };
            foreach (var c in colors)
            {
                var rb = new RadioButton
                {
                    Style = (Style)FindResource("Swatch"), GroupName = "swatch",
                    Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(c)),
                    IsChecked = string.Equals(_s.FixedColor, c, StringComparison.OrdinalIgnoreCase)
                };
                rb.Checked += (_, _) => { _s.FixedColor = c; Apply(); };
                swatches.Children.Add(rb);
            }
            swatches.IsEnabled = _s.AccentMode == "Fixed";
            _content.Children.Add(swatches);

            Slider("Krytí pozadí", 50, 100, 5, _s.BackgroundOpacity * 100, v => $"{v:0} %", v => { _s.BackgroundOpacity = v / 100.0; Apply(); });

            Chips("Velikost", new[] { ("Malý", "0.85"), ("Normální", "1"), ("Velký", "1.15") },
                _s.Scale.ToString("0.##", CultureInfo.InvariantCulture),
                v => { _s.Scale = double.Parse(v, CultureInfo.InvariantCulture); Apply(); });
        }

        private void Weather()
        {
            Header("Počasí");
            Label("Město");
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
            var find = new Button { Style = (Style)FindResource("AccentButton"), Content = "Najít", Margin = new Thickness(8, 0, 0, 0) };
            DockPanel.SetDock(find, Dock.Right);
            var box = new TextBox { Style = (Style)FindResource("DarkBox"), Text = _s.WeatherPlace };
            row.Children.Add(find);
            row.Children.Add(box);
            _content.Children.Add(row);
            var status = Hint($"Teď: {_s.WeatherPlace} ({_s.WeatherLat.ToString("0.00", CultureInfo.InvariantCulture)}, {_s.WeatherLon.ToString("0.00", CultureInfo.InvariantCulture)})");

            async Task Search()
            {
                string q = box.Text.Trim();
                if (q.Length < 2) return;
                status.Text = "Hledám…";
                try
                {
                    string url = "https://geocoding-api.open-meteo.com/v1/search?count=1&language=cs&format=json&name=" + Uri.EscapeDataString(q);
                    using var doc = JsonDocument.Parse(await Http.GetStringAsync(url));
                    if (!doc.RootElement.TryGetProperty("results", out var res) || res.GetArrayLength() == 0)
                    {
                        status.Text = "Město nenalezeno, zkus to napsat jinak.";
                        return;
                    }
                    var r = res[0];
                    _s.WeatherPlace = r.GetProperty("name").GetString() ?? q;
                    _s.WeatherLat = r.GetProperty("latitude").GetDouble();
                    _s.WeatherLon = r.GetProperty("longitude").GetDouble();
                    string region = r.TryGetProperty("admin1", out var a1) ? a1.GetString() ?? "" : "";
                    string country = r.TryGetProperty("country", out var co) ? co.GetString() ?? "" : "";
                    _s.Save();
                    box.Text = _s.WeatherPlace;
                    status.Text = $"✓ {_s.WeatherPlace}, {region}, {country}";
                    await _win.RefreshWeatherNow();
                }
                catch (Exception ex)
                {
                    status.Text = "Nepodařilo se připojit: " + ex.Message;
                }
            }
            find.Click += async (_, _) => await Search();
            box.KeyDown += async (_, e) => { if (e.Key == Key.Enter) await Search(); };

            Toggle("Počasí v malém ostrůvku (když nic nehraje)", _s.WeatherInCompact, v => { _s.WeatherInCompact = v; Apply(); });
            Hint("Data: Open-Meteo, zdarma a bez účtu. Obnovuje se každých 15 minut.");
        }

        private void Shifts()
        {
            Header("Směny");
            TimeBox("Denní směna začíná", _s.ShiftDayStart, v => _s.ShiftDayStart = v);
            TimeBox("Noční směna začíná", _s.ShiftNightStart, v => _s.ShiftNightStart = v);
            Hint("Směna trvá 12 hodin.");

            Chips("Připomenout předem", new[] { ("Vypnuto", "0"), ("30 min", "30"), ("1 h", "60"), ("2 h", "120") },
                _s.ShiftRemindMinutes.ToString(CultureInfo.InvariantCulture),
                v => { _s.ShiftRemindMinutes = int.Parse(v, CultureInfo.InvariantCulture); Apply(); });

            var preview = new TextBlock { Foreground = (Brush)FindResource("SetAccent"), FontSize = 12.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 10, 0, 0) };

            Label("Cyklus směn");
            var anchor = new TextBox { Style = (Style)FindResource("DarkBox"), Text = _s.ShiftAnchor, Width = 140, HorizontalAlignment = HorizontalAlignment.Left };
            anchor.LostKeyboardFocus += (_, _) =>
            {
                if (DateTime.TryParseExact(anchor.Text.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                {
                    _s.ShiftAnchor = anchor.Text.Trim(); Apply(); anchor.BorderBrush = Brushes.Transparent;
                    preview.Text = _win.ShiftPreview();
                }
                else anchor.BorderBrush = Brushes.IndianRed;
            };
            _content.Children.Add(anchor);
            Hint("Datum první denní směny cyklu ve tvaru RRRR-MM-DD.");

            var pattern = new TextBox { Style = (Style)FindResource("DarkBox"), Text = _s.ShiftPattern, Width = 140, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 8, 0, 0) };
            pattern.LostKeyboardFocus += (_, _) =>
            {
                string p = pattern.Text.Trim().ToUpperInvariant();
                if (p.Length is >= 2 and <= 31 && p.All(ch => ch is 'R' or 'N' or '_'))
                {
                    _s.ShiftPattern = p; pattern.Text = p; Apply(); pattern.BorderBrush = Brushes.Transparent;
                    preview.Text = _win.ShiftPreview();
                }
                else pattern.BorderBrush = Brushes.IndianRed;
            };
            _content.Children.Add(pattern);
            Hint("Vzor: R = denní, N = noční, _ = volno. Tvůj je RRNN____ (2 denní, 2 noční, 4 volna).");

            preview.Text = _win.ShiftPreview();
            _content.Children.Add(preview);
        }

        private void Battery()
        {
            Header("Baterie");
            Slider("Upozornit, když baterie klesne pod", 10, 50, 5, _s.BatteryWarnPercent, v => $"{v:0} %",
                v => { _s.BatteryWarnPercent = (int)v; Apply(); });

            Label("Nalezená zařízení");
            var devices = _win.BatteryDevices;
            if (devices.Count == 0)
                Hint("Žádné Bluetooth zařízení s údajem o baterii. Zařízení s vlastním USB přijímačem Windows nevidí.");
            foreach (var d in devices)
            {
                var sp = new DockPanel { Margin = new Thickness(0, 3, 0, 3) };
                var pct = new TextBlock { Text = d.Percent + " %", FontWeight = FontWeights.SemiBold };
                DockPanel.SetDock(pct, Dock.Right);
                sp.Children.Add(pct);
                sp.Children.Add(new TextBlock { Text = d.Glyph, FontFamily = Icons, Foreground = Dim, Width = 24, VerticalAlignment = VerticalAlignment.Center });
                sp.Children.Add(new TextBlock { Text = d.Name });
                _content.Children.Add(sp);
            }
        }

        private void Tabs()
        {
            Header("Záložky");
            Hint("Vyber, které záložky chceš v rozbaleném Lumo vidět.");
            var tabs = new[] { ("Music", "Hudba"), ("System", "Systém"), ("Weather", "Počasí"), ("Widgets", "Widgety"), ("Tools", "Nástroje") };
            foreach (var (key, name) in tabs)
            {
                var cb = Toggle(name, !_s.HiddenTabs.Contains(key), _ => { });
                cb.Checked += (_, _) => { _s.HiddenTabs.Remove(key); Apply(); };
                cb.Unchecked += (_, _) =>
                {
                    if (tabs.Count(t => !_s.HiddenTabs.Contains(t.Item1)) <= 1) { cb.IsChecked = true; return; }
                    if (!_s.HiddenTabs.Contains(key)) _s.HiddenTabs.Add(key);
                    Apply();
                };
            }
        }

        private void About()
        {
            Header("O aplikaci");
            var ver = typeof(SettingsWindow).Assembly.GetName().Version;
            _content.Children.Add(new TextBlock { Text = $"Lumo {ver?.ToString(3)}", FontSize = 20, FontWeight = FontWeights.SemiBold });
            Hint("Vlastní „Dynamic Island“ pro Windows od Bolek98.");
            Hint("Dříve Ostrůvek. Data jsou v %APPDATA%\\Lumo.");
            ButtonRow(("Otevřít složku s daty", () => OpenPath(Settings.Folder)),
                      ("Otevřít log", () => OpenPath(Path.Combine(Settings.Folder, "log.txt"))));
        }

        // ================= stavebnice =================

        private void Apply()
        {
            _s.Save();
            _win.ApplySettings();
        }

        private static void OpenPath(string p)
        {
            try { Process.Start(new ProcessStartInfo(p) { UseShellExecute = true }); } catch { }
        }

        private void Header(string text) =>
            _content.Children.Add(new TextBlock { Text = text, FontSize = 20, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 14, 0, 10) });

        private void Label(string text) =>
            _content.Children.Add(new TextBlock { Text = text, FontSize = 12, Foreground = Dim, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 14, 0, 6) });

        private TextBlock Hint(string text)
        {
            var t = new TextBlock { Text = text, FontSize = 11.5, Foreground = Dim, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 2) };
            _content.Children.Add(t);
            return t;
        }

        private CheckBox Toggle(string text, bool value, Action<bool> set)
        {
            var cb = new CheckBox { Style = (Style)FindResource("ToggleSwitch"), Content = new TextBlock { Text = text, FontSize = 13, TextWrapping = TextWrapping.Wrap }, IsChecked = value, Margin = new Thickness(0, 9, 0, 0) };
            cb.Checked += (_, _) => set(true);
            cb.Unchecked += (_, _) => set(false);
            _content.Children.Add(cb);
            return cb;
        }

        private void Chips(string label, (string Text, string Value)[] options, string current, Action<string> set)
        {
            Label(label);
            var wp = new WrapPanel();
            string group = "g" + Guid.NewGuid().ToString("N");
            foreach (var (text, value) in options)
            {
                var rb = new RadioButton { Style = (Style)FindResource("Chip"), Content = text, GroupName = group, IsChecked = value == current };
                rb.Checked += (_, _) => set(value);
                wp.Children.Add(rb);
            }
            _content.Children.Add(wp);
        }

        private void ButtonRow(params (string Text, Action Click)[] buttons)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
            foreach (var (text, click) in buttons)
            {
                var b = new Button { Style = (Style)FindResource("GhostButton"), Content = text, Margin = new Thickness(0, 0, 8, 0) };
                b.Click += (_, _) => click();
                sp.Children.Add(b);
            }
            _content.Children.Add(sp);
        }

        private void Slider(string label, double min, double max, double step, double value, Func<double, string> fmt, Action<double> set)
        {
            Label(label);
            var dp = new DockPanel();
            var val = new TextBlock { Text = fmt(value), Width = 50, TextAlignment = TextAlignment.Right, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(val, Dock.Right);
            var sl = new Slider { Minimum = min, Maximum = max, Value = value, TickFrequency = step, IsSnapToTickEnabled = true, VerticalAlignment = VerticalAlignment.Center, Focusable = false };
            sl.ValueChanged += (_, e) => { val.Text = fmt(e.NewValue); set(e.NewValue); };
            dp.Children.Add(val);
            dp.Children.Add(sl);
            _content.Children.Add(dp);
        }

        private void TimeBox(string label, string value, Action<string> set)
        {
            Label(label);
            var box = new TextBox { Style = (Style)FindResource("DarkBox"), Text = value, Width = 90, HorizontalAlignment = HorizontalAlignment.Left };
            box.LostKeyboardFocus += (_, _) =>
            {
                if (TimeSpan.TryParse(box.Text.Trim(), CultureInfo.InvariantCulture, out var t) && t >= TimeSpan.Zero && t < TimeSpan.FromDays(1))
                {
                    string v = $"{t.Hours:00}:{t.Minutes:00}";
                    box.Text = v; set(v); Apply();
                    box.BorderBrush = Brushes.Transparent;
                }
                else box.BorderBrush = Brushes.IndianRed;
            };
            box.KeyDown += (_, e) => { if (e.Key == Key.Enter) Keyboard.ClearFocus(); };
            _content.Children.Add(box);
        }
    }
}
