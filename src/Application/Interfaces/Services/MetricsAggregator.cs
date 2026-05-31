using System.Collections.Concurrent;
using System.Threading;
using Domain.Entities;
using Domain.Enums;

namespace Application.Services;

public class MetricsAggregator
{
    private readonly ConcurrentDictionary<string, ServiceMetricsData> _serviceMetrics = new();
    private readonly object _cleanupLock = new object();

    public void TrackLog(string serviceName, string logLevel)
    {
        var metrics = _serviceMetrics.GetOrAdd(serviceName, _ => new ServiceMetricsData());
        
        var timestamp = DateTime.UtcNow;
        metrics.Logs.Enqueue(new LogEntryData { Timestamp = timestamp, Level = logLevel });
        
        // Atomic increment for log count
        Interlocked.Increment(ref metrics.TotalLogs);
        
        // Remove logs older than 60 seconds with periodic cleanup
        if (metrics.Logs.Count > 1000)
        {
            CleanupOldLogs(metrics, timestamp);
        }
    }

    public List<ServiceMetric> GetMetrics()
    {
        var timestamp = DateTime.UtcNow;
        var metrics = new List<ServiceMetric>();

        foreach (var kvp in _serviceMetrics)
        {
            var serviceName = kvp.Key;
            var data = kvp.Value;

            // Cleanup before calculating metrics
            CleanupOldLogs(data, timestamp);

            // Calculate RPS (logs per second) over last 60 seconds
            var logsLast60Seconds = data.Logs.Count(l => (timestamp - l.Timestamp).TotalSeconds <= 60);
            var rps = logsLast60Seconds / 60;

            // Calculate Error Rate over last 60 seconds
            var errorLogsLast60Seconds = data.Logs.Count(l => 
                (timestamp - l.Timestamp).TotalSeconds <= 60 && 
                (l.Level == "Error" || l.Level == "Critical"));
            var errorRate = logsLast60Seconds > 0 ? (double)errorLogsLast60Seconds / logsLast60Seconds * 100 : 0;

            metrics.Add(new ServiceMetric
            {
                ServiceName = serviceName,
                Timestamp = timestamp,
                Rps = rps,
                ErrorRate = errorRate
            });
        }

        return metrics;
    }

    public bool CheckAnomalies(string serviceName)
    {
        if (!_serviceMetrics.TryGetValue(serviceName, out var data))
        {
            return false;
        }

        var timestamp = DateTime.UtcNow;
        
        // Cleanup before checking anomalies
        CleanupOldLogs(data, timestamp);

        var logsLast10Seconds = data.Logs.Count(l => (timestamp - l.Timestamp).TotalSeconds <= 10);

        if (logsLast10Seconds <= 10)
        {
            return false;
        }

        var errorLogsLast10Seconds = data.Logs.Count(l => 
            (timestamp - l.Timestamp).TotalSeconds <= 10 && 
            (l.Level == "Error" || l.Level == "Critical"));
        
        var errorRate = (double)errorLogsLast10Seconds / logsLast10Seconds * 100;

        return errorRate > 50;
    }

    private void CleanupOldLogs(ServiceMetricsData data, DateTime timestamp)
    {
        lock (_cleanupLock)
        {
            var queue = data.Logs;
            while (queue.TryPeek(out var oldestLog) && (timestamp - oldestLog.Timestamp).TotalSeconds > 60)
            {
                queue.TryDequeue(out _);
                // Atomic decrement for log count
                Interlocked.Decrement(ref data.TotalLogs);
            }
        }
    }

    private class ServiceMetricsData
    {
        public ConcurrentQueue<LogEntryData> Logs { get; } = new();
        public long TotalLogs;
    }

    private class LogEntryData
    {
        public DateTime Timestamp { get; set; }
        public string Level { get; set; } = string.Empty;
    }
}
