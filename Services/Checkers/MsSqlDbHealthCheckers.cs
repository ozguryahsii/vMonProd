using Microsoft.Data.SqlClient;
using vMonitor.Models;

namespace vMonitor.Services.Checkers;

/// <summary>DB İzleme Fazı 2 — MSSQL sağlık izlemeleri ortak tabanı.
/// Extra = veritabanı adı (opsiyonel; sağlık sorguları sunucu genelidir, boşsa master).
/// Gerekli yetki: GRANT VIEW SERVER STATE. Sayısal sonuç OverrideResponseValue'ya yazılır
/// (grafikte adet/% görünür); YavaşlıkEşiği (ResponseTimeThresholdMs) o sayıya eşik olur.</summary>
public abstract class MsSqlDbCheckerBase : CheckerBase
{
    protected async Task<SqlConnection> OpenAsync(MonitoredService service, Credential? credential, CancellationToken ct)
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
            csb.IntegratedSecurity = true;   // klasik MSSQL checker ile aynı davranış
        }

        var conn = new SqlConnection(csb.ConnectionString);
        await conn.OpenAsync(ct);
        return conn;
    }

    protected static async Task<T?> ScalarAsync<T>(SqlConnection conn, string sql, CancellationToken ct)
    {
        await using var cmd = new SqlCommand(sql, conn);
        var v = await cmd.ExecuteScalarAsync(ct);
        return v is null or DBNull ? default : (T)Convert.ChangeType(v, typeof(T));
    }
}

/// <summary>MSSQL GETDATE: bağlantı doğrulanır, gecikme grafiğe yazılır.
/// DB saati uygulama sunucusundan 60 sn'den fazla sapıyorsa ERROR (kesinti değil, uyarı).</summary>
public class MsSqlGetDateChecker : MsSqlDbCheckerBase
{
    public override ServiceType Type => ServiceType.MsSqlGetDate;

    protected override async Task<string?> ExecuteAsync(MonitoredService service, Credential? credential, CancellationToken ct)
    {
        await using var conn = await OpenAsync(service, credential, ct);
        var dbNow = await ScalarAsync<DateTime>(conn, "SELECT GETDATE()", ct);
        var driftSec = Math.Abs((DateTime.Now - dbNow).TotalSeconds);
        if (driftSec > 60)
        {
            IsThresholdError = true;
            return $"DB saati sapması: {Math.Round(driftSec)} sn (DB: {dbNow:HH:mm:ss}, sunucu: {DateTime.Now:HH:mm:ss})";
        }
        return null;
    }
}

/// <summary>MSSQL Aktif Sessions: çalışan (status='running') kullanıcı oturumu adedi.
/// Adet grafiğe yazılır; YavaşlıkEşiği doluysa aşımı YAVAŞ işaretler.</summary>
public class MsSqlActiveSessionsChecker : MsSqlDbCheckerBase
{
    public override ServiceType Type => ServiceType.MsSqlActiveSessions;

    protected override async Task<string?> ExecuteAsync(MonitoredService service, Credential? credential, CancellationToken ct)
    {
        await using var conn = await OpenAsync(service, credential, ct);
        var count = await ScalarAsync<long>(conn,
            "SELECT COUNT_BIG(*) FROM sys.dm_exec_sessions WHERE is_user_process = 1 AND status = 'running'", ct);
        OverrideResponseValue = count;
        return null;
    }
}

/// <summary>MSSQL Blocked Sessions: başka oturum tarafından bloklanan istek adedi.
/// Adet grafiğe yazılır; 0'dan büyükse ERROR (alarm) üretir — kesinti sayılmaz.</summary>
public class MsSqlBlockedSessionsChecker : MsSqlDbCheckerBase
{
    public override ServiceType Type => ServiceType.MsSqlBlockedSessions;

    protected override async Task<string?> ExecuteAsync(MonitoredService service, Credential? credential, CancellationToken ct)
    {
        await using var conn = await OpenAsync(service, credential, ct);
        var count = await ScalarAsync<long>(conn,
            "SELECT COUNT_BIG(*) FROM sys.dm_exec_requests WHERE blocking_session_id <> 0", ct);
        OverrideResponseValue = count;
        if (count > 0)
        {
            IsThresholdError = true;
            return $"{count} oturum bloklanmış durumda";
        }
        return null;
    }
}

/// <summary>MSSQL Uzun Süren Sorgular: 60 sn'den uzun süredir çalışan kullanıcı isteği adedi.
/// Adet grafiğe yazılır; YavaşlıkEşiği doluysa aşımı YAVAŞ işaretler.</summary>
public class MsSqlLongQueriesChecker : MsSqlDbCheckerBase
{
    public override ServiceType Type => ServiceType.MsSqlLongQueries;

    protected override async Task<string?> ExecuteAsync(MonitoredService service, Credential? credential, CancellationToken ct)
    {
        await using var conn = await OpenAsync(service, credential, ct);
        var count = await ScalarAsync<long>(conn,
            "SELECT COUNT_BIG(*) FROM sys.dm_exec_requests r " +
            "JOIN sys.dm_exec_sessions s ON s.session_id = r.session_id " +
            "WHERE s.is_user_process = 1 AND r.total_elapsed_time > 60000", ct);
        OverrideResponseValue = count;
        return null;
    }
}

/// <summary>MSSQL DB Durumu: ONLINE olmayan veritabanı adedi (suspect/recovery/offline yakalar).
/// Adet grafiğe yazılır; 0'dan büyükse hangi DB'lerin sorunlu olduğu mesaja yazılıp ERROR üretilir.</summary>
public class MsSqlDbStatusChecker : MsSqlDbCheckerBase
{
    public override ServiceType Type => ServiceType.MsSqlDbStatus;

    protected override async Task<string?> ExecuteAsync(MonitoredService service, Credential? credential, CancellationToken ct)
    {
        await using var conn = await OpenAsync(service, credential, ct);
        var count = await ScalarAsync<long>(conn,
            "SELECT COUNT_BIG(*) FROM sys.databases WHERE state_desc <> 'ONLINE'", ct);
        OverrideResponseValue = count;
        if (count > 0)
        {
            var names = new List<string>();
            await using (var cmd = new SqlCommand(
                "SELECT TOP 5 name + ' (' + state_desc + ')' FROM sys.databases WHERE state_desc <> 'ONLINE' ORDER BY name", conn))
            await using (var rd = await cmd.ExecuteReaderAsync(ct))
                while (await rd.ReadAsync(ct)) names.Add(rd.GetString(0));
            IsThresholdError = true;
            return $"{count} veritabanı ONLINE değil: {string.Join(", ", names)}{(count > 5 ? " …" : "")}";
        }
        return null;
    }
}

/// <summary>MSSQL Bağlantı Doluluğu: kullanıcı oturumu adedinin bağlantı limitine oranı (%).
/// 'user connections' limiti ayarlıysa (0 değilse) o, değilse 32767 varsayılanı kullanılır.
/// Yüzde grafiğe yazılır; YavaşlıkEşiği doluysa aşımı YAVAŞ işaretler (örn. 90).</summary>
public class MsSqlConnectionUsageChecker : MsSqlDbCheckerBase
{
    public override ServiceType Type => ServiceType.MsSqlConnectionUsage;

    protected override async Task<string?> ExecuteAsync(MonitoredService service, Credential? credential, CancellationToken ct)
    {
        await using var conn = await OpenAsync(service, credential, ct);
        var pct = await ScalarAsync<long>(conn,
            "SELECT CAST(ROUND(COUNT_BIG(*) * 100.0 / " +
            "(SELECT CASE WHEN CAST(value_in_use AS int) = 0 THEN 32767 ELSE CAST(value_in_use AS int) END " +
            " FROM sys.configurations WHERE name = 'user connections'), 0) AS bigint) " +
            "FROM sys.dm_exec_sessions WHERE is_user_process = 1", ct);
        OverrideResponseValue = pct;
        return null;
    }
}
