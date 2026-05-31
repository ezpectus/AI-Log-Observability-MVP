using Application.Interfaces;
using Domain.Models;
using Infrastructure.PostgreSql;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace Application.Services;

public class ErrorGroupingService : IErrorGroupingService
{
    private readonly IAiAnalysisService _aiAnalysisService;
    private readonly LogRepository _logRepository;
    private readonly ConcurrentDictionary<string, Guid> _errorCache = new();

    public ErrorGroupingService(IAiAnalysisService aiAnalysisService, LogRepository logRepository)
    {
        _aiAnalysisService = aiAnalysisService;
        _logRepository = logRepository;
    }

  public async Task<Guid?> HandleErrorGroupAsync(LogEntry log)
{
    var errorClass = ExtractErrorClass(log.Message);
    var errorHash = ComputeErrorHash(log.Message, log.StackTrace);

    // 1. Проверяем оперативную память (кэш)
    if (_errorCache.TryGetValue(errorHash, out var cachedGroupId))
    {
        var cachedGroup = await _logRepository.GetErrorGroupByIdAsync(cachedGroupId);
        if (cachedGroup != null)
        {
            cachedGroup.Count++;
            cachedGroup.LastSeenUtc = DateTime.UtcNow;

            // ЕСЛИ АНАЛИЗА НЕТ — ДОГЕНЕРИРУЕМ ЕГО
            if (string.IsNullOrWhiteSpace(cachedGroup.Summary))
            {
                var (analysis, patchCode) = await _aiAnalysisService.SummarizeErrorAsync(log.Message, log.StackTrace);
                cachedGroup.Summary = analysis;
                cachedGroup.SuggestedPatch = patchCode;
            }

            await _logRepository.UpdateErrorGroupAsync(cachedGroup);
            return cachedGroup.Id;
        }
        else
        {
            _errorCache.TryRemove(errorHash, out _);
        }
    }

    // 2. Проверяем базу данных
    var existingGroup = await _logRepository.GetErrorGroupByErrorClassAsync(errorClass);

    if (existingGroup != null)
    {
        existingGroup.Count++;
        existingGroup.LastSeenUtc = DateTime.UtcNow;

        // ЕСЛИ В БАЗЕ НАШЛАСЬ ГРУППА БЕЗ ИИ-АНАЛИЗА — ПИНАЕМ МИСТРАЛЬ
        if (string.IsNullOrWhiteSpace(existingGroup.Summary))
        {
            Console.WriteLine($"[AI] Generating missing analysis for existing group: {errorClass}");
            var (analysis, patchCode) = await _aiAnalysisService.SummarizeErrorAsync(log.Message, log.StackTrace);
            existingGroup.Summary = analysis;
            existingGroup.SuggestedPatch = patchCode;
        }

        await _logRepository.UpdateErrorGroupAsync(existingGroup);
        _errorCache.TryAdd(errorHash, existingGroup.Id);
        return existingGroup.Id;
    }

    // 3. Создаем абсолютно новую группу, если такой ошибки еще не было
    Console.WriteLine($"[AI] Requesting HuggingFace for new error type: {errorClass}");
    var (newAnalysis, newPatchCode) = await _aiAnalysisService.SummarizeErrorAsync(log.Message, log.StackTrace);

    var newGroup = new ErrorGroup
    {
        Id = Guid.NewGuid(),
        ErrorClass = errorClass,
        Summary = newAnalysis,
        SuggestedPatch = newPatchCode,
        FirstSeenUtc = DateTime.UtcNow,
        LastSeenUtc = DateTime.UtcNow,
        Count = 1
    };

    await _logRepository.AddErrorGroupAsync(newGroup);
    _errorCache.TryAdd(errorHash, newGroup.Id);
    return newGroup.Id;
}

    private string ComputeErrorHash(string message, string? stackTrace)
    {
        var combined = $"{message}|{stackTrace ?? ""}";
        using var sha256 = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(combined);
        var hash = sha256.ComputeHash(bytes);
        return Convert.ToHexString(hash);
    }

    private string ExtractErrorClass(string message)
    {
        if (string.IsNullOrEmpty(message))
        {
            return "UnknownError";
        }

        var colonIndex = message.IndexOf(':');
        if (colonIndex > 0)
        {
            var errorClass = message.Substring(0, colonIndex).Trim();
            if (!string.IsNullOrEmpty(errorClass))
            {
                return errorClass;
            }
        }

        var spaceIndex = message.IndexOf(' ');
        if (spaceIndex > 0)
        {
            var errorClass = message.Substring(0, spaceIndex).Trim();
            if (!string.IsNullOrEmpty(errorClass))
            {
                return errorClass;
            }
        }

        return message.Length > 50 ? message.Substring(0, 50) : message;
    }
}
