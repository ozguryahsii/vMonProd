namespace vMonitor.Models;

/// <summary>Desteklenen veritabanı sağlayıcıları (kurulum sihirbazında seçilir).</summary>
public enum DbProviderKind
{
    Sqlite = 0,
    SqlServer = 1,
    PostgreSql = 2,
    MySql = 3,
    Oracle = 4
}
