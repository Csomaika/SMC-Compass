# SMC Compass

Windowsos PSD sales munkaterület. Havi forecast, kézi tényadatok és éves cél, SpeedUp vevőlista és 9-box pontozás, valamint olvasási Sugar CRM kapcsolat. A felület a helyi böngészőben nyílik meg. A program és az adatbázis a gépen fut.

## Indítás

1. Bontsd ki a teljes ZIP-et egy mappába.
2. Indítsd el az `SMC-Compass.exe` fájlt. A program a `http://localhost:43781/` címen nyílik meg.
3. A **Források** oldalon látható az utolsó sikeres import és a forrás típusa. A programot a Források alján lehet leállítani.

## Sales és SpeedUp

- A sales import a kiválasztott munkalap április–márciusi oszlopait, valamint a `TARGET` és `ACTUAL` sorpárokat olvassa. Az alap a `REPORT26`, az előző évhez a `REPORT25` tartozik. Új üzleti évhez állítsd át a munkalapokat és a kezdő évet.
- A darabszámok év elejétől halmozottak. A Compass a havi értéket a két szomszédos halmozott érték különbségeként kezeli. A kézzel rögzített havi tény elsőbbséget kap az adott hónapnál. Hiányzó hónap után a forecast az utolsó folytonos adatból indul.
- Az alapmodell a legutóbbi legfeljebb három tényhónap átlagát használja. A szezonális módszer ezt fele-fele arányban vegyíti a korábbi teljes üzleti év havi mintájával, az aktuális és előző YTD arányával skálázva. A ± eltérés a jövőbeli növekményekre vonatkozik. A tartomány üzleti forgatókönyv.
- A 9-box tengelyei a piaci potenciál és az SMC-pozíció. Mindkettő értékelése 1–3. A helyi pontszám és következő lépés az ügyfélkódhoz kötődik.
- A SpeedUp-nál a munkalap és a táblázatok mentett Excel-szűrőit, valamint a mentett rejtett sorokat olvassa. A **Csak az Excelben látható sorok** kapcsoló az exportot is szabályozza. A böngészőben személyesen vagy ideiglenesen alkalmazott Excel-szűréshez a mentett fájlban is meg kell jelennie a hatásnak.
- Az Excel-export pillanatkép négy munkalappal. Az interaktív forecast újraszámítását a Compass kezeli.

## Automatikus forrásfrissítés

**OneDrive.** Szinkronizáld az érintett SharePoint-dokumentumtárat a céges OneDrive-klienssel. A Források oldalon válaszd a helyi módot, add meg a két fájl teljes szinkronizált útvonalát, majd kattints a **Frissítés** gombra. Ha az importált fájlokat te szeretnéd cserélni, válaszd a **Kézi Excel-import** módot. A Letöltések mappából felismert példány az adott letöltés állapotát tükrözi.

**Közvetlen SharePoint.** A megadott SharePoint-linkeknek `sourcedoc` azonosítót kell tartalmazniuk, és azonos tenantban kell lenniük. Az IT által jóváhagyott Entra alkalmazásregisztrációhoz a **SharePoint** API `AllSites.Read` delegált engedélye, mobil/asztali kliens átirányítás és `http://localhost:43781/oauth/callback` átirányítási URI szükséges. A Források oldalon válts közvetlen módra, add meg a tenant- és client-ID-t, majd jelentkezz be. A hozzáférési adatok a futó folyamat memóriájában maradnak. Újraindításkor ismét be kell jelentkezni. A lekérés a program futása alatt történik.

**Sugar CRM.** Add meg a példány HTTPS alapcímét és a REST-verziót, majd egy érvényes, a szervezet által biztosított Sugar REST access tokent. A program olvasási kéréssel hozza be az `Opportunities` rekordokat. A súlyozott pipeline pénznemenként összegez, a darabszámos forecasttól függetlenül.

## Helyi adatok és fejlesztés

A beállítások, importált adatok, kézi számok és vevőértékelések a `%LOCALAPPDATA%\SMC Compass\compass.json` fájlban vannak. Mentéshez az alkalmazás leállítása után ezt az egy fájlt másold a megfelelő céges helyre. Az állomány a munkafüzetekből és a Sugarból átvett üzleti adatokat tartalmazhat.

Futtatás forrásból Python 3.12-vel: `python app.py`. Hordozható csomag készítéséhez futtasd a `python build_windows.py` parancsot 64 bites CPython 3.12-vel Windowson. A csomagban futó CPythonhoz mellékelve van a Python-licenc. A környezetspecifikus `compass-profile.json` a futtatott `app.py` mellé helyezhető, vagy privát buildhez megadható a `--profile` kapcsolóval. A programlogót külön Compass embléma adja SMC-kék színvilággal.
