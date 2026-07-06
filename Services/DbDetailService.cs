using System.Data.Common;
using Microsoft.Data.SqlClient;
using MySqlConnector;
using Oracle.ManagedDataAccess.Client;
using vMonitor.Models;

namespace vMonitor.Services;

/// <summary>DB İzleme detay çekmecesi: bir DB sağlık metriğinin kutucuğuna tıklanınca
/// "kim / hangisi / ne kadar süredir" listesini CANLI çeker. Sorgular DB'yi yormayacak biçimde:
/// yalnız ilgili küçük filtrelenmiş küme, en fazla 50 satır, kısa komut zaman aşımı, salt-okunur.
/// İstek üzerine çalışır (dashboard periyodik yenilemesine BAĞLI DEĞİL).</summary>
public sealed class DbDetailService
{
    private const int MaxRows = 50;
    private const int CommandTimeoutSec = 10;

    public sealed record Result(string Title, IReadOnlyList<string> Columns, IReadOnlyList<IReadOnlyList<string>> Rows, string? Note = null);

    /// <summary>Tipe göre detay döner; detayı olmayan tiplerde null.</summary>
    public async Task<Result?> GetAsync(MonitoredService svc, Credential? cred, CancellationToken ct)
    {
        return svc.Type switch
        {
            // ---- Oracle ----
            ServiceType.OracleActiveSessions => await OracleAsync(svc, cred, ct, "Aktif Oturumlar",
                new[] { "SID", "Kullanıcı", "Makine", "Program", "Süre (sn)" },
                "SELECT * FROM (SELECT s.SID, s.USERNAME, s.MACHINE, s.PROGRAM, s.LAST_CALL_ET " +
                "FROM GV$SESSION s WHERE s.TYPE<>'BACKGROUND' AND s.STATUS='ACTIVE' AND s.USERNAME IS NOT NULL " +
                "ORDER BY s.LAST_CALL_ET DESC) WHERE ROWNUM <= 50"),

            ServiceType.OracleBlockedSessions => await OracleAsync(svc, cred, ct, "Bloklu Oturumlar",
                new[] { "SID", "Kullanıcı", "Bloklayan SID", "Bekleme (sn)", "Olay" },
                "SELECT * FROM (SELECT s.SID, s.USERNAME, s.BLOCKING_SESSION, s.SECONDS_IN_WAIT, s.EVENT " +
                "FROM GV$SESSION s WHERE s.BLOCKING_SESSION IS NOT NULL " +
                "ORDER BY s.SECONDS_IN_WAIT DESC) WHERE ROWNUM <= 50"),

            ServiceType.OracleLongQueries => await OracleAsync(svc, cred, ct, "Uzun Süren Sorgular",
                new[] { "SID", "Kullanıcı", "Süre (sn)", "Sorgu" },
                "SELECT * FROM (SELECT s.SID, s.USERNAME, s.LAST_CALL_ET, SUBSTR(q.SQL_TEXT,1,200) SQL_TEXT " +
                "FROM GV$SESSION s LEFT JOIN GV$SQL q ON q.SQL_ID=s.SQL_ID AND q.INST_ID=s.INST_ID " +
                "WHERE s.TYPE<>'BACKGROUND' AND s.STATUS='ACTIVE' AND s.LAST_CALL_ET>60 " +
                "ORDER BY s.LAST_CALL_ET DESC) WHERE ROWNUM <= 50",
                note: "Sorgu metni için GRANT SELECT ON gv_$sql gerekir."),

            ServiceType.OracleTablespaceStatus => await OracleAsync(svc, cred, ct, "Offline Tablespace'ler",
                new[] { "Tablespace", "Durum", "İçerik" },
                "SELECT TABLESPACE_NAME, STATUS, CONTENTS FROM DBA_TABLESPACES WHERE STATUS='OFFLINE' ORDER BY TABLESPACE_NAME"),

            // ---- MSSQL ----
            ServiceType.MsSqlActiveSessions => await MsSqlAsync(svc, cred, ct, "Aktif Oturumlar",
                new[] { "Oturum", "Kullanıcı", "Makine", "Program", "Süre (sn)" },
                "SELECT TOP 50 s.session_id, s.login_name, s.host_name, s.program_name, " +
                "DATEDIFF(SECOND, ISNULL(r.start_time, s.last_request_start_time), GETDATE()) " +
                "FROM sys.dm_exec_sessions s LEFT JOIN sys.dm_exec_requests r ON r.session_id=s.session_id " +
                "WHERE s.is_user_process=1 AND s.status='running' ORDER BY 5 DESC"),

            ServiceType.MsSqlBlockedSessions => await MsSqlAsync(svc, cred, ct, "Bloklu Oturumlar",
                new[] { "Oturum", "Kullanıcı", "Bloklayan", "Bekleme (sn)", "Bekleme Türü" },
                "SELECT TOP 50 r.session_id, s.login_name, r.blocking_session_id, " +
                "DATEDIFF(SECOND, r.start_time, GETDATE()), r.wait_type " +
                "FROM sys.dm_exec_requests r JOIN sys.dm_exec_sessions s ON s.session_id=r.session_id " +
                "WHERE r.blocking_session_id<>0 ORDER BY 4 DESC"),

            ServiceType.MsSqlLongQueries => await MsSqlAsync(svc, cred, ct, "Uzun Süren Sorgular",
                new[] { "Oturum", "Kullanıcı", "Süre (sn)", "Sorgu" },
                "SELECT TOP 50 r.session_id, s.login_name, r.total_elapsed_time/1000, SUBSTRING(t.text,1,200) " +
                "FROM sys.dm_exec_requests r JOIN sys.dm_exec_sessions s ON s.session_id=r.session_id " +
                "CROSS APPLY sys.dm_exec_sql_text(r.sql_handle) t " +
                "WHERE s.is_user_process=1 AND r.total_elapsed_time>60000 ORDER BY r.total_elapsed_time DESC"),

            ServiceType.MsSqlDbStatus => await MsSqlAsync(svc, cred, ct, "ONLINE Olmayan Veritabanları",
                new[] { "Veritabanı", "Durum", "Kurtarma Modeli" },
                "SELECT name, state_desc, recovery_model_desc FROM sys.databases WHERE state_desc<>'ONLINE' ORDER BY name"),

            // ---- MySQL ----
            ServiceType.MySqlActiveSessions => await MySqlAsync(svc, cred, ct, "Aktif Oturumlar",
                new[] { "ID", "Kullanıcı", "Host", "DB", "Komut", "Süre (sn)", "Sorgu" },
                "SELECT ID, USER, HOST, DB, COMMAND, TIME, LEFT(INFO,200) " +
                "FROM information_schema.PROCESSLIST WHERE COMMAND<>'Sleep' ORDER BY TIME DESC LIMIT 50"),

            ServiceType.MySqlBlockedSessions => await MySqlAsync(svc, cred, ct, "Kilit Bekleyen İşlemler",
                new[] { "Thread", "Başlangıç", "Bekleme (sn)", "Sorgu" },
                "SELECT trx_mysql_thread_id, trx_started, TIMESTAMPDIFF(SECOND, trx_wait_started, NOW()), LEFT(trx_query,200) " +
                "FROM information_schema.INNODB_TRX WHERE trx_state='LOCK WAIT' ORDER BY 3 DESC LIMIT 50"),

            ServiceType.MySqlLongQueries => await MySqlAsync(svc, cred, ct, "Uzun Süren Sorgular",
                new[] { "ID", "Kullanıcı", "Host", "Süre (sn)", "Sorgu" },
                "SELECT ID, USER, HOST, TIME, LEFT(INFO,200) " +
                "FROM information_schema.PROCESSLIST WHERE COMMAND<>'Sleep' AND TIME>60 ORDER BY TIME DESC LIMIT 50"),

            _ => null
        };
    }

    // ---- Sağlayıcıya özel bağlantı + ortak okuma ----

    private async Task<Result> OracleAsync(MonitoredService svc, Credential? cred, CancellationToken ct, string title, string[] cols, string sql, string? note = null)
    {
        if (cred == null) throw new InvalidOperationException("Kimlik bilgisi tanımlı değil");
        if (string.IsNullOrWhiteSpace(svc.Extra)) throw new InvalidOperationException("Service name (Ekstra) tanımlı değil");
        var port = svc.Port ?? 1521;
        var connect = svc.Extra.StartsWith("SID=", StringComparison.OrdinalIgnoreCase)
            ? $"(DESCRIPTION=(ADDRESS=(PROTOCOL=TCP)(HOST={svc.Target})(PORT={port}))(CONNECT_DATA=(SID={svc.Extra[4..]})))"
            : $"(DESCRIPTION=(ADDRESS=(PROTOCOL=TCP)(HOST={svc.Target})(PORT={port}))(CONNECT_DATA=(SERVICE_NAME={svc.Extra})))";
        var csb = new OracleConnectionStringBuilder
        {
            DataSource = connect,
            UserID = VaultClient.GetUsername(cred),
            Password = VaultClient.GetPassword(cred),
            ConnectionTimeout = svc.TimeoutSeconds
        };
        await using var conn = new OracleConnection(csb.ConnectionString);
        await conn.OpenAsync(ct);
        return await ReadAsync(conn, title, cols, sql, ct, note);
    }

    private async Task<Result> MsSqlAsync(MonitoredService svc, Credential? cred, CancellationToken ct, string title, string[] cols, string sql, string? note = null)
    {
        var csb = new SqlConnectionStringBuilder
        {
            DataSource = svc.Port.HasValue ? $"{svc.Target},{svc.Port}" : svc.Target,
            ConnectTimeout = svc.TimeoutSeconds,
            Encrypt = svc.UseSsl,
            TrustServerCertificate = svc.IgnoreCertErrors
        };
        if (!string.IsNullOrWhiteSpace(svc.Extra)) csb.InitialCatalog = svc.Extra;
        if (cred != null) { csb.UserID = VaultClient.GetUsername(cred); csb.Password = VaultClient.GetPassword(cred); }
        else csb.IntegratedSecurity = true;
        await using var conn = new SqlConnection(csb.ConnectionString);
        await conn.OpenAsync(ct);
        return await ReadAsync(conn, title, cols, sql, ct, note);
    }

    private async Task<Result> MySqlAsync(MonitoredService svc, Credential? cred, CancellationToken ct, string title, string[] cols, string sql, string? note = null)
    {
        if (cred == null) throw new InvalidOperationException("Kimlik bilgisi tanımlı değil");
        var csb = new MySqlConnectionStringBuilder
        {
            Server = svc.Target,
            Port = (uint)(svc.Port ?? 3306),
            UserID = VaultClient.GetUsername(cred),
            Password = VaultClient.GetPassword(cred),
            ConnectionTimeout = (uint)svc.TimeoutSeconds,
            SslMode = svc.UseSsl ? MySqlSslMode.Required : MySqlSslMode.Preferred
        };
        if (!string.IsNullOrWhiteSpace(svc.Extra)) csb.Database = svc.Extra;
        await using var conn = new MySqlConnection(csb.ConnectionString);
        await conn.OpenAsync(ct);
        return await ReadAsync(conn, title, cols, sql, ct, note);
    }

    /// <summary>SQL'i çalıştırıp satırları string tablosu olarak döner (en fazla 50 satır). Kolon başlıkları
    /// açıkça verilir (SELECT sırasıyla eşleşir) — böylece Türkçe başlık + i18n çevirisi kontrol edilebilir.</summary>
    private static async Task<Result> ReadAsync(DbConnection conn, string title, string[] cols, string sql, CancellationToken ct, string? note)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.CommandTimeout = CommandTimeoutSec;
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        var rows = new List<IReadOnlyList<string>>();
        int fields = Math.Min(rd.FieldCount, cols.Length);
        while (rows.Count < MaxRows && await rd.ReadAsync(ct))
        {
            var r = new string[cols.Length];
            for (int i = 0; i < cols.Length; i++)
                r[i] = i < fields && !await rd.IsDBNullAsync(i, ct) ? (Convert.ToString(rd.GetValue(i)) ?? "").Trim() : "";
            rows.Add(r);
        }
        return new Result(title, cols, rows, note);
    }
}
