using Xunit;
using Xunit.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Runtime;
using VrcGroupGuardian.Infrastructure;
using VrcGroupGuardian.Services.VrcApi;
using VrcGroupGuardian.Services.Auth;
using VrcGroupGuardian.Services.Groups;
using VrcGroupGuardian.Services.Instances;
using VrcGroupGuardian.Services.Enforcement;
using VrcGroupGuardian.Services.Members;
using VrcGroupGuardian.Services.Audit;

namespace VrcGroupGuardian.Tests.Performance;

public class PerformanceTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly IHost _host;
    private readonly IServiceProvider _serviceProvider;

    public PerformanceTests(ITestOutputHelper output)
    {
        _output = output;
        _host = CreateTestHost();
        _serviceProvider = _host.Services;
    }

    [Fact]
    public async Task ApplicationStartup_CompletesWithinTargetTime()
    {
        // Arrange
        const int targetStartupTimeMs = 3000; // 3 seconds
        const int testRuns = 5;
        var startupTimes = new List<long>();

        // Act & Assert
        for (int i = 0; i < testRuns; i++)
        {
            var stopwatch = Stopwatch.StartNew();
            
            // Simulate application startup sequence
            await SimulateApplicationStartup();
            
            stopwatch.Stop();
            startupTimes.Add(stopwatch.ElapsedMilliseconds);
            
            _output.WriteLine($"Startup Run {i + 1}: {stopwatch.ElapsedMilliseconds}ms");
            
            // Reset for next run
            GC.Collect(2, GCCollectionMode.Forced, true);
            GC.WaitForPendingFinalizers();
            await Task.Delay(100);
        }

        var averageStartupTime = startupTimes.Average();
        var maxStartupTime = startupTimes.Max();
        var minStartupTime = startupTimes.Min();

        _output.WriteLine($"Average Startup Time: {averageStartupTime:F0}ms");
        _output.WriteLine($"Min: {minStartupTime}ms, Max: {maxStartupTime}ms");
        _output.WriteLine($"Target: <{targetStartupTimeMs}ms");

        // Performance assertions
        Assert.True(averageStartupTime < targetStartupTimeMs, 
            $"Average startup time ({averageStartupTime:F0}ms) exceeds target ({targetStartupTimeMs}ms)");
        Assert.True(maxStartupTime < targetStartupTimeMs * 1.5, 
            $"Maximum startup time ({maxStartupTime}ms) is too slow");
    }

    [Fact]
    public async Task MemoryUsage_StaysWithinTargetLimits()
    {
        // Arrange
        const long targetMemoryMB = 100;
        const long criticalMemoryMB = 150;
        
        var initialMemory = GC.GetTotalMemory(true);
        _output.WriteLine($"Initial Memory: {initialMemory / 1024 / 1024}MB");

        // Act - Simulate typical application usage
        await SimulateTypicalUsage();

        // Force garbage collection to get accurate measurement
        GC.Collect(2, GCCollectionMode.Forced, true);
        GC.WaitForPendingFinalizers();
        GC.Collect(2, GCCollectionMode.Forced, true);

        var finalMemory = GC.GetTotalMemory(false);
        var memoryUsedMB = finalMemory / 1024 / 1024;

        _output.WriteLine($"Final Memory: {memoryUsedMB}MB");
        _output.WriteLine($"Target: <{targetMemoryMB}MB, Critical: <{criticalMemoryMB}MB");

        // Memory assertions
        Assert.True(memoryUsedMB < criticalMemoryMB, 
            $"Memory usage ({memoryUsedMB}MB) exceeds critical limit ({criticalMemoryMB}MB)");
        
        if (memoryUsedMB > targetMemoryMB)
        {
            _output.WriteLine($"WARNING: Memory usage ({memoryUsedMB}MB) exceeds target ({targetMemoryMB}MB)");
        }
    }

    [Fact]
    public async Task ApiCallEfficiency_MaintainsReasonableRate()
    {
        // Arrange
        var mockApiService = _serviceProvider.GetRequiredService<IVrcApiService>();
        var cacheService = _serviceProvider.GetRequiredService<ICacheService>();
        
        const int testDurationMs = 60000; // 1 minute
        const int maxApiCallsPerMinute = 20; // VRChat rate limit
        
        var apiCallCount = 0;
        var cacheHitCount = 0;
        var stopwatch = Stopwatch.StartNew();

        // Act - Simulate API-intensive operations
        while (stopwatch.ElapsedMilliseconds < testDurationMs)
        {
            try
            {
                // Simulate typical API calls that would happen during monitoring
                await SimulateApiCalls(mockApiService, cacheService);
                apiCallCount++;
                
                // Check cache hits
                var cacheStats = cacheService.GetStatistics();
                if (cacheStats.HitCount > cacheHitCount)
                {
                    cacheHitCount = (int)cacheStats.HitCount;
                }
                
                await Task.Delay(1000); // 1 second intervals
            }
            catch (Exception ex)
            {
                _output.WriteLine($"API call failed: {ex.Message}");
            }
        }

        stopwatch.Stop();
        var actualRate = (double)apiCallCount / (stopwatch.ElapsedMilliseconds / 60000.0);
        var cacheHitRate = cacheHitCount > 0 ? (double)cacheHitCount / apiCallCount : 0;

        _output.WriteLine($"API Calls Made: {apiCallCount}");
        _output.WriteLine($"Rate: {actualRate:F1} calls/minute");
        _output.WriteLine($"Cache Hit Rate: {cacheHitRate:P1}");
        _output.WriteLine($"Target Rate: <{maxApiCallsPerMinute} calls/minute");

        // API efficiency assertions
        Assert.True(actualRate <= maxApiCallsPerMinute, 
            $"API call rate ({actualRate:F1}/min) exceeds VRChat rate limit ({maxApiCallsPerMinute}/min)");
        Assert.True(cacheHitRate > 0.3, 
            $"Cache hit rate ({cacheHitRate:P1}) is too low - caching not effective");
    }

    [Fact]
    public async Task ServiceInitialization_OptimizedBootstrap()
    {
        // Arrange
        const int targetInitTimeMs = 1000; // 1 second
        var serviceTypes = new[]
        {
            typeof(ISecureStorage),
            typeof(ISettingsStore),
            typeof(IRateLimitService),
            typeof(ICacheService),
            typeof(IPerformanceOptimizer),
            typeof(IVrcApiService),
            typeof(IAuthService),
            typeof(IGroupService),
            typeof(IInstancesService),
            typeof(IEnforcementService),
            typeof(IMembersService),
            typeof(IAuditService)
        };

        var initTimes = new Dictionary<Type, long>();

        // Act
        foreach (var serviceType in serviceTypes)
        {
            var stopwatch = Stopwatch.StartNew();
            
            var service = _serviceProvider.GetRequiredService(serviceType);
            Assert.NotNull(service);
            
            stopwatch.Stop();
            initTimes[serviceType] = stopwatch.ElapsedMilliseconds;
            
            _output.WriteLine($"{serviceType.Name}: {stopwatch.ElapsedMilliseconds}ms");
        }

        var totalInitTime = initTimes.Values.Sum();
        var slowestService = initTimes.OrderByDescending(kvp => kvp.Value).First();

        _output.WriteLine($"Total Service Init Time: {totalInitTime}ms");
        _output.WriteLine($"Slowest Service: {slowestService.Key.Name} ({slowestService.Value}ms)");
        _output.WriteLine($"Target: <{targetInitTimeMs}ms");

        // Service initialization assertions
        Assert.True(totalInitTime < targetInitTimeMs, 
            $"Total service initialization time ({totalInitTime}ms) exceeds target ({targetInitTimeMs}ms)");
        Assert.True(slowestService.Value < 500, 
            $"Individual service init time ({slowestService.Value}ms) is too slow for {slowestService.Key.Name}");
    }

    [Fact]
    public async Task ConcurrentOperations_ScalesAppropriately()
    {
        // Arrange
        const int concurrentOperations = 10;
        const int operationsPerThread = 20;
        var performanceOptimizer = _serviceProvider.GetRequiredService<IPerformanceOptimizer>();

        // Warm up
        performanceOptimizer.OptimizeStartup();
        await Task.Delay(100);

        var stopwatch = Stopwatch.StartNew();

        // Act - Run concurrent operations
        var tasks = Enumerable.Range(0, concurrentOperations)
            .Select(i => RunConcurrentWorkload(i, operationsPerThread))
            .ToArray();

        await Task.WhenAll(tasks);
        stopwatch.Stop();

        var totalOperations = concurrentOperations * operationsPerThread;
        var operationsPerSecond = totalOperations / (stopwatch.ElapsedMilliseconds / 1000.0);

        _output.WriteLine($"Concurrent Operations: {concurrentOperations}");
        _output.WriteLine($"Operations per Thread: {operationsPerThread}");
        _output.WriteLine($"Total Time: {stopwatch.ElapsedMilliseconds}ms");
        _output.WriteLine($"Throughput: {operationsPerSecond:F1} ops/sec");

        // Concurrency performance assertions
        Assert.True(stopwatch.ElapsedMilliseconds < 10000, 
            $"Concurrent operations took too long ({stopwatch.ElapsedMilliseconds}ms)");
        Assert.True(operationsPerSecond > 50, 
            $"Throughput too low ({operationsPerSecond:F1} ops/sec)");
    }

    [Fact]
    public void GarbageCollection_OptimalBehavior()
    {
        // Arrange
        var gen0Before = GC.CollectionCount(0);
        var gen1Before = GC.CollectionCount(1);
        var gen2Before = GC.CollectionCount(2);

        // Act - Generate some garbage
        for (int i = 0; i < 1000; i++)
        {
            var temp = new byte[1024]; // 1KB allocations
            temp[0] = (byte)i;
        }

        // Allow GC to run
        await Task.Delay(100);

        var gen0After = GC.CollectionCount(0);
        var gen1After = GC.CollectionCount(1);
        var gen2After = GC.CollectionCount(2);

        var gen0Collections = gen0After - gen0Before;
        var gen1Collections = gen1After - gen1Before;
        var gen2Collections = gen2After - gen2Before;

        _output.WriteLine($"Gen 0 Collections: {gen0Collections}");
        _output.WriteLine($"Gen 1 Collections: {gen1Collections}");
        _output.WriteLine($"Gen 2 Collections: {gen2Collections}");

        // GC assertions - should not have excessive collections
        Assert.True(gen2Collections < 2, 
            $"Too many Gen 2 collections ({gen2Collections}) - possible memory leak");
    }

    private async Task SimulateApplicationStartup()
    {
        // Simulate the startup sequence from App.xaml.cs
        var performanceOptimizer = _serviceProvider.GetRequiredService<IPerformanceOptimizer>();
        var errorHandler = _serviceProvider.GetRequiredService<IErrorHandler>();
        var settingsStore = _serviceProvider.GetRequiredService<ISettingsStore>();

        // Simulate startup optimizations
        performanceOptimizer.OptimizeStartup();
        performanceOptimizer.EnableLazyInitialization();
        
        // Register error handlers
        errorHandler.RegisterGlobalErrorHandlers();
        
        // Check setup completion
        await settingsStore.GetSettingAsync("SetupCompleted", false);
        
        // Simulate service warmup
        await performanceOptimizer.WarmupServicesAsync();
    }

    private async Task SimulateTypicalUsage()
    {
        var authService = _serviceProvider.GetRequiredService<IAuthService>();
        var groupService = _serviceProvider.GetRequiredService<IGroupService>();
        var instancesService = _serviceProvider.GetRequiredService<IInstancesService>();
        var enforcementService = _serviceProvider.GetRequiredService<IEnforcementService>();
        var auditService = _serviceProvider.GetRequiredService<IAuditService>();

        // Simulate typical user operations
        for (int i = 0; i < 10; i++)
        {
            try
            {
                // Simulate periodic instance checking
                var instances = await instancesService.GetCurrentInstancesAsync("test-group");
                
                // Simulate policy evaluation
                foreach (var instance in instances)
                {
                    enforcementService.EvaluateInstance(instance);
                }
                
                // Simulate audit log access
                var auditRecords = await auditService.GetAuditRecordsAsync(DateTime.UtcNow.AddHours(-1), DateTime.UtcNow);
                
                await Task.Delay(100); // Small delay between operations
            }
            catch
            {
                // Ignore errors in performance testing
            }
        }
    }

    private async Task SimulateApiCalls(IVrcApiService apiService, ICacheService cacheService)
    {
        try
        {
            // These will be mocked/simulated calls
            await apiService.IsAuthenticatedAsync();
            
            // Simulate caching behavior
            cacheService.Set("test-key", "test-value", TimeSpan.FromMinutes(1));
            cacheService.Get<string>("test-key");
        }
        catch
        {
            // Expected with mock services
        }
    }

    private async Task RunConcurrentWorkload(int threadId, int operations)
    {
        var cacheService = _serviceProvider.GetRequiredService<ICacheService>();
        
        for (int i = 0; i < operations; i++)
        {
            // Simulate cache operations
            var key = $"thread{threadId}_op{i}";
            var value = $"data_{threadId}_{i}";
            
            cacheService.Set(key, value, TimeSpan.FromSeconds(10));
            cacheService.Get<string>(key);
            
            await Task.Delay(1); // Small delay to prevent tight loop
        }
    }

    private IHost CreateTestHost()
    {
        return Host.CreateDefaultBuilder()
            .ConfigureServices((context, services) =>
            {
                // Register all services for testing
                services.AddSingleton<ISecureStorage, SecureStorage>();
                services.AddSingleton<ISettingsStore, SettingsStore>();
                services.AddSingleton<IRateLimitService, RateLimitService>();
                services.AddSingleton<IVrchatHttpClientFactory, VrchatHttpClientFactory>();
                services.AddSingleton<INotificationService, NotificationService>();
                services.AddSingleton<IThemeManager, ThemeManager>();
                services.AddSingleton<IPerformanceOptimizer, PerformanceOptimizer>();
                services.AddSingleton<ICacheService, CacheService>();
                services.AddSingleton<IErrorHandler, ErrorHandler>();
                services.AddSingleton<IDiagnosticsService, DiagnosticsService>();
                services.AddSingleton<IDryRunMode, DryRunMode>();
                services.AddSingleton<ICircuitBreaker>(provider => new CircuitBreaker(new CircuitBreakerOptions 
                { 
                    FailureThreshold = 3, 
                    Timeout = TimeSpan.FromSeconds(10), 
                    RetryDelay = TimeSpan.FromMinutes(2) 
                }));

                // Business Services
                services.AddSingleton<IVrcApiService, VrcApiService>();
                services.AddSingleton<IAuthService, AuthService>();
                services.AddSingleton<IGroupService, GroupService>();
                services.AddSingleton<IInstancesService, InstancesService>();
                services.AddSingleton<IEnforcementService, EnforcementService>();
                services.AddSingleton<IMembersService, MembersService>();
                services.AddSingleton<IAuditService, AuditService>();

                // Configure HttpClient
                services.AddHttpClient("VRChatApi", client =>
                {
                    client.BaseAddress = new Uri("https://api.vrchat.cloud/api/1/");
                    client.DefaultRequestHeaders.Add("User-Agent", "VrcGroupGuardian/1.0");
                });

                // Configure logging
                services.AddLogging(builder =>
                {
                    builder.ClearProviders();
                    builder.AddConsole();
                    builder.SetMinimumLevel(LogLevel.Warning); // Reduce noise in tests
                });
            })
            .Build();
    }

    public void Dispose()
    {
        _host?.Dispose();
    }
}