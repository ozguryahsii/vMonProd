using System.Management;
using vMonitor.Models;

namespace vMonitor.Services.Checkers;

/// <summary>DHCP: uzak Windows sunucusunda DHCP Server servisinin durumuna
/// WMI ile bakar. Yetkili bir domain hesabı gerekir.
/// Extra = Windows servis adı (boşsa "DHCPServer").</summary>
public class DhcpChecker : CheckerBase
{
    public override ServiceType Type => ServiceType.DhcpWindowsService;

    protected override Task<string?> ExecuteAsync(MonitoredService service, Credential? credential, CancellationToken ct)
    {
        return Task.Run<string?>(() =>
        {
            var serviceName = string.IsNullOrWhiteSpace(service.Extra) ? "DHCPServer" : service.Extra.Trim();

            var options = new ConnectionOptions { Timeout = TimeSpan.FromSeconds(service.TimeoutSeconds) };
            if (credential != null)
            {
                var username = PlainUsername(credential);
                options.Username = string.IsNullOrWhiteSpace(credential.Domain)
                    ? username
                    : $"{credential.Domain}\\{username}";
                options.Password = PlainPassword(credential);
            }

            var scope = new ManagementScope($"\\\\{service.Target}\\root\\cimv2", options);
            scope.Connect();

            var query = new ObjectQuery(
                $"SELECT Name, State FROM Win32_Service WHERE Name = '{serviceName.Replace("'", "''")}'");
            using var searcher = new ManagementObjectSearcher(scope, query);
            using var results = searcher.Get();

            foreach (ManagementObject svc in results)
            {
                var state = svc["State"]?.ToString();
                return state == "Running" ? null : $"Servis durumu: {state}";
            }
            return $"'{serviceName}' adında servis bulunamadı";
        }, ct);
    }
}
