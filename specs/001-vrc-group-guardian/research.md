# Research: VRC Group Guardian Technical Decisions

## VRChat API Integration

**Decision**: Use HttpClient with Polly for resilient VRChat API integration
**Rationale**: 
- VRChat API has strict rate limiting (documented 20 req/min baseline)
- Requires exponential backoff on 429/5xx responses with jittered polling
- HttpClient provides cookie container for authToken persistence
- Polly library offers battle-tested retry policies and circuit breakers

**Alternatives considered**:
- RestSharp: More overhead, less control over low-level HTTP behavior
- Native System.Net: Would require manual retry/backoff implementation
- GraphQL client: VRChat API is REST-only

## Authentication & Security

**Decision**: Windows Credential Manager + DPAPI for secure token storage
**Rationale**:
- VRChat requires session cookies (authToken) after username/password + 2FA
- DPAPI provides OS-level encryption tied to user account
- Credential Manager offers standard Windows secure storage
- No plaintext credentials stored locally

**Alternatives considered**:
- File-based encrypted storage: Custom solution, more attack surface
- Registry storage: Less secure, not designed for secrets
- Memory-only: Would require re-authentication on every restart

## UI Framework Selection

**Decision**: WPF with MVVM pattern
**Rationale**:
- Native Windows look and feel required for moderator adoption
- Rich data binding for real-time instance monitoring
- Command pattern ideal for confirmation dialogs on destructive actions
- Mature ecosystem with extensive controls for tables/grids

**Alternatives considered**:
- WinUI 3: Newer but less stable, deployment complexity
- Electron: Non-native, larger footprint, security concerns for credential access
- Console application: Poor UX for tabular data and concurrent monitoring

## Polling Strategy

**Decision**: Jittered polling with token bucket rate limiting
**Rationale**:
- Prevents thundering herd problems with multiple app instances
- Token bucket allows burst capacity for manual refresh actions
- Jittered timing (60s ±20%) distributes API load
- Respects VRChat's published rate limits

**Alternatives considered**:
- WebSocket subscription: VRChat pipeline is receive-only, no instance creation events
- Push notifications: Not available in VRChat API
- Fixed interval polling: Could overwhelm API during peak usage

## Enforcement Engine Design

**Decision**: Timer-based countdown system with cancellation support
**Rationale**:
- Grace period (60-300s) allows manual override before auto-closure
- Visual countdown provides clear feedback to users
- Per-instance cancellation prevents accidental closures
- Audit trail for all enforcement decisions

**Alternatives considered**:
- Immediate closure: Too aggressive, no human oversight
- Queue-based processing: Over-engineered for single-user desktop app
- Event-driven: Adds complexity without clear benefit

## Data Storage Architecture

**Decision**: Local file storage with structured logging
**Rationale**:
- Offline-first design for moderator workflow continuity
- Structured logs enable audit trail export (CSV)
- No server infrastructure required
- Cache invalidation based on timestamps (15min for roles/permissions)

**Alternatives considered**:
- SQLite database: Over-engineered for simple audit logs and cache
- Cloud storage: Introduces privacy/compliance concerns for group data
- Registry storage: Not suitable for variable-length audit data

## Testing Strategy

**Decision**: Contract tests with WireMock.Net for VRChat API simulation
**Rationale**:
- VRChat API rate limits prevent integration testing against production
- WireMock allows testing error scenarios (401, 403, 429, 5xx)
- Contract tests ensure API compatibility as VRChat evolves
- Real Windows credential store can be tested in integration scenarios

**Alternatives considered**:
- Live API testing: Would violate rate limits, require test group setup
- Simple mocking: Insufficient for complex retry/backoff scenarios
- Manual testing only: Too slow for TDD cycle, error-prone

## Deployment & Distribution

**Decision**: Single-file self-contained .exe with PublishSingleFile
**Rationale**:
- Zero-friction deployment for non-technical moderators  
- No .NET runtime installation required
- Single executable reduces support complexity
- x64 target covers >95% of Windows desktop installations

**Alternatives considered**:
- Framework-dependent: Requires .NET 8 installation, support burden
- Installer package: Adds complexity, potential corporate firewall issues
- ClickOnce: Auto-update complexity not needed for v1.0

## Performance Optimizations

**Decision**: Differential caching with manual refresh option
**Rationale**:
- Only fetch changed instances to minimize API calls
- Cache stable data (roles/permissions) for 15-minute periods
- Manual refresh for immediate updates during active moderation
- Background polling continues for automatic enforcement

**Alternatives considered**:
- Full refresh every cycle: Inefficient API usage, poor user experience
- Server-sent events: Not supported by VRChat API
- Local database: Over-engineered for caching requirements