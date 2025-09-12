using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using VrcGroupGuardian.Infrastructure;
using VrcGroupGuardian.Services.VrcApi;
using VrcGroupGuardian.Services.Auth;

namespace VrcGroupGuardian.Tests.Performance;

[MemoryDiagnoser]
[SimpleJob(launchCount: 1, warmupCount: 3, iterationCount: 5)]
public class StartupBenchmark
{
    private IHost? _host;

    [GlobalSetup]
    public void Setup()
    {
        // Pre-create host for baseline comparison
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _host?.Dispose();
    }

    [Benchmark(Baseline = true)]
    public async Task ColdStartup()
    {
        _host = CreateHost();
        await _host.StartAsync();
        
        // Simulate essential service initialization
        var performanceOptimizer = _host.Services.GetRequiredService<IPerformanceOptimizer>();
        performanceOptimizer.OptimizeStartup();
        
        await _host.StopAsync();
        _host.Dispose();
        _host = null;
    }

    [Benchmark]
    public async Task OptimizedStartup()
    {
        _host = CreateHost();
        await _host.StartAsync();
        
        // Apply all startup optimizations
        var performanceOptimizer = _host.Services.GetRequiredService<IPerformanceOptimizer>();
        performanceOptimizer.OptimizeStartup();
        performanceOptimizer.EnableLazyInitialization();
        
        // Background warmup without blocking
        _ = Task.Run(async () =>
        {
            await performanceOptimizer.WarmupServicesAsync();
            performanceOptimizer.PrecompileViewModels();
            performanceOptimizer.EnableDataVirtualization();
        });
        
        await _host.StopAsync();
        _host.Dispose();
        _host = null;
    }

    [Benchmark]
    public void ServiceRegistration()
    {
        var services = new ServiceCollection();
        ConfigureServices(services);
        var provider = services.BuildServiceProvider();
        provider.Dispose();
    }

    [Benchmark]
    public void EssentialServiceCreation()
    {
        _host = CreateHost();
        
        // Create only essential services
        var secureStorage = _host.Services.GetRequiredService<ISecureStorage>();
        var settingsStore = _host.Services.GetRequiredService<ISettingsStore>();
        var cacheService = _host.Services.GetRequiredService<ICacheService>();
        
        _host.Dispose();
        _host = null;
    }

    [Benchmark]
    public async Task SettingsInitialization()
    {
        _host = CreateHost();
        var settingsStore = _host.Services.GetRequiredService<ISettingsStore>();
        
        // Simulate first-run settings check
        await settingsStore.GetSettingAsync("SetupCompleted", false);
        await settingsStore.GetSettingAsync("DryRunMode", false);
        await settingsStore.GetSettingAsync("LogLevel", "Information");
        
        _host.Dispose();
        _host = null;
    }

    private IHost CreateHost()
    {
        return Host.CreateDefaultBuilder()
            .ConfigureServices((context, services) => ConfigureServices(services))
            .Build();
    }

    private void ConfigureServices(IServiceCollection services)
    {
        // Infrastructure Services
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

        // Configure HttpClient
        services.AddHttpClient("VRChatApi", client =>
        {
            client.BaseAddress = new Uri("https://api.vrchat.cloud/api/1/");
            client.DefaultRequestHeaders.Add("User-Agent", "VrcGroupGuardian/1.0");
        });

        // Configure logging (minimal for benchmarking)
        services.AddLogging(builder => builder.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Critical));
    }
}

// Console application to run benchmarks
public class BenchmarkRunner
{
    public static void Main(string[] args)
    {
        var summary = BenchmarkRunner.Run<StartupBenchmark>();
        Console.WriteLine(summary);
    }
}