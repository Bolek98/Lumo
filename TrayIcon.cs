using System;
using System.Drawing;
using WF = System.Windows.Forms;

namespace Lumo
{
    /// <summary>Ikona v oznamovací oblasti (vedle hodin) s nastavením.</summary>
    public sealed class TrayIcon : IDisposable
    {
        private readonly WF.NotifyIcon _icon;
        private readonly MainWindow _win;

        public TrayIcon(MainWindow win, Settings s)
        {
            _win = win;
            _icon = new WF.NotifyIcon
            {
                Text = "Lumo",
                Visible = true,
                Icon = LoadIcon(),
                ContextMenuStrip = new WF.ContextMenuStrip()
            };
            _icon.ContextMenuStrip.Opening += (_, _) => Rebuild();
            _icon.DoubleClick += (_, _) => _win.Dispatcher.Invoke(_win.OpenSettings);
            Rebuild();
        }

        private static Icon LoadIcon()
        {
            try
            {
                var path = Environment.ProcessPath;
                if (path != null)
                {
                    var ic = Icon.ExtractAssociatedIcon(path);
                    if (ic != null) return ic;
                }
            }
            catch { }
            return SystemIcons.Application;
        }

        private void Rebuild()
        {
            var s = _win.Settings;
            var items = _icon.ContextMenuStrip!.Items;
            items.Clear();

            WF.ToolStripMenuItem Item(string text, bool isChecked, Action click)
            {
                var mi = new WF.ToolStripMenuItem(text) { Checked = isChecked };
                mi.Click += (_, _) => _win.Dispatcher.Invoke(click);
                return mi;
            }

            items.Add(new WF.ToolStripLabel("Lumo") { Font = new Font(WF.Control.DefaultFont, FontStyle.Bold) });
            items.Add(new WF.ToolStripSeparator());
            items.Add(new WF.ToolStripMenuItem("Nastavení…", null, (_, _) => _win.Dispatcher.Invoke(_win.OpenSettings)) { Font = new Font(WF.Control.DefaultFont, FontStyle.Bold) });
            items.Add(new WF.ToolStripSeparator());
            items.Add(Item("Nahoře", s.Position == IslandPosition.Top, () => _win.SetPosition(IslandPosition.Top)));
            items.Add(Item("Dole", s.Position == IslandPosition.Bottom, () => _win.SetPosition(IslandPosition.Bottom)));
            items.Add(Item("Vycentrovat", false, () => _win.CenterIsland()));
            items.Add(new WF.ToolStripSeparator());
            items.Add(Item("Jen na ploše (vyjede po najetí k okraji)", s.Layer == LayerMode.Desktop, () => _win.SetLayer(LayerMode.Desktop)));
            items.Add(Item("Vždy navrchu", s.Layer == LayerMode.AlwaysOnTop, () => _win.SetLayer(LayerMode.AlwaysOnTop)));
            items.Add(new WF.ToolStripSeparator());
            items.Add(Item("Schovat ve hrách přes celou obrazovku", s.HideInFullscreen, () => _win.SetHideInFullscreen(!s.HideInFullscreen)));
            items.Add(Item("Ukázat novou skladbu", s.PopOnNewTrack, () => _win.SetPopOnNewTrack(!s.PopOnNewTrack)));
            items.Add(Item("Spouštět s Windows", Settings.StartWithWindows, () => Settings.StartWithWindows = !Settings.StartWithWindows));
            if (!HardwareService.IsAdmin && !Packaging.IsPackaged)
                items.Add(Item("Spustit jako správce (teplota CPU)", false, () => _win.RestartAsAdmin()));
            items.Add(new WF.ToolStripSeparator());
            items.Add(Item("Ukončit", false, () => System.Windows.Application.Current.Shutdown()));
        }

        public void Dispose()
        {
            _icon.Visible = false;
            _icon.Dispose();
        }
    }
}
