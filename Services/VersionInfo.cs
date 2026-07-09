namespace vMonitor.Services;

/// <summary>TEK KAYNAK sürüm bilgisi. Hakkında rozeti ve self-update karşılaştırması buradan okur.
/// Sürüm çıkarken YALNIZCA burası artırılır (About.cshtml rozeti otomatik izler).</summary>
public static class VersionInfo
{
    public const string AppVersion = "v2.26.3";

    /// <summary>"v2.23.4" / "v2.23.5-pre.2" → karşılaştırılabilir dörtlü (major, minor, patch, pre).
    /// Pre içermeyen final sürüm, aynı numaralı pre'lerden BÜYÜK sayılır (pre=MaxValue).</summary>
    public static (int Major, int Minor, int Patch, int Pre) Parse(string v)
    {
        v = (v ?? "").Trim().TrimStart('v', 'V');
        int pre = int.MaxValue;
        var i = v.IndexOf("-pre.", StringComparison.OrdinalIgnoreCase);
        if (i >= 0)
        {
            pre = int.TryParse(v[(i + 5)..], out var p) ? p : 0;
            v = v[..i];
        }
        var parts = v.Split('.');
        int P(int idx) => parts.Length > idx && int.TryParse(parts[idx], out var x) ? x : 0;
        return (P(0), P(1), P(2), pre);
    }

    /// <summary>latest, current'tan daha yeni mi?</summary>
    public static bool IsNewer(string latest, string current)
    {
        var a = Parse(latest); var b = Parse(current);
        return (a.Major, a.Minor, a.Patch, a.Pre).CompareTo((b.Major, b.Minor, b.Patch, b.Pre)) > 0;
    }
}
