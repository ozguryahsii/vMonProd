using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace vMonitor.Services;

/// <summary>OS sürümü → "destek sonu" (eol). endoflife.date açık API'sinden ürün sürüm tablolarını çeker (sana ait
/// HİÇBİR veri göndermez; yalnız genel tabloları indirir), yerel dosyaya cache'ler ve eşleştirmeyi tamamen yerelde
/// yapar. Kapalıyken/cache yokken çağıran statik listeye düşer. Offline ortam için manuel JSON import desteklenir.</summary>
public record EolResult(string OsName, string Product, string Cycle, DateTime? Eol, int? DaysLeft, string Status);

public class EolService
{
    private readonly ILogger<EolService> _logger;
    private readonly string _cachePath;
    private Dictionary<string, Dictionary<string, string?>>? _cache;   // ürün -> (cycle -> eol "yyyy-MM-dd" | "PAST" | null)
    private DateTime _syncedAt;

    // endoflife.date ürün slug'ları (ilgili OS aileleri)
    private static readonly string[] Products =
        { "windows-server", "windows", "oracle-linux", "rhel", "ubuntu", "debian", "centos", "centos-stream", "almalinux", "rocky-linux", "sles", "amazon-linux" };

    public EolService(IWebHostEnvironment env, ILogger<EolService> logger)
    {
        _logger = logger;
        _cachePath = Path.Combine(env.ContentRootPath, "Data", "eol-cache.json");
    }

    public DateTime? SyncedAt { get { Load(); return _cache == null ? null : _syncedAt; } }
    public bool HasCache { get { Load(); return _cache is { Count: > 0 }; } }

    private sealed class CacheFile { public DateTime SyncedAt { get; set; } public Dictionary<string, Dictionary<string, string?>> Products { get; set; } = new(); }

    private Dictionary<string, Dictionary<string, string?>>? Load()
    {
        if (_cache != null) return _cache;
        try
        {
            if (File.Exists(_cachePath))
            {
                var doc = JsonSerializer.Deserialize<CacheFile>(File.ReadAllText(_cachePath));
                if (doc != null) { _cache = new(doc.Products, StringComparer.OrdinalIgnoreCase); _syncedAt = doc.SyncedAt; }
            }
        }
        catch (Exception ex) { _logger.LogDebug(ex, "EOL cache okunamadı"); }
        return _cache;
    }

    private void Save()
    {
        try { File.WriteAllText(_cachePath, JsonSerializer.Serialize(new CacheFile { SyncedAt = _syncedAt, Products = _cache! })); }
        catch (Exception ex) { _logger.LogError(ex, "EOL cache yazılamadı"); }
    }

    /// <summary>endoflife.date'ten ürün tablolarını çeker ve cache'ler.</summary>
    public async Task<(bool ok, string msg)> SyncAsync(string? proxy, CancellationToken ct = default)
    {
        try
        {
            using var handler = new HttpClientHandler();
            if (!string.IsNullOrWhiteSpace(proxy)) handler.Proxy = new WebProxy(proxy);
            using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(25) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("vMon/1.0 (+eol-check)");

            var result = new Dictionary<string, Dictionary<string, string?>>(StringComparer.OrdinalIgnoreCase);
            int okCount = 0;
            foreach (var p in Products)
            {
                try
                {
                    var json = await http.GetStringAsync($"https://endoflife.date/api/{p}.json", ct);
                    using var d = JsonDocument.Parse(json);
                    var cycles = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
                    foreach (var el in d.RootElement.EnumerateArray())
                    {
                        var cyc = el.TryGetProperty("cycle", out var c)
                            ? (c.ValueKind == JsonValueKind.String ? c.GetString() : c.ToString()) : null;
                        if (string.IsNullOrEmpty(cyc)) continue;
                        string? eol = null;
                        if (el.TryGetProperty("eol", out var e))
                            eol = e.ValueKind switch { JsonValueKind.String => e.GetString(), JsonValueKind.True => "PAST", _ => null };
                        cycles[cyc!] = eol;
                    }
                    result[p] = cycles; okCount++;
                }
                catch (Exception ex) { _logger.LogWarning(ex, "EOL ürün çekilemedi: {P}", p); }
            }
            if (okCount == 0) return (false, "Hiçbir ürün verisi alınamadı (internet/proxy erişimini kontrol edin).");
            _cache = result; _syncedAt = DateTime.UtcNow; Save();
            return (true, $"{okCount} ürün senkronize edildi.");
        }
        catch (Exception ex) { _logger.LogError(ex, "EOL senkron hatası"); return (false, ex.GetBaseException().Message); }
    }

    /// <summary>Offline ortam için: önceden indirilmiş cache JSON'unu içe aktarır ({product:{cycle:eol}} biçimi).</summary>
    public (bool ok, string msg) Import(string json)
    {
        try
        {
            var doc = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string?>>>(json);
            if (doc == null || doc.Count == 0) return (false, "Geçersiz/boş JSON.");
            _cache = new(doc, StringComparer.OrdinalIgnoreCase); _syncedAt = DateTime.UtcNow; Save();
            return (true, $"{doc.Count} ürün içe aktarıldı.");
        }
        catch (Exception ex) { return (false, ex.GetBaseException().Message); }
    }

    /// <summary>Bir OsName için EOL değerlendirmesi. Cache yoksa/eşleşme yoksa null döner (çağıran statik listeye düşer).</summary>
    public EolResult? Evaluate(string? osName, int warnDays)
    {
        if (string.IsNullOrWhiteSpace(osName)) return null;
        var cache = Load(); if (cache == null) return null;
        var parsed = Parse(osName!); if (parsed == null) return null;
        var (product, cycle) = parsed.Value;
        if (!cache.TryGetValue(product, out var cycles)) return null;

        if (!cycles.TryGetValue(cycle, out var eolStr))
        {
            var major = cycle.Split('.')[0];
            var match = cycles.Keys.FirstOrDefault(k => k == major || k.StartsWith(major + "."));
            if (match == null) return null;
            eolStr = cycles[match];
        }

        DateTime? eol = eolStr == "PAST" ? DateTime.Today.AddDays(-1)
            : (DateTime.TryParse(eolStr, out var dt) ? dt : (DateTime?)null);
        if (eol == null) return new EolResult(osName!, product, cycle, null, null, "ok");

        int days = (int)(eol.Value.Date - DateTime.Today).TotalDays;
        var status = days < 0 ? "eol" : days <= warnDays ? "soon" : "ok";
        return new EolResult(osName!, product, cycle, eol, days, status);
    }

    private static (string product, string cycle)? Parse(string osName)
    {
        var s = osName.ToLowerInvariant();
        string? Maj(string pattern) { var m = Regex.Match(s, pattern); return m.Success ? m.Groups[1].Value : null; }

        if (s.Contains("windows server"))
        {
            var m = Regex.Match(s, @"server\s+(\d{4})\s*(r2)?");
            if (m.Success) return ("windows-server", m.Groups[1].Value + (m.Groups[2].Success ? "-R2" : ""));
        }
        else if (s.Contains("oracle linux")) { var c = Maj(@"oracle linux\D+(\d+)"); if (c != null) return ("oracle-linux", c); }
        else if (s.Contains("red hat enterprise") || s.Contains("rhel")) { var c = Maj(@"(?:enterprise linux|rhel)\D*(\d+)"); if (c != null) return ("rhel", c); }
        else if (s.Contains("ubuntu")) { var m = Regex.Match(s, @"ubuntu\s+(\d{2}\.\d{2})"); if (m.Success) return ("ubuntu", m.Groups[1].Value); }
        else if (s.Contains("debian")) { var c = Maj(@"debian\D+(\d+)"); if (c != null) return ("debian", c); }
        else if (s.Contains("centos stream")) { var c = Maj(@"stream\D+(\d+)"); if (c != null) return ("centos-stream", c); }
        else if (s.Contains("centos")) { var c = Maj(@"centos\D+(\d+)"); if (c != null) return ("centos", c); }
        else if (s.Contains("almalinux") || s.Contains("alma linux")) { var c = Maj(@"alma\s*linux\D+(\d+)"); if (c != null) return ("almalinux", c); }
        else if (s.Contains("rocky")) { var c = Maj(@"rocky\D+(\d+)"); if (c != null) return ("rocky-linux", c); }
        else if (s.Contains("sles") || s.Contains("suse linux enterprise")) { var c = Maj(@"(?:sles|enterprise server)\D*(\d+)"); if (c != null) return ("sles", c); }
        else if (s.Contains("amazon linux")) { var c = Maj(@"amazon linux\D*(\d+)"); if (c != null) return ("amazon-linux", c); }
        return null;
    }
}
