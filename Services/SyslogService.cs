using System.Globalization;
using System.Net.Sockets;
using System.Text;
using vMonitor.Models;

namespace vMonitor.Services;

/// <summary>Denetim kayıtlarını merkezi SIEM/syslog sunucusuna iletir (PCI DSS 10.5.4, NIST AU-6).
/// Yapılandırma startup'ta + Ayarlar kaydında güncellenir (per-kayıt DB okuması yok). Gönderim fire-and-forget;
/// başarısızlık denetim/işlemi bozmaz.</summary>
public class SyslogService
{
    private readonly ILogger<SyslogService> _logger;
    public SyslogService(ILogger<SyslogService> logger) => _logger = logger;

    public volatile bool Enabled;
    private volatile string _host = "";
    private volatile int _port = 514;
    private volatile bool _tcp;

    public void Configure(MonitorSettings s)
    {
        Enabled = s.SyslogEnabled && !string.IsNullOrWhiteSpace(s.SyslogHost);
        _host = s.SyslogHost ?? "";
        _port = s.SyslogPort;
        _tcp = s.SyslogTcp;
    }

    public void Forward(AuditLog a)
    {
        if (!Enabled) return;
        var msg = Build(a);
        _ = Task.Run(() =>
        {
            try { SendOne(msg); }
            catch (Exception ex) { _logger.LogDebug(ex, "Syslog gönderilemedi"); }
        });
    }

    /// <summary>Bağlantı testi — Ayarlar'dan "Test" için.</summary>
    public (bool ok, string message) Test()
    {
        try { SendOne(Build(new AuditLog { Action = "syslog.test", User = "system", Detail = "vMon syslog baglanti testi", Success = true })); return (true, "Test mesajı gönderildi."); }
        catch (Exception ex) { return (false, ex.GetBaseException().Message); }
    }

    private string Build(AuditLog a)
    {
        // RFC 3164: <PRI>TIMESTAMP HOSTNAME TAG: MSG   (facility local0=16; severity: info=6 / warning=4)
        int sev = a.Success ? 6 : 4;
        int pri = 16 * 8 + sev;
        var ts = (a.At == default ? DateTime.Now : a.At.ToLocalTime()).ToString("MMM dd HH:mm:ss", CultureInfo.InvariantCulture);
        var host = Environment.MachineName;
        string San(string? s) => (s ?? "").Replace('\n', ' ').Replace('\r', ' ');
        var msg = $"vMon: action={San(a.Action)} user={San(a.User)} ip={San(a.Ip)} target={San(a.Target)} success={(a.Success ? 1 : 0)} detail=\"{San(a.Detail)}\"";
        return $"<{pri}>{ts} {host} {msg}";
    }

    private void SendOne(string message)
    {
        var bytes = Encoding.UTF8.GetBytes(message);
        if (_tcp)
        {
            using var c = new TcpClient();
            c.Connect(_host, _port);
            using var ns = c.GetStream();
            ns.Write(bytes, 0, bytes.Length);
            ns.WriteByte((byte)'\n');
        }
        else
        {
            using var u = new UdpClient();
            u.Send(bytes, bytes.Length, _host, _port);
        }
    }
}
