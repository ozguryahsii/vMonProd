# vMon — Kurulum

Bu paket **kendi içinde çalışır** (self-contained): hedef sunucuda .NET kurulu olmasına gerek yoktur.
Yönetici (Administrator) PowerShell ile çalıştırın.

## İçerik
- `app/` — uygulama dosyaları
- `install.ps1` / `upgrade.ps1` / `uninstall.ps1` — kurulum scriptleri

---

## Seçenek 1 — Windows Service (en kolay, IIS gerekmez)
Bağımlılık yok. Düz HTTP bir port açar (TLS için önüne ters proxy koyabilirsiniz).

```powershell
.\install.ps1 -Mode Service -Port 8080
```
Sonra tarayıcıda: `http://<sunucu>:8080` → **kurulum sihirbazı** (veritabanı / şirket / yönetici / SMTP).

TLS'i doğrudan Kestrel'de istiyorsan `-Https` ekle ve sertifikayı yapılandır (ileri seviye).

## Seçenek 2 — IIS
Önkoşul: **ASP.NET Core 8 Hosting Bundle** (https://dotnet.microsoft.com/download/dotnet/8.0 → Hosting Bundle).

```powershell
.\install.ps1 -Mode IIS -SiteName vMon -HostName vmon.firma.local -Port 80 -Path "C:\inetpub\wwwroot\vMon"
```
- App pool otomatik **No Managed Code** oluşturulur, Data izni verilir.
- HTTPS için IIS'te bu siteye bir **HTTPS binding + sertifika** ekleyin.
- Sonra tarayıcıda site adresini aç → **kurulum sihirbazı**.

---

## Güncelleme (yeni sürüm)
Yeni release zip'ini çıkar, içinden:
```powershell
.\upgrade.ps1 -Mode Service -ServiceName vMon -Path "C:\Program Files\vMon"
# veya
.\upgrade.ps1 -Mode IIS -SiteName vMon -Path "C:\inetpub\wwwroot\vMon"
```
**Veritabanı (Data) ve `appsettings.json` korunur.**

## Kaldırma
```powershell
.\uninstall.ps1 -Mode Service -ServiceName vMon                 # veriyi korur
.\uninstall.ps1 -Mode IIS -SiteName vMon -Path "..." -RemoveData  # her şeyi siler
```

---

## İlk kurulum sihirbazı
İlk açılışta uygulama `/Setup`'a yönlendirir ve şunları toplar:
1. **Veritabanı** (SQLite / SQL Server / PostgreSQL / MySQL / Oracle) + bağlantı testi
2. **Şirket adı**
3. **Yönetici hesabı** (şifreli yerel giriş)
4. **SMTP** (opsiyonel, test ile)

Tamamlanınca uygulama yeniden başlar ve giriş ekranı gelir.
