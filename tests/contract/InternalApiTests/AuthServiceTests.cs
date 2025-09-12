using System.Net;
using System.Text.Json;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using Xunit;

namespace VrcGroupGuardian.Tests.Contract.InternalApiTests;

public class AuthServiceTests : IDisposable
{
    private readonly WireMockServer _mockServer;
    private readonly HttpClient _httpClient;

    public AuthServiceTests()
    {
        _mockServer = WireMockServer.Start();
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(_mockServer.Urls[0])
        };
    }

    [Fact]
    public async Task Login_WithValidCredentials_ReturnsAuthResult()
    {
        // Arrange - Mock internal Auth service response
        var loginRequest = new
        {
            username = "testuser",
            password = "testpassword"
        };

        var expectedAuthResult = new
        {
            success = true,
            userId = "usr_12345678-1234-1234-1234-123456789012",
            displayName = "Test User",
            username = "testuser",
            requiresTwoFactor = false,
            tokenExpiry = "2024-01-16T10:30:00.000Z"
        };

        _mockServer
            .Given(Request.Create()
                .WithPath("/auth/login")
                .UsingPost()
                .WithHeader("Content-Type", "application/json")
                .WithBody(JsonSerializer.Serialize(loginRequest)))
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody(JsonSerializer.Serialize(expectedAuthResult)));

        // Act - This will fail because AuthServiceClient doesn't exist yet
        var authService = new AuthServiceClient(_httpClient);
        
        var result = await authService.LoginAsync("testuser", "testpassword");

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.Equal(expectedAuthResult.userId, result.UserId);
        Assert.Equal(expectedAuthResult.displayName, result.DisplayName);
        Assert.False(result.RequiresTwoFactor);
    }

    [Fact]
    public async Task Login_WithTwoFactorRequired_ReturnsRequires2FA()
    {
        // Arrange
        var loginRequest = new
        {
            username = "user2fa",
            password = "password123"
        };

        var twoFactorResult = new
        {
            success = false,
            userId = "usr_87654321-4321-4321-4321-210987654321",
            displayName = "2FA User",
            username = "user2fa",
            requiresTwoFactor = true,
            tokenExpiry = (string?)null
        };

        _mockServer
            .Given(Request.Create()
                .WithPath("/auth/login")
                .UsingPost()
                .WithBody(JsonSerializer.Serialize(loginRequest)))
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody(JsonSerializer.Serialize(twoFactorResult)));

        // Act - This will fail because AuthServiceClient doesn't exist yet
        var authService = new AuthServiceClient(_httpClient);
        
        var result = await authService.LoginAsync("user2fa", "password123");

        // Assert
        Assert.NotNull(result);
        Assert.False(result.Success);
        Assert.True(result.RequiresTwoFactor);
        Assert.Equal(twoFactorResult.userId, result.UserId);
    }

    [Fact]
    public async Task Login_WithTotpCode_ReturnsSuccess()
    {
        // Arrange
        var loginWithTotpRequest = new
        {
            username = "user2fa",
            password = "password123",
            totpCode = "123456"
        };

        var successResult = new
        {
            success = true,
            userId = "usr_87654321-4321-4321-4321-210987654321",
            displayName = "2FA User",
            username = "user2fa",
            requiresTwoFactor = false,
            tokenExpiry = "2024-01-16T10:30:00.000Z"
        };

        _mockServer
            .Given(Request.Create()
                .WithPath("/auth/login")
                .UsingPost()
                .WithBody(JsonSerializer.Serialize(loginWithTotpRequest)))
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody(JsonSerializer.Serialize(successResult)));

        // Act - This will fail because AuthServiceClient doesn't exist yet
        var authService = new AuthServiceClient(_httpClient);
        
        var result = await authService.LoginAsync("user2fa", "password123", "123456");

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.False(result.RequiresTwoFactor);
        Assert.Equal(successResult.userId, result.UserId);
    }

    [Fact]
    public async Task Login_WithInvalidCredentials_Returns401()
    {
        // Arrange
        var invalidLoginRequest = new
        {
            username = "wronguser",
            password = "wrongpassword"
        };

        _mockServer
            .Given(Request.Create()
                .WithPath("/auth/login")
                .UsingPost()
                .WithBody(JsonSerializer.Serialize(invalidLoginRequest)))
            .RespondWith(Response.Create()
                .WithStatusCode(401)
                .WithHeader("Content-Type", "application/json")
                .WithBody("{\"error\":\"Invalid credentials\"}"));

        // Act & Assert - This will fail because AuthServiceClient doesn't exist yet
        var authService = new AuthServiceClient(_httpClient);
        
        var exception = await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => authService.LoginAsync("wronguser", "wrongpassword"));
        
        Assert.Contains("Invalid credentials", exception.Message);
    }

    [Fact]
    public async Task Login_WithLockedAccount_Returns423()
    {
        // Arrange
        var lockedAccountRequest = new
        {
            username = "lockeduser",
            password = "password123"
        };

        _mockServer
            .Given(Request.Create()
                .WithPath("/auth/login")
                .UsingPost()
                .WithBody(JsonSerializer.Serialize(lockedAccountRequest)))
            .RespondWith(Response.Create()
                .WithStatusCode(423)
                .WithHeader("Content-Type", "application/json")
                .WithBody("{\"error\":\"Account locked or requires email verification\"}"));

        // Act & Assert - This will fail because AuthServiceClient doesn't exist yet
        var authService = new AuthServiceClient(_httpClient);
        
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => authService.LoginAsync("lockeduser", "password123"));
        
        Assert.Contains("Account locked", exception.Message);
    }

    [Fact]
    public async Task GetStatus_WhenAuthenticated_ReturnsAuthStatus()
    {
        // Arrange
        var expectedStatus = new
        {
            authenticated = true,
            userId = "usr_12345678-1234-1234-1234-123456789012",
            displayName = "Test User",
            tokenExpiry = "2024-01-16T10:30:00.000Z",
            sessionValid = true
        };

        _mockServer
            .Given(Request.Create()
                .WithPath("/auth/status")
                .UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody(JsonSerializer.Serialize(expectedStatus)));

        // Act - This will fail because AuthServiceClient doesn't exist yet
        var authService = new AuthServiceClient(_httpClient);
        
        var result = await authService.GetStatusAsync();

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Authenticated);
        Assert.Equal(expectedStatus.userId, result.UserId);
        Assert.Equal(expectedStatus.displayName, result.DisplayName);
        Assert.True(result.SessionValid);
    }

    [Fact]
    public async Task GetStatus_WhenNotAuthenticated_ReturnsUnauthenticated()
    {
        // Arrange
        var unauthenticatedStatus = new
        {
            authenticated = false,
            userId = (string?)null,
            displayName = (string?)null,
            tokenExpiry = (string?)null,
            sessionValid = false
        };

        _mockServer
            .Given(Request.Create()
                .WithPath("/auth/status")
                .UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody(JsonSerializer.Serialize(unauthenticatedStatus)));

        // Act - This will fail because AuthServiceClient doesn't exist yet
        var authService = new AuthServiceClient(_httpClient);
        
        var result = await authService.GetStatusAsync();

        // Assert
        Assert.NotNull(result);
        Assert.False(result.Authenticated);
        Assert.Null(result.UserId);
        Assert.False(result.SessionValid);
    }

    [Fact]
    public async Task Logout_WhenAuthenticated_ReturnsSuccess()
    {
        // Arrange
        _mockServer
            .Given(Request.Create()
                .WithPath("/auth/logout")
                .UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody("{\"message\":\"Logout successful\"}"));

        // Act - This will fail because AuthServiceClient doesn't exist yet
        var authService = new AuthServiceClient(_httpClient);
        
        var success = await authService.LogoutAsync();

        // Assert
        Assert.True(success);
    }

    public void Dispose()
    {
        _httpClient?.Dispose();
        _mockServer?.Stop();
        _mockServer?.Dispose();
    }
}

// Placeholder classes that will fail compilation - this is intentional for TDD
public class AuthServiceClient
{
    private readonly HttpClient _httpClient;

    public AuthServiceClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<AuthResultDto> LoginAsync(string username, string password, string? totpCode = null)
    {
        throw new NotImplementedException("AuthServiceClient.LoginAsync not implemented yet");
    }

    public async Task<AuthStatusDto> GetStatusAsync()
    {
        throw new NotImplementedException("AuthServiceClient.GetStatusAsync not implemented yet");
    }

    public async Task<bool> LogoutAsync()
    {
        throw new NotImplementedException("AuthServiceClient.LogoutAsync not implemented yet");
    }
}

public class AuthResultDto
{
    public bool Success { get; set; }
    public string? UserId { get; set; }
    public string? DisplayName { get; set; }
    public string? Username { get; set; }
    public bool RequiresTwoFactor { get; set; }
    public DateTime? TokenExpiry { get; set; }
}

public class AuthStatusDto
{
    public bool Authenticated { get; set; }
    public string? UserId { get; set; }
    public string? DisplayName { get; set; }
    public DateTime? TokenExpiry { get; set; }
    public bool SessionValid { get; set; }
}