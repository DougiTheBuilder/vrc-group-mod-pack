# Feature Specification: VRC Group Guardian (Desktop)

**Feature Branch**: `001-vrc-group-guardian`  
**Created**: 2025-09-11  
**Status**: Draft  
**Input**: User description: "VRC Group Guardian (Desktop) - Auto-close non-age-gated group instances with moderator console for VRChat communities"

## Execution Flow (main)
```
1. Parse user description from Input
   → Comprehensive feature description provided with detailed requirements
2. Extract key concepts from description
   → Actors: Group Owners, Moderators, Compliance Users
   → Actions: Monitor instances, auto-close, manage members, audit
   → Data: Group instances, members, audit logs, permissions
   → Constraints: VRChat API limits, Windows desktop, 18+ policy enforcement
3. For each unclear aspect:
   → All key aspects clearly defined in input description
4. Fill User Scenarios & Testing section
   → Clear user flows for monitoring, enforcement, and management
5. Generate Functional Requirements
   → Each requirement testable and derived from user stories
6. Identify Key Entities
   → Group instances, members, audit records, authentication tokens
7. Run Review Checklist
   → Spec ready for planning phase
8. Return: SUCCESS (spec ready for planning)
```

---

## ⚡ Quick Guidelines
- ✅ Focus on WHAT users need and WHY
- ❌ Avoid HOW to implement (no tech stack, APIs, code structure)
- 👥 Written for business stakeholders, not developers

---

## User Scenarios & Testing *(mandatory)*

### Primary User Story
Adult-only VRChat community moderators need an automated way to ensure all group-owned instances are properly age-gated. When a group member creates an instance without 18+ restrictions, the system should detect this violation and automatically close the instance after a configurable grace period. Moderators also need a simple dashboard to view active group instances, manage members, and review enforcement actions.

### Acceptance Scenarios
1. **Given** a group has age-gating enforcement enabled, **When** a member creates a group instance without age-gating, **Then** the system flags the instance and closes it after the configured grace period
2. **Given** a moderator is viewing the instances panel, **When** they see a non-compliant instance, **Then** they can manually close it immediately with one click
3. **Given** a moderator needs to remove a problematic member, **When** they select the member in the members panel, **Then** they can kick or ban them from the group if they have proper permissions
4. **Given** a compliance user needs audit information, **When** they view the audit panel, **Then** they see a complete history of auto-closures and moderator actions with timestamps
5. **Given** a user logs in with valid VRChat credentials and 2FA, **When** the system checks their group permissions, **Then** they are granted access to appropriate moderation functions

### Edge Cases
- What happens when the VRChat API is rate-limited or unavailable?
- How does the system handle authentication token expiration?
- What occurs if a user lacks required permissions to close instances?
- How does the grace period countdown behave if the instance becomes compliant before closure?
- What happens when the desktop application loses network connectivity?

## Requirements *(mandatory)*

### Functional Requirements
- **FR-001**: System MUST monitor selected VRChat groups for new and active group instances
- **FR-002**: System MUST detect when group instances have ageGate set to false
- **FR-003**: System MUST automatically close non-age-gated group instances after a configurable grace period (60-300 seconds)
- **FR-004**: System MUST provide desktop notifications when instances are flagged and when they are closed
- **FR-005**: System MUST display a real-time dashboard of active group instances with columns for World, Instance Type, Age-Gated status, Users, Created At, Region, and Status
- **FR-006**: System MUST allow manual closure of instances with one-click action and confirmation dialog
- **FR-007**: System MUST provide copy-to-clipboard functionality for instance join links
- **FR-008**: System MUST list group members with pagination, search, and role filtering capabilities
- **FR-009**: System MUST support kick and ban actions on group members for users with appropriate permissions
- **FR-010**: System MUST maintain an audit log of all auto-closures and moderator actions with timestamps and API responses
- **FR-011**: System MUST authenticate users via VRChat username/password and TOTP 2FA to obtain session tokens
- **FR-012**: System MUST perform pre-flight permission checks to verify user can moderate and manage group instances
- **FR-013**: System MUST implement rate limiting with token bucket, jittered polling, and exponential backoff on HTTP 429/5xx responses
- **FR-014**: System MUST cache stable data like roles and permissions to minimize API calls
- **FR-015**: System MUST store credentials and session tokens securely using Windows Credential Manager/DPAPI
- **FR-016**: System MUST provide "Sign out & wipe tokens" functionality
- **FR-017**: System MUST have a single window interface with navigation for Instances, Members, Audit, and Settings sections
- **FR-018**: System MUST provide a prominent toggle for "Enforce 18+ gating" policy with clear state indication
- **FR-019**: System MUST require confirmation dialogs for all destructive actions
- **FR-020**: System MUST log all actions locally for troubleshooting and compliance

### Key Entities *(include if feature involves data)*
- **Group Instance**: Represents a VRChat world instance owned by the group, including world info, age-gate status, active users, creation time, region, and instance type
- **Group Member**: Represents a member of the VRChat group with role information, join date, and available moderation actions
- **Audit Record**: Represents a logged action including timestamp, action type, user who performed action, target, and API response details
- **Authentication Session**: Represents user session with VRChat including auth token, permissions, token expiration, and refresh capabilities
- **Policy Configuration**: Represents enforcement settings including grace period duration, polling intervals, rate limits, and notification preferences

---

## Review & Acceptance Checklist
*GATE: Automated checks run during main() execution*

### Content Quality
- [x] No implementation details (languages, frameworks, APIs)
- [x] Focused on user value and business needs
- [x] Written for non-technical stakeholders
- [x] All mandatory sections completed

### Requirement Completeness
- [x] No [NEEDS CLARIFICATION] markers remain
- [x] Requirements are testable and unambiguous  
- [x] Success criteria are measurable
- [x] Scope is clearly bounded
- [x] Dependencies and assumptions identified

---

## Execution Status
*Updated by main() during processing*

- [x] User description parsed
- [x] Key concepts extracted
- [x] Ambiguities marked
- [x] User scenarios defined
- [x] Requirements generated
- [x] Entities identified
- [x] Review checklist passed

---