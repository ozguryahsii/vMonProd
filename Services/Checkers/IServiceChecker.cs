using vMonitor.Models;

namespace vMonitor.Services.Checkers;

public record HealthMetricsData(double? Cpu, double? Ram, double? MaxDisk, string? DiskDetail, string? Capacity = null);

public record CheckOutcome(bool IsUp, long ResponseTimeMs, string? Error = null, HealthMetricsData? Metrics = null, CheckStatus Status = CheckStatus.Up);

public interface IServiceChecker
{
    ServiceType Type { get; }
    Task<CheckOutcome> CheckAsync(MonitoredService service, Credential? credential, CancellationToken ct);
}

/// <summary>Ortak süre ölçümü + hata yakalama.</summary>
public abstract class CheckerBase : IServiceChecker
{
    public abstract ServiceType Type { get; }

    /// <summary>Sağlık checker'ları ExecuteAsync içinde doldurur; her CheckAsync başında sıfırlanır.
    /// (Checker'lar scoped — her kontrol kendi instance'ında çalışır.)</summary>
    protected HealthMetricsData? CollectedMetrics;

    /// <summary>Health checker'lar, ulaşıldığı halde eşik aşıldıysa true yapar → DOWN değil ERROR.</summary>
    protected bool IsThresholdError;

    public async Task<CheckOutcome> CheckAsync(MonitoredService service, Credential? credential, CancellationToken ct)
    {
        CollectedMetrics = null;
        IsThresholdError = false;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, service.TimeoutSeconds)));
        try
        {
            var error = await ExecuteAsync(service, credential, timeoutCts.Token);
            sw.Stop();
            var status = error == null ? CheckStatus.Up : (IsThresholdError ? CheckStatus.Error : CheckStatus.Down);
            return new CheckOutcome(error == null, sw.ElapsedMilliseconds, error, CollectedMetrics, status);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            sw.Stop();
            return new CheckOutcome(false, sw.ElapsedMilliseconds, $"Zaman aşımı ({service.TimeoutSeconds} sn)", null, CheckStatus.Down);
        }
        catch (Exception ex)
        {
            sw.Stop();
            var msg = ex.InnerException != null ? $"{ex.Message} — {ex.InnerException.Message}" : ex.Message;
            return new CheckOutcome(false, sw.ElapsedMilliseconds, msg, null, CheckStatus.Down);
        }
    }

    /// <summary>null = başarılı, aksi halde hata mesajı döner (veya exception fırlatır).</summary>
    protected abstract Task<string?> ExecuteAsync(MonitoredService service, Credential? credential, CancellationToken ct);

    /// <summary>Şifre çözümü: Manuel kimlikte DPAPI, Vault kimliğinde Vault'tan çekilir.</summary>
    protected static string PlainPassword(Credential? c) =>
        c == null ? "" : VaultClient.GetPassword(c);

    /// <summary>Kullanıcı adı çözümü: Vault kimliğinde secret'tan, manuelde karttan gelir.
    /// Checker'lar credential.Username yerine bunu kullanmalı.</summary>
    protected static string PlainUsername(Credential? c) =>
        c == null ? "" : VaultClient.GetUsername(c);
}
