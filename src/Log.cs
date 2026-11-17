using System;
using System.IO;
using System.Text;

namespace LanPerfMon
{
    internal static class Log
    {
        private static readonly object Sync = new object();
        private static readonly string FilePath = BuildPath();

        /// <summary>Resolved log file path (null if initialization failed).</summary>
        internal static string LogPath
        {
            get { return FilePath; }
        }

        private static string BuildPath()
        {
            try
            {
                string dir = Path.Combine(Path.GetTempPath(), "LanPerfMon");
                Directory.CreateDirectory(dir);
                return Path.Combine(dir, "LanPerfMon.log");
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Probe: attempt a write to the resolved log file and report the error.</summary>
        internal static string TestWrite()
        {
            if (FilePath == null)
                return "logPath=null (BuildPath failed; temp=" + SafeTemp() + ")";
            try
            {
                File.AppendAllText(FilePath, string.Empty, Encoding.UTF8);
                return "OK " + FilePath;
            }
            catch (Exception ex)
            {
                return "WRITE FAILED: " + ex.GetType().Name + ": " + ex.Message + " path=" + FilePath;
            }
        }

        private static string SafeTemp()
        {
            try { return Path.GetTempPath(); }
            catch (Exception ex) { return "GetTempPath THREW " + ex.Message; }
        }

        public static void Write(string message)
        {
            if (FilePath == null)
                return;
            try
            {
                lock (Sync)
                {
                    string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + "  " + message + Environment.NewLine;
                    File.AppendAllText(FilePath, line, Encoding.UTF8);
                }
            }
            catch { }
        }
    }
}
