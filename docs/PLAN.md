Kanonski plan migracije: [PLAN-MIGRACIJE.md](https://github.com/ductura/.github/blob/main/docs/plan/PLAN-MIGRACIJE.md). Ovaj dokument je dio tog plana.

# Ductura/desktop-windows: plan repozitorija

Stanje: 17. 11. 2026. · **Ovo je samo plan.** Dokument ništa ne gradi, ne kupuje i ne odlučuje (DAN-878, D1).
Izvori:
- [plan migracije](https://github.com/ductura/.github/blob/main/docs/plan/PLAN-MIGRACIJE.md) (u daljnjem tekstu: migracija): §1a, §1b, §3, §4 (faza E), §5, §6, §8 t. 5 do 8 i §9;
- [karta](https://github.com/ductura/.github/blob/main/docs/plan/KARTA.md) (u daljnjem tekstu: karta): paketi, §1, §3, §4 i §5 t. 12.

Oblik je isti kao u [planu servera](https://github.com/ductura/Institution_server/blob/main/docs/PLAN.md) i [planu weba](https://github.com/ductura/Institution_front_web_chrome/blob/main/docs/PLAN.md). Vlasnik je 16. 11. desktopu dopustio samo zadatke D1 do D6, bez odluka 8 i 12 (Ductus#2012).

## 0. Otvorene odluke (ovaj plan ih ne donosi)

| Stavka | Pitanje | Stanje |
|---|---|---|
| 8 | Tehnologija ljuske (migracija §9). Put A: C#/.NET s WebView2 ovdje i Swift s WKWebView u macOS-u. Put B: Tauri ili Electron, jedan kod za oba sustava. | **otvoreno**. Migracija §8 t. 5 kaže: najkasnije na kraju faze C, a spike D-0.1 ide ranije. |
| 12 | Jedan ili dva desktop repozitorija (migracija §9) | **otvoreno**. Uz put A to su `desktop-windows` i `desktop-macos`, uz put B jedan `desktop` s dva cilja. |
| potpis paketa | Certifikat za potpis koda: klasični certifikat ili Microsoftova usluga potpisa u oblaku (migracija §8 t. 7). Tko plaća taj trošak (karta §5 t. 12). | **otvoreno** |

Stavke koje ovise o tim odlukama nose oznaku „ovisi o odluci 8”, „ovisi o odluci 12” ili „ovisi o potpisu”.

Postojeći kostur u C#/.NET 8 je zatečeno stanje, a ne izbor puta A. Ako odluka 8 ili 12 ode drugim putem, kostur se zamjenjuje, a provjere iz §6 prelaze u repo koji ga zamijeni.

Karta za ugovorni klijent piše „C#/Swift klijent”. To vrijedi samo uz put A, pa je i to otvoreno.

## 1. Svrha i granica

**Svrha.** Windows aplikacija Ducture je tanka izvorna ljuska oko **istog web paketa** iz `ductura/Institution_front_web_chrome` (migracija §8 t. 5). Ljuska radi:
- prozor;
- instalaciju i ažuriranje;
- prijavu tokom po ADR-u S-1E-e;
- lokalno spremište.

Repo posjeduje i instalacijski paket, potpis paketa (ovisi o potpisu) i prvo pokretanje (migracija §1a).

**Granica (nikad ne ulazi):**
- vlastiti editor: izvorni editor u C#-u nikad, editor je web paket (migracija §8 t. 5);
- vlastiti server ili baza; poslovna pravila i odluke o ovlastima (samo server);
- druga prijava osim one preko servera. Studenti se prijavljuju samo kroz AAI@EduHr (D-09). Ljuska ne koristi kolačić nego token u zaglavlju (migracija §1b, S-1E-e);
- izravno pisanje u tablice evidencije, predaje i revizijskog traga (samo API);
- tajne i ključ za potpis u repou. Ključ smije postojati samo kao tajna CI-ja, i to kad postoji (ovisi o potpisu);
- stvarni osobni podaci i studentski radovi u testovima.

## 2. Zatečeno stanje (kostur, 17. 11.)

- C# / .NET 8. WinForms prozor s poveznicom na web (`src/Ductura.Desktop`). Pomoćne funkcije su u `src/Ductura.Core` (`LaunchOptions`, `Packaging`, `WorkId`, `WebAddress`, `Startup`).
- Spojeni PR-ovi:
  - #1, DAN-885: `ci-ok`;
  - #2, DAN-879: skeniranje tajni i pregled ovisnosti iz `ductura/.github`;
  - #3, DAN-881: ID rada iz poveznice;
  - #4, DAN-883: adresa weba iz konfiguracije paketa.
- Testovi:
  - xUnit u `tests/Ductura.Core.Tests`;
  - funkcionalni test `tests/functional/Install-And-Launch.ps1`: tiha instalacija, prečac u izborniku Start i pokretanje s `--smoke-out`, zatim isto u mapi s razmakom i hrvatskim znakovima uz deinstalaciju bez ostataka (DAN-996);
  - `scripts/ci-ok.test.mjs` (Node, samo za `ci-ok`).
- Paket: Inno Setup (`installer/Ductura.iss`) gradi `dist/Ductura-Setup-x64.exe`. Paket **nije potpisan**, pa SmartScreen upozorava korisnika.
- Adresa weba `https://ductura.org/app/` je zadana vrijednost dok se ne odluči V-0 (domena).
- Ugovorni klijent (DAN-958) postoji samo za `health`, iz zaključanog izdanja `@ductura/contracts`; vektori ID-a rada iz istog izdanja prolaze kroz `LaunchOptions.Parse` u `build-test` (DAN-1000); jezik C# je zatečeno stanje kostura (odluka 8 je otvorena). Još nema web paketa u ljusci, prijave tokom ni ažuriranja.

## 3. Sadržaj (PRIJEDLOG; ovisi o odluci 8)

```
src/Ductura.Core/         pomoćne funkcije bez sučelja (testirane)
src/Ductura.Desktop/      prozor i ljuska; web paket u WebView2 (ovisi o odluci 8)
tests/Ductura.Core.Tests/ testovi pomoćnih funkcija
tests/functional/         instalacija i pokretanje
installer/                instalacijski paket; potpis ovisi o potpisu
scripts/, docs/, .github/ (poziva ductura/.github punim SHA-om)
```

## 4. Što dolazi iz Ductusa i paketa

Iz Ductusa se ne prenosi nijedan kod (migracija §3). Ljuska učitava isti web paket. Polazi od:
- manifesta i simulacije preuzimanja u serveru (S-1H.5, S-1H.6);
- ADR-a S-1E-e;
- spikea D-0.1 (Ductus `spikes/`).

| Paket (karta) | Kako ga desktop koristi |
|---|---|
| `@ductura/contracts` | Klijent iz istog OpenAPI-ja s ugovornim vektorima. Jezik klijenta ovisi o odluci 8. |
| `@ductura/test-fakes` | Lažni server iz ugovora i lažni AAI za testove |
| `@ductura/editor-core` | Samo kroz web paket (karta: „ovisno o odluci 8”) |
| `@ductura/parity` | Scenariji S-1H.8/1..6 u ljusci |

## 5. Zadaci

| Korak | Što | Ovisi o |
|---|---|---|
| D1 (DAN-878) | Ovaj plan | — |
| D-0.1 | Spike ljuske (WebView2, WKWebView, Tauri) s istom web izgradnjom, u Ductusu | kraj faze B |
| odluke 8 i 12 | Vlasnik | D-0.1; najkasnije kraj faze C |
| Faza E · aktivacija desktop razvoja | Vlasnička radnja, odvojena od V-1b (karta §3) | vlasnik; ulaz faze E (migracija §4): odluke 8 i 12, potpis, V-5 |
| ljuska | Prozor učitava isti web paket s provjerene adrese (`WebAddress`) | odluka 8, aktivacija faze E |
| prijava | Token u zaglavlju po ADR-u S-1E-e; samo preko servera | S-1E-e, V-5 (Srce) |
| klijent ugovora | Iz OpenAPI-ja u `@ductura/contracts`; vektori u CI-ju | U10, odluka 8 |
| izdanje | GitHub Release desktop repoa kao izvor paketa. Server u manifestu drži adresu i SHA-256, a korisnik preuzima kroz `/preuzimanje/` (PRIJEDLOG, migracija §8 t. 8). | S-1H.5, S-1H.6, odluka 12 |
| potpis | Potpisani paket | ovisi o potpisu |

Koraci bez ID-a su PRIJEDLOG: ID dodjeljuje orkestrator.

## 6. Testovi

| Sloj | Konkretni slučajevi |
|---|---|
| **Pomoćne funkcije** | Argumenti, nazivi paketa, ID rada, adresa weba i ishod `--smoke` (postoje). Nove funkcije ljuske dobivaju testove u `Ductura.Core`. |
| **Funkcionalni** | Preuzimanje, instalacija i pokretanje iz izbornika Start. Razmaci i hrvatski znakovi u putanji (karta §4). |
| **Ugovor** | Klijent prikvači verziju ugovora i pokreće njegove vektore (migracija §6). |
| **Paritet** | `@ductura/parity` S-1H.8/1..6 u ljusci (ovisi o odluci 8). |
| **Simulacija preuzimanja** | Pravi paket, SHA-256 jednak manifestu, preuzimanje samo na klik (migracija §6). Pokreće ga CI desktopa pri izdanju. |
| **Sigurnost** | Nema tajni (`secret-scan`). Ljuska otvara samo provjerenu adresu. Prijava ide samo preko servera. |

**Matrica:** `windows-latest` (x64) za izgradnju i funkcionalni test, `ubuntu-latest` za `ci-ok-test` i `contract-client`.
**Prag „gotovo”:** `ci-ok` je zelen i ništa nije preskočeno. Funkcionalni test na čistom Windowsu prolazi.

## 7. CI i gate

- **Danas** (`.github/workflows/ci.yml`, `permissions: contents: read`, svaki `uses:` na punom SHA-u):
  - `build-test` (`windows-latest`);
  - `ci-ok-test`;
  - `secret-scan` i `dependency-review` iz `ductura/.github`;
  - `contract-client`: generirani klijent jednak je zaključanom ugovoru (DAN-958); vektori ID-a rada iz istog izdanja vrte se u `build-test` (DAN-1000);
  - obvezni `ci-ok`.
- **Kasnije:**
  - reusable `ci-dotnet` iz `ductura/.github` (ime vrijedi samo uz put A, ovisi o odluci 8);
  - ostali vektori ugovora (izvan ID-a rada i `health`);
  - izdanje na oznaku `vX.Y.Z` kao potpisani paket u GitHub Releaseu (migracija §5; ovisi o potpisu).
- **Risk (PRIJEDLOG):** `.github/**` je `critical`, kao u drugim repoima. Ostale putanje dodjeljuje tablica rizika.
- **Vlasničke radnje:**
  - Faza E · aktivacija desktop razvoja;
  - certifikat za Windows (ovisi o potpisu);
  - V-0, V-2, V-3 i V-5;
  - odluke 8 i 12.

## 8. Kriterij izlaza (faza E; ovisi o odlukama 8 i 12 i o potpisu)

Izlaz faze E prema migraciji §4: „Potpisani paketi; instalacija i pokretanje na čistom Windowsu i Macu; gumbi preloadera vode na prava izdanja”. Za ovaj repo to znači:

1. Paket je potpisan (ovisi o potpisu) i objavljen u GitHub Releaseu. SHA-256 je jednak onom u manifestu servera.
2. Instalacija i pokretanje prolaze na čistom Windowsu, uz razmake i hrvatske znakove u putanji.
3. Gumb preloadera za Windows vodi na to izdanje.
4. Editor dolazi iz web paketa. Nema nijednog retka izvornog editora.
5. Prijava ide samo preko servera, tokom (S-1E-e). Za studente je to AAI@EduHr (D-09).
6. `ci-ok` je zelen. Prihvat vala s instalacijom desktopa je **otvoren** (karta §4).
