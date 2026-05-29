using DomainLogLevel = Domain.Enums.LogLevel;
using Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.PostgreSql;

public class LogRepository
{
    private readonly ApplicationDbContext _context;

    public LogRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task SaveLogAsync(LogEntry log)
    {
        await _context.LogEntries.AddAsync(log);
        await _context.SaveChangesAsync();
    }

    public async Task<IEnumerable<LogEntry>> GetLogsAsync(string? service, DomainLogLevel? level, int limit, int offset)
    {
        var query = _context.LogEntries.AsQueryable();

        if (!string.IsNullOrEmpty(service))
        {
            query = query.Where(l => l.ServiceName == service);
        }

        if (level.HasValue)
        {
            query = query.Where(l => l.Level == level.Value);
        }

        query = query.OrderByDescending(l => l.CreatedAtUtc);

        if (offset > 0)
        {
            query = query.Skip(offset);
        }

        if (limit > 0)
        {
            query = query.Take(limit);
        }

        return await query.ToListAsync();
    }

    public async Task<ErrorGroup?> GetErrorGroupByErrorClassAsync(string errorClass)
    {
        return await _context.ErrorGroups
            .FirstOrDefaultAsync(eg => eg.ErrorClass == errorClass);
    }

    public async Task<ErrorGroup?> GetErrorGroupByIdAsync(Guid id)
    {
        return await _context.ErrorGroups
            .FirstOrDefaultAsync(eg => eg.Id == id);
    }

    public async Task AddErrorGroupAsync(ErrorGroup errorGroup)
    {
        await _context.ErrorGroups.AddAsync(errorGroup);
        await _context.SaveChangesAsync();
    }

    public async Task UpdateErrorGroupAsync(ErrorGroup errorGroup)
    {
        _context.ErrorGroups.Update(errorGroup);
        await _context.SaveChangesAsync();
    }
}
