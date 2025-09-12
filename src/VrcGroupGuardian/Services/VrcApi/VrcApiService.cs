using Microsoft.Extensions.Logging;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using VrcGroupGuardian.Infrastructure;
using VrcGroupGuardian.Models;

namespace VrcGroupGuardian.Services.VrcApi;

public interface IVrcApiService
{
    Task<AuthResult> LoginAsync(string username, string password);
    Task<AuthResult> VerifyTwoFactorAsync(string code);
    Task<bool> LogoutAsync();
    Task<List<GroupInstance>> GetGroupInstancesAsync(string groupId);
    Task<List<GroupMember>> GetGroupMembersAsync(string groupId);
    Task<bool> CloseInstanceAsync(string instanceId);
    Task<MemberActionResult> KickGroupMemberAsync(string groupId, string userId);
    Task<MemberActionResult> BanGroupMemberAsync(string groupId, string userId);
    Task<MemberActionResult> UnbanGroupMemberAsync(string groupId, string userId);
    Task<List<string>> GetGroupPermissionsAsync(string groupId);
    Task<List<AuditRecord>> GetGroupAuditLogsAsync(string groupId, int limit = 100);
    Task<bool> IsAuthenticatedAsync();
}

public class VrcApiService : IVrcApiService, IDisposable
{
    private readonly IVrchatHttpClientFactory _httpClientFactory;
    private readonly ICacheService _cacheService;
    private readonly IDryRunMode _dryRunMode;
    private readonly ILogger<VrcApiService> _logger;
    private readonly SemaphoreSlim _authLock = new(1, 1);
    
    private HttpClient? _authenticatedClient;
    private string? _currentAuthToken;
    private DateTime _tokenExpiry = DateTime.MinValue;

    public VrcApiService(IVrchatHttpClientFactory httpClientFactory, ICacheService cacheService, IDryRunMode dryRunMode, ILogger<VrcApiService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _cacheService = cacheService;
        _dryRunMode = dryRunMode;
        _logger = logger;
    }

    public async Task<AuthResult> LoginAsync(string username, string password)
    {
        return await _dryRunMode.ExecuteOrSimulateAsync(async () =>
        {
            await _authLock.WaitAsync();
            try
            {
                using var client = _httpClientFactory.CreateClient();
                
                var loginData = new { username, password };
            var response = await client.PostAsJsonAsync("api/1/auth/user", loginData);
            
            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                _logger.LogWarning("Login failed with status {StatusCode}: {Error}", response.StatusCode, error);
                
                return new AuthResult 
                { 
                    Success = false, 
                    ErrorMessage = response.StatusCode == System.Net.HttpStatusCode.Unauthorized 
                        ? "Invalid username or password" 
                        : "Login request failed",
                    RequiresTwoFactor = false
                };
            }

            var responseContent = await response.Content.ReadAsStringAsync();
            using var document = JsonDocument.Parse(responseContent);
            
            // Check if 2FA is required
            if (document.RootElement.TryGetProperty("requiresTwoFactorAuth", out var requires2FA) && 
                requires2FA.GetBoolean())
            {
                return new AuthResult 
                { 
                    Success = false, 
                    RequiresTwoFactor = true,
                    ErrorMessage = "Two-factor authentication required"
                };
            }

            // Extract auth token from cookies
            var authCookie = response.Headers.GetValues("Set-Cookie")
                .FirstOrDefault(c => c.StartsWith("auth="));
            
            if (authCookie == null)
            {
                _logger.LogError("No auth cookie received after successful login");
                return new AuthResult { Success = false, ErrorMessage = "Authentication token not received" };
            }

            var authToken = authCookie.Split('=')[1].Split(';')[0];
            await SetAuthTokenAsync(authToken);

            _logger.LogInformation("Login successful for user");
            return new AuthResult 
            { 
                Success = true, 
                AuthToken = authToken,
                RequiresTwoFactor = false
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Login request failed with exception");
            return new AuthResult { Success = false, ErrorMessage = "Network error during login" };
            }
            finally
            {
                _authLock.Release();
            }
        }, new AuthResult { Success = true, AuthToken = "mock-token", RequiresTwoFactor = false }, "Login");
    }

    public async Task<AuthResult> VerifyTwoFactorAsync(string code)
    {
        return await _dryRunMode.ExecuteOrSimulateAsync(async () =>
        {
            await _authLock.WaitAsync();
            try
            {
                using var client = _httpClientFactory.CreateClient();
                
                var twoFactorData = new { code };
                var response = await client.PostAsJsonAsync("api/1/auth/twofactorauth/totp/verify", twoFactorData);
            
            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                _logger.LogWarning("2FA verification failed with status {StatusCode}: {Error}", response.StatusCode, error);
                
                return new AuthResult 
                { 
                    Success = false, 
                    ErrorMessage = "Invalid two-factor code",
                    RequiresTwoFactor = true
                };
            }

            // Extract updated auth token
            var authCookie = response.Headers.GetValues("Set-Cookie")
                .FirstOrDefault(c => c.StartsWith("auth="));
            
            if (authCookie != null)
            {
                var authToken = authCookie.Split('=')[1].Split(';')[0];
                await SetAuthTokenAsync(authToken);
            }

            _logger.LogInformation("Two-factor authentication successful");
            return new AuthResult 
            { 
                Success = true, 
                AuthToken = _currentAuthToken,
                RequiresTwoFactor = false
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "2FA verification failed with exception");
            return new AuthResult { Success = false, ErrorMessage = "Network error during 2FA verification" };
            }
            finally
            {
                _authLock.Release();
            }
        }, new AuthResult { Success = true, AuthToken = "mock-token", RequiresTwoFactor = false }, "VerifyTwoFactor");
    }

    public async Task<bool> LogoutAsync()
    {
        return await _dryRunMode.ExecuteOrSimulateAsync(async () =>
        {
            await _authLock.WaitAsync();
            try
            {
                if (_authenticatedClient != null && !string.IsNullOrEmpty(_currentAuthToken))
                {
                    try
                    {
                        await _authenticatedClient.PutAsync("api/1/logout", null);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Logout API call failed, proceeding with local cleanup");
                    }
                }

                await ClearAuthAsync();
                _logger.LogInformation("Logout completed");
                return true;
            }
            finally
            {
                _authLock.Release();
            }
        }, true, "Logout");
    }

    public async Task<List<GroupInstance>> GetGroupInstancesAsync(string groupId)
    {
        return await _dryRunMode.ExecuteOrSimulateAsync(async () =>
        {
            var client = await GetAuthenticatedClientAsync();
            if (client == null)
            {
                _logger.LogWarning("Not authenticated, cannot get group instances");
                return new List<GroupInstance>();
            }

            try
            {
                var response = await client.GetAsync($"api/1/groups/{groupId}/instances");
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Failed to get group instances: {StatusCode}", response.StatusCode);
                    return new List<GroupInstance>();
                }

                var content = await response.Content.ReadAsStringAsync();
                using var document = JsonDocument.Parse(content);
                
                var instances = new List<GroupInstance>();
                
                if (document.RootElement.TryGetProperty("instances", out var instancesArray))
                {
                    foreach (var instanceElement in instancesArray.EnumerateArray())
                    {
                        var instance = ParseGroupInstance(instanceElement);
                        if (instance != null)
                        {
                            instances.Add(instance);
                        }
                    }
                }

                return instances;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get group instances for {GroupId}", groupId);
                return new List<GroupInstance>();
            }
        }, new List<GroupInstance>
        {
            new GroupInstance
            {
                InstanceId = "mock-instance-1",
                WorldName = "Mock World Alpha",
                WorldId = "wrld_mock_001",
                InstanceType = InstanceType.Group,
                UserCount = 5,
                MaxUsers = 20,
                Region = "us-west",
                Status = InstanceStatus.Active,
                LastUpdated = DateTime.UtcNow
            },
            new GroupInstance
            {
                InstanceId = "mock-instance-2",
                WorldName = "Mock World Beta",
                WorldId = "wrld_mock_002",
                InstanceType = InstanceType.GroupPlus,
                UserCount = 12,
                MaxUsers = 40,
                Region = "eu-west",
                Status = InstanceStatus.Active,
                LastUpdated = DateTime.UtcNow
            }
        }, "GetGroupInstances");
    }

    public async Task<List<GroupMember>> GetGroupMembersAsync(string groupId)
    {
        return await _dryRunMode.ExecuteOrSimulateAsync(async () =>
        {
            var client = await GetAuthenticatedClientAsync();
            if (client == null)
            {
                _logger.LogWarning("Not authenticated, cannot get group members");
                return new List<GroupMember>();
            }

            try
            {
                var response = await client.GetAsync($"api/1/groups/{groupId}/members");
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Failed to get group members: {StatusCode}", response.StatusCode);
                    return new List<GroupMember>();
                }

                var content = await response.Content.ReadAsStringAsync();
                using var document = JsonDocument.Parse(content);
                
                var members = new List<GroupMember>();
                
                if (document.RootElement.TryGetProperty("members", out var membersArray))
                {
                    foreach (var memberElement in membersArray.EnumerateArray())
                    {
                        var member = ParseGroupMember(memberElement);
                        if (member != null)
                        {
                            members.Add(member);
                        }
                    }
                }

                return members;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get group members for {GroupId}", groupId);
                return new List<GroupMember>();
            }
        }, new List<GroupMember>
        {
            new GroupMember
            {
                UserId = "usr_mock_admin_001",
                DisplayName = "MockAdmin",
                Username = "mockadmin",
                Role = "Admin",
                PermissionLevel = MemberPermissionLevel.Admin
            },
            new GroupMember
            {
                UserId = "usr_mock_member_002",
                DisplayName = "MockMember1",
                Username = "mockmember1",
                Role = "Member",
                PermissionLevel = MemberPermissionLevel.Member
            },
            new GroupMember
            {
                UserId = "usr_mock_mod_003",
                DisplayName = "MockModerator",
                Username = "mockmoderator",
                Role = "Moderator",
                PermissionLevel = MemberPermissionLevel.Moderator
            }
        }, "GetGroupMembers");
    }

    public async Task<bool> CloseInstanceAsync(string instanceId)
    {
        return await _dryRunMode.ExecuteOrSimulateAsync(async () =>
        {
            var client = await GetAuthenticatedClientAsync();
            if (client == null)
            {
                _logger.LogWarning("Not authenticated, cannot close instance");
                return false;
            }

            try
            {
                var response = await client.DeleteAsync($"api/1/instances/{instanceId}");
                var success = response.IsSuccessStatusCode;
                
                if (success)
                {
                    _logger.LogInformation("Successfully closed instance {InstanceId}", instanceId);
                }
                else
                {
                    _logger.LogWarning("Failed to close instance {InstanceId}: {StatusCode}", instanceId, response.StatusCode);
                }

                return success;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to close instance {InstanceId}", instanceId);
                return false;
            }
        }, true, "CloseInstance");
    }

    public async Task<MemberActionResult> KickGroupMemberAsync(string groupId, string userId)
    {
        return await _dryRunMode.ExecuteOrSimulateAsync(async () =>
        {
            return await PerformMemberActionAsync(groupId, userId, "kick", 
                () => _authenticatedClient!.DeleteAsync($"api/1/groups/{groupId}/members/{userId}"));
        }, new MemberActionResult
        {
            Success = true,
            UserId = userId,
            Message = "Member kick successful (simulated)",
            Reason = "kick"
        }, "KickGroupMember");
    }

    public async Task<MemberActionResult> BanGroupMemberAsync(string groupId, string userId)
    {
        return await _dryRunMode.ExecuteOrSimulateAsync(async () =>
        {
            return await PerformMemberActionAsync(groupId, userId, "ban", 
                () => _authenticatedClient!.PostAsync($"api/1/groups/{groupId}/bans", 
                    JsonContent.Create(new { userId })));
        }, new MemberActionResult
        {
            Success = true,
            UserId = userId,
            Message = "Member ban successful (simulated)",
            Reason = "ban"
        }, "BanGroupMember");
    }

    public async Task<MemberActionResult> UnbanGroupMemberAsync(string groupId, string userId)
    {
        return await _dryRunMode.ExecuteOrSimulateAsync(async () =>
        {
            return await PerformMemberActionAsync(groupId, userId, "unban", 
                () => _authenticatedClient!.DeleteAsync($"api/1/groups/{groupId}/bans/{userId}"));
        }, new MemberActionResult
        {
            Success = true,
            UserId = userId,
            Message = "Member unban successful (simulated)",
            Reason = "unban"
        }, "UnbanGroupMember");
    }

    public async Task<List<string>> GetGroupPermissionsAsync(string groupId)
    {
        return await _dryRunMode.ExecuteOrSimulateAsync(async () =>
        {
            var client = await GetAuthenticatedClientAsync();
            if (client == null)
            {
                return new List<string>();
            }

            try
            {
                var response = await client.GetAsync($"api/1/groups/{groupId}/permissions");
                if (!response.IsSuccessStatusCode)
                {
                    return new List<string>();
                }

                var content = await response.Content.ReadAsStringAsync();
                using var document = JsonDocument.Parse(content);
                
                var permissions = new List<string>();
                
                if (document.RootElement.TryGetProperty("permissions", out var permissionsArray))
                {
                    foreach (var permission in permissionsArray.EnumerateArray())
                    {
                        if (permission.TryGetString(out var permissionName))
                        {
                            permissions.Add(permissionName);
                        }
                    }
                }

                return permissions;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get group permissions for {GroupId}", groupId);
                return new List<string>();
            }
        }, new List<string>
        {
            "group.manage.instances",
            "group.manage.members",
            "group.moderate",
            "group.view.audit",
            "group.ban.members",
            "group.kick.members"
        }, "GetGroupPermissions");
    }

    public async Task<List<AuditRecord>> GetGroupAuditLogsAsync(string groupId, int limit = 100)
    {
        return await _dryRunMode.ExecuteOrSimulateAsync(async () =>
        {
            var client = await GetAuthenticatedClientAsync();
            if (client == null)
            {
                return new List<AuditRecord>();
            }

            try
            {
                var response = await client.GetAsync($"api/1/groups/{groupId}/auditLogs?n={limit}");
                if (!response.IsSuccessStatusCode)
                {
                    return new List<AuditRecord>();
                }

                var content = await response.Content.ReadAsStringAsync();
                using var document = JsonDocument.Parse(content);
                
                var auditRecords = new List<AuditRecord>();
                
                if (document.RootElement.TryGetProperty("results", out var resultsArray))
                {
                    foreach (var logElement in resultsArray.EnumerateArray())
                    {
                        var auditRecord = ParseAuditRecord(logElement);
                        if (auditRecord != null)
                        {
                            auditRecords.Add(auditRecord);
                        }
                    }
                }

                return auditRecords;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get audit logs for {GroupId}", groupId);
                return new List<AuditRecord>();
            }
        }, new List<AuditRecord>
        {
            new AuditRecord
            {
                Id = Guid.NewGuid(),
                Timestamp = DateTime.UtcNow.AddMinutes(-30),
                ActionType = AuditActionType.KickMember,
                ActorUserId = "usr_mock_admin_001",
                ActorDisplayName = "MockAdmin",
                TargetType = AuditTargetType.Member,
                TargetId = "usr_mock_member_002",
                TargetDisplayName = "MockMember1",
                Details = "Member kicked for policy violation",
                Success = true
            },
            new AuditRecord
            {
                Id = Guid.NewGuid(),
                Timestamp = DateTime.UtcNow.AddHours(-2),
                ActionType = AuditActionType.ManualClose,
                ActorUserId = "usr_mock_admin_001",
                ActorDisplayName = "MockAdmin",
                TargetType = AuditTargetType.Instance,
                TargetId = "mock-instance-1",
                TargetDisplayName = "Mock World Alpha",
                Details = "Instance closed manually",
                Success = true
            }
        }, "GetGroupAuditLogs");
    }

    public async Task<bool> IsAuthenticatedAsync()
    {
        return await _dryRunMode.ExecuteOrSimulateAsync(async () =>
        {
            return _currentAuthToken != null && 
                   DateTime.UtcNow < _tokenExpiry && 
                   _authenticatedClient != null;
        }, true, "IsAuthenticated");
    }

    private async Task SetAuthTokenAsync(string authToken)
    {
        _currentAuthToken = authToken;
        _tokenExpiry = DateTime.UtcNow.AddHours(24); // VRChat tokens typically last 24 hours
        
        _authenticatedClient?.Dispose();
        _authenticatedClient = _httpClientFactory.CreateRateLimitedClient(authToken);
    }

    private async Task ClearAuthAsync()
    {
        _currentAuthToken = null;
        _tokenExpiry = DateTime.MinValue;
        
        _authenticatedClient?.Dispose();
        _authenticatedClient = null;
    }

    private async Task<HttpClient?> GetAuthenticatedClientAsync()
    {
        if (!await IsAuthenticatedAsync())
        {
            return null;
        }
        return _authenticatedClient;
    }

    private async Task<MemberActionResult> PerformMemberActionAsync(string groupId, string userId, string action, Func<Task<HttpResponseMessage>> apiCall)
    {
        var client = await GetAuthenticatedClientAsync();
        if (client == null)
        {
            return new MemberActionResult 
            { 
                Success = false, 
                UserId = userId,
                Message = "Not authenticated",
                Reason = ""
            };
        }

        try
        {
            var response = await apiCall();
            var content = await response.Content.ReadAsStringAsync();
            
            return new MemberActionResult
            {
                Success = response.IsSuccessStatusCode,
                UserId = userId,
                Message = response.IsSuccessStatusCode ? $"Member {action} successful" : $"Member {action} failed: {response.StatusCode}",
                Reason = response.IsSuccessStatusCode ? action : content
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to {Action} member {UserId} from group {GroupId}", action, userId, groupId);
            return new MemberActionResult 
            { 
                Success = false, 
                UserId = userId,
                Message = $"Network error during {action}",
                Reason = ""
            };
        }
    }

    private GroupInstance? ParseGroupInstance(JsonElement element)
    {
        try
        {
            return new GroupInstance
            {
                InstanceId = element.GetProperty("id").GetString() ?? "",
                WorldName = element.GetProperty("worldName").GetString() ?? "",
                WorldId = element.GetProperty("worldId").GetString() ?? "",
                InstanceType = ParseInstanceType(element.GetProperty("type").GetString()),
                UserCount = element.GetProperty("userCount").GetInt32(),
                MaxUsers = element.GetProperty("capacity").GetInt32(),
                Region = element.GetProperty("region").GetString() ?? "",
                Status = InstanceStatus.Active,
                LastUpdated = DateTime.UtcNow
            };
        }
        catch
        {
            return null;
        }
    }

    private GroupMember? ParseGroupMember(JsonElement element)
    {
        try
        {
            return new GroupMember
            {
                UserId = element.GetProperty("id").GetString() ?? "",
                DisplayName = element.GetProperty("displayName").GetString() ?? "",
                Username = element.GetProperty("username").GetString() ?? "",
                Role = element.GetProperty("roleDisplayName").GetString() ?? "",
                PermissionLevel = ParsePermissionLevel(element.GetProperty("roleDisplayName").GetString())
            };
        }
        catch
        {
            return null;
        }
    }

    private AuditRecord? ParseAuditRecord(JsonElement element)
    {
        try
        {
            return new AuditRecord
            {
                Id = Guid.NewGuid(),
                Timestamp = element.GetProperty("created_at").GetDateTime(),
                ActionType = ParseAuditActionType(element.GetProperty("eventType").GetString()),
                ActorUserId = element.TryGetProperty("actorId", out var actorId) ? actorId.GetString() : null,
                ActorDisplayName = element.TryGetProperty("actorDisplayName", out var actorName) ? actorName.GetString() : null,
                TargetType = AuditTargetType.Instance,
                TargetId = element.GetProperty("targetId").GetString() ?? "",
                TargetDisplayName = element.TryGetProperty("targetDisplayName", out var targetName) ? targetName.GetString() ?? "" : "",
                Details = element.ToString(),
                Success = true
            };
        }
        catch
        {
            return null;
        }
    }

    private static InstanceType ParseInstanceType(string? type)
    {
        return type?.ToLower() switch
        {
            "group" => InstanceType.Group,
            "group+" => InstanceType.GroupPlus,
            "group public" => InstanceType.GroupPublic,
            _ => InstanceType.Group
        };
    }

    private static MemberPermissionLevel ParsePermissionLevel(string? role)
    {
        return role?.ToLower() switch
        {
            "owner" => MemberPermissionLevel.Owner,
            "admin" => MemberPermissionLevel.Admin,
            "moderator" => MemberPermissionLevel.Moderator,
            _ => MemberPermissionLevel.Member
        };
    }

    private static AuditActionType ParseAuditActionType(string? eventType)
    {
        return eventType?.ToLower() switch
        {
            "member.kick" => AuditActionType.KickMember,
            "member.ban" => AuditActionType.BanMember,
            "member.unban" => AuditActionType.UnbanMember,
            "instance.close" => AuditActionType.ManualClose,
            _ => AuditActionType.Login
        };
    }

    public void Dispose()
    {
        _authLock?.Dispose();
        _authenticatedClient?.Dispose();
    }
}

public class AuthResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public string? Message { get; set; }
    public string? AuthToken { get; set; }
    public bool RequiresTwoFactor { get; set; }
}