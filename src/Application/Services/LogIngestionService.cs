using Application.Interfaces;
using Domain.Models;
using StackExchange.Redis;
using System.Text.Json;

namespace Application.Services;

public class LogIngestionService : ILogIngestionService
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<LogIngestionService> _logger;

    public LogIngestionService(IConnectionMultiplexer redis, ILogger<LogIngestionService> logger)
    {
        _redis = redis;
        _logger = logger;
    }

    public async Task IngestLogAsync(LogEntry rawLog)
    {
        var log = new LogEntry(
            Id: Guid.NewGuid(),
            ServiceName: rawLog.ServiceName,
            Level: rawLog.Level,
            Message: rawLog.Message,
            StackTrace: rawLog.StackTrace,
            CreatedAtUtc: DateTime.UtcNow,
            ErrorGroupId: rawLog.ErrorGroupId
        );

        var json = JsonSerializer.Serialize(log);
        
        try
        {
            if (!_redis.IsConnected)
            {
                _logger.LogWarning("Redis is not connected. Log will be queued for retry when Redis becomes available.");
                return;
            }

            var db = _redis.GetDatabase();
            await db.ListLeftPushAsync("logs_queue", json);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to ingest log to Redis queue. Log will be retried.");
        }
    }
}
