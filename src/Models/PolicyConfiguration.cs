using System.Text.RegularExpressions;

namespace VrcGroupGuardian.Models;

public class PolicyConfiguration
{
    private static readonly Regex GroupIdPattern = new(@"^grp_[a-f0-9-]+$", RegexOptions.Compiled);

    public bool EnforcementEnabled { get; set; }
    public int GracePeriodSeconds { get; set; } = 120;
    public int PollingIntervalSeconds { get; set; } = 60;
    public bool NotificationsEnabled { get; set; } = true;
    public int RateLimitRequestsPerMinute { get; set; } = 20;
    public int CacheExpiryMinutes { get; set; } = 15;
    public LogLevel LogLevel { get; set; } = LogLevel.Info;
    public bool ExportAuditLogs { get; set; } = true;
    public string GroupId { get; set; } = string.Empty;
    public string GroupName { get; set; } = string.Empty;

    public bool IsValid()
    {
        if (GracePeriodSeconds < 60 || GracePeriodSeconds > 300)
            return false;

        if (PollingIntervalSeconds < 45 || PollingIntervalSeconds > 90)
            return false;

        if (RateLimitRequestsPerMinute < 1 || RateLimitRequestsPerMinute > 100)
            return false;

        if (CacheExpiryMinutes < 1 || CacheExpiryMinutes > 60)
            return false;

        if (!string.IsNullOrEmpty(GroupId) && !GroupIdPattern.IsMatch(GroupId))
            return false;

        return true;
    }

    public int GetJitteredPollingInterval()
    {
        var random = new Random();
        var jitter = random.Next(-20, 21); // ±20% jitter
        var jitteredInterval = PollingIntervalSeconds + (PollingIntervalSeconds * jitter / 100);
        return Math.Max(45, Math.Min(90, jitteredInterval));
    }
}