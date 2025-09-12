# Data Model: VRC Group Guardian

## Core Entities

### GroupInstance
Represents a VRChat world instance owned by the monitored group.

**Fields**:
- `InstanceId: string` - VRChat instance identifier (worldId:instanceId format)
- `WorldName: string` - Display name of the VRChat world
- `WorldId: string` - VRChat world identifier
- `InstanceType: InstanceType enum` - Group, GroupPlus, GroupPublic
- `AgeGated: bool` - Whether instance requires 18+ verification
- `UserCount: int` - Current number of users in instance
- `MaxUsers: int` - Maximum capacity of the instance
- `Region: string` - VRChat region (US West, EU, etc.)
- `CreatedAt: DateTime` - When the instance was created
- `Status: InstanceStatus enum` - Active, Flagged, ClosingCountdown, Closed
- `CountdownTimer: TimeSpan?` - Remaining time before auto-closure (null if not flagged)
- `LastUpdated: DateTime` - When this data was last refreshed from API

**Validation Rules**:
- InstanceId must match pattern: `wrld_[a-f0-9-]+:[0-9]+~[a-zA-Z0-9()]+`
- UserCount must be >= 0 and <= MaxUsers
- CreatedAt cannot be in the future
- CountdownTimer only valid when Status = ClosingCountdown

**State Transitions**:
- Active → Flagged (when AgeGated = false detected and enforcement enabled)
- Flagged → ClosingCountdown (after policy evaluation)
- ClosingCountdown → Closed (when timer expires)
- ClosingCountdown → Active (when manually cancelled or becomes compliant)
- Any State → Active (when AgeGated becomes true)

### GroupMember
Represents a member of the VRChat group being monitored.

**Fields**:
- `UserId: string` - VRChat user identifier (usr_*)
- `DisplayName: string` - User's current display name
- `Username: string` - User's VRChat username
- `Role: string` - User's role in the group
- `JoinedAt: DateTime` - When user joined the group
- `LastSeen: DateTime` - Last time user was active in group
- `PermissionLevel: MemberPermissionLevel enum` - Member, Moderator, Admin, Owner
- `CanKick: bool` - Whether current user can kick this member
- `CanBan: bool` - Whether current user can ban this member

**Validation Rules**:
- UserId must match pattern: `usr_[a-f0-9-]+`
- DisplayName length <= 64 characters
- JoinedAt cannot be in the future
- LastSeen cannot be in the future

### AuditRecord
Represents a logged action for compliance and troubleshooting.

**Fields**:
- `Id: Guid` - Unique identifier for the audit record
- `Timestamp: DateTime` - When the action occurred (UTC)
- `ActionType: AuditActionType enum` - AutoClose, ManualClose, Kick, Ban, PolicyChange, etc.
- `ActorUserId: string?` - User who performed the action (null for system actions)
- `ActorDisplayName: string?` - Display name of the actor at time of action
- `TargetType: AuditTargetType enum` - Instance, Member, Policy
- `TargetId: string` - ID of the affected target (instance ID, user ID, etc.)
- `TargetDisplayName: string` - Display name of target at time of action
- `Details: string` - Additional context (JSON serialized data)
- `ApiResponse: string?` - VRChat API response if applicable
- `Success: bool` - Whether the action succeeded
- `ErrorMessage: string?` - Error details if action failed

**Validation Rules**:
- Timestamp must be valid UTC DateTime
- TargetId required and non-empty
- Success = false requires ErrorMessage
- Success = true prohibits ErrorMessage

### AuthenticationSession
Represents the current VRChat authentication state.

**Fields**:
- `UserId: string` - Authenticated VRChat user ID
- `DisplayName: string` - User's display name
- `Username: string` - User's VRChat username
- `AuthToken: string` - VRChat session cookie (encrypted at rest)
- `TokenExpiry: DateTime` - When the token expires
- `TwoFactorAuthenticated: bool` - Whether 2FA was completed
- `GroupPermissions: Dictionary<string, List<string>>` - Group ID → list of permissions
- `LastRefresh: DateTime` - When session was last validated
- `IsValid: bool` - Whether session is currently valid

**Validation Rules**:
- AuthToken must be encrypted before storage
- TokenExpiry must be in the future for valid sessions
- TwoFactorAuthenticated required for any privileged operations
- GroupPermissions cached for max 15 minutes

**State Transitions**:
- Unauthenticated → Authenticated (successful login + 2FA)
- Authenticated → Expired (token expiry reached)
- Authenticated → Invalid (API returns 401/403)
- Any State → Unauthenticated (manual logout)

### PolicyConfiguration
Represents enforcement policy settings.

**Fields**:
- `EnforcementEnabled: bool` - Whether auto-closure is active
- `GracePeriodSeconds: int` - Delay before auto-closure (60-300)
- `PollingIntervalSeconds: int` - Time between API polls (45-90 with jitter)
- `NotificationsEnabled: bool` - Whether desktop notifications are shown
- `RateLimitRequestsPerMinute: int` - API request budget (default 20)
- `CacheExpiryMinutes: int` - How long to cache stable data (default 15)
- `LogLevel: LogLevel enum` - Minimum log level (Debug, Info, Warn, Error)
- `ExportAuditLogs: bool` - Whether to enable CSV export
- `GroupId: string` - Currently monitored group ID
- `GroupName: string` - Display name of monitored group

**Validation Rules**:
- GracePeriodSeconds must be 60-300
- PollingIntervalSeconds must be 45-90
- RateLimitRequestsPerMinute must be 1-100
- CacheExpiryMinutes must be 1-60
- GroupId must match pattern: `grp_[a-f0-9-]+`

## Enumerations

### InstanceType
- `Group` - Private group instance
- `GroupPlus` - Friends+ group instance  
- `GroupPublic` - Public group instance

### InstanceStatus
- `Active` - Normal operation
- `Flagged` - Detected non-compliant but not yet in countdown
- `ClosingCountdown` - Grace period active, will close unless cancelled
- `Closed` - Instance has been closed

### MemberPermissionLevel
- `Member` - Standard group member
- `Moderator` - Can moderate instances and members
- `Admin` - Can manage group settings
- `Owner` - Full group control

### AuditActionType
- `AutoClose` - System closed non-compliant instance
- `ManualClose` - User manually closed instance
- `CancelClose` - User cancelled pending auto-closure
- `KickMember` - Removed member from group
- `BanMember` - Banned member from group
- `UnbanMember` - Removed member ban
- `PolicyChange` - Modified enforcement settings
- `Login` - User authenticated
- `Logout` - User signed out

### AuditTargetType
- `Instance` - Group instance
- `Member` - Group member
- `Policy` - Enforcement policy
- `Session` - Authentication session

### LogLevel
- `Debug` - Detailed diagnostics
- `Info` - General information
- `Warn` - Non-critical issues
- `Error` - Error conditions

## Relationships

- **GroupInstance** ↔ **AuditRecord**: One-to-many (instances can have multiple audit records)
- **GroupMember** ↔ **AuditRecord**: One-to-many (members can have multiple audit records)
- **AuthenticationSession** → **GroupPermissions**: One-to-many (user can have permissions in multiple groups)
- **PolicyConfiguration** → **GroupInstance**: One-to-many (policy applies to all instances in the configured group)

## Data Flow

1. **Authentication**: User provides credentials → AuthenticationSession created → GroupPermissions cached
2. **Instance Monitoring**: PolicyConfiguration drives polling → GroupInstance records updated → State transitions triggered
3. **Enforcement**: Non-compliant GroupInstance → AuditRecord created → VRChat API called → Results logged
4. **Member Management**: User action on GroupMember → AuditRecord created → VRChat API called → Results logged
5. **Audit Trail**: All actions create AuditRecord entries → Available for export and compliance review