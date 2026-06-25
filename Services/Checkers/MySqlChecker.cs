using MySqlConnector;
using vMonitor.Models;

namespace vMonitor.Services.Checkers;

/// <summary>MySQL: yetkili kullanıcıyla bağlanıp SELECT 1 çalıştırır.
/// Extra = veritabanı adı (opsiyonel).</summary>
public class MySqlChecker : CheckerBase
{
    public override ServiceType Type => ServiceType.MySql;

    protected override async Task<string?> ExecuteAsync(MonitoredService service, Credential? credential, CancellationToken ct)
    {
        if (credential == null) return "Kimlik bilgisi tanımlı değil";

        var csb = new MySqlConnectionStringBuilder
        {
            Server = service.Target,
            Port = (uint)(service.Port ?? 3306),
            UserID = PlainUsername(credential),
            Password = PlainPassword(credential),
            ConnectionTimeout = (uint)service.TimeoutSeconds,
            SslMode = service.UseSsl ? MySqlSslMode.Required : MySqlSslMode.Preferred
        };
        if (!string.IsNullOrWhiteSpace(service.Extra)) csb.Database = service.Extra;

        await using var conn = new MySqlConnection(csb.ConnectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new MySqlCommand("SELECT 1", conn);
        await cmd.ExecuteScalarAsync(ct);
        return null;
    }
}
