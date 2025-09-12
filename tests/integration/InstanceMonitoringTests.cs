using System.Net;
using System.Text.Json;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using Xunit;

namespace VrcGroupGuardian.Tests.Integration;

public class InstanceMonitoringTests : IDisposable
{
    private readonly WireMockServer _mockVrchatApi;
    private readonly WireMockServer _mockInternalApi;
    private readonly HttpClient _vrchatClient;
    private readonly HttpClient _internalClient;
    private const string TestGroupId = "grp_12345678-1234-1234-1234-123456789012";

    public InstanceMonitoringTests()
    {
        _mockVrchatApi = WireMockServer.Start();
        _mockInternalApi = WireMockServer.Start();
        
        _vrchatClient = new HttpClient { BaseAddress = new Uri(_mockVrchatApi.Urls[0]) };
        _internalClient = new HttpClient { BaseAddress = new Uri(_mockInternalApi.Urls[0]) };
    }

    [Fact]
    public async Task StartInstanceMonitoring_ShouldDetectAndTrackActiveInstances()
    {
        // Arrange - User scenario: Start monitoring and detect instances
        
        // Mock VRChat API - Group instances response
        var groupInstances = new[]
        {
            new
            {
                instanceId = "wrld_12345678-1234-1234-1234-123456789012:12345~group(grp_12345678-1234-1234-1234-123456789012)",
                world = new { id = "wrld_12345678-1234-1234-1234-123456789012", name = "Test World", authorName = "Creator" },
                type = "group",
                ageGate = false,
                userCount = 5,
                capacity = 20,
                region = "us",
                createdAt = "2024-01-15T10:30:00.000Z"
            },
            new
            {
                instanceId = "wrld_87654321-4321-4321-4321-210987654321:67890~groupplus(grp_12345678-1234-1234-1234-123456789012)",
                world = new { id = "wrld_87654321-4321-4321-4321-210987654321", name = "Another World", authorName = "Other Creator" },
                type = "groupplus",
                ageGate = true,
                userCount = 15,
                capacity = 16,
                region = "eu",
                createdAt = "2024-01-15T11:15:00.000Z"
            }
        };

        _mockVrchatApi
            .Given(Request.Create().WithPath($"/api/1/groups/{TestGroupId}/instances").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200)
                .WithBody(JsonSerializer.Serialize(groupInstances)));

        // Mock Internal API - Instance list response
        var trackedInstances = new[]
        {
            new
            {
                instanceId = "wrld_12345678-1234-1234-1234-123456789012:12345~group(grp_12345678-1234-1234-1234-123456789012)",
                worldName = "Test World",
                instanceType = "Group",
                ageGated = false,
                userCount = 5,
                maxUsers = 20,
                status = "Active",
                countdownSeconds = (int?)null,
                createdAt = "2024-01-15T10:30:00.000Z",
                lastUpdated = "2024-01-15T14:30:00.000Z"
            },
            new
            {
                instanceId = "wrld_87654321-4321-4321-4321-210987654321:67890~groupplus(grp_12345678-1234-1234-1234-123456789012)",
                worldName = "Another World",
                instanceType = "GroupPlus",
                ageGated = true,
                userCount = 15,
                maxUsers = 16,
                status = "Flagged", // This one will be flagged for policy violation
                countdownSeconds = (int?)null,
                createdAt = "2024-01-15T11:15:00.000Z",
                lastUpdated = "2024-01-15T14:30:00.000Z"
            }
        };

        _mockInternalApi
            .Given(Request.Create().WithPath("/instances/list").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200)
                .WithBody(JsonSerializer.Serialize(trackedInstances)));

        // Act - This will fail because InstanceMonitoringOrchestrator doesn't exist yet
        var monitor = new InstanceMonitoringOrchestrator(_vrchatClient, _internalClient);
        
        var monitoringResult = await monitor.StartMonitoringAsync(TestGroupId);
        var currentInstances = await monitor.GetCurrentInstancesAsync();
        var monitoringStats = await monitor.GetMonitoringStatsAsync();

        // Assert
        Assert.NotNull(monitoringResult);
        Assert.True(monitoringResult.Success);
        Assert.True(monitoringResult.MonitoringActive);
        Assert.Equal(TestGroupId, monitoringResult.GroupId);

        Assert.NotNull(currentInstances);
        Assert.Equal(2, currentInstances.Count);
        
        var activeInstance = currentInstances.First(i => i.Status == "Active");
        Assert.Equal("Test World", activeInstance.WorldName);
        Assert.Equal(5, activeInstance.UserCount);
        Assert.False(activeInstance.AgeGated);

        var flaggedInstance = currentInstances.First(i => i.Status == "Flagged");
        Assert.Equal("Another World", flaggedInstance.WorldName);
        Assert.True(flaggedInstance.AgeGated);
        Assert.Equal("GroupPlus", flaggedInstance.InstanceType);

        Assert.NotNull(monitoringStats);
        Assert.Equal(2, monitoringStats.TotalInstancesTracked);
        Assert.Equal(1, monitoringStats.ActiveInstances);
        Assert.Equal(1, monitoringStats.FlaggedInstances);
    }

    [Fact]
    public async Task MonitorInstances_WithPolicyViolations_ShouldFlagInstancesCorrectly()
    {
        // Arrange - Instance violates policy (age-gated with high user count)
        var violatingInstance = new[]
        {
            new
            {
                instanceId = "wrld_adult-world:99999~group(grp_12345678-1234-1234-1234-123456789012)",
                world = new { id = "wrld_adult-world", name = "Adult Only World", authorName = "Adult Creator" },
                type = "group",
                ageGate = true, // Policy violation - age-gated content
                userCount = 18,
                capacity = 20,
                region = "us",
                createdAt = "2024-01-15T12:00:00.000Z"
            }
        };

        _mockVrchatApi
            .Given(Request.Create().WithPath($"/api/1/groups/{TestGroupId}/instances").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200)
                .WithBody(JsonSerializer.Serialize(violatingInstance)));

        var flaggedInstance = new[]
        {
            new
            {
                instanceId = "wrld_adult-world:99999~group(grp_12345678-1234-1234-1234-123456789012)",
                worldName = "Adult Only World",
                instanceType = "Group",
                ageGated = true,
                userCount = 18,
                maxUsers = 20,
                status = "Flagged",
                countdownSeconds = (int?)null,
                createdAt = "2024-01-15T12:00:00.000Z",
                lastUpdated = "2024-01-15T14:35:00.000Z"
            }
        };

        _mockInternalApi
            .Given(Request.Create().WithPath("/instances/list").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200)
                .WithBody(JsonSerializer.Serialize(flaggedInstance)));

        // Mock enforcement status showing violation detected
        var enforcementStatus = new
        {
            active = true,
            policiesChecked = 1,
            violationsFound = 1,
            lastPollTime = "2024-01-15T14:35:00.000Z",
            nextPollTime = "2024-01-15T14:36:00.000Z",
            rateLimited = false,
            errorMessage = (string?)null
        };

        _mockInternalApi
            .Given(Request.Create().WithPath("/enforcement/status").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200)
                .WithBody(JsonSerializer.Serialize(enforcementStatus)));

        // Act - This will fail because InstanceMonitoringOrchestrator doesn't exist yet
        var monitor = new InstanceMonitoringOrchestrator(_vrchatClient, _internalClient);
        
        await monitor.StartMonitoringAsync(TestGroupId);
        var policyCheck = await monitor.RunPolicyCheckAsync();
        var violations = await monitor.GetCurrentViolationsAsync();
        var status = await monitor.GetEnforcementStatusAsync();

        // Assert
        Assert.NotNull(policyCheck);
        Assert.True(policyCheck.CheckCompleted);
        Assert.Equal(1, policyCheck.ViolationsFound);
        Assert.Contains("age-gated", policyCheck.ViolationReasons);

        Assert.NotNull(violations);
        Assert.Single(violations);
        Assert.Equal("Adult Only World", violations[0].WorldName);
        Assert.Equal("Policy violation: Age-gated content detected", violations[0].ViolationReason);

        Assert.NotNull(status);
        Assert.True(status.Active);
        Assert.Equal(1, status.ViolationsFound);
    }

    [Fact]
    public async Task MonitorInstances_WithRateLimiting_ShouldHandleGracefully()
    {
        // Arrange - VRChat API returns rate limit error
        _mockVrchatApi
            .Given(Request.Create().WithPath($"/api/1/groups/{TestGroupId}/instances").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(429)
                .WithBody("{\"error\":\"Rate limit exceeded\"}"));

        // Mock enforcement status showing rate limiting
        var rateLimitedStatus = new
        {
            active = true,
            policiesChecked = 5,
            violationsFound = 0,
            lastPollTime = "2024-01-15T14:30:00.000Z",
            nextPollTime = "2024-01-15T14:40:00.000Z", // Delayed due to rate limiting
            rateLimited = true,
            errorMessage = "Rate limit exceeded, backing off for 10 minutes"
        };

        _mockInternalApi
            .Given(Request.Create().WithPath("/enforcement/status").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200)
                .WithBody(JsonSerializer.Serialize(rateLimitedStatus)));

        // Act - This will fail because InstanceMonitoringOrchestrator doesn't exist yet
        var monitor = new InstanceMonitoringOrchestrator(_vrchatClient, _internalClient);
        
        await monitor.StartMonitoringAsync(TestGroupId);
        var pollResult = await monitor.PollInstancesAsync();
        var status = await monitor.GetEnforcementStatusAsync();

        // Assert
        Assert.NotNull(pollResult);
        Assert.False(pollResult.Success);
        Assert.Contains("Rate limit", pollResult.ErrorMessage);

        Assert.NotNull(status);
        Assert.True(status.RateLimited);
        Assert.Contains("backing off", status.ErrorMessage);
        
        // Next poll should be delayed
        Assert.True(status.NextPollTime > status.LastPollTime.AddMinutes(5));
    }

    [Fact]
    public async Task MonitorInstances_WithNetworkError_ShouldRetryGracefully()
    {
        // Arrange - First call fails, second succeeds
        var sequence = 0;
        
        _mockVrchatApi
            .Given(Request.Create().WithPath($"/api/1/groups/{TestGroupId}/instances").UsingGet())
            .RespondWith(Request =>
            {
                sequence++;
                if (sequence == 1)
                {
                    return Response.Create().WithStatusCode(500).WithBody("{\"error\":\"Internal server error\"}");
                }
                else
                {
                    var instances = new[]
                    {
                        new
                        {
                            instanceId = "wrld_recovery-test:111~group(grp_12345678-1234-1234-1234-123456789012)",
                            world = new { id = "wrld_recovery-test", name = "Recovery Test World", authorName = "Test Creator" },
                            type = "group",
                            ageGate = false,
                            userCount = 3,
                            capacity = 20,
                            region = "us",
                            createdAt = "2024-01-15T14:00:00.000Z"
                        }
                    };
                    return Response.Create().WithStatusCode(200).WithBody(JsonSerializer.Serialize(instances));
                }
            });

        var recoveredStatus = new
        {
            active = true,
            policiesChecked = 1,
            violationsFound = 0,
            lastPollTime = "2024-01-15T14:31:00.000Z",
            nextPollTime = "2024-01-15T14:32:00.000Z",
            rateLimited = false,
            errorMessage = (string?)null
        };

        _mockInternalApi
            .Given(Request.Create().WithPath("/enforcement/status").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200)
                .WithBody(JsonSerializer.Serialize(recoveredStatus)));

        // Act - This will fail because InstanceMonitoringOrchestrator doesn't exist yet
        var monitor = new InstanceMonitoringOrchestrator(_vrchatClient, _internalClient);
        
        await monitor.StartMonitoringAsync(TestGroupId);
        
        // First poll should fail
        var firstPoll = await monitor.PollInstancesAsync();
        Assert.False(firstPoll.Success);
        Assert.Contains("server error", firstPoll.ErrorMessage);
        
        // Second poll should succeed after retry
        var secondPoll = await monitor.PollInstancesAsync();
        Assert.True(secondPoll.Success);
        Assert.Equal(1, secondPoll.InstancesFound);

        var status = await monitor.GetEnforcementStatusAsync();
        Assert.True(status.Active);
        Assert.Null(status.ErrorMessage);
    }

    [Fact]
    public async Task StopInstanceMonitoring_ShouldCleanupAndStop()
    {
        // Arrange - Monitoring is active
        _mockVrchatApi
            .Given(Request.Create().WithPath($"/api/1/groups/{TestGroupId}/instances").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200)
                .WithBody("[]"));

        var activeStatus = new
        {
            active = true,
            policiesChecked = 10,
            violationsFound = 2,
            lastPollTime = "2024-01-15T14:30:00.000Z",
            nextPollTime = "2024-01-15T14:31:00.000Z",
            rateLimited = false,
            errorMessage = (string?)null
        };

        var inactiveStatus = new
        {
            active = false,
            policiesChecked = 10,
            violationsFound = 2,
            lastPollTime = "2024-01-15T14:30:00.000Z",
            nextPollTime = (string?)null,
            rateLimited = false,
            errorMessage = "Monitoring stopped by user"
        };

        _mockInternalApi
            .Given(Request.Create().WithPath("/enforcement/status").UsingGet())
            .InScenario("monitoring")
            .WhenStateIs(Scenario.Started)
            .RespondWith(Response.Create().WithStatusCode(200)
                .WithBody(JsonSerializer.Serialize(activeStatus)))
            .WillSetStateTo("stopped");

        _mockInternalApi
            .Given(Request.Create().WithPath("/enforcement/status").UsingGet())
            .InScenario("monitoring")
            .WhenStateIs("stopped")
            .RespondWith(Response.Create().WithStatusCode(200)
                .WithBody(JsonSerializer.Serialize(inactiveStatus)));

        // Act - This will fail because InstanceMonitoringOrchestrator doesn't exist yet
        var monitor = new InstanceMonitoringOrchestrator(_vrchatClient, _internalClient);
        
        await monitor.StartMonitoringAsync(TestGroupId);
        
        var activeStatusResult = await monitor.GetEnforcementStatusAsync();
        Assert.True(activeStatusResult.Active);
        
        var stopResult = await monitor.StopMonitoringAsync();
        var stoppedStatusResult = await monitor.GetEnforcementStatusAsync();

        // Assert
        Assert.NotNull(stopResult);
        Assert.True(stopResult.Success);
        Assert.Contains("stopped", stopResult.Message);

        Assert.NotNull(stoppedStatusResult);
        Assert.False(stoppedStatusResult.Active);
        Assert.Contains("stopped by user", stoppedStatusResult.ErrorMessage);
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
public class InstanceMonitoringOrchestrator
{
    private readonly HttpClient _vrchatClient;
    private readonly HttpClient _internalClient;

    public InstanceMonitoringOrchestrator(HttpClient vrchatClient, HttpClient internalClient)
    {
        _vrchatClient = vrchatClient;
        _internalClient = internalClient;
    }

    public async Task<MonitoringResult> StartMonitoringAsync(string groupId)
    {
        throw new NotImplementedException("InstanceMonitoringOrchestrator.StartMonitoringAsync not implemented yet");
    }

    public async Task<List<MonitoredInstance>> GetCurrentInstancesAsync()
    {
        throw new NotImplementedException("InstanceMonitoringOrchestrator.GetCurrentInstancesAsync not implemented yet");
    }

    public async Task<MonitoringStats> GetMonitoringStatsAsync()
    {
        throw new NotImplementedException("InstanceMonitoringOrchestrator.GetMonitoringStatsAsync not implemented yet");
    }

    public async Task<PolicyCheckResult> RunPolicyCheckAsync()
    {
        throw new NotImplementedException("InstanceMonitoringOrchestrator.RunPolicyCheckAsync not implemented yet");
    }

    public async Task<List<PolicyViolation>> GetCurrentViolationsAsync()
    {
        throw new NotImplementedException("InstanceMonitoringOrchestrator.GetCurrentViolationsAsync not implemented yet");
    }

    public async Task<EnforcementStatusResult> GetEnforcementStatusAsync()
    {
        throw new NotImplementedException("InstanceMonitoringOrchestrator.GetEnforcementStatusAsync not implemented yet");
    }

    public async Task<PollResult> PollInstancesAsync()
    {
        throw new NotImplementedException("InstanceMonitoringOrchestrator.PollInstancesAsync not implemented yet");
    }

    public async Task<StopResult> StopMonitoringAsync()
    {
        throw new NotImplementedException("InstanceMonitoringOrchestrator.StopMonitoringAsync not implemented yet");
    }
}

public class MonitoringResult
{
    public bool Success { get; set; }
    public bool MonitoringActive { get; set; }
    public string GroupId { get; set; } = string.Empty;
}

public class MonitoredInstance
{
    public string InstanceId { get; set; } = string.Empty;
    public string WorldName { get; set; } = string.Empty;
    public string InstanceType { get; set; } = string.Empty;
    public bool AgeGated { get; set; }
    public int UserCount { get; set; }
    public string Status { get; set; } = string.Empty;
    public int? CountdownSeconds { get; set; }
}

public class MonitoringStats
{
    public int TotalInstancesTracked { get; set; }
    public int ActiveInstances { get; set; }
    public int FlaggedInstances { get; set; }
    public int ClosedInstances { get; set; }
}

public class PolicyCheckResult
{
    public bool CheckCompleted { get; set; }
    public int ViolationsFound { get; set; }
    public List<string> ViolationReasons { get; set; } = new();
}

public class PolicyViolation
{
    public string InstanceId { get; set; } = string.Empty;
    public string WorldName { get; set; } = string.Empty;
    public string ViolationReason { get; set; } = string.Empty;
}

public class EnforcementStatusResult
{
    public bool Active { get; set; }
    public int ViolationsFound { get; set; }
    public bool RateLimited { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime LastPollTime { get; set; }
    public DateTime? NextPollTime { get; set; }
}

public class PollResult
{
    public bool Success { get; set; }
    public int InstancesFound { get; set; }
    public string? ErrorMessage { get; set; }
}

public class StopResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
}