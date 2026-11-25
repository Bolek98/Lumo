using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Windows.Media.Control;

namespace Lumo
{
    /// <summary>Čte, co právě hraje (Spotify, prohlížeč, Apple Music…) přes systémové ovládání médií Windows.</summary>
    public class MediaService
    {
        private GlobalSystemMediaTransportControlsSessionManager? _mgr;
        private GlobalSystemMediaTransportControlsSession? _session;
        private readonly List<GlobalSystemMediaTransportControlsSession> _watched = new();
        private SynchronizationContext? _ui;
        private int _version;

        public event Action? Changed;
        public event Action? TrackChanged;

        public bool HasMedia => _session != null && !string.IsNullOrWhiteSpace(Title);
        public string Title { get; private set; } = "";
        public string Artist { get; private set; } = "";
        public string SourceApp { get; private set; } = "";
        public BitmapSource? Art { get; private set; }
        public Color? ArtColor { get; private set; }
        public bool IsPlaying { get; private set; }
        public TimeSpan Duration { get; private set; }
        private TimeSpan _position, _startTime;
        private DateTimeOffset _positionUpdated;

        public TimeSpan Position
        {
            get
            {
                var p = _position;
                if (IsPlaying && _positionUpdated != default)
                    p += DateTimeOffset.Now - _positionUpdated;
                if (Duration > TimeSpan.Zero && p > Duration) p = Duration;
                return p < TimeSpan.Zero ? TimeSpan.Zero : p;
            }
        }

        public async Task InitAsync()
        {
            _ui = SynchronizationContext.Current;
            try
            {
                _mgr = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
                _mgr.SessionsChanged += (_, _) => Post(RebuildWatch);
                _mgr.CurrentSessionChanged += (_, _) => Post(SelectSession);
                RebuildWatch();
            }
            catch (Exception ex) { Log.Write("Média nejdou načíst: " + ex.Message); }
        }

        private void Post(Action a)
        {
            if (_ui != null) _ui.Post(_ => a(), null); else a();
        }

        private void RebuildWatch()
        {
            if (_mgr == null) return;
            foreach (var s in _watched) s.PlaybackInfoChanged -= OnAnyPlayback;
            _watched.Clear();
            try
            {
                foreach (var s in _mgr.GetSessions())
                {
                    s.PlaybackInfoChanged += OnAnyPlayback;
                    _watched.Add(s);
                }
            }
            catch (Exception ex) { Log.Write("Seznam přehrávačů: " + ex.Message); }
            SelectSession();
        }

        private void OnAnyPlayback(GlobalSystemMediaTransportControlsSession s, PlaybackInfoChangedEventArgs e)
            => Post(SelectSession);

        /// <summary>Vybere přehrávač – přednost má ten, co právě hraje.</summary>
        private void SelectSession()
        {
            if (_mgr == null) return;
            GlobalSystemMediaTransportControlsSession? pick = null;
            try
            {
                pick = _watched.FirstOrDefault(s => IsSessionPlaying(s)) ?? _mgr.GetCurrentSession();
            }
            catch { }

            if (!SameSession(pick, _session))
            {
                if (_session != null)
                {
                    try
                    {
                        _session.MediaPropertiesChanged -= OnProps;
                        _session.TimelinePropertiesChanged -= OnTimeline;
                    }
                    catch { }
                }
                _session = pick;
                if (_session != null)
                {
                    _session.MediaPropertiesChanged += OnProps;
                    _session.TimelinePropertiesChanged += OnTimeline;
                }
                _ = RefreshPropsAsync();
            }
            RefreshPlayback();
            RefreshTimeline();
            Changed?.Invoke();
        }

        private static bool SameSession(GlobalSystemMediaTransportControlsSession? a, GlobalSystemMediaTransportControlsSession? b)
        {
            if (a == null || b == null) return a == b;
            try { return a.SourceAppUserModelId == b.SourceAppUserModelId; } catch { return false; }
        }

        private static bool IsSessionPlaying(GlobalSystemMediaTransportControlsSession s)
        {
            try
            {
                return s.GetPlaybackInfo()?.PlaybackStatus ==
                       GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            }
            catch { return false; }
        }

        private void OnProps(GlobalSystemMediaTransportControlsSession s, MediaPropertiesChangedEventArgs e)
            => Post(() => _ = RefreshPropsAsync());

        private void OnTimeline(GlobalSystemMediaTransportControlsSession s, TimelinePropertiesChangedEventArgs e)
            => Post(() => { RefreshTimeline(); Changed?.Invoke(); });

        private void RefreshPlayback()
        {
            IsPlaying = _session != null && IsSessionPlaying(_session);
        }

        private void RefreshTimeline()
        {
            try
            {
                var t = _session?.GetTimelineProperties();
                if (t == null) { Duration = TimeSpan.Zero; _position = TimeSpan.Zero; return; }
                _startTime = t.StartTime;
                Duration = t.EndTime - t.StartTime;
                _position = t.Position - t.StartTime;
                _positionUpdated = t.LastUpdatedTime;
                // některé přehrávače posílají nesmyslný čas aktualizace
                if (_positionUpdated > DateTimeOffset.Now || DateTimeOffset.Now - _positionUpdated > TimeSpan.FromHours(6))
                    _positionUpdated = DateTimeOffset.Now;
            }
            catch { Duration = TimeSpan.Zero; }
        }

        private async Task RefreshPropsAsync()
        {
            int v = Interlocked.Increment(ref _version);
            var session = _session;
            if (session == null)
            {
                Title = Artist = SourceApp = "";
                Art = null; ArtColor = null;
                Changed?.Invoke();
                return;
            }

            try
            {
                var props = await session.TryGetMediaPropertiesAsync();
                if (v != _version) return;

                string oldTitle = Title;
                Title = props?.Title ?? "";
                Artist = props?.Artist ?? "";
                if (string.IsNullOrWhiteSpace(Artist)) Artist = props?.AlbumArtist ?? "";
                SourceApp = PrettyAppName(session.SourceAppUserModelId);

                BitmapSource? art = null;
                if (props?.Thumbnail != null)
                {
                    try
                    {
                        using var ras = await props.Thumbnail.OpenReadAsync();
                        using var stream = ras.AsStreamForRead();
                        var ms = new MemoryStream();
                        await stream.CopyToAsync(ms);
                        ms.Position = 0;
                        var bmp = new BitmapImage();
                        bmp.BeginInit();
                        bmp.CacheOption = BitmapCacheOption.OnLoad;
                        bmp.DecodePixelWidth = 200;
                        bmp.StreamSource = ms;
                        bmp.EndInit();
                        bmp.Freeze();
                        art = bmp;
                    }
                    catch (Exception ex) { Log.Write("Obal alba: " + ex.Message); }
                }
                if (v != _version) return;

                Art = art;
                ArtColor = art != null ? ColorTools.DominantColor(art) : null;
                RefreshPlayback();
                RefreshTimeline();
                Changed?.Invoke();
                if (!string.IsNullOrWhiteSpace(Title) && Title != oldTitle && !string.IsNullOrEmpty(oldTitle))
                    TrackChanged?.Invoke();
            }
            catch (Exception ex)
            {
                Log.Write("Info o skladbě: " + ex.Message);
            }
        }

        private static string PrettyAppName(string aumid)
        {
            string a = aumid.ToLowerInvariant();
            if (a.Contains("spotify")) return "Spotify";
            if (a.Contains("chrome")) return "Chrome";
            if (a.Contains("msedge")) return "Edge";
            if (a.Contains("firefox")) return "Firefox";
            if (a.Contains("opera")) return "Opera";
            if (a.Contains("brave")) return "Brave";
            if (a.Contains("applemusic") || a.Contains("apple")) return "Apple Music";
            if (a.Contains("zune") || a.Contains("media")) return "Přehrávač médií";
            if (a.Contains("vlc")) return "VLC";
            var name = Path.GetFileNameWithoutExtension(aumid.Split('!')[0]);
            return string.IsNullOrEmpty(name) ? "" : name;
        }

        public async void TogglePlayPause()
        {
            try { if (_session != null) await _session.TryTogglePlayPauseAsync(); } catch { }
        }
        public async void Seek(TimeSpan pos)
        {
            if (_session == null || Duration <= TimeSpan.Zero) return;
            try
            {
                // hned posunout i zobrazení, ať to nečeká na přehrávač
                _position = pos;
                _positionUpdated = DateTimeOffset.Now;
                Changed?.Invoke();
                await _session.TryChangePlaybackPositionAsync((_startTime + pos).Ticks);
            }
            catch (Exception ex) { Log.Write("Přeskočení ve skladbě: " + ex.Message); }
        }

        public async void Next()
        {
            try { if (_session != null) await _session.TrySkipNextAsync(); } catch { }
        }
        public async void Previous()
        {
            try { if (_session != null) await _session.TrySkipPreviousAsync(); } catch { }
        }
    }

    public static class ColorTools
    {
        /// <summary>Výrazná barva z obalu alba (něco jako má Ember).</summary>
        public static Color DominantColor(BitmapSource src)
        {
            try
            {
                var scaled = new TransformedBitmap(src, new ScaleTransform(32.0 / src.PixelWidth, 32.0 / src.PixelHeight));
                var conv = new FormatConvertedBitmap(scaled, PixelFormats.Bgra32, null, 0);
                int w = conv.PixelWidth, h = conv.PixelHeight;
                var px = new byte[w * h * 4];
                conv.CopyPixels(px, w * 4, 0);

                double r = 0, g = 0, b = 0, wsum = 0;
                double ar = 0, ag = 0, ab = 0; int n = 0;
                for (int i = 0; i < px.Length; i += 4)
                {
                    double bb = px[i] / 255.0, gg = px[i + 1] / 255.0, rr = px[i + 2] / 255.0;
                    ar += rr; ag += gg; ab += bb; n++;
                    double max = Math.Max(rr, Math.Max(gg, bb)), min = Math.Min(rr, Math.Min(gg, bb));
                    double sat = max == 0 ? 0 : (max - min) / max;
                    if (sat < 0.25 || max < 0.2) continue;
                    double weight = sat * sat * max;
                    r += rr * weight; g += gg * weight; b += bb * weight; wsum += weight;
                }
                if (wsum > 0.5) { r /= wsum; g /= wsum; b /= wsum; }
                else if (n > 0) { r = ar / n; g = ag / n; b = ab / n; }
                return Normalize(r, g, b);
            }
            catch { return Color.FromRgb(0x7C, 0x8C, 0xFF); }
        }

        /// <summary>Upraví barvu, aby byla dost syté a světlá na černém pozadí.</summary>
        private static Color Normalize(double r, double g, double b)
        {
            double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
            double l = (max + min) / 2, s = 0, hue = 0, d = max - min;
            if (d > 0.0001)
            {
                s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
                if (max == r) hue = (g - b) / d + (g < b ? 6 : 0);
                else if (max == g) hue = (b - r) / d + 2;
                else hue = (r - g) / d + 4;
                hue /= 6;
            }
            if (s < 0.15) { s = 0.0; l = 0.8; }       // šedý obal → světle šedá
            else { s = Math.Max(s, 0.55); l = Math.Clamp(l, 0.55, 0.68); }
            return FromHsl(hue, s, l);
        }

        private static Color FromHsl(double h, double s, double l)
        {
            double q = l < 0.5 ? l * (1 + s) : l + s - l * s, p = 2 * l - q;
            byte C(double t)
            {
                if (t < 0) t += 1; if (t > 1) t -= 1;
                double v = t < 1.0 / 6 ? p + (q - p) * 6 * t
                         : t < 0.5 ? q
                         : t < 2.0 / 3 ? p + (q - p) * (2.0 / 3 - t) * 6
                         : p;
                return (byte)Math.Round(Math.Clamp(v, 0, 1) * 255);
            }
            if (s == 0)
            {
                byte gray = (byte)Math.Round(l * 255);
                return Color.FromRgb(gray, gray, gray);
            }
            return Color.FromRgb(C(h + 1.0 / 3), C(h), C(h - 1.0 / 3));
        }
    }
}
