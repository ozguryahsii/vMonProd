using System.Globalization;
using Renci.SshNet;
using vMonitor.Models;

namespace vMonitor.Services.Checkers;

/// <summary>Linux (Oracle Linux vb.) sunucu sağlığı: SSH ile bağlanıp
/// /proc/stat (CPU), free (RAM) ve df (disk) okur. Root gerekmez.</summary>
public class LinuxHealthChecker : CheckerBase
{
    public override ServiceType Type => ServiceType.LinuxHealth;

    protected override Task<string?> ExecuteAsync(MonitoredService service, Credential? credential, CancellationToken ct)
    {
        if (credential == null) return Task.FromResult<string?>("Kimlik bilgisi tanımlı değil");

        return Task.Run<string?>(() =>
        {
            var username = PlainUsername(credential);
            var connInfo = new Renci.SshNet.ConnectionInfo(
                service.Target,
                service.Port ?? 22,
                username,
                new PasswordAuthenticationMethod(username, PlainPassword(credential)))
            {
                Timeout = TimeSpan.FromSeconds(service.TimeoutSeconds)
            };

            using var ssh = new SshClient(connInfo);
            ssh.Connect();
            try
            {
                // CPU: /proc/stat'i 1 sn arayla iki kez oku, delta üzerinden kullanım hesapla
                var cpuRaw = Run(ssh, "grep -w '^cpu' /proc/stat; sleep 1; grep -w '^cpu' /proc/stat");
                double? cpu = ParseCpu(cpuRaw);

                // RAM: total ve available (modern free çıktısı, sütun 2 ve 7)
                var ramRaw = Run(ssh, "free -b | awk 'NR==2{print $2\" \"$7}'");
                double? ram = ParseRam(ramRaw);
                double? totalRamBytes = ParseFirstNumber(ramRaw);

                // Çekirdek sayısı
                var coresRaw = Run(ssh, "nproc 2>/dev/null").Trim();
                int? cores = int.TryParse(coresRaw, out var c) ? c : null;

                // Diskler: mount, kullanım% ve toplam bayt (kalıcı dosya sistemleri; tmpfs vb. hariç)
                var diskRaw = Run(ssh, "df -P -B1 -x tmpfs -x devtmpfs -x overlay -x squashfs 2>/dev/null | awk 'NR>1{print $6\" \"$5\" \"$2}'");
                var (maxDisk, diskDetail, diskCapacities, disks) = ParseDisks(diskRaw);

                var capacityParts = new List<string>();
                if (cores.HasValue) capacityParts.Add($"{cores} CPU");
                if (totalRamBytes is > 0) capacityParts.Add($"{Math.Round(totalRamBytes.Value / 1073741824.0):0} GB RAM");
                capacityParts.AddRange(diskCapacities);

                CollectedMetrics = new HealthMetricsData(cpu, ram, maxDisk, diskDetail,
                    capacityParts.Count > 0 ? string.Join(" · ", capacityParts) : null);
                var thresholdErr = WindowsHealthChecker.BuildThresholdError(service, cpu, ram, maxDisk, disks);
                IsThresholdError = thresholdErr != null;
                return thresholdErr;
            }
            finally
            {
                ssh.Disconnect();
            }
        }, ct);
    }

    private static string Run(SshClient ssh, string command)
    {
        using var cmd = ssh.CreateCommand(command);
        return cmd.Execute() ?? "";
    }

    private static double? ParseCpu(string twoLines)
    {
        // Her satır: cpu user nice system idle iowait irq softirq steal ...
        var lines = twoLines.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length < 2) return null;
        var a = ParseStatLine(lines[0]);
        var b = ParseStatLine(lines[^1]);
        if (a == null || b == null) return null;
        var totalDelta = b.Value.total - a.Value.total;
        var idleDelta = b.Value.idle - a.Value.idle;
        if (totalDelta <= 0) return null;
        return Math.Round(100.0 * (totalDelta - idleDelta) / totalDelta, 1);
    }

    private static (double total, double idle)? ParseStatLine(string line)
    {
        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 5 || parts[0] != "cpu") return null;
        var values = parts.Skip(1)
            .Select(p => double.TryParse(p, NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : 0)
            .ToArray();
        var total = values.Sum();
        var idle = values[3] + (values.Length > 4 ? values[4] : 0); // idle + iowait
        return (total, idle);
    }

    private static double? ParseRam(string line)
    {
        var parts = line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return null;
        if (!double.TryParse(parts[0], NumberStyles.Any, CultureInfo.InvariantCulture, out var total) || total <= 0) return null;
        if (!double.TryParse(parts[1], NumberStyles.Any, CultureInfo.InvariantCulture, out var available)) return null;
        return Math.Round(100.0 * (total - available) / total, 1);
    }

    private static double? ParseFirstNumber(string line)
    {
        var parts = line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 0 && double.TryParse(parts[0], NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : null;
    }

    private static (double? maxDisk, string? detail, List<string> capacities, List<(string id, double used)> disks) ParseDisks(string raw)
    {
        double? max = null;
        var parts = new List<string>();
        var capacities = new List<string>();
        var disks = new List<(string id, double used)>();
        foreach (var line in raw.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            // "mount kullanım% toplamBayt" — örn. "/ 82% 53687091200"
            var cols = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (cols.Length < 2) continue;
            var pctText = cols[1].TrimEnd('%');
            if (!double.TryParse(pctText, NumberStyles.Any, CultureInfo.InvariantCulture, out var pct)) continue;
            parts.Add($"{cols[0]} %{pct.ToString("0.#", CultureInfo.InvariantCulture)}");
            if (cols.Length >= 3 && double.TryParse(cols[2], NumberStyles.Any, CultureInfo.InvariantCulture, out var totalBytes) && totalBytes > 0)
                capacities.Add($"{cols[0]} {WindowsHealthChecker.FormatGb(totalBytes)}");
            disks.Add((cols[0], pct));
            if (max == null || pct > max) max = pct;
        }
        return (max, parts.Count > 0 ? string.Join(" · ", parts) : null, capacities, disks);
    }
}
