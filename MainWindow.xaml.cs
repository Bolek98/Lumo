using System;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace Lumo
{
    public partial class MainWindow : Window
    {
        // ---- rozměry ----
        private const double CompactH = 36, CompactWMedia = 190;
        private double CompactWIdle => _weather.HasData && _s.WeatherInCompact ? 178 : 120;
        private const double ExpW = 420, ExpH = 236;

        private static readonly Color DefaultAccent = Color.FromRgb(0x8C, 0x9C, 0xFF);
        private static readonly CultureInfo Cs = new("cs-CZ");

        private readonly Settings _s;
        private readonly MediaService _media = new();
        private readonly SolidColorBrush _accent = new(DefaultAccent);
        private readonly SolidColorBrush _accentBorder = new(Color.FromArgb(0x55, DefaultAccent.R, DefaultAccent.G, DefaultAccent.B));

        private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromMilliseconds(500) };
        private readonly DispatcherTimer _poll = new() { Interval = TimeSpan.FromMilliseconds(100) };
        private readonly DispatcherTimer _expandDelay = new() { Interval = TimeSpan.FromMilliseconds(120) };
        private readonly DispatcherTimer _collapseDelay = new() { Interval = TimeSpan.FromMilliseconds(380) };
        private readonly DispatcherTimer _volumeHide = new() { Interval = TimeSpan.FromMilliseconds(1400) };
        private readonly DispatcherTimer _popHide = new() { Interval = TimeSpan.FromMilliseconds(3500) };

        private IntPtr _hwnd;
        private bool _expanded, _hover, _revealed, _hiddenForFullscreen, _volumeShowing, _barsRunning;
        private DateTime _revealLeaveSince = DateTime.MinValue;

        public MainWindow(Settings settings)
        {
            _s = settings;
            InitializeComponent();

            Glow.Background = _accent;
            Island.BorderBrush = _accentBorder;
            ProgressFill.Background = _accent;
            SourceText.Foreground = _accent;
            foreach (var r in BarRects())
            {
                r.Fill = _accent;
                r.RenderTransform = new ScaleTransform(1, 0.35);
            }

            Island.MouseEnter += (_, _) => { _hover = true; _collapseDelay.Stop(); _popHide.Stop(); if (!_expanded) _expandDelay.Start(); };
            Island.MouseLeave += (_, _) => { _hover = false; _expandDelay.Stop(); if (_expanded) _collapseDelay.Start(); };
            Island.PreviewMouseLeftButtonDown += OnDragDown;
            Island.PreviewMouseMove += OnDragMove;
            Island.PreviewMouseLeftButtonUp += OnDragUp;
            Island.LostMouseCapture += (_, _) => { if (_dragPending) EndDrag(); };
            Island.MouseWheel += (_, e) => { ChangeVolume(e.Delta); e.Handled = true; };
            ProgressTrack.MouseLeftButtonDown += (_, e) =>
            {
                double w = ProgressTrack.ActualWidth;
                if (w <= 0 || _media.Duration <= TimeSpan.Zero) return;
                double frac = Math.Clamp(e.GetPosition(ProgressTrack).X / w, 0, 1);
                _media.Seek(TimeSpan.FromSeconds(_media.Duration.TotalSeconds * frac));
                e.Handled = true;
            };
            Closed += (_, _) => { if (_hook != IntPtr.Zero) Native.UnhookWindowsHookEx(_hook); };
            Island.MouseRightButtonUp += (_, e) => { ShowMenu(); e.Handled = true; };

            _expandDelay.Tick += (_, _) => { _expandDelay.Stop(); Expand(); };
            _collapseDelay.Tick += (_, _) => { _collapseDelay.Stop(); if (!_hover && !NotesBox.IsKeyboardFocusWithin) Collapse(); };
            _volumeHide.Tick += (_, _) => { _volumeHide.Stop(); _volumeShowing = false; UpdateClock(); };
            _popHide.Tick += (_, _) => { _popHide.Stop(); if (!_hover && !NotesBox.IsKeyboardFocusWithin) Collapse(); };
            _clock.Tick += (_, _) => { UpdateClock(); CheckShiftReminder(); };
            _poll.Tick += (_, _) => Poll();

            PrevBtn.Click += (_, _) => _media.Previous();
            NextBtn.Click += (_, _) => _media.Next();
            PlayBtn.Click += (_, _) => _media.TogglePlayPause();

            InitPages();
            InitV3();
            ApplyAppearance();
            ApplyTabs();
            _media.Changed += UpdateMedia;
            _media.TrackChanged += OnTrackChanged;

            SystemParameters.StaticPropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(SystemParameters.WorkArea)) ApplyPosition();
            };
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged += (_, _) => Dispatcher.BeginInvoke(ApplyPosition);

            Loaded += async (_, _) =>
            {
                UpdateClock();
                UpdateMedia();
                _clock.Start();
                _poll.Start();
                await _media.InitAsync();
                UpdateMedia();
            };
        }

        // ================= okno, vrstvy, pozice =================

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            _hwnd = new WindowInteropHelper(this).Handle;
            long ex = Native.GetWindowLongPtr(_hwnd, Native.GWL_EXSTYLE).ToInt64();
            ex |= Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE;
            Native.SetWindowLongPtr(_hwnd, Native.GWL_EXSTYLE, new IntPtr(ex));
            HwndSource.FromHwnd(_hwnd)?.AddHook(WndProc);
            ApplyPosition();
            ApplyLayer();
            InstallWheelHook();
        }

        // ================= kolečko myši (hlasitost) =================
        // Okno se neaktivuje, takže kolečko nemusí dostat. Proto globální hook,
        // který reaguje jen když je kurzor opravdu nad viditelným ostrůvkem.

        private IntPtr _hook;
        private Native.LowLevelMouseProc? _hookProc;

        private void InstallWheelHook()
        {
            _hookProc = WheelHook;
            _hook = Native.SetWindowsHookEx(Native.WH_MOUSE_LL, _hookProc, Native.GetModuleHandle(null), 0);
            if (_hook == IntPtr.Zero) Log.Write("Hook kolečka se nepodařilo nainstalovat: " + Marshal.GetLastWin32Error());
        }

        private IntPtr WheelHook(int nCode, IntPtr wParam, IntPtr lParam)
        {
            try
            {
                if (nCode >= 0 && wParam.ToInt32() == Native.WM_MOUSEWHEEL && Root.Visibility == Visibility.Visible)
                {
                    var info = Marshal.PtrToStructure<Native.MSLLHOOKSTRUCT>(lParam);
                    if (IsOverVisibleIsland(info.pt))
                    {
                        int delta = (short)((info.mouseData >> 16) & 0xFFFF);
                        var sv = ScrollViewerAt(info.pt);
                        if (sv != null) Dispatcher.BeginInvoke(() => sv.ScrollToVerticalOffset(sv.VerticalOffset - delta / 4.0));
                        else Dispatcher.BeginInvoke(() => ChangeVolume(delta));
                        return new IntPtr(1); // kolečko spolkneme, ať neroluje okno pod tím
                    }
                }
            }
            catch { }
            return Native.CallNextHookEx(_hook, nCode, wParam, lParam);
        }

        private bool IsOverVisibleIsland(Native.POINT pt)
        {
            // není ostrůvek překrytý jiným oknem?
            var under = Native.WindowFromPoint(pt);
            if (under != _hwnd && Native.GetAncestor(under, 2 /*GA_ROOT*/) != _hwnd) return false;
            if (Island.ActualWidth <= 0) return false;
            var tl = Island.PointToScreen(new Point(0, 0));
            var br = Island.PointToScreen(new Point(Island.ActualWidth, Island.ActualHeight));
            return pt.X >= tl.X && pt.X <= br.X && pt.Y >= tl.Y && pt.Y <= br.Y;
        }

        // ================= přetahování myší =================

        private bool _dragPending, _dragging;
        private Point _dragStartCursor;
        private double _dragStartLeft, _dragStartTop;

        private Point CursorDip()
        {
            Native.GetCursorPos(out var p);
            var src = PresentationSource.FromVisual(this);
            return src?.CompositionTarget?.TransformFromDevice.Transform(new Point(p.X, p.Y)) ?? new Point(p.X, p.Y);
        }

        private static bool IsInside<T>(object? source) where T : DependencyObject
        {
            var d = source as DependencyObject;
            while (d != null)
            {
                if (d is T) return true;
                d = d is Visual || d is System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
            }
            return false;
        }

        private void OnDragDown(object sender, MouseButtonEventArgs e)
        {
            if (IsInside<Button>(e.OriginalSource) || IsInside<TextBox>(e.OriginalSource) || IsNoDrag(e.OriginalSource)) return;
            if (e.OriginalSource is DependencyObject d && IsDescendantOf(d, ProgressTrack)) return;
            _dragPending = true;
            _dragging = false;
            _dragStartCursor = CursorDip();
            _dragStartLeft = Left;
            _dragStartTop = Top;
            Island.CaptureMouse();
            e.Handled = true;
        }

        private static bool IsDescendantOf(DependencyObject d, DependencyObject ancestor)
        {
            while (d != null)
            {
                if (d == ancestor) return true;
                d = d is Visual ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
            }
            return false;
        }

        private void OnDragMove(object sender, MouseEventArgs e)
        {
            if (!_dragPending) return;
            var c = CursorDip();
            double dx = c.X - _dragStartCursor.X, dy = c.Y - _dragStartCursor.Y;
            if (!_dragging && Math.Abs(dx) + Math.Abs(dy) < 6) return;
            if (!_dragging)
            {
                _dragging = true;
                _popHide.Stop();
                _expandDelay.Stop();
                if (_expanded) Collapse();
                Island.Cursor = Cursors.SizeAll;
            }
            Left = _dragStartLeft + dx;
            Top = _dragStartTop + dy;
        }

        private void OnDragUp(object sender, MouseButtonEventArgs e)
        {
            if (!_dragPending) return;
            bool wasDrag = _dragging;
            EndDrag();
            if (!wasDrag)
            {
                if (_expanded) Collapse(); else Expand();
            }
            e.Handled = true;
        }

        private void EndDrag()
        {
            bool wasDrag = _dragging;
            _dragPending = false;
            _dragging = false;
            Island.Cursor = Cursors.Arrow;
            if (Island.IsMouseCaptured) Island.ReleaseMouseCapture();
            if (!wasDrag) return;

            // přichytit nahoru / dolů podle toho, kde ho pustíš, a zapamatovat posun do strany
            var wa = SystemParameters.WorkArea;
            var c = CursorDip();
            _s.Position = c.Y < wa.Top + wa.Height / 2 ? IslandPosition.Top : IslandPosition.Bottom;
            _s.OffsetX = (Left + Width / 2) - (wa.Left + wa.Width / 2);
            _s.Save();
            ApplyPosition();
        }

        /// <summary>V režimu "jen na ploše" drží okno vespod pod všemi programy.</summary>
        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == Native.WM_CLIPBOARDUPDATE) Dispatcher.BeginInvoke(OnClipboardChanged);
            if (msg == Native.WM_WINDOWPOSCHANGING && _s.Layer == LayerMode.Desktop && !_revealed)
            {
                var pos = Marshal.PtrToStructure<Native.WINDOWPOS>(lParam);
                pos.hwndInsertAfter = Native.HWND_BOTTOM;
                pos.flags &= ~Native.SWP_NOZORDER;
                Marshal.StructureToPtr(pos, lParam, false);
            }
            return IntPtr.Zero;
        }

        public void ApplyLayer()
        {
            if (_hwnd == IntPtr.Zero) return;
            if (_s.Layer == LayerMode.AlwaysOnTop)
            {
                _revealed = false;
                Topmost = true;
            }
            else
            {
                _revealed = false;
                Topmost = false;
                Native.SetWindowPos(_hwnd, Native.HWND_BOTTOM, 0, 0, 0, 0,
                    Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
            }
        }

        public void ApplyPosition()
        {
            var wa = SystemParameters.WorkArea;
            // ostrůvek (i rozbalený) musí zůstat celý na obrazovce
            double maxOff = Math.Max(0, (wa.Width - ExpW * _s.Scale) / 2 - 8);
            _s.OffsetX = Math.Clamp(_s.OffsetX, -maxOff, maxOff);
            Left = wa.Left + (wa.Width - Width) / 2 + _s.OffsetX;
            bool top = _s.Position == IslandPosition.Top;
            Top = top ? wa.Top : wa.Bottom - Height;
            var va = top ? VerticalAlignment.Top : VerticalAlignment.Bottom;
            Island.VerticalAlignment = va;
            Glow.VerticalAlignment = va;
        }

        private void Reveal()
        {
            if (_revealed) return;
            _revealed = true;
            Topmost = true;
            Native.SetWindowPos(_hwnd, Native.HWND_TOPMOST, 0, 0, 0, 0,
                Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW);
        }

        private void Unreveal()
        {
            if (!_revealed) return;
            _revealed = false;
            Topmost = false;
            Native.SetWindowPos(_hwnd, Native.HWND_BOTTOM, 0, 0, 0, 0,
                Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
            if (_expanded && !_hover) Collapse();
        }

        /// <summary>Každých 100 ms: hra přes celou obrazovku? myš u okraje obrazovky?</summary>
        private void Poll()
        {
            if (_hwnd == IntPtr.Zero) return;
            bool fullscreen = Native.IsFullscreenAppActive(_hwnd);

            // schovat ve hrách / videu přes celou obrazovku
            bool hide = _s.HideInFullscreen && fullscreen && !_revealed;
            if (hide != _hiddenForFullscreen)
            {
                _hiddenForFullscreen = hide;
                Root.Visibility = hide ? Visibility.Hidden : Visibility.Visible;
            }

            CheckStaleHover();

            if (_s.Layer != LayerMode.Desktop) return;

            // vyjetí nad okna po najetí myší k okraji
            if (!Native.GetCursorPos(out var p)) return;
            var src = PresentationSource.FromVisual(this);
            if (src?.CompositionTarget == null) return;
            var pt = src.CompositionTarget.TransformFromDevice.Transform(new Point(p.X, p.Y));

            double screenH = SystemParameters.PrimaryScreenHeight;
            bool top = _s.Position == IslandPosition.Top;
            var islandPos = Island.TranslatePoint(new Point(0, 0), this);
            var islandEnd = Island.TranslatePoint(new Point(Island.ActualWidth, Island.ActualHeight), this);
            double ixL = Left + islandPos.X, ixR = Left + islandEnd.X;
            double iyT = Top + islandPos.Y, iyB = Top + islandEnd.Y;

            bool inZoneX = pt.X >= ixL - 150 && pt.X <= ixR + 150;
            bool atEdge = inZoneX && (top ? pt.Y <= 2 : pt.Y >= screenH - 3);
            bool nearIsland = pt.X >= ixL - 30 && pt.X <= ixR + 30 &&
                              pt.Y >= Math.Min(iyT, top ? 0 : iyT) - 30 &&
                              pt.Y <= Math.Max(iyB, top ? iyB : screenH) + 30;

            if (atEdge && !fullscreen)
            {
                Reveal();
                _revealLeaveSince = DateTime.MinValue;
            }
            else if (_revealed)
            {
                if (nearIsland || _hover || _typing || DateTime.Now < _forceRevealUntil) _revealLeaveSince = DateTime.MinValue;
                else if (_revealLeaveSince == DateTime.MinValue) _revealLeaveSince = DateTime.Now;
                else if ((DateTime.Now - _revealLeaveSince).TotalMilliseconds > 600) Unreveal();
            }
        }

        // ================= rozbalení / sbalení =================

        public static readonly DependencyProperty IslandRadiusProperty =
            DependencyProperty.Register(nameof(IslandRadius), typeof(double), typeof(MainWindow),
                new PropertyMetadata(18.0, (d, e) => ((MainWindow)d).Island.CornerRadius = new CornerRadius((double)e.NewValue)));

        public double IslandRadius
        {
            get => (double)GetValue(IslandRadiusProperty);
            set => SetValue(IslandRadiusProperty, value);
        }

        private void AnimateIsland(double w, double h, double radius, bool bouncy)
        {
            var dur = TimeSpan.FromMilliseconds(bouncy ? 420 : 300);
            IEasingFunction ease = bouncy
                ? new BackEase { Amplitude = 0.28, EasingMode = EasingMode.EaseOut }
                : new CubicEase { EasingMode = EasingMode.EaseOut };
            Island.BeginAnimation(WidthProperty, new DoubleAnimation(w, dur) { EasingFunction = ease });
            Island.BeginAnimation(HeightProperty, new DoubleAnimation(h, dur) { EasingFunction = ease });
            BeginAnimation(IslandRadiusProperty, new DoubleAnimation(radius, dur) { EasingFunction = ease });
        }

        private void ShowView(FrameworkElement target)
        {
            foreach (var v in new FrameworkElement[] { CompactView, ExpandedView })
            {
                if (v == target) continue;
                v.BeginAnimation(OpacityProperty, null);
                v.Opacity = 0;
                v.Visibility = Visibility.Collapsed;
            }
            target.Visibility = Visibility.Visible;
            target.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220))
            {
                BeginTime = TimeSpan.FromMilliseconds(_expanded ? 110 : 60)
            });
        }

        private void Expand()
        {
            bool wasExpanded = _expanded;
            _expanded = true;
            AnimateIsland(ExpW, ExpH, 30, true);
            if (!wasExpanded) ShowView(ExpandedView);
            ShowPage(_page, animate: wasExpanded);
            UpdateClock();
        }

        private void Collapse()
        {
            _expanded = false;
            AnimateIsland(_media.HasMedia ? CompactWMedia : CompactWIdle, CompactH, CompactH / 2, false);
            ShowView(CompactView);
            OnPagesHidden();
        }

        private void OnTrackChanged()
        {
            if (!_s.PopOnNewTrack || _hover || _hiddenForFullscreen || NotesBox.IsKeyboardFocusWithin) return;
            if (_s.HiddenTabs.Contains("Music")) return;
            _page = Page.Music;
            Expand();
            _popHide.Stop();
            _popHide.Start();
        }

        // ================= obsah =================

        private void UpdateMedia()
        {
            bool has = _media.HasMedia;

            var artBrush = _media.Art != null ? new ImageBrush(_media.Art) { Stretch = Stretch.UniformToFill } : null;
            CompactArt.Background = artBrush;
            CompactArt.Visibility = has && artBrush != null ? Visibility.Visible : Visibility.Collapsed;
            BigArt.Background = artBrush ?? (Brush)new SolidColorBrush(Color.FromArgb(0x22, 255, 255, 255));

            TitleText.Text = _media.Title;
            ArtistText.Text = _media.Artist;
            ArtistText.Visibility = string.IsNullOrWhiteSpace(_media.Artist) ? Visibility.Collapsed : Visibility.Visible;
            if (!_volumeShowing) SourceText.Text = _media.SourceApp;
            PlayBtn.Content = _media.IsPlaying ? "" : "";

            Bars.Visibility = has ? Visibility.Visible : Visibility.Collapsed;
            SetBarsRunning(has && _media.IsPlaying);

            var c = CurrentAccentColor();
            var dur = TimeSpan.FromMilliseconds(600);
            _accent.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(c, dur));
            _accentBorder.BeginAnimation(SolidColorBrush.ColorProperty,
                new ColorAnimation(Color.FromArgb(0x55, c.R, c.G, c.B), dur));
            Glow.Visibility = has ? Visibility.Visible : Visibility.Collapsed;
            UpdateCompactWeather();

            // velikost podle stavu
            if (_expanded)
            {
                if (_page == Page.Music) ShowPage(Page.Music, animate: false);
            }
            else
            {
                AnimateIsland(has ? CompactWMedia : CompactWIdle, CompactH, CompactH / 2, false);
            }
            UpdateClock();
        }

        private void UpdateClock()
        {
            var now = DateTime.Now;
            string time = now.ToString("HH:mm", Cs);
            string date = now.ToString("dddd d. MMMM", Cs);
            if (date.Length > 0) date = char.ToUpper(date[0], Cs) + date[1..];

            UpdateTimer();
            if (!_volumeShowing)
            {
                if (_timerRunning)
                {
                    CompactText.Text = FmtTimer(_timerLeft);
                    CompactIcon.Text = "\uE916";
                    CompactIcon.Visibility = Visibility.Visible;
                }
                else
                {
                    CompactText.Text = time;
                    CompactIcon.Visibility = Visibility.Collapsed;
                }
            }
            if (_expanded && _page == Page.Widgets) UpdateShift();
            ExpTime.Text = time;
            ExpDate.Text = now.ToString("d. M.", Cs);
            IdleTime.Text = time;
            IdleDate.Text = date;

            var d = _media.Duration;
            if (d.TotalSeconds >= 1)
            {
                ProgressRow.Visibility = Visibility.Visible;
                var p = _media.Position;
                PosText.Text = Fmt(p);
                DurText.Text = Fmt(d);
                double frac = Math.Clamp(p.TotalSeconds / d.TotalSeconds, 0, 1);
                ProgressFill.Width = Math.Max(0, ProgressTrack.ActualWidth * frac);
            }
            else
            {
                ProgressRow.Visibility = Visibility.Collapsed;
            }
        }

        private static string Fmt(TimeSpan t) =>
            t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss");

        private void ChangeVolume(int wheelDelta)
        {
            if (wheelDelta == 0) return;
            var level = AudioVolume.Change(wheelDelta > 0 ? 0.04f : -0.04f);
            if (level == null) return;
            int pct = (int)Math.Round(level.Value * 100);
            _volumeShowing = true;
            CompactIcon.Text = pct == 0 ? "" : "";
            CompactIcon.Visibility = Visibility.Visible;
            CompactText.Text = pct + " %";
            SourceText.Text = "Hlasitost " + pct + " %";
            _volumeHide.Stop();
            _volumeHide.Start();
        }

        // ================= ekvalizér =================

        private Rectangle[] BarRects()
        {
            var list = new Rectangle[Bars.Children.Count];
            for (int i = 0; i < list.Length; i++) list[i] = (Rectangle)Bars.Children[i];
            return list;
        }

        private void SetBarsRunning(bool run)
        {
            if (run == _barsRunning) return;
            _barsRunning = run;
            double[] speeds = { 0.42, 0.31, 0.47, 0.36 };
            double[] lows = { 0.30, 0.45, 0.25, 0.40 };
            var rects = BarRects();
            for (int i = 0; i < rects.Length; i++)
            {
                if (rects[i].RenderTransform is not ScaleTransform st)
                {
                    st = new ScaleTransform(1, 0.35);
                    rects[i].RenderTransform = st;
                }
                if (run)
                {
                    st.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(lows[i], 1.0, TimeSpan.FromSeconds(speeds[i]))
                    {
                        AutoReverse = true,
                        RepeatBehavior = RepeatBehavior.Forever,
                        EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
                    });
                }
                else
                {
                    st.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.25, TimeSpan.FromMilliseconds(250)));
                }
            }
        }

        // ================= nastavení (menu pravým tlačítkem i z ikony v liště) =================

        public Settings Settings => _s;

        public void SetPosition(IslandPosition p)
        {
            _s.Position = p; _s.Save();
            ApplyPosition();
        }

        public void SetLayer(LayerMode m)
        {
            _s.Layer = m; _s.Save();
            ApplyLayer();
        }

        public void CenterIsland()
        {
            _s.OffsetX = 0; _s.Save();
            ApplyPosition();
        }

        public void SetHideInFullscreen(bool v) { _s.HideInFullscreen = v; _s.Save(); }
        public void SetPopOnNewTrack(bool v) { _s.PopOnNewTrack = v; _s.Save(); }

        private void ShowMenu()
        {
            var m = new ContextMenu();
            MenuItem Item(string text, bool isChecked, Action click)
            {
                var mi = new MenuItem { Header = text, IsCheckable = true, IsChecked = isChecked };
                mi.Click += (_, _) => click();
                return mi;
            }
            var settingsItem = new MenuItem { Header = "Nastavení…", FontWeight = FontWeights.SemiBold };
            settingsItem.Click += (_, _) => OpenSettings();
            m.Items.Add(settingsItem);
            m.Items.Add(new Separator());
            m.Items.Add(Item("Nahoře", _s.Position == IslandPosition.Top, () => SetPosition(IslandPosition.Top)));
            m.Items.Add(Item("Dole", _s.Position == IslandPosition.Bottom, () => SetPosition(IslandPosition.Bottom)));
            var center = new MenuItem { Header = "Vycentrovat", IsEnabled = Math.Abs(_s.OffsetX) > 1 };
            center.Click += (_, _) => CenterIsland();
            m.Items.Add(center);
            m.Items.Add(new Separator());
            m.Items.Add(Item("Jen na ploše (vyjede po najetí k okraji)", _s.Layer == LayerMode.Desktop, () => SetLayer(LayerMode.Desktop)));
            m.Items.Add(Item("Vždy navrchu", _s.Layer == LayerMode.AlwaysOnTop, () => SetLayer(LayerMode.AlwaysOnTop)));
            m.Items.Add(new Separator());
            m.Items.Add(Item("Schovat ve hrách přes celou obrazovku", _s.HideInFullscreen, () => SetHideInFullscreen(!_s.HideInFullscreen)));
            m.Items.Add(Item("Ukázat novou skladbu", _s.PopOnNewTrack, () => SetPopOnNewTrack(!_s.PopOnNewTrack)));
            m.Items.Add(Item("Spouštět s Windows", Settings.StartWithWindows, () => Settings.StartWithWindows = !Settings.StartWithWindows));
            if (!HardwareService.IsAdmin)
            {
                var admin = new MenuItem { Header = "Spustit jako správce (teplota CPU)" };
                admin.Click += (_, _) => RestartAsAdmin();
                m.Items.Add(admin);
            }
            m.Items.Add(new Separator());
            var exit = new MenuItem { Header = "Ukončit Lumo" };
            exit.Click += (_, _) => Application.Current.Shutdown();
            m.Items.Add(exit);
            m.PlacementTarget = Island;
            m.IsOpen = true;
        }
    }
}
