using System.Globalization;
using System.Management;
using vMonitor.Models;

namespace vMonitor.Services.Checkers;

/// <summary>Windows sunucu sağlığı: WMI ile CPU yükü, RAM ve disk doluluk oranları.
/// Eşik (CpuThresholdPercent vb.) aşılırsa DOWN sayılır; eşik boşsa yalnızca ölçülür.</summary>
public class WindowsHealthChecker : CheckerBase
{
    public override ServiceType Type => ServiceType.WindowsHealth;

    protected override Task<string?> ExecuteAsync(MonitoredService service, Credential? credential, CancellationToken ct)
    {
        return Task.Run<string?>(() =>
        {
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

            // CPU — performans sayacından anlık toplam yük.
            // NOT: Win32_Processor.LoadPercentage KULLANILMAZ; o, her çekirdeği ~1 sn
            // örnekler ve çok çekirdekli sunucularda saniyelerce sürer. PerfFormattedData
            // _Total değeri sistem tarafından önceden hesaplanır ve anında döner.
            double? cpu = null;
            using (var searcher = new ManagementObjectSearcher(scope,
                new ObjectQuery("SELECT PercentProcessorTime FROM Win32_PerfFormattedData_PerfOS_Processor WHERE Name='_Total'")))
            {
                foreach (ManagementObject mo in searcher.Get())
                    if (mo["PercentProcessorTime"] != null) cpu = Math.Round(Convert.ToDouble(mo["PercentProcessorTime"]), 1);
            }

            // RAM (TotalVisibleMemorySize KB cinsindendir)
            double? ram = null, totalRamGb = null;
            using (var searcher = new ManagementObjectSearcher(scope,
                new ObjectQuery("SELECT TotalVisibleMemorySize, FreePhysicalMemory FROM Win32_OperatingSystem")))
            {
                foreach (ManagementObject mo in searcher.Get())
                {
                    var total = Convert.ToDouble(mo["TotalVisibleMemorySize"]);
                    var free = Convert.ToDouble(mo["FreePhysicalMemory"]);
                    if (total > 0)
                    {
                        ram = Math.Round(100.0 * (total - free) / total, 1);
                        totalRamGb = Math.Round(total / 1048576.0, 0);
                    }
                }
            }

            // Mantıksal işlemci sayısı
            int? cores = null;
            using (var searcher = new ManagementObjectSearcher(scope,
                new ObjectQuery("SELECT NumberOfLogicalProcessors FROM Win32_ComputerSystem")))
            {
                foreach (ManagementObject mo in searcher.Get())
                    if (mo["NumberOfLogicalProcessors"] != null)
                        cores = Convert.ToInt32(mo["NumberOfLogicalProcessors"]);
            }

            // Diskler (yalnızca sabit diskler, DriveType=3)
            double? maxDisk = null;
            var diskParts = new List<string>();
            var diskCapacities = new List<string>();
            var disks = new List<(string id, double used)>();
            using (var searcher = new ManagementObjectSearcher(scope,
                new ObjectQuery("SELECT DeviceID, Size, FreeSpace FROM Win32_LogicalDisk WHERE DriveType = 3")))
            {
                foreach (ManagementObject mo in searcher.Get())
                {
                    var size = Convert.ToDouble(mo["Size"]);
                    if (size <= 0) continue;
                    var freeSpace = Convert.ToDouble(mo["FreeSpace"]);
                    var used = Math.Round(100.0 * (size - freeSpace) / size, 1);
                    var id = mo["DeviceID"]?.ToString() ?? "?";
                    diskParts.Add($"{id} %{used.ToString("0.#", CultureInfo.InvariantCulture)}");
                    diskCapacities.Add($"{id} {FormatGb(size)}");
                    disks.Add((id, used));
                    if (maxDisk == null || used > maxDisk) maxDisk = used;
                }
            }

            var capacityParts = new List<string>();
            if (cores.HasValue) capacityParts.Add($"{cores} CPU");
            if (totalRamGb.HasValue) capacityParts.Add($"{totalRamGb:0} GB RAM");
            capacityParts.AddRange(diskCapacities);

            CollectedMetrics = new HealthMetricsData(cpu, ram, maxDisk,
                diskParts.Count > 0 ? string.Join(" · ", diskParts) : null,
                capacityParts.Count > 0 ? string.Join(" · ", capacityParts) : null);

            // Ulaşıldı; eşik aşıldıysa bu bir DOWN değil ERROR durumudur
            var thresholdErr = BuildThresholdError(service, cpu, ram, maxDisk, disks);
            IsThresholdError = thresholdErr != null;
            return thresholdErr;
        }, ct);
    }

    /// <summary>Bayt cinsinden boyutu okunur GB/TB metnine çevirir — Linux checker'ı da kullanır.</summary>
    internal static string FormatGb(double bytes)
    {
        var gb = bytes / 1073741824.0;
        return gb >= 1024
            ? $"{(gb / 1024).ToString("0.#", CultureInfo.InvariantCulture)} TB"
            : $"{gb.ToString("0", CultureInfo.InvariantCulture)} GB";
    }

    /// <summary>Ortak eşik kontrolü — Linux checker'ı da kullanır.
    /// disks verilirse disk eşiği aşıldığında HANGİ disk(ler)in aştığı yazılır (örn. "Disk C: %92, E: %96").</summary>
    internal static string? BuildThresholdError(MonitoredService service, double? cpu, double? ram, double? maxDisk,
        List<(string id, double used)>? disks = null)
    {
        var breaches = new List<string>();
        if (service.CpuThresholdPercent.HasValue && cpu.HasValue && cpu > service.CpuThresholdPercent)
            breaches.Add($"CPU %{cpu:0.#} (eşik %{service.CpuThresholdPercent})");
        if (service.RamThresholdPercent.HasValue && ram.HasValue && ram > service.RamThresholdPercent)
            breaches.Add($"RAM %{ram:0.#} (eşik %{service.RamThresholdPercent})");
        if (service.DiskThresholdPercent.HasValue && maxDisk.HasValue && maxDisk > service.DiskThresholdPercent)
        {
            var over = disks?
                .Where(d => d.used > service.DiskThresholdPercent.Value)
                .OrderByDescending(d => d.used)
                .Select(d => $"{d.id} %{d.used.ToString("0.#", CultureInfo.InvariantCulture)}")
                .ToList();
            breaches.Add(over != null && over.Count > 0
                ? $"Disk {string.Join(", ", over)} (eşik %{service.DiskThresholdPercent})"
                : $"Disk %{maxDisk:0.#} (eşik %{service.DiskThresholdPercent})");
        }
        return breaches.Count > 0 ? "Eşik aşıldı: " + string.Join(", ", breaches) : null;
    }
}
