using Domain.Models;

namespace Application.Interfaces;

public interface ILogIngestionService
{
    Task IngestLogAsync(LogEntry log);
}
