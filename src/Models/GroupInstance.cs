using System.Text.RegularExpressions;

namespace VrcGroupGuardian.Models;

public class GroupInstance
{
    private static readonly Regex InstanceIdPattern = new(@"^wrld_[a-f0-9-]+:[0-9]+~[a-zA-Z0-9()]+$", RegexOptions.Compiled);

    public string InstanceId { get; set; } = string.Empty;
    public string WorldName { get; set; } = string.Empty;
    public string WorldId { get; set; } = string.Empty;
    public string GroupName { get; set; } = string.Empty;
    public InstanceType InstanceType { get; set; }
    public bool AgeGated { get; set; }
    public int UserCount { get; set; }
    public int MaxUsers { get; set; }
    public string Region { get; set; } = string.Empty;
    public string UserRole { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public InstanceStatus Status { get; set; }
    public TimeSpan? CountdownTimer { get; set; }
    public DateTime LastUpdated { get; set; }

    public bool IsValid()
    {
        if (string.IsNullOrEmpty(InstanceId) || !InstanceIdPattern.IsMatch(InstanceId))
            return false;

        if (UserCount < 0 || UserCount > MaxUsers)
            return false;

        if (CreatedAt > DateTime.UtcNow)
            return false;

        if (Status == InstanceStatus.ClosingCountdown && CountdownTimer == null)
            return false;

        if (Status != InstanceStatus.ClosingCountdown && CountdownTimer != null)
            return false;

        return true;
    }

    public bool CanTransitionTo(InstanceStatus newStatus)
    {
        return Status switch
        {
            InstanceStatus.Active => newStatus == InstanceStatus.Flagged,
            InstanceStatus.Flagged => newStatus is InstanceStatus.ClosingCountdown or InstanceStatus.Active,
            InstanceStatus.ClosingCountdown => newStatus is InstanceStatus.Closed or InstanceStatus.Active,
            InstanceStatus.Closed => false,
            _ => false
        };
    }
}