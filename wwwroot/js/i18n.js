"use strict";

/* vMon — TR→EN çeviri katmanı (view'lar değiştirilmeden, çalışma anında).
   İki sözlük:
   - TEXT: bir metin düğümünün TAMAMI (trim) birebir eşleşirse çevrilir. Sonu " *" ile bitenler ayrıca denenir.
   - PROSE: blok/çocuk/link içermeyen bir kutunun (.hint/.alert/li/p) TÜM görünen metni (boşluk normalize) eşleşirse çevrilir
     (uzun yardım/uyarı cümleleri için). Link içeren kutular PROSE'a girmez → TEXT parça-parça çevirir (linkler korunur).
   GÜVENLİ: yalnız birebir eşleşen metin çevrilir; ad/sayı/tarih gibi dinamikler dokunulmaz. lang="en" değilse çalışmaz. */
(function () {
    if (document.documentElement.lang !== "en") return;

    const TEXT = {
        // Navigasyon / kullanıcı menüsü
        "Dashboard'lar": "Dashboards", "Raporlar": "Reports", "Servisler": "Services",
        "Kimlik Bilgileri": "Credentials", "Ayarlar": "Settings", "Kullanıcılar": "Users",
        "Denetim": "Audit", "Mutabakat": "Reconciliation", "Hakkında": "About",
        "Çıkış": "Logout", "Tema": "Theme", "Açık": "Light", "Koyu": "Dark", "Dil": "Language",
        "Türkçe": "Turkish", "English": "English",

        // Ortak butonlar / etiketler
        "Kaydet": "Save", "İptal": "Cancel", "Sil": "Delete", "Düzenle": "Edit", "Ekle": "Add",
        "Test": "Test", "Uygula": "Apply", "Yükle": "Upload", "Gönder": "Send", "Kapat": "Close",
        "Evet": "Yes", "Hayır": "No", "Geri": "Back", "Ara": "Search", "Yeni": "New", "Tümü": "All",
        "Filtrele": "Filter", "Temizle": "Clear", "Getir": "Load", "Kontrol Et": "Check", "Geçmiş": "History",
        "Aktif": "Active", "Pasif": "Passive", "Aktif/Pasif": "Active/Passive", "Bekliyor": "Pending", "Yerel": "Local",
        "Durum": "Status", "Yetkiler": "Permissions", "Yetkiler ": "Permissions", "Yetki yok": "No permissions",
        "Port": "Port", "Domain": "Domain", "Açıklama": "Description", "Kullanım": "Usage", "Kaynak": "Source",
        "Manuel": "Manual", "Manuel Şifre": "Manual Password", "Detay": "Detail", "Özet": "Summary",
        "Yükleniyor...": "Loading...", "Kayıtlar": "Records", "Sonuç": "Result", "Başarılı": "Success",
        "Başarısız": "Failed", "Eylem": "Action", "Hedef": "Target", "Zaman": "Time", "Kullanıcı": "User",
        "Ad": "Name", "Tip": "Type", "Etiket": "Tag", "Kimlik": "Credential", "Aralık": "Interval",
        "Rol / Yetkiler": "Role / Permissions", "Son Giriş": "Last Login", "Yetkiler:": "Permissions:",

        // Durum rozetleri (JS ile çizilen kartlar dahil)
        "YAVAŞ": "SLOW", "PASİF": "PASSIVE", "BEKLİYOR": "PENDING", "Yavaş": "Slow",
        "Toplam": "Total", "TOPLAM": "TOTAL",

        // Zaman aralıkları
        "Son 1 saat": "Last 1 hour", "Son 3 saat": "Last 3 hours", "Son 6 saat": "Last 6 hours",
        "Son 12 saat": "Last 12 hours", "Son 24 saat": "Last 24 hours", "Son 7 gün": "Last 7 days",
        "Son 1 ay": "Last 1 month", "24 saat": "24 hours", "7 gün": "7 days", "30 gün": "30 days",
        "90 gün": "90 days", "1 yıl": "1 year",

        // Dashboard'lar
        "Özel izleme ekranların — sekmeler arası hızlı geçiş": "Your custom monitoring screens — quick tab switching",
        "Hepsi": "All", "Yeni Dashboard": "New Dashboard", "Görünenleri Kontrol Et": "Check Visible",
        "Yanıt Süreleri": "Response Times", "Servis Seç": "Select Service", "Servis ara...": "Search service...",
        "Tümünü seç / kaldır": "Select / clear all", "CPU Kullanımı": "CPU Usage", "RAM Kullanımı": "RAM Usage",
        "Disk Doluluk": "Disk Usage",
        // Dashboard düzenleme formu
        "Dashboard Düzenle": "Edit Dashboard", "Dashboard Adı": "Dashboard Name",
        "Tip Filtresi (otomatik dahil)": "Type Filter (auto-included)", "Sıra": "Order",
        "Etiket Filtresi (otomatik dahil)": "Tag Filter (auto-included)", "Seçili Servisler": "Selected Services",
        "Görünenlerin hepsini seç": "Select all visible",
        "Ad / hedef / tip / etiket ara...": "Search name / target / type / tag...",
        "İstatistikler": "Statistics",
        "İzlenen altyapının canlı özeti — filtrelenebilir, sürüklenebilir panolar": "Live overview of monitored infrastructure — filterable, draggable panels",
        "Yenile": "Refresh", "Düzenle": "Edit", "Kilitle": "Lock",
        "Etiket ekle (mevcuda eklenir, virgülle ayırın)": "Add tag (appended to existing, comma-separated)",
        "Mevcut etiketler korunur; yazılanlar listeye eklenir (tekrarlar atlanır).": "Existing tags are kept; entries are added to the list (duplicates skipped).",
        "örn. Ping İzleme, Kritik DB'ler": "e.g. Ping Monitoring, Critical DBs",
        "Windows Sağlık": "Windows Health", "Linux Sağlık": "Linux Health",
        "Windows Servis": "Windows Service", "Linux Servis": "Linux Service",
        "Ping (ICMP)": "Ping (ICMP)", "AD / LDAP": "AD / LDAP",
        // Servis formu başlığı + açıklama
        "Servis Düzenle": "Edit Service",
        "örn. Üretim e-fatura uygulama sunucusu — Tomcat + PostgreSQL": "e.g. Production e-invoice app server — Tomcat + PostgreSQL",

        // Servisler (liste)
        "İzlenen servisleri yönetin, CSV ile toplu ekleyin": "Manage monitored services, bulk add via CSV",
        "CSV Dışa Aktar (Yedek)": "Export CSV (Backup)", "Örnek CSV İndir": "Download Sample CSV",
        "CSV İçe Aktar": "Import CSV", "Yeni Servis": "New Service", "Ad / hedef ara...": "Search name / target...",
        "Tüm Tipler": "All Types", "Tüm Etiketler": "All Tags", "Seçilenleri Düzenle": "Edit Selected",
        "Henüz servis yok.": "No services yet.", "Alarm kanalları & durum": "Alarm channels & status",
        "E-posta alarmı": "Email alarm", "SMS alarmı": "SMS alarm", "WhatsApp alarmı": "WhatsApp alarm",
        "Arama alarmı": "Call alarm", "Kontrol aralığı (dk)": "Check interval (min)",
        "Yavaşlık eşiği (ms)": "Slowness threshold (ms)", "CPU eşiği (%)": "CPU threshold (%)",
        "RAM eşiği (%)": "RAM threshold (%)", "Disk eşiği (%)": "Disk threshold (%)",
        "Dokunma": "Keep", "Aç": "On", "Kapat": "Off",
        "CSV ile Toplu Servis Ekleme": "Bulk Add Services via CSV", "CSV Dosyası": "CSV File",

        // Servis formu (etiketler — JS dinamik olanlar dahil)
        "Servis Adı": "Service Name", "İzleme Türü": "Monitoring Type",
        "Hedef": "Target", "Ekstra": "Extra", "Kimlik Bilgisi": "Credential", "Zaman Aşımı (sn)": "Timeout (s)",
        "Kontrol Aralığı (dk)": "Check Interval (min)", "Yavaşlık Eşiği (ms)": "Slowness Threshold (ms)",
        "SSL kullan": "Use SSL", "LDAPS kullan": "Use LDAPS", "IMAPS (993)": "IMAPS (993)",
        "Implicit TLS (465)": "Implicit TLS (465)", "Sertifika hatalarını yoksay": "Ignore certificate errors",
        "Alarm Yönetimi": "Alarm Management", "E-posta": "Email", "Arama": "Call",
        "CPU Eşiği (%)": "CPU Threshold (%)", "RAM Eşiği (%)": "RAM Threshold (%)", "Disk Eşiği (%)": "Disk Threshold (%)",
        "Veritabanı": "Database", "Altyapı": "Infrastructure", "Sunucu Sağlığı": "Server Health",
        "Servis Durumu (Uzaktan Kontrol)": "Service Status (Remote Control)", "Web / Genel": "Web / General",
        "Host / IP": "Host / IP", "Tam URL": "Full URL", "Veritabanı adı": "Database name",
        "Service Name": "Service Name", "Mail Sunucusu (host/IP)": "Mail Server (host/IP)",
        "Sunucu (host/IP)": "Server (host/IP)", "DNS Sunucu IP": "DNS Server IP",
        "DHCP Sunucusu (host/IP)": "DHCP Server (host/IP)", "Domain Controller / IP": "Domain Controller / IP",
        "Windows servis adı": "Windows service name", "Windows Servis Adı": "Windows Service Name",
        "systemd Unit Adı": "systemd Unit Name", "Maks. RTT (ms)": "Max RTT (ms)",
        "Test edilecek hostname": "Hostname to test", "Beklenen HTTP durum kodu": "Expected HTTP status code",
        "Kimlik (Basic Auth)": "Credential (Basic Auth)", "Yeni kimlik ekle": "Add new credential",
        // Servis formu — kimlik yardımları (link öncesi metin)
        "Opsiyonel — korumalı endpoint'ler için.": "Optional — for protected endpoints.",
        "DB izleme kullanıcısı (read-only önerilir).": "DB monitoring user (read-only recommended).",
        "SQL kullanıcısı; boşsa Windows auth (app pool hesabı) denenir.": "SQL user; if empty, Windows auth (app pool account) is tried.",
        "Oracle read-only izleme kullanıcısı.": "Oracle read-only monitoring user.",
        "AD hesabı (Domain alanını doldurun) veya tam DN.": "AD account (fill the Domain field) or full DN.",
        "SFTP kullanıcısı.": "SFTP user.",
        "Uzak WMI yetkisi olan domain hesabı (Domain alanını doldurun).": "Domain account with remote WMI rights (fill the Domain field).",
        "Uzak WMI okuma yetkisi olan domain hesabı (Domain alanını doldurun).": "Domain account with remote WMI read rights (fill the Domain field).",
        "SSH kullanıcısı (sıradan kullanıcı yeterli).": "SSH user (a regular user is enough).",
        "Uzak WMI + servis kontrol yetkisi olan domain hesabı (Domain alanını doldurun).": "Domain account with remote WMI + service control rights (fill the Domain field).",
        "SSH kullanıcısı (start/stop/restart için sudo -n yetkisi gerekir).": "SSH user (needs sudo -n rights for start/stop/restart).",

        // Bildirim Kanalları
        "Bildirim Kanalları": "Notification Channels", "Yeni Entegrasyon": "New Integration",
        "SMS ekle": "Add SMS", "WhatsApp ekle": "Add WhatsApp",
        "Sesli Arama / IVR ekle": "Add Voice Call / IVR", "Sesli Arama / IVR": "Voice Call / IVR", "Sesli/IVR": "Voice/IVR",
        "Kanal Türü": "Channel Type", "Entegrasyon Adı": "Integration Name",
        "Yöntem": "Method", "Alıcılar": "Recipients", "Alıcılar (opsiyonel)": "Recipients (optional)",
        "Gönderen / Başlık ({from})": "Sender / Header ({from})",
        "URL": "URL", "Kullanıcı Adı": "Username", "Kimlik Doğrulama": "Authentication",
        "POST Gövde Türü": "POST Body Type", "POST Gövdesi (şablon)": "POST Body (template)",
        "Başarı Metni (opsiyonel)": "Success Text (optional)",
        "Alarm Şablonu — Content SID (yalnızca WhatsApp)": "Alarm Template — Content SID (WhatsApp only)",

        // Ayarlar
        "Ayarlar": "Settings", "İzleme": "Monitoring", "Email (SMTP Relay)": "Email (SMTP Relay)",
        "Güvenlik ve Uyumluluk": "Security & Compliance",
        "Oturum Açma (LDAP / Active Directory)": "Sign-in (LDAP / Active Directory)",
        "Mutabakat (Envanter Karşılaştırma)": "Reconciliation (Inventory Comparison)",
        "Giriş Ekranı Logosu": "Login Screen Logo", "Yeni Logo Yükle": "Upload New Logo",
        "Global Kontrol Aralığı (dakika)": "Global Check Interval (minutes)",
        "Bildirim Eşiği (ardışık hata sayısı)": "Notification Threshold (consecutive failures)",
        "Geçmiş Saklama Süresi (gün)": "History Retention (days)", "Email bildirimleri aktif": "Email notifications enabled",
        "SMTP Sunucusu": "SMTP Server", "Gönderen Adres": "Sender Address", "Alıcılar:": "Recipients:",
        "Test Maili Gönder": "Send Test Email", "LDAP Sunucusu (DC host/IP)": "LDAP Server (DC host/IP)",
        "LDAPS kullan (port 636)": "Use LDAPS (port 636)", "Domain (NETBIOS / UPN)": "Domain (NETBIOS / UPN)",
        "Arama Temel DN (BaseDN)": "Search Base DN (BaseDN)",
        "İzin Verilen Güvenlik Grubu (tam DN)": "Allowed Security Group (full DN)",
        "Admin Kullanıcıları (yalnızca Ayarlar erişimi)": "Admin Users (Settings access only)",
        "Senkron Kimliği (grup üyelerini çekmek için)": "Sync Credential (to fetch group members)",
        "Test kullanıcı adı": "Test username", "Test şifre": "Test password", "Test Girişi": "Test Login",
        "İç sertifikalara güven (TLS doğrulamasını gevşet)": "Trust internal certificates (relax TLS validation)",
        "Hesap kilitleme eşiği (başarısız deneme)": "Account lockout threshold (failed attempts)",
        "Kilit süresi (dakika)": "Lockout duration (minutes)", "Denetim kaydı saklama (gün)": "Audit log retention (days)",
        "Mutabakat sekmesini etkinleştir": "Enable Reconciliation tab", "Bizim firma adı": "Our company name",
        "Hizmet aldığımız firma adı": "Vendor company name", "Yeni kimlik ekle": "Add new credential",
        "— Yok —": "— None —", "Tür": "Type",
        "LDAP ile oturum açma zorunlu (kapalıyken uygulama herkese açıktır)": "Require LDAP sign-in (when off, the app is open to everyone)",
        "Kullanıcılar ekranındaki \"LDAP'tan Senkronize Et\" için kullanılır.": "Used for \"Sync from LDAP\" on the Users screen.",
        "ekranından eklediğiniz bir hesabı seçin (Vault destekli olabilir; kullanıcı adı/şifre güvenli biçimde oradan çözülür). Salt-okunur bir AD hesabı yeterlidir.": "select an account you added (it can be Vault-backed; the username/password are resolved securely from there). A read-only AD account is enough.",
        "Oturum boşta kalma zaman aşımı 15 dakika olarak sabittir (PCI DSS 8.2.8). Tüm yönetimsel işlemler": "The idle session timeout is fixed at 15 minutes (PCI DSS 8.2.8). All administrative actions are tracked on the",
        "ekranında izlenir.": "screen.",
        "Kapalıyken sekme hiç kimseye (admin dahil) görünmez. Açıkken yalnızca \"Mutabakat sayfasını görüntüle\" yetkisi verilen kullanıcılar görür — yetkiyi": "When off, the tab is hidden from everyone (including admins). When on, only users granted the \"View reconciliation page\" permission see it — grant it from the",
        "ekranından verin.": "screen.",
        "Henüz entegrasyon yok. Yukarıdaki düğmelerle ekleyin.": "No integrations yet. Add one with the buttons above.",
        "Logoyu Kaldır": "Remove Logo",
        "Şu anda logo tanımlı değil. Giriş ekranında logo gösterilmez.": "No logo is currently set. No logo is shown on the login screen.",
        // Denetim (Audit)
        "Salt-okunur · PCI DSS 10.2 / ISO 27001 A.8.15 / NIST AU-2": "Read-only · PCI DSS 10.2 / ISO 27001 A.8.15 / NIST AU-2",
        "— Tümü —": "— All —", "tümü": "all", "örn. ahmet.yilmaz veya servis adı": "e.g. ahmet.yilmaz or service name",
        // Denetim açıklamaları (DB'de TR veri — sık geçenler)
        "Denetim kaydı görüntülendi": "Audit log viewed", "Admin girişi": "Admin login",
        "Kullanıcı girişi": "User login", "Uygulama başlatıldı.": "Application started.",
        // Entegrasyon düzenleme formu
        "Entegrasyon Düzenle": "Edit Integration", "Şifre ({password})": "Password ({password})",
        "Ek Başlıklar (opsiyonel — her satır \"Anahtar: Değer\")": "Extra Headers (optional — one \"Key: Value\" per line)",
        "•••• kayıtlı — değiştirmek için girin": "•••• saved — enter to change", "Varsa girin": "Enter if any",
        "Şifre girin": "Enter password",
        "ör. 00  (boşsa HTTP 2xx başarı sayılır)": "e.g. 00  (if empty, HTTP 2xx counts as success)",
        "URL ve gövde şablonlarında şu yer tutucuları kullanın; gönderimde gerçek değerlerle değiştirilir:": "Use these placeholders in the URL and body templates; they are replaced with real values when sending:",
        "(alıcı),": "(recipient),", "(gönderen/başlık),": "(sender/header),", "(mesaj),": "(message),",
        "Örnek (NetGSM GET):": "Example (NetGSM GET):",
        // Kimlik bilgisi formu
        "Kimlik Bilgisi Düzenle": "Edit Credential", "Yeni Kimlik Bilgisi": "New Credential",
        "Değiştirmek istemiyorsanız boş bırakın": "Leave empty if you don't want to change it",
        "örn. Oracle ReadOnly İzleme Hesabı": "e.g. Oracle ReadOnly Monitoring Account",
        "FIRMA (AD/DHCP için)": "COMPANY (for AD/DHCP)", "örn. username": "e.g. username", "örn. password": "e.g. password",
        "Kullanıcı adı ve şifre, kontrol anında secret'tan birlikte çekilir. Token, manuel şifrelerle aynı mekanizmayla (DPAPI, LocalMachine) şifrelenerek saklanır. Çekilen bilgiler diske yazılmaz; yalnızca bellekte kullanılır ve 5 dakika önbellekte tutulur.": "The username and password are pulled together from the secret at check time. The token is stored encrypted with the same mechanism as manual passwords (DPAPI, LocalMachine). Retrieved data is never written to disk; it is used only in memory and cached for 5 minutes.",
        // Kullanıcı yetkileri (Perms.All — kodda TR)
        "İzleme ekranlarını görüntüle (Dashboard / Raporlar)": "View monitoring screens (Dashboard / Reports)",
        "Servisleri şimdi kontrol et (manuel tetikleme)": "Check services now (manual trigger)",
        "Uzaktan servis kontrolü (başlat / durdur / yeniden başlat)": "Remote service control (start / stop / restart)",
        "Servisleri yönet (ekle / düzenle / sil / CSV)": "Manage services (add / edit / delete / CSV)",
        "Dashboard'ları yönet (oluştur / düzenle / sil)": "Manage dashboards (create / edit / delete)",
        "Kimlik bilgilerini yönet": "Manage credentials",
        "Mutabakat sayfasını görüntüle (envanter karşılaştırma)": "View the reconciliation page (inventory comparison)",
        "Alarmlara müdahale (WhatsApp/IVR ile servis başlat/durdur/yeniden başlat)": "Manage alarms (start/stop/restart services via WhatsApp/IVR)",

        // Kimlik Bilgileri
        "İzleme hesapları — manuel veya HashiCorp Vault": "Monitoring accounts — manual or HashiCorp Vault",
        "Yeni Kimlik": "New Credential", "Tanım Adı": "Display Name", "Şifre Kaynağı": "Password Source",
        "HashiCorp Vault": "HashiCorp Vault", "Şifre": "Password", "Vault Secret URL": "Vault Secret URL",
        "Vault Token": "Vault Token", "Kullanıcı Adı Anahtarı": "Username Key", "Şifre Anahtarı": "Password Key",
        "Henüz kimlik bilgisi yok.": "No credentials yet.", "Vault bağlantısını test et": "Test Vault connection",

        // Kullanıcılar / Denetim
        "Yetki Düzenle": "Edit Permissions", "Güvenlik grubundaki kullanıcılar ve granüler yetkileri": "Security group members and their granular permissions",
        "LDAP'tan Senkronize Et": "Sync from LDAP", "Rol / Yetkiler": "Role / Permissions", "Son Giriş": "Last Login",
        "Henüz giriş yapmış kullanıcı yok.": "No users have logged in yet.", "Yetkiler": "Permissions",
        "Telefon (WhatsApp/SMS interaktif için)": "Phone (for WhatsApp/SMS interactive)",
        "ADMIN (tüm yetkiler)": "ADMIN (all permissions)", "Denetim Kaydı": "Audit Log",
        "Ara (kullanıcı / hedef / IP / açıklama)": "Search (user / target / IP / detail)",
        "Eylem türü": "Action type", "Son (gün)": "Last (days)", "Kayıt bulunamadı.": "No records found.",

        // Raporlar
        "Erişilebilirlik Raporları": "Availability Reports",
        "Tarih–saat aralıklı geriye dönük analiz ve CSV dışa aktarma": "Date–time ranged historical analysis and CSV export",
        "Sadece kesintili": "Outages only", "Sadece hatalı": "Errors only", "Ad / hedef / etiket ara...": "Search name / target / tag...",
        "Servis": "Service", "Kontrol": "Checks", "Uptime": "Uptime", "Kesinti": "Outages",
        "Toplam Kesinti": "Total Outage", "Ort. Yanıt": "Avg. Response",

        // Mutabakat
        "Bizim envanter": "Our inventory",

        // Hakkında (yapısal)
        "Servis İzleme ve Raporlama Platformu": "Service Monitoring & Reporting Platform",
        "Genel Bilgiler": "General Information", "Geliştiren": "Developed by", "Yapım Tarihi": "Build Date",
        "Haziran 2026": "June 2026", "Versiyon": "Version", "Sürüm notları": "Release notes",
        "Yetenekler": "Capabilities", "Güvenlik": "Security"
    };

    // Sonu " *" ile biten zorunlu-alan etiketleri için baz çeviri + " *"
    const PROSE = {
        // Settings hints / alerts (link içermeyenler)
        "Servis bazında ayrıca özelleştirilebilir.": "Can also be customized per service.",
        "Bu kadar ardışık başarısız kontrol sonrası DOWN maili gönderilir.": "A DOWN email is sent after this many consecutive failed checks.",
        "Daha eski kontrol kayıtları otomatik silinir.": "Older check records are deleted automatically.",
        "Virgül veya noktalı virgül ile ayırın.": "Separate with comma or semicolon.",
        "Relay tabanlı gönderim. Test kayıtlı ayarlarla yapılır — önce kaydedin.": "Relay-based sending. Test uses saved settings — save first.",
        "PCI DSS 8.3.4: en çok 10.": "PCI DSS 8.3.4: max 10.",
        "PCI DSS 8.3.4: en az 30.": "PCI DSS 8.3.4: min 30.",
        "PCI DSS 10.5.1: en az 365.": "PCI DSS 10.5.1: min 365.",
        "Bu adlar Mutabakat ekranındaki başlık ve karşılaştırma kolonlarında görünür.": "These names appear in the Reconciliation screen header and comparison columns.",
        "Bu kadar ardışık başarısız kontrol sonrası DOWN maili gönderilir": "A DOWN email is sent after this many consecutive failed checks",
        // Services form
        "Virgülle birden çok eklenebilir.": "Multiple can be added, comma-separated.",
        "Sunucuda ne çalıştığı vb. Dashboard kartlarında ve raporlarda görünür; CSV'ye de yazılır.": "What runs on the server, etc. Shown on dashboard cards and reports; also written to CSV.",
        "Boşsa Ayarlar'daki global aralık geçerli.": "If empty, the global interval from Settings applies.",
        "Aşılırsa \"YAVAŞ\" işaretlenir.": "If exceeded, marked as \"SLOW\".",
        "Eşik aşılırsa servis DOWN sayılır ve bildirim gider. Boş bırakılan metrik yalnızca ölçülür, alarm üretmez.": "If a threshold is exceeded the service is considered DOWN and a notification is sent. A blank metric is only measured, no alarm.",
        "Bu servis için hangi kanallardan alarm gönderileceğini seçin. Genel anahtarlar (SMTP/SMS) Ayarlar'dadır; burada servis bazında kapatabilirsiniz (ör. test/önemsiz sistemler alarm üretmesin).": "Choose which channels send alarms for this service. Master switches (SMTP/SMS) are in Settings; here you can turn them off per service (e.g. so test/unimportant systems don't alarm).",
        // Credentials
        "LDAP için DOMAIN hesabı veya tam DN; DB için DB kullanıcısı.": "DOMAIN account or full DN for LDAP; DB user for databases.",
        "Secret JSON'ında kullanıcı adını tutan alan.": "The field in the secret JSON holding the username.",
        "Secret JSON'ında şifreyi tutan alan.": "The field in the secret JSON holding the password.",
        // Audit / Users
        "Boş = tüm kayıtlar": "Empty = all records",
        "Değişiklikler kullanıcının bir sonraki girişinde geçerli olur.": "Changes take effect on the user's next login.",
        "Kullanıcının erişebileceği yetenekleri işaretleyin:": "Select the capabilities the user can access:",
        "Bu kullanıcı admin listesinde — tüm yetkilere sahiptir, burada düzenlenemez.": "This user is in the admin list — has all permissions and cannot be edited here.",
        // Ayarlar — uzun yardım/uyarı metinleri
        "Yalnızca bu grubun (ve alt gruplarının) üyeleri giriş yapabilir. Boş bırakılırsa geçerli kimliği olan tüm domain kullanıcıları girebilir.": "Only members of this group (and its nested groups) can sign in. If left empty, all domain users with valid credentials can sign in.",
        "Virgülle ayrılmış kullanıcı adları. Yalnızca bu kişiler Ayarlar / Kullanıcılar sekmelerine girebilir; diğer kullanıcılar kendilerine atanan yetkilerle uygulamayı kullanır. Boş bırakılırsa giriş yapan herkes admin sayılır — kendinizi eklemeyi unutmayın.": "Comma-separated usernames. Only these people can access the Settings / Users tabs; other users use the app with their assigned permissions. If left empty, everyone who signs in is treated as admin — don't forget to add yourself.",
        "Önce ayarları kaydedin, sonra \"Test Girişi\" ile doğrulayın; çalıştığını gördükten sonra \"oturum açma zorunlu\" anahtarını açın. Kendinizi Admin Kullanıcıları listesine eklemeyi unutmayın; aksi halde Ayarlar erişiminiz kısıtlanabilir.": "First save the settings, then verify with \"Test Login\"; once it works, enable the \"require sign-in\" switch. Don't forget to add yourself to the Admin Users list; otherwise your access to Settings may be restricted.",
        "Vault ve LDAPS bağlantılarında sertifika zinciri doğrulaması gevşetilir. Varsayılan kapalı = tam doğrulama (PCI DSS 4.2.1 / NIST SC-8). Yalnızca kurum içi CA sertifikalarınız Windows güven deposunda kayıtlı değilse ve bağlantı doğrulama hatası alıyorsanız geçici olarak açın.": "Relaxes certificate chain validation for Vault and LDAPS connections. Default off = full validation (PCI DSS 4.2.1 / NIST SC-8). Turn on temporarily only if your internal CA certificates are not in the Windows trust store and you get connection validation errors.",
        "SMS / WhatsApp / Sesli arama / IVR entegrasyonlarını buradan tek yerden yönetin — ekleyin, düzenleyin, silin, aktif/pasif yapın. Kod değişikliği gerekmez. Her entegrasyon kendi alıcı listesini kullanabilir.": "Manage your SMS / WhatsApp / Voice Call / IVR integrations from one place — add, edit, delete, enable/disable. No code changes needed. Each integration can use its own recipient list.",
        "PNG, JPG, GIF veya WEBP (en fazla 2 MB). Logo dosyası uygulamayla birlikte gelmez; buradan yüklersiniz ve sunucuda saklanır.": "PNG, JPG, GIF or WEBP (max 2 MB). The logo file is not shipped with the app; you upload it here and it is stored on the server.",
        // Dashboard formu hint'leri
        "Bu tipteki tüm servisler (yeni eklenenler dahil) otomatik girer.": "All services of this type (including newly added ones) are included automatically.",
        "Bu etikete sahip tüm servisler (yeni eklenenler dahil) otomatik girer.": "All services with this tag (including newly added ones) are included automatically.",
        "Tip filtresi, etiket filtresi ve seçili servisler birleşir.": "The type filter, tag filter and selected services are combined.",
        // Entegrasyon formu hint'leri
        "Bu entegrasyon hangi kanaldan gönderir.": "Which channel this integration sends through.",
        "Listede bu adla görünür. \"Twilio\" kullanılamaz.": "Shown with this name in the list. \"Twilio\" cannot be used.",
        "Bu kanala özel alıcılar; virgül/; ile ayırın. Boşsa global SMS/WhatsApp alıcıları kullanılır.": "Recipients specific to this channel; separate with comma/;. If empty, the global SMS/WhatsApp recipients are used.",
        "Yalnızca POST'ta kullanılır.": "Used only with POST.",
        // Kimlik bilgisi formu hint'leri
        "Vault kimliğinde de gerekiyorsa doldurun (AD/DHCP).": "Fill in if also needed for the Vault credential (AD/DHCP).",
        "KV v2 için path'te /data/ segmenti bulunmalı. https:// öneki otomatik eklenir.": "For KV v2 the path must contain a /data/ segment. The https:// prefix is added automatically.",
        // Kullanıcı yetki formu hint'i
        "Bu numaradan gelen WhatsApp buton komutları bu kullanıcıyla eşlenir; işlem yapabilmesi için aşağıdaki \"Alarmlara müdahale\" yetkisi gerekir.": "WhatsApp button commands coming from this number are matched to this user; the \"Manage alarms\" permission below is required to perform actions.",

        // About
        "Servis İzleme ve Raporlama Platformu": "Service Monitoring & Reporting Platform",
        "Twilio onaylı WhatsApp şablonunun Content SID'i. Doluysa alarm butonlu (Başlat/Yeniden/Durdur) interaktif şablon olarak gider; boşsa düz metin gönderilir. Şablonu değiştirmek için yenisini buraya yazıp kaydedin.": "Content SID of the Twilio-approved WhatsApp template. If set, the alarm is sent as an interactive template with buttons (Start/Restart/Stop); if empty, plain text is sent. To change the template, enter a new one here and save.",

        // Servis formu — tür yardımları (JS ile yazılır)
        "URL'e GET atılır; durum kodu ve yanıt süresi kontrol edilir. Exchange OWA için: https://mail.firma.local/owa/healthcheck.htm": "A GET is sent to the URL; status code and response time are checked. For Exchange OWA: https://mail.firma.local/owa/healthcheck.htm",
        "Yalnızca TCP portunun açık olduğu doğrulanır.": "Only verifies that the TCP port is open.",
        "İzleme kullanıcısıyla gerçek login + SELECT 1 çalıştırılır.": "Performs a real login with the monitoring user + runs SELECT 1.",
        "Read-only izleme kullanıcısıyla login + SELECT 1 FROM DUAL çalıştırılır.": "Logs in with a read-only monitoring user + runs SELECT 1 FROM DUAL.",
        "Domain Controller'a yetkili kullanıcıyla gerçek bind yapılır. LDAPS için SSL işaretleyin (port 636).": "Performs a real bind to the Domain Controller with an authorized user. Check SSL for LDAPS (port 636).",
        "Hedef DNS sunucusuna doğrudan A kaydı sorgusu atılır (OS resolver kullanılmaz).": "Sends an A-record query directly to the target DNS server (OS resolver is not used).",
        "Yetkili kullanıcıyla gerçek SSH/SFTP oturumu açılır ve dizin listelenir.": "Opens a real SSH/SFTP session with an authorized user and lists a directory.",
        "Uzak Windows sunucusunda DHCP Server servisinin Running olduğu WMI ile doğrulanır.": "Verifies via WMI that the DHCP Server service is Running on the remote Windows server.",
        "SMTP'ye bağlanır, 220 banner + EHLO/250 doğrular. On-prem Exchange: mail sunucu:25. Exchange Online: smtp.office365.com:25.": "Connects to SMTP, validates the 220 banner + EHLO/250. On-prem Exchange: mail server:25. Exchange Online: smtp.office365.com:25.",
        "ICMP echo gönderilir; yanıt gelirse UP.": "Sends an ICMP echo; UP if a reply is received.",
        "WMI ile uzak Windows sunucusundan CPU yükü, RAM ve disk doluluk oranları okunur; eşikler aşılırsa alarm üretilir.": "Reads CPU load, RAM and disk usage from the remote Windows server via WMI; raises an alarm if thresholds are exceeded.",
        "SSH ile bağlanıp /proc/stat, free ve df okunur (root gerekmez); eşikler aşılırsa alarm üretilir.": "Connects via SSH and reads /proc/stat, free and df (no root needed); raises an alarm if thresholds are exceeded.",
        "IMAP'a bağlanır, \"* OK\" greeting'i doğrular. Exchange Online: outlook.office365.com:993 + SSL.": "Connects to IMAP, validates the \"* OK\" greeting. Exchange Online: outlook.office365.com:993 + SSL.",
        "Uzak Windows sunucusunda belirtilen servis çalışıyor mu (WMI) kontrol edilir; dashboard'dan start/stop/restart yapılabilir.": "Checks via WMI whether the specified service is running on the remote Windows server; start/stop/restart available from the dashboard.",
        "Uzak Linux sunucusunda systemd servis durumu (SSH) kontrol edilir; dashboard'dan start/stop/restart yapılabilir (sudo gerekir).": "Checks the systemd service status (SSH) on the remote Linux server; start/stop/restart available from the dashboard (sudo required).",
        // port/extra yardımları
        "Zorunlu.": "Required.", "Opsiyonel.": "Optional.",
        "Boşsa 3306.": "Default 3306 if empty.", "Boşsa 1433.": "Default 1433 if empty.",
        "Boşsa 1521.": "Default 1521 if empty.", "Boşsa 53.": "Default 53 if empty.", "Boşsa 22.": "Default 22 if empty.",
        "Boşsa 389 (LDAP) / 636 (SSL işaretliyse).": "Default 389 (LDAP) / 636 (if SSL checked).",
        "Boşsa 25. Implicit TLS (465) için SSL işaretleyin.": "Default 25 if empty. Check SSL for implicit TLS (465).",
        "Boşsa 143 / 993 (SSL işaretliyse).": "Default 143 / 993 (if SSL checked).",
        "Boşsa 2xx/3xx başarılı sayılır. örn. 200": "If empty, 2xx/3xx counts as success. e.g. 200",
        "örn. ORCLPDB1 — SID kullanılacaksa: SID=ORCL": "e.g. ORCLPDB1 — to use a SID: SID=ORCL",
        "örn. intranet.firma.local — bu kayıt çözülürse UP.": "e.g. intranet.company.local — UP if this record resolves.",
        "Boşsa DHCPServer.": "Default DHCPServer if empty.",
        "Opsiyonel — yanıt bu süreden uzunsa DOWN sayılır.": "Optional — counts as DOWN if the reply takes longer than this.",
        "örn. W3SVC (IIS), MSSQLSERVER, Spooler": "e.g. W3SVC (IIS), MSSQLSERVER, Spooler",
        "örn. nginx, httpd, docker": "e.g. nginx, httpd, docker",
        "Named instance için HOST\\INSTANCE yazın (port boş bırakın).": "For a named instance write HOST\\INSTANCE (leave port empty).",
        // hedef yardımları
        "örn. https://intranet.firma.local/health": "e.g. https://intranet.company.local/health",
        "örn. mail.firma.local veya smtp.office365.com": "e.g. mail.company.local or smtp.office365.com",
        "örn. mail.firma.local veya outlook.office365.com": "e.g. mail.company.local or outlook.office365.com"
    };

    const PREFIX = {
        "Son yenileme: ": "Last refresh: "
    };

    const NORM = s => s.replace(/\s+/g, " ").trim();

    // Boşluk-normalize sözlük (çok satırlı / fazla boşluklu metin düğümleri de eşleşsin)
    const NORMMAP = {};
    for (const k in TEXT) NORMMAP[NORM(k)] = TEXT[k];

    // Sonek eşlemeleri (dinamik önekli/sayılı metinler — örn. "123 kayıt gösteriliyor")
    const SUFFIX = { " kayıt gösteriliyor": " records shown" };

    // Alt-dize değişimleri (dinamik denetim açıklamaları — örn. "start → İşlem gönderildi (start).")
    const REPLACE = { "İşlem gönderildi": "Action sent" };

    function translateText(value) {
        const key = value.trim();
        if (!key) return null;
        let en = TEXT[key];
        if (en == null && /\*$/.test(key)) {
            const base = key.replace(/\s*\*$/, "").trim();
            if (TEXT[base] != null) en = TEXT[base] + " *";
        }
        if (en != null) return value.replace(key, en);

        // Boşluk-normalize eşleşme (parçalı/çok satırlı düğümler)
        const nm = NORMMAP[NORM(value)];
        if (nm != null) {
            const lead = /^\s/.test(value) ? " " : "";
            const trail = /\s$/.test(value) ? " " : "";
            return lead + nm + trail;
        }
        for (const p in PREFIX) { if (key.startsWith(p)) return value.replace(p, PREFIX[p]); }
        for (const s in SUFFIX) { if (key.endsWith(s)) return value.replace(s, SUFFIX[s]); }
        for (const r in REPLACE) { if (key.includes(r)) return value.split(r).join(REPLACE[r]); }
        return null;
    }

    const SKIP = new Set(["SCRIPT", "STYLE", "TEXTAREA", "NOSCRIPT"]);
    const PROSE_SEL = ".hint, .alert-info, .alert-warn, .alert-ok, .alert-err, li, p";

    function isLeafProse(el) {
        return !el.querySelector("input,select,textarea,button,form,table,ul,ol,div,svg,canvas,a");
    }

    function translateProse(root) {
        const els = [];
        if (root.matches && root.matches(PROSE_SEL)) els.push(root);
        if (root.querySelectorAll) root.querySelectorAll(PROSE_SEL).forEach(e => els.push(e));
        els.forEach(el => {
            if (!isLeafProse(el)) return;
            const en = PROSE[NORM(el.textContent)];   // EN sonuç tekrar eşleşmez → döngü olmaz
            if (en != null) el.textContent = en;
        });
    }

    function translateNodes(root) {
        const w = document.createTreeWalker(root, NodeFilter.SHOW_TEXT, {
            acceptNode(n) {
                return n.parentNode && !SKIP.has(n.parentNode.nodeName) && n.nodeValue.trim()
                    ? NodeFilter.FILTER_ACCEPT : NodeFilter.FILTER_REJECT;
            }
        });
        const nodes = []; let n;
        while ((n = w.nextNode())) nodes.push(n);
        nodes.forEach(node => { const v = translateText(node.nodeValue); if (v !== null) node.nodeValue = v; });
    }

    function translateAttrs(root) {
        if (!root.querySelectorAll) return;
        root.querySelectorAll("[placeholder],[title]").forEach(el => {
            ["placeholder", "title"].forEach(a => {
                if (el.hasAttribute(a)) { const en = TEXT[el.getAttribute(a).trim()]; if (en != null) el.setAttribute(a, en); }
            });
        });
    }

    function run(root) {
        try {
            if (root.nodeType === 1) { translateProse(root); translateAttrs(root); }
            translateNodes(root);
        } catch (e) { /* yut */ }
    }

    function start() {
        run(document.body);
        const mo = new MutationObserver(muts => {
            const seen = new Set();
            muts.forEach(m => {
                const t = (m.target && m.target.nodeType === 1) ? m.target : null;
                if (t && !seen.has(t)) { seen.add(t); run(t); }
            });
        });
        mo.observe(document.body, { childList: true, subtree: true });
    }

    if (document.body) start();
    else document.addEventListener("DOMContentLoaded", start);
})();
