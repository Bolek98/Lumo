using System;
using System.Runtime.InteropServices;

namespace Lumo
{
    /// <summary>Hlavní hlasitost systému přes Core Audio (bez zobrazení okénka hlasitosti Windows).</summary>
    public static class AudioVolume
    {
        public static float? Get()
        {
            try
            {
                var vol = GetEndpoint();
                if (vol == null) return null;
                vol.GetMasterVolumeLevelScalar(out float level);
                Marshal.ReleaseComObject(vol);
                return level;
            }
            catch (Exception ex) { Log.Write("Hlasitost (čtení): " + ex.Message); return null; }
        }

        public static float? Change(float delta)
        {
            try
            {
                var vol = GetEndpoint();
                if (vol == null) return null;
                vol.GetMasterVolumeLevelScalar(out float level);
                level = Math.Clamp(level + delta, 0f, 1f);
                var ctx = Guid.Empty;
                vol.SetMasterVolumeLevelScalar(level, ref ctx);
                if (level > 0) vol.SetMute(false, ref ctx);
                Marshal.ReleaseComObject(vol);
                return level;
            }
            catch (Exception ex) { Log.Write("Hlasitost (změna): " + ex.Message); return null; }
        }

        private static IAudioEndpointVolume? GetEndpoint()
        {
            var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorCom();
            try
            {
                if (enumerator.GetDefaultAudioEndpoint(0 /*eRender*/, 1 /*eMultimedia*/, out var device) != 0 || device == null)
                    return null;
                var iid = typeof(IAudioEndpointVolume).GUID;
                device.Activate(ref iid, 23 /*CLSCTX_ALL*/, IntPtr.Zero, out object o);
                Marshal.ReleaseComObject(device);
                return (IAudioEndpointVolume)o;
            }
            finally { Marshal.ReleaseComObject(enumerator); }
        }

        [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
        private class MMDeviceEnumeratorCom { }

        [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDeviceEnumerator
        {
            int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
            [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
        }

        [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDevice
        {
            [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams,
                [MarshalAs(UnmanagedType.IUnknown)] out object anInterface);
        }

        [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioEndpointVolume
        {
            int RegisterControlChangeNotify(IntPtr notify);
            int UnregisterControlChangeNotify(IntPtr notify);
            int GetChannelCount(out uint count);
            int SetMasterVolumeLevel(float levelDB, ref Guid ctx);
            int SetMasterVolumeLevelScalar(float level, ref Guid ctx);
            int GetMasterVolumeLevel(out float levelDB);
            int GetMasterVolumeLevelScalar(out float level);
            int SetChannelVolumeLevel(uint channel, float levelDB, ref Guid ctx);
            int SetChannelVolumeLevelScalar(uint channel, float level, ref Guid ctx);
            int GetChannelVolumeLevel(uint channel, out float levelDB);
            int GetChannelVolumeLevelScalar(uint channel, out float level);
            int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid ctx);
            int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
        }
    }
}
