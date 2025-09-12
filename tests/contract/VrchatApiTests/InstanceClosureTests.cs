using System.Net;
using System.Text.Json;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using Xunit;

namespace VrcGroupGuardian.Tests.Contract.VrchatApiTests;

public class InstanceClosureTests : IDisposable
{
    private readonly WireMockServer _mockServer;
    private readonly HttpClient _httpClient;
    private const string TestWorldId = "wrld_12345678-1234-1234-1234-123456789012";
    private const string TestInstanceId = "12345";
    private const string FullInstanceId = "wrld_12345678-1234-1234-1234-123456789012:12345~group(grp_12345678-1234-1234-1234-123456789012)";

    public InstanceClosureTests()
    {
        _mockServer = WireMockServer.Start();
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(_mockServer.Urls[0])
        };
    }

    [Fact]
    public async Task CloseInstance_WithValidInstanceId_ReturnsSuccess()
    {
        // Arrange - Mock VRChat API response
        _mockServer
            .Given(Request.Create()
                .WithPath($"/api/1/instances/{TestWorldId}:{TestInstanceId}")
                .UsingDelete()
                .WithHeader("Cookie", "authToken=valid-token"))
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody("{\"message\":\"Instance closed successfully\"}"));

        // Act - This will fail because VrcApiService doesn't exist yet
        var vrcApiService = new VrcApiService(_httpClient);
        _httpClient.DefaultRequestHeaders.Add("Cookie", "authToken=valid-token");
        
        var success = await vrcApiService.CloseInstanceAsync(FullInstanceId);

        // Assert
        Assert.True(success);
    }

    [Fact]
    public async Task CloseInstance_WithInsufficientPermissions_Returns403()
    {
        // Arrange
        _mockServer
            .Given(Request.Create()
                .WithPath($"/api/1/instances/{TestWorldId}:{TestInstanceId}")
                .UsingDelete()
                .WithHeader("Cookie", "authToken=limited-token"))
            .RespondWith(Response.Create()
                .WithStatusCode(403)
                .WithHeader("Content-Type", "application/json")
                .WithBody("{\"error\":\"Insufficient permissions to close instance\"}"));

        // Act & Assert - This will fail because VrcApiService doesn't exist yet
        var vrcApiService = new VrcApiService(_httpClient);
        _httpClient.DefaultRequestHeaders.Add("Cookie", "authToken=limited-token");
        
        var exception = await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => vrcApiService.CloseInstanceAsync(FullInstanceId));
        
        Assert.Contains("Insufficient permissions", exception.Message);
    }

    [Fact]
    public async Task CloseInstance_WithNonExistentInstance_Returns404()
    {
        // Arrange
        var invalidInstanceId = "wrld_invalid-world:99999~group(grp_invalid-group)";
        var invalidWorldId = "wrld_invalid-world";
        var invalidInstanceNumber = "99999";
        
        _mockServer
            .Given(Request.Create()
                .WithPath($"/api/1/instances/{invalidWorldId}:{invalidInstanceNumber}")
                .UsingDelete()
                .WithHeader("Cookie", "authToken=valid-token"))
            .RespondWith(Response.Create()
                .WithStatusCode(404)
                .WithHeader("Content-Type", "application/json")
                .WithBody("{\"error\":\"Instance not found\"}"));

        // Act & Assert - This will fail because VrcApiService doesn't exist yet
        var vrcApiService = new VrcApiService(_httpClient);
        _httpClient.DefaultRequestHeaders.Add("Cookie", "authToken=valid-token");
        
        var exception = await Assert.ThrowsAsync<HttpRequestException>(
            () => vrcApiService.CloseInstanceAsync(invalidInstanceId));
        
        Assert.Contains("Instance not found", exception.Message);
    }

    [Fact]
    public async Task CloseInstance_WithRateLimit_Returns429()
    {
        // Arrange
        _mockServer
            .Given(Request.Create()
                .WithPath($"/api/1/instances/{TestWorldId}:{TestInstanceId}")
                .UsingDelete())
            .RespondWith(Response.Create()
                .WithStatusCode(429)
                .WithHeader("Content-Type", "application/json")
                .WithBody("{\"error\":\"Rate limit exceeded\"}"));

        // Act & Assert - This will fail because VrcApiService doesn't exist yet
        var vrcApiService = new VrcApiService(_httpClient);
        
        var exception = await Assert.ThrowsAsync<HttpRequestException>(
            () => vrcApiService.CloseInstanceAsync(FullInstanceId));
        
        Assert.Contains("Rate limit exceeded", exception.Message);
    }

    [Fact]
    public async Task CloseInstance_WithoutAuthentication_Returns401()
    {
        // Arrange
        _mockServer
            .Given(Request.Create()
                .WithPath($"/api/1/instances/{TestWorldId}:{TestInstanceId}")
                .UsingDelete())
            .RespondWith(Response.Create()
                .WithStatusCode(401)
                .WithHeader("Content-Type", "application/json")
                .WithBody("{\"error\":\"Authentication required\"}"));

        // Act & Assert - This will fail because VrcApiService doesn't exist yet
        var vrcApiService = new VrcApiService(_httpClient);
        
        var exception = await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => vrcApiService.CloseInstanceAsync(FullInstanceId));
        
        Assert.Contains("Authentication required", exception.Message);
    }

    [Fact]
    public async Task CloseInstance_WithMalformedInstanceId_ThrowsArgumentException()
    {
        // Arrange
        var malformedInstanceId = "invalid-format-instance-id";

        // Act & Assert - This will fail because VrcApiService doesn't exist yet
        var vrcApiService = new VrcApiService(_httpClient);
        
        var exception = await Assert.ThrowsAsync<ArgumentException>(
            () => vrcApiService.CloseInstanceAsync(malformedInstanceId));
        
        Assert.Contains("Invalid instance ID format", exception.Message);
    }

    [Theory]
    [InlineData("wrld_12345678-1234-1234-1234-123456789012:12345~group(grp_12345678-1234-1234-1234-123456789012)")]
    [InlineData("wrld_87654321-4321-4321-4321-210987654321:67890~groupplus(grp_87654321-4321-4321-4321-210987654321)")]
    [InlineData("wrld_11111111-1111-1111-1111-111111111111:11111~grouppublic(grp_11111111-1111-1111-1111-111111111111)")]
    public async Task CloseInstance_WithValidInstanceFormats_CallsCorrectEndpoint(string instanceId)
    {
        // Arrange
        var parts = instanceId.Split(':');
        var worldId = parts[0];
        var instancePart = parts[1].Split('~')[0];
        
        _mockServer
            .Given(Request.Create()
                .WithPath($"/api/1/instances/{worldId}:{instancePart}")
                .UsingDelete()
                .WithHeader("Cookie", "authToken=valid-token"))
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody("{\"message\":\"Instance closed successfully\"}"));

        // Act - This will fail because VrcApiService doesn't exist yet
        var vrcApiService = new VrcApiService(_httpClient);
        _httpClient.DefaultRequestHeaders.Add("Cookie", "authToken=valid-token");
        
        var success = await vrcApiService.CloseInstanceAsync(instanceId);

        // Assert
        Assert.True(success);
        
        // Verify the correct endpoint was called
        var logEntries = _mockServer.LogEntries.Where(x => x.RequestMessage.Method == "DELETE").ToList();
        Assert.Single(logEntries);
        Assert.Contains($"{worldId}:{instancePart}", logEntries[0].RequestMessage.Path);
    }

    public void Dispose()
    {
        _httpClient?.Dispose();
        _mockServer?.Stop();
        _mockServer?.Dispose();
    }
}