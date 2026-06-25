using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using vMonitor.Models;

namespace vMonitor.Services;

/// <summary>Envanter mutabakatı: CSV/XLSX dosyalarını okur, bizim envanter ile hizmet aldığımız
/// firmanın listesini ve firmanın önceki/bu ay listelerini çapraz karşılaştırır.
/// Tamamen bellekte çalışır; hiçbir veri kalıcı saklanmaz, uygulamanın geri kalanından bağımsızdır.</summary>
public class MutabakatService
{
    // ---- Dosya okuma (CSV + XLSX) ----

    /// <summary>Dosyayı satır dizilerine çevirir. İlk satır başlık kabul edilir.</summary>
    public static (string[] headers, List<string[]> rows) ReadTable(Stream stream, string fileName)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        var all = ext == ".xlsx" ? ReadXlsx(stream) : ReadCsv(stream);
        if (all.Count == 0) return (Array.Empty<string>(), new List<string[]>());
        var headers = all[0];
        return (headers, all.Skip(1).ToList());
    }

    private static List<string[]> ReadCsv(Stream stream)
    {
        string text;
        using (var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
            text = reader.ReadToEnd();
        if (string.IsNullOrWhiteSpace(text)) return new();

        // Ayraç tespiti (ilk satıra göre): ; , veya TAB
        var firstLine = text.Split('\n')[0];
        char delim = new[] { ';', ',', '\t' }.OrderByDescending(c => firstLine.Count(ch => ch == c)).First();

        var rows = new List<string[]>();
        var cur = new List<string>();
        var sb = new StringBuilder();
        bool inQuotes = false;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"') { sb.Append('"'); i++; }
                    else inQuotes = false;
                }
                else sb.Append(c);
            }
            else if (c == '"') inQuotes = true;
            else if (c == delim) { cur.Add(sb.ToString()); sb.Clear(); }
            else if (c == '\r') { /* atla */ }
            else if (c == '\n')
            {
                cur.Add(sb.ToString()); sb.Clear();
                if (cur.Any(x => x.Length > 0) || cur.Count > 1) rows.Add(cur.ToArray());
                cur = new List<string>();
            }
            else sb.Append(c);
        }
        if (sb.Length > 0 || cur.Count > 0) { cur.Add(sb.ToString()); if (cur.Any(x => x.Length > 0)) rows.Add(cur.ToArray()); }
        return rows;
    }

    private static List<string[]> ReadXlsx(Stream stream)
    {
        // .xlsx = OOXML zip. Ek bağımlılık olmadan System.IO.Compression + Xml ile okunur.
        var result = new List<string[]>();
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        // Paylaşılan dizgeler
        var shared = new List<string>();
        var sstEntry = zip.GetEntry("xl/sharedStrings.xml");
        if (sstEntry != null)
        {
            using var s = sstEntry.Open();
            var doc = XDocument.Load(s);
            foreach (var si in doc.Root!.Elements(ns + "si"))
                shared.Add(string.Concat(si.Descendants(ns + "t").Select(t => t.Value)));
        }

        // İlk GÖRÜNÜR çalışma sayfasını workbook sırasına göre çöz (fiziksel sheet1.xml her zaman
        // ilk/veri sayfası olmayabilir; gizli sayfalar atlanır).
        var sheetEntry = ResolveFirstSheet(zip, ns)
            ?? zip.GetEntry("xl/worksheets/sheet1.xml")
            ?? zip.Entries.FirstOrDefault(e => e.FullName.StartsWith("xl/worksheets/sheet", StringComparison.OrdinalIgnoreCase));
        if (sheetEntry == null) return result;

        using var ss = sheetEntry.Open();
        var sheet = XDocument.Load(ss);
        var data = sheet.Root!.Element(ns + "sheetData");
        if (data == null) return result;

        int maxCols = 0;
        var temp = new List<Dictionary<int, string>>();
        foreach (var row in data.Elements(ns + "row"))
        {
            var cells = new Dictionary<int, string>();
            foreach (var c in row.Elements(ns + "c"))
            {
                var reference = (string?)c.Attribute("r") ?? "";
                int col = ColIndex(reference);
                string val;
                var t = (string?)c.Attribute("t");
                if (t == "s")
                {
                    var idx = c.Element(ns + "v")?.Value;
                    val = int.TryParse(idx, out var si2) && si2 >= 0 && si2 < shared.Count ? shared[si2] : "";
                }
                else if (t == "inlineStr")
                    val = string.Concat(c.Descendants(ns + "t").Select(x => x.Value));
                else
                    val = c.Element(ns + "v")?.Value ?? "";
                if (col >= 0) { cells[col] = val; if (col + 1 > maxCols) maxCols = col + 1; }
            }
            temp.Add(cells);
        }
        foreach (var cells in temp)
        {
            var arr = new string[maxCols];
            for (int i = 0; i < maxCols; i++) arr[i] = cells.TryGetValue(i, out var v) ? v : "";
            result.Add(arr);
        }
        return result;
    }

    /// <summary>workbook.xml + rels'ten ilk görünür sayfanın xml dosyasını bulur.</summary>
    private static ZipArchiveEntry? ResolveFirstSheet(ZipArchive zip, XNamespace ns)
    {
        try
        {
            var wbEntry = zip.GetEntry("xl/workbook.xml");
            var relsEntry = zip.GetEntry("xl/_rels/workbook.xml.rels");
            if (wbEntry == null || relsEntry == null) return null;

            XNamespace pr = "http://schemas.openxmlformats.org/package/2006/relationships";
            XNamespace rNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

            Dictionary<string, string> rels;
            using (var rs = relsEntry.Open())
                rels = XDocument.Load(rs).Root!.Elements(pr + "Relationship")
                    .ToDictionary(e => (string)e.Attribute("Id")!, e => (string)e.Attribute("Target")!);

            XElement sheets;
            using (var ws = wbEntry.Open())
                sheets = XDocument.Load(ws).Root!.Element(ns + "sheets")!;
            if (sheets == null) return null;

            var first = sheets.Elements(ns + "sheet")
                .FirstOrDefault(s => { var st = (string?)s.Attribute("state"); return string.IsNullOrEmpty(st) || st == "visible"; })
                ?? sheets.Elements(ns + "sheet").FirstOrDefault();
            if (first == null) return null;

            var rid = (string?)first.Attribute(rNs + "id");
            if (rid == null || !rels.TryGetValue(rid, out var target)) return null;

            target = target.Replace('\\', '/').TrimStart('/');
            if (!target.StartsWith("xl/", StringComparison.OrdinalIgnoreCase)) target = "xl/" + target;
            return zip.GetEntry(target);
        }
        catch { return null; }
    }

    private static int ColIndex(string cellRef)
    {
        int i = 0;
        foreach (char ch in cellRef)
        {
            if (char.IsLetter(ch)) i = i * 26 + (char.ToUpperInvariant(ch) - 'A' + 1);
            else break;
        }
        return i - 1;
    }

    // ---- Sütun eşleştirme ----

    private static string Norm(string s) => Regex.Replace((s ?? "").Trim().ToLowerInvariant(), @"\s+", " ");

    /// <summary>Başlıklar içinde önce tam (normalize) eşleşme, sonra "içeren" eşleşme arar.</summary>
    private static int Col(string[] headers, params string[] candidates)
    {
        for (int i = 0; i < headers.Length; i++)
            foreach (var cand in candidates)
                if (Norm(headers[i]) == Norm(cand)) return i;
        for (int i = 0; i < headers.Length; i++)
            foreach (var cand in candidates)
                if (Norm(headers[i]).Contains(Norm(cand))) return i;
        return -1;
    }

    private static string Cell(string[] row, int idx) => idx >= 0 && idx < row.Length ? (row[idx] ?? "").Trim() : "";

    private static double? Num(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var m = Regex.Match(s.Replace(",", "."), @"-?\d+(\.\d+)?");
        return m.Success && double.TryParse(m.Value, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : null;
    }

    private static string Key(string host)
    {
        var h = (host ?? "").Trim().ToLowerInvariant();
        var dot = h.IndexOf('.');
        if (dot > 0) h = h[..dot];                 // FQDN ise ilk etiket (domain'i yok say)
        // Yalnızca harf/rakam/-/_ bırak: boşluk, sekme, BOM, görünmez karakter vb. temizlenir
        return Regex.Replace(h, @"[^a-z0-9_\-]", "");
    }

    private static string OsFamilyFromText(string? os)
    {
        var t = (os ?? "").ToLowerInvariant();
        if (t.Length == 0) return "";
        if (t.Contains("windows") || t.Contains("microsoft") || Regex.IsMatch(t, @"\bwin\b")) return "Windows";
        if (Regex.IsMatch(t, @"linux|unix|red\s*hat|redhat|cent\s*os|ubuntu|suse|oracle linux|aix|solaris|debian|rocky|alma")) return "Unix";
        return "";
    }

    private static bool Flag(string s) => s.Trim() is "1" or "1.0" || s.Trim().Equals("evet", StringComparison.OrdinalIgnoreCase)
        || s.Trim().Equals("x", StringComparison.OrdinalIgnoreCase) || s.Trim().Equals("true", StringComparison.OrdinalIgnoreCase);

    // ---- Ayrıştırma ----

    /// <summary>Bizim envanter dosyasını sunucu listesine çevirir.</summary>
    public static List<InvServer> ParseOur(Stream stream, string fileName)
    {
        var (h, rows) = ReadTable(stream, fileName);
        int cHost = Col(h, "Hostname"), cIp = Col(h, "IP Adresi", "IP"), cCpu = Col(h, "vCPU", "CPU"),
            cMem = Col(h, "RAM (GB)", "RAM"), cDisk = Col(h, "Disk (GB)", "Disk"), cOs = Col(h, "OS Ref", "İşletim Sistemi");
        var list = new List<InvServer>();
        foreach (var r in rows)
        {
            var host = Cell(r, cHost);
            if (string.IsNullOrWhiteSpace(host)) continue;
            var os = Cell(r, cOs);
            list.Add(new InvServer
            {
                Host = host, Key = Key(host), Ip = Cell(r, cIp),
                Cpu = Num(Cell(r, cCpu)), Mem = Num(Cell(r, cMem)), Disk = Num(Cell(r, cDisk)),
                OsText = os, OsFamily = OsFamilyFromText(os)
            });
        }
        return list;
    }

    /// <summary>Hizmet aldığımız firmanın listesini sunucu listesine çevirir.</summary>
    public static List<InvServer> ParseVendor(Stream stream, string fileName)
    {
        var (h, rows) = ReadTable(stream, fileName);
        int cHost = Col(h, "Envanter", "Hostname"), cIp = Col(h, "IP"),
            cCpu = Col(h, "Ks Cpu", "KS CPU", "CPU"), cMem = Col(h, "Ks Memory", "KS Memory", "Memory"),
            cDisk = Col(h, "Ks Storage Usage", "Storage Usage", "Storage"),
            cWin = Col(h, "İşletim Sistemi Yönetimi-Microsoft", "Sistem Yönetimi-Microsoft", "Microsoft"),
            cUnix = Col(h, "İşletim Sistemi Yönetimi-Unix", "Sistem Yönetimi-Unix", "Unix");
        var list = new List<InvServer>();
        foreach (var r in rows)
        {
            var host = Cell(r, cHost);
            if (string.IsNullOrWhiteSpace(host)) continue;
            bool win = cWin >= 0 && Flag(Cell(r, cWin));
            bool unix = cUnix >= 0 && Flag(Cell(r, cUnix));
            list.Add(new InvServer
            {
                Host = host, Key = Key(host), Ip = Cell(r, cIp),
                Cpu = Num(Cell(r, cCpu)), Mem = Num(Cell(r, cMem)), Disk = Num(Cell(r, cDisk)),
                OsFamily = win && unix ? "Çoklu" : win ? "Windows" : unix ? "Unix" : ""
            });
        }
        return list;
    }

    // ---- Karşılaştırma ----

    private static bool NumDiffer(double? a, double? b)
    {
        if (a == null && b == null) return false;
        if (a == null || b == null) return true;
        return Math.Abs(Math.Round(a.Value) - Math.Round(b.Value)) >= 0.5;
    }
    private static string Fmt(double? v) => v.HasValue ? v.Value.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) : "(yok)";
    private static string FmtS(string? s) => string.IsNullOrWhiteSpace(s) ? "(yok)" : s.Trim();

    /// <summary>Bizim envanter ↔ firma (bu ay) ve firma önceki ay ↔ bu ay karşılaştırması.</summary>
    public MutabakatResult Compare(List<InvServer> ours, List<InvServer> vendor, List<InvServer>? vendorPrev,
        string ownName, string vendorName, string ourMonth, string vendorMonth, string vendorPrevMonth)
    {
        var res = new MutabakatResult
        {
            OwnName = string.IsNullOrWhiteSpace(ownName) ? "Bizim Envanter" : ownName,
            VendorName = string.IsNullOrWhiteSpace(vendorName) ? "Hizmet Aldığımız Firma" : vendorName,
            OurMonth = ourMonth, VendorMonth = vendorMonth, VendorPrevMonth = vendorPrevMonth,
            OurCount = ours.Count, VendorCount = vendor.Count
        };

        var ourMap = ToMap(ours, res.Warnings, res.OwnName);
        var venMap = ToMap(vendor, res.Warnings, res.VendorName);

        // 1) Bizde var / firmada yok ve tersi
        res.OnlyOurs = ourMap.Where(kv => !venMap.ContainsKey(kv.Key)).Select(kv => kv.Value).OrderBy(s => s.Host).ToList();
        res.OnlyVendor = venMap.Where(kv => !ourMap.ContainsKey(kv.Key)).Select(kv => kv.Value).OrderBy(s => s.Host).ToList();
        res.MatchedCount = ourMap.Keys.Count(k => venMap.ContainsKey(k));

        // Teşhis: hiç eşleşme yoksa örnek anahtarları göster (ad/format farkını anında görmek için)
        if (res.MatchedCount == 0 && ourMap.Count > 0 && venMap.Count > 0)
            res.Warnings.Add($"Hiç sunucu eşleşmedi. Örnek eşleştirme anahtarları — {res.OwnName}: [{string.Join(", ", ourMap.Keys.Take(5))}] · {res.VendorName}: [{string.Join(", ", venMap.Keys.Take(5))}]");

        // 1b) Eşleşenlerde alan farkları
        foreach (var kv in ourMap)
        {
            if (!venMap.TryGetValue(kv.Key, out var v)) continue;
            var o = kv.Value;
            AddDiffs(res.FieldDiffs, o.Host, o, v, includeOs: true, osCompareText: true);
        }

        // 2) Firma önceki ay ↔ bu ay
        if (vendorPrev != null && vendorPrev.Count > 0)
        {
            res.HasPrev = true;
            res.VendorPrevCount = vendorPrev.Count;
            var prevMap = ToMap(vendorPrev, res.Warnings, res.VendorName + " (önceki)");
            res.VendorAdded = venMap.Where(kv => !prevMap.ContainsKey(kv.Key)).Select(kv => kv.Value).OrderBy(s => s.Host).ToList();
            res.VendorRemoved = prevMap.Where(kv => !venMap.ContainsKey(kv.Key)).Select(kv => kv.Value).OrderBy(s => s.Host).ToList();
            foreach (var kv in prevMap)
            {
                if (!venMap.TryGetValue(kv.Key, out var now)) continue;
                AddDiffs(res.VendorChanged, kv.Value.Host, kv.Value, now, includeOs: true, osCompareText: false);
            }
        }
        return res;
    }

    private static Dictionary<string, InvServer> ToMap(List<InvServer> list, List<string> warnings, string label)
    {
        var map = new Dictionary<string, InvServer>(StringComparer.OrdinalIgnoreCase);
        int dup = 0;
        foreach (var s in list)
        {
            if (string.IsNullOrWhiteSpace(s.Key)) continue;
            if (!map.TryAdd(s.Key, s)) dup++;
        }
        if (dup > 0) warnings.Add($"{label}: {dup} tekrarlı sunucu adı atlandı (ilk kayıt kullanıldı).");
        return map;
    }

    // o = "bizim"/"önceki", v = "firma"/"bu ay"
    private static void AddDiffs(List<FieldDiff> diffs, string host, InvServer o, InvServer v, bool includeOs, bool osCompareText)
    {
        if (!string.IsNullOrWhiteSpace(o.Ip) && !string.IsNullOrWhiteSpace(v.Ip) &&
            !string.Equals(o.Ip.Trim(), v.Ip.Trim(), StringComparison.OrdinalIgnoreCase))
            diffs.Add(new FieldDiff { Host = host, Field = "IP", Ours = FmtS(o.Ip), Theirs = FmtS(v.Ip) });
        if (NumDiffer(o.Cpu, v.Cpu)) diffs.Add(new FieldDiff { Host = host, Field = "CPU", Ours = Fmt(o.Cpu), Theirs = Fmt(v.Cpu) });
        if (NumDiffer(o.Mem, v.Mem)) diffs.Add(new FieldDiff { Host = host, Field = "RAM (GB)", Ours = Fmt(o.Mem), Theirs = Fmt(v.Mem) });
        if (NumDiffer(o.Disk, v.Disk)) diffs.Add(new FieldDiff { Host = host, Field = "Disk (GB)", Ours = Fmt(o.Disk), Theirs = Fmt(v.Disk) });

        if (includeOs)
        {
            var of = o.OsFamily ?? "";
            var vf = v.OsFamily ?? "";
            if (of.Length > 0 && vf.Length > 0 && !of.Equals(vf, StringComparison.OrdinalIgnoreCase))
                diffs.Add(new FieldDiff { Host = host, Field = "İşletim Sistemi", Ours = osCompareText ? FmtS(o.OsText) + $" ({of})" : of, Theirs = vf });
            else if (osCompareText && of.Length > 0 && vf.Length == 0)
                diffs.Add(new FieldDiff { Host = host, Field = "İşletim Sistemi", Ours = FmtS(o.OsText) + $" ({of})", Theirs = "(firma OS yönetimi işaretlememiş)" });
        }
    }
}
