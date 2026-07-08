import { apiGet, apiSend } from "./api";

export interface ServiceItem {
  id: number;
  name: string;
  type: string;
  target: string;
  port: number | null;
  extra: string | null;
  useSsl: boolean;
  ignoreCertErrors: boolean;
  credentialId: number | null;
  credentialName: string | null;
  enabled: boolean;
  intervalMinutesOverride: number | null;
  responseTimeThresholdMs: number | null;
  timeoutSeconds: number;
  cpuThresholdPercent: number | null;
  ramThresholdPercent: number | null;
  diskThresholdPercent: number | null;
  keyword: string | null;
  description: string | null;
  alertMail: boolean;
  alertSms: boolean;
  alertWhatsapp: boolean;
  alertCall: boolean;
  selfHealEnabled: boolean;
  selfHealMaxRetries: number;
  selfHealAfterFailures: number;
  showOnStatusPage: boolean;
  lastCheckedAt: string | null;
  lastIsUp: boolean | null;
  lastStatus: number;
  lastResponseTimeMs: number | null;
  lastError: string | null;
  slow: boolean;
}

export type ServiceStatus = "up" | "slow" | "down" | "error";

export function statusOf(s: ServiceItem): ServiceStatus {
  if (s.lastStatus === 2) return "error";
  if (s.lastIsUp !== true) return "down";
  if (s.slow) return "slow";
  return "up";
}

export interface ServiceInput {
  name: string;
  type: string;
  target: string;
  port: number | null;
  extra: string | null;
  useSsl: boolean;
  ignoreCertErrors: boolean;
  credentialId: number | null;
  enabled: boolean;
  intervalMinutesOverride: number | null;
  responseTimeThresholdMs: number | null;
  timeoutSeconds: number;
  cpuThresholdPercent: number | null;
  ramThresholdPercent: number | null;
  diskThresholdPercent: number | null;
  keyword: string | null;
  description: string | null;
  alertMail: boolean;
  alertSms: boolean;
  alertWhatsapp: boolean;
  alertCall: boolean;
  selfHealEnabled: boolean;
  selfHealMaxRetries: number | null;
  selfHealAfterFailures: number | null;
  showOnStatusPage: boolean;
}

export interface ServicesMeta {
  types: string[];
  credentials: { id: number; name: string }[];
}

export const CONTROL_TYPES = ["WindowsServiceControl", "LinuxServiceControl"];

/* ================= DB İzleme Fazı: veritabanı sağlık metrikleri meta'sı =================
   Bu tiplerde ResponseTimeMs alanında ms DEĞİL metrik değeri (adet / % / sn) saklanır.
   Kartlar, grafikler ve widget'lar birimi buradan alır. */
export type DbPlatform = "Oracle" | "MSSQL" | "MySQL";
export interface DbMetricMeta {
  platform: DbPlatform;
  metric: "clock" | "active" | "blocked" | "long" | "status" | "usage" | "repl";
  unit: "ms" | "adet" | "%" | "sn";
  short: string;               // kart/panel kısa etiketi
}
export const DB_METRIC_META: Record<string, DbMetricMeta> = {
  OracleSysdate:          { platform: "Oracle", metric: "clock",   unit: "ms",   short: "Bağlantı & Saat" },
  OracleActiveSessions:   { platform: "Oracle", metric: "active",  unit: "adet", short: "Aktif Oturum" },
  OracleBlockedSessions:  { platform: "Oracle", metric: "blocked", unit: "adet", short: "Bloklu Oturum" },
  OracleLongQueries:      { platform: "Oracle", metric: "long",    unit: "adet", short: "Uzun Sorgu" },
  OracleTablespaceStatus: { platform: "Oracle", metric: "status",  unit: "adet", short: "Offline Tablespace" },
  OracleConnectionUsage:  { platform: "Oracle", metric: "usage",   unit: "%",    short: "Bağlantı Doluluğu" },
  MsSqlGetDate:           { platform: "MSSQL",  metric: "clock",   unit: "ms",   short: "Bağlantı & Saat" },
  MsSqlActiveSessions:    { platform: "MSSQL",  metric: "active",  unit: "adet", short: "Aktif Oturum" },
  MsSqlBlockedSessions:   { platform: "MSSQL",  metric: "blocked", unit: "adet", short: "Bloklu Oturum" },
  MsSqlLongQueries:       { platform: "MSSQL",  metric: "long",    unit: "adet", short: "Uzun Sorgu" },
  MsSqlDbStatus:          { platform: "MSSQL",  metric: "status",  unit: "adet", short: "Sorunlu Veritabanı" },
  MsSqlConnectionUsage:   { platform: "MSSQL",  metric: "usage",   unit: "%",    short: "Bağlantı Doluluğu" },
  MySqlNow:               { platform: "MySQL",  metric: "clock",   unit: "ms",   short: "Bağlantı & Saat" },
  MySqlActiveSessions:    { platform: "MySQL",  metric: "active",  unit: "adet", short: "Aktif Oturum" },
  MySqlBlockedSessions:   { platform: "MySQL",  metric: "blocked", unit: "adet", short: "Bloklu Oturum" },
  MySqlLongQueries:       { platform: "MySQL",  metric: "long",    unit: "adet", short: "Uzun Sorgu" },
  MySqlReplication:       { platform: "MySQL",  metric: "repl",    unit: "sn",   short: "Replikasyon Gecikmesi" },
  MySqlConnectionUsage:   { platform: "MySQL",  metric: "usage",   unit: "%",    short: "Bağlantı Doluluğu" },
};
export const isDbHealthType = (t: string) => t in DB_METRIC_META;

/** SSL sertifika izleme (SLLTracker mirası): değer = KALAN GÜN (ms değil). */
export const isCertType = (t: string) => t === "SslCertificate";

/** Metrik kutusuna tıklayınca yan panelde canlı liste (kim/hangisi/ne kadar) gösterilebilen tipler:
 *  aktif/bloklu oturum, uzun sorgu, (Oracle tablespace / MSSQL DB durumu), bağlantı doluluğu. */
export const hasDbDetail = (t: string) => {
  if (isCertType(t)) return true;   // canlı sertifika detayı (iç/dış CN, veren, bitiş, parmak izi)
  const m = DB_METRIC_META[t];
  return !!m && (m.metric === "active" || m.metric === "blocked" || m.metric === "long"
    || m.metric === "status" || m.metric === "usage");
};

/** DB metriği değer biçimlendirme: "34 oturum", "%71", "12 sn", "45 ms", sertifikada "N gün" */
export function fmtDbValue(type: string, v: number | null | undefined): string {
  if (v == null) return "—";
  if (isCertType(type)) return `${v} gün`;
  const m = DB_METRIC_META[type];
  if (!m) return `${v} ms`;
  if (m.unit === "%") return `%${v}`;
  return `${v} ${m.unit}`;
}

/** Platform marka rengi (metin sınıfı) */
export const DB_PLATFORM_CLS: Record<DbPlatform, string> = {
  Oracle: "text-red-400", MSSQL: "text-sky-400", MySQL: "text-teal-400",
};

/* ================= İzleme tipi kataloğu (eski formdaki typeConfig birebir + Database Fazı) =================
   Tip seçilince form alanları/etiketleri/ipuçları buna göre değişir. */
export interface TypeMeta {
  group: string;
  label: string;
  hint: string;
  target: { label: string; hint?: string };
  port: { hint: string } | null;          // null = port alanı gizli
  extra: { label: string; hint?: string } | null;
  cred: { label: string; hint?: string } | null;
  ssl: string | null;                     // null = SSL anahtarı gizli; string = etiketi
  cert: boolean;                          // sertifika-yoksay anahtarı görünür mü
  health?: boolean;                       // CPU/RAM/Disk eşik bloğu görünür mü
}

export const TYPE_META: Record<string, TypeMeta> = {
  Http: { group: "Web / Genel", label: "HTTP / Endpoint URL", hint: "URL'e GET atılır; durum kodu ve yanıt süresi kontrol edilir. Exchange OWA için: https://mail.firma.local/owa/healthcheck.htm", target: { label: "Tam URL *", hint: "örn. https://intranet.firma.local/health" }, port: null, extra: { label: "Beklenen HTTP durum kodu", hint: "Boşsa 2xx/3xx başarılı sayılır. örn. 200" }, cred: { label: "Kimlik (Basic Auth)", hint: "Opsiyonel — korumalı endpoint'ler için." }, ssl: null, cert: true },
  Tcp: { group: "Web / Genel", label: "Genel TCP Port", hint: "Yalnızca TCP portunun açık olduğu doğrulanır.", target: { label: "Host / IP *" }, port: { hint: "Zorunlu." }, extra: null, cred: null, ssl: null, cert: false },
  MySql: { group: "Veritabanı", label: "MySQL", hint: "İzleme kullanıcısıyla gerçek login + SELECT 1 çalıştırılır.", target: { label: "Host / IP *" }, port: { hint: "Boşsa 3306." }, extra: { label: "Veritabanı adı", hint: "Opsiyonel." }, cred: { label: "Kimlik Bilgisi *", hint: "DB izleme kullanıcısı (read-only önerilir)." }, ssl: "Bağlantıda SSL zorunlu olsun", cert: false },
  MsSql: { group: "Veritabanı", label: "MSSQL", hint: "İzleme kullanıcısıyla gerçek login + SELECT 1 çalıştırılır.", target: { label: "Host / IP *", hint: "Named instance için HOST\\INSTANCE yazın (port boş bırakın)." }, port: { hint: "Boşsa 1433." }, extra: { label: "Veritabanı adı", hint: "Opsiyonel." }, cred: { label: "Kimlik Bilgisi", hint: "SQL kullanıcısı; boşsa Windows auth denenir." }, ssl: "Bağlantıyı şifrele (Encrypt)", cert: true },
  Oracle: { group: "Veritabanı", label: "Oracle", hint: "Read-only izleme kullanıcısıyla login + SELECT 1 FROM DUAL çalıştırılır.", target: { label: "Host / IP *" }, port: { hint: "Boşsa 1521." }, extra: { label: "Service Name *", hint: "örn. ORCLPDB1 — SID kullanılacaksa: SID=ORCL" }, cred: { label: "Kimlik Bilgisi *", hint: "Oracle read-only izleme kullanıcısı." }, ssl: null, cert: false },
  // ---- Database Fazı: Oracle sağlık izlemeleri ----
  OracleSysdate: { group: "Veritabanı", label: "Oracle SYSDATE (bağlantı + saat)", hint: "SELECT SYSDATE FROM DUAL çalıştırılır: bağlantı + gecikme grafiğe yazılır; DB saati sunucudan 60 sn'den fazla sapıyorsa HATA üretir.", target: { label: "Host / IP *" }, port: { hint: "Boşsa 1521." }, extra: { label: "Service Name *", hint: "örn. ORCLPDB1 — SID için: SID=ORCL" }, cred: { label: "Kimlik Bilgisi *", hint: "CREATE SESSION yetkili izleme kullanıcısı." }, ssl: null, cert: false },
  OracleActiveSessions: { group: "Veritabanı", label: "Oracle Aktif Sessions", hint: "GV$SESSION'dan background olmayan AKTİF oturum adedi çekilir; adet grafiğe yazılır. Yavaşlık Eşiği alanı bu adede eşik olur (aşınca YAVAŞ).", target: { label: "Host / IP *" }, port: { hint: "Boşsa 1521." }, extra: { label: "Service Name *", hint: "örn. ORCLPDB1 — SID için: SID=ORCL" }, cred: { label: "Kimlik Bilgisi *", hint: "GV$SESSION okuma yetkili kullanıcı (SELECT ON gv_$session)." }, ssl: null, cert: false },
  OracleBlockedSessions: { group: "Veritabanı", label: "Oracle Blocked Sessions", hint: "Birbirini bloklayan (BLOCKING_SESSION dolu) oturum adedi çekilir; adet grafiğe yazılır. Adet > 0 ise HATA (alarm) üretir.", target: { label: "Host / IP *" }, port: { hint: "Boşsa 1521." }, extra: { label: "Service Name *", hint: "örn. ORCLPDB1 — SID için: SID=ORCL" }, cred: { label: "Kimlik Bilgisi *", hint: "GV$SESSION okuma yetkili kullanıcı (SELECT ON gv_$session)." }, ssl: null, cert: false },
  OracleLongQueries: { group: "Veritabanı", label: "Oracle Uzun Süren Sorgular", hint: "60 sn'den uzun süredir aktif çalışan oturum adedi çekilir; adet grafiğe yazılır. Yavaşlık Eşiği alanı bu adede eşik olur (aşınca YAVAŞ).", target: { label: "Host / IP *" }, port: { hint: "Boşsa 1521." }, extra: { label: "Service Name *", hint: "örn. ORCLPDB1 — SID için: SID=ORCL" }, cred: { label: "Kimlik Bilgisi *", hint: "GV$SESSION okuma yetkili kullanıcı (SELECT ON gv_$session)." }, ssl: null, cert: false },
  OracleTablespaceStatus: { group: "Veritabanı", label: "Oracle Tablespace Durumu", hint: "OFFLINE tablespace adedi çekilir (READ ONLY sayılmaz); adet grafiğe yazılır. Adet > 0 ise HATA (alarm) üretir.", target: { label: "Host / IP *" }, port: { hint: "Boşsa 1521." }, extra: { label: "Service Name *", hint: "örn. ORCLPDB1 — SID için: SID=ORCL" }, cred: { label: "Kimlik Bilgisi *", hint: "DBA_TABLESPACES okuma yetkili kullanıcı (SELECT ON dba_tablespaces)." }, ssl: null, cert: false },
  OracleConnectionUsage: { group: "Veritabanı", label: "Oracle Bağlantı Doluluğu (%)", hint: "processes limitinin yüzde kaçının dolu olduğu çekilir (V$RESOURCE_LIMIT); yüzde grafiğe yazılır. Yavaşlık Eşiği alanına örn. 90 yazarsanız aşınca YAVAŞ işaretlenir.", target: { label: "Host / IP *" }, port: { hint: "Boşsa 1521." }, extra: { label: "Service Name *", hint: "örn. ORCLPDB1 — SID için: SID=ORCL" }, cred: { label: "Kimlik Bilgisi *", hint: "V$RESOURCE_LIMIT okuma yetkili kullanıcı (SELECT ON v_$resource_limit)." }, ssl: null, cert: false },
  MsSqlGetDate: { group: "Veritabanı", label: "MSSQL GETDATE (bağlantı + saat)", hint: "SELECT GETDATE() çalıştırılır: bağlantı + gecikme grafiğe yazılır; DB saati sunucudan 60 sn'den fazla sapıyorsa HATA üretir.", target: { label: "Host / IP *", hint: "Named instance için HOST\\INSTANCE yazın (port boş bırakın)." }, port: { hint: "Boşsa 1433." }, extra: { label: "Veritabanı adı", hint: "Opsiyonel — boşsa master." }, cred: { label: "Kimlik Bilgisi", hint: "SQL kullanıcısı; boşsa Windows auth denenir." }, ssl: "Bağlantıyı şifrele (Encrypt)", cert: true },
  MsSqlActiveSessions: { group: "Veritabanı", label: "MSSQL Aktif Sessions", hint: "Çalışan (status='running') kullanıcı oturumu adedi çekilir; adet grafiğe yazılır. Yavaşlık Eşiği alanı bu adede eşik olur (aşınca YAVAŞ).", target: { label: "Host / IP *", hint: "Named instance için HOST\\INSTANCE yazın (port boş bırakın)." }, port: { hint: "Boşsa 1433." }, extra: { label: "Veritabanı adı", hint: "Opsiyonel — boşsa master." }, cred: { label: "Kimlik Bilgisi", hint: "VIEW SERVER STATE yetkili SQL kullanıcısı; boşsa Windows auth denenir." }, ssl: "Bağlantıyı şifrele (Encrypt)", cert: true },
  MsSqlBlockedSessions: { group: "Veritabanı", label: "MSSQL Blocked Sessions", hint: "Başka oturum tarafından bloklanan istek adedi çekilir; adet grafiğe yazılır. Adet > 0 ise HATA (alarm) üretir.", target: { label: "Host / IP *", hint: "Named instance için HOST\\INSTANCE yazın (port boş bırakın)." }, port: { hint: "Boşsa 1433." }, extra: { label: "Veritabanı adı", hint: "Opsiyonel — boşsa master." }, cred: { label: "Kimlik Bilgisi", hint: "VIEW SERVER STATE yetkili SQL kullanıcısı; boşsa Windows auth denenir." }, ssl: "Bağlantıyı şifrele (Encrypt)", cert: true },
  MsSqlLongQueries: { group: "Veritabanı", label: "MSSQL Uzun Süren Sorgular", hint: "60 sn'den uzun süredir çalışan kullanıcı isteği adedi çekilir; adet grafiğe yazılır. Yavaşlık Eşiği alanı bu adede eşik olur (aşınca YAVAŞ).", target: { label: "Host / IP *", hint: "Named instance için HOST\\INSTANCE yazın (port boş bırakın)." }, port: { hint: "Boşsa 1433." }, extra: { label: "Veritabanı adı", hint: "Opsiyonel — boşsa master." }, cred: { label: "Kimlik Bilgisi", hint: "VIEW SERVER STATE yetkili SQL kullanıcısı; boşsa Windows auth denenir." }, ssl: "Bağlantıyı şifrele (Encrypt)", cert: true },
  MsSqlDbStatus: { group: "Veritabanı", label: "MSSQL Veritabanı Durumu", hint: "ONLINE olmayan veritabanı adedi çekilir (suspect/recovery/offline yakalar); adet grafiğe yazılır. Adet > 0 ise sorunlu DB adları mesaja yazılıp HATA üretilir.", target: { label: "Host / IP *", hint: "Named instance için HOST\\INSTANCE yazın (port boş bırakın)." }, port: { hint: "Boşsa 1433." }, extra: { label: "Veritabanı adı", hint: "Opsiyonel — boşsa master." }, cred: { label: "Kimlik Bilgisi", hint: "VIEW SERVER STATE yetkili SQL kullanıcısı; boşsa Windows auth denenir." }, ssl: "Bağlantıyı şifrele (Encrypt)", cert: true },
  MsSqlConnectionUsage: { group: "Veritabanı", label: "MSSQL Bağlantı Doluluğu (%)", hint: "Kullanıcı oturumu adedinin bağlantı limitine oranı (%) çekilir; yüzde grafiğe yazılır. Yavaşlık Eşiği alanına örn. 90 yazarsanız aşınca YAVAŞ işaretlenir.", target: { label: "Host / IP *", hint: "Named instance için HOST\\INSTANCE yazın (port boş bırakın)." }, port: { hint: "Boşsa 1433." }, extra: { label: "Veritabanı adı", hint: "Opsiyonel — boşsa master." }, cred: { label: "Kimlik Bilgisi", hint: "VIEW SERVER STATE yetkili SQL kullanıcısı; boşsa Windows auth denenir." }, ssl: "Bağlantıyı şifrele (Encrypt)", cert: true },
  MySqlNow: { group: "Veritabanı", label: "MySQL NOW (bağlantı + saat)", hint: "SELECT NOW() çalıştırılır: bağlantı + gecikme grafiğe yazılır; DB saati sunucudan 60 sn'den fazla sapıyorsa HATA üretir.", target: { label: "Host / IP *" }, port: { hint: "Boşsa 3306." }, extra: { label: "Veritabanı adı", hint: "Opsiyonel." }, cred: { label: "Kimlik Bilgisi *", hint: "MySQL izleme kullanıcısı." }, ssl: "Bağlantıda SSL zorunlu olsun", cert: false },
  MySqlActiveSessions: { group: "Veritabanı", label: "MySQL Aktif Sessions", hint: "Uyumayan (COMMAND<>'Sleep') bağlantı adedi çekilir; adet grafiğe yazılır. Yavaşlık Eşiği alanı bu adede eşik olur (aşınca YAVAŞ).", target: { label: "Host / IP *" }, port: { hint: "Boşsa 3306." }, extra: { label: "Veritabanı adı", hint: "Opsiyonel." }, cred: { label: "Kimlik Bilgisi *", hint: "PROCESS yetkili izleme kullanıcısı (GRANT PROCESS)." }, ssl: "Bağlantıda SSL zorunlu olsun", cert: false },
  MySqlBlockedSessions: { group: "Veritabanı", label: "MySQL Blocked Sessions", hint: "Kilit bekleyen (LOCK WAIT) InnoDB işlem adedi çekilir; adet grafiğe yazılır. Adet > 0 ise HATA (alarm) üretir.", target: { label: "Host / IP *" }, port: { hint: "Boşsa 3306." }, extra: { label: "Veritabanı adı", hint: "Opsiyonel." }, cred: { label: "Kimlik Bilgisi *", hint: "PROCESS yetkili izleme kullanıcısı (GRANT PROCESS)." }, ssl: "Bağlantıda SSL zorunlu olsun", cert: false },
  MySqlLongQueries: { group: "Veritabanı", label: "MySQL Uzun Süren Sorgular", hint: "60 sn'den uzun süredir çalışan sorgu adedi çekilir; adet grafiğe yazılır. Yavaşlık Eşiği alanı bu adede eşik olur (aşınca YAVAŞ).", target: { label: "Host / IP *" }, port: { hint: "Boşsa 3306." }, extra: { label: "Veritabanı adı", hint: "Opsiyonel." }, cred: { label: "Kimlik Bilgisi *", hint: "PROCESS yetkili izleme kullanıcısı (GRANT PROCESS)." }, ssl: "Bağlantıda SSL zorunlu olsun", cert: false },
  MySqlReplication: { group: "Veritabanı", label: "MySQL Replikasyon Sağlığı", hint: "Replika IO/SQL thread durumu + kaynağın kaç sn gerisinde olduğu çekilir; gecikme (sn) grafiğe yazılır. Thread durmuşsa HATA; Yavaşlık Eşiği alanı gecikmeye sn eşiği olur.", target: { label: "Replika Host / IP *" }, port: { hint: "Boşsa 3306." }, extra: { label: "Veritabanı adı", hint: "Opsiyonel." }, cred: { label: "Kimlik Bilgisi *", hint: "REPLICATION CLIENT yetkili kullanıcı (GRANT REPLICATION CLIENT)." }, ssl: "Bağlantıda SSL zorunlu olsun", cert: false },
  MySqlConnectionUsage: { group: "Veritabanı", label: "MySQL Bağlantı Doluluğu (%)", hint: "Bağlantı adedinin max_connections limitine oranı (%) çekilir; yüzde grafiğe yazılır. Yavaşlık Eşiği alanına örn. 90 yazarsanız aşınca YAVAŞ işaretlenir.", target: { label: "Host / IP *" }, port: { hint: "Boşsa 3306." }, extra: { label: "Veritabanı adı", hint: "Opsiyonel." }, cred: { label: "Kimlik Bilgisi *", hint: "PROCESS yetkili izleme kullanıcısı (GRANT PROCESS)." }, ssl: "Bağlantıda SSL zorunlu olsun", cert: false },
  SslCertificate: { group: "Web / Genel", label: "SSL Sertifikası (iç/dış kontrol)", hint: "Alan adının SSL sertifikası DIŞARIDAN denetlenir (public DNS ile — iç ağın DNS'i atlanır, gerçekten dışarıdan görünen sertifika alınır); kalan gün grafiğe yazılır. İç kontrol hedefi doldurulursa sunucudaki sertifika da alınıp dışarıdakiyle KARŞILAŞTIRILIR — sunucuda yenilenen ama F5/yük dengeleyicide eski kalan sertifika yakalanır. Uyarı eşiği (gün) altına inince veya süre dolunca HATA üretir.", target: { label: "Alan adı *", hint: "örn. portal.firma.com (https:// yazmadan da olur)" }, port: { hint: "Boşsa 443." }, extra: { label: "İç kontrol hedefi (host[:port])", hint: "Opsiyonel — örn. 10.184.10.5 veya sunucu.local:8443. Doluysa iç/dış sertifikalar karşılaştırılır." }, cred: null, ssl: null, cert: false },
  Ldap: { group: "Altyapı", label: "Active Directory / LDAP(S)", hint: "Domain Controller'a yetkili kullanıcıyla gerçek bind yapılır. LDAPS için SSL işaretleyin (port 636).", target: { label: "Domain Controller / IP *" }, port: { hint: "Boşsa 389 (LDAP) / 636 (SSL işaretliyse)." }, extra: null, cred: { label: "Kimlik Bilgisi *", hint: "AD hesabı (Domain alanını doldurun) veya tam DN." }, ssl: "LDAPS kullan", cert: true },
  Dns: { group: "Altyapı", label: "DNS", hint: "Hedef DNS sunucusuna doğrudan A kaydı sorgusu atılır (OS resolver kullanılmaz).", target: { label: "DNS Sunucu IP *" }, port: { hint: "Boşsa 53." }, extra: { label: "Test edilecek hostname *", hint: "örn. intranet.firma.local — bu kayıt çözülürse UP." }, cred: null, ssl: null, cert: false },
  Sftp: { group: "Altyapı", label: "SFTP", hint: "Yetkili kullanıcıyla gerçek SSH/SFTP oturumu açılır ve dizin listelenir.", target: { label: "Host / IP *" }, port: { hint: "Boşsa 22." }, extra: null, cred: { label: "Kimlik Bilgisi *", hint: "SFTP kullanıcısı." }, ssl: null, cert: false },
  DhcpWindowsService: { group: "Altyapı", label: "DHCP (Windows servis kontrolü)", hint: "Uzak Windows sunucusunda DHCP Server servisinin Running olduğu WMI ile doğrulanır.", target: { label: "DHCP Sunucusu (host/IP) *" }, port: null, extra: { label: "Windows servis adı", hint: "Boşsa DHCPServer." }, cred: { label: "Kimlik Bilgisi *", hint: "Uzak WMI yetkisi olan domain hesabı (Domain alanını doldurun)." }, ssl: null, cert: false },
  Ping: { group: "Altyapı", label: "Ping (ICMP)", hint: "ICMP echo gönderilir; yanıt gelirse UP.", target: { label: "Host / IP *" }, port: null, extra: { label: "Maks. RTT (ms)", hint: "Opsiyonel — yanıt bu süreden uzunsa DOWN sayılır." }, cred: null, ssl: null, cert: false },
  Smtp: { group: "Mail (Exchange / Exchange Online)", label: "SMTP", hint: "SMTP'ye bağlanır, 220 banner + EHLO/250 doğrular. On-prem: mail sunucu:25. Exchange Online: smtp.office365.com:25.", target: { label: "Mail Sunucusu (host/IP) *", hint: "örn. mail.firma.local veya smtp.office365.com" }, port: { hint: "Boşsa 25. Implicit TLS (465) için SSL işaretleyin." }, extra: null, cred: null, ssl: "Implicit TLS (465)", cert: true },
  Imap: { group: "Mail (Exchange / Exchange Online)", label: "IMAP", hint: "IMAP'a bağlanır, \"* OK\" greeting'i doğrular. Exchange Online: outlook.office365.com:993 + SSL.", target: { label: "Mail Sunucusu (host/IP) *", hint: "örn. mail.firma.local veya outlook.office365.com" }, port: { hint: "Boşsa 143 / 993 (SSL işaretliyse)." }, extra: null, cred: null, ssl: "IMAPS (993)", cert: true },
  WindowsHealth: { group: "Sunucu Sağlığı", label: "Windows Sunucu (CPU/RAM/Disk)", hint: "WMI ile uzak Windows sunucusundan CPU, RAM ve disk doluluk oranları okunur; eşikler aşılırsa alarm üretilir.", target: { label: "Sunucu (host/IP) *" }, port: null, extra: null, cred: { label: "Kimlik Bilgisi *", hint: "Uzak WMI okuma yetkisi olan domain hesabı (Domain alanını doldurun)." }, ssl: null, cert: false, health: true },
  LinuxHealth: { group: "Sunucu Sağlığı", label: "Linux Sunucu (CPU/RAM/Disk, SSH)", hint: "SSH ile bağlanıp /proc/stat, free ve df okunur (root gerekmez); eşikler aşılırsa alarm üretilir.", target: { label: "Sunucu (host/IP) *" }, port: { hint: "Boşsa 22." }, extra: null, cred: { label: "Kimlik Bilgisi *", hint: "SSH kullanıcısı (sıradan kullanıcı yeterli)." }, ssl: null, cert: false, health: true },
  WindowsServiceControl: { group: "Servis Durumu (Uzaktan Kontrol)", label: "Windows Servis (start/stop/restart)", hint: "Uzak Windows sunucusunda belirtilen servis çalışıyor mu (WMI) kontrol edilir; dashboard'dan start/stop/restart yapılabilir.", target: { label: "Sunucu (host/IP) *" }, port: null, extra: { label: "Windows Servis Adı *", hint: "örn. W3SVC (IIS), MSSQLSERVER, Spooler" }, cred: { label: "Kimlik Bilgisi *", hint: "Uzak WMI + servis kontrol yetkisi olan domain hesabı (Domain alanını doldurun)." }, ssl: null, cert: false },
  LinuxServiceControl: { group: "Servis Durumu (Uzaktan Kontrol)", label: "Linux Servis (systemd, SSH)", hint: "Uzak Linux sunucusunda systemd servis durumu (SSH) kontrol edilir; dashboard'dan start/stop/restart yapılabilir (sudo gerekir).", target: { label: "Sunucu (host/IP) *" }, port: { hint: "Boşsa 22." }, extra: { label: "systemd Unit Adı *", hint: "örn. nginx, httpd, docker" }, cred: { label: "Kimlik Bilgisi *", hint: "SSH kullanıcısı (start/stop/restart için sudo -n yetkisi gerekir)." }, ssl: null, cert: false },
};

/** Kategori sırası (eski formdaki optgroup düzeni) */
export const TYPE_GROUP_ORDER = [
  "Veritabanı", "Altyapı", "Mail (Exchange / Exchange Online)",
  "Sunucu Sağlığı", "Servis Durumu (Uzaktan Kontrol)", "Web / Genel",
];

export const listServices = (signal?: AbortSignal) => apiGet<ServiceItem[]>("/services", signal);
export const servicesMeta = (signal?: AbortSignal) => apiGet<ServicesMeta>("/services/meta", signal);
export const createService = (input: ServiceInput) => apiSend<{ id: number }>("POST", "/services", input);
export const updateService = (id: number, input: ServiceInput) => apiSend<{ id: number }>("PUT", `/services/${id}`, input);
export const deleteService = (id: number) => apiSend<{ ok: boolean }>("DELETE", `/services/${id}`);
export const checkService = (id: number) => apiSend<{ isUp: boolean; responseTimeMs: number; error: string | null }>("POST", `/check/${id}`);
// ---- Toplu işlemler + CSV ----
export interface BulkEditInput {
  ids: number[];
  alertMail: string | null;      // "on" | "off" | null (dokunma)
  alertSms: string | null;
  alertWhatsapp: string | null;
  alertCall: string | null;
  enabled: string | null;
  showOnStatusPage: string | null;
  setInterval: boolean; interval: number | null;
  setSlow: boolean; slow: number | null;
  setCpu: boolean; cpu: number | null;
  setRam: boolean; ram: number | null;
  setDisk: boolean; disk: number | null;
  addKeywords: string | null;
}

export const bulkDelete = (ids: number[]) => apiSend<{ deleted: number }>("POST", "/services/bulk-delete", { ids });
export const bulkEdit = (input: BulkEditInput) => apiSend<{ updated: number; changes: string[] }>("POST", "/services/bulk-edit", input);

export const exportCsvUrl = (ids?: number[]) =>
  ids && ids.length > 0 ? `/api/services/export?ids=${ids.join(",")}` : "/api/services/export";
export const sampleCsvUrl = "/Services/SampleCsv";

/** CSV içe aktarım — multipart + CSRF header. */
export async function importCsv(file: File): Promise<{ added: number; skipped: number; errors: string[] }> {
  const token = await formCsrf();
  const fd = new FormData();
  fd.append("csvFile", file);
  const res = await fetch("/api/services/import", {
    method: "POST", credentials: "same-origin",
    headers: { "X-CSRF-TOKEN": token },
    body: fd,
  });
  if (!res.ok) throw new ApiError(res.status, (await res.text()) || `Sunucu hatası (${res.status})`);
  return res.json();
}

/** Görünen servisleri toplu kontrol (eski 'Görünenleri Kontrol Et'). */
export const checkIds = (ids: number[]) =>
  sendForm<{ id: number; isUp: boolean; responseTimeMs?: number; error: string | null }[]>(
    "/check-ids", new URLSearchParams({ ids: ids.join(",") }));

export const serviceAction = (id: number, action: "start" | "stop" | "restart") => {
  const body = new URLSearchParams({ action });
  // service-action [FromForm] bekliyor → form-encoded gönderiyoruz (apiSend JSON; burada özel fetch)
  return sendForm<{ ok: boolean; message: string }>(`/service-action/${id}`, body);
};

// service-action [FromForm] olduğu için form-encoded gönderen küçük yardımcı (CSRF dahil)
import { ApiError } from "./api";
let _csrf: string | null = null;
async function formCsrf(): Promise<string> {
  if (_csrf) return _csrf;
  const r = await fetch("/api/antiforgery", { credentials: "same-origin" });
  _csrf = (await r.json()).token as string;
  return _csrf;
}
async function sendForm<T>(path: string, body: URLSearchParams): Promise<T> {
  const token = await formCsrf();
  const res = await fetch(`/api${path}`, {
    method: "POST",
    credentials: "same-origin",
    headers: { "X-CSRF-TOKEN": token, "Content-Type": "application/x-www-form-urlencoded" },
    body,
  });
  if (res.status === 401) throw new ApiError(401, "Oturum gerekli.");
  if (res.status === 403) throw new ApiError(403, "Bu işlem için yetkiniz yok. Yöneticinizden 'Servis başlat/durdur' yetkisi isteyebilirsiniz.");
  if (!res.ok) throw new ApiError(res.status, `Sunucu hatası (${res.status})`);
  return res.json() as Promise<T>;
}
