# Ductura / desktop-windows

Windows aplikacija i instalacijski paket. Tehnologija: C# / .NET 8, WinForms prozor s WebView2 (spike D-0.1, DAN-956; ovisi o odluci 8, otvoreno), paket preko Inno Setupa.

Plan repozitorija: [docs/PLAN.md](docs/PLAN.md).

## Funkcionalna provjera

Korisnik preuzima paket, instalira ga i pokreće Ducturu iz izbornika Start.
Pokriva je `tests/functional/Install-And-Launch.ps1`: tiha instalacija, provjera prečaca u izborniku Start, pokretanje preko njega (`--smoke-out`) i prozor s WebView2 (`--shell-smoke-out`), uz zasebnu objavu s neispravnom adresom. Radi samo na Windowsu i pokreće se u CI-ju.

## Struktura

| Mapa | Sadržaj |
|---|---|
| `src/Ductura.Core` | Pomoćne funkcije (argumenti, nazivi paketa, prečac, verzije, adresa weba, odluka o navigaciji ljuske `Navigation.Decide`, provjera verzije `ClientVersion.Decide`) |
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

`WebAddress` prihvaća samo kanonsku `https` adresu s hosta s popisa, bez porta, korisnika, upita i fragmenta, s putem koji završava s `/`. Ako adresa ne prolazi, prozor prikazuje samo poruku i ne stvara WebView2, a `--smoke` vraća 1.

Konačnu domenu (V-0) upiši u ta dva retka, ne preko `/p:`. Adresa izdavača u `installer/Ductura.iss` (`AppPublisherURL`) mijenja se zasebno.

## Prozor s WebView2 (spike D-0.1, DAN-956)

Prozor s provjerenom adresom stvara kontrolu WebView2 i otvara adresu iz `WebAddress`. Bez provjerene adrese prikazuje samo poruku. Nema prijave, nadogradnji ni potpisa.

| Nalaz | Stanje |
|---|---|
| Paket | `Microsoft.Web.WebView2` 1.0.3912.50 dodaje oko 0,9 MB nekomprimiranih datoteka: `Microsoft.Web.WebView2.Core.dll` 650 KB, `WebView2Loader.dll` (win-x64) 159 KB, `Microsoft.Web.WebView2.WinForms.dll` 39 KB i `Microsoft.Web.WebView2.Wpf.dll` 83 KB. Izmjereno u CI-ju (artefakt `Ductura-Setup-x64`, 17. 11. 2026.): 50 873 818 B s WebView2 prema 50 535 114 B na `main`, dakle oko 0,33 MB više. Najveći dio paketa i dalje je samostalni .NET 8. |
| Runtime | Paket ne nosi WebView2 runtime (Evergreen). Windows 11 ga ima ugrađenog, a Windows 10 obično preko Microsoft Edgea. Ako runtime nedostaje, prozor prikazuje „Za prikaz Ducture treba Microsoft Edge WebView2 Runtime.” Aplikacija runtime ne preuzima i ne instalira. To ponašanje CI ne može pokazati jer `windows-latest` ima runtime; dokazano je samo jediničnim testom. |
| Podaci | WebView2 sprema podatke u `%LOCALAPPDATA%\Ductura\WebView2`, jer instalacija u Program Files nije zapisiva. Način `--shell-smoke-out` koristi zasebnu mapu `%TEMP%\Ductura-shell-smoke\WebView2`, pa funkcionalni test ne dira korisnikov profil. |
| Navigacija | Još nema pravila za navigaciju unutar prozora (DAN-960). |

Funkcionalni test pokreće prozor s `--shell-smoke-out <datoteka>`. Prozor zapiše `Ductura WebView2 <adresa>` (kod 0) kad WebView2 počne otvarati adresu, `Ductura poruka` (kod 1) kad adresa nije ispravna `Ductura bez WebView2` (kod 3) kad nema runtimea i `Ductura greška WebView2` (kod 4) kad se WebView2 ne pokrene iz drugog razloga. Ako u 90 sekundi nema ishoda, izlazi s kodom 2. U tom načinu navigacija se zaustavlja čim počne, pa test ne ovisi o mreži ni o serveru. Kod neispravne adrese test provjerava i da podatkovna mapa WebView2 nije nastala.

## Provjera verzije pri pokretanju (DAN-944)

`ClientVersion.Decide` u `Ductura.Core` uspoređuje `minClientVersion` iz `health` sa svojom verzijom. Poziv `health` i prozor još nisu spojeni (944-2, nakon ugovornog klijenta DAN-958). Paket je sada 0.1.0, a server šalje `minClientVersion` 1.0.0, pa spajanje čeka odluku o verziji (pitanje za vlasnika).

| Ishod | Kada | Web |
|---|---|---|
| `UpdateRequired` | klijent je stariji od `minClientVersion`, ili vlastita verzija nije strogi `major.minor.patch` | ne otvara se; poruka „Ova verzija Ducture više nije podržana. Preuzmite i instalirajte noviju verziju.” |
| `ServerUnavailable` | server nije odgovorio (mreža, rok, ne-200, tijelo izvan ugovora) ili `minClientVersion` nije u obliku iz ugovora | otvara se: web i preloader (D-300) prikazuju stanje servera; `minClientVersion` je savjet za nadogradnju, ne sigurnosna granica |
| `Current` | klijent je jednak ili noviji, ili odgovor nema `minClientVersion` | otvara se |

## Pokretanje

```powershell
dotnet test tests/Ductura.Core.Tests
dotnet publish src/Ductura.Desktop -c Release -r win-x64 --self-contained true -o publish
iscc /DSourceDir=..\publish installer\Ductura.iss
./tests/functional/Install-And-Launch.ps1
```

Paket nije potpisan. Prije javnog izdanja treba certifikat za potpisivanje koda, inače Windows SmartScreen upozorava korisnike.
