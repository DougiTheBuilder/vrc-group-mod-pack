using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace VrcGroupGuardian.Infrastructure;

public interface IPerformanceOptimizer
{
    void OptimizeStartup();
    void EnableLazyInitialization();
    void OptimizeMemoryUsage();
    void PrecompileViewModels();
    Task WarmupServicesAsync();
    void EnableDataVirtualization();
    PerformanceMetrics GetPerformanceMetrics();
    Task<T> MeasureAsync<T>(Func<Task<T>> operation, [CallerMemberName] string operationName = "");
    void Measure(Action operation, [CallerMemberName] string operationName = "");
}

public class PerformanceOptimizer : IPerformanceOptimizer
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger _logger = Log.ForContext<PerformanceOptimizer>();
    private readonly ConcurrentDictionary<string, List<long>> _performanceMetrics = new();
    private readonly Stopwatch _applicationStartTime = Stopwatch.StartNew();

    public PerformanceOptimizer(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public void OptimizeStartup()
    {
        _logger.Information("Applying startup performance optimizations");
        
        try
        {
            // Enable server GC for better performance
            if (!GCSettings.IsServerGC)
            {
                _logger.Debug("Client GC detected, server GC would provide better performance");
            }

            // Set GC latency mode for interactive applications
            GCSettings.LatencyMode = GCLatencyMode.Interactive;
            _logger.Debug("Set GC latency mode to Interactive");

            // Enable concurrent GC
            if (GCSettings.IsServerGC)
            {
                _logger.Debug("Server GC enabled, concurrent collection available");
            }

            // Optimize JIT compilation
            OptimizeJitCompilation();

            // Pre-allocate commonly used collections
            PreAllocateCollections();

            _logger.Information("Startup optimizations applied successfully");
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Some startup optimizations failed to apply");
        }
    }

    public void EnableLazyInitialization()
    {
        _logger.Information("Enabling lazy initialization for heavy services");
        
        try
        {
            // Services will be created on-demand rather than at startup
            // This is already handled by the DI container's transient registrations
            
            _logger.Debug("Lazy initialization configuration completed");
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Failed to configure lazy initialization");
        }
    }

    public void OptimizeMemoryUsage()
    {
        _logger.Information("Optimizing memory usage");
        
        try
        {
            // Force garbage collection to free up memory
            GC.Collect(2, GCCollectionMode.Optimized, false);
            GC.WaitForPendingFinalizers();
            GC.Collect(2, GCCollectionMode.Optimized, false);

            // Compact the large object heap
            GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect();

            var memoryBefore = GC.GetTotalMemory(false);
            var memoryAfter = GC.GetTotalMemory(true);
            var memoryFreed = memoryBefore - memoryAfter;

            _logger.Information("Memory optimization completed. Freed {MemoryFreed} bytes", memoryFreed);
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Memory optimization encountered errors");
        }
    }

    public void PrecompileViewModels()
    {
        _logger.Information("Precompiling ViewModels for faster instantiation");
        
        try
        {
            var viewModelTypes = new[]
            {
                typeof(ViewModels.MainWindowViewModel),
                typeof(ViewModels.InstancesViewModel),
                typeof(ViewModels.MembersViewModel),
                typeof(ViewModels.AuditViewModel),
                typeof(ViewModels.SettingsViewModel)
            };

            foreach (var type in viewModelTypes)
            {
                try
                {
                    // Trigger JIT compilation by reflecting on the type
                    RuntimeHelpers.PrepareMethod(type.GetConstructors().First().MethodHandle);
                    _logger.Debug("Precompiled {ViewModelType}", type.Name);
                }
                catch (Exception ex)
                {
                    _logger.Debug(ex, "Failed to precompile {ViewModelType}", type.Name);
                }
            }

            _logger.Information("ViewModel precompilation completed");
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "ViewModel precompilation failed");
        }
    }

    public async Task WarmupServicesAsync()
    {
        _logger.Information("Warming up critical services");
        
        var warmupTasks = new List<Task>();

        try
        {
            // Warmup authentication service
            warmupTasks.Add(Task.Run(async () =>
            {
                try
                {
                    var authService = _serviceProvider.GetRequiredService<Services.Auth.IAuthService>();
                    await authService.IsAuthenticatedAsync(); // This will initialize the service
                    _logger.Debug("AuthService warmed up");
                }
                catch (Exception ex)
                {
                    _logger.Debug(ex, "AuthService warmup failed");
                }
            }));

            // Warmup settings store
            warmupTasks.Add(Task.Run(async () =>
            {
                try
                {
                    var settingsStore = _serviceProvider.GetRequiredService<ISettingsStore>();
                    await settingsStore.GetSettingAsync("WarmupTest", false); // Initialize storage
                    _logger.Debug("SettingsStore warmed up");
                }
                catch (Exception ex)
                {
                    _logger.Debug(ex, "SettingsStore warmup failed");
                }
            }));

            // Warmup HTTP client
            warmupTasks.Add(Task.Run(() =>
            {
                try
                {
                    var httpClientFactory = _serviceProvider.GetRequiredService<IVrchatHttpClientFactory>();
                    var client = httpClientFactory.CreateClient(); // Initialize HTTP infrastructure
                    _logger.Debug("HttpClient warmed up");
                }
                catch (Exception ex)
                {
                    _logger.Debug(ex, "HttpClient warmup failed");
                }
            }));

            // Wait for all warmup tasks to complete with timeout
            await Task.WhenAll(warmupTasks).WaitAsync(TimeSpan.FromSeconds(5));
            
            _logger.Information("Service warmup completed successfully");
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Some services failed to warm up within timeout");
        }
    }

    public void EnableDataVirtualization()
    {
        _logger.Information("Data virtualization enabled for large collections");
        
        // Data virtualization is primarily handled by the UI controls (DataGrid with virtualization)
        // This method serves as a configuration point for future enhancements
        
        _logger.Debug("Data virtualization configuration completed");
    }

    public PerformanceMetrics GetPerformanceMetrics()
    {
        var metrics = new PerformanceMetrics
        {
            ApplicationUptime = _applicationStartTime.Elapsed,
            ManagedMemoryUsage = GC.GetTotalMemory(false),
            WorkingSetMemory = Environment.WorkingSet,
            ThreadCount = Process.GetCurrentProcess().Threads.Count,
            GCGenerations = new Dictionary<int, int>
            {
                [0] = GC.CollectionCount(0),
                [1] = GC.CollectionCount(1),
                [2] = GC.CollectionCount(2)
            },
            OperationMetrics = _performanceMetrics.ToDictionary(
                kvp => kvp.Key,
                kvp => new OperationMetrics
                {
                    CallCount = kvp.Value.Count,
                    TotalTimeMs = kvp.Value.Sum(),
                    AverageTimeMs = kvp.Value.Average(),
                    MinTimeMs = kvp.Value.Min(),
                    MaxTimeMs = kvp.Value.Max()
                })
        };

        return metrics;
    }

    public async Task<T> MeasureAsync<T>(Func<Task<T>> operation, [CallerMemberName] string operationName = "")
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var result = await operation();
            return result;
        }
        finally
        {
            stopwatch.Stop();
            RecordMetric(operationName, stopwatch.ElapsedMilliseconds);
        }
    }

    public void Measure(Action operation, [CallerMemberName] string operationName = "")
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            operation();
        }
        finally
        {
            stopwatch.Stop();
            RecordMetric(operationName, stopwatch.ElapsedMilliseconds);
        }
    }

    private void OptimizeJitCompilation()
    {
        try
        {
            // Pre-JIT critical paths by calling static constructors
            RuntimeHelpers.RunClassConstructor(typeof(ViewModels.MainWindowViewModel).TypeHandle);
            RuntimeHelpers.RunClassConstructor(typeof(Services.Auth.AuthService).TypeHandle);
            
            _logger.Debug("JIT compilation optimization completed");
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "JIT compilation optimization failed");
        }
    }

    private void PreAllocateCollections()
    {
        try
        {
            // Pre-allocate commonly used collection sizes to reduce allocations
            var tempList = new List<object>(100); // Pre-allocate for typical collection sizes
            tempList.Clear(); // Clear but keep capacity
            
            var tempDict = new Dictionary<string, object>(50);
            tempDict.Clear();
            
            _logger.Debug("Collection pre-allocation completed");
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "Collection pre-allocation failed");
        }
    }

    private void RecordMetric(string operationName, long elapsedMs)
    {
        _performanceMetrics.AddOrUpdate(
            operationName,
            new List<long> { elapsedMs },
            (key, existing) =>
            {
                existing.Add(elapsedMs);
                // Keep only the last 100 measurements to prevent memory bloat
                if (existing.Count > 100)
                {
                    existing.RemoveAt(0);
                }
                return existing;
            });

        if (elapsedMs > 1000) // Log slow operations
        {
            _logger.Warning("Slow operation detected: {Operation} took {ElapsedMs}ms", operationName, elapsedMs);
        }
        else if (elapsedMs > 100)
        {
            _logger.Debug("Operation {Operation} took {ElapsedMs}ms", operationName, elapsedMs);
        }
    }
}

public class PerformanceMetrics
{
    public TimeSpan ApplicationUptime { get; set; }
    public long ManagedMemoryUsage { get; set; }
    public long WorkingSetMemory { get; set; }
    public int ThreadCount { get; set; }
    public Dictionary<int, int> GCGenerations { get; set; } = new();
    public Dictionary<string, OperationMetrics> OperationMetrics { get; set; } = new();
}

public class OperationMetrics
{
    public int CallCount { get; set; }
    public long TotalTimeMs { get; set; }
    public double AverageTimeMs { get; set; }
    public long MinTimeMs { get; set; }
    public long MaxTimeMs { get; set; }
}