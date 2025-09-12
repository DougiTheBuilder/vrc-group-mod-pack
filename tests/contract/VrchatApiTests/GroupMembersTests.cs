using System.Net;
using System.Text.Json;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using Xunit;

namespace VrcGroupGuardian.Tests.Contract.VrchatApiTests;

public class GroupMembersTests : IDisposable
{
    private readonly WireMockServer _mockServer;
    private readonly HttpClient _httpClient;
    private const string TestGroupId = "grp_12345678-1234-1234-1234-123456789012";
    private const string TestUserId = "usr_87654321-4321-4321-4321-210987654321";

    public GroupMembersTests()
    {
        _mockServer = WireMockServer.Start();
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(_mockServer.Urls[0])
        };
    }

    [Fact]
    public async Task GetGroupMembers_WithValidGroupId_ReturnsMemberList()
    {
        // Arrange - Mock VRChat API response
        var expectedMembers = new[]
        {
            new
            {
                id = "usr_12345678-1234-1234-1234-123456789012",
                displayName = "Test Member 1",
                username = "testmember1",
                roleId = "grol_moderator",
                roleName = "Moderator",
                joinedAt = "2024-01-10T09:00:00.000Z",
                lastActivity = "2024-01-15T14:30:00.000Z"
            },
            new
            {
                id = "usr_87654321-4321-4321-4321-210987654321",
                displayName = "Test Member 2", 
                username = "testmember2",
                roleId = "grol_member",
                roleName = "Member",
                joinedAt = "2024-01-12T11:15:00.000Z",
                lastActivity = "2024-01-15T13:45:00.000Z"
            }
        };

        _mockServer
            .Given(Request.Create()
                .WithPath($"/api/1/groups/{TestGroupId}/members")
                .UsingGet()
                .WithParam("n", "60")
                .WithParam("offset", "0")
                .WithHeader("Cookie", "authToken=valid-token"))
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody(JsonSerializer.Serialize(expectedMembers)));

        // Act - This will fail because VrcApiService doesn't exist yet
        var vrcApiService = new VrcApiService(_httpClient);
        _httpClient.DefaultRequestHeaders.Add("Cookie", "authToken=valid-token");
        
        var result = await vrcApiService.GetGroupMembersAsync(TestGroupId, limit: 60, offset: 0);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(2, result.Count);
        
        var firstMember = result[0];
        Assert.Equal(expectedMembers[0].id, firstMember.UserId);
        Assert.Equal(expectedMembers[0].displayName, firstMember.DisplayName);
        Assert.Equal(expectedMembers[0].username, firstMember.Username);
        Assert.Equal(expectedMembers[0].roleName, firstMember.Role);
    }

    [Fact]
    public async Task GetGroupMembers_WithRoleFilter_ReturnsFilteredMembers()
    {
        // Arrange
        var moderatorMembers = new[]
        {
            new
            {
                id = "usr_12345678-1234-1234-1234-123456789012",
                displayName = "Moderator User",
                username = "moduser",
                roleId = "grol_moderator",
                roleName = "Moderator",
                joinedAt = "2024-01-10T09:00:00.000Z",
                lastActivity = "2024-01-15T14:30:00.000Z"
            }
        };

        _mockServer
            .Given(Request.Create()
                .WithPath($"/api/1/groups/{TestGroupId}/members")
                .UsingGet()
                .WithParam("roleId", "grol_moderator")
                .WithHeader("Cookie", "authToken=valid-token"))
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody(JsonSerializer.Serialize(moderatorMembers)));

        // Act - This will fail because VrcApiService doesn't exist yet
        var vrcApiService = new VrcApiService(_httpClient);
        _httpClient.DefaultRequestHeaders.Add("Cookie", "authToken=valid-token");
        
        var result = await vrcApiService.GetGroupMembersAsync(TestGroupId, roleId: "grol_moderator");

        // Assert
        Assert.NotNull(result);
        Assert.Single(result);
        Assert.Equal("Moderator", result[0].Role);
    }

    [Fact]
    public async Task KickGroupMember_WithValidIds_ReturnsSuccess()
    {
        // Arrange
        _mockServer
            .Given(Request.Create()
                .WithPath($"/api/1/groups/{TestGroupId}/members/{TestUserId}")
                .UsingDelete()
                .WithHeader("Cookie", "authToken=valid-token"))
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody("{\"message\":\"Member kicked successfully\"}"));

        // Act - This will fail because VrcApiService doesn't exist yet
        var vrcApiService = new VrcApiService(_httpClient);
        _httpClient.DefaultRequestHeaders.Add("Cookie", "authToken=valid-token");
        
        var success = await vrcApiService.KickGroupMemberAsync(TestGroupId, TestUserId);

        // Assert
        Assert.True(success);
    }

    [Fact]
    public async Task KickGroupMember_WithInsufficientPermissions_Returns403()
    {
        // Arrange
        _mockServer
            .Given(Request.Create()
                .WithPath($"/api/1/groups/{TestGroupId}/members/{TestUserId}")
                .UsingDelete()
                .WithHeader("Cookie", "authToken=limited-token"))
            .RespondWith(Response.Create()
                .WithStatusCode(403)
                .WithHeader("Content-Type", "application/json")
                .WithBody("{\"error\":\"Insufficient permissions\"}"));

        // Act & Assert - This will fail because VrcApiService doesn't exist yet
        var vrcApiService = new VrcApiService(_httpClient);
        _httpClient.DefaultRequestHeaders.Add("Cookie", "authToken=limited-token");
        
        var exception = await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => vrcApiService.KickGroupMemberAsync(TestGroupId, TestUserId));
        
        Assert.Contains("Insufficient permissions", exception.Message);
    }

    [Fact]
    public async Task BanGroupMember_WithValidIds_ReturnsSuccess()
    {
        // Arrange
        var banRequest = new { userId = TestUserId };
        
        _mockServer
            .Given(Request.Create()
                .WithPath($"/api/1/groups/{TestGroupId}/bans")
                .UsingPost()
                .WithHeader("Cookie", "authToken=valid-token")
                .WithBody(JsonSerializer.Serialize(banRequest)))
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody("{\"message\":\"User banned successfully\"}"));

        // Act - This will fail because VrcApiService doesn't exist yet
        var vrcApiService = new VrcApiService(_httpClient);
        _httpClient.DefaultRequestHeaders.Add("Cookie", "authToken=valid-token");
        
        var success = await vrcApiService.BanGroupMemberAsync(TestGroupId, TestUserId);

        // Assert
        Assert.True(success);
    }

    [Fact]
    public async Task UnbanGroupMember_WithValidIds_ReturnsSuccess()
    {
        // Arrange
        _mockServer
            .Given(Request.Create()
                .WithPath($"/api/1/groups/{TestGroupId}/bans/{TestUserId}")
                .UsingDelete()
                .WithHeader("Cookie", "authToken=valid-token"))
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody("{\"message\":\"User unbanned successfully\"}"));

        // Act - This will fail because VrcApiService doesn't exist yet
        var vrcApiService = new VrcApiService(_httpClient);
        _httpClient.DefaultRequestHeaders.Add("Cookie", "authToken=valid-token");
        
        var success = await vrcApiService.UnbanGroupMemberAsync(TestGroupId, TestUserId);

        // Assert
        Assert.True(success);
    }

    [Fact]
    public async Task GetGroupMembers_WithMemberNotFound_Returns404()
    {
        // Arrange
        var invalidGroupId = "grp_invalid-group-id";
        
        _mockServer
            .Given(Request.Create()
                .WithPath($"/api/1/groups/{invalidGroupId}/members")
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
            () => vrcApiService.GetGroupMembersAsync(invalidGroupId));
        
        Assert.Contains("Group not found", exception.Message);
    }

    public void Dispose()
    {
        _httpClient?.Dispose();
        _mockServer?.Stop();
        _mockServer?.Dispose();
    }
}

// Placeholder DTO class that will fail compilation - this is intentional for TDD
public class GroupMemberDto
{
    public string UserId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public DateTime JoinedAt { get; set; }
    public DateTime? LastSeen { get; set; }
}