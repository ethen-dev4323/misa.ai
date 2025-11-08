using MISA.Core.Services;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace MISA.Core.Engine
{
    public class ResourceManager
    {
        private readonly ConfigService _configService;
        private readonly LoggingService _loggingService;
        private readonly Timer _monitorTimer;
        private readonly PerformanceCounter? _cpuCounter;
        private readonly PerformanceCounter? _memoryCounter;
        private readonly object _lockObject = new object();

        private ResourceMetrics _currentMetrics;
        private bool _isMonitoring;
        private PowerPlan _currentPowerPlan;

        public event EventHandler<ResourceMetrics>? OnMetricsUpdated;
        public event EventHandler<string>? OnResourceWarning;

        public ResourceManager(ConfigService configService, LoggingService loggingService)
        {
            _configService = configService ?? throw new ArgumentNullException(nameof(configService));
            _loggingService = loggingService ?? throw new ArgumentNullException(nameof(loggingService));

            try
            {
                // Initialize performance counters
                _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
                _memoryCounter = new PerformanceCounter("Memory", "Available MBytes");

                _currentMetrics = new ResourceMetrics();
                _monitorTimer = new Timer(MonitorResources, null, TimeSpan.Zero, TimeSpan.FromSeconds(5));

                _isMonitoring = true;
                _loggingService.LogInformation("Resource Manager initialized with performance monitoring");
            }
            catch (Exception ex)
            {
                _loggingService.LogWarning(ex, "Performance counters not available, using alternative methods");
                _monitorTimer = new Timer(MonitorResourcesAlternative, null, TimeSpan.Zero, TimeSpan.FromSeconds(5));
            }

            DetectHardwareCapabilities();
        }

        public async Task InitializeAsync()
        {
            try
            {
                _loggingService.LogInformation("Initializing Resource Manager...");

                // Detect GPU capabilities
                await DetectGPUCapabilitiesAsync();

                // Set optimal resource limits
                SetOptimalResourceLimits();

                // Initialize power management
                InitializePowerManagement();

                _loggingService.LogInformation("Resource Manager initialized successfully");
            }
            catch (Exception ex)
            {
                _loggingService.LogError(ex, "Failed to initialize Resource Manager");
                throw;
            }
        }

        private void DetectHardwareCapabilities()
        {
            try
            {
                // CPU Information
                _currentMetrics.CpuCores = Environment.ProcessorCount;
                _currentMetrics.CpuName = GetProcessorName();

                // Memory Information
                _currentMetrics.TotalMemoryGB = GetTotalMemoryGB();
                _currentMetrics.TotalDiskSpaceGB = GetTotalDiskSpaceGB();

                // GPU Information
                _currentMetrics.HasNvidiaGPU = DetectNvidiaGPU();
                _currentMetrics.HasAMDGPU = DetectAMDGPU();
                _currentMetrics.HasIntelGPU = DetectIntelGPU();

                _loggingService.LogInformation($"Hardware detected: {_currentMetrics.CpuName}, {_currentMetrics.CpuCores} cores, {_currentMetrics.TotalMemoryGB}GB RAM, GPU: {(HasGPU() ? "Yes" : "No")}");
            }
            catch (Exception ex)
            {
                _loggingService.LogWarning(ex, "Failed to detect some hardware capabilities");
            }
        }

        private async Task DetectGPUCapabilitiesAsync()
        {
            try
            {
                // Check for CUDA availability
                _currentMetrics.HasCUDA = await CheckCUDAAvailabilityAsync();

                if (_currentMetrics.HasCUDA)
                {
                    _currentMetrics.CUDAVersion = await GetCUDAVersionAsync();
                    _currentMetrics.GPUMemoryGB = await GetGPUMemoryGBAsync();
                    _loggingService.LogInformation($"NVIDIA GPU detected: {_currentMetrics.GPUMemoryGB}GB VRAM, CUDA {_currentMetrics.CUDAVersion}");
                }

                // Check for ROCm (AMD GPU computing)
                _currentMetrics.HasROCm = await CheckROCAvailabilityAsync();

                // Check for OpenCL
                _currentMetrics.HasOpenCL = await CheckOpenCLAvailabilityAsync();
            }
            catch (Exception ex)
            {
                _loggingService.LogWarning(ex, "Failed to detect GPU capabilities");
            }
        }

        private void SetOptimalResourceLimits()
        {
            try
            {
                var maxMemoryUsage = _configService.GetValue<int>("Resources.MaxMemoryUsageGB", 8);
                var maxCpuUsage = _configService.GetValue<int>("Resources.MaxCpuUsagePercent", 50);

                // Adjust limits based on available resources
                if (_currentMetrics.TotalMemoryGB < 16)
                {
                    maxMemoryUsage = (int)(_currentMetrics.TotalMemoryGB * 0.6);
                    OnResourceWarning?.Invoke(this, $"Limited memory detected. Adjusting max usage to {maxMemoryUsage}GB");
                }

                if (!_currentMetrics.HasCUDA && !_currentMetrics.HasROCm)
                {
                    _loggingService.LogInformation("No GPU acceleration available, will use CPU-only mode");
                }

                _currentMetrics.MaxMemoryUsageGB = Math.Min(maxMemoryUsage, _currentMetrics.TotalMemoryGB * 0.8);
                _currentMetrics.MaxCpuUsagePercent = maxCpuUsage;

                _loggingService.LogInformation($"Resource limits set: Max Memory: {_currentMetrics.MaxMemoryUsageGB}GB, Max CPU: {_currentMetrics.MaxCpuUsagePercent}%");
            }
            catch (Exception ex)
            {
                _loggingService.LogWarning(ex, "Failed to set optimal resource limits");
            }
        }

        private void InitializePowerManagement()
        {
            try
            {
                _currentPowerPlan = GetCurrentPowerPlan();
                _currentMetrics.IsOnBattery = IsRunningOnBattery();
                _currentMetrics.ThermalThrottlingEnabled = _configService.GetValue<bool>("Resources.ThermalThrottling", true);

                _loggingService.LogInformation($"Power management initialized: Plan={_currentPowerPlan}, OnBattery={_currentMetrics.IsOnBattery}");
            }
            catch (Exception ex)
            {
                _loggingService.LogWarning(ex, "Failed to initialize power management");
            }
        }

        private void MonitorResources(object? state)
        {
            if (!_isMonitoring) return;

            try
            {
                lock (_lockObject)
                {
                    // Update CPU usage
                    if (_cpuCounter != null)
                    {
                        _currentMetrics.CpuUsagePercent = _cpuCounter.NextValue();
                    }

                    // Update memory usage
                    if (_memoryCounter != null)
                    {
                        var availableMemoryMB = _memoryCounter.NextValue();
                        _currentMetrics.AvailableMemoryGB = availableMemoryMB / 1024.0;
                        _currentMetrics.UsedMemoryGB = _currentMetrics.TotalMemoryGB - _currentMetrics.AvailableMemoryGB;
                        _currentMetrics.MemoryUsagePercent = (_currentMetrics.UsedMemoryGB / _currentMetrics.TotalMemoryGB) * 100;
                    }

                    // Update other metrics
                    _currentMetrics.IsOnBattery = IsRunningOnBattery();
                    _currentMetrics.TemperatureC = GetCPUTemperature();

                    // Check resource limits
                    CheckResourceLimits();
                }

                OnMetricsUpdated?.Invoke(this, _currentMetrics);
            }
            catch (Exception ex)
            {
                _loggingService.LogWarning(ex, "Error monitoring resources");
            }
        }

        private void MonitorResourcesAlternative(object? state)
        {
            if (!_isMonitoring) return;

            try
            {
                lock (_lockObject)
                {
                    // Alternative CPU usage calculation
                    _currentMetrics.CpuUsagePercent = CalculateCPUUsageAlternative();

                    // Alternative memory calculation
                    var gc = GC.GetTotalMemory(false);
                    _currentMetrics.UsedMemoryGB = gc / (1024.0 * 1024.0 * 1024.0);
                    _currentMetrics.MemoryUsagePercent = (_currentMetrics.UsedMemoryGB / _currentMetrics.TotalMemoryGB) * 100;

                    // Update power status
                    _currentMetrics.IsOnBattery = IsRunningOnBattery();
                }

                OnMetricsUpdated?.Invoke(this, _currentMetrics);
            }
            catch (Exception ex)
            {
                _loggingService.LogWarning(ex, "Error monitoring resources (alternative method)");
            }
        }

        private void CheckResourceLimits()
        {
            // Check CPU usage
            if (_currentMetrics.CpuUsagePercent > _currentMetrics.MaxCpuUsagePercent)
            {
                OnResourceWarning?.Invoke(this, $"High CPU usage: {_currentMetrics.CpuUsagePercent:F1}% (limit: {_currentMetrics.MaxCpuUsagePercent}%)");
            }

            // Check memory usage
            if (_currentMetrics.MemoryUsagePercent > 90)
            {
                OnResourceWarning?.Invoke(this, $"High memory usage: {_currentMetrics.MemoryUsagePercent:F1}%");
            }

            // Check temperature
            if (_currentMetrics.TemperatureC > 80 && _currentMetrics.ThermalThrottlingEnabled)
            {
                OnResourceWarning?.Invoke(this, $"High CPU temperature: {_currentMetrics.TemperatureC}°C - enabling thermal throttling");
            }

            // Check battery
            if (_currentMetrics.IsOnBattery && _currentMetrics.CpuUsagePercent > 70)
            {
                OnResourceWarning?.Invoke(this, "High CPU usage on battery detected - consider reducing performance");
            }
        }

        public bool ShouldThrottlePerformance()
        {
            return (_currentMetrics.IsOnBattery && _configService.GetValue<bool>("Resources.BatteryOptimization", true)) ||
                   (_currentMetrics.TemperatureC > 80 && _currentMetrics.ThermalThrottlingEnabled) ||
                   (_currentMetrics.MemoryUsagePercent > 90);
        }

        public PerformanceProfile GetRecommendedProfile()
        {
            if (ShouldThrottlePerformance())
            {
                return PerformanceProfile.PowerSaver;
            }

            if (_currentMetrics.HasCUDA && _currentMetrics.GPUMemoryGB >= 8)
            {
                return PerformanceProfile.HighPerformance;
            }

            if (_currentMetrics.TotalMemoryGB >= 16)
            {
                return PerformanceProfile.Balanced;
            }

            return PerformanceProfile.LowPower;
        }

        public void ApplyPowerSettings(PerformanceProfile profile)
        {
            try
            {
                switch (profile)
                {
                    case PerformanceProfile.HighPerformance:
                        SetHighPerformanceMode();
                        break;
                    case PerformanceProfile.Balanced:
                        SetBalancedMode();
                        break;
                    case PerformanceProfile.PowerSaver:
                        SetPowerSaverMode();
                        break;
                    case PerformanceProfile.LowPower:
                        SetLowPowerMode();
                        break;
                }

                _loggingService.LogInformation($"Applied power settings for profile: {profile}");
            }
            catch (Exception ex)
            {
                _loggingService.LogWarning(ex, $"Failed to apply power settings for profile: {profile}");
            }
        }

        public ResourceMetrics GetCurrentMetrics()
        {
            lock (_lockObject)
            {
                return new ResourceMetrics(_currentMetrics);
            }
        }

        public bool HasGPU()
        {
            return _currentMetrics.HasNvidiaGPU || _currentMetrics.HasAMDGPU || _currentMetrics.HasIntelGPU;
        }

        public bool CanAccelerateAI()
        {
            return _currentMetrics.HasCUDA || _currentMetrics.HasROCm;
        }

        public async Task<bool> IsHealthyAsync()
        {
            try
            {
                return _currentMetrics.MemoryUsagePercent < 95 &&
                       _currentMetrics.CpuUsagePercent < 95 &&
                       _currentMetrics.TemperatureC < 90;
            }
            catch
            {
                return false;
            }
        }

        private string GetProcessorName()
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
                return key?.GetValue("ProcessorNameString")?.ToString() ?? "Unknown CPU";
            }
            catch
            {
                return "Unknown CPU";
            }
        }

        private double GetTotalMemoryGB()
        {
            try
            {
                var gc = GC.GetTotalMemory(false);
                return Math.Round(gc / (1024.0 * 1024.0 * 1024.0) * 2, 1); // Rough estimate
            }
            catch
            {
                return 8.0; // Default assumption
            }
        }

        private double GetTotalDiskSpaceGB()
        {
            try
            {
                var drive = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory) ?? "C:");
                return Math.Round(drive.TotalSize / (1024.0 * 1024.0 * 1024.0), 1);
            }
            catch
            {
                return 100.0; // Default assumption
            }
        }

        private bool DetectNvidiaGPU()
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\NVIDIA Corporation\Global\Driver");
                return key != null;
            }
            catch
            {
                return false;
            }
        }

        private bool DetectAMDGPU()
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\AMD\ULPS");
                return key != null;
            }
            catch
            {
                return false;
            }
        }

        private bool DetectIntelGPU()
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Intel\Display\IGFX");
                return key != null;
            }
            catch
            {
                return false;
            }
        }

        private async Task<bool> CheckCUDAAvailabilityAsync()
        {
            try
            {
                var processInfo = new ProcessStartInfo
                {
                    FileName = "nvidia-smi.exe",
                    Arguments = "--query-gpu=name,memory.total --format=csv,noheader,nounits",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };

                using var process = Process.Start(processInfo);
                if (process != null)
                {
                    var output = await process.StandardOutput.ReadToEndAsync();
                    await process.WaitForExitAsync();
                    return process.ExitCode == 0 && !string.IsNullOrWhiteSpace(output);
                }
                return false;
            }
            catch
            {
                return false;
            }
        }

        private async Task<string> GetCUDAVersionAsync()
        {
            try
            {
                var processInfo = new ProcessStartInfo
                {
                    FileName = "nvcc.exe",
                    Arguments = "--version",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };

                using var process = Process.Start(processInfo);
                if (process != null)
                {
                    var output = await process.StandardOutput.ReadToEndAsync();
                    await process.WaitForExitAsync();

                    var match = System.Text.RegularExpressions.Regex.Match(output, @"release (\d+\.\d+)");
                    return match.Success ? match.Groups[1].Value : "Unknown";
                }
                return "Unknown";
            }
            catch
            {
                return "Unknown";
            }
        }

        private async Task<double> GetGPUMemoryGBAsync()
        {
            try
            {
                var processInfo = new ProcessStartInfo
                {
                    FileName = "nvidia-smi.exe",
                    Arguments = "--query-gpu=memory.total --format=csv,noheader,nounits",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };

                using var process = Process.Start(processInfo);
                if (process != null)
                {
                    var output = await process.StandardOutput.ReadToEndAsync();
                    await process.WaitForExitAsync();

                    if (double.TryParse(output.Trim(), out var memoryMB))
                    {
                        return Math.Round(memoryMB / 1024.0, 1);
                    }
                }
                return 0;
            }
            catch
            {
                return 0;
            }
        }

        private async Task<bool> CheckROCAvailabilityAsync()
        {
            try
            {
                var processInfo = new ProcessStartInfo
                {
                    FileName = "rocminfo",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };

                using var process = Process.Start(processInfo);
                if (process != null)
                {
                    await process.WaitForExitAsync();
                    return process.ExitCode == 0;
                }
                return false;
            }
            catch
            {
                return false;
            }
        }

        private async Task<bool> CheckOpenCLAvailabilityAsync()
        {
            try
            {
                var processInfo = new ProcessStartInfo
                {
                    FileName = "clinfo",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };

                using var process = Process.Start(processInfo);
                if (process != null)
                {
                    await process.WaitForExitAsync();
                    return process.ExitCode == 0;
                }
                return false;
            }
            catch
            {
                return false;
            }
        }

        private PowerPlan GetCurrentPowerPlan()
        {
            try
            {
                var processInfo = new ProcessStartInfo
                {
                    FileName = "powercfg.exe",
                    Arguments = "/getactivescheme",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };

                using var process = Process.Start(processInfo);
                if (process != null)
                {
                    var output = process.StandardOutput.ReadToEnd();
                    process.WaitForExit();

                    if (output.Contains("High performance"))
                        return PowerPlan.HighPerformance;
                    if (output.Contains("Power saver"))
                        return PowerPlan.PowerSaver;
                }
                return PowerPlan.Balanced;
            }
            catch
            {
                return PowerPlan.Balanced;
            }
        }

        private bool IsRunningOnBattery()
        {
            try
            {
                var powerStatus = SystemInformation.PowerStatus;
                return powerStatus.PowerLineStatus == PowerLineStatus.Offline;
            }
            catch
            {
                return false;
            }
        }

        private double GetCPUTemperature()
        {
            try
            {
                // This would require platform-specific implementations
                // For Windows, could use WMI or third-party libraries
                return 0; // Placeholder
            }
            catch
            {
                return 0;
            }
        }

        private double CalculateCPUUsageAlternative()
        {
            try
            {
                var startTime = DateTime.UtcNow;
                var startCpuUsage = Process.GetCurrentProcess().TotalProcessorTime.TotalMilliseconds;

                Task.Delay(100).Wait();

                var endTime = DateTime.UtcNow;
                var endCpuUsage = Process.GetCurrentProcess().TotalProcessorTime.TotalMilliseconds;

                var cpuUsedMs = endCpuUsage - startCpuUsage;
                var totalMsPassed = (endTime - startTime).TotalMilliseconds * Environment.ProcessorCount;

                return (cpuUsedMs / totalMsPassed) * 100;
            }
            catch
            {
                return 0;
            }
        }

        private void SetHighPerformanceMode()
        {
            try
            {
                var processInfo = new ProcessStartInfo
                {
                    FileName = "powercfg.exe",
                    Arguments = "/setactive SCHEME_MIN",
                    UseShellExecute = true
                };
                Process.Start(processInfo);
            }
            catch (Exception ex)
            {
                _loggingService.LogWarning(ex, "Failed to set high performance mode");
            }
        }

        private void SetBalancedMode()
        {
            try
            {
                var processInfo = new ProcessStartInfo
                {
                    FileName = "powercfg.exe",
                    Arguments = "/setactive SCHEME_BALANCED",
                    UseShellExecute = true
                };
                Process.Start(processInfo);
            }
            catch (Exception ex)
            {
                _loggingService.LogWarning(ex, "Failed to set balanced mode");
            }
        }

        private void SetPowerSaverMode()
        {
            try
            {
                var processInfo = new ProcessStartInfo
                {
                    FileName = "powercfg.exe",
                    Arguments = "/setactive SCHEME_MAX",
                    UseShellExecute = true
                };
                Process.Start(processInfo);
            }
            catch (Exception ex)
            {
                _loggingService.LogWarning(ex, "Failed to set power saver mode");
            }
        }

        private void SetLowPowerMode()
        {
            SetPowerSaverMode();
        }

        public void Dispose()
        {
            _isMonitoring = false;
            _monitorTimer?.Dispose();
            _cpuCounter?.Dispose();
            _memoryCounter?.Dispose();
        }
    }

    public class ResourceMetrics
    {
        public ResourceMetrics() { }

        public ResourceMetrics(ResourceMetrics other)
        {
            CpuUsagePercent = other.CpuUsagePercent;
            MemoryUsagePercent = other.MemoryUsagePercent;
            UsedMemoryGB = other.UsedMemoryGB;
            AvailableMemoryGB = other.AvailableMemoryGB;
            CpuCores = other.CpuCores;
            CpuName = other.CpuName;
            TotalMemoryGB = other.TotalMemoryGB;
            IsOnBattery = other.IsOnBattery;
            TemperatureC = other.TemperatureC;
            HasCUDA = other.HasCUDA;
            HasROCm = other.HasROCm;
            GPUMemoryGB = other.GPUMemoryGB;
            CUDAVersion = other.CUDAVersion;
        }

        public double CpuUsagePercent { get; set; }
        public double MemoryUsagePercent { get; set; }
        public double UsedMemoryGB { get; set; }
        public double AvailableMemoryGB { get; set; }
        public int CpuCores { get; set; }
        public string CpuName { get; set; } = string.Empty;
        public double TotalMemoryGB { get; set; }
        public double TotalDiskSpaceGB { get; set; }
        public bool IsOnBattery { get; set; }
        public double TemperatureC { get; set; }
        public bool HasNvidiaGPU { get; set; }
        public bool HasAMDGPU { get; set; }
        public bool HasIntelGPU { get; set; }
        public bool HasCUDA { get; set; }
        public bool HasROCm { get; set; }
        public bool HasOpenCL { get; set; }
        public double GPUMemoryGB { get; set; }
        public string CUDAVersion { get; set; } = string.Empty;
        public double MaxMemoryUsageGB { get; set; }
        public double MaxCpuUsagePercent { get; set; }
        public bool ThermalThrottlingEnabled { get; set; }
        public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
    }

    public enum PowerPlan
    {
        PowerSaver,
        Balanced,
        HighPerformance
    }

    public enum PerformanceProfile
    {
        LowPower,
        PowerSaver,
        Balanced,
        HighPerformance
    }
}