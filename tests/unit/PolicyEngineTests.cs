using Xunit;
using Microsoft.Extensions.Logging;
using Moq;
using VrcGroupGuardian.Models;
using VrcGroupGuardian.Services.Enforcement;
using VrcGroupGuardian.Services.Instances;
using VrcGroupGuardian.Services.Audit;
using VrcGroupGuardian.Infrastructure;

namespace VrcGroupGuardian.Tests.Unit;

public class PolicyEngineTests
{
    private readonly Mock<IInstancesService> _mockInstancesService;
    private readonly Mock<IAuditService> _mockAuditService;
    private readonly Mock<INotificationService> _mockNotificationService;
    private readonly Mock<ILogger<EnforcementService>> _mockLogger;
    private readonly EnforcementService _policyEngine;

    public PolicyEngineTests()
    {
        _mockInstancesService = new Mock<IInstancesService>();
        _mockAuditService = new Mock<IAuditService>();
        _mockNotificationService = new Mock<INotificationService>();
        _mockLogger = new Mock<ILogger<EnforcementService>>();
        
        _policyEngine = new EnforcementService(
            _mockInstancesService.Object,
            _mockAuditService.Object,
            _mockNotificationService.Object,
            _mockLogger.Object);
    }

    [Fact]
    public void ApplyPolicyConfiguration_ValidConfiguration_UpdatesSettings()
    {
        // Arrange
        var policyConfig = new PolicyConfiguration
        {
            MonitoringEnabled = true,
            AutoCloseEnabled = true,
            GracePeriodMinutes = 15,
            MaxConcurrentInstances = 3,
            AllowedInstanceTypes = [InstanceType.GroupPlus, InstanceType.Group],
            RestrictedInstanceTypes = [InstanceType.GroupPublic],
            RequiredRoles = ["Admin", "Moderator"],
            ExcludedUserIds = ["usr_123", "usr_456"],
            NotifyOnDetection = true,
            NotifyOnClosure = true,
            ExportAuditLogs = true
        };

        // Act
        _policyEngine.ApplyPolicyConfiguration(policyConfig);
        var appliedConfig = _policyEngine.GetCurrentConfiguration();

        // Assert
        Assert.True(appliedConfig.MonitoringEnabled);
        Assert.True(appliedConfig.AutoCloseEnabled);
        Assert.Equal(15, appliedConfig.GracePeriodMinutes);
        Assert.Equal(3, appliedConfig.MaxConcurrentInstances);
        Assert.Contains(InstanceType.GroupPlus, appliedConfig.AllowedInstanceTypes);
        Assert.Contains(InstanceType.Group, appliedConfig.AllowedInstanceTypes);
        Assert.Contains(InstanceType.GroupPublic, appliedConfig.RestrictedInstanceTypes);
        Assert.Contains("Admin", appliedConfig.RequiredRoles);
        Assert.Contains("usr_123", appliedConfig.ExcludedUserIds);
    }

    [Fact]
    public void EvaluateInstance_AllowedInstanceType_ReturnsCompliant()
    {
        // Arrange
        var policyConfig = new PolicyConfiguration
        {
            AllowedInstanceTypes = [InstanceType.Group, InstanceType.GroupPlus],
            RestrictedInstanceTypes = [InstanceType.GroupPublic]
        };
        _policyEngine.ApplyPolicyConfiguration(policyConfig);

        var instance = new GroupInstance
        {
            Id = "wrld_123",
            Type = InstanceType.Group,
            UserCount = 5,
            MaxUsers = 10,
            Region = "us-west"
        };

        // Act
        var result = _policyEngine.EvaluateInstance(instance);

        // Assert
        Assert.Equal(PolicyViolationType.None, result.ViolationType);
        Assert.True(result.IsCompliant);
        Assert.Empty(result.ViolationReasons);
    }

    [Fact]
    public void EvaluateInstance_RestrictedInstanceType_ReturnsViolation()
    {
        // Arrange
        var policyConfig = new PolicyConfiguration
        {
            AllowedInstanceTypes = [InstanceType.Group],
            RestrictedInstanceTypes = [InstanceType.GroupPublic]
        };
        _policyEngine.ApplyPolicyConfiguration(policyConfig);

        var instance = new GroupInstance
        {
            Id = "wrld_123",
            Type = InstanceType.GroupPublic,
            UserCount = 5,
            MaxUsers = 10,
            Region = "us-west"
        };

        // Act
        var result = _policyEngine.EvaluateInstance(instance);

        // Assert
        Assert.Equal(PolicyViolationType.RestrictedInstanceType, result.ViolationType);
        Assert.False(result.IsCompliant);
        Assert.Contains("Instance type 'GroupPublic' is restricted", result.ViolationReasons);
    }

    [Fact]
    public void EvaluateInstance_ExceedsMaxConcurrentInstances_ReturnsViolation()
    {
        // Arrange
        var policyConfig = new PolicyConfiguration
        {
            MaxConcurrentInstances = 2,
            AllowedInstanceTypes = [InstanceType.Group]
        };
        _policyEngine.ApplyPolicyConfiguration(policyConfig);

        // Mock current instances to exceed limit
        var currentInstances = new List<GroupInstance>
        {
            new() { Id = "wrld_001", Type = InstanceType.Group },
            new() { Id = "wrld_002", Type = InstanceType.Group }
        };
        _mockInstancesService.Setup(x => x.GetCurrentInstancesAsync("test-group"))
            .ReturnsAsync(currentInstances);

        var newInstance = new GroupInstance
        {
            Id = "wrld_003",
            Type = InstanceType.Group,
            UserCount = 3,
            MaxUsers = 10,
            Region = "us-east"
        };

        // Act
        var result = _policyEngine.EvaluateInstance(newInstance);

        // Assert
        Assert.Equal(PolicyViolationType.ExcessiveInstances, result.ViolationType);
        Assert.False(result.IsCompliant);
        Assert.Contains("Maximum concurrent instances (2) would be exceeded", result.ViolationReasons);
    }

    [Theory]
    [InlineData(InstanceType.Group, true)]
    [InlineData(InstanceType.GroupPlus, true)]
    [InlineData(InstanceType.GroupPublic, false)]
    [InlineData(InstanceType.InvitePlus, false)]
    public void IsInstanceTypeAllowed_VariousTypes_ReturnsExpectedResult(InstanceType type, bool expected)
    {
        // Arrange
        var policyConfig = new PolicyConfiguration
        {
            AllowedInstanceTypes = [InstanceType.Group, InstanceType.GroupPlus],
            RestrictedInstanceTypes = [InstanceType.GroupPublic, InstanceType.InvitePlus]
        };
        _policyEngine.ApplyPolicyConfiguration(policyConfig);

        var instance = new GroupInstance { Type = type };

        // Act
        var result = _policyEngine.EvaluateInstance(instance);

        // Assert
        Assert.Equal(expected, result.IsCompliant);
    }

    [Fact]
    public void ScheduleInstanceClosure_ValidInstance_CreatesClosureSchedule()
    {
        // Arrange
        var instance = new GroupInstance
        {
            Id = "wrld_123",
            Type = InstanceType.GroupPublic,
            UserCount = 5,
            MaxUsers = 10
        };

        var policyConfig = new PolicyConfiguration
        {
            GracePeriodMinutes = 10,
            AutoCloseEnabled = true
        };
        _policyEngine.ApplyPolicyConfiguration(policyConfig);

        // Act
        _policyEngine.ScheduleInstanceClosure(instance, "Policy violation");

        // Assert
        var scheduledClosures = _policyEngine.GetScheduledClosures();
        Assert.Single(scheduledClosures);
        Assert.Equal("wrld_123", scheduledClosures.First().InstanceId);
        Assert.Equal("Policy violation", scheduledClosures.First().Reason);
    }

    [Fact]
    public void ScheduleInstanceClosure_DuplicateInstance_UpdatesExistingSchedule()
    {
        // Arrange
        var instance = new GroupInstance { Id = "wrld_123" };
        var policyConfig = new PolicyConfiguration
        {
            GracePeriodMinutes = 10,
            AutoCloseEnabled = true
        };
        _policyEngine.ApplyPolicyConfiguration(policyConfig);

        // Act
        _policyEngine.ScheduleInstanceClosure(instance, "First reason");
        _policyEngine.ScheduleInstanceClosure(instance, "Updated reason");

        // Assert
        var scheduledClosures = _policyEngine.GetScheduledClosures();
        Assert.Single(scheduledClosures);
        Assert.Equal("Updated reason", scheduledClosures.First().Reason);
    }

    [Fact]
    public void CancelInstanceClosure_ExistingSchedule_RemovesFromSchedule()
    {
        // Arrange
        var instance = new GroupInstance { Id = "wrld_123" };
        var policyConfig = new PolicyConfiguration { GracePeriodMinutes = 10 };
        _policyEngine.ApplyPolicyConfiguration(policyConfig);
        
        _policyEngine.ScheduleInstanceClosure(instance, "Test reason");

        // Act
        var result = _policyEngine.CancelInstanceClosure("wrld_123");

        // Assert
        Assert.True(result);
        var scheduledClosures = _policyEngine.GetScheduledClosures();
        Assert.Empty(scheduledClosures);
    }

    [Fact]
    public void CancelInstanceClosure_NonExistingSchedule_ReturnsFalse()
    {
        // Act
        var result = _policyEngine.CancelInstanceClosure("wrld_nonexistent");

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task ProcessScheduledClosures_ExpiredClosure_ClosesInstance()
    {
        // Arrange
        var instance = new GroupInstance { Id = "wrld_123" };
        var policyConfig = new PolicyConfiguration
        {
            GracePeriodMinutes = 0, // Immediate closure for testing
            AutoCloseEnabled = true
        };
        _policyEngine.ApplyPolicyConfiguration(policyConfig);
        
        _policyEngine.ScheduleInstanceClosure(instance, "Test closure");
        
        _mockInstancesService.Setup(x => x.CloseInstanceAsync("wrld_123"))
            .ReturnsAsync(true);

        // Act
        await _policyEngine.ProcessScheduledClosures();

        // Assert
        _mockInstancesService.Verify(x => x.CloseInstanceAsync("wrld_123"), Times.Once);
        var scheduledClosures = _policyEngine.GetScheduledClosures();
        Assert.Empty(scheduledClosures);
    }

    [Fact]
    public async Task ProcessScheduledClosures_NotExpiredClosure_LeavesInSchedule()
    {
        // Arrange
        var instance = new GroupInstance { Id = "wrld_123" };
        var policyConfig = new PolicyConfiguration
        {
            GracePeriodMinutes = 60, // Long grace period
            AutoCloseEnabled = true
        };
        _policyEngine.ApplyPolicyConfiguration(policyConfig);
        
        _policyEngine.ScheduleInstanceClosure(instance, "Test closure");

        // Act
        await _policyEngine.ProcessScheduledClosures();

        // Assert
        _mockInstancesService.Verify(x => x.CloseInstanceAsync(It.IsAny<string>()), Times.Never);
        var scheduledClosures = _policyEngine.GetScheduledClosures();
        Assert.Single(scheduledClosures);
    }

    [Fact]
    public void GetPolicyStatistics_WithViolations_ReturnsAccurateStats()
    {
        // Arrange
        var policyConfig = new PolicyConfiguration
        {
            RestrictedInstanceTypes = [InstanceType.GroupPublic]
        };
        _policyEngine.ApplyPolicyConfiguration(policyConfig);

        var compliantInstance = new GroupInstance { Type = InstanceType.Group };
        var violatingInstance = new GroupInstance { Type = InstanceType.GroupPublic };

        // Act
        _policyEngine.EvaluateInstance(compliantInstance);
        _policyEngine.EvaluateInstance(violatingInstance);
        _policyEngine.EvaluateInstance(violatingInstance); // Second violation

        var stats = _policyEngine.GetPolicyStatistics();

        // Assert
        Assert.Equal(3, stats.TotalEvaluations);
        Assert.Equal(1, stats.CompliantInstances);
        Assert.Equal(2, stats.ViolatingInstances);
        Assert.True(stats.ViolationRate > 0);
    }

    [Fact]
    public void ResetPolicyStatistics_ClearsAllCounters()
    {
        // Arrange
        var instance = new GroupInstance { Type = InstanceType.Group };
        _policyEngine.EvaluateInstance(instance);

        // Act
        _policyEngine.ResetPolicyStatistics();
        var stats = _policyEngine.GetPolicyStatistics();

        // Assert
        Assert.Equal(0, stats.TotalEvaluations);
        Assert.Equal(0, stats.CompliantInstances);
        Assert.Equal(0, stats.ViolatingInstances);
    }
}