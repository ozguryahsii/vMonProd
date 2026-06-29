# vMon tek-komut guncelleme: en son (veya verilen) release zip'ini indirir, ayiklar ve upgrade.ps1 calistirir.
# Kullanim:  .\update-vmon.ps1 -Token "PAT"            (en son surum)
#            .\update-vmon.ps1 -Token "PAT" -Tag v2.12.3  (belirli surum)
param(
    [string]$Token = "PAT_BURAYA",
    [string]$Repo = "ozguryahsii/vMontest",
    [string]$ServiceName = "vMon",
    [string]$Tag = ""
)
$ErrorActionPreference = "Stop"
$hdr = @{ Authorization = "token $Token"; "User-Agent" = "vmon" }

$relUrl = if ($Tag) { "https://api.github.com/repos/$Repo/releases/tags/$Tag" }
          else { "https://api.github.com/repos/$Repo/releases/latest" }
$rel = Invoke-RestMethod $relUrl -Headers $hdr
$asset = $rel.assets | Where-Object { $_.name -like "*win-x64.zip" } | Select-Object -First 1
if (-not $asset) { throw "Release zip'i bulunamadi (build henuz bitmemis olabilir)." }
Write-Host "Surum: $($rel.tag_name)  Dosya: $($asset.name)" -ForegroundColor Green

$zip = Join-Path $env:TEMP $asset.name
Invoke-WebRequest -Uri $asset.url -Headers ($hdr + @{ Accept = "application/octet-stream" }) -OutFile $zip
$dest = Join-Path $env:TEMP "vMon-update"
Remove-Item $dest -Recurse -Force -ErrorAction SilentlyContinue
Expand-Archive $zip -DestinationPath $dest -Force
Get-ChildItem "$dest\*.ps1" -Recurse | Unblock-File

$up = Get-ChildItem $dest -Recurse -Filter upgrade.ps1 | Select-Object -First 1
if (-not $up) { throw "Pakette upgrade.ps1 bulunamadi." }
& $up.FullName -Mode Service -ServiceName $ServiceName

Write-Host "Guncelleme tamam: $($rel.tag_name)" -ForegroundColor Green
