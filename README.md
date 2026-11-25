<p align="center"><img src="lumo.png" width="160" alt="Lumo"></p>

<h1 align="center">Lumo</h1>
<p align="center">Vlastní „Dynamic Island“ pro Windows 10 a 11 · od <b>Bolek98</b> [B98]</p>

<p align="center">
  <a href="https://github.com/Bolek98/Lumo/releases/latest"><b>⬇ Stáhnout instalátor</b></a> ·
  <a href="https://bolek98.github.io/#programy">Bolkova dílna</a>
</p>

---

Lumo je malý černý ostrůvek nahoře (nebo dole) na obrazovce. V klidu ukazuje hodiny a počasí,
když hraje hudba, má obal alba a ekvalizér. Po najetí myší se rozbalí do panelu se záložkami.

## Co umí

**🎵 Hudba**
- Spotify, YouTube v prohlížeči, Apple Music a další přehrávače (přes ovládání médií Windows)
- Obal alba, název, interpret, posuvník (kliknutím přeskočíš), předchozí / pauza / další
- Barva záře podle obalu alba, kolečkem myši hlasitost
- Při nové skladbě se sám na chvíli rozbalí

**🖥️ Systém**
- Teplota a vytížení GPU (NVIDIA i AMD), vytížení CPU, RAM a rychlost sítě
- Teplota CPU při spuštění jako správce a s ovladačem [PawnIO](https://pawnio.eu)
- Baterie Bluetooth sluchátek, ovladačů a myší, s upozorněním při vybití

**☀️ Počasí**
- Aktuální počasí, pocitová teplota, vítr, 3 dny dopředu a upozornění „za 2 h začne pršet“
- Libovolné město, data z [Open-Meteo](https://open-meteo.com) (zdarma, bez účtu)

**📝 Widgety**
- Směny: dnes denní / noční / volno, odpočet do směny a připomínka předem (cyklus si nastavíš)
- Časovač s upozorněním
- Poznámky, které se samy ukládají

**🔧 Nástroje**
- Přihrádka na soubory: přetáhni soubor na ostrůvek, později ho vytáhneš zpátky
- Historie schránky: posledních 10 textů, jen v paměti (hesla ze správců hesel se nezachytávají)
- Rychlé spouštění oblíbených aplikací

**⚙️ Nastavení**
- Nahoře / dole, přetažení myší kamkoli
- „Jen na ploše“ (okna ho překryjí, vyjede po najetí k okraji obrazovky) nebo „vždy navrchu“
- Barva, průhlednost, velikost, které záložky zobrazit, spouštění s Windows
- Ve hrách přes celou obrazovku se schová

## Instalace

1. Stáhni **Lumo-Setup.exe** z [Releases](https://github.com/Bolek98/Lumo/releases/latest)
2. Spusť ho. Práva správce nejsou potřeba, instaluje se do `%LOCALAPPDATA%\Programs\Lumo`
3. Když Windows ukáže modré okno SmartScreen, klikni na **Další informace → Přesto spustit**
   (instalátor není digitálně podepsaný)

Odinstalace: *Nastavení Windows → Aplikace → Lumo*, nebo „Odinstalovat Lumo“ v nabídce Start.
Nastavení a poznámky jsou v `%APPDATA%\Lumo`.

## Sestavení ze zdrojáků

Potřebuješ [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

```bash
dotnet publish -c Release
# výsledek: bin/Release/net8.0-windows10.0.19041.0/win-x64/publish/Lumo.exe
```

Instalátor: zkopíruj `Lumo.exe` do složky `dist/` a spusť
`makensis -DVERSION=4.0.0 installer/lumo.nsi` ([NSIS](https://nsis.sourceforge.io)).

Použité knihovny: [LibreHardwareMonitor](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor) (senzory).

## Historie verzí

- **4.0** – nový název Lumo (dříve Ostrůvek), okno Nastavení, barvy, velikost, výběr záložek, instalátor
- **3.0** – počasí, baterie zařízení, přihrádka, historie schránky, rychlé spouštění, odpočet směny
- **2.0** – záložky Systém (teploty, vytížení) a Widgety (směny, časovač, poznámky)
- **1.1** – přetahování myší, vyjetí od okraje, hlasitost kolečkem, přeskakování ve skladbě
- **1.0** – první verze: hodiny, hudba, obal alba, nahoře / dole
