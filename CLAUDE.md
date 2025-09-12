# VRC Group Guardian Development Guidelines

Auto-generated from feature plans. Last updated: 2025-09-11

## Active Technologies
- .NET 8 (C#) with WPF MVVM pattern (001-vrc-group-guardian)
- HttpClient + Polly for VRChat API integration
- Windows Credential Manager/DPAPI for secure storage
- xUnit + WireMock.Net for testing
- Serilog for structured logging

## Project Structure
```
src/
├── models/       # Domain models and entities
├── services/     # Business logic services (Auth, Group, Instance, Enforcement, Members, Audit)
├── cli/          # Command-line interfaces for each service
└── lib/          # Shared utilities and infrastructure

tests/
├── contract/     # API contract tests
├── integration/  # Service integration tests
└── unit/         # Unit tests
```

## Service Libraries
- **VrcApi**: VRChat API integration with rate limiting and caching
- **Auth**: Authentication service with Windows credential management
- **Enforcement**: Policy engine with timer-based countdown system
- **GroupService**: Group selection and permissions management
- **InstancesService**: Instance monitoring and closure operations
- **MembersService**: Member management (kick/ban operations)
- **AuditService**: Local audit logging and CSV export

## Commands
```bash
# Service CLIs (each library exposes testing interface)
dotnet run --project src/VrcApi -- --help
dotnet run --project src/Auth -- login --username user --password pass
dotnet run --project src/Enforcement -- policy --get
dotnet run --project src/Audit -- export --format csv

# Testing
dotnet test tests/contract/    # Contract tests first (TDD)
dotnet test tests/integration/ # Integration tests
dotnet test tests/unit/        # Unit tests
```

## Code Style
- Follow standard .NET conventions and naming
- MVVM pattern for UI with proper data binding
- Async/await for all API calls
- Structured logging with Serilog
- Guard clauses and fail-fast validation
- No implementation before tests (TDD enforced)

## VRChat API Integration Patterns
- Always use Polly for retry policies (exponential backoff on 429/5xx)
- Implement token bucket rate limiting (default 20 req/min)
- Cache stable data (roles/permissions) for 15 minutes
- Use jittered polling intervals (60s ±20%)
- Handle authentication token expiration gracefully
- Respect VRChat's undocumented rate limits with conservative approach

## Security Requirements
- Never store plaintext passwords or tokens
- Use Windows DPAPI for encryption at rest
- Implement secure credential wiping on logout
- Validate all user inputs and API responses
- Log security events for audit trail

## Testing Strategy
- Contract tests against VRChat API using WireMock
- RED-GREEN-Refactor cycle strictly enforced
- Integration tests with real Windows credential store
- Error scenario testing (401, 403, 429, 5xx responses)
- Performance testing (startup time, memory usage)

## Recent Changes
- 001-vrc-group-guardian: Added Windows desktop application for VRChat group moderation
- Implemented WPF MVVM pattern with policy enforcement engine
- Added VRChat API integration with rate limiting and secure credential storage

<!-- MANUAL ADDITIONS START -->
<!-- Add any custom development notes here -->
<!-- MANUAL ADDITIONS END -->