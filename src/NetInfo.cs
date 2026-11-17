using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;

namespace LanPerfMon
{
    internal static class NetInfo
    {
        public static string GetLocalIPv4()
        {
            List<string> all = GetAllIPv4();
            if (all.Count == 0)
                return "127.0.0.1";
            return all[0];
        }

        public static List<string> GetAllIPv4()
        {
            List<string> result = new List<string>();
            List<string> fallback = new List<string>();

            try
            {
                NetworkInterface[] interfaces = NetworkInterface.GetAllNetworkInterfaces();
                for (int i = 0; i < interfaces.Length; i++)
                {
                    NetworkInterface nic = interfaces[i];
                    if (nic.OperationalStatus != OperationalStatus.Up)
                        continue;
                    if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback ||
                        nic.NetworkInterfaceType == NetworkInterfaceType.Tunnel)
                        continue;

                    string name = nic.Name == null ? string.Empty : nic.Name.ToLowerInvariant();
                    if (name.IndexOf("virtual", StringComparison.Ordinal) >= 0 ||
                        name.IndexOf("vmware", StringComparison.Ordinal) >= 0 ||
                        name.IndexOf("vethernet", StringComparison.Ordinal) >= 0 ||
                        name.IndexOf("vbox", StringComparison.Ordinal) >= 0)
                        continue;

                    IPInterfaceProperties properties;
                    try { properties = nic.GetIPProperties(); }
                    catch { continue; }

                    bool hasGateway = properties.GatewayAddresses != null && properties.GatewayAddresses.Count > 0;

                    foreach (UnicastIPAddressInformation address in properties.UnicastAddresses)
                    {
                        if (address.Address == null || address.Address.AddressFamily != AddressFamily.InterNetwork)
                            continue;
                        string text = address.Address.ToString();
                        if (text.StartsWith("127.", StringComparison.Ordinal) ||
                            text.StartsWith("169.254.", StringComparison.Ordinal))
                            continue;
                        if (!result.Contains(text))
                        {
                            if (hasGateway)
                                result.Add(text);
                            else if (!fallback.Contains(text))
                                fallback.Add(text);
                        }
                    }
                }
            }
            catch { }

            if (result.Count == 0)
                result.AddRange(fallback);
            return result;
        }

        public static string RuleName(int port)
        {
            return "LanPerfMon TCP " + port;
        }

        public static bool EnsureFirewallRule(int port, out string message)
        {
            message = string.Empty;
            if (!Native.IsElevated)
            {
                message = "当前不是管理员，未添加防火墙规则。";
                return false;
            }

            string name = RuleName(port);
            string output;
            int code = RunHidden("netsh.exe",
                "advfirewall firewall show rule name=\"" + name + "\"", out output);
            if (code == 0)
                return true;

            code = RunHidden("netsh.exe",
                "advfirewall firewall add rule name=\"" + name + "\" dir=in action=allow protocol=TCP localport=" +
                port.ToString() + " profile=any description=\"LAN PerfMon web UI\"", out output);
            if (code != 0)
                message = "防火墙规则添加失败：" + Shorten(output, 160);
            return code == 0;
        }

        public static void RemoveFirewallRule(int port)
        {
            try
            {
                string output;
                RunHidden("netsh.exe", "advfirewall firewall delete rule name=\"" + RuleName(port) + "\"", out output);
            }
            catch { }
        }

        public static bool SetAutoStart(bool enabled, out string message)
        {
            message = string.Empty;
            if (!Native.IsElevated)
            {
                message = "修改开机自启需要管理员权限。";
                return false;
            }

            try
            {
                if (enabled)
                {
                    string exe = System.Windows.Forms.Application.ExecutablePath;
                    string taskCommand = "\"" + exe + "\" --minimized";
                    string args = "/Create /TN \"LanPerfMon\" /TR \"" + taskCommand.Replace("\"", "\\\"") +
                                  "\" /SC ONLOGON /RL HIGHEST /F";
                    string output;
                    int code = RunHidden("schtasks.exe", args, out output);
                    if (code != 0)
                    {
                        message = "开机自启设置失败：" + Shorten(output, 180);
                        return false;
                    }
                }
                else
                {
                    string output;
                    int code = RunHidden("schtasks.exe", "/Delete /TN \"LanPerfMon\" /F", out output);
                    if (code != 0)
                    {
                        // Not existing is fine.
                        if (output.IndexOf("找不到", StringComparison.Ordinal) < 0 &&
                            output.IndexOf("cannot find", StringComparison.OrdinalIgnoreCase) < 0 &&
                            output.IndexOf("ERROR:", StringComparison.OrdinalIgnoreCase) < 0)
                        {
                            message = "取消开机自启失败：" + Shorten(output, 180);
                            return false;
                        }
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return false;
            }
        }

        public static int RunHidden(string fileName, string arguments, out string output)
        {
            output = string.Empty;
            try
            {
                ProcessStartInfo info = new ProcessStartInfo();
                info.FileName = fileName;
                info.Arguments = arguments;
                info.UseShellExecute = false;
                info.CreateNoWindow = true;
                info.RedirectStandardOutput = true;
                info.RedirectStandardError = true;
                info.StandardOutputEncoding = Encoding.UTF8;
                info.StandardErrorEncoding = Encoding.UTF8;

                using (Process process = Process.Start(info))
                {
                    if (process == null)
                        return -1;
                    string stdout = process.StandardOutput.ReadToEnd();
                    string stderr = process.StandardError.ReadToEnd();
                    if (!process.WaitForExit(6000))
                    {
                        try { process.Kill(); }
                        catch { }
                        output = "命令超时";
                        return -1;
                    }
                    output = (stdout + "\n" + stderr).Trim();
                    return process.ExitCode;
                }
            }
            catch (Exception ex)
            {
                output = ex.Message;
                return -1;
            }
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
    }
}
