using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using vMonitor.Models;

namespace vMonitor.Data;

/// <summary>Sağlayıcıdan bağımsız, GÜVENLİ şema yükseltme: model'de olup veritabanında EKSİK olan kolonları ekler.
/// ASLA kolon/tablo silmez, tip değiştirmez, yeniden adlandırmaz → tasarımı gereği veri kaybı riski yoktur.
/// Yeni kolonlar NULL-able eklenir (var olan satırları bozmaz). EnsureCreated'tan SONRA çalıştırılır.
/// (Klasik EF Migrations'ın çoklu-sağlayıcı karmaşası olmadan, bu uygulamanın eklemeli değişim deseni için.)</summary>
public static class SchemaSync
{
    /// <summary>Model'de olup veritabanında EKSİK olan TABLOLARI oluşturur (yeni özellikler yeni tablo getirdiğinde).
    /// EF'in kendi migration SQL üreticisini kullanır → 5 sağlayıcıda da doğru DDL. Var olan tabloya dokunmaz.</summary>
    public static async Task EnsureTablesAsync(AppDbContext ctx, DbProviderKind provider, ILogger logger, CancellationToken ct = default)
    {
        DbConnection conn = ctx.Database.GetDbConnection();
        bool opened = false;
        try
        {
            if (conn.State != System.Data.ConnectionState.Open) { await conn.OpenAsync(ct); opened = true; }

            // Eksik tabloları tespit et
            var missing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var et in ctx.Model.GetEntityTypes())
            {
                var table = et.GetTableName();
                if (string.IsNullOrEmpty(table)) continue;
                HashSet<string> cols;
                try { cols = await GetColumnsAsync(conn, provider, table, ct); }
                catch { continue; }
                if (cols.Count == 0) missing.Add(table);
            }
            if (missing.Count == 0) return;

            var differ = ctx.GetService<IMigrationsModelDiffer>();
            var sqlGen = ctx.GetService<IMigrationsSqlGenerator>();
            var target = ctx.GetService<IDesignTimeModel>().Model.GetRelationalModel();
            var ops = differ.GetDifferences(null, target);
            var createOps = ops.OfType<CreateTableOperation>()
                .Where(o => missing.Contains(o.Name))
                .Cast<MigrationOperation>().ToList();
            if (createOps.Count == 0) return;

            foreach (var cmd in sqlGen.Generate(createOps, ctx.Model))
            {
                try
                {
                    await ctx.Database.ExecuteSqlRawAsync(cmd.CommandText, ct);
                    logger.LogInformation("Şema senkron: tablo oluşturuldu/komut çalıştı");
                }
                catch (Exception ex) { logger.LogDebug(ex, "Tablo oluşturma adımı atlandı"); }
            }
        }
        catch (Exception ex) { logger.LogError(ex, "Tablo senkronu başarısız (atlandı)."); }
        finally { if (opened) try { await conn.CloseAsync(); } catch { } }
    }

    public static async Task EnsureColumnsAsync(AppDbContext ctx, DbProviderKind provider, ILogger logger, CancellationToken ct = default)
    {
        DbConnection conn = ctx.Database.GetDbConnection();
        bool opened = false;
        try
        {
            if (conn.State != System.Data.ConnectionState.Open) { await conn.OpenAsync(ct); opened = true; }

            foreach (var et in ctx.Model.GetEntityTypes())
            {
                var table = et.GetTableName();
                if (string.IsNullOrEmpty(table)) continue;
                var schema = et.GetSchema();
                var storeObj = StoreObjectIdentifier.Table(table, schema);

                HashSet<string> existing;
                try { existing = await GetColumnsAsync(conn, provider, table, ct); }
                catch (Exception ex) { logger.LogDebug(ex, "Şema senkron: '{T}' kolonları okunamadı (atlandı)", table); continue; }
                if (existing.Count == 0) continue; // tablo yok/erişilemedi → EnsureCreated'a bırak

                foreach (var prop in et.GetProperties())
                {
                    var col = prop.GetColumnName(storeObj);
                    if (string.IsNullOrEmpty(col) || existing.Contains(col)) continue;
                    var type = prop.GetColumnType();
                    if (string.IsNullOrEmpty(type)) continue;
                    var sql = BuildAddColumn(provider, table, schema, col, type);
                    try
                    {
                        await ctx.Database.ExecuteSqlRawAsync(sql, ct);
                        logger.LogInformation("Şema senkron: {Table}.{Col} eklendi ({Type})", table, col, type);
                    }
                    catch (Exception ex) { logger.LogDebug(ex, "Şema senkron adımı atlandı: {Sql}", sql); }
                }
            }
        }
        catch (Exception ex) { logger.LogError(ex, "Şema senkronu başarısız (atlandı)."); }
        finally { if (opened) try { await conn.CloseAsync(); } catch { } }
    }

    /// <summary>Non-nullable (bool/int/string vb.) kolonlardaki NULL değerleri varsayılanla DOLDURUR.
    /// Eski bir DB geri yüklenip yeni kolonlar NULL eklendiğinde, EF non-nullable bir değeri NULL okuyamayıp
    /// çöker ("The data is NULL at ordinal N"). Bu pas her açılışta o NULL'ları güvenle düzeltir (idempotent).</summary>
    public static async Task BackfillNonNullableAsync(AppDbContext ctx, DbProviderKind provider, ILogger logger, CancellationToken ct = default)
    {
        DbConnection conn = ctx.Database.GetDbConnection();
        bool opened = false;
        try
        {
            if (conn.State != System.Data.ConnectionState.Open) { await conn.OpenAsync(ct); opened = true; }

            foreach (var et in ctx.Model.GetEntityTypes())
            {
                var table = et.GetTableName();
                if (string.IsNullOrEmpty(table)) continue;
                var storeObj = StoreObjectIdentifier.Table(table, et.GetSchema());

                HashSet<string> existing;
                try { existing = await GetColumnsAsync(conn, provider, table, ct); }
                catch { continue; }
                if (existing.Count == 0) continue;

                foreach (var prop in et.GetProperties())
                {
                    if (prop.IsNullable || prop.IsPrimaryKey()) continue;     // yalnız zorunlu (non-null) alanlar
                    var col = prop.GetColumnName(storeObj);
                    if (string.IsNullOrEmpty(col) || !existing.Contains(col)) continue;
                    var def = DefaultLiteral(prop.ClrType, provider);
                    if (def == null) continue;
                    var sql = $"UPDATE {QuoteIdent(provider, table)} SET {QuoteIdent(provider, col)} = {def} WHERE {QuoteIdent(provider, col)} IS NULL";
                    try
                    {
                        var n = await ctx.Database.ExecuteSqlRawAsync(sql, ct);
                        if (n > 0) logger.LogWarning("Şema iyileştirme: {Table}.{Col} → {N} NULL satır varsayılanla dolduruldu.", table, col, n);
                    }
                    catch (Exception ex) { logger.LogDebug(ex, "Backfill adımı atlandı: {Sql}", sql); }
                }
            }
        }
        catch (Exception ex) { logger.LogError(ex, "NULL backfill başarısız (atlandı)."); }
        finally { if (opened) try { await conn.CloseAsync(); } catch { } }
    }

    private static string QuoteIdent(DbProviderKind p, string id) => p switch
    {
        DbProviderKind.MySql => $"`{id}`",
        DbProviderKind.SqlServer => $"[{id}]",
        _ => $"\"{id}\""
    };

    private static string? DefaultLiteral(Type t, DbProviderKind p)
    {
        t = Nullable.GetUnderlyingType(t) ?? t;
        if (t.IsEnum) return "0";
        if (t == typeof(bool) || t == typeof(byte) || t == typeof(sbyte) || t == typeof(short) || t == typeof(ushort)
            || t == typeof(int) || t == typeof(uint) || t == typeof(long) || t == typeof(ulong)
            || t == typeof(double) || t == typeof(float) || t == typeof(decimal)) return "0";
        if (t == typeof(string)) return "''";
        if (t == typeof(DateTime) || t == typeof(DateTimeOffset))
            return p switch
            {
                DbProviderKind.SqlServer => "'1970-01-01T00:00:00'",
                DbProviderKind.Oracle => "TIMESTAMP '1970-01-01 00:00:00'",
                _ => "'1970-01-01 00:00:00'"
            };
        return null; // Guid vb. → atla
    }

    /// <summary>Performans için sık taranan zaman-serisi tablolarına indeks ekler (yoksa). İşlevsellik değişmez,
    /// yalnız sorgular hızlanır. Var olan indeks hatası yutulur (idempotent).</summary>
    public static async Task EnsureIndexesAsync(AppDbContext ctx, DbProviderKind provider, ILogger logger, CancellationToken ct = default)
    {
        var idx = new (string Name, string Table, string Cols)[]
        {
            ("IX_HealthMetrics_Svc_Chk", "HealthMetrics", "ServiceId, CheckedAt"),
            ("IX_HealthMetrics_Chk",     "HealthMetrics", "CheckedAt"),
            ("IX_CheckResults_Svc_Chk",  "CheckResults",  "ServiceId, CheckedAt"),
            ("IX_CheckResults_Chk",      "CheckResults",  "CheckedAt"),
            ("IX_Outages_Svc_Start",     "Outages",       "ServiceId, StartedAt"),
        };
        string Q(string id) => provider switch { DbProviderKind.MySql => $"`{id}`", DbProviderKind.SqlServer => $"[{id}]", _ => $"\"{id}\"" };
        foreach (var (name, table, cols) in idx)
        {
            var colList = string.Join(", ", cols.Split(',', StringSplitOptions.TrimEntries).Select(Q));
            var ifne = provider is DbProviderKind.Sqlite or DbProviderKind.PostgreSql ? "IF NOT EXISTS " : "";
            var sql = $"CREATE INDEX {ifne}{Q(name)} ON {Q(table)} ({colList})";
            try { await ctx.Database.ExecuteSqlRawAsync(sql, ct); }
            catch (Exception ex) { logger.LogDebug(ex, "İndeks atlandı (muhtemelen zaten var): {Name}", name); }
        }
    }

    private static async Task<HashSet<string>> GetColumnsAsync(DbConnection conn, DbProviderKind p, string table, CancellationToken ct)
    {
        // Tablo adı modelden gelir (kullanıcı girdisi değil) → gömülü kullanımı güvenli.
        string q = p switch
        {
            DbProviderKind.Sqlite => $"SELECT name FROM pragma_table_info('{table}')",
            DbProviderKind.SqlServer => $"SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = '{table}'",
            DbProviderKind.PostgreSql => $"SELECT column_name FROM information_schema.columns WHERE table_name = '{table}'",
            DbProviderKind.MySql => $"SELECT COLUMN_NAME FROM information_schema.COLUMNS WHERE TABLE_NAME = '{table}' AND TABLE_SCHEMA = DATABASE()",
            DbProviderKind.Oracle => $"SELECT COLUMN_NAME FROM USER_TAB_COLUMNS WHERE TABLE_NAME = '{table}'",
            _ => throw new NotSupportedException()
        };
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = q;
        using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) if (!r.IsDBNull(0)) set.Add(r.GetString(0));
        return set;
    }

    private static string BuildAddColumn(DbProviderKind p, string table, string? schema, string col, string type)
    {
        string Q(string id) => p switch
        {
            DbProviderKind.MySql => $"`{id}`",
            DbProviderKind.SqlServer => $"[{id}]",
            _ => $"\"{id}\""
        };
        string t = string.IsNullOrEmpty(schema) ? Q(table) : $"{Q(schema)}.{Q(table)}";
        // Yeni kolon her zaman NULL-able (var olan satırları bozmamak için)
        return p switch
        {
            DbProviderKind.SqlServer => $"ALTER TABLE {t} ADD {Q(col)} {type} NULL",
            DbProviderKind.Oracle => $"ALTER TABLE {t} ADD ({Q(col)} {type} NULL)",
            _ => $"ALTER TABLE {t} ADD COLUMN {Q(col)} {type} NULL"
        };
    }
}
