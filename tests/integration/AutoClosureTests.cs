using System.Net;
using System.Text.Json;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using Xunit;

namespace VrcGroupGuardian.Tests.Integration;

public class AutoClosureTests : IDisposable
{
    private readonly WireMockServer _mockVrchatApi;
    private readonly WireMockServer _mockInternalApi;
    private readonly HttpClient _vrchatClient;
    private readonly HttpClient _internalClient;
    private const string TestGroupId = "grp_12345678-1234-1234-1234-123456789012";
    private const string ViolatingInstanceId = "wrld_violation-world:12345~group(grp_12345678-1234-1234-1234-123456789012)";

    public AutoClosureTests()
    {
        _mockVrchatApi = WireMockServer.Start();
        _mockInternalApi = WireMockServer.Start();
        
        _vrchatClient = new HttpClient { BaseAddress = new Uri(_mockVrchatApi.Urls[0]) };
        _internalClient = new HttpClient { BaseAddress = new Uri(_mockInternalApi.Urls[0]) };
    }

    [Fact]
    public async Task AutoClosure_WithGracePeriod_ShouldCountdownAndCloseInstance()
    {
        // Arrange - User scenario: Instance violates policy, auto-closure with 180s grace period
        
        // Mock VRChat API - Instance closure endpoint
        _mockVrchatApi
            .Given(Request.Create()
                .WithPath("/api/1/instances/wrld_violation-world:12345")
                .UsingDelete())
            .RespondWith(Response.Create().WithStatusCode(200)
                .WithBody("{\"message\":\"Instance closed successfully\"}"));

        // Mock Internal API - Instance state progression
        var flaggedInstance = new[]
        {
            new
            {
                instanceId = ViolatingInstanceId,
                worldName = "Violation World",
                instanceType = "Group",
                ageGated = true,
                userCount = 18,
                maxUsers = 20,
                status = "Flagged",
                countdownSeconds = (int?)null,
                createdAt = "2024-01-15T14:00:00.000Z",
                lastUpdated = "2024-01-15T14:30:00.000Z"
            }
        };

        var countdownInstance = new[]
        {
            new
            {
                instanceId = ViolatingInstanceId,
                worldName = "Violation World",
                instanceType = "Group",
                ageGated = true,
                userCount = 18,
                maxUsers = 20,
                status = "ClosingCountdown",
                countdownSeconds = 180,
                createdAt = "2024-01-15T14:00:00.000Z",
                lastUpdated = "2024-01-15T14:31:00.000Z"
            }
        };

        var closedInstance = new[]
        {
            new
            {
                instanceId = ViolatingInstanceId,
                worldName = "Violation World",
                instanceType = "Group",
                ageGated = true,
                userCount = 0,
                maxUsers = 20,
                status = "Closed",
                countdownSeconds = (int?)null,
                createdAt = "2024-01-15T14:00:00.000Z",
                lastUpdated = "2024-01-15T14:34:00.000Z"
            }
        };

        // Sequential responses for instance state progression
        _mockInternalApi
            .Given(Request.Create().WithPath("/instances/list").UsingGet())
            .InScenario("auto-closure")
            .WhenStateIs(Scenario.Started)
            .RespondWith(Response.Create().WithStatusCode(200)
                .WithBody(JsonSerializer.Serialize(flaggedInstance)))
            .WillSetStateTo("countdown");

        _mockInternalApi
            .Given(Request.Create().WithPath("/instances/list").UsingGet())
            .InScenario("auto-closure")
            .WhenStateIs("countdown")
            .RespondWith(Response.Create().WithStatusCode(200)
                .WithBody(JsonSerializer.Serialize(countdownInstance)))
            .WillSetStateTo("closed");

        _mockInternalApi
            .Given(Request.Create().WithPath("/instances/list").UsingGet())
            .InScenario("auto-closure")
            .WhenStateIs("closed")
            .RespondWith(Response.Create().WithStatusCode(200)
                .WithBody(JsonSerializer.Serialize(closedInstance)));

        // Mock instance closure endpoint
        _mockInternalApi
            .Given(Request.Create().WithPath("/instances/close").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200)
                .WithBody("{\"message\":\"Instance closed successfully\"}"));

        // Act - This will fail because AutoClosureOrchestrator doesn't exist yet
        var autoClosureService = new AutoClosureOrchestrator(_vrchatClient, _internalClient);
        
        // Step 1: Start auto-closure process
        var startResult = await autoClosureService.StartAutoClosureAsync(ViolatingInstanceId, "Age-gated content detected");
        
        // Step 2: Check status during countdown
        var countdownStatus = await autoClosureService.GetClosureStatusAsync(ViolatingInstanceId);
        
        // Step 3: Simulate countdown completion and closure
        var closureResult = await autoClosureService.ExecuteClosureAsync(ViolatingInstanceId);
        
        // Step 4: Verify final state
        var finalStatus = await autoClosureService.GetClosureStatusAsync(ViolatingInstanceId);

        // Assert
        Assert.NotNull(startResult);
        Assert.True(startResult.Success);
        Assert.Equal(ViolatingInstanceId, startResult.InstanceId);
        Assert.Equal("Age-gated content detected", startResult.Reason);

        Assert.NotNull(countdownStatus);
        Assert.Equal("ClosingCountdown", countdownStatus.Status);
        Assert.Equal(180, countdownStatus.CountdownSeconds);
        Assert.True(countdownStatus.CanCancel);

        Assert.NotNull(closureResult);
        Assert.True(closureResult.Success);
        Assert.Contains("closed successfully", closureResult.Message);

        Assert.NotNull(finalStatus);
        Assert.Equal("Closed", finalStatus.Status);
        Assert.Null(finalStatus.CountdownSeconds);
        Assert.False(finalStatus.CanCancel);
    }

    [Fact]
    public async Task AutoClosure_CancelledDuringGracePeriod_ShouldStopCountdownAndResetStatus()
    {
        // Arrange - User cancels auto-closure during countdown
        var countdownInstance = new[]
        {
            new
            {
                instanceId = ViolatingInstanceId,
                worldName = "Violation World",
                instanceType = "Group",
                ageGated = true,
                userCount = 12,
                maxUsers = 20,
                status = "ClosingCountdown",
                countdownSeconds = 120,
                createdAt = "2024-01-15T14:00:00.000Z",
                lastUpdated = "2024-01-15T14:31:00.000Z"
            }
        };

        var cancelledInstance = new[]
        {
            new
            {
                instanceId = ViolatingInstanceId,
                worldName = "Violation World",
                instanceType = "Group",
                ageGated = true,
                userCount = 12,
                maxUsers = 20,
                status = "Active", // Reset to active after cancellation
                countdownSeconds = (int?)null,
                createdAt = "2024-01-15T14:00:00.000Z",
                lastUpdated = "2024-01-15T14:32:00.000Z"
            }
        };

        _mockInternalApi
            .Given(Request.Create().WithPath("/instances/list").UsingGet())
            .InScenario("cancellation")
            .WhenStateIs(Scenario.Started)
            .RespondWith(Response.Create().WithStatusCode(200)
                .WithBody(JsonSerializer.Serialize(countdownInstance)))
            .WillSetStateTo("cancelled");

        _mockInternalApi
            .Given(Request.Create().WithPath("/instances/list").UsingGet())
            .InScenario("cancellation")
            .WhenStateIs("cancelled")
            .RespondWith(Response.Create().WithStatusCode(200)
                .WithBody(JsonSerializer.Serialize(cancelledInstance)));

        // Mock enforcement cancellation endpoint
        _mockInternalApi
            .Given(Request.Create().WithPath("/enforcement/cancel").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200)
                .WithBody("{\"message\":\"Closure cancelled successfully\"}"));

        // Act - This will fail because AutoClosureOrchestrator doesn't exist yet
        var autoClosureService = new AutoClosureOrchestrator(_vrchatClient, _internalClient);
        
        // Step 1: Check current countdown status
        var beforeCancellation = await autoClosureService.GetClosureStatusAsync(ViolatingInstanceId);
        
        // Step 2: Cancel the pending closure
        var cancellationResult = await autoClosureService.CancelClosureAsync(ViolatingInstanceId, "Moderator intervention");
        
        // Step 3: Verify instance status reset
        var afterCancellation = await autoClosureService.GetClosureStatusAsync(ViolatingInstanceId);

        // Assert
        Assert.NotNull(beforeCancellation);
        Assert.Equal("ClosingCountdown", beforeCancellation.Status);
        Assert.Equal(120, beforeCancellation.CountdownSeconds);
        Assert.True(beforeCancellation.CanCancel);

        Assert.NotNull(cancellationResult);
        Assert.True(cancellationResult.Success);
        Assert.Contains("cancelled", cancellationResult.Message);
        Assert.Equal("Moderator intervention", cancellationResult.Reason);

        Assert.NotNull(afterCancellation);
        Assert.Equal("Active", afterCancellation.Status);
        Assert.Null(afterCancellation.CountdownSeconds);
        Assert.False(afterCancellation.CanCancel);
    }

    [Fact]
    public async Task AutoClosure_WithDifferentGracePeriods_ShouldRespectPolicySettings()
    {
        // Arrange - Test different grace periods (60s, 180s, 300s)
        var testCases = new[]
        {
            new { GracePeriod = 60, InstanceSuffix = "60s", ExpectedCountdown = 60 },
            new { GracePeriod = 180, InstanceSuffix = "180s", ExpectedCountdown = 180 },
            new { GracePeriod = 300, InstanceSuffix = "300s", ExpectedCountdown = 300 }
        };

        foreach (var testCase in testCases)
        {
            var instanceId = $"wrld_test-{testCase.InstanceSuffix}:123~group({TestGroupId})";
            
            var countdownInstance = new[]
            {
                new
                {
                    instanceId = instanceId,
                    worldName = $"Test World {testCase.InstanceSuffix}",
                    instanceType = "Group",
                    ageGated = false,
                    userCount = 5,
                    maxUsers = 20,
                    status = "ClosingCountdown",
                    countdownSeconds = testCase.ExpectedCountdown,
                    createdAt = "2024-01-15T14:00:00.000Z",
                    lastUpdated = "2024-01-15T14:30:00.000Z"
                }
            };

            _mockInternalApi
                .Given(Request.Create().WithPath("/instances/list").UsingGet())
                .RespondWith(Response.Create().WithStatusCode(200)
                    .WithBody(JsonSerializer.Serialize(countdownInstance)));

            // Act - This will fail because AutoClosureOrchestrator doesn't exist yet
            var autoClosureService = new AutoClosureOrchestrator(_vrchatClient, _internalClient);
            
            var startResult = await autoClosureService.StartAutoClosureAsync(instanceId, "Policy test");
            var status = await autoClosureService.GetClosureStatusAsync(instanceId);

            // Assert
            Assert.NotNull(status);
            Assert.Equal("ClosingCountdown", status.Status);
            Assert.Equal(testCase.ExpectedCountdown, status.CountdownSeconds);
        }
    }

    [Fact]
    public async Task AutoClosure_WithInstanceAlreadyClosed_ShouldHandleGracefully()
    {
        // Arrange - Instance is already closed when auto-closure attempts to execute
        _mockVrchatApi
            .Given(Request.Create()
                .WithPath("/api/1/instances/wrld_already-closed:12345")
                .UsingDelete())
            .RespondWith(Response.Create().WithStatusCode(404)
                .WithBody("{\"error\":\"Instance not found\"}"));

        var alreadyClosedInstance = new[]
        {
            new
            {
                instanceId = "wrld_already-closed:12345~group(grp_12345678-1234-1234-1234-123456789012)",
                worldName = "Already Closed World",
                instanceType = "Group",
                ageGated = false,
                userCount = 0,
                maxUsers = 20,
                status = "Closed",
                countdownSeconds = (int?)null,
                createdAt = "2024-01-15T14:00:00.000Z",
                lastUpdated = "2024-01-15T14:35:00.000Z"
            }
        };

        _mockInternalApi
            .Given(Request.Create().WithPath("/instances/list").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200)
                .WithBody(JsonSerializer.Serialize(alreadyClosedInstance)));

        _mockInternalApi
            .Given(Request.Create().WithPath("/instances/close").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(404)
                .WithBody("{\"error\":\"Instance not found\"}"));

        // Act - This will fail because AutoClosureOrchestrator doesn't exist yet
        var autoClosureService = new AutoClosureOrchestrator(_vrchatClient, _internalClient);
        
        var instanceId = "wrld_already-closed:12345~group(grp_12345678-1234-1234-1234-123456789012)";
        var closureResult = await autoClosureService.ExecuteClosureAsync(instanceId);
        var status = await autoClosureService.GetClosureStatusAsync(instanceId);

        // Assert
        Assert.NotNull(closureResult);
        Assert.True(closureResult.Success); // Should succeed even if instance already closed
        Assert.Contains("already closed", closureResult.Message, StringComparison.OrdinalIgnoreCase);

        Assert.NotNull(status);
        Assert.Equal("Closed", status.Status);
        Assert.Equal(0, status.UserCount);
    }

    [Fact]
    public async Task AutoClosure_WithMultipleInstancesInCountdown_ShouldTrackAllCountdowns()
    {
        // Arrange - Multiple instances in different countdown states
        var multipleInstances = new[]
        {
            new
            {
                instanceId = "wrld_instance1:111~group(grp_12345678-1234-1234-1234-123456789012)",
                worldName = "Instance 1",
                instanceType = "Group",
                ageGated = true,
                userCount = 10,
                maxUsers = 20,
                status = "ClosingCountdown",
                countdownSeconds = 180,
                createdAt = "2024-01-15T14:00:00.000Z",
                lastUpdated = "2024-01-15T14:30:00.000Z"
            },
            new
            {
                instanceId = "wrld_instance2:222~group(grp_12345678-1234-1234-1234-123456789012)",
                worldName = "Instance 2",
                instanceType = "GroupPlus",
                ageGated = false,
                userCount = 15,
                maxUsers = 16,
                status = "ClosingCountdown",
                countdownSeconds = 120,
                createdAt = "2024-01-15T14:00:00.000Z",
                lastUpdated = "2024-01-15T14:31:00.000Z"
            },
            new
            {
                instanceId = "wrld_instance3:333~group(grp_12345678-1234-1234-1234-123456789012)",
                worldName = "Instance 3",
                instanceType = "Group",
                ageGated = false,
                userCount = 5,
                maxUsers = 20,
                status = "Active",
                countdownSeconds = (int?)null,
                createdAt = "2024-01-15T14:00:00.000Z",
                lastUpdated = "2024-01-15T14:30:00.000Z"
            }
        };

        _mockInternalApi
            .Given(Request.Create().WithPath("/instances/list").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200)
                .WithBody(JsonSerializer.Serialize(multipleInstances)));

        // Act - This will fail because AutoClosureOrchestrator doesn't exist yet
        var autoClosureService = new AutoClosureOrchestrator(_vrchatClient, _internalClient);
        
        var allCountdowns = await autoClosureService.GetAllActiveCountdownsAsync();
        var closuresSummary = await autoClosureService.GetClosureSummaryAsync();

        // Assert
        Assert.NotNull(allCountdowns);
        Assert.Equal(2, allCountdowns.Count); // Only instances with active countdowns
        
        var instance1Countdown = allCountdowns.First(c => c.InstanceId.Contains("instance1"));
        Assert.Equal(180, instance1Countdown.CountdownSeconds);
        Assert.Equal("Instance 1", instance1Countdown.WorldName);
        
        var instance2Countdown = allCountdowns.First(c => c.InstanceId.Contains("instance2"));
        Assert.Equal(120, instance2Countdown.CountdownSeconds);
        Assert.Equal("Instance 2", instance2Countdown.WorldName);

        Assert.NotNull(closuresSummary);
        Assert.Equal(2, closuresSummary.ActiveCountdowns);
        Assert.Equal(1, closuresSummary.ActiveInstances);
        Assert.Equal(0, closuresSummary.ClosedInstances);
    }

    public void Dispose()
    {
        _vrchatClient?.Dispose();
        _internalClient?.Dispose();
        _mockVrchatApi?.Stop();
        _mockVrchatApi?.Dispose();
        _mockInternalApi?.Stop();
        _mockInternalApi?.Dispose();
    }
}

// Placeholder classes that will fail compilation - this is intentional for TDD
public class AutoClosureOrchestrator
{
    private readonly HttpClient _vrchatClient;
    private readonly HttpClient _internalClient;

    public AutoClosureOrchestrator(HttpClient vrchatClient, HttpClient internalClient)
    {
        _vrchatClient = vrchatClient;
        _internalClient = internalClient;
    }

    public async Task<AutoClosureStartResult> StartAutoClosureAsync(string instanceId, string reason)
    {
        throw new NotImplementedException("AutoClosureOrchestrator.StartAutoClosureAsync not implemented yet");
    }

    public async Task<ClosureStatus> GetClosureStatusAsync(string instanceId)
    {
        throw new NotImplementedException("AutoClosureOrchestrator.GetClosureStatusAsync not implemented yet");
    }

    public async Task<ClosureExecutionResult> ExecuteClosureAsync(string instanceId)
    {
        throw new NotImplementedException("AutoClosureOrchestrator.ExecuteClosureAsync not implemented yet");
    }

    public async Task<CancellationResult> CancelClosureAsync(string instanceId, string reason)
    {
        throw new NotImplementedException("AutoClosureOrchestrator.CancelClosureAsync not implemented yet");
    }

    public async Task<List<CountdownInfo>> GetAllActiveCountdownsAsync()
    {
        throw new NotImplementedException("AutoClosureOrchestrator.GetAllActiveCountdownsAsync not implemented yet");
    }

    public async Task<ClosureSummary> GetClosureSummaryAsync()
    {
        throw new NotImplementedException("AutoClosureOrchestrator.GetClosureSummaryAsync not implemented yet");
    }
}

public class AutoClosureStartResult
{
    public bool Success { get; set; }
    public string InstanceId { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}

public class ClosureStatus
{
    public string Status { get; set; } = string.Empty;
    public int? CountdownSeconds { get; set; }
    public bool CanCancel { get; set; }
    public int UserCount { get; set; }
}

public class ClosureExecutionResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
}

public class CancellationResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}

public class CountdownInfo
{
    public string InstanceId { get; set; } = string.Empty;
    public string WorldName { get; set; } = string.Empty;
    public int CountdownSeconds { get; set; }
}

public class ClosureSummary
{
    public int ActiveCountdowns { get; set; }
    public int ActiveInstances { get; set; }
    public int ClosedInstances { get; set; }
}