<#
.SYNOPSIS
  vMon uninstall. By default files/Data are KEPT; add -RemoveData to delete everything.

.EXAMPLE
  .\uninstall.ps1 -Mode Service -ServiceName vMon
  .\uninstall.ps1 -Mode IIS -SiteName vMon -Path "C:\inetpub\wwwroot\vMon" -RemoveData
#>
param(
    [ValidateSet('IIS','Service')] [string]$Mode = 'Service',
    [string]$Path = 'C:\Program Files\vMon',
    [string]$SiteName = 'vMon',
    [string]$ServiceName = 'vMon',
    [switch]$RemoveData
)
$ErrorActionPreference = 'Stop'

if ($Mode -eq 'IIS') {
    Import-Module WebAdministration
    if (Test-Path "IIS:\Sites\$SiteName") { Remove-Website -Name $SiteName }
    if (Test-Path "IIS:\AppPools\$SiteName") { Remove-WebAppPool -Name $SiteName }
}
else {
    if (Get-Service $ServiceName -ErrorAction SilentlyContinue) {
        Stop-Service $ServiceName -Force -ErrorAction SilentlyContinue
        & sc.exe delete $ServiceName | Out-Null
    }
}

if ($RemoveData) {
    if (Test-Path $Path) { Remove-Item $Path -Recurse -Force }
    Write-Host "vMon and ALL data removed: $Path" -ForegroundColor Yellow
} else {
    Write-Host "vMon removed. Files/Data kept: $Path" -ForegroundColor Green
}
