# ClinicNow — Dokumentacija sistema preporuke

> Ovaj dokument opisuje sistem preporuke implementiran u sklopu seminarskog rada iz predmeta
> **Razvoj softvera II**. Prema uputama za izradu rada (poglavlje 2.4), implementacija sistema
> preporuke **mora odgovarati ovoj dokumentaciji**. Sve navedeno ovdje predstavlja obavezujući
> opis ponašanja koda; svaka izmjena pristupa mora se prvo odraziti u ovom dokumentu.

- **Projekat:** ClinicNow — digitalizacija poslovanja malih privatnih klinika
- **Autor:** Ibrahim Hodžić (IB210082)
- **Tip preporuke:** Content-based filtering (primarno) + Popularity-based (fallback za hladni start)
- **Tehnologija:** ASP.NET Core (C#, .NET 10), ML.NET (`Microsoft.ML`)
- **Svojstvo:** Objašnjive preporuke (*explainable recommendations*)

---

## 1. Svrha i cilj

Pacijentu se pri zakazivanju termina i na početnom ekranu mobilne aplikacije nude **preporučeni
slobodni termini i doktori**. Cilj je smanjiti broj koraka potrebnih za zakazivanje i predložiti
pacijentu one preglede koji najviše odgovaraju njegovim ranijim navikama (vrsta pregleda, doktor,
dan u sedmici i doba dana), pri čemu je uz svaki prijedlog vidljivo i **kratko obrazloženje** zašto
je baš taj termin predložen.

Sistem je namjerno realizovan kao **jednostavniji modul preporuke** koristeći poznati algoritam
(content-based filtriranje uz kosinusnu sličnost), u skladu sa zahtjevom iz uputa.

---

## 2. Pristup i motivacija

### 2.1. Content-based filtering (primarni pristup)

Odabran je **content-based** pristup jer klinika u ranoj fazi rada nema dovoljno korisnika za
pouzdano kolaborativno filtriranje (*collaborative filtering* pati od problema rijetke matrice i
hladnog starta na nivou cijelog sistema). Content-based pristup radi nad **historijom samog
pacijenta** i sadržajnim atributima usluga i doktora, pa daje smislene preporuke već nakon
nekoliko obavljenih termina.

Ideja: iz obavljenih termina pacijenta izgradimo njegov „profil ukusa", a zatim rangiramo sve
trenutno dostupne slobodne termine prema sličnosti sa tim profilom.

### 2.2. Popularity-based fallback (hladni start)

Za pacijente **bez historije** (novi korisnici) content-based pristup nema ulaznih podataka. U tom
slučaju sistem prelazi na **popularity-based** preporuku: nude se najčešće zakazivane usluge i
doktori u posljednjem periodu (npr. 90 dana), izračunato jednim agregatnim `GROUP BY` upitom.

> **Napomena o usklađenosti:** ova dokumentacija opisuje **hibrid content-based + popularity-based**
> i implementacija mora pratiti oba pristupa. Nije dozvoljeno u dokumentaciji navesti jedan pristup,
> a implementirati drugi.

---

## 3. Izvori podataka i signali

Svi signali koje algoritam koristi **stvarno se upisuju u bazu podataka** tokom korištenja
aplikacije — ne postoje sintetički niti nekorišteni signali. Prema zahtjevu iz uputa, **svaki
prikupljeni signal koji ulazi u bodovanje zaista se i koristi** (nije dozvoljeno prikupljati npr.
prosječnu ocjenu, a zatim je ignorisati).

| Signal | Izvor (tabela / polje) | Kada se upisuje | Uloga u preporuci |
|---|---|---|---|
| Historija termina | `Appointment` (status `Completed`/`Confirmed`) | pri zakazivanju i promjeni statusa | gradnja profila pacijenta |
| Vrsta pregleda | `Appointment.MedicalServiceId → MedicalService` | pri kreiranju termina | sadržajna karakteristika (feature) |
| Doktor | `Appointment.DoctorId → Doctor` | pri kreiranju termina | sadržajna karakteristika (feature) |
| Specijalizacija | `Doctor` ↔ `Specialization` (M:N) | referentni podatak | sadržajna karakteristika (feature) |
| Dan u sedmici | izvedeno iz `Appointment.DateTimeUtc` (UTC) | pri kreiranju termina | vremenski obrazac (feature) |
| Doba dana | izvedeno iz `Appointment.DateTimeUtc` (UTC) | pri kreiranju termina | vremenski obrazac (feature) |
| Interakcije (pregledi/pretrage) | `RecommenderInteraction` | pri pregledu doktora/usluge i pretrazi | pojačanje (*boost*) profila |

Tabela **`RecommenderInteraction`** (`UserId`, `InteractionType`, `DoctorId?`, `MedicalServiceId?`,
`DateTimeUtc`) puni se kroz stvarne akcije korisnika u aplikaciji (npr. otvaranje detalja
doktora ili usluge, pretraga). Time se osigurava da su ulazni podaci recommendera realni.

> Napomena o jeziku: nazivi tabela/polja u ovom dokumentu (`Appointment`, `Doctor`,
> `MedicalService`, ...) su na engleskom jer je programski kod pisan na engleskom
> (vidi CLAUDE.md "Language"); ostatak teksta ostaje na bosanskom jeziku.

> Sva vremena čuvaju se u **UTC** (`DateTime.UtcNow`); izvođenje dana u sedmici i doba dana radi se
> nad UTC vrijednostima kako bi rezultat bio konzistentan i u Docker okruženju.

---

## 4. Izgradnja obilježja (feature engineering)

Za svaku **uslugu/termin** gradi se tekstualni sadržajni opis kombinovanjem relevantnih atributa u
jedinstven skup obilježja. Koriste se ML.NET transformacije teksta:

- `FeaturizeText` nad nazivom i opisom **usluge** (`MedicalService.Name`, `MedicalService.Description`),
- `FeaturizeText` nad **specijalizacijom** doktora,
- `FeaturizeText` nad **imenom i prezimenom doktora** (kao identitetom),
- kategorijska obilježja za **dan u sedmici** i **doba dana** (jutro / popodne / veče).

`FeaturizeText` interno provodi normalizaciju teksta, tokenizaciju i **TF-IDF** vektorizaciju
n-gramima. Sva pojedinačna obilježja se na kraju spajaju (`Concatenate`) u jedan numerički vektor
obilježja `Features` po stavci.

```
pipeline =
    FeaturizeText("MedicalServiceFeat",  MedicalService.Name + Description)
    → FeaturizeText("SpecializationFeat", Specialization)
    → FeaturizeText("DoctorFeat",         Doctor.FirstName + LastName)
    → kategorijska obilježja (DayOfWeek, TimeOfDay)
    → Concatenate("Features", [MedicalServiceFeat, SpecializationFeat, DoctorFeat, DayOfWeek, TimeOfDay])
```

---

## 5. Algoritam sličnosti i bodovanje

### 5.1. Kosinusna sličnost

Sličnost dva vektora obilježja **A** i **B** računa se kao **kosinusna sličnost**:

```
cosine(A, B) = (A · B) / (||A|| · ||B||)
```

Vrijednost je u rasponu [0, 1] (obilježja su nenegativna); veća vrijednost znači veću sadržajnu
sličnost.

### 5.2. Bodovanje kandidata

Neka je `H` skup obavljenih termina pacijenta (profil), a `C` skup **trenutno dostupnih slobodnih
termina** (kandidati koji nisu zauzeti i za koje doktor nije blokiran). Za svaki kandidat `c ∈ C`:

```
score(c) = ( Σ_{h ∈ H}  w(h) · cosine( Features(c), Features(h) ) )  /  Σ_{h ∈ H} w(h)
```

gdje je `w(h)` težina historijske stavke koja spaja **frekvenciju i skorašnjost**:

```
w(h) = frequencyFactor(h) · recencyFactor(h)
recencyFactor(h) = 1 / (1 + daysSinceAppointment(h) / 30)
```

Time nedavni i češće birani obrasci imaju veći uticaj. Kandidati koji odgovaraju terminima koje
pacijent već ima rezervisane (status `Pending`/`Confirmed`) isključuju se iz preporuke - ne nudi se
ponovo isti termin koji je već zakazan; već završeni (`Completed`) termini ostaju validni kandidati,
jer upravo oni najčešće grade profil za buduće preporuke. Rezultati se sortiraju opadajuće po
`score(c)` i uzima se **Top-N** (podrazumijevano `N = 5`).

### 5.3. Preciziranje `frequencyFactor` i doprinosa interakcija

`frequencyFactor(h)` iz poglavlja 5.2 definisan je kao broj ponavljanja iste
kombinacije **(doktor, usluga)** unutar historije pacijenta `H` - pacijent
koji je istom doktoru/usluzi dolazio više puta dobija veći uticaj tog
obrasca na rangiranje.

Stavke iz `RecommenderInteraction` (poglavlje 3, red "Interakcije") ulaze u
`H` kao dodatne, slabije ponderisane stavke - **baznom težinom 0.4** u
odnosu na obavljen termin (`w(i) = 0.4 · frequencyFactor(i) · recencyFactor(i)`,
ista formula skorašnjosti kao za termine), gdje je `frequencyFactor(i)` broj
ponavljanja iste kombinacije (`InteractionType`, doktor, usluga) unutar
posljednjih 180 dana. Time je zadovoljen zahtjev da se **svaki** prikupljeni
signal zaista koristi u bodovanju (poglavlje 3), a ne samo evidentira.

Kandidati (poglavlje 5.2's `C`) traže se kao najraniji stvarno slobodan
termin za svaki par (doktor, usluga) unutar konfigurabilnog prozora
(`RECOMMENDER_CANDIDATE_LOOKAHEAD_DAYS`, podrazumijevano 14 dana) - ograničeno
po dizajnu na obim demonstracionog kataloga (nekoliko doktora/usluga), u
skladu sa obimom seminarskog rada.

---

## 6. Objašnjive preporuke (explainability)

Uz svaku preporuku sistem generiše **kratko, razumljivo obrazloženje** na osnovu obilježja koja su
najviše doprinijela sličnosti (dominantni doktor / usluga / vremenski obrazac u historiji
pacijenta). Obrazloženje se vraća kao dio DTO-a preporuke (`Reason`) i prikazuje se u korisničkom
interfejsu ispod prijedloga.

Primjeri:

- „Predlažemo ovaj termin jer ste ranije više puta birali **dermatologa ponedjeljkom ujutro**."
- „Preporučeno na osnovu vaših prethodnih **kontrolnih pregleda kod dr. Kovač**."
- (fallback) „Popularno ove sedmice: **opći pregled** je jedan od najčešće zakazivanih usluga."

Obrazloženje se izvodi iz stvarnih signala iz poglavlja 3, pa je uvijek u skladu sa razlogom
rangiranja.

---

## 7. Upravljanje modelom

- **Treniranje:** `pipeline.Fit(...)` gradi transformacioni model nad skupom svih usluga/termina.
- **Čuvanje/učitavanje:** model se serijalizuje na disk (`model.zip`) i učitava pri pokretanju;
  ako fajl ne postoji, model se trenira i snima pri prvom pozivu.
- **Ponovno treniranje (re-train):** model se osvježava periodično / pri značajnijoj promjeni
  kataloga usluga i doktora (nova usluga, novi doktor). Predikcija po korisniku (rangiranje
  kandidata) radi se u realnom vremenu nad učitanim modelom.
- **Konfiguracija:** putanje i parametri (npr. `N`, prozor za popularnost) čitaju se iz
  konfiguracije (`.env`), a ne hardkodiraju u kodu.

Servis preporuke registrovan je kao **`Scoped`** (koristi `DbContext`), a teški resursi (npr.
učitani model) dijele se na nivou aplikacije uz odgovarajuće keširanje.

---

## 8. Integracija u aplikaciju

- **API:** endpoint tipa `GET api/Recommendation/appointments` (autorizovan JWT-om). `UserId` se
  **uvijek preuzima iz JWT tokena**, nikada iz rute ili tijela zahtjeva.
- **Servisni sloj:** `IRecommenderService` → `RecommenderService`; kontroler ne sadrži poslovnu
  logiku niti direktno pristupa bazi.
- **Mobilna aplikacija:** preporuke se prikazuju na zasebnom tabu **„Preporuke“** i u **toku
  zakazivanja** termina (prijedlozi slobodnih termina uz obrazloženje).
- **Podaci za rangiranje** dolaze isključivo iz baze (historija termina, katalog usluga/doktora,
  interakcije), čime je zadovoljen zahtjev da ulazni signali budu stvarni.

---

## 9. Rubni slučajevi (edge cases)

- **Novi pacijent bez historije** → popularity-based fallback (poglavlje 2.2).
- **Nema dostupnih slobodnih termina** → prazna lista uz jasnu poruku, bez greške.
- **Nedovoljno usluga u katalogu** → sistem vraća najbliže dostupne, bez pada.
- **Svi kandidati koje pacijent već ima rezervisane** → isključuju se; ako nema novih, koristi se popularnost.

---

## 10. Ograničenja i moguća poboljšanja

- Content-based pristup ne otkriva potpuno nove vrste pregleda izvan pacijentovih navika
  (*filter bubble*); popularity fallback djelimično to ublažava.
- Buduće nadogradnje: hibrid sa kolaborativnim filtriranjem kada baza korisnika naraste,
  uključivanje ocjena/zadovoljstva pacijenta kao dodatnog težinskog signala (uz obavezu da se taj
  signal onda i stvarno koristi u bodovanju).

---

## 11. Sažetak usklađenosti sa zahtjevima (poglavlje 2.4 uputa)

- ✅ Implementiran jednostavniji modul preporuke poznatim algoritmom (content-based + cosine).
- ✅ Preporuke su **objašnjive** — svaki prijedlog nosi razlog.
- ✅ Ulazni podaci (historija, interakcije) **stvarno se upisuju** u aplikaciji.
- ✅ **Svi** korišteni signali zaista se koriste u bodovanju (nema prikupljanja pa ignorisanja).
- ✅ Implementacija prati ovu dokumentaciju (content-based **i** popularity-based).
