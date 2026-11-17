# Ductura / desktop-windows

Windows aplikacija i instalacijski paket. Tehnologija: C# / .NET 8, WinForms prozor s WebView2 (spike D-0.1, DAN-956; ovisi o odluci 8, otvoreno), paket preko Inno Setupa.

Plan repozitorija: [docs/PLAN.md](docs/PLAN.md).

## Funkcionalna provjera

Korisnik preuzima paket, instalira ga i pokreće Ducturu iz izbornika Start.
Pokriva je `tests/functional/Install-And-Launch.ps1`: tiha instalacija, provjera prečaca u izborniku Start, pokretanje preko njega (`--smoke-out`) i prozor s WebView2 (`--shell-smoke-out`), uz zasebnu objavu s neispravnom adresom. Zatim deinstalira paket i ponavlja instalaciju u mapu s razmakom i hrvatskim znakovima (`C:\Ductura test\Šđčćž mapa`, Inno `/DIR=`): provjerava ključ deinstalacije, prečac i `--smoke-out` iz te mape te deinstalaciju bez ostataka (ključ, mapa, prečac, protokol `ductura`). Prije toga isti prolaz bez navodnika oko `/DIR` mora završiti u `C:\Ductura` i pasti na provjeri, inače test pada (DAN-996). Radi samo na Windowsu i pokreće se u CI-ju.

## Struktura

| Mapa | Sadržaj |
|---|---|
| `src/Ductura.Core` | Pomoćne funkcije (argumenti, nazivi paketa, prečac, verzije, adresa weba, odluka o navigaciji ljuske `Navigation.Decide`, provjera verzije `ClientVersion.Decide`, poziv `health` pri pokretanju `HealthProbe.ReadAsync`) |
| `src/Ductura.Desktop` | Windows aplikacija (`Ductura.exe`) |
| `src/Ductura.Contracts` | Ugovorni klijent generiran iz OpenAPI-ja servera (DAN-958); vidi „Ugovorni klijent” |
| `tests/Ductura.Core.Tests` | Testovi pomoćnih funkcija (xUnit), ugovornih vektora `health` (DAN-997) i vektora ID-a rada (DAN-1000) |
| `tests/functional` | Funkcionalni test instalacije |
| `scripts/contract-client.mjs` | Generiranje i provjera ugovornog klijenta (`generate`, `check`) |
| `scripts/ci-ok.test.mjs` | Test obvezne zbirne provjere `ci-ok` u `.github/workflows/ci.yml` (`npm ci` pa `node --test`, u CI-ju na Linuxu). `package.json` i `package-lock.json` služe samo Node skriptama CI-ja (YAML parser `yaml`); aplikacija ne koristi Node. |
| `.github/workflows/ci.yml` | Jedini workflow za PR: `build-test`, `ci-ok-test`, `contract-client`, skeniranje tajni i pregled ovisnosti iz `ductura/.github` (puni SHA) i obvezna zbirna provjera `ci-ok`. Dozvola je samo `contents: read`. |
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

## Ugovorni klijent (DAN-958)

`src/Ductura.Contracts` je C# klijent koji Kiota (verzija u `.config/dotnet-tools.json`) generira iz `openapi/v1.json` zaključanog izdanja `@ductura/contracts`. Izdanje je zapisano u `src/Ductura.Contracts/contract-lock.json`: samo verzija i sha256 tarballa; URL na GitHub Releases ductura/servera slijedi iz verzije. Paketi za izvođenje (`Microsoft.Kiota.Bundle`) zaključani su u `packages.lock.json`. Generira se samo `/api/v1/health` (`platform.health.get`, u C#-u `client.Api.V1.Health.GetAsync()`). Koristi ga `HealthProbe.ReadAsync` u `Ductura.Core` (DAN-944-2a): zahtjev gradi generirani klijent, a tijelo provjerava `HealthProbe`, jer generirani model krivi tip polja pretvara u null. Prozor ga još ne poziva (944-2b). Paketi klijenta (Kiota) time ulaze i u instalacijski paket. Jezik C# je zatečeno stanje kostura; odluka 8 je otvorena ([docs/PLAN.md](docs/PLAN.md) §0).

- Nova verzija ugovora: promijeni verziju i sha256 u `contract-lock.json` (sha256 je u bilješci izdanja), pokreni `dotnet tool restore` i `node scripts/contract-client.mjs generate` na Linuxu (na Windowsu skripta odbija rad, jer uspoređuje bajtove) i predaj promjene u `Generated/`. Datoteke u `Generated/` se ne uređuju ručno.
- CI posao `contract-client` pokreće `node scripts/contract-client.mjs check`: preuzme tarball, provjeri sha256, ponovno generira klijent u privremenu mapu i pada kad se ijedna datoteka razlikuje, nedostaje ili je višak. Zadnji korak dokazuje da `check` pada kad se izmijeni generirana datoteka i kad se izmijeni opis polja `health` u OpenAPI-ju. Posao preuzima tarball s github.com, pa ispad GitHuba ruši `ci-ok`.

## Ugovorni vektori `health` (DAN-997)

`tests/Ductura.Core.Tests/ContractVectorsTests.cs` provjerava klijent prema `health` vektorima lažnog servera iz izdanja `@ductura/test-fakes` zapisanog u `src/Ductura.Contracts/test-fakes.lock.json` (verzija, URL, sha256; URL mora biti točno onaj na GitHub Releases ductura/servera koji slijedi iz verzije). Test preuzme tarball bez tokena, pada ako se sha256 ne slaže s lockom, i svaki vektor rute `platform.health.get` šalje kroz `HealthProbe.ReadAsync` i `ClientVersion.Decide`. Očekivani ishodi su tablica u testu; vektor koji se pojavi ili nestane iz izdanja ruši test. Test traži mrežu, i lokalno i u poslu `build-test`, pa ispad github.com ruši `ci-ok`. Nova verzija test-fakes: promijeni sve tri vrijednosti u locku i po potrebi tablicu.

## Vektori ID-a rada (DAN-1000)

`tests/Ductura.Core.Tests/WorkIdVectorsTests.cs` čita `vectors/work-id.json` iz izdanja `@ductura/contracts` zapisanog u `src/Ductura.Contracts/contract-lock.json` (isti tarball i sha256 kao ugovorni klijent; vektori postoje od 2.5.0). Datoteku čita strogo: višak, manjak ili krivi tip ključa ruši test. Svaki vektor ide kroz `LaunchOptions.Parse` kao poveznica `ductura://doc/<segment>`: `workIdFromPath` doslovno, a `isWorkId` s `%` i `/` kodiranima, pa parser dobiva natrag isti ID. Ulaz koji nije string mora biti odbijen, jer je argument pokretanja uvijek string. Test pada kad se promijeni presuda ili `id` vektora, kad vektor nestane, pridođe ili promijeni ime (broj i sha256 imena su u testu). Vektor `workIdFromPath` koji završava s `/` dao bi drukčiji ishod (ljuska skida jedan `/`); takav vektor je nalaz, ne promjena testa. Ručni slučajevi u `WorkIdTests.cs` ostaju jer nisu svi u vektorima. Test traži mrežu, kao DAN-997.

## Provjera verzije pri pokretanju (DAN-944)

`ClientVersion.Decide` u `Ductura.Core` uspoređuje `minClientVersion` iz `health` sa svojom verzijom. Poziv `health` je `HealthProbe.ReadAsync` kroz ugovorni klijent (DAN-944-2a, vidi „Ugovorni klijent”); spajanje u prozor još nije napravljeno (944-2b). Paket je sada 0.1.0, a server šalje `minClientVersion` 1.0.0, pa spajanje čeka odluku o verziji (pitanje za vlasnika).

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
