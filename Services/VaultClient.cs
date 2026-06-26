using System.Collections.Concurrent;
using System.Text.Json;
using vMonitor.Models;

namespace vMonitor.Services;

/// <summary>HashiCorp Vault'tan kullanıcı adı + şifre çekme (KV v2; v1 yanıtı da tolere edilir).
/// Token DPAPI'den çözülür; secret içeriği 5 dk bellekte önbelleklenir — diske asla yazılmaz.</summary>
public static class VaultClient
{
    /// <summary>İç kurum CA'sıyla imzalı sertifikalara güven (Ayarlar → güvenlik). Varsayılan KAPALI =
    /// tam TLS doğrulaması (PCI DSS 4.2.1, NIST SC-8). Uygulama açılışında ve Ayarlar kaydında güncellenir.</summary>
    public static volatile bool TrustInternalCertificates = false;

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var handler = new HttpClientHandler
        {
            // Varsayılan: sertifika zinciri tam doğrulanır. Yalnızca ayar açıkken zincir hataları yoksayılır
            // (iç ağ Vault'u genel güven deposunda olmayan kurum içi CA kullanıyorsa).
            ServerCertificateCustomValidationCallback = (_, _, _, errors) =>
                errors == System.Net.Security.SslPolicyErrors.None || TrustInternalCertificates,
            // curl gibi DOĞRUDAN bağlan — kurumsal proxy iç Vault adresini engellemesin/değiştirmesin
            UseProxy = false,
            AllowAutoRedirect = false
        };
        var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };
        // Bazı ağ geçitleri User-Agent'sız isteği reddeder; curl da bir UA gönderir
        client.DefaultRequestHeaders.UserAgent.ParseAdd("vMon/1.0");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        return client;
    }

    private static readonly ConcurrentDictionary<int, (DateTime FetchedAt, Dictionary<string, string> Data)> Cache = new();
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    /// <summary>Şifre: Manuel → DPAPI çöz, Vault → secret'taki VaultKey alanı.</summary>
    public static string GetPassword(Credential c)
    {
        if (c.SourceType == CredentialSource.Manual)
            return CryptoHelper.Decrypt(c.PasswordEncrypted);
        return GetField(c, c.VaultKey, "şifre");
    }

    /// <summary>Kullanıcı adı: Manuel → karttaki değer, Vault → secret'taki VaultUserKey alanı
    /// (anahtar boşsa karttaki değere düşer).</summary>
    public static string GetUsername(Credential c)
    {
        if (c.SourceType == CredentialSource.Manual || string.IsNullOrWhiteSpace(c.VaultUserKey))
            return c.Username;
        return GetField(c, c.VaultUserKey, "kullanıcı adı");
    }

    /// <summary>Kimlik kaydedildiğinde önbelleği temizle (token/url/key değişmiş olabilir).</summary>
    public static void Invalidate(int credentialId) => Cache.TryRemove(credentialId, out _);

    /// <summary>Bağlantı testi: secret erişimi + anahtar varlıkları. Şifre değeri asla dönmez;
    /// doğrulama için çözülen kullanıcı adı döner.</summary>
    public static async Task<(string? Error, string? ResolvedUsername)> TestAsync(Credential c, CancellationToken ct = default)
    {
        try
        {
            var data = await FetchSecretAsync(c, ct);
            if (!string.IsNullOrWhiteSpace(c.VaultKey) && !data.ContainsKey(c.VaultKey))
                return ($"Secret içinde şifre anahtarı '{c.VaultKey}' yok. Mevcut anahtarlar: {string.Join(", ", data.Keys.Take(10))}", null);

            string username = c.Username;
            if (!string.IsNullOrWhiteSpace(c.VaultUserKey))
            {
                if (!data.TryGetValue(c.VaultUserKey, out var u))
                    return ($"Secret içinde kullanıcı adı anahtarı '{c.VaultUserKey}' yok. Mevcut anahtarlar: {string.Join(", ", data.Keys.Take(10))}", null);
                username = u;
            }
            return (null, username);
        }
        catch (Exception ex)
        {
            return (ex.Message, null);
        }
    }

    private static string GetField(Credential c, string? key, string label)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new InvalidOperationException($"Vault {label} anahtarı tanımlı değil.");

        var data = GetSecretData(c);
        if (!data.TryGetValue(key, out var value))
            throw new InvalidOperationException(
                $"Secret içinde '{key}' anahtarı yok. Mevcut anahtarlar: {string.Join(", ", data.Keys.Take(10))}");
        return value;
    }

    private static Dictionary<string, string> GetSecretData(Credential c)
    {
        if (Cache.TryGetValue(c.Id, out var cached) && DateTime.UtcNow - cached.FetchedAt < CacheTtl)
            return cached.Data;

        var data = FetchSecretAsync(c).GetAwaiter().GetResult();
        Cache[c.Id] = (DateTime.UtcNow, data);
        return data;
    }

    private static async Task<Dictionary<string, string>> FetchSecretAsync(Credential c, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(c.VaultUrl)) throw new InvalidOperationException("Vault URL tanımlı değil.");
        if (string.IsNullOrWhiteSpace(c.VaultTokenEncrypted)) throw new InvalidOperationException("Vault token tanımlı değil.");

        var url = c.VaultUrl.Trim();
        if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            url = "https://" + url;

        // Token'daki görünmez karakterleri temizle (kopyala-yapıştır kaynaklı boşluk,
        // satır sonu, kıvrık tırnak) — curl'de bunlar elle ayıklanır, bizde de ayıklayalım
        var token = CryptoHelper.Decrypt(c.VaultTokenEncrypted)
            .Trim().Trim('"', '\'', '“', '”', '‘', '’');

        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        // TryAddWithoutValidation: token'ı curl gibi ham gönder, .NET başlık doğrulamasına takılma
        req.Headers.TryAddWithoutValidation("X-Vault-Token", token);

        using var resp = await Http.SendAsync(req, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode)
        {
            var hint = (int)resp.StatusCode switch
            {
                400 => "İstek reddedildi — namespace gerekiyor olabilir veya path biçimi hatalı.",
                403 => "Token geçersiz/süresi dolmuş ya da bu secret'a okuma yetkisi yok.",
                404 => "Secret bulunamadı — URL'deki path'i kontrol edin (KV v2'de /data/ segmenti gerekir).",
                _ => ""
            };
            // Vault'un gerçek hata gövdesini de göster — kök nedeni doğrudan görelim
            var snippet = body.Length > 300 ? body[..300] : body;
            throw new InvalidOperationException(
                $"Vault HTTP {(int)resp.StatusCode}. {hint} Yanıt: {snippet}".Trim());
        }

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        // KV v2: data.data.{key} — KV v1: data.{key}
        JsonElement dataEl;
        if (root.TryGetProperty("data", out var d1) && d1.ValueKind == JsonValueKind.Object)
            dataEl = d1.TryGetProperty("data", out var d2) && d2.ValueKind == JsonValueKind.Object ? d2 : d1;
        else
            throw new InvalidOperationException("Vault yanıtında 'data' alanı yok — beklenmeyen yanıt biçimi.");

        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var prop in dataEl.EnumerateObject())
            result[prop.Name] = prop.Value.ValueKind == JsonValueKind.String
                ? prop.Value.GetString() ?? ""
                : prop.Value.GetRawText();
        return result;
    }
}
