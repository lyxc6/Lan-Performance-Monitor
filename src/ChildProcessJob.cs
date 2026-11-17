using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace LanPerfMon
{
    /// <summary>
    /// Keeps the PresentMon child in a Windows job object configured with
    /// JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE.  If this process is killed (even by
    /// Task Manager), the job handle is closed by the OS and PresentMon is
    /// terminated with it.  This prevents an orphaned child from holding the
    /// HTTP port or an ETW session.
    /// </summary>
    internal static class ChildProcessJob
    {
        private const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x00002000;
        private const int JobObjectExtendedLimitInformation = 9;

        private static IntPtr _job;

        [StructLayout(LayoutKind.Sequential)]
        private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
        {
            public long PerProcessUserTimeLimit;
            public long PerJobUserTimeLimit;
            public uint LimitFlags;
            public UIntPtr MinimumWorkingSetSize;
            public UIntPtr MaximumWorkingSetSize;
            public uint ActiveProcessLimit;
            public UIntPtr Affinity;
            public uint PriorityClass;
            public uint SchedulingClass;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct IO_COUNTERS
        {
            public ulong ReadOperationCount;
            public ulong WriteOperationCount;
            public ulong OtherOperationCount;
            public ulong ReadTransferCount;
            public ulong WriteTransferCount;
            public ulong OtherTransferCount;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
        {
            public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
            public IO_COUNTERS IoInfo;
            public UIntPtr ProcessMemoryLimit;
            public UIntPtr JobMemoryLimit;
            public UIntPtr PeakProcessMemoryUsed;
            public UIntPtr PeakJobMemoryUsed;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateJobObject(IntPtr securityAttributes, string name);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetInformationJobObject(
            IntPtr job,
            int infoClass,
            ref JOBOBJECT_EXTENDED_LIMIT_INFORMATION info,
            uint infoLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr handle);

        public static bool Assign(Process process)
        {
            if (process == null)
                return false;

            try
            {
                if (_job == IntPtr.Zero)
                {
                    IntPtr job = CreateJobObject(IntPtr.Zero, null);
                    if (job == IntPtr.Zero)
                    {
                        Log.Write("ChildProcessJob: CreateJobObject failed " + Marshal.GetLastWin32Error());
                        return false;
                    }

                    JOBOBJECT_EXTENDED_LIMIT_INFORMATION info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
                    info.BasicLimitInformation.LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;
                    if (!SetInformationJobObject(job, JobObjectExtendedLimitInformation, ref info,
                        (uint)Marshal.SizeOf(typeof(JOBOBJECT_EXTENDED_LIMIT_INFORMATION))))
                    {
                        Log.Write("ChildProcessJob: SetInformationJobObject failed " + Marshal.GetLastWin32Error());
                        CloseHandle(job);
                        return false;
                    }

                    _job = job;
                }

                if (!AssignProcessToJobObject(_job, process.Handle))
                {
                    Log.Write("ChildProcessJob: AssignProcessToJobObject failed " + Marshal.GetLastWin32Error());
                    return false;
                }
                Log.Write("ChildProcessJob: assigned PresentMon pid=" + process.Id);
                return true;
            }
            catch (Exception ex)
            {
                Log.Write("ChildProcessJob: " + ex.Message);
                return false;
            }
        }
    }
}
