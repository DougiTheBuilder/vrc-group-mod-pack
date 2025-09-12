using System.Net;
using System.Text.Json;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using Xunit;

namespace VrcGroupGuardian.Tests.Contract.InternalApiTests;

public class AuditServiceTests : IDisposable
{
    private readonly WireMockServer _mockServer;
    private readonly HttpClient _httpClient;

    public AuditServiceTests()
    {
        _mockServer = WireMockServer.Start();
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(_mockServer.Urls[0])
        };
    }

    [Fact]
    public async Task GetAuditLogs_WithDefaultParameters_ReturnsAuditEntries()
    {
        // Arrange - Mock internal Audit service response
        var expectedAuditEntries = new[]
        {
            new
            {
                id = "550e8400-e29b-41d4-a716-446655440001",
                timestamp = "2024-01-15T14:30:00.000Z",
                actionType = "AutoClose",
                actorUserId = "usr_12345678-1234-1234-1234-123456789012",
                actorDisplayName = "System",
                targetType = "Instance",
                targetId = "wrld_12345678-1234-1234-1234-123456789012:12345",
                targetDisplayName = "Test World Instance",
                details = "Instance closed due to policy violation",
                apiResponse = "200 OK",
                success = true,
                errorMessage = (string?)null
            },
            new
            {
                id = "550e8400-e29b-41d4-a716-446655440002",
                timestamp = "2024-01-15T13:15:00.000Z",
                actionType = "KickMember",
                actorUserId = "usr_87654321-4321-4321-4321-210987654321",
                actorDisplayName = "Moderator User",
                targetType = "Member",
                targetId = "usr_11111111-1111-1111-1111-111111111111",
                targetDisplayName = "Problem User",
                details = "Kicked for disruptive behavior",
                apiResponse = "200 OK",
                success = true,
                errorMessage = (string?)null
            }
        };

        _mockServer
            .Given(Request.Create()
                .WithPath("/audit/logs")
                .UsingGet()
                .WithParam("limit", "100")
                .WithParam("offset", "0"))
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody(JsonSerializer.Serialize(expectedAuditEntries)));

        // Act - This will fail because AuditServiceClient doesn't exist yet
        var auditService = new AuditServiceClient(_httpClient);
        
        var result = await auditService.GetAuditLogsAsync();

        // Assert
        Assert.NotNull(result);
        Assert.Equal(2, result.Count);
        
        var firstEntry = result[0];
        Assert.Equal(expectedAuditEntries[0].id, firstEntry.Id.ToString());
        Assert.Equal("AutoClose", firstEntry.ActionType);
        Assert.Equal("Instance", firstEntry.TargetType);
        Assert.True(firstEntry.Success);
        Assert.Null(firstEntry.ErrorMessage);

        var secondEntry = result[1];
        Assert.Equal("KickMember", secondEntry.ActionType);
        Assert.Equal("Moderator User", secondEntry.ActorDisplayName);
        Assert.Equal("Member", secondEntry.TargetType);
    }

    [Fact]
    public async Task GetAuditLogs_WithActionTypeFilter_ReturnsFilteredEntries()
    {
        // Arrange
        var autoCloseEntries = new[]
        {
            new
            {
                id = "550e8400-e29b-41d4-a716-446655440001",
                timestamp = "2024-01-15T14:30:00.000Z",
                actionType = "AutoClose",
                actorUserId = "usr_12345678-1234-1234-1234-123456789012",
                actorDisplayName = "System",
                targetType = "Instance",
                targetId = "wrld_12345678-1234-1234-1234-123456789012:12345",
                targetDisplayName = "Test World Instance",
                details = "Instance closed due to policy violation",
                apiResponse = "200 OK",
                success = true,
                errorMessage = (string?)null
            }
        };

        _mockServer
            .Given(Request.Create()
                .WithPath("/audit/logs")
                .UsingGet()
                .WithParam("actionType", "AutoClose")
                .WithParam("limit", "100")
                .WithParam("offset", "0"))
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody(JsonSerializer.Serialize(autoCloseEntries)));

        // Act - This will fail because AuditServiceClient doesn't exist yet
        var auditService = new AuditServiceClient(_httpClient);
        
        var result = await auditService.GetAuditLogsAsync(actionType: "AutoClose");

        // Assert
        Assert.NotNull(result);
        Assert.Single(result);
        Assert.Equal("AutoClose", result[0].ActionType);
    }

    [Fact]
    public async Task GetAuditLogs_WithDateRange_ReturnsEntriesInRange()
    {
        // Arrange
        var startDate = DateTime.Parse("2024-01-15T13:00:00.000Z");
        var endDate = DateTime.Parse("2024-01-15T15:00:00.000Z");

        var dateRangeEntries = new[]
        {
            new
            {
                id = "550e8400-e29b-41d4-a716-446655440001",
                timestamp = "2024-01-15T14:30:00.000Z",
                actionType = "ManualClose",
                actorUserId = "usr_12345678-1234-1234-1234-123456789012",
                actorDisplayName = "Moderator",
                targetType = "Instance",
                targetId = "wrld_12345678-1234-1234-1234-123456789012:12345",
                targetDisplayName = "Test World Instance",
                details = "Manually closed by moderator",
                apiResponse = "200 OK",
                success = true,
                errorMessage = (string?)null
            }
        };

        _mockServer
            .Given(Request.Create()
                .WithPath("/audit/logs")
                .UsingGet()
                .WithParam("startDate", "2024-01-15T13:00:00.000Z")
                .WithParam("endDate", "2024-01-15T15:00:00.000Z")
                .WithParam("limit", "100")
                .WithParam("offset", "0"))
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody(JsonSerializer.Serialize(dateRangeEntries)));

        // Act - This will fail because AuditServiceClient doesn't exist yet
        var auditService = new AuditServiceClient(_httpClient);
        
        var result = await auditService.GetAuditLogsAsync(startDate: startDate, endDate: endDate);

        // Assert
        Assert.NotNull(result);
        Assert.Single(result);
        Assert.Equal("ManualClose", result[0].ActionType);
        Assert.Equal("Moderator", result[0].ActorDisplayName);
    }

    [Fact]
    public async Task GetAuditLogs_WithPagination_ReturnsCorrectPage()
    {
        // Arrange
        var page2Entries = new[]
        {
            new
            {
                id = "550e8400-e29b-41d4-a716-446655440003",
                timestamp = "2024-01-15T12:00:00.000Z",
                actionType = "Login",
                actorUserId = "usr_12345678-1234-1234-1234-123456789012",
                actorDisplayName = "Test User",
                targetType = "Session",
                targetId = "session_12345",
                targetDisplayName = "Login Session",
                details = "User logged in successfully",
                apiResponse = "200 OK",
                success = true,
                errorMessage = (string?)null
            }
        };

        _mockServer
            .Given(Request.Create()
                .WithPath("/audit/logs")
                .UsingGet()
                .WithParam("limit", "50")
                .WithParam("offset", "50"))
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody(JsonSerializer.Serialize(page2Entries)));

        // Act - This will fail because AuditServiceClient doesn't exist yet
        var auditService = new AuditServiceClient(_httpClient);
        
        var result = await auditService.GetAuditLogsAsync(limit: 50, offset: 50);

        // Assert
        Assert.NotNull(result);
        Assert.Single(result);
        Assert.Equal("Login", result[0].ActionType);
    }

    [Fact]
    public async Task ExportAuditLogs_WithDefaultParameters_ReturnsCsvData()
    {
        // Arrange
        var csvData = "Id,Timestamp,ActionType,ActorDisplayName,TargetType,TargetDisplayName,Details,Success\n" +
                     "550e8400-e29b-41d4-a716-446655440001,2024-01-15T14:30:00.000Z,AutoClose,System,Instance,Test World Instance,Instance closed due to policy violation,True\n" +
                     "550e8400-e29b-41d4-a716-446655440002,2024-01-15T13:15:00.000Z,KickMember,Moderator User,Member,Problem User,Kicked for disruptive behavior,True";

        _mockServer
            .Given(Request.Create()
                .WithPath("/audit/export")
                .UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "text/csv")
                .WithBody(csvData));

        // Act - This will fail because AuditServiceClient doesn't exist yet
        var auditService = new AuditServiceClient(_httpClient);
        
        var result = await auditService.ExportAuditLogsAsync();

        // Assert
        Assert.NotNull(result);
        Assert.Contains("Id,Timestamp,ActionType", result);
        Assert.Contains("AutoClose,System", result);
        Assert.Contains("KickMember,Moderator User", result);
    }

    [Fact]
    public async Task ExportAuditLogs_WithDateRange_ReturnsCsvWithDateFilter()
    {
        // Arrange
        var startDate = DateTime.Parse("2024-01-15T00:00:00.000Z");
        var endDate = DateTime.Parse("2024-01-15T23:59:59.000Z");

        var csvData = "Id,Timestamp,ActionType,ActorDisplayName,TargetType,TargetDisplayName,Details,Success\n" +
                     "550e8400-e29b-41d4-a716-446655440001,2024-01-15T14:30:00.000Z,PolicyChange,Admin User,Policy,enforcement_policy,Updated enforcement settings,True";

        _mockServer
            .Given(Request.Create()
                .WithPath("/audit/export")
                .UsingGet()
                .WithParam("startDate", "2024-01-15T00:00:00.000Z")
                .WithParam("endDate", "2024-01-15T23:59:59.000Z"))
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "text/csv")
                .WithBody(csvData));

        // Act - This will fail because AuditServiceClient doesn't exist yet
        var auditService = new AuditServiceClient(_httpClient);
        
        var result = await auditService.ExportAuditLogsAsync(startDate, endDate);

        // Assert
        Assert.NotNull(result);
        Assert.Contains("PolicyChange,Admin User", result);
        Assert.Contains("Updated enforcement settings", result);
    }

    [Theory]
    [InlineData("AutoClose")]
    [InlineData("ManualClose")]
    [InlineData("CancelClose")]
    [InlineData("KickMember")]
    [InlineData("BanMember")]
    [InlineData("UnbanMember")]
    [InlineData("PolicyChange")]
    [InlineData("Login")]
    [InlineData("Logout")]
    public async Task GetAuditLogs_WithDifferentActionTypes_ReturnsCorrectActionType(string actionType)
    {
        // Arrange
        var auditEntry = new
        {
            id = "550e8400-e29b-41d4-a716-446655440001",
            timestamp = "2024-01-15T14:30:00.000Z",
            actionType = actionType,
            actorUserId = "usr_12345678-1234-1234-1234-123456789012",
            actorDisplayName = "Test Actor",
            targetType = "Instance",
            targetId = "target_123",
            targetDisplayName = "Test Target",
            details = $"Test {actionType} action",
            apiResponse = "200 OK",
            success = true,
            errorMessage = (string?)null
        };

        _mockServer
            .Given(Request.Create()
                .WithPath("/audit/logs")
                .UsingGet()
                .WithParam("actionType", actionType))
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody(JsonSerializer.Serialize(new[] { auditEntry })));

        // Act - This will fail because AuditServiceClient doesn't exist yet
        var auditService = new AuditServiceClient(_httpClient);
        
        var result = await auditService.GetAuditLogsAsync(actionType: actionType);

        // Assert
        Assert.NotNull(result);
        Assert.Single(result);
        Assert.Equal(actionType, result[0].ActionType);
    }

    public void Dispose()
    {
        _httpClient?.Dispose();
        _mockServer?.Stop();
        _mockServer?.Dispose();
    }
}

// Placeholder classes that will fail compilation - this is intentional for TDD
public class AuditServiceClient
{
    private readonly HttpClient _httpClient;

    public AuditServiceClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<List<AuditEntryDto>> GetAuditLogsAsync(int limit = 100, int offset = 0, string? actionType = null, DateTime? startDate = null, DateTime? endDate = null)
    {
        throw new NotImplementedException("AuditServiceClient.GetAuditLogsAsync not implemented yet");
    }

    public async Task<string> ExportAuditLogsAsync(DateTime? startDate = null, DateTime? endDate = null)
    {
        throw new NotImplementedException("AuditServiceClient.ExportAuditLogsAsync not implemented yet");
    }
}

public class AuditEntryDto
{
    public Guid Id { get; set; }
    public DateTime Timestamp { get; set; }
    public string ActionType { get; set; } = string.Empty;
    public string? ActorUserId { get; set; }
    public string? ActorDisplayName { get; set; }
    public string TargetType { get; set; } = string.Empty;
    public string TargetId { get; set; } = string.Empty;
    public string? TargetDisplayName { get; set; }
    public string? Details { get; set; }
    public string? ApiResponse { get; set; }
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
}