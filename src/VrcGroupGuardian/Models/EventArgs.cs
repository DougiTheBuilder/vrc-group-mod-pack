namespace VrcGroupGuardian.Models;

public class AuditRecordCreatedEventArgs : EventArgs
{
    public AuditRecord Record { get; }

    public AuditRecordCreatedEventArgs(AuditRecord record)
    {
        Record = record;
    }
}

public class MemberUpdatedEventArgs : EventArgs
{
    public GroupMember Member { get; }
    public GroupMember NewMember { get; }
    public string? ChangeDescription { get; set; }

    public MemberUpdatedEventArgs(GroupMember member, string? changeDescription = null)
    {
        Member = member;
        NewMember = member; // For compatibility
        ChangeDescription = changeDescription;
    }

    public MemberUpdatedEventArgs(GroupMember oldMember, GroupMember newMember, string? changeDescription = null)
    {
        Member = oldMember;
        NewMember = newMember;
        ChangeDescription = changeDescription;
    }
}

public class MemberActionEventArgs : EventArgs
{
    public string UserId { get; }
    public string Action { get; }
    public bool Success { get; }
    public string? ErrorMessage { get; set; }

    public MemberActionEventArgs(string userId, string action, bool success, string? errorMessage = null)
    {
        UserId = userId;
        Action = action;
        Success = success;
        ErrorMessage = errorMessage;
    }
}

public class AuthenticationStateChangedEventArgs : EventArgs
{
    public bool IsAuthenticated { get; }
    public string? Username { get; set; }
    public string? ErrorMessage { get; set; }

    public AuthenticationStateChangedEventArgs(bool isAuthenticated, string? username = null, string? errorMessage = null)
    {
        IsAuthenticated = isAuthenticated;
        Username = username;
        ErrorMessage = errorMessage;
    }
}

public class MemberJoinedEventArgs : EventArgs
{
    public GroupMember Member { get; }

    public MemberJoinedEventArgs(GroupMember member)
    {
        Member = member;
    }
}

public class MemberLeftEventArgs : EventArgs
{
    public string UserId { get; }
    public string? DisplayName { get; set; }
    public GroupMember? Member { get; set; }

    public MemberLeftEventArgs(string userId, string? displayName = null)
    {
        UserId = userId;
        DisplayName = displayName;
    }

    public MemberLeftEventArgs(GroupMember member)
    {
        UserId = member.UserId;
        DisplayName = member.DisplayName;
        Member = member;
    }
}