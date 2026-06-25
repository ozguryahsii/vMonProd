using Microsoft.Data.SqlClient;
using vMonitor.Models;

namespace vMonitor.Services.Checkers;

/// <summary>MSSQL: yetkili kullanıcıyla bağlanıp SELECT 1 çalıştırır.
/// Extra = veritabanı adı (opsiyonel). Credential boşsa Windows auth (app pool hesabı) denenir.</summary>
public class MsSqlChecker : CheckerBase
{
    public override ServiceType Type => ServiceType.MsSql;

    protected override async Task<string?> ExecuteAsync(MonitoredService service, Credential? credential, CancellationToken ct)
    {
        var csb = new SqlConnectionStringBuilder
        {
            DataSource = service.Port.HasValue ? $"{service.Target},{service.Port}" : service.Target,
            ConnectTimeout = service.TimeoutSeconds,
            Encrypt = service.UseSsl,
            TrustServerCertificate = service.IgnoreCertErrors
        };
        if (!string.IsNullOrWhiteSpace(service.Extra)) csb.InitialCatalog = service.Extra;

        if (credential != null)
        {
            csb.UserID = PlainUsername(credential);
            csb.Password = PlainPassword(credential);
        }
        else
        {
            csb.IntegratedSecurity = true;
        }

        await using var conn = new SqlConnection(csb.ConnectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand("SELECT 1", conn);
        await cmd.ExecuteScalarAsync(ct);
        return null;
    }
}
