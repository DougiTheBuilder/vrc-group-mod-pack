using Xunit;
using Microsoft.Extensions.Logging;
using Moq;
using VrcGroupGuardian.Infrastructure;

namespace VrcGroupGuardian.Tests.Unit;

public class RateLimitTests
{
    private readonly Mock<ILogger<RateLimitService>> _mockLogger;
    private readonly RateLimitService _rateLimitService;

    public RateLimitTests()
    {
        _mockLogger = new Mock<ILogger<RateLimitService>>();
        _rateLimitService = new RateLimitService(_mockLogger.Object);
    }

    [Fact]
    public async Task CheckRateLimitAsync_InitialRequest_ReturnsTrue()
    {
        // Act
        var result = await _rateLimitService.CheckRateLimitAsync("test-endpoint");

        // Assert
        Assert.True(result.IsAllowed);
        Assert.True(result.RemainingRequests > 0);
        Assert.True(result.ResetTime > DateTime.UtcNow);
    }

    [Fact]
    public async Task CheckRateLimitAsync_WithinLimits_ReturnsTrue()
    {
        // Arrange
        const string endpoint = "test-endpoint";

        // Act - Make several requests within default limit (20 requests per minute)
        for (int i = 0; i < 5; i++)
        {
            var result = await _rateLimitService.CheckRateLimitAsync(endpoint);
            Assert.True(result.IsAllowed);
        }

        // Final check
        var finalResult = await _rateLimitService.CheckRateLimitAsync(endpoint);

        // Assert
        Assert.True(finalResult.IsAllowed);
        Assert.Equal(14, finalResult.RemainingRequests); // 20 - 6 = 14
    }

    [Fact]
    public async Task CheckRateLimitAsync_ExceedsLimit_ReturnsFalse()
    {
        // Arrange
        const string endpoint = "test-endpoint";
        
        // Configure a low limit for testing
        _rateLimitService.ConfigureEndpoint(endpoint, 3, TimeSpan.FromMinutes(1));

        // Act - Exceed the limit
        for (int i = 0; i < 3; i++)
        {
            await _rateLimitService.CheckRateLimitAsync(endpoint);
        }
        
        var result = await _rateLimitService.CheckRateLimitAsync(endpoint);

        // Assert
        Assert.False(result.IsAllowed);
        Assert.Equal(0, result.RemainingRequests);
        Assert.NotNull(result.RetryAfter);
        Assert.True(result.RetryAfter > TimeSpan.Zero);
    }

    [Fact]
    public async Task CheckRateLimitAsync_DifferentEndpoints_IndependentLimits()
    {
        // Arrange
        const string endpoint1 = "endpoint1";
        const string endpoint2 = "endpoint2";
        
        _rateLimitService.ConfigureEndpoint(endpoint1, 2, TimeSpan.FromMinutes(1));
        _rateLimitService.ConfigureEndpoint(endpoint2, 2, TimeSpan.FromMinutes(1));

        // Act - Exhaust limit for endpoint1
        await _rateLimitService.CheckRateLimitAsync(endpoint1);
        await _rateLimitService.CheckRateLimitAsync(endpoint1);
        
        var endpoint1Result = await _rateLimitService.CheckRateLimitAsync(endpoint1);
        var endpoint2Result = await _rateLimitService.CheckRateLimitAsync(endpoint2);

        // Assert
        Assert.False(endpoint1Result.IsAllowed);
        Assert.True(endpoint2Result.IsAllowed);
    }

    [Theory]
    [InlineData(1, 60)]  // 1 request per minute
    [InlineData(10, 30)] // 10 requests per 30 seconds
    [InlineData(100, 300)] // 100 requests per 5 minutes
    public void ConfigureEndpoint_ValidConfiguration_UpdatesSettings(int maxRequests, int windowSeconds)
    {
        // Arrange
        const string endpoint = "test-endpoint";
        var window = TimeSpan.FromSeconds(windowSeconds);

        // Act
        _rateLimitService.ConfigureEndpoint(endpoint, maxRequests, window);

        // Assert - Verify by checking initial state
        var result = _rateLimitService.CheckRateLimitAsync(endpoint).Result;
        Assert.True(result.IsAllowed);
        Assert.Equal(maxRequests - 1, result.RemainingRequests);
    }

    [Fact]
    public void ConfigureEndpoint_InvalidMaxRequests_ThrowsException()
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() =>
            _rateLimitService.ConfigureEndpoint("test", 0, TimeSpan.FromMinutes(1)));
        
        Assert.Throws<ArgumentException>(() =>
            _rateLimitService.ConfigureEndpoint("test", -1, TimeSpan.FromMinutes(1)));
    }

    [Fact]
    public void ConfigureEndpoint_InvalidWindow_ThrowsException()
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() =>
            _rateLimitService.ConfigureEndpoint("test", 10, TimeSpan.Zero));
        
        Assert.Throws<ArgumentException>(() =>
            _rateLimitService.ConfigureEndpoint("test", 10, TimeSpan.FromSeconds(-1)));
    }

    [Fact]
    public async Task ResetLimitsAsync_ResetsAllEndpoints()
    {
        // Arrange
        const string endpoint1 = "endpoint1";
        const string endpoint2 = "endpoint2";
        
        _rateLimitService.ConfigureEndpoint(endpoint1, 1, TimeSpan.FromMinutes(1));
        _rateLimitService.ConfigureEndpoint(endpoint2, 1, TimeSpan.FromMinutes(1));

        // Exhaust limits
        await _rateLimitService.CheckRateLimitAsync(endpoint1);
        await _rateLimitService.CheckRateLimitAsync(endpoint2);

        // Verify limits are exhausted
        var result1Before = await _rateLimitService.CheckRateLimitAsync(endpoint1);
        var result2Before = await _rateLimitService.CheckRateLimitAsync(endpoint2);
        Assert.False(result1Before.IsAllowed);
        Assert.False(result2Before.IsAllowed);

        // Act
        await _rateLimitService.ResetLimitsAsync();

        // Assert
        var result1After = await _rateLimitService.CheckRateLimitAsync(endpoint1);
        var result2After = await _rateLimitService.CheckRateLimitAsync(endpoint2);
        Assert.True(result1After.IsAllowed);
        Assert.True(result2After.IsAllowed);
    }

    [Fact]
    public async Task ResetLimitsAsync_SpecificEndpoint_ResetsOnlyThatEndpoint()
    {
        // Arrange
        const string endpoint1 = "endpoint1";
        const string endpoint2 = "endpoint2";
        
        _rateLimitService.ConfigureEndpoint(endpoint1, 1, TimeSpan.FromMinutes(1));
        _rateLimitService.ConfigureEndpoint(endpoint2, 1, TimeSpan.FromMinutes(1));

        // Exhaust both limits
        await _rateLimitService.CheckRateLimitAsync(endpoint1);
        await _rateLimitService.CheckRateLimitAsync(endpoint2);

        // Act - Reset only endpoint1
        await _rateLimitService.ResetLimitsAsync(endpoint1);

        // Assert
        var result1 = await _rateLimitService.CheckRateLimitAsync(endpoint1);
        var result2 = await _rateLimitService.CheckRateLimitAsync(endpoint2);
        
        Assert.True(result1.IsAllowed);  // Should be reset
        Assert.False(result2.IsAllowed); // Should still be limited
    }

    [Fact]
    public void GetRateLimitInfo_ReturnsCurrentState()
    {
        // Arrange
        const string endpoint = "test-endpoint";
        _rateLimitService.ConfigureEndpoint(endpoint, 10, TimeSpan.FromMinutes(1));
        
        // Make a few requests
        _rateLimitService.CheckRateLimitAsync(endpoint).Wait();
        _rateLimitService.CheckRateLimitAsync(endpoint).Wait();

        // Act
        var info = _rateLimitService.GetRateLimitInfo(endpoint);

        // Assert
        Assert.NotNull(info);
        Assert.Equal(8, info.RemainingRequests); // 10 - 2 = 8
        Assert.Equal(10, info.MaxRequests);
        Assert.True(info.ResetTime > DateTime.UtcNow);
    }

    [Fact]
    public void GetRateLimitInfo_NonExistentEndpoint_ReturnsNull()
    {
        // Act
        var info = _rateLimitService.GetRateLimitInfo("non-existent");

        // Assert
        Assert.Null(info);
    }

    [Fact]
    public async Task TokenBucket_RefillOverTime_AllowsMoreRequests()
    {
        // Arrange
        const string endpoint = "test-endpoint";
        
        // Configure with 2 requests per 2 seconds for faster testing
        _rateLimitService.ConfigureEndpoint(endpoint, 2, TimeSpan.FromSeconds(2));

        // Exhaust the bucket
        await _rateLimitService.CheckRateLimitAsync(endpoint);
        await _rateLimitService.CheckRateLimitAsync(endpoint);
        
        var exhaustedResult = await _rateLimitService.CheckRateLimitAsync(endpoint);
        Assert.False(exhaustedResult.IsAllowed);

        // Act - Wait for refill (simulate time passage)
        await Task.Delay(2100); // Wait slightly more than window

        // Assert
        var refillResult = await _rateLimitService.CheckRateLimitAsync(endpoint);
        Assert.True(refillResult.IsAllowed);
    }

    [Fact]
    public async Task ConcurrentRequests_ThreadSafe()
    {
        // Arrange
        const string endpoint = "concurrent-test";
        const int concurrentRequests = 50;
        const int maxAllowed = 20;
        
        _rateLimitService.ConfigureEndpoint(endpoint, maxAllowed, TimeSpan.FromMinutes(1));

        // Act - Make concurrent requests
        var tasks = Enumerable.Range(0, concurrentRequests)
            .Select(_ => _rateLimitService.CheckRateLimitAsync(endpoint))
            .ToArray();

        var results = await Task.WhenAll(tasks);

        // Assert
        var allowedCount = results.Count(r => r.IsAllowed);
        var blockedCount = results.Count(r => !r.IsAllowed);

        Assert.Equal(maxAllowed, allowedCount);
        Assert.Equal(concurrentRequests - maxAllowed, blockedCount);
    }

    [Fact]
    public void GetAllEndpointStats_ReturnsComprehensiveInfo()
    {
        // Arrange
        _rateLimitService.ConfigureEndpoint("endpoint1", 10, TimeSpan.FromMinutes(1));
        _rateLimitService.ConfigureEndpoint("endpoint2", 20, TimeSpan.FromMinutes(2));
        
        _rateLimitService.CheckRateLimitAsync("endpoint1").Wait();
        _rateLimitService.CheckRateLimitAsync("endpoint2").Wait();
        _rateLimitService.CheckRateLimitAsync("endpoint2").Wait();

        // Act
        var allStats = _rateLimitService.GetAllEndpointStats();

        // Assert
        Assert.Equal(2, allStats.Count);
        
        var endpoint1Stats = allStats["endpoint1"];
        Assert.Equal(9, endpoint1Stats.RemainingRequests);
        Assert.Equal(10, endpoint1Stats.MaxRequests);
        
        var endpoint2Stats = allStats["endpoint2"];
        Assert.Equal(18, endpoint2Stats.RemainingRequests);
        Assert.Equal(20, endpoint2Stats.MaxRequests);
    }
}