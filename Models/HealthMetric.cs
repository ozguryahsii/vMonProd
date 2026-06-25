namespace vMonitor.Models;

/// <summary>Sunucu sağlığı kontrollerinin (CPU/RAM/Disk) zaman serisi kaydı.</summary>
public class HealthMetric
{
    public long Id { get; set; }
    public int ServiceId { get; set; }
    public DateTime CheckedAt { get; set; }
    public double? CpuPercent { get; set; }
    public double? RamPercent { get; set; }
    public double? MaxDiskPercent { get; set; }

    /// <summary>Disk detayı, örn. "C: %71 · D: %35" veya "/: %80 · /data: %45".</summary>
    public string? DiskDetail { get; set; }
}
