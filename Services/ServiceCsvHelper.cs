using Microsoft.EntityFrameworkCore;
using vMonitor.Data;
using vMonitor.Models;

namespace vMonitor.Services;

/// <summary>Servis CSV içe/dışa aktarım mantığı — klasik MVC (ServicesController) ve React API (ApiController)
/// aynı davranışı paylaşır (tek nokta).</summary>
public static class ServiceCsvHelper
{
    public const string Header = "Ad;Tip;Hedef;Port;Ekstra;KimlikAdi;SSL;SertifikaYoksay;AralikDk;ZamanAsimiSn;YavaslikEsigiMs;CpuEsik;RamEsik;DiskEsik;Aktif;Keyword;Aciklama;AlarmMail;AlarmSms;AlarmWhatsapp;AlarmArama";

    public static byte[] BuildExportCsv(IEnumerable<MonitoredService> services)
    {
        string B(bool v) => v ? "1" : "0";
        string Q(string? v) => "\"" + (v ?? "").Replace("\"", "\"\"") + "\"";

        var sb = new System.Text.StringBuilder();
        sb.AppendLine(Header);
        foreach (var s in services)
        {
            sb.Append(Q(s.Name)).Append(';')
              .Append(s.Type).Append(';')
              .Append(Q(s.Target)).Append(';')
              .Append(s.Port?.ToString() ?? "").Append(';')
              .Append(Q(s.Extra)).Append(';')
              .Append(Q(s.Credential?.Name)).Append(';')
              .Append(B(s.UseSsl)).Append(';')
              .Append(B(s.IgnoreCertErrors)).Append(';')
              .Append(s.IntervalMinutesOverride?.ToString() ?? "").Append(';')
              .Append(s.TimeoutSeconds).Append(';')
              .Append(s.ResponseTimeThresholdMs?.ToString() ?? "").Append(';')
              .Append(s.CpuThresholdPercent?.ToString() ?? "").Append(';')
              .Append(s.RamThresholdPercent?.ToString() ?? "").Append(';')
              .Append(s.DiskThresholdPercent?.ToString() ?? "").Append(';')
              .Append(B(s.Enabled)).Append(';')
              .Append(Q(s.Keyword)).Append(';')
              .Append(Q(s.Description)).Append(';')
              .Append(B(s.AlertMail)).Append(';')
              .Append(B(s.AlertSms)).Append(';')
              .Append(B(s.AlertWhatsapp)).Append(';')
              .Append(B(s.AlertCall))
              .AppendLine();
        }
        return System.Text.Encoding.UTF8.GetPreamble()
            .Concat(System.Text.Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
    }

    public record ImportResult(int Added, int Skipped, List<string> Errors);

    /// <summary>CSV içeriğini işleyip yeni servisleri ekler (SaveChanges çağırır). Ayraç ; , veya TAB.</summary>
    public static async Task<ImportResult> ImportAsync(AppDbContext db, string content, CancellationToken ct = default)
    {
        var lines = content.Split('\n')
            .Select(l => l.TrimEnd('\r'))
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .ToList();
        if (lines.Count < 2)
            return new ImportResult(0, 0, new List<string> { "CSV boş veya yalnızca başlık satırı içeriyor." });

        var candidates = new[] { ';', ',', '\t' };
        var sep = candidates.OrderByDescending(c => lines[0].Count(ch => ch == c)).First();
        if (lines[0].Count(ch => ch == sep) == 0)
            return new ImportResult(0, 0, new List<string>
            { "Dosyada ayraç bulunamadı (; , veya sekme). Excel'den kaydederken \"CSV UTF-8\" veya \"Metin (Sekme ayrılmış)\" türünü seçin." });

        var credentials = await db.Credentials.AsNoTracking().ToListAsync(ct);
        var existingNames = (await db.Services.AsNoTracking().Select(s => s.Name).ToListAsync(ct))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        int added = 0, skipped = 0;
        var errors = new List<string>();

        for (int i = 1; i < lines.Count; i++)
        {
            var rowNo = i + 1;
            var cols = lines[i].Split(sep);
            string Col(int idx) => idx < cols.Length ? cols[idx].Trim().Trim('"') : "";

            try
            {
                var name = Col(0);
                var typeText = Col(1);
                var target = Col(2);

                if (string.IsNullOrWhiteSpace(name)) { errors.Add($"Satır {rowNo}: Ad boş."); skipped++; continue; }
                if (existingNames.Contains(name)) { errors.Add($"Satır {rowNo}: '{name}' zaten mevcut, atlandı."); skipped++; continue; }
                if (!Enum.TryParse<ServiceType>(typeText, true, out var type))
                { errors.Add($"Satır {rowNo}: Geçersiz tip '{typeText}'."); skipped++; continue; }
                if (string.IsNullOrWhiteSpace(target)) { errors.Add($"Satır {rowNo}: Hedef boş."); skipped++; continue; }

                int? credId = null;
                var credName = Col(5);
                if (!string.IsNullOrWhiteSpace(credName))
                {
                    var cred = credentials.FirstOrDefault(c => string.Equals(c.Name, credName, StringComparison.OrdinalIgnoreCase));
                    if (cred == null)
                    { errors.Add($"Satır {rowNo}: '{credName}' adlı kimlik bilgisi bulunamadı (önce Kimlik Bilgileri'nden ekleyin)."); skipped++; continue; }
                    credId = cred.Id;
                }

                static bool ParseBool(string v) =>
                    v is "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase) || v.Equals("evet", StringComparison.OrdinalIgnoreCase);
                static int? ParseInt(string v) => int.TryParse(v, out var n) ? n : null;

                db.Services.Add(new MonitoredService
                {
                    Name = name,
                    Type = type,
                    Target = target,
                    Port = ParseInt(Col(3)),
                    Extra = string.IsNullOrWhiteSpace(Col(4)) ? null : Col(4),
                    CredentialId = credId,
                    UseSsl = ParseBool(Col(6)),
                    IgnoreCertErrors = string.IsNullOrWhiteSpace(Col(7)) || ParseBool(Col(7)),
                    IntervalMinutesOverride = ParseInt(Col(8)),
                    TimeoutSeconds = ParseInt(Col(9)) ?? 15,
                    ResponseTimeThresholdMs = ParseInt(Col(10)),
                    CpuThresholdPercent = ParseInt(Col(11)),
                    RamThresholdPercent = ParseInt(Col(12)),
                    DiskThresholdPercent = ParseInt(Col(13)),
                    Enabled = string.IsNullOrWhiteSpace(Col(14)) || ParseBool(Col(14)),
                    Keyword = string.IsNullOrWhiteSpace(Col(15)) ? null : Col(15),
                    Description = string.IsNullOrWhiteSpace(Col(16)) ? null : Col(16),
                    AlertMail = string.IsNullOrWhiteSpace(Col(17)) || ParseBool(Col(17)),
                    AlertSms = ParseBool(Col(18)),
                    AlertWhatsapp = ParseBool(Col(19)),
                    AlertCall = ParseBool(Col(20))
                });
                existingNames.Add(name);
                added++;
            }
            catch (Exception ex)
            {
                errors.Add($"Satır {rowNo}: {ex.Message}");
                skipped++;
            }
        }

        if (added > 0) await db.SaveChangesAsync(ct);
        return new ImportResult(added, skipped, errors);
    }
}
