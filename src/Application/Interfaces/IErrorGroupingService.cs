using Domain.Models;

namespace Application.Interfaces;

public interface IErrorGroupingService
{
    Task<Guid?> HandleErrorGroupAsync(LogEntry log);
}
