using Microsoft.EntityFrameworkCore;
using vMonitor.Models;
using vMonitor.Services;

namespace vMonitor.Data;

/// <summary>Seçilen sağlayıcıya göre bağlantı dizisini güvenli builder'larla üretir ve DbContext'e uygular.
/// (Tek nokta — çoklu-DB ve ileride multi-tenant için.)</summary>
public static class DbProviderConfig
{
    public static int DefaultPort(DbProviderKind k) => k switch
    {
        DbProviderKind.SqlServer => 1433,
        DbProviderKind.PostgreSql => 5432,
        DbProviderKind.MySql => 3306,
        DbProviderKind.Oracle => 1521,
        _ => 0
    };

    /// <summary>Şifreyi (düz) alıp sağlayıcıya uygun bağlantı dizisi üretir. Builder kullanır (escape/injection güvenli).</summary>
    public static string BuildConnectionString(BootstrapConfig c, string password)
    {
        int port = c.Port > 0 ? c.Port : DefaultPort(c.Provider);
        switch (c.Provider)
        {
            case DbProviderKind.Sqlite:
                return $"Data Source={c.SqlitePath}";

            case DbProviderKind.SqlServer:
                // Named instance (host içinde '\' var) ile explicit port birlikte kullanılamaz;
                // SQL Server named instance'ı kendi portunu bilir → sadece host\instance yeterli.
                // Explicit port yalnızca default instance veya named instance'a sabit port atanmışsa kullan.
                var namedInstance = c.Host.Contains('\\');
                var dataSource = (c.Port > 0 && !namedInstance) ? $"{c.Host},{c.Port}" : c.Host;
                var sb = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder
                {
                    DataSource = dataSource,
                    InitialCatalog = c.Database,
                    UserID = c.Username,
                    Password = password,
                    Encrypt = c.UseSsl,
                    TrustServerCertificate = c.TrustServerCertificate,
                    ConnectTimeout = 10
                };
                return sb.ConnectionString;

            case DbProviderKind.PostgreSql:
                var nb = new Npgsql.NpgsqlConnectionStringBuilder
                {
                    Host = c.Host,
                    Port = port,
                    Database = c.Database,
                    Username = c.Username,
                    Password = password,
                    // Npgsql 8: sertifika doğrulamasını gevşetmek için SslMode kullanılır (TrustServerCertificate kaldırıldı/işlevsiz)
                    SslMode = c.UseSsl
                        ? (c.TrustServerCertificate ? Npgsql.SslMode.Require : Npgsql.SslMode.VerifyFull)
                        : Npgsql.SslMode.Prefer
                };
                return nb.ConnectionString;

            case DbProviderKind.MySql:
                var mb = new MySqlConnector.MySqlConnectionStringBuilder
                {
                    Server = c.Host,
                    Port = (uint)port,
                    Database = c.Database,
                    UserID = c.Username,
                    Password = password,
                    SslMode = c.UseSsl ? MySqlConnector.MySqlSslMode.Required : MySqlConnector.MySqlSslMode.Preferred
                };
                return mb.ConnectionString;

            case DbProviderKind.Oracle:
                // Oracle: host:port/ServiceName (Database alanı = Service Name)
                return $"User Id={c.Username};Password={password};Data Source={c.Host}:{port}/{c.Database}";

            default:
                throw new NotSupportedException($"Bilinmeyen sağlayıcı: {c.Provider}");
        }
    }

    /// <summary>DbContextOptionsBuilder'a sağlayıcıyı uygular. Ağ üstü DB'lerde geçici hata dayanıklılığı (retry) açık.</summary>
    public static void Apply(DbContextOptionsBuilder o, BootstrapConfig c, string connStr)
    {
        switch (c.Provider)
        {
            case DbProviderKind.Sqlite:
                o.UseSqlite(connStr);
                break;
            case DbProviderKind.SqlServer:
                o.UseSqlServer(connStr, x => x.EnableRetryOnFailure());
                break;
            case DbProviderKind.PostgreSql:
                o.UseNpgsql(connStr, x => x.EnableRetryOnFailure());
                break;
            case DbProviderKind.MySql:
                Version mv; try { mv = Version.Parse(c.MySqlServerVersion); } catch { mv = new Version(8, 0, 21); }
                o.UseMySql(connStr, new MySqlServerVersion(mv), x => x.EnableRetryOnFailure());
                break;
            case DbProviderKind.Oracle:
                o.UseOracle(connStr);
                break;
            default:
                throw new NotSupportedException($"Bilinmeyen sağlayıcı: {c.Provider}");
        }
    }

    /// <summary>Bootstrap'tan DB şifresini çözer: Vault açıksa Vault'tan, değilse DPAPI/AES ile.</summary>
    public static string ResolvePassword(BootstrapConfig c, ISecretProtector secrets)
    {
        if (c.Provider == DbProviderKind.Sqlite) return "";
        if (c.UseVault && !string.IsNullOrWhiteSpace(c.VaultUrl))
        {
            try
            {
                // Mevcut VaultClient'ı geçici bir Credential ile yeniden kullan (bootstrap'ta DB/Credential tablosu yok)
                var cred = new Credential
                {
                    Id = -1,
                    SourceType = CredentialSource.Vault,
                    VaultUrl = c.VaultUrl,
                    VaultTokenEncrypted = c.VaultTokenEncrypted,
                    VaultUserKey = c.VaultUserKey,
                    VaultKey = c.VaultKey,
                    Username = c.Username
                };
                var user = VaultClient.GetUsername(cred);
                if (!string.IsNullOrWhiteSpace(user)) c.Username = user;
                return VaultClient.GetPassword(cred);
            }
            catch { return ""; }
        }
        return secrets.Unprotect(c.PasswordEncrypted);
    }
}
