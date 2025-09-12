using System.Net;
using System.Text.Json;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using Xunit;

namespace VrcGroupGuardian.Tests.Contract.InternalApiTests;

public class EnforcementServiceTests : IDisposable
{
    private readonly WireMockServer _mockServer;
    private readonly HttpClient _httpClient;

    public EnforcementServiceTests()
    {
        _mockServer = WireMockServer.Start();
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(_mockServer.Urls[0])
        };
    }

    [Fact]
    public async Task GetPolicy_ReturnsCurrentPolicySettings()
    {
        // Arrange - Mock internal Enforcement service response
        var expectedPolicy = new
        {
            enforcementEnabled = true,
            gracePeriodSeconds = 180,
            pollingIntervalSeconds = 60,
            notificationsEnabled = true,
            rateLimitRequestsPerMinute = 20,
            cacheExpiryMinutes = 15
        };

        _mockServer
            .Given(Request.Create()
                .WithPath("/enforcement/policy")
                .UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody(JsonSerializer.Serialize(expectedPolicy)));

        // Act - This will fail because EnforcementServiceClient doesn't exist yet
        var enforcementService = new EnforcementServiceClient(_httpClient);
        
        var result = await enforcementService.GetPolicyAsync();

        // Assert
        Assert.NotNull(result);
        Assert.True(result.EnforcementEnabled);
        Assert.Equal(180, result.GracePeriodSeconds);
        Assert.Equal(60, result.PollingIntervalSeconds);
        Assert.True(result.NotificationsEnabled);
        Assert.Equal(20, result.RateLimitRequestsPerMinute);
        Assert.Equal(15, result.CacheExpiryMinutes);
    }

    [Fact]
    public async Task UpdatePolicy_WithValidSettings_ReturnsSuccess()
    {
        // Arrange
        var policyUpdate = new
        {
            enforcementEnabled = false,
            gracePeriodSeconds = 240,
            pollingIntervalSeconds = 75,
            notificationsEnabled = false,
            rateLimitRequestsPerMinute = 15,
            cacheExpiryMinutes = 30
        };

        _mockServer
            .Given(Request.Create()
                .WithPath("/enforcement/policy")
                .UsingPost()
                .WithHeader("Content-Type", "application/json")
                .WithBody(JsonSerializer.Serialize(policyUpdate)))
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody("{\"message\":\"Policy updated successfully\"}"));

        // Act - This will fail because EnforcementServiceClient doesn't exist yet
        var enforcementService = new EnforcementServiceClient(_httpClient);
        var policySettings = new PolicySettingsDto
        {
            EnforcementEnabled = false,
            GracePeriodSeconds = 240,
            PollingIntervalSeconds = 75,
            NotificationsEnabled = false,
            RateLimitRequestsPerMinute = 15,
            CacheExpiryMinutes = 30
        };
        
        var success = await enforcementService.UpdatePolicyAsync(policySettings);

        // Assert
        Assert.True(success);
    }

    [Fact]
    public async Task GetStatus_ReturnsEnforcementStatus()
    {
        // Arrange
        var expectedStatus = new
        {
            active = true,
            policiesChecked = 42,
            violationsFound = 3,
            lastPollTime = "2024-01-15T14:30:00.000Z",
            nextPollTime = "2024-01-15T14:31:00.000Z",
            rateLimited = false,
            errorMessage = (string?)null
        };

        _mockServer
            .Given(Request.Create()
                .WithPath("/enforcement/status")
                .UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody(JsonSerializer.Serialize(expectedStatus)));

        // Act - This will fail because EnforcementServiceClient doesn't exist yet
        var enforcementService = new EnforcementServiceClient(_httpClient);
        
        var result = await enforcementService.GetStatusAsync();

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Active);
        Assert.Equal(42, result.PoliciesChecked);
        Assert.Equal(3, result.ViolationsFound);
        Assert.False(result.RateLimited);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public async Task GetStatus_WhenRateLimited_ReturnsRateLimitedStatus()
    {
        // Arrange
        var rateLimitedStatus = new
        {
            active = true,
            policiesChecked = 15,
            violationsFound = 0,
            lastPollTime = "2024-01-15T14:25:00.000Z",
            nextPollTime = "2024-01-15T14:35:00.000Z",
            rateLimited = true,
            errorMessage = "Rate limit exceeded, backing off for 10 minutes"
        };

        _mockServer
            .Given(Request.Create()
                .WithPath("/enforcement/status")
                .UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody(JsonSerializer.Serialize(rateLimitedStatus)));

        // Act - This will fail because EnforcementServiceClient doesn't exist yet
        var enforcementService = new EnforcementServiceClient(_httpClient);
        
        var result = await enforcementService.GetStatusAsync();

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Active);
        Assert.True(result.RateLimited);
        Assert.Contains("Rate limit exceeded", result.ErrorMessage);
    }

    [Fact]
    public async Task GetStatus_WhenInactive_ReturnsInactiveStatus()
    {
        // Arrange
        var inactiveStatus = new
        {
            active = false,
            policiesChecked = 0,
            violationsFound = 0,
            lastPollTime = "2024-01-15T14:00:00.000Z",
            nextPollTime = (string?)null,
            rateLimited = false,
            errorMessage = "Enforcement disabled by policy"
        };

        _mockServer
            .Given(Request.Create()
                .WithPath("/enforcement/status")
                .UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody(JsonSerializer.Serialize(inactiveStatus)));

        // Act - This will fail because EnforcementServiceClient doesn't exist yet
        var enforcementService = new EnforcementServiceClient(_httpClient);
        
        var result = await enforcementService.GetStatusAsync();

        // Assert
        Assert.NotNull(result);
        Assert.False(result.Active);
        Assert.Equal(0, result.PoliciesChecked);
        Assert.Equal(0, result.ViolationsFound);
        Assert.Contains("Enforcement disabled", result.ErrorMessage);
    }

    [Fact]
    public async Task CancelPendingClosure_WithValidInstanceId_ReturnsSuccess()
    {
        // Arrange
        var instanceId = "wrld_12345678-1234-1234-1234-123456789012:12345~group(grp_12345678-1234-1234-1234-123456789012)";
        var reason = "Moderator intervention";

        var cancelRequest = new
        {
            instanceId = instanceId,
            reason = reason
        };

        _mockServer
            .Given(Request.Create()
                .WithPath("/enforcement/cancel")
                .UsingPost()
                .WithHeader("Content-Type", "application/json")
                .WithBody(JsonSerializer.Serialize(cancelRequest)))
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody("{\"message\":\"Closure cancelled successfully\"}"));

        // Act - This will fail because EnforcementServiceClient doesn't exist yet
        var enforcementService = new EnforcementServiceClient(_httpClient);
        
        var success = await enforcementService.CancelPendingClosureAsync(instanceId, reason);

        // Assert
        Assert.True(success);
    }

    [Fact]
    public async Task CancelPendingClosure_WithDefaultReason_ReturnsSuccess()
    {
        // Arrange
        var instanceId = "wrld_12345678-1234-1234-1234-123456789012:12345~group(grp_12345678-1234-1234-1234-123456789012)";

        var cancelRequest = new
        {
            instanceId = instanceId,
            reason = "Cancelled by moderator"
        };

        _mockServer
            .Given(Request.Create()
                .WithPath("/enforcement/cancel")
                .UsingPost()
                .WithBody(JsonSerializer.Serialize(cancelRequest)))
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody("{\"message\":\"Closure cancelled successfully\"}"));

        // Act - This will fail because EnforcementServiceClient doesn't exist yet
        var enforcementService = new EnforcementServiceClient(_httpClient);
        
        var success = await enforcementService.CancelPendingClosureAsync(instanceId);

        // Assert
        Assert.True(success);
    }

    [Fact]
    public async Task CancelPendingClosure_WithNonExistentPendingClosure_Returns404()
    {
        // Arrange
        var instanceId = "wrld_12345678-1234-1234-1234-123456789012:12345~group(grp_12345678-1234-1234-1234-123456789012)";
        var reason = "Test cancellation";

        var cancelRequest = new
        {
            instanceId = instanceId,
            reason = reason
        };

        _mockServer
            .Given(Request.Create()
                .WithPath("/enforcement/cancel")
                .UsingPost()
                .WithBody(JsonSerializer.Serialize(cancelRequest)))
            .RespondWith(Response.Create()
                .WithStatusCode(404)
                .WithHeader("Content-Type", "application/json")
                .WithBody("{\"error\":\"No pending closure for instance\"}"));

        // Act & Assert - This will fail because EnforcementServiceClient doesn't exist yet
        var enforcementService = new EnforcementServiceClient(_httpClient);
        
        var exception = await Assert.ThrowsAsync<HttpRequestException>(
            () => enforcementService.CancelPendingClosureAsync(instanceId, reason));
        
        Assert.Contains("No pending closure", exception.Message);
    }

    [Theory]
    [InlineData(60, 60, true)]   // Minimum valid values
    [InlineData(300, 90, true)]  // Maximum valid values
    [InlineData(180, 75, true)]  // Typical values
    [InlineData(120, 45, false)] // Invalid polling interval (below 45)
    [InlineData(30, 60, false)]  // Invalid grace period (below 60)
    public async Task UpdatePolicy_WithDifferentValidationScenarios_ReturnsExpectedResult(
        int gracePeriod, int pollingInterval, bool shouldSucceed)
    {
        // Arrange
        var policyUpdate = new
        {
            enforcementEnabled = true,
            gracePeriodSeconds = gracePeriod,
            pollingIntervalSeconds = pollingInterval,
            notificationsEnabled = true,
            rateLimitRequestsPerMinute = 20,
            cacheExpiryMinutes = 15
        };

        var responseStatusCode = shouldSucceed ? 200 : 400;
        var responseBody = shouldSucceed 
            ? "{\"message\":\"Policy updated successfully\"}"
            : "{\"error\":\"Invalid policy settings\"}";

        _mockServer
            .Given(Request.Create()
                .WithPath("/enforcement/policy")
                .UsingPost()
                .WithBody(JsonSerializer.Serialize(policyUpdate)))
            .RespondWith(Response.Create()
                .WithStatusCode(responseStatusCode)
                .WithHeader("Content-Type", "application/json")
                .WithBody(responseBody));

        // Act - This will fail because EnforcementServiceClient doesn't exist yet
        var enforcementService = new EnforcementServiceClient(_httpClient);
        var policySettings = new PolicySettingsDto
        {
            EnforcementEnabled = true,
            GracePeriodSeconds = gracePeriod,
            PollingIntervalSeconds = pollingInterval,
            NotificationsEnabled = true,
            RateLimitRequestsPerMinute = 20,
            CacheExpiryMinutes = 15
        };

        if (shouldSucceed)
        {
            var success = await enforcementService.UpdatePolicyAsync(policySettings);
            Assert.True(success);
        }
        else
        {
            var exception = await Assert.ThrowsAsync<HttpRequestException>(
                () => enforcementService.UpdatePolicyAsync(policySettings));
            Assert.Contains("Invalid policy settings", exception.Message);
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
public class EnforcementServiceClient
{
    private readonly HttpClient _httpClient;

    public EnforcementServiceClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<PolicySettingsDto> GetPolicyAsync()
    {
        throw new NotImplementedException("EnforcementServiceClient.GetPolicyAsync not implemented yet");
    }

    public async Task<bool> UpdatePolicyAsync(PolicySettingsDto policySettings)
    {
        throw new NotImplementedException("EnforcementServiceClient.UpdatePolicyAsync not implemented yet");
    }

    public async Task<EnforcementStatusDto> GetStatusAsync()
    {
        throw new NotImplementedException("EnforcementServiceClient.GetStatusAsync not implemented yet");
    }

    public async Task<bool> CancelPendingClosureAsync(string instanceId, string? reason = null)
    {
        throw new NotImplementedException("EnforcementServiceClient.CancelPendingClosureAsync not implemented yet");
    }
}

public class PolicySettingsDto
{
    public bool EnforcementEnabled { get; set; }
    public int GracePeriodSeconds { get; set; }
    public int PollingIntervalSeconds { get; set; }
    public bool NotificationsEnabled { get; set; }
    public int RateLimitRequestsPerMinute { get; set; }
    public int CacheExpiryMinutes { get; set; }
}

public class EnforcementStatusDto
{
    public bool Active { get; set; }
    public int PoliciesChecked { get; set; }
    public int ViolationsFound { get; set; }
    public DateTime LastPollTime { get; set; }
    public DateTime? NextPollTime { get; set; }
    public bool RateLimited { get; set; }
    public string? ErrorMessage { get; set; }
}