# Local Disks, Cleaner, Icon & Installer — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a WizTree-style Local Disks usage tab and a safe, preview-first disk/cache cleaner to NetSearch, give the app a real icon, and ship it as an Inno Setup installer.

**Architecture:** Pure logic (size formatting, folder-size tree, squarified treemap, clean-scan helpers) lives in `NetSearch.Core` and is unit-tested with no admin or live volume. The WPF app gains a 3-tab shell (Поиск / Локальные диски / Очистка) with two new child view-models. The disk scan reuses the existing `MftEnumerator` (elevated NTFS) or `Crawler` (fallback). The cleaner deletes only allow-listed locations through the shell (Recycle Bin where feasible). An `IconGen` tool produces a committed multi-size `.ico`; `publish.ps1` compiles an Inno Setup `Setup.exe`.

**Tech Stack:** .NET 9 · WPF (`net9.0-windows`) · CommunityToolkit.Mvvm · xUnit · System.Drawing.Common (IconGen only) · Inno Setup.

## Global Constraints

- **Target framework:** `NetSearch.Core` and tests = `net9.0`; `NetSearch.App` = `net9.0-windows`; `tools/IconGen` = `net9.0-windows`. No other framework. (Machine has only the .NET 9 SDK.)
- **Windows-only code** in `Core` must be annotated `[SupportedOSPlatform("windows")]` and guarded with `OperatingSystem.IsWindows()` at runtime where reachable cross-platform; `Core` must stay build-clean (0 warnings).
- **App stays `asInvoker`** — do NOT change `app.manifest` to require admin. Elevation is obtained by relaunch only.
- **Cleaner safety (non-negotiable):** allow-list paths only; never Documents/Desktop/Downloads/Pictures/source; always Scan→preview→explicit click→confirm; Recycle Bin via `FOF_ALLOWUNDO` where the provider says so; locked/denied files are skipped, never fatal.
- **Tests never touch real system locations** — every cleaner/scanner test points at a `Path.GetTempPath()` sandbox it creates and deletes.
- **UI is verified manually** (the project has no automated GUI tests); pure/sandboxed logic is TDD.
- **Version:** bump app to `0.2.0`. Installer artifact: `NetSearch-Setup-0.2.0.exe`.
- **Commits:** conventional-commit style matching the repo (`feat(disks): …`, `test(cleaner): …`); end every commit message with the `Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>` trailer.
- **Russian UI copy** — all user-facing strings are Russian, matching the existing UI.

---

## Phase 1 — App icon

### Task 1: IconGen tool + committed `NetSearch.ico` + wiring

**Files:**
- Create: `tools/IconGen/IconGen.csproj`
- Create: `tools/IconGen/Program.cs`
- Create (generated, committed): `NetSearch.ico` (repo root)
- Modify: `src/NetSearch.App/NetSearch.App.csproj` (add `<ApplicationIcon>`)
- Modify: `src/NetSearch.App/MainWindow.xaml` (add window `Icon`)
- Modify: `NetSearch.sln` (add the IconGen project — optional; it can stay solution-less)

**Interfaces:**
- Produces: a committed `NetSearch.ico` containing PNG-encoded entries at 16/32/48/64/256 px, consumed by the App csproj and the installer (Task 16).

- [ ] **Step 1: Create the IconGen project**

`tools/IconGen/IconGen.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net9.0-windows</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <UseWindowsForms>false</UseWindowsForms>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="System.Drawing.Common" Version="9.0.0" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Write the icon generator**

`tools/IconGen/Program.cs` — draws the mark at each size and writes a valid ICO of PNG entries, then self-verifies the header:
```csharp
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

// Output path: first arg, else repo-root NetSearch.ico (two levels up from tools/IconGen).
var outPath = args.Length > 0
    ? args[0]
    : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "NetSearch.ico"));

int[] sizes = { 16, 32, 48, 64, 256 };
var pngs = sizes.Select(s => (size: s, data: RenderPng(s))).ToList();
WriteIco(outPath, pngs);

// Self-verify: re-read ICONDIR and assert the image count.
using (var fs = File.OpenRead(outPath))
{
    var hdr = new byte[6];
    fs.ReadExactly(hdr, 0, 6);
    int count = BitConverter.ToUInt16(hdr, 4);
    if (count != sizes.Length) { Console.Error.WriteLine($"ICO verify FAILED: {count} != {sizes.Length}"); return 1; }
}
Console.WriteLine($"Wrote {outPath} ({pngs.Count} sizes)");
return 0;

static byte[] RenderPng(int s)
{
    using var bmp = new Bitmap(s, s, PixelFormat.Format32bppArgb);
    using (var g = Graphics.FromImage(bmp))
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Color.Transparent);
        float u = s / 16f; // scale unit

        // Rounded accent tile background (accent #2D6CDF).
        using var bg = new SolidBrush(Color.FromArgb(0x2D, 0x6C, 0xDF));
        FillRoundedRect(g, bg, new RectangleF(0.5f * u, 0.5f * u, 15f * u, 15f * u), 3.2f * u);

        // Stacked-disk motif (three ellipses) in light tint, lower-left.
        using var disk = new SolidBrush(Color.FromArgb(235, 255, 255, 255));
        using var diskEdge = new Pen(Color.FromArgb(120, 0, 0, 0), Math.Max(1f, 0.4f * u));
        for (int i = 0; i < 3; i++)
        {
            var y = (9.2f - i * 1.7f) * u;
            var e = new RectangleF(3.0f * u, y, 6.4f * u, 1.7f * u);
            g.FillEllipse(disk, e);
            g.DrawEllipse(diskEdge, e);
        }

        // Magnifier (ring + handle), upper-right.
        using var ringPen = new Pen(Color.White, Math.Max(1.4f, 1.6f * u)) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        var ring = new RectangleF(7.6f * u, 2.2f * u, 5.0f * u, 5.0f * u);
        g.DrawEllipse(ringPen, ring);
        g.DrawLine(ringPen, 12.2f * u, 6.8f * u, 13.9f * u, 8.5f * u);
    }
    using var ms = new MemoryStream();
    bmp.Save(ms, ImageFormat.Png);
    return ms.ToArray();
}

static void FillRoundedRect(Graphics g, Brush b, RectangleF r, float radius)
{
    using var p = new GraphicsPath();
    float d = radius * 2;
    p.AddArc(r.X, r.Y, d, d, 180, 90);
    p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
    p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
    p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
    p.CloseFigure();
    g.FillPath(b, p);
}

static void WriteIco(string path, List<(int size, byte[] data)> imgs)
{
    using var fs = File.Create(path);
    using var w = new BinaryWriter(fs);
    w.Write((ushort)0);              // reserved
    w.Write((ushort)1);              // type = icon
    w.Write((ushort)imgs.Count);     // image count
    int offset = 6 + imgs.Count * 16;
    foreach (var (size, data) in imgs)
    {
        w.Write((byte)(size >= 256 ? 0 : size)); // width  (0 => 256)
        w.Write((byte)(size >= 256 ? 0 : size)); // height (0 => 256)
        w.Write((byte)0);            // palette
        w.Write((byte)0);            // reserved
        w.Write((ushort)1);          // color planes
        w.Write((ushort)32);         // bpp
        w.Write((uint)data.Length);  // bytes in resource
        w.Write((uint)offset);       // image offset
        offset += data.Length;
    }
    foreach (var (_, data) in imgs) w.Write(data);
}
```

- [ ] **Step 3: Generate the icon**

Run:
```
dotnet run --project tools/IconGen
```
Expected output: `Wrote …\NetSearch.ico (5 sizes)` and exit code 0. Confirm `NetSearch.ico` exists at repo root and is non-empty.

- [ ] **Step 4: Wire the icon into the app**

In `src/NetSearch.App/NetSearch.App.csproj`, inside the first `<PropertyGroup>` (after `<ApplicationManifest>`), add:
```xml
    <ApplicationIcon>..\..\NetSearch.ico</ApplicationIcon>
```
In `src/NetSearch.App/MainWindow.xaml`, add to the `<Window …>` opening tag (after `FontFamily="Segoe UI"`):
```xml
        Icon="..\..\..\NetSearch.ico"
```
> The XAML `Icon` path is relative to the project dir at build; if WPF cannot resolve it, fall back to a pack URI by adding the ico as `<Resource Include="..\..\NetSearch.ico" Link="NetSearch.ico"/>` and `Icon="NetSearch.ico"`. Verify in Step 5.

- [ ] **Step 5: Build & verify**

Run:
```
dotnet build src/NetSearch.App/NetSearch.App.csproj -c Debug
```
Expected: 0 errors. Manually: `dotnet run --project src/NetSearch.App` and confirm the window/taskbar shows the new icon (not the default). The exe icon is verified later via the installed build (Task 16).

- [ ] **Step 6: Commit**
```
git add tools/IconGen NetSearch.ico src/NetSearch.App/NetSearch.App.csproj src/NetSearch.App/MainWindow.xaml
git commit -m "feat(icon): add NetSearch app icon and IconGen tool"
```

---

## Phase 2 — Local Disks tab

### Task 2: `SizeFormatter` (pure)

**Files:**
- Create: `src/NetSearch.Core/DiskUsage/SizeFormatter.cs`
- Test: `tests/NetSearch.Core.Tests/SizeFormatterTests.cs`

**Interfaces:**
- Produces: `static string SizeFormatter.Format(long bytes)` → e.g. `"0 B"`, `"512 B"`, `"1.4 KB"`, `"3.0 GB"`. Used by `FolderNode`, the disks UI, and the cleaner UI.

- [ ] **Step 1: Write the failing test**

`tests/NetSearch.Core.Tests/SizeFormatterTests.cs`:
```csharp
using Xunit;
using NetSearch.Core.DiskUsage;

namespace NetSearch.Core.Tests;

public class SizeFormatterTests
{
    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(512, "512 B")]
    [InlineData(1024, "1.0 KB")]
    [InlineData(1536, "1.5 KB")]
    [InlineData(1048576, "1.0 MB")]
    [InlineData(3221225472, "3.0 GB")]
    public void Format_renders_human_readable(long bytes, string expected)
        => Assert.Equal(expected, SizeFormatter.Format(bytes));

    [Fact]
    public void Format_clamps_negative_to_zero() => Assert.Equal("0 B", SizeFormatter.Format(-5));
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/NetSearch.Core.Tests --filter SizeFormatterTests`
Expected: FAIL — `SizeFormatter` does not exist (compile error).

- [ ] **Step 3: Write minimal implementation**

`src/NetSearch.Core/DiskUsage/SizeFormatter.cs`:
```csharp
using System.Globalization;

namespace NetSearch.Core.DiskUsage;

public static class SizeFormatter
{
    private static readonly string[] Units = { "B", "KB", "MB", "GB", "TB", "PB" };

    public static string Format(long bytes)
    {
        if (bytes <= 0) return "0 B";
        double value = bytes;
        int unit = 0;
        while (value >= 1024 && unit < Units.Length - 1) { value /= 1024; unit++; }
        // Bytes are whole; KB and above show one decimal.
        return unit == 0
            ? $"{(long)value} {Units[unit]}"
            : value.ToString("0.0", CultureInfo.InvariantCulture) + " " + Units[unit];
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/NetSearch.Core.Tests --filter SizeFormatterTests`
Expected: PASS (7 cases).

- [ ] **Step 5: Commit**
```
git add src/NetSearch.Core/DiskUsage/SizeFormatter.cs tests/NetSearch.Core.Tests/SizeFormatterTests.cs
git commit -m "feat(disks): human-readable byte size formatter"
```

### Task 3: `FolderNode` + `FolderTreeBuilder` (pure)

**Files:**
- Create: `src/NetSearch.Core/DiskUsage/FolderNode.cs`
- Create: `src/NetSearch.Core/DiskUsage/FolderTreeBuilder.cs`
- Test: `tests/NetSearch.Core.Tests/FolderTreeBuilderTests.cs`

**Interfaces:**
- Consumes: `FileEntry` (`Name`, `ParentPath`, `IsDir`, `Size`, `FullPath`) from `NetSearch.Core.Models`; `SizeFormatter.Format` (Task 2).
- Produces:
  - `FolderNode` — `string Name`, `string FullPath`, `FolderNode? Parent`, `long SizeBytes`, `long FileCount`, `IReadOnlyList<FolderNode> Children`, `string SizeText`, `double FractionOfParent`.
  - `FolderTreeBuilder(string rootPath)` with `void AddBatch(IReadOnlyList<FileEntry>)` and `FolderNode Build()` (children sorted by size desc, aggregates summed).

- [ ] **Step 1: Write the failing test**

`tests/NetSearch.Core.Tests/FolderTreeBuilderTests.cs`:
```csharp
using Xunit;
using NetSearch.Core.DiskUsage;
using NetSearch.Core.Models;

namespace NetSearch.Core.Tests;

public class FolderTreeBuilderTests
{
    private static FileEntry File(string parent, string name, long size)
        => FileEntry.FromComponents(0, name, parent, isDir: false, size, 0);
    private static FileEntry Dir(string parent, string name)
        => FileEntry.FromComponents(0, name, parent, isDir: true, 0, 0);

    [Fact]
    public void Aggregates_sizes_up_the_tree_and_sorts_children_desc()
    {
        var b = new FolderTreeBuilder(@"C:");
        b.AddBatch(new[]
        {
            Dir(@"C:", "Windows"),
            Dir(@"C:", "Users"),
            File(@"C:\Windows", "big.dll", 300),
            File(@"C:\Windows\System32", "small.dll", 50),
            Dir(@"C:\Windows", "System32"),
            File(@"C:\Users", "u.txt", 100),
        });
        var root = b.Build();

        Assert.Equal(450, root.SizeBytes);          // 300 + 50 + 100
        Assert.Equal(3, root.FileCount);
        // Children sorted by size desc: Windows (350) before Users (100).
        Assert.Equal("Windows", root.Children[0].Name);
        Assert.Equal(350, root.Children[0].SizeBytes);
        Assert.Equal("Users", root.Children[1].Name);
        // Nested folder aggregates.
        var sys = root.Children[0].Children.Single(c => c.Name == "System32");
        Assert.Equal(50, sys.SizeBytes);
        Assert.Equal(1, sys.FileCount);
    }

    [Fact]
    public void Fraction_of_parent_is_relative_size()
    {
        var b = new FolderTreeBuilder(@"C:");
        b.AddBatch(new[] { File(@"C:\A", "x", 75), File(@"C:\B", "y", 25), Dir(@"C:", "A"), Dir(@"C:", "B") });
        var root = b.Build();
        Assert.Equal(0.75, root.Children[0].FractionOfParent, 3);
    }

    [Fact]
    public void Entries_outside_root_are_ignored()
    {
        var b = new FolderTreeBuilder(@"C:\Target");
        b.AddBatch(new[] { File(@"D:\Other", "z", 999), File(@"C:\Target", "ok", 10) });
        var root = b.Build();
        Assert.Equal(10, root.SizeBytes);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/NetSearch.Core.Tests --filter FolderTreeBuilderTests`
Expected: FAIL — types do not exist.

- [ ] **Step 3a: Implement `FolderNode`**

`src/NetSearch.Core/DiskUsage/FolderNode.cs`:
```csharp
namespace NetSearch.Core.DiskUsage;

public sealed class FolderNode
{
    private readonly List<FolderNode> _children = new();

    public FolderNode(string name, string fullPath)
    {
        Name = name;
        FullPath = fullPath;
    }

    public string Name { get; }
    public string FullPath { get; }
    public FolderNode? Parent { get; internal set; }

    /// <summary>Aggregate size of this folder's whole subtree (logical bytes). Set by Build().</summary>
    public long SizeBytes { get; internal set; }
    /// <summary>Aggregate file count of this folder's whole subtree. Set by Build().</summary>
    public long FileCount { get; internal set; }

    public IReadOnlyList<FolderNode> Children => _children;
    internal List<FolderNode> MutableChildren => _children;

    // Bytes contributed by files directly in this folder (not subfolders); used during Build().
    internal long OwnFileBytes;
    internal long OwnFileCount;

    public string SizeText => SizeFormatter.Format(SizeBytes);

    public double FractionOfParent =>
        Parent is { SizeBytes: > 0 } p ? (double)SizeBytes / p.SizeBytes : 1.0;
}
```

- [ ] **Step 3b: Implement `FolderTreeBuilder`**

`src/NetSearch.Core/DiskUsage/FolderTreeBuilder.cs`:
```csharp
using NetSearch.Core.Models;

namespace NetSearch.Core.DiskUsage;

/// <summary>
/// Folds a stream of <see cref="FileEntry"/> into an aggregated folder-size tree (WizTree-style).
/// Files add their bytes to the folder named by their <see cref="FileEntry.ParentPath"/>; Build()
/// sums each subtree and sorts children largest-first.
/// </summary>
public sealed class FolderTreeBuilder
{
    private readonly string _rootPath;
    private readonly FolderNode _root;
    private readonly Dictionary<string, FolderNode> _byPath =
        new(StringComparer.OrdinalIgnoreCase);

    public FolderTreeBuilder(string rootPath)
    {
        _rootPath = Trim(rootPath);
        _root = new FolderNode(LeafName(_rootPath), _rootPath);
        _byPath[_rootPath] = _root;
    }

    public void AddBatch(IReadOnlyList<FileEntry> batch)
    {
        foreach (var e in batch) Add(e);
    }

    public void Add(FileEntry e)
    {
        if (e.IsDir)
        {
            GetOrCreate(Trim(e.FullPath));
        }
        else
        {
            var folder = GetOrCreate(Trim(e.ParentPath));
            if (folder is null) return;
            folder.OwnFileBytes += e.Size;
            folder.OwnFileCount += 1;
        }
    }

    public FolderNode Build()
    {
        Sum(_root);
        return _root;
    }

    private (long size, long count) Sum(FolderNode node)
    {
        long size = node.OwnFileBytes, count = node.OwnFileCount;
        foreach (var child in node.MutableChildren)
        {
            var (cs, cc) = Sum(child);
            size += cs;
            count += cc;
        }
        node.SizeBytes = size;
        node.FileCount = count;
        node.MutableChildren.Sort((a, b) => b.SizeBytes.CompareTo(a.SizeBytes));
        return (size, count);
    }

    // Returns the node for fullPath, creating it and any missing ancestors up to the root.
    // Returns null if fullPath is not under the configured root.
    private FolderNode? GetOrCreate(string fullPath)
    {
        if (_byPath.TryGetValue(fullPath, out var existing)) return existing;
        if (!IsUnderRoot(fullPath)) return null;

        var parentPath = ParentOf(fullPath);
        var parent = parentPath is null ? _root : GetOrCreate(parentPath);
        if (parent is null) return null;

        var node = new FolderNode(LeafName(fullPath), fullPath) { Parent = parent };
        parent.MutableChildren.Add(node);
        _byPath[fullPath] = node;
        return node;
    }

    private bool IsUnderRoot(string path) =>
        path.Equals(_rootPath, StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith(_rootPath + "\\", StringComparison.OrdinalIgnoreCase);

    private string? ParentOf(string path)
    {
        if (path.Equals(_rootPath, StringComparison.OrdinalIgnoreCase)) return null;
        var idx = path.LastIndexOf('\\');
        if (idx < 0) return null;
        var parent = path[..idx];
        return parent.Length == 0 ? _rootPath : parent;
    }

    private static string Trim(string p) => p.TrimEnd('\\', '/');

    private static string LeafName(string path)
    {
        var idx = path.LastIndexOf('\\');
        return idx >= 0 && idx < path.Length - 1 ? path[(idx + 1)..] : path;
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/NetSearch.Core.Tests --filter FolderTreeBuilderTests`
Expected: PASS (3 tests).

- [ ] **Step 5: Commit**
```
git add src/NetSearch.Core/DiskUsage/FolderNode.cs src/NetSearch.Core/DiskUsage/FolderTreeBuilder.cs tests/NetSearch.Core.Tests/FolderTreeBuilderTests.cs
git commit -m "feat(disks): aggregated folder-size tree builder"
```

### Task 4: `Treemap` squarified layout (pure)

**Files:**
- Create: `src/NetSearch.Core/DiskUsage/Treemap.cs`
- Test: `tests/NetSearch.Core.Tests/TreemapTests.cs`

**Interfaces:**
- Produces:
  - `readonly record struct TreemapRect(double X, double Y, double Width, double Height)`.
  - `static IReadOnlyList<TreemapRect> Treemap.Squarify(double boundsWidth, double boundsHeight, IReadOnlyList<double> weights)` — returns one rect per input weight, in input order; non-positive weights map to a zero rect; rects tile the bounds.

- [ ] **Step 1: Write the failing test**

`tests/NetSearch.Core.Tests/TreemapTests.cs`:
```csharp
using Xunit;
using NetSearch.Core.DiskUsage;

namespace NetSearch.Core.Tests;

public class TreemapTests
{
    [Fact]
    public void Returns_one_rect_per_weight_in_input_order()
    {
        var rects = Treemap.Squarify(100, 100, new double[] { 50, 30, 20 });
        Assert.Equal(3, rects.Count);
    }

    [Fact]
    public void Rects_cover_the_full_area_without_overflow()
    {
        var rects = Treemap.Squarify(200, 120, new double[] { 6, 6, 4, 3, 2, 1 });
        double area = rects.Sum(r => r.Width * r.Height);
        Assert.Equal(200d * 120d, area, 0); // total tiled area equals bounds
        foreach (var r in rects)
        {
            Assert.True(r.X >= -1e-6 && r.Y >= -1e-6);
            Assert.True(r.X + r.Width <= 200 + 1e-6);
            Assert.True(r.Y + r.Height <= 120 + 1e-6);
        }
    }

    [Fact]
    public void Nonpositive_weights_become_zero_rects()
    {
        var rects = Treemap.Squarify(100, 100, new double[] { 0, 100 });
        Assert.Equal(0, rects[0].Width * rects[0].Height);
        Assert.Equal(100d * 100d, rects[1].Width * rects[1].Height, 0);
    }

    [Fact]
    public void Empty_or_degenerate_bounds_return_zero_rects()
    {
        Assert.All(Treemap.Squarify(0, 100, new double[] { 1, 2 }),
            r => Assert.Equal(0, r.Width * r.Height));
        Assert.Empty(Treemap.Squarify(100, 100, System.Array.Empty<double>()));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/NetSearch.Core.Tests --filter TreemapTests`
Expected: FAIL — `Treemap` does not exist.

- [ ] **Step 3: Implement the squarified treemap**

`src/NetSearch.Core/DiskUsage/Treemap.cs`:
```csharp
namespace NetSearch.Core.DiskUsage;

public readonly record struct TreemapRect(double X, double Y, double Width, double Height);

/// <summary>
/// Squarified treemap (Bruls, Huizing, van Wijk 2000): packs weighted items into a rectangle so
/// each tile stays close to square. Pure geometry — no UI types — so it is unit-tested directly.
/// </summary>
public static class Treemap
{
    public static IReadOnlyList<TreemapRect> Squarify(
        double boundsWidth, double boundsHeight, IReadOnlyList<double> weights)
    {
        var output = new TreemapRect[weights.Count];
        if (weights.Count == 0) return output;
        if (boundsWidth <= 0 || boundsHeight <= 0) return output; // all zero rects

        // Keep only positive weights, remembering original positions.
        var idx = new List<int>();
        double totalWeight = 0;
        for (int i = 0; i < weights.Count; i++)
            if (weights[i] > 0) { idx.Add(i); totalWeight += weights[i]; }
        if (totalWeight <= 0) return output;

        // Scale weights to areas filling the bounds, sorted largest-first for good aspect ratios.
        idx.Sort((a, b) => weights[b].CompareTo(weights[a]));
        double scale = (boundsWidth * boundsHeight) / totalWeight;
        var areas = idx.Select(i => weights[i] * scale).ToList();

        var rects = new List<TreemapRect>(areas.Count);
        Layout(areas, new TreemapRect(0, 0, boundsWidth, boundsHeight), rects);

        for (int k = 0; k < idx.Count; k++) output[idx[k]] = rects[k];
        return output;
    }

    private static void Layout(List<double> areas, TreemapRect bounds, List<TreemapRect> rects)
    {
        var free = bounds;
        var row = new List<double>();
        double rowSum = 0, rowMax = 0, rowMin = double.MaxValue;

        int i = 0;
        while (i < areas.Count)
        {
            double a = areas[i];
            double side = Math.Min(free.Width, free.Height);
            double newSum = rowSum + a, newMax = Math.Max(rowMax, a), newMin = Math.Min(rowMin, a);

            if (row.Count == 0 ||
                Worst(rowSum, rowMax, rowMin, side) >= Worst(newSum, newMax, newMin, side))
            {
                row.Add(a); rowSum = newSum; rowMax = newMax; rowMin = newMin; i++;
            }
            else
            {
                free = PlaceRow(row, rowSum, free, rects);
                row.Clear(); rowSum = 0; rowMax = 0; rowMin = double.MaxValue;
            }
        }
        if (row.Count > 0) PlaceRow(row, rowSum, free, rects);
    }

    private static double Worst(double sum, double max, double min, double side)
    {
        if (sum <= 0 || side <= 0) return double.MaxValue;
        double s2 = sum * sum, w2 = side * side;
        return Math.Max(w2 * max / s2, s2 / (w2 * min));
    }

    // Lays one row along the shorter side of `free`; returns the remaining free rectangle.
    private static TreemapRect PlaceRow(List<double> row, double rowSum, TreemapRect free, List<TreemapRect> rects)
    {
        double side = Math.Min(free.Width, free.Height);
        double thickness = side <= 0 ? 0 : rowSum / side;

        if (free.Width <= free.Height)
        {
            double x = free.X;
            foreach (var a in row)
            {
                double w = thickness <= 0 ? 0 : a / thickness;
                rects.Add(new TreemapRect(x, free.Y, w, thickness));
                x += w;
            }
            return new TreemapRect(free.X, free.Y + thickness, free.Width, free.Height - thickness);
        }
        else
        {
            double y = free.Y;
            foreach (var a in row)
            {
                double h = thickness <= 0 ? 0 : a / thickness;
                rects.Add(new TreemapRect(free.X, y, thickness, h));
                y += h;
            }
            return new TreemapRect(free.X + thickness, free.Y, free.Width - thickness, free.Height);
        }
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/NetSearch.Core.Tests --filter TreemapTests`
Expected: PASS (4 tests).

- [ ] **Step 5: Commit**
```
git add src/NetSearch.Core/DiskUsage/Treemap.cs tests/NetSearch.Core.Tests/TreemapTests.cs
git commit -m "feat(disks): squarified treemap layout"
```

### Task 5: `DriveProbe` + `DiskUsageScanner`

**Files:**
- Create: `src/NetSearch.Core/DiskUsage/DriveProbe.cs`
- Create: `src/NetSearch.Core/DiskUsage/DiskUsageScanner.cs`
- Test: `tests/NetSearch.Core.Tests/DiskUsageScannerTests.cs`

**Interfaces:**
- Consumes: `Crawler` (`NetSearch.Core.Indexing`), `MftEnumerator`, `IndexStrategySelector`, `IndexBackend`, `WindowsEnvironmentProbe` (`NetSearch.Core.Native`), `FolderTreeBuilder` (Task 3), `CrawlProgress`.
- Produces:
  - `readonly record struct DriveUsage(char Letter, string Label, string FileSystem, long TotalBytes, long FreeBytes)` with `long UsedBytes => TotalBytes - FreeBytes` and `string RootPath => Letter + ":\\"`.
  - `static IReadOnlyList<DriveUsage> DriveProbe.FixedDrives()`.
  - `DiskUsageScanner` with `FolderNode ScanDirectory(string path, CancellationToken, IProgress<CrawlProgress>?)` (always crawler — unit-tested) and `FolderNode Scan(string driveRoot, CancellationToken, IProgress<CrawlProgress>?)` (MFT when elevated NTFS, else `ScanDirectory`).

- [ ] **Step 1: Write the failing test** (ScanDirectory against a sandbox tree)

`tests/NetSearch.Core.Tests/DiskUsageScannerTests.cs`:
```csharp
using Xunit;
using NetSearch.Core.DiskUsage;

namespace NetSearch.Core.Tests;

public class DiskUsageScannerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"du_{Guid.NewGuid():N}");

    public DiskUsageScannerTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "big"));
        Directory.CreateDirectory(Path.Combine(_root, "small"));
        File.WriteAllBytes(Path.Combine(_root, "big", "a.bin"), new byte[1000]);
        File.WriteAllBytes(Path.Combine(_root, "big", "b.bin"), new byte[500]);
        File.WriteAllBytes(Path.Combine(_root, "small", "c.bin"), new byte[100]);
    }

    [Fact]
    public void ScanDirectory_builds_aggregated_sorted_tree()
    {
        var node = new DiskUsageScanner().ScanDirectory(_root, CancellationToken.None, null);
        Assert.Equal(1600, node.SizeBytes);
        Assert.Equal("big", node.Children[0].Name);    // 1500 sorts before 100
        Assert.Equal(1500, node.Children[0].SizeBytes);
        Assert.Equal("small", node.Children[1].Name);
    }

    [Fact]
    public void DriveProbe_lists_at_least_the_system_drive()
    {
        var drives = DriveProbe.FixedDrives();
        Assert.NotEmpty(drives);
        Assert.All(drives, d => Assert.True(d.TotalBytes >= 0 && d.UsedBytes >= 0));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/NetSearch.Core.Tests --filter DiskUsageScannerTests`
Expected: FAIL — types do not exist.

- [ ] **Step 3a: Implement `DriveProbe`**

`src/NetSearch.Core/DiskUsage/DriveProbe.cs`:
```csharp
using System.IO;

namespace NetSearch.Core.DiskUsage;

public readonly record struct DriveUsage(char Letter, string Label, string FileSystem, long TotalBytes, long FreeBytes)
{
    public long UsedBytes => Math.Max(0, TotalBytes - FreeBytes);
    public string RootPath => Letter + ":\\";
    public double UsedFraction => TotalBytes > 0 ? (double)UsedBytes / TotalBytes : 0;
}

public static class DriveProbe
{
    public static IReadOnlyList<DriveUsage> FixedDrives()
    {
        var list = new List<DriveUsage>();
        foreach (var d in DriveInfo.GetDrives())
        {
            try
            {
                if (d.DriveType != DriveType.Fixed || !d.IsReady) continue;
                list.Add(new DriveUsage(d.Name[0], string.IsNullOrEmpty(d.VolumeLabel) ? "Локальный диск" : d.VolumeLabel,
                    d.DriveFormat, d.TotalSize, d.AvailableFreeSpace));
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        return list;
    }
}
```

- [ ] **Step 3b: Implement `DiskUsageScanner`**

`src/NetSearch.Core/DiskUsage/DiskUsageScanner.cs`:
```csharp
using NetSearch.Core.Indexing;
using NetSearch.Core.Native;

namespace NetSearch.Core.DiskUsage;

/// <summary>
/// Scans a drive into a <see cref="FolderNode"/> tree. Uses the fast MFT enumerator when the
/// process is elevated and the volume is fixed NTFS; otherwise (and on any MFT failure) it falls
/// back to the recursive <see cref="Crawler"/> — the same transparent fallback the indexer uses.
/// </summary>
public sealed class DiskUsageScanner
{
    public FolderNode Scan(string driveRoot, CancellationToken ct, IProgress<CrawlProgress>? progress = null)
    {
        if (OperatingSystem.IsWindows())
        {
            var probe = new WindowsEnvironmentProbe();
            if (IndexStrategySelector.Select(driveRoot, probe) == IndexBackend.Mft)
            {
                try { return ScanViaMft(driveRoot, ct, progress); }
                catch (OperationCanceledException) { throw; }
                catch { /* fall through to the crawler */ }
            }
        }
        return ScanDirectory(driveRoot, ct, progress);
    }

    public FolderNode ScanDirectory(string path, CancellationToken ct, IProgress<CrawlProgress>? progress)
    {
        var builder = new FolderTreeBuilder(path);
        new Crawler(parallelism: 4).Crawl(0, path, builder.AddBatch, ct, progress);
        return builder.Build();
    }

    private static FolderNode ScanViaMft(string driveRoot, CancellationToken ct, IProgress<CrawlProgress>? progress)
    {
        var builder = new FolderTreeBuilder(driveRoot);
        new MftEnumerator().Enumerate(0, driveRoot, builder.AddBatch, ct, progress);
        return builder.Build();
    }
}
```
> `Crawler.Crawl` and `MftEnumerator.Enumerate` both accept `Action<IReadOnlyList<FileEntry>>`, so `builder.AddBatch` is passed directly as the `onBatch` callback.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/NetSearch.Core.Tests --filter DiskUsageScannerTests`
Expected: PASS (2 tests).

- [ ] **Step 5: Run the full Core suite (no regressions)**

Run: `dotnet test tests/NetSearch.Core.Tests`
Expected: PASS — previous 39 + new tests all green.

- [ ] **Step 6: Commit**
```
git add src/NetSearch.Core/DiskUsage/DriveProbe.cs src/NetSearch.Core/DiskUsage/DiskUsageScanner.cs tests/NetSearch.Core.Tests/DiskUsageScannerTests.cs
git commit -m "feat(disks): drive probe and MFT/crawler disk-usage scanner"
```

### Task 6: Tab-shell refactor of `MainWindow`

**Files:**
- Modify: `src/NetSearch.App/MainWindow.xaml` (wrap existing content in a `TabControl`)

**Interfaces:**
- Consumes: nothing new. Produces: a `TabControl` whose first tab hosts the unchanged search UI; two empty placeholder tabs are added (filled in Tasks 9 and 15).

- [ ] **Step 1: Wrap the search UI in a tab**

In `src/NetSearch.App/MainWindow.xaml`, replace the **outer** `<Grid Margin="14"> … </Grid>` (the entire current body) so it becomes the content of a `TabControl`'s first `TabItem`. The existing inner markup (rows 8–175: header, search box, filters, results, progress, status) is moved **verbatim** inside `<TabItem Header="Поиск">`. New structure:
```xml
    <TabControl Margin="8" Background="{StaticResource BgBrush}" BorderThickness="0">
        <TabItem Header="Поиск">
            <!-- PASTE the existing <Grid Margin="14"> … </Grid> here UNCHANGED -->
        </TabItem>
        <TabItem Header="Локальные диски">
            <!-- Filled in Task 9 -->
            <Grid/>
        </TabItem>
        <TabItem Header="Очистка">
            <!-- Filled in Task 15 -->
            <Grid/>
        </TabItem>
    </TabControl>
```
Keep the `<Window>` root element and its attributes; only the body changes from a single `Grid` to the `TabControl` above.

- [ ] **Step 2: Build & manual smoke**

Run:
```
dotnet build src/NetSearch.App -c Debug
```
Expected: 0 errors. Then `dotnet run --project src/NetSearch.App` and confirm: three tabs appear; the Поиск tab works exactly as before (type to search, filters, refresh). The other two tabs are empty.

- [ ] **Step 3: Commit**
```
git add src/NetSearch.App/MainWindow.xaml
git commit -m "feat(ui): host search view in a 3-tab shell"
```

### Task 7: `ElevationService` + elevation banner style

**Files:**
- Create: `src/NetSearch.App/Services/ElevationService.cs`
- Modify: `src/NetSearch.App/Themes/AppTheme.xaml` (add a `WarningBanner` border style)

**Interfaces:**
- Produces:
  - `static bool ElevationService.IsElevated`.
  - `static void ElevationService.RelaunchAsAdmin()` — relaunches the current exe with the `runas` verb and shuts the app down; silently no-ops if the user declines UAC.
  - XAML style key `WarningBanner` (a `Border` style) for the "needs admin" strip.

- [ ] **Step 1: Implement `ElevationService`**

`src/NetSearch.App/Services/ElevationService.cs`:
```csharp
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using NetSearch.Core.Native;

namespace NetSearch.App.Services;

public static class ElevationService
{
    public static bool IsElevated =>
        OperatingSystem.IsWindows() && new WindowsEnvironmentProbe().IsWindowsElevated;

    /// <summary>
    /// Relaunches this executable elevated (UAC prompt) and shuts the current instance down.
    /// No-ops if not running from a real exe path or if the user declines the prompt.
    /// Works for the published self-contained exe; under `dotnet run` the host path may differ.
    /// </summary>
    public static void RelaunchAsAdmin()
    {
        var exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe)) return;
        try
        {
            Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true, Verb = "runas" });
            Application.Current.Shutdown();
        }
        catch (Win32Exception) { /* user cancelled the UAC prompt — stay running unelevated */ }
    }
}
```

- [ ] **Step 2: Add the banner style**

In `src/NetSearch.App/Themes/AppTheme.xaml`, add (near the other `Border`/`Style` resources):
```xml
    <Style x:Key="WarningBanner" TargetType="Border">
        <Setter Property="Background" Value="#3A2A00"/>
        <Setter Property="BorderBrush" Value="#7A5A00"/>
        <Setter Property="BorderThickness" Value="1"/>
        <Setter Property="CornerRadius" Value="6"/>
        <Setter Property="Padding" Value="10,8"/>
        <Setter Property="Margin" Value="0,0,0,10"/>
    </Style>
```

- [ ] **Step 3: Build & verify**

Run: `dotnet build src/NetSearch.App -c Debug`
Expected: 0 errors. (Behavior is exercised in Task 9.)

- [ ] **Step 4: Commit**
```
git add src/NetSearch.App/Services/ElevationService.cs src/NetSearch.App/Themes/AppTheme.xaml
git commit -m "feat(ui): elevation service and warning-banner style"
```

### Task 8: `DiskUsageViewModel` + child-VM wiring

**Files:**
- Create: `src/NetSearch.App/ViewModels/DiskUsageViewModel.cs`
- Create: `src/NetSearch.App/ViewModels/TreemapTile.cs`
- Modify: `src/NetSearch.App/ViewModels/MainViewModel.cs` (expose `Disk`)

**Interfaces:**
- Consumes: `DriveProbe`, `DriveUsage`, `DiskUsageScanner`, `FolderNode`, `Treemap`, `TreemapRect` (Core); `ElevationService` (Task 7); `CrawlProgress`.
- Produces:
  - `TreemapTile` — `double X, Y, Width, Height`; `Brush Fill`; `string Label, Tooltip`; `FolderNode Node`.
  - `DiskUsageViewModel` with: `ObservableCollection<DriveUsage> Drives`; `DriveUsage? SelectedDrive`; `bool IsElevated`; `bool IsScanning`; `string Status`; `ObservableCollection<FolderNode> TreeRoots`; `FolderNode? CurrentNode`; `ObservableCollection<TreemapTile> Tiles`; commands `ScanCommand`, `RelaunchAdminCommand`, `DrillCommand` (param `FolderNode`), `OpenFolderCommand`; method `void LayoutTiles(double width, double height)`.
  - `MainViewModel.Disk` (a `DiskUsageViewModel`).

- [ ] **Step 1: Implement `TreemapTile`**

`src/NetSearch.App/ViewModels/TreemapTile.cs`:
```csharp
using System.Windows.Media;
using NetSearch.Core.DiskUsage;

namespace NetSearch.App.ViewModels;

public sealed class TreemapTile
{
    public required double X { get; init; }
    public required double Y { get; init; }
    public required double Width { get; init; }
    public required double Height { get; init; }
    public required Brush Fill { get; init; }
    public required string Label { get; init; }
    public required string Tooltip { get; init; }
    public required FolderNode Node { get; init; }
    public bool ShowLabel => Width > 46 && Height > 22;
}
```

- [ ] **Step 2: Implement `DiskUsageViewModel`**

`src/NetSearch.App/ViewModels/DiskUsageViewModel.cs`:
```csharp
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NetSearch.App.Services;
using NetSearch.Core.DiskUsage;
using NetSearch.Core.Indexing;

namespace NetSearch.App.ViewModels;

public partial class DiskUsageViewModel : ObservableObject
{
    private double _lastWidth = 600, _lastHeight = 360;

    public ObservableCollection<DriveUsage> Drives { get; } = new();
    public ObservableCollection<FolderNode> TreeRoots { get; } = new();
    public ObservableCollection<TreemapTile> Tiles { get; } = new();

    [ObservableProperty] private DriveUsage? _selectedDrive;
    [ObservableProperty] private bool _isElevated = ElevationService.IsElevated;
    [ObservableProperty] private bool _isScanning;
    [ObservableProperty] private string _status = "Выберите диск и нажмите «Сканировать».";
    [ObservableProperty] private FolderNode? _currentNode;

    private CancellationTokenSource? _cts;

    public DiskUsageViewModel()
    {
        foreach (var d in DriveProbe.FixedDrives()) Drives.Add(d);
        SelectedDrive = Drives.Count > 0 ? Drives[0] : null;
    }

    [RelayCommand]
    private async Task ScanAsync()
    {
        if (IsScanning || SelectedDrive is not { } drive) return;
        IsScanning = true;
        _cts = new CancellationTokenSource();
        Status = $"Сканирование {drive.RootPath}…";
        var progress = new Progress<CrawlProgress>(p => Status = $"Сканирование… {p.Count} объектов");
        try
        {
            var token = _cts.Token;
            var root = await Task.Run(() => new DiskUsageScanner().Scan(drive.RootPath, token, progress), token);
            TreeRoots.Clear();
            TreeRoots.Add(root);
            CurrentNode = root;
            LayoutTiles(_lastWidth, _lastHeight);
            Status = $"{drive.RootPath} — {root.SizeText} в {root.FileCount} файлах";
        }
        catch (OperationCanceledException) { Status = "Сканирование отменено."; }
        catch (Exception ex) { Status = "Ошибка сканирования: " + ex.Message; }
        finally { IsScanning = false; _cts?.Dispose(); _cts = null; }
    }

    [RelayCommand] private void CancelScan() => _cts?.Cancel();

    [RelayCommand] private void RelaunchAdmin() => ElevationService.RelaunchAsAdmin();

    [RelayCommand]
    private void Drill(FolderNode? node)
    {
        if (node is null || node.Children.Count == 0) return;
        CurrentNode = node;
        LayoutTiles(_lastWidth, _lastHeight);
    }

    [RelayCommand]
    private void DrillUp()
    {
        if (CurrentNode?.Parent is { } p) { CurrentNode = p; LayoutTiles(_lastWidth, _lastHeight); }
    }

    [RelayCommand]
    private void OpenFolder(FolderNode? node)
    {
        var target = node ?? CurrentNode;
        if (target is null) return;
        try { Process.Start(new ProcessStartInfo("explorer.exe", $"\"{target.FullPath}\"")); }
        catch (Exception ex) { Status = "Не удалось открыть: " + ex.Message; }
    }

    public void LayoutTiles(double width, double height)
    {
        _lastWidth = width > 0 ? width : _lastWidth;
        _lastHeight = height > 0 ? height : _lastHeight;
        Tiles.Clear();
        if (CurrentNode is null) return;

        var children = CurrentNode.Children.Where(c => c.SizeBytes > 0).ToList();
        var weights = children.Select(c => (double)c.SizeBytes).ToList();
        var rects = Treemap.Squarify(_lastWidth, _lastHeight, weights);
        for (int i = 0; i < children.Count; i++)
        {
            var r = rects[i];
            if (r.Width <= 0 || r.Height <= 0) continue;
            var c = children[i];
            Tiles.Add(new TreemapTile
            {
                X = r.X, Y = r.Y, Width = r.Width, Height = r.Height,
                Fill = new SolidColorBrush(ColorFor(i)),
                Label = $"{c.Name}  {c.SizeText}",
                Tooltip = $"{c.FullPath}\n{c.SizeText} • {c.FileCount} файлов",
                Node = c,
            });
        }
    }

    // Stable, distinct fill per child index, spread around the accent blue.
    private static Color ColorFor(int i)
    {
        double hue = (210 + i * 47) % 360;
        return FromHsl(hue, 0.55, 0.55);
    }

    private static Color FromHsl(double h, double s, double l)
    {
        double c = (1 - Math.Abs(2 * l - 1)) * s;
        double x = c * (1 - Math.Abs((h / 60) % 2 - 1));
        double m = l - c / 2;
        (double r, double g, double b) = h switch
        {
            < 60  => (c, x, 0.0),
            < 120 => (x, c, 0.0),
            < 180 => (0.0, c, x),
            < 240 => (0.0, x, c),
            < 300 => (x, 0.0, c),
            _     => (c, 0.0, x),
        };
        return Color.FromRgb((byte)((r + m) * 255), (byte)((g + m) * 255), (byte)((b + m) * 255));
    }
}
```

- [ ] **Step 3: Expose the child VM on `MainViewModel`**

In `src/NetSearch.App/ViewModels/MainViewModel.cs`, add a property and initialize it in the constructor:
```csharp
    public DiskUsageViewModel Disk { get; } = new();
```
Place the property near `public ObservableCollection<FileRow> Results { get; } = new();` (around line 41). No other changes to `MainViewModel`.

- [ ] **Step 4: Build & verify**

Run: `dotnet build src/NetSearch.App -c Debug`
Expected: 0 errors, 0 warnings. (Confirm the `Palette` field was removed so there is no unused-field warning.)

- [ ] **Step 5: Commit**
```
git add src/NetSearch.App/ViewModels/DiskUsageViewModel.cs src/NetSearch.App/ViewModels/TreemapTile.cs src/NetSearch.App/ViewModels/MainViewModel.cs
git commit -m "feat(disks): disk-usage view-model with treemap tiles"
```

### Task 9: Local Disks tab UI (tree-list + treemap)

**Files:**
- Modify: `src/NetSearch.App/MainWindow.xaml` (fill the "Локальные диски" tab)
- Modify: `src/NetSearch.App/MainWindow.xaml.cs` (treemap size + tile click handlers)

**Interfaces:**
- Consumes: `MainViewModel.Disk` (Task 8). Produces: the populated disks tab.

- [ ] **Step 1: Fill the disks tab**

In `src/NetSearch.App/MainWindow.xaml`, replace the `Локальные диски` tab's placeholder `<Grid/>` with the following. All bindings are rooted at `Disk` (the child VM):
```xml
        <TabItem Header="Локальные диски">
            <Grid Margin="14" DataContext="{Binding Disk}">
                <Grid.RowDefinitions>
                    <RowDefinition Height="Auto"/> <!-- elevation banner -->
                    <RowDefinition Height="Auto"/> <!-- drive bar -->
                    <RowDefinition Height="Auto"/> <!-- breadcrumb/progress -->
                    <RowDefinition Height="*"/>    <!-- content -->
                </Grid.RowDefinitions>

                <Border Grid.Row="0" Style="{StaticResource WarningBanner}"
                        Visibility="{Binding IsElevated, Converter={StaticResource BoolToVis}, ConverterParameter=invert}">
                    <DockPanel>
                        <Button DockPanel.Dock="Right" Content="Перезапустить от имени администратора"
                                Command="{Binding RelaunchAdminCommand}"/>
                        <TextBlock VerticalAlignment="Center" TextWrapping="Wrap"
                                   Text="Для быстрого сканирования всего диска нужны права администратора. Без них используется медленный обход папок."/>
                    </DockPanel>
                </Border>

                <DockPanel Grid.Row="1" Margin="0,0,0,10">
                    <Button DockPanel.Dock="Right" Content="Сканировать" Style="{StaticResource AccentButton}"
                            Command="{Binding ScanCommand}"/>
                    <ComboBox ItemsSource="{Binding Drives}" SelectedItem="{Binding SelectedDrive}" Width="280">
                        <ComboBox.ItemTemplate>
                            <DataTemplate>
                                <TextBlock>
                                    <Run Text="{Binding RootPath, Mode=OneWay}" FontWeight="SemiBold"/>
                                    <Run Text="{Binding Label, Mode=OneWay}" Foreground="{StaticResource MutedBrush}"/>
                                </TextBlock>
                            </DataTemplate>
                        </ComboBox.ItemTemplate>
                    </ComboBox>
                </DockPanel>

                <DockPanel Grid.Row="2" Margin="0,0,0,8">
                    <Button DockPanel.Dock="Left" Content="▲ Вверх" Margin="0,0,8,0"
                            Command="{Binding DrillUpCommand}"/>
                    <ProgressBar Width="120" Height="6" IsIndeterminate="True" DockPanel.Dock="Right"
                                 Visibility="{Binding IsScanning, Converter={StaticResource BoolToVis}}"
                                 Foreground="{StaticResource AccentBrush}" Background="{StaticResource BorderBrush}" BorderThickness="0"/>
                    <TextBlock Text="{Binding Status}" Style="{StaticResource MutedText}" VerticalAlignment="Center"/>
                </DockPanel>

                <Grid Grid.Row="3">
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width="2*"/>
                        <ColumnDefinition Width="6"/>
                        <ColumnDefinition Width="3*"/>
                    </Grid.ColumnDefinitions>

                    <!-- Tree-list -->
                    <Border Grid.Column="0" Style="{StaticResource CardBorder}" Padding="0">
                        <TreeView ItemsSource="{Binding TreeRoots}" BorderThickness="0"
                                  Background="{StaticResource SurfaceBrush}"
                                  VirtualizingPanel.IsVirtualizing="True" VirtualizingPanel.VirtualizationMode="Recycling">
                            <TreeView.ItemTemplate>
                                <HierarchicalDataTemplate ItemsSource="{Binding Children}">
                                    <Grid Width="320">
                                        <Grid.ColumnDefinitions>
                                            <ColumnDefinition Width="*"/>
                                            <ColumnDefinition Width="90"/>
                                        </Grid.ColumnDefinitions>
                                        <TextBlock Grid.Column="0" Text="{Binding Name}" TextTrimming="CharacterEllipsis"/>
                                        <TextBlock Grid.Column="1" Text="{Binding SizeText}" HorizontalAlignment="Right"
                                                   Foreground="{StaticResource MutedBrush}"/>
                                    </Grid>
                                </HierarchicalDataTemplate>
                            </TreeView.ItemTemplate>
                        </TreeView>
                    </Border>

                    <GridSplitter Grid.Column="1" Width="6" HorizontalAlignment="Stretch"
                                  Background="{StaticResource BorderBrush}"/>

                    <!-- Treemap -->
                    <Border Grid.Column="2" Style="{StaticResource CardBorder}" Padding="0" ClipToBounds="True">
                        <ItemsControl x:Name="TreemapHost" ItemsSource="{Binding Tiles}"
                                      SizeChanged="OnTreemapSizeChanged">
                            <ItemsControl.ItemsPanel>
                                <ItemsPanelTemplate><Canvas/></ItemsPanelTemplate>
                            </ItemsControl.ItemsPanel>
                            <ItemsControl.ItemContainerStyle>
                                <Style TargetType="ContentPresenter">
                                    <Setter Property="Canvas.Left" Value="{Binding X}"/>
                                    <Setter Property="Canvas.Top" Value="{Binding Y}"/>
                                </Style>
                            </ItemsControl.ItemContainerStyle>
                            <ItemsControl.ItemTemplate>
                                <DataTemplate>
                                    <Border Width="{Binding Width}" Height="{Binding Height}"
                                            Background="{Binding Fill}" BorderBrush="#202830" BorderThickness="1"
                                            ToolTip="{Binding Tooltip}" MouseLeftButtonUp="OnTileClick" Tag="{Binding Node}">
                                        <TextBlock Text="{Binding Label}" Margin="4,2" Foreground="#101010"
                                                   FontSize="11" TextTrimming="CharacterEllipsis"
                                                   Visibility="{Binding ShowLabel, Converter={StaticResource BoolToVis}}"/>
                                    </Border>
                                </DataTemplate>
                            </ItemsControl.ItemTemplate>
                        </ItemsControl>
                    </Border>
                </Grid>
            </Grid>
        </TabItem>
```

- [ ] **Step 2: Confirm the `BoolToVis` converter supports an `invert` parameter**

Open `src/NetSearch.App/Themes/AppTheme.xaml` and check the `BoolToVis` resource. If it is the stock `BooleanToVisibilityConverter`, it does **not** support `ConverterParameter=invert`. In that case, add an inverting converter and use it for the banner instead. Add to `AppTheme.xaml`:
```xml
    <local:InverseBoolToVisConverter x:Key="InverseBoolToVis"/>
```
(with `xmlns:local="clr-namespace:NetSearch.App"` already present in that file — if not, add it to the ResourceDictionary root), create `src/NetSearch.App/Converters/InverseBoolToVisConverter.cs`:
```csharp
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace NetSearch.App;

public sealed class InverseBoolToVisConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c)
        => value is true ? Visibility.Collapsed : Visibility.Visible;
    public object ConvertBack(object value, Type t, object p, CultureInfo c)
        => value is Visibility.Visible ? false : true;
}
```
and change the banner's `Visibility` binding to `Visibility="{Binding IsElevated, Converter={StaticResource InverseBoolToVis}}"` (drop the `ConverterParameter`).

- [ ] **Step 3: Add code-behind handlers**

In `src/NetSearch.App/MainWindow.xaml.cs`, add these handlers (inside the `MainWindow` class):
```csharp
    private void OnTreemapSizeChanged(object sender, System.Windows.SizeChangedEventArgs e)
    {
        if (DataContext is ViewModels.MainViewModel vm)
            vm.Disk.LayoutTiles(e.NewSize.Width, e.NewSize.Height);
    }

    private void OnTileClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is System.Windows.Controls.Border { Tag: NetSearch.Core.DiskUsage.FolderNode node }
            && DataContext is ViewModels.MainViewModel vm)
            vm.Disk.DrillCommand.Execute(node);
    }
```

- [ ] **Step 4: Build & manual smoke**

Run: `dotnet build src/NetSearch.App -c Debug`, then `dotnet run --project src/NetSearch.App`.
Manual checks: open **Локальные диски**; if not elevated the amber banner shows; pick a drive → **Сканировать**; the tree-list fills (folders largest-first, sizes shown) and the treemap renders colored tiles; clicking a tile drills in; **▲ Вверх** goes back; resizing the window re-layouts the treemap. (A full-drive scan may take seconds unelevated.)

- [ ] **Step 5: Commit**
```
git add src/NetSearch.App/MainWindow.xaml src/NetSearch.App/MainWindow.xaml.cs src/NetSearch.App/Converters/InverseBoolToVisConverter.cs src/NetSearch.App/Themes/AppTheme.xaml
git commit -m "feat(disks): local disks tab with tree-list and treemap"
```

---

## Phase 3 — Disk cleaner

### Task 10: Clean model, scan helpers & file-based providers

**Files:**
- Create: `src/NetSearch.Core/Cleaning/CleanItem.cs`
- Create: `src/NetSearch.Core/Cleaning/CleanCategory.cs`
- Create: `src/NetSearch.Core/Cleaning/ICleanProvider.cs`
- Create: `src/NetSearch.Core/Cleaning/CleanScanHelpers.cs`
- Create: `src/NetSearch.Core/Cleaning/Providers/TempFilesProvider.cs`
- Create: `src/NetSearch.Core/Cleaning/Providers/BrowserCacheProvider.cs`
- Create: `src/NetSearch.Core/Cleaning/Providers/SystemDevCacheProvider.cs`
- Test: `tests/NetSearch.Core.Tests/CleanProvidersTests.cs`

**Interfaces:**
- Produces:
  - `readonly record struct CleanItem(string Path, long SizeBytes, bool IsDir)` with `const string RecycleBinSentinel = "::recyclebin::"`.
  - `sealed record CleanCategory(string Id, string DisplayName, string Description, bool NeedsAdmin, bool UsesRecycleBin, bool DefaultSelected)`.
  - `interface ICleanProvider { CleanCategory Category { get; } IEnumerable<CleanItem> Scan(CancellationToken ct); }`.
  - `static class CleanScanHelpers` — `IEnumerable<CleanItem> TopLevelEntries(IEnumerable<string> roots, CancellationToken)`, `CleanItem? TryDescribe(string path)`, `long DirectorySize(string dir)`.
  - `TempFilesProvider(IReadOnlyList<string> roots)` + `static TempFilesProvider ForSystem(bool elevated)`.
  - `BrowserCacheProvider(IReadOnlyList<string> roots)` + `static BrowserCacheProvider ForSystem()`.
  - `SystemDevCacheProvider(IReadOnlyList<string> roots)` + `static SystemDevCacheProvider ForSystem(bool elevated)`.

- [ ] **Step 1: Write the failing test**

`tests/NetSearch.Core.Tests/CleanProvidersTests.cs`:
```csharp
using Xunit;
using NetSearch.Core.Cleaning;
using NetSearch.Core.Cleaning.Providers;

namespace NetSearch.Core.Tests;

public class CleanProvidersTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"clean_{Guid.NewGuid():N}");

    public CleanProvidersTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "cacheA"));
        File.WriteAllBytes(Path.Combine(_root, "loose.tmp"), new byte[200]);
        File.WriteAllBytes(Path.Combine(_root, "cacheA", "x.dat"), new byte[300]);
        File.WriteAllBytes(Path.Combine(_root, "cacheA", "y.dat"), new byte[100]);
    }

    [Fact]
    public void TopLevelEntries_lists_files_and_dirs_with_aggregate_sizes()
    {
        var items = CleanScanHelpers.TopLevelEntries(new[] { _root }, CancellationToken.None).ToList();
        Assert.Equal(2, items.Count); // loose.tmp + cacheA
        Assert.Equal(200, items.Single(i => i.Path.EndsWith("loose.tmp")).SizeBytes);
        var dir = items.Single(i => i.IsDir);
        Assert.Equal(400, dir.SizeBytes); // 300 + 100
    }

    [Fact]
    public void Provider_scans_its_injected_roots()
    {
        var provider = new TempFilesProvider(new[] { _root });
        var total = provider.Scan(CancellationToken.None).Sum(i => i.SizeBytes);
        Assert.Equal(600, total);
        Assert.Equal("temp", provider.Category.Id);
        Assert.True(provider.Category.DefaultSelected);
    }

    [Fact]
    public void Missing_roots_are_skipped()
    {
        var items = CleanScanHelpers.TopLevelEntries(
            new[] { Path.Combine(_root, "nope") }, CancellationToken.None).ToList();
        Assert.Empty(items);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/NetSearch.Core.Tests --filter CleanProvidersTests`
Expected: FAIL — types do not exist.

- [ ] **Step 3a: Model types**

`src/NetSearch.Core/Cleaning/CleanItem.cs`:
```csharp
namespace NetSearch.Core.Cleaning;

public readonly record struct CleanItem(string Path, long SizeBytes, bool IsDir)
{
    /// <summary>Sentinel path meaning "empty the Recycle Bin" (handled specially by the executor).</summary>
    public const string RecycleBinSentinel = "::recyclebin::";
}
```
`src/NetSearch.Core/Cleaning/CleanCategory.cs`:
```csharp
namespace NetSearch.Core.Cleaning;

public sealed record CleanCategory(
    string Id, string DisplayName, string Description,
    bool NeedsAdmin, bool UsesRecycleBin, bool DefaultSelected);
```
`src/NetSearch.Core/Cleaning/ICleanProvider.cs`:
```csharp
namespace NetSearch.Core.Cleaning;

public interface ICleanProvider
{
    CleanCategory Category { get; }
    IEnumerable<CleanItem> Scan(CancellationToken ct);
}
```

- [ ] **Step 3b: Scan helpers**

`src/NetSearch.Core/Cleaning/CleanScanHelpers.cs`:
```csharp
using System.IO;

namespace NetSearch.Core.Cleaning;

public static class CleanScanHelpers
{
    /// <summary>Yields each immediate child (file or directory) of every existing root, with size.</summary>
    public static IEnumerable<CleanItem> TopLevelEntries(IEnumerable<string> roots, CancellationToken ct)
    {
        foreach (var root in roots)
        {
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) continue;
            IEnumerable<string> entries;
            try { entries = Directory.EnumerateFileSystemEntries(root); }
            catch { continue; }
            foreach (var path in entries)
            {
                ct.ThrowIfCancellationRequested();
                if (TryDescribe(path) is { } item) yield return item;
            }
        }
    }

    public static CleanItem? TryDescribe(string path)
    {
        try
        {
            if (Directory.Exists(path)) return new CleanItem(path, DirectorySize(path), true);
            var fi = new FileInfo(path);
            if (fi.Exists) return new CleanItem(path, fi.Length, false);
        }
        catch { }
        return null;
    }

    public static long DirectorySize(string dir)
    {
        long total = 0;
        try
        {
            foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
            {
                try { total += new FileInfo(f).Length; } catch { }
            }
        }
        catch { }
        return total;
    }
}
```

- [ ] **Step 3c: Providers**

`src/NetSearch.Core/Cleaning/Providers/TempFilesProvider.cs`:
```csharp
using System.IO;

namespace NetSearch.Core.Cleaning.Providers;

public sealed class TempFilesProvider : ICleanProvider
{
    private readonly IReadOnlyList<string> _roots;
    public TempFilesProvider(IReadOnlyList<string> roots) => _roots = roots;

    public static TempFilesProvider ForSystem(bool elevated)
    {
        var roots = new List<string> { Path.GetTempPath() };
        if (elevated)
            roots.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp"));
        return new TempFilesProvider(roots);
    }

    public CleanCategory Category => new(
        "temp", "Временные файлы", "%TEMP% и системная папка Temp",
        NeedsAdmin: false, UsesRecycleBin: true, DefaultSelected: true);

    public IEnumerable<CleanItem> Scan(CancellationToken ct) => CleanScanHelpers.TopLevelEntries(_roots, ct);
}
```
`src/NetSearch.Core/Cleaning/Providers/BrowserCacheProvider.cs`:
```csharp
using System.IO;

namespace NetSearch.Core.Cleaning.Providers;

public sealed class BrowserCacheProvider : ICleanProvider
{
    private readonly IReadOnlyList<string> _roots;
    public BrowserCacheProvider(IReadOnlyList<string> roots) => _roots = roots;

    public static BrowserCacheProvider ForSystem()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var roots = new List<string>
        {
            Path.Combine(local, @"Microsoft\Edge\User Data\Default\Cache"),
            Path.Combine(local, @"Microsoft\Edge\User Data\Default\Code Cache"),
            Path.Combine(local, @"Google\Chrome\User Data\Default\Cache"),
            Path.Combine(local, @"Google\Chrome\User Data\Default\Code Cache"),
            Path.Combine(local, @"Microsoft\Windows\Explorer"), // thumbcache_*.db / iconcache_*.db
        };
        return new BrowserCacheProvider(roots);
    }

    public CleanCategory Category => new(
        "browser", "Кэш браузеров и эскизов", "Кэш Edge/Chrome и кэш эскизов проводника",
        NeedsAdmin: false, UsesRecycleBin: true, DefaultSelected: true);

    public IEnumerable<CleanItem> Scan(CancellationToken ct) => CleanScanHelpers.TopLevelEntries(_roots, ct);
}
```
`src/NetSearch.Core/Cleaning/Providers/SystemDevCacheProvider.cs`:
```csharp
using System.IO;

namespace NetSearch.Core.Cleaning.Providers;

public sealed class SystemDevCacheProvider : ICleanProvider
{
    private readonly IReadOnlyList<string> _roots;
    public SystemDevCacheProvider(IReadOnlyList<string> roots) => _roots = roots;

    public static SystemDevCacheProvider ForSystem(bool elevated)
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var roots = new List<string>
        {
            Path.Combine(local, @"npm-cache"),
            Path.Combine(profile, @".nuget\packages\.tools"),
            Path.Combine(local, @"pip\cache"),
            Path.Combine(local, @"CrashDumps"),
        };
        if (elevated)
        {
            var win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            roots.Add(Path.Combine(win, @"SoftwareDistribution\Download"));
        }
        return new SystemDevCacheProvider(roots);
    }

    public CleanCategory Category => new(
        "sysdev", "Системный и dev-кэш", "Кэш обновлений Windows, дампы, кэш npm/NuGet/pip",
        NeedsAdmin: true, UsesRecycleBin: false, DefaultSelected: false);

    public IEnumerable<CleanItem> Scan(CancellationToken ct) => CleanScanHelpers.TopLevelEntries(_roots, ct);
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/NetSearch.Core.Tests --filter CleanProvidersTests`
Expected: PASS (3 tests).

- [ ] **Step 5: Commit**
```
git add src/NetSearch.Core/Cleaning tests/NetSearch.Core.Tests/CleanProvidersTests.cs
git commit -m "feat(cleaner): clean model, scan helpers and file-based providers"
```

### Task 11: `CleanScanner`

**Files:**
- Create: `src/NetSearch.Core/Cleaning/CleanScanner.cs`
- Test: `tests/NetSearch.Core.Tests/CleanScannerTests.cs`

**Interfaces:**
- Consumes: `ICleanProvider`, `CleanItem`, `CleanCategory` (Task 10).
- Produces:
  - `sealed record CategoryScan(CleanCategory Category, IReadOnlyList<CleanItem> Items, long TotalBytes)`.
  - `CleanScanner(IReadOnlyList<ICleanProvider> providers)` with `IReadOnlyList<CategoryScan> Scan(IEnumerable<string> selectedCategoryIds, CancellationToken ct)` — only selected categories, provider exceptions swallowed to an empty category.

- [ ] **Step 1: Write the failing test**

`tests/NetSearch.Core.Tests/CleanScannerTests.cs`:
```csharp
using Xunit;
using NetSearch.Core.Cleaning;

namespace NetSearch.Core.Tests;

public class CleanScannerTests
{
    private sealed class FakeProvider : ICleanProvider
    {
        private readonly CleanItem[] _items;
        private readonly bool _throws;
        public FakeProvider(string id, bool throws, params CleanItem[] items)
        { Category = new CleanCategory(id, id, "", false, false, true); _items = items; _throws = throws; }
        public CleanCategory Category { get; }
        public IEnumerable<CleanItem> Scan(CancellationToken ct)
        { if (_throws) throw new IOException("boom"); return _items; }
    }

    [Fact]
    public void Scans_only_selected_categories_and_totals_sizes()
    {
        var scanner = new CleanScanner(new ICleanProvider[]
        {
            new FakeProvider("a", false, new CleanItem("p1", 100, false), new CleanItem("p2", 50, false)),
            new FakeProvider("b", false, new CleanItem("p3", 999, false)),
        });
        var result = scanner.Scan(new[] { "a" }, CancellationToken.None);
        Assert.Single(result);
        Assert.Equal("a", result[0].Category.Id);
        Assert.Equal(150, result[0].TotalBytes);
    }

    [Fact]
    public void Provider_failure_yields_empty_category_not_exception()
    {
        var scanner = new CleanScanner(new ICleanProvider[] { new FakeProvider("a", true) });
        var result = scanner.Scan(new[] { "a" }, CancellationToken.None);
        Assert.Single(result);
        Assert.Equal(0, result[0].TotalBytes);
        Assert.Empty(result[0].Items);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/NetSearch.Core.Tests --filter CleanScannerTests`
Expected: FAIL — `CleanScanner` does not exist.

- [ ] **Step 3: Implement `CleanScanner`**

`src/NetSearch.Core/Cleaning/CleanScanner.cs`:
```csharp
namespace NetSearch.Core.Cleaning;

public sealed record CategoryScan(CleanCategory Category, IReadOnlyList<CleanItem> Items, long TotalBytes);

public sealed class CleanScanner
{
    private readonly IReadOnlyList<ICleanProvider> _providers;
    public CleanScanner(IReadOnlyList<ICleanProvider> providers) => _providers = providers;

    public IReadOnlyList<CategoryScan> Scan(IEnumerable<string> selectedCategoryIds, CancellationToken ct)
    {
        var selected = new HashSet<string>(selectedCategoryIds, StringComparer.OrdinalIgnoreCase);
        var results = new List<CategoryScan>();
        foreach (var p in _providers)
        {
            if (!selected.Contains(p.Category.Id)) continue;
            List<CleanItem> items;
            try { items = p.Scan(ct).ToList(); }
            catch (OperationCanceledException) { throw; }
            catch { items = new List<CleanItem>(); }
            results.Add(new CategoryScan(p.Category, items, items.Sum(i => i.SizeBytes)));
        }
        return results;
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/NetSearch.Core.Tests --filter CleanScannerTests`
Expected: PASS (2 tests).

- [ ] **Step 5: Commit**
```
git add src/NetSearch.Core/Cleaning/CleanScanner.cs tests/NetSearch.Core.Tests/CleanScannerTests.cs
git commit -m "feat(cleaner): category scanner"
```

### Task 12: `ShellFileOps` interop + `CleanExecutor`

**Files:**
- Create: `src/NetSearch.Core/Cleaning/Native/ShellFileOps.cs`
- Create: `src/NetSearch.Core/Cleaning/CleanExecutor.cs`
- Test: `tests/NetSearch.Core.Tests/CleanExecutorTests.cs`

**Interfaces:**
- Consumes: `CleanItem` (Task 10).
- Produces:
  - `static class ShellFileOps` (Windows-only): `void DeleteToRecycleBin(string path)`, `bool TryEmptyRecycleBin()`, `long QueryRecycleBinSize()`.
  - `sealed record CleanOutcome(long FreedBytes, int DeletedCount, IReadOnlyList<string> Skipped)`.
  - `CleanExecutor` with `CleanOutcome Delete(IEnumerable<CleanItem> items, bool useRecycleBin, CancellationToken ct)`.

- [ ] **Step 1: Write the failing test** (permanent-delete branch in a sandbox)

`tests/NetSearch.Core.Tests/CleanExecutorTests.cs`:
```csharp
using Xunit;
using NetSearch.Core.Cleaning;

namespace NetSearch.Core.Tests;

public class CleanExecutorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"cexec_{Guid.NewGuid():N}");

    public CleanExecutorTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void Delete_permanent_removes_files_and_dirs_and_reports_freed()
    {
        var file = Path.Combine(_root, "f.tmp");
        var dir = Path.Combine(_root, "d");
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(file, new byte[120]);
        File.WriteAllBytes(Path.Combine(dir, "inner.bin"), new byte[80]);

        var items = new[] { new CleanItem(file, 120, false), new CleanItem(dir, 80, true) };
        var outcome = new CleanExecutor().Delete(items, useRecycleBin: false, CancellationToken.None);

        Assert.False(File.Exists(file));
        Assert.False(Directory.Exists(dir));
        Assert.Equal(2, outcome.DeletedCount);
        Assert.Equal(200, outcome.FreedBytes);
        Assert.Empty(outcome.Skipped);
    }

    [Fact]
    public void Locked_file_is_skipped_not_fatal()
    {
        var locked = Path.Combine(_root, "locked.tmp");
        using var stream = new FileStream(locked, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
        var ok = Path.Combine(_root, "ok.tmp");
        File.WriteAllBytes(ok, new byte[10]);

        var items = new[] { new CleanItem(locked, 50, false), new CleanItem(ok, 10, false) };
        var outcome = new CleanExecutor().Delete(items, useRecycleBin: false, CancellationToken.None);

        Assert.Equal(1, outcome.DeletedCount);
        Assert.Equal(10, outcome.FreedBytes);
        Assert.Contains(locked, outcome.Skipped);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, true); } catch { }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/NetSearch.Core.Tests --filter CleanExecutorTests`
Expected: FAIL — types do not exist.

- [ ] **Step 3a: Implement `ShellFileOps`**

`src/NetSearch.Core/Cleaning/Native/ShellFileOps.cs`:
```csharp
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace NetSearch.Core.Cleaning.Native;

[SupportedOSPlatform("windows")]
public static class ShellFileOps
{
    private const ushort FO_DELETE = 0x0003;
    private const ushort FOF_ALLOWUNDO = 0x0040;
    private const ushort FOF_NOCONFIRMATION = 0x0010;
    private const ushort FOF_NOERRORUI = 0x0400;
    private const ushort FOF_SILENT = 0x0004;

    private const uint SHERB_NOCONFIRMATION = 0x00000001;
    private const uint SHERB_NOPROGRESSUI = 0x00000002;
    private const uint SHERB_NOSOUND = 0x00000004;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEOPSTRUCT
    {
        public IntPtr hwnd;
        public uint wFunc;
        public string pFrom;
        public string? pTo;
        public ushort fFlags;
        public int fAnyOperationsAborted;
        public IntPtr hNameMappings;
        public string? lpszProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHFileOperation(ref SHFILEOPSTRUCT lpFileOp);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHEmptyRecycleBin(IntPtr hwnd, string? pszRootPath, uint dwFlags);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHQUERYRBINFO { public int cbSize; public long i64Size; public long i64NumItems; }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHQueryRecycleBin(string? pszRootPath, ref SHQUERYRBINFO pSHQueryRBInfo);

    public static void DeleteToRecycleBin(string path)
    {
        var op = new SHFILEOPSTRUCT
        {
            wFunc = FO_DELETE,
            pFrom = path + "\0\0", // double-null-terminated list
            fFlags = (ushort)(FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_NOERRORUI | FOF_SILENT),
        };
        int rc = SHFileOperation(ref op);
        if (rc != 0 || op.fAnyOperationsAborted != 0)
            throw new IOException($"SHFileOperation failed (0x{rc:X}) for {path}");
    }

    public static bool TryEmptyRecycleBin()
    {
        int rc = SHEmptyRecycleBin(IntPtr.Zero, null,
            SHERB_NOCONFIRMATION | SHERB_NOPROGRESSUI | SHERB_NOSOUND);
        return rc == 0;
    }

    public static long QueryRecycleBinSize()
    {
        var info = new SHQUERYRBINFO { cbSize = Marshal.SizeOf<SHQUERYRBINFO>() };
        return SHQueryRecycleBin(null, ref info) == 0 ? info.i64Size : 0;
    }
}
```

- [ ] **Step 3b: Implement `CleanExecutor`**

`src/NetSearch.Core/Cleaning/CleanExecutor.cs`:
```csharp
using System.IO;
using NetSearch.Core.Cleaning.Native;

namespace NetSearch.Core.Cleaning;

public sealed record CleanOutcome(long FreedBytes, int DeletedCount, IReadOnlyList<string> Skipped);

public sealed class CleanExecutor
{
    public CleanOutcome Delete(IEnumerable<CleanItem> items, bool useRecycleBin, CancellationToken ct)
    {
        long freed = 0;
        int deleted = 0;
        var skipped = new List<string>();

        foreach (var item in items)
        {
            ct.ThrowIfCancellationRequested();

            if (item.Path == CleanItem.RecycleBinSentinel)
            {
                if (OperatingSystem.IsWindows() && ShellFileOps.TryEmptyRecycleBin())
                { freed += item.SizeBytes; deleted++; }
                else skipped.Add("Корзина");
                continue;
            }

            try
            {
                if (useRecycleBin && OperatingSystem.IsWindows())
                    ShellFileOps.DeleteToRecycleBin(item.Path);
                else if (item.IsDir)
                    Directory.Delete(item.Path, recursive: true);
                else
                    File.Delete(item.Path);

                freed += item.SizeBytes;
                deleted++;
            }
            catch (OperationCanceledException) { throw; }
            catch { skipped.Add(item.Path); }
        }

        return new CleanOutcome(freed, deleted, skipped);
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/NetSearch.Core.Tests --filter CleanExecutorTests`
Expected: PASS (2 tests).

- [ ] **Step 5: Commit**
```
git add src/NetSearch.Core/Cleaning/Native/ShellFileOps.cs src/NetSearch.Core/Cleaning/CleanExecutor.cs tests/NetSearch.Core.Tests/CleanExecutorTests.cs
git commit -m "feat(cleaner): shell delete interop and clean executor"
```

### Task 13: `RecycleBinProvider`

**Files:**
- Create: `src/NetSearch.Core/Cleaning/Providers/RecycleBinProvider.cs`

**Interfaces:**
- Consumes: `ShellFileOps.QueryRecycleBinSize` (Task 12), `CleanItem.RecycleBinSentinel`.
- Produces: `RecycleBinProvider : ICleanProvider` whose `Scan` yields one sentinel item sized to the current bin (Windows only; empty otherwise).

- [ ] **Step 1: Implement the provider** (interop scan — verified manually, no unit test)

`src/NetSearch.Core/Cleaning/Providers/RecycleBinProvider.cs`:
```csharp
using NetSearch.Core.Cleaning.Native;

namespace NetSearch.Core.Cleaning.Providers;

public sealed class RecycleBinProvider : ICleanProvider
{
    public CleanCategory Category => new(
        "recyclebin", "Корзина", "Очистить корзину на всех дисках",
        NeedsAdmin: false, UsesRecycleBin: false, DefaultSelected: true);

    public IEnumerable<CleanItem> Scan(CancellationToken ct)
    {
        long size = OperatingSystem.IsWindows() ? ShellFileOps.QueryRecycleBinSize() : 0;
        if (size > 0)
            yield return new CleanItem(CleanItem.RecycleBinSentinel, size, true);
    }
}
```

- [ ] **Step 2: Build & verify**

Run: `dotnet build src/NetSearch.Core -c Debug`
Expected: 0 errors, 0 warnings.

- [ ] **Step 3: Commit**
```
git add src/NetSearch.Core/Cleaning/Providers/RecycleBinProvider.cs
git commit -m "feat(cleaner): recycle-bin provider"
```

### Task 14: `CleanerViewModel`

**Files:**
- Create: `src/NetSearch.App/ViewModels/CleanCategoryRow.cs`
- Create: `src/NetSearch.App/ViewModels/CleanerViewModel.cs`
- Modify: `src/NetSearch.App/ViewModels/MainViewModel.cs` (expose `Cleaner`)

**Interfaces:**
- Consumes: `CleanScanner`, `CleanExecutor`, `CategoryScan`, `CleanCategory`, `CleanItem`, all providers (Core); `ElevationService`; `SizeFormatter`.
- Produces:
  - `CleanCategoryRow : ObservableObject` — `CleanCategory Category`; `bool IsSelected`; `long SizeBytes`; `int ItemCount`; `string SizeText`; `bool Scanned`; holds `IReadOnlyList<CleanItem> Items`.
  - `CleanerViewModel` with `ObservableCollection<CleanCategoryRow> Categories`; `bool IsElevated`; `bool IsBusy`; `string Status`; `long SelectedFreeable`; commands `ScanCommand`, `CleanCommand`, `RelaunchAdminCommand`.
  - `MainViewModel.Cleaner`.

- [ ] **Step 1: Implement `CleanCategoryRow`**

`src/NetSearch.App/ViewModels/CleanCategoryRow.cs`:
```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using NetSearch.Core.Cleaning;
using NetSearch.Core.DiskUsage;

namespace NetSearch.App.ViewModels;

public partial class CleanCategoryRow : ObservableObject
{
    public CleanCategoryRow(CleanCategory category, bool enabled)
    {
        Category = category;
        IsEnabled = enabled;
        IsSelected = enabled && category.DefaultSelected;
    }

    public CleanCategory Category { get; }
    public string DisplayName => Category.DisplayName;
    public string Description => Category.Description;
    public bool NeedsAdmin => Category.NeedsAdmin;
    public bool UsesRecycleBin => Category.UsesRecycleBin;

    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private bool _isEnabled;
    [ObservableProperty] private long _sizeBytes;
    [ObservableProperty] private int _itemCount;
    [ObservableProperty] private bool _scanned;

    public IReadOnlyList<CleanItem> Items { get; set; } = System.Array.Empty<CleanItem>();
    public string SizeText => Scanned ? SizeFormatter.Format(SizeBytes) : "—";

    partial void OnSizeBytesChanged(long value) => OnPropertyChanged(nameof(SizeText));
    partial void OnScannedChanged(bool value) => OnPropertyChanged(nameof(SizeText));
}
```

- [ ] **Step 2: Implement `CleanerViewModel`**

`src/NetSearch.App/ViewModels/CleanerViewModel.cs`:
```csharp
using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NetSearch.App.Services;
using NetSearch.Core.Cleaning;
using NetSearch.Core.Cleaning.Providers;
using NetSearch.Core.DiskUsage;

namespace NetSearch.App.ViewModels;

public partial class CleanerViewModel : ObservableObject
{
    private readonly List<ICleanProvider> _providers;

    public ObservableCollection<CleanCategoryRow> Categories { get; } = new();

    [ObservableProperty] private bool _isElevated = ElevationService.IsElevated;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _status = "Нажмите «Сканировать», чтобы оценить, что можно удалить.";

    private CancellationTokenSource? _cts;

    public CleanerViewModel()
    {
        bool elevated = IsElevated;
        _providers = new List<ICleanProvider>
        {
            TempFilesProvider.ForSystem(elevated),
            new RecycleBinProvider(),
            BrowserCacheProvider.ForSystem(),
            SystemDevCacheProvider.ForSystem(elevated),
        };
        foreach (var p in _providers)
            Categories.Add(new CleanCategoryRow(p.Category, enabled: !p.Category.NeedsAdmin || elevated));
    }

    [RelayCommand]
    private async Task ScanAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        _cts = new CancellationTokenSource();
        Status = "Сканирование…";
        try
        {
            var ids = Categories.Where(c => c.IsEnabled).Select(c => c.Category.Id).ToList();
            var scanner = new CleanScanner(_providers);
            var token = _cts.Token;
            var results = await Task.Run(() => scanner.Scan(ids, token), token);
            foreach (var row in Categories)
            {
                var scan = results.FirstOrDefault(r => r.Category.Id == row.Category.Id);
                row.Items = scan?.Items ?? System.Array.Empty<CleanItem>();
                row.SizeBytes = scan?.TotalBytes ?? 0;
                row.ItemCount = scan?.Items.Count ?? 0;
                row.Scanned = true;
            }
            Status = $"Можно освободить: {SizeFormatter.Format(SelectedFreeable)} (выбрано категорий: {Categories.Count(c => c.IsSelected)})";
        }
        catch (OperationCanceledException) { Status = "Сканирование отменено."; }
        catch (Exception ex) { Status = "Ошибка: " + ex.Message; }
        finally { IsBusy = false; _cts?.Dispose(); _cts = null; }
    }

    public long SelectedFreeable => Categories.Where(c => c.IsSelected).Sum(c => c.SizeBytes);

    [RelayCommand]
    private async Task CleanAsync()
    {
        if (IsBusy) return;
        var chosen = Categories.Where(c => c.IsSelected && c.Scanned && c.ItemCount > 0).ToList();
        if (chosen.Count == 0) { Status = "Нечего удалять — сначала отсканируйте и выберите категории."; return; }

        long total = chosen.Sum(c => c.SizeBytes);
        var confirm = MessageBox.Show(
            $"Удалить {SizeFormatter.Format(total)} из {chosen.Count} категорий?\n\n" +
            "Файлы пользователя (документы, загрузки) не затрагиваются. Где возможно — в Корзину.",
            "Подтверждение очистки", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.OK) return;

        IsBusy = true;
        _cts = new CancellationTokenSource();
        Status = "Очистка…";
        try
        {
            var token = _cts.Token;
            var executor = new CleanExecutor();
            long freed = 0; int deleted = 0; var skipped = new List<string>();
            await Task.Run(() =>
            {
                foreach (var row in chosen)
                {
                    token.ThrowIfCancellationRequested();
                    var outcome = executor.Delete(row.Items, row.Category.UsesRecycleBin, token);
                    freed += outcome.FreedBytes; deleted += outcome.DeletedCount;
                    skipped.AddRange(outcome.Skipped);
                }
            }, token);

            foreach (var row in chosen) { row.Items = System.Array.Empty<CleanItem>(); row.SizeBytes = 0; row.ItemCount = 0; }
            Status = skipped.Count == 0
                ? $"Освобождено {SizeFormatter.Format(freed)} ({deleted} объектов)."
                : $"Освобождено {SizeFormatter.Format(freed)} ({deleted} объектов). Пропущено: {skipped.Count}.";
        }
        catch (OperationCanceledException) { Status = "Очистка отменена."; }
        catch (Exception ex) { Status = "Ошибка очистки: " + ex.Message; }
        finally { IsBusy = false; _cts?.Dispose(); _cts = null; }
    }

    [RelayCommand] private void RelaunchAdmin() => ElevationService.RelaunchAsAdmin();
}
```

- [ ] **Step 3: Expose `Cleaner` on `MainViewModel`**

In `src/NetSearch.App/ViewModels/MainViewModel.cs`, next to the `Disk` property added in Task 8, add:
```csharp
    public CleanerViewModel Cleaner { get; } = new();
```

- [ ] **Step 4: Build & verify**

Run: `dotnet build src/NetSearch.App -c Debug`
Expected: 0 errors, 0 warnings.

- [ ] **Step 5: Commit**
```
git add src/NetSearch.App/ViewModels/CleanCategoryRow.cs src/NetSearch.App/ViewModels/CleanerViewModel.cs src/NetSearch.App/ViewModels/MainViewModel.cs
git commit -m "feat(cleaner): cleaner view-model with scan/confirm/clean"
```

### Task 15: Очистка tab UI

**Files:**
- Modify: `src/NetSearch.App/MainWindow.xaml` (fill the "Очистка" tab)

**Interfaces:**
- Consumes: `MainViewModel.Cleaner` (Task 14).

- [ ] **Step 1: Fill the cleaner tab**

In `src/NetSearch.App/MainWindow.xaml`, replace the `Очистка` tab's placeholder `<Grid/>` with:
```xml
        <TabItem Header="Очистка">
            <Grid Margin="14" DataContext="{Binding Cleaner}">
                <Grid.RowDefinitions>
                    <RowDefinition Height="Auto"/>
                    <RowDefinition Height="Auto"/>
                    <RowDefinition Height="*"/>
                    <RowDefinition Height="Auto"/>
                </Grid.RowDefinitions>

                <Border Grid.Row="0" Style="{StaticResource WarningBanner}"
                        Visibility="{Binding IsElevated, Converter={StaticResource InverseBoolToVis}}">
                    <DockPanel>
                        <Button DockPanel.Dock="Right" Content="Перезапустить от имени администратора"
                                Command="{Binding RelaunchAdminCommand}"/>
                        <TextBlock VerticalAlignment="Center" TextWrapping="Wrap"
                                   Text="Системные категории (кэш обновлений Windows и т.п.) требуют прав администратора и сейчас отключены."/>
                    </DockPanel>
                </Border>

                <DockPanel Grid.Row="1" Margin="0,0,0,10">
                    <Button DockPanel.Dock="Right" Content="Очистить выбранное" Style="{StaticResource AccentButton}"
                            Command="{Binding CleanCommand}"/>
                    <Button DockPanel.Dock="Right" Content="Сканировать" Margin="0,0,8,0"
                            Command="{Binding ScanCommand}"/>
                    <TextBlock Text="Выберите категории и отсканируйте. Удаление — только после подтверждения; где возможно — в Корзину."
                               TextWrapping="Wrap" Style="{StaticResource MutedText}" VerticalAlignment="Center"/>
                </DockPanel>

                <Border Grid.Row="2" Style="{StaticResource CardBorder}" Padding="0">
                    <ItemsControl ItemsSource="{Binding Categories}">
                        <ItemsControl.ItemTemplate>
                            <DataTemplate>
                                <Grid Margin="10,8" IsEnabled="{Binding IsEnabled}">
                                    <Grid.ColumnDefinitions>
                                        <ColumnDefinition Width="Auto"/>
                                        <ColumnDefinition Width="*"/>
                                        <ColumnDefinition Width="Auto"/>
                                    </Grid.ColumnDefinitions>
                                    <CheckBox Grid.Column="0" IsChecked="{Binding IsSelected}" VerticalAlignment="Center" Margin="0,0,10,0"/>
                                    <StackPanel Grid.Column="1">
                                        <StackPanel Orientation="Horizontal">
                                            <TextBlock Text="{Binding DisplayName}" FontWeight="SemiBold"/>
                                            <Border Background="#7A5A00" CornerRadius="3" Padding="5,1" Margin="8,0,0,0"
                                                    Visibility="{Binding NeedsAdmin, Converter={StaticResource BoolToVis}}">
                                                <TextBlock Text="админ" FontSize="10" Foreground="White"/>
                                            </Border>
                                        </StackPanel>
                                        <TextBlock Text="{Binding Description}" Style="{StaticResource MutedText}"/>
                                    </StackPanel>
                                    <TextBlock Grid.Column="2" Text="{Binding SizeText}" VerticalAlignment="Center"
                                               FontWeight="SemiBold" Margin="10,0,0,0"/>
                                </Grid>
                            </DataTemplate>
                        </ItemsControl.ItemTemplate>
                    </ItemsControl>
                </Border>

                <Border Grid.Row="3" Background="{StaticResource SurfaceBrush}" BorderBrush="{StaticResource BorderBrush}"
                        BorderThickness="1" CornerRadius="6" Margin="0,10,0,0" Padding="10,6">
                    <TextBlock Text="{Binding Status}" Style="{StaticResource MutedText}"/>
                </Border>
            </Grid>
        </TabItem>
```

- [ ] **Step 2: Build & manual smoke**

Run: `dotnet build src/NetSearch.App -c Debug`, then `dotnet run --project src/NetSearch.App`.
Manual checks (use a throwaway/elevated session): open **Очистка**; the four categories show; the system/dev row is disabled (and the banner shows) when not elevated; **Сканировать** fills sizes; **Очистить выбранное** shows the confirm dialog; on OK it reports freed space; re-scan shows reduced sizes. Verify your Documents/Downloads are untouched.

- [ ] **Step 3: Commit**
```
git add src/NetSearch.App/MainWindow.xaml
git commit -m "feat(cleaner): cleanup tab UI"
```

---

## Phase 4 — Installer

### Task 16: Version bump, Inno Setup script, publish pipeline, README

**Files:**
- Modify: `src/NetSearch.App/NetSearch.App.csproj` (`<Version>0.2.0</Version>`)
- Create: `installer/netsearch.iss`
- Modify: `publish.ps1` (publish → locate ISCC → compile installer)
- Modify: `README.md` (install section + version)

**Interfaces:**
- Consumes: the committed `NetSearch.ico` (Task 1) and the published `publish/NetSearch.exe`.
- Produces: `dist/NetSearch-Setup-0.2.0.exe`.

- [ ] **Step 1: Bump the version**

In `src/NetSearch.App/NetSearch.App.csproj`, change:
```xml
    <Version>0.1.0</Version>
```
to:
```xml
    <Version>0.2.0</Version>
```

- [ ] **Step 2: Write the Inno Setup script**

`installer/netsearch.iss`:
```ini
; NetSearch installer (Inno Setup 6+). Built by publish.ps1.
#define AppName "NetSearch"
#define AppVersion "0.2.0"
#define AppPublisher "denfry"
#define AppExe "NetSearch.exe"

[Setup]
AppId={{8E6F2C2A-7B4D-4C1E-9E2A-NETSEARCH0002}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
UninstallDisplayIcon={app}\{#AppExe}
OutputDir=..\dist
OutputBaseFilename=NetSearch-Setup-{#AppVersion}
SetupIconFile=..\NetSearch.ico
Compression=lzma2
SolidCompression=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
WizardStyle=modern

[Languages]
Name: "ru"; MessagesFile: "compiler:Languages\Russian.isl"

[Tasks]
Name: "desktopicon"; Description: "Создать ярлык на рабочем столе"; GroupDescription: "Дополнительно:"

[Files]
Source: "..\publish\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{group}\Удалить {#AppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "Запустить {#AppName}"; Flags: nowait postinstall skipifsilent
```
> The `AppId` GUID above is a stable identity; keep it constant across versions so upgrades replace cleanly.

- [ ] **Step 3: Extend the publish pipeline**

Replace `publish.ps1` with:
```powershell
$ErrorActionPreference = "Stop"

# 1) Publish the portable single-file exe (existing behavior).
dotnet publish src/NetSearch.App/NetSearch.App.csproj -c Release -o publish
Write-Host "Built publish/NetSearch.exe"

# 2) Locate the Inno Setup compiler.
$iscc = Get-Command iscc.exe -ErrorAction SilentlyContinue
if (-not $iscc) {
    foreach ($p in @(
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "${env:ProgramFiles}\Inno Setup 6\ISCC.exe")) {
        if (Test-Path $p) { $iscc = $p; break }
    }
} else { $iscc = $iscc.Source }

if (-not $iscc) {
    Write-Warning "Inno Setup (ISCC.exe) not found. Install it, e.g.: winget install JRSoftware.InnoSetup"
    Write-Warning "Portable publish/NetSearch.exe is ready; skipping installer."
    exit 0
}

# 3) Compile the installer into dist/.
New-Item -ItemType Directory -Force -Path dist | Out-Null
& $iscc "installer/netsearch.iss"
Write-Host "Built dist/NetSearch-Setup-0.2.0.exe"
```

- [ ] **Step 4: Update the README**

In `README.md`, update the **Установка** section so it offers both the installer and the portable exe, and change the **Сборка** note to mention the installer. Replace the current Установка block (lines ~25–31) with:
```markdown
## Установка

**Вариант 1 — установщик (рекомендуется):** скачайте `NetSearch-Setup-0.2.0.exe`
со страницы [Releases](https://github.com/denfry/NetSearch/releases/latest) и
запустите — ярлыки и деинсталлятор создаются автоматически.

**Вариант 2 — портативно:** скачайте `NetSearch.exe` и запустите без установки.

> Требования: Windows 10/11 (x64). Среда выполнения .NET встроена в `.exe`.
> Для быстрого сканирования локальных дисков и очистки нужны права администратора.
```
And under **Сборка из исходников**, change the publish line note to:
```markdown
pwsh ./publish.ps1    # портативный publish/NetSearch.exe + dist/NetSearch-Setup-0.2.0.exe (нужен Inno Setup)
```

- [ ] **Step 5: Build the installer & manual verify**

Run:
```
pwsh ./publish.ps1
```
Expected: prints `Built publish/NetSearch.exe`; if Inno Setup is installed, also `Built dist/NetSearch-Setup-0.2.0.exe`. If ISCC is missing, install via `winget install JRSoftware.InnoSetup` and re-run.
Manual: run `dist/NetSearch-Setup-0.2.0.exe`, install (UAC prompt → admin), confirm Start-menu + desktop shortcuts and the app icon, launch the app, then uninstall via Add/Remove Programs and confirm clean removal.

- [ ] **Step 6: Commit**
```
git add src/NetSearch.App/NetSearch.App.csproj installer/netsearch.iss publish.ps1 README.md
git commit -m "feat(installer): Inno Setup installer and publish pipeline; bump to 0.2.0"
```

---

## Final verification

- [ ] **Run the full Core test suite:** `dotnet test tests/NetSearch.Core.Tests` → all green (previous 39 + new SizeFormatter, FolderTreeBuilder, Treemap, DiskUsageScanner, CleanProviders, CleanScanner, CleanExecutor tests).
- [ ] **Build the app clean:** `dotnet build src/NetSearch.App -c Release` → 0 warnings, 0 errors.
- [ ] **Manual smoke (elevated and non-elevated):** all three tabs work; disks scan + treemap drill-down; cleaner scan → confirm → freed report with Documents/Downloads untouched; installer installs/uninstalls with icon and shortcuts.
- [ ] **Update memory** if any environment fact changed (e.g., Inno Setup now required for `publish.ps1`).

## Self-review notes (addressed)

- **Spec coverage:** icon (Task 1), disks tab tree-list+treemap (Tasks 2–9), cleaner 4 categories + safety (Tasks 10–15), installer + version bump (Task 16), elevation relaunch (Task 7, used in 9/15). All spec sections map to tasks.
- **Type consistency:** `MftEnumerator.Enumerate`/`Crawler.Crawl` `Action<IReadOnlyList<FileEntry>>` matches `FolderTreeBuilder.AddBatch`; `Treemap.Squarify` returns `IReadOnlyList<TreemapRect>` consumed by `LayoutTiles`; `CleanItem`/`CleanCategory`/`CategoryScan`/`CleanOutcome` names are consistent across Tasks 10–15; `InverseBoolToVis` is defined in Task 9 before use in Task 15.
- **Known follow-ups (out of scope):** logical (not allocated) sizes; `dotnet run` relaunch-as-admin uses the host path (works for the published exe); treemap shows one drill level at a time.
```