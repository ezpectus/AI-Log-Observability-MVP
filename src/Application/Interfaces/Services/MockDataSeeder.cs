using DomainLogLevel = Domain.Enums.LogLevel;
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

    private static readonly (DomainLogLevel Level, string Message)[] MockMessages = new[]
    {
        // Info level - successful operations
        (DomainLogLevel.Info, "Model inference successful - processed 256 tokens in 1.24s"),
        (DomainLogLevel.Info, "Token count updated: total usage 15.2K tokens this hour"),
        (DomainLogLevel.Info, "Cache hit ratio: 87% - performance optimal"),
        (DomainLogLevel.Info, "API request completed successfully - response time 234ms"),
        (DomainLogLevel.Info, "Database connection pool refreshed - 5/10 connections active"),
        (DomainLogLevel.Info, "Batch inference completed: 42 samples processed"),
        (DomainLogLevel.Info, "Health check passed - all services operational"),
        (DomainLogLevel.Info, "Model weights loaded successfully - version 2.1.0"),

        // Warning level - potential issues
        (DomainLogLevel.Warning, "Token limit reached warning - 85% of daily quota used"),
        (DomainLogLevel.Warning, "High latency detected - avg response time 1.2s (threshold: 800ms)"),
        (DomainLogLevel.Warning, "Cache miss ratio increased to 23% - consider optimization"),
        (DomainLogLevel.Warning, "PostgreSQL connection timeout warning - retry attempt 1/3"),
        (DomainLogLevel.Warning, "Memory usage at 78% - GC triggered"),
        (DomainLogLevel.Warning, "Slow query detected - SELECT took 2.3s (threshold: 1s)"),
        (DomainLogLevel.Warning, "Network latency spike detected - 450ms jitter observed"),
        (DomainLogLevel.Warning, "Model inference took longer than expected - 3.4s vs avg 1.2s"),

        // Error level - failures that need attention
        (DomainLogLevel.Error, "Model inference failed - OOM error encountered"),
        (DomainLogLevel.Error, "Database connection timeout - max retries exceeded"),
        (DomainLogLevel.Error, "Cache serialization error - unable to serialize response"),
        (DomainLogLevel.Error, "API validation failed - invalid token format provided"),
        (DomainLogLevel.Error, "File not found - model checkpoint missing from storage"),
        (DomainLogLevel.Error, "Network error: connection reset by peer"),
        (DomainLogLevel.Error, "Unauthorized access attempt detected - invalid API key"),
        (DomainLogLevel.Error, "Rate limit exceeded - client 192.168.1.105 blocked for 60s"),

        // Critical level - severe issues
        (DomainLogLevel.Critical, "CRITICAL: Service unavailable - all model inference endpoints down"),
        (DomainLogLevel.Critical, "CRITICAL: Database connection lost - unable to persist logs"),
        (DomainLogLevel.Critical, "CRITICAL: Redis cache failure - restart required"),
        (DomainLogLevel.Critical, "CRITICAL: Model loading failed - system degraded mode activated"),
        (DomainLogLevel.Critical, "CRITICAL: API gateway crashed - requests being dropped")
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
                StackTrace: level >= DomainLogLevel.Error ? GenerateStackTrace() : null,
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
