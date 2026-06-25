using System.DirectoryServices.Protocols;
using System.Net;
using vMonitor.Models;

namespace vMonitor.Services;

public record LdapAuthResult(bool Success, string? Error = null, string? DisplayName = null, string? Sam = null);

/// <summary>LDAP/Active Directory ile oturum doğrulama. Kullanıcı kendi kimliğiyle
/// bind eder (şifre doğrulanır), ardından izin verilen güvenlik grubunun üyesi mi
/// diye iç içe grupları da kapsayan AD zincir kuralıyla kontrol edilir.</summary>
public class LdapAuthService
{
    public LdapAuthResult Validate(MonitorSettings s, string username, string password)
    {
        if (string.IsNullOrWhiteSpace(s.LdapAuthHost))
            return new(false, "LDAP sunucusu Ayarlar'da tanımlı değil.");
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password))
            return new(false, "Kullanıcı adı ve şifre gerekli.");

        // Aramada kullanılacak sAMAccountName: DOMAIN\user veya user@domain girilse de sade ad
        var sam = username;
        if (sam.Contains('\\')) sam = sam[(sam.IndexOf('\\') + 1)..];
        else if (sam.Contains('@')) sam = sam[..sam.IndexOf('@')];

        try
        {
            var port = s.LdapAuthPort > 0 ? s.LdapAuthPort : (s.LdapAuthUseSsl ? 636 : 389);
            var id = new LdapDirectoryIdentifier(s.LdapAuthHost, port, fullyQualifiedDnsHostName: true, connectionless: false);
            var cred = string.IsNullOrWhiteSpace(s.LdapAuthDomain)
                ? new NetworkCredential(username, password)
                : new NetworkCredential(sam, password, s.LdapAuthDomain);

            using var conn = new LdapConnection(id, cred, AuthType.Negotiate);
            conn.SessionOptions.ProtocolVersion = 3;
            conn.Timeout = TimeSpan.FromSeconds(15);
            if (s.LdapAuthUseSsl)
            {
                conn.SessionOptions.SecureSocketLayer = true;
                // Varsayılan: sertifika doğrulanır. Yalnızca "iç sertifikalara güven" açıkken gevşetilir
                // (PCI DSS 4.2.1, NIST SC-8).
                if (s.TrustInternalTlsCertificates)
                    conn.SessionOptions.VerifyServerCertificate = (_, _) => true;
            }

            conn.Bind(); // hatalı şifrede LdapException fırlatır

            // Grup üyeliği kontrolü (boşsa: geçerli kimlikli herkes girebilir)
            if (!string.IsNullOrWhiteSpace(s.LdapAuthGroupDn))
            {
                if (string.IsNullOrWhiteSpace(s.LdapAuthBaseDn))
                    return new(false, "Arama temel DN'i (BaseDN) tanımlı değil.");

                // memberOf zincir kuralı (1.2.840.113556.1.4.1941) → iç içe grup üyeliğini de yakalar
                var filter = $"(&(sAMAccountName={EscapeFilter(sam)})" +
                             $"(memberOf:1.2.840.113556.1.4.1941:={EscapeFilter(s.LdapAuthGroupDn)}))";
                var req = new SearchRequest(s.LdapAuthBaseDn, filter, SearchScope.Subtree,
                    "displayName", "distinguishedName");
                var resp = (SearchResponse)conn.SendRequest(req);

                if (resp.Entries.Count == 0)
                    return new(false, "Bu uygulamaya erişim yetkiniz yok (gerekli güvenlik grubunda değilsiniz).");

                var display = resp.Entries[0].Attributes["displayName"]?[0]?.ToString();
                return new(true, null, string.IsNullOrWhiteSpace(display) ? sam : display, sam);
            }

            return new(true, null, sam, sam);
        }
        catch (LdapException ex) when (ex.ErrorCode == 49) // invalid credentials
        {
            return new(false, "Kullanıcı adı veya şifre hatalı.");
        }
        catch (LdapException ex)
        {
            return new(false, $"LDAP hatası: {ex.Message}");
        }
        catch (Exception ex)
        {
            return new(false, $"LDAP bağlantı hatası: {ex.Message}");
        }
    }

    public record GroupMember(string Sam, string? DisplayName);

    /// <summary>Ayarlardaki güvenlik grubunun üyelerini (iç içe gruplar dahil) seçilen kimlikle listeler.
    /// Kimlik bilgisi Vault destekli olabilir (kullanıcı/şifre Vault'tan çözülür).</summary>
    public (string? Error, List<GroupMember> Members) ListGroupMembers(MonitorSettings s, Credential? syncCred)
    {
        var list = new List<GroupMember>();
        if (string.IsNullOrWhiteSpace(s.LdapAuthHost)) return ("LDAP sunucusu tanımlı değil.", list);
        if (string.IsNullOrWhiteSpace(s.LdapAuthGroupDn)) return ("İzin verilen güvenlik grubu (DN) tanımlı değil.", list);
        if (string.IsNullOrWhiteSpace(s.LdapAuthBaseDn)) return ("Arama temel DN'i tanımlı değil.", list);
        if (syncCred == null) return ("Senkronizasyon kimlik bilgisi seçilmedi (Ayarlar → Senkron Kimliği).", list);

        try
        {
            var port = s.LdapAuthPort > 0 ? s.LdapAuthPort : (s.LdapAuthUseSsl ? 636 : 389);
            var id = new LdapDirectoryIdentifier(s.LdapAuthHost, port, true, false);
            var user = VaultClient.GetUsername(syncCred);
            var pwd = VaultClient.GetPassword(syncCred);
            var domain = !string.IsNullOrWhiteSpace(syncCred.Domain) ? syncCred.Domain : s.LdapAuthDomain;
            var cred = string.IsNullOrWhiteSpace(domain)
                ? new NetworkCredential(user, pwd)
                : new NetworkCredential(user, pwd, domain);

            using var conn = new LdapConnection(id, cred, AuthType.Negotiate);
            conn.SessionOptions.ProtocolVersion = 3;
            conn.Timeout = TimeSpan.FromSeconds(30);
            if (s.LdapAuthUseSsl)
            {
                conn.SessionOptions.SecureSocketLayer = true;
                // Varsayılan: sertifika doğrulanır. Yalnızca "iç sertifikalara güven" açıkken gevşetilir
                // (PCI DSS 4.2.1, NIST SC-8).
                if (s.TrustInternalTlsCertificates)
                    conn.SessionOptions.VerifyServerCertificate = (_, _) => true;
            }
            conn.Bind();

            var filter = $"(&(objectCategory=person)(objectClass=user)" +
                         $"(memberOf:1.2.840.113556.1.4.1941:={EscapeFilter(s.LdapAuthGroupDn)}))";
            var pageControl = new PageResultRequestControl(500);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            while (true)
            {
                var req = new SearchRequest(s.LdapAuthBaseDn, filter, SearchScope.Subtree, "sAMAccountName", "displayName");
                req.Controls.Add(pageControl);
                var resp = (SearchResponse)conn.SendRequest(req);
                foreach (SearchResultEntry e in resp.Entries)
                {
                    var sam = e.Attributes["sAMAccountName"]?[0]?.ToString();
                    if (string.IsNullOrWhiteSpace(sam) || !seen.Add(sam)) continue;
                    list.Add(new GroupMember(sam, e.Attributes["displayName"]?[0]?.ToString()));
                }
                var prc = resp.Controls.OfType<PageResultResponseControl>().FirstOrDefault();
                if (prc == null || prc.Cookie.Length == 0) break;
                pageControl.Cookie = prc.Cookie;
            }
            return (null, list);
        }
        catch (LdapException ex) when (ex.ErrorCode == 49) { return ("Senkron hesabı kullanıcı adı/şifresi hatalı.", list); }
        catch (Exception ex) { return (ex.Message, list); }
    }

    /// <summary>LDAP arama filtresi için özel karakter kaçışı (RFC 4515).</summary>
    private static string EscapeFilter(string value)
    {
        var sb = new System.Text.StringBuilder(value.Length);
        foreach (var c in value)
        {
            switch (c)
            {
                case '\\': sb.Append("\\5c"); break;
                case '*': sb.Append("\\2a"); break;
                case '(': sb.Append("\\28"); break;
                case ')': sb.Append("\\29"); break;
                case '\0': sb.Append("\\00"); break;
                default: sb.Append(c); break;
            }
        }
        return sb.ToString();
    }
}
