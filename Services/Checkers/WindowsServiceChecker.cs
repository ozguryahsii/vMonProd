using System.Management;
using vMonitor.Models;

namespace vMonitor.Services.Checkers;

/// <summary>Uzak Windows sunucusunda belirli bir Windows servisinin durumunu WMI ile kontrol eder.
/// Extra = servis adı (örn. W3SVC, MSSQLSERVER). Running → UP, aksi halde DOWN.</summary>
public class WindowsServiceChecker : CheckerBase
{
    public override ServiceType Type => ServiceType.WindowsServiceControl;

    protected override Task<string?> ExecuteAsync(MonitoredService service, Credential? credential, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(service.Extra)) return Task.FromResult<string?>("Servis adı (Ekstra) tanımlı değil");

        return Task.Run<string?>(() =>
        {
            var state = ServiceControl.WindowsServiceState(service, credential);
            return state == "Running" ? null : $"Servis durumu: {state ?? "bulunamadı"}";
        }, ct);
    }
}
