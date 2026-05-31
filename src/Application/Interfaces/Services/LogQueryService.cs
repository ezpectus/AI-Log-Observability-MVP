using Application.Interfaces;
using DomainLogLevel = Domain.Enums.LogLevel;
using Domain.Models;
using Infrastructure.PostgreSql;

namespace Application.Services;

public class LogQueryService : ILogQueryService
{
    private readonly LogRepository _logRepository;

    public LogQueryService(LogRepository logRepository)
    {
        _logRepository = logRepository;
    }

    public async Task<IEnumerable<LogEntry>> GetLogsAsync(string? service, DomainLogLevel? level, int limit, int offset)
    {
        return await _logRepository.GetLogsAsync(service, level, limit, offset);
    }
}
