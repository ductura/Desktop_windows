# Funkcionalna provjera: korisnik preuzima paket, instalira ga i pokreće Ducturu iz izbornika Start.
# Pokreće se na Windowsu (CI: windows-latest) nakon izrade dist\Ductura-Setup-x64.exe.
param([string]$Installer = "$PSScriptRoot\..\..\dist\Ductura-Setup-x64.exe")
$ErrorActionPreference = 'Stop'

if (-not (Test-Path $Installer)) { throw "Paket ne postoji: $Installer" }

# Passes 2 and 3 (DAN-996) install into C:\Ductura and C:\Ductura test; they must not exist beforehand.
foreach ($d in 'C:\Ductura', 'C:\Ductura test') { if (Test-Path -LiteralPath $d) { throw "Mapa već postoji: $d" } }
$appKey = 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{7B0E3D52-5C1A-4A55-9C8B-1D3C0D6F2A11}_is1'

# Arguments go as one string: Start-Process neither adds nor removes quotes, so a missing quote stays missing.
function Install-Ductura([string]$Arguments) {
    Write-Host "Instalacija: $Arguments"
    $p = Start-Process $downloaded -ArgumentList $Arguments -PassThru
    $null = $p.Handle
    if (-not $p.WaitForExit(300000)) { Stop-Process -Id $p.Id -Force; throw "Instalacija nije završila u 300 s" }
    return $p.ExitCode
}

# Per-user install in $Dir: the exe, the uninstall key (HKCU only) and the Start menu shortcut all point there.
function Assert-InstalledAt([string]$Dir) {
    $exe = Join-Path $Dir 'Ductura.exe'
    if (-not (Test-Path -LiteralPath $exe)) { throw "Ductura.exe nije u mapi instalacije: $exe" }
    $key = Get-ItemProperty -LiteralPath "HKCU:\$appKey" -ErrorAction SilentlyContinue
    if (-not $key -or $key.InstallLocation.TrimEnd('\') -ine $Dir.TrimEnd('\')) {
        throw "Ključ deinstalacije ne pokazuje na $Dir (InstallLocation: $($key.InstallLocation))"
    }
    foreach ($k in "HKLM:\$appKey", "HKLM:\$($appKey -replace '^Software', 'Software\WOW6432Node')") {
        if (Test-Path -LiteralPath $k) { throw "Instalacija za trenutnog korisnika upisala je ključ u HKLM: $k" }
    }
    $lnk = Join-Path ([Environment]::GetFolderPath('Programs')) 'Ductura\Ductura.lnk'
    if (-not (Test-Path -LiteralPath $lnk)) { throw "Nema prečaca u izborniku Start: $lnk" }
    # WScript.Shell returns TargetPath through the ANSI code page (đ, č, ć become d, c, c); Shell.Application reads Unicode.
    $target = (New-Object -ComObject Shell.Application).NameSpace((Split-Path $lnk)).ParseName('Ductura.lnk').GetLink.Path
    if ($target -ine $exe) { throw "Prečac pokazuje na $target, a ne na $exe" }
    return $exe
}

# The uninstaller hands off to a copy in TEMP and returns at once, so the leftovers are polled for up to 120 s.
function Uninstall-Ductura {
    $dir = (Get-ItemProperty -LiteralPath "HKCU:\$appKey" -ErrorAction Stop).InstallLocation.TrimEnd('\')
    Write-Host "Deinstalacija: $dir"
    $null = Start-Process (Join-Path $dir 'unins000.exe') -ArgumentList '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART' -PassThru
    $watch = "HKCU:\$appKey", $dir, (Join-Path ([Environment]::GetFolderPath('Programs')) 'Ductura'), 'HKCU:\Software\Classes\ductura'
    $deadline = [DateTime]::UtcNow.AddSeconds(120)
    do {
        Start-Sleep -Seconds 2
        $left = @($watch | Where-Object { Test-Path -LiteralPath $_ })
    } while ($left.Count -gt 0 -and [DateTime]::UtcNow -lt $deadline)
    if ($left.Count -gt 0) { throw "Deinstalacija je ostavila: $($left -join ', ')" }
}

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

# 7. Pass 1 is removed so that every later pass starts clean.
Uninstall-Ductura

# 8. Folder with a space and Croatian letters (DAN-996), built from code points so the file encoding cannot change it.
$dir = 'C:\Ductura test\' + (-join [char[]](0x160, 0x111, 0x10D, 0x107, 0x17E)) + ' mapa'
$base = '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CURRENTUSER'

# 8a. Negative first: without quotes Inno reads /DIR=C:\Ductura and ignores the rest. Only that exact outcome is
#     accepted, and the same check as in 8b must reject it.
$code = Install-Ductura "$base /DIR=$dir"
if ($code -ne 0 -or (Test-Path -LiteralPath 'C:\Ductura test') -or -not (Test-Path -LiteralPath 'C:\Ductura\Ductura.exe')) {
    throw "Neočekivan ishod negativnog prolaza (kod $code): očekivana je instalacija u C:\Ductura"
}
$caught = $false
try { $null = Assert-InstalledAt $dir } catch { $caught = $true; Write-Host "Pokvaren navodnik otkriven: $_" }
if (-not $caught) { throw "Pokvaren navodnik u putanji nije otkriven" }
Uninstall-Ductura

# 8b. Quoted /DIR: install, shortcut, --smoke-out from that folder, then uninstall without leftovers.
$code = Install-Ductura "$base /DIR=`"$dir`""
if ($code -ne 0) { throw "Instalacija u $dir nije uspjela (kod $code)" }
$exe = Assert-InstalledAt $dir
$out2 = Join-Path $env:TEMP 'ductura-smoke-2.txt'
Remove-Item $out2 -ErrorAction SilentlyContinue
$run = Start-Process $exe -ArgumentList "--smoke-out `"$out2`"" -Wait -PassThru
if ($run.ExitCode -ne 0) { throw "Ductura se nije pokrenula iz $dir (kod $($run.ExitCode))" }
$line2 = Get-Content $out2 -Raw
if ($line2 -notmatch '^Ductura OK') { throw "Neočekivan izlaz iz ${dir}: $line2" }
Uninstall-Ductura
Remove-Item -LiteralPath 'C:\Ductura test' -Recurse -Force -ErrorAction SilentlyContinue

Write-Host "OK: $dir instalirano, prečac pokazuje onamo, pokretanje radi ($line2), deinstalacija bez ostataka; pokvaren navodnik je otkriven"
