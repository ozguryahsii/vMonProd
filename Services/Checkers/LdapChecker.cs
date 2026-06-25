using System.DirectoryServices.Protocols;
using System.Net;
using vMonitor.Models;

namespace vMonitor.Services.Checkers;

/// <summary>Active Directory / LDAP(S): yetkili kullanıcıyla bind yapılır.
/// Port 636 + UseSsl = LDAPS. Username "DOMAIN\user", "user@domain" veya tam DN olabilir.</summary>
public class LdapChecker : CheckerBase
{
    public override ServiceType Type => ServiceType.Ldap;

    protected override Task<string?> ExecuteAsync(MonitoredService service, Credential? credential, CancellationToken ct)
    {
        if (credential == null) return Task.FromResult<string?>("Kimlik bilgisi tanımlı değil");

        // LdapConnection senkron API — Task.Run ile sarılıyor, timeout'u CheckerBase yönetiyor.
        return Task.Run((Func<string?>)(() =>
        {
            var port = service.Port ?? (service.UseSsl ? 636 : 389);
            var identifier = new LdapDirectoryIdentifier(service.Target, port, fullyQualifiedDnsHostName: true, connectionless: false);

            var username = PlainUsername(credential);
            var cred = string.IsNullOrWhiteSpace(credential.Domain)
                ? new NetworkCredential(username, PlainPassword(credential))
                : new NetworkCredential(username, PlainPassword(credential), credential.Domain);

            // Tam DN ile Basic, aksi halde Negotiate (NTLM/Kerberos) bind
            var authType = username.Contains('=') ? AuthType.Basic : AuthType.Negotiate;

            using var conn = new LdapConnection(identifier, cred, authType);
            conn.SessionOptions.ProtocolVersion = 3;
            conn.Timeout = TimeSpan.FromSeconds(service.TimeoutSeconds);
            if (service.UseSsl)
            {
                conn.SessionOptions.SecureSocketLayer = true;
                if (service.IgnoreCertErrors)
                    conn.SessionOptions.VerifyServerCertificate = (_, _) => true;
            }

            conn.Bind(); // başarısızsa LdapException fırlatır
            return null;
        }), ct);
    }
}
