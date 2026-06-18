# NetSearch UI Redesign Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Restyle the NetSearch WPF app into a clean, friendly light-themed interface that a non-technical office worker immediately understands — hero search, segmented mode buttons, collapsible friendly filters (dates/size/quick-types), an onboarding overlay, tooltips everywhere, an accent primary action, and an indexing progress bar.

**Architecture:** All visual styling is centralized in a single merged `ResourceDictionary` (`Themes/AppTheme.xaml`). The two windows (`MainWindow`, `SettingsWindow`) are restructured to use those styles. `MainViewModel` gains a few UI-state members (date filters, filters-expanded, onboarding flag, files/folders checkboxes, a quick-type command) and finally wires the date filter into the query. Core indexing/search logic is unchanged except that the date filter is now passed through. The only unit-tested new logic is a pure `FileTypeGroups` mapping placed in Core.

**Tech Stack:** C# / .NET 9, WPF (`net9.0-windows`), `CommunityToolkit.Mvvm` 8.2.2, xUnit (Core tests only).

## Global Constraints

- App project `NetSearch.App` is `net9.0-windows`, `<UseWPF>true</UseWPF>`; namespaces `NetSearch.App`, `NetSearch.App.ViewModels`, `NetSearch.App.Converters`, `NetSearch.App.Helpers`.
- All UI text is Russian.
- Accent color `#2D7FF9`; accent-hover `#1F6FE5`; window bg `#F4F6F8`; surface `#FFFFFF`; border `#E1E5EA`; text `#1F2430`; muted `#6B7280`. Use these exact hex values.
- All styling lives in `src/NetSearch.App/Themes/AppTheme.xaml`, merged via `App.xaml`. No new inline ad-hoc color literals in window XAML except where a value is one-off layout (margins, widths).
- Core logic (`NetSearch.Core`) is NOT changed except adding `FileTypeGroups`. The 41 existing Core tests must stay green; Task 1 adds `FileTypeGroups` tests.
- Test files require an explicit `using Xunit;` (xunit 2.8.1 has no ImplicitUsings).
- UI tasks have no unit tests; their acceptance bar is: `dotnet build src/NetSearch.App/NetSearch.App.csproj -nologo` succeeds with 0 errors AND `dotnet test tests/NetSearch.Core.Tests/NetSearch.Core.Tests.csproj -nologo` stays green. Interactive GUI smoke is deferred to the controller/user.

---

## File Structure

```
src/NetSearch.Core/
  Search/FileTypeGroups.cs        # NEW: pure "PDF"/"Word"/... -> extensions string
src/NetSearch.App/
  Converters/EnumMatchConverter.cs # NEW: value == parameter (segmented radios)
  Helpers/PlaceholderHelper.cs     # NEW: attached-property placeholder for TextBox
  Themes/AppTheme.xaml             # NEW: brushes, converters, styles
  App.xaml                         # MODIFY: merge AppTheme.xaml
  ViewModels/MainViewModel.cs      # MODIFY: date filters, onboarding, quick-type, files/folders
  MainWindow.xaml                  # REWRITE: new layout
  Views/SettingsWindow.xaml        # MODIFY: restyle + helper text
tests/NetSearch.Core.Tests/
  FileTypeGroupsTests.cs           # NEW
```

---

## Task 1: `FileTypeGroups` (Core, pure mapping)

**Files:**
- Create: `src/NetSearch.Core/Search/FileTypeGroups.cs`
- Create: `tests/NetSearch.Core.Tests/FileTypeGroupsTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `static class FileTypeGroups { static string Extensions(string group); }` returning a comma-separated extension list for the known groups, or `""` for unknown.

- [ ] **Step 1: Write the failing test**

`tests/NetSearch.Core.Tests/FileTypeGroupsTests.cs`:
```csharp
using Xunit;
using NetSearch.Core.Search;

namespace NetSearch.Core.Tests;

public class FileTypeGroupsTests
{
    [Theory]
    [InlineData("PDF", "pdf")]
    [InlineData("Word", "doc, docx")]
    [InlineData("Excel", "xls, xlsx")]
    [InlineData("Фото", "jpg, jpeg, png, gif, bmp")]
    [InlineData("unknown", "")]
    [InlineData("", "")]
    public void Extensions_maps_known_groups(string group, string expected)
    {
        Assert.Equal(expected, FileTypeGroups.Extensions(group));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/NetSearch.Core.Tests/NetSearch.Core.Tests.csproj --filter "FullyQualifiedName~FileTypeGroupsTests"`
Expected: FAIL (compile error — `FileTypeGroups` does not exist).

- [ ] **Step 3: Implement `FileTypeGroups`**

`src/NetSearch.Core/Search/FileTypeGroups.cs`:
```csharp
namespace NetSearch.Core.Search;

/// <summary>Maps friendly file-type group names (shown as quick buttons in the UI)
/// to the comma-separated extension list used by the extension filter.</summary>
public static class FileTypeGroups
{
    public static string Extensions(string group) => group switch
    {
        "PDF" => "pdf",
        "Word" => "doc, docx",
        "Excel" => "xls, xlsx",
        "Фото" => "jpg, jpeg, png, gif, bmp",
        _ => "",
    };
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/NetSearch.Core.Tests/NetSearch.Core.Tests.csproj --filter "FullyQualifiedName~FileTypeGroupsTests"`
Expected: PASS (6 theory cases).

- [ ] **Step 5: Commit**

```bash
git add src/NetSearch.Core/Search/FileTypeGroups.cs tests/NetSearch.Core.Tests/FileTypeGroupsTests.cs
git commit -m "feat(core): add FileTypeGroups mapping for quick type buttons"
```

---

## Task 2: Converters & placeholder helper (App infrastructure)

**Files:**
- Create: `src/NetSearch.App/Converters/EnumMatchConverter.cs`
- Create: `src/NetSearch.App/Helpers/PlaceholderHelper.cs`

**Interfaces:**
- Consumes: nothing.
- Produces:
  - `NetSearch.App.Converters.EnumMatchConverter : IValueConverter` — `Convert` returns `value?.ToString() == parameter?.ToString()`; `ConvertBack` returns the parsed enum when the bound value is `true`, else `Binding.DoNothing`.
  - `NetSearch.App.Helpers.PlaceholderHelper` — attached `string` property `Placeholder` that draws grey hint text over an empty `TextBox`.

- [ ] **Step 1: Implement `EnumMatchConverter`**

`src/NetSearch.App/Converters/EnumMatchConverter.cs`:
```csharp
using System.Globalization;
using System.Windows.Data;

namespace NetSearch.App.Converters;

/// <summary>True when the bound enum value's name equals the ConverterParameter string.
/// On ConvertBack (a radio becoming checked) returns the parsed enum, else Binding.DoNothing.</summary>
public sealed class EnumMatchConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value?.ToString() == parameter?.ToString();

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true && parameter is string name
            ? Enum.Parse(targetType, name)
            : Binding.DoNothing;
}
```

- [ ] **Step 2: Implement `PlaceholderHelper`**

`src/NetSearch.App/Helpers/PlaceholderHelper.cs`:
```csharp
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace NetSearch.App.Helpers;

/// <summary>Attached property that renders grey placeholder text over an empty TextBox.</summary>
public static class PlaceholderHelper
{
    public static readonly DependencyProperty PlaceholderProperty =
        DependencyProperty.RegisterAttached(
            "Placeholder", typeof(string), typeof(PlaceholderHelper),
            new PropertyMetadata(string.Empty, OnPlaceholderChanged));

    public static string GetPlaceholder(DependencyObject o) => (string)o.GetValue(PlaceholderProperty);
    public static void SetPlaceholder(DependencyObject o, string v) => o.SetValue(PlaceholderProperty, v);

    private static void OnPlaceholderChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBox tb) return;
        tb.Loaded -= Refresh;
        tb.TextChanged -= Refresh;
        tb.Loaded += Refresh;
        tb.TextChanged += Refresh;
    }

    private static void Refresh(object sender, RoutedEventArgs e)
    {
        var tb = (TextBox)sender;
        var layer = AdornerLayer.GetAdornerLayer(tb);
        if (layer is null) return;

        var existing = layer.GetAdorners(tb);
        if (existing is not null)
            foreach (var a in existing)
                if (a is PlaceholderAdorner pa) layer.Remove(pa);

        if (string.IsNullOrEmpty(tb.Text) && !string.IsNullOrEmpty(GetPlaceholder(tb)))
            layer.Add(new PlaceholderAdorner(tb, GetPlaceholder(tb)));
    }

    private sealed class PlaceholderAdorner : Adorner
    {
        private readonly string _text;

        public PlaceholderAdorner(UIElement adorned, string text) : base(adorned)
        {
            _text = text;
            IsHitTestVisible = false;
        }

        protected override void OnRender(DrawingContext ctx)
        {
            var tb = (TextBox)AdornedElement;
            var ft = new FormattedText(
                _text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                new Typeface(tb.FontFamily, tb.FontStyle, tb.FontWeight, tb.FontStretch),
                tb.FontSize, new SolidColorBrush(Color.FromRgb(0x9A, 0xA0, 0xA6)),
                VisualTreeHelper.GetDpi(tb).PixelsPerDip);
            ctx.DrawText(ft, new Point(tb.Padding.Left + 6, (tb.ActualHeight - ft.Height) / 2));
        }
    }
}
```

- [ ] **Step 3: Build to verify it compiles**

Run: `dotnet build src/NetSearch.App/NetSearch.App.csproj -nologo`
Expected: build succeeds, 0 errors.

- [ ] **Step 4: Commit**

```bash
git add src/NetSearch.App/Converters/EnumMatchConverter.cs src/NetSearch.App/Helpers/PlaceholderHelper.cs
git commit -m "feat(app): add EnumMatchConverter and PlaceholderHelper"
```

---

## Task 3: Theme resource dictionary + App.xaml merge

**Files:**
- Create: `src/NetSearch.App/Themes/AppTheme.xaml`
- Modify: `src/NetSearch.App/App.xaml`

**Interfaces:**
- Consumes: `NetSearch.App.Converters.EnumMatchConverter` (Task 2).
- Produces (resource keys usable from any window):
  - Brushes: `BgBrush`, `SurfaceBrush`, `BorderBrush`, `AccentBrush`, `AccentHoverBrush`, `TextBrush`, `MutedBrush`.
  - Converters: `EnumMatch`, `BoolToVis`.
  - Styles: implicit `Button`, `TextBox`, `TextBlock`, `CheckBox` defaults; keyed `AccentButton`, `SegmentToggle` (RadioButton), `FilterToggle` (ToggleButton), `SearchBox` (TextBox), `CardBorder` (Border), `MutedText` (TextBlock).

- [ ] **Step 1: Create `AppTheme.xaml`**

`src/NetSearch.App/Themes/AppTheme.xaml`:
```xml
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                    xmlns:conv="clr-namespace:NetSearch.App.Converters">

    <!-- Palette -->
    <Color x:Key="AccentColor">#2D7FF9</Color>
    <SolidColorBrush x:Key="BgBrush" Color="#F4F6F8"/>
    <SolidColorBrush x:Key="SurfaceBrush" Color="#FFFFFF"/>
    <SolidColorBrush x:Key="BorderBrush" Color="#E1E5EA"/>
    <SolidColorBrush x:Key="AccentBrush" Color="#2D7FF9"/>
    <SolidColorBrush x:Key="AccentHoverBrush" Color="#1F6FE5"/>
    <SolidColorBrush x:Key="TextBrush" Color="#1F2430"/>
    <SolidColorBrush x:Key="MutedBrush" Color="#6B7280"/>

    <!-- Converters -->
    <conv:EnumMatchConverter x:Key="EnumMatch"/>
    <BooleanToVisibilityConverter x:Key="BoolToVis"/>

    <!-- TextBlock default -->
    <Style TargetType="TextBlock">
        <Setter Property="Foreground" Value="{StaticResource TextBrush}"/>
        <Setter Property="FontFamily" Value="Segoe UI"/>
        <Setter Property="FontSize" Value="13"/>
    </Style>
    <Style x:Key="MutedText" TargetType="TextBlock" BasedOn="{StaticResource {x:Type TextBlock}}">
        <Setter Property="Foreground" Value="{StaticResource MutedBrush}"/>
    </Style>

    <!-- TextBox default (rounded) -->
    <Style TargetType="TextBox">
        <Setter Property="FontFamily" Value="Segoe UI"/>
        <Setter Property="FontSize" Value="13"/>
        <Setter Property="Foreground" Value="{StaticResource TextBrush}"/>
        <Setter Property="Background" Value="{StaticResource SurfaceBrush}"/>
        <Setter Property="BorderBrush" Value="{StaticResource BorderBrush}"/>
        <Setter Property="BorderThickness" Value="1"/>
        <Setter Property="Padding" Value="8,6"/>
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="TextBox">
                    <Border CornerRadius="6" Background="{TemplateBinding Background}"
                            BorderBrush="{TemplateBinding BorderBrush}"
                            BorderThickness="{TemplateBinding BorderThickness}">
                        <ScrollViewer x:Name="PART_ContentHost" VerticalAlignment="Center"
                                      Margin="{TemplateBinding Padding}"/>
                    </Border>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
        <Style.Triggers>
            <Trigger Property="IsKeyboardFocused" Value="True">
                <Setter Property="BorderBrush" Value="{StaticResource AccentBrush}"/>
            </Trigger>
        </Style.Triggers>
    </Style>

    <Style x:Key="SearchBox" TargetType="TextBox" BasedOn="{StaticResource {x:Type TextBox}}">
        <Setter Property="FontSize" Value="16"/>
        <Setter Property="Padding" Value="12,10"/>
    </Style>

    <!-- Button default (rounded) -->
    <Style TargetType="Button">
        <Setter Property="FontFamily" Value="Segoe UI"/>
        <Setter Property="FontSize" Value="13"/>
        <Setter Property="Foreground" Value="{StaticResource TextBrush}"/>
        <Setter Property="Background" Value="{StaticResource SurfaceBrush}"/>
        <Setter Property="BorderBrush" Value="{StaticResource BorderBrush}"/>
        <Setter Property="BorderThickness" Value="1"/>
        <Setter Property="Padding" Value="12,7"/>
        <Setter Property="Cursor" Value="Hand"/>
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="Button">
                    <Border CornerRadius="6" Background="{TemplateBinding Background}"
                            BorderBrush="{TemplateBinding BorderBrush}"
                            BorderThickness="{TemplateBinding BorderThickness}">
                        <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"
                                          Margin="{TemplateBinding Padding}"/>
                    </Border>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
        <Style.Triggers>
            <Trigger Property="IsMouseOver" Value="True">
                <Setter Property="Background" Value="{StaticResource BgBrush}"/>
            </Trigger>
            <Trigger Property="IsEnabled" Value="False">
                <Setter Property="Opacity" Value="0.5"/>
            </Trigger>
        </Style.Triggers>
    </Style>

    <Style x:Key="AccentButton" TargetType="Button" BasedOn="{StaticResource {x:Type Button}}">
        <Setter Property="Background" Value="{StaticResource AccentBrush}"/>
        <Setter Property="Foreground" Value="White"/>
        <Setter Property="BorderBrush" Value="{StaticResource AccentBrush}"/>
        <Setter Property="FontWeight" Value="SemiBold"/>
        <Style.Triggers>
            <Trigger Property="IsMouseOver" Value="True">
                <Setter Property="Background" Value="{StaticResource AccentHoverBrush}"/>
            </Trigger>
        </Style.Triggers>
    </Style>

    <!-- Segmented mode buttons (RadioButton) -->
    <Style x:Key="SegmentToggle" TargetType="RadioButton">
        <Setter Property="Foreground" Value="{StaticResource TextBrush}"/>
        <Setter Property="FontSize" Value="13"/>
        <Setter Property="Padding" Value="14,6"/>
        <Setter Property="Cursor" Value="Hand"/>
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="RadioButton">
                    <Border x:Name="b" CornerRadius="6" Background="Transparent"
                            BorderBrush="{StaticResource BorderBrush}" BorderThickness="1">
                        <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"
                                          Margin="{TemplateBinding Padding}"/>
                    </Border>
                    <ControlTemplate.Triggers>
                        <Trigger Property="IsMouseOver" Value="True">
                            <Setter TargetName="b" Property="Background" Value="{StaticResource BgBrush}"/>
                        </Trigger>
                        <Trigger Property="IsChecked" Value="True">
                            <Setter TargetName="b" Property="Background" Value="{StaticResource AccentBrush}"/>
                            <Setter TargetName="b" Property="BorderBrush" Value="{StaticResource AccentBrush}"/>
                            <Setter Property="Foreground" Value="White"/>
                        </Trigger>
                    </ControlTemplate.Triggers>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

    <!-- Filters toggle (link-style) -->
    <Style x:Key="FilterToggle" TargetType="ToggleButton">
        <Setter Property="Foreground" Value="{StaticResource AccentBrush}"/>
        <Setter Property="FontSize" Value="13"/>
        <Setter Property="Background" Value="Transparent"/>
        <Setter Property="Cursor" Value="Hand"/>
        <Setter Property="Padding" Value="8,6"/>
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="ToggleButton">
                    <Border Background="Transparent">
                        <ContentPresenter VerticalAlignment="Center" Margin="{TemplateBinding Padding}"/>
                    </Border>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

    <!-- Card / panel -->
    <Style x:Key="CardBorder" TargetType="Border">
        <Setter Property="Background" Value="{StaticResource SurfaceBrush}"/>
        <Setter Property="BorderBrush" Value="{StaticResource BorderBrush}"/>
        <Setter Property="BorderThickness" Value="1"/>
        <Setter Property="CornerRadius" Value="8"/>
        <Setter Property="Padding" Value="12"/>
    </Style>

    <!-- CheckBox default text colour -->
    <Style TargetType="CheckBox">
        <Setter Property="Foreground" Value="{StaticResource TextBrush}"/>
        <Setter Property="FontSize" Value="13"/>
        <Setter Property="VerticalContentAlignment" Value="Center"/>
    </Style>

</ResourceDictionary>
```

- [ ] **Step 2: Merge the theme in `App.xaml`**

Replace the entire contents of `src/NetSearch.App/App.xaml` with:
```xml
<Application x:Class="NetSearch.App.App"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <Application.Resources>
        <ResourceDictionary>
            <ResourceDictionary.MergedDictionaries>
                <ResourceDictionary Source="Themes/AppTheme.xaml"/>
            </ResourceDictionary.MergedDictionaries>
        </ResourceDictionary>
    </Application.Resources>
</Application>
```

(Do not touch `App.xaml.cs`; `OnStartup` is unchanged.)

- [ ] **Step 3: Build to verify it compiles**

Run: `dotnet build src/NetSearch.App/NetSearch.App.csproj -nologo`
Expected: build succeeds, 0 errors. (The existing windows now inherit the base styles; layout is refined in later tasks.)

- [ ] **Step 4: Commit**

```bash
git add src/NetSearch.App/Themes/AppTheme.xaml src/NetSearch.App/App.xaml
git commit -m "feat(app): add centralized light theme and merge it in App.xaml"
```

---

## Task 4: `MainViewModel` UI-state additions + date wiring

**Files:**
- Modify: `src/NetSearch.App/ViewModels/MainViewModel.cs`

**Interfaces:**
- Consumes: `NetSearch.Core.Search.FileTypeGroups` (Task 1), existing `QueryBuilder`, `SearchEngine`, `EntryKind`.
- Produces (new bindable members on `MainViewModel`):
  - `DateTime? ModifiedAfter`, `DateTime? ModifiedBefore`
  - `bool FiltersExpanded`
  - `bool FilesOnly`, `bool FoldersOnly`
  - `bool ShowOnboarding`
  - `ICommand QuickTypeCommand` (parameter: group name string)
  - `RunSearch` now passes the date filters into `QueryBuilder.Build`.

- [ ] **Step 1: Add the new observable properties**

In `src/NetSearch.App/ViewModels/MainViewModel.cs`, find the existing observable-property block ending with:
```csharp
    [ObservableProperty] private string _statusText = "Готово";
    [ObservableProperty] private bool _isBusy;
```
and insert immediately after it:
```csharp
    [ObservableProperty] private DateTime? _modifiedAfter;
    [ObservableProperty] private DateTime? _modifiedBefore;
    [ObservableProperty] private bool _filtersExpanded;
    [ObservableProperty] private bool _filesOnly;
    [ObservableProperty] private bool _foldersOnly;
    [ObservableProperty] private bool _showOnboarding = true;
```

- [ ] **Step 2: Add change handlers for the new filter inputs**

Find the existing handlers block:
```csharp
    partial void OnSearchTextChanged(string value) => Restart();
    partial void OnSelectedModeChanged(SearchMode value) => Restart();
    partial void OnMinSizeChanged(string value) => Restart();
    partial void OnMaxSizeChanged(string value) => Restart();
    partial void OnExtensionsChanged(string value) => Restart();
    partial void OnSelectedKindChanged(EntryKind value) => Restart();
```
and insert immediately after it:
```csharp
    partial void OnModifiedAfterChanged(DateTime? value) => Restart();
    partial void OnModifiedBeforeChanged(DateTime? value) => Restart();
    partial void OnFilesOnlyChanged(bool value) => RecomputeKind();
    partial void OnFoldersOnlyChanged(bool value) => RecomputeKind();

    private void RecomputeKind()
    {
        SelectedKind = (FilesOnly, FoldersOnly) switch
        {
            (true, false) => EntryKind.FilesOnly,
            (false, true) => EntryKind.FoldersOnly,
            _ => EntryKind.All,
        };
        Restart();
    }
```

- [ ] **Step 3: Show onboarding when the index is empty**

Replace the existing `LoadIndexIntoMemory` method:
```csharp
    private void LoadIndexIntoMemory()
    {
        _all = _store.LoadAll().ToList();
        StatusText = $"Индекс: {_all.Count} записей";
        RunSearch();
    }
```
with:
```csharp
    private void LoadIndexIntoMemory()
    {
        _all = _store.LoadAll().ToList();
        ShowOnboarding = _all.Count == 0;
        StatusText = $"Индекс: {_all.Count} записей";
        RunSearch();
    }
```

- [ ] **Step 4: Wire the date filters into the query**

In `RunSearch`, replace:
```csharp
        var query = QueryBuilder.Build(SearchText, SelectedMode, MinSize, MaxSize,
            after: null, before: null, Extensions, SelectedKind);
```
with:
```csharp
        DateTimeOffset? after = ModifiedAfter is { } a ? new DateTimeOffset(a.Date) : null;
        DateTimeOffset? before = ModifiedBefore is { } b
            ? new DateTimeOffset(b.Date.AddDays(1).AddTicks(-1)) : null;
        var query = QueryBuilder.Build(SearchText, SelectedMode, MinSize, MaxSize,
            after, before, Extensions, SelectedKind);
```

- [ ] **Step 5: Add the quick-type command**

Add a new command method next to the other `[RelayCommand]` methods (e.g., right after `OpenSettings`):
```csharp
    [RelayCommand]
    private void QuickType(string group) => Extensions = FileTypeGroups.Extensions(group);
```
Ensure the file's `using` directives include `using NetSearch.Core.Search;` (it is already present from `SearchMode`/`QueryBuilder` usage — verify it is there; if not, add it).

- [ ] **Step 6: Build to verify it compiles**

Run: `dotnet build src/NetSearch.App/NetSearch.App.csproj -nologo`
Expected: build succeeds, 0 errors.

- [ ] **Step 7: Confirm Core tests still green**

Run: `dotnet test tests/NetSearch.Core.Tests/NetSearch.Core.Tests.csproj -nologo`
Expected: all pass (41 + FileTypeGroups from Task 1).

- [ ] **Step 8: Commit**

```bash
git add src/NetSearch.App/ViewModels/MainViewModel.cs
git commit -m "feat(app): wire date filters, onboarding flag and quick-type into MainViewModel"
```

---

## Task 5: `MainWindow.xaml` redesigned layout

**Files:**
- Rewrite: `src/NetSearch.App/MainWindow.xaml`

**Interfaces:**
- Consumes: theme keys (`AccentButton`, `SegmentToggle`, `FilterToggle`, `SearchBox`, `CardBorder`, `MutedText`, `EnumMatch`, `BoolToVis`, brushes) from Task 3; `PlaceholderHelper` (Task 2); `NotConverter` (existing, `NetSearch.App`); `MainViewModel` members incl. the Task 4 additions (`ModifiedAfter`, `ModifiedBefore`, `FiltersExpanded`, `FilesOnly`, `FoldersOnly`, `ShowOnboarding`, `QuickTypeCommand`) and existing commands (`RefreshCommand`, `CancelRefreshCommand`, `ContentSearchCommand`, `OpenSettingsCommand`, `OpenFileCommand`, `OpenFolderCommand`, `CopyPathCommand`), `SelectedRow`.
- Produces: the redesigned main window.

- [ ] **Step 1: Replace `MainWindow.xaml` entirely**

`src/NetSearch.App/MainWindow.xaml`:
```xml
<Window x:Class="NetSearch.App.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:local="clr-namespace:NetSearch.App"
        xmlns:helpers="clr-namespace:NetSearch.App.Helpers"
        Title="NetSearch" Height="680" Width="1060"
        Background="{StaticResource BgBrush}" FontFamily="Segoe UI">
    <Grid Margin="14">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>   <!-- 0 header -->
            <RowDefinition Height="Auto"/>   <!-- 1 search -->
            <RowDefinition Height="Auto"/>   <!-- 2 mode + actions -->
            <RowDefinition Height="Auto"/>   <!-- 3 filters panel (collapsible) -->
            <RowDefinition Height="*"/>      <!-- 4 results -->
            <RowDefinition Height="Auto"/>   <!-- 5 progress -->
            <RowDefinition Height="Auto"/>   <!-- 6 status -->
        </Grid.RowDefinitions>

        <!-- Header -->
        <DockPanel Grid.Row="0" Margin="0,0,0,12">
            <TextBlock DockPanel.Dock="Left" Text="🔍  NetSearch" FontSize="20" FontWeight="SemiBold"
                       VerticalAlignment="Center"/>
            <StackPanel Orientation="Horizontal" HorizontalAlignment="Right">
                <Button Content="⚙  Настройки" Command="{Binding OpenSettingsCommand}" Margin="0,0,8,0"
                        ToolTip="Сетевые пути, авто-обновление и параметры поиска по содержимому"/>
                <Button Content="⟳  Обновить" Style="{StaticResource AccentButton}"
                        Command="{Binding RefreshCommand}"
                        IsEnabled="{Binding IsBusy, Converter={x:Static local:NotConverter.Instance}}"
                        ToolTip="Пересканировать сетевые пути и обновить индекс"/>
            </StackPanel>
        </DockPanel>

        <!-- Hero search -->
        <TextBox Grid.Row="1" Style="{StaticResource SearchBox}"
                 Text="{Binding SearchText, UpdateSourceTrigger=PropertyChanged}"
                 helpers:PlaceholderHelper.Placeholder="Введите имя файла или папки…"
                 ToolTip="Начните печатать — список отфильтруется по мере ввода"/>

        <!-- Mode + actions + filters toggle -->
        <DockPanel Grid.Row="2" Margin="0,10,0,0">
            <TextBlock Text="Искать:" VerticalAlignment="Center" Margin="0,0,8,0"/>
            <RadioButton Content="Имя" GroupName="mode" Style="{StaticResource SegmentToggle}" Margin="0,0,6,0"
                         IsChecked="{Binding SelectedMode, Converter={StaticResource EnumMatch}, ConverterParameter=Substring}"
                         ToolTip="Поиск по части имени"/>
            <RadioButton Content="Маска" GroupName="mode" Style="{StaticResource SegmentToggle}" Margin="0,0,6,0"
                         IsChecked="{Binding SelectedMode, Converter={StaticResource EnumMatch}, ConverterParameter=Wildcard}"
                         ToolTip="Шаблоны со знаками * и ? — например *.pdf"/>
            <RadioButton Content="Regex" GroupName="mode" Style="{StaticResource SegmentToggle}"
                         IsChecked="{Binding SelectedMode, Converter={StaticResource EnumMatch}, ConverterParameter=Regex}"
                         ToolTip="Регулярное выражение для опытных пользователей"/>

            <ToggleButton DockPanel.Dock="Right" Content="▾ Фильтры" Style="{StaticResource FilterToggle}"
                          IsChecked="{Binding FiltersExpanded}"
                          ToolTip="Дополнительные условия: размер, дата, тип"/>
            <Button DockPanel.Dock="Right" Content="🔎 По содержимому" Command="{Binding ContentSearchCommand}"
                    Margin="0,0,8,0"
                    ToolTip="Искать текст ВНУТРИ уже найденных по имени файлов"/>
            <TextBlock/>
        </DockPanel>

        <!-- Filters panel (collapsible; Collapsed row takes 0 height) -->
        <Border Grid.Row="3" Style="{StaticResource CardBorder}" Margin="0,10,0,0"
                Visibility="{Binding FiltersExpanded, Converter={StaticResource BoolToVis}}">
            <WrapPanel>
                <TextBlock Text="Размер:" VerticalAlignment="Center" Margin="0,0,4,0"/>
                <TextBox Width="80" Margin="0,0,4,8" Text="{Binding MinSize, UpdateSourceTrigger=PropertyChanged}"
                         helpers:PlaceholderHelper.Placeholder="напр. 10МБ" ToolTip="Минимальный размер (КБ/МБ/ГБ)"/>
                <TextBlock Text="–" VerticalAlignment="Center" Margin="0,0,4,0"/>
                <TextBox Width="80" Margin="0,0,16,8" Text="{Binding MaxSize, UpdateSourceTrigger=PropertyChanged}"
                         helpers:PlaceholderHelper.Placeholder="напр. 1ГБ" ToolTip="Максимальный размер (КБ/МБ/ГБ)"/>

                <TextBlock Text="Изменён:" VerticalAlignment="Center" Margin="0,0,4,0"/>
                <DatePicker Width="120" Margin="0,0,4,8" SelectedDate="{Binding ModifiedAfter}"
                            ToolTip="Изменён не раньше этой даты"/>
                <TextBlock Text="–" VerticalAlignment="Center" Margin="0,0,4,0"/>
                <DatePicker Width="120" Margin="0,0,16,8" SelectedDate="{Binding ModifiedBefore}"
                            ToolTip="Изменён не позже этой даты"/>

                <TextBlock Text="Тип:" VerticalAlignment="Center" Margin="0,0,4,0"/>
                <Button Content="PDF" Command="{Binding QuickTypeCommand}" CommandParameter="PDF" Margin="0,0,4,8" Padding="8,4"/>
                <Button Content="Word" Command="{Binding QuickTypeCommand}" CommandParameter="Word" Margin="0,0,4,8" Padding="8,4"/>
                <Button Content="Excel" Command="{Binding QuickTypeCommand}" CommandParameter="Excel" Margin="0,0,4,8" Padding="8,4"/>
                <Button Content="Фото" Command="{Binding QuickTypeCommand}" CommandParameter="Фото" Margin="0,0,6,8" Padding="8,4"/>
                <TextBox Width="150" Margin="0,0,16,8" Text="{Binding Extensions, UpdateSourceTrigger=PropertyChanged}"
                         helpers:PlaceholderHelper.Placeholder="pdf, docx" ToolTip="Расширения через запятую"/>

                <CheckBox Content="только файлы" IsChecked="{Binding FilesOnly}" Margin="0,0,12,8"/>
                <CheckBox Content="только папки" IsChecked="{Binding FoldersOnly}" Margin="0,0,0,8"/>
            </WrapPanel>
        </Border>

        <!-- Results + onboarding overlay -->
        <Grid Grid.Row="4" Margin="0,12,0,0">
            <Border Style="{StaticResource CardBorder}" Padding="0">
                <DataGrid ItemsSource="{Binding Results}" AutoGenerateColumns="False"
                          IsReadOnly="True" SelectionMode="Single" BorderThickness="0"
                          Background="{StaticResource SurfaceBrush}" RowBackground="{StaticResource SurfaceBrush}"
                          GridLinesVisibility="Horizontal" HorizontalGridLinesBrush="{StaticResource BorderBrush}"
                          VirtualizingPanel.IsVirtualizing="True" VirtualizingPanel.VirtualizationMode="Recycling"
                          EnableRowVirtualization="True"
                          SelectedItem="{Binding SelectedRow, Mode=OneWayToSource}"
                          MouseDoubleClick="OnRowDoubleClick">
                    <DataGrid.Columns>
                        <DataGridTextColumn Header="Имя" Binding="{Binding Name}" Width="260"/>
                        <DataGridTextColumn Header="Путь" Binding="{Binding Path}" Width="*"/>
                        <DataGridTextColumn Header="Размер" Binding="{Binding SizeText}" Width="90"/>
                        <DataGridTextColumn Header="Изменён" Binding="{Binding Modified}" Width="130"/>
                        <DataGridTextColumn Header="Тип" Binding="{Binding Type}" Width="80"/>
                    </DataGrid.Columns>
                    <DataGrid.ContextMenu>
                        <ContextMenu>
                            <MenuItem Header="Открыть"
                                      Command="{Binding PlacementTarget.DataContext.OpenFileCommand, RelativeSource={RelativeSource AncestorType=ContextMenu}}"/>
                            <MenuItem Header="Открыть папку"
                                      Command="{Binding PlacementTarget.DataContext.OpenFolderCommand, RelativeSource={RelativeSource AncestorType=ContextMenu}}"/>
                            <MenuItem Header="Копировать путь"
                                      Command="{Binding PlacementTarget.DataContext.CopyPathCommand, RelativeSource={RelativeSource AncestorType=ContextMenu}}"/>
                        </ContextMenu>
                    </DataGrid.ContextMenu>
                </DataGrid>
            </Border>

            <Border Style="{StaticResource CardBorder}"
                    Visibility="{Binding ShowOnboarding, Converter={StaticResource BoolToVis}}"
                    HorizontalAlignment="Center" VerticalAlignment="Center" MaxWidth="460">
                <StackPanel>
                    <TextBlock Text="Как начать" FontSize="18" FontWeight="SemiBold" Margin="0,0,0,10"/>
                    <TextBlock Text="1️⃣  Откройте «Настройки» и добавьте сетевой путь (например \\HOST-01\work)"
                               TextWrapping="Wrap" Margin="0,0,0,6"/>
                    <TextBlock Text="2️⃣  Нажмите «Обновить» и дождитесь индексации"
                               TextWrapping="Wrap" Margin="0,0,0,6"/>
                    <TextBlock Text="3️⃣  Начните печатать имя файла — результаты появятся сразу"
                               TextWrapping="Wrap"/>
                </StackPanel>
            </Border>
        </Grid>

        <!-- Indexing progress -->
        <Grid Grid.Row="5" Margin="0,10,0,0"
              Visibility="{Binding IsBusy, Converter={StaticResource BoolToVis}}">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="*"/>
                <ColumnDefinition Width="Auto"/>
            </Grid.ColumnDefinitions>
            <ProgressBar Grid.Column="0" Height="6" IsIndeterminate="True" VerticalAlignment="Center"
                         Foreground="{StaticResource AccentBrush}" Background="{StaticResource BorderBrush}"
                         BorderThickness="0"/>
            <Button Grid.Column="1" Content="Отмена" Margin="10,0,0,0" Command="{Binding CancelRefreshCommand}"
                    ToolTip="Остановить индексацию"/>
        </Grid>

        <!-- Status -->
        <Border Grid.Row="6" Background="{StaticResource SurfaceBrush}" BorderBrush="{StaticResource BorderBrush}"
                BorderThickness="1" CornerRadius="6" Margin="0,10,0,0" Padding="10,6">
            <TextBlock Text="{Binding StatusText}" Style="{StaticResource MutedText}"/>
        </Border>
    </Grid>
</Window>
```

(`MainWindow.xaml.cs` is unchanged — it still has `OnRowDoubleClick` and `InitializeComponent`.)

- [ ] **Step 2: Build to verify it compiles**

Run: `dotnet build src/NetSearch.App/NetSearch.App.csproj -nologo`
Expected: build succeeds, 0 errors. Watch for: unresolved `helpers:`/`local:` namespaces, missing resource keys, binding-to-missing-member (XAML bindings compile even if a member is missing, but a missing resource key is a compile error).

- [ ] **Step 3: Confirm Core tests still green**

Run: `dotnet test tests/NetSearch.Core.Tests/NetSearch.Core.Tests.csproj -nologo`
Expected: all pass.

- [ ] **Step 4: Commit**

```bash
git add src/NetSearch.App/MainWindow.xaml
git commit -m "feat(app): redesigned MainWindow — hero search, segmented modes, filters, onboarding, progress"
```

---

## Task 6: `SettingsWindow` restyle + repackage & final verification

**Files:**
- Modify: `src/NetSearch.App/Views/SettingsWindow.xaml`

**Interfaces:**
- Consumes: theme keys (`AccentButton`, `CardBorder`, `MutedText`, brushes), `PlaceholderHelper`, existing `SettingsViewModel` bindings (`RootsText`, `AutoRefreshMinutes`, `ContentMaxMB`, `TextExtensionsText`) and `OnSave`/`OnCancel` handlers.
- Produces: a settings dialog matching the new theme, with helper text and tooltips.

- [ ] **Step 1: Replace `SettingsWindow.xaml`**

`src/NetSearch.App/Views/SettingsWindow.xaml`:
```xml
<Window x:Class="NetSearch.App.Views.SettingsWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="Настройки" Height="470" Width="600"
        Background="{StaticResource BgBrush}" FontFamily="Segoe UI">
    <Grid Margin="16">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="*"/>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="Auto"/>
        </Grid.RowDefinitions>

        <TextBlock Grid.Row="0" Text="Сетевые пути" FontSize="15" FontWeight="SemiBold" Margin="0,0,0,2"/>
        <TextBlock Grid.Row="1" Style="{StaticResource MutedText}" TextWrapping="Wrap" Margin="0,0,0,6"
                   Text="Например: \\HOST-01\work или Z:\ — по одному пути на строку."/>
        <TextBox Grid.Row="2" Text="{Binding RootsText}" AcceptsReturn="True"
                 VerticalScrollBarVisibility="Auto" TextWrapping="NoWrap" MinHeight="150"
                 ToolTip="Каждая строка — отдельный сетевой путь для индексации"/>

        <StackPanel Grid.Row="3" Orientation="Horizontal" Margin="0,12,0,0">
            <TextBlock Text="Авто-обновление, мин (0 = выкл):" VerticalAlignment="Center" Margin="0,0,8,0"/>
            <TextBox Text="{Binding AutoRefreshMinutes}" Width="70"
                     ToolTip="Через сколько минут автоматически пересканировать пути (0 — никогда)"/>
        </StackPanel>

        <StackPanel Grid.Row="4" Orientation="Horizontal" Margin="0,10,0,0">
            <TextBlock Text="Макс. размер файла для поиска по содержимому, МБ:" VerticalAlignment="Center" Margin="0,0,8,0"/>
            <TextBox Text="{Binding ContentMaxMB}" Width="70"
                     ToolTip="Файлы крупнее не будут открываться при поиске по содержимому"/>
        </StackPanel>

        <StackPanel Grid.Row="5" Margin="0,10,0,0">
            <TextBlock Text="Текстовые расширения (через запятую):" Margin="0,0,0,4"/>
            <TextBox Text="{Binding TextExtensionsText}"
                     ToolTip="Какие файлы считать текстовыми для поиска по содержимому"/>
        </StackPanel>

        <StackPanel Grid.Row="6" Orientation="Horizontal" HorizontalAlignment="Right" Margin="0,16,0,0">
            <Button Content="Сохранить" Width="110" Margin="0,0,8,0" Style="{StaticResource AccentButton}" Click="OnSave"/>
            <Button Content="Отмена" Width="100" Click="OnCancel"/>
        </StackPanel>
    </Grid>
</Window>
```

(`SettingsWindow.xaml.cs` is unchanged — `OnSave`/`OnCancel` handlers stay.)

- [ ] **Step 2: Build to verify it compiles**

Run: `dotnet build src/NetSearch.App/NetSearch.App.csproj -nologo`
Expected: build succeeds, 0 errors.

- [ ] **Step 3: Full verification**

Run: `dotnet test tests/NetSearch.Core.Tests/NetSearch.Core.Tests.csproj -nologo`
Expected: all pass.

- [ ] **Step 4: Repackage the single-file exe**

First confirm no running instance locks the output, then publish:
```bash
dotnet publish src/NetSearch.App/NetSearch.App.csproj -c Release -o publish -nologo
```
Expected: `publish/NetSearch.exe` is produced (~60 MB). (If publish fails with a file lock, close any running `NetSearch.exe` first.)

- [ ] **Step 5: Commit**

```bash
git add src/NetSearch.App/Views/SettingsWindow.xaml
git commit -m "feat(app): restyle SettingsWindow to match the new theme"
```

---

## Self-Review Notes

- **Spec coverage:** light friendly theme + accent `#2D7FF9` (Task 3); centralized theme dict merged in App.xaml (Task 3); hero search with placeholder (Tasks 2, 5); segmented mode buttons via EnumMatch (Tasks 2, 5); collapsible filters (Tasks 4, 5); date filters wired into the query (Task 4); size placeholders + quick-type buttons (Tasks 1, 4, 5); files/folders checkboxes → EntryKind (Task 4); onboarding overlay (Tasks 4, 5); tooltips everywhere (Tasks 5, 6); indexing progress bar + cancel (Task 5, reusing existing `IsBusy`/`CancelRefreshCommand`); settings restyle + helper text (Task 6); repackage exe (Task 6). All spec sections map to a task.
- **Type consistency:** new VM members (`ModifiedAfter`/`ModifiedBefore` as `DateTime?` to bind `DatePicker.SelectedDate`; `FiltersExpanded`, `FilesOnly`, `FoldersOnly`, `ShowOnboarding`; `QuickTypeCommand` from `[RelayCommand] QuickType(string)`) are referenced identically in Task 5 XAML. Theme resource keys used in Tasks 5–6 are exactly those defined in Task 3. `FileTypeGroups.Extensions` (Task 1) is the only signature consumed by Task 4.
- **No core behavior change** beyond passing the date filter through `QueryBuilder.Build` (the engine already supported it); existing tests remain valid.
- **Deferred (YAGNI, per spec §9):** dark theme/toggle, localization, search-algorithm changes.
```
