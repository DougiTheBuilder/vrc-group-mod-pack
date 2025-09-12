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
    Task<List<VrcGroup>> GetUserGroupsAsync();
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
    private string? _pendingAuthToken; // Store intermediate auth state for 2FA

    public VrcApiService(IVrchatHttpClientFactory httpClientFactory, ICacheService cacheService, IDryRunMode dryRunMode, ILogger<VrcApiService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _cacheService = cacheService;
        _dryRunMode = dryRunMode;
        _logger = logger;
    }

    public async Task<AuthResult> LoginAsync(string username, string password)
    {
        _logger.LogDebug("LoginAsync called - DryRun mode enabled: {DryRunEnabled}", _dryRunMode.IsEnabled);
        return await _dryRunMode.ExecuteOrSimulateAsync(async () =>
        {
            await _authLock.WaitAsync();
            try
            {
                using var client = _httpClientFactory.CreateClient();
                
                // VRChat API uses Basic Authentication with GET to /auth/user
                var credentials = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{username}:{password}"));
                client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", credentials);
                
                var response = await client.GetAsync("api/1/auth/user");
            
            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                _logger.LogWarning("Login failed with status {StatusCode}: {Error}", response.StatusCode, error);
                
                string userFriendlyError;
                switch (response.StatusCode)
                {
                    case System.Net.HttpStatusCode.Unauthorized:
                        userFriendlyError = "Invalid username or password";
                        break;
                    case System.Net.HttpStatusCode.Forbidden:
                        userFriendlyError = "Account may be banned or suspended";
                        break;
                    case System.Net.HttpStatusCode.TooManyRequests:
                        userFriendlyError = "Too many login attempts - please wait and try again";
                        break;
                    case System.Net.HttpStatusCode.ServiceUnavailable:
                        userFriendlyError = "VRChat servers are temporarily unavailable";
                        break;
                    default:
                        userFriendlyError = $"Login request failed (HTTP {(int)response.StatusCode})";
                        break;
                }
                
                return new AuthResult 
                { 
                    Success = false, 
                    ErrorMessage = userFriendlyError,
                    RequiresTwoFactor = false
                };
            }

            var responseContent = await response.Content.ReadAsStringAsync();
            using var document = JsonDocument.Parse(responseContent);
            
            // Check if 2FA is required - VRChat API can return different types
            if (document.RootElement.TryGetProperty("requiresTwoFactorAuth", out var requires2FA))
            {
                bool requires2FABool = false;
                
                try
                {
                    // Try different possible types
                    if (requires2FA.ValueKind == JsonValueKind.True)
                    {
                        requires2FABool = true;
                    }
                    else if (requires2FA.ValueKind == JsonValueKind.Array && requires2FA.GetArrayLength() > 0)
                    {
                        // VRChat might return an array of 2FA types required
                        requires2FABool = true;
                    }
                    else if (requires2FA.ValueKind == JsonValueKind.String)
                    {
                        var str = requires2FA.GetString();
                        requires2FABool = !string.IsNullOrEmpty(str) && str.ToLower() != "false";
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to parse requiresTwoFactorAuth field from VRChat API response");
                }
                
                if (requires2FABool)
                {
                    // Store the intermediate auth token for 2FA verification
                    var intermediateToken = ExtractAuthTokenFromResponse(response);
                    if (!string.IsNullOrEmpty(intermediateToken))
                    {
                        _pendingAuthToken = intermediateToken;
                    }
                    
                    return new AuthResult 
                    { 
                        Success = false, 
                        RequiresTwoFactor = true,
                        ErrorMessage = "Two-factor authentication required"
                    };
                }
            }

            // Extract auth token from cookies
            var authToken = ExtractAuthTokenFromResponse(response);
            
            if (string.IsNullOrEmpty(authToken))
            {
                _logger.LogError("No auth cookie received after successful login");
                return new AuthResult { Success = false, ErrorMessage = "Authentication token not received" };
            }

            await SetAuthTokenAsync(authToken);

            _logger.LogInformation("Login successful for user");
            return new AuthResult 
            { 
                Success = true, 
                AuthToken = authToken,
                RequiresTwoFactor = false
            };
        }
        catch (HttpRequestException httpEx)
        {
            _logger.LogError(httpEx, "HTTP request failed during login");
            return new AuthResult { Success = false, ErrorMessage = $"Connection error: {httpEx.Message}" };
        }
        catch (TaskCanceledException tcEx)
        {
            _logger.LogError(tcEx, "Login request timed out");
            return new AuthResult { Success = false, ErrorMessage = "Request timed out - please try again" };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Login request failed with unexpected exception");
            return new AuthResult { Success = false, ErrorMessage = $"Network error: {ex.Message}" };
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
                if (string.IsNullOrEmpty(_pendingAuthToken))
                {
                    _logger.LogWarning("No pending auth token found for 2FA verification");
                    return new AuthResult 
                    { 
                        Success = false, 
                        ErrorMessage = "Authentication state lost - please login again",
                        RequiresTwoFactor = false
                    };
                }

                // Create client with the pending auth token
                using var client = _httpClientFactory.CreateRateLimitedClient(_pendingAuthToken);
                
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

            // Extract updated auth token from response or use pending token
            var authToken = ExtractAuthTokenFromResponse(response) ?? _pendingAuthToken;
            
            if (!string.IsNullOrEmpty(authToken))
            {
                await SetAuthTokenAsync(authToken);
            }

            // Clear pending auth token after successful 2FA
            _pendingAuthToken = null;

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

    public async Task<List<VrcGroup>> GetUserGroupsAsync()
    {
        return await _dryRunMode.ExecuteOrSimulateAsync(async () =>
        {
            var client = await GetAuthenticatedClientAsync();
            if (client == null)
            {
                _logger.LogWarning("Not authenticated, cannot get user groups");
                return new List<VrcGroup>();
            }

            try
            {
                // First get current user info which should contain group memberships
                var userResponse = await client.GetAsync("api/1/auth/user");
                if (!userResponse.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Failed to get current user info: {StatusCode}", userResponse.StatusCode);
                    return new List<VrcGroup>();
                }

                var userContent = await userResponse.Content.ReadAsStringAsync();
                _logger.LogDebug("User API response: {Response}", userContent.Length > 500 ? userContent.Substring(0, 500) + "..." : userContent);
                using var userDoc = JsonDocument.Parse(userContent);
                
                var groups = new List<VrcGroup>();

                // Check if user data contains group information
                if (userDoc.RootElement.TryGetProperty("groups", out var groupsArray))
                {
                    foreach (var groupElement in groupsArray.EnumerateArray())
                    {
                        var group = ParseVrcGroup(groupElement);
                        if (group != null)
                        {
                            groups.Add(group);
                        }
                    }
                    
                    _logger.LogInformation("Retrieved {GroupCount} groups from user data", groups.Count);
                    return groups;
                }
                
                // If no groups in user data, try alternative endpoint
                var response = await client.GetAsync("api/1/groups");
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Failed to get user groups: {StatusCode}", response.StatusCode);
                    return new List<VrcGroup>();
                }

                var content = await response.Content.ReadAsStringAsync();
                using var document = JsonDocument.Parse(content);
                
                if (document.RootElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var groupElement in document.RootElement.EnumerateArray())
                    {
                        var group = ParseVrcGroup(groupElement);
                        if (group != null)
                        {
                            groups.Add(group);
                        }
                    }
                }
                else if (document.RootElement.TryGetProperty("groups", out var altGroupsArray))
                {
                    foreach (var groupElement in altGroupsArray.EnumerateArray())
                    {
                        var group = ParseVrcGroup(groupElement);
                        if (group != null)
                        {
                            groups.Add(group);
                        }
                    }
                }

                _logger.LogInformation("Retrieved {GroupCount} groups from groups endpoint", groups.Count);
                return groups;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get user groups");
                return new List<VrcGroup>();
            }
        }, new List<VrcGroup>
        {
            new VrcGroup
            {
                Id = "grp_mock_group_001",
                Name = "Mock Moderation Group",
                Description = "A mock group for testing VRC Group Guardian",
                MemberCount = 25,
                OwnerDisplayName = "MockOwner",
                IsPrivate = false,
                UserRole = "Owner"
            },
            new VrcGroup
            {
                Id = "grp_mock_group_002", 
                Name = "Another Test Group",
                Description = "Secondary test group",
                MemberCount = 12,
                OwnerDisplayName = "AnotherOwner",
                IsPrivate = true,
                UserRole = "Admin"
            }
        }, "GetUserGroups");
    }

    private async Task SetAuthTokenAsync(string authToken)
    {
        _currentAuthToken = authToken;
        _tokenExpiry = DateTime.UtcNow.AddHours(24); // VRChat tokens typically last 24 hours
        
        _authenticatedClient?.Dispose();
        _authenticatedClient = _httpClientFactory.CreateRateLimitedClient(authToken);
    }

    private string? ExtractAuthTokenFromResponse(HttpResponseMessage response)
    {
        try
        {
            // Try to get auth cookie from Set-Cookie headers
            if (response.Headers.TryGetValues("Set-Cookie", out var cookies))
            {
                var authCookie = cookies.FirstOrDefault(c => c.StartsWith("auth="));
                if (authCookie != null)
                {
                    var authToken = authCookie.Split('=')[1].Split(';')[0];
                    _logger.LogDebug("Extracted auth token from cookie");
                    return authToken;
                }
            }
            
            _logger.LogWarning("No auth cookie found in response");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to extract auth token from response");
            return null;
        }
    }

    private async Task ClearAuthAsync()
    {
        _currentAuthToken = null;
        _pendingAuthToken = null;
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

    private VrcGroup? ParseVrcGroup(JsonElement element)
    {
        try
        {
            var id = element.GetProperty("id").GetString() ?? "";
            var name = element.GetProperty("name").GetString() ?? "";
            
            // Extract user role with detailed logging
            var userRole = "";
            if (element.TryGetProperty("myMember", out var member))
            {
                if (member.TryGetProperty("roleDisplayName", out var role))
                {
                    userRole = role.GetString() ?? "";
                    _logger.LogDebug("Group {GroupName} myMember.roleDisplayName: '{UserRole}'", name, userRole);
                }
                else
                {
                    _logger.LogDebug("Group {GroupName} myMember exists but no roleDisplayName", name);
                    // Try alternative role fields
                    if (member.TryGetProperty("role", out var altRole))
                    {
                        userRole = altRole.GetString() ?? "";
                        _logger.LogDebug("Group {GroupName} using myMember.role: '{UserRole}'", name, userRole);
                    }
                }
            }
            else
            {
                _logger.LogDebug("Group {GroupName} has no myMember property", name);
            }

            return new VrcGroup
            {
                Id = id,
                Name = name,
                Description = element.TryGetProperty("description", out var desc) ? desc.GetString() ?? "" : "",
                MemberCount = element.TryGetProperty("memberCount", out var count) ? count.GetInt32() : 0,
                OwnerDisplayName = element.TryGetProperty("ownerId", out var owner) ? owner.GetString() ?? "" : "",
                IsPrivate = element.TryGetProperty("privacy", out var privacy) ? privacy.GetString() != "public" : true,
                UserRole = userRole
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse VrcGroup from JSON element");
            return null;
        }
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

public class VrcGroup
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int MemberCount { get; set; }
    public string OwnerDisplayName { get; set; } = string.Empty;
    public bool IsPrivate { get; set; }
    public string UserRole { get; set; } = string.Empty;
}