namespace VrcGroupGuardian.Models;

public class AuditRecord
{
    public Guid Id { get; set; }
    public DateTime Timestamp { get; set; }
    public AuditActionType ActionType { get; set; }
    public AuditSeverity Severity { get; set; }
    public string? ActorUserId { get; set; }
    public string? ActorDisplayName { get; set; }
    public AuditTargetType TargetType { get; set; }
    public string TargetId { get; set; } = string.Empty;
    public string TargetDisplayName { get; set; } = string.Empty;
    public string Details { get; set; } = string.Empty;
    public string? ApiResponse { get; set; }
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }

    public bool IsValid()
    {
        if (Timestamp == DateTime.MinValue)
            return false;

        if (string.IsNullOrEmpty(TargetId))
            return false;

        if (!Success && string.IsNullOrEmpty(ErrorMessage))
            return false;

        if (Success && !string.IsNullOrEmpty(ErrorMessage))
            return false;

        return true;
    }
}