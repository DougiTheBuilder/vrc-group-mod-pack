using System.Net;
using System.Text.Json;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using Xunit;

namespace VrcGroupGuardian.Tests.Integration;

public class ManualInstanceManagementTests : IDisposable
{
    private readonly WireMockServer _mockVrchatApi;
    private readonly WireMockServer _mockInternalApi;
    private readonly HttpClient _vrchatClient;
    private readonly HttpClient _internalClient;
    private const string TestGroupId = "grp_12345678-1234-1234-1234-123456789012";
    private const string TestInstanceId = "wrld_manual-test:12345~group(grp_12345678-1234-1234-1234-123456789012)";

    public ManualInstanceManagementTests()
    {
        _mockVrchatApi = WireMockServer.Start();
        _mockInternalApi = WireMockServer.Start();
        
        _vrchatClient = new HttpClient { BaseAddress = new Uri(_mockVrchatApi.Urls[0]) };
        _internalClient = new HttpClient { BaseAddress = new Uri(_mockInternalApi.Urls[0]) };
    }

    [Fact]
    public async Task ManualCloseInstance_WithValidPermissions_ShouldCloseImmediately()
    {
        // Arrange - User scenario: Moderator manually closes instance without countdown
        
        // Mock VRChat API - Instance closure
        _mockVrchatApi
            .Given(Request.Create()
                .WithPath("/api/1/instances/wrld_manual-test:12345")
                .UsingDelete())
            .RespondWith(Response.Create().WithStatusCode(200)
                .WithBody("{\"message\":\"Instance closed successfully\"}"));

        // Mock Internal API - Manual instance closure
        var closeRequest = new
        {
            instanceId = TestInstanceId,
            reason = "Manual closure by moderator"
        };

        _mockInternalApi
            .Given(Request.Create()
                .WithPath("/instances/close")
                .UsingPost()
                .WithBody(JsonSerializer.Serialize(closeRequest)))
            .RespondWith(Response.Create().WithStatusCode(200)
                .WithBody("{\"message\":\"Instance closed successfully\"}"));

        // Mock instance state before and after closure
        var activeInstance = new[]
        {
            new
            {
                instanceId = TestInstanceId,
                worldName = "Manual Test World",
                instanceType = "Group",
                ageGated = false,
                userCount = 8,
                maxUsers = 20,
                status = "Active",
                countdownSeconds = (int?)null,
                createdAt = "2024-01-15T14:00:00.000Z",
                lastUpdated = "2024-01-15T14:30:00.000Z"
            }
        };

        var closedInstance = new[]
        {
            new
            {
                instanceId = TestInstanceId,
                worldName = "Manual Test World",
                instanceType = "Group",
                ageGated = false,
                userCount = 0,
                maxUsers = 20,
                status = "Closed",
                countdownSeconds = (int?)null,
                createdAt = "2024-01-15T14:00:00.000Z",
                lastUpdated = "2024-01-15T14:31:00.000Z"
            }
        };

        _mockInternalApi
            .Given(Request.Create().WithPath("/instances/list").UsingGet())
            .InScenario("manual-close")
            .WhenStateIs(Scenario.Started)
            .RespondWith(Response.Create().WithStatusCode(200)
                .WithBody(JsonSerializer.Serialize(activeInstance)))
            .WillSetStateTo("closed");

        _mockInternalApi
            .Given(Request.Create().WithPath("/instances/list").UsingGet())
            .InScenario("manual-close")
            .WhenStateIs("closed")
            .RespondWith(Response.Create().WithStatusCode(200)
                .WithBody(JsonSerializer.Serialize(closedInstance)));

        // Act - This will fail because ManualInstanceManager doesn't exist yet
        var instanceManager = new ManualInstanceManager(_vrchatClient, _internalClient);
        
        // Step 1: Get current instance info
        var beforeClosure = await instanceManager.GetInstanceInfoAsync(TestInstanceId);
        
        // Step 2: Manually close instance
        var closureResult = await instanceManager.CloseInstanceAsync(TestInstanceId, "Manual closure by moderator");
        
        // Step 3: Verify closure
        var afterClosure = await instanceManager.GetInstanceInfoAsync(TestInstanceId);

        // Assert
        Assert.NotNull(beforeClosure);
        Assert.Equal("Active", beforeClosure.Status);
        Assert.Equal(8, beforeClosure.UserCount);
        Assert.Equal("Manual Test World", beforeClosure.WorldName);

        Assert.NotNull(closureResult);
        Assert.True(closureResult.Success);
        Assert.Equal(TestInstanceId, closureResult.InstanceId);
        Assert.Contains("closed successfully", closureResult.Message);
        Assert.False(closureResult.UsedCountdown); // Manual closure skips countdown

        Assert.NotNull(afterClosure);
        Assert.Equal("Closed", afterClosure.Status);
        Assert.Equal(0, afterClosure.UserCount);
    }

    [Fact]
    public async Task ManualCloseInstance_WithInsufficientPermissions_ShouldFail()
    {
        // Arrange - User lacks required permissions for manual closure
        _mockVrchatApi
            .Given(Request.Create()
                .WithPath("/api/1/instances/wrld_manual-test:12345")
                .UsingDelete())
            .RespondWith(Response.Create().WithStatusCode(403)
                .WithBody("{\"error\":\"Insufficient permissions to close instance\"}"));

        _mockInternalApi
            .Given(Request.Create().WithPath("/instances/close").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(403)
                .WithBody("{\"error\":\"Insufficient permissions\"}"));

        // Act & Assert - This will fail because ManualInstanceManager doesn't exist yet
        var instanceManager = new ManualInstanceManager(_vrchatClient, _internalClient);
        
        var exception = await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => instanceManager.CloseInstanceAsync(TestInstanceId, "Attempted manual closure"));
        
        Assert.Contains("Insufficient permissions", exception.Message);
    }

    [Fact]
    public async Task CancelPendingAutoClosure_ShouldStopCountdownAndResetInstance()
    {
        // Arrange - Instance has pending auto-closure that user wants to cancel
        var countdownInstance = new[]
        {
            new
            {
                instanceId = TestInstanceId,
                worldName = "Countdown Test World",
                instanceType = "Group",
                ageGated = true,
                userCount = 10,
                maxUsers = 20,
                status = "ClosingCountdown",
                countdownSeconds = 150,
                createdAt = "2024-01-15T14:00:00.000Z",
                lastUpdated = "2024-01-15T14:30:00.000Z"
            }
        };

        var resetInstance = new[]
        {
            new
            {
                instanceId = TestInstanceId,
                worldName = "Countdown Test World",
                instanceType = "Group",
                ageGated = true,
                userCount = 10,
                maxUsers = 20,
                status = "Active",
                countdownSeconds = (int?)null,
                createdAt = "2024-01-15T14:00:00.000Z",
                lastUpdated = "2024-01-15T14:31:00.000Z"
            }
        };

        _mockInternalApi
            .Given(Request.Create().WithPath("/instances/list").UsingGet())
            .InScenario("cancel-countdown")
            .WhenStateIs(Scenario.Started)
            .RespondWith(Response.Create().WithStatusCode(200)
                .WithBody(JsonSerializer.Serialize(countdownInstance)))
            .WillSetStateTo("reset");

        _mockInternalApi
            .Given(Request.Create().WithPath("/instances/list").UsingGet())
            .InScenario("cancel-countdown")
            .WhenStateIs("reset")
            .RespondWith(Response.Create().WithStatusCode(200)
                .WithBody(JsonSerializer.Serialize(resetInstance)));

        // Mock enforcement cancellation
        var cancelRequest = new
        {
            instanceId = TestInstanceId,
            reason = "Moderator decided to keep instance open"
        };

        _mockInternalApi
            .Given(Request.Create()
                .WithPath("/enforcement/cancel")
                .UsingPost()
                .WithBody(JsonSerializer.Serialize(cancelRequest)))
            .RespondWith(Response.Create().WithStatusCode(200)
                .WithBody("{\"message\":\"Closure cancelled successfully\"}"));

        // Act - This will fail because ManualInstanceManager doesn't exist yet
        var instanceManager = new ManualInstanceManager(_vrchatClient, _internalClient);
        
        // Step 1: Check countdown status
        var beforeCancel = await instanceManager.GetInstanceInfoAsync(TestInstanceId);
        
        // Step 2: Cancel the pending closure
        var cancellationResult = await instanceManager.CancelPendingClosureAsync(TestInstanceId, "Moderator decided to keep instance open");
        
        // Step 3: Verify instance reset to active
        var afterCancel = await instanceManager.GetInstanceInfoAsync(TestInstanceId);

        // Assert
        Assert.NotNull(beforeCancel);
        Assert.Equal("ClosingCountdown", beforeCancel.Status);
        Assert.Equal(150, beforeCancel.CountdownSeconds);

        Assert.NotNull(cancellationResult);
        Assert.True(cancellationResult.Success);
        Assert.Equal(TestInstanceId, cancellationResult.InstanceId);
        Assert.Contains("cancelled", cancellationResult.Message);

        Assert.NotNull(afterCancel);
        Assert.Equal("Active", afterCancel.Status);
        Assert.Null(afterCancel.CountdownSeconds);
    }

    [Fact]
    public async Task ViewInstanceDetails_ShouldProvideComprehensiveInformation()
    {
        // Arrange - User wants detailed information about instance
        var detailedInstance = new[]
        {
            new
            {
                instanceId = TestInstanceId,
                worldName = "Detailed Info World",
                worldId = "wrld_manual-test",
                instanceType = "GroupPlus",
                ageGated = false,
                userCount = 12,
                maxUsers = 16,
                region = "eu",
                status = "Active",
                countdownSeconds = (int?)null,
                createdAt = "2024-01-15T10:00:00.000Z",
                lastUpdated = "2024-01-15T14:30:00.000Z"
            }
        };

        _mockInternalApi
            .Given(Request.Create().WithPath("/instances/list").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200)
                .WithBody(JsonSerializer.Serialize(detailedInstance)));

        // Mock VRChat API for additional world details (if needed)
        var worldDetails = new
        {
            id = "wrld_manual-test",
            name = "Detailed Info World",
            authorName = "World Creator",
            description = "A test world for manual management",
            tags = new[] { "roleplay", "hangout", "world" },
            imageUrl = "https://vrchat.com/api/1/image/file_12345",
            releaseStatus = "public",
            capacity = 16,
            recommendedCapacity = 8
        };

        // Act - This will fail because ManualInstanceManager doesn't exist yet
        var instanceManager = new ManualInstanceManager(_vrchatClient, _internalClient);
        
        var instanceInfo = await instanceManager.GetInstanceInfoAsync(TestInstanceId);
        var instanceDetails = await instanceManager.GetDetailedInstanceInfoAsync(TestInstanceId);

        // Assert
        Assert.NotNull(instanceInfo);
        Assert.Equal("Detailed Info World", instanceInfo.WorldName);
        Assert.Equal("GroupPlus", instanceInfo.InstanceType);
        Assert.Equal(12, instanceInfo.UserCount);
        Assert.Equal(16, instanceInfo.MaxUsers);
        Assert.Equal("eu", instanceInfo.Region);

        Assert.NotNull(instanceDetails);
        Assert.Equal(TestInstanceId, instanceDetails.InstanceId);
        Assert.Equal("Active", instanceDetails.Status);
        Assert.False(instanceDetails.AgeGated);
        Assert.True(instanceDetails.IsNearCapacity); // 12/16 = 75% > threshold
        Assert.Contains("2024-01-15", instanceDetails.CreatedAt.ToString("yyyy-MM-dd"));
    }

    [Fact]
    public async Task ListManageableInstances_ShouldFilterByPermissions()
    {
        // Arrange - User can see all instances but can only manage some
        var allInstances = new[]
        {
            new
            {
                instanceId = "wrld_manageable1:111~group(grp_12345678-1234-1234-1234-123456789012)",
                worldName = "Manageable World 1",
                instanceType = "Group",
                ageGated = false,
                userCount = 5,
                maxUsers = 20,
                status = "Active",
                countdownSeconds = (int?)null,
                createdAt = "2024-01-15T14:00:00.000Z",
                lastUpdated = "2024-01-15T14:30:00.000Z"
            },
            new
            {
                instanceId = "wrld_manageable2:222~group(grp_12345678-1234-1234-1234-123456789012)",
                worldName = "Manageable World 2",
                instanceType = "GroupPlus",
                ageGated = true,
                userCount = 8,
                maxUsers = 16,
                status = "ClosingCountdown",
                countdownSeconds = 120,
                createdAt = "2024-01-15T14:00:00.000Z",
                lastUpdated = "2024-01-15T14:30:00.000Z"
            },
            new
            {
                instanceId = "wrld_readonly:333~group(grp_12345678-1234-1234-1234-123456789012)",
                worldName = "Read Only World",
                instanceType = "Group",
                ageGated = false,
                userCount = 15,
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
                .WithBody(JsonSerializer.Serialize(allInstances)));

        // Mock permissions check
        var userPermissions = new
        {
            permissions = new[] { "group-instance-moderate", "group-member-moderate" }
            // Missing "group-instance-manage" for full management
        };

        _mockVrchatApi
            .Given(Request.Create().WithPath($"/api/1/groups/{TestGroupId}/permissions").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200)
                .WithBody(JsonSerializer.Serialize(userPermissions)));

        // Act - This will fail because ManualInstanceManager doesn't exist yet
        var instanceManager = new ManualInstanceManager(_vrchatClient, _internalClient);
        
        var allInstancesList = await instanceManager.GetAllInstancesAsync();
        var manageableInstances = await instanceManager.GetManageableInstancesAsync();
        var userPerms = await instanceManager.GetUserPermissionsAsync(TestGroupId);

        // Assert
        Assert.NotNull(allInstancesList);
        Assert.Equal(3, allInstancesList.Count);

        Assert.NotNull(manageableInstances);
        Assert.Equal(2, manageableInstances.Count); // Can moderate 2 out of 3
        Assert.Contains(manageableInstances, i => i.WorldName == "Manageable World 1");
        Assert.Contains(manageableInstances, i => i.WorldName == "Manageable World 2");
        Assert.DoesNotContain(manageableInstances, i => i.WorldName == "Read Only World");

        Assert.NotNull(userPerms);
        Assert.True(userPerms.CanModerate);
        Assert.False(userPerms.CanFullyManage); // Missing full management permission
        Assert.Contains("group-instance-moderate", userPerms.Permissions);
    }

    [Fact]
    public async Task BulkInstanceOperations_ShouldHandleMultipleInstancesEfficiently()
    {
        // Arrange - User wants to perform bulk operations on multiple instances
        var bulkInstances = new[]
        {
            "wrld_bulk1:111~group(grp_12345678-1234-1234-1234-123456789012)",
            "wrld_bulk2:222~group(grp_12345678-1234-1234-1234-123456789012)",
            "wrld_bulk3:333~group(grp_12345678-1234-1234-1234-123456789012)"
        };

        // Mock successful closure for first two instances
        foreach (var instanceId in bulkInstances.Take(2))
        {
            var parts = instanceId.Split(':')[0].Replace("wrld_", "");
            _mockVrchatApi
                .Given(Request.Create().WithPath($"/api/1/instances/{instanceId.Split('~')[0]}").UsingDelete())
                .RespondWith(Response.Create().WithStatusCode(200)
                    .WithBody("{\"message\":\"Instance closed successfully\"}"));
        }

        // Mock failure for third instance (insufficient permissions)
        _mockVrchatApi
            .Given(Request.Create().WithPath("/api/1/instances/wrld_bulk3:333").UsingDelete())
            .RespondWith(Response.Create().WithStatusCode(403)
                .WithBody("{\"error\":\"Insufficient permissions\"}"));

        foreach (var instanceId in bulkInstances)
        {
            var closeRequest = new { instanceId = instanceId, reason = "Bulk closure operation" };
            var statusCode = instanceId.Contains("bulk3") ? 403 : 200;
            var responseBody = instanceId.Contains("bulk3") 
                ? "{\"error\":\"Insufficient permissions\"}"
                : "{\"message\":\"Instance closed successfully\"}";

            _mockInternalApi
                .Given(Request.Create().WithPath("/instances/close").UsingPost()
                    .WithBody(JsonSerializer.Serialize(closeRequest)))
                .RespondWith(Response.Create().WithStatusCode(statusCode)
                    .WithBody(responseBody));
        }

        // Act - This will fail because ManualInstanceManager doesn't exist yet
        var instanceManager = new ManualInstanceManager(_vrchatClient, _internalClient);
        
        var bulkResult = await instanceManager.BulkCloseInstancesAsync(bulkInstances, "Bulk closure operation");

        // Assert
        Assert.NotNull(bulkResult);
        Assert.Equal(3, bulkResult.TotalAttempted);
        Assert.Equal(2, bulkResult.SuccessfulClosures);
        Assert.Equal(1, bulkResult.FailedClosures);
        
        Assert.Contains(bulkResult.SuccessfulInstances, id => id.Contains("bulk1"));
        Assert.Contains(bulkResult.SuccessfulInstances, id => id.Contains("bulk2"));
        Assert.Contains(bulkResult.FailedInstances.Keys, id => id.Contains("bulk3"));
        Assert.Contains("Insufficient permissions", bulkResult.FailedInstances.Values.First());
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
public class ManualInstanceManager
{
    private readonly HttpClient _vrchatClient;
    private readonly HttpClient _internalClient;

    public ManualInstanceManager(HttpClient vrchatClient, HttpClient internalClient)
    {
        _vrchatClient = vrchatClient;
        _internalClient = internalClient;
    }

    public async Task<InstanceInfo> GetInstanceInfoAsync(string instanceId)
    {
        throw new NotImplementedException("ManualInstanceManager.GetInstanceInfoAsync not implemented yet");
    }

    public async Task<ManualClosureResult> CloseInstanceAsync(string instanceId, string reason)
    {
        throw new NotImplementedException("ManualInstanceManager.CloseInstanceAsync not implemented yet");
    }

    public async Task<CancellationResult> CancelPendingClosureAsync(string instanceId, string reason)
    {
        throw new NotImplementedException("ManualInstanceManager.CancelPendingClosureAsync not implemented yet");
    }

    public async Task<DetailedInstanceInfo> GetDetailedInstanceInfoAsync(string instanceId)
    {
        throw new NotImplementedException("ManualInstanceManager.GetDetailedInstanceInfoAsync not implemented yet");
    }

    public async Task<List<InstanceInfo>> GetAllInstancesAsync()
    {
        throw new NotImplementedException("ManualInstanceManager.GetAllInstancesAsync not implemented yet");
    }

    public async Task<List<InstanceInfo>> GetManageableInstancesAsync()
    {
        throw new NotImplementedException("ManualInstanceManager.GetManageableInstancesAsync not implemented yet");
    }

    public async Task<UserPermissions> GetUserPermissionsAsync(string groupId)
    {
        throw new NotImplementedException("ManualInstanceManager.GetUserPermissionsAsync not implemented yet");
    }

    public async Task<BulkOperationResult> BulkCloseInstancesAsync(string[] instanceIds, string reason)
    {
        throw new NotImplementedException("ManualInstanceManager.BulkCloseInstancesAsync not implemented yet");
    }
}

public class InstanceInfo
{
    public string InstanceId { get; set; } = string.Empty;
    public string WorldName { get; set; } = string.Empty;
    public string InstanceType { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int UserCount { get; set; }
    public int MaxUsers { get; set; }
    public int? CountdownSeconds { get; set; }
    public string Region { get; set; } = string.Empty;
    public bool AgeGated { get; set; }
}

public class ManualClosureResult
{
    public bool Success { get; set; }
    public string InstanceId { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public bool UsedCountdown { get; set; }
}

public class DetailedInstanceInfo
{
    public string InstanceId { get; set; } = string.Empty;
    public string WorldName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public bool AgeGated { get; set; }
    public bool IsNearCapacity { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class UserPermissions
{
    public bool CanModerate { get; set; }
    public bool CanFullyManage { get; set; }
    public List<string> Permissions { get; set; } = new();
}

public class BulkOperationResult
{
    public int TotalAttempted { get; set; }
    public int SuccessfulClosures { get; set; }
    public int FailedClosures { get; set; }
    public List<string> SuccessfulInstances { get; set; } = new();
    public Dictionary<string, string> FailedInstances { get; set; } = new();
}