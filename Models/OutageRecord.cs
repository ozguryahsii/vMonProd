namespace vMonitor.Models;

/// <summary>Kesinti kaydı: DOWN'a geçişte açılır, UP'a dönüşte kapanır.</summary>
public class OutageRecord
{
    public int Id { get; set; }
    public int ServiceId { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? EndedAt { get; set; }
    public string? FirstError { get; set; }
}
