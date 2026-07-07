using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace vMonitor.Services;

/// <summary>SELF-UPDATE (yol haritası #2.5). Public GitHub reposundaki en son release'i denetler;
/// onay gelince zip'i indirir, SHA-256 doğrular, temp'e açar ve paketteki upgrade.ps1'i AYRIK bir
/// PowerShell süreciyle başlatır (çalışan süreç kendi dosyalarını değiştiremez — güncelleyici
/// servisten bağımsız yaşar: servisi durdurur → dosyaları değiştirir → servisi başlatır).
/// Yalnız Windows Service kurulumunda çalışır (LocalSystem yetkisi dosya değişimine yeter).
/// Data + appsettings.json upgrade.ps1 tarafından KORUNUR.</summary>
public sealed class UpdateService
{
    private const string DefaultRepo = "ozguryahsii/vMonProd";

    private readonly IWebHostEnvironment _env;
    private readonly IConfiguration _cfg;
    private readonly ILogger<UpdateService> _logger;

    public UpdateService(IWebHostEnvironment env, IConfiguration cfg, ILogger<UpdateService> logger)
    { _env = env; _cfg = cfg; _logger = logger; }

    private string Repo => string.IsNullOrWhiteSpace(_cfg["Update:Repo"]) ? DefaultRepo : _cfg["Update:Repo"]!;

    public sealed record CheckResult(
        string Current, string Latest, bool IsNewer, string Notes,
        string? AssetUrl, string? AssetName, double SizeMb, string? Digest, string? PublishedAt);

    /// <summary>GitHub'dan en son release'i çeker (public repo — anonim, PAT gerekmez).</summary>
    public async Task<CheckResult> CheckAsync(CancellationToken ct)
    {
        using var http = NewHttp();
        var json = await http.GetStringAsync($"https://api.github.com/repos/{Repo}/releases/latest", ct);
        using var d = JsonDocument.Parse(json);
        var root = d.RootElement;

        var tag = root.GetProperty("tag_name").GetString() ?? "";
        // Sürüm notları DOĞRUDAN release body'sinden gelir (satıcı elle yazar; son-kullanıcı diliyle).
        // Ham commit başlıkları/iç detay ASLA gösterilmez.
        var notes = root.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "";
        var published = root.TryGetProperty("published_at", out var p) ? p.GetString() : null;

        string? assetUrl = null, assetName = null, digest = null;
        double sizeMb = 0;
        if (root.TryGetProperty("assets", out var assets))
            foreach (var a in assets.EnumerateArray())
            {
                var name = a.GetProperty("name").GetString() ?? "";
                if (!name.EndsWith("win-x64.zip", StringComparison.OrdinalIgnoreCase)) continue;
                assetName = name;
                assetUrl = a.GetProperty("browser_download_url").GetString();
                sizeMb = Math.Round(a.GetProperty("size").GetInt64() / 1024.0 / 1024.0, 1);
                if (a.TryGetProperty("digest", out var dg)) digest = dg.GetString();   // "sha256:..." (GitHub sağlıyorsa)
                break;
            }

        return new CheckResult(VersionInfo.AppVersion, tag, VersionInfo.IsNewer(tag, VersionInfo.AppVersion),
            notes, assetUrl, assetName, sizeMb, digest, published);
    }

    /// <summary>Güncellemeyi uygular: indir → doğrula → aç → ayrık güncelleyiciyi başlat.
    /// Dönüşten birkaç saniye sonra servis güncelleyici tarafından durdurulup yenisiyle başlatılır.</summary>
    public async Task<(bool ok, string message)> ApplyAsync(CancellationToken ct)
    {
        if (!Microsoft.Extensions.Hosting.WindowsServices.WindowsServiceHelpers.IsWindowsService())
            return (false, "Self-update yalnız Windows Service kurulumunda desteklenir. IIS kurulumunda upgrade.ps1 ile elle güncelleyin.");

        var check = await CheckAsync(ct);
        if (!check.IsNewer) return (false, $"Zaten güncelsiniz ({check.Current}).");
        if (string.IsNullOrEmpty(check.AssetUrl)) return (false, "Release paketinde win-x64.zip bulunamadı (CI henüz bitmemiş olabilir).");

        // 1) İndir (temp altına; uygulama dizinine DOKUNMA — orası güncelleyicinin işi)
        var staging = Path.Combine(Path.GetTempPath(), "vmon-selfupdate", check.Latest);
        try { if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true); } catch { }
        Directory.CreateDirectory(staging);
        var zipPath = Path.Combine(staging, check.AssetName ?? "package.zip");

        using (var http = NewHttp(TimeSpan.FromMinutes(10)))
        await using (var src = await http.GetStreamAsync(check.AssetUrl, ct))
        await using (var dst = File.Create(zipPath))
            await src.CopyToAsync(dst, ct);

        // 2) Bütünlük: GitHub API'nin bildirdiği SHA-256 ile karşılaştır (HTTPS üstüne ikinci kontrol)
        if (!string.IsNullOrEmpty(check.Digest) && check.Digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
        {
            await using var fs = File.OpenRead(zipPath);
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(fs, ct)).ToLowerInvariant();
            var expected = check.Digest[7..].ToLowerInvariant();
            if (hash != expected)
                return (false, "İndirilen paketin SHA-256 özeti release ile eşleşmiyor — güncelleme İPTAL edildi (paket bozulmuş veya değiştirilmiş olabilir).");
        }

        // 3) Aç ve doğrula (upgrade.ps1 + app/ paket kökünde olmalı)
        var pkgDir = Path.Combine(staging, "pkg");
        ZipFile.ExtractToDirectory(zipPath, pkgDir, overwriteFiles: true);
        var upgrade = Path.Combine(pkgDir, "upgrade.ps1");
        if (!File.Exists(upgrade) || !Directory.Exists(Path.Combine(pkgDir, "app")))
            return (false, "Paket beklenen düzende değil (upgrade.ps1 / app klasörü yok).");

        // 4) Ayrık güncelleyici script'i üret: servis adını exe yolundan çözer, upgrade.ps1'i çalıştırır,
        //    her şeyi Data\selfupdate.log'a yazar. 3 sn bekleme: bu HTTP yanıtının kullanıcıya ulaşması için.
        var appPath = _env.ContentRootPath.TrimEnd('\\');
        var exePath = Path.Combine(appPath, "vMonitor.exe");
        var logPath = Path.Combine(appPath, "Data", "selfupdate.log");
        var runner = Path.Combine(staging, "runner.ps1");
        File.WriteAllText(runner, $@"
Start-Sleep -Seconds 3
Start-Transcript -Path ""{logPath}"" -Append
try {{
    $svc = (Get-CimInstance Win32_Service | Where-Object {{ $_.PathName -like ""*{exePath}*"" }} | Select-Object -First 1).Name
    if (-not $svc) {{ $svc = 'vMon' }}
    Write-Output ""Self-update: servis=$svc hedef={appPath} paket={pkgDir}""
    & ""{upgrade}"" -Mode Service -ServiceName $svc -Path ""{appPath}""
    Write-Output 'Self-update tamamlandi.'
}}
catch {{ $_ | Out-String | Write-Output }}
finally {{ Stop-Transcript }}
");

        // 5) Güncelleyiciyi SERVİSTEN BAĞIMSIZ başlat (child süreç job objesine bağlı değil —
        //    servis durdurulunca yaşamaya devam eder; standart self-update deseni).
        Process.Start(new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{runner}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = staging
        });

        _logger.LogWarning("Self-update başlatıldı: {From} → {To} (günlük: {Log})", check.Current, check.Latest, logPath);
        return (true, $"Güncelleme başlatıldı: {check.Current} → {check.Latest}. Uygulama birkaç saniye içinde yeniden başlayacak.");
    }

    private static HttpClient NewHttp(TimeSpan? timeout = null)
    {
        var http = new HttpClient { Timeout = timeout ?? TimeSpan.FromSeconds(20) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("vMon-SelfUpdate/1.0");
        return http;
    }
}
