namespace Domain.Models;

public class ErrorGroup
{
    public Guid Id { get; set; }
    public string ErrorClass { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string SuggestedPatch { get; set; } = string.Empty;
    public DateTime FirstSeenUtc { get; set; }
    public DateTime LastSeenUtc { get; set; }
    public int Count { get; set; }
    
    public ICollection<LogEntry> LogEntries { get; set; } = new List<LogEntry>();
}
