# Implementation Plan: VRC Group Guardian (Desktop)

**Branch**: `001-vrc-group-guardian` | **Date**: 2025-09-11 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/001-vrc-group-guardian/spec.md`

## Execution Flow (/plan command scope)
```
1. Load feature spec from Input path
   → If not found: ERROR "No feature spec at {path}"
2. Fill Technical Context (scan for NEEDS CLARIFICATION)
   → Detect Project Type from context (web=frontend+backend, mobile=app+api)
   → Set Structure Decision based on project type
3. Evaluate Constitution Check section below
   → If violations exist: Document in Complexity Tracking
   → If no justification possible: ERROR "Simplify approach first"
   → Update Progress Tracking: Initial Constitution Check
4. Execute Phase 0 → research.md
   → If NEEDS CLARIFICATION remain: ERROR "Resolve unknowns"
5. Execute Phase 1 → contracts, data-model.md, quickstart.md, agent-specific template file (e.g., `CLAUDE.md` for Claude Code, `.github/copilot-instructions.md` for GitHub Copilot, or `GEMINI.md` for Gemini CLI).
6. Re-evaluate Constitution Check section
   → If new violations: Refactor design, return to Phase 1
   → Update Progress Tracking: Post-Design Constitution Check
7. Plan Phase 2 → Describe task generation approach (DO NOT create tasks.md)
8. STOP - Ready for /tasks command
```

**IMPORTANT**: The /plan command STOPS at step 7. Phases 2-4 are executed by other commands:
- Phase 2: /tasks command creates tasks.md
- Phase 3-4: Implementation execution (manual or via tools)

## Summary
Desktop Windows application for VRChat community moderators to automatically detect and close non-age-gated group instances. Provides a single-window interface for monitoring active group instances, managing members, and viewing audit logs. Built with .NET 8/WPF using MVVM pattern, integrates with VRChat API for authentication, instance monitoring, and enforcement actions.

## Technical Context
**Language/Version**: .NET 8 (C#)
**Primary Dependencies**: WPF, HttpClient, Polly (retries/backoff), Serilog (logging), xUnit (testing), WireMock.Net (API mocking)
**Storage**: Local files (audit logs, cache), Windows Credential Manager/DPAPI (secure token storage)
**Testing**: xUnit with WireMock.Net for VRChat API simulations
**Target Platform**: Windows desktop (.exe), offline-first, single-file self-contained x64
**Project Type**: single - desktop application
**Performance Goals**: <3s startup time, 60s±20% polling intervals, 20 req/min API budget
**Constraints**: VRChat API rate limits, Windows-only, no telemetry by default, MVVM UI pattern
**Scale/Scope**: Single group monitoring, 100s of instances/members, local audit storage

## Constitution Check
*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

**Simplicity**:
- Projects: 1 (desktop app only)
- Using framework directly? Yes (WPF directly, no custom UI framework)
- Single data model? Yes (domain models used directly for UI binding)
- Avoiding patterns? Yes (direct service calls, no repository pattern)

**Architecture**:
- EVERY feature as library? Yes (all services as separate libraries)
- Libraries listed: VrcApi (VRChat integration), Enforcement (policy engine), Auth (authentication), Audit (logging)
- CLI per library: Yes (each service library exposes CLI for testing/diagnostics)
- Library docs: llms.txt format planned? Yes

**Testing (NON-NEGOTIABLE)**:
- RED-GREEN-Refactor cycle enforced? Yes (tests written first)
- Git commits show tests before implementation? Yes (contract tests first)
- Order: Contract→Integration→E2E→Unit strictly followed? Yes
- Real dependencies used? Yes (WireMock for VRChat API, real Windows credential store)
- Integration tests for: VRChat API contracts, authentication flow, enforcement policies
- FORBIDDEN: Implementation before test, skipping RED phase - Enforced

**Observability**:
- Structured logging included? Yes (Serilog with structured output)
- Frontend logs → backend? N/A (single desktop app)
- Error context sufficient? Yes (full context for VRChat API errors)

**Versioning**:
- Version number assigned? 1.0.0 (MAJOR.MINOR.BUILD)
- BUILD increments on every change? Yes
- Breaking changes handled? Yes (settings migration, API contract versioning)

## Project Structure

### Documentation (this feature)
```
specs/[###-feature]/
├── plan.md              # This file (/plan command output)
├── research.md          # Phase 0 output (/plan command)
├── data-model.md        # Phase 1 output (/plan command)
├── quickstart.md        # Phase 1 output (/plan command)
├── contracts/           # Phase 1 output (/plan command)
└── tasks.md             # Phase 2 output (/tasks command - NOT created by /plan)
```

### Source Code (repository root)
```
# Option 1: Single project (DEFAULT)
src/
├── models/
├── services/
├── cli/
└── lib/

tests/
├── contract/
├── integration/
└── unit/

# Option 2: Web application (when "frontend" + "backend" detected)
backend/
├── src/
│   ├── models/
│   ├── services/
│   └── api/
└── tests/

frontend/
├── src/
│   ├── components/
│   ├── pages/
│   └── services/
└── tests/

# Option 3: Mobile + API (when "iOS/Android" detected)
api/
└── [same as backend above]

ios/ or android/
└── [platform-specific structure]
```

**Structure Decision**: Option 1 (Single project) - Desktop application structure

## Phase 0: Outline & Research
1. **Extract unknowns from Technical Context** above:
   - For each NEEDS CLARIFICATION → research task
   - For each dependency → best practices task
   - For each integration → patterns task

2. **Generate and dispatch research agents**:
   ```
   For each unknown in Technical Context:
     Task: "Research {unknown} for {feature context}"
   For each technology choice:
     Task: "Find best practices for {tech} in {domain}"
   ```

3. **Consolidate findings** in `research.md` using format:
   - Decision: [what was chosen]
   - Rationale: [why chosen]
   - Alternatives considered: [what else evaluated]

**Output**: research.md with all NEEDS CLARIFICATION resolved

## Phase 1: Design & Contracts
*Prerequisites: research.md complete*

1. **Extract entities from feature spec** → `data-model.md`:
   - Entity name, fields, relationships
   - Validation rules from requirements
   - State transitions if applicable

2. **Generate API contracts** from functional requirements:
   - For each user action → endpoint
   - Use standard REST/GraphQL patterns
   - Output OpenAPI/GraphQL schema to `/contracts/`

3. **Generate contract tests** from contracts:
   - One test file per endpoint
   - Assert request/response schemas
   - Tests must fail (no implementation yet)

4. **Extract test scenarios** from user stories:
   - Each story → integration test scenario
   - Quickstart test = story validation steps

5. **Update agent file incrementally** (O(1) operation):
   - Run `/scripts/update-agent-context.sh [claude|gemini|copilot]` for your AI assistant
   - If exists: Add only NEW tech from current plan
   - Preserve manual additions between markers
   - Update recent changes (keep last 3)
   - Keep under 150 lines for token efficiency
   - Output to repository root

**Output**: data-model.md, /contracts/*, failing tests, quickstart.md, agent-specific file

## Phase 2: Task Planning Approach
*This section describes what the /tasks command will do - DO NOT execute during /plan*

**Task Generation Strategy**:
- Load `/templates/tasks-template.md` as base
- Generate tasks from Phase 1 design docs (contracts, data model, quickstart)
- Contract tests for VRChat API endpoints (auth, groups, instances, members) [P]
- Contract tests for internal service CLIs [P]
- Entity model creation (GroupInstance, GroupMember, AuditRecord, AuthenticationSession, PolicyConfiguration) [P]
- Service library creation (VrcApi, Auth, Enforcement, GroupService, InstancesService, MembersService, AuditService)
- WPF UI implementation (MainWindow, ViewModels, Commands)
- Integration tests for complete user scenarios from quickstart.md
- Deployment configuration (single-file executable, Windows installer)

**Ordering Strategy**:
- TDD order: Contract tests → Integration tests → Unit tests → Implementation
- Dependency order: Models → Services → CLI → UI
- Mark [P] for parallel execution (independent entities/contracts)
- Group related tasks (Auth flow, Instance monitoring, Member management)

**Task Categories**:
1. **Contract Tests** (8-10 tasks): VRChat API contracts, internal service contracts
2. **Models & Entities** (5 tasks): Core domain models with validation
3. **Service Libraries** (7 tasks): Business logic services with CLI interfaces
4. **UI Components** (6-8 tasks): WPF views, view models, commands
5. **Integration & E2E** (4-6 tasks): Complete user scenario validation
6. **Deployment** (2-3 tasks): Packaging, distribution, installer

**Estimated Output**: 32-37 numbered, ordered tasks in tasks.md

**IMPORTANT**: This phase is executed by the /tasks command, NOT by /plan

## Phase 3+: Future Implementation
*These phases are beyond the scope of the /plan command*

**Phase 3**: Task execution (/tasks command creates tasks.md)  
**Phase 4**: Implementation (execute tasks.md following constitutional principles)  
**Phase 5**: Validation (run tests, execute quickstart.md, performance validation)

## Complexity Tracking
*Fill ONLY if Constitution Check has violations that must be justified*

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| [e.g., 4th project] | [current need] | [why 3 projects insufficient] |
| [e.g., Repository pattern] | [specific problem] | [why direct DB access insufficient] |


## Progress Tracking
*This checklist is updated during execution flow*

**Phase Status**:
- [x] Phase 0: Research complete (/plan command)
- [x] Phase 1: Design complete (/plan command)
- [x] Phase 2: Task planning complete (/plan command - describe approach only)
- [ ] Phase 3: Tasks generated (/tasks command)
- [ ] Phase 4: Implementation complete
- [ ] Phase 5: Validation passed

**Gate Status**:
- [x] Initial Constitution Check: PASS
- [x] Post-Design Constitution Check: PASS
- [x] All NEEDS CLARIFICATION resolved
- [x] Complexity deviations documented (none required)

---
*Based on Constitution v2.1.1 - See `/memory/constitution.md`*