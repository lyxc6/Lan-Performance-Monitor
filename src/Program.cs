using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Windows.Forms;

namespace LanPerfMon
{
    internal static class Program
    {
        private static Mutex _mutex;

        [STAThread]
        private static void Main(string[] args)
        {
            bool created;
            _mutex = new Mutex(true, "Local\\LanPerfMon.SingleInstance", out created);
            if (!created)
            {
                MessageBox.Show("LAN 性能监控已经在运行。\n请查看桌面右下角的托盘图标。",
                    "LAN 性能监控", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            // Install the embedded-assembly resolver before any type that touches
            // LibreHardwareMonitor is loaded.  Keep this method free of such types.
            AppDomain.CurrentDomain.AssemblyResolve += ResourceLoader.ResolveAssembly;

            try
            {
                Run(args);
            }
            catch (Exception ex)
            {
                MessageBox.Show("启动失败：\n" + ex.Message, "LAN 性能监控",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                GC.KeepAlive(_mutex);
                if (_mutex != null)
                {
                    try { _mutex.ReleaseMutex(); }
                    catch { }
                    _mutex.Close();
                }
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void Run(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            AppOptions options = AppOptions.Parse(args);
            Log.Write("Run: creating MainForm");
            MainForm form = new MainForm(options);
            Log.Write("Run: MainForm created, starting message loop");
            Application.Run(form);
            Log.Write("Run: message loop exited");
        }
    }
}
