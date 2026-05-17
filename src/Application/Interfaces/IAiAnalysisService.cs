namespace Application.Interfaces;

public interface IAiAnalysisService
{
    Task<(string Analysis, string PatchCode)> SummarizeErrorAsync(string message, string? stackTrace);
}
