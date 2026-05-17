using Application.Interfaces;
using Domain.Models;
using StackExchange.Redis;
using System.Text.Json;

namespace Application.Services;

public class LogIngestionService : ILogIngestionService
{
    private readonly IConnectionMultiplexer _redis;

    public LogIngestionService(IConnectionMultiplexer redis)
    {
        _redis = redis;
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
        var db = _redis.GetDatabase();
        await db.ListLeftPushAsync("logs_queue", json);
    }
}
