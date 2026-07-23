using Microsoft.Data.SqlClient;
using MySqlConnector;
using Oracle.ManagedDataAccess.Client;
using Renci.SshNet;
using vMonitor.Models;

namespace vMonitor.Services.Checkers;

/// <summary>Zamanlanmış Görevler Fazı 1 (PULL): OS/DB katmanındaki zamanlanmış görevlerin
/// son koşusu ne zaman başladı, ne kadar sürdü, sonucu ne — izleme olarak.
/// TEK İZLEME BİRDEN ÇOK GÖREVİ izleyebilir (JobName ';' ayraçlı liste — örn. bir ortamın job seti).
/// Ortak kurallar:
///  - Herhangi bir görevin son koşusu BAŞARISIZ veya görev bulunamadı → DOWN (hangileri mesajda)
///  - Görev devre dışı veya SESSİZLİK eşiği (MaxSilenceHours) aşıldı → ERROR (uyarı)
///  - Grafik değeri: süre bilinen tiplerde EN UZUN son koşu süresi (sn);
///    süre bilinmeyen tiplerde (Windows Task, MySQL Event) EN ESKİ son koşu yaşı (dk).
///  - Süre eşiği: mevcut ResponseTimeThresholdMs alanı (sn) → aşım YAVAŞ işareti.</summary>
public static class JobCommon
{
    /// <summary>';' ayraçlı görev listesini böler (tekrarsız, boşluklar kırpılmış).</summary>
    public static List<string> SplitList(string? jobName) =>
        (jobName ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>"OWNER.AD" biçimini böler; nokta yoksa owner null döner.</summary>
    public static (string? Owner, string Name) SplitOwned(string jobName)
    {
        var i = jobName.IndexOf('.');
        return i > 0 ? (jobName[..i].Trim(), jobName[(i + 1)..].Trim()) : (null, jobName.Trim());
    }

    /// <summary>Tek görevin son durumu (çoklu izleme değerlendirmesine girer).
    /// AgeSec = son koşunun üzerinden geçen SANİYE (sunucu tarafında hesaplanır — TZ sapması olmaz).</summary>
    public sealed record JobState(string Name, bool Found, bool Enabled, string? FailText, long? AgeSec, long? DurSec);

    /// <summary>Görev setini tek izleme sonucuna indirger: (grafik değeri, eşik-hatası mı, mesaj).
    /// Ayrıca görev-başına durumları LastJobStates biçiminde serileştirir (dashboard mini kutuları).</summary>
    public static (long Value, bool IsThresholdError, string? Error, string States) Evaluate(MonitoredService svc, List<JobState> jobs, bool durationBased)
    {
        long value = 0;
        foreach (var j in jobs)
            value = Math.Max(value, durationBased ? (j.DurSec ?? 0) : Math.Max(0, (j.AgeSec ?? 0) / 60));

        // Görev-başına durum kodu: nf (bulunamadı) / fail / dis (devre dışı) / sil (sessizlik aşımı) / ok
        string StOf(JobState j)
        {
            if (!j.Found) return "nf";
            if (j.FailText != null) return "fail";
            if (!j.Enabled) return "dis";
            if (svc.MaxSilenceHours is > 0 && (j.AgeSec == null || j.AgeSec < 0 || j.AgeSec.Value / 3600.0 > svc.MaxSilenceHours.Value)) return "sil";
            return "ok";
        }
        // Biçim: ad|durum|süre_sn|son_koşu_epoch (UTC sn; -1 = bilinmiyor) — mini kutular
        // her tipte AYNI bilgiyi gösterir: tarih-saat + süre (süre bilinmeyen tiplerde yalnız tarih).
        var nowEpoch = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var states = string.Join(";", jobs.Select(j =>
        {
            var last = j.AgeSec is >= 0 ? nowEpoch - j.AgeSec.Value : -1;
            var safeName = j.Name.Replace(";", "_").Replace("|", "_");
            return $"{safeName}|{StOf(j)}|{j.DurSec ?? -1}|{last}";
        }));

        // Koşu GEÇMİŞİ için anlık görüntü: CheckRunner bunlardan JobRunHistory üretir
        // (kaynak geçmiş tutmasa bile geçmiş vMon DB'sinde birikir).
        svc.PendingJobRuns = jobs
            .Where(j => j.Found && j.AgeSec is >= 0)
            .Select(j => new JobRunSnapshot(
                j.Name.Replace(";", "_").Replace("|", "_"),
                DateTimeOffset.FromUnixTimeSeconds(nowEpoch - j.AgeSec!.Value).UtcDateTime,
                j.DurSec is >= 0 ? (int?)Math.Min(j.DurSec.Value, int.MaxValue) : null,
                j.FailText != null,
                j.FailText))
            .ToList();

        static string Names(IEnumerable<string> l)
        {
            var a = l.ToList();
            return string.Join(", ", a.Take(3)) + (a.Count > 3 ? $" (+{a.Count - 3})" : "");
        }

        var notFound = jobs.Where(j => !j.Found).Select(j => j.Name).ToList();
        if (notFound.Count > 0) return (value, false, $"Görev bulunamadı: {Names(notFound)}", states);

        var failed = jobs.Where(j => j.Found && j.FailText != null).ToList();
        if (failed.Count > 0)
            return (value, false, $"{failed.Count}/{jobs.Count} görev başarısız — " +
                string.Join(" | ", failed.Take(2).Select(f => $"{f.Name}: {f.FailText}")) + (failed.Count > 2 ? " | …" : ""), states);

        var disabled = jobs.Where(j => j.Found && !j.Enabled).Select(j => j.Name).ToList();
        if (disabled.Count > 0) return (value, true, $"{disabled.Count} görev devre dışı: {Names(disabled)}", states);

        if (svc.MaxSilenceHours is > 0)
        {
            var silent = jobs.Where(j => j.Found && j.Enabled && j.FailText == null &&
                (j.AgeSec == null || j.AgeSec < 0 || j.AgeSec.Value / 3600.0 > svc.MaxSilenceHours!.Value))
                .Select(j => j.Name).ToList();
            if (silent.Count > 0)
                return (value, true, $"Sessizlik eşiği ({svc.MaxSilenceHours} sa) aşıldı: {Names(silent)}", states);
        }
        return (value, false, null, states);
    }
}

/// <summary>Oracle Scheduler job seti: DBA_/ALL_SCHEDULER_JOBS + her görevin SON koşusu.
/// Extra = service name (SID= destekli), JobName = ';' ayraçlı "OWNER.JOB" listesi.</summary>
public class OracleSchedulerJobChecker : OracleDbCheckerBase
{
    public override ServiceType Type => ServiceType.OracleSchedulerJob;

    protected override async Task<string?> ExecuteAsync(MonitoredService service, Credential? credential, CancellationToken ct)
    {
        var wanted = JobCommon.SplitList(service.JobName);
        if (wanted.Count == 0) return "Görev adı tanımlı değil";
        await using var conn = await OpenAsync(service, credential, ct);

        foreach (var prefix in new[] { "dba", "all" })
        {
            try
            {
                var states = await QueryAsync(conn, prefix, wanted, ct);
                var (val, thr, err, ser) = JobCommon.Evaluate(service, states, durationBased: true);
                OverrideResponseValue = val;
                IsThresholdError = thr;
                service.LastJobStates = ser;
                return err;
            }
            catch (OracleException ex) when (ex.Number == 942 && prefix == "dba") { /* dba görünümü yok → all_ dene */ }
        }
        return "Scheduler görünümlerine erişilemedi";
    }

    private static async Task<List<JobCommon.JobState>> QueryAsync(OracleConnection conn, string prefix, List<string> wanted, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.BindByName = true;
        var conds = new List<string>();
        for (int i = 0; i < wanted.Count; i++)
        {
            var (ow, nm) = JobCommon.SplitOwned(wanted[i]);
            conds.Add($"(UPPER(j.job_name) = UPPER(:n{i}) AND (:o{i} IS NULL OR UPPER(j.owner) = UPPER(:o{i})))");
            cmd.Parameters.Add($"n{i}", nm);
            cmd.Parameters.Add($"o{i}", (object?)ow ?? DBNull.Value);
        }
        cmd.CommandText =
            // ROUND şart: SYSDATE-tarih farkı yüksek hassasiyetli NUMBER üretir; ODP.NET decimal'e
            // çeviremeyip "Specified cast is not valid / Arithmetic overflow" fırlatır (v2.27.0-pre.4 hatası).
            // Yaş SANİYE cinsinden tam sayıya yuvarlanır (mini kutuda tarih-saat sn hassasiyetiyle türetilir).
            "SELECT j.owner, j.job_name, j.enabled, ROUND((SYSDATE - CAST(j.last_start_date AS DATE)) * 86400) AS age_sec, " +
            "d.status, d.dur_sec " +
            $"FROM {prefix}_scheduler_jobs j LEFT JOIN (" +
            "  SELECT owner, job_name, status, " +
            "         EXTRACT(DAY FROM run_duration)*86400 + EXTRACT(HOUR FROM run_duration)*3600 + " +
            "         EXTRACT(MINUTE FROM run_duration)*60 + ROUND(EXTRACT(SECOND FROM run_duration)) AS dur_sec, " +
            "         ROW_NUMBER() OVER (PARTITION BY owner, job_name ORDER BY actual_start_date DESC) rn " +
            $"  FROM {prefix}_scheduler_job_run_details) d " +
            "ON d.owner = j.owner AND d.job_name = j.job_name AND d.rn = 1 " +
            "WHERE " + string.Join(" OR ", conds);

        var rows = new List<(string Owner, string Name, bool Enabled, long? AgeSec, string? Status, long? DurSec)>();
        await using (var rd = await cmd.ExecuteReaderAsync(ct))
            while (await rd.ReadAsync(ct))
                rows.Add((
                    rd.IsDBNull(0) ? "" : rd.GetString(0),
                    rd.IsDBNull(1) ? "" : rd.GetString(1),
                    !rd.IsDBNull(2) && string.Equals(rd.GetString(2), "TRUE", StringComparison.OrdinalIgnoreCase),
                    rd.IsDBNull(3) ? null : Convert.ToInt64(rd.GetValue(3)),
                    rd.IsDBNull(4) ? null : rd.GetString(4),
                    rd.IsDBNull(5) ? null : Convert.ToInt64(rd.GetValue(5))));

        var states = new List<JobCommon.JobState>();
        foreach (var w in wanted)
        {
            var (ow, nm) = JobCommon.SplitOwned(w);
            var matches = rows.Where(r => string.Equals(r.Name, nm, StringComparison.OrdinalIgnoreCase)
                && (ow == null || string.Equals(r.Owner, ow, StringComparison.OrdinalIgnoreCase))).ToList();
            if (matches.Count == 0) { states.Add(new(w, false, false, null, null, null)); continue; }
            foreach (var r in matches)
            {
                var fail = r.Status != null && !string.Equals(r.Status, "SUCCEEDED", StringComparison.OrdinalIgnoreCase) ? r.Status : null;
                states.Add(new($"{r.Owner}.{r.Name}", true, r.Enabled, fail, r.AgeSec, r.DurSec));
            }
        }
        return states;
    }
}

/// <summary>MSSQL Agent job seti: msdb sysjobs + her job'ın SON koşusu (step 0).
/// Extra = veritabanı (opsiyonel), JobName = ';' ayraçlı Agent job adları.</summary>
public class MsSqlAgentJobChecker : MsSqlDbCheckerBase
{
    public override ServiceType Type => ServiceType.MsSqlAgentJob;

    protected override async Task<string?> ExecuteAsync(MonitoredService service, Credential? credential, CancellationToken ct)
    {
        var wanted = JobCommon.SplitList(service.JobName);
        if (wanted.Count == 0) return "Görev adı tanımlı değil";
        await using var conn = await OpenAsync(service, credential, ct);

        await using var cmd = conn.CreateCommand();
        var pars = new List<string>();
        for (int i = 0; i < wanted.Count; i++)
        {
            pars.Add($"@n{i}");
            cmd.Parameters.Add(new SqlParameter($"@n{i}", wanted[i]));
        }
        cmd.CommandText =
            "SELECT j.name, j.enabled, h.run_status, " +
            "DATEDIFF(SECOND, msdb.dbo.agent_datetime(h.run_date, h.run_time), GETDATE()) AS age_sec, " +
            "(h.run_duration/10000)*3600 + ((h.run_duration/100)%100)*60 + (h.run_duration%100) AS dur_sec, " +
            "SUBSTRING(h.message, 1, 120) " +
            "FROM msdb.dbo.sysjobs j " +
            "OUTER APPLY (SELECT TOP 1 * FROM msdb.dbo.sysjobhistory x " +
            "  WHERE x.job_id = j.job_id AND x.step_id = 0 ORDER BY x.instance_id DESC) h " +
            $"WHERE j.name IN ({string.Join(", ", pars)})";

        var states = new List<JobCommon.JobState>();
        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using (var rd = await cmd.ExecuteReaderAsync(ct))
            while (await rd.ReadAsync(ct))
            {
                var name = rd.GetString(0);
                found.Add(name);
                var enabled = !rd.IsDBNull(1) && Convert.ToInt32(rd.GetValue(1)) == 1;
                int? runStatus = rd.IsDBNull(2) ? null : Convert.ToInt32(rd.GetValue(2));
                long? ageSec = rd.IsDBNull(3) ? null : Convert.ToInt64(rd.GetValue(3));
                long? durSec = rd.IsDBNull(4) ? null : Convert.ToInt64(rd.GetValue(4));
                var msg = rd.IsDBNull(5) ? null : rd.GetString(5);
                // run_status: 0=Failed 1=Succeeded 2=Retry 3=Canceled 4=InProgress
                var fail = runStatus is 0 or 3
                    ? (runStatus == 0 ? "BAŞARISIZ" : "İPTAL") + (string.IsNullOrWhiteSpace(msg) ? "" : $" ({msg})")
                    : null;
                states.Add(new(name, true, enabled, fail, ageSec, durSec));
            }
        foreach (var w in wanted)
            if (!found.Contains(w)) states.Add(new(w, false, false, null, null, null));

        var (val, thr, err, ser) = JobCommon.Evaluate(service, states, durationBased: true);
        OverrideResponseValue = val;
        IsThresholdError = thr;
        service.LastJobStates = ser;
        return err;
    }
}

/// <summary>MySQL Event seti: information_schema.EVENTS — MySQL sonuç/süre TUTMAZ; yalnız son
/// çalıştırma zamanı ve etkinlik izlenir. Grafik: EN ESKİ son koşu yaşı (dk).
/// Extra = veritabanı (opsiyonel), JobName = ';' ayraçlı "schema.event" listesi.</summary>
public class MySqlEventJobChecker : MySqlDbCheckerBase
{
    public override ServiceType Type => ServiceType.MySqlEventJob;

    protected override async Task<string?> ExecuteAsync(MonitoredService service, Credential? credential, CancellationToken ct)
    {
        var wanted = JobCommon.SplitList(service.JobName);
        if (wanted.Count == 0) return "Event adı tanımlı değil";
        await using var conn = await OpenAsync(service, credential, ct);

        await using var cmd = conn.CreateCommand();
        var conds = new List<string>();
        for (int i = 0; i < wanted.Count; i++)
        {
            var (sc, nm) = JobCommon.SplitOwned(wanted[i]);
            conds.Add($"(EVENT_NAME = @n{i} AND (@s{i} IS NULL OR EVENT_SCHEMA = @s{i}))");
            cmd.Parameters.Add(new MySqlParameter($"@n{i}", nm));
            cmd.Parameters.Add(new MySqlParameter($"@s{i}", (object?)sc ?? DBNull.Value));
        }
        cmd.CommandText =
            "SELECT EVENT_SCHEMA, EVENT_NAME, STATUS, TIMESTAMPDIFF(SECOND, LAST_EXECUTED, NOW()) " +
            "FROM information_schema.EVENTS WHERE " + string.Join(" OR ", conds);

        var rows = new List<(string Schema, string Name, string Status, long? AgeSec)>();
        await using (var rd = await cmd.ExecuteReaderAsync(ct))
            while (await rd.ReadAsync(ct))
                rows.Add((rd.GetString(0), rd.GetString(1),
                    rd.IsDBNull(2) ? "" : rd.GetString(2),
                    rd.IsDBNull(3) ? null : Convert.ToInt64(rd.GetValue(3))));

        var states = new List<JobCommon.JobState>();
        foreach (var w in wanted)
        {
            var (sc, nm) = JobCommon.SplitOwned(w);
            var matches = rows.Where(r => string.Equals(r.Name, nm, StringComparison.OrdinalIgnoreCase)
                && (sc == null || string.Equals(r.Schema, sc, StringComparison.OrdinalIgnoreCase))).ToList();
            if (matches.Count == 0) { states.Add(new(w, false, false, null, null, null)); continue; }
            foreach (var r in matches)
                states.Add(new($"{r.Schema}.{r.Name}", true,
                    r.Status.StartsWith("ENABLED", StringComparison.OrdinalIgnoreCase), null, r.AgeSec, null));
        }

        var (val, thr, err, ser) = JobCommon.Evaluate(service, states, durationBased: false);
        OverrideResponseValue = val;
        IsThresholdError = thr;
        service.LastJobStates = ser;
        return err;
    }
}

/// <summary>Windows Görev Zamanlayıcı seti: Schedule.Service COM ile uzak bağlantı.
/// JobName = ';' ayraçlı görev yolları ("\Klasör\Ad"). Grafik: EN ESKİ son koşu yaşı (dk).</summary>
public class WindowsTaskJobChecker : CheckerBase
{
    public override ServiceType Type => ServiceType.WindowsTaskJob;

    protected override Task<string?> ExecuteAsync(MonitoredService service, Credential? credential, CancellationToken ct)
    {
        var wanted = JobCommon.SplitList(service.JobName);
        if (wanted.Count == 0) return Task.FromResult<string?>("Görev yolu tanımlı değil");

        return Task.Run<string?>(() =>
        {
            dynamic? ts = null;
            try
            {
                ts = WindowsTaskTools.Connect(service.Target, credential, service.TimeoutSeconds);
                var states = new List<JobCommon.JobState>();
                foreach (var path in wanted)
                    states.Add(WindowsTaskTools.ReadTaskState(ts!, path));
                var (val, thr, err, ser) = JobCommon.Evaluate(service, states, durationBased: false);
                OverrideResponseValue = val;
                IsThresholdError = thr;
                service.LastJobStates = ser;
                return err;
            }
            finally
            {
                if (ts != null) System.Runtime.InteropServices.Marshal.ReleaseComObject(ts);
            }
        }, ct);
    }
}

/// <summary>systemd timer seti (SSH): her timer'ın son tetiklenmesi + tetiklediği servisin sonucu/süresi.
/// Yaş/süre hesapları SUNUCU tarafında yapılır (saat dilimi sapması olmaz).
/// JobName = ';' ayraçlı timer birimleri. Grafik: EN UZUN son koşu süresi (sn).</summary>
public class SystemdTimerJobChecker : CheckerBase
{
    public override ServiceType Type => ServiceType.SystemdTimerJob;

    protected override Task<string?> ExecuteAsync(MonitoredService service, Credential? credential, CancellationToken ct)
    {
        if (credential == null) return Task.FromResult<string?>("Kimlik bilgisi tanımlı değil");
        var wanted = JobCommon.SplitList(service.JobName);
        if (wanted.Count == 0) return Task.FromResult<string?>("Timer adı tanımlı değil");
        var units = wanted.Select(SystemdTools.NormalizeTimer).ToList();
        if (units.Any(u => u == null)) return Task.FromResult<string?>("Geçersiz timer adı (yalnız harf/rakam/@ . _ - kullanın)");

        return Task.Run<string?>(() =>
        {
            using var ssh = SystemdTools.Connect(service, credential);
            // Tek SSH oturumunda tüm timer'lar: T=<timer>|EN|ACT|RES|AGE|DUR satırları
            var script =
                $"for t in {string.Join(" ", units)}; do " +
                "u=$(systemctl show -p Unit --value \"$t\" 2>/dev/null); " +
                "en=$(systemctl is-enabled \"$t\" 2>/dev/null || echo unknown); " +
                "act=$(systemctl is-active \"$t\" 2>/dev/null || echo unknown); " +
                "now=$(date +%s); " +
                "lts=$(date -d \"$(systemctl show -p LastTriggerUSec --value \"$t\" 2>/dev/null)\" +%s 2>/dev/null || echo 0); " +
                "res=$(systemctl show -p Result --value \"$u\" 2>/dev/null); " +
                "s1=$(date -d \"$(systemctl show -p ExecMainStartTimestamp --value \"$u\" 2>/dev/null)\" +%s 2>/dev/null || echo 0); " +
                "s2=$(date -d \"$(systemctl show -p ExecMainExitTimestamp --value \"$u\" 2>/dev/null)\" +%s 2>/dev/null || echo 0); " +
                "if [ \"$lts\" -gt 0 ]; then age=$((now-lts)); else age=-1; fi; " +
                "if [ \"$s1\" -gt 0 ] && [ \"$s2\" -ge \"$s1\" ]; then dur=$((s2-s1)); else dur=-1; fi; " +
                "echo \"T=$t|EN=$en|ACT=$act|RES=$res|AGE=$age|DUR=$dur\"; done";
            using var cmd = ssh.CreateCommand(script);
            var outp = cmd.Execute() ?? "";

            var states = new List<JobCommon.JobState>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var line in outp.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var map = line.Split('|').Select(p => p.Split('=', 2))
                    .Where(a => a.Length == 2).ToDictionary(a => a[0], a => a[1]);
                if (!map.TryGetValue("T", out var tname)) continue;
                seen.Add(tname);
                var act = map.GetValueOrDefault("ACT", "unknown");
                var res = map.GetValueOrDefault("RES", "");
                long ageSec = long.TryParse(map.GetValueOrDefault("AGE"), out var a) ? a : -1;
                long durSec = long.TryParse(map.GetValueOrDefault("DUR"), out var d) ? d : -1;

                bool found = act != "unknown";
                bool enabled = act is "active" or "activating";
                var fail = !string.IsNullOrEmpty(res) && res != "success" ? $"Result: {res}" : null;
                states.Add(new(tname, found, enabled, fail,
                    ageSec >= 0 ? ageSec : null, durSec >= 0 ? durSec : null));
            }
            foreach (var u in units)
                if (u != null && !seen.Contains(u)) states.Add(new(u, false, false, null, null, null));

            if (states.Count == 0) return "systemctl çıktısı okunamadı";
            var (val, thr, err, ser) = JobCommon.Evaluate(service, states, durationBased: true);
            OverrideResponseValue = val;
            IsThresholdError = thr;
            service.LastJobStates = ser;
            return err;
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

    /// <summary>Tek görevin durumunu okur (bulunamazsa Found=false).</summary>
    public static JobCommon.JobState ReadTaskState(dynamic ts, string path)
    {
        dynamic? task = null;
        try
        {
            var (folder, name) = SplitPath(path);
            task = ts.GetFolder(folder).GetTask(name);

            bool enabled = task.Enabled;
            int state = task.State;                     // 1=Disabled 2=Queued 3=Ready 4=Running
            DateTime lastRun = task.LastRunTime;
            int lastResult = task.LastTaskResult;

            bool neverRan = lastRun.Year < 2000;        // 1899-12-30 = hiç koşmadı
            long? ageSec = neverRan ? null : (long)Math.Max(0, (DateTime.Now - lastRun).TotalSeconds);
            // 0 = başarı; 0x41301 = şu an çalışıyor; 0x41303 = henüz hiç koşmadı
            var fail = state != 4 && !neverRan && lastResult != 0 && lastResult != 0x41301 && lastResult != 0x41303
                ? $"sonuç kodu 0x{lastResult:X}" : null;
            return new JobCommon.JobState(path, true, enabled && state != 1, fail, ageSec, null);
        }
        catch (System.IO.FileNotFoundException) { return new JobCommon.JobState(path, false, false, null, null, null); }
        catch (System.Runtime.InteropServices.COMException ex) when ((uint)ex.HResult == 0x80070002)
        { return new JobCommon.JobState(path, false, false, null, null, null); }
        finally
        {
            if (task != null) System.Runtime.InteropServices.Marshal.ReleaseComObject(task);
        }
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
