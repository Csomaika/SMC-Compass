# SMC Compass · verziónapló

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
