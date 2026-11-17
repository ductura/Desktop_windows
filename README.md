# Ductura / desktop-windows

Windows aplikacija i instalacijski paket. Tehnologija: C# / .NET 8 (WinForms za početak, editor kasnije preko WebView2; ovisi o odluci 8, otvoreno), paket preko Inno Setupa.

Plan repozitorija: [docs/PLAN.md](docs/PLAN.md).

## Funkcionalna provjera

Korisnik preuzima paket, instalira ga i pokreće Ducturu iz izbornika Start.
Pokriva je `tests/functional/Install-And-Launch.ps1`: tiha instalacija, provjera prečaca u izborniku Start i pokretanje preko njega (`--smoke-out`). Radi samo na Windowsu i pokreće se u CI-ju.

## Struktura

| Mapa | Sadržaj |
|---|---|
| `src/Ductura.Core` | Pomoćne funkcije (argumenti, nazivi paketa, prečac, verzije, adresa weba, odluka o navigaciji ljuske `Navigation.Decide`) |
| `src/Ductura.Desktop` | Windows aplikacija (`Ductura.exe`) |
| `tests/Ductura.Core.Tests` | Testovi pomoćnih funkcija (xUnit) |
| `tests/functional` | Funkcionalni test instalacije |
| `scripts/ci-ok.test.mjs` | Test obvezne zbirne provjere `ci-ok` u `.github/workflows/ci.yml` (`npm ci` pa `node --test`, u CI-ju na Linuxu). `package.json` i `package-lock.json` služe samo tom testu (YAML parser `yaml`); aplikacija ne koristi Node. |
| `.github/workflows/ci.yml` | Jedini workflow za PR: `build-test`, `ci-ok-test`, skeniranje tajni i pregled ovisnosti iz `ductura/.github` (puni SHA) i obvezna zbirna provjera `ci-ok`. Dozvola je samo `contents: read`. |
| `installer/Ductura.iss` | Inno Setup skripta → `dist/Ductura-Setup-x64.exe` |

## Adresa weba

Adresa web aplikacije nije upisana u kod. Čita se iz konfiguracije paketa u `Directory.Build.props`, a build je upisuje u sklop `Ductura.Core`.

| Svojstvo | Zadano | Značenje |
|---|---|---|
| `DucturaWebUrl` | `https://ductura.org/app/` | Adresa s putem; rad se otvara na `<adresa>#/doc/<ID>` |
| `DucturaWebHosts` | `ductura.org` | Dopušteni hostovi, odvojeni s `;` |

`WebAddress` prihvaća samo kanonsku `https` adresu s hosta s popisa, bez porta, korisnika, upita i fragmenta, s putem koji završava s `/`. Ako adresa ne prolazi, aplikacija ne nudi poveznicu, a `--smoke` vraća 1.

Konačnu domenu (V-0) upiši u ta dva retka, ne preko `/p:`. Adresa izdavača u `installer/Ductura.iss` (`AppPublisherURL`) mijenja se zasebno.

## Pokretanje

```powershell
dotnet test tests/Ductura.Core.Tests
dotnet publish src/Ductura.Desktop -c Release -r win-x64 --self-contained true -o publish
iscc /DSourceDir=..\publish installer\Ductura.iss
./tests/functional/Install-And-Launch.ps1
```

Paket nije potpisan. Prije javnog izdanja treba certifikat za potpisivanje koda, inače Windows SmartScreen upozorava korisnike.
