# 🖱️ D3Energy.UI.Automation – WPF Automatický Tester Klikání

## Popis
WPF aplikace pro nahrávání sekvencí kliknutí myší a jejich automatické přehrávání.
Zachycuje chyby při přehrávání a zobrazuje podrobný log.

## Funkce
- **Nahrávání** – globální mouse hook zachytí každé kliknutí (levé, pravé, prostřední) s přesnými souřadnicemi a časovými odstupy
- **Přehrávání** – automaticky pohybuje kurzorem a simuluje kliknutí pomocí WinAPI (`SetCursorPos` + `mouse_event`)
- **Spouštění aplikací podle názvu** – umí spustit aplikaci zadanou názvem/aliasem, cestou k `.exe`, přes AUMID i podle identity MSIX balíčku (Name/PackageFamilyName/PackageFullName)
- **Vyplňování textu** – do sekvence lze přidat krok „TEXT INPUT“ a při přehrávání vyplnit text do zvoleného pole (přes FlaUI nebo klávesnici)
- **Attach režim (strict)** – po připojení aplikace recorder přijímá jen kliknutí z daného procesu a přehrávání blokuje kroky, které by mohly mířit mimo cílový proces
- **Opakování** – nastav kolikrát se sekvence má opakovat (1–999)
- **Rychlost** – multiplikátor: `0.5` = 2× rychleji, `2.0` = 2× pomaleji
- **Error catching** – každý krok je zabalený v try/catch, chyby se zobrazí v logu a neporuší přehrávání
- **Live log** – timestampovaný log všech akcí s barevným rozlišením (OK / CHYBA / INFO)

## Požadavky
- Windows 10/11
- .NET 8 SDK: https://dotnet.microsoft.com/download

## Spuštění

```bash
cd D3Energy.UI.Automation
dotnet build
dotnet run --project D3Energy.UI.Automation
```

Nebo otevři `D3Energy.UI.Automation.sln` ve **Visual Studio 2022**.

## Struktura projektu

```
D3Energy.UI.Automation/
├── D3Energy.UI.Automation.sln
└── D3Energy.UI.Automation/
    ├── D3Energy.UI.Automation.csproj
    ├── App.xaml / App.xaml.cs
    ├── MainWindow.xaml / MainWindow.xaml.cs      ← UI + logika
    ├── Models/
    │   └── ClickAction.cs                         ← datové modely
    └── Services/
        ├── GlobalMouseHook.cs                     ← WinAPI low-level mouse hook
        └── PlaybackService.cs                     ← simulace kliknutí
```

## Jak používat

1. Spusť aplikaci
2. Klikni **"⏺ Spustit nahrávání"**
3. Klikej libovolně po obrazovce (i mimo aplikaci)
4. Klikni **"⏹ Zastavit"**
5. (Volitelné) v sekci **TEXT INPUT** napiš hodnotu a klikni **"⌨ Přidat textový krok"**
6. Nastav počet opakování a rychlost
7. (Volitelné) v sekci **PŘEHRÁVÁNÍ** zadej název aplikace (např. `Kalkulačka`), AUMID (např. `Microsoft.WindowsCalculator_8wekyb3d8bbwe!App`) nebo identitu MSIX balíčku a klikni **"🚀 Spustit aplikaci"**
8. Klikni **"▶ Přehrát"** – aplikace automaticky zreplikuje kliknutí i textové kroky
9. Sleduj log – úspěšné kroky jsou zelené ✓, chyby červené ✗

## Poznámky
- Aplikace používá **globální hook**, ale v attach režimu jsou kroky filtrovány pouze na připojený proces
- Při nahrávání se zaznamenávají **přesné časy** mezi kliknutími
- Při přehrávání lze nastavit **zrychlení/zpomalení** (multiplikátor)
- Každé kliknutí má **30ms settle time** před samotným klikem
