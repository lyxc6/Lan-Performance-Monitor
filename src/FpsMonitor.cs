using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Threading;

namespace LanPerfMon
{
    internal sealed class FpsMonitor : IDisposable
    {
        private sealed class ProcessStat
        {
            public string App = string.Empty;
            public int ProcessId;
            public List<double> Samples = new List<double>();
            public List<DateTime> Times = new List<DateTime>();
            public List<double> LowSamples = new List<double>();
            public List<DateTime> LowTimes = new List<DateTime>();
            public double Fps;
            public DateTime LastSeen;
            public long Frames;
        }

        private readonly object _sync = new object();
        private readonly Dictionary<int, ProcessStat> _processes = new Dictionary<int, ProcessStat>();
        private Thread _thread;
        private volatile bool _running;
        private Process _process;
        private bool _presentMonRunning;
        private string _status = "FPS 初始化";
        private string _lastError = string.Empty;
        private int _headerApplication = -1;
        private int _headerProcessId = -1;
        private int _headerMsBetween = -1;
        private bool _headerSeen;
        private bool _parseErrorReported;

        public void Start()
        {
            if (_running)
                return;
            _running = true;
            _thread = new Thread(Loop);
            _thread.IsBackground = true;
            _thread.Name = "LanPerfMon.FPS";
            _thread.Start();
            Log.Write("FpsMonitor.Start: thread started");
        }

        private void Loop()
        {
            StopStaleSessions();
            int failures = 0;
            while (_running)
            {
                try
                {
                    RunPresentMon();
                    if (!_running)
                        break;
                    failures++;
                    Thread.Sleep(Math.Min(30000, 5000 + failures * 5000));
                }
                catch (Exception ex)
                {
                    _lastError = Shorten(ex.Message, 180);
                    lock (_sync)
                    {
                        _status = "PresentMon 启动失败";
                        _presentMonRunning = false;
                    }
                    failures++;
                    if (!_running)
                        break;
                    Thread.Sleep(Math.Min(30000, 5000 + failures * 5000));
                }
            }
        }

        private static void StopStaleSessions()
        {
            try
            {
                string output;
                if (NetInfo.RunHidden("logman.exe", "query -ets", out output) != 0)
                    return;

                string[] lines = output.Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i].Trim();
                    int index = line.IndexOf("LanPerfMon", StringComparison.OrdinalIgnoreCase);
                    if (index < 0)
                        continue;

                    string name = line.Substring(index);
                    int space = name.IndexOfAny(new char[] { ' ', '\t' });
                    if (space > 0)
                        name = name.Substring(0, space);
                    if (name.Length < 10 || !name.StartsWith("LanPerfMon", StringComparison.OrdinalIgnoreCase))
                        continue;

                    string ignored;
                    NetInfo.RunHidden("logman.exe", "stop \"" + name + "\" -ets", out ignored);
                    Log.Write("FpsMonitor: stopped stale ETW session " + name);
                }
            }
            catch (Exception ex)
            {
                Log.Write("FpsMonitor: stale session cleanup failed: " + ex.Message);
            }
        }

        private void RunPresentMon()
        {
            string exePath = ResourceLoader.ExtractTool("tool.PresentMon.exe", "PresentMon-2.6.0.exe");
            Log.Write("FpsMonitor: PresentMon extracted to " + exePath);

            _lastError = string.Empty;
            lock (_sync)
            {
                _status = "PresentMon 启动中";
            }

            int processId = Process.GetCurrentProcess().Id;
            ProcessStartInfo info = new ProcessStartInfo();
            info.FileName = exePath;
            info.Arguments = "--output_stdout --no_console_stats --no_csv --no_track_gpu --no_track_input " +
                             "--session_name LanPerfMon-" + processId.ToString(CultureInfo.InvariantCulture);
            info.UseShellExecute = false;
            info.CreateNoWindow = true;
            info.RedirectStandardOutput = true;
            info.RedirectStandardError = true;
            info.StandardOutputEncoding = Encoding.UTF8;
            info.StandardErrorEncoding = Encoding.UTF8;

            _headerSeen = false;
            _headerApplication = -1;
            _headerProcessId = -1;
            _headerMsBetween = -1;
            _parseErrorReported = false;

            Process process = new Process();
            process.StartInfo = info;
            process.OutputDataReceived += ProcessOutputDataReceived;
            process.ErrorDataReceived += ProcessErrorDataReceived;

            try
            {
                process.Start();
                Log.Write("FpsMonitor: PresentMon started pid=" + process.Id);
                ChildProcessJob.Assign(process);
                lock (_sync)
                {
                    _process = process;
                    _presentMonRunning = true;
                    _status = "PresentMon 运行中";
                }

                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                process.WaitForExit();

                int exitCode = 0;
                try { exitCode = process.ExitCode; }
                catch { }
                Log.Write("FpsMonitor: PresentMon exited code=" + exitCode + " error=" + _lastError);

                lock (_sync)
                {
                    _presentMonRunning = false;
                    if (_running)
                    {
                        _status = exitCode == 0 ? "PresentMon 已退出" : "PresentMon 退出码 " + exitCode;
                        if (!string.IsNullOrEmpty(_lastError))
                            _status = FriendlyStatus(_lastError, _lastError);
                        if (!Native.IsElevated && exitCode != 0)
                            _status = "需要管理员权限才能采集 FPS";
                    }
                    _process = null;
                }
            }
            finally
            {
                lock (_sync)
                {
                    _presentMonRunning = false;
                    _process = null;
                }
                try { process.Dispose(); }
                catch { }
            }
        }

        private void ProcessOutputDataReceived(object sender, DataReceivedEventArgs e)
        {
            if (e == null || e.Data == null)
                return;

            string line = e.Data.Trim();
            if (line.Length == 0)
                return;

            try
            {
                ParseCsvLine(line);
            }
            catch (Exception ex)
            {
                if (!_parseErrorReported)
                {
                    _parseErrorReported = true;
                    _lastError = "解析 FPS 数据失败: " + Shorten(ex.Message, 120);
                }
            }
        }

        private void ProcessErrorDataReceived(object sender, DataReceivedEventArgs e)
        {
            if (e == null || e.Data == null)
                return;
            string text = e.Data.Trim();
            if (text.Length == 0)
                return;

            string lower = text.ToLowerInvariant();
            if (lower.StartsWith("warning:") || lower.StartsWith("ignoring:") || lower.StartsWith("info:"))
            {
                if (lower.IndexOf("elevated", StringComparison.Ordinal) >= 0 ||
                    lower.IndexOf("access denied", StringComparison.Ordinal) >= 0)
                {
                    _lastError = "FPS 捕获需要管理员权限";
                    lock (_sync) { _status = _lastError; }
                }
                return;
            }
            if (lower.IndexOf("access denied", StringComparison.Ordinal) >= 0)
            {
                _lastError = "FPS 捕获需要管理员权限";
                lock (_sync) { _status = _lastError; }
                return;
            }

            _lastError = Shorten(text, 200);
            lock (_sync)
            {
                _status = FriendlyStatus(text, _lastError);
            }
        }

        /// <summary>
        /// PresentMon reports ETW permission problems in English (for example
        /// "Startup failed: ... \"Performance Log Users\" user group ...", exit code 6).
        /// That raw text used to reach the web UI verbatim, which is unreadable for
        /// a Chinese-only user, so map the known permission cases to Chinese while
        /// keeping the original line in the log.
        /// </summary>
        private static string FriendlyStatus(string raw, string fallback)
        {
            if (string.IsNullOrEmpty(raw))
                return fallback;

            string lower = raw.ToLowerInvariant();
            if (lower.IndexOf("performance log users", StringComparison.Ordinal) >= 0 ||
                lower.IndexOf("access denied", StringComparison.Ordinal) >= 0 ||
                lower.IndexOf("elevated", StringComparison.Ordinal) >= 0)
            {
                Log.Write("FpsMonitor: permission denied, raw output: " + Shorten(raw, 200));
                return "需要管理员权限才能采集 FPS";
            }

            return fallback;
        }

        private void ParseCsvLine(string line)
        {
            List<string> fields = SplitCsv(line);

            if (!_headerSeen)
            {
                if (fields.Count > 2 && fields[0].Trim().TrimStart('\uFEFF').Equals("Application", StringComparison.OrdinalIgnoreCase))
                {
                    for (int i = 0; i < fields.Count; i++)
                    {
                        string name = fields[i].Trim();
                        if (name.Equals("Application", StringComparison.OrdinalIgnoreCase))
                            _headerApplication = i;
                        else if (name.Equals("ProcessID", StringComparison.OrdinalIgnoreCase) ||
                                 name.Equals("ProcessId", StringComparison.OrdinalIgnoreCase))
                            _headerProcessId = i;
                        else if (name.Equals("MsBetweenPresents", StringComparison.OrdinalIgnoreCase))
                            _headerMsBetween = i;
                    }

                    _headerSeen = _headerApplication >= 0 && _headerProcessId >= 0 && _headerMsBetween >= 0;
                    if (!_headerSeen)
                    {
                        _lastError = "PresentMon CSV 缺少必要列";
                        lock (_sync) { _status = _lastError; }
                    }
                    return;
                }
                return;
            }

            int max = _headerApplication;
            if (_headerProcessId > max) max = _headerProcessId;
            if (_headerMsBetween > max) max = _headerMsBetween;
            if (fields.Count <= max)
                return;

            string app = fields[_headerApplication].Trim();
            int pid;
            if (!int.TryParse(fields[_headerProcessId].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out pid) || pid <= 0)
                return;
            if (app.Length == 0 || app.Equals("<unknown>", StringComparison.OrdinalIgnoreCase))
                return;

            double ms;
            if (!double.TryParse(fields[_headerMsBetween].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out ms))
                return;
            if (ms <= 0.001 || ms > 60000)
                return;

            DateTime now = DateTime.UtcNow;
            lock (_sync)
            {
                ProcessStat stat;
                if (!_processes.TryGetValue(pid, out stat))
                {
                    stat = new ProcessStat();
                    stat.ProcessId = pid;
                    _processes[pid] = stat;
                }

                stat.App = app;
                stat.LastSeen = now;
                stat.Frames++;
                stat.Samples.Add(ms);
                stat.Times.Add(now);
                stat.LowSamples.Add(ms);
                stat.LowTimes.Add(now);

                int remove = 0;
                while (remove < stat.Times.Count && (now - stat.Times[remove]).TotalSeconds > 2.0)
                    remove++;
                if (stat.Times.Count > 300)
                    remove = Math.Max(remove, stat.Times.Count - 300);
                if (remove > 0)
                {
                    stat.Times.RemoveRange(0, remove);
                    stat.Samples.RemoveRange(0, remove);
                }

                int lowRemove = 0;
                while (lowRemove < stat.LowTimes.Count && (now - stat.LowTimes[lowRemove]).TotalSeconds > 30.0)
                    lowRemove++;
                if (stat.LowTimes.Count > 2400)
                    lowRemove = Math.Max(lowRemove, stat.LowTimes.Count - 2400);
                if (lowRemove > 0)
                {
                    stat.LowTimes.RemoveRange(0, lowRemove);
                    stat.LowSamples.RemoveRange(0, lowRemove);
                }

                int take = Math.Min(90, stat.Samples.Count);
                if (take > 0)
                {
                    double sum = 0;
                    for (int i = stat.Samples.Count - take; i < stat.Samples.Count; i++)
                        sum += stat.Samples[i];
                    double average = sum / take;
                    if (average > 0.0001)
                        stat.Fps = 1000.0 / average;
                }
            }
        }

        private static List<string> SplitCsv(string line)
        {
            List<string> result = new List<string>();
            StringBuilder current = new StringBuilder();
            bool quoted = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (c == '"')
                {
                    if (quoted && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        current.Append('"');
                        i++;
                    }
                    else
                    {
                        quoted = !quoted;
                    }
                }
                else if (c == ',' && !quoted)
                {
                    result.Add(current.ToString());
                    current.Length = 0;
                }
                else
                {
                    current.Append(c);
                }
            }
            result.Add(current.ToString());
            return result;
        }

        public FpsSnapshot GetSnapshot()
        {
            FpsSnapshot snapshot = new FpsSnapshot();
            DateTime now = DateTime.UtcNow;
            int foregroundPid = GetForegroundProcessId();

            lock (_sync)
            {
                snapshot.PresentMonRunning = _presentMonRunning;
                snapshot.Status = _status;

                ProcessStat selected = null;
                ProcessStat foreground;
                if (foregroundPid > 0 && _processes.TryGetValue(foregroundPid, out foreground) &&
                    !IsExcluded(foreground.App) && (now - foreground.LastSeen).TotalSeconds < 5.0)
                {
                    selected = foreground;
                }

                if (selected == null)
                {
                    // Pass 1: foreground process (already handled above)
                    // Pass 2: recent non-excluded processes that are likely fullscreen
                    foreach (ProcessStat stat in _processes.Values)
                    {
                        if ((now - stat.LastSeen).TotalSeconds > 5.0)
                            continue;
                        if (IsExcluded(stat.App))
                            continue;
                        if (!IsLikelyFullscreenApp(stat.ProcessId))
                            continue;
                        if (selected == null || stat.LastSeen > selected.LastSeen)
                            selected = stat;
                    }
                    // Pass 3: fallback — any recent non-excluded process with samples
                    if (selected == null)
                    {
                        foreach (ProcessStat stat in _processes.Values)
                        {
                            if ((now - stat.LastSeen).TotalSeconds > 5.0)
                                continue;
                            if (IsExcluded(stat.App))
                                continue;
                            if (selected == null || stat.LastSeen > selected.LastSeen)
                                selected = stat;
                        }
                    }
                }

                if (selected != null)
                {
                    snapshot.Available = (now - selected.LastSeen).TotalSeconds < 2.0 && selected.Fps > 0.05;
                    snapshot.Value = snapshot.Available ? selected.Fps : 0;
                    snapshot.App = selected.App;
                    snapshot.ProcessId = selected.ProcessId;
                    snapshot.OnePercentLow = CalculateOnePercentLow(selected.LowSamples);
                    if (snapshot.PresentMonRunning && !snapshot.Available)
                        snapshot.Status = "等待游戏画面";
                }
                else if (snapshot.PresentMonRunning)
                {
                    snapshot.Status = "等待游戏画面";
                }
            }

            return snapshot;
        }

        private static double? CalculateOnePercentLow(List<double> samples)
        {
            if (samples == null || samples.Count < 10)
                return null;

            List<double> sorted = new List<double>(samples);
            sorted.Sort();
            int count = Math.Max(1, sorted.Count / 100);
            double sum = 0;
            for (int i = sorted.Count - count; i < sorted.Count; i++)
                sum += sorted[i];
            double average = sum / count;
            if (average <= 0.0001)
                return null;
            return 1000.0 / average;
        }

        private static bool IsLikelyFullscreenApp(int processId)
        {
            try
            {
                using (Process process = Process.GetProcessById(processId))
                {
                    IntPtr window = process.MainWindowHandle;
                    if (window == IntPtr.Zero || !Native.IsWindowVisible(window))
                        return false;

                    Native.RECT rect;
                    if (!Native.GetWindowRect(window, out rect))
                        return false;

                    int width = rect.Right - rect.Left;
                    int height = rect.Bottom - rect.Top;
                    int screenWidth = Native.GetSystemMetrics(0);
                    int screenHeight = Native.GetSystemMetrics(1);
                    if (width <= 0 || height <= 0 || screenWidth <= 0 || screenHeight <= 0)
                        return false;

                    return width >= screenWidth * 0.55 && height >= screenHeight * 0.55;
                }
            }
            catch
            {
                return false;
            }
        }

        private static int GetForegroundProcessId()
        {
            try
            {
                IntPtr window = Native.GetForegroundWindow();
                if (window == IntPtr.Zero)
                    return 0;
                uint pid;
                Native.GetWindowThreadProcessId(window, out pid);
                return (int)pid;
            }
            catch
            {
                return 0;
            }
        }

        private static bool IsExcluded(string app)
        {
            if (string.IsNullOrEmpty(app))
                return true;
            string name = app.ToLowerInvariant();
            if (name.EndsWith(".exe", StringComparison.Ordinal))
                name = name.Substring(0, name.Length - 4);

            string[] excluded = new string[]
            {
                "dwm", "explorer", "searchhost", "startmenuexperiencehost",
                "shellexperiencehost", "textinputhost", "lockapp", "taskmgr",
                "msedge", "chrome", "firefox", "iexplore", "opera", "brave",
                "code", "devenv", "discord", "spotify", "vlc", "mpc-hc",
                "obs64", "obs32", "rtss", "afterburner", "presentmon",
                "lanperfmon", "perfmon", "nvidia share", "amdow",
                "deepseek harness", "deepseek", "electron", "windowsterminal",
                "conhost", "powershell", "pwsh", "cmd", "notepad", "calc",
                "systemsettings", "settings", "teams", "slack", "zoom",
                "msedgewebview2", "webview2"
            };

            for (int i = 0; i < excluded.Length; i++)
            {
                if (name == excluded[i])
                    return true;
            }
            return false;
        }

        private static string Shorten(string text, int max)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;
            text = text.Replace("\r", " ").Replace("\n", " ").Trim();
            if (text.Length <= max)
                return text;
            return text.Substring(0, max) + "…";
        }

        public void Dispose()
        {
            _running = false;
            Process process;
            lock (_sync)
            {
                process = _process;
            }

            if (process != null)
            {
                try
                {
                    if (!process.HasExited)
                        process.Kill();
                }
                catch { }
            }

            if (_thread != null)
            {
                try { _thread.Join(3000); }
                catch { }
                _thread = null;
            }

            try
            {
                int pid = Process.GetCurrentProcess().Id;
                string ignored;
                NetInfo.RunHidden("logman.exe", "stop \"LanPerfMon-" + pid.ToString(CultureInfo.InvariantCulture) + "\" -ets", out ignored);
            }
            catch { }
        }
    }
}
