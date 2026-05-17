using Domain.Enums;

namespace Domain.Models;

public record LogEntry(
    Guid Id,
    string ServiceName,
    LogLevel Level,
    string Message,
    string? StackTrace,
    DateTime CreatedAtUtc,
    Guid? ErrorGroupId
);
