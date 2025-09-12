using System.Net;
using System.Text.Json;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using Xunit;

namespace VrcGroupGuardian.Tests.Contract.InternalApiTests;

public class InstanceServiceTests : IDisposable
{
    private readonly WireMockServer _mockServer;
    private readonly HttpClient _httpClient;

    public InstanceServiceTests()
    {
        _mockServer = WireMockServer.Start();
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(_mockServer.Urls[0])
        };
    }

    [Fact]
    public async Task ListInstances_WithValidAuth_ReturnsInstanceList()
    {
        // Arrange - Mock internal Instance service response
        var expectedInstances = new[]
        {
            new
            {
                instanceId = "wrld_12345678-1234-1234-1234-123456789012:12345~group(grp_12345678-1234-1234-1234-123456789012)",
                worldName = "Test World",
                worldId = "wrld_12345678-1234-1234-1234-123456789012",
                instanceType = "Group",
                ageGated = false,
                userCount = 5,
                maxUsers = 20,
                region = "us",
                createdAt = "2024-01-15T10:30:00.000Z",
                status = "Active",
                countdownSeconds = (int?)null,
                lastUpdated = "2024-01-15T14:30:00.000Z"
            },
            new
            {
                instanceId = "wrld_87654321-4321-4321-4321-210987654321:67890~groupplus(grp_12345678-1234-1234-1234-123456789012)",
                worldName = "Another World",
                worldId = "wrld_87654321-4321-4321-4321-210987654321",
                instanceType = "GroupPlus",
                ageGated = true,
                userCount = 8,
                maxUsers = 16,
                region = "eu",
                createdAt = "2024-01-15T11:15:00.000Z",
                status = "ClosingCountdown",
                countdownSeconds = 120,
                lastUpdated = "2024-01-15T14:32:00.000Z"
            }
        };

        _mockServer
            .Given(Request.Create()
                .WithPath("/instances/list")
                .UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody(JsonSerializer.Serialize(expectedInstances)));

        // Act - This will fail because InstanceServiceClient doesn't exist yet
        var instanceService = new InstanceServiceClient(_httpClient);
        
        var result = await instanceService.ListInstancesAsync();

        // Assert
        Assert.NotNull(result);
        Assert.Equal(2, result.Count);
        
        var firstInstance = result[0];
        Assert.Equal(expectedInstances[0].instanceId, firstInstance.InstanceId);
        Assert.Equal(expectedInstances[0].worldName, firstInstance.WorldName);
        Assert.Equal(expectedInstances[0].userCount, firstInstance.UserCount);
        Assert.Equal("Active", firstInstance.Status);
        Assert.Null(firstInstance.CountdownSeconds);

        var secondInstance = result[1];
        Assert.Equal("ClosingCountdown", secondInstance.Status);
        Assert.Equal(120, secondInstance.CountdownSeconds);
    }

    [Fact]
    public async Task ListInstances_WithNoInstances_ReturnsEmptyList()
    {
        // Arrange
        _mockServer
            .Given(Request.Create()
                .WithPath("/instances/list")
                .UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody("[]"));

        // Act - This will fail because InstanceServiceClient doesn't exist yet
        var instanceService = new InstanceServiceClient(_httpClient);
        
        var result = await instanceService.ListInstancesAsync();

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public async Task CloseInstance_WithValidInstanceId_ReturnsSuccess()
    {
        // Arrange
        var instanceId = "wrld_12345678-1234-1234-1234-123456789012:12345~group(grp_12345678-1234-1234-1234-123456789012)";
        var reason = "Policy violation detected";
        
        var closeRequest = new
        {
            instanceId = instanceId,
            reason = reason
        };

        _mockServer
            .Given(Request.Create()
                .WithPath("/instances/close")
                .UsingPost()
                .WithHeader("Content-Type", "application/json")
                .WithBody(JsonSerializer.Serialize(closeRequest)))
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody("{\"message\":\"Instance closed successfully\"}"));

        // Act - This will fail because InstanceServiceClient doesn't exist yet
        var instanceService = new InstanceServiceClient(_httpClient);
        
        var success = await instanceService.CloseInstanceAsync(instanceId, reason);

        // Assert
        Assert.True(success);
    }

    [Fact]
    public async Task CloseInstance_WithDefaultReason_ReturnsSuccess()
    {
        // Arrange
        var instanceId = "wrld_12345678-1234-1234-1234-123456789012:12345~group(grp_12345678-1234-1234-1234-123456789012)";
        
        var closeRequest = new
        {
            instanceId = instanceId,
            reason = "Manual closure by moderator"
        };

        _mockServer
            .Given(Request.Create()
                .WithPath("/instances/close")
                .UsingPost()
                .WithBody(JsonSerializer.Serialize(closeRequest)))
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody("{\"message\":\"Instance closed successfully\"}"));

        // Act - This will fail because InstanceServiceClient doesn't exist yet
        var instanceService = new InstanceServiceClient(_httpClient);
        
        var success = await instanceService.CloseInstanceAsync(instanceId);

        // Assert
        Assert.True(success);
    }

    [Fact]
    public async Task CloseInstance_WithInsufficientPermissions_Returns403()
    {
        // Arrange
        var instanceId = "wrld_12345678-1234-1234-1234-123456789012:12345~group(grp_12345678-1234-1234-1234-123456789012)";
        var reason = "Policy violation";
        
        var closeRequest = new
        {
            instanceId = instanceId,
            reason = reason
        };

        _mockServer
            .Given(Request.Create()
                .WithPath("/instances/close")
                .UsingPost()
                .WithBody(JsonSerializer.Serialize(closeRequest)))
            .RespondWith(Response.Create()
                .WithStatusCode(403)
                .WithHeader("Content-Type", "application/json")
                .WithBody("{\"error\":\"Insufficient permissions\"}"));

        // Act & Assert - This will fail because InstanceServiceClient doesn't exist yet
        var instanceService = new InstanceServiceClient(_httpClient);
        
        var exception = await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => instanceService.CloseInstanceAsync(instanceId, reason));
        
        Assert.Contains("Insufficient permissions", exception.Message);
    }

    [Fact]
    public async Task CloseInstance_WithNonExistentInstance_Returns404()
    {
        // Arrange
        var invalidInstanceId = "wrld_invalid-instance:99999~group(grp_invalid)";
        
        var closeRequest = new
        {
            instanceId = invalidInstanceId,
            reason = "Manual closure by moderator"
        };

        _mockServer
            .Given(Request.Create()
                .WithPath("/instances/close")
                .UsingPost()
                .WithBody(JsonSerializer.Serialize(closeRequest)))
            .RespondWith(Response.Create()
                .WithStatusCode(404)
                .WithHeader("Content-Type", "application/json")
                .WithBody("{\"error\":\"Instance not found\"}"));

        // Act & Assert - This will fail because InstanceServiceClient doesn't exist yet
        var instanceService = new InstanceServiceClient(_httpClient);
        
        var exception = await Assert.ThrowsAsync<HttpRequestException>(
            () => instanceService.CloseInstanceAsync(invalidInstanceId));
        
        Assert.Contains("Instance not found", exception.Message);
    }

    [Theory]
    [InlineData("Active")]
    [InlineData("Flagged")]
    [InlineData("ClosingCountdown")]
    [InlineData("Closed")]
    public async Task ListInstances_WithDifferentStatuses_ReturnsCorrectStatus(string status)
    {
        // Arrange
        var instance = new
        {
            instanceId = "wrld_12345678-1234-1234-1234-123456789012:12345~group(grp_12345678-1234-1234-1234-123456789012)",
            worldName = "Test World",
            worldId = "wrld_12345678-1234-1234-1234-123456789012",
            instanceType = "Group",
            ageGated = false,
            userCount = 5,
            maxUsers = 20,
            region = "us",
            createdAt = "2024-01-15T10:30:00.000Z",
            status = status,
            countdownSeconds = status == "ClosingCountdown" ? 180 : (int?)null,
            lastUpdated = "2024-01-15T14:30:00.000Z"
        };

        _mockServer
            .Given(Request.Create()
                .WithPath("/instances/list")
                .UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody(JsonSerializer.Serialize(new[] { instance })));

        // Act - This will fail because InstanceServiceClient doesn't exist yet
        var instanceService = new InstanceServiceClient(_httpClient);
        
        var result = await instanceService.ListInstancesAsync();

        // Assert
        Assert.NotNull(result);
        Assert.Single(result);
        Assert.Equal(status, result[0].Status);
        
        if (status == "ClosingCountdown")
        {
            Assert.Equal(180, result[0].CountdownSeconds);
        }
        else
        {
            Assert.Null(result[0].CountdownSeconds);
        }
    }

    public void Dispose()
    {
        _httpClient?.Dispose();
        _mockServer?.Stop();
        _mockServer?.Dispose();
    }
}

// Placeholder classes that will fail compilation - this is intentional for TDD
public class InstanceServiceClient
{
    private readonly HttpClient _httpClient;

    public InstanceServiceClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<List<InstanceInfoDto>> ListInstancesAsync()
    {
        throw new NotImplementedException("InstanceServiceClient.ListInstancesAsync not implemented yet");
    }

    public async Task<bool> CloseInstanceAsync(string instanceId, string? reason = null)
    {
        throw new NotImplementedException("InstanceServiceClient.CloseInstanceAsync not implemented yet");
    }
}

public class InstanceInfoDto
{
    public string InstanceId { get; set; } = string.Empty;
    public string WorldName { get; set; } = string.Empty;
    public string WorldId { get; set; } = string.Empty;
    public string InstanceType { get; set; } = string.Empty;
    public bool AgeGated { get; set; }
    public int UserCount { get; set; }
    public int MaxUsers { get; set; }
    public string Region { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public string Status { get; set; } = string.Empty;
    public int? CountdownSeconds { get; set; }
    public DateTime LastUpdated { get; set; }
}