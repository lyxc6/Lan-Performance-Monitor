using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace LanPerfMon
{
    internal static class StatsJson
    {
        public static string Build(AppHost host)
        {
            MonitorSnapshot monitor = host.Monitor.Current;
            FpsSnapshot fps = host.Fps.GetSnapshot();
            VolumeState volume;
            try { volume = host.Volume.Get(); }
            catch (Exception ex)
            {
                volume = new VolumeState();
                volume.Error = ex.Message;
            }

            StringBuilder sb = new StringBuilder(1500);
            sb.Append("{\"ok\":true,\"t\":");
            sb.Append(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"clock\":");
            JsonString(sb, DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture));
            sb.Append(",\"date\":");
            JsonString(sb, DateTime.Now.ToString("MM-dd", CultureInfo.InvariantCulture));
            sb.Append(",\"host\":");
            JsonString(sb, Environment.MachineName);
            sb.Append(",\"sensorOk\":");
            sb.Append(monitor.Ok ? "true" : "false");
            sb.Append(",\"sensorError\":");
            JsonString(sb, monitor.Error ?? string.Empty);

            sb.Append(",\"cpu\":{");
            sb.Append("\"name\":"); JsonString(sb, monitor.CpuName);
            sb.Append(",\"load\":"); Number(sb, monitor.CpuLoad, "0.0");
            sb.Append(",\"freq\":"); Number(sb, monitor.CpuFrequency, "0");
            sb.Append(",\"freqMax\":"); Number(sb, monitor.CpuFrequencyMax, "0");
            sb.Append(",\"voltage\":"); Number(sb, monitor.CpuVoltage, "0.00");
            sb.Append(",\"power\":"); Number(sb, monitor.CpuPower, "0.0");
            sb.Append(",\"temp\":"); Number(sb, monitor.CpuTemperature, "0.0");
            sb.Append(",\"cores\":"); sb.Append(monitor.CpuCores.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"threads\":"); sb.Append(monitor.CpuThreads.ToString(CultureInfo.InvariantCulture));
            sb.Append("}");

            sb.Append(",\"gpu\":{");
            sb.Append("\"name\":"); JsonString(sb, monitor.GpuName);
            sb.Append(",\"load\":"); Number(sb, monitor.GpuLoad, "0.0");
            sb.Append(",\"freq\":"); Number(sb, monitor.GpuFrequency, "0");
            sb.Append(",\"memFreq\":"); Number(sb, monitor.GpuMemoryFrequency, "0");
            sb.Append(",\"voltage\":"); Number(sb, monitor.GpuVoltage, "0.00");
            sb.Append(",\"power\":"); Number(sb, monitor.GpuPower, "0.0");
            sb.Append(",\"temp\":"); Number(sb, monitor.GpuTemperature, "0.0");
            sb.Append(",\"fan\":"); Number(sb, monitor.GpuFan, "0");
            sb.Append(",\"vramUsed\":"); Number(sb, monitor.VramUsedMb, "0");
            sb.Append(",\"vramTotal\":"); Number(sb, monitor.VramTotalMb, "0");
            sb.Append(",\"vramPercent\":"); Number(sb, monitor.VramPercent, "0.0");
            sb.Append("}");

            sb.Append(",\"ram\":{");
            sb.Append("\"used\":"); Number(sb, monitor.RamUsedGb, "0.0");
            sb.Append(",\"total\":"); Number(sb, monitor.RamTotalGb, "0.0");
            sb.Append(",\"percent\":"); Number(sb, monitor.RamPercent, "0.0");
            sb.Append(",\"freq\":"); Number(sb, monitor.RamFrequency, "0");
            sb.Append("}");

            sb.Append(",\"fps\":{");
            sb.Append("\"available\":"); sb.Append(fps.Available ? "true" : "false");
            sb.Append(",\"value\":"); Number(sb, fps.Value, "0.0");
            sb.Append(",\"onePercentLow\":"); Number(sb, fps.OnePercentLow, "0.0");
            sb.Append(",\"app\":"); JsonString(sb, fps.App);
            sb.Append(",\"pid\":"); sb.Append(fps.ProcessId.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"presentmon\":"); sb.Append(fps.PresentMonRunning ? "true" : "false");
            sb.Append(",\"status\":"); JsonString(sb, fps.Status);
            sb.Append("}");

            sb.Append(",\"volume\":{");
            sb.Append("\"available\":"); sb.Append(volume.Available ? "true" : "false");
            sb.Append(",\"value\":"); sb.Append(volume.Percent.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"mute\":"); sb.Append(volume.Mute ? "true" : "false");
            sb.Append(",\"error\":"); JsonString(sb, volume.Error ?? string.Empty);
            sb.Append("}");

            sb.Append(",\"sys\":{");
            sb.Append("\"elevated\":"); sb.Append(Native.IsElevated ? "true" : "false");
            sb.Append(",\"port\":"); sb.Append(host.Port.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"version\":"); JsonString(sb, AppHost.Version);
            sb.Append(",\"layout\":"); JsonString(sb, host.Layout ?? "auto");
            sb.Append(",\"uptime\":"); sb.Append(((long)(DateTime.UtcNow - host.StartedUtc).TotalSeconds).ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"ips\":[");
            List<string> ips = NetInfo.GetAllIPv4();
            for (int i = 0; i < ips.Count; i++)
            {
                if (i > 0) sb.Append(',');
                JsonString(sb, ips[i]);
            }
            sb.Append("]");
            sb.Append("}");

            sb.Append("}");
            return sb.ToString();
        }

        public static string BuildVolume(VolumeState volume)
        {
            StringBuilder sb = new StringBuilder(160);
            sb.Append("{\"ok\":");
            sb.Append(volume.Available ? "true" : "false");
            sb.Append(",\"available\":");
            sb.Append(volume.Available ? "true" : "false");
            sb.Append(",\"value\":");
            sb.Append(volume.Percent.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"mute\":");
            sb.Append(volume.Mute ? "true" : "false");
            sb.Append(",\"error\":");
            JsonString(sb, volume.Error ?? string.Empty);
            sb.Append("}");
            return sb.ToString();
        }

        public static string SimpleOk()
        {
            return "{\"ok\":true}";
        }

        public static void Quote(StringBuilder sb, string text)
        {
            JsonString(sb, text ?? string.Empty);
        }

        public static string Error(string message)
        {
            StringBuilder sb = new StringBuilder(120);
            sb.Append("{\"ok\":false,\"error\":");
            JsonString(sb, message ?? string.Empty);
            sb.Append("}");
            return sb.ToString();
        }

        private static void Number(StringBuilder sb, double? value, string format)
        {
            if (!value.HasValue)
            {
                sb.Append("null");
                return;
            }
            double v = value.Value;
            if (double.IsNaN(v) || double.IsInfinity(v))
            {
                sb.Append("null");
                return;
            }
            sb.Append(v.ToString(format, CultureInfo.InvariantCulture));
        }

        private static void JsonString(StringBuilder sb, string text)
        {
            if (text == null)
            {
                sb.Append("null");
                return;
            }
            sb.Append('"');
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < ' ')
                            sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else
                            sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }
    }
}
