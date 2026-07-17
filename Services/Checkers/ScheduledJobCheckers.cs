using Microsoft.Data.SqlClient;
using MySqlConnector;
using Oracle.ManagedDataAccess.Client;
using Renci.SshNet;
using vMonitor.Models;

namespace vMonitor.Services.Checkers;

/// <summary>Zamanlanmış Görevler Fazı 1 (PULL): OS/DB katmanındaki zamanlanmış görevlerin
/// son koşusu ne zaman başladı, ne kadar sürdü, sonucu ne — izleme olarak.
/// Ortak kurallar:
///  - Son koşu BAŞARISIZ → DOWN (normal alarm akışı)
///  - Görev devre dışı veya SESSİZLİK eşiği (MaxSilenceHours) aşıldı → ERROR (uyarı)
///  - Grafiğe yazılan değer: süre bilinen tiplerde son koşu SÜRESİ (sn);
///    süre bilinmeyen tiplerde (Windows Task, MySQL Event) son koşudan bu yana geçen DAKİKA.
///  - Süre eşiği: mevcut ResponseTimeThresholdMs alanı (sn olarak yorumlanır) → aşım YAVAŞ işareti.</summary>
public static class JobCommon
{
    /// <summary>Sessizlik değerlendirmesi: dakika cinsinden yaş verilir; eşik aşıldıysa hata metni döner.</summary>
    public static string? SilenceError(MonitoredService svc, double? ageMinutes)
    {
        if (svc.MaxSilenceHours is not > 0) return null;
        if (ageMinutes == null || ageMinutes < 0)
            return $"Görev hiç koşmamış görünüyor (sessizlik eşiği: {svc.MaxSilenceHours} sa).";
        var ageH = ageMinutes.Value / 60.0;
        if (ageH > svc.MaxSilenceHours.Value)
            return $"Son koşu {Math.Round(ageH, 1)} saat önce — sessizlik eşiği ({svc.MaxSilenceHours} sa) aşıldı.";
        return null;
    }

    /// <summary>"OWNER.AD" biçimini böler; nokta yoksa owner null döner.</summary>
    public static (string? Owner, string Name) SplitOwned(string jobName)
    {
        var i = jobName.IndexOf('.');
        return i > 0 ? (jobName[..i].Trim(), jobName[(i + 1)..].Trim()) : (null, jobName.Trim());
    }
}

/// <summary>Oracle Scheduler job: DBA_SCHEDULER_JOBS + son koşu DBA_SCHEDULER_JOB_RUN_DETAILS.
/// Extra = service name (SID= destekli), JobName = "OWNER.JOB" (veya yalnız job adı).</summary>
public class OracleSchedulerJobChecker : OracleDbCheckerBase
{
    public override ServiceType Type => ServiceType.OracleSchedulerJob;

    protected override async Task<string?> ExecuteAsync(MonitoredService service, Credential? credential, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(service.JobName)) return "Görev adı tanımlı değil";
        var (owner, name) = JobCommon.SplitOwned(service.JobName!);
        await using var conn = await OpenAsync(service, credential, ct);

        // DBA_ görünümleri yoksa (yetki) ALL_ ile yeniden dene
        foreach (var prefix in new[] { "dba", "all" })
        {
            try { return await QueryAsync(conn, prefix, owner, name, service, ct); }
            catch (OracleException ex) when (ex.Number == 942 && prefix == "dba") { /* dba görünümü yok → all_ dene */ }
        }
        return "Scheduler görünümlerine erişilemedi";
    }

    private async Task<string?> QueryAsync(OracleConnection conn, string prefix, string? owner, string name,
        MonitoredService svc, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.BindByName = true;
        cmd.CommandText =
            $"SELECT j.enabled, j.state, (SYSDATE - CAST(j.last_start_date AS DATE)) * 1440 AS age_min, " +
            "d.status, d.dur_sec, d.additional_info " +
            $"FROM {prefix}_scheduler_jobs j LEFT JOIN (" +
            "  SELECT owner, job_name, status, additional_info, " +
            "         EXTRACT(DAY FROM run_duration)*86400 + EXTRACT(HOUR FROM run_duration)*3600 + " +
            "         EXTRACT(MINUTE FROM run_duration)*60 + ROUND(EXTRACT(SECOND FROM run_duration)) AS dur_sec, " +
            "         ROW_NUMBER() OVER (PARTITION BY owner, job_name ORDER BY actual_start_date DESC) rn " +
            $"  FROM {prefix}_scheduler_job_run_details) d " +
            "ON d.owner = j.owner AND d.job_name = j.job_name AND d.rn = 1 " +
            "WHERE UPPER(j.job_name) = UPPER(:jn) AND (:ow IS NULL OR UPPER(j.owner) = UPPER(:ow))";
        cmd.Parameters.Add("jn", name);
        cmd.Parameters.Add("ow", (object?)owner ?? DBNull.Value);

        await using var rd = await cmd.ExecuteReaderAsync(ct);
        if (!await rd.ReadAsync(ct)) return $"Görev bulunamadı: {svc.JobName}";

        var enabled = rd.IsDBNull(0) ? "TRUE" : rd.GetString(0);
        double? ageMin = rd.IsDBNull(2) ? null : Convert.ToDouble(rd.GetValue(2));
        var lastStatus = rd.IsDBNull(3) ? null : rd.GetString(3);
        long? durSec = rd.IsDBNull(4) ? null : Convert.ToInt64(rd.GetValue(4));
        var info = rd.IsDBNull(5) ? null : rd.GetString(5);

        OverrideResponseValue = durSec ?? 0;

        if (!string.Equals(enabled, "TRUE", StringComparison.OrdinalIgnoreCase))
        { IsThresholdError = true; return "Görev devre dışı (DISABLED)"; }

        if (lastStatus != null && !string.Equals(lastStatus, "SUCCEEDED", StringComparison.OrdinalIgnoreCase))
            return $"Son koşu {lastStatus}{(string.IsNullOrWhiteSpace(info) ? "" : " — " + info[..Math.Min(200, info.Length)])}";

        var silence = JobCommon.SilenceError(svc, ageMin);
        if (silence != null) { IsThresholdError = true; return silence; }
        return null;
    }
}

/// <summary>MSSQL Agent job: msdb sysjobs + sysjobhistory (step 0 = job sonucu).
/// Extra = veritabanı (opsiyonel), JobName = Agent job adı.</summary>
public class MsSqlAgentJobChecker : MsSqlDbCheckerBase
{
    public override ServiceType Type => ServiceType.MsSqlAgentJob;

    protected override async Task<string?> ExecuteAsync(MonitoredService service, Credential? credential, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(service.JobName)) return "Görev adı tanımlı değil";
        await using var conn = await OpenAsync(service, credential, ct);
        await using var cmd = new SqlCommand(
            "SELECT TOP 1 j.enabled, h.run_status, " +
            "DATEDIFF(MINUTE, msdb.dbo.agent_datetime(h.run_date, h.run_time), GETDATE()) AS age_min, " +
            "(h.run_duration/10000)*3600 + ((h.run_duration/100)%100)*60 + (h.run_duration%100) AS dur_sec, " +
            "h.message " +
            "FROM msdb.dbo.sysjobs j " +
            "LEFT JOIN msdb.dbo.sysjobhistory h ON h.job_id = j.job_id AND h.step_id = 0 " +
            "WHERE j.name = @n ORDER BY h.instance_id DESC", conn);
        cmd.Parameters.AddWithValue("@n", service.JobName!.Trim());

        await using var rd = await cmd.ExecuteReaderAsync(ct);
        if (!await rd.ReadAsync(ct)) return $"Agent job bulunamadı: {service.JobName}";

        var enabled = !rd.IsDBNull(0) && Convert.ToInt32(rd.GetValue(0)) == 1;
        int? runStatus = rd.IsDBNull(1) ? null : Convert.ToInt32(rd.GetValue(1));
        double? ageMin = rd.IsDBNull(2) ? null : Convert.ToDouble(rd.GetValue(2));
        long? durSec = rd.IsDBNull(3) ? null : Convert.ToInt64(rd.GetValue(3));
        var msg = rd.IsDBNull(4) ? null : rd.GetString(4);

        OverrideResponseValue = durSec ?? 0;

        if (!enabled) { IsThresholdError = true; return "Agent job devre dışı"; }

        // run_status: 0=Failed 1=Succeeded 2=Retry 3=Canceled 4=InProgress
        if (runStatus is 0 or 3)
            return $"Son koşu {(runStatus == 0 ? "BAŞARISIZ" : "İPTAL")}{(string.IsNullOrWhiteSpace(msg) ? "" : " — " + msg[..Math.Min(200, msg.Length)])}";

        var silence = JobCommon.SilenceError(service, ageMin);
        if (silence != null) { IsThresholdError = true; return silence; }
        return null;
    }
}

/// <summary>MySQL Event: information_schema.EVENTS — MySQL sonuç/süre TUTMAZ; yalnız son çalıştırma
/// zamanı ve etkinlik durumu izlenir. Grafik değeri: son koşudan bu yana geçen DAKİKA.
/// Extra = veritabanı (opsiyonel), JobName = "schema.event" (veya yalnız ad).</summary>
public class MySqlEventJobChecker : MySqlDbCheckerBase
{
    public override ServiceType Type => ServiceType.MySqlEventJob;

    protected override async Task<string?> ExecuteAsync(MonitoredService service, Credential? credential, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(service.JobName)) return "Event adı tanımlı değil";
        var (schema, name) = JobCommon.SplitOwned(service.JobName!);
        await using var conn = await OpenAsync(service, credential, ct);

        await using var cmd = new MySqlCommand(
            "SELECT STATUS, TIMESTAMPDIFF(MINUTE, LAST_EXECUTED, NOW()) " +
            "FROM information_schema.EVENTS WHERE EVENT_NAME = @n AND (@s IS NULL OR EVENT_SCHEMA = @s) LIMIT 1", conn);
        cmd.Parameters.AddWithValue("@n", name);
        cmd.Parameters.AddWithValue("@s", (object?)schema ?? DBNull.Value);

        await using var rd = await cmd.ExecuteReaderAsync(ct);
        if (!await rd.ReadAsync(ct)) return $"Event bulunamadı: {service.JobName}";

        var status = rd.IsDBNull(0) ? "" : rd.GetString(0);
        double? ageMin = rd.IsDBNull(1) ? null : Convert.ToDouble(rd.GetValue(1));

        OverrideResponseValue = ageMin.HasValue ? (long)Math.Max(0, Math.Round(ageMin.Value)) : 0;

        if (!status.StartsWith("ENABLED", StringComparison.OrdinalIgnoreCase))
        { IsThresholdError = true; return $"Event etkin değil ({status})"; }

        var silence = JobCommon.SilenceError(service, ageMin);
        if (silence != null) { IsThresholdError = true; return silence; }
        return null;
    }
}

/// <summary>Windows Görev Zamanlayıcı: Schedule.Service COM API'siyle uzak sunucuya bağlanır
/// (WMI ile aynı RPC erişimi + kimlik). JobName = tam görev yolu ("\Klasör\Ad" veya kökte "Ad").
/// Görev geçmişi (süre) event log'a bağlı olduğundan v1'de süre yoktur; grafik: son koşudan bu yana DAKİKA.</summary>
public class WindowsTaskJobChecker : CheckerBase
{
    public override ServiceType Type => ServiceType.WindowsTaskJob;

    protected override Task<string?> ExecuteAsync(MonitoredService service, Credential? credential, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(service.JobName)) return Task.FromResult<string?>("Görev yolu tanımlı değil");

        return Task.Run<string?>(() =>
        {
            dynamic? ts = null; dynamic? task = null;
            try
            {
                ts = WindowsTaskTools.Connect(service.Target, credential, service.TimeoutSeconds);
                var (folder, name) = WindowsTaskTools.SplitPath(service.JobName!);
                task = ts!.GetFolder(folder).GetTask(name);

                bool enabled = task.Enabled;
                int state = task.State;                     // 1=Disabled 2=Queued 3=Ready 4=Running
                DateTime lastRun = task.LastRunTime;
                int lastResult = task.LastTaskResult;

                bool neverRan = lastRun.Year < 2000;        // 1899-12-30 = hiç koşmadı
                double? ageMin = neverRan ? null : (DateTime.Now - lastRun).TotalMinutes;
                OverrideResponseValue = ageMin.HasValue ? (long)Math.Max(0, Math.Round(ageMin.Value)) : 0;

                if (!enabled || state == 1) { IsThresholdError = true; return "Görev devre dışı"; }

                // 0 = başarı; 0x41301 = şu an çalışıyor; 0x41303 = henüz hiç koşmadı
                if (state != 4 && !neverRan && lastResult != 0 && lastResult != 0x41301 && lastResult != 0x41303)
                    return $"Son koşu başarısız — sonuç kodu 0x{lastResult:X} ({lastResult})";

                var silence = JobCommon.SilenceError(service, ageMin);
                if (silence != null) { IsThresholdError = true; return silence; }
                return null;
            }
            catch (System.IO.FileNotFoundException) { return $"Görev bulunamadı: {service.JobName}"; }
            catch (System.Runtime.InteropServices.COMException ex) when ((uint)ex.HResult == 0x80070002)
            { return $"Görev bulunamadı: {service.JobName}"; }
            finally
            {
                if (task != null) System.Runtime.InteropServices.Marshal.ReleaseComObject(task);
                if (ts != null) System.Runtime.InteropServices.Marshal.ReleaseComObject(ts);
            }
        }, ct);
    }
}

/// <summary>systemd timer (Linux, SSH): timer'ın son tetiklenmesi + tetiklediği servisin
/// sonucu/süresi. Yaş/süre hesapları SUNUCU tarafında yapılır (saat dilimi sapması olmaz).
/// JobName = timer birimi (örn. "backup.timer"; .timer eklenmemişse otomatik eklenir).</summary>
public class SystemdTimerJobChecker : CheckerBase
{
    public override ServiceType Type => ServiceType.SystemdTimerJob;

    protected override Task<string?> ExecuteAsync(MonitoredService service, Credential? credential, CancellationToken ct)
    {
        if (credential == null) return Task.FromResult<string?>("Kimlik bilgisi tanımlı değil");
        var unit = SystemdTools.NormalizeTimer(service.JobName);
        if (unit == null) return Task.FromResult<string?>("Geçersiz timer adı (yalnız harf/rakam/@ . _ - kullanın)");

        return Task.Run<string?>(() =>
        {
            using var ssh = SystemdTools.Connect(service, credential);
            // Tek komutta: etkin mi, son tetikleme yaşı (sn), servis sonucu, servis süresi (sn)
            var script =
                $"t='{unit}'; u=$(systemctl show -p Unit --value \"$t\" 2>/dev/null); " +
                "en=$(systemctl is-enabled \"$t\" 2>/dev/null || echo unknown); " +
                "act=$(systemctl is-active \"$t\" 2>/dev/null || echo unknown); " +
                "now=$(date +%s); " +
                "lts=$(date -d \"$(systemctl show -p LastTriggerUSec --value \"$t\" 2>/dev/null)\" +%s 2>/dev/null || echo 0); " +
                "res=$(systemctl show -p Result --value \"$u\" 2>/dev/null); " +
                "s1=$(date -d \"$(systemctl show -p ExecMainStartTimestamp --value \"$u\" 2>/dev/null)\" +%s 2>/dev/null || echo 0); " +
                "s2=$(date -d \"$(systemctl show -p ExecMainExitTimestamp --value \"$u\" 2>/dev/null)\" +%s 2>/dev/null || echo 0); " +
                "if [ \"$lts\" -gt 0 ]; then age=$((now-lts)); else age=-1; fi; " +
                "if [ \"$s1\" -gt 0 ] && [ \"$s2\" -ge \"$s1\" ]; then dur=$((s2-s1)); else dur=-1; fi; " +
                "echo \"EN=$en|ACT=$act|RES=$res|AGE=$age|DUR=$dur\"";
            using var cmd = ssh.CreateCommand(script);
            var outp = (cmd.Execute() ?? "").Trim();
            var map = outp.Split('|').Select(p => p.Split('=', 2))
                .Where(a => a.Length == 2).ToDictionary(a => a[0], a => a[1]);

            if (map.Count == 0) return "systemctl çıktısı okunamadı";
            var act = map.GetValueOrDefault("ACT", "unknown");
            var res = map.GetValueOrDefault("RES", "");
            long ageSec = long.TryParse(map.GetValueOrDefault("AGE"), out var a) ? a : -1;
            long durSec = long.TryParse(map.GetValueOrDefault("DUR"), out var d) ? d : -1;

            OverrideResponseValue = durSec >= 0 ? durSec : 0;

            if (act != "active" && act != "activating")
            { IsThresholdError = true; return $"Timer aktif değil ({act}) — birim bulunamamış olabilir"; }

            if (!string.IsNullOrEmpty(res) && res != "success")
                return $"Son koşu başarısız — Result: {res}";

            double? ageMin = ageSec >= 0 ? ageSec / 60.0 : null;
            var silence = JobCommon.SilenceError(service, ageMin);
            if (silence != null) { IsThresholdError = true; return silence; }
            return null;
        }, ct);
    }
}

/// <summary>Keşif: formdaki "Görevleri Listele" düğmesi — sunucudaki görev adlarını canlı çeker.</summary>
public static class JobDiscovery
{
    public static async Task<List<string>> OracleAsync(MonitoredService s, Credential cred, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(s.Extra)) throw new InvalidOperationException("Service name (Ekstra) tanımlı değil");
        var port = s.Port ?? 1521;
        var connect = s.Extra!.StartsWith("SID=", StringComparison.OrdinalIgnoreCase)
            ? $"(DESCRIPTION=(ADDRESS=(PROTOCOL=TCP)(HOST={s.Target})(PORT={port}))(CONNECT_DATA=(SID={s.Extra[4..]})))"
            : $"(DESCRIPTION=(ADDRESS=(PROTOCOL=TCP)(HOST={s.Target})(PORT={port}))(CONNECT_DATA=(SERVICE_NAME={s.Extra})))";
        var csb = new OracleConnectionStringBuilder
        { DataSource = connect, UserID = VaultClient.GetUsername(cred), Password = VaultClient.GetPassword(cred), ConnectionTimeout = s.TimeoutSeconds };
        await using var conn = new OracleConnection(csb.ConnectionString);
        await conn.OpenAsync(ct);
        foreach (var prefix in new[] { "dba", "all" })
        {
            try { return await ListAsync(conn, $"SELECT owner || '.' || job_name FROM {prefix}_scheduler_jobs ORDER BY owner, job_name", ct); }
            catch (OracleException ex) when (ex.Number == 942 && prefix == "dba") { /* all_ ile dene */ }
        }
        return new List<string>();
    }

    public static async Task<List<string>> MsSqlAsync(MonitoredService s, Credential? cred, CancellationToken ct)
    {
        var csb = new SqlConnectionStringBuilder
        {
            DataSource = s.Port.HasValue ? $"{s.Target},{s.Port}" : s.Target,
            ConnectTimeout = s.TimeoutSeconds, Encrypt = s.UseSsl, TrustServerCertificate = s.IgnoreCertErrors
        };
        if (cred != null) { csb.UserID = VaultClient.GetUsername(cred); csb.Password = VaultClient.GetPassword(cred); }
        else csb.IntegratedSecurity = true;
        await using var conn = new SqlConnection(csb.ConnectionString);
        await conn.OpenAsync(ct);
        return await ListAsync(conn, "SELECT name FROM msdb.dbo.sysjobs ORDER BY name", ct);
    }

    public static async Task<List<string>> MySqlAsync(MonitoredService s, Credential cred, CancellationToken ct)
    {
        var csb = new MySqlConnectionStringBuilder
        {
            Server = s.Target, Port = (uint)(s.Port ?? 3306),
            UserID = VaultClient.GetUsername(cred), Password = VaultClient.GetPassword(cred),
            ConnectionTimeout = (uint)s.TimeoutSeconds,
            SslMode = s.UseSsl ? MySqlSslMode.Required : MySqlSslMode.Preferred
        };
        await using var conn = new MySqlConnection(csb.ConnectionString);
        await conn.OpenAsync(ct);
        return await ListAsync(conn, "SELECT CONCAT(EVENT_SCHEMA, '.', EVENT_NAME) FROM information_schema.EVENTS ORDER BY 1", ct);
    }

    private static async Task<List<string>> ListAsync(System.Data.Common.DbConnection conn, string sql, CancellationToken ct)
    {
        var list = new List<string>();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.CommandTimeout = 10;
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        while (await rd.ReadAsync(ct))
            if (!rd.IsDBNull(0)) list.Add(rd.GetString(0));
        return list;
    }
}

/// <summary>Windows Görev Zamanlayıcı COM yardımcıları (checker + keşif + detay ortak kullanır).</summary>
public static class WindowsTaskTools
{
    public static dynamic Connect(string host, Credential? cred, int timeoutSec)
    {
        var t = System.Type.GetTypeFromProgID("Schedule.Service")
            ?? throw new InvalidOperationException("Schedule.Service COM bileşeni yok");
        dynamic svc = Activator.CreateInstance(t)!;
        if (cred != null)
        {
            var user = VaultClient.GetUsername(cred);
            svc.Connect(host, user, string.IsNullOrWhiteSpace(cred.Domain) ? null : cred.Domain, VaultClient.GetPassword(cred));
        }
        else
            svc.Connect(host);
        return svc;
    }

    public static (string Folder, string Name) SplitPath(string jobPath)
    {
        var p = jobPath.Replace('/', '\\').Trim();
        if (!p.StartsWith('\\')) p = "\\" + p;
        var i = p.LastIndexOf('\\');
        var folder = i <= 0 ? "\\" : p[..i];
        return (folder, p[(i + 1)..]);
    }

    /// <summary>Klasörleri özyineli gezerek görev yollarını listeler ("\Microsoft" ağacı atlanır — yüzlerce yerleşik görev).</summary>
    public static List<string> ListTasks(dynamic ts, bool includeMicrosoft = false)
    {
        var result = new List<string>();
        void Walk(dynamic folder)
        {
            string path = folder.Path;
            if (!includeMicrosoft && path.StartsWith("\\Microsoft", StringComparison.OrdinalIgnoreCase)) return;
            dynamic tasks = folder.GetTasks(1);   // 1 = TASK_ENUM_HIDDEN dahil
            foreach (dynamic task in tasks) { result.Add((string)task.Path); System.Runtime.InteropServices.Marshal.ReleaseComObject(task); }
            dynamic subs = folder.GetFolders(0);
            foreach (dynamic sub in subs) { Walk(sub); System.Runtime.InteropServices.Marshal.ReleaseComObject(sub); }
        }
        dynamic root = ts.GetFolder("\\");
        Walk(root);
        result.Sort(StringComparer.OrdinalIgnoreCase);
        return result;
    }
}

/// <summary>systemd SSH yardımcıları (checker + keşif ortak kullanır).</summary>
public static class SystemdTools
{
    /// <summary>Timer adı doğrulama + ".timer" tamamlama (shell enjeksiyonuna kapalı karakter kümesi).</summary>
    public static string? NormalizeTimer(string? name)
    {
        var v = (name ?? "").Trim();
        if (v.Length == 0 || !System.Text.RegularExpressions.Regex.IsMatch(v, @"^[A-Za-z0-9@._\-]+$")) return null;
        return v.EndsWith(".timer", StringComparison.OrdinalIgnoreCase) ? v : v + ".timer";
    }

    public static SshClient Connect(MonitoredService s, Credential cred)
    {
        var user = VaultClient.GetUsername(cred);
        var info = new Renci.SshNet.ConnectionInfo(s.Target, s.Port ?? 22, user,
            new PasswordAuthenticationMethod(user, VaultClient.GetPassword(cred)))
        { Timeout = TimeSpan.FromSeconds(Math.Max(5, s.TimeoutSeconds)) };
        var ssh = new SshClient(info);
        ssh.Connect();
        return ssh;
    }

    public static List<string> ListTimers(MonitoredService s, Credential cred)
    {
        using var ssh = Connect(s, cred);
        using var cmd = ssh.CreateCommand(
            "systemctl list-timers --all --no-legend --no-pager 2>/dev/null | awk '{for(i=1;i<=NF;i++) if($i ~ /\\.timer$/) print $i}' | sort -u");
        var outp = cmd.Execute() ?? "";
        return outp.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
    }
}
