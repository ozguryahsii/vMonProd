<#
.SYNOPSIS
  vMon installer - IIS or Windows Service mode. Run inside the extracted release folder
  (the 'app' subfolder must be next to this script). Requires an elevated (Administrator) PowerShell.

.EXAMPLE
  # Windows Service (plain HTTP, e.g. behind a reverse proxy) - easiest, no dependencies:
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
    [switch]$Https
)
$ErrorActionPreference = 'Stop'

$src = Join-Path $PSScriptRoot 'app'
if (-not (Test-Path $src)) { throw "Source 'app' folder not found: $src (run inside the extracted release folder)" }

Write-Host "Installing vMon -> Mode: $Mode, Target: $Path" -ForegroundColor Cyan

# 1) Copy files (existing Data is preserved)
New-Item -ItemType Directory -Force $Path | Out-Null
New-Item -ItemType Directory -Force (Join-Path $Path 'Data') | Out-Null
robocopy $src $Path /E /XD Data | Out-Null
if ($LASTEXITCODE -ge 8) { throw "File copy failed (robocopy $LASTEXITCODE)" }

if ($Mode -eq 'IIS') {
    Import-Module WebAdministration
    if (-not (Get-WebGlobalModule -Name 'AspNetCoreModuleV2' -ErrorAction SilentlyContinue)) {
        Write-Warning "ASP.NET Core Hosting Bundle (AspNetCoreModuleV2) not found - REQUIRED for IIS."
        Write-Warning "Install: https://dotnet.microsoft.com/download/dotnet/8.0  ->  Hosting Bundle, then run: iisreset"
    }
    if (-not (Test-Path "IIS:\AppPools\$SiteName")) { New-WebAppPool -Name $SiteName | Out-Null }
    Set-ItemProperty "IIS:\AppPools\$SiteName" -Name managedRuntimeVersion -Value ''   # No Managed Code
    if (Test-Path "IIS:\Sites\$SiteName") { Remove-Website -Name $SiteName }
    if ($HostName) { New-Website -Name $SiteName -PhysicalPath $Path -ApplicationPool $SiteName -Port $Port -HostHeader $HostName | Out-Null }
    else           { New-Website -Name $SiteName -PhysicalPath $Path -ApplicationPool $SiteName -Port $Port | Out-Null }
    icacls $Path /grant "IIS AppPool\${SiteName}:(OI)(CI)M" /T | Out-Null
    Start-Website -Name $SiteName
    $disp = if ($HostName) { "http(s)://$HostName" } else { "http(s)://<server>:$Port" }
    Write-Host "`nIIS install DONE. Open in browser: $disp  (the /Setup wizard appears)" -ForegroundColor Green
    Write-Host "Note: for HTTPS add an HTTPS binding + certificate to this site in IIS Manager." -ForegroundColor Yellow
}
else {
    # Windows Service. Plain HTTP -> RequireHttps=false so login works over HTTP. With -Https leave it as-is.
    if (-not $Https) {
        $cfg = Join-Path $Path 'appsettings.json'
        try {
            $j = Get-Content $cfg -Raw | ConvertFrom-Json
            if (-not ($j.PSObject.Properties.Name -contains 'Hosting')) { $j | Add-Member -NotePropertyName Hosting -NotePropertyValue ([pscustomobject]@{}) }
            $j.Hosting | Add-Member -NotePropertyName RequireHttps -NotePropertyValue $false -Force
            ($j | ConvertTo-Json -Depth 12) | Set-Content $cfg -Encoding UTF8
        } catch { Write-Warning "Could not update appsettings.json (RequireHttps): $_" }
    }
    $exe = Join-Path $Path 'vMonitor.exe'
    $bin = "`"$exe`" --urls http://*:$Port"
    if (Get-Service $ServiceName -ErrorAction SilentlyContinue) {
        Stop-Service $ServiceName -Force -ErrorAction SilentlyContinue
        & sc.exe delete $ServiceName | Out-Null; Start-Sleep -Seconds 2
    }
    New-Service -Name $ServiceName -BinaryPathName $bin -DisplayName 'vMon Monitoring' -StartupType Automatic | Out-Null
    # Kurulum sonrasi self-restart icin servis kurtarma: sifirdan-farkli kodla cikinca SCM yeniden baslatir
    & sc.exe failure $ServiceName reset= 86400 actions= restart/3000 | Out-Null
    & sc.exe failureflag $ServiceName 1 | Out-Null
    Start-Service $ServiceName
    Write-Host "`nService install DONE. Open in browser: http://<server>:$Port  (the /Setup wizard appears)" -ForegroundColor Green
}
