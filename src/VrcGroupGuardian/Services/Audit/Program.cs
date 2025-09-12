using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using VrcGroupGuardian.Infrastructure;
using VrcGroupGuardian.Models;
using VrcGroupGuardian.Services.Audit;
using VrcGroupGuardian.Services.VrcApi;

namespace VrcGroupGuardian.Services.Audit.Cli;

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
        var auditService = _serviceProvider!.GetRequiredService<IAuditService>();
        
        try
        {
            return command switch
            {
                "list" => await HandleList(auditService, args),
                "export" => await HandleExport(auditService, args),
                "stats" => await HandleStats(auditService, args),
                "purge" => await HandlePurge(auditService, args),
                "sync" => await HandleSync(auditService, args),
                "log" => await HandleLog(auditService, args),
                "start" => await HandleStart(auditService),
                "stop" => await HandleStop(auditService),
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
        
        services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Warning));
        services.AddSingleton<ISettingsStore, SettingsStore>();
        services.AddSingleton<IRateLimitService, RateLimitService>();
        services.AddSingleton<ISecureStorage, SecureStorage>();
        services.AddSingleton<IVrchatHttpClientFactory, VrchatHttpClientFactory>();
        services.AddSingleton<IVrcApiService, VrcApiService>();
        services.AddSingleton<IAuditService, AuditService>();
        
        _serviceProvider = services.BuildServiceProvider();
    }

    private static async Task<int> HandleList(IAuditService auditService, string[] args)
    {
        DateTime? startDate = null;
        DateTime? endDate = null;
        AuditActionType? actionType = null;
        AuditTargetType? targetType = null;
        int? limit = null;

        // Parse optional parameters
        for (int i = 1; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg.StartsWith("--start="))
            {
                if (DateTime.TryParse(arg.Substring(8), out var start))
                    startDate = start;
            }
            else if (arg.StartsWith("--end="))
            {
                if (DateTime.TryParse(arg.Substring(6), out var end))
                    endDate = end;
            }
            else if (arg.StartsWith("--action="))
            {
                if (Enum.TryParse<AuditActionType>(arg.Substring(9), true, out var action))
                    actionType = action;
            }
            else if (arg.StartsWith("--target="))
            {
                if (Enum.TryParse<AuditTargetType>(arg.Substring(9), true, out var target))
                    targetType = target;
            }
            else if (arg.StartsWith("--limit="))
            {
                if (int.TryParse(arg.Substring(8), out var lim))
                    limit = lim;
            }
        }

        var records = await auditService.GetAuditRecordsAsync(startDate, endDate, actionType, targetType, limit);
        
        if (records.Count == 0)
        {
            Console.WriteLine("No audit records found.");
            return 0;
        }

        Console.WriteLine($"Audit Records ({records.Count} found):");
        Console.WriteLine();
        Console.WriteLine("Timestamp".PadRight(20) + "Action".PadRight(15) + "Target".PadRight(12) + "Actor".PadRight(20) + "Success".PadRight(8) + "Details");
        Console.WriteLine(new string('-', 120));

        foreach (var record in records.Take(50)) // Limit display to 50 for readability
        {
            var timestamp = record.Timestamp.ToString("MM-dd HH:mm:ss");
            var success = record.Success ? "✓" : "✗";
            var actor = !string.IsNullOrEmpty(record.ActorDisplayName) ? record.ActorDisplayName : "System";
            var details = record.Details.Length > 40 ? record.Details.Substring(0, 37) + "..." : record.Details;
            
            Console.WriteLine($"{timestamp.PadRight(20)}{record.ActionType.ToString().PadRight(15)}{record.TargetType.ToString().PadRight(12)}{actor.PadRight(20)}{success.PadRight(8)}{details}");
        }

        if (records.Count > 50)
        {
            Console.WriteLine($"\n... and {records.Count - 50} more records. Use export command for full data.");
        }

        return 0;
    }

    private static async Task<int> HandleExport(IAuditService auditService, string[] args)
    {
        if (args.Length < 2)
        {
            Console.WriteLine("Usage: dotnet run export <format> [options]");
            Console.WriteLine("Formats: csv, json");
            Console.WriteLine("Options: --start=YYYY-MM-DD --end=YYYY-MM-DD --output=filename");
            return 1;
        }

        var format = args[1].ToLower();
        if (format != "csv" && format != "json")
        {
            Console.WriteLine("Invalid format. Use 'csv' or 'json'.");
            return 1;
        }

        DateTime? startDate = null;
        DateTime? endDate = null;
        string? outputFile = null;

        // Parse optional parameters
        for (int i = 2; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg.StartsWith("--start="))
            {
                if (DateTime.TryParse(arg.Substring(8), out var start))
                    startDate = start;
            }
            else if (arg.StartsWith("--end="))
            {
                if (DateTime.TryParse(arg.Substring(6), out var end))
                    endDate = end;
            }
            else if (arg.StartsWith("--output="))
            {
                outputFile = arg.Substring(9);
            }
        }

        // Generate default filename if not specified
        if (string.IsNullOrEmpty(outputFile))
        {
            var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            outputFile = $"audit-export-{timestamp}.{format}";
        }

        Console.WriteLine($"Exporting audit records to {format.ToUpper()} format...");
        Console.WriteLine($"Output file: {outputFile}");
        if (startDate.HasValue) Console.WriteLine($"Start date: {startDate:yyyy-MM-dd}");
        if (endDate.HasValue) Console.WriteLine($"End date: {endDate:yyyy-MM-dd}");

        bool success;
        if (format == "csv")
        {
            success = await auditService.ExportToCsvAsync(outputFile, startDate, endDate);
        }
        else
        {
            success = await auditService.ExportToJsonAsync(outputFile, startDate, endDate);
        }

        if (success)
        {
            Console.WriteLine("✓ Export completed successfully");
            return 0;
        }
        else
        {
            Console.WriteLine("✗ Export failed");
            return 1;
        }
    }

    private static async Task<int> HandleStats(IAuditService auditService, string[] args)
    {
        TimeSpan? period = null;
        
        if (args.Length > 1)
        {
            var periodArg = args[1];
            if (periodArg.EndsWith("d"))
            {
                if (int.TryParse(periodArg[..^1], out var days))
                    period = TimeSpan.FromDays(days);
            }
            else if (periodArg.EndsWith("h"))
            {
                if (int.TryParse(periodArg[..^1], out var hours))
                    period = TimeSpan.FromHours(hours);
            }
            else if (periodArg.EndsWith("m"))
            {
                if (int.TryParse(periodArg[..^1], out var minutes))
                    period = TimeSpan.FromMinutes(minutes);
            }
        }

        var stats = await auditService.GetAuditStatsAsync(period);
        
        Console.WriteLine("Audit Statistics");
        Console.WriteLine("================");
        
        if (stats.Period.HasValue)
        {
            Console.WriteLine($"Period: Last {stats.Period.Value.TotalDays:F0} days");
        }
        else
        {
            Console.WriteLine("Period: All time");
        }
        
        Console.WriteLine($"Generated: {stats.GeneratedAt:yyyy-MM-dd HH:mm:ss} UTC");
        Console.WriteLine();
        
        Console.WriteLine($"Total Records: {stats.TotalRecords}");
        Console.WriteLine($"Successful Actions: {stats.SuccessfulActions} ({(stats.TotalRecords > 0 ? stats.SuccessfulActions * 100.0 / stats.TotalRecords : 0):F1}%)");
        Console.WriteLine($"Failed Actions: {stats.FailedActions} ({(stats.TotalRecords > 0 ? stats.FailedActions * 100.0 / stats.TotalRecords : 0):F1}%)");
        
        if (stats.ActionTypeCounts.Count > 0)
        {
            Console.WriteLine("\nActions by Type:");
            foreach (var actionType in stats.ActionTypeCounts.OrderByDescending(kv => kv.Value))
            {
                Console.WriteLine($"  {actionType.Key}: {actionType.Value}");
            }
        }
        
        if (stats.TargetTypeCounts.Count > 0)
        {
            Console.WriteLine("\nActions by Target:");
            foreach (var targetType in stats.TargetTypeCounts.OrderByDescending(kv => kv.Value))
            {
                Console.WriteLine($"  {targetType.Key}: {targetType.Value}");
            }
        }

        return 0;
    }

    private static async Task<int> HandlePurge(IAuditService auditService, string[] args)
    {
        if (args.Length < 2)
        {
            Console.WriteLine("Usage: dotnet run purge <retention-period>");
            Console.WriteLine("Examples: 30d (30 days), 6m (6 months), 1y (1 year)");
            return 1;
        }

        var retentionArg = args[1];
        TimeSpan retentionPeriod;
        
        if (retentionArg.EndsWith("d"))
        {
            if (int.TryParse(retentionArg[..^1], out var days))
                retentionPeriod = TimeSpan.FromDays(days);
            else
            {
                Console.WriteLine("Invalid retention period format.");
                return 1;
            }
        }
        else if (retentionArg.EndsWith("m"))
        {
            if (int.TryParse(retentionArg[..^1], out var months))
                retentionPeriod = TimeSpan.FromDays(months * 30);
            else
            {
                Console.WriteLine("Invalid retention period format.");
                return 1;
            }
        }
        else if (retentionArg.EndsWith("y"))
        {
            if (int.TryParse(retentionArg[..^1], out var years))
                retentionPeriod = TimeSpan.FromDays(years * 365);
            else
            {
                Console.WriteLine("Invalid retention period format.");
                return 1;
            }
        }
        else
        {
            Console.WriteLine("Invalid retention period format. Use format like: 30d, 6m, 1y");
            return 1;
        }

        Console.WriteLine($"Purging audit records older than {retentionPeriod.TotalDays:F0} days...");
        Console.WriteLine("This action cannot be undone. Continue? (y/N)");
        
        var response = Console.ReadLine();
        if (response?.ToLower() != "y" && response?.ToLower() != "yes")
        {
            Console.WriteLine("Purge cancelled.");
            return 0;
        }

        var success = await auditService.PurgeOldRecordsAsync(retentionPeriod);
        
        if (success)
        {
            Console.WriteLine("✓ Purge completed successfully");
            return 0;
        }
        else
        {
            Console.WriteLine("✗ Purge failed");
            return 1;
        }
    }

    private static async Task<int> HandleSync(IAuditService auditService, string[] args)
    {
        if (args.Length < 2)
        {
            Console.WriteLine("Usage: dotnet run sync <group-id>");
            return 1;
        }

        var groupId = args[1];
        
        Console.WriteLine($"Syncing audit records with VRChat for group: {groupId}");
        
        var newRecords = await auditService.SyncWithVrchatAuditLogsAsync(groupId);
        
        Console.WriteLine($"✓ Sync completed - {newRecords.Count} new records imported");
        
        if (newRecords.Count > 0)
        {
            Console.WriteLine("\nNew records:");
            foreach (var record in newRecords.Take(10))
            {
                Console.WriteLine($"  {record.Timestamp:MM-dd HH:mm} - {record.ActionType} - {record.TargetDisplayName}");
            }
            
            if (newRecords.Count > 10)
            {
                Console.WriteLine($"  ... and {newRecords.Count - 10} more records");
            }
        }

        return 0;
    }

    private static async Task<int> HandleLog(IAuditService auditService, string[] args)
    {
        if (args.Length < 4)
        {
            Console.WriteLine("Usage: dotnet run log <action> <target-type> <target-id> [target-name] [--actor=name] [--details=text] [--success=true/false]");
            Console.WriteLine("Actions: AutoClose, ManualClose, KickMember, BanMember, etc.");
            Console.WriteLine("Target Types: Instance, Member, Policy, Session");
            return 1;
        }

        if (!Enum.TryParse<AuditActionType>(args[1], true, out var actionType))
        {
            Console.WriteLine($"Invalid action type: {args[1]}");
            return 1;
        }

        if (!Enum.TryParse<AuditTargetType>(args[2], true, out var targetType))
        {
            Console.WriteLine($"Invalid target type: {args[2]}");
            return 1;
        }

        var targetId = args[3];
        var targetName = args.Length > 4 ? args[4] : "";
        
        string? actorName = null;
        string? details = null;
        bool success = true;
        
        // Parse optional parameters
        for (int i = 5; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg.StartsWith("--actor="))
                actorName = arg.Substring(8);
            else if (arg.StartsWith("--details="))
                details = arg.Substring(10);
            else if (arg.StartsWith("--success="))
                bool.TryParse(arg.Substring(10), out success);
        }

        var logSuccess = await auditService.LogActionAsync(actionType, targetId, targetName, targetType, 
            null, actorName, details, success);
        
        if (logSuccess)
        {
            Console.WriteLine("✓ Audit record logged successfully");
            return 0;
        }
        else
        {
            Console.WriteLine("✗ Failed to log audit record");
            return 1;
        }
    }

    private static async Task<int> HandleStart(IAuditService auditService)
    {
        Console.WriteLine("Starting real-time audit logging...");
        
        var success = await auditService.StartRealTimeLoggingAsync();
        
        if (success)
        {
            Console.WriteLine("✓ Real-time audit logging started");
            return 0;
        }
        else
        {
            Console.WriteLine("✗ Failed to start real-time audit logging");
            return 1;
        }
    }

    private static async Task<int> HandleStop(IAuditService auditService)
    {
        Console.WriteLine("Stopping real-time audit logging...");
        
        var success = await auditService.StopRealTimeLoggingAsync();
        
        if (success)
        {
            Console.WriteLine("✓ Real-time audit logging stopped");
            return 0;
        }
        else
        {
            Console.WriteLine("✗ Failed to stop real-time audit logging");
            return 1;
        }
    }

    private static int ShowHelp()
    {
        Console.WriteLine("VRC Group Guardian - Audit Service CLI");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  dotnet run list [options]             - List audit records");
        Console.WriteLine("  dotnet run export <format> [options]  - Export audit records (csv/json)");
        Console.WriteLine("  dotnet run stats [period]             - Show audit statistics");
        Console.WriteLine("  dotnet run purge <retention>          - Purge old audit records");
        Console.WriteLine("  dotnet run sync <group-id>            - Sync with VRChat audit logs");
        Console.WriteLine("  dotnet run log <action> <target-type> <target-id> [options] - Log a manual audit record");
        Console.WriteLine("  dotnet run start                      - Start real-time logging");
        Console.WriteLine("  dotnet run stop                       - Stop real-time logging");
        Console.WriteLine("  dotnet run help                       - Show this help");
        Console.WriteLine();
        Console.WriteLine("List options:");
        Console.WriteLine("  --start=YYYY-MM-DD    Start date filter");
        Console.WriteLine("  --end=YYYY-MM-DD      End date filter");
        Console.WriteLine("  --action=ActionType   Action type filter");
        Console.WriteLine("  --target=TargetType   Target type filter");
        Console.WriteLine("  --limit=N             Limit number of results");
        Console.WriteLine();
        Console.WriteLine("Export options:");
        Console.WriteLine("  --start=YYYY-MM-DD    Start date filter");
        Console.WriteLine("  --end=YYYY-MM-DD      End date filter");
        Console.WriteLine("  --output=filename     Output filename");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  dotnet run list --start=2024-01-01 --limit=50");
        Console.WriteLine("  dotnet run export csv --output=audit.csv");
        Console.WriteLine("  dotnet run stats 7d");
        Console.WriteLine("  dotnet run purge 30d");
        Console.WriteLine("  dotnet run sync grp_12345678-1234-1234-1234-123456789012");
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