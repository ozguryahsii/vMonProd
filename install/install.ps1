<#
.SYNOPSIS
  vMon kurulum scripti — IIS veya Windows Service modunda. Release zip'ini çıkardığın klasörde çalıştır
  (yanında 'app' klasörü olmalı). Yönetici PowerShell gerekir.

.EXAMPLE
  # Windows Service (düz HTTP, örn. ters proxy arkası) — en kolay, bağımlılıksız:
  .\install.ps1 -Mode Service -Port 8080

.EXAMPLE
  # IIS:
  .\install.ps1 -Mode IIS -SiteName vMon -HostName vmon.firma.local -Port 80 -Path "C:\inetpub\wwwroot\vMon"
#>
param(
    [ValidateSet('IIS','Service')] [string]$Mode = 'Service',
    [string]$Path = 'C:\Program Files\vMon',
    [int]$Port = 8080,
    [string]$HostName = '',
    [string]$SiteName = 'vMon',
    [string]$ServiceName = 'vMon',
    [switch]$Https   # Service modunda HTTPS bekleniyorsa (RequireHttps=true bırakılır)
)
$ErrorActionPreference = 'Stop'

$src = Join-Path $PSScriptRoot 'app'
if (-not (Test-Path $src)) { throw "Kaynak 'app' klasörü bulunamadı: $src (release zip'ini çıkardığın yerde çalıştır)" }

Write-Host "vMon kuruluyor → Mod: $Mode, Hedef: $Path" -ForegroundColor Cyan

# 1) Dosyaları kopyala (mevcut Data korunur)
New-Item -ItemType Directory -Force $Path | Out-Null
New-Item -ItemType Directory -Force (Join-Path $Path 'Data') | Out-Null
robocopy $src $Path /E /XD Data | Out-Null
if ($LASTEXITCODE -ge 8) { throw "Dosya kopyalama başarısız (robocopy $LASTEXITCODE)" }

if ($Mode -eq 'IIS') {
    Import-Module WebAdministration
    if (-not (Get-WebGlobalModule -Name 'AspNetCoreModuleV2' -ErrorAction SilentlyContinue)) {
        Write-Warning "ASP.NET Core Hosting Bundle (AspNetCoreModuleV2) bulunamadı — IIS için GEREKLİ."
        Write-Warning "Kur: https://dotnet.microsoft.com/download/dotnet/8.0  → 'ASP.NET Core Runtime → Hosting Bundle'"
    }
    if (-not (Test-Path "IIS:\AppPools\$SiteName")) { New-WebAppPool -Name $SiteName | Out-Null }
    Set-ItemProperty "IIS:\AppPools\$SiteName" -Name managedRuntimeVersion -Value ''   # No Managed Code
    if (Test-Path "IIS:\Sites\$SiteName") { Remove-Website -Name $SiteName }
    if ($HostName) { New-Website -Name $SiteName -PhysicalPath $Path -ApplicationPool $SiteName -Port $Port -HostHeader $HostName | Out-Null }
    else           { New-Website -Name $SiteName -PhysicalPath $Path -ApplicationPool $SiteName -Port $Port | Out-Null }
    icacls $Path /grant "IIS AppPool\$SiteName:(OI)(CI)M" /T | Out-Null
    Start-Website -Name $SiteName
    $disp = if ($HostName) { "http(s)://$HostName" } else { "http(s)://<sunucu>:$Port" }
    Write-Host "`nIIS kurulumu TAMAM. Tarayıcıda aç → $disp  (kurulum sihirbazı /Setup açılır)" -ForegroundColor Green
    Write-Host "Not: HTTPS için IIS'te bu siteye bir HTTPS binding + sertifika ekleyin." -ForegroundColor Yellow
}
else {
    # Windows Service. Düz-HTTP ise RequireHttps=false (HTTP'de login çalışsın). -Https verilirse dokunma.
    if (-not $Https) {
        $cfg = Join-Path $Path 'appsettings.json'
        try {
            $j = Get-Content $cfg -Raw | ConvertFrom-Json
            if (-not ($j.PSObject.Properties.Name -contains 'Hosting')) { $j | Add-Member -NotePropertyName Hosting -NotePropertyValue ([pscustomobject]@{}) }
            $j.Hosting | Add-Member -NotePropertyName RequireHttps -NotePropertyValue $false -Force
            ($j | ConvertTo-Json -Depth 12) | Set-Content $cfg -Encoding UTF8
        } catch { Write-Warning "appsettings.json güncellenemedi (RequireHttps): $_" }
    }
    $exe = Join-Path $Path 'vMonitor.exe'
    $bin = "`"$exe`" --urls http://*:$Port"
    if (Get-Service $ServiceName -ErrorAction SilentlyContinue) {
        Stop-Service $ServiceName -Force -ErrorAction SilentlyContinue
        & sc.exe delete $ServiceName | Out-Null; Start-Sleep -Seconds 2
    }
    New-Service -Name $ServiceName -BinaryPathName $bin -DisplayName 'vMon Monitoring' -StartupType Automatic | Out-Null
    Start-Service $ServiceName
    Write-Host "`nServis kurulumu TAMAM. Tarayıcıda aç → http://<sunucu>:$Port  (kurulum sihirbazı /Setup açılır)" -ForegroundColor Green
}
