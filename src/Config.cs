using System;
using System.Globalization;
using System.IO;
using System.Windows.Forms;

namespace LanPerfMon
{
    internal sealed class Config
    {
        public int Port = 8686;
        public bool AutoStart;
        public int LastPort;
        /// <summary>Display layout: auto | landscape | portrait.</summary>
        public string Layout = "auto";

        private static string _path;

        public static string PathOnDisk
        {
            get { return _path; }
        }

        public static Config Load()
        {
            Config config = new Config();
            _path = Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "LanPerfMon.ini");

            if (!File.Exists(_path))
            {
                string appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LanPerfMon");
                string alt = Path.Combine(appData, "LanPerfMon.ini");
                if (File.Exists(alt))
                    _path = alt;
            }

            try
            {
                if (File.Exists(_path))
                {
                    string[] lines = File.ReadAllLines(_path);
                    for (int i = 0; i < lines.Length; i++)
                    {
                        string line = lines[i].Trim();
                        if (line.Length == 0 || line.StartsWith("#") || line.StartsWith(";"))
                            continue;
                        int eq = line.IndexOf('=');
                        if (eq <= 0)
                            continue;
                        string key = line.Substring(0, eq).Trim();
                        string value = line.Substring(eq + 1).Trim();
                        if (key.Equals("port", StringComparison.OrdinalIgnoreCase))
                        {
                            int p;
                            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out p) && p >= 1 && p <= 65535)
                                config.Port = p;
                        }
                        else if (key.Equals("lastport", StringComparison.OrdinalIgnoreCase))
                        {
                            int p;
                            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out p))
                                config.LastPort = p;
                        }
                        else if (key.Equals("autostart", StringComparison.OrdinalIgnoreCase))
                        {
                            config.AutoStart = value == "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase);
                        }
                        else if (key.Equals("layout", StringComparison.OrdinalIgnoreCase))
                        {
                            string v = value.ToLowerInvariant();
                            if (v == "landscape" || v == "portrait" || v == "auto")
                                config.Layout = v;
                        }
                    }
                }
            }
            catch { }

            if (config.Port < 1 || config.Port > 65535)
                config.Port = 8686;

            return config;
        }

        public void Save()
        {
            string target = _path;
            if (string.IsNullOrEmpty(target))
                target = Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "LanPerfMon.ini");

            try
            {
                WriteTo(target);
            }
            catch
            {
                try
                {
                    string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LanPerfMon");
                    Directory.CreateDirectory(dir);
                    target = Path.Combine(dir, "LanPerfMon.ini");
                    WriteTo(target);
                    _path = target;
                }
                catch { }
            }
        }

        private void WriteTo(string path)
        {
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            string text = "# LAN PerfMon configuration" + Environment.NewLine +
                          "port=" + Port.ToString(CultureInfo.InvariantCulture) + Environment.NewLine +
                          "lastport=" + LastPort.ToString(CultureInfo.InvariantCulture) + Environment.NewLine +
                          "autostart=" + (AutoStart ? "1" : "0") + Environment.NewLine +
                          "layout=" + Layout + Environment.NewLine;

            string temp = path + ".tmp";
            File.WriteAllText(temp, text, System.Text.Encoding.UTF8);
            if (File.Exists(path))
                File.Delete(path);
            File.Move(temp, path);
        }
    }
}
