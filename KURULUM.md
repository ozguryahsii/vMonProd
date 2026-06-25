# vMonitor — Kurulum ve Yayınlama

Servis izleme uygulaması (ASP.NET Core 8.0 MVC + SQLite, IIS InProcess).
CertBridge/SLLTracker ile aynı yayınlama yaklaşımı; tamamen ayrı site ve DB.

## Yayınlama (lokal makinede)

```powershell
cd C:\Users\ozgur.yahsi\vMonitor
dotnet publish -c Release -o C:\publish\vMonitor
```

> Not: SDK kullanıcı profiline kurulu — `dotnet` tanınmazsa önce:
> `$env:PATH = "$env:USERPROFILE\.dotnet;$env:PATH"`

## Sunucuya kopyalama

```powershell
# Sunucudaki hedef klasöre kopyala (mevcut DB'yi EZME!)
Copy-Item C:\publish\vMonitor\* D:\Sites\vMonitor\ -Recurse -Force -Exclude monitoring.db
iisreset
```

`Data\monitoring.db` publish çıktısına dahil edilmez (csproj'da hariç tutuldu);
ilk açılışta uygulama otomatik oluşturur. Sonraki kopyalamalarda sunucudaki
DB olduğu gibi kalır.

## IIS — ilk kurulum (tek seferlik)

1. .NET 8 Hosting Bundle sunucuda kurulu olmalı (CertBridge için zaten kurulu).
2. Yeni Application Pool: **vMonitor** — .NET CLR Version: *No Managed Code*.
3. Yeni Site: **vMonitor**, fiziksel yol `D:\Sites\vMonitor`, farklı bir port (örn. 8090).
4. App Pool kimliğine site klasöründe **yazma izni** verin
   (SQLite DB + `logs\` klasörü için): `IIS AppPool\vMonitor`.
5. DHCP kontrolü (WMI) ve MSSQL Windows auth kullanılacaksa App Pool Identity'yi
   yetkili bir domain hesabı yapmak gerekebilir; aksi halde uygulama içinden
   kimlik bilgisi tanımlayın.

## İlk yapılandırma (UI)

1. **Ayarlar** → kontrol aralığı, SMTP relay sunucusu, gönderen/alıcılar,
   bildirim eşiği. "Test Maili Gönder" ile doğrulayın (önce Kaydet).
2. **Kimlik Bilgileri** → izleme hesaplarını ekleyin
   (Oracle read-only kullanıcı, AD bind hesabı, SFTP hesabı, DHCP için domain hesabı...).
   Şifreler DPAPI (LocalMachine) ile şifrelenir — **DB başka sunucuya taşınırsa
   şifreler çözülmez, yeniden girilmeleri gerekir.**
3. **Servisler** → izlenecek servisleri tanımlayın, kimlik bilgisi atayın.

## Servis tipleri

| Tip | Test | Hedef | Ekstra alanı |
|---|---|---|---|
| HTTP | GET + durum kodu | tam URL | beklenen kod (boş = 2xx/3xx) |
| MySQL | login + `SELECT 1` | host | DB adı (ops.) |
| MSSQL | login + `SELECT 1` | host (veya HOST\INSTANCE) | DB adı (ops.) |
| Oracle | login + `SELECT 1 FROM DUAL` | host | service name (SID için `SID=XXX`) |
| AD/LDAP(S) | kimlik doğrulamalı bind | DC host | — (636 + SSL işaretle = LDAPS) |
| DNS | hedef sunucuya A kaydı sorgusu | DNS sunucu IP | test hostname |
| SFTP | gerçek login + dizin listeleme | host | — |
| DHCP | WMI ile Windows servis durumu | DHCP sunucusu | servis adı (boş = DHCPServer) |
| SMTP | banner + EHLO doğrulama | mail sunucusu (Exchange / smtp.office365.com) | — |
| IMAP | greeting doğrulama | mail sunucusu (993 + SSL = IMAPS) | — |
| TCP | port açık mı | host | — |

Exchange izleme önerisi: on-prem için SMTP (port 25) + OWA sağlığı için HTTP tipi
(`https://mail.firma.local/owa/healthcheck.htm`, beklenen kod 200). Exchange Online
için SMTP (`smtp.office365.com`) ve IMAP (`outlook.office365.com:993` + SSL).

## Notlar

- BackgroundService her dakika uyanır, aralığı dolan servisleri (en fazla 5 paralel) kontrol eder.
- DOWN maili "ardışık hata eşiği" dolunca bir kez gider; düzelince RECOVERED maili (kesinti süresiyle) gider.
- Geçmiş kayıtları, Ayarlar'daki saklama süresinden (varsayılan 365 gün) eski ise günlük olarak silinir.
- **Raporlar** sayfası: tarih aralığı seçerek (24 saat – 1 yıl) servis bazında uptime %,
  kesinti sayısı/süresi, günlük erişilebilirlik şeridi ve CSV dışa aktarma.
- `/api/*` hataları düz metin döner (HTML değil).

## Uyumluluk Notları (PCI DSS / ISO 27001 / NIST)

Uygulama kod tarafında karşılananlar (v1.5.x): denetim kaydı (PCI 10.2/10.3), denetim
kaydına ve yetkisiz erişim girişimlerine ait loglama (10.2.1.3/10.2.1.4), giden TLS
doğrulaması (4.2.1 / SC-8), hesap kilitleme (8.3.4), 15 dk oturum zaman aşımı (8.2.8),
CSRF + güvenlik başlıkları + HSTS, sırların DPAPI/Vault ile korunması, AD grubundan
düşen kullanıcının pasifleştirilmesi (8.2.4-8.2.5), yetkili erişim bandı (AC-8).

Aşağıdakiler **altyapı/işletim katmanında** ele alınır (uygulama kodu kapsamı dışı) —
denetimde bu notlar gerekçe olarak kullanılabilir:

1. **Zaman senkronizasyonu (PCI 10.6):** Sunucu domain'e bağlı; saat AD/NTP ile senkronize.
   Denetim zaman damgaları sunucunun yerel (senkron) saatinden üretilir.
2. **TLS sürüm/şifre paketi (PCI 4.2.1):** TLS 1.2+ zorunlu, zayıf cipher'lar kapalı —
   Windows/IIS (Schannel) seviyesinde yönetilir; sunucu sıkılaştırma standardına tabidir.
3. **Denetim kaydı bütünlüğü (PCI 10.5.2 / 10.3.4):** Kayıtlar UI'dan değiştirilemez/silinemez
   (salt-ekleme). Değiştirilemezlik için kurum SIEM'i varsa Windows Event/syslog forwarder ile
   merkezî SIEM'e iletilmesi önerilir; yoksa DB dosyasına erişim OS yetkileriyle kısıtlanır.
4. **Çok faktörlü kimlik doğrulama (PCI 8.4/8.5):** vMon kimlik doğrulamayı AD/LDAP'a devreder.
   MFA gerekiyorsa (uygulama PCI CDE kapsamındaysa) AD/SSO/Conditional Access seviyesinde sağlanır;
   uygulamaya gömülmez. CDE dışıysa kapsam dışıdır.
5. **Pasif hesap devre dışı bırakma (PCI 8.2.6):** AD tarafında yönetilir; ayrıca vMon, güvenlik
   grubundan düşen kullanıcıyı senkronizasyonda pasifleştirir ve girişini engeller.
6. **Durağan veri şifrelemesi (data-at-rest):** `monitoring.db` için OS düzeyinde **BitLocker**
   önerilir (risk kabulü — bkz. Hakkında sürüm notları).
7. **İçerik Güvenlik Politikası (CSP):** Tailwind Play CDN nedeniyle tam CSP uygulanmadı
   (yalnızca `frame-ancestors`); ileride statik Tailwind derlemesiyle sıkılaştırılabilir (risk kabulü).
8. **API oran sınırlama (NIST SC-5):** Uygulama katmanında uygulanmadı (çok sayıda servis +
   tek kurumsal IP nedeniyle yanlış pozitif/DoS-benzeri kesinti riski); DoS koruması ağ/WAF/IIS
   katmanında ele alınır (risk kabulü).
