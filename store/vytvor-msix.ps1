# Vytvoří balíček Lumo.msix pro Microsoft Store.
# Spusť dvojklikem na VYTVOR-MSIX.cmd (ve stejné složce).
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $here

Write-Host ''
Write-Host '=== Lumo -> MSIX pro Microsoft Store ===' -ForegroundColor Cyan

# 1) kontrola manifestu
$manifest = Get-Content -Raw -Encoding UTF8 (Join-Path $here 'AppxManifest.xml')
if ($manifest -match '__PACKAGE_NAME__|__PUBLISHER__') {
    Write-Host 'V AppxManifest.xml chybí údaje z Partner Center (Package/Identity/Name a Publisher).' -ForegroundColor Red
    Write-Host 'Pošli je Claudovi, doplní je za tebe.'
    Read-Host 'Enter = konec'; exit 1
}
if (-not (Test-Path (Join-Path $here 'Lumo.exe'))) {
    Write-Host 'Ve složce chybí Lumo.exe.' -ForegroundColor Red
    Read-Host 'Enter = konec'; exit 1
}

# 2) MakeAppx z oficiálního balíčku Microsoft.Windows.SDK.BuildTools (NuGet)
$ver = '10.0.28000.2957'
$tools = Join-Path $env:LOCALAPPDATA 'Lumo-BuildTools'
$makeappx = Join-Path $tools "bin\10.0.28000.0\x64\makeappx.exe"
if (-not (Test-Path $makeappx)) {
    Write-Host 'Stahuji MakeAppx od Microsoftu (asi 22 MB, jen poprvé)...'
    New-Item -ItemType Directory -Force -Path $tools | Out-Null
    $zip = Join-Path $tools 'buildtools.zip'
    $url = "https://api.nuget.org/v3-flatcontainer/microsoft.windows.sdk.buildtools/$ver/microsoft.windows.sdk.buildtools.$ver.nupkg"
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    Invoke-WebRequest -Uri $url -OutFile $zip -UseBasicParsing
    Expand-Archive -Path $zip -DestinationPath $tools -Force
    Remove-Item $zip
}
$sig = Get-AuthenticodeSignature $makeappx
if ($sig.Status -ne 'Valid' -or $sig.SignerCertificate.Subject -notmatch 'Microsoft') {
    Write-Host 'MakeAppx nemá platný podpis Microsoftu – končím pro jistotu.' -ForegroundColor Red
    Read-Host 'Enter = konec'; exit 1
}

# 3) složka s obsahem balíčku
$stage = Join-Path $env:TEMP 'Lumo-msix-stage'
if (Test-Path $stage) { Remove-Item -Recurse -Force $stage }
New-Item -ItemType Directory -Force -Path $stage | Out-Null
Copy-Item (Join-Path $here 'Lumo.exe') $stage
Copy-Item (Join-Path $here 'AppxManifest.xml') $stage
Copy-Item (Join-Path $here 'Assets') (Join-Path $stage 'Assets') -Recurse

# 4) zabalit
$verMatch = [regex]::Match($manifest, 'Version="([0-9.]+)"')
$out = Join-Path $here ("Lumo-" + $verMatch.Groups[1].Value + ".msix")
& $makeappx pack /d $stage /p $out /o
if ($LASTEXITCODE -ne 0) {
    Write-Host 'MakeAppx skončil chybou (viz výše). Pošli výpis Claudovi.' -ForegroundColor Red
    Read-Host 'Enter = konec'; exit 1
}
Remove-Item -Recurse -Force $stage

Write-Host ''
Write-Host "Hotovo: $out" -ForegroundColor Green
Write-Host 'Tenhle soubor nahraj v Partner Center do sekce Packages.'
Read-Host 'Enter = konec'
