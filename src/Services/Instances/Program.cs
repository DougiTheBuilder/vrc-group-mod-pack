using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using VrcGroupGuardian.Infrastructure;
using VrcGroupGuardian.Services.Auth;
using VrcGroupGuardian.Services.Groups;
using VrcGroupGuardian.Services.Instances;
using VrcGroupGuardian.Services.VrcApi;

namespace VrcGroupGuardian.Services.Instances.Cli;

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
        var instancesService = _serviceProvider!.GetRequiredService<IInstancesService>();
        var authService = _serviceProvider!.GetRequiredService<IAuthService>();
        var groupService = _serviceProvider!.GetRequiredService<IGroupService>();
        
        try
        {
            return command switch
            {
                "list" => await HandleList(instancesService, groupService, args),
                "get" => await HandleGet(instancesService, args),
                "close" => await HandleClose(instancesService, args),
                "monitor" => await HandleMonitor(instancesService, groupService, args),
                "stop" => await HandleStopMonitoring(instancesService),
                "status" => await HandleStatus(instancesService),
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
        
        _serviceProvider = services.BuildServiceProvider();
    }

    private static async Task<int> HandleList(IInstancesService instancesService, IGroupService groupService, string[] args)
    {
        var selectedGroup = await groupService.GetSelectedGroupAsync();
        if (selectedGroup == null)
        {
            Console.WriteLine("No group selected. Use the group service to select a group first.");
            return 1;
        }

        Console.WriteLine($"Listing instances for group: {selectedGroup.GroupName}");
        
        var instances = await instancesService.GetGroupInstancesAsync(selectedGroup.GroupId);
        
        if (instances.Count == 0)
        {
            Console.WriteLine("No instances found.");
            return 0;
        }

        Console.WriteLine();
        Console.WriteLine("Instance ID".PadRight(45) + "World Name".PadRight(30) + "Users".PadRight(8) + "Type".PadRight(12) + "Status");
        Console.WriteLine(new string('-', 100));
        
        foreach (var instance in instances)
        {
            Console.WriteLine($"{instance.InstanceId.PadRight(45)}{instance.WorldName.PadRight(30)}{$"{instance.UserCount}/{instance.MaxUsers}".PadRight(8)}{instance.InstanceType.ToString().PadRight(12)}{instance.Status}");
        }
        
        Console.WriteLine();
        Console.WriteLine($"Total: {instances.Count} instances");
        
        return 0;
    }

    private static async Task<int> HandleGet(IInstancesService instancesService, string[] args)
    {
        if (args.Length < 2)
        {
            Console.WriteLine("Usage: dotnet run get <instance-id>");
            return 1;
        }

        var instanceId = args[1];
        var instance = await instancesService.GetInstanceAsync(instanceId);
        
        if (instance == null)
        {
            Console.WriteLine($"Instance not found: {instanceId}");
            return 1;
        }

        Console.WriteLine("Instance Details:");
        Console.WriteLine($"  Instance ID: {instance.InstanceId}");
        Console.WriteLine($"  World Name: {instance.WorldName}");
        Console.WriteLine($"  World ID: {instance.WorldId}");
        Console.WriteLine($"  Type: {instance.InstanceType}");
        Console.WriteLine($"  Users: {instance.UserCount}/{instance.MaxUsers}");
        Console.WriteLine($"  Region: {instance.Region}");
        Console.WriteLine($"  Age Gated: {(instance.AgeGated ? "Yes" : "No")}");
        Console.WriteLine($"  Status: {instance.Status}");
        Console.WriteLine($"  Created: {instance.CreatedAt:yyyy-MM-dd HH:mm:ss} UTC");
        Console.WriteLine($"  Last Updated: {instance.LastUpdated:yyyy-MM-dd HH:mm:ss} UTC");
        
        if (instance.CountdownTimer.HasValue)
        {
            Console.WriteLine($"  Countdown Timer: {instance.CountdownTimer.Value.TotalMinutes:F1} minutes remaining");
        }
        
        return 0;
    }

    private static async Task<int> HandleClose(IInstancesService instancesService, string[] args)
    {
        if (args.Length < 2)
        {
            Console.WriteLine("Usage: dotnet run close <instance-id>");
            return 1;
        }

        var instanceId = args[1];
        
        Console.WriteLine($"Closing instance: {instanceId}");
        
        var success = await instancesService.CloseInstanceAsync(instanceId);
        
        if (success)
        {
            Console.WriteLine("✓ Instance closed successfully");
            return 0;
        }
        else
        {
            Console.WriteLine("✗ Failed to close instance");
            return 1;
        }
    }

    private static async Task<int> HandleMonitor(IInstancesService instancesService, IGroupService groupService, string[] args)
    {
        var selectedGroup = await groupService.GetSelectedGroupAsync();
        if (selectedGroup == null)
        {
            Console.WriteLine("No group selected. Use the group service to select a group first.");
            return 1;
        }

        Console.WriteLine($"Starting instance monitoring for group: {selectedGroup.GroupName}");
        Console.WriteLine("Press Ctrl+C to stop monitoring...");
        
        // Set up event handlers
        instancesService.InstanceDetected += (sender, e) =>
        {
            Console.WriteLine($"[{e.Timestamp:HH:mm:ss}] NEW: {e.Instance.InstanceId} - {e.Instance.WorldName}");
        };
        
        instancesService.InstanceUpdated += (sender, e) =>
        {
            Console.WriteLine($"[{e.Timestamp:HH:mm:ss}] UPD: {e.NewInstance.InstanceId} - Users: {e.OldInstance.UserCount}->{e.NewInstance.UserCount}");
        };
        
        instancesService.InstanceClosed += (sender, e) =>
        {
            Console.WriteLine($"[{e.Timestamp:HH:mm:ss}] CLS: {e.Instance.InstanceId} - {e.ClosedBy}");
        };
        
        var success = await instancesService.StartMonitoringAsync(selectedGroup.GroupId);
        
        if (!success)
        {
            Console.WriteLine("✗ Failed to start monitoring");
            return 1;
        }

        Console.WriteLine("✓ Monitoring started");
        
        // Wait for Ctrl+C
        var tcs = new TaskCompletionSource<bool>();
        Console.CancelKeyPress += (sender, e) =>
        {
            e.Cancel = true;
            tcs.SetResult(true);
        };
        
        await tcs.Task;
        
        Console.WriteLine("\nStopping monitoring...");
        await instancesService.StopMonitoringAsync();
        Console.WriteLine("✓ Monitoring stopped");
        
        return 0;
    }

    private static async Task<int> HandleStopMonitoring(IInstancesService instancesService)
    {
        Console.WriteLine("Stopping instance monitoring...");
        
        var success = await instancesService.StopMonitoringAsync();
        
        if (success)
        {
            Console.WriteLine("✓ Monitoring stopped");
            return 0;
        }
        else
        {
            Console.WriteLine("✗ Failed to stop monitoring");
            return 1;
        }
    }

    private static async Task<int> HandleStatus(IInstancesService instancesService)
    {
        var isMonitoring = await instancesService.IsMonitoringAsync();
        
        Console.WriteLine($"Monitoring Status: {(isMonitoring ? "✓ Active" : "✗ Inactive")}");
        
        return 0;
    }

    private static int ShowHelp()
    {
        Console.WriteLine("VRC Group Guardian - Instance Service CLI");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  dotnet run list                    - List all group instances");
        Console.WriteLine("  dotnet run get <instance-id>       - Get instance details");
        Console.WriteLine("  dotnet run close <instance-id>     - Close an instance");
        Console.WriteLine("  dotnet run monitor                 - Start real-time monitoring");
        Console.WriteLine("  dotnet run stop                    - Stop monitoring");
        Console.WriteLine("  dotnet run status                  - Show monitoring status");
        Console.WriteLine("  dotnet run help                    - Show this help");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  dotnet run list");
        Console.WriteLine("  dotnet run get wrld_12345:67890~private");
        Console.WriteLine("  dotnet run close wrld_12345:67890~private");
        Console.WriteLine("  dotnet run monitor");
        Console.WriteLine();
        Console.WriteLine("Note: You must have a group selected via the group service first.");
        return 0;
    }

    private static int HandleUnknownCommand(string command)
    {
        Console.WriteLine($"Unknown command: {command}");
        Console.WriteLine("Use 'dotnet run help' to see available commands.");
        return 1;
    }
}