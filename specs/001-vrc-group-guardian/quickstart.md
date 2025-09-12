# Quickstart Guide: VRC Group Guardian

## Overview
This quickstart guide validates all user stories from the feature specification through end-to-end testing scenarios.

## Prerequisites
- Windows 10/11 x64
- VRChat account with group moderation permissions
- Test VRChat group for validation (non-production)

## Installation & Setup

### 1. Download and Launch
```bash
# Download single-file executable
curl -LO https://releases.github.com/vrc-group-guardian/v1.0.0/VrcGroupGuardian.exe

# Launch application
./VrcGroupGuardian.exe
```

### 2. Initial Authentication
1. **Launch Application**: Double-click `VrcGroupGuardian.exe`
2. **Login Screen**: Enter VRChat username and password
3. **2FA Verification**: Enter TOTP code from authenticator app
4. **Permission Check**: Verify green checkmark for "Group Instance Management"

**Expected Result**: Main window opens with "Not monitoring any group" message

## Core User Scenarios

### Scenario 1: Policy Configuration & Group Selection
**User Story**: Group Owner wants "set and forget" policy enforcement

**Steps**:
1. **Select Group**: 
   - Click "Select Group" button
   - Search for test group name
   - Click "Monitor Group" button
2. **Enable Policy**:
   - Toggle "Enforce 18+ gating" to ON
   - Set grace period to 120 seconds
   - Verify policy status shows "Active"

**Expected Results**:
- Group name appears in header
- Policy toggle shows "ON" with green indicator
- Status dot shows green "Monitoring Active"

### Scenario 2: Instance Monitoring & Detection
**User Story**: Moderator sees all active group instances and compliance status

**Preparation** (using test group):
- Create 1 group instance WITH age-gating enabled
- Create 1 group instance WITHOUT age-gating enabled

**Steps**:
1. **View Instances Tab**: Click "Instances" in left navigation
2. **Observe Instance List**: Verify table shows both instances
3. **Check Compliance**: Non-age-gated instance should be highlighted red

**Expected Results**:
```
| World Name    | Type  | Age-Gated | Users | Status      | Actions     |
|---------------|-------|-----------|-------|-------------|-------------|
| Test World 1  | Group | ✅ Yes    | 1/8   | Active      | Close, Copy |
| Test World 2  | Group | ❌ No     | 1/8   | Flagged     | Close, Copy |
```

### Scenario 3: Auto-Closure with Grace Period
**User Story**: System detects violations and enforces policy automatically

**Continuing from Scenario 2**:
1. **Monitor Countdown**: Non-compliant instance shows "Closing in 120s" chip
2. **Countdown Progress**: Timer counts down every second
3. **Manual Cancel Test**: Click "Cancel" to test override (optional)
4. **Auto-Closure**: Wait for timer to reach 0

**Expected Results**:
- Desktop notification: "Instance flagged for closure"
- Countdown timer visible and decreasing
- After timeout: "Instance closed automatically"
- Instance status changes to "Closed"
- New audit log entry created

### Scenario 4: Manual Instance Management
**User Story**: Moderator can immediately close non-compliant instances

**Steps**:
1. **Manual Close**: Click "Close now" button on flagged instance
2. **Confirmation Dialog**: Click "Yes, close instance" 
3. **Verify Closure**: Instance status updates to "Closed"
4. **Copy Join Link**: Click "Copy" button on active instance

**Expected Results**:
- Confirmation dialog appears before closure
- API call succeeds (check network logs)
- Audit log entry: "Manual closure by [username]"
- Join link copied to clipboard

### Scenario 5: Member Management
**User Story**: Moderator can kick/ban problematic members

**Steps**:
1. **Members Tab**: Click "Members" in left navigation
2. **Search Member**: Type partial username in search box
3. **View Member Info**: Verify role and join date display
4. **Kick Action** (if permitted):
   - Click "Kick" button next to member
   - Enter reason: "Test kick action"
   - Confirm action
5. **Ban Test** (if permitted):
   - Click "Ban" button
   - Enter reason: "Test ban"
   - Confirm action

**Expected Results**:
- Search filters member list correctly
- Actions only available if user has permissions
- Confirmation dialogs for destructive actions
- Member disappears from list after kick
- Audit log entries for both actions

### Scenario 6: Audit Trail & Compliance
**User Story**: Compliance user needs proof of policy enforcement

**Steps**:
1. **Audit Tab**: Click "Audit" in left navigation
2. **Review Entries**: Verify all previous actions are logged
3. **Filter by Action**: Select "Auto-Close" from dropdown
4. **Export Data**: Click "Export CSV" button
5. **Open Export**: Verify CSV contains all audit data

**Expected Results**:
```
Timestamp           | Action      | Actor         | Target           | Result  | Details
2025-09-11 14:30:15 | AutoClose   | System        | Test World 2     | Success | Grace period expired
2025-09-11 14:28:45 | Manual Close| moderatoruser | Test World 3     | Success | Manual closure
2025-09-11 14:27:12 | KickMember  | moderatoruser | testuser123      | Success | Test kick action
```

### Scenario 7: Error Handling & Rate Limiting
**User Story**: System handles API limitations gracefully

**Steps**:
1. **Rate Limit Test**: 
   - Click refresh button rapidly (>20 times in 1 minute)
   - Observe rate limiting behavior
2. **Network Error Test**:
   - Disconnect internet
   - Wait for next poll cycle
   - Reconnect internet
3. **Permission Error Test**:
   - Use account without group permissions
   - Attempt to close instance

**Expected Results**:
- Rate limit banner: "Slowing down API requests due to rate limiting"
- Network error banner: "Connection lost, retrying..."
- Permission error: "Insufficient permissions to close instances"
- Exponential backoff increases poll intervals
- No application crashes or data loss

## Settings Validation

### Authentication Settings
**Steps**:
1. **Settings Tab**: Click "Settings" in navigation
2. **View Session**: Verify current user and token expiry
3. **Sign Out Test**: Click "Sign out & wipe tokens"
4. **Re-authenticate**: Login again to verify credential wiping

### Policy Tuning
**Steps**:
1. **Adjust Grace Period**: Change from 120s to 300s
2. **Polling Interval**: Modify poll frequency (45-90s range)
3. **Rate Limiting**: Adjust requests per minute (1-100 range)
4. **Notifications**: Toggle desktop notifications on/off

**Expected Results**:
- All settings persist between app restarts
- Policy changes take effect immediately
- Validation prevents invalid values
- Help tooltips explain each setting

## Performance Validation

### Startup Performance
**Measurement**: Time from launch to main window appearance
**Target**: <3 seconds on average hardware
**Test**: 5 consecutive cold starts, measure with stopwatch

### Resource Usage  
**Measurement**: Memory and CPU usage during normal operation
**Test**: Monitor Task Manager during 30-minute monitoring session
**Expected**: <100MB memory, <5% CPU when idle

### API Efficiency
**Measurement**: API calls made during typical usage
**Test**: Monitor network tab during 10-minute session
**Expected**: Within rate limits, appropriate caching behavior

## Security Validation

### Credential Storage
**Steps**:
1. Login and close application
2. Check Windows Credential Manager for stored tokens
3. Verify no plaintext passwords in application files
4. Test credential encryption with different user accounts

### Token Security
**Steps**:
1. Extract token from credential manager (if possible)
2. Verify tokens are encrypted at rest
3. Test token wiping on sign out
4. Verify session invalidation on logout

## Troubleshooting Common Issues

### Authentication Failures
- **2FA timeout**: Ensure system clock is accurate
- **Account locked**: Check VRChat website for verification emails
- **Invalid credentials**: Verify username/password on VRChat.com

### Permission Issues  
- **Cannot close instances**: Verify "Manage Group Instances" role permission
- **Cannot kick members**: Verify "Moderate Group Instances" permission
- **Empty group list**: Check group membership and role assignments

### Network Issues
- **Rate limiting**: Reduce polling frequency in settings
- **Connection timeouts**: Check firewall/antivirus settings
- **SSL errors**: Ensure Windows is updated with latest certificates

## Success Criteria

✅ **All user scenarios complete without errors**
✅ **Performance targets met (<3s startup, <100MB memory)**  
✅ **Security validation passed (encrypted storage, no plaintext)**
✅ **API compliance confirmed (respects rate limits)**
✅ **Audit trail complete and exportable**
✅ **Error handling graceful (no crashes)**

## Next Steps

After successful quickstart validation:
1. Deploy to production group with real moderators
2. Monitor audit logs for effectiveness  
3. Gather feedback on UI/UX improvements
4. Plan additional features based on usage patterns

---

**Note**: This quickstart guide serves as both user documentation and integration test specification. All scenarios must pass before release candidate approval.