using System.Diagnostics;
using System.IO;
using System.Management;
using System.Reflection;
using System.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace VrcGroupGuardian.Infrastructure;

public interface IDiagnosticsService
{
    Task<SystemDiagnostics> GetSystemDiagnosticsAsync();
    Task<ApplicationDiagnostics> GetApplicationDiagnosticsAsync();
    Task<ServiceHealthCheck> CheckServiceHealthAsync();
    Task<string> GenerateDiagnosticReportAsync();
    void StartPerformanceMonitoring();
    void StopPerformanceMonitoring();
    IEnumerable<PerformanceCounter> GetActiveCounters();
}

public class DiagnosticsService : IDiagnosticsService, IDisposable
{
    private readonly ILogger _logger = Log.ForContext<DiagnosticsService>();
    private readonly IServiceProvider _serviceProvider;
    private readonly Dictionary<string, PerformanceCounter> _performanceCounters = new();
    private readonly Timer? _monitoringTimer;
    private readonly Process _currentProcess;
    private bool _monitoringEnabled;
    private readonly DiagnosticMetrics _metrics = new();

    public DiagnosticsService(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
        _currentProcess = Process.GetCurrentProcess();
        
        InitializePerformanceCounters();
        
        // Start monitoring timer (every 30 seconds)
        _monitoringTimer = new Timer(CollectMetrics, null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
        
        _logger.Information("DiagnosticsService initialized");
    }

    public async Task<SystemDiagnostics> GetSystemDiagnosticsAsync()
    {
        try
        {
            var systemInfo = new SystemDiagnostics
            {
                Timestamp = DateTime.UtcNow,
                MachineName = Environment.MachineName,
                UserName = Environment.UserName,
                OSVersion = Environment.OSVersion.ToString(),
                ProcessorCount = Environment.ProcessorCount,
                SystemDirectory = Environment.SystemDirectory,
                UserDomainName = Environment.UserDomainName,
                Is64BitOperatingSystem = Environment.Is64BitOperatingSystem,
                Is64BitProcess = Environment.Is64BitProcess,
                SystemPageSize = Environment.SystemPageSize
            };

            // Get memory information
            var memoryStatus = GetMemoryStatus();
            systemInfo.TotalPhysicalMemory = memoryStatus.TotalPhysical;
            systemInfo.AvailablePhysicalMemory = memoryStatus.AvailablePhysical;
            systemInfo.TotalVirtualMemory = memoryStatus.TotalVirtual;
            systemInfo.AvailableVirtualMemory = memoryStatus.AvailableVirtual;

            // Get CPU information
            systemInfo.CPUUsage = await GetCurrentCpuUsageAsync();
            
            // Get disk information
            systemInfo.DiskInfo = GetDiskInfo();
            
            // Get network information
            systemInfo.NetworkAdapters = GetNetworkAdapters();

            return systemInfo;
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Failed to collect system diagnostics");
            return new SystemDiagnostics { Timestamp = DateTime.UtcNow };
        }
    }

    public async Task<ApplicationDiagnostics> GetApplicationDiagnosticsAsync()
    {
        try
        {
            var appInfo = new ApplicationDiagnostics
            {
                Timestamp = DateTime.UtcNow,
                ApplicationVersion = Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "Unknown",
                RuntimeVersion = Environment.Version.ToString(),
                StartTime = _currentProcess.StartTime,
                Uptime = DateTime.Now - _currentProcess.StartTime,
                ProcessId = _currentProcess.Id,
                ThreadCount = _currentProcess.Threads.Count,
                HandleCount = _currentProcess.HandleCount,
                WorkingSet = _currentProcess.WorkingSet64,
                PrivateMemory = _currentProcess.PrivateMemorySize64,
                VirtualMemory = _currentProcess.VirtualMemorySize64,
                PagedMemory = _currentProcess.PagedMemorySize64,
                NonPagedMemory = _currentProcess.NonpagedSystemMemorySize64,
                GCTotalMemory = GC.GetTotalMemory(false),
                GCMaxGeneration = GC.MaxGeneration
            };

            // Garbage collection information
            for (int i = 0; i <= GC.MaxGeneration; i++)
            {
                appInfo.GCCollectionCounts[i] = GC.CollectionCount(i);
            }

            // Assembly information
            var loadedAssemblies = AppDomain.CurrentDomain.GetAssemblies();
            appInfo.LoadedAssemblies = loadedAssemblies.Select(a => new AssemblyInfo
            {
                Name = a.GetName().Name ?? "Unknown",
                Version = a.GetName().Version?.ToString() ?? "Unknown",
                Location = GetAssemblyLocation(a),
                IsGAC = a.GlobalAssemblyCache,
                IsDynamic = a.IsDynamic
            }).ToList();

            // Get performance metrics
            if (_performanceCounters.Count > 0)
            {
                appInfo.PerformanceMetrics = _performanceCounters.ToDictionary(
                    kvp => kvp.Key,
                    kvp => GetCounterValue(kvp.Value)
                );
            }

            return appInfo;
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Failed to collect application diagnostics");
            return new ApplicationDiagnostics { Timestamp = DateTime.UtcNow };
        }
    }

    public async Task<ServiceHealthCheck> CheckServiceHealthAsync()
    {
        var healthCheck = new ServiceHealthCheck
        {
            Timestamp = DateTime.UtcNow,
            OverallHealth = ServiceHealth.Healthy,
            Checks = new List<IndividualHealthCheck>()
        };

        try
        {
            // Check memory usage
            var memoryCheck = CheckMemoryHealth();
            healthCheck.Checks.Add(memoryCheck);
            
            // Check database/storage connectivity
            var storageCheck = await CheckStorageHealthAsync();
            healthCheck.Checks.Add(storageCheck);
            
            // Check network connectivity
            var networkCheck = await CheckNetworkHealthAsync();
            healthCheck.Checks.Add(networkCheck);
            
            // Check service dependencies
            var dependencyCheck = await CheckServiceDependenciesAsync();
            healthCheck.Checks.Add(dependencyCheck);

            // Determine overall health
            if (healthCheck.Checks.Any(c => c.Status == ServiceHealth.Critical))
            {
                healthCheck.OverallHealth = ServiceHealth.Critical;
            }
            else if (healthCheck.Checks.Any(c => c.Status == ServiceHealth.Warning))
            {
                healthCheck.OverallHealth = ServiceHealth.Warning;
            }

            return healthCheck;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to perform health check");
            healthCheck.OverallHealth = ServiceHealth.Critical;
            healthCheck.Checks.Add(new IndividualHealthCheck
            {
                Name = "Health Check System",
                Status = ServiceHealth.Critical,
                Message = $"Health check system failed: {ex.Message}",
                Duration = TimeSpan.Zero
            });
            
            return healthCheck;
        }
    }

    public async Task<string> GenerateDiagnosticReportAsync()
    {
        var report = new System.Text.StringBuilder();
        
        try
        {
            report.AppendLine("=== VRC Group Guardian Diagnostic Report ===");
            report.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            report.AppendLine();

            // System Information
            var systemDiagnostics = await GetSystemDiagnosticsAsync();
            report.AppendLine("=== System Information ===");
            report.AppendLine($"Machine Name: {systemDiagnostics.MachineName}");
            report.AppendLine($"OS Version: {systemDiagnostics.OSVersion}");
            report.AppendLine($"Processor Count: {systemDiagnostics.ProcessorCount}");
            report.AppendLine($"64-bit OS: {systemDiagnostics.Is64BitOperatingSystem}");
            report.AppendLine($"64-bit Process: {systemDiagnostics.Is64BitProcess}");
            report.AppendLine($"Total Physical Memory: {FormatBytes(systemDiagnostics.TotalPhysicalMemory)}");
            report.AppendLine($"Available Physical Memory: {FormatBytes(systemDiagnostics.AvailablePhysicalMemory)}");
            report.AppendLine($"CPU Usage: {systemDiagnostics.CPUUsage:F1}%");
            report.AppendLine();

            // Application Information
            var appDiagnostics = await GetApplicationDiagnosticsAsync();
            report.AppendLine("=== Application Information ===");
            report.AppendLine($"Version: {appDiagnostics.ApplicationVersion}");
            report.AppendLine($"Runtime Version: {appDiagnostics.RuntimeVersion}");
            report.AppendLine($"Start Time: {appDiagnostics.StartTime:yyyy-MM-dd HH:mm:ss}");
            report.AppendLine($"Uptime: {appDiagnostics.Uptime}");
            report.AppendLine($"Process ID: {appDiagnostics.ProcessId}");
            report.AppendLine($"Thread Count: {appDiagnostics.ThreadCount}");
            report.AppendLine($"Handle Count: {appDiagnostics.HandleCount}");
            report.AppendLine($"Working Set: {FormatBytes(appDiagnostics.WorkingSet)}");
            report.AppendLine($"Private Memory: {FormatBytes(appDiagnostics.PrivateMemory)}");
            report.AppendLine($"GC Total Memory: {FormatBytes(appDiagnostics.GCTotalMemory)}");
            report.AppendLine();

            // Garbage Collection Information
            report.AppendLine("=== Garbage Collection ===");
            foreach (var gc in appDiagnostics.GCCollectionCounts)
            {
                report.AppendLine($"Gen {gc.Key} Collections: {gc.Value}");
            }
            report.AppendLine();

            // Health Check
            var healthCheck = await CheckServiceHealthAsync();
            report.AppendLine("=== Health Check ===");
            report.AppendLine($"Overall Health: {healthCheck.OverallHealth}");
            foreach (var check in healthCheck.Checks)
            {
                report.AppendLine($"  {check.Name}: {check.Status} - {check.Message} ({check.Duration.TotalMilliseconds:F0}ms)");
            }
            report.AppendLine();

            // Performance Counters
            if (_performanceCounters.Count > 0)
            {
                report.AppendLine("=== Performance Counters ===");
                foreach (var counter in _performanceCounters)
                {
                    var value = GetCounterValue(counter.Value);
                    report.AppendLine($"{counter.Key}: {value:F2}");
                }
                report.AppendLine();
            }

            // Recent Log Entries (if available)
            report.AppendLine("=== Recent Log Summary ===");
            report.AppendLine("Check application logs for detailed information");
            report.AppendLine();

            return report.ToString();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to generate diagnostic report");
            return $"Failed to generate diagnostic report: {ex.Message}";
        }
    }

    public void StartPerformanceMonitoring()
    {
        _monitoringEnabled = true;
        _logger.Information("Performance monitoring started");
    }

    public void StopPerformanceMonitoring()
    {
        _monitoringEnabled = false;
        _logger.Information("Performance monitoring stopped");
    }

    public IEnumerable<PerformanceCounter> GetActiveCounters()
    {
        return _performanceCounters.Values;
    }

    private void InitializePerformanceCounters()
    {
        try
        {
            var processName = _currentProcess.ProcessName;
            
            _performanceCounters["CPU Usage"] = new PerformanceCounter("Process", "% Processor Time", processName);
            _performanceCounters["Working Set"] = new PerformanceCounter("Process", "Working Set", processName);
            _performanceCounters["Private Bytes"] = new PerformanceCounter("Process", "Private Bytes", processName);
            _performanceCounters["Thread Count"] = new PerformanceCounter("Process", "Thread Count", processName);
            _performanceCounters["Handle Count"] = new PerformanceCounter("Process", "Handle Count", processName);
            
            _logger.Debug("Performance counters initialized for process: {ProcessName}", processName);
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Failed to initialize performance counters");
        }
    }

    private void CollectMetrics(object? state)
    {
        if (!_monitoringEnabled)
            return;

        try
        {
            foreach (var counter in _performanceCounters)
            {
                var value = GetCounterValue(counter.Value);
                _metrics.RecordMetric(counter.Key, value);
            }

            // Collect GC metrics
            _metrics.RecordMetric("GC Total Memory", GC.GetTotalMemory(false));
            _metrics.RecordMetric("GC Gen 0 Collections", GC.CollectionCount(0));
            _metrics.RecordMetric("GC Gen 1 Collections", GC.CollectionCount(1));
            _metrics.RecordMetric("GC Gen 2 Collections", GC.CollectionCount(2));
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "Error collecting performance metrics");
        }
    }

    private float GetCounterValue(PerformanceCounter counter)
    {
        try
        {
            return counter.NextValue();
        }
        catch
        {
            return 0f;
        }
    }

    private MemoryStatus GetMemoryStatus()
    {
        try
        {
            var memStatus = new MemoryStatus();
            
            // Use Environment class for basic memory information
            memStatus.TotalPhysical = GC.GetTotalMemory(false);
            memStatus.AvailablePhysical = memStatus.TotalPhysical; // Approximation
            
            return memStatus;
        }
        catch
        {
            return new MemoryStatus();
        }
    }

    private async Task<float> GetCurrentCpuUsageAsync()
    {
        try
        {
            if (_performanceCounters.TryGetValue("CPU Usage", out var cpuCounter))
            {
                // First call always returns 0, so we need two calls
                cpuCounter.NextValue();
                await Task.Delay(100);
                return cpuCounter.NextValue();
            }
            
            return 0f;
        }
        catch
        {
            return 0f;
        }
    }

    private List<DiskInfo> GetDiskInfo()
    {
        var diskInfos = new List<DiskInfo>();
        
        try
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                if (drive.IsReady)
                {
                    diskInfos.Add(new DiskInfo
                    {
                        Name = drive.Name,
                        TotalSize = drive.TotalSize,
                        FreeSpace = drive.AvailableFreeSpace,
                        DriveType = drive.DriveType.ToString()
                    });
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "Failed to get disk information");
        }
        
        return diskInfos;
    }

    private List<NetworkAdapterInfo> GetNetworkAdapters()
    {
        var adapters = new List<NetworkAdapterInfo>();
        
        try
        {
            var interfaces = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces();
            foreach (var adapter in interfaces)
            {
                adapters.Add(new NetworkAdapterInfo
                {
                    Name = adapter.Name,
                    Description = adapter.Description,
                    Status = adapter.OperationalStatus.ToString(),
                    Speed = adapter.Speed,
                    NetworkInterfaceType = adapter.NetworkInterfaceType.ToString()
                });
            }
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "Failed to get network adapter information");
        }
        
        return adapters;
    }

    private IndividualHealthCheck CheckMemoryHealth()
    {
        var stopwatch = Stopwatch.StartNew();
        
        try
        {
            var workingSet = _currentProcess.WorkingSet64;
            var privateMemory = _currentProcess.PrivateMemorySize64;
            
            // Consider memory usage above 500MB as warning, above 1GB as critical
            var status = ServiceHealth.Healthy;
            var message = $"Working Set: {FormatBytes(workingSet)}, Private: {FormatBytes(privateMemory)}";
            
            if (privateMemory > 1024 * 1024 * 1024) // 1GB
            {
                status = ServiceHealth.Critical;
                message = $"High memory usage: {message}";
            }
            else if (privateMemory > 512 * 1024 * 1024) // 512MB
            {
                status = ServiceHealth.Warning;
                message = $"Elevated memory usage: {message}";
            }
            
            return new IndividualHealthCheck
            {
                Name = "Memory Usage",
                Status = status,
                Message = message,
                Duration = stopwatch.Elapsed
            };
        }
        catch (Exception ex)
        {
            return new IndividualHealthCheck
            {
                Name = "Memory Usage",
                Status = ServiceHealth.Critical,
                Message = $"Memory check failed: {ex.Message}",
                Duration = stopwatch.Elapsed
            };
        }
    }

    private async Task<IndividualHealthCheck> CheckStorageHealthAsync()
    {
        var stopwatch = Stopwatch.StartNew();
        
        try
        {
            var settingsStore = _serviceProvider.GetService<ISettingsStore>();
            if (settingsStore != null)
            {
                await settingsStore.GetSettingAsync("HealthCheck", "test");
                
                return new IndividualHealthCheck
                {
                    Name = "Storage",
                    Status = ServiceHealth.Healthy,
                    Message = "Storage accessible",
                    Duration = stopwatch.Elapsed
                };
            }
            
            return new IndividualHealthCheck
            {
                Name = "Storage",
                Status = ServiceHealth.Warning,
                Message = "Storage service not available",
                Duration = stopwatch.Elapsed
            };
        }
        catch (Exception ex)
        {
            return new IndividualHealthCheck
            {
                Name = "Storage",
                Status = ServiceHealth.Critical,
                Message = $"Storage check failed: {ex.Message}",
                Duration = stopwatch.Elapsed
            };
        }
    }

    private async Task<IndividualHealthCheck> CheckNetworkHealthAsync()
    {
        var stopwatch = Stopwatch.StartNew();
        
        try
        {
            using var ping = new System.Net.NetworkInformation.Ping();
            var reply = await ping.SendPingAsync("8.8.8.8", 5000);
            
            if (reply.Status == System.Net.NetworkInformation.IPStatus.Success)
            {
                return new IndividualHealthCheck
                {
                    Name = "Network Connectivity",
                    Status = ServiceHealth.Healthy,
                    Message = $"Network accessible (RTT: {reply.RoundtripTime}ms)",
                    Duration = stopwatch.Elapsed
                };
            }
            else
            {
                return new IndividualHealthCheck
                {
                    Name = "Network Connectivity",
                    Status = ServiceHealth.Warning,
                    Message = $"Network issues detected: {reply.Status}",
                    Duration = stopwatch.Elapsed
                };
            }
        }
        catch (Exception ex)
        {
            return new IndividualHealthCheck
            {
                Name = "Network Connectivity",
                Status = ServiceHealth.Critical,
                Message = $"Network check failed: {ex.Message}",
                Duration = stopwatch.Elapsed
            };
        }
    }

    private async Task<IndividualHealthCheck> CheckServiceDependenciesAsync()
    {
        var stopwatch = Stopwatch.StartNew();
        
        try
        {
            // Check if critical services are available
            var authService = _serviceProvider.GetService<Services.Auth.IAuthService>();
            var cacheService = _serviceProvider.GetService<ICacheService>();
            
            var issues = new List<string>();
            
            if (authService == null)
                issues.Add("AuthService");
            if (cacheService == null)
                issues.Add("CacheService");
            
            if (issues.Count == 0)
            {
                return new IndividualHealthCheck
                {
                    Name = "Service Dependencies",
                    Status = ServiceHealth.Healthy,
                    Message = "All critical services available",
                    Duration = stopwatch.Elapsed
                };
            }
            else
            {
                return new IndividualHealthCheck
                {
                    Name = "Service Dependencies",
                    Status = ServiceHealth.Warning,
                    Message = $"Missing services: {string.Join(", ", issues)}",
                    Duration = stopwatch.Elapsed
                };
            }
        }
        catch (Exception ex)
        {
            return new IndividualHealthCheck
            {
                Name = "Service Dependencies",
                Status = ServiceHealth.Critical,
                Message = $"Dependency check failed: {ex.Message}",
                Duration = stopwatch.Elapsed
            };
        }
    }

    private string GetAssemblyLocation(Assembly assembly)
    {
        try
        {
            return assembly.IsDynamic ? "Dynamic" : assembly.Location;
        }
        catch
        {
            return "Unknown";
        }
    }

    private string FormatBytes(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB", "TB" };
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len = len / 1024;
        }
        return $"{len:0.##} {sizes[order]}";
    }

    public void Dispose()
    {
        _monitoringTimer?.Dispose();
        
        foreach (var counter in _performanceCounters.Values)
        {
            counter?.Dispose();
        }
        
        _performanceCounters.Clear();
        _currentProcess?.Dispose();
        
        _logger.Information("DiagnosticsService disposed");
    }
}

// Supporting classes and data structures
public class SystemDiagnostics
{
    public DateTime Timestamp { get; set; }
    public string MachineName { get; set; } = "";
    public string UserName { get; set; } = "";
    public string OSVersion { get; set; } = "";
    public int ProcessorCount { get; set; }
    public string SystemDirectory { get; set; } = "";
    public string UserDomainName { get; set; } = "";
    public bool Is64BitOperatingSystem { get; set; }
    public bool Is64BitProcess { get; set; }
    public int SystemPageSize { get; set; }
    public long TotalPhysicalMemory { get; set; }
    public long AvailablePhysicalMemory { get; set; }
    public long TotalVirtualMemory { get; set; }
    public long AvailableVirtualMemory { get; set; }
    public float CPUUsage { get; set; }
    public List<DiskInfo> DiskInfo { get; set; } = new();
    public List<NetworkAdapterInfo> NetworkAdapters { get; set; } = new();
}

public class ApplicationDiagnostics
{
    public DateTime Timestamp { get; set; }
    public string ApplicationVersion { get; set; } = "";
    public string RuntimeVersion { get; set; } = "";
    public DateTime StartTime { get; set; }
    public TimeSpan Uptime { get; set; }
    public int ProcessId { get; set; }
    public int ThreadCount { get; set; }
    public int HandleCount { get; set; }
    public long WorkingSet { get; set; }
    public long PrivateMemory { get; set; }
    public long VirtualMemory { get; set; }
    public long PagedMemory { get; set; }
    public long NonPagedMemory { get; set; }
    public long GCTotalMemory { get; set; }
    public int GCMaxGeneration { get; set; }
    public Dictionary<int, int> GCCollectionCounts { get; set; } = new();
    public List<AssemblyInfo> LoadedAssemblies { get; set; } = new();
    public Dictionary<string, float> PerformanceMetrics { get; set; } = new();
}

public class ServiceHealthCheck
{
    public DateTime Timestamp { get; set; }
    public ServiceHealth OverallHealth { get; set; }
    public List<IndividualHealthCheck> Checks { get; set; } = new();
}

public class IndividualHealthCheck
{
    public string Name { get; set; } = "";
    public ServiceHealth Status { get; set; }
    public string Message { get; set; } = "";
    public TimeSpan Duration { get; set; }
}

public class AssemblyInfo
{
    public string Name { get; set; } = "";
    public string Version { get; set; } = "";
    public string Location { get; set; } = "";
    public bool IsGAC { get; set; }
    public bool IsDynamic { get; set; }
}

public class DiskInfo
{
    public string Name { get; set; } = "";
    public long TotalSize { get; set; }
    public long FreeSpace { get; set; }
    public string DriveType { get; set; } = "";
}

public class NetworkAdapterInfo
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Status { get; set; } = "";
    public long Speed { get; set; }
    public string NetworkInterfaceType { get; set; } = "";
}

public class MemoryStatus
{
    public long TotalPhysical { get; set; }
    public long AvailablePhysical { get; set; }
    public long TotalVirtual { get; set; }
    public long AvailableVirtual { get; set; }
}

public enum ServiceHealth
{
    Healthy,
    Warning,
    Critical
}

public class DiagnosticMetrics
{
    private readonly Dictionary<string, List<float>> _metrics = new();
    private readonly object _lock = new object();

    public void RecordMetric(string name, float value)
    {
        lock (_lock)
        {
            if (!_metrics.TryGetValue(name, out var values))
            {
                values = new List<float>();
                _metrics[name] = values;
            }
            
            values.Add(value);
            
            // Keep only the last 100 values
            if (values.Count > 100)
            {
                values.RemoveAt(0);
            }
        }
    }

    public Dictionary<string, List<float>> GetAllMetrics()
    {
        lock (_lock)
        {
            return new Dictionary<string, List<float>>(_metrics);
        }
    }
}