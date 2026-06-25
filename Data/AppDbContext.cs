using Microsoft.EntityFrameworkCore;
using vMonitor.Models;

namespace vMonitor.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

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
    }
}
