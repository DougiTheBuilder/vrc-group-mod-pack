using Microsoft.Extensions.Logging;
using VrcGroupGuardian.Infrastructure;
using VrcGroupGuardian.Models;
using VrcGroupGuardian.Services.VrcApi;

namespace VrcGroupGuardian.Services.Auth;

public interface IAuthService
{
    Task<AuthResult> LoginAsync(string username, string password);
    Task<AuthResult> VerifyTwoFactorAsync(string code);
    Task<bool> LogoutAsync();
    Task<AuthenticationSession?> GetCurrentSessionAsync();
    Task<bool> RefreshSessionAsync();
    Task<bool> IsAuthenticatedAsync();
    Task<List<string>> GetGroupPermissionsAsync(string groupId);
}

public class AuthService : IAuthService
{
    private readonly IVrcApiService _vrcApiService;
    private readonly ISecureStorage _secureStorage;
    private readonly ILogger<AuthService> _logger;
    
    private AuthenticationSession? _currentSession;
    private readonly SemaphoreSlim _sessionLock = new(1, 1);

    private const string AuthTokenKey = "vrchat-auth-token";
    private const string UsernameKey = "vrchat-username";

    public AuthService(IVrcApiService vrcApiService, ISecureStorage secureStorage, ILogger<AuthService> logger)
    {
        _vrcApiService = vrcApiService;
        _secureStorage = secureStorage;
        _logger = logger;
    }

    public async Task<AuthResult> LoginAsync(string username, string password)
    {
        await _sessionLock.WaitAsync();
        try
        {
            _logger.LogInformation("Attempting login for user {Username}", username);
            
            var result = await _vrcApiService.LoginAsync(username, password);
            
            if (result.Success && !string.IsNullOrEmpty(result.AuthToken))
            {
                await CreateSessionAsync(username, result.AuthToken, false);
                await StoreCredentialsAsync(username, result.AuthToken);
                
                _logger.LogInformation("Login successful for user {Username}", username);
            }
            else if (result.RequiresTwoFactor)
            {
                // Store partial session for 2FA completion
                await StoreCredentialsAsync(username, "");
                _logger.LogInformation("Two-factor authentication required for user {Username}", username);
            }
            else
            {
                _logger.LogWarning("Login failed for user {Username}: {Error}", username, result.ErrorMessage);
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Login failed with exception for user {Username}", username);
            return new AuthResult { Success = false, ErrorMessage = "Authentication service error" };
        }
        finally
        {
            _sessionLock.Release();
        }
    }

    public async Task<AuthResult> VerifyTwoFactorAsync(string code)
    {
        await _sessionLock.WaitAsync();
        try
        {
            _logger.LogInformation("Attempting 2FA verification");
            
            var result = await _vrcApiService.VerifyTwoFactorAsync(code);
            
            if (result.Success && !string.IsNullOrEmpty(result.AuthToken))
            {
                var username = await _secureStorage.RetrieveCredentialAsync(UsernameKey);
                if (!string.IsNullOrEmpty(username))
                {
                    await CreateSessionAsync(username, result.AuthToken, true);
                    await StoreCredentialsAsync(username, result.AuthToken);
                    
                    _logger.LogInformation("2FA verification successful");
                }
                else
                {
                    _logger.LogError("Username not found after 2FA verification");
                    result.Success = false;
                    result.ErrorMessage = "Session error after 2FA verification";
                }
            }
            else
            {
                _logger.LogWarning("2FA verification failed: {Error}", result.ErrorMessage);
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "2FA verification failed with exception");
            return new AuthResult { Success = false, ErrorMessage = "Authentication service error" };
        }
        finally
        {
            _sessionLock.Release();
        }
    }

    public async Task<bool> LogoutAsync()
    {
        await _sessionLock.WaitAsync();
        try
        {
            _logger.LogInformation("Logging out current session");

            // Call VRChat API logout
            var apiLogoutResult = await _vrcApiService.LogoutAsync();
            
            // Clear local session regardless of API result
            await ClearSessionAsync();
            await ClearStoredCredentialsAsync();
            
            _logger.LogInformation("Logout completed, API result: {ApiResult}", apiLogoutResult);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during logout, proceeding with local cleanup");
            
            // Ensure local cleanup even if there's an error
            await ClearSessionAsync();
            await ClearStoredCredentialsAsync();
            return true;
        }
        finally
        {
            _sessionLock.Release();
        }
    }

    public async Task<AuthenticationSession?> GetCurrentSessionAsync()
    {
        await _sessionLock.WaitAsync();
        try
        {
            if (_currentSession == null)
            {
                // Try to restore session from storage
                await RestoreSessionFromStorageAsync();
            }
            
            return _currentSession;
        }
        finally
        {
            _sessionLock.Release();
        }
    }

    public async Task<bool> RefreshSessionAsync()
    {
        await _sessionLock.WaitAsync();
        try
        {
            if (_currentSession == null || !_currentSession.IsSessionValid())
            {
                _logger.LogWarning("Cannot refresh invalid session");
                return false;
            }

            // Update last refresh time
            _currentSession.LastRefresh = DateTime.UtcNow;
            
            // Validate session with VRChat API
            var isValid = await _vrcApiService.IsAuthenticatedAsync();
            if (!isValid)
            {
                _logger.LogWarning("Session validation failed, invalidating local session");
                _currentSession.InvalidateSession();
                return false;
            }

            _logger.LogDebug("Session refreshed successfully");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Session refresh failed");
            return false;
        }
        finally
        {
            _sessionLock.Release();
        }
    }

    public async Task<bool> IsAuthenticatedAsync()
    {
        var session = await GetCurrentSessionAsync();
        return session?.IsSessionValid() == true && await _vrcApiService.IsAuthenticatedAsync();
    }

    public async Task<List<string>> GetGroupPermissionsAsync(string groupId)
    {
        var session = await GetCurrentSessionAsync();
        if (session == null || !session.IsSessionValid())
        {
            _logger.LogWarning("Cannot get permissions: not authenticated");
            return new List<string>();
        }

        // Check cache first
        if (session.IsPermissionsCacheValid() && session.GroupPermissions.ContainsKey(groupId))
        {
            return session.GroupPermissions[groupId];
        }

        try
        {
            var permissions = await _vrcApiService.GetGroupPermissionsAsync(groupId);
            
            // Update cache
            session.GroupPermissions[groupId] = permissions;
            session.LastRefresh = DateTime.UtcNow;
            
            _logger.LogDebug("Retrieved {PermissionCount} permissions for group {GroupId}", permissions.Count, groupId);
            return permissions;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get permissions for group {GroupId}", groupId);
            return session.GroupPermissions.GetValueOrDefault(groupId, new List<string>());
        }
    }

    private async Task CreateSessionAsync(string username, string authToken, bool twoFactorAuthenticated)
    {
        _currentSession = new AuthenticationSession
        {
            UserId = $"usr_{Guid.NewGuid():N}[0..24]", // Placeholder until we get real user ID
            DisplayName = username,
            Username = username,
            AuthToken = authToken,
            TokenExpiry = DateTime.UtcNow.AddHours(24),
            TwoFactorAuthenticated = twoFactorAuthenticated,
            LastRefresh = DateTime.UtcNow,
            IsValid = true
        };
        
        _logger.LogDebug("Created new authentication session for {Username}, 2FA: {TwoFactor}", 
            username, twoFactorAuthenticated);
    }

    private async Task ClearSessionAsync()
    {
        if (_currentSession != null)
        {
            // Securely wipe the auth token
            if (!string.IsNullOrEmpty(_currentSession.AuthToken))
            {
                _secureStorage.WipeMemory(_currentSession.AuthToken);
            }
            
            _currentSession.InvalidateSession();
            _currentSession = null;
        }
        
        _logger.LogDebug("Cleared authentication session");
    }

    private async Task StoreCredentialsAsync(string username, string authToken)
    {
        try
        {
            await _secureStorage.StoreCredentialAsync(UsernameKey, username);
            
            if (!string.IsNullOrEmpty(authToken))
            {
                await _secureStorage.StoreCredentialAsync(AuthTokenKey, authToken);
            }
            
            _logger.LogDebug("Stored credentials for user {Username}", username);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to store credentials securely");
        }
    }

    private async Task ClearStoredCredentialsAsync()
    {
        try
        {
            await _secureStorage.DeleteCredentialAsync(UsernameKey);
            await _secureStorage.DeleteCredentialAsync(AuthTokenKey);
            
            _logger.LogDebug("Cleared stored credentials");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to clear stored credentials");
        }
    }

    private async Task RestoreSessionFromStorageAsync()
    {
        try
        {
            var username = await _secureStorage.RetrieveCredentialAsync(UsernameKey);
            var authToken = await _secureStorage.RetrieveCredentialAsync(AuthTokenKey);
            
            if (!string.IsNullOrEmpty(username) && !string.IsNullOrEmpty(authToken))
            {
                // Create session from stored credentials
                await CreateSessionAsync(username, authToken, true); // Assume 2FA was completed if token exists
                
                // Validate the restored session
                var isValid = await _vrcApiService.IsAuthenticatedAsync();
                if (!isValid)
                {
                    _logger.LogWarning("Restored session is invalid, clearing");
                    await ClearSessionAsync();
                    await ClearStoredCredentialsAsync();
                }
                else
                {
                    _logger.LogInformation("Successfully restored session for {Username}", username);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to restore session from storage");
        }
    }
}