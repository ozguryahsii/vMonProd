using System.Net.Security;
using System.Net.Sockets;
using System.Text;
using vMonitor.Models;

namespace vMonitor.Services.Checkers;

/// <summary>IMAP (Exchange mailbox erişimi): bağlanır, "* OK" greeting'i doğrular.
/// UseSsl = implicit TLS (port 993).</summary>
public class ImapChecker : CheckerBase
{
    public override ServiceType Type => ServiceType.Imap;

    protected override async Task<string?> ExecuteAsync(MonitoredService service, Credential? credential, CancellationToken ct)
    {
        using var client = new TcpClient();
        await client.ConnectAsync(service.Target, service.Port ?? (service.UseSsl ? 993 : 143), ct);

        Stream stream = client.GetStream();
        if (service.UseSsl)
        {
            var ssl = new SslStream(stream, false,
                (_, _, _, errors) => service.IgnoreCertErrors || errors == SslPolicyErrors.None);
            await ssl.AuthenticateAsClientAsync(service.Target);
            stream = ssl;
        }

        using var reader = new StreamReader(stream, Encoding.ASCII);
        var greeting = await reader.ReadLineAsync().WaitAsync(ct);
        if (greeting == null || !greeting.StartsWith("* OK"))
            return $"Beklenmeyen IMAP greeting: {greeting ?? "(boş)"}";
        return null;
    }
}
