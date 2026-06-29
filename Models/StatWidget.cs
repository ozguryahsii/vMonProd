namespace vMonitor.Models;

/// <summary>İstatistikler sayfasındaki bir kutucuk (widget). v1: tek ortak düzen (kullanıcı-bazlı değil).
/// Konum/boyut Gridstack ızgarasına göre saklanır; Source hangi veriyi, Type hangi görseli belirler.</summary>
public class StatWidget
{
    public int Id { get; set; }

    /// <summary>Görsel tipi: "counter" | "pie" | "gauge" | "bar" | "resource".</summary>
    public string Type { get; set; } = "counter";

    /// <summary>Veri kaynağı anahtarı, örn. "total_servers", "status", "cpu", "ram", "disk",
    /// "os_kind", "os_version", "tag", "avg_cpu", "avg_ram", "avg_disk".</summary>
    public string Source { get; set; } = "total_servers";

    /// <summary>Başlık (boşsa kaynaktan otomatik).</summary>
    public string? Title { get; set; }

    /// <summary>Ek ayar (JSON). Örn. tag widget'ı için {"tags":["IIS","NGINX"]}.</summary>
    public string? ConfigJson { get; set; }

    // Gridstack konum/boyut
    public int X { get; set; }
    public int Y { get; set; }
    public int W { get; set; } = 3;
    public int H { get; set; } = 2;
    public int SortOrder { get; set; }
}
