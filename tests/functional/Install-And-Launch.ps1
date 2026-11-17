# Funkcionalna provjera: korisnik preuzima paket, instalira ga i pokreće Ducturu iz izbornika Start.
# Pokreće se na Windowsu (CI: windows-latest) nakon izrade dist\Ductura-Setup-x64.exe.
param([string]$Installer = "$PSScriptRoot\..\..\dist\Ductura-Setup-x64.exe")
$ErrorActionPreference = 'Stop'

if (-not (Test-Path $Installer)) { throw "Paket ne postoji: $Installer" }

# 1. "Preuzimanje": kopija paketa u mapu Preuzimanja kao što bi to napravio preglednik.
$downloads = Join-Path $env:TEMP 'ductura-download'
New-Item -ItemType Directory -Force $downloads | Out-Null
$downloaded = Join-Path $downloads 'Ductura-Setup-x64.exe'
Copy-Item $Installer $downloaded -Force

# 2. Tiha instalacija za trenutnog korisnika.
$p = Start-Process $downloaded -ArgumentList '/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/CURRENTUSER' -Wait -PassThru
if ($p.ExitCode -ne 0) { throw "Instalacija nije uspjela (kod $($p.ExitCode))" }

# 3. Prečac u izborniku Start.
$programs = [Environment]::GetFolderPath('Programs')
$shortcut = Join-Path $programs 'Ductura\Ductura.lnk'
if (-not (Test-Path $shortcut)) { throw "Nema prečaca u izborniku Start: $shortcut" }

# 4. Pokretanje preko prečaca (kao klik u izborniku Start).
$target = (New-Object -ComObject WScript.Shell).CreateShortcut($shortcut).TargetPath
$out = Join-Path $env:TEMP 'ductura-smoke.txt'
Remove-Item $out -ErrorAction SilentlyContinue
$run = Start-Process $target -ArgumentList '--smoke-out', "`"$out`"" -Wait -PassThru
if ($run.ExitCode -ne 0) { throw "Ductura se nije pokrenula (kod $($run.ExitCode))" }
$line = Get-Content $out -Raw
if ($line -notmatch '^Ductura OK') { throw "Neočekivan izlaz: $line" }

# 5. Prozor stvara WebView2 s adresom iz konfiguracije paketa (DAN-956); redak piše nakon NavigationStarting.
function Invoke-ShellSmoke([string]$Exe, [int]$ExpectedCode, [string]$ExpectedLine) {
    $shellOut = Join-Path $env:TEMP 'ductura-shell-smoke.txt'
    Remove-Item $shellOut -ErrorAction SilentlyContinue
    # Not -Wait: it also waits for WebView2 child processes. The app gives up after 90 s, the test after 120 s.
    $p = Start-Process $Exe -ArgumentList '--shell-smoke-out', "`"$shellOut`"" -PassThru
    $null = $p.Handle
    if (-not $p.WaitForExit(120000)) { Stop-Process -Id $p.Id -Force; throw "Prozor se nije zatvorio u 120 s" }
    $got = if (Test-Path $shellOut) { Get-Content $shellOut -Raw } else { '<nema retka>' }
    if ($p.ExitCode -ne $ExpectedCode -or $got -cne $ExpectedLine) {
        throw "Prozor: očekivano kod $ExpectedCode i '$ExpectedLine', dobiveno kod $($p.ExitCode) i '$got'"
    }
}
# The smoke mode has its own WebView2 folder in TEMP; the user's profile in LOCALAPPDATA is never touched.
$data = Join-Path ([IO.Path]::GetTempPath()) 'Ductura-shell-smoke\WebView2'
Remove-Item $data -Recurse -Force -ErrorAction SilentlyContinue
[xml]$props = Get-Content "$PSScriptRoot\..\..\Directory.Build.props"
$webUrl = ($props.Project.PropertyGroup | Where-Object { $_.DucturaWebUrl }).DucturaWebUrl
Invoke-ShellSmoke $target 0 "Ductura WebView2 $webUrl"

# 6. S neispravnom adresom (http) prozor pokazuje samo poruku, bez WebView2: ni podatkovna mapa ne nastaje.
#    /p: je ovdje samo za test; isporučena adresa ostaje u Directory.Build.props.
if (-not (Test-Path $data)) { throw "WebView2 nije stvorio podatkovnu mapu: $data" }
# WebView2 child processes may hold the folder briefly after the window closes.
Get-Process msedgewebview2 -ErrorAction SilentlyContinue | Wait-Process -Timeout 30 -ErrorAction SilentlyContinue
Remove-Item $data -Recurse -Force
$invalid = Join-Path $env:TEMP 'ductura-invalid-address'
dotnet publish "$PSScriptRoot\..\..\src\Ductura.Desktop" -c Release -r win-x64 --self-contained true -o $invalid /p:DucturaWebUrl=http://ductura.org/app/
if ($LASTEXITCODE -ne 0) { throw "Objava s neispravnom adresom nije uspjela (kod $LASTEXITCODE)" }
Invoke-ShellSmoke (Join-Path $invalid 'Ductura.exe') 1 'Ductura poruka'
if (Test-Path $data) { throw "S neispravnom adresom nastala je podatkovna mapa WebView2: $data" }

Write-Host "OK: instalirano, prečac postoji, pokretanje radi ($line), WebView2 otvara $webUrl, neispravna adresa daje poruku"
