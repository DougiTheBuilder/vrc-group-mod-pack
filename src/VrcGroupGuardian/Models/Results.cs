namespace VrcGroupGuardian.Models;

public class MemberActionResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public string? Message { get; set; }
    public string? Reason { get; set; }
    public string? UserId { get; set; }
    public string? DisplayName { get; set; }
}

public class BulkMemberResult
{
    public int TotalRequested { get; set; }
    public int TotalAttempted { get; set; }
    public int SuccessfulActions { get; set; }
    public int FailedActions { get; set; }
    public List<MemberActionResult> Results { get; set; } = new();
    public List<string> SuccessfulMembers { get; set; } = new();
    public Dictionary<string, string> FailedMembers { get; set; } = new();
    public string? OverallErrorMessage { get; set; }

    // Convenience properties for backward compatibility
    public bool Success => FailedActions == 0;
    public int SuccessCount => SuccessfulActions;
    public string? Message => OverallErrorMessage;
}

public class CurrentUser
{
    public string Id { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
}

public class BanStatus
{
    public bool IsBanned { get; set; }
    public DateTime? BannedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public string? Reason { get; set; }
}

public class ExportResult
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public string? FilePath { get; set; }
    public int RecordCount { get; set; }
    public string? Format { get; set; }
}

public class ClearRecordsResult
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public int RecordsDeleted { get; set; }
}