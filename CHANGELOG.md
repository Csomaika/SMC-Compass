# SMC Compass · verziónapló

## 1.2.16 · Sugar CRM csatlakozás

- A teljes Sugar-böngészőlink használható. A weboldal megnyitása és az Opportunities REST-import külön művelet.
- A Sugar-import önállóan fut. Az engedélyezett Sugar API-platformhoz tartozó OAuth-belépés futás közben tud tokent megújítani. A kézzel megadott REST token továbbra is használható.
- A pipeline jelzi az érvénytelen vagy lejárt tokent, a hiányzó olvasási engedélyt és az API helyett érkező webes belépést.

## 1.2.15 · Oldalsáv megjelenítési javítása

- A menüpontok minden képkocka előtt újrarajzolják saját hátterüket. Ez javítja a maximalizált ablakban lapváltáskor összecsúszó feliratokat.

## 1.2.14 · Kék és menta árnyalatok

- A navigáció kékből mentába váltó hátteret kapott. A 9-box jobb oldali összesítője türkiz fejlécen emeli ki a vevőszámot.

## 1.2.13 · Világosabb munkaterület

- Az oldalsáv világos hátteret kapott, a 9-box összefoglaló fehér lett. A kijelölt menüpontot és a mátrixmezőt türkiz jelzi.

## 1.2.12 · Egyértelműbb 9-box besorolások

- A mezők 01-től 09-ig számozottak, rövid nevet kaptak, és a lista a mezőszám mellett mindkét pontszámot mutatja.
- A két tengely értelmezését a jobb oldali panel magyarázza. Csempe kiválasztásakor itt jelenik meg az adott helyzethez javasolt értékesítési lépés.

## 1.2.11 · Megújult 9-box vevőmátrix

- A csempék megjelenítik a darabszámot és a besorolt vevőkön belüli részarányt. A kitöltés és az aránysáv szűréskor animálva igazodik az adatokhoz.
- A piaci potenciál és az SMC-pozíció külön tengelyt kapott. Egyetlen kattintással szűrhető az adott mező vagy a pontozásra váró vevők listája.

## 1.2.10 · Folyamatosabb animációk

- A forecast, a menü, a mutatószámok, a kapcsolatjelző és a frissítési sáv közös, közel 60 FPS-es ütemezést kapott.
- A Windows összeállítás közben külön mérjük, hogy a vezérlők tényleges újrarajzolása eléri-e a 30 FPS-t.

## 1.2.9 · Mozgás a munkaterületen

- A menü kékeszöld jelölője finoman vált oldalt. Az Áttekintés megnyitásakor fénypont kíséri a felrajzolódó forecastot.
- Adatfrissítés közben mozgó sáv jelenik meg a fejlécben. Az aktív forrásnál lassan pulzál a kapcsolatjelző. A mozgások követik a Windows animációs beállítását.

## 1.2.8 · Új Compass-ikon

- Az új kék iránytű jelenik meg a Start menüben, a tálcán, az ablak címsorában és az alkalmazás oldalsávjában. Az ikon tíz méretben készült.

## 1.2.7 · Egyetlen adatfrissítő gomb és beépített belépés

- Az Adatfrissítés gomb minden beállított forrást frissít. A programverzió frissítése a Verziónaplóban található.
- A Microsoft bejelentkezési felülete a Compass munkaterületén jelenik meg. A Források alatt beállítható a céges e-mail-cím és a WebView2 jelszómentési ajánlata.

## 1.2.6 · Sales-frissítés bejelentkezés után

- Ha a SharePoint a letöltés helyett a webhely másik oldalára visz, a Compass egyszer újrakéri a munkafüzetet. A bejelentkezési ablakból kézzel is folytatható a letöltés.
- A grafikon alatt látható az utolsó sikeres import ideje és az első hiányzó tényhónap. Frissítéskor az állapotsor jelzi, ha a kiválasztott mutató tényadatai változatlanok.

## 1.2.5 · Sales-import a Compasson belül

- A SharePoint-frissítés egy saját ideiglenes fájlba tölt, menti a beolvasott adatokat, majd eltávolítja a példányt. Az állapotsor jelzi az import idejét és a kijelzett utolsó tényhónapot.
- Az időzített frissítéshez a Compass beépített Edge WebView2 nézetet használ. Ha bejelentkezés szükséges, a kézi Sales-frissítés gombbal nyitható meg.

## 1.2.4 · Időzített Sales-frissítés SharePointról

- A böngészős Sales-forrás indításkor és a beállított percenként automatikusan letölti és importálja a munkafüzetet.
- A régi, örökölt helyi mód SharePoint-link és üres helyi útvonal esetén automatikusan böngészős módra vált.

## 1.2.3 · Programfrissítés a Compassból

- A **Programfrissítés** jóváhagyás után letölti és ellenőrzi az új MSI-t, bezárja a Compasst, majd elindítja a Windows Installer telepítését.

## 1.2.2 · Sales-frissítés böngészőből

- A **Sales frissítése böngészőből** gomb a beállított SharePoint-munkafüzet letöltését indítja az alapértelmezett böngészőben.
- A Compass felismeri a gombnyomás után érkező Excel-fájlt, beolvassa a Sales-adatokat és újraszámolja a forecastot. A letöltési mappa állítható.

## 1.2.1 · Automatikus Excel-frissítés

- A Sales és a SpeedUp külön választhat OneDrive-szinkronizált, közvetlen SharePoint- vagy kézi forrást.
- A helyi és szinkronizált Excel-fájlokat a Compass 15 másodpercenként ellenőrzi. Az új mentésből automatikusan újraszámolja az adatokat.
- A Források fül külön jelzi, ha a SharePoint-belépéshez az azonosítók hiányoznak, valamint az utolsó sikeres import idejét.

## 1.2.0 · Telepítő és új műszerfal

- Windows Installer MSI, Start menüvel és a Windows Telepített alkalmazások listájából indítható eltávolítással.
- Sötét oldalsáv, gradient fejléc, animált KPI-számok és kirajzolódó előrejelzési grafikon. A három forgatókönyv külön kártyán látszik.
- Telepített kiadásnál a programfrissítés az új Windows MSI-kiadást mutatja meg. A korábbi munkaterület adatai megmaradnak.

## 1.1.0 · Windows Desktop

- Natív Windows-ablak, böngésző és helyi webszerver nélkül.
- Interaktív forecast, havi tényadatok, éves cél és 9-box vevőmátrix az asztali felületen.
- Külön adatfrissítés és programfrissítés. A program a nyilvános GitHub-kiadásokból ellenőrzi az új verziót, és telepítés előtt ellenőrzi a csomag SHA-256 lenyomatát.
- Verziónapló a programon belül. A frissítés átvált az új verzióra, a helyi adatok és a forrásbeállítások megmaradnak.
- A közvetlen SharePoint-elérés eszközkódos Microsoft-belépést használ. A OneDrive-szinkronizált és kézi Excel-import elérhető.

## 1.0.0 · Első kiadás

- Darabszámos sales forecast, három forgatókönyv, havi ténybevitel és Excel-export.
- SpeedUp 9-box pontozás, mentett Excel-szűrés és Sugar CRM pipeline.
- A Windows-indító a helyi böngészőben nyitotta meg a felületet.
