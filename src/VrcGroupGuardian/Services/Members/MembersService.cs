using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using VrcGroupGuardian.Models;
using VrcGroupGuardian.Services.Auth;
using VrcGroupGuardian.Services.Groups;
using VrcGroupGuardian.Services.VrcApi;

namespace VrcGroupGuardian.Services.Members;

public interface IMembersService
{
    Task<List<GroupMember>> GetGroupMembersAsync(string groupId);
    Task<GroupMember?> GetMemberAsync(string groupId, string userId);
    Task<GroupMember?> GetMemberDetailsAsync(string groupId, string userId);
    Task<List<GroupMember>> SearchMembersAsync(string groupId, string searchTerm);
    Task<List<GroupMember>> GetMembersByRoleAsync(string groupId, string role);
    Task<MemberActionResult> KickMemberAsync(string groupId, string userId, string reason);
    Task<MemberActionResult> BanMemberAsync(string groupId, string userId, string reason);
    Task<MemberActionResult> UnbanMemberAsync(string groupId, string userId);
    Task<BanStatus> GetMemberBanStatusAsync(string groupId, string userId);
    Task<BulkMemberResult> BulkKickMembersAsync(string groupId, string[] memberIds, string reason);
    Task<BulkMemberResult> BulkBanMembersAsync(string groupId, string[] memberIds, string reason);
    Task<bool> RefreshMemberCacheAsync(string groupId);
    Task<ExportResult> ExportMembersAsync(string groupId, List<GroupMember> members);

    event EventHandler<MemberJoinedEventArgs>? MemberJoined;
    event EventHandler<MemberLeftEventArgs>? MemberLeft;
    event EventHandler<MemberUpdatedEventArgs>? MemberUpdated;
    event EventHandler<MemberActionEventArgs>? MemberKicked;
    event EventHandler<MemberActionEventArgs>? MemberBanned;
}

public class MembersService : IMembersService
{
    private readonly IVrcApiService _vrcApiService;
    private readonly IAuthService _authService;
    private readonly IGroupService _groupService;
    private readonly ILogger<MembersService> _logger;
    
    private readonly ConcurrentDictionary<string, CachedMemberList> _memberCache = new();
    private readonly TimeSpan _cacheExpiry = TimeSpan.FromMinutes(5);

    public event EventHandler<MemberJoinedEventArgs>? MemberJoined;
    public event EventHandler<MemberLeftEventArgs>? MemberLeft;
    public event EventHandler<MemberUpdatedEventArgs>? MemberUpdated;
    public event EventHandler<MemberActionEventArgs>? MemberKicked;
    public event EventHandler<MemberActionEventArgs>? MemberBanned;

    public MembersService(
        IVrcApiService vrcApiService,
        IAuthService authService,
        IGroupService groupService,
        ILogger<MembersService> logger)
    {
        _vrcApiService = vrcApiService;
        _authService = authService;
        _groupService = groupService;
        _logger = logger;
    }

    public async Task<List<GroupMember>> GetGroupMembersAsync(string groupId)
    {
        if (string.IsNullOrEmpty(groupId))
            return new List<GroupMember>();

        if (!await _authService.IsAuthenticatedAsync())
        {
            _logger.LogWarning("Cannot get group members: not authenticated");
            return new List<GroupMember>();
        }

        // Check cache first
        if (_memberCache.TryGetValue(groupId, out var cachedMembers) && 
            DateTime.UtcNow - cachedMembers.CachedAt < _cacheExpiry)
        {
            return cachedMembers.Members;
        }

        try
        {
            var members = await _vrcApiService.GetGroupMembersAsync(groupId);
            
            // Update cache
            _memberCache.AddOrUpdate(groupId, new CachedMemberList { Members = members, CachedAt = DateTime.UtcNow },
                (key, old) => new CachedMemberList { Members = members, CachedAt = DateTime.UtcNow });

            _logger.LogDebug("Retrieved {MemberCount} members for group {GroupId}", members.Count, groupId);
            return members;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get group members for {GroupId}", groupId);
            
            // Return cached data if available, even if expired
            if (cachedMembers != null)
            {
                _logger.LogWarning("Returning expired cache data for group {GroupId}", groupId);
                return cachedMembers.Members;
            }
            
            return new List<GroupMember>();
        }
    }

    public async Task<GroupMember?> GetMemberAsync(string groupId, string userId)
    {
        if (string.IsNullOrEmpty(groupId) || string.IsNullOrEmpty(userId))
            return null;

        var members = await GetGroupMembersAsync(groupId);
        return members.FirstOrDefault(m => m.UserId.Equals(userId, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<List<GroupMember>> SearchMembersAsync(string groupId, string searchTerm)
    {
        if (string.IsNullOrEmpty(groupId) || string.IsNullOrEmpty(searchTerm))
            return new List<GroupMember>();

        var members = await GetGroupMembersAsync(groupId);
        var searchTermLower = searchTerm.ToLower();
        
        return members.Where(m => 
            m.DisplayName.ToLower().Contains(searchTermLower) ||
            m.Username.ToLower().Contains(searchTermLower) ||
            m.Role.ToLower().Contains(searchTermLower)
        ).ToList();
    }

    public async Task<List<GroupMember>> GetMembersByRoleAsync(string groupId, string role)
    {
        if (string.IsNullOrEmpty(groupId) || string.IsNullOrEmpty(role))
            return new List<GroupMember>();

        var members = await GetGroupMembersAsync(groupId);
        return members.Where(m => m.Role.Equals(role, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    public async Task<MemberActionResult> KickMemberAsync(string groupId, string userId, string reason)
    {
        if (string.IsNullOrEmpty(groupId) || string.IsNullOrEmpty(userId))
        {
            return new MemberActionResult
            {
                Success = false,
                UserId = userId,
                Message = "Invalid group ID or user ID",
                Reason = reason
            };
        }

        if (!await _authService.IsAuthenticatedAsync())
        {
            return new MemberActionResult
            {
                Success = false,
                UserId = userId,
                Message = "Not authenticated",
                Reason = reason
            };
        }

        if (!await _groupService.CanManageMembersAsync(groupId))
        {
            return new MemberActionResult
            {
                Success = false,
                UserId = userId,
                Message = "Insufficient permissions to kick members",
                Reason = reason
            };
        }

        try
        {
            _logger.LogInformation("Kicking member {UserId} from group {GroupId} for reason: {Reason}", 
                userId, groupId, reason);

            var result = await _vrcApiService.KickGroupMemberAsync(groupId, userId);
            
            if (result.Success)
            {
                // Invalidate member cache
                _memberCache.TryRemove(groupId, out _);
                _logger.LogInformation("Successfully kicked member {UserId} from group {GroupId}", userId, groupId);
            }
            else
            {
                _logger.LogWarning("Failed to kick member {UserId} from group {GroupId}: {Message}", 
                    userId, groupId, result.Message);
            }

            result.Reason = reason;
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error kicking member {UserId} from group {GroupId}", userId, groupId);
            return new MemberActionResult
            {
                Success = false,
                UserId = userId,
                Message = "Network error during kick operation",
                Reason = reason
            };
        }
    }

    public async Task<MemberActionResult> BanMemberAsync(string groupId, string userId, string reason)
    {
        if (string.IsNullOrEmpty(groupId) || string.IsNullOrEmpty(userId))
        {
            return new MemberActionResult
            {
                Success = false,
                UserId = userId,
                Message = "Invalid group ID or user ID",
                Reason = reason
            };
        }

        if (!await _authService.IsAuthenticatedAsync())
        {
            return new MemberActionResult
            {
                Success = false,
                UserId = userId,
                Message = "Not authenticated",
                Reason = reason
            };
        }

        if (!await _groupService.CanManageMembersAsync(groupId))
        {
            return new MemberActionResult
            {
                Success = false,
                UserId = userId,
                Message = "Insufficient permissions to ban members",
                Reason = reason
            };
        }

        try
        {
            _logger.LogInformation("Banning member {UserId} from group {GroupId} for reason: {Reason}", 
                userId, groupId, reason);

            var result = await _vrcApiService.BanGroupMemberAsync(groupId, userId);
            
            if (result.Success)
            {
                // Invalidate member cache
                _memberCache.TryRemove(groupId, out _);
                _logger.LogInformation("Successfully banned member {UserId} from group {GroupId}", userId, groupId);
            }
            else
            {
                _logger.LogWarning("Failed to ban member {UserId} from group {GroupId}: {Message}", 
                    userId, groupId, result.Message);
            }

            result.Reason = reason;
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error banning member {UserId} from group {GroupId}", userId, groupId);
            return new MemberActionResult
            {
                Success = false,
                UserId = userId,
                Message = "Network error during ban operation",
                Reason = reason
            };
        }
    }

    public async Task<MemberActionResult> UnbanMemberAsync(string groupId, string userId)
    {
        if (string.IsNullOrEmpty(groupId) || string.IsNullOrEmpty(userId))
        {
            return new MemberActionResult
            {
                Success = false,
                UserId = userId,
                Message = "Invalid group ID or user ID",
                Reason = ""
            };
        }

        if (!await _authService.IsAuthenticatedAsync())
        {
            return new MemberActionResult
            {
                Success = false,
                UserId = userId,
                Message = "Not authenticated",
                Reason = ""
            };
        }

        if (!await _groupService.CanManageMembersAsync(groupId))
        {
            return new MemberActionResult
            {
                Success = false,
                UserId = userId,
                Message = "Insufficient permissions to unban members",
                Reason = ""
            };
        }

        try
        {
            _logger.LogInformation("Unbanning member {UserId} from group {GroupId}", userId, groupId);

            var result = await _vrcApiService.UnbanGroupMemberAsync(groupId, userId);
            
            if (result.Success)
            {
                _logger.LogInformation("Successfully unbanned member {UserId} from group {GroupId}", userId, groupId);
            }
            else
            {
                _logger.LogWarning("Failed to unban member {UserId} from group {GroupId}: {Message}", 
                    userId, groupId, result.Message);
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error unbanning member {UserId} from group {GroupId}", userId, groupId);
            return new MemberActionResult
            {
                Success = false,
                UserId = userId,
                Message = "Network error during unban operation",
                Reason = ""
            };
        }
    }

    public async Task<BanStatus> GetMemberBanStatusAsync(string groupId, string userId)
    {
        if (string.IsNullOrEmpty(groupId) || string.IsNullOrEmpty(userId))
        {
            return new BanStatus { IsBanned = false };
        }

        try
        {
            // Note: VRChat API doesn't have a direct "get ban status" endpoint
            // This would typically require checking the banned members list
            // For now, return placeholder implementation
            
            _logger.LogDebug("Checking ban status for member {UserId} in group {GroupId}", userId, groupId);
            
            // Try to get the member - if they're not in the member list, they might be banned
            var member = await GetMemberAsync(groupId, userId);
            if (member == null)
            {
                // Could be banned or simply not a member
                return new BanStatus { IsBanned = false }; // Cannot determine without ban list API
            }

            return new BanStatus { IsBanned = false };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking ban status for member {UserId} in group {GroupId}", userId, groupId);
            return new BanStatus { IsBanned = false };
        }
    }

    public async Task<BulkMemberResult> BulkKickMembersAsync(string groupId, string[] memberIds, string reason)
    {
        var result = new BulkMemberResult
        {
            TotalAttempted = memberIds.Length,
            SuccessfulMembers = new List<string>(),
            FailedMembers = new Dictionary<string, string>()
        };

        if (!await _authService.IsAuthenticatedAsync())
        {
            foreach (var memberId in memberIds)
            {
                result.FailedMembers[memberId] = "Not authenticated";
            }
            result.FailedActions = memberIds.Length;
            return result;
        }

        if (!await _groupService.CanManageMembersAsync(groupId))
        {
            foreach (var memberId in memberIds)
            {
                result.FailedMembers[memberId] = "Insufficient permissions";
            }
            result.FailedActions = memberIds.Length;
            return result;
        }

        _logger.LogInformation("Starting bulk kick operation for {MemberCount} members in group {GroupId}", 
            memberIds.Length, groupId);

        // Process members in parallel with some throttling
        var semaphore = new SemaphoreSlim(3, 3); // Limit to 3 concurrent operations
        var tasks = memberIds.Select(async memberId =>
        {
            await semaphore.WaitAsync();
            try
            {
                var kickResult = await KickMemberAsync(groupId, memberId, reason);
                return new { MemberId = memberId, Result = kickResult };
            }
            finally
            {
                semaphore.Release();
            }
        });

        var results = await Task.WhenAll(tasks);

        foreach (var memberResult in results)
        {
            if (memberResult.Result.Success)
            {
                result.SuccessfulMembers.Add(memberResult.MemberId);
                result.SuccessfulActions++;
            }
            else
            {
                result.FailedMembers[memberResult.MemberId] = memberResult.Result.Message;
                result.FailedActions++;
            }
        }

        _logger.LogInformation("Bulk kick operation completed: {Successful}/{Total} successful", 
            result.SuccessfulActions, result.TotalAttempted);

        return result;
    }

    public async Task<GroupMember?> GetMemberDetailsAsync(string groupId, string userId)
    {
        // For now, just delegate to GetMemberAsync - could be enhanced with more detailed info
        return await GetMemberAsync(groupId, userId);
    }

    public async Task<BulkMemberResult> BulkBanMembersAsync(string groupId, string[] memberIds, string reason)
    {
        var result = new BulkMemberResult
        {
            TotalAttempted = memberIds.Length,
            SuccessfulMembers = new List<string>(),
            FailedMembers = new Dictionary<string, string>()
        };

        if (!await _authService.IsAuthenticatedAsync())
        {
            foreach (var memberId in memberIds)
            {
                result.FailedMembers[memberId] = "Not authenticated";
            }
            result.FailedActions = memberIds.Length;
            return result;
        }

        if (!await _groupService.CanManageMembersAsync(groupId))
        {
            foreach (var memberId in memberIds)
            {
                result.FailedMembers[memberId] = "Insufficient permissions";
            }
            result.FailedActions = memberIds.Length;
            return result;
        }

        foreach (var memberId in memberIds)
        {
            try
            {
                var banResult = await BanMemberAsync(groupId, memberId, reason);
                if (banResult.Success)
                {
                    result.SuccessfulMembers.Add(memberId);
                    result.SuccessfulActions++;
                }
                else
                {
                    result.FailedMembers[memberId] = banResult.Message ?? "Unknown error";
                    result.FailedActions++;
                }
            }
            catch (Exception ex)
            {
                result.FailedMembers[memberId] = ex.Message;
                result.FailedActions++;
                _logger.LogError(ex, "Failed to ban member {MemberId} from group {GroupId}", memberId, groupId);
            }
        }

        _logger.LogInformation("Bulk ban completed for group {GroupId}: {Successful}/{Total} successful", 
            groupId, result.SuccessfulActions, result.TotalAttempted);

        return result;
    }

    public async Task<bool> RefreshMemberCacheAsync(string groupId)
    {
        if (string.IsNullOrEmpty(groupId))
            return false;

        try
        {
            // Remove from cache to force refresh
            _memberCache.TryRemove(groupId, out _);
            
            // Fetch fresh data
            var members = await GetGroupMembersAsync(groupId);
            
            _logger.LogDebug("Refreshed member cache for group {GroupId} with {MemberCount} members", 
                groupId, members.Count);
            
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refresh member cache for group {GroupId}", groupId);
            return false;
        }
    }

    public async Task<ExportResult> ExportMembersAsync(string groupId, List<GroupMember> members)
    {
        if (string.IsNullOrEmpty(groupId) || members == null || members.Count == 0)
        {
            return new ExportResult
            {
                Success = false,
                Message = "Invalid parameters or no members to export",
                RecordCount = 0
            };
        }

        try
        {
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var fileName = $"members_export_{groupId}_{timestamp}.csv";
            var filePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), fileName);

            var csv = new StringBuilder();
            csv.AppendLine("UserId,DisplayName,Username,Role,JoinedAt");

            foreach (var member in members)
            {
                csv.AppendLine($"{member.UserId},{member.DisplayName},{member.Username},{member.Role},{member.JoinedAt}");
            }

            await File.WriteAllTextAsync(filePath, csv.ToString());

            _logger.LogInformation("Exported {MemberCount} members to {FilePath}", members.Count, filePath);

            return new ExportResult
            {
                Success = true,
                Message = "Export completed successfully",
                FilePath = filePath,
                RecordCount = members.Count,
                Format = "CSV"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to export members for group {GroupId}", groupId);
            return new ExportResult
            {
                Success = false,
                Message = $"Export failed: {ex.Message}",
                RecordCount = 0
            };
        }
    }

    private class CachedMemberList
    {
        public List<GroupMember> Members { get; set; } = new();
        public DateTime CachedAt { get; set; }
    }
}