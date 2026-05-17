using Domain.Enums;
using Domain.Models;

namespace Application.Interfaces;

public interface ILogQueryService
{
    Task<IEnumerable<LogEntry>> GetLogsAsync(string? service, LogLevel? level, int limit, int offset);
}
