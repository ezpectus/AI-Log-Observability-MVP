using Application.Services;
using Infrastructure.PostgreSql;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Background;

public class MockDataSeederHostedService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<MockDataSeederHostedService> _logger;

    public MockDataSeederHostedService(IServiceProvider serviceProvider, ILogger<MockDataSeederHostedService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            // Small delay to allow other services to initialize
            await Task.Delay(500, stoppingToken);

            using (var scope = _serviceProvider.CreateScope())
            {
                var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                try
                {
                    // Check if database has any logs already
                    var existingLogsCount = await dbContext.LogEntries.CountAsync(cancellationToken: stoppingToken);

                    if (existingLogsCount == 0)
                    {
                        _logger.LogInformation("Database is empty. Seeding mock data...");

                        var mockLogs = MockDataSeeder.GenerateMockLogs(18).ToList();

                        foreach (var log in mockLogs)
                        {
                            await dbContext.LogEntries.AddAsync(log, cancellationToken: stoppingToken);
                        }

                        await dbContext.SaveChangesAsync(stoppingToken);

                        _logger.LogInformation($"Successfully seeded {mockLogs.Count} mock log entries");
                    }
                    else
                    {
                        _logger.LogInformation($"Database already contains {existingLogsCount} log entries. Skipping seed.");
                    }
                }
                catch (Exception dbEx) when (dbEx is not OperationCanceledException)
                {
                    _logger.LogWarning(dbEx, "Failed to seed database. Using in-memory database fallback. Mock data seeding skipped.");
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("MockDataSeederHostedService was cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during mock data seeding");
        }
    }
}
