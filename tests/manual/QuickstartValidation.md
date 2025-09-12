# Quickstart Manual Testing Checklist

**Version**: 1.0  
**Date**: 2025-09-11  
**Tester**: [Name]  
**Environment**: Windows [Version]  

## Prerequisites Validation

- [ ] **OS Version**: Windows 10/11 x64 confirmed
- [ ] **VRChat Account**: Test account has group moderation permissions
- [ ] **Test Group**: Non-production VRChat group available for testing
- [ ] **Network**: Stable internet connection available

## Installation & Setup Testing

### Initial Launch
- [ ] **Application Start**: VrcGroupGuardian.exe launches without errors
- [ ] **Setup Wizard**: First-run wizard appears on initial startup
- [ ] **UI Rendering**: All UI elements display correctly (no missing text/icons)
- [ ] **Window Behavior**: Application window is resizable and responsive

**Notes**: _______________________________________________________________

### Authentication Flow
- [ ] **Login Form**: Username/password fields accept input correctly
- [ ] **Credentials Validation**: Invalid credentials show appropriate error
- [ ] **2FA Prompt**: Two-factor authentication prompt appears after valid login
- [ ] **2FA Validation**: TOTP code validation works correctly
- [ ] **Permission Check**: Group management permissions verified automatically
- [ ] **Main Window**: Application transitions to main interface after successful auth

**Auth Test Results**:
- Login Time: _______ seconds
- Any Errors: _________________________________________________________________

## Core Scenario Testing

### Scenario 1: Policy Configuration & Group Selection
**Objective**: Validate policy setup and group monitoring activation

- [ ] **Group Selection UI**: Group picker shows available groups
- [ ] **Group Search**: Search functionality filters groups correctly
- [ ] **Group Activation**: "Monitor Group" button enables monitoring
- [ ] **Policy Toggle**: 18+ gating policy can be enabled/disabled
- [ ] **Grace Period Setting**: Grace period accepts values 60-600 seconds
- [ ] **Status Indicators**: Green "Monitoring Active" status appears
- [ ] **UI Updates**: Group name displays in header correctly

**Configuration Used**:
- Selected Group: ___________________________
- Grace Period: _________ seconds
- Policy Status: ________________________

### Scenario 2: Instance Monitoring & Detection
**Objective**: Verify instance discovery and compliance checking

**Test Setup Required**:
- [ ] **Compliant Instance Created**: Age-gated group instance active
- [ ] **Non-Compliant Instance Created**: Non-age-gated group instance active

**Testing Steps**:
- [ ] **Instance List**: Both test instances appear in Instances tab
- [ ] **Compliance Status**: Age-gated instance shows green/compliant
- [ ] **Violation Status**: Non-age-gated instance shows red/flagged
- [ ] **Instance Details**: World name, user count, and type display correctly
- [ ] **Real-time Updates**: Instance list refreshes automatically
- [ ] **Visual Indicators**: Color coding clearly distinguishes compliant vs flagged

**Instance Detection Results**:
```
Instance Name          | Compliant | Status    | Notes
______________________|___________|___________|______________
                      |           |           |
                      |           |           |
```

### Scenario 3: Auto-Closure with Grace Period
**Objective**: Test automated policy enforcement with countdown

- [ ] **Grace Period Timer**: Non-compliant instance shows countdown timer
- [ ] **Timer Accuracy**: Countdown decreases by 1 second intervals
- [ ] **Desktop Notification**: "Instance flagged for closure" notification appears
- [ ] **Cancel Function**: "Cancel closure" button works correctly
- [ ] **Auto-Execution**: Instance closes automatically when timer reaches 0
- [ ] **Status Update**: Instance status changes to "Closed" after closure
- [ ] **Audit Logging**: Auto-closure creates audit log entry
- [ ] **Success Notification**: "Instance closed automatically" confirmation

**Timer Test Results**:
- Grace Period Set: _______ seconds
- Actual Countdown Time: _______ seconds
- Auto-closure Success: [ ] Yes [ ] No
- Notification Received: [ ] Yes [ ] No

### Scenario 4: Manual Instance Management
**Objective**: Verify manual intervention capabilities

- [ ] **Manual Close Button**: "Close now" button available on flagged instances
- [ ] **Confirmation Dialog**: Closure confirmation dialog appears
- [ ] **Immediate Closure**: Instance closes immediately upon confirmation
- [ ] **API Success**: Network logs show successful VRChat API call
- [ ] **Status Update**: Instance status updates to "Closed" in UI
- [ ] **Audit Entry**: Manual closure logged with username and timestamp
- [ ] **Copy Link Function**: "Copy" button copies join link to clipboard
- [ ] **Link Validation**: Copied link is valid VRChat instance URL

**Manual Management Results**:
- Manual close time: _______ seconds
- Join link copied: [ ] Yes [ ] No
- Link format valid: [ ] Yes [ ] No

### Scenario 5: Member Management
**Objective**: Test member kick/ban functionality

- [ ] **Members Tab Navigation**: Members tab loads member list correctly
- [ ] **Member Search**: Search box filters members by username
- [ ] **Member Details**: Role, join date, and status display for each member
- [ ] **Permission Check**: Kick/Ban buttons only appear if user has permissions
- [ ] **Kick Functionality**: Kick member flow works with reason input
- [ ] **Ban Functionality**: Ban member flow works with reason input
- [ ] **Confirmation Dialogs**: Confirmation required for destructive actions
- [ ] **UI Updates**: Member removed from list after successful action
- [ ] **Audit Logging**: Member actions logged with reason and timestamp

**Member Management Results**:
- Total members shown: _________
- Search functionality: [ ] Working [ ] Issues
- Kick test: [ ] Success [ ] Failed [ ] No Permission
- Ban test: [ ] Success [ ] Failed [ ] No Permission

### Scenario 6: Audit Trail & Compliance
**Objective**: Validate audit logging and export functionality

- [ ] **Audit Tab**: All previous actions appear in audit log
- [ ] **Log Completeness**: Every tested action has corresponding audit entry
- [ ] **Timestamp Accuracy**: Timestamps match actual action times
- [ ] **Action Filtering**: Filter dropdown correctly filters by action type
- [ ] **CSV Export**: "Export CSV" button generates download
- [ ] **Export Content**: CSV contains all required columns and data
- [ ] **Data Integrity**: Exported data matches UI display

**Audit Trail Results**:
- Total audit entries: _________
- Export file size: _______ KB
- Data accuracy: [ ] Perfect [ ] Minor issues [ ] Major issues

**Sample Audit Entries**:
```
Timestamp           | Action      | Actor    | Target      | Result  | Details
___________________|_____________|__________|_____________|_________|______________
                   |             |          |             |         |
                   |             |          |             |         |
                   |             |          |             |         |
```

### Scenario 7: Error Handling & Rate Limiting
**Objective**: Test resilience and graceful degradation

#### Rate Limiting Test
- [ ] **Rapid Requests**: Clicked refresh 25+ times within 1 minute
- [ ] **Rate Limit Detection**: Application detects rate limiting
- [ ] **User Notification**: Rate limit warning banner appears
- [ ] **Automatic Throttling**: Requests automatically slowed down
- [ ] **Recovery**: Normal operation resumes after cool-down period

#### Network Error Test
- [ ] **Connection Loss**: Disconnected internet during monitoring
- [ ] **Error Detection**: Application detects network failure
- [ ] **User Feedback**: "Connection lost, retrying..." message appears
- [ ] **Reconnection**: Automatic reconnection when internet restored
- [ ] **State Preservation**: Monitoring state preserved through disconnection

#### Permission Error Test
- [ ] **Insufficient Permissions**: Tested with limited permission account
- [ ] **Error Handling**: Permission errors handled gracefully
- [ ] **User Message**: Clear permission error messages displayed
- [ ] **Graceful Degradation**: Non-permitted functions disabled in UI

**Error Handling Results**:
- Rate limit recovery time: _______ seconds
- Network error recovery: [ ] Automatic [ ] Manual [ ] Failed
- Permission errors clear: [ ] Yes [ ] No

## Settings Validation

### Authentication Settings
- [ ] **Session Display**: Current user and token expiry shown
- [ ] **Sign Out Function**: "Sign out & wipe tokens" works correctly
- [ ] **Credential Clearing**: Tokens removed from credential manager
- [ ] **Re-authentication**: Can log in again after sign out
- [ ] **Session Security**: No credential remnants after logout

### Policy Tuning
- [ ] **Grace Period Adjustment**: Can modify grace period (60-600s range)
- [ ] **Polling Interval**: Polling frequency adjustable (45-90s range)
- [ ] **Rate Limit Settings**: Requests per minute configurable (1-100 range)
- [ ] **Notification Toggle**: Desktop notifications can be enabled/disabled
- [ ] **Settings Persistence**: Settings saved between application restarts
- [ ] **Input Validation**: Invalid values rejected with helpful messages

**Settings Test Results**:
- Settings persist: [ ] Yes [ ] No
- Validation working: [ ] Yes [ ] No
- Help tooltips: [ ] Present [ ] Missing

## Performance Validation

### Startup Performance
**Test Method**: 5 consecutive cold starts with stopwatch timing

| Attempt | Startup Time (seconds) | Notes |
|---------|------------------------|-------|
| 1       |                        |       |
| 2       |                        |       |
| 3       |                        |       |
| 4       |                        |       |
| 5       |                        |       |

**Average Startup Time**: _______ seconds  
**Target Met (<3 seconds)**: [ ] Yes [ ] No

### Resource Usage
**Test Method**: 30-minute monitoring session with Task Manager observation

- **Memory Usage**:
  - Initial: _______ MB
  - Peak: _______ MB
  - Average: _______ MB
  - Target Met (<100MB): [ ] Yes [ ] No

- **CPU Usage**:
  - Idle Average: _______ %
  - Peak During Activity: _______ %
  - Target Met (<5% idle): [ ] Yes [ ] No

### API Efficiency
**Test Method**: 10-minute session with network monitoring

- [ ] **Within Rate Limits**: All API calls respect VRChat rate limits
- [ ] **Caching Behavior**: Repeated requests use cached data appropriately
- [ ] **Request Efficiency**: No unnecessary or duplicate API calls
- [ ] **Error Handling**: Failed requests handled without retrying excessively

**API Efficiency Results**:
- Total API calls: _________
- Cache hit rate: _______ %
- Rate limit violations: _________

## Security Validation

### Credential Storage
- [ ] **Windows Credential Manager**: Tokens stored in credential manager
- [ ] **Encryption at Rest**: Credentials encrypted (not plaintext)
- [ ] **User Account Isolation**: Different users have separate credential stores
- [ ] **File System Check**: No plaintext passwords in application files
- [ ] **Memory Security**: No credentials visible in process memory dumps

### Token Security
- [ ] **Token Encryption**: Stored tokens are encrypted
- [ ] **Secure Wiping**: Sign out completely removes stored tokens
- [ ] **Session Invalidation**: Logout invalidates server-side sessions
- [ ] **Token Rotation**: Tokens refresh automatically before expiration

**Security Test Results**:
- Credential manager entry found: [ ] Yes [ ] No
- Tokens encrypted: [ ] Yes [ ] No
- Complete token wipe: [ ] Yes [ ] No

## Overall Results Summary

### Test Completion Status
- [ ] All scenarios completed without critical failures
- [ ] Performance targets achieved
- [ ] Security validation passed
- [ ] UI/UX acceptable for production use

### Critical Issues Found
1. ________________________________________________________________
2. ________________________________________________________________
3. ________________________________________________________________

### Minor Issues/Improvements
1. ________________________________________________________________
2. ________________________________________________________________
3. ________________________________________________________________

### Recommendation
- [ ] **PASS**: Ready for production deployment
- [ ] **CONDITIONAL PASS**: Ready with minor fixes
- [ ] **FAIL**: Critical issues must be resolved before release

**Overall Assessment**: ___________________________________________________

**Tester Signature**: _________________________ **Date**: _____________

---

## Notes for Future Testing
- Update test group credentials if they change
- Monitor VRChat API changes that might affect functionality
- Test with different Windows versions and hardware configurations
- Validate accessibility features on high-contrast displays
- Test with various antivirus/firewall configurations