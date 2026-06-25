using vMonitor.Models;

namespace vMonitor.Services.Checkers;

/// <summary>Uzak Linux sunucusunda systemd servis durumunu SSH ile kontrol eder.
/// Extra = servis/unit adı (örn. nginx, httpd). active → UP, aksi halde DOWN.</summary>
public class LinuxServiceChecker : CheckerBase
{
    public override ServiceType Type => ServiceType.LinuxServiceControl;

    protected override Task<string?> ExecuteAsync(MonitoredService service, Credential? credential, CancellationToken ct)
    {
        if (credential == null) return Task.FromResult<string?>("Kimlik bilgisi tanımlı değil");
        if (string.IsNullOrWhiteSpace(service.Extra)) return Task.FromResult<string?>("Servis adı (Ekstra) tanımlı değil");

        return Task.Run<string?>(() =>
        {
            var state = ServiceControl.LinuxServiceState(service, credential);
            return state == "active" ? null : $"Servis durumu: {state}";
        }, ct);
    }
}
