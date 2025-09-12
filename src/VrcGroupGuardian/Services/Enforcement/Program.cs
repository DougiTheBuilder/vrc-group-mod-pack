using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using VrcGroupGuardian.Infrastructure;
using VrcGroupGuardian.Models;
using VrcGroupGuardian.Services.Auth;
using VrcGroupGuardian.Services.Enforcement;
using VrcGroupGuardian.Services.Groups;
using VrcGroupGuardian.Services.Instances;
using VrcGroupGuardian.Services.VrcApi;

namespace VrcGroupGuardian.Services.Enforcement.Cli;

public class Program
{
    private static IServiceProvider? _serviceProvider;

    public static async Task<int> Main(string[] args)
    {
        ConfigureServices();
        
        if (args.Length == 0)
        {
            ShowHelp();
            return 1;
        }

        var command = args[0].ToLower();
        var enforcementService = _serviceProvider!.GetRequiredService<IEnforcementService>();
        
        try
        {
            return command switch
            {
                "start" => await HandleStart(enforcementService),
                "stop" => await HandleStop(enforcementService),
                "status" => await HandleStatus(enforcementService),
                "policy" => await HandlePolicy(enforcementService, args),
                "cancel" => await HandleCancel(enforcementService, args),
                "list" => await HandleList(enforcementService),
                "monitor" => await HandleMonitor(enforcementService),
                "help" or "--help" or "-h" => ShowHelp(),
                _ => HandleUnknownCommand(command)
            };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }

    private static void ConfigureServices()
    {
        var services = new ServiceCollection();
        
        services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Information));
        services.AddSingleton<IRateLimitService, RateLimitService>();
        services.AddSingleton<ISecureStorage, SecureStorage>();
        services.AddSingleton<ISettingsStore, SettingsStore>();
        services.AddSingleton<IVrchatHttpClientFactory, VrchatHttpClientFactory>();
        services.AddSingleton<IVrcApiService, VrcApiService>();
        services.AddSingleton<IAuthService, AuthService>();
        services.AddSingleton<IGroupService, GroupService>();
        services.AddSingleton<IInstancesService, InstancesService>();
        services.AddSingleton<IEnforcementService, EnforcementService>();
        
        _serviceProvider = services.BuildServiceProvider();
    }

    private static async Task<int> HandleStart(IEnforcementService enforcementService)
    {
        Console.WriteLine("Starting policy enforcement...");
        
        var success = await enforcementService.StartEnforcementAsync();
        
        if (success)
        {
            Console.WriteLine("✓ Policy enforcement started");
            var config = await enforcementService.GetPolicyConfigurationAsync();
            Console.WriteLine($"Grace period: {config.GracePeriodSeconds} seconds");
            Console.WriteLine($"Polling interval: {config.PollingIntervalSeconds} seconds");
            return 0;
        }
        else
        {
            Console.WriteLine("✗ Failed to start policy enforcement");
            return 1;
        }
    }

    private static async Task<int> HandleStop(IEnforcementService enforcementService)
    {
        Console.WriteLine("Stopping policy enforcement...");
        
        var success = await enforcementService.StopEnforcementAsync();
        
        if (success)
        {
            Console.WriteLine("✓ Policy enforcement stopped");
            return 0;
        }
        else
        {
            Console.WriteLine("✗ Failed to stop policy enforcement");
            return 1;
        }
    }

    private static async Task<int> HandleStatus(IEnforcementService enforcementService)
    {
        var isActive = await enforcementService.IsEnforcementActiveAsync();
        var config = await enforcementService.GetPolicyConfigurationAsync();
        var enforcementStatus = await enforcementService.GetEnforcementStatusAsync();
        
        Console.WriteLine($"Enforcement Status: {(isActive ? "✓ Active" : "✗ Inactive")}");
        Console.WriteLine();
        
        Console.WriteLine("Policy Configuration:");
        Console.WriteLine($"  Enforcement Enabled: {(config.EnforcementEnabled ? "Yes" : "No")}");
        Console.WriteLine($"  Grace Period: {config.GracePeriodSeconds} seconds");
        Console.WriteLine($"  Polling Interval: {config.PollingIntervalSeconds} seconds");
        Console.WriteLine($"  Notifications: {(config.NotificationsEnabled ? "Enabled" : "Disabled")}");
        Console.WriteLine($"  Rate Limit: {config.RateLimitRequestsPerMinute} requests/minute");
        Console.WriteLine($"  Log Level: {config.LogLevel}");
        Console.WriteLine($"  Monitored Group: {config.GroupName} ({config.GroupId})");
        
        if (enforcementStatus.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine($"Active Enforcement Actions ({enforcementStatus.Count}):");
            Console.WriteLine();
            Console.WriteLine("Instance ID".PadRight(45) + "World Name".PadRight(30) + "Status".PadRight(15) + "Time Remaining");
            Console.WriteLine(new string('-', 110));
            
            foreach (var status in enforcementStatus)
            {
                var timeRemaining = status.TimeRemaining.TotalMinutes > 0 
                    ? $"{status.TimeRemaining.TotalMinutes:F1}m" 
                    : "Expired";
                    
                Console.WriteLine($"{status.InstanceId.PadRight(45)}{status.InstanceName.PadRight(30)}{status.Status.ToString().PadRight(15)}{timeRemaining}");
            }
        }
        else if (isActive)
        {
            Console.WriteLine();
            Console.WriteLine("No instances currently under enforcement action.");
        }
        
        return 0;
    }

    private static async Task<int> HandlePolicy(IEnforcementService enforcementService, string[] args)
    {
        if (args.Length < 2)
        {
            var config = await enforcementService.GetPolicyConfigurationAsync();
            
            Console.WriteLine("Current Policy Configuration:");
            Console.WriteLine($"  Enforcement Enabled: {config.EnforcementEnabled}");
            Console.WriteLine($"  Grace Period: {config.GracePeriodSeconds} seconds");
            Console.WriteLine($"  Polling Interval: {config.PollingIntervalSeconds} seconds");
            Console.WriteLine($"  Notifications: {config.NotificationsEnabled}");
            Console.WriteLine($"  Rate Limit: {config.RateLimitRequestsPerMinute} requests/minute");
            Console.WriteLine($"  Cache Expiry: {config.CacheExpiryMinutes} minutes");
            Console.WriteLine($"  Log Level: {config.LogLevel}");
            Console.WriteLine($"  Export Logs: {config.ExportAuditLogs}");
            Console.WriteLine($"  Group ID: {config.GroupId}");
            Console.WriteLine($"  Group Name: {config.GroupName}");
            
            return 0;
        }

        var subCommand = args[1].ToLower();
        var config = await enforcementService.GetPolicyConfigurationAsync();
        
        switch (subCommand)
        {
            case "enable":
                config.EnforcementEnabled = true;
                break;
            case "disable":
                config.EnforcementEnabled = false;
                break;
            case "grace":
                if (args.Length < 3 || !int.TryParse(args[2], out var gracePeriod))
                {
                    Console.WriteLine("Usage: dotnet run policy grace <seconds>");
                    Console.WriteLine("Valid range: 60-300 seconds");
                    return 1;
                }
                if (gracePeriod < 60 || gracePeriod > 300)
                {
                    Console.WriteLine("Grace period must be between 60 and 300 seconds");
                    return 1;
                }
                config.GracePeriodSeconds = gracePeriod;
                break;
            case "polling":
                if (args.Length < 3 || !int.TryParse(args[2], out var pollingInterval))
                {
                    Console.WriteLine("Usage: dotnet run policy polling <seconds>");
                    Console.WriteLine("Valid range: 45-90 seconds");
                    return 1;
                }
                if (pollingInterval < 45 || pollingInterval > 90)
                {
                    Console.WriteLine("Polling interval must be between 45 and 90 seconds");
                    return 1;
                }
                config.PollingIntervalSeconds = pollingInterval;
                break;
            case "notifications":
                if (args.Length < 3)
                {
                    Console.WriteLine("Usage: dotnet run policy notifications <true/false>");
                    return 1;
                }
                if (!bool.TryParse(args[2], out var notifications))
                {
                    Console.WriteLine("Invalid value. Use 'true' or 'false'");
                    return 1;
                }
                config.NotificationsEnabled = notifications;
                break;
            case "ratelimit":
                if (args.Length < 3 || !int.TryParse(args[2], out var rateLimit))
                {
                    Console.WriteLine("Usage: dotnet run policy ratelimit <requests-per-minute>");
                    Console.WriteLine("Valid range: 1-100");
                    return 1;
                }
                if (rateLimit < 1 || rateLimit > 100)
                {
                    Console.WriteLine("Rate limit must be between 1 and 100 requests per minute");
                    return 1;
                }
                config.RateLimitRequestsPerMinute = rateLimit;
                break;
            default:
                Console.WriteLine($"Unknown policy command: {subCommand}");
                Console.WriteLine("Available commands: enable, disable, grace, polling, notifications, ratelimit");
                return 1;
        }

        var success = await enforcementService.UpdatePolicyConfigurationAsync(config);
        
        if (success)
        {
            Console.WriteLine("✓ Policy configuration updated successfully");
            return 0;
        }
        else
        {
            Console.WriteLine("✗ Failed to update policy configuration");
            return 1;
        }
    }

    private static async Task<int> HandleCancel(IEnforcementService enforcementService, string[] args)
    {
        if (args.Length < 2)
        {
            Console.WriteLine("Usage: dotnet run cancel <instance-id>");
            return 1;
        }

        var instanceId = args[1];
        
        Console.WriteLine($"Cancelling scheduled closure for instance: {instanceId}");
        
        var result = await enforcementService.CancelScheduledClosureAsync(instanceId);
        
        if (result.Success)
        {
            Console.WriteLine($"✓ {result.Message}");
            return 0;
        }
        else
        {
            Console.WriteLine($"✗ {result.Message}");
            return 1;
        }
    }

    private static async Task<int> HandleList(IEnforcementService enforcementService)
    {
        var enforcementStatus = await enforcementService.GetEnforcementStatusAsync();
        
        if (enforcementStatus.Count == 0)
        {
            Console.WriteLine("No instances currently under enforcement action.");
            return 0;
        }

        Console.WriteLine($"Active Enforcement Actions ({enforcementStatus.Count}):");
        Console.WriteLine();
        Console.WriteLine("Instance ID".PadRight(45) + "World Name".PadRight(30) + "Status".PadRight(15) + "Time Remaining".PadRight(15) + "Scheduled");
        Console.WriteLine(new string('-', 125));
        
        foreach (var status in enforcementStatus.OrderBy(s => s.TimeRemaining))
        {
            var timeRemaining = status.TimeRemaining.TotalMinutes > 0 
                ? $"{status.TimeRemaining.TotalMinutes:F1}m" 
                : "Expired";
            
            var scheduled = status.ScheduledAt.ToString("MM-dd HH:mm");
                
            Console.WriteLine($"{status.InstanceId.PadRight(45)}{status.InstanceName.PadRight(30)}{status.Status.ToString().PadRight(15)}{timeRemaining.PadRight(15)}{scheduled}");
        }
        
        return 0;
    }

    private static async Task<int> HandleMonitor(IEnforcementService enforcementService)
    {
        var isActive = await enforcementService.IsEnforcementActiveAsync();
        
        if (!isActive)
        {
            Console.WriteLine("Enforcement is not active. Start enforcement first with 'dotnet run start'");
            return 1;
        }

        Console.WriteLine("Monitoring enforcement events in real-time...");
        Console.WriteLine("Press Ctrl+C to stop monitoring...");
        Console.WriteLine();
        
        // Set up event handlers
        enforcementService.InstanceFlagged += (sender, e) =>
        {
            Console.WriteLine($"[{e.FlaggedAt:HH:mm:ss}] FLAGGED: {e.Instance.InstanceId} - {e.Instance.WorldName} ({e.Reason})");
        };
        
        enforcementService.ClosureScheduled += (sender, e) =>
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] SCHEDULED: {e.Instance.InstanceId} - Closure at {e.ScheduledFor:HH:mm:ss} ({e.Reason})");
        };
        
        enforcementService.ClosureCancelled += (sender, e) =>
        {
            Console.WriteLine($"[{e.CancelledAt:HH:mm:ss}] CANCELLED: {e.Instance?.InstanceId ?? "Unknown"} - {e.CancelledBy}");
        };
        
        enforcementService.AutoCloseExecuted += (sender, e) =>
        {
            var result = e.Success ? "SUCCESS" : "FAILED";
            Console.WriteLine($"[{e.ExecutedAt:HH:mm:ss}] AUTO-CLOSE {result}: {e.Instance?.InstanceId ?? "Unknown"} ({e.Reason})");
        };
        
        // Wait for Ctrl+C
        var tcs = new TaskCompletionSource<bool>();
        Console.CancelKeyPress += (sender, e) =>
        {
            e.Cancel = true;
            tcs.SetResult(true);
        };
        
        await tcs.Task;
        
        Console.WriteLine();
        Console.WriteLine("Monitoring stopped.");
        
        return 0;
    }

    private static int ShowHelp()
    {
        Console.WriteLine("VRC Group Guardian - Enforcement Service CLI");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  dotnet run start                      - Start policy enforcement");
        Console.WriteLine("  dotnet run stop                       - Stop policy enforcement");
        Console.WriteLine("  dotnet run status                     - Show enforcement status");
        Console.WriteLine("  dotnet run policy [command] [value]   - Manage policy configuration");
        Console.WriteLine("  dotnet run cancel <instance-id>       - Cancel scheduled instance closure");
        Console.WriteLine("  dotnet run list                       - List active enforcement actions");
        Console.WriteLine("  dotnet run monitor                    - Monitor enforcement events in real-time");
        Console.WriteLine("  dotnet run help                       - Show this help");
        Console.WriteLine();
        Console.WriteLine("Policy commands:");
        Console.WriteLine("  dotnet run policy                     - Show current policy");
        Console.WriteLine("  dotnet run policy enable              - Enable enforcement");
        Console.WriteLine("  dotnet run policy disable             - Disable enforcement");
        Console.WriteLine("  dotnet run policy grace <seconds>     - Set grace period (60-300)");
        Console.WriteLine("  dotnet run policy polling <seconds>   - Set polling interval (45-90)");
        Console.WriteLine("  dotnet run policy notifications <true/false> - Enable/disable notifications");
        Console.WriteLine("  dotnet run policy ratelimit <rpm>     - Set rate limit (1-100 requests/min)");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  dotnet run start");
        Console.WriteLine("  dotnet run policy grace 180");
        Console.WriteLine("  dotnet run cancel wrld_12345:67890~private");
        Console.WriteLine("  dotnet run monitor");
        Console.WriteLine();
        return 0;
    }

    private static int HandleUnknownCommand(string command)
    {
        Console.WriteLine($"Unknown command: {command}");
        Console.WriteLine("Use 'dotnet run help' to see available commands.");
        return 1;
    }
}