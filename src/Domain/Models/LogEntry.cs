using Domain.Entities;
using DomainLogLevel = Domain.Enums.LogLevel;

namespace Domain.Models;

public record LogEntry(
    Guid Id,
    string ServiceName,
    DomainLogLevel Level,
    string Message,
    string? StackTrace,
    DateTime CreatedAtUtc,
    Guid? ErrorGroupId
)
{
    public ErrorGroup? ErrorGroup { get; set; }
};
