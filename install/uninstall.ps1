<#
.SYNOPSIS
  vMon kaldırma. Varsayılan olarak dosyalar/Data KORUNUR; tamamen silmek için -RemoveData ekle.

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
    Write-Host "vMon ve TÜM veriler kaldırıldı: $Path" -ForegroundColor Yellow
} else {
    Write-Host "vMon kaldırıldı. Dosyalar/Data korundu: $Path" -ForegroundColor Green
}
