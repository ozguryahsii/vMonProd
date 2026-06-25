using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using vMonitor.Data;
using vMonitor.Models;

namespace vMonitor.Controllers;

public class ServicesController : MvcBase
{
    private readonly AppDbContext _db;
    private readonly vMonitor.Services.AuditService _audit;
    public ServicesController(AppDbContext db, vMonitor.Services.AuditService audit) { _db = db; _audit = audit; }

    // Tüm servis yönetim eylemleri "services.manage" yetkisi ister
    public override void OnActionExecuting(ActionExecutingContext context)
    {
        if (!Can(Perms.ServicesManage)) context.Result = Denied();
        base.OnActionExecuting(context);
    }

    public async Task<IActionResult> Index()
    {
        var services = await _db.Services.AsNoTracking()
            .Include(s => s.Credential)
            .OrderBy(s => s.Name)
            .ToListAsync();
        return View(services);
    }

    public async Task<IActionResult> Create()
    {
        await LoadCredentialsAsync();
        return View("Form", new MonitoredService());
    }

    public async Task<IActionResult> Edit(int id)
    {
        var svc = await _db.Services.FindAsync(id);
        if (svc == null) return NotFound();
        await LoadCredentialsAsync();
        return View("Form", svc);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(MonitoredService model)
    {
        ModelState.Remove(nameof(MonitoredService.Credential));

        // Servis kontrol tiplerinde servis adı (Extra) yalnızca güvenli karakterler içersin
        // (WMI/WQL ve shell komut enjeksiyon yüzeyini daraltır)
        if ((model.Type == ServiceType.WindowsServiceControl || model.Type == ServiceType.LinuxServiceControl)
            && !string.IsNullOrWhiteSpace(model.Extra)
            && !System.Text.RegularExpressions.Regex.IsMatch(model.Extra, @"^[A-Za-z0-9._@\-]+$"))
        {
            ModelState.AddModelError(nameof(model.Extra), "Servis adı yalnızca harf, rakam, nokta, tire ve alt çizgi içerebilir.");
        }

        if (!ModelState.IsValid)
        {
            await LoadCredentialsAsync();
            return View("Form", model);
        }

        var isNew = model.Id == 0;
        if (model.Id == 0)
        {
            _db.Services.Add(model);
        }
        else
        {
            var existing = await _db.Services.FindAsync(model.Id);
            if (existing == null) return NotFound();
            existing.Name = model.Name;
            existing.Type = model.Type;
            existing.Target = model.Target;
            existing.Port = model.Port;
            existing.Extra = model.Extra;
            existing.UseSsl = model.UseSsl;
            existing.IgnoreCertErrors = model.IgnoreCertErrors;
            existing.CredentialId = model.CredentialId;
            existing.Enabled = model.Enabled;
            existing.IntervalMinutesOverride = model.IntervalMinutesOverride;
            existing.ResponseTimeThresholdMs = model.ResponseTimeThresholdMs;
            existing.TimeoutSeconds = model.TimeoutSeconds;
            existing.CpuThresholdPercent = model.CpuThresholdPercent;
            existing.RamThresholdPercent = model.RamThresholdPercent;
            existing.DiskThresholdPercent = model.DiskThresholdPercent;
            existing.Keyword = string.IsNullOrWhiteSpace(model.Keyword) ? null : model.Keyword.Trim();
            existing.Description = string.IsNullOrWhiteSpace(model.Description) ? null : model.Description.Trim();
            existing.AlertMail = model.AlertMail;
            existing.AlertSms = model.AlertSms;
            existing.AlertWhatsapp = model.AlertWhatsapp;
            existing.AlertCall = model.AlertCall;
        }
        await _db.SaveChangesAsync();
        await _audit.LogAsync(isNew ? "service.create" : "service.update", model.Name, $"Tip: {model.Type}");
        TempData["Message"] = "Servis kaydedildi.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var svc = await _db.Services.FindAsync(id);
        if (svc != null)
        {
            await _db.CheckResults.Where(r => r.ServiceId == id).ExecuteDeleteAsync();
            await _db.Outages.Where(o => o.ServiceId == id).ExecuteDeleteAsync();
            await _db.HealthMetrics.Where(m => m.ServiceId == id).ExecuteDeleteAsync();
            _db.Services.Remove(svc);
            await _db.SaveChangesAsync();
            await _audit.LogAsync("service.delete", svc.Name);
            TempData["Message"] = "Servis silindi.";
        }
        return RedirectToAction(nameof(Index));
    }

    /// <summary>Seçili servisleri toplu sil (filtre + çoklu seçim sonrası).</summary>
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteMany(string ids)
    {
        var idList = (ids ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => int.TryParse(x, out _)).Select(int.Parse).Distinct().ToList();
        if (idList.Count == 0)
        {
            TempData["Error"] = "Silinecek servis seçilmedi.";
            return RedirectToAction(nameof(Index));
        }

        await _db.CheckResults.Where(r => idList.Contains(r.ServiceId)).ExecuteDeleteAsync();
        await _db.Outages.Where(o => idList.Contains(o.ServiceId)).ExecuteDeleteAsync();
        await _db.HealthMetrics.Where(m => idList.Contains(m.ServiceId)).ExecuteDeleteAsync();
        var deleted = await _db.Services.Where(s => idList.Contains(s.Id)).ExecuteDeleteAsync();

        await _audit.LogAsync("service.delete-many", null, $"{deleted} servis toplu silindi.");
        TempData["Message"] = $"{deleted} servis silindi.";
        return RedirectToAction(nameof(Index));
    }

    /// <summary>Seçili servisleri toplu düzenle: alarm kanalları ve aktiflik tek tuşla.
    /// Her alan "" (değiştirme) / "on" / "off" değeri alır.</summary>
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> EditMany(string ids, string? alertMail, string? alertSms,
        string? alertWhatsapp, string? alertCall, string? enabled,
        bool setInterval = false, int? interval = null,
        bool setSlow = false, int? slow = null,
        bool setCpu = false, int? cpu = null,
        bool setRam = false, int? ram = null,
        bool setDisk = false, int? disk = null,
        string? addKeywords = null)
    {
        var idList = (ids ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => int.TryParse(x, out _)).Select(int.Parse).Distinct().ToList();
        if (idList.Count == 0)
        {
            TempData["Error"] = "Düzenlenecek servis seçilmedi.";
            return RedirectToAction(nameof(Index));
        }

        var services = await _db.Services.Where(s => idList.Contains(s.Id)).ToListAsync();
        var changes = new List<string>();
        void Apply(string? v, string label, Action<bool> set)
        {
            if (v == "on") { set(true); changes.Add(label + "=açık"); }
            else if (v == "off") { set(false); changes.Add(label + "=kapalı"); }
        }
        // Eklenecek keyword listesi (boş değilse)
        var newKws = MonitoredService.SplitKeywords(addKeywords);
        // Sayısal alanları güvenli aralığa kırp (boş = ilgili özelliği temizle)
        int? Clamp(int? v, int min, int max) => v.HasValue ? Math.Clamp(v.Value, min, max) : (int?)null;
        var iv = Clamp(interval, 1, 1440);
        var sl = Clamp(slow, 1, 600000);
        var cp = Clamp(cpu, 1, 100);
        var rm = Clamp(ram, 1, 100);
        var dk = Clamp(disk, 1, 100);

        foreach (var s in services)
        {
            Apply(alertMail, "Mail", b => s.AlertMail = b);
            Apply(alertSms, "SMS", b => s.AlertSms = b);
            Apply(alertWhatsapp, "WhatsApp", b => s.AlertWhatsapp = b);
            Apply(alertCall, "Arama", b => s.AlertCall = b);
            Apply(enabled, "Aktif", b => s.Enabled = b);

            if (setInterval) s.IntervalMinutesOverride = iv;
            if (setSlow) s.ResponseTimeThresholdMs = sl;
            if (setCpu) s.CpuThresholdPercent = cp;
            if (setRam) s.RamThresholdPercent = rm;
            if (setDisk) s.DiskThresholdPercent = dk;

            if (newKws.Count > 0)
            {
                var existing = MonitoredService.SplitKeywords(s.Keyword);
                var merged = existing.Concat(newKws)
                    .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                s.Keyword = string.Join(", ", merged);
            }
        }
        if (setInterval) changes.Add(iv.HasValue ? $"Aralık={iv}dk" : "Aralık=global");
        if (setSlow) changes.Add(sl.HasValue ? $"Yavaşlık={sl}ms" : "Yavaşlık=kapalı");
        if (setCpu) changes.Add(cp.HasValue ? $"CPU={cp}%" : "CPU=kapalı");
        if (setRam) changes.Add(rm.HasValue ? $"RAM={rm}%" : "RAM=kapalı");
        if (setDisk) changes.Add(dk.HasValue ? $"Disk={dk}%" : "Disk=kapalı");
        if (newKws.Count > 0) changes.Add("Keyword+(" + string.Join("/", newKws) + ")");
        if (changes.Count == 0)
        {
            TempData["Error"] = "Değiştirilecek bir alan seçmediniz.";
            return RedirectToAction(nameof(Index));
        }
        await _db.SaveChangesAsync();
        await _audit.LogAsync("service.bulk-edit", null, $"{services.Count} servis: {string.Join(", ", changes.Distinct())}");
        TempData["Message"] = $"{services.Count} servis güncellendi ({string.Join(", ", changes.Distinct())}).";
        return RedirectToAction(nameof(Index));
    }

    /// <summary>Mevcut tüm servisleri import formatında CSV olarak dışa aktarır (yedek/rollback).
    /// Şifreler taşınmaz; kimlik bilgileri tanım adıyla yeniden eşleşir.</summary>
    [HttpGet]
    public async Task<IActionResult> ExportCsv()
    {
        var services = await _db.Services.AsNoTracking()
            .Include(s => s.Credential)
            .OrderBy(s => s.Name)
            .ToListAsync();

        string B(bool v) => v ? "1" : "0";
        string Q(string? v) => "\"" + (v ?? "").Replace("\"", "\"\"") + "\"";

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("Ad;Tip;Hedef;Port;Ekstra;KimlikAdi;SSL;SertifikaYoksay;AralikDk;ZamanAsimiSn;YavaslikEsigiMs;CpuEsik;RamEsik;DiskEsik;Aktif;Keyword;Aciklama;AlarmMail;AlarmSms;AlarmWhatsapp;AlarmArama");
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

        var bytes = System.Text.Encoding.UTF8.GetPreamble()
            .Concat(System.Text.Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
        var name = $"vmon-servisler-yedek_{DateTime.Now:yyyyMMdd_HHmm}.csv";
        return File(bytes, "text/csv; charset=utf-8", name);
    }

    /// <summary>Örnek CSV şablonu — başlık satırı + her tip için örnek satır.</summary>
    [HttpGet]
    public IActionResult SampleCsv()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("Ad;Tip;Hedef;Port;Ekstra;KimlikAdi;SSL;SertifikaYoksay;AralikDk;ZamanAsimiSn;YavaslikEsigiMs;CpuEsik;RamEsik;DiskEsik;Aktif;Keyword;Aciklama;AlarmMail;AlarmSms;AlarmWhatsapp;AlarmArama");
        // ...;Aktif;Keyword;Aciklama;AlarmMail;AlarmSms;AlarmWhatsapp;AlarmArama  (1=açık 0=kapalı; AlarmMail boşsa açık sayılır)
        sb.AppendLine("Intranet Anasayfa;Http;https://intranet.firma.local/health;;200;;;1;5;15;3000;;;;1;web;Kurumsal intranet portali;1;0;0;0");
        sb.AppendLine("Uretim MySQL;MySql;10.0.0.10;3306;uygulamadb;MySQL Izleme;;;5;15;;;;;1;uretim;Uretim uygulama veritabani;1;1;0;0");
        sb.AppendLine("Uretim MSSQL;MsSql;10.0.0.11;1433;master;MSSQL Izleme;;1;5;15;;;;;1;uretim;Raporlama veritabani;1;1;0;0");
        sb.AppendLine("Uretim Oracle;Oracle;10.0.0.12;1521;ORCLPDB1;Oracle ReadOnly;;;5;15;;;;;1;uretim;ERP veritabani;1;1;0;0");
        sb.AppendLine("Domain Controller LDAPS;Ldap;dc01.firma.local;636;;AD Bind Hesabi;1;1;5;15;;;;;1;altyapi;Birincil etki alani denetleyicisi;1;1;0;0");
        sb.AppendLine("DNS Sunucu;Dns;10.0.0.13;53;intranet.firma.local;;;;5;10;;;;;1;altyapi;Ic DNS sunucusu;1;0;0;0");
        sb.AppendLine("SFTP Sunucu;Sftp;10.0.0.14;22;;SFTP Izleme;;;5;15;;;;;1;dosya;Mutabakat dosya aktarim sunucusu;1;0;0;0");
        sb.AppendLine("DHCP Sunucu;DhcpWindowsService;dhcp01.firma.local;;;AD Bind Hesabi;;;5;30;;;;;1;altyapi;DHCP servisi;1;0;0;0");
        sb.AppendLine("Exchange SMTP;Smtp;mail.firma.local;25;;;;;5;15;;;;;1;mail;Giden e-posta (SMTP);1;0;0;0");
        sb.AppendLine("Exchange IMAPS;Imap;mail.firma.local;993;;;1;1;5;15;;;;;1;mail;Gelen e-posta (IMAP);1;0;0;0");
        sb.AppendLine("Switch Ping;Ping;10.0.0.1;;;;;;2;5;;;;;1;network;Omurga switch erisilebilirligi;1;0;0;0");
        sb.AppendLine("Web Sunucu TCP;Tcp;10.0.0.15;8080;;;;;5;10;;;;;1;web;Uygulama sunucusu portu;1;0;0;0");
        sb.AppendLine("App Sunucu Saglik;WindowsHealth;app01.firma.local;;;AD Bind Hesabi;;;5;30;;90;90;85;1;uretim;Uretim uygulama sunucusu (IIS);1;1;0;0");
        sb.AppendLine("Oracle Linux Saglik;LinuxHealth;10.0.0.20;22;;Linux SSH Izleme;;;5;30;;90;90;85;1;uretim;Oracle Linux uygulama sunucusu;1;1;0;0");
        // Uzaktan kontrol edilebilen servisler — Ekstra alanı = servis adı (Windows servis adı / Linux systemd birimi)
        sb.AppendLine("Print Spooler (Windows);WindowsServiceControl;app01.firma.local;;Spooler;AD Bind Hesabi;;;5;30;;;;;1;servis;Yazdirma kuyrugu servisi;1;0;0;0");
        sb.AppendLine("Crond (Linux);LinuxServiceControl;10.0.0.20;22;crond;Linux SSH Izleme;;;5;30;;;;;1;servis;Zamanlanmis gorev servisi;1;0;0;0");

        // UTF-8 BOM — Türkçe karakterler Excel'de doğru görünsün
        var bytes = System.Text.Encoding.UTF8.GetPreamble()
            .Concat(System.Text.Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
        return File(bytes, "text/csv; charset=utf-8", "vmon-ornek-servisler.csv");
    }

    /// <summary>CSV'den toplu servis ekleme. Ayraç ; veya , olabilir; ilk satır başlıktır.</summary>
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ImportCsv(IFormFile? csvFile)
    {
        if (csvFile == null || csvFile.Length == 0)
        {
            TempData["Error"] = "Dosya seçilmedi.";
            return RedirectToAction(nameof(Index));
        }

        string content;
        using (var reader = new StreamReader(csvFile.OpenReadStream(), System.Text.Encoding.UTF8, true))
            content = await reader.ReadToEndAsync();

        var lines = content.Split('\n')
            .Select(l => l.TrimEnd('\r'))
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .ToList();
        if (lines.Count < 2)
        {
            TempData["Error"] = "CSV boş veya yalnızca başlık satırı içeriyor.";
            return RedirectToAction(nameof(Index));
        }

        // Ayraç tespiti: ; , veya TAB (Excel "Metin (Sekme ayrılmış)" kaydı) — en çok geçen kazanır
        var candidates = new[] { ';', ',', '\t' };
        var sep = candidates.OrderByDescending(c => lines[0].Count(ch => ch == c)).First();
        if (lines[0].Count(ch => ch == sep) == 0)
        {
            TempData["Error"] = "Dosyada ayraç bulunamadı (; , veya sekme). Excel'den kaydederken " +
                "\"CSV UTF-8 (Virgülle ayrılmış)\" veya \"Metin (Sekme ayrılmış)\" türünü seçin.";
            return RedirectToAction(nameof(Index));
        }

        var credentials = await _db.Credentials.AsNoTracking().ToListAsync();
        var existingNames = (await _db.Services.AsNoTracking().Select(s => s.Name).ToListAsync())
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

                _db.Services.Add(new MonitoredService
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
                    AlertMail = string.IsNullOrWhiteSpace(Col(17)) || ParseBool(Col(17)),   // boşsa açık (varsayılan)
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

        if (added > 0) await _db.SaveChangesAsync();
        if (added > 0) await _audit.LogAsync("service.import", null, $"{added} servis CSV ile eklendi, {skipped} atlandı.");

        var msg = $"{added} servis eklendi" + (skipped > 0 ? $", {skipped} satır atlandı." : ".");
        if (errors.Count > 0)
            TempData["Error"] = msg + " — " + string.Join(" | ", errors.Take(8))
                + (errors.Count > 8 ? $" | ... ve {errors.Count - 8} hata daha" : "");
        else
            TempData["Message"] = msg;

        return RedirectToAction(nameof(Index));
    }

    private async Task LoadCredentialsAsync()
    {
        ViewBag.Credentials = new SelectList(
            await _db.Credentials.AsNoTracking().OrderBy(c => c.Name).ToListAsync(),
            nameof(Credential.Id), nameof(Credential.Name));
    }
}
