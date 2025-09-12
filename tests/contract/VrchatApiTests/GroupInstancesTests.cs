using System.Net;
using System.Text.Json;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using Xunit;

namespace VrcGroupGuardian.Tests.Contract.VrchatApiTests;

public class GroupInstancesTests : IDisposable
{
    private readonly WireMockServer _mockServer;
    private readonly HttpClient _httpClient;
    private const string TestGroupId = "grp_12345678-1234-1234-1234-123456789012";

    public GroupInstancesTests()
    {
        _mockServer = WireMockServer.Start();
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(_mockServer.Urls[0])
        };
    }

    [Fact]
    public async Task GetGroupInstances_WithValidGroupId_ReturnsInstanceList()
    {
        // Arrange - Mock VRChat API response
        var expectedInstances = new[]
        {
            new
            {
                instanceId = "wrld_12345678-1234-1234-1234-123456789012:12345~group(grp_12345678-1234-1234-1234-123456789012)",
                world = new
                {
                    id = "wrld_12345678-1234-1234-1234-123456789012",
                    name = "Test World",
                    authorName = "World Author"
                },
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
                world = new
                {
                    id = "wrld_87654321-4321-4321-4321-210987654321",
                    name = "Another Test World",
                    authorName = "Another Author"
                },
                type = "groupplus",
                ageGate = true,
                userCount = 12,
                capacity = 16,
                region = "eu",
                createdAt = "2024-01-15T11:15:00.000Z"
            }
        };

        _mockServer
            .Given(Request.Create()
                .WithPath($"/api/1/groups/{TestGroupId}/instances")
                .UsingGet()
                .WithHeader("Cookie", "authToken=valid-token"))
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody(JsonSerializer.Serialize(expectedInstances)));

        // Act - This will fail because VrcApiService doesn't exist yet
        var vrcApiService = new VrcApiService(_httpClient);
        _httpClient.DefaultRequestHeaders.Add("Cookie", "authToken=valid-token");
        
        var result = await vrcApiService.GetGroupInstancesAsync(TestGroupId);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(2, result.Count);
        
        var firstInstance = result[0];
        Assert.Equal(expectedInstances[0].instanceId, firstInstance.InstanceId);
        Assert.Equal(expectedInstances[0].world.name, firstInstance.WorldName);
        Assert.Equal(expectedInstances[0].userCount, firstInstance.UserCount);
        Assert.Equal(expectedInstances[0].capacity, firstInstance.Capacity);
        Assert.Equal("Group", firstInstance.InstanceType);
        Assert.False(firstInstance.AgeGated);
    }

    [Fact]
    public async Task GetGroupInstances_WithInvalidGroupId_Returns404()
    {
        // Arrange
        var invalidGroupId = "grp_invalid-group-id";
        
        _mockServer
            .Given(Request.Create()
                .WithPath($"/api/1/groups/{invalidGroupId}/instances")
                .UsingGet()
                .WithHeader("Cookie", "authToken=valid-token"))
            .RespondWith(Response.Create()
                .WithStatusCode(404)
                .WithHeader("Content-Type", "application/json")
                .WithBody("{\"error\":\"Group not found\"}"));

        // Act & Assert - This will fail because VrcApiService doesn't exist yet
        var vrcApiService = new VrcApiService(_httpClient);
        _httpClient.DefaultRequestHeaders.Add("Cookie", "authToken=valid-token");
        
        var exception = await Assert.ThrowsAsync<HttpRequestException>(
            () => vrcApiService.GetGroupInstancesAsync(invalidGroupId));
        
        Assert.Contains("Group not found", exception.Message);
    }

    [Fact]
    public async Task GetGroupInstances_WithInsufficientPermissions_Returns403()
    {
        // Arrange
        _mockServer
            .Given(Request.Create()
                .WithPath($"/api/1/groups/{TestGroupId}/instances")
                .UsingGet()
                .WithHeader("Cookie", "authToken=limited-token"))
            .RespondWith(Response.Create()
                .WithStatusCode(403)
                .WithHeader("Content-Type", "application/json")
                .WithBody("{\"error\":\"Insufficient permissions to view group instances\"}"));

        // Act & Assert - This will fail because VrcApiService doesn't exist yet
        var vrcApiService = new VrcApiService(_httpClient);
        _httpClient.DefaultRequestHeaders.Add("Cookie", "authToken=limited-token");
        
        var exception = await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => vrcApiService.GetGroupInstancesAsync(TestGroupId));
        
        Assert.Contains("Insufficient permissions", exception.Message);
    }

    [Fact]
    public async Task GetGroupInstances_WithRateLimit_Returns429()
    {
        // Arrange
        _mockServer
            .Given(Request.Create()
                .WithPath($"/api/1/groups/{TestGroupId}/instances")
                .UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(429)
                .WithHeader("Content-Type", "application/json")
                .WithBody("{\"error\":\"Rate limit exceeded\"}"));

        // Act & Assert - This will fail because VrcApiService doesn't exist yet
        var vrcApiService = new VrcApiService(_httpClient);
        
        var exception = await Assert.ThrowsAsync<HttpRequestException>(
            () => vrcApiService.GetGroupInstancesAsync(TestGroupId));
        
        Assert.Contains("Rate limit exceeded", exception.Message);
    }

    [Fact]
    public async Task GetGroupInstances_WithEmptyResponse_ReturnsEmptyList()
    {
        // Arrange
        _mockServer
            .Given(Request.Create()
                .WithPath($"/api/1/groups/{TestGroupId}/instances")
                .UsingGet()
                .WithHeader("Cookie", "authToken=valid-token"))
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody("[]"));

        // Act - This will fail because VrcApiService doesn't exist yet
        var vrcApiService = new VrcApiService(_httpClient);
        _httpClient.DefaultRequestHeaders.Add("Cookie", "authToken=valid-token");
        
        var result = await vrcApiService.GetGroupInstancesAsync(TestGroupId);

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    public void Dispose()
    {
        _httpClient?.Dispose();
        _mockServer?.Stop();
        _mockServer?.Dispose();
    }
}

// Placeholder DTO classes that will fail compilation - this is intentional for TDD
public class GroupInstanceDto
{
    public string InstanceId { get; set; } = string.Empty;
    public string WorldName { get; set; } = string.Empty;
    public string WorldId { get; set; } = string.Empty;
    public string InstanceType { get; set; } = string.Empty;
    public bool AgeGated { get; set; }
    public int UserCount { get; set; }
    public int Capacity { get; set; }
    public string Region { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}