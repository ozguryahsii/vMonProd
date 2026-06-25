using System.ComponentModel.DataAnnotations;

namespace vMonitor.Models;

/// <summary>Özel dashboard tanımı: seçili servisler ve/veya tip filtresine göre
/// kendi sayfasında izleme ekranı.</summary>
public class DashboardDef
{
    public int Id { get; set; }

    [Required, MaxLength(200)]
    public string Name { get; set; } = "";

    /// <summary>Dahil edilen servis Id'leri, virgülle ayrılmış. Boş olabilir.</summary>
    public string? ServiceIdsCsv { get; set; }

    /// <summary>Tip filtresi (örn. "Ping"): bu tipteki TÜM servisler otomatik dahil. Boş olabilir.</summary>
    [MaxLength(50)]
    public string? TypeFilter { get; set; }

    /// <summary>Keyword filtresi: bu anahtar kelimeye sahip TÜM servisler otomatik dahil. Boş olabilir.</summary>
    [MaxLength(200)]
    public string? KeywordFilter { get; set; }

    public int SortOrder { get; set; }

    public List<int> GetServiceIds() =>
        string.IsNullOrWhiteSpace(ServiceIdsCsv)
            ? new List<int>()
            : ServiceIdsCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(x => int.TryParse(x, out _)).Select(int.Parse).ToList();
}
