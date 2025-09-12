using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using VrcGroupGuardian.Infrastructure;
using VrcGroupGuardian.Services.Auth;
using VrcGroupGuardian.Services.Groups;
using VrcGroupGuardian.Services.Members;
using VrcGroupGuardian.Services.VrcApi;

namespace VrcGroupGuardian.Services.Members.Cli;

public class Program
{
    private static IServiceProvider? _serviceProvider;

    public static async Task<int> RunCli(string[] args)
    {
        ConfigureServices();
        
        if (args.Length == 0)
        {
            ShowHelp();
            return 1;
        }

        var command = args[0].ToLower();
        var membersService = _serviceProvider!.GetRequiredService<IMembersService>();
        var groupService = _serviceProvider!.GetRequiredService<IGroupService>();
        
        try
        {
            return command switch
            {
                "list" => await HandleList(membersService, groupService, args),
                "get" => await HandleGet(membersService, groupService, args),
                "search" => await HandleSearch(membersService, groupService, args),
                "kick" => await HandleKick(membersService, groupService, args),
                "ban" => await HandleBan(membersService, groupService, args),
                "unban" => await HandleUnban(membersService, groupService, args),
                "bulk-kick" => await HandleBulkKick(membersService, groupService, args),
                "refresh" => await HandleRefresh(membersService, groupService),
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
        services.AddSingleton<IRateLimitService, RateLimitService>();
        services.AddSingleton<ISecureStorage, SecureStorage>();
        services.AddSingleton<ISettingsStore, SettingsStore>();
        services.AddSingleton<IVrchatHttpClientFactory, VrchatHttpClientFactory>();
        services.AddSingleton<IVrcApiService, VrcApiService>();
        services.AddSingleton<IAuthService, AuthService>();
        services.AddSingleton<IGroupService, GroupService>();
        services.AddSingleton<IMembersService, MembersService>();
        
        _serviceProvider = services.BuildServiceProvider();
    }

    private static async Task<int> HandleList(IMembersService membersService, IGroupService groupService, string[] args)
    {
        var selectedGroup = await groupService.GetSelectedGroupAsync();
        if (selectedGroup == null)
        {
            Console.WriteLine("No group selected. Use the group service to select a group first.");
            return 1;
        }

        string? roleFilter = null;
        if (args.Length > 1 && args[1].StartsWith("--role="))
        {
            roleFilter = args[1].Substring(7);
        }

        Console.WriteLine($"Listing members for group: {selectedGroup.GroupName}");
        if (!string.IsNullOrEmpty(roleFilter))
        {
            Console.WriteLine($"Filtered by role: {roleFilter}");
        }
        
        var members = roleFilter != null 
            ? await membersService.GetMembersByRoleAsync(selectedGroup.GroupId, roleFilter)
            : await membersService.GetGroupMembersAsync(selectedGroup.GroupId);
        
        if (members.Count == 0)
        {
            Console.WriteLine("No members found.");
            return 0;
        }

        Console.WriteLine();
        Console.WriteLine("User ID".PadRight(30) + "Display Name".PadRight(25) + "Username".PadRight(20) + "Role".PadRight(15) + "Permissions");
        Console.WriteLine(new string('-', 110));
        
        foreach (var member in members)
        {
            var permissions = new List<string>();
            if (member.CanKick) permissions.Add("Kick");
            if (member.CanBan) permissions.Add("Ban");
            
            Console.WriteLine($"{member.UserId.PadRight(30)}{member.DisplayName.PadRight(25)}{member.Username.PadRight(20)}{member.Role.PadRight(15)}{string.Join(", ", permissions)}");
        }
        
        Console.WriteLine();
        Console.WriteLine($"Total: {members.Count} members");
        
        return 0;
    }

    private static async Task<int> HandleGet(IMembersService membersService, IGroupService groupService, string[] args)
    {
        if (args.Length < 2)
        {
            Console.WriteLine("Usage: dotnet run get <user-id>");
            return 1;
        }

        var selectedGroup = await groupService.GetSelectedGroupAsync();
        if (selectedGroup == null)
        {
            Console.WriteLine("No group selected. Use the group service to select a group first.");
            return 1;
        }

        var userId = args[1];
        var member = await membersService.GetMemberAsync(selectedGroup.GroupId, userId);
        
        if (member == null)
        {
            Console.WriteLine($"Member not found: {userId}");
            return 1;
        }

        Console.WriteLine("Member Details:");
        Console.WriteLine($"  User ID: {member.UserId}");
        Console.WriteLine($"  Display Name: {member.DisplayName}");
        Console.WriteLine($"  Username: {member.Username}");
        Console.WriteLine($"  Role: {member.Role}");
        Console.WriteLine($"  Permission Level: {member.PermissionLevel}");
        Console.WriteLine($"  Can Kick: {(member.CanKick ? "Yes" : "No")}");
        Console.WriteLine($"  Can Ban: {(member.CanBan ? "Yes" : "No")}");
        Console.WriteLine($"  Joined At: {member.JoinedAt:yyyy-MM-dd HH:mm:ss} UTC");
        Console.WriteLine($"  Last Seen: {member.LastSeen:yyyy-MM-dd HH:mm:ss} UTC");
        
        return 0;
    }

    private static async Task<int> HandleSearch(IMembersService membersService, IGroupService groupService, string[] args)
    {
        if (args.Length < 2)
        {
            Console.WriteLine("Usage: dotnet run search <search-term>");
            return 1;
        }

        var selectedGroup = await groupService.GetSelectedGroupAsync();
        if (selectedGroup == null)
        {
            Console.WriteLine("No group selected. Use the group service to select a group first.");
            return 1;
        }

        var searchTerm = args[1];
        var members = await membersService.SearchMembersAsync(selectedGroup.GroupId, searchTerm);
        
        Console.WriteLine($"Search results for '{searchTerm}' in group: {selectedGroup.GroupName}");
        
        if (members.Count == 0)
        {
            Console.WriteLine("No members found matching the search term.");
            return 0;
        }

        Console.WriteLine();
        Console.WriteLine("User ID".PadRight(30) + "Display Name".PadRight(25) + "Username".PadRight(20) + "Role");
        Console.WriteLine(new string('-', 90));
        
        foreach (var member in members)
        {
            Console.WriteLine($"{member.UserId.PadRight(30)}{member.DisplayName.PadRight(25)}{member.Username.PadRight(20)}{member.Role}");
        }
        
        Console.WriteLine();
        Console.WriteLine($"Found: {members.Count} members");
        
        return 0;
    }

    private static async Task<int> HandleKick(IMembersService membersService, IGroupService groupService, string[] args)
    {
        if (args.Length < 3)
        {
            Console.WriteLine("Usage: dotnet run kick <user-id> \"<reason>\"");
            return 1;
        }

        var selectedGroup = await groupService.GetSelectedGroupAsync();
        if (selectedGroup == null)
        {
            Console.WriteLine("No group selected. Use the group service to select a group first.");
            return 1;
        }

        var userId = args[1];
        var reason = args[2];
        
        Console.WriteLine($"Kicking member {userId} from group {selectedGroup.GroupName}");
        Console.WriteLine($"Reason: {reason}");
        
        var result = await membersService.KickMemberAsync(selectedGroup.GroupId, userId, reason);
        
        if (result.Success)
        {
            Console.WriteLine("✓ Member kicked successfully");
            return 0;
        }
        else
        {
            Console.WriteLine($"✗ Failed to kick member: {result.Message}");
            return 1;
        }
    }

    private static async Task<int> HandleBan(IMembersService membersService, IGroupService groupService, string[] args)
    {
        if (args.Length < 3)
        {
            Console.WriteLine("Usage: dotnet run ban <user-id> \"<reason>\"");
            return 1;
        }

        var selectedGroup = await groupService.GetSelectedGroupAsync();
        if (selectedGroup == null)
        {
            Console.WriteLine("No group selected. Use the group service to select a group first.");
            return 1;
        }

        var userId = args[1];
        var reason = args[2];
        
        Console.WriteLine($"Banning member {userId} from group {selectedGroup.GroupName}");
        Console.WriteLine($"Reason: {reason}");
        
        var result = await membersService.BanMemberAsync(selectedGroup.GroupId, userId, reason);
        
        if (result.Success)
        {
            Console.WriteLine("✓ Member banned successfully");
            return 0;
        }
        else
        {
            Console.WriteLine($"✗ Failed to ban member: {result.Message}");
            return 1;
        }
    }

    private static async Task<int> HandleUnban(IMembersService membersService, IGroupService groupService, string[] args)
    {
        if (args.Length < 2)
        {
            Console.WriteLine("Usage: dotnet run unban <user-id>");
            return 1;
        }

        var selectedGroup = await groupService.GetSelectedGroupAsync();
        if (selectedGroup == null)
        {
            Console.WriteLine("No group selected. Use the group service to select a group first.");
            return 1;
        }

        var userId = args[1];
        
        Console.WriteLine($"Unbanning member {userId} from group {selectedGroup.GroupName}");
        
        var result = await membersService.UnbanMemberAsync(selectedGroup.GroupId, userId);
        
        if (result.Success)
        {
            Console.WriteLine("✓ Member unbanned successfully");
            return 0;
        }
        else
        {
            Console.WriteLine($"✗ Failed to unban member: {result.Message}");
            return 1;
        }
    }

    private static async Task<int> HandleBulkKick(IMembersService membersService, IGroupService groupService, string[] args)
    {
        if (args.Length < 3)
        {
            Console.WriteLine("Usage: dotnet run bulk-kick \"<reason>\" <user-id1> <user-id2> ...");
            return 1;
        }

        var selectedGroup = await groupService.GetSelectedGroupAsync();
        if (selectedGroup == null)
        {
            Console.WriteLine("No group selected. Use the group service to select a group first.");
            return 1;
        }

        var reason = args[1];
        var memberIds = args.Skip(2).ToArray();
        
        Console.WriteLine($"Bulk kicking {memberIds.Length} members from group {selectedGroup.GroupName}");
        Console.WriteLine($"Reason: {reason}");
        Console.WriteLine("This may take a moment...");
        
        var result = await membersService.BulkKickMembersAsync(selectedGroup.GroupId, memberIds, reason);
        
        Console.WriteLine();
        Console.WriteLine($"Bulk kick results:");
        Console.WriteLine($"  Total attempted: {result.TotalAttempted}");
        Console.WriteLine($"  Successful: {result.SuccessfulActions}");
        Console.WriteLine($"  Failed: {result.FailedActions}");
        
        if (result.SuccessfulMembers.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("Successfully kicked:");
            foreach (var memberId in result.SuccessfulMembers)
            {
                Console.WriteLine($"  ✓ {memberId}");
            }
        }
        
        if (result.FailedMembers.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("Failed to kick:");
            foreach (var failure in result.FailedMembers)
            {
                Console.WriteLine($"  ✗ {failure.Key}: {failure.Value}");
            }
        }
        
        return result.FailedActions == 0 ? 0 : 1;
    }

    private static async Task<int> HandleRefresh(IMembersService membersService, IGroupService groupService)
    {
        var selectedGroup = await groupService.GetSelectedGroupAsync();
        if (selectedGroup == null)
        {
            Console.WriteLine("No group selected. Use the group service to select a group first.");
            return 1;
        }

        Console.WriteLine($"Refreshing member cache for group: {selectedGroup.GroupName}");
        
        var success = await membersService.RefreshMemberCacheAsync(selectedGroup.GroupId);
        
        if (success)
        {
            Console.WriteLine("✓ Member cache refreshed successfully");
            return 0;
        }
        else
        {
            Console.WriteLine("✗ Failed to refresh member cache");
            return 1;
        }
    }

    private static int ShowHelp()
    {
        Console.WriteLine("VRC Group Guardian - Members Service CLI");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  dotnet run list [--role=<role>]       - List all group members (optionally filtered by role)");
        Console.WriteLine("  dotnet run get <user-id>              - Get member details");
        Console.WriteLine("  dotnet run search <search-term>       - Search members by name or username");
        Console.WriteLine("  dotnet run kick <user-id> \"<reason>\"  - Kick a member from the group");
        Console.WriteLine("  dotnet run ban <user-id> \"<reason>\"   - Ban a member from the group");
        Console.WriteLine("  dotnet run unban <user-id>            - Unban a member from the group");
        Console.WriteLine("  dotnet run bulk-kick \"<reason>\" <user-id1> <user-id2> ... - Kick multiple members");
        Console.WriteLine("  dotnet run refresh                    - Refresh member cache");
        Console.WriteLine("  dotnet run help                       - Show this help");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  dotnet run list");
        Console.WriteLine("  dotnet run list --role=Moderator");
        Console.WriteLine("  dotnet run get usr_12345678-1234-1234-1234-123456789012");
        Console.WriteLine("  dotnet run search \"john\"");
        Console.WriteLine("  dotnet run kick usr_12345678-1234-1234-1234-123456789012 \"Spam\"");
        Console.WriteLine("  dotnet run ban usr_12345678-1234-1234-1234-123456789012 \"Harassment\"");
        Console.WriteLine("  dotnet run bulk-kick \"Clean up\" usr_123 usr_456 usr_789");
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