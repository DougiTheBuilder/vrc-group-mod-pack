# Contributing to VRC Group Guardian

Thank you for your interest in contributing to VRC Group Guardian! This document provides guidelines and information for contributors.

## 🤝 Ways to Contribute

### 🐛 Bug Reports
- Use the [Bug Report Template](https://github.com/your-username/vrc-group-mod-pack/issues/new?template=bug_report.md)
- Include steps to reproduce, expected vs actual behavior
- Provide system information (Windows version, .NET version, etc.)
- Attach screenshots or logs when helpful

### 💡 Feature Requests  
- Use the [Feature Request Template](https://github.com/your-username/vrc-group-mod-pack/issues/new?template=feature_request.md)
- Describe the problem you're trying to solve
- Explain your proposed solution
- Consider alternative solutions

### 📝 Documentation
- Fix typos or improve clarity
- Add missing documentation for features
- Create tutorials or guides
- Translate documentation (future)

### 💻 Code Contributions
- Bug fixes
- Feature implementations
- Performance improvements
- Test coverage improvements

## 🚀 Getting Started

### Prerequisites
```bash
# Required software
.NET 8.0 SDK or later
Git
Windows 10/11 (for full development)

# Recommended tools
Visual Studio 2022 or VS Code
Windows Terminal
Git extensions or GitHub Desktop
```

### Development Setup
1. **Fork and Clone**
   ```bash
   git clone https://github.com/your-username/vrc-group-mod-pack.git
   cd vrc-group-mod-pack
   ```

2. **Install Dependencies**
   ```bash
   dotnet restore
   ```

3. **Run Tests**
   ```bash
   dotnet test
   ```

4. **Build Project**
   ```bash
   dotnet build --configuration Debug
   ```

5. **Run Application**
   ```bash
   dotnet run --project src/VrcGroupGuardian
   ```

## 📋 Development Guidelines

### Code Style
Follow the established patterns in the codebase:

```csharp
// ✅ Good: Clear, descriptive naming
public class PolicyEnforcementService : IPolicyEnforcementService
{
    private readonly ILogger<PolicyEnforcementService> _logger;
    
    public async Task<PolicyResult> EvaluateInstanceAsync(GroupInstance instance)
    {
        if (instance == null)
            throw new ArgumentNullException(nameof(instance));
            
        var result = await ValidateComplianceAsync(instance);
        return result;
    }
}

// ❌ Avoid: Unclear naming, missing validation
public class PES
{
    public async Task<PolicyResult> Check(GroupInstance i)
    {
        return await Validate(i);
    }
}
```

### Architecture Patterns
- **MVVM**: Use proper Model-View-ViewModel separation for UI
- **Dependency Injection**: Register services in DI container
- **Async/Await**: Use async methods for I/O operations
- **Error Handling**: Wrap operations in try-catch with proper logging

```csharp
// ✅ Good: Proper MVVM pattern
public class SettingsViewModel : INotifyPropertyChanged
{
    private readonly ISettingsService _settingsService;
    private bool _monitoringEnabled;
    
    public bool MonitoringEnabled
    {
        get => _monitoringEnabled;
        set => SetProperty(ref _monitoringEnabled, value);
    }
    
    public ICommand SaveSettingsCommand { get; }
    
    private async Task SaveSettingsAsync()
    {
        try
        {
            await _settingsService.SaveAsync(GetCurrentSettings());
            StatusMessage = "Settings saved successfully";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save settings");
            StatusMessage = "Failed to save settings";
        }
    }
}
```

### Testing Requirements
All contributions must include appropriate tests:

```csharp
// Unit test example
[Fact]
public async Task EvaluateInstance_WithNonCompliantInstance_ReturnsViolation()
{
    // Arrange
    var mockService = new Mock<IPolicyService>();
    var instance = new GroupInstance 
    { 
        Type = InstanceType.GroupPublic,
        IsAgeGated = false 
    };
    
    var evaluator = new PolicyEvaluator(mockService.Object);
    
    // Act
    var result = await evaluator.EvaluateInstanceAsync(instance);
    
    // Assert
    Assert.False(result.IsCompliant);
    Assert.Equal(ViolationType.AgeGating, result.ViolationType);
}

// Integration test example
[Fact]
public async Task FullWorkflow_MonitorAndEnforce_CompletesSuccessfully()
{
    // Arrange - Use TestHost with real services
    var host = CreateTestHost();
    var monitor = host.Services.GetRequiredService<IInstanceMonitor>();
    
    // Act
    await monitor.StartMonitoringAsync("test-group");
    await Task.Delay(1000); // Allow monitoring cycle
    await monitor.StopMonitoringAsync();
    
    // Assert
    var auditService = host.Services.GetRequiredService<IAuditService>();
    var records = await auditService.GetAuditRecordsAsync(DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow);
    Assert.NotEmpty(records);
}
```

### Security Guidelines
Security is paramount for this application:

```csharp
// ✅ Good: Secure credential handling
public async Task StoreCredentialsAsync(AuthSession session)
{
    var encryptedData = ProtectedData.Protect(
        Encoding.UTF8.GetBytes(session.Token),
        null,
        DataProtectionScope.CurrentUser);
        
    await _credentialManager.StoreAsync("VrcGroupGuardian", encryptedData);
    
    // Clear sensitive data from memory
    Array.Clear(Encoding.UTF8.GetBytes(session.Token), 0, session.Token.Length);
}

// ❌ Avoid: Plaintext storage, logging sensitive data
public async Task StoreCredentialsAsync(AuthSession session)
{
    _logger.LogInformation($"Storing token: {session.Token}"); // ❌ NEVER LOG TOKENS
    await File.WriteAllTextAsync("token.txt", session.Token);   // ❌ NEVER PLAINTEXT
}
```

## 🔄 Contribution Workflow

### 1. Create Feature Branch
```bash
git checkout -b feature/your-feature-name

# Branch naming conventions:
# feature/add-bulk-member-actions
# bugfix/fix-authentication-timeout
# docs/improve-setup-guide
# test/add-integration-tests
```

### 2. Make Changes
- Write tests first (TDD approach preferred)
- Implement feature/fix
- Ensure all tests pass
- Update documentation as needed

### 3. Commit Changes
```bash
# Use conventional commit format
git commit -m "feat: add bulk member selection functionality

- Add checkbox selection in members view
- Implement bulk kick/ban operations  
- Add confirmation dialogs for bulk actions
- Update audit logging for bulk operations

Fixes #123"

# Commit types:
# feat: new features
# fix: bug fixes
# docs: documentation changes
# test: test additions/changes
# refactor: code restructuring
# style: formatting changes
# chore: maintenance tasks
```

### 4. Push and Create PR
```bash
git push origin feature/your-feature-name
```

Create Pull Request with:
- Clear title and description
- Link to related issues
- Screenshots for UI changes
- Test evidence
- Breaking change notes (if any)

### 5. Code Review Process
- Automated checks must pass (build, tests, security scan)
- Peer review by maintainer
- Address feedback
- Maintain clean commit history

## 🧪 Testing Strategy

### Test Categories
```bash
# Unit Tests - Fast, isolated tests
dotnet test tests/unit/

# Integration Tests - Service interaction tests
dotnet test tests/integration/

# Contract Tests - API contract validation
dotnet test tests/contract/

# Performance Tests - Benchmarks and load tests
dotnet test tests/performance/

# Manual Tests - UI and end-to-end scenarios
# See: tests/manual/QuickstartValidation.md
```

### Test Coverage Requirements
- **Minimum Coverage**: 80% for new code
- **Critical Paths**: 95% coverage required
- **Security Code**: 100% coverage required
- **UI Code**: Focus on ViewModels, not Views

### Writing Good Tests
```csharp
// ✅ Good: Clear, focused test
[Fact]
public async Task AuthService_LoginWithValidCredentials_ReturnsSuccessResult()
{
    // Arrange
    var mockHttpClient = CreateMockHttpClient();
    mockHttpClient.SetupSuccessfulLoginResponse();
    
    var authService = new AuthService(mockHttpClient.Object);
    
    // Act
    var result = await authService.LoginAsync("testuser", "testpass");
    
    // Assert
    Assert.True(result.Success);
    Assert.NotNull(result.AuthToken);
    Assert.False(result.RequiresTwoFactor);
}

// ❌ Avoid: Testing multiple concerns, unclear intent
[Fact]
public async Task TestAuth()
{
    var auth = new AuthService();
    var result = await auth.LoginAsync("user", "pass");
    var groups = await auth.GetGroupsAsync(); // Different concern
    Assert.True(result.Success && groups.Count > 0); // Multiple assertions
}
```

## 📚 Documentation Standards

### Code Documentation
```csharp
/// <summary>
/// Evaluates a group instance against configured policies to determine compliance.
/// </summary>
/// <param name="instance">The group instance to evaluate</param>
/// <returns>A policy result indicating compliance status and any violations</returns>
/// <exception cref="ArgumentNullException">Thrown when instance is null</exception>
/// <exception cref="InvalidOperationException">Thrown when no policies are configured</exception>
public async Task<PolicyResult> EvaluateInstanceAsync(GroupInstance instance)
{
    // Implementation
}
```

### README Updates
When adding features, update relevant documentation:
- Feature list in README.md
- Configuration examples
- Usage instructions
- FAQ entries (if commonly asked)

### Changelog Entries
Add entries to CHANGELOG.md:
```markdown
## [Unreleased]

### Added
- Bulk member selection and actions in Members view
- Keyboard shortcuts for common operations (Ctrl+A, Delete, etc.)

### Changed  
- Improved error messages for authentication failures
- Updated member search to include role filtering

### Fixed
- Fixed issue where grace period timer continued after manual closure
- Resolved memory leak in instance monitoring service

### Security
- Enhanced credential encryption with additional entropy
```

## 🚀 Release Process

### Version Numbering
We follow Semantic Versioning (SemVer):
```
MAJOR.MINOR.PATCH
1.0.0 - Initial release
1.0.1 - Patch release (bug fixes)
1.1.0 - Minor release (new features, backward compatible)
2.0.0 - Major release (breaking changes)
```

### Release Checklist
Before creating releases:
- [ ] All tests passing
- [ ] Security scan passed
- [ ] Documentation updated
- [ ] Changelog updated
- [ ] Version numbers updated
- [ ] Performance benchmarks verified
- [ ] Manual testing completed

## 🛠️ Development Tools

### Recommended Extensions (VS Code)
```json
{
  "recommendations": [
    "ms-dotnettools.csharp",
    "ms-dotnettools.vscode-dotnet-runtime",
    "formulahendry.dotnet-test-explorer",
    "jchannon.csharpextensions",
    "kreativ-software.csharpextensions",
    "adrianwilczynski.namespace",
    "fernandoescolar.vscode-solution-explorer"
  ]
}
```

### Project Templates
Use provided templates for consistency:
```bash
# Create new service
dotnet new classlib -n MyNewService -o src/Services/MyNewService

# Create new test project  
dotnet new xunit -n MyNewService.Tests -o tests/unit/MyNewService.Tests
```

### Build Scripts
Use provided build scripts:
```bash
# Development build
.\build\build.ps1 -Configuration Debug

# Release build with tests
.\build\build.ps1 -Configuration Release -Runtime win-x64

# Performance testing
.\build\build.ps1 -Configuration Release -SkipTests:$false
dotnet test tests/performance/ --configuration Release
```

## 🏆 Recognition

### Contributor Credits
Contributors are recognized in:
- GitHub contributor graphs
- Release notes acknowledgments  
- Annual contributor highlights
- Special recognition for significant contributions

### Maintainer Path
Active contributors may be invited to become maintainers:
- Consistent, high-quality contributions
- Good understanding of project goals
- Positive community interaction
- Technical expertise in relevant areas

## 📞 Getting Help

### Community Channels
- **GitHub Discussions**: General questions and ideas
- **Discord Server**: Real-time chat with community
- **GitHub Issues**: Technical questions and bug reports

### Maintainer Contact
- **General**: Open GitHub Discussion
- **Security**: security@your-domain.com
- **Private**: Direct message maintainers on Discord

### Office Hours
Maintainers host virtual office hours:
- **When**: Bi-weekly Saturdays, 2-4 PM UTC
- **Where**: Discord voice channel
- **Topics**: Architecture discussions, complex contributions, Q&A

## 📋 Code of Conduct

### Our Pledge
We pledge to make participation in our project a harassment-free experience for everyone, regardless of age, body size, disability, ethnicity, gender identity and expression, level of experience, nationality, personal appearance, race, religion, or sexual identity and orientation.

### Standards
Examples of behavior that contributes to creating a positive environment:
- Using welcoming and inclusive language
- Being respectful of differing viewpoints and experiences
- Gracefully accepting constructive criticism
- Focusing on what is best for the community
- Showing empathy towards other community members

### Enforcement
Instances of abusive, harassing, or otherwise unacceptable behavior may be reported to the project maintainers. All complaints will be reviewed and investigated and will result in a response that is deemed necessary and appropriate to the circumstances.

---

**Thank you for contributing to VRC Group Guardian!** Your efforts help make VRChat group moderation better for everyone.

*Last updated: 2025-09-11*