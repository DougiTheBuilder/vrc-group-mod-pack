namespace VrcGroupGuardian.Models;

public enum InstanceType
{
    Group,
    GroupPlus,
    GroupPublic
}

public enum InstanceStatus
{
    Active,
    Flagged,
    ClosingCountdown,
    Closed
}

public enum MemberPermissionLevel
{
    Member,
    Moderator,
    Admin,
    Owner
}

public enum AuditActionType
{
    AutoClose,
    ManualClose,
    CancelClose,
    KickMember,
    BanMember,
    UnbanMember,
    PolicyChange,
    Login,
    Logout
}

public enum AuditTargetType
{
    Instance,
    Member,
    Policy,
    Session
}

public enum LogLevel
{
    Debug,
    Info,
    Warn,
    Error
}

public enum AuditSeverity
{
    Low,
    Medium,
    High,
    Critical
}