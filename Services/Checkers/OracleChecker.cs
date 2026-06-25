using Oracle.ManagedDataAccess.Client;
using vMonitor.Models;

namespace vMonitor.Services.Checkers;

/// <summary>Oracle: read-only kullanıcıyla bağlanıp SELECT 1 FROM DUAL çalıştırır.
/// Extra = service name (örn. ORCLPDB1). SID kullanılacaksa başına "SID=" yazılır.</summary>
public class OracleChecker : CheckerBase
{
    public override ServiceType Type => ServiceType.Oracle;

    protected override async Task<string?> ExecuteAsync(MonitoredService service, Credential? credential, CancellationToken ct)
    {
        if (credential == null) return "Kimlik bilgisi tanımlı değil";
        if (string.IsNullOrWhiteSpace(service.Extra)) return "Service name (Extra) tanımlı değil";

        var port = service.Port ?? 1521;
        var connect = service.Extra.StartsWith("SID=", StringComparison.OrdinalIgnoreCase)
            ? $"(DESCRIPTION=(ADDRESS=(PROTOCOL=TCP)(HOST={service.Target})(PORT={port}))(CONNECT_DATA=(SID={service.Extra[4..]})))"
            : $"(DESCRIPTION=(ADDRESS=(PROTOCOL=TCP)(HOST={service.Target})(PORT={port}))(CONNECT_DATA=(SERVICE_NAME={service.Extra})))";

        var csb = new OracleConnectionStringBuilder
        {
            DataSource = connect,
            UserID = PlainUsername(credential),
            Password = PlainPassword(credential),
            ConnectionTimeout = service.TimeoutSeconds
        };

        await using var conn = new OracleConnection(csb.ConnectionString);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM DUAL";
        await cmd.ExecuteScalarAsync(ct);
        return null;
    }
}
