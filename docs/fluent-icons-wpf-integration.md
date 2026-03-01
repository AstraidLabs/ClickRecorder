# Microsoft Fluent UI System Icons ve WPF (MVVM, net10.0-windows)

## Proč `FluentIcons.Wpf` (2.x) a ne starší `FluentIcons.WPF`

`FluentIcons.Wpf` je aktuálně udržovaná WPF knihovna pro Microsoft Fluent System Icons, navržená pro moderní .NET/WPF scénáře. Oproti staršímu/deprecated balíčku `FluentIcons.WPF` je vhodnější, protože:

- je aktivněji udržovaná (novější API a opravy),
- má WPF-first integraci přes XAML controly,
- používá vektorový rendering (čisté škálování bez PNG),
- umí běžné stylování přes `Foreground`, velikost a styly,
- je kompatibilní s MVVM patternem bez závislosti na dalších UI frameworcích.

> Doporučení pro tento projekt: používej **`FluentIcons.Wpf`** (pozor na casing názvu balíčku).

---

## KROK ZA KROKEM CHECKLIST

1. [ ] Nainstaluj NuGet balíček `FluentIcons.Wpf`.
2. [ ] (Doporučeno pro MVVM) Nainstaluj `CommunityToolkit.Mvvm`.
3. [ ] Přidej `xmlns` do XAML souborů, kde používáš Fluent ikony.
4. [ ] Definuj globální styly ikon (`App.xaml` / `ResourceDictionary`) – velikosti + `Foreground` přes `DynamicResource`.
5. [ ] Použij ikony v `Button`, `MenuItem` a `ToolBar`.
6. [ ] U `IconVariant` používej `Regular`/`Filled`; vyhni se `Color` variantě ve WPF.
7. [ ] V MVVM přepínej ikony přes `DataTrigger` (čisté WPF řešení bez converteru navíc).
8. [ ] Ověř disabled stav (ikona se zšedne přes děděný `Foreground`/`Opacity`).

---

## Instalace

### 1) Visual Studio (NuGet UI)

1. Pravým klikem na projekt → **Manage NuGet Packages...**
2. Záložka **Browse**.
3. Vyhledej: `FluentIcons.Wpf`
4. Nainstaluj do projektu `D3Energy.UI.Automation`.

Volitelně také:
- `CommunityToolkit.Mvvm`

### 2) .NET CLI

```bash
dotnet add package FluentIcons.Wpf
dotnet add package CommunityToolkit.Mvvm
```

### 3) `PackageReference` do `.csproj`

```xml
<ItemGroup>
  <PackageReference Include="FluentIcons.Wpf" Version="2.0.0" />
  <PackageReference Include="CommunityToolkit.Mvvm" Version="8.4.0" />
</ItemGroup>
```

---

## Hotové snippet-y

## `App.xaml`

```xml
<Application x:Class="D3Energy.UI.Automation.App"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:icons="clr-namespace:FluentIcons.Wpf;assembly=FluentIcons.Wpf"
             StartupUri="MainWindow.xaml">
    <Application.Resources>
        <ResourceDictionary>
            <ResourceDictionary.MergedDictionaries>
                <ResourceDictionary Source="Themes/LightTheme.xaml" />
            </ResourceDictionary.MergedDictionaries>

            <!-- Projektový brush, který můžeš přepínat dle tématu -->
            <SolidColorBrush x:Key="PrimaryForegroundBrush" Color="#E8EAED"/>
            <SolidColorBrush x:Key="SecondaryForegroundBrush" Color="#9AA0A6"/>

            <!-- Crisp rendering pro appku -->
            <Style TargetType="FrameworkElement">
                <Setter Property="UseLayoutRounding" Value="True" />
                <Setter Property="SnapsToDevicePixels" Value="True" />
            </Style>

            <!-- Výchozí styl Fluent ikon -->
            <Style TargetType="icons:FluentIcon">
                <Setter Property="Width" Value="16" />
                <Setter Property="Height" Value="16" />
                <Setter Property="Foreground" Value="{DynamicResource PrimaryForegroundBrush}" />
                <Setter Property="VerticalAlignment" Value="Center" />
                <Setter Property="IconVariant" Value="Regular" />
            </Style>

            <!-- Kontextové varianty velikosti -->
            <Style x:Key="ToolbarFluentIconStyle" TargetType="icons:FluentIcon" BasedOn="{StaticResource {x:Type icons:FluentIcon}}">
                <Setter Property="Width" Value="20" />
                <Setter Property="Height" Value="20" />
            </Style>

            <Style x:Key="HeroFluentIconStyle" TargetType="icons:FluentIcon" BasedOn="{StaticResource {x:Type icons:FluentIcon}}">
                <Setter Property="Width" Value="24" />
                <Setter Property="Height" Value="24" />
            </Style>
        </ResourceDictionary>
    </Application.Resources>
</Application>
```

> Poznámka: `IconVariant="Color"` může být ve WPF problematický. V produkci používej primárně `Regular`/`Filled`.

---

## `MainWindow.xaml`

```xml
<Window x:Class="D3Energy.UI.Automation.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:icons="clr-namespace:FluentIcons.Wpf;assembly=FluentIcons.Wpf"
        xmlns:vm="clr-namespace:D3Energy.UI.Automation.ViewModels"
        Title="D3Energy.UI.Automation" Height="500" Width="900"
        TextOptions.TextFormattingMode="Display"
        TextOptions.TextRenderingMode="ClearType">

    <Window.DataContext>
        <vm:MainViewModel />
    </Window.DataContext>

    <DockPanel Margin="16">
        <!-- Menu -->
        <Menu DockPanel.Dock="Top">
            <MenuItem Header="Soubor">
                <MenuItem.Icon>
                    <icons:FluentIcon Icon="Document" IconVariant="Regular" />
                </MenuItem.Icon>
                <MenuItem Header="Nový test">
                    <MenuItem.Icon>
                        <icons:FluentIcon Icon="DocumentAdd" IconVariant="Filled" />
                    </MenuItem.Icon>
                </MenuItem>
            </MenuItem>
        </Menu>

        <!-- Toolbar -->
        <ToolBarTray DockPanel.Dock="Top" Margin="0,8,0,8">
            <ToolBar>
                <Button ToolTip="Spustit / Pozastavit"
                        Command="{Binding TogglePlayCommand}">
                    <StackPanel Orientation="Horizontal">
                        <icons:FluentIcon Style="{StaticResource ToolbarFluentIconStyle}">
                            <icons:FluentIcon.Style>
                                <Style TargetType="icons:FluentIcon" BasedOn="{StaticResource ToolbarFluentIconStyle}">
                                    <Setter Property="Icon" Value="Play" />
                                    <Style.Triggers>
                                        <DataTrigger Binding="{Binding IsPlaying}" Value="True">
                                            <Setter Property="Icon" Value="Pause" />
                                            <Setter Property="IconVariant" Value="Filled" />
                                        </DataTrigger>
                                    </Style.Triggers>
                                </Style>
                            </icons:FluentIcon.Style>
                        </icons:FluentIcon>
                        <TextBlock Text="Přehrát" Margin="8,0,0,0" VerticalAlignment="Center" />
                    </StackPanel>
                </Button>

                <!-- Icon-only button -->
                <Button ToolTip="Stop" Margin="8,0,0,0" IsEnabled="{Binding IsPlaying}">
                    <icons:FluentIcon Style="{StaticResource ToolbarFluentIconStyle}"
                                      Icon="Stop"
                                      IconVariant="Filled" />
                </Button>
            </ToolBar>
        </ToolBarTray>

        <!-- Tlačítko mimo toolbar: ukázka per-control override -->
        <StackPanel Orientation="Horizontal" VerticalAlignment="Center">
            <Button Command="{Binding TogglePlayCommand}" IsEnabled="{Binding IsTransportEnabled}">
                <StackPanel Orientation="Horizontal">
                    <icons:FluentIcon Width="24" Height="24" Foreground="{DynamicResource SecondaryForegroundBrush}">
                        <icons:FluentIcon.Style>
                            <Style TargetType="icons:FluentIcon" BasedOn="{StaticResource HeroFluentIconStyle}">
                                <Setter Property="Icon" Value="Play" />
                                <Setter Property="IconVariant" Value="Regular" />
                                <Style.Triggers>
                                    <DataTrigger Binding="{Binding IsPlaying}" Value="True">
                                        <Setter Property="Icon" Value="Pause" />
                                        <Setter Property="IconVariant" Value="Filled" />
                                    </DataTrigger>
                                </Style.Triggers>
                            </Style>
                        </icons:FluentIcon.Style>
                    </icons:FluentIcon>
                    <TextBlock Text="Toggle" Margin="8,0,0,0" VerticalAlignment="Center" />
                </StackPanel>
            </Button>
        </StackPanel>
    </DockPanel>
</Window>
```

### Proč `DataTrigger` místo converteru pro Play/Pause?

Ve WPF je pro jednoduché přepnutí jedné nebo dvou hodnot (`Icon`, `IconVariant`) `DataTrigger` čistší:
- méně infrastruktury (žádný extra converter),
- logika je přímo u vizuálu,
- jednodušší údržba i čitelnost v XAML.

Converter dává smysl až u složitější mapovací logiky nebo opakovaného použití napříč mnoha view.

---

## `MainViewModel.cs` (CommunityToolkit.Mvvm)

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace D3Energy.UI.Automation.ViewModels;

public partial class MainViewModel : ObservableObject
{
    [ObservableProperty]
    private bool isPlaying;

    [ObservableProperty]
    private bool isTransportEnabled = true;

    public MainViewModel()
    {
        TogglePlayCommand = new RelayCommand(TogglePlay, CanTogglePlay);
    }

    public IRelayCommand TogglePlayCommand { get; }

    private void TogglePlay()
    {
        IsPlaying = !IsPlaying;
    }

    private bool CanTogglePlay() => IsTransportEnabled;

    partial void OnIsTransportEnabledChanged(bool value)
    {
        TogglePlayCommand.NotifyCanExecuteChanged();
    }
}
```

---

## Výkon + kvalita (produkční doporučení)

- Používej sdílené `Style` v `App.xaml`/`ResourceDictionary` místo opakovaného inline nastavování.
- V icon-heavy UI preferuj šablony (`ControlTemplate`) a sdílené resources.
- Nevytvářej zbytečně nové instance brushů inline v každém controlu; používej `DynamicResource`.
- Používej konzistentní velikosti ikon (16/20/24) kvůli pixel alignmentu a vizuální čistotě.
- Nech ikony dědit `Foreground` z rodiče (u disabled stavu se ztlumení řeší přirozeně stylem/opacity rodiče).

---

## Troubleshooting: když se ikona nezobrazuje

1. Zkontroluj NuGet balíček:
   - je opravdu `FluentIcons.Wpf` (ne staré/deprecated `FluentIcons.WPF`)?
2. Zkontroluj `xmlns`:
   - `xmlns:icons="clr-namespace:FluentIcons.Wpf;assembly=FluentIcons.Wpf"`
3. Zkontroluj název ikony (`Icon="..."`):
   - překlep v názvu enum hodnoty je nejčastější problém.
4. Zkontroluj variantu:
   - používej `Regular`/`Filled`; `Color` ve WPF raději nepoužívej.
5. Zkontroluj kontrast:
   - `Foreground` může splývat s pozadím.
6. Zkontroluj DataContext/Binding:
   - pokud trigger navazuje na `IsPlaying`, musí být správně nastavený DataContext.
