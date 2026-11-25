using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Media;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace Lumo
{
    /// <summary>Verze 2: záložky Hudba / Systém / Widgety.</summary>
    public partial class MainWindow
    {
        private enum Page { Music, System, Weather, Widgets, Tools }

        private Page _page = Page.Music;
        private FrameworkElement? _currentPage;

        private readonly HardwareService _hw = new();
        private readonly DispatcherTimer _hwTimer = new() { Interval = TimeSpan.FromMilliseconds(1500) };
        private bool _hwBusy, _hwFirst = true;

        private TimeSpan _timerLeft;
        private DateTime _timerEnd;
        private bool _timerRunning;
        private DateTime _forceRevealUntil = DateTime.MinValue;

        private bool _typing;
        private readonly DispatcherTimer _notesSave = new() { Interval = TimeSpan.FromMilliseconds(800) };
        private static string NotesPath => Path.Combine(Settings.Folder, "poznamky.txt");

        private static readonly Brush TabIdleBrush = new SolidColorBrush(Color.FromArgb(0x80, 255, 255, 255));

        private void InitPages()
        {
            if (Enum.TryParse<Page>(_s.LastPage, out var p)) _page = p;

            TabMusic.Click += (_, _) => SwitchPage(Page.Music);
            TabSystem.Click += (_, _) => SwitchPage(Page.System);
            TabWidgets.Click += (_, _) => SwitchPage(Page.Widgets);
            TabWeather.Click += (_, _) => SwitchPage(Page.Weather);
            TabTools.Click += (_, _) => SwitchPage(Page.Tools);

            // ---- systém ----
            _hwTimer.Tick += async (_, _) => await RefreshHardwareAsync();
            Closed += (_, _) => _hw.Dispose();

            // ---- časovač ----
            TimerPlus1.Click += (_, _) => AddTimer(TimeSpan.FromMinutes(1));
            TimerPlus5.Click += (_, _) => AddTimer(TimeSpan.FromMinutes(5));
            TimerPlay.Click += (_, _) => ToggleTimer();
            TimerReset.Click += (_, _) => { _timerRunning = false; _timerLeft = TimeSpan.Zero; UpdateClock(); };

            // ---- poznámky ----
            try { if (File.Exists(NotesPath)) NotesBox.Text = File.ReadAllText(NotesPath); } catch { }
            UpdateNotesPlaceholder();
            NotesBox.TextChanged += (_, _) => { UpdateNotesPlaceholder(); _notesSave.Stop(); _notesSave.Start(); };
            _notesSave.Tick += (_, _) => { _notesSave.Stop(); SaveNotes(); };
            NotesBox.PreviewMouseLeftButtonDown += (_, _) => StartTyping();
            NotesBox.PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { StopTyping(); e.Handled = true; } };
            Deactivated += (_, _) =>
            {
                if (!_typing) return;
                StopTyping();
                if (!_hover && _expanded) _collapseDelay.Start();
            };
        }

        // ================= přepínání záložek =================

        private void SwitchPage(Page p)
        {
            _page = p;
            _s.LastPage = p.ToString();
            _s.Save();
            ShowPage(p, animate: true);
        }

        private void ShowPage(Page p, bool animate)
        {
            FrameworkElement target = p switch
            {
                Page.System => PageSystem,
                Page.Widgets => PageWidgets,
                Page.Weather => PageWeather,
                Page.Tools => PageTools,
                _ => _media.HasMedia ? PageMedia : PageIdle
            };

            TabMusic.Foreground = p == Page.Music ? _accent : TabIdleBrush;
            TabSystem.Foreground = p == Page.System ? _accent : TabIdleBrush;
            TabWidgets.Foreground = p == Page.Widgets ? _accent : TabIdleBrush;
            TabWeather.Foreground = p == Page.Weather ? _accent : TabIdleBrush;
            TabTools.Foreground = p == Page.Tools ? _accent : TabIdleBrush;
            if (p == Page.System) RefreshBatteryStrip();
            if (p == Page.Weather) RefreshWeatherPage();

            if (p == Page.System)
            {
                if (!_hwTimer.IsEnabled) { _hwTimer.Start(); _ = RefreshHardwareAsync(); }
            }
            else _hwTimer.Stop();

            if (p == Page.Widgets) UpdateShift();
            if (p != Page.Widgets && _typing) StopTyping();

            if (target == _currentPage && target.Visibility == Visibility.Visible) return;
            foreach (var v in new FrameworkElement[] { PageMedia, PageIdle, PageSystem, PageWidgets, PageWeather, PageTools })
            {
                if (v == target) continue;
                v.BeginAnimation(OpacityProperty, null);
                v.Visibility = Visibility.Collapsed;
            }
            _currentPage = target;
            target.Visibility = Visibility.Visible;
            if (animate)
                target.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));
            else
            {
                target.BeginAnimation(OpacityProperty, null);
                target.Opacity = 1;
            }
        }

        private void OnPagesHidden()
        {
            _hwTimer.Stop();
            if (_typing) StopTyping();
        }

        // ================= systém =================

        private async Task RefreshHardwareAsync()
        {
            if (_hwBusy) return;
            _hwBusy = true;
            if (_hwFirst)
            {
                CpuSub.Text = GpuSub.Text = "Načítám senzory…";
            }
            try
            {
                await Task.Run(_hw.Update);
                _hwFirst = false;
                RefreshSystemPage();
            }
            catch (Exception ex) { Log.Write("Systém: " + ex.Message); }
            finally { _hwBusy = false; }
        }

        private void RefreshSystemPage()
        {
            // CPU
            CpuValue.Text = _hw.CpuTemp.HasValue ? $"{_hw.CpuTemp:0} °C" : (_hw.CpuLoad.HasValue ? $"{_hw.CpuLoad:0} %" : "—");
            string cpuLoad = _hw.CpuLoad.HasValue ? $"Vytížení {_hw.CpuLoad:0} %" : "";
            if (_hw.CpuTemp.HasValue)
            {
                CpuSub.Text = cpuLoad;
                CpuSub.ToolTip = null;
            }
            else
            {
                CpuSub.Text = Packaging.IsPackaged ? cpuLoad : HardwareService.IsAdmin ? "Teplota: chybí PawnIO" : "Teplota: jako správce";
                CpuSub.ToolTip = HardwareService.IsAdmin
                    ? "Pro teplotu CPU nainstaluj ovladač PawnIO (pawnio.eu) a restartuj Lumo."
                    : "Pro teplotu CPU: pravé tlačítko na Lumo → Spustit jako správce.\nPotřeba je i ovladač PawnIO (pawnio.eu).";
            }
            SetBar(CpuBar, (_hw.CpuLoad ?? 0) / 100.0);

            // GPU
            GpuLabel.Text = string.IsNullOrEmpty(_hw.GpuName) ? "GPU" : "GPU · " + _hw.GpuName.ToUpperInvariant();
            GpuValue.Text = _hw.GpuTemp.HasValue ? $"{_hw.GpuTemp:0} °C" : "—";
            GpuSub.Text = _hw.GpuLoad.HasValue ? $"Vytížení {_hw.GpuLoad:0} %" : "Nenalezeno";
            SetBar(GpuBar, (_hw.GpuLoad ?? 0) / 100.0);

            // RAM
            var cs = CultureInfo.GetCultureInfo("cs-CZ");
            RamValue.Text = _hw.RamUsedGb.ToString("0.0", cs) + " GB";
            RamSub.Text = $"z {_hw.RamTotalGb:0} GB · {_hw.RamPercent:0} %";
            SetBar(RamBar, _hw.RamPercent / 100.0);

            // síť
            NetDown.Text = "↓ " + HardwareService.FormatSpeed(_hw.NetDownBps);
            NetUp.Text = "↑ " + HardwareService.FormatSpeed(_hw.NetUpBps);
        }

        private void SetBar(Border bar, double frac)
        {
            bar.Background = _accent;
            if (bar.Parent is FrameworkElement track)
            {
                double w = track.ActualWidth * Math.Clamp(frac, 0, 1);
                bar.BeginAnimation(WidthProperty, new DoubleAnimation(w, TimeSpan.FromMilliseconds(400)));
            }
        }

        // ================= směny =================

        private static readonly CultureInfo CsCz = CultureInfo.GetCultureInfo("cs-CZ");

        private char ShiftOn(DateTime day)
        {
            if (string.IsNullOrEmpty(_s.ShiftPattern) ||
                !DateTime.TryParseExact(_s.ShiftAnchor, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var anchor))
                return '?';
            int len = _s.ShiftPattern.Length;
            int days = (int)(day.Date - anchor.Date).TotalDays;
            int idx = ((days % len) + len) % len;
            return char.ToUpperInvariant(_s.ShiftPattern[idx]);
        }

        private static string ShiftName(char c) => c switch
        {
            'R' => "Denní",
            'N' => "Noční",
            '?' => "—",
            _ => "Volno"
        };

        private static Brush ShiftBrush(char c) => c switch
        {
            'R' => new SolidColorBrush(Color.FromRgb(0xFF, 0xC8, 0x57)),
            'N' => new SolidColorBrush(Color.FromRgb(0x9C, 0xA8, 0xFF)),
            _ => new SolidColorBrush(Color.FromRgb(0x6E, 0xE7, 0xA0))
        };

        private void UpdateShift()
        {
            UpdateShiftCountdown();
            var today = DateTime.Today;
            char t = ShiftOn(today);
            ShiftToday.Text = ShiftName(t);
            ShiftToday.Foreground = ShiftBrush(t);

            char tomorrow = ShiftOn(today.AddDays(1));
            if (t is 'R' or 'N')
            {
                ShiftNext.Text = "Zítra: " + ShiftName(tomorrow);
            }
            else
            {
                for (int i = 1; i <= 31; i++)
                {
                    var d = today.AddDays(i);
                    char c = ShiftOn(d);
                    if (c is 'R' or 'N')
                    {
                        string when = i == 1 ? "zítra" : i == 2 ? "pozítří" : d.ToString("ddd d. M.", CsCz);
                        ShiftNext.Text = $"{ShiftName(c)} {when}";
                        return;
                    }
                }
                ShiftNext.Text = "";
            }
        }

        // ================= časovač =================

        private void AddTimer(TimeSpan add)
        {
            if (_timerRunning) _timerEnd += add;
            else _timerLeft += add;
            if (_timerLeft > TimeSpan.FromHours(10)) _timerLeft = TimeSpan.FromHours(10);
            UpdateClock();
        }

        private void ToggleTimer()
        {
            if (_timerRunning)
            {
                _timerLeft = _timerEnd - DateTime.Now;
                _timerRunning = false;
            }
            else if (_timerLeft > TimeSpan.Zero)
            {
                _timerEnd = DateTime.Now + _timerLeft;
                _timerRunning = true;
            }
            UpdateClock();
        }

        private void UpdateTimer()
        {
            if (_timerRunning)
            {
                _timerLeft = _timerEnd - DateTime.Now;
                if (_timerLeft <= TimeSpan.Zero) TimerFinished();
            }
            if (_timerLeft < TimeSpan.Zero) _timerLeft = TimeSpan.Zero;
            TimerText.Text = FmtTimer(_timerLeft);
            TimerPlay.Content = _timerRunning ? "" : "";
        }

        private static string FmtTimer(TimeSpan t)
        {
            t = TimeSpan.FromSeconds(Math.Ceiling(Math.Max(0, t.TotalSeconds)));
            return t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"mm\:ss");
        }

        private void TimerFinished()
        {
            _timerRunning = false;
            _timerLeft = TimeSpan.Zero;

            // zvuk 3×
            int plays = 0;
            var sound = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(900) };
            sound.Tick += (_, _) => { SystemSounds.Exclamation.Play(); if (++plays >= 3) sound.Stop(); };
            SystemSounds.Exclamation.Play(); plays++;
            sound.Start();

            // ukázat ostrůvek i nad okny
            _forceRevealUntil = DateTime.Now.AddSeconds(8);
            if (_s.Layer == LayerMode.Desktop) Reveal();
            if (_hiddenForFullscreen) { _hiddenForFullscreen = false; Root.Visibility = Visibility.Visible; }
            _page = Page.Widgets;
            Expand();

            // bliknutí záře
            Glow.Visibility = Visibility.Visible;
            Glow.BeginAnimation(OpacityProperty, new DoubleAnimation(0.2, 1.0, TimeSpan.FromMilliseconds(350))
            {
                AutoReverse = true,
                RepeatBehavior = new RepeatBehavior(4),
                FillBehavior = FillBehavior.Stop
            });

            var hide = new DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };
            hide.Tick += (_, _) =>
            {
                hide.Stop();
                Glow.Visibility = _media.HasMedia ? Visibility.Visible : Visibility.Collapsed;
                if (!_hover && !NotesBox.IsKeyboardFocusWithin) Collapse();
            };
            hide.Start();
        }

        // ================= poznámky =================

        /// <summary>Okno se normálně neaktivuje (nekrade focus). Pro psaní ho dočasně aktivujeme.</summary>
        private void StartTyping()
        {
            if (_typing) return;
            _typing = true;
            long ex = Native.GetWindowLongPtr(_hwnd, Native.GWL_EXSTYLE).ToInt64();
            Native.SetWindowLongPtr(_hwnd, Native.GWL_EXSTYLE, new IntPtr(ex & ~Native.WS_EX_NOACTIVATE));
            Activate();
            NotesBox.Focus();
        }

        private void StopTyping()
        {
            if (!_typing) return;
            _typing = false;
            SaveNotes();
            Keyboard.ClearFocus();
            FocusManager.SetFocusedElement(this, null);
            long ex = Native.GetWindowLongPtr(_hwnd, Native.GWL_EXSTYLE).ToInt64();
            Native.SetWindowLongPtr(_hwnd, Native.GWL_EXSTYLE, new IntPtr(ex | Native.WS_EX_NOACTIVATE));
        }

        private void UpdateNotesPlaceholder() =>
            NotesPlaceholder.Visibility = string.IsNullOrEmpty(NotesBox.Text) ? Visibility.Visible : Visibility.Collapsed;

        private void SaveNotes()
        {
            try
            {
                Directory.CreateDirectory(Settings.Folder);
                File.WriteAllText(NotesPath, NotesBox.Text);
            }
            catch (Exception ex) { Log.Write("Poznámky nejde uložit: " + ex.Message); }
        }

        // ================= spuštění jako správce =================

        public void RestartAsAdmin()
        {
            try
            {
                SaveNotes();
                App.ReleaseSingleInstance();
                Process.Start(new ProcessStartInfo(Environment.ProcessPath!)
                {
                    UseShellExecute = true,
                    Verb = "runas",
                    Arguments = "--restart"
                });
                Application.Current.Shutdown();
            }
            catch (Exception ex)
            {
                // uživatel zrušil UAC dotaz → běžíme dál
                Log.Write("Spuštění jako správce zrušeno: " + ex.Message);
                App.ReacquireSingleInstance();
            }
        }
    }
}
