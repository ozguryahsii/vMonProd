using Microsoft.Data.Sqlite;
using vMonitor.Models;

namespace vMonitor.Services;

public record BackupFile(string Name, double SizeMb, DateTime ModifiedUtc);

/// <summary>SQLite veritabanının tutarlı (online) yedeğini alır/geri yükler. DB yedekleme araçlarından bağımsız,
/// uygulama içinden çalışır. SQLite'ın online backup API'si (SqliteConnection.BackupDatabase) kullanılır —
/// uygulama çalışırken bile bütünlüğü bozulmayan bir kopya üretir.</summary>
public class BackupService
{
    private readonly BootstrapConfig _bootstrap;
    private readonly ILogger<BackupService> _logger;
    private const string Prefix = "monitoring-";

    public BackupService(BootstrapConfig bootstrap, ILogger<BackupService> logger)
    {
        _bootstrap = bootstrap;
        _logger = logger;
    }

    public bool IsSqlite => _bootstrap.Provider == DbProviderKind.Sqlite;
    public string LivePath => _bootstrap.SqlitePath;

    /// <summary>Şimdi tutarlı yedek al. Zaman damgalı dosya üretir, retention uygular. (dosyaAdı, hata) döner.</summary>
    public async Task<(string? file, string? error)> BackupNowAsync(string targetDir, int retentionCount, CancellationToken ct = default)
    {
        if (!IsSqlite) return (null, "Yedekleme yalnızca SQLite içindir. Diğer veritabanlarında native yedekleme araçlarını kullanın.");
        if (string.IsNullOrWhiteSpace(targetDir)) return (null, "Yedek klasörü tanımlı değil.");
        if (!File.Exists(LivePath)) return (null, $"Aktif veritabanı bulunamadı: {LivePath}");

        try
        {
            Directory.CreateDirectory(targetDir);
            var name = $"{Prefix}{DateTime.Now:yyyyMMdd-HHmmss}.db";
            var dest = Path.Combine(targetDir, name);

            await Task.Run(() =>
            {
                using var src = new SqliteConnection($"Data Source={LivePath};Mode=ReadWrite");
                src.Open();
                using var dst = new SqliteConnection($"Data Source={dest}");
                dst.Open();
                src.BackupDatabase(dst);          // tutarlı online snapshot
            }, ct);

            ApplyRetention(targetDir, retentionCount);
            _logger.LogInformation("Yedek alındı: {Dest}", dest);
            return (name, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Yedek alınamadı");
            return (null, ex.GetBaseException().Message);
        }
    }

    /// <summary>Klasördeki yedekleri (yeni→eski) listeler.</summary>
    public List<BackupFile> List(string? targetDir)
    {
        var result = new List<BackupFile>();
        if (string.IsNullOrWhiteSpace(targetDir) || !Directory.Exists(targetDir)) return result;
        foreach (var f in new DirectoryInfo(targetDir).GetFiles($"{Prefix}*.db"))
            result.Add(new BackupFile(f.Name, Math.Round(f.Length / 1048576.0, 2), f.LastWriteTimeUtc));
        return result.OrderByDescending(x => x.ModifiedUtc).ToList();
    }

    private void ApplyRetention(string targetDir, int keep)
    {
        if (keep <= 0) return;
        var files = new DirectoryInfo(targetDir).GetFiles($"{Prefix}*.db")
            .OrderByDescending(f => f.LastWriteTimeUtc).Skip(keep).ToList();
        foreach (var f in files)
            try { f.Delete(); } catch (Exception ex) { _logger.LogWarning(ex, "Eski yedek silinemedi: {F}", f.Name); }
    }

    /// <summary>Klasördeki bir yedeğin tam yolunu güvenli biçimde döner (dizin dışına çıkışı engeller).</summary>
    public string? SafeBackupPath(string? targetDir, string fileName)
    {
        if (string.IsNullOrWhiteSpace(targetDir)) return null;
        var safe = Path.GetFileName(fileName);
        if (string.IsNullOrEmpty(safe) || !safe.StartsWith(Prefix) || !safe.EndsWith(".db")) return null;
        var full = Path.Combine(targetDir, safe);
        return File.Exists(full) ? full : null;
    }

    /// <summary>Verilen .db dosyasının geçerli bir vMon veritabanı olup olmadığını doğrular.</summary>
    public bool Validate(string dbFile, out string error)
    {
        error = "";
        try
        {
            using var c = new SqliteConnection($"Data Source={dbFile};Mode=ReadOnly");
            c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name IN ('Services','Settings')";
            var n = Convert.ToInt32(cmd.ExecuteScalar());
            if (n < 2) { error = "Bu dosya geçerli bir vMon veritabanı değil (beklenen tablolar yok)."; return false; }
            return true;
        }
        catch (Exception ex) { error = "Dosya okunamadı: " + ex.GetBaseException().Message; return false; }
    }

    /// <summary>Verilen .db dosyasını AKTİF veritabanının üzerine yazar (online backup ile). Geri yüklemeden sonra
    /// uygulamanın yeniden başlatılması önerilir (çağıran tetikler).</summary>
    public async Task<(bool ok, string? error)> RestoreAsync(string sourceDbFile, CancellationToken ct = default)
    {
        if (!IsSqlite) return (false, "Geri yükleme yalnızca SQLite içindir.");
        if (!Validate(sourceDbFile, out var verr)) return (false, verr);
        try
        {
            await Task.Run(() =>
            {
                using var src = new SqliteConnection($"Data Source={sourceDbFile};Mode=ReadOnly");
                src.Open();
                using var dst = new SqliteConnection($"Data Source={LivePath};Mode=ReadWrite");
                dst.Open();
                src.BackupDatabase(dst);          // kaynağı aktif DB'nin üzerine yaz
            }, ct);
            _logger.LogWarning("Veritabanı geri yüklendi: {Src} -> {Live}", sourceDbFile, LivePath);
            return (true, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Geri yükleme başarısız");
            return (false, ex.GetBaseException().Message);
        }
    }
}
