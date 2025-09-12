using System.Collections.Concurrent;
using System.Runtime.Caching;
using Serilog;

namespace VrcGroupGuardian.Infrastructure;

public interface ICacheService
{
    Task<T?> GetAsync<T>(string key) where T : class;
    Task SetAsync<T>(string key, T value, TimeSpan? expiry = null) where T : class;
    Task RemoveAsync(string key);
    Task ClearAsync();
    Task<T> GetOrSetAsync<T>(string key, Func<Task<T>> factory, TimeSpan? expiry = null) where T : class;
    CacheStatistics GetStatistics();
}

public class CacheService : ICacheService, IDisposable
{
    private readonly MemoryCache _cache;
    private readonly ILogger _logger = Log.ForContext<CacheService>();
    private readonly ConcurrentDictionary<string, DateTime> _accessTimes = new();
    private readonly CacheStatistics _statistics = new();
    private readonly Timer _cleanupTimer;

    public CacheService()
    {
        _cache = new MemoryCache("VrcGroupGuardian");
        
        // Setup periodic cleanup every 5 minutes
        _cleanupTimer = new Timer(PerformCleanup, null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
        
        _logger.Information("CacheService initialized");
    }

    public Task<T?> GetAsync<T>(string key) where T : class
    {
        try
        {
            var cacheKey = GenerateCacheKey<T>(key);
            var value = _cache.Get(cacheKey) as T;
            
            if (value != null)
            {
                _accessTimes[cacheKey] = DateTime.UtcNow;
                Interlocked.Increment(ref _statistics.Hits);
                _logger.Debug("Cache hit for key: {Key}", cacheKey);
            }
            else
            {
                Interlocked.Increment(ref _statistics.Misses);
                _logger.Debug("Cache miss for key: {Key}", cacheKey);
            }

            return Task.FromResult(value);
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Error retrieving from cache for key: {Key}", key);
            Interlocked.Increment(ref _statistics.Errors);
            return Task.FromResult<T?>(null);
        }
    }

    public Task SetAsync<T>(string key, T value, TimeSpan? expiry = null) where T : class
    {
        try
        {
            var cacheKey = GenerateCacheKey<T>(key);
            var policy = CreateCacheItemPolicy(expiry);
            
            _cache.Set(cacheKey, value, policy);
            _accessTimes[cacheKey] = DateTime.UtcNow;
            
            Interlocked.Increment(ref _statistics.Sets);
            _logger.Debug("Cached item with key: {Key}, expiry: {Expiry}", cacheKey, policy.AbsoluteExpiration);
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Error setting cache for key: {Key}", key);
            Interlocked.Increment(ref _statistics.Errors);
        }

        return Task.CompletedTask;
    }

    public Task RemoveAsync(string key)
    {
        try
        {
            var cacheKey = GenerateCacheKey<object>(key);
            _cache.Remove(cacheKey);
            _accessTimes.TryRemove(cacheKey, out _);
            
            Interlocked.Increment(ref _statistics.Removals);
            _logger.Debug("Removed cache item with key: {Key}", cacheKey);
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Error removing from cache for key: {Key}", key);
            Interlocked.Increment(ref _statistics.Errors);
        }

        return Task.CompletedTask;
    }

    public Task ClearAsync()
    {
        try
        {
            var keysToRemove = _accessTimes.Keys.ToList();
            foreach (var key in keysToRemove)
            {
                _cache.Remove(key);
            }
            
            _accessTimes.Clear();
            _statistics.Reset();
            
            _logger.Information("Cache cleared");
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Error clearing cache");
            Interlocked.Increment(ref _statistics.Errors);
        }

        return Task.CompletedTask;
    }

    public async Task<T> GetOrSetAsync<T>(string key, Func<Task<T>> factory, TimeSpan? expiry = null) where T : class
    {
        var cached = await GetAsync<T>(key);
        if (cached != null)
        {
            return cached;
        }

        try
        {
            var value = await factory();
            if (value != null)
            {
                await SetAsync(key, value, expiry);
            }
            return value;
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Error in GetOrSetAsync factory for key: {Key}", key);
            throw;
        }
    }

    public CacheStatistics GetStatistics()
    {
        return new CacheStatistics
        {
            Hits = _statistics.Hits,
            Misses = _statistics.Misses,
            Sets = _statistics.Sets,
            Removals = _statistics.Removals,
            Errors = _statistics.Errors,
            HitRatio = _statistics.TotalRequests > 0 ? (double)_statistics.Hits / _statistics.TotalRequests : 0,
            ItemCount = _accessTimes.Count
        };
    }

    private string GenerateCacheKey<T>(string key)
    {
        return $"{typeof(T).Name}:{key}";
    }

    private CacheItemPolicy CreateCacheItemPolicy(TimeSpan? expiry)
    {
        var policy = new CacheItemPolicy();
        
        if (expiry.HasValue)
        {
            policy.AbsoluteExpiration = DateTimeOffset.UtcNow.Add(expiry.Value);
        }
        else
        {
            // Default expiry of 15 minutes for cached items
            policy.AbsoluteExpiration = DateTimeOffset.UtcNow.AddMinutes(15);
        }
        
        // Add removal callback to update statistics
        policy.RemovedCallback = (args) =>
        {
            _accessTimes.TryRemove(args.CacheItem.Key, out _);
            
            if (args.RemovedReason == CacheEntryRemovedReason.Expired)
            {
                Interlocked.Increment(ref _statistics.Expirations);
                _logger.Debug("Cache item expired: {Key}", args.CacheItem.Key);
            }
        };
        
        return policy;
    }

    private void PerformCleanup(object? state)
    {
        try
        {
            var cutoffTime = DateTime.UtcNow.AddMinutes(-30);
            var keysToRemove = _accessTimes
                .Where(kvp => kvp.Value < cutoffTime)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var key in keysToRemove)
            {
                _cache.Remove(key);
                _accessTimes.TryRemove(key, out _);
            }

            if (keysToRemove.Count > 0)
            {
                _logger.Debug("Cleanup removed {Count} unused cache entries", keysToRemove.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Error during cache cleanup");
        }
    }

    public void Dispose()
    {
        _cleanupTimer?.Dispose();
        _cache?.Dispose();
        _logger.Information("CacheService disposed");
    }
}

public class CacheStatistics
{
    public long Hits { get; set; }
    public long Misses { get; set; }
    public long Sets { get; set; }
    public long Removals { get; set; }
    public long Errors { get; set; }
    public long Expirations { get; set; }
    public long TotalRequests => Hits + Misses;
    public double HitRatio { get; set; }
    public int ItemCount { get; set; }

    public void Reset()
    {
        Hits = 0;
        Misses = 0;
        Sets = 0;
        Removals = 0;
        Errors = 0;
        Expirations = 0;
        HitRatio = 0;
        ItemCount = 0;
    }
}