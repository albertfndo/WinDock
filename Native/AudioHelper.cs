using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace WinDock.Native
{
    public static class AudioHelper
    {
        [ComImport]
        [Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
        private class MMDeviceEnumeratorComObject { }

        [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDeviceEnumerator
        {
            int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr ppDevices);
            int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice ppDevice);
        }

        [Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDevice
        {
            int Activate(ref Guid id, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object ppInterface);
        }

        [Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioEndpointVolume
        {
            int RegisterControlChangeNotify(IntPtr pNotify);
            int UnregisterControlChangeNotify(IntPtr pNotify);
            int GetChannelCount(out uint pnChannelCount);
            int SetMasterVolumeLevel(float fLevelDB, ref Guid pguidEventContext);
            int SetMasterVolumeLevelScalar(float fLevel, ref Guid pguidEventContext);
            int GetMasterVolumeLevel(out float pfLevelDB);
            int GetMasterVolumeLevelScalar(out float pfLevel);
            int SetMute([MarshalAs(UnmanagedType.Bool)] bool bMute, ref Guid pguidEventContext);
            int GetMute([MarshalAs(UnmanagedType.Bool)] out bool pbMute);
        }

        private static IAudioEndpointVolume? GetEndpointVolume()
        {
            try
            {
                var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
                // 0 = eRender, 1 = eMultimedia
                int hr = enumerator.GetDefaultAudioEndpoint(0, 1, out IMMDevice dev);
                if (hr != 0 || dev == null) return null;

                var guid = typeof(IAudioEndpointVolume).GUID;
                hr = dev.Activate(ref guid, 1 /* CLSCTX_INPROC_SERVER */, IntPtr.Zero, out object epvObj);
                if (hr == 0 && epvObj is IAudioEndpointVolume epv)
                {
                    return epv;
                }
            }
            catch { }
            return null;
        }

        public static (int VolumePercent, bool IsMuted) GetMasterVolume()
        {
            try
            {
                var epv = GetEndpointVolume();
                if (epv != null)
                {
                    epv.GetMasterVolumeLevelScalar(out float level);
                    epv.GetMute(out bool isMuted);
                    return ((int)Math.Round(level * 100), isMuted);
                }
            }
            catch { }
            return (50, false);
        }

        public static void SetMasterVolume(float level0To1)
        {
            try
            {
                var epv = GetEndpointVolume();
                if (epv != null)
                {
                    var guid = Guid.Empty;
                    epv.SetMasterVolumeLevelScalar(Math.Clamp(level0To1, 0f, 1f), ref guid);
                    return;
                }
            }
            catch { }
        }

        public static void ToggleMute()
        {
            try
            {
                var epv = GetEndpointVolume();
                if (epv != null)
                {
                    epv.GetMute(out bool isMuted);
                    var guid = Guid.Empty;
                    epv.SetMute(!isMuted, ref guid);
                    return;
                }
            }
            catch { }

            NativeMethods.keybd_event(NativeMethods.VK_VOLUME_MUTE, 0, 0, 0);
            NativeMethods.keybd_event(NativeMethods.VK_VOLUME_MUTE, 0, NativeMethods.KEYEVENTF_KEYUP, 0);
        }

        public static void VolumeStep(bool up)
        {
            try
            {
                var epv = GetEndpointVolume();
                if (epv != null)
                {
                    epv.GetMasterVolumeLevelScalar(out float level);
                    float newLevel = up ? Math.Min(1f, level + 0.03f) : Math.Max(0f, level - 0.03f);
                    var guid = Guid.Empty;
                    epv.SetMasterVolumeLevelScalar(newLevel, ref guid);
                    return;
                }
            }
            catch { }

            byte vk = up ? NativeMethods.VK_VOLUME_UP : NativeMethods.VK_VOLUME_DOWN;
            NativeMethods.keybd_event(vk, 0, 0, 0);
            NativeMethods.keybd_event(vk, 0, NativeMethods.KEYEVENTF_KEYUP, 0);
        }

        public static void OpenSoundSettings()
        {
            try
            {
                Process.Start(new ProcessStartInfo { FileName = "ms-settings:sound", UseShellExecute = true });
            }
            catch
            {
                try { Process.Start(new ProcessStartInfo { FileName = "sndvol.exe", UseShellExecute = true }); } catch { }
            }
        }
    }
}
