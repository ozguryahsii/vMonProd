<#
.SYNOPSIS
  vMon upgrade - replaces app files with the new version. Data and appsettings.json are PRESERVED.
  Run inside the extracted release folder (the 'app' subfolder must be next to this script).

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
if (-not (Test-Path $src)) { throw "Source 'app' folder not found: $src" }

# /XD Data -> database preserved ; /XF appsettings.json -> your environment config (RequireHttps etc.) preserved
if ($Mode -eq 'IIS') {
    Set-Content (Join-Path $Path 'app_offline.htm') '<h1>Updating...</h1>'
    robocopy $src $Path /E /XD Data /XF appsettings.json app_offline.htm | Out-Null
    Remove-Item (Join-Path $Path 'app_offline.htm') -Force -ErrorAction SilentlyContinue
}
else {
    Stop-Service $ServiceName -Force
    robocopy $src $Path /E /XD Data /XF appsettings.json | Out-Null
    Start-Service $ServiceName
}
Write-Host "Upgrade DONE ($Mode)." -ForegroundColor Green
