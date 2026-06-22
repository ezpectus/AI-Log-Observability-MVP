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
    private static readonly ConcurrentDictionary<string, Guid> _errorCache = new();

    public ErrorGroupingService(IAiAnalysisService aiAnalysisService, LogRepository logRepository)
    {
        _aiAnalysisService = aiAnalysisService;
        _logRepository = logRepository;
    }

    public async Task<Guid?> HandleErrorGroupAsync(LogEntry log)
    {
        var errorClass = ExtractErrorClass(log.Message);
        var errorHash = ComputeErrorHash(log.Message, log.StackTrace);

        // 1. Check in-memory cache first
        if (_errorCache.TryGetValue(errorHash, out var cachedGroupId))
        {
            var cachedGroup = await _logRepository.GetErrorGroupByIdAsync(cachedGroupId);
            if (cachedGroup != null)
            {
                cachedGroup.Count++;
                cachedGroup.LastSeenUtc = DateTime.UtcNow;

                // If analysis is missing, generate it on-demand
                if (NeedsAiAnalysis(cachedGroup))
                {
                    Console.WriteLine($"[AI] Generating missing analysis for cached group: {errorClass}");
                    try
                    {
                        var (analysis, patchCode) = await _aiAnalysisService.SummarizeErrorAsync(log.Message, log.StackTrace);
                        cachedGroup.Summary = analysis;
                        cachedGroup.SuggestedPatch = patchCode;
                        Console.WriteLine(IsFallbackAnalysis(analysis, patchCode)
                            ? $"[AI] AI analysis fallback returned for cached group: {errorClass}"
                            : $"[AI] Successfully generated analysis for cached group: {errorClass}");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[AI] Failed to generate analysis for cached group {errorClass}: {ex.Message}");
                    }
                }

                await _logRepository.UpdateErrorGroupAsync(cachedGroup);
                return cachedGroup.Id;
            }
            else
            {
                _errorCache.TryRemove(errorHash, out _);
            }
        }

        // 2. Check database
        var existingGroup = await _logRepository.GetErrorGroupByErrorClassAsync(errorClass);

        if (existingGroup != null)
        {
            existingGroup.Count++;
            existingGroup.LastSeenUtc = DateTime.UtcNow;

            // If analysis is missing in database, generate it on-demand
            if (NeedsAiAnalysis(existingGroup))
            {
                Console.WriteLine($"[AI] Generating missing analysis for existing database group: {errorClass}");
                try
                {
                    var (analysis, patchCode) = await _aiAnalysisService.SummarizeErrorAsync(log.Message, log.StackTrace);
                    existingGroup.Summary = analysis;
                    existingGroup.SuggestedPatch = patchCode;
                    Console.WriteLine(IsFallbackAnalysis(analysis, patchCode)
                        ? $"[AI] AI analysis fallback returned for database group: {errorClass}"
                        : $"[AI] Successfully generated analysis for database group: {errorClass}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[AI] Failed to generate analysis for database group {errorClass}: {ex.Message}");
                }
            }

            await _logRepository.UpdateErrorGroupAsync(existingGroup);
            _errorCache.TryAdd(errorHash, existingGroup.Id);
            return existingGroup.Id;
        }

        // 3. Create new group if this error type hasn't been seen before
        Console.WriteLine($"[AI] Requesting HuggingFace for new error type: {errorClass}");
        try
        {
            var (newAnalysis, newPatchCode) = await _aiAnalysisService.SummarizeErrorAsync(log.Message, log.StackTrace);
            Console.WriteLine(IsFallbackAnalysis(newAnalysis, newPatchCode)
                ? $"[AI] AI analysis fallback returned for new error type: {errorClass}"
                : $"[AI] Successfully generated analysis for new error type: {errorClass}");

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
        catch (Exception ex)
        {
            Console.WriteLine($"[AI] Failed to generate analysis for new error type {errorClass}: {ex.Message}");
            
            // Create group without AI analysis if service fails
            var fallbackGroup = new ErrorGroup
            {
                Id = Guid.NewGuid(),
                ErrorClass = errorClass,
                Summary = "AI analysis unavailable - service error",
                SuggestedPatch = string.Empty,
                FirstSeenUtc = DateTime.UtcNow,
                LastSeenUtc = DateTime.UtcNow,
                Count = 1
            };

            await _logRepository.AddErrorGroupAsync(fallbackGroup);
            _errorCache.TryAdd(errorHash, fallbackGroup.Id);
            return fallbackGroup.Id;
        }
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

    private static bool NeedsAiAnalysis(ErrorGroup group)
    {
        return string.IsNullOrWhiteSpace(group.Summary)
            || string.IsNullOrWhiteSpace(group.SuggestedPatch)
            || IsFallbackAnalysis(group.Summary, group.SuggestedPatch);
    }

    private static bool IsFallbackAnalysis(string analysis, string patchCode)
    {
        return analysis.Contains("AI analysis temporarily unavailable", StringComparison.OrdinalIgnoreCase)
            || analysis.Contains("AI analysis unavailable", StringComparison.OrdinalIgnoreCase)
            || string.Equals(patchCode.Trim(), "// Patch not generated", StringComparison.OrdinalIgnoreCase);
    }
}
