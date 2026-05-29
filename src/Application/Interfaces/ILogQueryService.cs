using DomainLogLevel = Domain.Enums.LogLevel;
using Domain.Models;

namespace Application.Interfaces;

public interface ILogQueryService
{
    Task<IEnumerable<LogEntry>> GetLogsAsync(string? service, DomainLogLevel? level, int limit, int offset);
}
