using System.Security.Claims;

namespace vMonitor.Models;

/// <summary>Uygulamaya giriş yapmış (yetkili güvenlik grubundan) kullanıcı ve
/// kendisine atanmış granüler yetkiler.</summary>
public class AppUser
{
    public int Id { get; set; }
    public string Sam { get; set; } = "";
    public string? DisplayName { get; set; }
    /// <summary>Virgülle ayrılmış izin anahtarları (bkz. Perms).</summary>
    public string PermissionsCsv { get; set; } = "";
    public DateTime? LastLogin { get; set; }

    /// <summary>Yetkili AD güvenlik grubunda mı? Senkronizasyonda gruptan düşenler pasifleştirilir;
    /// pasif kullanıcı giriş yapamaz (PCI DSS 8.2.4-8.2.5, ISO 27001 A.5.18).</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>WhatsApp/SMS interaktif yanıt için telefon (E.164). Gelen buton bu numarayla kullanıcıya eşlenir.</summary>
    public string? Phone { get; set; }

    /// <summary>Kullanıcı arayüz teması: "light" veya "dark". Sonraki girişlerde hatırlanır.</summary>
    public string Theme { get; set; } = "light";

    /// <summary>Arayüz dili: "tr" veya "en". Sonraki girişlerde hatırlanır.</summary>
    public string Language { get; set; } = "tr";

    /// <summary>Yerel (LDAP olmayan) kullanıcı mı? İlk kurulumda oluşturulan admin yereldir; LDAP senkronundan sonra silinebilir.</summary>
    public bool IsLocal { get; set; } = false;

    /// <summary>Yerel kullanıcı için PBKDF2 şifre hash'i (LDAP kullanıcılarında boş).</summary>
    public string? PasswordHash { get; set; }

    public HashSet<string> Permissions() =>
        (PermissionsCsv ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
}

/// <summary>Granüler izin anahtarları ve okunur adları.</summary>
public static class Perms
{
    public const string DashboardsView = "dashboards.view";   // Dashboard / Raporlar görüntüleme
    public const string ServicesCheck = "services.check";     // Kontrol Et / Tümünü kontrol et
    public const string ServicesControl = "services.control"; // Uzaktan start/stop/restart
    public const string ServicesManage = "services.manage";   // Servis ekle/düzenle/sil, CSV
    public const string DashboardsManage = "dashboards.manage"; // Dashboard oluştur/düzenle/sil
    public const string CredentialsManage = "credentials.manage"; // Kimlik bilgileri yönetimi
    public const string MutabakatView = "mutabakat.view";         // Mutabakat (envanter karşılaştırma) sayfası
    public const string AlarmManage = "alarm.manage";             // WhatsApp/IVR ile servise uzaktan müdahale (start/stop/restart)

    public static readonly (string Key, string Label)[] All =
    {
        (DashboardsView,   "İzleme ekranlarını görüntüle (Dashboard / Raporlar)"),
        (ServicesCheck,    "Servisleri şimdi kontrol et (manuel tetikleme)"),
        (ServicesControl,  "Uzaktan servis kontrolü (başlat / durdur / yeniden başlat)"),
        (ServicesManage,   "Servisleri yönet (ekle / düzenle / sil / CSV)"),
        (DashboardsManage, "Dashboard'ları yönet (oluştur / düzenle / sil)"),
        (CredentialsManage,"Kimlik bilgilerini yönet"),
        (MutabakatView,    "Mutabakat sayfasını görüntüle (envanter karşılaştırma)"),
        (AlarmManage,      "Alarmlara müdahale (WhatsApp/IVR ile servis başlat/durdur/yeniden başlat)")
    };

    /// <summary>İzin kontrolü. Oturum açma kapalıyken (açık mod) veya admin'de her şey serbest.</summary>
    public static bool Can(this ClaimsPrincipal? user, string perm)
    {
        if (user?.Identity?.IsAuthenticated != true) return true; // açık mod
        if (user.HasClaim("admin", "true")) return true;
        return user.HasClaim("perm", perm);
    }

    public static bool IsAppAdmin(this ClaimsPrincipal? user) =>
        user?.Identity?.IsAuthenticated != true || user.HasClaim("admin", "true");
}
