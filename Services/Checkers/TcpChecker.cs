using System.Net.Sockets;
using vMonitor.Models;

namespace vMonitor.Services.Checkers;

/// <summary>Genel TCP port kontrolü — diğer tiplere uymayan servisler için.</summary>
public class TcpChecker : CheckerBase
{
    public override ServiceType Type => ServiceType.Tcp;

    protected override async Task<string?> ExecuteAsync(MonitoredService service, Credential? credential, CancellationToken ct)
    {
        using var client = new TcpClient();
        await client.ConnectAsync(service.Target, service.Port ?? 0, ct);
        return null;
    }
}
