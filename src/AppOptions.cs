using System;
using System.Globalization;

namespace LanPerfMon
{
    internal sealed class AppOptions
    {
        public int Port;
        public bool Minimized;
        public bool NoFps;
        public bool NoFirewall;
        public bool LocalOnly;

        public AppOptions()
        {
            Port = 0;
        }

        public static AppOptions Parse(string[] args)
        {
            AppOptions options = new AppOptions();
            if (args == null)
                return options;

            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i] == null ? string.Empty : args[i].Trim();
                if (a.Equals("--minimized", StringComparison.OrdinalIgnoreCase) ||
                    a.Equals("-min", StringComparison.OrdinalIgnoreCase))
                {
                    options.Minimized = true;
                }
                else if (a.Equals("--no-fps", StringComparison.OrdinalIgnoreCase))
                {
                    options.NoFps = true;
                }
                else if (a.Equals("--no-firewall", StringComparison.OrdinalIgnoreCase))
                {
                    options.NoFirewall = true;
                }
                else if (a.Equals("--local", StringComparison.OrdinalIgnoreCase))
                {
                    options.LocalOnly = true;
                }
                else if (a.Equals("--port", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    int p;
                    if (int.TryParse(args[++i], NumberStyles.Integer, CultureInfo.InvariantCulture, out p))
                        options.Port = p;
                }
                else if (a.StartsWith("--port=", StringComparison.OrdinalIgnoreCase))
                {
                    int p;
                    if (int.TryParse(a.Substring(7), NumberStyles.Integer, CultureInfo.InvariantCulture, out p))
                        options.Port = p;
                }
            }

            if (options.Port != 0 && (options.Port < 1 || options.Port > 65535))
                options.Port = 0;

            return options;
        }
    }
}
