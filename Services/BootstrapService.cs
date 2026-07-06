using System.Text.Json;
using vMonitor.Models;

namespace vMonitor.Services;

/// <summary>Veritabanı-DIŞI önyükleme yapılandırması. Hangi DB sağlayıcısı + bağlantı bilgisi burada tutulur
/// (DB'nin kendisi yapılandırıldığı için bu bilgi DB'de duramaz). Şifre DPAPI/AES ile şifreli veya Vault'tan çözülür.</summary>
public class BootstrapConfig
{
    public bool Configured { get; set; } = false;
    public DbProviderKind Provider { get; set; } = DbProviderKind.Sqlite;

    // Ağ üstü DB'ler için bağlantı alanları
    public string Host { get; set; } = "";
    public int Port { get; set; } = 0;          // 0 = sağlayıcı varsayılanı
    public string Database { get; set; } = "";  // Oracle'da Service Name
    public string Username { get; set; } = "";
    public string PasswordEncrypted { get; set; } = "";
    public bool UseSsl { get; set; } = false;
    public bool TrustServerCertificate { get; set; } = false;
    /// <summary>Pomelo (MySQL) için sabit sunucu sürümü — açılışta DB'ye bağlanıp AutoDetect yapmamak için.</summary>
    public string MySqlServerVersion { get; set; } = "8.0.21";

    /// <summary>Gelişmiş: ham bağlantı dizesi (ADO.NET) veya JDBC URL. Doluysa Host/Port/Database/Username/Password yok sayılır.
    /// Şifre içerebileceği için diske ŞİFRELİ (ConnectionStringRawEncrypted) yazılır; bu alan yalnız çalışma-anı (persist edilmez).</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string ConnectionStringRaw { get; set; } = "";
    public string ConnectionStringRawEncrypted { get; set; } = "";

    // SQLite
    public string SqlitePath { get; set; } = "";

    /// <summary>Lisans key (VMON1.payload.imza) — DB'den ÖNCE gerekir (Basic yalnız SQLite kurabilir),
    /// bu yüzden DB'de değil burada durur. Boş/geçersiz/süresi dolmuş → lisans kapısı uygulamayı kilitler.</summary>
    public string LicenseKey { get; set; } = "";

    // Vault ile DB şifresi çözme (opsiyonel). Bootstrap'ta Credentials tablosu yok → vault bilgisi burada.
    public bool UseVault { get; set; } = false;
    public string VaultUrl { get; set; } = "";
    public string VaultTokenEncrypted { get; set; } = "";
    public string VaultUserKey { get; set; } = "";
    public string VaultKey { get; set; } = "";
}

/// <summary>bootstrap.json oku/yaz + mevcut SQLite kurulumundan otomatik geriye-uyum.</summary>
public class BootstrapService
{
    private readonly string _path;
    private readonly string _legacySqlite;
    private static readonly JsonSerializerOptions JsonOpt = new() { WriteIndented = true };

    public BootstrapService(IWebHostEnvironment env)
    {
        var dataDir = Path.Combine(env.ContentRootPath, "Data");
        try { Directory.CreateDirectory(dataDir); } catch { /* izin yoksa açılışta çökme; sonra ele alınır */ }
        _path = Path.Combine(dataDir, "bootstrap.json");
        _legacySqlite = Path.Combine(dataDir, "monitoring.db");
    }

    public string LegacySqlitePath => _legacySqlite;

    public BootstrapConfig Load()
    {
        try
        {
            if (File.Exists(_path))
                return JsonSerializer.Deserialize<BootstrapConfig>(File.ReadAllText(_path)) ?? new BootstrapConfig();
        }
        catch { }
        return new BootstrapConfig { Configured = false };
    }

    public void Save(BootstrapConfig c)
        => File.WriteAllText(_path, JsonSerializer.Serialize(c, JsonOpt));

    /// <summary>Açılışta çağrılır. bootstrap.json yoksa ama eski SQLite DB varsa → otomatik SQLite bootstrap'ı yaz
    /// (mevcut kurulumlar wizard görmeden, veri kaybı olmadan çalışmaya devam eder).</summary>
    public BootstrapConfig EnsureConfig()
    {
        var c = Load();
        if (!c.Configured && File.Exists(_legacySqlite))
        {
            c = new BootstrapConfig { Configured = true, Provider = DbProviderKind.Sqlite, SqlitePath = _legacySqlite };
            try { Save(c); } catch { }
        }
        return c;
    }
}
