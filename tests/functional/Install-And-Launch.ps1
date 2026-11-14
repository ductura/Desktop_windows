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

Write-Host "OK: instalirano, prečac postoji, pokretanje radi ($line)"
