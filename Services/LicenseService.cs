using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace vMonitor.Services;

// ============================================================================
// LİSANS FAZI L1 — offline imzalı lisans key doğrulama (sunucu GEREKMEZ).
// Key formatı: VMON1.<base64url(payloadJson)>.<base64url(ECDsa-P256-SHA256 imza)>
// (ayraç '.', base64url alfabesinde olmadığı için — JWT deseni)
// Payload: { ed: Basic|Standard|Enterprise, co: firma, iat: yyyy-MM-dd, exp: yyyy-MM-dd }
// KURALLAR: her paket (Basic dahil) key ister; tüm lisanslar YILLIK (exp-iat ≤ 370 gün);
// süresi dolan lisans uygulamayı kilitler (yenileme ekranından yeni key girilir).
// ============================================================================

public enum LicenseEdition { Basic = 0, Standard = 1, Enterprise = 2 }
public enum LicenseStatus { Missing, Invalid, Expired, Valid }

public sealed class LicenseInfo
{
    public LicenseEdition Edition { get; init; }
    public string Company { get; init; } = "";
    public DateTime IssuedAt { get; init; }
    public DateTime ExpiresAt { get; init; }     // bu tarih DAHİL son geçerli gün

    public int DaysLeft => (ExpiresAt.Date - DateTime.Today).Days;
    public bool IsExpired => DateTime.Today > ExpiresAt.Date;

    // ---- Paket limitleri (kullanıcı spesifikasyonu, 2026-07-06) ----
    public int MaxMonitors => Edition switch { LicenseEdition.Basic => 40, LicenseEdition.Standard => 200, _ => int.MaxValue };
    public int MaxUsers => Edition switch { LicenseEdition.Basic => 1, LicenseEdition.Standard => 5, _ => int.MaxValue };
    public int MaxDashboards => Edition == LicenseEdition.Basic ? 5 : int.MaxValue;
    public bool SqliteOnly => Edition == LicenseEdition.Basic;
    public bool EmailOnlyNotifications => Edition == LicenseEdition.Basic;
    public bool SiemAllowed => Edition != LicenseEdition.Basic;
}

public sealed class LicenseService
{
    /// <summary>Uygulamaya gömülü açık anahtar (SubjectPublicKeyInfo). Karşılığı olan ÖZEL anahtar yalnız
    /// satıcıdadır (vmon-lic aracı) — key'i yalnız o üretebilir; buradan özel anahtar TÜRETİLEMEZ.</summary>
    public const string PublicKeyB64 =
        "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAE92ZnkGHky9x0OC3DiQhG4OWWvYTuiG2j5hhw5BCF+txMD2Fvh7rwivGBVsHttvuwdLyD4e+5XrPdO2XQxT1cYA==";

    private readonly BootstrapService _bootstrap;
    private readonly BootstrapConfig _cfg;
    private readonly object _lock = new();

    public LicenseService(BootstrapService bootstrap, BootstrapConfig cfg)
    {
        _bootstrap = bootstrap;
        _cfg = cfg;
        Reload();
    }

    public LicenseStatus Status { get; private set; } = LicenseStatus.Missing;
    public LicenseInfo? Current { get; private set; }

    /// <summary>Geçerli VE süresi dolmamış lisans var mı (uygulama kapısı bunu sorar).</summary>
    public bool IsUsable => Status == LicenseStatus.Valid;

    public void Reload()
    {
        lock (_lock)
        {
            if (string.IsNullOrWhiteSpace(_cfg.LicenseKey)) { Status = LicenseStatus.Missing; Current = null; return; }
            if (!TryParse(_cfg.LicenseKey, out var info, out _)) { Status = LicenseStatus.Invalid; Current = null; return; }
            Current = info;
            Status = info!.IsExpired ? LicenseStatus.Expired : LicenseStatus.Valid;
        }
    }

    /// <summary>Yeni key uygula: doğrula, bootstrap.json'a kalıcı yaz, durumu tazele.</summary>
    public (bool ok, string error) Apply(string key)
    {
        key = Clean(key);   // satır kırılması/boşluk temizlenmiş hali saklanır
        if (!TryParse(key, out var info, out var err)) return (false, err);
        if (info!.IsExpired) return (false, "Bu lisansın süresi dolmuş (bitiş: " + info.ExpiresAt.ToString("dd.MM.yyyy") + "). Yeni bir key alın.");
        lock (_lock)
        {
            _cfg.LicenseKey = key;
            _bootstrap.Save(_cfg);
            Current = info;
            Status = LicenseStatus.Valid;
        }
        return (true, "");
    }

    /// <summary>Bu makinenin lisans bağlama kodu (hostname + Windows MachineGuid özeti).
    /// Müşteri bu kodu satıcıya iletir; satıcı key'i bu koda bağlı üretir (vmon-lic --machine).
    /// Koda bağlı key başka makinede GEÇERSİZDİR. Donanım/hostname değişirse yeni key gerekir.</summary>
    public static string MachineCode
    {
        get
        {
            string guid = "";
            try
            {
                guid = Microsoft.Win32.Registry.LocalMachine
                    .OpenSubKey(@"SOFTWARE\Microsoft\Cryptography")?.GetValue("MachineGuid")?.ToString() ?? "";
            }
            catch { /* okunamazsa yalnız hostname kullanılır */ }
            var h = SHA256.HashData(Encoding.UTF8.GetBytes($"{Environment.MachineName}|{guid}".ToUpperInvariant()));
            return $"{Convert.ToHexString(h, 0, 2)}-{Convert.ToHexString(h, 2, 2)}-{Convert.ToHexString(h, 4, 2)}";
        }
    }

    /// <summary>Key'i çözüp imzasını doğrular; kaydetmez. Setup sihirbazı ve Apply kullanır.</summary>
    /// <summary>Key'i kopya-yapıştır kirinden arındırır: konsol satır kırılması, boşluk, sekme,
    /// görünmez birleşme karakterleri (soft-hyphen/BOM/zero-width) atılır. Müşteri key'i mesajdan/
    /// e-postadan yapıştırırken araya satır sonu girmesi çok yaygın — parser bunları tolere etmeli.</summary>
    public static string Clean(string? key) =>
        new string((key ?? "").Where(c => !char.IsWhiteSpace(c) && c is not ('­' or '﻿' or '​' or '‌' or '‍')).ToArray());

    public static bool TryParse(string? key, out LicenseInfo? info, out string error)
    {
        info = null; error = "";
        key = Clean(key);
        if (key.Length == 0) { error = "Lisans key boş."; return false; }

        var parts = key.Split('.');
        if (parts.Length != 3 || parts[0] != "VMON1") { error = "Lisans key biçimi tanınmadı."; return false; }

        byte[] payload, sig;
        try { payload = FromB64Url(parts[1]); sig = FromB64Url(parts[2]); }
        catch { error = "Lisans key çözülemedi (bozuk kopyalanmış olabilir)."; return false; }

        using var ecdsa = ECDsa.Create();
        try { ecdsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(PublicKeyB64), out _); }
        catch { error = "Uygulama açık anahtarı yüklenemedi."; return false; }
        if (!ecdsa.VerifyData(payload, sig, HashAlgorithmName.SHA256))
        { error = "Lisans imzası geçersiz — key bu ürün için üretilmemiş veya değiştirilmiş."; return false; }

        try
        {
            var j = JsonDocument.Parse(payload).RootElement;
            var edStr = j.GetProperty("ed").GetString() ?? "";
            if (!Enum.TryParse<LicenseEdition>(edStr, true, out var ed))
            { error = "Lisans paketi tanınmadı: " + edStr; return false; }
            var iat = DateTime.ParseExact(j.GetProperty("iat").GetString()!, "yyyy-MM-dd", null);
            var exp = DateTime.ParseExact(j.GetProperty("exp").GetString()!, "yyyy-MM-dd", null);

            // YILLIK kural savunması: 370 günden uzun ömürlü key kabul edilmez (CLI da 365 ile sınırlar).
            if ((exp - iat).TotalDays > 370) { error = "Lisans süresi izin verilen üst sınırı aşıyor."; return false; }
            if (iat > DateTime.Today.AddDays(2)) { error = "Lisans başlangıç tarihi gelecekte görünüyor (sunucu saati?)."; return false; }

            // Makine bağlama: key'de makine kodu varsa bu makineninkiyle eşleşmeli (kopyalama engeli)
            if (j.TryGetProperty("mac", out var mc))
            {
                var want = mc.GetString() ?? "";
                if (!string.IsNullOrWhiteSpace(want) && !string.Equals(want, MachineCode, StringComparison.OrdinalIgnoreCase))
                { error = $"Bu lisans başka bir makine için üretilmiş. Bu makinenin kodu: {MachineCode} — yeni key isterken bu kodu iletin."; return false; }
            }

            info = new LicenseInfo
            {
                Edition = ed,
                Company = j.TryGetProperty("co", out var co) ? co.GetString() ?? "" : "",
                IssuedAt = iat,
                ExpiresAt = exp
            };
            return true;
        }
        catch { error = "Lisans içeriği okunamadı."; return false; }
    }

    private static byte[] FromB64Url(string s)
    {
        s = s.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(s.PadRight(s.Length + (4 - s.Length % 4) % 4, '='));
    }
}
