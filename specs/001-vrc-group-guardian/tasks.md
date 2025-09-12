# Tasks: VRC Group Guardian (Desktop)

**Input**: Design documents from `/specs/001-vrc-group-guardian/`
**Prerequisites**: plan.md, research.md, data-model.md, contracts/, quickstart.md

## Execution Flow (main)
```
1. Load plan.md from feature directory
   → Tech stack: .NET 8, WPF MVVM, HttpClient + Polly, Serilog, xUnit
   → Structure: Single project with src/ and tests/
2. Load design documents:
   → data-model.md: 5 entities (GroupInstance, GroupMember, AuditRecord, AuthenticationSession, PolicyConfiguration)
   → contracts/: VRChat API + Internal API contracts
   → quickstart.md: 7 main user scenarios for E2E testing
3. Generated 35 tasks across 6 phases
4. Applied TDD ordering: Tests before implementation
5. Marked [P] for parallel execution (different files)
6. SUCCESS (tasks ready for execution)
```

## Format: `[ID] [P?] Description`
- **[P]**: Can run in parallel (different files, no dependencies)
- File paths are absolute from repository root

## Phase 3.1: Project Setup (Epic A) ✅ COMPLETED
- [x] T001 Create .NET 8 WPF project structure: src/, tests/, VrcGroupGuardian.sln
- [x] T002 Configure project dependencies: WPF, Serilog, Polly, xUnit, WireMock.Net in src/VrcGroupGuardian.csproj
- [x] T003 [P] Setup single-file publish configuration in src/VrcGroupGuardian.csproj (PublishSingleFile=true, SelfContained=true)
- [x] T004 [P] Configure EditorConfig and code style rules in .editorconfig
- [x] T005 [P] Create Serilog configuration with file sink in src/Infrastructure/LoggingConfiguration.cs

## Phase 3.2: Tests First (TDD) ⚠️ MUST COMPLETE BEFORE 3.3
**CRITICAL: These tests MUST be written and MUST FAIL before ANY implementation**

### Contract Tests (VRChat API) ✅ COMPLETED
- [x] T006 [P] VRChat auth contract test in tests/contract/VrchatApiTests/AuthEndpointTests.cs
- [x] T007 [P] VRChat group instances contract test in tests/contract/VrchatApiTests/GroupInstancesTests.cs  
- [x] T008 [P] VRChat group members contract test in tests/contract/VrchatApiTests/GroupMembersTests.cs
- [x] T009 [P] VRChat instance closure contract test in tests/contract/VrchatApiTests/InstanceClosureTests.cs
- [x] T010 [P] VRChat permissions contract test in tests/contract/VrchatApiTests/PermissionsTests.cs

### Contract Tests (Internal Services) ✅ COMPLETED
- [x] T011 [P] Auth service CLI contract test in tests/contract/InternalApiTests/AuthServiceTests.cs
- [x] T012 [P] Instance service CLI contract test in tests/contract/InternalApiTests/InstanceServiceTests.cs
- [x] T013 [P] Member service CLI contract test in tests/contract/InternalApiTests/MemberServiceTests.cs
- [x] T014 [P] Audit service CLI contract test in tests/contract/InternalApiTests/AuditServiceTests.cs
- [x] T015 [P] Enforcement service CLI contract test in tests/contract/InternalApiTests/EnforcementServiceTests.cs

### Integration Tests (User Scenarios) ✅ COMPLETED
- [x] T016 [P] Policy configuration & group selection integration test in tests/integration/PolicyConfigurationTests.cs
- [x] T017 [P] Instance monitoring & detection integration test in tests/integration/InstanceMonitoringTests.cs
- [x] T018 [P] Auto-closure with grace period integration test in tests/integration/AutoClosureTests.cs
- [x] T019 [P] Manual instance management integration test in tests/integration/ManualInstanceManagementTests.cs
- [x] T020 [P] Member management integration test in tests/integration/MemberManagementTests.cs
- [x] T021 [P] Audit trail & compliance integration test in tests/integration/AuditTrailTests.cs
- [x] T022 [P] Error handling & rate limiting integration test in tests/integration/ErrorHandlingTests.cs

## Phase 3.3: Core Models (Only after tests are failing) ✅ COMPLETED
- [x] T023 [P] GroupInstance entity model in src/Models/GroupInstance.cs
- [x] T024 [P] GroupMember entity model in src/Models/GroupMember.cs
- [x] T025 [P] AuditRecord entity model in src/Models/AuditRecord.cs
- [x] T026 [P] AuthenticationSession entity model in src/Models/AuthenticationSession.cs
- [x] T027 [P] PolicyConfiguration entity model in src/Models/PolicyConfiguration.cs
- [x] T028 [P] Enumerations (InstanceType, InstanceStatus, etc.) in src/Models/Enums.cs

## Phase 3.4: Infrastructure Services (Epic A continued) ✅ COMPLETED
- [x] T029 [P] DPAPI/Credential Manager wrapper in src/Infrastructure/SecureStorage.cs
- [x] T030 [P] HttpClient factory with Polly configuration in src/Infrastructure/VrchatHttpClientFactory.cs  
- [x] T031 [P] Rate limiting service with token bucket in src/Infrastructure/RateLimitService.cs
- [x] T032 [P] Settings store with local file persistence in src/Infrastructure/SettingsStore.cs

## Phase 3.5: Business Services (Epics B, C, D, E) ✅ COMPLETED
- [x] T033 VrcApi service library in src/Services/VrcApi/VrcApiService.cs (depends on T030, T031)
- [x] T034 Auth service with login flow & 2FA in src/Services/Auth/AuthService.cs (depends on T029, T033)
- [x] T035 Group service with selection & permissions in src/Services/Groups/GroupService.cs (depends on T033, T034)
- [x] T036 Instances service with polling & monitoring in src/Services/Instances/InstancesService.cs (depends on T033, T035)
- [x] T037 Enforcement service with policy engine in src/Services/Enforcement/EnforcementService.cs (depends on T036)
- [x] T038 Members service with kick/ban operations in src/Services/Members/MembersService.cs (depends on T033, T035)
- [x] T039 Audit service with local logging & export in src/Services/Audit/AuditService.cs

## Phase 3.6: Service CLI Interfaces
- [ ] T040 [P] Auth service CLI in src/Services/Auth/Program.cs (CLI entry point)
- [ ] T041 [P] Instance service CLI in src/Services/Instances/Program.cs 
- [ ] T042 [P] Member service CLI in src/Services/Members/Program.cs
- [ ] T043 [P] Audit service CLI in src/Services/Audit/Program.cs
- [ ] T044 [P] Enforcement service CLI in src/Services/Enforcement/Program.cs

## Phase 3.7: WPF UI Implementation (Epic F)
- [ ] T045 MainWindow XAML layout with left navigation in src/Views/MainWindow.xaml
- [ ] T046 MainWindow ViewModel with navigation in src/ViewModels/MainWindowViewModel.cs (depends on T034-T039)
- [ ] T047 Instances view with data grid in src/Views/InstancesView.xaml
- [ ] T048 Instances ViewModel with policy controls in src/ViewModels/InstancesViewModel.cs (depends on T036, T037)
- [ ] T049 Members view with search & actions in src/Views/MembersView.xaml  
- [ ] T050 Members ViewModel with kick/ban commands in src/ViewModels/MembersViewModel.cs (depends on T038)
- [ ] T051 Audit view with filtering & export in src/Views/AuditView.xaml
- [ ] T052 Audit ViewModel with CSV export in src/ViewModels/AuditViewModel.cs (depends on T039)
- [ ] T053 Settings view with authentication controls in src/Views/SettingsView.xaml
- [ ] T054 Settings ViewModel with credential management in src/ViewModels/SettingsViewModel.cs (depends on T034)

## Phase 3.8: Integration & Polish (Epic G)
- [ ] T055 Wire up WPF navigation and dependency injection in src/App.xaml.cs (depends on T046-T054)
- [ ] T056 First-run setup wizard implementation in src/Views/SetupWizardView.xaml
- [ ] T057 Desktop notifications for policy enforcement in src/Infrastructure/NotificationService.cs
- [ ] T058 High-contrast theme and accessibility support in src/Themes/
- [ ] T059 Performance optimization and startup time improvement
- [ ] T060 Error handling and graceful degradation 
- [ ] T061 Comprehensive logging and diagnostics
- [ ] T062 "Dry run" mode for testing without actual API calls

## Phase 3.9: Final Testing & Release (Epic G continued)
- [ ] T063 [P] Unit tests for policy logic in tests/unit/PolicyEngineTests.cs
- [ ] T064 [P] Unit tests for rate limiting in tests/unit/RateLimitTests.cs  
- [ ] T065 [P] Unit tests for timer management in tests/unit/TimerTests.cs
- [ ] T066 End-to-end manual testing per quickstart.md scenarios
- [ ] T067 Performance testing (startup time, memory usage, API efficiency)
- [ ] T068 Security validation (credential storage, token wiping)
- [ ] T069 Build pipeline and signed executable generation
- [ ] T070 Release documentation and deployment guide

## Dependencies
- Setup (T001-T005) before all other phases
- Tests (T006-T022) before implementation (T023-T062)
- Models (T023-T028) before services (T033-T039)
- Infrastructure (T029-T032) before services
- Services before CLI interfaces (T040-T044) 
- Services before UI ViewModels (T046-T054)
- Core UI before integration (T055-T062)
- Implementation before final testing (T063-T070)

## Parallel Execution Examples

### Contract Tests Phase (T006-T015)
```bash
# Launch all contract tests in parallel:
Task: "VRChat auth contract test in tests/contract/VrchatApiTests/AuthEndpointTests.cs"
Task: "VRChat group instances contract test in tests/contract/VrchatApiTests/GroupInstancesTests.cs" 
Task: "VRChat group members contract test in tests/contract/VrchatApiTests/GroupMembersTests.cs"
Task: "VRChat instance closure contract test in tests/contract/VrchatApiTests/InstanceClosureTests.cs"
Task: "VRChat permissions contract test in tests/contract/VrchatApiTests/PermissionsTests.cs"
```

### Integration Tests Phase (T016-T022)
```bash
# Launch all integration tests in parallel:
Task: "Policy configuration & group selection integration test in tests/integration/PolicyConfigurationTests.cs"
Task: "Instance monitoring & detection integration test in tests/integration/InstanceMonitoringTests.cs"
Task: "Auto-closure with grace period integration test in tests/integration/AutoClosureTests.cs"
Task: "Manual instance management integration test in tests/integration/ManualInstanceManagementTests.cs"
Task: "Member management integration test in tests/integration/MemberManagementTests.cs"
Task: "Audit trail & compliance integration test in tests/integration/AuditTrailTests.cs"
Task: "Error handling & rate limiting integration test in tests/integration/ErrorHandlingTests.cs"
```

### Model Creation Phase (T023-T028)
```bash
# Launch all model classes in parallel:
Task: "GroupInstance entity model in src/Models/GroupInstance.cs"
Task: "GroupMember entity model in src/Models/GroupMember.cs"  
Task: "AuditRecord entity model in src/Models/AuditRecord.cs"
Task: "AuthenticationSession entity model in src/Models/AuthenticationSession.cs"
Task: "PolicyConfiguration entity model in src/Models/PolicyConfiguration.cs"
Task: "Enumerations (InstanceType, InstanceStatus, etc.) in src/Models/Enums.cs"
```

## Notes
- [P] tasks target different files and have no dependencies between them
- Tests MUST fail before implementing corresponding features (TDD)
- Commit after each task completion for clear git history
- Use WireMock.Net for all VRChat API testing to avoid rate limits
- Focus on Windows-specific features (DPAPI, Credential Manager, desktop notifications)

## Validation Checklist
*GATE: Checked before task execution*

- [x] All VRChat API contracts have corresponding tests (T006-T010)
- [x] All internal service contracts have corresponding tests (T011-T015)
- [x] All entities from data-model.md have model tasks (T023-T027)
- [x] All user scenarios have integration tests (T016-T022)
- [x] All tests come before implementation (Phase 3.2 before 3.3+)
- [x] Parallel tasks truly independent (different files)
- [x] Each task specifies exact file path
- [x] No task modifies same file as another [P] task
- [x] Epic requirements mapped to specific tasks
- [x] Acceptance checklist items covered by integration tests