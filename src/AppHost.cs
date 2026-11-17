using System;
using System.Collections.Generic;

namespace LanPerfMon
{
    internal sealed class AppHost : IDisposable
    {
        public const string Version = "1.0.0";

        private readonly AppOptions _options;

        public Config Config;
        public MonitorService Monitor;
        public FpsMonitor Fps;
        public VolumeController Volume;
        public HttpServer Server;

        public int Port;
        public string Url = string.Empty;
        public string ServerError = string.Empty;
        public string FirewallMessage = string.Empty;
        public DateTime StartedUtc = DateTime.UtcNow;

        /// <summary>Display layout pushed to web clients: auto | landscape | portrait.</summary>
        public string Layout;

        /// <summary>Raised when the layout mode changes (HTTP or UI). Runs on the caller thread.</summary>
        public event Action LayoutChanged;

        public void OnLayoutChanged()
        {
            Action handler = LayoutChanged;
            if (handler == null)
                return;
            try { handler(); }
            catch (Exception ex) { Log.Write("LayoutChanged handler failed: " + ex.Message); }
        }

        /// <summary>Set and persist the layout mode. Returns the applied value.</summary>
        public string SetLayout(string mode)
        {
            string next = "auto";
            if (mode == "landscape" || mode == "portrait")
                next = mode;

            Layout = next;
            Config.Layout = next;
            Config.Save();
            Log.Write("Layout set to " + next);
            return next;
        }

        public AppHost(AppOptions options)
        {
            _options = options;
            Config = Config.Load();
            Port = options.Port > 0 ? options.Port : Config.Port;
            Layout = Config.Layout == "landscape" || Config.Layout == "portrait"
                ? Config.Layout : "auto";

            Monitor = new MonitorService();
            Fps = new FpsMonitor();
            Volume = new VolumeController();
        }

        public void Start()
        {
            StartedUtc = DateTime.UtcNow;
            Log.Write("AppHost.Start enter, port=" + Port);
            Monitor.Start();
            Log.Write("AppHost.Start: monitor started");
            if (!_options.NoFps)
            {
                Fps.Start();
                Log.Write("AppHost.Start: fps started");
            }

            TryStartServer(Port, out ServerError);
            Log.Write("AppHost.Start: server=" + (Server != null ? "ok" : "failed") + " error=" + ServerError);
        }

        private bool TryStartServer(int port, out string error)
        {
            error = string.Empty;
            HttpServer server = new HttpServer(this);
            if (!server.Start(port, _options.LocalOnly))
            {
                server.Dispose();
                error = "端口 " + port + " 无法监听，可能已被其他程序占用。";
                ServerError = error;
                return false;
            }

            Server = server;
            Port = port;
            ServerError = string.Empty;
            Url = BuildUrl(port);

            int oldPort = Config.LastPort;
            Config.Port = port;
            Config.LastPort = port;
            Config.Save();

            if (!_options.NoFirewall && Native.IsElevated)
            {
                if (oldPort > 0 && oldPort != port)
                    NetInfo.RemoveFirewallRule(oldPort);
                string message;
                NetInfo.EnsureFirewallRule(port, out message);
                FirewallMessage = message;
            }
            else if (!Native.IsElevated)
            {
                FirewallMessage = "未以管理员身份运行，无法自动添加防火墙规则。";
            }

            return true;
        }

        public bool RestartServer(int newPort, out string error)
        {
            error = string.Empty;
            if (newPort < 1 || newPort > 65535)
            {
                error = "端口必须在 1 到 65535 之间。";
                return false;
            }

            if (Server != null && Port == newPort)
                return true;

            int oldPort = Port;
            if (Server != null)
            {
                Server.Stop();
                Server.Dispose();
                Server = null;
            }
            Port = 0;

            if (TryStartServer(newPort, out error))
                return true;

            // Roll back to the previous port so the desktop side keeps working.
            if (oldPort > 0)
            {
                string ignored;
                TryStartServer(oldPort, out ignored);
            }
            return false;
        }

        private string BuildUrl(int port)
        {
            if (_options.LocalOnly)
                return "http://127.0.0.1:" + port + "/";
            string ip = NetInfo.GetLocalIPv4();
            return "http://" + ip + ":" + port + "/";
        }

        public string PrimaryIp
        {
            get
            {
                string ip = NetInfo.GetLocalIPv4();
                return ip;
            }
        }

        public IList<string> AllIps
        {
            get { return NetInfo.GetAllIPv4(); }
        }

        public void Dispose()
        {
            if (Server != null)
            {
                Server.Stop();
                Server.Dispose();
                Server = null;
            }
            if (Fps != null)
            {
                Fps.Dispose();
                Fps = null;
            }
            if (Monitor != null)
            {
                Monitor.Dispose();
                Monitor = null;
            }
        }
    }
}
