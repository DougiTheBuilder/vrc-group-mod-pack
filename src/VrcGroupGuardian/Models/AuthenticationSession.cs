namespace VrcGroupGuardian.Models;

public class AuthenticationSession
{
    public string UserId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string AuthToken { get; set; } = string.Empty;
    public DateTime TokenExpiry { get; set; }
    public bool TwoFactorAuthenticated { get; set; }
    public Dictionary<string, List<string>> GroupPermissions { get; set; } = new();
    public DateTime LastRefresh { get; set; }
    public bool IsValid { get; set; }

    public bool IsSessionValid()
    {
        if (!IsValid)
            return false;

        if (TokenExpiry <= DateTime.UtcNow)
            return false;

        if (string.IsNullOrEmpty(AuthToken))
            return false;

        return true;
    }

    public bool IsPermissionsCacheValid()
    {
        var cacheAge = DateTime.UtcNow - LastRefresh;
        return cacheAge.TotalMinutes <= 15;
    }

    public bool HasGroupPermission(string groupId, string permission)
    {
        if (!IsPermissionsCacheValid())
            return false;

        return GroupPermissions.ContainsKey(groupId) && 
               GroupPermissions[groupId].Contains(permission);
    }

    public void InvalidateSession()
    {
        IsValid = false;
        AuthToken = string.Empty;
        GroupPermissions.Clear();
    }
}