using Application.Interfaces;
using Application.Services;
using Domain.Enums;
using Domain.Models;
using Infrastructure.PostgreSql;
using Infrastructure.Realtime;
using Microsoft.AspNetCore.SignalR;
using StackExchange.Redis;
using System.Text.Json;

namespace Infrastructure.Background;

public class LogWorker : BackgroundService
{
    private readonly IConnectionMultiplexer _redis;
    private readonly IHubContext<LogHub> _hubContext;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly MetricsAggregator _metricsAggregator;

    public LogWorker(
        IConnectionMultiplexer redis,
        IHubContext<LogHub> hubContext,
        IServiceScopeFactory scopeFactory,
        MetricsAggregator metricsAggregator)
    {
        _redis = redis;
        _hubContext = hubContext;
        _scopeFactory = scopeFactory;
        _metricsAggregator = metricsAggregator;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var db = _redis.GetDatabase();
        
        // Start metrics broadcasting task
        var metricsTask = BroadcastMetricsAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            string? json = await db.ListRightPopAsync("logs_queue");

            if (json != null)
            {
                try
                {
                    var log = JsonSerializer.Deserialize<LogEntry>(json);
                    if (log != null)
                    {
                        // Track metrics
                        _metricsAggregator.TrackLog(log.ServiceName, log.Level.ToString());

                        using (var scope = _scopeFactory.CreateScope())
                        {
                            var repository = scope.ServiceProvider.GetRequiredService<LogRepository>();
                            var errorGroupingService = scope.ServiceProvider.GetRequiredService<IErrorGroupingService>();

                            if (log.Level == LogLevel.Error || log.Level == LogLevel.Critical)
                            {
                                var errorGroupId = await errorGroupingService.HandleErrorGroupAsync(log);
                                log = log with { ErrorGroupId = errorGroupId };
                            }

                            await repository.SaveLogAsync(log);
                        }

                        await _hubContext.Clients.All.SendAsync("ReceiveLog", log, stoppingToken);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error processing log: {ex.Message}");
                }
            }
            else
            {
                await Task.Delay(100, stoppingToken);
            }
        }

        await metricsTask;
    }

    private async Task BroadcastMetricsAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(5000, stoppingToken);

                var metrics = _metricsAggregator.GetMetrics();
                await _hubContext.Clients.All.SendAsync("ReceiveMetrics", metrics, stoppingToken);

                // Check for anomalies
                foreach (var metric in metrics)
                {
                    if (_metricsAggregator.CheckAnomalies(metric.ServiceName))
                    {
                        await _hubContext.Clients.All.SendAsync("ReceiveAlert", new 
                        { 
                            Service = metric.ServiceName, 
                            Message = "🚨 ANOMALY DETECTED: Sudden error spike!" 
                        }, stoppingToken);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error broadcasting metrics: {ex.Message}");
            }
        }
    }
}
