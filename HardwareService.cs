using System;
using System.Linq;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Security.Principal;
using LibreHardwareMonitor.Hardware;

namespace Lumo
{
    /// <summary>Teploty a vytížení CPU/GPU (LibreHardwareMonitor), RAM a síť.</summary>
    public sealed class HardwareService : IDisposable
    {
        private Computer? _pc;
        private bool _openFailed;
        private long _lastRx, _lastTx;
        private DateTime _lastNet = DateTime.MinValue;
        private readonly object _lock = new();

        public float? CpuTemp, CpuLoad, GpuTemp, GpuLoad;
        public string GpuName = "GPU";
        public double RamUsedGb, RamTotalGb, RamPercent;
        public double NetDownBps, NetUpBps;

        public static bool IsAdmin
        {
            get
            {
                try { return new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator); }
                catch { return false; }
            }
        }

        /// <summary>Volat z pozadí (může trvat stovky ms).</summary>
        public void Update()
        {
            lock (_lock)
            {
                UpdateSensors();
                UpdateRam();
                UpdateNet();
            }
        }

        private void UpdateSensors()
        {
            if (_openFailed) return;
            try
            {
                if (_pc == null)
                {
                    _pc = new Computer { IsCpuEnabled = true, IsGpuEnabled = true };
                    _pc.Open();
                }

                CpuTemp = CpuLoad = GpuTemp = GpuLoad = null;

                foreach (var hw in _pc.Hardware)
                {
                    hw.Update();
                    if (hw.HardwareType == HardwareType.Cpu)
                    {
                        CpuLoad = hw.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Load && s.Name == "CPU Total")?.Value;
                        var temps = hw.Sensors.Where(s => s.SensorType == SensorType.Temperature && s.Value.HasValue && s.Value > 0).ToList();
                        CpuTemp = (temps.FirstOrDefault(s => s.Name.Contains("Tctl") || s.Name.Contains("Tdie"))
                                   ?? temps.FirstOrDefault(s => s.Name.Contains("Package"))
                                   ?? temps.FirstOrDefault())?.Value;
                    }
                    else if ((hw.HardwareType == HardwareType.GpuNvidia || hw.HardwareType == HardwareType.GpuAmd) && GpuTemp == null)
                    {
                        GpuName = ShortGpuName(hw.Name);
                        GpuTemp = (hw.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Temperature && s.Name == "GPU Core")
                                   ?? hw.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Temperature))?.Value;
                        GpuLoad = (hw.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Load && s.Name == "GPU Core")
                                   ?? hw.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Load))?.Value;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Write("Senzory (LibreHardwareMonitor): " + ex.Message);
                _openFailed = true;
                try { _pc?.Close(); } catch { }
                _pc = null;
            }
        }

        private static string ShortGpuName(string name)
        {
            foreach (var p in new[] { "NVIDIA GeForce ", "NVIDIA ", "AMD Radeon ", "AMD " })
                if (name.StartsWith(p, StringComparison.OrdinalIgnoreCase)) return name[p.Length..];
            return name;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MEMORYSTATUSEX
        {
            public uint dwLength, dwMemoryLoad;
            public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
        }

        [DllImport("kernel32.dll")]
        private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX m);

        private void UpdateRam()
        {
            var m = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
            if (!GlobalMemoryStatusEx(ref m)) return;
            RamTotalGb = m.ullTotalPhys / 1073741824.0;
            RamUsedGb = (m.ullTotalPhys - m.ullAvailPhys) / 1073741824.0;
            RamPercent = m.dwMemoryLoad;
        }

        private void UpdateNet()
        {
            long rx = 0, tx = 0;
            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up) continue;
                    if (ni.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
                    var st = ni.GetIPStatistics();
                    rx += st.BytesReceived;
                    tx += st.BytesSent;
                }
            }
            catch { return; }

            var now = DateTime.Now;
            if (_lastNet != DateTime.MinValue)
            {
                double dt = (now - _lastNet).TotalSeconds;
                if (dt > 0.2)
                {
                    NetDownBps = Math.Max(0, (rx - _lastRx) / dt);
                    NetUpBps = Math.Max(0, (tx - _lastTx) / dt);
                }
            }
            _lastRx = rx; _lastTx = tx; _lastNet = now;
        }

        public static string FormatSpeed(double bytesPerSec)
        {
            double bits = bytesPerSec * 8;
            if (bits >= 1_000_000) return (bits / 1_000_000).ToString("0.0") + " Mb/s";
            if (bits >= 1_000) return (bits / 1_000).ToString("0") + " kb/s";
            return bits.ToString("0") + " b/s";
        }

        public void Dispose()
        {
            lock (_lock)
            {
                try { _pc?.Close(); } catch { }
                _pc = null;
            }
        }
    }
}
