# Ductura / desktop-windows

Windows aplikacija i instalacijski paket. Tehnologija: C# / .NET 8 (WinForms za početak, editor kasnije preko WebView2), paket preko Inno Setupa.

## Funkcionalna provjera

Korisnik preuzima paket, instalira ga i pokreće Ducturu iz izbornika Start.
Pokriva je `tests/functional/Install-And-Launch.ps1`: tiha instalacija, provjera prečaca u izborniku Start i pokretanje preko njega (`--smoke-out`). Radi samo na Windowsu i pokreće se u CI-ju.

## Struktura

| Mapa | Sadržaj |
|---|---|
| `src/Ductura.Core` | Pomoćne funkcije (argumenti, nazivi paketa, prečac, verzije) |
| `src/Ductura.Desktop` | Windows aplikacija (`Ductura.exe`) |
| `tests/Ductura.Core.Tests` | Testovi pomoćnih funkcija (xUnit) |
| `tests/functional` | Funkcionalni test instalacije |
| `scripts/ci-ok.test.mjs` | Test obvezne zbirne provjere `ci-ok` u `.github/workflows/ci.yml` (`node --test`, u CI-ju na Linuxu) |
| `installer/Ductura.iss` | Inno Setup skripta → `dist/Ductura-Setup-x64.exe` |

## Pokretanje

```powershell
dotnet test tests/Ductura.Core.Tests
dotnet publish src/Ductura.Desktop -c Release -r win-x64 --self-contained true -o publish
iscc /DSourceDir=..\publish installer\Ductura.iss
./tests/functional/Install-And-Launch.ps1
```

Paket nije potpisan. Prije javnog izdanja treba certifikat za potpisivanje koda, inače Windows SmartScreen upozorava korisnike.
