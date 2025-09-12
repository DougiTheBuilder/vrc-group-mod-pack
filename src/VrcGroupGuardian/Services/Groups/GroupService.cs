using Microsoft.Extensions.Logging;
using VrcGroupGuardian.Infrastructure;
using VrcGroupGuardian.Models;
using VrcGroupGuardian.Services.Auth;
using VrcGroupGuardian.Services.VrcApi;

namespace VrcGroupGuardian.Services.Groups;

public interface IGroupService
{
    Task<List<GroupInfo>> GetAvailableGroupsAsync();
    Task<List<GroupInfo>> GetUserGroupsAsync();
    Task<GroupInfo?> GetGroupInfoAsync(string groupId);
    Task<bool> SelectGroupAsync(string groupId);
    Task<bool> SetSelectedGroupAsync(GroupInfo groupInfo);
    Task<GroupInfo?> GetSelectedGroupAsync();
    Task<List<string>> GetCurrentUserPermissionsAsync(string groupId);
    Task<bool> HasPermissionAsync(string groupId, string permission);
    Task<bool> CanManageInstancesAsync(string groupId);
    Task<bool> CanManageMembersAsync(string groupId);
}

public class GroupService : IGroupService
{
    private readonly IVrcApiService _vrcApiService;
    private readonly IAuthService _authService;
    private readonly ISettingsStore _settingsStore;
    private readonly ILogger<GroupService> _logger;
    
    private GroupInfo? _selectedGroup;
    private readonly SemaphoreSlim _groupLock = new(1, 1);

    public GroupService(
        IVrcApiService vrcApiService, 
        IAuthService authService, 
        ISettingsStore settingsStore, 
        ILogger<GroupService> logger)
    {
        _vrcApiService = vrcApiService;
        _authService = authService;
        _settingsStore = settingsStore;
        _logger = logger;
    }

    public async Task<List<GroupInfo>> GetAvailableGroupsAsync()
    {
        if (!await _authService.IsAuthenticatedAsync())
        {
            _logger.LogWarning("Cannot get available groups: not authenticated");
            return new List<GroupInfo>();
        }

        try
        {
            // Get user's groups from VRChat API
            var vrcGroups = await _vrcApiService.GetUserGroupsAsync();
            
            var groupInfos = new List<GroupInfo>();
            foreach (var vrcGroup in vrcGroups)
            {
                _logger.LogDebug("Group: {GroupName} ({GroupId}) - User Role: '{UserRole}', IsManagement: {IsManagement}", 
                    vrcGroup.Name, vrcGroup.Id, vrcGroup.UserRole, IsManagementRole(vrcGroup.UserRole));
                
                // Only include groups where user has management permissions
                if (IsManagementRole(vrcGroup.UserRole))
                {
                    groupInfos.Add(new GroupInfo
                    {
                        GroupId = vrcGroup.Id,
                        GroupName = vrcGroup.Name,
                        Description = vrcGroup.Description,
                        MemberCount = vrcGroup.MemberCount,
                        OwnerDisplayName = vrcGroup.OwnerDisplayName,
                        IsPrivate = vrcGroup.IsPrivate,
                        UserRole = vrcGroup.UserRole
                    });
                }
                else
                {
                    _logger.LogDebug("Filtering out group {GroupName} - role '{UserRole}' is not a management role", 
                        vrcGroup.Name, vrcGroup.UserRole);
                }
            }

            _logger.LogInformation("Retrieved {GroupCount} manageable groups for user", groupInfos.Count);
            return groupInfos;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get available groups");
            return new List<GroupInfo>();
        }
    }

    public async Task<List<GroupInfo>> GetUserGroupsAsync()
    {
        // GetUserGroupsAsync is typically the same as GetAvailableGroupsAsync for user's own groups
        return await GetAvailableGroupsAsync();
    }

    public async Task<GroupInfo?> GetGroupInfoAsync(string groupId)
    {
        if (string.IsNullOrEmpty(groupId))
            return null;

        if (!await _authService.IsAuthenticatedAsync())
        {
            _logger.LogWarning("Cannot get group info: not authenticated");
            return null;
        }

        try
        {
            // Note: This would require implementing VRChat's group info endpoint
            // For now, return placeholder data based on stored policy configuration
            var policy = await _settingsStore.LoadPolicyConfigurationAsync();
            
            if (policy.GroupId == groupId)
            {
                return new GroupInfo
                {
                    GroupId = policy.GroupId,
                    GroupName = policy.GroupName,
                    Description = "Monitored group for VRC Group Guardian",
                    MemberCount = 0, // Would need to call API to get real count
                    OwnerDisplayName = "Unknown",
                    IsPrivate = true,
                    UserRole = "Moderator" // Assume moderator role for monitoring capabilities
                };
            }

            _logger.LogWarning("Group info not available for {GroupId} - not in policy configuration", groupId);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get group info for {GroupId}", groupId);
            return null;
        }
    }

    public async Task<bool> SelectGroupAsync(string groupId)
    {
        if (string.IsNullOrEmpty(groupId))
            return false;

        await _groupLock.WaitAsync();
        try
        {
            if (!await _authService.IsAuthenticatedAsync())
            {
                _logger.LogWarning("Cannot select group: not authenticated");
                return false;
            }

            // Verify we have permissions for this group
            var permissions = await GetCurrentUserPermissionsAsync(groupId);
            if (permissions.Count == 0)
            {
                _logger.LogWarning("Cannot select group {GroupId}: no permissions found", groupId);
                return false;
            }

            // Get group info
            var groupInfo = await GetGroupInfoAsync(groupId);
            if (groupInfo == null)
            {
                _logger.LogWarning("Cannot select group {GroupId}: group info not found", groupId);
                return false;
            }

            // Update policy configuration with selected group
            var policy = await _settingsStore.LoadPolicyConfigurationAsync();
            policy.GroupId = groupId;
            policy.GroupName = groupInfo.GroupName;
            
            var saved = await _settingsStore.SavePolicyConfigurationAsync(policy);
            if (!saved)
            {
                _logger.LogError("Failed to save group selection to policy configuration");
                return false;
            }

            _selectedGroup = groupInfo;
            
            _logger.LogInformation("Selected group {GroupName} ({GroupId}) with {PermissionCount} permissions", 
                groupInfo.GroupName, groupId, permissions.Count);
            
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to select group {GroupId}", groupId);
            return false;
        }
        finally
        {
            _groupLock.Release();
        }
    }

    public async Task<bool> SetSelectedGroupAsync(GroupInfo groupInfo)
    {
        if (groupInfo == null)
            return false;

        await _groupLock.WaitAsync();
        try
        {
            if (!await _authService.IsAuthenticatedAsync())
            {
                _logger.LogWarning("Cannot set selected group: not authenticated");
                return false;
            }

            // Update policy configuration with selected group
            var policy = await _settingsStore.LoadPolicyConfigurationAsync();
            policy.GroupId = groupInfo.GroupId;
            policy.GroupName = groupInfo.GroupName;
            
            var saved = await _settingsStore.SavePolicyConfigurationAsync(policy);
            if (!saved)
            {
                _logger.LogError("Failed to save group selection to policy configuration");
                return false;
            }

            _selectedGroup = groupInfo;
            
            _logger.LogInformation("Set selected group to {GroupName} ({GroupId})", 
                groupInfo.GroupName, groupInfo.GroupId);
            
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to set selected group {GroupId}", groupInfo.GroupId);
            return false;
        }
        finally
        {
            _groupLock.Release();
        }
    }

    public async Task<GroupInfo?> GetSelectedGroupAsync()
    {
        await _groupLock.WaitAsync();
        try
        {
            if (_selectedGroup == null)
            {
                // Try to load from policy configuration
                var policy = await _settingsStore.LoadPolicyConfigurationAsync();
                if (!string.IsNullOrEmpty(policy.GroupId))
                {
                    _selectedGroup = await GetGroupInfoAsync(policy.GroupId);
                }
            }
            
            return _selectedGroup;
        }
        finally
        {
            _groupLock.Release();
        }
    }

    public async Task<List<string>> GetCurrentUserPermissionsAsync(string groupId)
    {
        if (string.IsNullOrEmpty(groupId))
            return new List<string>();

        try
        {
            return await _authService.GetGroupPermissionsAsync(groupId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get permissions for group {GroupId}", groupId);
            return new List<string>();
        }
    }

    public async Task<bool> HasPermissionAsync(string groupId, string permission)
    {
        if (string.IsNullOrEmpty(groupId) || string.IsNullOrEmpty(permission))
            return false;

        var permissions = await GetCurrentUserPermissionsAsync(groupId);
        return permissions.Contains(permission, StringComparer.OrdinalIgnoreCase);
    }

    public async Task<bool> CanManageInstancesAsync(string groupId)
    {
        // Check for instance management permissions
        var hasInstancePermission = await HasPermissionAsync(groupId, "group.instances.manage") ||
                                   await HasPermissionAsync(groupId, "group.moderator") ||
                                   await HasPermissionAsync(groupId, "group.admin") ||
                                   await HasPermissionAsync(groupId, "group.owner");

        if (hasInstancePermission)
        {
            _logger.LogDebug("User has instance management permissions for group {GroupId}", groupId);
        }
        else
        {
            _logger.LogDebug("User lacks instance management permissions for group {GroupId}", groupId);
        }

        return hasInstancePermission;
    }

    public async Task<bool> CanManageMembersAsync(string groupId)
    {
        // Check for member management permissions
        var hasMemberPermission = await HasPermissionAsync(groupId, "group.members.manage") ||
                                 await HasPermissionAsync(groupId, "group.members.kick") ||
                                 await HasPermissionAsync(groupId, "group.members.ban") ||
                                 await HasPermissionAsync(groupId, "group.moderator") ||
                                 await HasPermissionAsync(groupId, "group.admin") ||
                                 await HasPermissionAsync(groupId, "group.owner");

        if (hasMemberPermission)
        {
            _logger.LogDebug("User has member management permissions for group {GroupId}", groupId);
        }
        else
        {
            _logger.LogDebug("User lacks member management permissions for group {GroupId}", groupId);
        }

        return hasMemberPermission;
    }

    private static bool IsManagementRole(string userRole)
    {
        // Only show groups where user has management capabilities
        return userRole.Equals("Owner", StringComparison.OrdinalIgnoreCase) ||
               userRole.Equals("Admin", StringComparison.OrdinalIgnoreCase) ||
               userRole.Equals("Moderator", StringComparison.OrdinalIgnoreCase);
    }
}

public class GroupInfo
{
    public string GroupId { get; set; } = string.Empty;
    public string GroupName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int MemberCount { get; set; }
    public string OwnerDisplayName { get; set; } = string.Empty;
    public bool IsPrivate { get; set; }
    public string UserRole { get; set; } = string.Empty;
}