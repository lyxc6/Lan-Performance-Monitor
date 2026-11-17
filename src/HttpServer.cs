using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace LanPerfMon
{
    internal sealed class HttpServer : IDisposable
    {
        private readonly AppHost _host;
        private TcpListener _listener;
        private Thread _acceptThread;
        private volatile bool _running;
        private int _port;

        public HttpServer(AppHost host)
        {
            _host = host;
        }

        public int Port
        {
            get { return _port; }
        }

        public bool Start(int port, bool localOnly)
        {
            Stop();
            try
            {
                IPAddress bind = localOnly ? IPAddress.Loopback : IPAddress.Any;
                TcpListener listener = new TcpListener(bind, port);
                listener.Start(64);
                try
                {
                    // Do not let a redirected child process inherit the listening
                    // socket.  Without this a force-killed PresentMon could keep
                    // the HTTP port bound after the UI exits.
                    Native.SetHandleInformation(listener.Server.Handle, 1, 0);
                }
                catch { }
                _listener = listener;
                _port = port;
                _running = true;
                _acceptThread = new Thread(AcceptLoop);
                _acceptThread.IsBackground = true;
                _acceptThread.Name = "LanPerfMon.HTTP";
                _acceptThread.Start();
                return true;
            }
            catch
            {
                _listener = null;
                _running = false;
                _port = 0;
                return false;
            }
        }

        private void AcceptLoop()
        {
            while (_running)
            {
                TcpClient client = null;
                try
                {
                    client = _listener.AcceptTcpClient();
                }
                catch
                {
                    if (!_running)
                        break;
                    Thread.Sleep(20);
                    continue;
                }

                try
                {
                    ThreadPool.QueueUserWorkItem(HandleClient, client);
                }
                catch
                {
                    try { client.Close(); }
                    catch { }
                }
            }
        }

        private void HandleClient(object state)
        {
            TcpClient client = state as TcpClient;
            if (client == null)
                return;

            try
            {
                client.NoDelay = true;
                client.ReceiveTimeout = 5000;
                client.SendTimeout = 5000;

                IPEndPoint remote = client.Client.RemoteEndPoint as IPEndPoint;
                if (remote != null && !Native.IsPrivateOrLoopback(remote.Address))
                    return;

                using (NetworkStream stream = client.GetStream())
                {
                    Request request = ReadRequest(stream);
                    if (request == null)
                        return;
                    Route(stream, request);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("HTTP client error: " + ex.Message);
            }
            finally
            {
                try { client.Close(); }
                catch { }
            }
        }

        private sealed class Request
        {
            public string Method = "GET";
            public string Path = "/";
            public string Query = string.Empty;
            public Dictionary<string, string> Headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            public string Body = string.Empty;
            public bool HeadOnly;
        }

        private static Request ReadRequest(NetworkStream stream)
        {
            byte[] buffer = new byte[16384];
            int length = 0;

            while (length < buffer.Length)
            {
                int value = stream.ReadByte();
                if (value < 0)
                    return null;
                buffer[length++] = (byte)value;
                if (length >= 4 &&
                    buffer[length - 4] == (byte)'\r' && buffer[length - 3] == (byte)'\n' &&
                    buffer[length - 2] == (byte)'\r' && buffer[length - 1] == (byte)'\n')
                    break;
            }

            if (length == 0)
                return null;

            string headerText = Encoding.ASCII.GetString(buffer, 0, length);
            string[] lines = headerText.Split(new string[] { "\r\n" }, StringSplitOptions.None);
            if (lines.Length == 0)
                return null;

            string[] requestLine = lines[0].Split(' ');
            if (requestLine.Length < 2)
                return null;

            Request request = new Request();
            request.Method = requestLine[0].ToUpperInvariant();
            request.HeadOnly = request.Method == "HEAD";

            string target = requestLine[1];
            if (target.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                target.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    Uri uri = new Uri(target);
                    target = uri.PathAndQuery;
                }
                catch { }
            }

            int queryIndex = target.IndexOf('?');
            if (queryIndex >= 0)
            {
                request.Path = target.Substring(0, queryIndex);
                request.Query = target.Substring(queryIndex + 1);
            }
            else
            {
                request.Path = target;
            }
            if (request.Path.Length == 0)
                request.Path = "/";

            for (int i = 1; i < lines.Length; i++)
            {
                int colon = lines[i].IndexOf(':');
                if (colon <= 0)
                    continue;
                string key = lines[i].Substring(0, colon).Trim();
                string value = lines[i].Substring(colon + 1).Trim();
                if (!request.Headers.ContainsKey(key))
                    request.Headers.Add(key, value);
            }

            int contentLength = 0;
            string lengthText;
            if (request.Headers.TryGetValue("Content-Length", out lengthText))
                int.TryParse(lengthText, NumberStyles.Integer, CultureInfo.InvariantCulture, out contentLength);
            if (contentLength < 0 || contentLength > 65536)
                return null;

            if (contentLength > 0)
            {
                byte[] body = new byte[contentLength];
                int offset = 0;
                while (offset < contentLength)
                {
                    int read = stream.Read(body, offset, contentLength - offset);
                    if (read <= 0)
                        break;
                    offset += read;
                }
                request.Body = Encoding.UTF8.GetString(body, 0, offset);
            }

            return request;
        }

        private void Route(NetworkStream stream, Request request)
        {
            string path = request.Path;
            bool head = request.HeadOnly;

            if (path.Equals("/api/stats", StringComparison.OrdinalIgnoreCase))
            {
                if (request.Method != "GET" && request.Method != "HEAD")
                {
                    WriteJson(stream, 405, StatsJson.Error("Method not allowed"), head);
                    return;
                }
                WriteJson(stream, 200, StatsJson.Build(_host), head);
                return;
            }

            if (path.Equals("/api/volume", StringComparison.OrdinalIgnoreCase))
            {
                if (request.Method == "GET" || request.Method == "HEAD")
                {
                    WriteJson(stream, 200, StatsJson.BuildVolume(_host.Volume.Get()), head);
                    return;
                }
                if (request.Method == "POST")
                {
                    Dictionary<string, string> form = ParseForm(request.Body);
                    VolumeState state;
                    string value;
                    if (form.TryGetValue("v", out value))
                    {
                        int percent;
                        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out percent))
                        {
                            WriteJson(stream, 400, StatsJson.Error("Invalid volume"), head);
                            return;
                        }
                        state = _host.Volume.SetPercent(percent);
                    }
                    else if (form.ContainsKey("mute"))
                    {
                        state = _host.Volume.ToggleMute();
                    }
                    else
                    {
                        WriteJson(stream, 400, StatsJson.Error("Missing volume value"), head);
                        return;
                    }
                    WriteJson(stream, 200, StatsJson.BuildVolume(state), head);
                    return;
                }
                WriteJson(stream, 405, StatsJson.Error("Method not allowed"), head);
                return;
            }

            if (path.Equals("/api/layout", StringComparison.OrdinalIgnoreCase))
            {
                if (request.Method == "POST")
                {
                    Dictionary<string, string> form = ParseForm(request.Body);
                    string mode;
                    if (form.TryGetValue("mode", out mode))
                    {
                        mode = (mode ?? string.Empty).Trim().ToLowerInvariant();
                        if (mode != "auto" && mode != "landscape" && mode != "portrait")
                        {
                            WriteJson(stream, 400, StatsJson.Error("mode must be auto|landscape|portrait"), head);
                            return;
                        }
                        _host.SetLayout(mode);
                        _host.OnLayoutChanged();
                    }
                }
                StringBuilder lsb = new StringBuilder(64);
                lsb.Append("{\"ok\":true,\"layout\":");
                StatsJson.Quote(lsb, _host.Layout);
                lsb.Append("}");
                WriteJson(stream, 200, lsb.ToString(), head);
                return;
            }

            if (path.Equals("/api/config", StringComparison.OrdinalIgnoreCase))
            {
                StringBuilder sb = new StringBuilder(200);
                sb.Append("{\"ok\":true,\"port\":").Append(_host.Port.ToString(CultureInfo.InvariantCulture));
                sb.Append(",\"version\":\"").Append(AppHost.Version).Append("\",\"ips\":[");
                List<string> ips = NetInfo.GetAllIPv4();
                for (int i = 0; i < ips.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    sb.Append('"').Append(ips[i]).Append('"');
                }
                sb.Append("]}");
                WriteJson(stream, 200, sb.ToString(), head);
                return;
            }

            if (path.Equals("/api/health", StringComparison.OrdinalIgnoreCase))
            {
                WriteJson(stream, 200, StatsJson.SimpleOk(), head);
                return;
            }

            if (path.Equals("/api/debug", StringComparison.OrdinalIgnoreCase))
            {
                StringBuilder sb = new StringBuilder(900);
                sb.Append("{\"ok\":true");
                sb.Append(",\"logPath\":"); StatsJson.Quote(sb, Log.LogPath);
                sb.Append(",\"logTest\":"); StatsJson.Quote(sb, Log.TestWrite());
                sb.Append(",\"newFileWrite\":"); StatsJson.Quote(sb, Diag.NewFileWrite());
                sb.Append(",\"integrity\":"); StatsJson.Quote(sb, Diag.Integrity());
                sb.Append(",\"rawShellAdd\":"); StatsJson.Quote(sb, Diag.RawShellAdd());
                sb.Append(",\"temp\":"); StatsJson.Quote(sb, Environment.GetEnvironmentVariable("TEMP"));
                sb.Append(",\"cwd\":"); StatsJson.Quote(sb, Environment.CurrentDirectory);
                sb.Append(",\"userInteractive\":").Append(Environment.UserInteractive ? "true" : "false");
                sb.Append(",\"session\":").Append(Native.GetSessionId().ToString(CultureInfo.InvariantCulture));
                sb.Append(",\"elevated\":").Append(Native.IsElevated ? "true" : "false");
                sb.Append(",\"trayRetries\":").Append(MainForm.TrayRetryDone.ToString(CultureInfo.InvariantCulture));
                sb.Append(",\"uptime\":").Append(((long)(DateTime.UtcNow - _host.StartedUtc).TotalSeconds).ToString(CultureInfo.InvariantCulture));
                sb.Append("}");
                WriteJson(stream, 200, sb.ToString(), head);
                return;
            }

            if (path.Equals("/", StringComparison.OrdinalIgnoreCase) ||
                path.Equals("/index.html", StringComparison.OrdinalIgnoreCase))
            {
                byte[] body = ResourceLoader.Read("web.index.html");
                WriteResponse(stream, 200, "text/html; charset=utf-8", body, head, "no-cache");
                return;
            }

            if (path.Equals("/favicon.ico", StringComparison.OrdinalIgnoreCase))
            {
                byte[] body = ResourceLoader.Read("app.ico");
                WriteResponse(stream, 200, "image/x-icon", body, head, "max-age=86400");
                return;
            }

            if (path.Equals("/apple-touch-icon.png", StringComparison.OrdinalIgnoreCase) ||
                path.Equals("/apple-touch-icon-precomposed.png", StringComparison.OrdinalIgnoreCase))
            {
                byte[] body = ResourceLoader.Read("web.apple-touch-icon.png");
                WriteResponse(stream, 200, "image/png", body, head, "max-age=86400");
                return;
            }

            WriteResponse(stream, 404, "text/plain; charset=utf-8",
                Encoding.UTF8.GetBytes("Not Found"), head, "no-store");
        }

        private static Dictionary<string, string> ParseForm(string body)
        {
            Dictionary<string, string> result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(body))
                return result;

            string[] pairs = body.Split('&');
            for (int i = 0; i < pairs.Length; i++)
            {
                string pair = pairs[i];
                if (pair.Length == 0)
                    continue;
                int eq = pair.IndexOf('=');
                string key;
                string value;
                if (eq < 0)
                {
                    key = pair;
                    value = string.Empty;
                }
                else
                {
                    key = pair.Substring(0, eq);
                    value = pair.Substring(eq + 1);
                }
                try
                {
                    key = Uri.UnescapeDataString(key.Replace("+", " "));
                    value = Uri.UnescapeDataString(value.Replace("+", " "));
                }
                catch { }
                result[key] = value;
            }
            return result;
        }

        private static void WriteJson(NetworkStream stream, int status, string json, bool headOnly)
        {
            WriteResponse(stream, status, "application/json; charset=utf-8",
                Encoding.UTF8.GetBytes(json), headOnly, "no-store");
        }

        private static void WriteResponse(NetworkStream stream, int status, string contentType,
            byte[] body, bool headOnly, string cacheControl)
        {
            string reason = ReasonPhrase(status);
            StringBuilder header = new StringBuilder(240);
            header.Append("HTTP/1.1 ").Append(status).Append(' ').Append(reason).Append("\r\n");
            header.Append("Content-Type: ").Append(contentType).Append("\r\n");
            header.Append("Content-Length: ").Append(body.Length.ToString(CultureInfo.InvariantCulture)).Append("\r\n");
            header.Append("Cache-Control: ").Append(cacheControl).Append("\r\n");
            header.Append("Connection: close\r\n");
            header.Append("Server: LanPerfMon/").Append(AppHost.Version).Append("\r\n");
            header.Append("X-Content-Type-Options: nosniff\r\n");
            header.Append("Access-Control-Allow-Origin: *\r\n");
            header.Append("\r\n");

            byte[] headerBytes = Encoding.ASCII.GetBytes(header.ToString());
            stream.Write(headerBytes, 0, headerBytes.Length);
            if (!headOnly && body.Length > 0)
                stream.Write(body, 0, body.Length);
            stream.Flush();
        }

        private static string ReasonPhrase(int status)
        {
            switch (status)
            {
                case 200: return "OK";
                case 204: return "No Content";
                case 400: return "Bad Request";
                case 404: return "Not Found";
                case 405: return "Method Not Allowed";
                default: return "Internal Server Error";
            }
        }

        public void Stop()
        {
            _running = false;
            if (_listener != null)
            {
                try { _listener.Stop(); }
                catch { }
                _listener = null;
            }
            if (_acceptThread != null)
            {
                try { _acceptThread.Join(1000); }
                catch { }
                _acceptThread = null;
            }
            _port = 0;
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
