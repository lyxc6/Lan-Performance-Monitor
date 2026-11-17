using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace LanPerfMon
{
    internal sealed class MainForm : Form
    {
        private readonly AppHost _host;
        private readonly AppOptions _options;

        private NotifyIcon _tray;
        private readonly Icon _icon;
        private readonly Timer _timer;

        private TextBox _txtUrl;
        private NumericUpDown _numPort;
        private Button _btnCopy;
        private Button _btnApply;
        private Button _btnOpen;
        private Button _btnExit;
        private Button _btnLayout;
        private CheckBox _chkAutoStart;
        private Label _lblStatus;
        private Label _lblFps;
        private Label _lblVolume;
        private Label _lblFirewall;
        private Label _lblHint;

        private bool _allowExit;
        private bool _loading;
        private bool _balloonShown;
        private ToolStripMenuItem _trayAutoStart;
        private DateTime _copyResetAt = DateTime.MinValue;

        public MainForm(AppOptions options)
        {
            _options = options;
            _host = new AppHost(options);
            _icon = ResourceLoader.LoadIcon();

            BuildForm();
            BuildTray();

            // The HTTP thread may set the layout; marshal back to the UI thread.
            _host.LayoutChanged += delegate
            {
                try
                {
                    if (IsDisposed)
                        return;
                    BeginInvoke((MethodInvoker)delegate { ApplyLayout(_host.Layout); });
                }
                catch { }
            };

            _host.Start();

            _loading = true;
            _numPort.Value = _host.Port > 0 ? _host.Port : _host.Config.Port;
            _chkAutoStart.Checked = _host.Config.AutoStart;
            _trayAutoStart.Checked = _host.Config.AutoStart;
            _loading = false;

            // Reflect the persisted layout in the button + status line.
            _btnLayout.Text = LayoutButtonText(_host.Layout);
            if (_host.Layout == "auto")
            {
                _btnLayout.BackColor = Color.FromArgb(20, 34, 47);
                _btnLayout.ForeColor = Color.FromArgb(196, 220, 234);
            }
            else
            {
                _btnLayout.BackColor = Color.FromArgb(30, 184, 214);
                _btnLayout.ForeColor = Color.FromArgb(4, 18, 24);
            }

            UpdateStatus();
            _timer = new Timer();
            _timer.Interval = 1000;
            _timer.Tick += TimerTick;
            _timer.Start();

            if (_options.Minimized)
            {
                ShowInTaskbar = false;
                WindowState = FormWindowState.Minimized;
            }
        }

        private void BuildForm()
        {
            Text = "LAN 性能监控";
            ClientSize = new Size(540, 390);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            BackColor = Color.FromArgb(9, 13, 19);
            ForeColor = Color.FromArgb(225, 238, 248);
            Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular);
            Icon = _icon;

            Panel header = new Panel();
            header.Dock = DockStyle.Top;
            header.Height = 68;
            header.BackColor = Color.FromArgb(13, 24, 36);
            Controls.Add(header);

            Label title = new Label();
            title.Text = "LAN PERF MON";
            title.Font = new Font("Consolas", 18F, FontStyle.Bold);
            title.ForeColor = Color.FromArgb(92, 236, 255);
            title.AutoSize = true;
            title.Location = new Point(18, 10);
            header.Controls.Add(title);

            Label subtitle = new Label();
            subtitle.Text = "本机硬件 · 局域网实时监控";
            subtitle.Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Regular);
            subtitle.ForeColor = Color.FromArgb(128, 153, 176);
            subtitle.AutoSize = true;
            subtitle.Location = new Point(21, 43);
            header.Controls.Add(subtitle);

            Label urlLabel = new Label();
            urlLabel.Text = "手机访问地址（iPhone 4S / iOS 6.1.3 直接打开）";
            urlLabel.ForeColor = Color.FromArgb(139, 164, 185);
            urlLabel.AutoSize = true;
            urlLabel.Location = new Point(18, 81);
            Controls.Add(urlLabel);

            _txtUrl = new TextBox();
            _txtUrl.Location = new Point(18, 102);
            _txtUrl.Size = new Size(384, 28);
            _txtUrl.ReadOnly = true;
            _txtUrl.BorderStyle = BorderStyle.FixedSingle;
            _txtUrl.BackColor = Color.FromArgb(14, 24, 34);
            _txtUrl.ForeColor = Color.FromArgb(92, 236, 255);
            _txtUrl.Font = new Font("Consolas", 12F, FontStyle.Regular);
            Controls.Add(_txtUrl);

            _btnCopy = MakeButton("复制地址", 18);
            _btnCopy.Location = new Point(408, 101);
            _btnCopy.Size = new Size(114, 30);
            _btnCopy.Click += delegate { CopyUrl(); };
            Controls.Add(_btnCopy);

            Label portLabel = new Label();
            portLabel.Text = "端口";
            portLabel.ForeColor = Color.FromArgb(139, 164, 185);
            portLabel.AutoSize = true;
            portLabel.Location = new Point(18, 146);
            Controls.Add(portLabel);

            _numPort = new NumericUpDown();
            _numPort.Minimum = 1;
            _numPort.Maximum = 65535;
            _numPort.Value = 8686;
            _numPort.Location = new Point(56, 142);
            _numPort.Size = new Size(88, 25);
            _numPort.BackColor = Color.FromArgb(14, 24, 34);
            _numPort.ForeColor = Color.FromArgb(225, 238, 248);
            _numPort.BorderStyle = BorderStyle.FixedSingle;
            Controls.Add(_numPort);

            _btnApply = MakeButton("应用端口", 154);
            _btnApply.Location = new Point(154, 141);
            _btnApply.Size = new Size(96, 28);
            _btnApply.Click += delegate { ApplyPort(); };
            Controls.Add(_btnApply);

            Label portHint = new Label();
            portHint.Text = "修改后服务自动重启，并在防火墙放行新端口";
            portHint.ForeColor = Color.FromArgb(107, 128, 146);
            portHint.AutoSize = true;
            portHint.Location = new Point(258, 146);
            Controls.Add(portHint);

            _lblStatus = MakeStatusLabel(178);
            _lblFps = MakeStatusLabel(202);
            _lblVolume = MakeStatusLabel(226);
            _lblFirewall = MakeStatusLabel(250);

            _lblHint = new Label();
            _lblHint.Text = "同一 Wi-Fi 下用手机浏览器访问上方地址；可添加到主屏幕全屏显示。\n" +
                           "FPS 来自 PresentMon（随游戏画面自动识别）；温度/功耗/电压需要管理员权限。";
            _lblHint.ForeColor = Color.FromArgb(112, 133, 151);
            _lblHint.Font = new Font("Microsoft YaHei UI", 8F, FontStyle.Regular);
            _lblHint.Location = new Point(18, 280);
            _lblHint.Size = new Size(504, 38);
            Controls.Add(_lblHint);

            _btnOpen = MakeButton("打开监控页面", 18);
            _btnOpen.Location = new Point(18, 330);
            _btnOpen.Size = new Size(150, 34);
            _btnOpen.BackColor = Color.FromArgb(30, 184, 214);
            _btnOpen.ForeColor = Color.FromArgb(4, 18, 24);
            _btnOpen.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
            _btnOpen.Click += delegate { OpenBrowser(); };
            Controls.Add(_btnOpen);

            _chkAutoStart = new CheckBox();
            _chkAutoStart.Text = "开机自动启动";
            _chkAutoStart.ForeColor = Color.FromArgb(166, 188, 204);
            _chkAutoStart.AutoSize = true;
            _chkAutoStart.Location = new Point(187, 337);
            _chkAutoStart.CheckedChanged += AutoStartChanged;
            Controls.Add(_chkAutoStart);

            // Display layout switch: cycles 自动 → 横屏 → 竖屏 → 自动.
            // The mode is pushed to every web client via /api/stats (sys.layout).
            _btnLayout = MakeButton(LayoutButtonText(_host.Layout), 288);
            _btnLayout.Location = new Point(288, 330);
            _btnLayout.Size = new Size(118, 34);
            _btnLayout.Click += delegate { CycleLayout(); };
            Controls.Add(_btnLayout);

            _btnExit = MakeButton("退出", 420);
            _btnExit.Location = new Point(420, 330);
            _btnExit.Size = new Size(102, 34);
            _btnExit.Click += delegate { ExitApp(); };
            Controls.Add(_btnExit);

            FormClosing += MainFormClosing;
            Resize += delegate
            {
                if (WindowState == FormWindowState.Minimized)
                    Hide();
            };
        }

        private static Label MakeStatusLabel(int y)
        {
            Label label = new Label();
            label.Location = new Point(18, y);
            label.Size = new Size(504, 22);
            label.ForeColor = Color.FromArgb(176, 198, 214);
            label.Font = new Font("Consolas", 9F, FontStyle.Regular);
            return label;
        }

        private static Button MakeButton(string text, int x)
        {
            Button button = new Button();
            button.Text = text;
            button.Location = new Point(x, 0);
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderColor = Color.FromArgb(42, 67, 86);
            button.FlatAppearance.BorderSize = 1;
            button.BackColor = Color.FromArgb(20, 34, 47);
            button.ForeColor = Color.FromArgb(196, 220, 234);
            button.Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Regular);
            button.Cursor = Cursors.Hand;
            return button;
        }

        private void BuildTray()
        {
            ContextMenuStrip menu = new ContextMenuStrip();
            menu.BackColor = Color.FromArgb(16, 27, 38);
            menu.ForeColor = Color.FromArgb(220, 234, 244);

            ToolStripMenuItem show = new ToolStripMenuItem("显示主窗口");
            show.Click += delegate { ShowForm(); };
            menu.Items.Add(show);

            ToolStripMenuItem open = new ToolStripMenuItem("打开监控页面");
            open.Click += delegate { OpenBrowser(); };
            menu.Items.Add(open);

            ToolStripMenuItem copy = new ToolStripMenuItem("复制访问地址");
            copy.Click += delegate { CopyUrl(); };
            menu.Items.Add(copy);

            menu.Items.Add(new ToolStripSeparator());

            _trayAutoStart = new ToolStripMenuItem("开机自动启动");
            _trayAutoStart.Click += delegate
            {
                _chkAutoStart.Checked = !_chkAutoStart.Checked;
            };
            menu.Items.Add(_trayAutoStart);

            menu.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem exit = new ToolStripMenuItem("退出");
            exit.Click += delegate { ExitApp(); };
            menu.Items.Add(exit);

            _tray = new NotifyIcon();
            _tray.Icon = _icon;
            _tray.Text = "LAN 性能监控";
            _tray.Visible = true;
            _tray.ContextMenuStrip = menu;
            _tray.DoubleClick += delegate { ShowForm(); };

            // At logon the app races Explorer: if Shell_NotifyIcon(NIM_ADD) runs
            // before the notification area exists it fails silently and the icon
            // never appears. Re-register a few times during the first minute.
            _trayRetryCount = 0;
            _startedAt = DateTime.Now;
        }

        private static readonly int[] TrayRetrySeconds = { 3, 10, 30, 60 };

        private int _trayRetryCount;
        private DateTime _startedAt = DateTime.Now;

        /// <summary>Number of completed tray re-registration passes (for /api/debug).</summary>
        internal static int TrayRetryDone;

        /// <summary>
        /// Force Shell_NotifyIcon(NIM_DELETE + NIM_ADD) by toggling Visible.
        /// Fixes the missing tray icon when auto-started at logon.
        /// </summary>
        private void ReRegisterTray()
        {
            try
            {
                if (_tray == null)
                    return;
                _tray.Visible = false;
                _tray.Visible = true;
                TrayRetryDone = _trayRetryCount;
                Log.Write("Tray icon re-registered (attempt " + _trayRetryCount + ")");
            }
            catch (Exception ex)
            {
                Log.Write("Tray re-register failed: " + ex.Message);
            }
        }

        private void TimerTick(object sender, EventArgs e)
        {
            UpdateStatus();
            if (_copyResetAt != DateTime.MinValue && DateTime.Now >= _copyResetAt)
            {
                _btnCopy.Text = "复制地址";
                _copyResetAt = DateTime.MinValue;
            }

            if (_trayRetryCount < TrayRetrySeconds.Length &&
                (DateTime.Now - _startedAt).TotalSeconds >= TrayRetrySeconds[_trayRetryCount])
            {
                _trayRetryCount++;
                ReRegisterTray();
            }
        }

        private void UpdateStatus()
        {
            try
            {
                if (_host.Server != null)
                {
                    _txtUrl.Text = _host.Url;
                    _txtUrl.ForeColor = Color.FromArgb(92, 236, 255);
                    _lblFirewall.Text = "服务: 监听 0.0.0.0:" + _host.Port +
                        (string.IsNullOrEmpty(_host.FirewallMessage) ? "  ·  防火墙已放行" : "  ·  " + _host.FirewallMessage);
                }
                else
                {
                    _txtUrl.Text = "服务未启动";
                    _txtUrl.ForeColor = Color.FromArgb(255, 120, 120);
                    _lblFirewall.Text = "服务: " + (_host.ServerError.Length > 0 ? _host.ServerError : "未启动");
                }

                MonitorSnapshot monitor = _host.Monitor.Current;
                string sensorText;
                if (monitor.Ok)
                {
                    sensorText = "传感器: " + (monitor.GpuSensorsAvailable ? "CPU/GPU/RAM 在线" : "部分在线") +
                        "  ·  " + (Native.IsElevated ? "管理员权限" : "普通权限");
                }
                else
                {
                    sensorText = "传感器: 初始化中" +
                        (string.IsNullOrEmpty(monitor.Error) ? string.Empty : "  ·  " + monitor.Error);
                }
                // Keep the active layout visible; the button label alone is easy
                // to miss on this dark UI.
                _lblStatus.Text = sensorText + "  ·  " + LayoutShortText(_host.Layout);

                FpsSnapshot fps = _host.Fps.GetSnapshot();
                if (fps.Available)
                    _lblFps.Text = "FPS: " + Math.Round(fps.Value).ToString() + "  ·  " + fps.App;
                else if (fps.PresentMonRunning)
                    _lblFps.Text = "FPS: 等待游戏画面  ·  PresentMon 已连接";
                else
                    _lblFps.Text = "FPS: 未就绪  ·  " + fps.Status;

                VolumeState volume = _host.Volume.Get();
                if (volume.Available)
                    _lblVolume.Text = "音量: " + volume.Percent + "%" + (volume.Mute ? "  ·  静音" : string.Empty);
                else
                    _lblVolume.Text = "音量: 不可用";
            }
            catch (Exception ex)
            {
                _lblStatus.Text = "状态读取失败: " + ex.Message;
            }
        }

        private void ApplyPort()
        {
            int port = (int)_numPort.Value;
            string error;
            if (_host.RestartServer(port, out error))
            {
                _txtUrl.Text = _host.Url;
                _lblFirewall.Text = "服务: 监听 0.0.0.0:" + _host.Port;
            }
            else
            {
                MessageBox.Show(error, "端口切换失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                if (_host.Port > 0)
                    _numPort.Value = _host.Port;
            }
        }

        private void CopyUrl()
        {
            if (_host.Server == null)
            {
                MessageBox.Show("服务尚未启动。", "复制地址", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            try
            {
                Clipboard.SetText(_host.Url);
                _btnCopy.Text = "已复制";
                _copyResetAt = DateTime.Now.AddSeconds(2);
            }
            catch (Exception ex)
            {
                MessageBox.Show("复制失败：" + ex.Message, "复制地址", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void OpenBrowser()
        {
            if (_host.Server == null)
            {
                MessageBox.Show("服务尚未启动，无法打开页面。", "打开监控页面",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            try
            {
                Process.Start(new ProcessStartInfo(_host.Url) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show("打开浏览器失败：" + ex.Message, "打开监控页面",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void AutoStartChanged(object sender, EventArgs e)
        {
            if (_loading)
                return;

            string error;
            if (!NetInfo.SetAutoStart(_chkAutoStart.Checked, out error))
            {
                MessageBox.Show(error, "开机自启", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                _loading = true;
                _chkAutoStart.Checked = !_chkAutoStart.Checked;
                _trayAutoStart.Checked = _chkAutoStart.Checked;
                _loading = false;
                return;
            }

            _host.Config.AutoStart = _chkAutoStart.Checked;
            _host.Config.Save();
            _trayAutoStart.Checked = _chkAutoStart.Checked;
        }

        private static string LayoutButtonText(string mode)
        {
            if (mode == "landscape")
                return "布局:横屏";
            if (mode == "portrait")
                return "布局:竖屏";
            return "布局:自动";
        }

        private static string LayoutShortText(string mode)
        {
            if (mode == "landscape")
                return "显示: 强制横屏";
            if (mode == "portrait")
                return "显示: 强制竖屏";
            return "显示: 自动";
        }

        /// <summary>
        /// Cycle display layout: auto -> landscape -> portrait -> auto.
        /// The chosen mode is saved to config and pushed to every web client
        /// through sys.layout in /api/stats, so the phone page switches even
        /// when its own orientation detection fails (iOS 6.1.3 Safari).
        /// </summary>
        private void CycleLayout()
        {
            string next;
            if (_host.Layout == "auto")
                next = "landscape";
            else if (_host.Layout == "landscape")
                next = "portrait";
            else
                next = "auto";

            ApplyLayout(next);
        }

        /// <summary>
        /// Apply a layout mode through AppHost (persists it) and refresh the UI.
        /// Also used by the /api/layout endpoint so both entry points agree.
        /// </summary>
        internal void ApplyLayout(string mode)
        {
            string applied = _host.SetLayout(mode);
            _btnLayout.Text = LayoutButtonText(applied);
            // Give the button a matching accent so the change is obvious.
            if (applied == "auto")
            {
                _btnLayout.BackColor = Color.FromArgb(20, 34, 47);
                _btnLayout.ForeColor = Color.FromArgb(196, 220, 234);
            }
            else
            {
                _btnLayout.BackColor = Color.FromArgb(30, 184, 214);
                _btnLayout.ForeColor = Color.FromArgb(4, 18, 24);
            }
            UpdateStatus();
        }

        private void ShowForm()
        {
            ShowInTaskbar = true;
            Show();
            WindowState = FormWindowState.Normal;
            Activate();
            BringToFront();
        }

        private void MainFormClosing(object sender, FormClosingEventArgs e)
        {
            if (_allowExit)
                return;

            if (e.CloseReason == CloseReason.WindowsShutDown ||
                e.CloseReason == CloseReason.TaskManagerClosing)
            {
                _allowExit = true;
                return;
            }

            e.Cancel = true;
            Hide();
            ShowInTaskbar = false;
            if (!_balloonShown)
            {
                _balloonShown = true;
                try
                {
                    _tray.ShowBalloonTip(2500, "LAN 性能监控",
                        "程序仍在后台运行，手机仍可访问。右键托盘图标可退出。",
                        ToolTipIcon.Info);
                }
                catch { }
            }
        }

        private void ExitApp()
        {
            _allowExit = true;
            if (_timer != null)
                _timer.Stop();
            try { _tray.Visible = false; }
            catch { }
            try { _host.Dispose(); }
            catch { }
            try { _tray.Dispose(); }
            catch { }
            Close();
            Application.Exit();
        }
    }
}
