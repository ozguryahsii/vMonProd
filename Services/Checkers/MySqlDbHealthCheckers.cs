using MySqlConnector;
using vMonitor.Models;

namespace vMonitor.Services.Checkers;

/// <summary>DB İzleme Fazı 2 — MySQL sağlık izlemeleri ortak tabanı.
/// Extra = veritabanı adı (opsiyonel; sağlık sorguları sunucu genelidir).
/// Gerekli yetki: GRANT PROCESS (replikasyon için ek REPLICATION CLIENT).
/// Sayısal sonuç OverrideResponseValue'ya yazılır; YavaşlıkEşiği o sayıya eşik olur.</summary>
public abstract class MySqlDbCheckerBase : CheckerBase
{
    protected async Task<MySqlConnection> OpenAsync(MonitoredService service, Credential? credential, CancellationToken ct)
    {
        if (credential == null) throw new InvalidOperationException("Kimlik bilgisi tanımlı değil");

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

        var conn = new MySqlConnection(csb.ConnectionString);
        await conn.OpenAsync(ct);
        return conn;
    }

    protected static async Task<T?> ScalarAsync<T>(MySqlConnection conn, string sql, CancellationToken ct)
    {
        await using var cmd = new MySqlCommand(sql, conn);
        var v = await cmd.ExecuteScalarAsync(ct);
        return v is null or DBNull ? default : (T)Convert.ChangeType(v, typeof(T));
    }
}

/// <summary>MySQL NOW: bağlantı doğrulanır, gecikme grafiğe yazılır.
/// DB saati uygulama sunucusundan 60 sn'den fazla sapıyorsa ERROR (kesinti değil, uyarı).</summary>
public class MySqlNowChecker : MySqlDbCheckerBase
{
    public override ServiceType Type => ServiceType.MySqlNow;

    protected override async Task<string?> ExecuteAsync(MonitoredService service, Credential? credential, CancellationToken ct)
    {
        await using var conn = await OpenAsync(service, credential, ct);
        var dbNow = await ScalarAsync<DateTime>(conn, "SELECT NOW()", ct);
        var driftSec = Math.Abs((DateTime.Now - dbNow).TotalSeconds);
        if (driftSec > 60)
        {
            IsThresholdError = true;
            return $"DB saati sapması: {Math.Round(driftSec)} sn (DB: {dbNow:HH:mm:ss}, sunucu: {DateTime.Now:HH:mm:ss})";
        }
        return null;
    }
}

/// <summary>MySQL Aktif Sessions: uyumayan (COMMAND <> 'Sleep') bağlantı adedi.
/// Adet grafiğe yazılır; YavaşlıkEşiği doluysa aşımı YAVAŞ işaretler.</summary>
public class MySqlActiveSessionsChecker : MySqlDbCheckerBase
{
    public override ServiceType Type => ServiceType.MySqlActiveSessions;

    protected override async Task<string?> ExecuteAsync(MonitoredService service, Credential? credential, CancellationToken ct)
    {
        await using var conn = await OpenAsync(service, credential, ct);
        var count = await ScalarAsync<long>(conn,
            "SELECT COUNT(*) FROM information_schema.PROCESSLIST WHERE COMMAND <> 'Sleep'", ct);
        OverrideResponseValue = count;
        return null;
    }
}

/// <summary>MySQL Blocked Sessions: kilit bekleyen (LOCK WAIT) InnoDB işlem adedi.
/// Adet grafiğe yazılır; 0'dan büyükse ERROR (alarm) üretir — kesinti sayılmaz.</summary>
public class MySqlBlockedSessionsChecker : MySqlDbCheckerBase
{
    public override ServiceType Type => ServiceType.MySqlBlockedSessions;

    protected override async Task<string?> ExecuteAsync(MonitoredService service, Credential? credential, CancellationToken ct)
    {
        await using var conn = await OpenAsync(service, credential, ct);
        var count = await ScalarAsync<long>(conn,
            "SELECT COUNT(*) FROM information_schema.INNODB_TRX WHERE trx_state = 'LOCK WAIT'", ct);
        OverrideResponseValue = count;
        if (count > 0)
        {
            IsThresholdError = true;
            return $"{count} işlem kilit bekliyor (LOCK WAIT)";
        }
        return null;
    }
}

/// <summary>MySQL Uzun Süren Sorgular: 60 sn'den uzun süredir çalışan sorgu adedi.
/// Adet grafiğe yazılır; YavaşlıkEşiği doluysa aşımı YAVAŞ işaretler.</summary>
public class MySqlLongQueriesChecker : MySqlDbCheckerBase
{
    public override ServiceType Type => ServiceType.MySqlLongQueries;

    protected override async Task<string?> ExecuteAsync(MonitoredService service, Credential? credential, CancellationToken ct)
    {
        await using var conn = await OpenAsync(service, credential, ct);
        var count = await ScalarAsync<long>(conn,
            "SELECT COUNT(*) FROM information_schema.PROCESSLIST WHERE COMMAND <> 'Sleep' AND TIME > 60", ct);
        OverrideResponseValue = count;
        return null;
    }
}

/// <summary>MySQL Replikasyon Sağlığı: IO/SQL thread çalışıyor mu + kaynağın kaç sn gerisinde.
/// Gecikme (sn) grafiğe yazılır; thread durmuşsa veya gecikme bilinmiyorsa ERROR üretir.
/// YavaşlıkEşiği doluysa gecikme o sn eşiğini aşınca YAVAŞ işaretlenir. Sunucu replika değilse ERROR.</summary>
public class MySqlReplicationChecker : MySqlDbCheckerBase
{
    public override ServiceType Type => ServiceType.MySqlReplication;

    protected override async Task<string?> ExecuteAsync(MonitoredService service, Credential? credential, CancellationToken ct)
    {
        await using var conn = await OpenAsync(service, credential, ct);

        // MySQL 8.0.22+ = SHOW REPLICA STATUS; eskiler = SHOW SLAVE STATUS (kolon adları da farklı)
        var (found, io, sql, behind) = await ReadStatusAsync(conn, "SHOW REPLICA STATUS",
            "Replica_IO_Running", "Replica_SQL_Running", "Seconds_Behind_Source", ct);
        if (!found)
            (found, io, sql, behind) = await ReadStatusAsync(conn, "SHOW SLAVE STATUS",
                "Slave_IO_Running", "Slave_SQL_Running", "Seconds_Behind_Master", ct);

        if (!found)
        {
            IsThresholdError = true;
            return "Bu sunucu replika değil (replikasyon durumu boş)";
        }

        OverrideResponseValue = behind ?? 0;
        if (io != "Yes" || sql != "Yes")
        {
            IsThresholdError = true;
            return $"Replikasyon thread'i durmuş (IO: {io}, SQL: {sql})";
        }
        if (behind == null)
        {
            IsThresholdError = true;
            return "Replikasyon gecikmesi bilinmiyor (Seconds_Behind = NULL)";
        }
        return null;
    }

    private static async Task<(bool found, string io, string sql, long? behind)> ReadStatusAsync(
        MySqlConnection conn, string statement, string ioCol, string sqlCol, string behindCol, CancellationToken ct)
    {
        try
        {
            await using var cmd = new MySqlCommand(statement, conn);
            await using var rd = await cmd.ExecuteReaderAsync(ct);
            if (!await rd.ReadAsync(ct)) return (false, "", "", null);
            var io = rd.GetString(rd.GetOrdinal(ioCol));
            var sql = rd.GetString(rd.GetOrdinal(sqlCol));
            var bi = rd.GetOrdinal(behindCol);
            long? behind = rd.IsDBNull(bi) ? null : Convert.ToInt64(rd.GetValue(bi));
            return (true, io, sql, behind);
        }
        catch (MySqlException)
        {
            // eski/yeni sözdizimi bu sürümde yoksa diğer varyant denensin
            return (false, "", "", null);
        }
    }
}

/// <summary>MySQL Bağlantı Doluluğu: bağlantı adedinin max_connections limitine oranı (%).
/// Yüzde grafiğe yazılır; YavaşlıkEşiği doluysa aşımı YAVAŞ işaretler (örn. 90).</summary>
public class MySqlConnectionUsageChecker : MySqlDbCheckerBase
{
    public override ServiceType Type => ServiceType.MySqlConnectionUsage;

    protected override async Task<string?> ExecuteAsync(MonitoredService service, Credential? credential, CancellationToken ct)
    {
        await using var conn = await OpenAsync(service, credential, ct);
        var pct = await ScalarAsync<long>(conn,
            "SELECT ROUND(COUNT(*) * 100 / @@max_connections) FROM information_schema.PROCESSLIST", ct);
        OverrideResponseValue = pct;
        return null;
    }
}
