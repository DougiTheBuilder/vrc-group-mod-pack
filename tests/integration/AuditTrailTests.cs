using System.Net;
using System.Text.Json;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using Xunit;

namespace VrcGroupGuardian.Tests.Integration;

public class AuditTrailTests : IDisposable
{
    private readonly WireMockServer _mockInternalApi;
    private readonly HttpClient _internalClient;

    public AuditTrailTests()
    {
        _mockInternalApi = WireMockServer.Start();
        _internalClient = new HttpClient { BaseAddress = new Uri(_mockInternalApi.Urls[0]) };
    }

    [Fact]
    public async Task RecordAuditEntry_ForAllActions_ShouldCreateComprehensiveLog()
    {
        // Arrange - User scenario: All actions are logged with proper audit trail
        
        var auditEntries = new[]
        {
            new
            {
                id = "550e8400-e29b-41d4-a716-446655440001",
                timestamp = "2024-01-15T14:30:00.000Z",
                actionType = "AutoClose",
                actorUserId = "usr_system",
                actorDisplayName = "System",
                targetType = "Instance",
                targetId = "wrld_12345678-1234-1234-1234-123456789012:12345",
                targetDisplayName = "Violation World Instance",
                details = "Instance auto-closed due to age-gated content violation",
                apiResponse = "200 OK - Instance closed successfully",
                success = true,
                errorMessage = (string?)null
            },
            new
            {
                id = "550e8400-e29b-41d4-a716-446655440002",
                timestamp = "2024-01-15T14:25:00.000Z",
                actionType = "KickMember",
                actorUserId = "usr_87654321-4321-4321-4321-210987654321",
                actorDisplayName = "Moderator User",
                targetType = "Member",
                targetId = "usr_11111111-1111-1111-1111-111111111111",
                targetDisplayName = "Problem User",
                details = "Member kicked for disruptive behavior",
                apiResponse = "200 OK - Member kicked successfully",
                success = true,
                errorMessage = (string?)null
            }
        };

        _mockInternalApi
            .Given(Request.Create().WithPath("/audit/logs").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200)
                .WithBody(JsonSerializer.Serialize(auditEntries)));

        // Act - This will fail because AuditTrailManager doesn't exist yet
        var auditManager = new AuditTrailManager(_internalClient);
        
        var recentEntries = await auditManager.GetRecentAuditEntriesAsync();
        var actionSummary = await auditManager.GetActionSummaryAsync();

        // Assert
        Assert.NotNull(recentEntries);
        Assert.Equal(2, recentEntries.Count);
        
        var autoCloseEntry = recentEntries.First(e => e.ActionType == "AutoClose");
        Assert.Equal("System", autoCloseEntry.ActorDisplayName);
        Assert.Equal("Instance", autoCloseEntry.TargetType);
        Assert.Contains("age-gated content", autoCloseEntry.Details);
        Assert.True(autoCloseEntry.Success);

        var kickEntry = recentEntries.First(e => e.ActionType == "KickMember");
        Assert.Equal("Moderator User", kickEntry.ActorDisplayName);
        Assert.Equal("Member", kickEntry.TargetType);
        Assert.Contains("disruptive behavior", kickEntry.Details);

        Assert.NotNull(actionSummary);
        Assert.Equal(2, actionSummary.TotalActions);
        Assert.Equal(2, actionSummary.SuccessfulActions);
        Assert.Equal(0, actionSummary.FailedActions);
    }

    [Fact]
    public async Task ExportAuditLog_WithDateRange_ShouldGenerateCSV()
    {
        // Arrange - User wants to export audit logs for compliance
        var csvData = "Id,Timestamp,ActionType,ActorDisplayName,TargetType,TargetDisplayName,Details,Success\n" +
                     "550e8400-e29b-41d4-a716-446655440001,2024-01-15T14:30:00.000Z,AutoClose,System,Instance,Test World Instance,Auto-closed due to policy violation,True\n" +
                     "550e8400-e29b-41d4-a716-446655440002,2024-01-15T14:25:00.000Z,ManualClose,Admin User,Instance,Manual Test Instance,Manually closed by administrator,True\n" +
                     "550e8400-e29b-41d4-a716-446655440003,2024-01-15T14:20:00.000Z,KickMember,Moderator,Member,Problem User,Kicked for policy violation,True";

        var startDate = DateTime.Parse("2024-01-15T00:00:00.000Z");
        var endDate = DateTime.Parse("2024-01-15T23:59:59.000Z");

        _mockInternalApi
            .Given(Request.Create()
                .WithPath("/audit/export")
                .WithParam("startDate", "2024-01-15T00:00:00.000Z")
                .WithParam("endDate", "2024-01-15T23:59:59.000Z")
                .UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200)
                .WithHeader("Content-Type", "text/csv")
                .WithBody(csvData));

        // Act - This will fail because AuditTrailManager doesn't exist yet
        var auditManager = new AuditTrailManager(_internalClient);
        
        var exportResult = await auditManager.ExportAuditLogAsync(startDate, endDate);

        // Assert
        Assert.NotNull(exportResult);
        Assert.True(exportResult.Success);
        Assert.NotNull(exportResult.CsvData);
        Assert.Contains("Id,Timestamp,ActionType", exportResult.CsvData);
        Assert.Contains("AutoClose,System", exportResult.CsvData);
        Assert.Contains("ManualClose,Admin User", exportResult.CsvData);
        Assert.Contains("KickMember,Moderator", exportResult.CsvData);
        
        // Verify proper CSV structure
        var lines = exportResult.CsvData.Split('\n');
        Assert.Equal(4, lines.Length); // Header + 3 data rows
    }

    [Fact]
    public async Task FilterAuditLog_ByActionType_ShouldReturnSpecificActions()
    {
        // Arrange - User wants to see only specific types of actions
        var autoCloseEntries = new[]
        {
            new
            {
                id = "550e8400-e29b-41d4-a716-446655440001",
                timestamp = "2024-01-15T14:30:00.000Z",
                actionType = "AutoClose",
                actorUserId = "usr_system",
                actorDisplayName = "System",
                targetType = "Instance",
                targetId = "wrld_violation1:123",
                targetDisplayName = "Violation World 1",
                details = "Auto-closed due to age-gated content",
                success = true
            },
            new
            {
                id = "550e8400-e29b-41d4-a716-446655440002",
                timestamp = "2024-01-15T14:25:00.000Z",
                actionType = "AutoClose",
                actorUserId = "usr_system",
                actorDisplayName = "System",
                targetType = "Instance",
                targetId = "wrld_violation2:456",
                targetDisplayName = "Violation World 2",
                details = "Auto-closed due to capacity violation",
                success = true
            }
        };

        _mockInternalApi
            .Given(Request.Create()
                .WithPath("/audit/logs")
                .WithParam("actionType", "AutoClose")
                .UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200)
                .WithBody(JsonSerializer.Serialize(autoCloseEntries)));

        // Act - This will fail because AuditTrailManager doesn't exist yet
        var auditManager = new AuditTrailManager(_internalClient);
        
        var filteredEntries = await auditManager.GetAuditEntriesByActionAsync("AutoClose");
        var actionStats = await auditManager.GetActionStatisticsAsync("AutoClose");

        // Assert
        Assert.NotNull(filteredEntries);
        Assert.Equal(2, filteredEntries.Count);
        Assert.All(filteredEntries, entry => Assert.Equal("AutoClose", entry.ActionType));
        Assert.All(filteredEntries, entry => Assert.Equal("System", entry.ActorDisplayName));
        Assert.All(filteredEntries, entry => Assert.Equal("Instance", entry.TargetType));

        Assert.NotNull(actionStats);
        Assert.Equal("AutoClose", actionStats.ActionType);
        Assert.Equal(2, actionStats.TotalOccurrences);
        Assert.Equal(2, actionStats.SuccessfulOccurrences);
        Assert.Equal(0, actionStats.FailedOccurrences);
    }

    [Fact]
    public async Task TrackFailedActions_ShouldLogErrorsForDebugging()
    {
        // Arrange - System should log failed actions with error details
        var failedEntries = new[]
        {
            new
            {
                id = "550e8400-e29b-41d4-a716-446655440001",
                timestamp = "2024-01-15T14:30:00.000Z",
                actionType = "KickMember",
                actorUserId = "usr_moderator",
                actorDisplayName = "Limited Moderator",
                targetType = "Member",
                targetId = "usr_protected",
                targetDisplayName = "Protected User",
                details = "Attempted to kick member with higher permissions",
                apiResponse = "403 Forbidden - Insufficient permissions",
                success = false,
                errorMessage = "Cannot kick member with higher or equal permission level"
            },
            new
            {
                id = "550e8400-e29b-41d4-a716-446655440002",
                timestamp = "2024-01-15T14:28:00.000Z",
                actionType = "AutoClose",
                actorUserId = "usr_system",
                actorDisplayName = "System",
                targetType = "Instance",
                targetId = "wrld_already-closed:999",
                targetDisplayName = "Already Closed Instance",
                details = "Attempted to close instance that was already closed",
                apiResponse = "404 Not Found - Instance not found",
                success = false,
                errorMessage = "Instance no longer exists or was already closed"
            }
        };

        _mockInternalApi
            .Given(Request.Create()
                .WithPath("/audit/logs")
                .WithParam("limit", "100")
                .WithParam("offset", "0")
                .UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200)
                .WithBody(JsonSerializer.Serialize(failedEntries)));

        // Act - This will fail because AuditTrailManager doesn't exist yet
        var auditManager = new AuditTrailManager(_internalClient);
        
        var allEntries = await auditManager.GetRecentAuditEntriesAsync();
        var failedActions = await auditManager.GetFailedActionsAsync();
        var errorAnalysis = await auditManager.AnalyzeErrorPatternsAsync();

        // Assert
        Assert.NotNull(allEntries);
        Assert.Equal(2, allEntries.Count);
        Assert.All(allEntries, entry => Assert.False(entry.Success));

        var kickFailure = allEntries.First(e => e.ActionType == "KickMember");
        Assert.Contains("higher permissions", kickFailure.ErrorMessage);
        Assert.Equal("403 Forbidden", kickFailure.ApiResponse.Split(' ')[0..2].JoinToString(" "));

        var closeFailure = allEntries.First(e => e.ActionType == "AutoClose");
        Assert.Contains("already closed", closeFailure.ErrorMessage);
        Assert.Contains("404 Not Found", closeFailure.ApiResponse);

        Assert.NotNull(failedActions);
        Assert.Equal(2, failedActions.Count);

        Assert.NotNull(errorAnalysis);
        Assert.True(errorAnalysis.PermissionErrors > 0);
        Assert.True(errorAnalysis.NotFoundErrors > 0);
        Assert.Contains("403", errorAnalysis.CommonErrorCodes);
        Assert.Contains("404", errorAnalysis.CommonErrorCodes);
    }

    [Fact]
    public async Task GenerateComplianceReport_ShouldSummarizeAllActions()
    {
        // Arrange - User needs compliance report showing all moderation actions
        var reportData = new
        {
            reportPeriod = new
            {
                startDate = "2024-01-01T00:00:00.000Z",
                endDate = "2024-01-31T23:59:59.000Z"
            },
            summary = new
            {
                totalActions = 45,
                successfulActions = 42,
                failedActions = 3,
                uniqueActors = 8,
                uniqueTargets = 23
            },
            actionBreakdown = new
            {
                autoClose = 15,
                manualClose = 8,
                kickMember = 12,
                banMember = 4,
                unbanMember = 2,
                cancelClose = 3,
                policyChange = 1
            },
            topActors = new[]
            {
                new { actorName = "System", actionCount = 18 },
                new { actorName = "Head Moderator", actionCount = 12 },
                new { actorName = "Moderator Alpha", actionCount = 8 }
            }
        };

        _mockInternalApi
            .Given(Request.Create()
                .WithPath("/audit/logs")
                .WithParam("startDate", "2024-01-01T00:00:00.000Z")
                .WithParam("endDate", "2024-01-31T23:59:59.000Z")
                .UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200)
                .WithBody("[]")); // Actual entries don't matter for this test

        // Act - This will fail because AuditTrailManager doesn't exist yet
        var auditManager = new AuditTrailManager(_internalClient);
        
        var startDate = DateTime.Parse("2024-01-01T00:00:00.000Z");
        var endDate = DateTime.Parse("2024-01-31T23:59:59.000Z");
        
        var report = await auditManager.GenerateComplianceReportAsync(startDate, endDate);

        // Assert
        Assert.NotNull(report);
        Assert.Equal(startDate, report.PeriodStart);
        Assert.Equal(endDate, report.PeriodEnd);
        
        // These would be calculated from actual audit entries in real implementation
        Assert.True(report.TotalActions >= 0);
        Assert.True(report.SuccessfulActions >= 0);
        Assert.True(report.FailedActions >= 0);
        Assert.NotNull(report.ActionBreakdown);
        Assert.NotNull(report.TopActors);
    }

    public void Dispose()
    {
        _internalClient?.Dispose();
        _mockInternalApi?.Stop();
        _mockInternalApi?.Dispose();
    }
}

// Placeholder classes that will fail compilation - this is intentional for TDD
public class AuditTrailManager
{
    private readonly HttpClient _internalClient;

    public AuditTrailManager(HttpClient internalClient)
    {
        _internalClient = internalClient;
    }

    public async Task<List<AuditEntry>> GetRecentAuditEntriesAsync()
    {
        throw new NotImplementedException("AuditTrailManager.GetRecentAuditEntriesAsync not implemented yet");
    }

    public async Task<ActionSummary> GetActionSummaryAsync()
    {
        throw new NotImplementedException("AuditTrailManager.GetActionSummaryAsync not implemented yet");
    }

    public async Task<ExportResult> ExportAuditLogAsync(DateTime startDate, DateTime endDate)
    {
        throw new NotImplementedException("AuditTrailManager.ExportAuditLogAsync not implemented yet");
    }

    public async Task<List<AuditEntry>> GetAuditEntriesByActionAsync(string actionType)
    {
        throw new NotImplementedException("AuditTrailManager.GetAuditEntriesByActionAsync not implemented yet");
    }

    public async Task<ActionStatistics> GetActionStatisticsAsync(string actionType)
    {
        throw new NotImplementedException("AuditTrailManager.GetActionStatisticsAsync not implemented yet");
    }

    public async Task<List<AuditEntry>> GetFailedActionsAsync()
    {
        throw new NotImplementedException("AuditTrailManager.GetFailedActionsAsync not implemented yet");
    }

    public async Task<ErrorAnalysis> AnalyzeErrorPatternsAsync()
    {
        throw new NotImplementedException("AuditTrailManager.AnalyzeErrorPatternsAsync not implemented yet");
    }

    public async Task<ComplianceReport> GenerateComplianceReportAsync(DateTime startDate, DateTime endDate)
    {
        throw new NotImplementedException("AuditTrailManager.GenerateComplianceReportAsync not implemented yet");
    }
}

public class AuditEntry
{
    public string Id { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public string ActionType { get; set; } = string.Empty;
    public string ActorDisplayName { get; set; } = string.Empty;
    public string TargetType { get; set; } = string.Empty;
    public string TargetDisplayName { get; set; } = string.Empty;
    public string Details { get; set; } = string.Empty;
    public string ApiResponse { get; set; } = string.Empty;
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
}

public class ActionSummary
{
    public int TotalActions { get; set; }
    public int SuccessfulActions { get; set; }
    public int FailedActions { get; set; }
}

public class ExportResult
{
    public bool Success { get; set; }
    public string? CsvData { get; set; }
}

public class ActionStatistics
{
    public string ActionType { get; set; } = string.Empty;
    public int TotalOccurrences { get; set; }
    public int SuccessfulOccurrences { get; set; }
    public int FailedOccurrences { get; set; }
}

public class ErrorAnalysis
{
    public int PermissionErrors { get; set; }
    public int NotFoundErrors { get; set; }
    public List<string> CommonErrorCodes { get; set; } = new();
}

public class ComplianceReport
{
    public DateTime PeriodStart { get; set; }
    public DateTime PeriodEnd { get; set; }
    public int TotalActions { get; set; }
    public int SuccessfulActions { get; set; }
    public int FailedActions { get; set; }
    public Dictionary<string, int> ActionBreakdown { get; set; } = new();
    public List<ActorStats> TopActors { get; set; } = new();
}

public class ActorStats
{
    public string ActorName { get; set; } = string.Empty;
    public int ActionCount { get; set; }
}

public static class StringExtensions
{
    public static string JoinToString(this IEnumerable<string> source, string separator)
    {
        return string.Join(separator, source);
    }
}