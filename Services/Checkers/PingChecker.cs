using System.Net.NetworkInformation;
using vMonitor.Models;

namespace vMonitor.Services.Checkers;

/// <summary>ICMP ping. Extra = beklenen maksimum RTT ms (opsiyonel).</summary>
public class PingChecker : CheckerBase
{
    public override ServiceType Type => ServiceType.Ping;

    protected override async Task<string?> ExecuteAsync(MonitoredService service, Credential? credential, CancellationToken ct)
    {
        using var ping = new Ping();
        var reply = await ping.SendPingAsync(service.Target, Math.Max(1000, service.TimeoutSeconds * 1000));

        if (reply.Status != IPStatus.Success)
            return $"Ping başarısız: {reply.Status}";

        if (int.TryParse(service.Extra, out var maxRtt) && reply.RoundtripTime > maxRtt)
            return $"RTT yüksek: {reply.RoundtripTime} ms (eşik {maxRtt} ms)";

        return null;
    }
}
