using Oracle.ManagedDataAccess.Client;
using vMonitor.Models;

namespace vMonitor.Services.Checkers;

/// <summary>Database Fazı — Oracle sağlık izlemeleri ortak tabanı.
/// Extra = service name (SID için "SID=..."). Sonuç sayısal ise OverrideResponseValue'ya yazılır
/// (grafikte adet görünür); YavaşlıkEşiği (ResponseTimeThresholdMs) o sayıya eşik olur.</summary>
public abstract class OracleDbCheckerBase : CheckerBase
{
    protected async Task<OracleConnection> OpenAsync(MonitoredService service, Credential? credential, CancellationToken ct)
    {
        if (credential == null) throw new InvalidOperationException("Kimlik bilgisi tanımlı değil");
        if (string.IsNullOrWhiteSpace(service.Extra)) throw new InvalidOperationException("Service name (Ekstra) tanımlı değil");

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
        var conn = new OracleConnection(csb.ConnectionString);
        await conn.OpenAsync(ct);
        return conn;
    }

    protected static async Task<T?> ScalarAsync<T>(OracleConnection conn, string sql, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        var v = await cmd.ExecuteScalarAsync(ct);
        return v is null or DBNull ? default : (T)Convert.ChangeType(v, typeof(T));
    }
}

/// <summary>Oracle SYSDATE: SELECT SYSDATE FROM DUAL — bağlantı doğrulanır, gecikme grafiğe yazılır.
/// DB saati uygulama sunucusundan 60 sn'den fazla sapıyorsa ERROR (kesinti değil, uyarı).</summary>
public class OracleSysdateChecker : OracleDbCheckerBase
{
    public override ServiceType Type => ServiceType.OracleSysdate;

    protected override async Task<string?> ExecuteAsync(MonitoredService service, Credential? credential, CancellationToken ct)
    {
        await using var conn = await OpenAsync(service, credential, ct);
        var dbNow = await ScalarAsync<DateTime>(conn, "SELECT SYSDATE FROM DUAL", ct);
        var driftSec = Math.Abs((DateTime.Now - dbNow).TotalSeconds);
        if (driftSec > 60)
        {
            IsThresholdError = true;   // ulaşıldı ama saat sapması var → ERROR
            return $"DB saati sapması: {Math.Round(driftSec)} sn (DB: {dbNow:HH:mm:ss}, sunucu: {DateTime.Now:HH:mm:ss})";
        }
        return null;
    }
}

/// <summary>Oracle Aktif Sessions: background olmayan, STATUS='ACTIVE' oturum adedi.
/// Adet grafiğe yazılır; YavaşlıkEşiği doluysa aşımı YAVAŞ işaretler.</summary>
public class OracleActiveSessionsChecker : OracleDbCheckerBase
{
    public override ServiceType Type => ServiceType.OracleActiveSessions;

    protected override async Task<string?> ExecuteAsync(MonitoredService service, Credential? credential, CancellationToken ct)
    {
        await using var conn = await OpenAsync(service, credential, ct);
        var count = await ScalarAsync<long>(conn,
            "SELECT COUNT(*) FROM GV$SESSION WHERE TYPE <> 'BACKGROUND' AND STATUS = 'ACTIVE'", ct);
        OverrideResponseValue = count;
        return null;
    }
}

/// <summary>Oracle Blocked Sessions: BLOCKING_SESSION dolu (bloklanan) oturum adedi.
/// Adet grafiğe yazılır; 0'dan büyükse ERROR (uyarı) üretir — kesinti sayılmaz.</summary>
public class OracleBlockedSessionsChecker : OracleDbCheckerBase
{
    public override ServiceType Type => ServiceType.OracleBlockedSessions;

    protected override async Task<string?> ExecuteAsync(MonitoredService service, Credential? credential, CancellationToken ct)
    {
        await using var conn = await OpenAsync(service, credential, ct);
        var count = await ScalarAsync<long>(conn,
            "SELECT COUNT(*) FROM GV$SESSION WHERE BLOCKING_SESSION IS NOT NULL", ct);
        OverrideResponseValue = count;
        if (count > 0)
        {
            IsThresholdError = true;   // bloklanma var → ERROR (alarm), kesinti değil
            return $"{count} oturum bloklanmış durumda";
        }
        return null;
    }
}
