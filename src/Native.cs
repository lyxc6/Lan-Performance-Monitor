using System;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace LanPerfMon
{
    internal static class Native
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct FILETIME
        {
            public uint dwLowDateTime;
            public uint dwHighDateTime;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool GetSystemTimes(out FILETIME idleTime, out FILETIME kernelTime, out FILETIME userTime);

        [StructLayout(LayoutKind.Sequential)]
        public struct MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

        [StructLayout(LayoutKind.Sequential)]
        public struct PROCESSOR_POWER_INFORMATION
        {
            public uint Number;
            public uint MaxMhz;
            public uint CurrentMhz;
            public uint MhzLimit;
            public uint MaxIdleState;
            public uint CurrentIdleState;
        }

        [DllImport("powrprof.dll")]
        public static extern uint CallNtPowerInformation(
            int InformationLevel,
            IntPtr lpInputBuffer,
            uint nInputBufferSize,
            IntPtr lpOutputBuffer,
            uint nOutputBufferSize);

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [DllImport("user32.dll")]
        public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

        [DllImport("user32.dll")]
        public static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern int GetSystemMetrics(int index);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool SetHandleInformation(IntPtr handle, uint mask, uint flags);

        public static bool IsElevated
        {
            get
            {
                try
                {
                    using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
                    {
                        WindowsPrincipal principal = new WindowsPrincipal(identity);
                        return principal.IsInRole(WindowsBuiltInRole.Administrator);
                    }
                }
                catch
                {
                    return false;
                }
            }
        }

        public static ulong ToUInt64(FILETIME value)
        {
            return ((ulong)value.dwHighDateTime << 32) | value.dwLowDateTime;
        }

        public static int GetSessionId()
        {
            try
            {
                return System.Diagnostics.Process.GetCurrentProcess().SessionId;
            }
            catch
            {
                return -1;
            }
        }

        public static bool IsPrivateOrLoopback(IPAddress address)
        {
            if (address == null)
                return false;

            if (address.AddressFamily == AddressFamily.InterNetworkV6)
            {
                if (IPAddress.IPv6Loopback.Equals(address))
                    return true;
                if (address.IsIPv4MappedToIPv6)
                    address = address.MapToIPv4();
                else
                    return false;
            }

            byte[] b = address.GetAddressBytes();
            if (b.Length != 4)
                return false;

            if (b[0] == 10)
                return true;
            if (b[0] == 127)
                return true;
            if (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
                return true;
            if (b[0] == 192 && b[1] == 168)
                return true;
            if (b[0] == 169 && b[1] == 254)
                return true;
            return false;
        }
    }

    internal sealed class CpuUsageMeter
    {
        private ulong _previousIdle;
        private ulong _previousKernel;
        private ulong _previousUser;
        private bool _hasPrevious;

        public double Next()
        {
            Native.FILETIME idle;
            Native.FILETIME kernel;
            Native.FILETIME user;
            if (!Native.GetSystemTimes(out idle, out kernel, out user))
                return 0;

            ulong idleValue = Native.ToUInt64(idle);
            ulong kernelValue = Native.ToUInt64(kernel);
            ulong userValue = Native.ToUInt64(user);

            if (!_hasPrevious)
            {
                _previousIdle = idleValue;
                _previousKernel = kernelValue;
                _previousUser = userValue;
                _hasPrevious = true;
                return 0;
            }

            ulong idleDelta = idleValue - _previousIdle;
            ulong kernelDelta = kernelValue - _previousKernel;
            ulong userDelta = userValue - _previousUser;
            _previousIdle = idleValue;
            _previousKernel = kernelValue;
            _previousUser = userValue;

            ulong total = kernelDelta + userDelta;
            if (total == 0)
                return 0;

            double busy = (double)(total - idleDelta);
            double percent = busy * 100.0 / (double)total;
            if (percent < 0) percent = 0;
            if (percent > 100) percent = 100;
            return percent;
        }
    }

    internal static class CpuFrequency
    {
        public static void Read(out double currentMhz, out double maxMhz)
        {
            currentMhz = 0;
            maxMhz = 0;

            int count = Environment.ProcessorCount;
            int size = Marshal.SizeOf(typeof(Native.PROCESSOR_POWER_INFORMATION));
            if (count <= 0 || size <= 0)
                return;

            IntPtr buffer = IntPtr.Zero;
            try
            {
                buffer = Marshal.AllocHGlobal(size * count);
                uint status = Native.CallNtPowerInformation(11, IntPtr.Zero, 0, buffer, (uint)(size * count));
                if (status != 0)
                    return;

                double sum = 0;
                int valid = 0;
                uint maximum = 0;
                for (int i = 0; i < count; i++)
                {
                    IntPtr item = new IntPtr(buffer.ToInt64() + (long)i * size);
                    Native.PROCESSOR_POWER_INFORMATION info =
                        (Native.PROCESSOR_POWER_INFORMATION)Marshal.PtrToStructure(item, typeof(Native.PROCESSOR_POWER_INFORMATION));
                    if (info.MaxMhz > maximum)
                        maximum = info.MaxMhz;
                    if (info.CurrentMhz > 0)
                    {
                        sum += info.CurrentMhz;
                        valid++;
                    }
                }

                if (valid > 0)
                    currentMhz = sum / valid;
                maxMhz = maximum;
            }
            catch { }
            finally
            {
                if (buffer != IntPtr.Zero)
                    Marshal.FreeHGlobal(buffer);
            }
        }
    }

    internal sealed class RamFallback
    {
        public double UsedGb;
        public double TotalGb;
        public double Percent;

        public bool Read()
        {
            try
            {
                Native.MEMORYSTATUSEX status = new Native.MEMORYSTATUSEX();
                status.dwLength = (uint)Marshal.SizeOf(typeof(Native.MEMORYSTATUSEX));
                if (!Native.GlobalMemoryStatusEx(ref status))
                    return false;
                if (status.ullTotalPhys == 0)
                    return false;

                TotalGb = status.ullTotalPhys / 1073741824.0;
                UsedGb = (status.ullTotalPhys - status.ullAvailPhys) / 1073741824.0;
                Percent = UsedGb * 100.0 / TotalGb;
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
