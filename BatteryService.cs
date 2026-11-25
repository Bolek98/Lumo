using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Windows.Devices.Enumeration.Pnp;

namespace Lumo
{
    /// <summary>
    /// Baterie Bluetooth zařízení (sluchátka, ovladač, myš…) – stejné údaje, jaké ukazuje
    /// Nastavení Windows → Bluetooth a zařízení. Zařízení přes vlastní USB přijímač tu nejsou.
    /// </summary>
    public class BatteryService
    {
        private const string BatteryKey = "{104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2";
        private const string ContainerKey = "System.Devices.ContainerId";
        private const string NameKey = "System.ItemNameDisplay";

        public record Device(string Name, int Percent, string Glyph);

        public List<Device> Devices { get; private set; } = new();

        public async Task RefreshAsync()
        {
            try
            {
                var found = new Dictionary<Guid, int>();
                var all = await PnpObject.FindAllAsync(PnpObjectType.Device, new[] { BatteryKey, ContainerKey }, "");
                foreach (var d in all)
                {
                    if (!d.Properties.TryGetValue(BatteryKey, out var b) || b == null) continue;
                    int pct;
                    try { pct = Convert.ToInt32(b); } catch { continue; }
                    if (pct < 0 || pct > 100) continue;
                    var cid = d.Properties.TryGetValue(ContainerKey, out var c) && c is Guid g ? g : Guid.NewGuid();
                    found[cid] = found.TryGetValue(cid, out var old) ? Math.Min(old, pct) : pct;
                }

                var list = new List<Device>();
                foreach (var (cid, pct) in found)
                {
                    string name = "Zařízení";
                    try
                    {
                        var cont = await PnpObject.CreateFromIdAsync(PnpObjectType.DeviceContainer,
                            "{" + cid + "}", new[] { NameKey });
                        if (cont.Properties.TryGetValue(NameKey, out var n) && n is string s && s.Length > 0) name = s;
                    }
                    catch { }
                    list.Add(new Device(name, pct, GlyphFor(name)));
                }
                Devices = list.OrderBy(d => d.Name).ToList();
            }
            catch (Exception ex)
            {
                Log.Write("Baterie: " + ex.Message);
            }
        }

        private static string GlyphFor(string name)
        {
            string n = name.ToLowerInvariant();
            if (n.Contains("controller") || n.Contains("gamepad") || n.Contains("xbox") || n.Contains("dualsense") || n.Contains("ovladač")) return "";
            if (n.Contains("mouse") || n.Contains("myš")) return "";
            if (n.Contains("keyboard") || n.Contains("klávesnice")) return "";
            if (n.Contains("watch") || n.Contains("band")) return "";
            if (n.Contains("phone") || n.Contains("iphone") || n.Contains("galaxy") || n.Contains("pixel")) return "";
            return ""; // sluchátka – nejčastější
        }

        /// <summary>Ikona baterie podle procent (Segoe MDL2 Battery0–Battery10).</summary>
        public static string BatteryGlyph(int pct)
        {
            int lvl = Math.Clamp((int)Math.Round(pct / 10.0), 0, 10);
            return lvl == 10 ? "" : ((char)(0xE850 + lvl)).ToString();
        }
    }
}
