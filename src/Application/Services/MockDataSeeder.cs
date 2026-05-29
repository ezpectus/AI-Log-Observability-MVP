using Domain.Enums;
using Domain.Models;

namespace Application.Services;

public class MockDataSeeder
{
    private static readonly Random _random = new Random();

    private static readonly string[] ServiceNames = new[]
    {
        "LLMOrchestrator",
        "TokenCounter",
        "LatencyMonitor",
        "DatabaseConnection",
        "ModelInference",
        "APIGateway",
        "CacheService",
        "MetricsAggregator"
    };

    private static readonly (LogLevel Level, string Message)[] MockMessages = new[]
    {
        // Info level - successful operations
        (LogLevel.Info, "Model inference successful - processed 256 tokens in 1.24s"),
        (LogLevel.Info, "Token count updated: total usage 15.2K tokens this hour"),
        (LogLevel.Info, "Cache hit ratio: 87% - performance optimal"),
        (LogLevel.Info, "API request completed successfully - response time 234ms"),
        (LogLevel.Info, "Database connection pool refreshed - 5/10 connections active"),
        (LogLevel.Info, "Batch inference completed: 42 samples processed"),
        (LogLevel.Info, "Health check passed - all services operational"),
        (LogLevel.Info, "Model weights loaded successfully - version 2.1.0"),

        // Warning level - potential issues
        (LogLevel.Warning, "Token limit reached warning - 85% of daily quota used"),
        (LogLevel.Warning, "High latency detected - avg response time 1.2s (threshold: 800ms)"),
        (LogLevel.Warning, "Cache miss ratio increased to 23% - consider optimization"),
        (LogLevel.Warning, "PostgreSQL connection timeout warning - retry attempt 1/3"),
        (LogLevel.Warning, "Memory usage at 78% - GC triggered"),
        (LogLevel.Warning, "Slow query detected - SELECT took 2.3s (threshold: 1s)"),
        (LogLevel.Warning, "Network latency spike detected - 450ms jitter observed"),
        (LogLevel.Warning, "Model inference took longer than expected - 3.4s vs avg 1.2s"),

        // Error level - failures that need attention
        (LogLevel.Error, "Model inference failed - OOM error encountered"),
        (LogLevel.Error, "Database connection timeout - max retries exceeded"),
        (LogLevel.Error, "Cache serialization error - unable to serialize response"),
        (LogLevel.Error, "API validation failed - invalid token format provided"),
        (LogLevel.Error, "File not found - model checkpoint missing from storage"),
        (LogLevel.Error, "Network error: connection reset by peer"),
        (LogLevel.Error, "Unauthorized access attempt detected - invalid API key"),
        (LogLevel.Error, "Rate limit exceeded - client 192.168.1.105 blocked for 60s"),

        // Critical level - severe issues
        (LogLevel.Critical, "CRITICAL: Service unavailable - all model inference endpoints down"),
        (LogLevel.Critical, "CRITICAL: Database connection lost - unable to persist logs"),
        (LogLevel.Critical, "CRITICAL: Redis cache failure - restart required"),
        (LogLevel.Critical, "CRITICAL: Model loading failed - system degraded mode activated"),
        (LogLevel.Critical, "CRITICAL: API gateway crashed - requests being dropped")
    };

    public static IEnumerable<LogEntry> GenerateMockLogs(int count = 18)
    {
        var logs = new List<LogEntry>();
        var now = DateTime.UtcNow;

        for (int i = 0; i < count; i++)
        {
            // Spread timestamps across the last 30 minutes with some clustering
            var minutesAgo = _random.Next(0, 30);
            var secondsOffset = _random.Next(0, 60);
            var timestamp = now.AddMinutes(-minutesAgo).AddSeconds(-secondsOffset);

            var (level, message) = MockMessages[_random.Next(MockMessages.Length)];
            var serviceName = ServiceNames[_random.Next(ServiceNames.Length)];

            var log = new LogEntry(
                Id: Guid.NewGuid(),
                ServiceName: serviceName,
                Level: level,
                Message: message,
                StackTrace: level >= LogLevel.Error ? GenerateStackTrace() : null,
                CreatedAtUtc: timestamp,
                ErrorGroupId: null
            );

            logs.Add(log);
        }

        // Sort by timestamp descending for more realistic display (newest first)
        return logs.OrderByDescending(l => l.CreatedAtUtc);
    }

    private static string GenerateStackTrace()
    {
        var traces = new[]
        {
            @"at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw() in System.Private.CoreLib.dll
at Application.Services.ModelInferenceService.InvokeModelAsync(String input) in ModelInferenceService.cs:line 145
at Api.Controllers.InferenceController.Infer(String input) in InferenceController.cs:line 89
at lambda_method(Closure, Object)",
            @"at Npgsql.PostgresException.ThrowAsync(DbMessage msg, CancellationToken cancellationToken) in Npgsql.PostgresException.cs:line 61
at Npgsql.PostgresException.<ThrowAsync>g__EatExceptionIfNotShutdown|22_0(Boolean, Func`2 func) in Npgsql.PostgresException.cs:line 89
at Infrastructure.PostgreSql.LogRepository.SaveLogAsync(LogEntry log) in LogRepository.cs:line 23
at lambda_method(Closure, Object)",
            @"at StackExchange.Redis.RedisConnectionPool.GetConnection(String configuration) in RedisConnectionPool.cs:line 112
at StackExchange.Redis.ConnectionMultiplexer.GetConnectionAsync(String configuration) in ConnectionMultiplexer.cs:line 78
at Application.Services.CacheService.GetValueAsync(String key) in CacheService.cs:line 45"
        };

        return traces[_random.Next(traces.Length)];
    }
}
