using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace LanPerfMon
{
    /// <summary>
    /// Diagnostics used by /api/debug: token integrity, raw file write,
    /// raw Shell_NotifyIcon NIM_ADD with Win32 error codes.
    /// </summary>
    internal static class Diag
    {
        private const int TokenIntegrityLevel = 25;
        private const uint TokenQuery = 0x0008;

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetCurrentProcess();

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool OpenProcessToken(IntPtr processHandle, uint desiredAccess, out IntPtr tokenHandle);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool GetTokenInformation(IntPtr tokenHandle, int tokenInformationClass,
            IntPtr tokenInformation, int tokenInformationLength, out int returnLength);

        [DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr handle);

        public static string Integrity()
        {
            IntPtr token;
            if (!OpenProcessToken(GetCurrentProcess(), TokenQuery, out token))
                return "open-token-failed win32=" + Marshal.GetLastWin32Error();
            try
            {
                int len;
                GetTokenInformation(token, TokenIntegrityLevel, IntPtr.Zero, 0, out len);
                if (len <= 0)
                    return "len-failed win32=" + Marshal.GetLastWin32Error();
                IntPtr buf = Marshal.AllocHGlobal(len);
                try
                {
                    if (!GetTokenInformation(token, TokenIntegrityLevel, buf, len, out len))
                        return "get-info-failed win32=" + Marshal.GetLastWin32Error();
                    // TOKEN_MANDATORY_LABEL { SID_AND_ATTRIBUTES Label; } -> first field is the SID pointer
                    IntPtr sid = Marshal.ReadIntPtr(buf);
                    string sidValue;
                    try
                    {
                        sidValue = new System.Security.Principal.SecurityIdentifier(sid).Value;
                    }
                    catch (Exception ex)
                    {
                        return "sid-parse-failed " + ex.Message;
                    }
                    string level = "?";
                    switch (sidValue)
                    {
                        case "S-1-16-0": level = "UNTRUSTED"; break;
                        case "S-1-16-4096": level = "LOW"; break;
                        case "S-1-16-8192": level = "MEDIUM"; break;
                        case "S-1-16-12288": level = "HIGH"; break;
                        case "S-1-16-16384": level = "SYSTEM"; break;
                    }
                    return sidValue + " (" + level + ")";
                }
                finally
                {
                    Marshal.FreeHGlobal(buf);
                }
            }
            finally
            {
                CloseHandle(token);
            }
        }

        /// <summary>Create a brand-new file in the log directory (discriminates file-specific vs process-wide denial).</summary>
        public static string NewFileWrite()
        {
            try
            {
                string dir = Path.Combine(Path.GetTempPath(), "LanPerfMon");
                Directory.CreateDirectory(dir);
                string probe = Path.Combine(dir, "appnew-" + Guid.NewGuid().ToString("N").Substring(0, 6) + ".txt");
                File.WriteAllText(probe, "x");
                File.Delete(probe);
                return "OK";
            }
            catch (Exception ex)
            {
                return "FAIL " + ex.GetType().Name + " hr=0x" + ex.HResult.ToString("X8") + " :: " + ex.Message;
            }
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct NOTIFYICONDATA
        {
            public int cbSize;
            public IntPtr hWnd;
            public uint uID;
            public uint uFlags;
            public uint uCallbackMessage;
            public IntPtr hIcon;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
            public uint dwState;
            public uint dwStateMask;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
            public uint uTimeoutOrVersion;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
            public uint dwInfoFlags;
            public Guid guidItem;
            public IntPtr hBalloonIcon;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern bool Shell_NotifyIcon(uint dwMessage, ref NOTIFYICONDATA lpData);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr CreateWindowExW(uint dwExStyle, string lpClassName, string lpWindowName,
            uint dwStyle, int x, int y, int nWidth, int nHeight, IntPtr hWndParent, IntPtr hMenu,
            IntPtr hInstance, IntPtr lpParam);

        [DllImport("user32.dll")]
        private static extern bool DestroyWindow(IntPtr hWnd);

        /// <summary>
        /// Raw Shell_NotifyIcon(NIM_ADD) using a message-only window; returns Win32 result.
        /// </summary>
        public static string RawShellAdd()
        {
            IntPtr hwnd = CreateWindowExW(0, "Static", "LanPerfMonShellProbe",
                0, 0, 0, 0, 0, new IntPtr(-3), IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            int werr = Marshal.GetLastWin32Error();
            if (hwnd == IntPtr.Zero)
                return "create-window=FAIL win32=" + werr;
            try
            {
                NOTIFYICONDATA nid = new NOTIFYICONDATA();
                nid.cbSize = Marshal.SizeOf(typeof(NOTIFYICONDATA));
                nid.hWnd = hwnd;
                nid.uID = 4242;
                nid.uFlags = 2 | 4; // NIF_ICON | NIF_TIP
                nid.hIcon = SystemIcons.Application.Handle;
                nid.szTip = "RawShellProbe";
                bool ok = Shell_NotifyIcon(0 /*NIM_ADD*/, ref nid);
                int err = Marshal.GetLastWin32Error();
                bool ok2 = Shell_NotifyIcon(2 /*NIM_DELETE*/, ref nid);
                int err2 = Marshal.GetLastWin32Error();
                return "window=OK add=" + ok + " err=" + err + " delete=" + ok2 + " err2=" + err2;
            }
            catch (Exception ex)
            {
                return "EXC " + ex.GetType().Name + " :: " + ex.Message;
            }
            finally
            {
                DestroyWindow(hwnd);
            }
        }
    }
}
