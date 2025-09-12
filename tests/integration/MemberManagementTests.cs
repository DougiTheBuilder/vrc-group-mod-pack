using System.Net;
using System.Text.Json;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using Xunit;

namespace VrcGroupGuardian.Tests.Integration;

public class MemberManagementTests : IDisposable
{
    private readonly WireMockServer _mockVrchatApi;
    private readonly WireMockServer _mockInternalApi;
    private readonly HttpClient _vrchatClient;
    private readonly HttpClient _internalClient;
    private const string TestGroupId = "grp_12345678-1234-1234-1234-123456789012";
    private const string TestUserId = "usr_87654321-4321-4321-4321-210987654321";

    public MemberManagementTests()
    {
        _mockVrchatApi = WireMockServer.Start();
        _mockInternalApi = WireMockServer.Start();
        
        _vrchatClient = new HttpClient { BaseAddress = new Uri(_mockVrchatApi.Urls[0]) };
        _internalClient = new HttpClient { BaseAddress = new Uri(_mockInternalApi.Urls[0]) };
    }

    [Fact]
    public async Task KickMember_WithValidPermissions_ShouldRemoveFromGroup()
    {
        // Arrange - User scenario: Moderator kicks disruptive member
        
        // Mock VRChat API - Member kick
        _mockVrchatApi
            .Given(Request.Create()
                .WithPath($"/api/1/groups/{TestGroupId}/members/{TestUserId}")
                .UsingDelete())
            .RespondWith(Response.Create().WithStatusCode(200)
                .WithBody("{\"message\":\"Member kicked successfully\"}"));

        // Mock Internal API - Member kick
        var kickRequest = new
        {
            userId = TestUserId,
            reason = "Disruptive behavior in instance"
        };

        _mockInternalApi
            .Given(Request.Create()
                .WithPath("/members/kick")
                .UsingPost()
                .WithBody(JsonSerializer.Serialize(kickRequest)))
            .RespondWith(Response.Create().WithStatusCode(200)
                .WithBody("{\"message\":\"Member kicked successfully\"}"));

        // Mock member info before and after kick
        var activeMember = new
        {
            userId = TestUserId,
            displayName = "Disruptive User",
            username = "disruptiveuser",
            role = "Member",
            joinedAt = "2024-01-10T09:00:00.000Z",
            lastSeen = "2024-01-15T14:20:00.000Z",
            permissionLevel = "Member",
            canKick = false,
            canBan = false
        };

        _mockInternalApi
            .Given(Request.Create().WithPath("/members/list").UsingGet())
            .InScenario("member-kick")
            .WhenStateIs(Scenario.Started)
            .RespondWith(Response.Create().WithStatusCode(200)
                .WithBody(JsonSerializer.Serialize(new[] { activeMember })))
            .WillSetStateTo("kicked");

        _mockInternalApi
            .Given(Request.Create().WithPath("/members/list").UsingGet())
            .InScenario("member-kick")
            .WhenStateIs("kicked")
            .RespondWith(Response.Create().WithStatusCode(200)
                .WithBody("[]")); // Member no longer in group

        // Act - This will fail because MemberManager doesn't exist yet
        var memberManager = new MemberManager(_vrchatClient, _internalClient);
        
        // Step 1: Verify member exists
        var beforeKick = await memberManager.GetMembersAsync();
        var targetMember = beforeKick.FirstOrDefault(m => m.UserId == TestUserId);
        
        // Step 2: Kick member
        var kickResult = await memberManager.KickMemberAsync(TestUserId, "Disruptive behavior in instance");
        
        // Step 3: Verify member removed
        var afterKick = await memberManager.GetMembersAsync();

        // Assert
        Assert.NotNull(targetMember);
        Assert.Equal("Disruptive User", targetMember.DisplayName);
        Assert.Equal("Member", targetMember.Role);

        Assert.NotNull(kickResult);
        Assert.True(kickResult.Success);
        Assert.Equal(TestUserId, kickResult.UserId);
        Assert.Equal("Disruptive behavior in instance", kickResult.Reason);
        Assert.Contains("kicked", kickResult.Message);

        Assert.DoesNotContain(afterKick, m => m.UserId == TestUserId);
    }

    [Fact]
    public async Task BanMember_WithValidPermissions_ShouldBanFromGroup()
    {
        // Arrange - User scenario: Moderator bans repeat offender
        
        // Mock VRChat API - Member ban
        var banRequest = new { userId = TestUserId };
        
        _mockVrchatApi
            .Given(Request.Create()
                .WithPath($"/api/1/groups/{TestGroupId}/bans")
                .UsingPost()
                .WithBody(JsonSerializer.Serialize(banRequest)))
            .RespondWith(Response.Create().WithStatusCode(200)
                .WithBody("{\"message\":\"User banned successfully\"}"));

        // Mock Internal API - Member ban
        var internalBanRequest = new
        {
            userId = TestUserId,
            reason = "Repeated policy violations"
        };

        _mockInternalApi
            .Given(Request.Create()
                .WithPath("/members/ban")
                .UsingPost()
                .WithBody(JsonSerializer.Serialize(internalBanRequest)))
            .RespondWith(Response.Create().WithStatusCode(200)
                .WithBody("{\"message\":\"Member banned successfully\"}"));

        // Act - This will fail because MemberManager doesn't exist yet
        var memberManager = new MemberManager(_vrchatClient, _internalClient);
        
        var banResult = await memberManager.BanMemberAsync(TestUserId, "Repeated policy violations");
        var banStatus = await memberManager.GetMemberBanStatusAsync(TestUserId);

        // Assert
        Assert.NotNull(banResult);
        Assert.True(banResult.Success);
        Assert.Equal(TestUserId, banResult.UserId);
        Assert.Equal("Repeated policy violations", banResult.Reason);
        Assert.Contains("banned", banResult.Message);

        Assert.NotNull(banStatus);
        Assert.True(banStatus.IsBanned);
        Assert.Contains("Repeated policy violations", banStatus.BanReason);
    }

    [Fact]
    public async Task SearchMembers_WithFilters_ShouldReturnFilteredResults()
    {
        // Arrange - User searches for specific members
        var allMembers = new[]
        {
            new
            {
                userId = "usr_member1",
                displayName = "Regular Member 1",
                username = "regular1",
                role = "Member",
                joinedAt = "2024-01-01T09:00:00.000Z",
                lastSeen = "2024-01-15T14:00:00.000Z",
                permissionLevel = "Member",
                canKick = false,
                canBan = false
            },
            new
            {
                userId = "usr_moderator1",
                displayName = "Moderator User",
                username = "moduser1",
                role = "Moderator",
                joinedAt = "2024-01-01T08:00:00.000Z",
                lastSeen = "2024-01-15T14:30:00.000Z",
                permissionLevel = "Moderator",
                canKick = true,
                canBan = true
            }
        };

        var searchResults = new[]
        {
            allMembers[1] // Only moderator matches search
        };

        _mockInternalApi
            .Given(Request.Create()
                .WithPath("/members/list")
                .WithParam("search", "Moderator")
                .UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200)
                .WithBody(JsonSerializer.Serialize(searchResults)));

        _mockInternalApi
            .Given(Request.Create()
                .WithPath("/members/list")
                .WithParam("role", "Moderator")
                .UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200)
                .WithBody(JsonSerializer.Serialize(searchResults)));

        // Act - This will fail because MemberManager doesn't exist yet
        var memberManager = new MemberManager(_vrchatClient, _internalClient);
        
        var searchByName = await memberManager.SearchMembersAsync("Moderator");
        var filterByRole = await memberManager.GetMembersByRoleAsync("Moderator");

        // Assert
        Assert.NotNull(searchByName);
        Assert.Single(searchByName);
        Assert.Equal("Moderator User", searchByName[0].DisplayName);
        Assert.Equal("Moderator", searchByName[0].Role);

        Assert.NotNull(filterByRole);
        Assert.Single(filterByRole);
        Assert.True(filterByRole[0].CanKick);
        Assert.True(filterByRole[0].CanBan);
    }

    [Fact]
    public async Task UnbanMember_WithValidPermissions_ShouldRemoveBan()
    {
        // Arrange - User unbans previously banned member
        
        // Mock VRChat API - Member unban
        _mockVrchatApi
            .Given(Request.Create()
                .WithPath($"/api/1/groups/{TestGroupId}/bans/{TestUserId}")
                .UsingDelete())
            .RespondWith(Response.Create().WithStatusCode(200)
                .WithBody("{\"message\":\"User unbanned successfully\"}"));

        // Mock Internal API - Member unban
        var unbanRequest = new { userId = TestUserId };

        _mockInternalApi
            .Given(Request.Create()
                .WithPath("/members/unban")
                .UsingPost()
                .WithBody(JsonSerializer.Serialize(unbanRequest)))
            .RespondWith(Response.Create().WithStatusCode(200)
                .WithBody("{\"message\":\"Member unbanned successfully\"}"));

        // Act - This will fail because MemberManager doesn't exist yet
        var memberManager = new MemberManager(_vrchatClient, _internalClient);
        
        var unbanResult = await memberManager.UnbanMemberAsync(TestUserId);
        var banStatus = await memberManager.GetMemberBanStatusAsync(TestUserId);

        // Assert
        Assert.NotNull(unbanResult);
        Assert.True(unbanResult.Success);
        Assert.Equal(TestUserId, unbanResult.UserId);
        Assert.Contains("unbanned", unbanResult.Message);

        Assert.NotNull(banStatus);
        Assert.False(banStatus.IsBanned);
        Assert.Null(banStatus.BanReason);
    }

    [Fact]
    public async Task BulkMemberOperations_ShouldHandleMultipleMembersEfficiently()
    {
        // Arrange - User performs bulk operations on multiple members
        var memberIds = new[] { "usr_bulk1", "usr_bulk2", "usr_bulk3" };
        
        // Mock successful kicks for first two members
        foreach (var memberId in memberIds.Take(2))
        {
            _mockVrchatApi
                .Given(Request.Create().WithPath($"/api/1/groups/{TestGroupId}/members/{memberId}").UsingDelete())
                .RespondWith(Response.Create().WithStatusCode(200)
                    .WithBody("{\"message\":\"Member kicked successfully\"}"));

            var kickRequest = new { userId = memberId, reason = "Bulk moderation action" };
            _mockInternalApi
                .Given(Request.Create().WithPath("/members/kick").UsingPost()
                    .WithBody(JsonSerializer.Serialize(kickRequest)))
                .RespondWith(Response.Create().WithStatusCode(200)
                    .WithBody("{\"message\":\"Member kicked successfully\"}"));
        }

        // Mock failure for third member (insufficient permissions)
        _mockVrchatApi
            .Given(Request.Create().WithPath($"/api/1/groups/{TestGroupId}/members/usr_bulk3").UsingDelete())
            .RespondWith(Response.Create().WithStatusCode(403)
                .WithBody("{\"error\":\"Insufficient permissions\"}"));

        var failedKickRequest = new { userId = "usr_bulk3", reason = "Bulk moderation action" };
        _mockInternalApi
            .Given(Request.Create().WithPath("/members/kick").UsingPost()
                .WithBody(JsonSerializer.Serialize(failedKickRequest)))
            .RespondWith(Response.Create().WithStatusCode(403)
                .WithBody("{\"error\":\"Insufficient permissions\"}"));

        // Act - This will fail because MemberManager doesn't exist yet
        var memberManager = new MemberManager(_vrchatClient, _internalClient);
        
        var bulkResult = await memberManager.BulkKickMembersAsync(memberIds, "Bulk moderation action");

        // Assert
        Assert.NotNull(bulkResult);
        Assert.Equal(3, bulkResult.TotalAttempted);
        Assert.Equal(2, bulkResult.SuccessfulActions);
        Assert.Equal(1, bulkResult.FailedActions);
        
        Assert.Contains(bulkResult.SuccessfulMembers, id => id == "usr_bulk1");
        Assert.Contains(bulkResult.SuccessfulMembers, id => id == "usr_bulk2");
        Assert.Contains(bulkResult.FailedMembers.Keys, id => id == "usr_bulk3");
        Assert.Contains("Insufficient permissions", bulkResult.FailedMembers.Values.First());
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
public class MemberManager
{
    private readonly HttpClient _vrchatClient;
    private readonly HttpClient _internalClient;

    public MemberManager(HttpClient vrchatClient, HttpClient internalClient)
    {
        _vrchatClient = vrchatClient;
        _internalClient = internalClient;
    }

    public async Task<List<MemberInfo>> GetMembersAsync()
    {
        throw new NotImplementedException("MemberManager.GetMembersAsync not implemented yet");
    }

    public async Task<MemberActionResult> KickMemberAsync(string userId, string reason)
    {
        throw new NotImplementedException("MemberManager.KickMemberAsync not implemented yet");
    }

    public async Task<MemberActionResult> BanMemberAsync(string userId, string reason)
    {
        throw new NotImplementedException("MemberManager.BanMemberAsync not implemented yet");
    }

    public async Task<MemberActionResult> UnbanMemberAsync(string userId)
    {
        throw new NotImplementedException("MemberManager.UnbanMemberAsync not implemented yet");
    }

    public async Task<BanStatus> GetMemberBanStatusAsync(string userId)
    {
        throw new NotImplementedException("MemberManager.GetMemberBanStatusAsync not implemented yet");
    }

    public async Task<List<MemberInfo>> SearchMembersAsync(string searchTerm)
    {
        throw new NotImplementedException("MemberManager.SearchMembersAsync not implemented yet");
    }

    public async Task<List<MemberInfo>> GetMembersByRoleAsync(string role)
    {
        throw new NotImplementedException("MemberManager.GetMembersByRoleAsync not implemented yet");
    }

    public async Task<BulkMemberResult> BulkKickMembersAsync(string[] memberIds, string reason)
    {
        throw new NotImplementedException("MemberManager.BulkKickMembersAsync not implemented yet");
    }
}

public class MemberInfo
{
    public string UserId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public bool CanKick { get; set; }
    public bool CanBan { get; set; }
}

public class MemberActionResult
{
    public bool Success { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}

public class BanStatus
{
    public bool IsBanned { get; set; }
    public string? BanReason { get; set; }
}

public class BulkMemberResult
{
    public int TotalAttempted { get; set; }
    public int SuccessfulActions { get; set; }
    public int FailedActions { get; set; }
    public List<string> SuccessfulMembers { get; set; } = new();
    public Dictionary<string, string> FailedMembers { get; set; } = new();
}