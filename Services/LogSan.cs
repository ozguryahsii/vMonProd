namespace vMonitor.Services;

/// <summary>Log forging önleme (CodeQL cs/log-forging): kullanıcı kaynaklı değerler loga yazılmadan
/// önce satır sonlarından arındırılır — sahte log satırı enjekte edilemez.</summary>
public static class LogSan
{
    public static string S(string? v) => (v ?? "").Replace("\r", " ").Replace("\n", " ");
}
