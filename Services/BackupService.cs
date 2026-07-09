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
    public async Task<(string? file, string? error)> BackupNowAsync(string targetDir, int retentionCount, bool encrypt = false, string? password = null, CancellationToken ct = default)
    {
        if (!IsSqlite) return (null, "Yedekleme yalnızca SQLite içindir. Diğer veritabanlarında native yedekleme araçlarını kullanın.");
        if (string.IsNullOrWhiteSpace(targetDir)) return (null, "Yedek klasörü tanımlı değil.");
        if (!File.Exists(LivePath)) return (null, $"Aktif veritabanı bulunamadı: {LivePath}");
        if (encrypt && string.IsNullOrWhiteSpace(password)) return (null, "Yedek şifreleme açık ama parola tanımlı değil (Ayarlar → Yedekleme).");

        try
        {
            Directory.CreateDirectory(targetDir);
            var baseName = $"{Prefix}{DateTime.Now:yyyyMMdd-HHmmss}.db";
            var plain = Path.Combine(targetDir, baseName);

            await Task.Run(() =>
            {
                using var src = new SqliteConnection($"Data Source={LivePath};Mode=ReadWrite");
                src.Open();
                using var dst = new SqliteConnection($"Data Source={plain}");
                dst.Open();
                src.BackupDatabase(dst);          // tutarlı online snapshot
            }, ct);

            string name = baseName;
            if (encrypt)
            {
                var enc = plain + ".enc";
                await Task.Run(() => AesFileCrypto.EncryptFile(plain, enc, password!), ct);
                try { File.Delete(plain); } catch { }
                name = baseName + ".enc";
            }

            ApplyRetention(targetDir, retentionCount);
            _logger.LogInformation("Yedek alındı: {Dest}", Path.Combine(targetDir, name));
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
        foreach (var f in new DirectoryInfo(targetDir).GetFiles($"{Prefix}*.db*"))
            if (f.Name.EndsWith(".db", StringComparison.OrdinalIgnoreCase) || f.Name.EndsWith(".db.enc", StringComparison.OrdinalIgnoreCase))
                result.Add(new BackupFile(f.Name, Math.Round(f.Length / 1048576.0, 2), f.LastWriteTimeUtc));
        return result.OrderByDescending(x => x.ModifiedUtc).ToList();
    }

    private void ApplyRetention(string targetDir, int keep)
    {
        if (keep <= 0) return;
        var files = new DirectoryInfo(targetDir).GetFiles($"{Prefix}*.db*")
            .Where(f => f.Name.EndsWith(".db", StringComparison.OrdinalIgnoreCase) || f.Name.EndsWith(".db.enc", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(f => f.LastWriteTimeUtc).Skip(keep).ToList();
        foreach (var f in files)
            try { f.Delete(); } catch (Exception ex) { _logger.LogWarning(ex, "Eski yedek silinemedi: {F}", f.Name); }
    }

    /// <summary>Klasördeki bir yedeğin tam yolunu güvenli biçimde döner (dizin dışına çıkışı engeller).</summary>
    public string? SafeBackupPath(string? targetDir, string fileName)
    {
        if (string.IsNullOrWhiteSpace(targetDir)) return null;
        var safe = Path.GetFileName(fileName);
        if (string.IsNullOrEmpty(safe) || !safe.StartsWith(Prefix)
            || !(safe.EndsWith(".db", StringComparison.OrdinalIgnoreCase) || safe.EndsWith(".db.enc", StringComparison.OrdinalIgnoreCase))) return null;
        // Kanonikleştir + kapsama testi (CodeQL cs/path-injection): sonuç mutlaka hedef klasörün İÇİNDE kalmalı
        var root = Path.GetFullPath(targetDir);
        var full = Path.GetFullPath(Path.Combine(root, safe));
        if (!full.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return null;
        return File.Exists(full) ? full : null;
    }

    /// <summary>Verilen .db dosyasının geçerli bir vMon veritabanı olup olmadığını doğrular.</summary>
    public bool Validate(string dbFile, out string error)
    {
        error = "";
        try
        {
            // Bağlantı dizesi BUILDER ile kurulur (CodeQL cs/resource-injection): yol, dizeye gömülmez
            var cs = new SqliteConnectionStringBuilder { DataSource = dbFile, Mode = SqliteOpenMode.ReadOnly }.ToString();
            using var c = new SqliteConnection(cs);
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
    public async Task<(bool ok, string? error)> RestoreAsync(string sourceDbFile, string? password = null, CancellationToken ct = default)
    {
        if (!IsSqlite) return (false, "Geri yükleme yalnızca SQLite içindir.");

        // Şifreli yedek (.enc) ise önce geçici bir .db'ye çöz
        string actualSource = sourceDbFile;
        string? tempDecrypted = null;
        if (AesFileCrypto.IsEncrypted(sourceDbFile))
        {
            if (string.IsNullOrWhiteSpace(password)) return (false, "Şifreli yedek için parola gerekli (Ayarlar → Yedekleme parolası).");
            try
            {
                tempDecrypted = Path.Combine(Path.GetTempPath(), "vmon-dec-" + Guid.NewGuid().ToString("N") + ".db");
                await Task.Run(() => AesFileCrypto.DecryptFile(sourceDbFile, tempDecrypted!, password!), ct);
                actualSource = tempDecrypted;
            }
            catch (Exception ex)
            {
                try { if (tempDecrypted != null && File.Exists(tempDecrypted)) File.Delete(tempDecrypted); } catch { }
                return (false, "Şifre çözülemedi (parola yanlış olabilir): " + ex.GetBaseException().Message);
            }
        }

        try
        {
            if (!Validate(actualSource, out var verr)) return (false, verr);
            await Task.Run(() =>
            {
                var srcCs = new SqliteConnectionStringBuilder { DataSource = actualSource, Mode = SqliteOpenMode.ReadOnly }.ToString();
                using var src = new SqliteConnection(srcCs);
                src.Open();
                var dstCs = new SqliteConnectionStringBuilder { DataSource = LivePath, Mode = SqliteOpenMode.ReadWrite }.ToString();
                using var dst = new SqliteConnection(dstCs);
                dst.Open();
                src.BackupDatabase(dst);          // kaynağı aktif DB'nin üzerine yaz
            }, ct);
            _logger.LogWarning("Veritabanı geri yüklendi: {Src} -> {Live}", LogSan.S(sourceDbFile), LivePath);
            return (true, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Geri yükleme başarısız");
            return (false, ex.GetBaseException().Message);
        }
        finally
        {
            try { if (tempDecrypted != null && File.Exists(tempDecrypted)) File.Delete(tempDecrypted); } catch { }
        }
    }
}
