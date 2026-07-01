using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using vMonitor.Models;

namespace vMonitor.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    // UTC sakla / UTC oku: yazarken yerel/belirsiz değer UTC'ye çevrilir, okurken Kind=Utc işaretlenir.
    // Böylece tüm DB tarihleri UTC tutulur; JSON 'Z' ile serialize edilir (JS yerele çevirir),
    // EF sorgu parametreleri (rapor tarih aralıkları) de otomatik UTC'ye dönüşür. Gösterim için .ToLocalTime().
    private static readonly ValueConverter<DateTime, DateTime> UtcConverter = new(
        v => v.Kind == DateTimeKind.Utc ? v : v.ToUniversalTime(),
        v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
    private static readonly ValueConverter<DateTime?, DateTime?> UtcNullableConverter = new(
        v => v.HasValue ? (v.Value.Kind == DateTimeKind.Utc ? v.Value : v.Value.ToUniversalTime()) : v,
        v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : v);

    // Oracle boş string ''yi NULL olarak saklar → NOT NULL string kolonlara "" yazınca ORA-01400.
    // Yaz: "" → " " (Oracle non-null saklar), Oku: " " → "" → uygulama daima "" görür (NOT NULL korunur, null-crash yok).
    // Yalnız Oracle'da uygulanır; diğer sağlayıcılarda "" olduğu gibi kalır.
    private static readonly ValueConverter<string, string> OracleEmptyStringConverter = new(
        v => v == "" ? " " : v,
        v => v == " " ? "" : v);


    public DbSet<MonitoredService> Services => Set<MonitoredService>();
    public DbSet<Credential> Credentials => Set<Credential>();
    public DbSet<CheckResult> CheckResults => Set<CheckResult>();
    public DbSet<OutageRecord> Outages => Set<OutageRecord>();
    public DbSet<AppSetting> Settings => Set<AppSetting>();
    public DbSet<DashboardDef> Dashboards => Set<DashboardDef>();
    public DbSet<HealthMetric> HealthMetrics => Set<HealthMetric>();
    public DbSet<AppUser> AppUsers => Set<AppUser>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<SmsProvider> SmsProviders => Set<SmsProvider>();
    public DbSet<AlarmSession> AlarmSessions => Set<AlarmSession>();
    public DbSet<StatWidget> StatWidgets => Set<StatWidget>();

    protected override void OnModelCreating(ModelBuilder mb)
    {
        mb.Entity<CheckResult>().HasIndex(r => new { r.ServiceId, r.CheckedAt });
        mb.Entity<OutageRecord>().HasIndex(o => o.ServiceId);
        mb.Entity<HealthMetric>().HasIndex(m => new { m.ServiceId, m.CheckedAt });
        mb.Entity<AuditLog>().HasIndex(a => a.At);
        mb.Entity<MonitoredService>()
          .HasOne(s => s.Credential)
          .WithMany()
          .HasForeignKey(s => s.CredentialId)
          .OnDelete(DeleteBehavior.SetNull);

        var isOracle = Database.ProviderName?.Contains("Oracle", StringComparison.OrdinalIgnoreCase) == true;

        // Tüm DateTime alanlarına UTC dönüştürücüsü; Oracle'da string alanlara boş-string dönüştürücüsü uygula
        foreach (var entity in mb.Model.GetEntityTypes())
            foreach (var prop in entity.GetProperties())
            {
                if (prop.ClrType == typeof(DateTime)) prop.SetValueConverter(UtcConverter);
                else if (prop.ClrType == typeof(DateTime?)) prop.SetValueConverter(UtcNullableConverter);
                else if (isOracle && prop.ClrType == typeof(string)) prop.SetValueConverter(OracleEmptyStringConverter);
            }
    }
}
