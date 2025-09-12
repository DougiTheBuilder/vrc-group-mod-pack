using Xunit;
using Microsoft.Extensions.Logging;
using Moq;
using VrcGroupGuardian.Models;
using VrcGroupGuardian.Services.Enforcement;
using VrcGroupGuardian.Services.Instances;
using VrcGroupGuardian.Services.Audit;
using VrcGroupGuardian.Infrastructure;

namespace VrcGroupGuardian.Tests.Unit;

public class TimerTests
{
    private readonly Mock<IInstancesService> _mockInstancesService;
    private readonly Mock<IAuditService> _mockAuditService;
    private readonly Mock<INotificationService> _mockNotificationService;
    private readonly Mock<ILogger<EnforcementService>> _mockLogger;
    private readonly EnforcementService _enforcementService;

    public TimerTests()
    {
        _mockInstancesService = new Mock<IInstancesService>();
        _mockAuditService = new Mock<IAuditService>();
        _mockNotificationService = new Mock<INotificationService>();
        _mockLogger = new Mock<ILogger<EnforcementService>>();
        
        _enforcementService = new EnforcementService(
            _mockInstancesService.Object,
            _mockAuditService.Object,
            _mockNotificationService.Object,
            _mockLogger.Object);
    }

    [Fact]
    public void StartMonitoring_EnablesPeriodicInstanceChecking()
    {
        // Arrange
        var config = new PolicyConfiguration
        {
            MonitoringEnabled = true,
            PollingIntervalMinutes = 1
        };
        _enforcementService.ApplyPolicyConfiguration(config);

        // Act
        _enforcementService.StartMonitoring();

        // Assert
        Assert.True(_enforcementService.IsMonitoringActive);
    }

    [Fact]
    public void StopMonitoring_DisablesPeriodicChecking()
    {
        // Arrange
        var config = new PolicyConfiguration { MonitoringEnabled = true };
        _enforcementService.ApplyPolicyConfiguration(config);
        _enforcementService.StartMonitoring();

        // Act
        _enforcementService.StopMonitoring();

        // Assert
        Assert.False(_enforcementService.IsMonitoringActive);
    }

    [Fact]
    public void ScheduleInstanceClosure_CreatesTimerWithCorrectDelay()
    {
        // Arrange
        var instance = new GroupInstance
        {
            Id = "wrld_test123",
            Type = InstanceType.GroupPublic,
            UserCount = 5
        };

        var config = new PolicyConfiguration
        {
            GracePeriodMinutes = 15,
            AutoCloseEnabled = true
        };
        _enforcementService.ApplyPolicyConfiguration(config);

        // Act
        _enforcementService.ScheduleInstanceClosure(instance, "Test violation");

        // Assert
        var scheduledClosures = _enforcementService.GetScheduledClosures();
        Assert.Single(scheduledClosures);
        
        var closure = scheduledClosures.First();
        Assert.Equal("wrld_test123", closure.InstanceId);
        Assert.True(closure.ScheduledTime > DateTime.UtcNow.AddMinutes(14));
        Assert.True(closure.ScheduledTime < DateTime.UtcNow.AddMinutes(16));
    }

    [Fact]
    public void ScheduleInstanceClosure_ZeroGracePeriod_SchedulesImmediateExecution()
    {
        // Arrange
        var instance = new GroupInstance { Id = "wrld_immediate" };
        var config = new PolicyConfiguration
        {
            GracePeriodMinutes = 0,
            AutoCloseEnabled = true
        };
        _enforcementService.ApplyPolicyConfiguration(config);

        // Act
        _enforcementService.ScheduleInstanceClosure(instance, "Immediate closure");

        // Assert
        var scheduledClosures = _enforcementService.GetScheduledClosures();
        var closure = scheduledClosures.First();
        
        // Should be scheduled within the next few seconds
        Assert.True(closure.ScheduledTime <= DateTime.UtcNow.AddSeconds(5));
    }

    [Fact]
    public void ScheduleInstanceClosure_UpdateExistingSchedule_UpdatesTimerCorrectly()
    {
        // Arrange
        var instance = new GroupInstance { Id = "wrld_update" };
        var config = new PolicyConfiguration
        {
            GracePeriodMinutes = 10,
            AutoCloseEnabled = true
        };
        _enforcementService.ApplyPolicyConfiguration(config);

        // Act
        _enforcementService.ScheduleInstanceClosure(instance, "First reason");
        var firstScheduleTime = _enforcementService.GetScheduledClosures().First().ScheduledTime;
        
        Thread.Sleep(100); // Small delay to ensure different timestamps
        
        _enforcementService.ScheduleInstanceClosure(instance, "Updated reason");
        var updatedScheduleTime = _enforcementService.GetScheduledClosures().First().ScheduledTime;

        // Assert
        Assert.Single(_enforcementService.GetScheduledClosures());
        Assert.True(updatedScheduleTime > firstScheduleTime);
        Assert.Equal("Updated reason", _enforcementService.GetScheduledClosures().First().Reason);
    }

    [Fact]
    public void CancelInstanceClosure_RemovesTimerAndSchedule()
    {
        // Arrange
        var instance = new GroupInstance { Id = "wrld_cancel" };
        var config = new PolicyConfiguration { GracePeriodMinutes = 30 };
        _enforcementService.ApplyPolicyConfiguration(config);
        
        _enforcementService.ScheduleInstanceClosure(instance, "To be cancelled");
        Assert.Single(_enforcementService.GetScheduledClosures());

        // Act
        var result = _enforcementService.CancelInstanceClosure("wrld_cancel");

        // Assert
        Assert.True(result);
        Assert.Empty(_enforcementService.GetScheduledClosures());
    }

    [Fact]
    public async Task ProcessScheduledClosures_ExpiredTimer_ExecutesClosure()
    {
        // Arrange
        var instance = new GroupInstance { Id = "wrld_expired" };
        var config = new PolicyConfiguration
        {
            GracePeriodMinutes = 0,
            AutoCloseEnabled = true
        };
        _enforcementService.ApplyPolicyConfiguration(config);
        
        _mockInstancesService.Setup(x => x.CloseInstanceAsync("wrld_expired"))
            .ReturnsAsync(true);

        _enforcementService.ScheduleInstanceClosure(instance, "Timer test");

        // Act
        await Task.Delay(100); // Allow timer to potentially fire
        await _enforcementService.ProcessScheduledClosures();

        // Assert
        _mockInstancesService.Verify(x => x.CloseInstanceAsync("wrld_expired"), Times.Once);
        Assert.Empty(_enforcementService.GetScheduledClosures());
    }

    [Fact]
    public async Task ProcessScheduledClosures_NotExpiredTimer_LeavesScheduleIntact()
    {
        // Arrange
        var instance = new GroupInstance { Id = "wrld_future" };
        var config = new PolicyConfiguration
        {
            GracePeriodMinutes = 60, // Long delay
            AutoCloseEnabled = true
        };
        _enforcementService.ApplyPolicyConfiguration(config);
        
        _enforcementService.ScheduleInstanceClosure(instance, "Future closure");

        // Act
        await _enforcementService.ProcessScheduledClosures();

        // Assert
        _mockInstancesService.Verify(x => x.CloseInstanceAsync(It.IsAny<string>()), Times.Never);
        Assert.Single(_enforcementService.GetScheduledClosures());
    }

    [Fact]
    public async Task ProcessScheduledClosures_ClosureFailure_KeepsInSchedule()
    {
        // Arrange
        var instance = new GroupInstance { Id = "wrld_fail" };
        var config = new PolicyConfiguration
        {
            GracePeriodMinutes = 0,
            AutoCloseEnabled = true
        };
        _enforcementService.ApplyPolicyConfiguration(config);
        
        _mockInstancesService.Setup(x => x.CloseInstanceAsync("wrld_fail"))
            .ReturnsAsync(false); // Simulate failure

        _enforcementService.ScheduleInstanceClosure(instance, "Will fail");

        // Act
        await Task.Delay(100);
        await _enforcementService.ProcessScheduledClosures();

        // Assert
        _mockInstancesService.Verify(x => x.CloseInstanceAsync("wrld_fail"), Times.Once);
        
        // Should still be in schedule for retry
        Assert.Single(_enforcementService.GetScheduledClosures());
    }

    [Fact]
    public async Task MonitoringTimer_PeriodicExecution_CallsInstanceCheck()
    {
        // Arrange
        var instances = new List<GroupInstance>
        {
            new() { Id = "wrld_1", Type = InstanceType.Group },
            new() { Id = "wrld_2", Type = InstanceType.GroupPublic }
        };

        _mockInstancesService.Setup(x => x.GetCurrentInstancesAsync(It.IsAny<string>()))
            .ReturnsAsync(instances);

        var config = new PolicyConfiguration
        {
            MonitoringEnabled = true,
            PollingIntervalMinutes = 0.01, // Very short for testing (0.6 seconds)
            RestrictedInstanceTypes = [InstanceType.GroupPublic]
        };
        _enforcementService.ApplyPolicyConfiguration(config);

        // Act
        _enforcementService.StartMonitoring();
        await Task.Delay(1000); // Wait for at least one poll cycle
        _enforcementService.StopMonitoring();

        // Assert
        _mockInstancesService.Verify(x => x.GetCurrentInstancesAsync(It.IsAny<string>()), 
            Times.AtLeastOnce);
    }

    [Fact]
    public void GetRemainingTime_ActiveSchedule_ReturnsCorrectTime()
    {
        // Arrange
        var instance = new GroupInstance { Id = "wrld_timing" };
        var config = new PolicyConfiguration { GracePeriodMinutes = 15 };
        _enforcementService.ApplyPolicyConfiguration(config);
        
        _enforcementService.ScheduleInstanceClosure(instance, "Timing test");

        // Act
        var remainingTime = _enforcementService.GetRemainingTime("wrld_timing");

        // Assert
        Assert.NotNull(remainingTime);
        Assert.True(remainingTime.Value.TotalMinutes > 14);
        Assert.True(remainingTime.Value.TotalMinutes < 16);
    }

    [Fact]
    public void GetRemainingTime_NoSchedule_ReturnsNull()
    {
        // Act
        var remainingTime = _enforcementService.GetRemainingTime("wrld_nonexistent");

        // Assert
        Assert.Null(remainingTime);
    }

    [Theory]
    [InlineData(1)]   // 1 minute
    [InlineData(5)]   // 5 minutes
    [InlineData(30)]  // 30 minutes
    [InlineData(60)]  // 1 hour
    public void ScheduleInstanceClosure_VariousGracePeriods_CreatesCorrectTimers(int minutes)
    {
        // Arrange
        var instance = new GroupInstance { Id = $"wrld_test{minutes}" };
        var config = new PolicyConfiguration
        {
            GracePeriodMinutes = minutes,
            AutoCloseEnabled = true
        };
        _enforcementService.ApplyPolicyConfiguration(config);

        // Act
        var beforeSchedule = DateTime.UtcNow;
        _enforcementService.ScheduleInstanceClosure(instance, $"Test {minutes}min");
        
        // Assert
        var closure = _enforcementService.GetScheduledClosures().First();
        var expectedTime = beforeSchedule.AddMinutes(minutes);
        var actualTime = closure.ScheduledTime;
        
        // Allow for small timing differences (within 5 seconds)
        Assert.True(Math.Abs((actualTime - expectedTime).TotalSeconds) < 5);
    }

    [Fact]
    public async Task ConcurrentTimerOperations_ThreadSafe()
    {
        // Arrange
        var config = new PolicyConfiguration
        {
            GracePeriodMinutes = 5,
            AutoCloseEnabled = true
        };
        _enforcementService.ApplyPolicyConfiguration(config);

        var instances = Enumerable.Range(1, 10)
            .Select(i => new GroupInstance { Id = $"wrld_concurrent{i}" })
            .ToArray();

        // Act - Schedule multiple closures concurrently
        var scheduleTasks = instances.Select(instance =>
            Task.Run(() => _enforcementService.ScheduleInstanceClosure(instance, "Concurrent test")))
            .ToArray();

        await Task.WhenAll(scheduleTasks);

        // Assert
        var scheduledClosures = _enforcementService.GetScheduledClosures();
        Assert.Equal(10, scheduledClosures.Count());
        Assert.Equal(10, scheduledClosures.Select(c => c.InstanceId).Distinct().Count());
    }

    [Fact]
    public void TimerStatistics_TrackingAccuracy()
    {
        // Arrange
        var config = new PolicyConfiguration { GracePeriodMinutes = 10 };
        _enforcementService.ApplyPolicyConfiguration(config);

        var instance1 = new GroupInstance { Id = "wrld_stats1" };
        var instance2 = new GroupInstance { Id = "wrld_stats2" };

        // Act
        _enforcementService.ScheduleInstanceClosure(instance1, "Stats test 1");
        _enforcementService.ScheduleInstanceClosure(instance2, "Stats test 2");
        _enforcementService.CancelInstanceClosure("wrld_stats1");

        // Assert
        var stats = _enforcementService.GetEnforcementStatistics();
        Assert.Equal(2, stats.TotalScheduledClosures);
        Assert.Equal(1, stats.CancelledClosures);
        Assert.Equal(1, stats.PendingClosures);
    }
}