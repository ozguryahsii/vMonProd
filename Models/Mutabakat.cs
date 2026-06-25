namespace vMonitor.Models;

/// <summary>Mutabakat (envanter karşılaştırma) için tek bir sunucu satırı.
/// Hem bizim envanterden hem hizmet aldığımız firmanın listesinden doldurulur.</summary>
public class InvServer
{
    public string Host { get; set; } = "";       // görüntülenen ad
    public string Key { get; set; } = "";          // eşleştirme anahtarı (normalize)
    public string? Ip { get; set; }
    public double? Cpu { get; set; }
    public double? Mem { get; set; }
    public double? Disk { get; set; }
    public string? OsText { get; set; }            // bizim listede OS Ref metni
    public string? OsFamily { get; set; }          // "Windows" / "Unix" / "" (hesaplanır)
}

/// <summary>Eşleşen sunucularda tek bir alan farkı.</summary>
public class FieldDiff
{
    public string Host { get; set; } = "";
    public string Field { get; set; } = "";
    public string Ours { get; set; } = "";
    public string Theirs { get; set; } = "";
}

/// <summary>Mutabakat karşılaştırma sonucu (tamamen bellekte, kalıcı değil).</summary>
public class MutabakatResult
{
    public string OwnName { get; set; } = "Bizim Envanter";
    public string VendorName { get; set; } = "Hizmet Aldığımız Firma";
    public string OurMonth { get; set; } = "";
    public string VendorMonth { get; set; } = "";
    public string VendorPrevMonth { get; set; } = "";

    public int OurCount { get; set; }
    public int VendorCount { get; set; }
    public int VendorPrevCount { get; set; }
    public int MatchedCount { get; set; }   // bizim ↔ firma (bu ay) eşleşen sunucu sayısı

    // Karşılaştırma 1: bizim envanter ↔ firma (bu ay)
    public List<InvServer> OnlyOurs { get; set; } = new();     // bizde var, firmada yok
    public List<InvServer> OnlyVendor { get; set; } = new();   // firmada var, bizde yok
    public List<FieldDiff> FieldDiffs { get; set; } = new();   // eşleşenlerde alan farkı

    // Karşılaştırma 2: firma önceki ay ↔ firma bu ay
    public bool HasPrev { get; set; }
    public List<InvServer> VendorAdded { get; set; } = new();    // firma bu ay ekledi
    public List<InvServer> VendorRemoved { get; set; } = new();  // firma bu ay çıkardı
    public List<FieldDiff> VendorChanged { get; set; } = new();  // firma listesinde alan değişimi

    public List<string> Warnings { get; set; } = new();
}
