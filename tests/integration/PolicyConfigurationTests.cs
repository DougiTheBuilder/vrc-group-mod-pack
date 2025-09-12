using System.Net;
using System.Text.Json;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using Xunit;

namespace VrcGroupGuardian.Tests.Integration;

public class PolicyConfigurationTests : IDisposable
{
    private readonly WireMockServer _mockVrchatApi;
    private readonly WireMockServer _mockInternalApi;
    private readonly HttpClient _vrchatClient;
    private readonly HttpClient _internalClient;
    private const string TestGroupId = "grp_12345678-1234-1234-1234-123456789012";

    public PolicyConfigurationTests()
    {
        _mockVrchatApi = WireMockServer.Start();
        _mockInternalApi = WireMockServer.Start();
        
        _vrchatClient = new HttpClient { BaseAddress = new Uri(_mockVrchatApi.Urls[0]) };
        _internalClient = new HttpClient { BaseAddress = new Uri(_mockInternalApi.Urls[0]) };
    }

    [Fact]
    public async Task ConfigurePolicy_AndSelectGroup_ShouldSetupEnforcementWithPermissionValidation()
    {
        // Arrange - User scenario: Admin sets up policy and selects group for monitoring
        
        // Mock VRChat API - Get group permissions 
        var groupPermissions = new
        {
            permissions = new[] { "group-instance-moderate", "group-instance-manage", "group-member-moderate" }
        };

        _mockVrchatApi
            .Given(Request.Create().WithPath($"/api/1/groups/{TestGroupId}/permissions").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200)
                .WithBody(JsonSerializer.Serialize(groupPermissions)));

        // Mock Internal API - Group selection
        var groupInfo = new
        {
            groupId = TestGroupId,
            groupName = "Test Moderation Group",
            memberCount = 150,
            description = "VRChat moderation group"
        };

        _mockInternalApi
            .Given(Request.Create().WithPath("/groups/select").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200)
                .WithBody(JsonSerializer.Serialize(groupInfo)));

        // Mock Internal API - Policy configuration
        var policySettings = new
        {
            enforcementEnabled = true,
            gracePeriodSeconds = 180,
            pollingIntervalSeconds = 60,
            notificationsEnabled = true,
            rateLimitRequestsPerMinute = 20,
            cacheExpiryMinutes = 15
        };

        _mockInternalApi
            .Given(Request.Create().WithPath("/enforcement/policy").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200)
                .WithBody("{\"message\":\"Policy updated successfully\"}"));

        _mockInternalApi
            .Given(Request.Create().WithPath("/enforcement/policy").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200)
                .WithBody(JsonSerializer.Serialize(policySettings)));

        // Act - This will fail because GroupGuardianOrchestrator doesn't exist yet
        var orchestrator = new GroupGuardianOrchestrator(_vrchatClient, _internalClient);
        
        // Step 1: Select group and validate permissions
        var groupSelection = await orchestrator.SelectGroupForMonitoringAsync(TestGroupId);
        
        // Step 2: Configure enforcement policy
        var policyConfig = new PolicyConfiguration
        {
            EnforcementEnabled = true,
            GracePeriodSeconds = 180,
            PollingIntervalSeconds = 60,
            NotificationsEnabled = true
        };
        
        var policyResult = await orchestrator.ConfigurePolicyAsync(policyConfig);
        
        // Step 3: Verify setup is complete and active
        var setupStatus = await orchestrator.GetSetupStatusAsync();

        // Assert
        Assert.NotNull(groupSelection);
        Assert.True(groupSelection.Success);
        Assert.Equal("Test Moderation Group", groupSelection.GroupName);
        Assert.True(groupSelection.HasRequiredPermissions);
        Assert.Contains("group-instance-moderate", groupSelection.UserPermissions);

        Assert.NotNull(policyResult);
        Assert.True(policyResult.Success);
        Assert.True(policyResult.EnforcementEnabled);
        Assert.Equal(180, policyResult.GracePeriodSeconds);

        Assert.NotNull(setupStatus);
        Assert.True(setupStatus.IsConfigured);
        Assert.True(setupStatus.HasValidGroup);
        Assert.True(setupStatus.HasValidPermissions);
        Assert.True(setupStatus.EnforcementActive);
    }

    [Fact]
    public async Task ConfigurePolicy_WithInsufficientPermissions_ShouldFailWithValidationError()
    {
        // Arrange - User has limited permissions (member only)
        var limitedPermissions = new
        {
            permissions = new[] { "group-member-view" } // Missing instance management permissions
        };

        _mockVrchatApi
            .Given(Request.Create().WithPath($"/api/1/groups/{TestGroupId}/permissions").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200)
                .WithBody(JsonSerializer.Serialize(limitedPermissions)));

        // Act & Assert - This will fail because GroupGuardianOrchestrator doesn't exist yet
        var orchestrator = new GroupGuardianOrchestrator(_vrchatClient, _internalClient);
        
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => orchestrator.SelectGroupForMonitoringAsync(TestGroupId));
        
        Assert.Contains("Insufficient permissions", exception.Message);
        Assert.Contains("group-instance-moderate", exception.Message);
    }

    [Fact]
    public async Task ConfigurePolicy_WithInvalidSettings_ShouldFailValidation()
    {
        // Arrange - Valid group permissions but invalid policy settings
        var groupPermissions = new
        {
            permissions = new[] { "group-instance-moderate", "group-instance-manage" }
        };

        _mockVrchatApi
            .Given(Request.Create().WithPath($"/api/1/groups/{TestGroupId}/permissions").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200)
                .WithBody(JsonSerializer.Serialize(groupPermissions)));

        _mockInternalApi
            .Given(Request.Create().WithPath("/groups/select").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200)
                .WithBody($"{{\"groupId\":\"{TestGroupId}\",\"groupName\":\"Test Group\"}}"));

        // Mock policy update with validation error
        _mockInternalApi
            .Given(Request.Create().WithPath("/enforcement/policy").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(400)
                .WithBody("{\"error\":\"Grace period must be between 60 and 300 seconds\"}"));

        // Act & Assert - This will fail because GroupGuardianOrchestrator doesn't exist yet
        var orchestrator = new GroupGuardianOrchestrator(_vrchatClient, _internalClient);
        
        await orchestrator.SelectGroupForMonitoringAsync(TestGroupId);
        
        var invalidPolicy = new PolicyConfiguration
        {
            EnforcementEnabled = true,
            GracePeriodSeconds = 30, // Invalid - too short
            PollingIntervalSeconds = 60,
            NotificationsEnabled = true
        };
        
        var exception = await Assert.ThrowsAsync<ArgumentException>(
            () => orchestrator.ConfigurePolicyAsync(invalidPolicy));
        
        Assert.Contains("Grace period must be between 60 and 300 seconds", exception.Message);
    }

    [Fact]
    public async Task ReconfigurePolicy_ShouldUpdateExistingSettings()
    {
        // Arrange - System already configured, user wants to update settings
        _mockVrchatApi
            .Given(Request.Create().WithPath($"/api/1/groups/{TestGroupId}/permissions").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200)
                .WithBody("{\"permissions\":[\"group-instance-moderate\"]}"));

        _mockInternalApi
            .Given(Request.Create().WithPath("/groups/select").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200)
                .WithBody($"{{\"groupId\":\"{TestGroupId}\",\"groupName\":\"Test Group\"}}"));

        // Initial policy
        var initialPolicy = new
        {
            enforcementEnabled = true,
            gracePeriodSeconds = 180,
            pollingIntervalSeconds = 60,
            notificationsEnabled = true,
            rateLimitRequestsPerMinute = 20,
            cacheExpiryMinutes = 15
        };

        // Updated policy
        var updatedPolicy = new
        {
            enforcementEnabled = true,
            gracePeriodSeconds = 240, // Changed
            pollingIntervalSeconds = 45, // Changed  
            notificationsEnabled = false, // Changed
            rateLimitRequestsPerMinute = 15, // Changed
            cacheExpiryMinutes = 30 // Changed
        };

        _mockInternalApi
            .Given(Request.Create().WithPath("/enforcement/policy").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200)
                .WithBody(JsonSerializer.Serialize(initialPolicy)));

        _mockInternalApi
            .Given(Request.Create().WithPath("/enforcement/policy").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200)
                .WithBody("{\"message\":\"Policy updated successfully\"}"));

        // Act - This will fail because GroupGuardianOrchestrator doesn't exist yet
        var orchestrator = new GroupGuardianOrchestrator(_vrchatClient, _internalClient);
        
        await orchestrator.SelectGroupForMonitoringAsync(TestGroupId);
        
        var currentPolicy = await orchestrator.GetCurrentPolicyAsync();
        Assert.Equal(180, currentPolicy.GracePeriodSeconds);
        
        var newPolicyConfig = new PolicyConfiguration
        {
            EnforcementEnabled = true,
            GracePeriodSeconds = 240,
            PollingIntervalSeconds = 45,
            NotificationsEnabled = false
        };
        
        var result = await orchestrator.ConfigurePolicyAsync(newPolicyConfig);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.Equal(240, result.GracePeriodSeconds);
        Assert.Equal(45, result.PollingIntervalSeconds);
        Assert.False(result.NotificationsEnabled);
    }

    [Fact]
    public async Task DisableEnforcement_ShouldStopPolicyEngine()
    {
        // Arrange - User wants to disable enforcement while keeping configuration
        _mockVrchatApi
            .Given(Request.Create().WithPath($"/api/1/groups/{TestGroupId}/permissions").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200)
                .WithBody("{\"permissions\":[\"group-instance-moderate\"]}"));

        _mockInternalApi
            .Given(Request.Create().WithPath("/groups/select").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200)
                .WithBody($"{{\"groupId\":\"{TestGroupId}\",\"groupName\":\"Test Group\"}}"));

        var disabledPolicy = new
        {
            enforcementEnabled = false, // Disabled
            gracePeriodSeconds = 180,
            pollingIntervalSeconds = 60,
            notificationsEnabled = true,
            rateLimitRequestsPerMinute = 20,
            cacheExpiryMinutes = 15
        };

        _mockInternalApi
            .Given(Request.Create().WithPath("/enforcement/policy").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200)
                .WithBody("{\"message\":\"Policy updated successfully\"}"));

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

        _mockInternalApi
            .Given(Request.Create().WithPath("/enforcement/status").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200)
                .WithBody(JsonSerializer.Serialize(inactiveStatus)));

        // Act - This will fail because GroupGuardianOrchestrator doesn't exist yet
        var orchestrator = new GroupGuardianOrchestrator(_vrchatClient, _internalClient);
        
        await orchestrator.SelectGroupForMonitoringAsync(TestGroupId);
        
        var disableConfig = new PolicyConfiguration
        {
            EnforcementEnabled = false,
            GracePeriodSeconds = 180,
            PollingIntervalSeconds = 60,
            NotificationsEnabled = true
        };
        
        var result = await orchestrator.ConfigurePolicyAsync(disableConfig);
        var status = await orchestrator.GetEnforcementStatusAsync();

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.False(result.EnforcementEnabled);

        Assert.NotNull(status);
        Assert.False(status.Active);
        Assert.Equal(0, status.PoliciesChecked);
        Assert.Contains("Enforcement disabled", status.ErrorMessage);
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
public class GroupGuardianOrchestrator
{
    private readonly HttpClient _vrchatClient;
    private readonly HttpClient _internalClient;

    public GroupGuardianOrchestrator(HttpClient vrchatClient, HttpClient internalClient)
    {
        _vrchatClient = vrchatClient;
        _internalClient = internalClient;
    }

    public async Task<GroupSelectionResult> SelectGroupForMonitoringAsync(string groupId)
    {
        throw new NotImplementedException("GroupGuardianOrchestrator.SelectGroupForMonitoringAsync not implemented yet");
    }

    public async Task<PolicyConfigurationResult> ConfigurePolicyAsync(PolicyConfiguration config)
    {
        throw new NotImplementedException("GroupGuardianOrchestrator.ConfigurePolicyAsync not implemented yet");
    }

    public async Task<SetupStatus> GetSetupStatusAsync()
    {
        throw new NotImplementedException("GroupGuardianOrchestrator.GetSetupStatusAsync not implemented yet");
    }

    public async Task<PolicyConfiguration> GetCurrentPolicyAsync()
    {
        throw new NotImplementedException("GroupGuardianOrchestrator.GetCurrentPolicyAsync not implemented yet");
    }

    public async Task<EnforcementStatus> GetEnforcementStatusAsync()
    {
        throw new NotImplementedException("GroupGuardianOrchestrator.GetEnforcementStatusAsync not implemented yet");
    }
}

public class PolicyConfiguration
{
    public bool EnforcementEnabled { get; set; }
    public int GracePeriodSeconds { get; set; }
    public int PollingIntervalSeconds { get; set; }
    public bool NotificationsEnabled { get; set; }
    public int RateLimitRequestsPerMinute { get; set; } = 20;
    public int CacheExpiryMinutes { get; set; } = 15;
}

public class GroupSelectionResult
{
    public bool Success { get; set; }
    public string GroupName { get; set; } = string.Empty;
    public bool HasRequiredPermissions { get; set; }
    public List<string> UserPermissions { get; set; } = new();
}

public class PolicyConfigurationResult
{
    public bool Success { get; set; }
    public bool EnforcementEnabled { get; set; }
    public int GracePeriodSeconds { get; set; }
    public int PollingIntervalSeconds { get; set; }
    public bool NotificationsEnabled { get; set; }
}

public class SetupStatus
{
    public bool IsConfigured { get; set; }
    public bool HasValidGroup { get; set; }
    public bool HasValidPermissions { get; set; }
    public bool EnforcementActive { get; set; }
}

public class EnforcementStatus
{
    public bool Active { get; set; }
    public int PoliciesChecked { get; set; }
    public int ViolationsFound { get; set; }
    public string? ErrorMessage { get; set; }
}