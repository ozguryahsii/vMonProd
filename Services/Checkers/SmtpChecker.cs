using System.Net.Security;
using System.Net.Sockets;
using System.Text;
using vMonitor.Models;

namespace vMonitor.Services.Checkers;

/// <summary>SMTP (Exchange / Exchange Online / relay): bağlanır, 220 banner'ı bekler,
/// EHLO gönderip 250 yanıtı doğrular. UseSsl = implicit TLS (örn. port 465);
/// Exchange Online için smtp.office365.com:25/587 banner kontrolü yeterlidir.</summary>
public class SmtpChecker : CheckerBase
{
    public override ServiceType Type => ServiceType.Smtp;

    protected override async Task<string?> ExecuteAsync(MonitoredService service, Credential? credential, CancellationToken ct)
    {
        using var client = new TcpClient();
        await client.ConnectAsync(service.Target, service.Port ?? 25, ct);

        Stream stream = client.GetStream();
        if (service.UseSsl)
        {
            var ssl = new SslStream(stream, false,
                (_, _, _, errors) => service.IgnoreCertErrors || errors == SslPolicyErrors.None);
            await ssl.AuthenticateAsClientAsync(service.Target);
            stream = ssl;
        }

        using var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, leaveOpen: true);
        using var writer = new StreamWriter(stream, Encoding.ASCII, 1024, leaveOpen: true) { AutoFlush = true, NewLine = "\r\n" };

        var banner = await ReadLineAsync(reader, ct);
        if (banner == null || !banner.StartsWith("220"))
            return $"Beklenmeyen SMTP banner: {banner ?? "(boş)"}";

        await writer.WriteLineAsync("EHLO vmon.local");
        string? line, code = null;
        // EHLO çok satırlı döner: "250-..." satırları, son satır "250 ..."
        while ((line = await ReadLineAsync(reader, ct)) != null)
        {
            code = line.Length >= 3 ? line[..3] : line;
            if (line.Length < 4 || line[3] != '-') break;
        }
        if (code != "250") return $"EHLO yanıtı beklenmedik: {line ?? "(boş)"}";

        await writer.WriteLineAsync("QUIT");
        return null;
    }

    private static async Task<string?> ReadLineAsync(StreamReader reader, CancellationToken ct)
    {
        var task = reader.ReadLineAsync();
        return await task.WaitAsync(ct);
    }
}
