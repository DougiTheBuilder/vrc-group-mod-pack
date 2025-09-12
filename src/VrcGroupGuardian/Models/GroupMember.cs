using System.Text.RegularExpressions;

namespace VrcGroupGuardian.Models;

public class GroupMember
{
    private static readonly Regex UserIdPattern = new(@"^usr_[a-f0-9-]+$", RegexOptions.Compiled);

    public string UserId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public List<string> Roles { get; set; } = new();
    public bool IsOnline { get; set; }
    public DateTime JoinedAt { get; set; }
    public DateTime LastSeen { get; set; }
    public MemberPermissionLevel PermissionLevel { get; set; }
    public bool CanKick { get; set; }
    public bool CanBan { get; set; }

    public bool IsValid()
    {
        if (string.IsNullOrEmpty(UserId) || !UserIdPattern.IsMatch(UserId))
            return false;

        if (DisplayName.Length > 64)
            return false;

        if (JoinedAt > DateTime.UtcNow)
            return false;

        if (LastSeen > DateTime.UtcNow)
            return false;

        return true;
    }
}