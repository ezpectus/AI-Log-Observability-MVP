namespace Domain.Entities;

public class ServiceMetric
{
    public string ServiceName { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public int Rps { get; set; }
    public double ErrorRate { get; set; }
}
