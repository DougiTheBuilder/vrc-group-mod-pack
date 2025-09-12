using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace VrcGroupGuardian.Infrastructure;

public interface IRateLimitService
{
    Task WaitForAvailabilityAsync(int requestsPerMinute, CancellationToken cancellationToken = default);
    bool IsRequestAllowed(int requestsPerMinute);
    void ResetLimits();
    RateLimitStatus GetStatus(int requestsPerMinute);
}

public class RateLimitService : IRateLimitService, IDisposable
{
    private readonly ILogger<RateLimitService> _logger;
    private readonly ConcurrentDictionary<int, TokenBucket> _buckets = new();
    private readonly Timer _cleanupTimer;

    public RateLimitService(ILogger<RateLimitService> logger)
    {
        _logger = logger;
        _cleanupTimer = new Timer(CleanupOldBuckets, null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
    }

    public async Task WaitForAvailabilityAsync(int requestsPerMinute, CancellationToken cancellationToken = default)
    {
        var bucket = _buckets.GetOrAdd(requestsPerMinute, rpm => new TokenBucket(rpm, _logger));
        
        while (!bucket.TryConsume() && !cancellationToken.IsCancellationRequested)
        {
            var waitTime = bucket.GetTimeUntilNextToken();
            _logger.LogDebug("Rate limit exceeded for {RequestsPerMinute} RPM, waiting {WaitTime}ms", requestsPerMinute, waitTime.TotalMilliseconds);
            
            await Task.Delay(waitTime, cancellationToken);
        }
    }

    public bool IsRequestAllowed(int requestsPerMinute)
    {
        var bucket = _buckets.GetOrAdd(requestsPerMinute, rpm => new TokenBucket(rpm, _logger));
        return bucket.TryConsume();
    }

    public void ResetLimits()
    {
        _buckets.Clear();
        _logger.LogInformation("Rate limits reset");
    }

    public RateLimitStatus GetStatus(int requestsPerMinute)
    {
        if (_buckets.TryGetValue(requestsPerMinute, out var bucket))
        {
            return new RateLimitStatus
            {
                RequestsPerMinute = requestsPerMinute,
                AvailableTokens = bucket.AvailableTokens,
                TimeUntilRefill = bucket.GetTimeUntilNextToken(),
                LastRefillTime = bucket.LastRefillTime
            };
        }

        return new RateLimitStatus
        {
            RequestsPerMinute = requestsPerMinute,
            AvailableTokens = requestsPerMinute,
            TimeUntilRefill = TimeSpan.Zero,
            LastRefillTime = DateTime.UtcNow
        };
    }

    private void CleanupOldBuckets(object? state)
    {
        var cutoff = DateTime.UtcNow.AddMinutes(-10);
        var keysToRemove = new List<int>();

        foreach (var kvp in _buckets)
        {
            if (kvp.Value.LastRefillTime < cutoff)
            {
                keysToRemove.Add(kvp.Key);
            }
        }

        foreach (var key in keysToRemove)
        {
            _buckets.TryRemove(key, out _);
        }

        if (keysToRemove.Count > 0)
        {
            _logger.LogDebug("Cleaned up {Count} unused rate limit buckets", keysToRemove.Count);
        }
    }

    public void Dispose()
    {
        _cleanupTimer?.Dispose();
        _buckets.Clear();
    }
}

public class TokenBucket
{
    private readonly int _capacity;
    private readonly double _refillRate; // tokens per millisecond
    private readonly ILogger _logger;
    private readonly object _lock = new();
    private double _tokens;
    private DateTime _lastRefill;

    public int AvailableTokens => (int)Math.Floor(_tokens);
    public DateTime LastRefillTime { get; private set; }

    public TokenBucket(int requestsPerMinute, ILogger logger)
    {
        _capacity = requestsPerMinute;
        _refillRate = requestsPerMinute / 60000.0; // Convert to tokens per millisecond
        _logger = logger;
        _tokens = requestsPerMinute;
        _lastRefill = DateTime.UtcNow;
        LastRefillTime = DateTime.UtcNow;
    }

    public bool TryConsume(int tokens = 1)
    {
        lock (_lock)
        {
            RefillTokens();
            
            if (_tokens >= tokens)
            {
                _tokens -= tokens;
                return true;
            }
            
            return false;
        }
    }

    public TimeSpan GetTimeUntilNextToken()
    {
        lock (_lock)
        {
            RefillTokens();
            
            if (_tokens >= 1)
                return TimeSpan.Zero;

            var tokensNeeded = 1 - _tokens;
            var millisecondsToWait = tokensNeeded / _refillRate;
            
            // Add small buffer to avoid tight loops
            return TimeSpan.FromMilliseconds(Math.Max(100, millisecondsToWait));
        }
    }

    private void RefillTokens()
    {
        var now = DateTime.UtcNow;
        var elapsed = (now - _lastRefill).TotalMilliseconds;
        
        if (elapsed > 0)
        {
            var tokensToAdd = elapsed * _refillRate;
            _tokens = Math.Min(_capacity, _tokens + tokensToAdd);
            _lastRefill = now;
            LastRefillTime = now;
        }
    }
}

public class RateLimitStatus
{
    public int RequestsPerMinute { get; set; }
    public int AvailableTokens { get; set; }
    public TimeSpan TimeUntilRefill { get; set; }
    public DateTime LastRefillTime { get; set; }
}