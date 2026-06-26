<#
.SYNOPSIS
  vMon güncelleme — yeni sürümün dosyalarını yerine koyar. Data ve appsettings.json KORUNUR.
  Release zip'ini çıkardığın klasörde (yanında 'app' olmalı) çalıştır.

.EXAMPLE
  .\upgrade.ps1 -Mode Service -ServiceName vMon -Path "C:\Program Files\vMon"
  .\upgrade.ps1 -Mode IIS -SiteName vMon -Path "C:\inetpub\wwwroot\vMon"
#>
param(
    [ValidateSet('IIS','Service')] [string]$Mode = 'Service',
    [string]$Path = 'C:\Program Files\vMon',
    [string]$SiteName = 'vMon',
    [string]$ServiceName = 'vMon'
)
$ErrorActionPreference = 'Stop'
$src = Join-Path $PSScriptRoot 'app'
if (-not (Test-Path $src)) { throw "Kaynak 'app' klasörü bulunamadı: $src" }

# /XD Data → veritabanı korunur ; /XF appsettings.json → ortam ayarın (RequireHttps vb.) korunur
if ($Mode -eq 'IIS') {
    Set-Content (Join-Path $Path 'app_offline.htm') '<h1>Guncelleniyor...</h1>'
    robocopy $src $Path /E /XD Data /XF appsettings.json app_offline.htm | Out-Null
    Remove-Item (Join-Path $Path 'app_offline.htm') -Force -ErrorAction SilentlyContinue
}
else {
    Stop-Service $ServiceName -Force
    robocopy $src $Path /E /XD Data /XF appsettings.json | Out-Null
    Start-Service $ServiceName
}
Write-Host "Güncelleme TAMAM ($Mode)." -ForegroundColor Green
