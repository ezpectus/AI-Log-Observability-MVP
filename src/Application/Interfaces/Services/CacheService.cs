using StackExchange.Redis;
using System.Text.Json;

namespace Application.Interfaces.Services;

/// <summary>
/// A resilient caching service that gracefully degrades when Redis is unavailable.
/// All Redis operations are wrapped in try-catch blocks to prevent cache failures
/// from blocking application flow. Designed for No-Docker mode reliability.
/// </summary>
public class CacheService : ICacheService
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<CacheService> _logger;

    public CacheService(IConnectionMultiplexer redis, ILogger<CacheService> logger)
    {
        _redis = redis;
        _logger = logger;
    }

    /// <summary>
    /// Retrieves a cached value by key. Returns null if key doesn't exist or if cache is unavailable.
    /// </summary>
    public async Task<T?> GetValueAsync<T>(string key) where T : class
    {
        try
        {
            if (!_redis.IsConnected)
            {
                _logger.LogWarning("Cache temporary unavailable, bypassing...");
                return null;
            }

            var db = _redis.GetDatabase();
            var value = await db.StringGetAsync(key);

            if (value.IsNullOrEmpty)
            {
                return null;
            }

            try
            {
                return JsonSerializer.Deserialize<T>(value.ToString());
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Failed to deserialize cached value for key: {Key}. Returning null.", key);
                return null;
            }
        }
        catch (RedisConnectionException ex)
        {
            _logger.LogWarning(ex, "Cache temporary unavailable, bypassing...");
            return null;
        }
        catch (TimeoutException ex)
        {
            _logger.LogWarning(ex, "Cache operation timed out, bypassing...");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unexpected error accessing cache for key: {Key}. Bypassing cache.", key);
            return null;
        }
    }

    /// <summary>
    /// Retrieves a cached string value by key. Returns null if key doesn't exist or if cache is unavailable.
    /// </summary>
    public async Task<string?> GetStringAsync(string key)
    {
        try
        {
            if (!_redis.IsConnected)
            {
                _logger.LogWarning("Cache temporary unavailable, bypassing...");
                return null;
            }

            var db = _redis.GetDatabase();
            var value = await db.StringGetAsync(key);

            return value.IsNullOrEmpty ? null : value.ToString();
        }
        catch (RedisConnectionException ex)
        {
            _logger.LogWarning(ex, "Cache temporary unavailable, bypassing...");
            return null;
        }
        catch (TimeoutException ex)
        {
            _logger.LogWarning(ex, "Cache operation timed out, bypassing...");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unexpected error accessing cache for key: {Key}. Bypassing cache.", key);
            return null;
        }
    }

    /// <summary>
    /// Sets a cached value with an optional expiration. Silently fails if cache is unavailable.
    /// </summary>
    public async Task SetValueAsync<T>(string key, T value, TimeSpan? expiration = null) where T : class
    {
        try
        {
            if (!_redis.IsConnected)
            {
                _logger.LogWarning("Cache temporary unavailable, bypassing...");
                return;
            }

            var json = JsonSerializer.Serialize(value);
            var db = _redis.GetDatabase();
            await db.StringSetAsync(key, json, expiration);
        }
        catch (RedisConnectionException ex)
        {
            _logger.LogWarning(ex, "Cache temporary unavailable, bypassing...");
        }
        catch (TimeoutException ex)
        {
            _logger.LogWarning(ex, "Cache operation timed out, bypassing...");
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to serialize value for cache key: {Key}. Bypassing cache.", key);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unexpected error writing to cache for key: {Key}. Bypassing cache.", key);
        }
    }

    /// <summary>
    /// Sets a cached string value with an optional expiration. Silently fails if cache is unavailable.
    /// </summary>
    public async Task SetStringAsync(string key, string value, TimeSpan? expiration = null)
    {
        try
        {
            if (!_redis.IsConnected)
            {
                _logger.LogWarning("Cache temporary unavailable, bypassing...");
                return;
            }

            var db = _redis.GetDatabase();
            await db.StringSetAsync(key, value, expiration);
        }
        catch (RedisConnectionException ex)
        {
            _logger.LogWarning(ex, "Cache temporary unavailable, bypassing...");
        }
        catch (TimeoutException ex)
        {
            _logger.LogWarning(ex, "Cache operation timed out, bypassing...");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unexpected error writing to cache for key: {Key}. Bypassing cache.", key);
        }
    }

    /// <summary>
    /// Deletes a cached key. Silently fails if cache is unavailable.
    /// </summary>
    public async Task DeleteAsync(string key)
    {
        try
        {
            if (!_redis.IsConnected)
            {
                _logger.LogWarning("Cache temporary unavailable, bypassing...");
                return;
            }

            var db = _redis.GetDatabase();
            await db.KeyDeleteAsync(key);
        }
        catch (RedisConnectionException ex)
        {
            _logger.LogWarning(ex, "Cache temporary unavailable, bypassing...");
        }
        catch (TimeoutException ex)
        {
            _logger.LogWarning(ex, "Cache operation timed out, bypassing...");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unexpected error deleting cache key: {Key}. Bypassing cache.", key);
        }
    }

    /// <summary>
    /// Checks if a key exists in the cache. Returns false if cache is unavailable.
    /// </summary>
    public async Task<bool> KeyExistsAsync(string key)
    {
        try
        {
            if (!_redis.IsConnected)
            {
                _logger.LogWarning("Cache temporary unavailable, bypassing...");
                return false;
            }

            var db = _redis.GetDatabase();
            return await db.KeyExistsAsync(key);
        }
        catch (RedisConnectionException ex)
        {
            _logger.LogWarning(ex, "Cache temporary unavailable, bypassing...");
            return false;
        }
        catch (TimeoutException ex)
        {
            _logger.LogWarning(ex, "Cache operation timed out, bypassing...");
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unexpected error checking cache key: {Key}. Bypassing cache.", key);
            return false;
        }
    }
}
