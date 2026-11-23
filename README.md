# SMC Compass

Natív, 64 bites Windows-alkalmazás PSD sales munkához. Havi forecast, kézi tényadatok és éves cél, SpeedUp vevőlista és 9-box pontozás, valamint olvasási Sugar CRM kapcsolat. A felület WinForms ablakban fut, helyi webszerver nélkül.

## Indítás

1. Bontsd ki a teljes ZIP-et egy mappába.
2. Indítsd el az `SMC-Compass.exe` fájlt. A Windows-ablak közvetlenül megnyílik.
3. A **Források** fülön látható az utolsó sikeres import és a forrás típusa. Az **Adatfrissítés** a beállított adatforrásokat frissíti.

## Programfrissítés és verziónapló

A **Programfrissítés** gomb ellenőrzi a `Csomaika/SMC-Compass` legfrissebb nyilvános GitHub-kiadását. Új verziónál megmutatja a kiadás leírását, majd jóváhagyás után ellenőrzi a ZIP SHA-256 lenyomatát, külön verziómappába telepíti és megnyitja az új verziót. A régi indító ezután a telepített új verziót indítja. A helyi beállítások és adatok a felhasználói adatmappában maradnak. A **Verziónapló** fülön a telepített verzió és a kiadástörténet olvasható.

## Sales és SpeedUp

- A sales import a kiválasztott munkalap április–márciusi oszlopait, valamint a `TARGET` és `ACTUAL` sorpárokat olvassa. Az alap a `REPORT26`, az előző évhez a `REPORT25` tartozik. Új üzleti évhez állítsd át a munkalapokat és a kezdő évet.
- A darabszámok év elejétől halmozottak. A Compass a havi értéket a két szomszédos halmozott érték különbségeként kezeli. A kézzel rögzített havi tény elsőbbséget kap az adott hónapnál. Hiányzó hónap után a forecast az utolsó folytonos adatból indul.
- Az alapmodell a legutóbbi legfeljebb három tényhónap átlagát használja. A szezonális módszer ezt fele-fele arányban vegyíti a korábbi teljes üzleti év havi mintájával, az aktuális és előző YTD arányával skálázva. A ± eltérés a jövőbeli növekményekre vonatkozik. A tartomány üzleti forgatókönyv.
- A 9-box tengelyei a piaci potenciál és az SMC-pozíció. Mindkettő értékelése 1–3. A helyi pontszám és következő lépés az ügyfélkódhoz kötődik.
- A SpeedUp-nál a munkalap és a táblázatok mentett Excel-szűrőit, valamint a mentett rejtett sorokat olvassa. A **Csak az Excelben látható sorok** kapcsoló az exportot is szabályozza. A böngészőben személyesen vagy ideiglenesen alkalmazott Excel-szűréshez a mentett fájlban is meg kell jelennie a hatásnak.
- Az Excel-export pillanatkép négy munkalappal. Az interaktív forecast újraszámítását a Compass kezeli.

## Automatikus forrásfrissítés

**OneDrive.** Szinkronizáld az érintett SharePoint-dokumentumtárat a céges OneDrive-klienssel. A Források oldalon válaszd a helyi módot, add meg a két fájl teljes szinkronizált útvonalát, majd kattints az **Adatfrissítés** gombra. Ha az importált fájlokat te szeretnéd cserélni, válaszd a **Kézi Excel-import** módot. A Letöltések mappából felismert példány az adott letöltés állapotát tükrözi.

**Közvetlen SharePoint.** A SharePoint-linkeknek `sourcedoc` azonosítót kell tartalmazniuk, és azonos tenantban kell lenniük. Az IT által jóváhagyott Entra alkalmazásregisztrációhoz engedélyezett nyilvános kliens, eszközkódos bejelentkezés és a **SharePoint** API `AllSites.Read` delegált engedélye szükséges. A Források fülön válts közvetlen módra, add meg a tenant- és client-ID-t, majd a Compassban megjelenő kóddal jelentkezz be a Microsoft oldalán. A hozzáférési adatok a futó folyamat memóriájában maradnak. Újraindításkor ismét be kell jelentkezni. A lekérés a program futása alatt történik.

**Sugar CRM.** Add meg a példány HTTPS alapcímét és a REST-verziót, majd egy érvényes, a szervezet által biztosított Sugar REST access tokent. A program olvasási kéréssel hozza be az `Opportunities` rekordokat. A súlyozott pipeline pénznemenként összegez, a darabszámos forecasttól függetlenül.

## Helyi adatok és fejlesztés

A beállítások, importált adatok, kézi számok és vevőértékelések a `%LOCALAPPDATA%\SMC Compass\compass.json` fájlban vannak. Mentéshez az alkalmazás leállítása után ezt az egy fájlt másold a megfelelő céges helyre. Az állomány a munkafüzetekből és a Sugarból átvett üzleti adatokat tartalmazhat.

Hordozható csomag készítéséhez futtasd a `python build_windows.py` parancsot 64 bites CPython 3.12-vel Windowson. Az alkalmazás WinForms-szal fordul, az Excel-kezelő háttérfolyamat a csomagolt CPythont használja. A környezetspecifikus `compass-profile.json` privát buildhez a `--profile` kapcsolóval adható meg. Kiadáshoz emeld a `version.txt` verziószámát és frissítsd a `CHANGELOG.md` fájlt. A nyilvános kiadást a GitHub Actions munkafolyamat csomagolja.
