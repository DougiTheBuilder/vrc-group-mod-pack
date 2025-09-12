# VRC Group Guardian

A Windows desktop application for VRChat group moderation and automated instance management.

## 🚀 Quick Setup

### Prerequisites

```
.NET 8 SDK
Windows 10/11 (Required for credential storage)
VRChat Account with group moderation permissions
```

### Installation

1. **Clone the repository**
   ```bash
   git clone <repository-url>
   cd vrc-group-mod-pack
   ```

2. **Build the project**
   ```bash
   dotnet restore
   dotnet build
   ```

3. **Run the application**
   ```bash
   dotnet run --project src/VrcGroupGuardian
   ```

## 📋 Development Setup

### Project Structure
```
VrcGroupGuardian/
├── src/
│   ├── Infrastructure/          # Core services (logging, HTTP, storage)
│   ├── Models/                  # Domain entities 
│   ├── Services/               # Business logic services
│   ├── Views/                  # WPF UI views
│   ├── ViewModels/             # MVVM view models
│   └── VrcGroupGuardian.csproj
├── tests/
│   ├── contract/               # API contract tests
│   ├── integration/            # Service integration tests  
│   └── unit/                   # Unit tests
├── specs/                      # Feature specifications
└── VrcGroupGuardian.sln
```

### Running Tests

```bash
# Contract tests (using WireMock for VRChat API)
dotnet test tests/contract/

# Integration tests
dotnet test tests/integration/

# Unit tests
dotnet test tests/unit/

# All tests
dotnet test
```

### Build Configuration

The project is configured for:
- **Single-file deployment**: `PublishSingleFile=true`
- **Self-contained**: `SelfContained=true` 
- **Windows-specific**: Uses DPAPI and Credential Manager

```bash
# Create release build
dotnet publish -c Release --self-contained true -r win-x64
```

## 🔧 Core Features

- **🔐 Secure Authentication**: Windows Credential Manager integration
- **👥 Group Management**: Multi-group monitoring and permissions
- **🏠 Instance Monitoring**: Real-time instance detection and closure
- **⚖️ Policy Engine**: Automated enforcement with countdown timers
- **👨‍👩‍👧‍👦 Member Management**: Kick/ban operations with audit trail
- **📊 Audit Logging**: Comprehensive activity logging and CSV export
- **🎨 Modern UI**: WPF with MVVM pattern and accessibility support

## 🛠️ Development Workflow

This project follows **Test-Driven Development (TDD)**:

1. **Write failing tests first** (Phase 3.2)
2. **Implement models and services** (Phase 3.3-3.6) 
3. **Build WPF UI** (Phase 3.7)
4. **Integration and polish** (Phase 3.8-3.9)

### Current Status: Phase 3.1 Complete ✅

- [x] .NET 8 WPF project structure created
- [x] Dependencies configured (WPF, Serilog, Polly, xUnit)
- [x] Single-file publish configuration
- [x] EditorConfig and code style rules
- [x] Serilog configuration with file sink

**Next**: Phase 3.2 - Contract and Integration Tests

## 📚 Documentation

- [`specs/001-vrc-group-guardian/`](specs/001-vrc-group-guardian/) - Complete feature specification
- [`CLAUDE.md`](CLAUDE.md) - Development guidelines and patterns
- [Tasks](specs/001-vrc-group-guardian/tasks.md) - Detailed task breakdown (70 tasks)

## 🔒 Security

- Passwords stored via Windows DPAPI
- VRChat tokens encrypted at rest
- Secure credential wiping on logout
- Audit trail for all moderation actions

## ⚠️ Important Notes

- **Windows Only**: Requires Windows for credential storage features
- **VRChat API**: Implements conservative rate limiting (20 req/min)
- **TDD Required**: Tests must be written before implementation
- **Single Executable**: Builds to self-contained .exe file
