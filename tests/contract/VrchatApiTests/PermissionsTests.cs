using System.Net;
using System.Text.Json;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using Xunit;

namespace VrcGroupGuardian.Tests.Contract.VrchatApiTests;

public class PermissionsTests : IDisposable
{
    private readonly WireMockServer _mockServer;
    private readonly HttpClient _httpClient;
    private const string TestGroupId = "grp_12345678-1234-1234-1234-123456789012";

    public PermissionsTests()
    {
        _mockServer = WireMockServer.Start();
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(_mockServer.Urls[0])
        };
    }

    [Fact]
    public async Task GetGroupPermissions_WithValidGroupId_ReturnsPermissions()
    {
        // Arrange - Mock VRChat API response for group permissions
        var expectedPermissions = new
        {
            permissions = new[]
            {
                "group-instance-moderate",
                "group-instance-manage", 
                "group-member-moderate",
                "group-audit-view"
            }
        };

        _mockServer
            .Given(Request.Create()
                .WithPath($"/api/1/groups/{TestGroupId}/permissions")
                .UsingGet()
                .WithHeader("Cookie", "authToken=valid-token"))
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody(JsonSerializer.Serialize(expectedPermissions)));

        // Act - This will fail because VrcApiService doesn't exist yet
        var vrcApiService = new VrcApiService(_httpClient);
        _httpClient.DefaultRequestHeaders.Add("Cookie", "authToken=valid-token");
        
        var result = await vrcApiService.GetGroupPermissionsAsync(TestGroupId);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(4, result.Permissions.Count);
        Assert.Contains("group-instance-moderate", result.Permissions);
        Assert.Contains("group-instance-manage", result.Permissions);
        Assert.Contains("group-member-moderate", result.Permissions);
        Assert.Contains("group-audit-view", result.Permissions);
    }

    [Fact]
    public async Task GetGroupPermissions_WithModeratorRole_ReturnsModeratorPermissions()
    {
        // Arrange - Moderator has limited permissions
        var moderatorPermissions = new
        {
            permissions = new[]
            {
                "group-instance-moderate",
                "group-member-moderate"
            }
        };

        _mockServer
            .Given(Request.Create()
                .WithPath($"/api/1/groups/{TestGroupId}/permissions")
                .UsingGet()
                .WithHeader("Cookie", "authToken=moderator-token"))
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody(JsonSerializer.Serialize(moderatorPermissions)));

        // Act - This will fail because VrcApiService doesn't exist yet
        var vrcApiService = new VrcApiService(_httpClient);
        _httpClient.DefaultRequestHeaders.Add("Cookie", "authToken=moderator-token");
        
        var result = await vrcApiService.GetGroupPermissionsAsync(TestGroupId);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(2, result.Permissions.Count);
        Assert.Contains("group-instance-moderate", result.Permissions);
        Assert.Contains("group-member-moderate", result.Permissions);
        Assert.DoesNotContain("group-instance-manage", result.Permissions);
    }

    [Fact]
    public async Task GetGroupPermissions_WithMemberRole_ReturnsLimitedPermissions()
    {
        // Arrange - Regular members have minimal permissions
        var memberPermissions = new
        {
            permissions = new string[] { }
        };

        _mockServer
            .Given(Request.Create()
                .WithPath($"/api/1/groups/{TestGroupId}/permissions")
                .UsingGet()
                .WithHeader("Cookie", "authToken=member-token"))
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody(JsonSerializer.Serialize(memberPermissions)));

        // Act - This will fail because VrcApiService doesn't exist yet
        var vrcApiService = new VrcApiService(_httpClient);
        _httpClient.DefaultRequestHeaders.Add("Cookie", "authToken=member-token");
        
        var result = await vrcApiService.GetGroupPermissionsAsync(TestGroupId);

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result.Permissions);
    }

    [Fact]
    public async Task GetGroupPermissions_WithInvalidGroupId_Returns404()
    {
        // Arrange
        var invalidGroupId = "grp_invalid-group-id";
        
        _mockServer
            .Given(Request.Create()
                .WithPath($"/api/1/groups/{invalidGroupId}/permissions")
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
            () => vrcApiService.GetGroupPermissionsAsync(invalidGroupId));
        
        Assert.Contains("Group not found", exception.Message);
    }

    [Fact]
    public async Task GetGroupPermissions_WithoutAuthentication_Returns401()
    {
        // Arrange
        _mockServer
            .Given(Request.Create()
                .WithPath($"/api/1/groups/{TestGroupId}/permissions")
                .UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(401)
                .WithHeader("Content-Type", "application/json")
                .WithBody("{\"error\":\"Authentication required\"}"));

        // Act & Assert - This will fail because VrcApiService doesn't exist yet
        var vrcApiService = new VrcApiService(_httpClient);
        
        var exception = await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => vrcApiService.GetGroupPermissionsAsync(TestGroupId));
        
        Assert.Contains("Authentication required", exception.Message);
    }

    [Fact]
    public async Task GetGroupPermissions_WithExpiredToken_Returns401()
    {
        // Arrange
        _mockServer
            .Given(Request.Create()
                .WithPath($"/api/1/groups/{TestGroupId}/permissions")
                .UsingGet()
                .WithHeader("Cookie", "authToken=expired-token"))
            .RespondWith(Response.Create()
                .WithStatusCode(401)
                .WithHeader("Content-Type", "application/json")
                .WithBody("{\"error\":\"Token expired\"}"));

        // Act & Assert - This will fail because VrcApiService doesn't exist yet
        var vrcApiService = new VrcApiService(_httpClient);
        _httpClient.DefaultRequestHeaders.Add("Cookie", "authToken=expired-token");
        
        var exception = await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => vrcApiService.GetGroupPermissionsAsync(TestGroupId));
        
        Assert.Contains("Token expired", exception.Message);
    }

    [Fact]
    public async Task GetGroupAuditLogs_WithValidGroupId_ReturnsAuditLogs()
    {
        // Arrange - Mock audit logs response
        var expectedAuditLogs = new[]
        {
            new
            {
                id = "audit_12345678-1234-1234-1234-123456789012",
                createdAt = "2024-01-15T14:30:00.000Z",
                eventType = "group.instance.kick",
                actorId = "usr_87654321-4321-4321-4321-210987654321",
                actorDisplayName = "Moderator User",
                targetId = "usr_11111111-1111-1111-1111-111111111111",
                targetDisplayName = "Problem User",
                data = new { reason = "Disruptive behavior" }
            },
            new
            {
                id = "audit_87654321-4321-4321-4321-210987654321",
                createdAt = "2024-01-15T13:15:00.000Z",
                eventType = "group.instance.close",
                actorId = "usr_87654321-4321-4321-4321-210987654321",
                actorDisplayName = "Moderator User",
                targetId = "wrld_12345678-1234-1234-1234-123456789012:12345",
                targetDisplayName = "Test World Instance",
                data = new { reason = "Policy violation" }
            }
        };

        _mockServer
            .Given(Request.Create()
                .WithPath($"/api/1/groups/{TestGroupId}/auditLogs")
                .UsingGet()
                .WithParam("n", "60")
                .WithParam("offset", "0")
                .WithHeader("Cookie", "authToken=valid-token"))
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody(JsonSerializer.Serialize(expectedAuditLogs)));

        // Act - This will fail because VrcApiService doesn't exist yet
        var vrcApiService = new VrcApiService(_httpClient);
        _httpClient.DefaultRequestHeaders.Add("Cookie", "authToken=valid-token");
        
        var result = await vrcApiService.GetGroupAuditLogsAsync(TestGroupId, limit: 60, offset: 0);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(2, result.Count);
        
        var firstLog = result[0];
        Assert.Equal(expectedAuditLogs[0].id, firstLog.Id);
        Assert.Equal(expectedAuditLogs[0].eventType, firstLog.EventType);
        Assert.Equal(expectedAuditLogs[0].actorDisplayName, firstLog.ActorDisplayName);
    }

    public void Dispose()
    {
        _httpClient?.Dispose();
        _mockServer?.Stop();
        _mockServer?.Dispose();
    }
}

// Placeholder DTO classes that will fail compilation - this is intentional for TDD
public class GroupPermissionsDto
{
    public List<string> Permissions { get; set; } = new();
}

public class AuditLogDto
{
    public string Id { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string ActorId { get; set; } = string.Empty;
    public string ActorDisplayName { get; set; } = string.Empty;
    public string TargetId { get; set; } = string.Empty;
    public string TargetDisplayName { get; set; } = string.Empty;
    public object? Data { get; set; }
}