using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using VrcGroupGuardian.Infrastructure;
using VrcGroupGuardian.Services.Auth;
using VrcGroupGuardian.Services.Audit;
using VrcGroupGuardian.Services.Enforcement;
using VrcGroupGuardian.Services.Groups;
using VrcGroupGuardian.Services.Instances;
using VrcGroupGuardian.Services.Members;
using VrcGroupGuardian.Services.VrcApi;
using VrcGroupGuardian.ViewModels;
using VrcGroupGuardian.Views;

namespace VrcGroupGuardian;

public partial class App : Application
{
    private IHost? _host;
    private IServiceProvider? _serviceProvider;

    protected override async void OnStartup(StartupEventArgs e)
    {
        try
        {
            // Configure logging first
            LoggingConfiguration.ConfigureLogging();

            // Build dependency injection container
            _host = CreateHostBuilder(e.Args).Build();
            _serviceProvider = _host.Services;

            // Start the host
            await _host.StartAsync();

            // Apply performance optimizations early
            var performanceOptimizer = _serviceProvider.GetRequiredService<IPerformanceOptimizer>();
            performanceOptimizer.OptimizeStartup();
            performanceOptimizer.EnableLazyInitialization();
            
            // Register global error handlers
            var errorHandler = _serviceProvider.GetRequiredService<IErrorHandler>();
            errorHandler.RegisterGlobalErrorHandlers();
            
            // Start service warmup in background
            _ = Task.Run(async () =>
            {
                try
                {
                    await performanceOptimizer.WarmupServicesAsync();
                    performanceOptimizer.PrecompileViewModels();
                    performanceOptimizer.EnableDataVirtualization();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Background warmup failed: {ex.Message}");
                }
            });

            // Check if first-run setup is needed
            var settingsStore = _serviceProvider.GetRequiredService<ISettingsStore>();
            var setupCompleted = await settingsStore.GetSettingAsync("SetupCompleted", false);
            
            if (!setupCompleted)
            {
                // Show setup wizard first
                var setupWizard = _serviceProvider.GetRequiredService<SetupWizardView>();
                var setupResult = setupWizard.ShowDialog();
                
                if (setupResult != true)
                {
                    // User cancelled setup, exit application
                    Current.Shutdown(0);
                    return;
                }
            }

            // Create and show main window
            var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
            mainWindow.Show();

            base.OnStartup(e);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Application startup failed: {ex.Message}", "Startup Error", 
                MessageBoxButton.OK, MessageBoxImage.Error);
            Current.Shutdown(1);
        }
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        try
        {
            if (_host != null)
            {
                await _host.StopAsync();
                _host.Dispose();
            }

            LoggingConfiguration.CloseAndFlush();
            base.OnExit(e);
        }
        catch (Exception ex)
        {
            // Log the error but don't prevent shutdown
            System.Diagnostics.Debug.WriteLine($"Error during application shutdown: {ex.Message}");
        }
    }

    private static IHostBuilder CreateHostBuilder(string[] args) =>
        Host.CreateDefaultBuilder(args)
            .ConfigureServices((context, services) =>
            {
                // Register Infrastructure Services
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

                // Register Business Services
                services.AddSingleton<IVrcApiService, VrcApiService>();
                services.AddSingleton<IAuthService, AuthService>();
                services.AddSingleton<IGroupService, GroupService>();
                services.AddSingleton<IInstancesService, InstancesService>();
                services.AddSingleton<IEnforcementService, EnforcementService>();
                services.AddSingleton<IMembersService, MembersService>();
                services.AddSingleton<IAuditService, AuditService>();

                // Register ViewModels
                services.AddTransient<MainWindowViewModel>();
                services.AddTransient<InstancesViewModel>();
                services.AddTransient<MembersViewModel>();
                services.AddTransient<AuditViewModel>();
                services.AddTransient<SettingsViewModel>();
                services.AddTransient<SetupWizardViewModel>();

                // Register Views
                services.AddSingleton<MainWindow>();
                services.AddTransient<SetupWizardView>();

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
                    builder.AddSerilog();
                });
            });

    public static T GetRequiredService<T>() where T : notnull
    {
        if (Current is App app && app._serviceProvider != null)
        {
            return app._serviceProvider.GetRequiredService<T>();
        }
        throw new InvalidOperationException("Service provider not initialized");
    }

    public static T? GetService<T>()
    {
        if (Current is App app && app._serviceProvider != null)
        {
            return app._serviceProvider.GetService<T>();
        }
        return default;
    }
}