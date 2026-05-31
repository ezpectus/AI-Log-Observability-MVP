namespace Application.Interfaces;

/// <summary>
/// Interface for a resilient caching service that gracefully handles cache unavailability.
/// All methods silently fail and return defaults when cache is offline, ensuring
/// the application continues to function without cache dependencies.
/// </summary>
public interface ICacheService
{
    /// <summary>
    /// Retrieves a typed cached value by key.
    /// Returns null if the key doesn't exist or if cache is unavailable.
    /// </summary>
    Task<T?> GetValueAsync<T>(string key) where T : class;

    /// <summary>
    /// Retrieves a string cached value by key.
    /// Returns null if the key doesn't exist or if cache is unavailable.
    /// </summary>
    Task<string?> GetStringAsync(string key);

    /// <summary>
    /// Sets a typed cached value with optional expiration.
    /// Silently fails if cache is unavailable.
    /// </summary>
    Task SetValueAsync<T>(string key, T value, TimeSpan? expiration = null) where T : class;

    /// <summary>
    /// Sets a string cached value with optional expiration.
    /// Silently fails if cache is unavailable.
    /// </summary>
    Task SetStringAsync(string key, string value, TimeSpan? expiration = null);

    /// <summary>
    /// Deletes a cache key. Silently fails if cache is unavailable.
    /// </summary>
    Task DeleteAsync(string key);

    /// <summary>
    /// Checks if a key exists in cache.
    /// Returns false if cache is unavailable.
    /// </summary>
    Task<bool> KeyExistsAsync(string key);
}
