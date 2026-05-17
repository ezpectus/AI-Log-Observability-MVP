using Application.Interfaces;
using Domain.Enums;
using Domain.Models;
using Infrastructure.PostgreSql;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class LogsController : ControllerBase
{
    private readonly ILogIngestionService _logIngestionService;
    private readonly ILogQueryService _logQueryService;
    private readonly ApplicationDbContext _context;
    private readonly ILogger<LogsController> _logger;

    public LogsController(
        ILogIngestionService logIngestionService,
        ILogQueryService logQueryService,
        ApplicationDbContext context,
        ILogger<LogsController> logger)
    {
        _logIngestionService = logIngestionService;
        _logQueryService = logQueryService;
        _context = context;
        _logger = logger;
    }

    [HttpPost]
    [EnableRateLimiting("LogIngestionPolicy")]
    public async Task<IActionResult> IngestLog([FromBody] LogEntry log)
    {
        if (log == null)
        {
            _logger.LogWarning("Received null log entry");
            return BadRequest(new { error = "Invalid request", message = "Log entry cannot be null" });
        }

        if (string.IsNullOrWhiteSpace(log.ServiceName))
        {
            _logger.LogWarning("Received log with empty service name");
            return BadRequest(new { error = "Invalid request", message = "Service name is required" });
        }

        if (log.ServiceName.Length > 100)
        {
            _logger.LogWarning("Received log with service name exceeding 100 characters: {ServiceNameLength}", log.ServiceName.Length);
            return BadRequest(new { error = "Invalid request", message = "Service name cannot exceed 100 characters" });
        }

        if (string.IsNullOrWhiteSpace(log.Message))
        {
            _logger.LogWarning("Received log with empty message");
            return BadRequest(new { error = "Invalid request", message = "Message is required" });
        }

        if (log.Message.Length > 10000)
        {
            _logger.LogWarning("Received log with message exceeding 10000 characters: {MessageLength}", log.Message.Length);
            return BadRequest(new { error = "Invalid request", message = "Message cannot exceed 10000 characters" });
        }

        await _logIngestionService.IngestLogAsync(log);
        return Accepted();
    }

    [HttpGet]
    public async Task<IActionResult> GetLogs(
        [FromQuery] string? service,
        [FromQuery] LogLevel? level,
        [FromQuery] int limit = 100,
        [FromQuery] int offset = 0)
    {
        if (limit < 0 || limit > 1000)
        {
            return BadRequest(new { error = "Invalid request", message = "Limit must be between 0 and 1000" });
        }

        if (offset < 0)
        {
            return BadRequest(new { error = "Invalid request", message = "Offset must be non-negative" });
        }

        var logs = await _logQueryService.GetLogsAsync(service, level, limit, offset);
        return Ok(logs);
    }

    [HttpGet("errors/groups")]
    public async Task<IActionResult> GetErrorGroups()
    {
        var groups = await _context.ErrorGroups
            .OrderByDescending(eg => eg.LastSeenUtc)
            .ToListAsync();
        return Ok(groups);
    }

    [HttpPost("errors/{id}/apply-patch")]
    public async Task<IActionResult> ApplyPatch(Guid id)
    {
        // Simulate patch application process
        await Task.Delay(2000);

        var branchName = $"fix/issue-{Guid.NewGuid().ToString().Substring(0, 8)}";
        
        return Ok(new { message = $"Патч успешно сгенерирован и применен к локальной ветке {branchName}" });
    }
}
