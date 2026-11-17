using System;

namespace LanPerfMon
{
    internal sealed class MonitorSnapshot
    {
        public bool Ok;
        public string Error = string.Empty;
        public DateTime Updated = DateTime.MinValue;

        public string CpuName = string.Empty;
        public double? CpuLoad;
        public double? CpuFrequency;
        public double? CpuFrequencyMax;
        public double? CpuVoltage;
        public double? CpuPower;
        public double? CpuTemperature;
        public int CpuCores;
        public int CpuThreads;

        public string GpuName = string.Empty;
        public double? GpuLoad;
        public double? GpuFrequency;
        public double? GpuMemoryFrequency;
        public double? GpuVoltage;
        public double? GpuPower;
        public double? GpuTemperature;
        public double? GpuFan;
        public double? VramUsedMb;
        public double? VramTotalMb;
        public double? VramPercent;

        public double? RamUsedGb;
        public double? RamTotalGb;
        public double? RamPercent;
        public double? RamFrequency;

        public bool CpuSensorsAvailable;
        public bool GpuSensorsAvailable;
        public bool RamSensorsAvailable;
    }

    internal sealed class VolumeState
    {
        public bool Available;
        public int Percent;
        public bool Mute;
        public string Error = string.Empty;
    }

    internal sealed class FpsSnapshot
    {
        public bool Available;
        public double Value;
        public double? OnePercentLow;
        public string App = string.Empty;
        public int ProcessId;
        public bool PresentMonRunning;
        public string Status = string.Empty;
    }
}
