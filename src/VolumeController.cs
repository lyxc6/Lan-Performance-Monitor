using System;
using System.Runtime.InteropServices;

namespace LanPerfMon
{
    internal sealed class VolumeController
    {
        private static readonly Guid CLSID_MMDeviceEnumerator = new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E");
        private static Guid IID_IAudioEndpointVolume = new Guid("5CDF2C82-841E-4546-9722-0CF74078229A");
        private readonly object _sync = new object();

        public VolumeState Get()
        {
            lock (_sync)
            {
                VolumeState state = new VolumeState();
                IMMDeviceEnumerator enumerator = null;
                IMMDevice device = null;
                IntPtr volumePointer = IntPtr.Zero;
                object volumeObject = null;
                try
                {
                    enumerator = CreateEnumerator();
                    int hr = enumerator.GetDefaultAudioEndpoint(0, 1, out device);
                    if (hr < 0)
                        throw Marshal.GetExceptionForHR(hr);

                    hr = device.Activate(ref IID_IAudioEndpointVolume, 23, IntPtr.Zero, out volumePointer);
                    if (hr < 0)
                        throw Marshal.GetExceptionForHR(hr);

                    volumeObject = Marshal.GetObjectForIUnknown(volumePointer);
                    IAudioEndpointVolume volume = (IAudioEndpointVolume)volumeObject;

                    float scalar;
                    hr = volume.GetMasterVolumeLevelScalar(out scalar);
                    if (hr < 0)
                        throw Marshal.GetExceptionForHR(hr);

                    bool mute;
                    hr = volume.GetMute(out mute);
                    if (hr < 0)
                        throw Marshal.GetExceptionForHR(hr);

                    int percent = (int)Math.Round(scalar * 100.0);
                    if (percent < 0) percent = 0;
                    if (percent > 100) percent = 100;

                    state.Available = true;
                    state.Percent = percent;
                    state.Mute = mute;
                }
                catch (Exception ex)
                {
                    state.Available = false;
                    state.Error = ex.Message;
                }
                finally
                {
                    if (volumePointer != IntPtr.Zero)
                        Marshal.Release(volumePointer);
                    if (volumeObject != null && Marshal.IsComObject(volumeObject))
                        Marshal.ReleaseComObject(volumeObject);
                    if (device != null && Marshal.IsComObject(device))
                        Marshal.ReleaseComObject(device);
                    if (enumerator != null && Marshal.IsComObject(enumerator))
                        Marshal.ReleaseComObject(enumerator);
                }
                return state;
            }
        }

        public VolumeState SetPercent(int percent)
        {
            lock (_sync)
            {
                VolumeState state = new VolumeState();
                IMMDeviceEnumerator enumerator = null;
                IMMDevice device = null;
                IntPtr volumePointer = IntPtr.Zero;
                object volumeObject = null;
                try
                {
                    if (percent < 0) percent = 0;
                    if (percent > 100) percent = 100;

                    enumerator = CreateEnumerator();
                    int hr = enumerator.GetDefaultAudioEndpoint(0, 1, out device);
                    if (hr < 0)
                        throw Marshal.GetExceptionForHR(hr);

                    hr = device.Activate(ref IID_IAudioEndpointVolume, 23, IntPtr.Zero, out volumePointer);
                    if (hr < 0)
                        throw Marshal.GetExceptionForHR(hr);

                    volumeObject = Marshal.GetObjectForIUnknown(volumePointer);
                    IAudioEndpointVolume volume = (IAudioEndpointVolume)volumeObject;

                    hr = volume.SetMasterVolumeLevelScalar((float)(percent / 100.0), Guid.Empty);
                    if (hr < 0)
                        throw Marshal.GetExceptionForHR(hr);

                    hr = volume.SetMute(false, Guid.Empty);
                    if (hr < 0)
                        throw Marshal.GetExceptionForHR(hr);

                    state.Available = true;
                    state.Percent = percent;
                    state.Mute = false;
                }
                catch (Exception ex)
                {
                    state.Available = false;
                    state.Error = ex.Message;
                }
                finally
                {
                    if (volumePointer != IntPtr.Zero)
                        Marshal.Release(volumePointer);
                    if (volumeObject != null && Marshal.IsComObject(volumeObject))
                        Marshal.ReleaseComObject(volumeObject);
                    if (device != null && Marshal.IsComObject(device))
                        Marshal.ReleaseComObject(device);
                    if (enumerator != null && Marshal.IsComObject(enumerator))
                        Marshal.ReleaseComObject(enumerator);
                }
                return state;
            }
        }

        public VolumeState ToggleMute()
        {
            lock (_sync)
            {
                VolumeState state = new VolumeState();
                IMMDeviceEnumerator enumerator = null;
                IMMDevice device = null;
                IntPtr volumePointer = IntPtr.Zero;
                object volumeObject = null;
                try
                {
                    enumerator = CreateEnumerator();
                    int hr = enumerator.GetDefaultAudioEndpoint(0, 1, out device);
                    if (hr < 0)
                        throw Marshal.GetExceptionForHR(hr);

                    hr = device.Activate(ref IID_IAudioEndpointVolume, 23, IntPtr.Zero, out volumePointer);
                    if (hr < 0)
                        throw Marshal.GetExceptionForHR(hr);

                    volumeObject = Marshal.GetObjectForIUnknown(volumePointer);
                    IAudioEndpointVolume volume = (IAudioEndpointVolume)volumeObject;

                    bool mute;
                    hr = volume.GetMute(out mute);
                    if (hr < 0)
                        throw Marshal.GetExceptionForHR(hr);

                    hr = volume.SetMute(!mute, Guid.Empty);
                    if (hr < 0)
                        throw Marshal.GetExceptionForHR(hr);

                    state = GetUnlocked();
                }
                catch (Exception ex)
                {
                    state.Available = false;
                    state.Error = ex.Message;
                }
                finally
                {
                    if (volumePointer != IntPtr.Zero)
                        Marshal.Release(volumePointer);
                    if (volumeObject != null && Marshal.IsComObject(volumeObject))
                        Marshal.ReleaseComObject(volumeObject);
                    if (device != null && Marshal.IsComObject(device))
                        Marshal.ReleaseComObject(device);
                    if (enumerator != null && Marshal.IsComObject(enumerator))
                        Marshal.ReleaseComObject(enumerator);
                }
                return state;
            }
        }

        private VolumeState GetUnlocked()
        {
            VolumeState state = new VolumeState();
            IMMDeviceEnumerator enumerator = null;
            IMMDevice device = null;
            IntPtr volumePointer = IntPtr.Zero;
            object volumeObject = null;
            try
            {
                enumerator = CreateEnumerator();
                int hr = enumerator.GetDefaultAudioEndpoint(0, 1, out device);
                if (hr < 0)
                    throw Marshal.GetExceptionForHR(hr);
                hr = device.Activate(ref IID_IAudioEndpointVolume, 23, IntPtr.Zero, out volumePointer);
                if (hr < 0)
                    throw Marshal.GetExceptionForHR(hr);

                volumeObject = Marshal.GetObjectForIUnknown(volumePointer);
                IAudioEndpointVolume volume = (IAudioEndpointVolume)volumeObject;
                float scalar;
                bool mute;
                hr = volume.GetMasterVolumeLevelScalar(out scalar);
                if (hr < 0)
                    throw Marshal.GetExceptionForHR(hr);
                hr = volume.GetMute(out mute);
                if (hr < 0)
                    throw Marshal.GetExceptionForHR(hr);

                int percent = (int)Math.Round(scalar * 100.0);
                if (percent < 0) percent = 0;
                if (percent > 100) percent = 100;
                state.Available = true;
                state.Percent = percent;
                state.Mute = mute;
            }
            catch (Exception ex)
            {
                state.Available = false;
                state.Error = ex.Message;
            }
            finally
            {
                if (volumePointer != IntPtr.Zero)
                    Marshal.Release(volumePointer);
                if (volumeObject != null && Marshal.IsComObject(volumeObject))
                    Marshal.ReleaseComObject(volumeObject);
                if (device != null && Marshal.IsComObject(device))
                    Marshal.ReleaseComObject(device);
                if (enumerator != null && Marshal.IsComObject(enumerator))
                    Marshal.ReleaseComObject(enumerator);
            }
            return state;
        }

        private static IMMDeviceEnumerator CreateEnumerator()
        {
            Type type = Type.GetTypeFromCLSID(CLSID_MMDeviceEnumerator);
            object instance = Activator.CreateInstance(type);
            return (IMMDeviceEnumerator)instance;
        }

        [ComImport]
        [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDeviceEnumerator
        {
            [PreserveSig]
            int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);

            [PreserveSig]
            int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice endpoint);

            [PreserveSig]
            int GetDevice(string id, out IMMDevice device);

            [PreserveSig]
            int RegisterEndpointNotificationCallback(IntPtr client);

            [PreserveSig]
            int UnregisterEndpointNotificationCallback(IntPtr client);
        }

        [ComImport]
        [Guid("D666063F-1587-4E43-81F1-B948E807363F")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDevice
        {
            [PreserveSig]
            int Activate(ref Guid iid, int dwClsCtx, IntPtr pActivationParams, out IntPtr ppInterface);

            [PreserveSig]
            int OpenPropertyStore(int access, out IntPtr properties);

            [PreserveSig]
            int GetId(out IntPtr id);

            [PreserveSig]
            int GetState(out int state);
        }

        [ComImport]
        [Guid("5CDF2C82-841E-4546-9722-0CF74078229A")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioEndpointVolume
        {
            [PreserveSig]
            int RegisterControlChangeNotify(IntPtr notify);

            [PreserveSig]
            int UnregisterControlChangeNotify(IntPtr notify);

            [PreserveSig]
            int GetChannelCount(out uint count);

            [PreserveSig]
            int SetMasterVolumeLevel(float level, Guid eventContext);

            [PreserveSig]
            int SetMasterVolumeLevelScalar(float level, Guid eventContext);

            [PreserveSig]
            int GetMasterVolumeLevel(out float level);

            [PreserveSig]
            int GetMasterVolumeLevelScalar(out float level);

            [PreserveSig]
            int SetChannelVolumeLevel(uint channel, float level, Guid eventContext);

            [PreserveSig]
            int SetChannelVolumeLevelScalar(uint channel, float level, Guid eventContext);

            [PreserveSig]
            int GetChannelVolumeLevel(uint channel, out float level);

            [PreserveSig]
            int GetChannelVolumeLevelScalar(uint channel, out float level);

            [PreserveSig]
            int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, Guid eventContext);

            [PreserveSig]
            int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);

            [PreserveSig]
            int GetVolumeStepInfo(out uint step, out uint stepCount);

            [PreserveSig]
            int VolumeStepUp(Guid eventContext);

            [PreserveSig]
            int VolumeStepDown(Guid eventContext);

            [PreserveSig]
            int QueryHardwareSupport(out uint mask);

            [PreserveSig]
            int GetVolumeRange(out float minimum, out float maximum, out float increment);
        }
    }
}

