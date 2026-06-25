using System.Management;
using Renci.SshNet;
using vMonitor.Models;

namespace vMonitor.Services;

/// <summary>Windows (WMI) ve Linux (SSH) servislerinin durum sorgusu ve uzaktan
/// start/stop/restart işlemleri. Kimlik bilgisi şifresi Vault/DPAPI'den çözülür.</summary>
public static class ServiceControl
{
    public record ActionResult(bool Ok, string Message);

    private static ManagementScope WmiScope(MonitoredService s, Credential? cred)
    {
        var options = new ConnectionOptions { Timeout = TimeSpan.FromSeconds(Math.Max(5, s.TimeoutSeconds)) };
        if (cred != null)
        {
            var user = VaultClient.GetUsername(cred);
            options.Username = string.IsNullOrWhiteSpace(cred.Domain) ? user : $"{cred.Domain}\\{user}";
            options.Password = VaultClient.GetPassword(cred);
        }
        var scope = new ManagementScope($"\\\\{s.Target}\\root\\cimv2", options);
        scope.Connect();
        return scope;
    }

    public static string? WindowsServiceState(MonitoredService s, Credential? cred)
    {
        var scope = WmiScope(s, cred);
        using var searcher = new ManagementObjectSearcher(scope,
            new ObjectQuery($"SELECT State FROM Win32_Service WHERE Name = '{(s.Extra ?? "").Replace("'", "''")}'"));
        foreach (ManagementObject mo in searcher.Get())
            return mo["State"]?.ToString();
        return null;
    }

    public static ActionResult WindowsAction(MonitoredService s, Credential? cred, string action)
    {
        try
        {
            var scope = WmiScope(s, cred);
            using var searcher = new ManagementObjectSearcher(scope,
                new ObjectQuery($"SELECT * FROM Win32_Service WHERE Name = '{(s.Extra ?? "").Replace("'", "''")}'"));
            ManagementObject? svc = null;
            foreach (ManagementObject mo in searcher.Get()) { svc = mo; break; }
            if (svc == null) return new(false, $"'{s.Extra}' servisi bulunamadı");

            uint Invoke(string m) => (uint)svc.InvokeMethod(m, null);
            uint rc;
            switch (action)
            {
                case "start": rc = Invoke("StartService"); break;
                case "stop": rc = Invoke("StopService"); break;
                case "restart":
                    Invoke("StopService");
                    System.Threading.Thread.Sleep(3000);
                    rc = Invoke("StartService");
                    break;
                default: return new(false, "Geçersiz işlem");
            }
            // 0 = başarılı, 10 = zaten çalışıyor, 5 = zaten durmuş gibi durumları başarı say
            return rc == 0 || rc == 10 || rc == 5
                ? new(true, $"İşlem gönderildi ({action}).")
                : new(false, $"WMI dönüş kodu {rc} — işlem reddedildi olabilir.");
        }
        catch (Exception ex) { return new(false, ex.Message); }
    }

    private static SshClient SshConnect(MonitoredService s, Credential cred)
    {
        var user = VaultClient.GetUsername(cred);
        var info = new Renci.SshNet.ConnectionInfo(s.Target, s.Port ?? 22, user,
            new PasswordAuthenticationMethod(user, VaultClient.GetPassword(cred)))
        { Timeout = TimeSpan.FromSeconds(Math.Max(5, s.TimeoutSeconds)) };
        var ssh = new SshClient(info);
        ssh.Connect();
        return ssh;
    }

    public static string LinuxServiceState(MonitoredService s, Credential cred)
    {
        using var ssh = SshConnect(s, cred);
        try
        {
            using var cmd = ssh.CreateCommand($"systemctl is-active {Shell(s.Extra)}");
            var outp = (cmd.Execute() ?? "").Trim();
            return string.IsNullOrEmpty(outp) ? "unknown" : outp;
        }
        finally { ssh.Disconnect(); }
    }

    public static ActionResult LinuxAction(MonitoredService s, Credential cred, string action)
    {
        if (action != "start" && action != "stop" && action != "restart") return new(false, "Geçersiz işlem");
        try
        {
            using var ssh = SshConnect(s, cred);
            try
            {
                using var cmd = ssh.CreateCommand($"sudo -n systemctl {action} {Shell(s.Extra)} 2>&1; echo RC=$?");
                var outp = (cmd.Execute() ?? "").Trim();
                var ok = outp.Contains("RC=0");
                return new(ok, ok ? $"İşlem gönderildi ({action})." : "Komut başarısız: " + outp);
            }
            finally { ssh.Disconnect(); }
        }
        catch (Exception ex) { return new(false, ex.Message); }
    }

    // Basit shell argüman kaçışı
    private static string Shell(string? v) => "'" + (v ?? "").Replace("'", "'\\''") + "'";
}
