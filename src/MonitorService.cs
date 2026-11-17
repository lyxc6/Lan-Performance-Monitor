using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using LibreHardwareMonitor.Hardware;

namespace LanPerfMon
{
    internal sealed class MonitorService : IDisposable
    {
        private Computer _computer;
        private IHardware _cpu;
        private IHardware _gpu;
        private IHardware _ram;

        private Thread _thread;
        private volatile bool _running;
        private volatile MonitorSnapshot _current = new MonitorSnapshot();
        private readonly CpuUsageMeter _usage = new CpuUsageMeter();
        private readonly RamFallback _ramFallback = new RamFallback();
        private DateTime _nextOpenAttempt = DateTime.MinValue;
        private double? _ramFrequencyMhz;   // SMBios memory speed, read once
        private string _lastError = string.Empty;
        private double _observedMaxFrequency;

        public MonitorSnapshot Current
        {
            get { return _current; }
        }

        public bool IsOpen
        {
            get { return _computer != null; }
        }

        public string LastError
        {
            get { return _lastError; }
        }

        public void Start()
        {
            if (_running)
                return;
            _running = true;
            _thread = new Thread(Loop);
            _thread.IsBackground = true;
            _thread.Name = "LanPerfMon.Sensors";
            _thread.Start();
        }

        private void Loop()
        {
            while (_running)
            {
                try
                {
                    if (_computer == null && DateTime.UtcNow >= _nextOpenAttempt)
                        OpenComputer();

                    if (_computer != null)
                        ReadSensors();
                }
                catch (Exception ex)
                {
                    _lastError = ex.Message;
                    SafeCloseComputer();
                    _nextOpenAttempt = DateTime.UtcNow.AddSeconds(10);
                }

                for (int i = 0; i < 10 && _running; i++)
                    Thread.Sleep(100);
            }
        }

        private void OpenComputer()
        {
            try
            {
                Computer computer = new Computer();
                computer.IsCpuEnabled = true;
                computer.IsGpuEnabled = true;
                computer.IsMemoryEnabled = true;
                computer.IsMotherboardEnabled = false;
                computer.IsControllerEnabled = false;
                computer.IsStorageEnabled = false;
                computer.IsNetworkEnabled = false;
                computer.IsPsuEnabled = false;
                computer.Open();

                _computer = computer;
                _cpu = FindHardware(computer, HardwareType.Cpu);
                _gpu = FindGpu(computer);
                _ram = FindRam(computer);
                _lastError = string.Empty;

                // Prime the CPU usage meter and sensors.
                _usage.Next();
                ReadSensors();
            }
            catch (Exception ex)
            {
                _lastError = ex.Message;
                SafeCloseComputer();
                _nextOpenAttempt = DateTime.UtcNow.AddSeconds(10);
            }
        }

        private static IHardware FindHardware(Computer computer, HardwareType type)
        {
            foreach (IHardware hw in computer.Hardware)
            {
                if (hw.HardwareType == type)
                    return hw;
            }
            return null;
        }

        private static IHardware FindGpu(Computer computer)
        {
            IHardware fallback = null;
            foreach (IHardware hw in computer.Hardware)
            {
                if (hw.HardwareType == HardwareType.GpuIntel ||
                    hw.HardwareType == HardwareType.GpuNvidia ||
                    hw.HardwareType == HardwareType.GpuAmd)
                {
                    if (fallback == null)
                        fallback = hw;
                    string name = hw.Name == null ? string.Empty : hw.Name;
                    if (name.IndexOf("Arc", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        name.IndexOf("B580", StringComparison.OrdinalIgnoreCase) >= 0)
                        return hw;
                }
            }
            return fallback;
        }

        private static IHardware FindRam(Computer computer)
        {
            IHardware fallback = null;
            foreach (IHardware hw in computer.Hardware)
            {
                if (hw.HardwareType != HardwareType.Memory)
                    continue;
                if (fallback == null)
                    fallback = hw;
                if (hw.Name != null && hw.Name.IndexOf("Virtual", StringComparison.OrdinalIgnoreCase) < 0)
                    return hw;
            }
            return fallback;
        }

        private void ReadSensors()
        {
            if (_computer == null)
                return;

            MonitorSnapshot snapshot = new MonitorSnapshot();
            snapshot.Ok = true;
            snapshot.Updated = DateTime.Now;

            try
            {
                UpdateHardware(_cpu);
                UpdateHardware(_gpu);
                UpdateHardware(_ram);
            }
            catch (Exception ex)
            {
                _lastError = ex.Message;
            }

            ReadCpu(snapshot);
            ReadGpu(snapshot);
            ReadRam(snapshot);

            snapshot.CpuThreads = Environment.ProcessorCount;
            if (snapshot.CpuThreads < 1)
                snapshot.CpuThreads = 1;

            _current = snapshot;
        }

        private static void UpdateHardware(IHardware hardware)
        {
            if (hardware == null)
                return;
            hardware.Update();
            if (hardware.SubHardware != null)
            {
                foreach (IHardware sub in hardware.SubHardware)
                {
                    try { sub.Update(); }
                    catch { }
                }
            }
        }

        private void ReadCpu(MonitorSnapshot snapshot)
        {
            if (_cpu == null)
                return;

            snapshot.CpuName = CleanName(_cpu.Name);
            snapshot.CpuLoad = Value(FindSensor(_cpu, SensorType.Load, "CPU Total"));
            if (!snapshot.CpuLoad.HasValue)
                snapshot.CpuLoad = _usage.Next();

            snapshot.CpuTemperature = FirstValue(_cpu, SensorType.Temperature,
                "CPU Package", "Core Max", "Core Average", "P-Core #1");
            if (!snapshot.CpuTemperature.HasValue)
                snapshot.CpuTemperature = FirstSensorValue(_cpu, SensorType.Temperature, "Distance");

            snapshot.CpuPower = FirstValue(_cpu, SensorType.Power,
                "CPU Package", "CPU Cores", "CPU Platform");
            if (snapshot.CpuPower.HasValue && snapshot.CpuPower.Value <= 0.01)
                snapshot.CpuPower = null;

            snapshot.CpuVoltage = FirstValue(_cpu, SensorType.Voltage,
                "CPU Core", "Vcore", "CPU VDD", "CPU Voltage");
            if (!snapshot.CpuVoltage.HasValue)
                snapshot.CpuVoltage = FirstSensorValue(_cpu, SensorType.Voltage, null);
            if (snapshot.CpuVoltage.HasValue && snapshot.CpuVoltage.Value <= 0.001)
                snapshot.CpuVoltage = null;

            double sum = 0;
            int count = 0;
            foreach (ISensor sensor in _cpu.Sensors)
            {
                if (sensor.SensorType != SensorType.Clock)
                    continue;
                string name = sensor.Name == null ? string.Empty : sensor.Name;
                if (name.IndexOf("Bus", StringComparison.OrdinalIgnoreCase) >= 0)
                    continue;
                double? value = Value(sensor);
                if (value.HasValue && value.Value > 0)
                {
                    sum += value.Value;
                    count++;
                    if (value.Value > _observedMaxFrequency)
                        _observedMaxFrequency = value.Value;
                }
            }

            double fallbackCurrent;
            double fallbackMax;
            CpuFrequency.Read(out fallbackCurrent, out fallbackMax);

            if (count > 0)
                snapshot.CpuFrequency = sum / count;
            else if (fallbackCurrent > 0)
                snapshot.CpuFrequency = fallbackCurrent;

            double maximum = fallbackMax > _observedMaxFrequency ? fallbackMax : _observedMaxFrequency;
            if (maximum > 0)
                snapshot.CpuFrequencyMax = maximum;
            else if (snapshot.CpuFrequency.HasValue)
                snapshot.CpuFrequencyMax = snapshot.CpuFrequency.Value * 1.2;

            snapshot.CpuCores = CountPhysicalCores(_cpu);
            if (snapshot.CpuCores < 1)
                snapshot.CpuCores = Environment.ProcessorCount;

            snapshot.CpuSensorsAvailable = snapshot.CpuLoad.HasValue ||
                                           snapshot.CpuFrequency.HasValue ||
                                           snapshot.CpuTemperature.HasValue ||
                                           snapshot.CpuPower.HasValue ||
                                           snapshot.CpuVoltage.HasValue;
        }

        private static int CountPhysicalCores(IHardware cpu)
        {
            Dictionary<string, bool> names = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            foreach (ISensor sensor in cpu.Sensors)
            {
                if (sensor.SensorType != SensorType.Load)
                    continue;
                string name = sensor.Name == null ? string.Empty : sensor.Name;
                if (!name.StartsWith("CPU Core #", StringComparison.OrdinalIgnoreCase))
                    continue;
                int thread = name.IndexOf(" Thread", StringComparison.OrdinalIgnoreCase);
                if (thread > 0)
                    name = name.Substring(0, thread);
                names[name] = true;
            }
            return names.Count;
        }

        private void ReadGpu(MonitorSnapshot snapshot)
        {
            if (_gpu == null)
                return;

            snapshot.GpuName = CleanName(_gpu.Name);
            snapshot.GpuLoad = FirstValue(_gpu, SensorType.Load, "GPU Core", "GPU Render/Compute", "D3D 3D");
            if (!snapshot.GpuLoad.HasValue)
                snapshot.GpuLoad = FirstSensorValue(_gpu, SensorType.Load, "Memory");

            snapshot.GpuFrequency = FirstValue(_gpu, SensorType.Clock, "GPU Core");
            snapshot.GpuMemoryFrequency = FirstValue(_gpu, SensorType.Clock, "GPU Memory");
            snapshot.GpuVoltage = FirstValue(_gpu, SensorType.Voltage, "GPU Core");
            snapshot.GpuPower = FirstValue(_gpu, SensorType.Power, "GPU Package", "GPU Power");
            snapshot.GpuTemperature = FirstValue(_gpu, SensorType.Temperature, "GPU Core", "GPU Hot Spot");
            snapshot.GpuFan = FirstValue(_gpu, SensorType.Fan, "GPU Fan");

            double? used = FirstValue(_gpu, SensorType.SmallData, "GPU Memory Used", "GPU Memory Allocated");
            double? total = FirstValue(_gpu, SensorType.SmallData, "GPU Memory Total");
            if (!used.HasValue)
                used = FirstValue(_gpu, SensorType.Data, "GPU Memory Used");
            if (!total.HasValue)
                total = FirstValue(_gpu, SensorType.Data, "GPU Memory Total");

            if (used.HasValue && used.Value > 0)
                snapshot.VramUsedMb = used;
            if (total.HasValue && total.Value > 0)
                snapshot.VramTotalMb = total;
            if (snapshot.VramUsedMb.HasValue && snapshot.VramTotalMb.HasValue && snapshot.VramTotalMb.Value > 0)
                snapshot.VramPercent = snapshot.VramUsedMb.Value * 100.0 / snapshot.VramTotalMb.Value;

            if (snapshot.GpuPower.HasValue && snapshot.GpuPower.Value <= 0.01)
                snapshot.GpuPower = null;
            if (snapshot.GpuVoltage.HasValue && snapshot.GpuVoltage.Value <= 0.001)
                snapshot.GpuVoltage = null;

            snapshot.GpuSensorsAvailable = snapshot.GpuLoad.HasValue ||
                                            snapshot.GpuFrequency.HasValue ||
                                            snapshot.GpuTemperature.HasValue ||
                                            snapshot.GpuPower.HasValue;
        }

        private void ReadRam(MonitorSnapshot snapshot)
        {
            if (_ram != null)
            {
                double? used = FirstValue(_ram, SensorType.Data, "Memory Used");
                double? available = FirstValue(_ram, SensorType.Data, "Memory Available");
                if (used.HasValue && available.HasValue && used.Value + available.Value > 0)
                {
                    snapshot.RamUsedGb = used;
                    snapshot.RamTotalGb = used.Value + available.Value;
                    snapshot.RamPercent = used.Value * 100.0 / snapshot.RamTotalGb.Value;
                }

                double? load = FirstValue(_ram, SensorType.Load, "Memory");
                if (load.HasValue)
                    snapshot.RamPercent = load;
            }

            if (!snapshot.RamUsedGb.HasValue || !snapshot.RamTotalGb.HasValue)
            {
                if (_ramFallback.Read())
                {
                    snapshot.RamUsedGb = _ramFallback.UsedGb;
                    snapshot.RamTotalGb = _ramFallback.TotalGb;
                    snapshot.RamPercent = _ramFallback.Percent;
                }
            }

            if (!_ramFrequencyMhz.HasValue)
            {
                double? speed = ReadRamFrequencyFromSmbios();
                if (speed.HasValue && speed.Value > 0)
                    _ramFrequencyMhz = speed;
            }
            snapshot.RamFrequency = _ramFrequencyMhz;

            snapshot.RamSensorsAvailable = snapshot.RamUsedGb.HasValue && snapshot.RamTotalGb.HasValue;
        }

        private double? ReadRamFrequencyFromSmbios()
        {
            try
            {
                if (_computer == null || _computer.SMBios == null)
                    return null;
                MemoryDevice[] devices = _computer.SMBios.MemoryDevices;
                if (devices == null)
                    return null;

                double best = 0;
                foreach (MemoryDevice device in devices)
                {
                    if (device == null)
                        continue;
                    // ConfiguredSpeed is the actual operating speed; fall back to rated Speed.
                    double mhz = device.ConfiguredSpeed > 0 ? device.ConfiguredSpeed : device.Speed;
                    if (mhz > best)
                        best = mhz;
                }
                return best > 0 ? (double?)best : null;
            }
            catch
            {
                return null;
            }
        }

        private static ISensor FindSensor(IHardware hardware, SensorType type, string name)
        {
            if (hardware == null)
                return null;
            foreach (ISensor sensor in hardware.Sensors)
            {
                if (sensor.SensorType == type && string.Equals(sensor.Name, name, StringComparison.OrdinalIgnoreCase))
                    return sensor;
            }
            return null;
        }

        private static double? FirstValue(IHardware hardware, SensorType type, params string[] names)
        {
            if (hardware == null)
                return null;
            for (int i = 0; i < names.Length; i++)
            {
                ISensor sensor = FindSensor(hardware, type, names[i]);
                double? value = Value(sensor);
                if (value.HasValue)
                    return value;
            }
            return null;
        }

        private static double? FirstSensorValue(IHardware hardware, SensorType type, string excludeWord)
        {
            if (hardware == null)
                return null;
            foreach (ISensor sensor in hardware.Sensors)
            {
                if (sensor.SensorType != type)
                    continue;
                string name = sensor.Name == null ? string.Empty : sensor.Name;
                if (excludeWord != null && name.IndexOf(excludeWord, StringComparison.OrdinalIgnoreCase) >= 0)
                    continue;
                double? value = Value(sensor);
                if (value.HasValue)
                    return value;
            }
            return null;
        }

        private static double? Value(ISensor sensor)
        {
            if (sensor == null || !sensor.Value.HasValue)
                return null;
            double value = sensor.Value.Value;
            if (double.IsNaN(value) || double.IsInfinity(value))
                return null;
            return value;
        }

        private static string CleanName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return string.Empty;
            return name.Replace("(R)", string.Empty)
                       .Replace("(TM)", string.Empty)
                       .Replace("  ", " ")
                       .Trim();
        }

        private void SafeCloseComputer()
        {
            if (_computer == null)
                return;
            try { _computer.Close(); }
            catch { }
            _computer = null;
            _cpu = null;
            _gpu = null;
            _ram = null;
        }

        public void Dispose()
        {
            _running = false;
            if (_thread != null)
            {
                try { _thread.Join(2000); }
                catch { }
                _thread = null;
            }
            SafeCloseComputer();
        }
    }
}
