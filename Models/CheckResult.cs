namespace vMonitor.Models;

public class CheckResult
{
    public long Id { get; set; }
    public int ServiceId { get; set; }
    public DateTime CheckedAt { get; set; }
    public bool IsUp { get; set; }
    /// <summary>0=Up, 1=Down, 2=Error (eşik aşımı/uyarı).</summary>
    public int Status { get; set; }
    public long ResponseTimeMs { get; set; }
    public string? Error { get; set; }
}
