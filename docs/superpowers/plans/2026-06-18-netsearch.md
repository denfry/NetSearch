# NetSearch Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a Windows desktop app (WizFile-style) that indexes one or more network (SMB) paths into a local SQLite index and searches files/folders instantly by name, with size/date/type filters, regex, and an on-demand content search over the current result set.

**Architecture:** A WPF `.exe` split into a UI-free core class library (`NetSearch.Core`) and a WPF front-end (`NetSearch.App`). The core holds `IndexStore` (SQLite + in-memory list), `Crawler` (directory traversal), `IndexManager` (full/incremental indexing), `SearchEngine` (name/filter/regex matching), and `ContentSearcher` (text-in-file search over a selection). The app layer wires these to a `MainViewModel` and XAML views. All non-UI logic is unit-tested with xUnit against temp folders and temp SQLite files.

**Tech Stack:** C# / .NET 9, WPF (`net9.0-windows`), `Microsoft.Data.Sqlite` 8.x, `CommunityToolkit.Mvvm` 8.x, `System.Text.Json`, xUnit + `Microsoft.NET.Test.Sdk`.

## Global Constraints

- Target framework: `net9.0` for `NetSearch.Core` and tests; `net9.0-windows` with `<UseWPF>true</UseWPF>` for `NetSearch.App`. (Only the .NET 9 SDK is installed on the target machine.)
- Platform: Windows only. Enable long-path support (`<AppContext>` / app manifest `longPathAware`).
- Index DB and settings live in `%LOCALAPPDATA%\NetSearch\` (`index.db`, `settings.json`).
- All file/path comparisons for identity are case-insensitive (`StringComparer.OrdinalIgnoreCase`); store `name_lower` for fast name matching.
- Timestamps stored as Unix seconds (UTC) in the DB.
- Regex matching always uses a match timeout (1 second) to guard against catastrophic backtracking.
- All long-running operations (crawl, content search) accept a `CancellationToken` and never block the UI thread.
- Project namespaces: `NetSearch.Core.Models`, `NetSearch.Core.Storage`, `NetSearch.Core.Indexing`, `NetSearch.Core.Search`, `NetSearch.Core.Settings`, `NetSearch.App`, `NetSearch.App.ViewModels`.

---

## File Structure

```
NetSearch.sln
src/
  NetSearch.Core/
    NetSearch.Core.csproj
    Models/FileEntry.cs          # immutable record for an indexed entry
    Models/RootPath.cs           # a configured root + last_indexed
    Storage/IndexStore.cs        # SQLite schema + bulk upsert + load + remove
    Indexing/Crawler.cs          # recursive enumeration, error-skipping, batches
    Indexing/IndexManager.cs     # full rebuild + incremental update + counts
    Search/SearchQuery.cs        # query model (mode, text, filters)
    Search/SearchEngine.cs       # name matching (substring/wildcard/regex) + filters
    Search/ContentSearcher.cs    # text-in-file search over a selection
    Settings/AppSettings.cs      # settings POCO + SettingsStore (json load/save)
  NetSearch.App/
    NetSearch.App.csproj
    app.manifest                 # longPathAware
    App.xaml / App.xaml.cs       # bootstrap, ensure LOCALAPPDATA dir
    MainWindow.xaml / .cs        # search bar, filters, results grid, status
    Views/SettingsWindow.xaml / .cs
    ViewModels/MainViewModel.cs  # state -> SearchQuery, debounce, commands
    ViewModels/SettingsViewModel.cs
    ViewModels/FileRow.cs        # display row wrapper for the grid
tests/
  NetSearch.Core.Tests/
    NetSearch.Core.Tests.csproj
    IndexStoreTests.cs
    CrawlerTests.cs
    IndexManagerTests.cs
    SearchEngineTests.cs
    ContentSearcherTests.cs
    SettingsStoreTests.cs
    MainViewModelLogicTests.cs
```

---

## Task 1: Solution & project scaffold

**Files:**
- Create: `NetSearch.sln`
- Create: `src/NetSearch.Core/NetSearch.Core.csproj`
- Create: `src/NetSearch.App/NetSearch.App.csproj`
- Create: `tests/NetSearch.Core.Tests/NetSearch.Core.Tests.csproj`
- Create: `src/NetSearch.Core/Models/Placeholder.cs` (temporary, to give Core a compile target)
- Create: `tests/NetSearch.Core.Tests/SmokeTests.cs`
- Create: `.gitignore`

**Interfaces:**
- Consumes: nothing.
- Produces: a buildable solution; `NetSearch.Core` referenced by both the app and the test project.

- [ ] **Step 1: Write the smoke test**

`tests/NetSearch.Core.Tests/SmokeTests.cs`:
```csharp
namespace NetSearch.Core.Tests;

public class SmokeTests
{
    [Fact]
    public void Solution_builds_and_tests_run()
    {
        Assert.True(true);
    }
}
```

- [ ] **Step 2: Create the projects and solution**

`src/NetSearch.Core/NetSearch.Core.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <LangVersion>latest</LangVersion>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.Data.Sqlite" Version="8.0.6" />
  </ItemGroup>
</Project>
```

`src/NetSearch.App/NetSearch.App.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net9.0-windows</TargetFramework>
    <UseWPF>true</UseWPF>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <ApplicationManifest>app.manifest</ApplicationManifest>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="CommunityToolkit.Mvvm" Version="8.2.2" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\NetSearch.Core\NetSearch.Core.csproj" />
  </ItemGroup>
</Project>
```

`tests/NetSearch.Core.Tests/NetSearch.Core.Tests.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.10.0" />
    <PackageReference Include="xunit" Version="2.8.1" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.1" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\NetSearch.Core\NetSearch.Core.csproj" />
  </ItemGroup>
</Project>
```

`src/NetSearch.Core/Models/Placeholder.cs`:
```csharp
namespace NetSearch.Core.Models;

// Temporary type so the Core library has a compile target. Removed in Task 2.
internal static class Placeholder { }
```

`.gitignore`:
```
bin/
obj/
*.user
%LOCALAPPDATA%/
.vs/
```

Create the solution and wire projects:
```bash
dotnet new sln -n NetSearch
dotnet sln add src/NetSearch.Core/NetSearch.Core.csproj
dotnet sln add src/NetSearch.App/NetSearch.App.csproj
dotnet sln add tests/NetSearch.Core.Tests/NetSearch.Core.Tests.csproj
```

- [ ] **Step 3: Run the test to verify it passes**

Run: `dotnet test`
Expected: build succeeds, 1 test passes.

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "chore: scaffold NetSearch solution (core, app, tests)"
```

---

## Task 2: Core models (`FileEntry`, `RootPath`)

**Files:**
- Create: `src/NetSearch.Core/Models/FileEntry.cs`
- Create: `src/NetSearch.Core/Models/RootPath.cs`
- Delete: `src/NetSearch.Core/Models/Placeholder.cs`
- Create: `tests/NetSearch.Core.Tests/FileEntryTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces:
  - `record FileEntry { long Id; int RootId; string Name; string NameLower; string ParentPath; bool IsDir; long Size; string Ext; long Modified; string FullPath; }`
  - `static FileEntry FileEntry.FromFileSystem(int rootId, string fullPath, bool isDir, long size, long modifiedUnix)`
  - `record RootPath(int Id, string Path, long LastIndexed)`

- [ ] **Step 1: Write the failing test**

`tests/NetSearch.Core.Tests/FileEntryTests.cs`:
```csharp
using NetSearch.Core.Models;

namespace NetSearch.Core.Tests;

public class FileEntryTests
{
    [Fact]
    public void FromFileSystem_derives_name_namelower_parent_and_ext()
    {
        var e = FileEntry.FromFileSystem(
            rootId: 1,
            fullPath: @"\\server\share\Docs\Report.PDF",
            isDir: false,
            size: 1234,
            modifiedUnix: 1700000000);

        Assert.Equal("Report.PDF", e.Name);
        Assert.Equal("report.pdf", e.NameLower);
        Assert.Equal(@"\\server\share\Docs", e.ParentPath);
        Assert.Equal("pdf", e.Ext);
        Assert.False(e.IsDir);
        Assert.Equal(1234, e.Size);
        Assert.Equal(1700000000, e.Modified);
        Assert.Equal(@"\\server\share\Docs\Report.PDF", e.FullPath);
    }

    [Fact]
    public void FromFileSystem_directory_has_empty_ext()
    {
        var e = FileEntry.FromFileSystem(2, @"C:\Temp\SubFolder", isDir: true, size: 0, modifiedUnix: 0);
        Assert.True(e.IsDir);
        Assert.Equal("", e.Ext);
        Assert.Equal("SubFolder", e.Name);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~FileEntryTests"`
Expected: FAIL (compile error — `FileEntry` does not exist).

- [ ] **Step 3: Implement the models**

`src/NetSearch.Core/Models/FileEntry.cs`:
```csharp
namespace NetSearch.Core.Models;

public sealed record FileEntry
{
    public long Id { get; init; }
    public int RootId { get; init; }
    public required string Name { get; init; }
    public required string NameLower { get; init; }
    public required string ParentPath { get; init; }
    public bool IsDir { get; init; }
    public long Size { get; init; }
    public required string Ext { get; init; }
    public long Modified { get; init; }

    public string FullPath =>
        ParentPath.Length == 0 ? Name : Path.Combine(ParentPath, Name);

    public static FileEntry FromFileSystem(int rootId, string fullPath, bool isDir, long size, long modifiedUnix)
    {
        var name = Path.GetFileName(fullPath.TrimEnd('\\', '/'));
        var parent = Path.GetDirectoryName(fullPath.TrimEnd('\\', '/')) ?? "";
        var ext = isDir ? "" : Path.GetExtension(name).TrimStart('.').ToLowerInvariant();
        return new FileEntry
        {
            RootId = rootId,
            Name = name,
            NameLower = name.ToLowerInvariant(),
            ParentPath = parent,
            IsDir = isDir,
            Size = size,
            Ext = ext,
            Modified = modifiedUnix,
        };
    }
}
```

`src/NetSearch.Core/Models/RootPath.cs`:
```csharp
namespace NetSearch.Core.Models;

public sealed record RootPath(int Id, string Path, long LastIndexed);
```

Delete `src/NetSearch.Core/Models/Placeholder.cs`.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter "FullyQualifiedName~FileEntryTests"`
Expected: PASS (2 tests).

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(core): add FileEntry and RootPath models"
```

---

## Task 3: `IndexStore` (SQLite persistence + load)

**Files:**
- Create: `src/NetSearch.Core/Storage/IndexStore.cs`
- Create: `tests/NetSearch.Core.Tests/IndexStoreTests.cs`

**Interfaces:**
- Consumes: `FileEntry`, `RootPath`.
- Produces (`sealed class IndexStore : IDisposable`):
  - `IndexStore(string dbPath)` — opens connection, applies pragmas.
  - `void Initialize()` — creates schema (idempotent).
  - `int UpsertRoot(string path)` — returns root id (existing or new).
  - `void SetRootIndexed(int rootId, long unixTime)`
  - `IReadOnlyList<RootPath> GetRoots()`
  - `void BulkUpsert(IEnumerable<FileEntry> entries)` — insert-or-update on `(root_id,parent_path,name)`.
  - `void RemoveByIds(IEnumerable<long> ids)`
  - `IReadOnlyList<FileEntry> LoadAll()` — entries across all roots, with `Id` populated.
  - `IReadOnlyList<FileEntry> LoadByRoot(int rootId)`
  - `void DeleteEntriesForRoot(int rootId)`

- [ ] **Step 1: Write the failing test**

`tests/NetSearch.Core.Tests/IndexStoreTests.cs`:
```csharp
using NetSearch.Core.Models;
using NetSearch.Core.Storage;

namespace NetSearch.Core.Tests;

public class IndexStoreTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"netsearch_{Guid.NewGuid():N}.db");

    private IndexStore NewStore()
    {
        var s = new IndexStore(_dbPath);
        s.Initialize();
        return s;
    }

    private static FileEntry Entry(int rootId, string fullPath, bool isDir = false, long size = 10, long mod = 100)
        => FileEntry.FromFileSystem(rootId, fullPath, isDir, size, mod);

    [Fact]
    public void UpsertRoot_is_idempotent_and_returns_stable_id()
    {
        using var s = NewStore();
        var id1 = s.UpsertRoot(@"\\srv\share");
        var id2 = s.UpsertRoot(@"\\srv\share");
        Assert.Equal(id1, id2);
        Assert.Single(s.GetRoots());
    }

    [Fact]
    public void BulkUpsert_then_LoadAll_roundtrips_entries()
    {
        using var s = NewStore();
        var root = s.UpsertRoot(@"C:\Data");
        s.BulkUpsert(new[]
        {
            Entry(root, @"C:\Data\a.txt", size: 5, mod: 111),
            Entry(root, @"C:\Data\sub", isDir: true, size: 0, mod: 222),
        });

        var all = s.LoadAll().OrderBy(e => e.Name).ToList();
        Assert.Equal(2, all.Count);
        Assert.Equal("a.txt", all[0].Name);
        Assert.True(all[0].Id > 0);
        Assert.Equal(5, all[0].Size);
        Assert.True(all[1].IsDir);
    }

    [Fact]
    public void BulkUpsert_updates_existing_entry_on_conflict()
    {
        using var s = NewStore();
        var root = s.UpsertRoot(@"C:\Data");
        s.BulkUpsert(new[] { Entry(root, @"C:\Data\a.txt", size: 5, mod: 111) });
        s.BulkUpsert(new[] { Entry(root, @"C:\Data\a.txt", size: 99, mod: 222) });

        var all = s.LoadAll();
        Assert.Single(all);
        Assert.Equal(99, all[0].Size);
        Assert.Equal(222, all[0].Modified);
    }

    [Fact]
    public void RemoveByIds_deletes_only_targeted_rows()
    {
        using var s = NewStore();
        var root = s.UpsertRoot(@"C:\Data");
        s.BulkUpsert(new[] { Entry(root, @"C:\Data\a.txt"), Entry(root, @"C:\Data\b.txt") });
        var all = s.LoadAll();
        var idA = all.First(e => e.Name == "a.txt").Id;

        s.RemoveByIds(new[] { idA });

        var remaining = s.LoadAll();
        Assert.Single(remaining);
        Assert.Equal("b.txt", remaining[0].Name);
    }

    [Fact]
    public void Data_survives_reopening_the_same_db_file()
    {
        var root = 0;
        using (var s = NewStore())
        {
            root = s.UpsertRoot(@"C:\Data");
            s.BulkUpsert(new[] { Entry(root, @"C:\Data\a.txt") });
        }
        using var s2 = new IndexStore(_dbPath);
        s2.Initialize();
        Assert.Single(s2.LoadAll());
    }

    public void Dispose()
    {
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
        foreach (var suffix in new[] { "-wal", "-shm" })
            if (File.Exists(_dbPath + suffix)) File.Delete(_dbPath + suffix);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~IndexStoreTests"`
Expected: FAIL (compile error — `IndexStore` does not exist).

- [ ] **Step 3: Implement `IndexStore`**

`src/NetSearch.Core/Storage/IndexStore.cs`:
```csharp
using Microsoft.Data.Sqlite;
using NetSearch.Core.Models;

namespace NetSearch.Core.Storage;

public sealed class IndexStore : IDisposable
{
    private readonly SqliteConnection _conn;

    public IndexStore(string dbPath)
    {
        _conn = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Private,
        }.ToString());
        _conn.Open();
        Exec("PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL;");
    }

    public void Initialize()
    {
        Exec("""
            CREATE TABLE IF NOT EXISTS roots (
              id INTEGER PRIMARY KEY AUTOINCREMENT,
              path TEXT NOT NULL UNIQUE,
              last_indexed INTEGER NOT NULL DEFAULT 0
            );
            CREATE TABLE IF NOT EXISTS entries (
              id INTEGER PRIMARY KEY AUTOINCREMENT,
              root_id INTEGER NOT NULL,
              name TEXT NOT NULL,
              name_lower TEXT NOT NULL,
              parent_path TEXT NOT NULL,
              is_dir INTEGER NOT NULL,
              size INTEGER NOT NULL,
              ext TEXT NOT NULL,
              modified INTEGER NOT NULL,
              UNIQUE(root_id, parent_path, name)
            );
            CREATE INDEX IF NOT EXISTS idx_entries_name_lower ON entries(name_lower);
            CREATE INDEX IF NOT EXISTS idx_entries_ext ON entries(ext);
            CREATE INDEX IF NOT EXISTS idx_entries_modified ON entries(modified);
            CREATE INDEX IF NOT EXISTS idx_entries_size ON entries(size);
            """);
    }

    public int UpsertRoot(string path)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "INSERT INTO roots(path) VALUES($p) ON CONFLICT(path) DO NOTHING;";
        cmd.Parameters.AddWithValue("$p", path);
        cmd.ExecuteNonQuery();

        using var sel = _conn.CreateCommand();
        sel.CommandText = "SELECT id FROM roots WHERE path=$p;";
        sel.Parameters.AddWithValue("$p", path);
        return Convert.ToInt32(sel.ExecuteScalar());
    }

    public void SetRootIndexed(int rootId, long unixTime)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "UPDATE roots SET last_indexed=$t WHERE id=$id;";
        cmd.Parameters.AddWithValue("$t", unixTime);
        cmd.Parameters.AddWithValue("$id", rootId);
        cmd.ExecuteNonQuery();
    }

    public IReadOnlyList<RootPath> GetRoots()
    {
        var list = new List<RootPath>();
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT id, path, last_indexed FROM roots ORDER BY id;";
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new RootPath(r.GetInt32(0), r.GetString(1), r.GetInt64(2)));
        return list;
    }

    public void BulkUpsert(IEnumerable<FileEntry> entries)
    {
        using var tx = _conn.BeginTransaction();
        using var cmd = _conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO entries(root_id,name,name_lower,parent_path,is_dir,size,ext,modified)
            VALUES($root,$name,$namel,$parent,$isdir,$size,$ext,$mod)
            ON CONFLICT(root_id,parent_path,name) DO UPDATE SET
              is_dir=excluded.is_dir, size=excluded.size,
              ext=excluded.ext, modified=excluded.modified;
            """;
        var pRoot = cmd.Parameters.Add("$root", SqliteType.Integer);
        var pName = cmd.Parameters.Add("$name", SqliteType.Text);
        var pNameL = cmd.Parameters.Add("$namel", SqliteType.Text);
        var pParent = cmd.Parameters.Add("$parent", SqliteType.Text);
        var pIsDir = cmd.Parameters.Add("$isdir", SqliteType.Integer);
        var pSize = cmd.Parameters.Add("$size", SqliteType.Integer);
        var pExt = cmd.Parameters.Add("$ext", SqliteType.Text);
        var pMod = cmd.Parameters.Add("$mod", SqliteType.Integer);

        foreach (var e in entries)
        {
            pRoot.Value = e.RootId;
            pName.Value = e.Name;
            pNameL.Value = e.NameLower;
            pParent.Value = e.ParentPath;
            pIsDir.Value = e.IsDir ? 1 : 0;
            pSize.Value = e.Size;
            pExt.Value = e.Ext;
            pMod.Value = e.Modified;
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
    }

    public void RemoveByIds(IEnumerable<long> ids)
    {
        using var tx = _conn.BeginTransaction();
        using var cmd = _conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "DELETE FROM entries WHERE id=$id;";
        var p = cmd.Parameters.Add("$id", SqliteType.Integer);
        foreach (var id in ids)
        {
            p.Value = id;
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
    }

    public void DeleteEntriesForRoot(int rootId)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "DELETE FROM entries WHERE root_id=$r;";
        cmd.Parameters.AddWithValue("$r", rootId);
        cmd.ExecuteNonQuery();
    }

    public IReadOnlyList<FileEntry> LoadAll() => Load(null);
    public IReadOnlyList<FileEntry> LoadByRoot(int rootId) => Load(rootId);

    private IReadOnlyList<FileEntry> Load(int? rootId)
    {
        var list = new List<FileEntry>();
        using var cmd = _conn.CreateCommand();
        cmd.CommandText =
            "SELECT id,root_id,name,name_lower,parent_path,is_dir,size,ext,modified FROM entries"
            + (rootId is null ? ";" : " WHERE root_id=$r;");
        if (rootId is not null) cmd.Parameters.AddWithValue("$r", rootId.Value);
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(new FileEntry
            {
                Id = r.GetInt64(0),
                RootId = r.GetInt32(1),
                Name = r.GetString(2),
                NameLower = r.GetString(3),
                ParentPath = r.GetString(4),
                IsDir = r.GetInt64(5) != 0,
                Size = r.GetInt64(6),
                Ext = r.GetString(7),
                Modified = r.GetInt64(8),
            });
        }
        return list;
    }

    private void Exec(string sql)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    public void Dispose()
    {
        _conn.Close();
        _conn.Dispose();
        SqliteConnection.ClearAllPools();
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter "FullyQualifiedName~IndexStoreTests"`
Expected: PASS (5 tests).

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(core): add IndexStore SQLite persistence"
```

---

## Task 4: `Crawler` (recursive enumeration, error-skipping, batches)

**Files:**
- Create: `src/NetSearch.Core/Indexing/Crawler.cs`
- Create: `tests/NetSearch.Core.Tests/CrawlerTests.cs`

**Interfaces:**
- Consumes: `FileEntry`.
- Produces:
  - `sealed record CrawlResult(int Count, IReadOnlyList<string> Skipped)`
  - `sealed class Crawler`
    - `Crawler(int batchSize = 1000)`
    - `CrawlResult Crawl(int rootId, string rootPath, Action<IReadOnlyList<FileEntry>> onBatch, CancellationToken ct)`
  - Inaccessible directories are recorded in `Skipped` and traversal continues; both files and directories are emitted as `FileEntry`. Timestamps come from `File.GetLastWriteTimeUtc` as Unix seconds.

> Note: v1 crawls sequentially for correctness and testability. The settings expose a `CrawlParallelism` value reserved for a future parallel traversal; it is intentionally unused here (do not block this task on it).

- [ ] **Step 1: Write the failing test**

`tests/NetSearch.Core.Tests/CrawlerTests.cs`:
```csharp
using NetSearch.Core.Indexing;
using NetSearch.Core.Models;

namespace NetSearch.Core.Tests;

public class CrawlerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"crawl_{Guid.NewGuid():N}");

    public CrawlerTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "sub"));
        File.WriteAllText(Path.Combine(_root, "a.txt"), "hello");
        File.WriteAllText(Path.Combine(_root, "sub", "b.log"), "world!!");
    }

    private List<FileEntry> CrawlAll(out CrawlResult result)
    {
        var collected = new List<FileEntry>();
        var crawler = new Crawler(batchSize: 1);
        result = crawler.Crawl(1, _root, batch => collected.AddRange(batch), CancellationToken.None);
        return collected;
    }

    [Fact]
    public void Crawl_emits_all_files_and_directories()
    {
        var all = CrawlAll(out var result);
        var names = all.Select(e => e.Name).OrderBy(n => n).ToList();
        Assert.Contains("a.txt", names);
        Assert.Contains("b.log", names);
        Assert.Contains("sub", names);
        Assert.Equal(all.Count, result.Count);
    }

    [Fact]
    public void Crawl_sets_size_and_is_dir_correctly()
    {
        var all = CrawlAll(out _);
        var file = all.First(e => e.Name == "a.txt");
        var dir = all.First(e => e.Name == "sub");
        Assert.False(file.IsDir);
        Assert.Equal(5, file.Size); // "hello"
        Assert.True(dir.IsDir);
    }

    [Fact]
    public void Crawl_of_missing_root_records_it_as_skipped()
    {
        var crawler = new Crawler();
        var missing = Path.Combine(_root, "does-not-exist");
        var result = crawler.Crawl(1, missing, _ => { }, CancellationToken.None);
        Assert.Contains(missing, result.Skipped);
        Assert.Equal(0, result.Count);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~CrawlerTests"`
Expected: FAIL (compile error — `Crawler` does not exist).

- [ ] **Step 3: Implement `Crawler`**

`src/NetSearch.Core/Indexing/Crawler.cs`:
```csharp
using NetSearch.Core.Models;

namespace NetSearch.Core.Indexing;

public sealed record CrawlResult(int Count, IReadOnlyList<string> Skipped);

public sealed class Crawler
{
    private readonly int _batchSize;

    public Crawler(int batchSize = 1000) => _batchSize = Math.Max(1, batchSize);

    public CrawlResult Crawl(int rootId, string rootPath, Action<IReadOnlyList<FileEntry>> onBatch, CancellationToken ct)
    {
        var skipped = new List<string>();
        var batch = new List<FileEntry>(_batchSize);
        var count = 0;

        void Flush()
        {
            if (batch.Count == 0) return;
            onBatch(batch);
            batch = new List<FileEntry>(_batchSize);
        }

        void Add(FileEntry e)
        {
            batch.Add(e);
            count++;
            if (batch.Count >= _batchSize) Flush();
        }

        void Recurse(string dir)
        {
            ct.ThrowIfCancellationRequested();
            IEnumerable<string> children;
            try
            {
                children = Directory.EnumerateFileSystemEntries(dir);
            }
            catch (Exception)
            {
                skipped.Add(dir);
                return;
            }

            foreach (var path in children)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var info = new FileInfo(path);
                    var isDir = (info.Attributes & FileAttributes.Directory) != 0;
                    var mod = new DateTimeOffset(File.GetLastWriteTimeUtc(path)).ToUnixTimeSeconds();
                    var size = isDir ? 0 : info.Length;
                    Add(FileEntry.FromFileSystem(rootId, path, isDir, size, mod));
                    if (isDir) Recurse(path);
                }
                catch (Exception)
                {
                    skipped.Add(path);
                }
            }
        }

        if (!Directory.Exists(rootPath))
        {
            skipped.Add(rootPath);
            return new CrawlResult(0, skipped);
        }

        Recurse(rootPath);
        Flush();
        return new CrawlResult(count, skipped);
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter "FullyQualifiedName~CrawlerTests"`
Expected: PASS (3 tests).

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(core): add Crawler for recursive enumeration"
```

---

## Task 5: `IndexManager` (full rebuild + incremental update)

**Files:**
- Create: `src/NetSearch.Core/Indexing/IndexManager.cs`
- Create: `tests/NetSearch.Core.Tests/IndexManagerTests.cs`

**Interfaces:**
- Consumes: `IndexStore`, `Crawler`, `FileEntry`.
- Produces:
  - `sealed record IndexResult(int Added, int Updated, int Removed, int Skipped)`
  - `sealed class IndexManager`
    - `IndexManager(IndexStore store, Func<Crawler> crawlerFactory)`
    - `IndexResult RebuildRoot(int rootId, string rootPath, CancellationToken ct)` — wipes the root then re-indexes; all entries counted as `Added`.
    - `IndexResult UpdateRoot(int rootId, string rootPath, CancellationToken ct)` — incremental: compares crawl against stored entries by `FullPath` (case-insensitive); new→Added, changed `Size`/`Modified`/`IsDir`→Updated, vanished→Removed.
  - Both call `store.SetRootIndexed(rootId, nowUnix)` using the timestamp passed in via a `Func<long>` clock (default `DateTimeOffset.UtcNow`).

> Note: `IndexManager` takes an optional `Func<long> clock` constructor parameter so tests pass a fixed time. Signature: `IndexManager(IndexStore store, Func<Crawler> crawlerFactory, Func<long>? clock = null)`.

- [ ] **Step 1: Write the failing test**

`tests/NetSearch.Core.Tests/IndexManagerTests.cs`:
```csharp
using NetSearch.Core.Indexing;
using NetSearch.Core.Storage;

namespace NetSearch.Core.Tests;

public class IndexManagerTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"nsidx_{Guid.NewGuid():N}.db");
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"nsroot_{Guid.NewGuid():N}");

    public IndexManagerTests() => Directory.CreateDirectory(_root);

    private IndexStore NewStore()
    {
        var s = new IndexStore(_dbPath);
        s.Initialize();
        return s;
    }

    [Fact]
    public void RebuildRoot_indexes_all_current_files()
    {
        File.WriteAllText(Path.Combine(_root, "a.txt"), "x");
        File.WriteAllText(Path.Combine(_root, "b.txt"), "y");
        using var store = NewStore();
        var id = store.UpsertRoot(_root);
        var mgr = new IndexManager(store, () => new Crawler(), () => 555);

        var result = mgr.RebuildRoot(id, _root, CancellationToken.None);

        Assert.Equal(2, result.Added);
        Assert.Equal(2, store.LoadAll().Count);
        Assert.Equal(555, store.GetRoots().Single().LastIndexed);
    }

    [Fact]
    public void UpdateRoot_detects_added_removed_and_changed()
    {
        var fa = Path.Combine(_root, "a.txt");
        var fb = Path.Combine(_root, "b.txt");
        File.WriteAllText(fa, "x");
        File.WriteAllText(fb, "y");

        using var store = NewStore();
        var id = store.UpsertRoot(_root);
        var mgr = new IndexManager(store, () => new Crawler(), () => 1);
        mgr.RebuildRoot(id, _root, CancellationToken.None);

        // Mutate the tree: delete b, add c, grow a.
        File.Delete(fb);
        File.WriteAllText(Path.Combine(_root, "c.txt"), "z");
        File.WriteAllText(fa, "xxxxxxxx");

        var result = mgr.UpdateRoot(id, _root, CancellationToken.None);

        Assert.Equal(1, result.Added);   // c.txt
        Assert.Equal(1, result.Removed); // b.txt
        Assert.True(result.Updated >= 1); // a.txt grew
        var names = store.LoadAll().Select(e => e.Name).OrderBy(n => n).ToList();
        Assert.Equal(new[] { "a.txt", "c.txt" }, names);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
        foreach (var s in new[] { "-wal", "-shm" })
            if (File.Exists(_dbPath + s)) File.Delete(_dbPath + s);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~IndexManagerTests"`
Expected: FAIL (compile error — `IndexManager` does not exist).

- [ ] **Step 3: Implement `IndexManager`**

`src/NetSearch.Core/Indexing/IndexManager.cs`:
```csharp
using NetSearch.Core.Models;
using NetSearch.Core.Storage;

namespace NetSearch.Core.Indexing;

public sealed record IndexResult(int Added, int Updated, int Removed, int Skipped);

public sealed class IndexManager
{
    private readonly IndexStore _store;
    private readonly Func<Crawler> _crawlerFactory;
    private readonly Func<long> _clock;

    public IndexManager(IndexStore store, Func<Crawler> crawlerFactory, Func<long>? clock = null)
    {
        _store = store;
        _crawlerFactory = crawlerFactory;
        _clock = clock ?? (() => DateTimeOffset.UtcNow.ToUnixTimeSeconds());
    }

    public IndexResult RebuildRoot(int rootId, string rootPath, CancellationToken ct)
    {
        _store.DeleteEntriesForRoot(rootId);
        var added = 0;
        var crawl = _crawlerFactory().Crawl(rootId, rootPath, batch =>
        {
            _store.BulkUpsert(batch);
            added += batch.Count;
        }, ct);
        _store.SetRootIndexed(rootId, _clock());
        return new IndexResult(added, 0, 0, crawl.Skipped.Count);
    }

    public IndexResult UpdateRoot(int rootId, string rootPath, CancellationToken ct)
    {
        var existing = _store.LoadByRoot(rootId)
            .ToDictionary(e => e.FullPath, StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var added = 0;
        var updated = 0;

        var crawl = _crawlerFactory().Crawl(rootId, rootPath, batch =>
        {
            foreach (var e in batch)
            {
                seen.Add(e.FullPath);
                if (!existing.TryGetValue(e.FullPath, out var old))
                    added++;
                else if (old.Size != e.Size || old.Modified != e.Modified || old.IsDir != e.IsDir)
                    updated++;
            }
            _store.BulkUpsert(batch);
        }, ct);

        var removedIds = existing
            .Where(kv => !seen.Contains(kv.Key))
            .Select(kv => kv.Value.Id)
            .ToList();
        _store.RemoveByIds(removedIds);

        _store.SetRootIndexed(rootId, _clock());
        return new IndexResult(added, updated, removedIds.Count, crawl.Skipped.Count);
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter "FullyQualifiedName~IndexManagerTests"`
Expected: PASS (2 tests).

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(core): add IndexManager full and incremental indexing"
```

---

## Task 6: `SearchQuery` + `SearchEngine` (name matching + filters)

**Files:**
- Create: `src/NetSearch.Core/Search/SearchQuery.cs`
- Create: `src/NetSearch.Core/Search/SearchEngine.cs`
- Create: `tests/NetSearch.Core.Tests/SearchEngineTests.cs`

**Interfaces:**
- Consumes: `FileEntry`.
- Produces:
  - `enum SearchMode { Substring, Wildcard, Regex }`
  - `enum EntryKind { All, FilesOnly, FoldersOnly }`
  - `sealed record SearchQuery { string Text=""; SearchMode Mode=Substring; long? MinSize; long? MaxSize; long? ModifiedAfterUnix; long? ModifiedBeforeUnix; IReadOnlyList<string> Extensions=[]; EntryKind Kind=All; }`
  - `static class SearchEngine { IReadOnlyList<FileEntry> Search(IReadOnlyList<FileEntry> source, SearchQuery query); }`
  - Empty `Text` matches everything (filters still apply). Wildcard supports `*` and `?`. Regex uses `RegexOptions.IgnoreCase` with a 1s timeout; an invalid regex yields an empty result (no throw).

- [ ] **Step 1: Write the failing test**

`tests/NetSearch.Core.Tests/SearchEngineTests.cs`:
```csharp
using NetSearch.Core.Models;
using NetSearch.Core.Search;

namespace NetSearch.Core.Tests;

public class SearchEngineTests
{
    private static FileEntry E(string fullPath, bool isDir = false, long size = 100, long mod = 1000)
        => FileEntry.FromFileSystem(1, fullPath, isDir, size, mod);

    private readonly IReadOnlyList<FileEntry> _data = new[]
    {
        E(@"C:\d\Report.pdf", size: 500, mod: 2000),
        E(@"C:\d\report_draft.docx", size: 50, mod: 1000),
        E(@"C:\d\photo.PNG", size: 9000, mod: 3000),
        E(@"C:\d\Archive", isDir: true, size: 0, mod: 1500),
    };

    [Fact]
    public void Substring_is_case_insensitive()
    {
        var r = SearchEngine.Search(_data, new SearchQuery { Text = "report" });
        Assert.Equal(2, r.Count);
    }

    [Fact]
    public void Empty_text_returns_all()
    {
        var r = SearchEngine.Search(_data, new SearchQuery { Text = "" });
        Assert.Equal(4, r.Count);
    }

    [Fact]
    public void Wildcard_matches_extension_pattern()
    {
        var r = SearchEngine.Search(_data, new SearchQuery { Text = "*.pdf", Mode = SearchMode.Wildcard });
        Assert.Single(r);
        Assert.Equal("Report.pdf", r[0].Name);
    }

    [Fact]
    public void Regex_matches_and_invalid_regex_returns_empty()
    {
        var ok = SearchEngine.Search(_data, new SearchQuery { Text = @"^report", Mode = SearchMode.Regex });
        Assert.Equal(2, ok.Count);
        var bad = SearchEngine.Search(_data, new SearchQuery { Text = "(", Mode = SearchMode.Regex });
        Assert.Empty(bad);
    }

    [Fact]
    public void Size_filter_restricts_results()
    {
        var r = SearchEngine.Search(_data, new SearchQuery { MinSize = 1000 });
        Assert.Single(r);
        Assert.Equal("photo.PNG", r[0].Name);
    }

    [Fact]
    public void Date_filter_restricts_results()
    {
        var r = SearchEngine.Search(_data, new SearchQuery { ModifiedAfterUnix = 1800 });
        Assert.Equal(2, r.Count); // pdf(2000), png(3000)
    }

    [Fact]
    public void Extension_filter_is_case_insensitive()
    {
        var r = SearchEngine.Search(_data, new SearchQuery { Extensions = new[] { "png" } });
        Assert.Single(r);
        Assert.Equal("photo.PNG", r[0].Name);
    }

    [Fact]
    public void Kind_filter_selects_folders_only()
    {
        var r = SearchEngine.Search(_data, new SearchQuery { Kind = EntryKind.FoldersOnly });
        Assert.Single(r);
        Assert.True(r[0].IsDir);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~SearchEngineTests"`
Expected: FAIL (compile error — `SearchEngine` / `SearchQuery` do not exist).

- [ ] **Step 3: Implement `SearchQuery` and `SearchEngine`**

`src/NetSearch.Core/Search/SearchQuery.cs`:
```csharp
namespace NetSearch.Core.Search;

public enum SearchMode { Substring, Wildcard, Regex }
public enum EntryKind { All, FilesOnly, FoldersOnly }

public sealed record SearchQuery
{
    public string Text { get; init; } = "";
    public SearchMode Mode { get; init; } = SearchMode.Substring;
    public long? MinSize { get; init; }
    public long? MaxSize { get; init; }
    public long? ModifiedAfterUnix { get; init; }
    public long? ModifiedBeforeUnix { get; init; }
    public IReadOnlyList<string> Extensions { get; init; } = Array.Empty<string>();
    public EntryKind Kind { get; init; } = EntryKind.All;
}
```

`src/NetSearch.Core/Search/SearchEngine.cs`:
```csharp
using System.Text.RegularExpressions;
using NetSearch.Core.Models;

namespace NetSearch.Core.Search;

public static class SearchEngine
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);

    public static IReadOnlyList<FileEntry> Search(IReadOnlyList<FileEntry> source, SearchQuery query)
    {
        Func<FileEntry, bool> nameMatch;
        try
        {
            nameMatch = BuildNameMatcher(query);
        }
        catch (ArgumentException)
        {
            return Array.Empty<FileEntry>(); // invalid regex
        }

        var exts = new HashSet<string>(
            query.Extensions.Select(x => x.Trim().TrimStart('.').ToLowerInvariant())
                            .Where(x => x.Length > 0),
            StringComparer.OrdinalIgnoreCase);

        var result = new List<FileEntry>();
        foreach (var e in source)
        {
            if (query.Kind == EntryKind.FilesOnly && e.IsDir) continue;
            if (query.Kind == EntryKind.FoldersOnly && !e.IsDir) continue;
            if (query.MinSize is { } min && e.Size < min) continue;
            if (query.MaxSize is { } max && e.Size > max) continue;
            if (query.ModifiedAfterUnix is { } after && e.Modified < after) continue;
            if (query.ModifiedBeforeUnix is { } before && e.Modified > before) continue;
            if (exts.Count > 0 && !exts.Contains(e.Ext)) continue;
            if (!nameMatch(e)) continue;
            result.Add(e);
        }
        return result;
    }

    private static Func<FileEntry, bool> BuildNameMatcher(SearchQuery query)
    {
        if (string.IsNullOrEmpty(query.Text))
            return _ => true;

        switch (query.Mode)
        {
            case SearchMode.Substring:
                var needle = query.Text.ToLowerInvariant();
                return e => e.NameLower.Contains(needle);

            case SearchMode.Wildcard:
                var wild = "^" + Regex.Escape(query.Text)
                    .Replace("\\*", ".*").Replace("\\?", ".") + "$";
                var wre = new Regex(wild, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexTimeout);
                return e => SafeMatch(wre, e.Name);

            case SearchMode.Regex:
                var re = new Regex(query.Text, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexTimeout);
                return e => SafeMatch(re, e.Name);

            default:
                return _ => true;
        }
    }

    private static bool SafeMatch(Regex re, string input)
    {
        try { return re.IsMatch(input); }
        catch (RegexMatchTimeoutException) { return false; }
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter "FullyQualifiedName~SearchEngineTests"`
Expected: PASS (8 tests).

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(core): add SearchEngine name matching and filters"
```

---

## Task 7: `ContentSearcher` (text-in-file over a selection)

**Files:**
- Create: `src/NetSearch.Core/Search/ContentSearcher.cs`
- Create: `tests/NetSearch.Core.Tests/ContentSearcherTests.cs`

**Interfaces:**
- Consumes: `FileEntry`.
- Produces:
  - `sealed record ContentSearchOptions(long MaxFileBytes, IReadOnlyList<string> TextExtensions, int MaxParallelism)`
  - `sealed record ContentMatch(FileEntry Entry, int LineNumber, string LineText)`
  - `sealed class ContentSearcher`
    - `ContentSearcher(ContentSearchOptions options)`
    - `Task<IReadOnlyList<ContentMatch>> SearchAsync(IReadOnlyList<FileEntry> entries, string text, bool useRegex, IProgress<int>? progress, CancellationToken ct)`
  - Skips directories, files over `MaxFileBytes`, and files whose `Ext` is not in `TextExtensions` (when that list is non-empty). Reports first match line per file. `progress` reports number of files scanned.

- [ ] **Step 1: Write the failing test**

`tests/NetSearch.Core.Tests/ContentSearcherTests.cs`:
```csharp
using NetSearch.Core.Models;
using NetSearch.Core.Search;

namespace NetSearch.Core.Tests;

public class ContentSearcherTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"content_{Guid.NewGuid():N}");

    public ContentSearcherTests() => Directory.CreateDirectory(_dir);

    private FileEntry Write(string name, string content)
    {
        var p = Path.Combine(_dir, name);
        File.WriteAllText(p, content);
        return FileEntry.FromFileSystem(1, p, false, new FileInfo(p).Length, 0);
    }

    [Fact]
    public async Task Finds_substring_and_reports_line_number()
    {
        var f = Write("a.txt", "first line\nhas needle here\nlast");
        var searcher = new ContentSearcher(new ContentSearchOptions(1_000_000, new[] { "txt" }, 2));

        var matches = await searcher.SearchAsync(new[] { f }, "needle", useRegex: false, null, CancellationToken.None);

        Assert.Single(matches);
        Assert.Equal(2, matches[0].LineNumber);
        Assert.Contains("needle", matches[0].LineText);
    }

    [Fact]
    public async Task Skips_files_over_size_limit_and_wrong_extension()
    {
        var big = Write("big.txt", new string('x', 50) + "needle");
        var bin = Write("c.bin", "needle");
        var searcher = new ContentSearcher(new ContentSearchOptions(MaxFileBytes: 10, new[] { "txt" }, 2));

        var matches = await searcher.SearchAsync(new[] { big, bin }, "needle", false, null, CancellationToken.None);

        Assert.Empty(matches); // big over size; bin wrong ext
    }

    [Fact]
    public async Task Reports_progress_for_each_candidate()
    {
        var f1 = Write("a.txt", "needle");
        var f2 = Write("b.txt", "nothing");
        var seen = 0;
        var progress = new Progress<int>(_ => Interlocked.Increment(ref seen));
        var searcher = new ContentSearcher(new ContentSearchOptions(1_000_000, new[] { "txt" }, 1));

        var matches = await searcher.SearchAsync(new[] { f1, f2 }, "needle", false, progress, CancellationToken.None);

        Assert.Single(matches);
        // Progress is asynchronous; allow it to drain.
        await Task.Delay(50);
        Assert.True(seen >= 1);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~ContentSearcherTests"`
Expected: FAIL (compile error — `ContentSearcher` does not exist).

- [ ] **Step 3: Implement `ContentSearcher`**

`src/NetSearch.Core/Search/ContentSearcher.cs`:
```csharp
using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using NetSearch.Core.Models;

namespace NetSearch.Core.Search;

public sealed record ContentSearchOptions(long MaxFileBytes, IReadOnlyList<string> TextExtensions, int MaxParallelism);

public sealed record ContentMatch(FileEntry Entry, int LineNumber, string LineText);

public sealed class ContentSearcher
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);
    private readonly ContentSearchOptions _options;

    public ContentSearcher(ContentSearchOptions options) => _options = options;

    public async Task<IReadOnlyList<ContentMatch>> SearchAsync(
        IReadOnlyList<FileEntry> entries, string text, bool useRegex,
        IProgress<int>? progress, CancellationToken ct)
    {
        var allowedExt = new HashSet<string>(
            _options.TextExtensions.Select(x => x.Trim().TrimStart('.').ToLowerInvariant()),
            StringComparer.OrdinalIgnoreCase);

        var candidates = entries.Where(e =>
            !e.IsDir &&
            e.Size <= _options.MaxFileBytes &&
            (allowedExt.Count == 0 || allowedExt.Contains(e.Ext))).ToList();

        Regex? re = null;
        if (useRegex)
        {
            try { re = new Regex(text, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexTimeout); }
            catch (ArgumentException) { return Array.Empty<ContentMatch>(); }
        }

        var results = new ConcurrentBag<ContentMatch>();
        var scanned = 0;

        await Parallel.ForEachAsync(
            candidates,
            new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, _options.MaxParallelism), CancellationToken = ct },
            async (entry, token) =>
            {
                var match = await ScanFileAsync(entry, text, re, token).ConfigureAwait(false);
                if (match is not null) results.Add(match);
                progress?.Report(Interlocked.Increment(ref scanned));
            }).ConfigureAwait(false);

        return results.ToList();
    }

    private static async Task<ContentMatch?> ScanFileAsync(FileEntry entry, string text, Regex? re, CancellationToken ct)
    {
        try
        {
            using var reader = new StreamReader(entry.FullPath);
            var lineNo = 0;
            string? line;
            while ((line = await reader.ReadLineAsync(ct).ConfigureAwait(false)) is not null)
            {
                lineNo++;
                var hit = re is null
                    ? line.Contains(text, StringComparison.OrdinalIgnoreCase)
                    : SafeMatch(re, line);
                if (hit) return new ContentMatch(entry, lineNo, line.Trim());
            }
        }
        catch (Exception) { /* unreadable file: skip */ }
        return null;
    }

    private static bool SafeMatch(Regex re, string input)
    {
        try { return re.IsMatch(input); }
        catch (RegexMatchTimeoutException) { return false; }
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter "FullyQualifiedName~ContentSearcherTests"`
Expected: PASS (3 tests).

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(core): add ContentSearcher for text-in-file search"
```

---

## Task 8: `AppSettings` + `SettingsStore` (JSON persistence)

**Files:**
- Create: `src/NetSearch.Core/Settings/AppSettings.cs`
- Create: `tests/NetSearch.Core.Tests/SettingsStoreTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces:
  - `sealed class AppSettings { List<string> Roots; int AutoRefreshMinutes=0; int CrawlParallelism=2; long ContentMaxFileBytes=5_000_000; List<string> TextExtensions; }` with sensible default `TextExtensions`.
  - `static class SettingsStore { string DefaultDir; string DefaultSettingsPath; string DefaultDbPath; AppSettings Load(string path); void Save(string path, AppSettings settings); }`
  - `Load` returns defaults if the file is missing or unreadable; `Save` creates the parent directory.

- [ ] **Step 1: Write the failing test**

`tests/NetSearch.Core.Tests/SettingsStoreTests.cs`:
```csharp
using NetSearch.Core.Settings;

namespace NetSearch.Core.Tests;

public class SettingsStoreTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"nsset_{Guid.NewGuid():N}.json");

    [Fact]
    public void Load_missing_file_returns_defaults()
    {
        var s = SettingsStore.Load(_path);
        Assert.Empty(s.Roots);
        Assert.Equal(0, s.AutoRefreshMinutes);
        Assert.Contains("txt", s.TextExtensions);
    }

    [Fact]
    public void Save_then_Load_roundtrips()
    {
        var s = new AppSettings
        {
            Roots = new List<string> { @"\\srv\share", @"Z:\" },
            AutoRefreshMinutes = 30,
            ContentMaxFileBytes = 1234,
        };
        SettingsStore.Save(_path, s);

        var loaded = SettingsStore.Load(_path);
        Assert.Equal(2, loaded.Roots.Count);
        Assert.Equal(30, loaded.AutoRefreshMinutes);
        Assert.Equal(1234, loaded.ContentMaxFileBytes);
    }

    [Fact]
    public void DefaultDir_is_under_localappdata()
    {
        Assert.Contains("NetSearch", SettingsStore.DefaultDir);
    }

    public void Dispose()
    {
        if (File.Exists(_path)) File.Delete(_path);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~SettingsStoreTests"`
Expected: FAIL (compile error — `SettingsStore` does not exist).

- [ ] **Step 3: Implement settings**

`src/NetSearch.Core/Settings/AppSettings.cs`:
```csharp
using System.Text.Json;

namespace NetSearch.Core.Settings;

public sealed class AppSettings
{
    public List<string> Roots { get; set; } = new();
    public int AutoRefreshMinutes { get; set; } = 0; // 0 = disabled
    public int CrawlParallelism { get; set; } = 2;   // reserved for future parallel crawl
    public long ContentMaxFileBytes { get; set; } = 5_000_000;
    public List<string> TextExtensions { get; set; } = new()
    {
        "txt", "log", "csv", "md", "json", "xml", "html", "htm",
        "cs", "js", "ts", "py", "java", "sql", "ini", "cfg", "yml", "yaml",
    };
}

public static class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static string DefaultDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NetSearch");

    public static string DefaultSettingsPath => Path.Combine(DefaultDir, "settings.json");
    public static string DefaultDbPath => Path.Combine(DefaultDir, "index.db");

    public static AppSettings Load(string path)
    {
        try
        {
            if (!File.Exists(path)) return new AppSettings();
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
        }
        catch (Exception)
        {
            return new AppSettings();
        }
    }

    public static void Save(string path, AppSettings settings)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(path, JsonSerializer.Serialize(settings, JsonOptions));
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter "FullyQualifiedName~SettingsStoreTests"`
Expected: PASS (3 tests).

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(core): add AppSettings and JSON SettingsStore"
```

---

## Task 9: `MainViewModel` query-building logic (testable, UI-free)

**Files:**
- Create: `src/NetSearch.App/ViewModels/QueryBuilder.cs`
- Create: `tests/NetSearch.Core.Tests/QueryBuilderTests.cs`
- Modify: `tests/NetSearch.Core.Tests/NetSearch.Core.Tests.csproj` (add ProjectReference to `NetSearch.App` is NOT allowed because App is `net9.0-windows`; instead place `QueryBuilder` in Core).

> Decision: to keep this logic unit-testable from the `net9.0` test project, `QueryBuilder` lives in **`NetSearch.Core/Search/QueryBuilder.cs`**, not in the App. Update the Files list accordingly:
> - Create: `src/NetSearch.Core/Search/QueryBuilder.cs`
> - Create: `tests/NetSearch.Core.Tests/QueryBuilderTests.cs`

**Interfaces:**
- Consumes: `SearchQuery`, `SearchMode`, `EntryKind`.
- Produces:
  - `static class QueryBuilder`
    - `IReadOnlyList<string> ParseExtensions(string raw)` — splits on comma/space/semicolon, trims dots, lowercases, drops blanks.
    - `long? ParseSize(string raw)` — accepts plain bytes or `KB`/`MB`/`GB` suffixes (case-insensitive); returns null on blank/invalid.
    - `SearchQuery Build(string text, SearchMode mode, string minSize, string maxSize, DateTimeOffset? after, DateTimeOffset? before, string extensions, EntryKind kind)`

- [ ] **Step 1: Write the failing test**

`tests/NetSearch.Core.Tests/QueryBuilderTests.cs`:
```csharp
using NetSearch.Core.Search;

namespace NetSearch.Core.Tests;

public class QueryBuilderTests
{
    [Theory]
    [InlineData("pdf, docx; png", new[] { "pdf", "docx", "png" })]
    [InlineData(".PDF .Doc", new[] { "pdf", "doc" })]
    [InlineData("", new string[0])]
    public void ParseExtensions_normalizes(string raw, string[] expected)
    {
        Assert.Equal(expected, QueryBuilder.ParseExtensions(raw).ToArray());
    }

    [Theory]
    [InlineData("1024", 1024L)]
    [InlineData("1KB", 1024L)]
    [InlineData("2 MB", 2L * 1024 * 1024)]
    [InlineData("", null)]
    [InlineData("garbage", null)]
    public void ParseSize_handles_units(string raw, long? expected)
    {
        Assert.Equal(expected, QueryBuilder.ParseSize(raw));
    }

    [Fact]
    public void Build_assembles_a_complete_query()
    {
        var after = DateTimeOffset.FromUnixTimeSeconds(1000);
        var q = QueryBuilder.Build(
            text: "report", mode: SearchMode.Wildcard,
            minSize: "1KB", maxSize: "",
            after: after, before: null,
            extensions: "pdf, docx", kind: EntryKind.FilesOnly);

        Assert.Equal("report", q.Text);
        Assert.Equal(SearchMode.Wildcard, q.Mode);
        Assert.Equal(1024, q.MinSize);
        Assert.Null(q.MaxSize);
        Assert.Equal(1000, q.ModifiedAfterUnix);
        Assert.Equal(new[] { "pdf", "docx" }, q.Extensions.ToArray());
        Assert.Equal(EntryKind.FilesOnly, q.Kind);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~QueryBuilderTests"`
Expected: FAIL (compile error — `QueryBuilder` does not exist).

- [ ] **Step 3: Implement `QueryBuilder`**

`src/NetSearch.Core/Search/QueryBuilder.cs`:
```csharp
using System.Globalization;

namespace NetSearch.Core.Search;

public static class QueryBuilder
{
    private static readonly char[] Separators = { ',', ';', ' ', '\t' };

    public static IReadOnlyList<string> ParseExtensions(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return Array.Empty<string>();
        return raw.Split(Separators, StringSplitOptions.RemoveEmptyEntries)
                  .Select(x => x.Trim().TrimStart('.').ToLowerInvariant())
                  .Where(x => x.Length > 0)
                  .ToList();
    }

    public static long? ParseSize(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var s = raw.Trim().ToUpperInvariant();
        long mult = 1;
        foreach (var (suffix, m) in new[] { ("GB", 1024L * 1024 * 1024), ("MB", 1024L * 1024), ("KB", 1024L), ("B", 1L) })
        {
            if (s.EndsWith(suffix))
            {
                mult = m;
                s = s[..^suffix.Length].Trim();
                break;
            }
        }
        return long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)
            ? n * mult
            : null;
    }

    public static SearchQuery Build(
        string text, SearchMode mode,
        string minSize, string maxSize,
        DateTimeOffset? after, DateTimeOffset? before,
        string extensions, EntryKind kind)
    {
        return new SearchQuery
        {
            Text = text ?? "",
            Mode = mode,
            MinSize = ParseSize(minSize),
            MaxSize = ParseSize(maxSize),
            ModifiedAfterUnix = after?.ToUnixTimeSeconds(),
            ModifiedBeforeUnix = before?.ToUnixTimeSeconds(),
            Extensions = ParseExtensions(extensions),
            Kind = kind,
        };
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter "FullyQualifiedName~QueryBuilderTests"`
Expected: PASS (all theory cases + Build test).

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(core): add QueryBuilder for parsing UI inputs into SearchQuery"
```

---

## Task 10: WPF shell — `MainViewModel`, `App` bootstrap, `MainWindow` UI

**Files:**
- Create: `src/NetSearch.App/app.manifest`
- Create: `src/NetSearch.App/ViewModels/FileRow.cs`
- Create: `src/NetSearch.App/ViewModels/MainViewModel.cs`
- Create: `src/NetSearch.App/App.xaml`
- Create: `src/NetSearch.App/App.xaml.cs`
- Create: `src/NetSearch.App/MainWindow.xaml`
- Create: `src/NetSearch.App/MainWindow.xaml.cs`

**Interfaces:**
- Consumes: `IndexStore`, `IndexManager`, `Crawler`, `SearchEngine`, `ContentSearcher`, `QueryBuilder`, `AppSettings`, `SettingsStore`.
- Produces: a runnable WPF window. `MainViewModel` exposes:
  - bindable `SearchText`, `SelectedMode`, `MinSize`, `MaxSize`, `Extensions`, `SelectedKind`, `StatusText`, `IsBusy`
  - `ObservableCollection<FileRow> Results`
  - commands `RefreshCommand`, `ContentSearchCommand`, `OpenFileCommand`, `OpenFolderCommand`, `CopyPathCommand`
  - it loads all entries from `IndexStore` into an in-memory `List<FileEntry>` and re-runs `SearchEngine.Search` (debounced) on a background thread.

> This task is UI-heavy and verified by **building + manual smoke run**, not unit tests (the testable logic was extracted into Core in Tasks 6–9).

- [ ] **Step 1: Add the app manifest (long-path support)**

`src/NetSearch.App/app.manifest`:
```xml
<?xml version="1.0" encoding="utf-8"?>
<assembly manifestVersion="1.0" xmlns="urn:schemas-microsoft-com:asm.v1">
  <application xmlns="urn:schemas-microsoft-com:asm.v3">
    <windowsSettings>
      <longPathAware xmlns="http://schemas.microsoft.com/SMI/2016/WindowsSettings">true</longPathAware>
    </windowsSettings>
  </application>
</assembly>
```

- [ ] **Step 2: Add the display row wrapper**

`src/NetSearch.App/ViewModels/FileRow.cs`:
```csharp
using NetSearch.Core.Models;

namespace NetSearch.App.ViewModels;

public sealed class FileRow
{
    public FileRow(FileEntry entry) => Entry = entry;

    public FileEntry Entry { get; }
    public string Name => Entry.Name;
    public string Path => Entry.FullPath;
    public string SizeText => Entry.IsDir ? "" : FormatSize(Entry.Size);
    public string Modified =>
        DateTimeOffset.FromUnixTimeSeconds(Entry.Modified).LocalDateTime.ToString("yyyy-MM-dd HH:mm");
    public string Type => Entry.IsDir ? "Папка" : (Entry.Ext.Length > 0 ? Entry.Ext.ToUpperInvariant() : "Файл");

    private static string FormatSize(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double v = bytes;
        var i = 0;
        while (v >= 1024 && i < units.Length - 1) { v /= 1024; i++; }
        return $"{v:0.#} {units[i]}";
    }
}
```

- [ ] **Step 3: Add `MainViewModel`**

`src/NetSearch.App/ViewModels/MainViewModel.cs`:
```csharp
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NetSearch.Core.Indexing;
using NetSearch.Core.Models;
using NetSearch.Core.Search;
using NetSearch.Core.Settings;
using NetSearch.Core.Storage;

namespace NetSearch.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly IndexStore _store;
    private readonly AppSettings _settings;
    private readonly string _settingsPath;
    private List<FileEntry> _all = new();
    private readonly DispatcherTimer _debounce;
    private readonly DispatcherTimer _autoRefresh;

    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private SearchMode _selectedMode = SearchMode.Substring;
    [ObservableProperty] private string _minSize = "";
    [ObservableProperty] private string _maxSize = "";
    [ObservableProperty] private string _extensions = "";
    [ObservableProperty] private EntryKind _selectedKind = EntryKind.All;
    [ObservableProperty] private string _statusText = "Готово";
    [ObservableProperty] private bool _isBusy;

    public ObservableCollection<FileRow> Results { get; } = new();
    public FileRow? SelectedRow { get; set; }

    public Array Modes => Enum.GetValues(typeof(SearchMode));
    public Array Kinds => Enum.GetValues(typeof(EntryKind));

    public MainViewModel(IndexStore store, AppSettings settings, string settingsPath)
    {
        _store = store;
        _settings = settings;
        _settingsPath = settingsPath;

        _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        _debounce.Tick += (_, _) => { _debounce.Stop(); RunSearch(); };

        _autoRefresh = new DispatcherTimer();
        _autoRefresh.Tick += async (_, _) => await RefreshAsync();
        ConfigureAutoRefresh();

        LoadIndexIntoMemory();
    }

    public void ConfigureAutoRefresh()
    {
        _autoRefresh.Stop();
        if (_settings.AutoRefreshMinutes > 0)
        {
            _autoRefresh.Interval = TimeSpan.FromMinutes(_settings.AutoRefreshMinutes);
            _autoRefresh.Start();
        }
    }

    private void LoadIndexIntoMemory()
    {
        _all = _store.LoadAll().ToList();
        StatusText = $"Индекс: {_all.Count} записей";
        RunSearch();
    }

    partial void OnSearchTextChanged(string value) => Restart();
    partial void OnSelectedModeChanged(SearchMode value) => Restart();
    partial void OnMinSizeChanged(string value) => Restart();
    partial void OnMaxSizeChanged(string value) => Restart();
    partial void OnExtensionsChanged(string value) => Restart();
    partial void OnSelectedKindChanged(EntryKind value) => Restart();

    private void Restart() { _debounce.Stop(); _debounce.Start(); }

    private void RunSearch()
    {
        var query = QueryBuilder.Build(SearchText, SelectedMode, MinSize, MaxSize,
            after: null, before: null, Extensions, SelectedKind);
        var snapshot = _all;
        Task.Run(() => SearchEngine.Search(snapshot, query))
            .ContinueWith(t =>
            {
                Results.Clear();
                foreach (var e in t.Result.Take(50_000))
                    Results.Add(new FileRow(e));
                StatusText = $"Найдено {t.Result.Count} из {snapshot.Count}";
            }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        StatusText = "Индексирование…";
        try
        {
            await Task.Run(() =>
            {
                var mgr = new IndexManager(_store, () => new Crawler());
                foreach (var path in _settings.Roots)
                {
                    var id = _store.UpsertRoot(path);
                    mgr.UpdateRoot(id, path, CancellationToken.None);
                }
            });
            LoadIndexIntoMemory();
            StatusText = $"Обновлено в {DateTime.Now:HH:mm}. Записей: {_all.Count}";
        }
        catch (Exception ex)
        {
            StatusText = "Ошибка индексирования: " + ex.Message;
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task ContentSearchAsync()
    {
        if (IsBusy || string.IsNullOrEmpty(SearchText)) return;
        IsBusy = true;
        try
        {
            var current = Results.Select(r => r.Entry).ToList();
            var searcher = new ContentSearcher(new ContentSearchOptions(
                _settings.ContentMaxFileBytes, _settings.TextExtensions, _settings.CrawlParallelism));
            var progress = new Progress<int>(n => StatusText = $"Просмотрено файлов: {n}");
            var matches = await searcher.SearchAsync(current, SearchText,
                useRegex: SelectedMode == SearchMode.Regex, progress, CancellationToken.None);

            Results.Clear();
            foreach (var m in matches) Results.Add(new FileRow(m.Entry));
            StatusText = $"Совпадений по содержимому: {matches.Count}";
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private void OpenFile()
    {
        if (SelectedRow is null) return;
        TryStart(new ProcessStartInfo(SelectedRow.Path) { UseShellExecute = true });
    }

    [RelayCommand]
    private void OpenFolder()
    {
        if (SelectedRow is null) return;
        TryStart(new ProcessStartInfo("explorer.exe", $"/select,\"{SelectedRow.Path}\""));
    }

    [RelayCommand]
    private void CopyPath()
    {
        if (SelectedRow is null) return;
        Clipboard.SetText(SelectedRow.Path);
    }

    private void TryStart(ProcessStartInfo psi)
    {
        try { Process.Start(psi); }
        catch (Exception ex) { StatusText = "Не удалось открыть: " + ex.Message; }
    }
}
```

- [ ] **Step 4: Add `App.xaml` / `App.xaml.cs` bootstrap**

`src/NetSearch.App/App.xaml`:
```xml
<Application x:Class="NetSearch.App.App"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" />
```

`src/NetSearch.App/App.xaml.cs`:
```csharp
using System.IO;
using System.Windows;
using NetSearch.App.ViewModels;
using NetSearch.Core.Settings;
using NetSearch.Core.Storage;

namespace NetSearch.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Directory.CreateDirectory(SettingsStore.DefaultDir);

        var settings = SettingsStore.Load(SettingsStore.DefaultSettingsPath);
        var store = new IndexStore(SettingsStore.DefaultDbPath);
        store.Initialize();

        var vm = new MainViewModel(store, settings, SettingsStore.DefaultSettingsPath);
        var window = new MainWindow { DataContext = vm };
        window.Closed += (_, _) => store.Dispose();
        window.Show();
    }
}
```

> Remove the auto-generated `StartupUri` if a template added one; startup is handled in code above.

- [ ] **Step 5: Add `MainWindow.xaml` / code-behind**

`src/NetSearch.App/MainWindow.xaml`:
```xml
<Window x:Class="NetSearch.App.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="NetSearch" Height="650" Width="1000">
    <Grid Margin="8">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="*"/>
            <RowDefinition Height="Auto"/>
        </Grid.RowDefinitions>

        <!-- Search bar -->
        <DockPanel Grid.Row="0" LastChildFill="True" Margin="0,0,0,6">
            <ComboBox DockPanel.Dock="Left" Width="110" Margin="0,0,6,0"
                      ItemsSource="{Binding Modes}" SelectedItem="{Binding SelectedMode}"/>
            <Button DockPanel.Dock="Right" Content="Обновить" Width="90" Margin="6,0,0,0"
                    Command="{Binding RefreshCommand}" IsEnabled="{Binding IsBusy, Converter={x:Static local:NotConverter.Instance}}"/>
            <Button DockPanel.Dock="Right" Content="По содержимому" Width="120"
                    Command="{Binding ContentSearchCommand}"/>
            <TextBox Text="{Binding SearchText, UpdateSourceTrigger=PropertyChanged}"
                     VerticalContentAlignment="Center" FontSize="14"/>
        </DockPanel>

        <!-- Filters -->
        <WrapPanel Grid.Row="1" Margin="0,0,0,6">
            <TextBlock Text="Размер от:" VerticalAlignment="Center" Margin="0,0,4,0"/>
            <TextBox Text="{Binding MinSize, UpdateSourceTrigger=PropertyChanged}" Width="70" Margin="0,0,8,0"/>
            <TextBlock Text="до:" VerticalAlignment="Center" Margin="0,0,4,0"/>
            <TextBox Text="{Binding MaxSize, UpdateSourceTrigger=PropertyChanged}" Width="70" Margin="0,0,8,0"/>
            <TextBlock Text="Типы:" VerticalAlignment="Center" Margin="0,0,4,0"/>
            <TextBox Text="{Binding Extensions, UpdateSourceTrigger=PropertyChanged}" Width="160" Margin="0,0,8,0"/>
            <TextBlock Text="Что:" VerticalAlignment="Center" Margin="0,0,4,0"/>
            <ComboBox ItemsSource="{Binding Kinds}" SelectedItem="{Binding SelectedKind}" Width="120"/>
        </WrapPanel>

        <!-- Results -->
        <DataGrid Grid.Row="2" ItemsSource="{Binding Results}" AutoGenerateColumns="False"
                  IsReadOnly="True" SelectionMode="Single"
                  VirtualizingPanel.IsVirtualizing="True" VirtualizingPanel.VirtualizationMode="Recycling"
                  EnableRowVirtualization="True"
                  SelectedItem="{Binding SelectedRow, Mode=OneWayToSource}"
                  MouseDoubleClick="OnRowDoubleClick">
            <DataGrid.Columns>
                <DataGridTextColumn Header="Имя" Binding="{Binding Name}" Width="240"/>
                <DataGridTextColumn Header="Путь" Binding="{Binding Path}" Width="*"/>
                <DataGridTextColumn Header="Размер" Binding="{Binding SizeText}" Width="90"/>
                <DataGridTextColumn Header="Изменён" Binding="{Binding Modified}" Width="130"/>
                <DataGridTextColumn Header="Тип" Binding="{Binding Type}" Width="80"/>
            </DataGrid.Columns>
            <DataGrid.ContextMenu>
                <ContextMenu>
                    <MenuItem Header="Открыть" Command="{Binding OpenFileCommand}"/>
                    <MenuItem Header="Открыть папку" Command="{Binding OpenFolderCommand}"/>
                    <MenuItem Header="Копировать путь" Command="{Binding CopyPathCommand}"/>
                </ContextMenu>
            </DataGrid.ContextMenu>
        </DataGrid>

        <StatusBar Grid.Row="3">
            <TextBlock Text="{Binding StatusText}"/>
        </StatusBar>
    </Grid>
</Window>
```

> The `local:` namespace and `NotConverter` are added next; if you prefer, drop the `IsEnabled` binding on the Обновить button for the first compile and add it back after Step 6.

`src/NetSearch.App/MainWindow.xaml.cs`:
```csharp
using System.Windows;
using System.Windows.Controls;
using NetSearch.App.ViewModels;

namespace NetSearch.App;

public partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();

    private void OnRowDoubleClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm && vm.OpenFileCommand.CanExecute(null))
            vm.OpenFileCommand.Execute(null);
    }
}
```

- [ ] **Step 6: Add the `NotConverter` and wire `local:` namespace**

Add to `MainWindow.xaml` root element attributes:
```
xmlns:local="clr-namespace:NetSearch.App"
```

Create `src/NetSearch.App/NotConverter.cs`:
```csharp
using System.Globalization;
using System.Windows.Data;

namespace NetSearch.App;

public sealed class NotConverter : IValueConverter
{
    public static readonly NotConverter Instance = new();
    public object Convert(object value, Type t, object p, CultureInfo c) => !(bool)value;
    public object ConvertBack(object value, Type t, object p, CultureInfo c) => !(bool)value;
}
```

- [ ] **Step 7: Build and smoke-run**

Run: `dotnet build`
Expected: build succeeds with no errors.

Manual smoke test:
1. `dotnet run --project src/NetSearch.App` — the window opens, status reads "Индекс: 0 записей".
2. (Settings UI comes in Task 11; for now seed a root by editing `%LOCALAPPDATA%\NetSearch\settings.json` to add a local folder path, restart, click **Обновить**.)
3. Confirm files appear, typing in the search box filters live, right-click → **Открыть папку** works.

- [ ] **Step 8: Commit**

```bash
git add -A
git commit -m "feat(app): add MainViewModel and MainWindow WPF shell"
```

---

## Task 11: Settings window + auto-refresh wiring

**Files:**
- Create: `src/NetSearch.App/ViewModels/SettingsViewModel.cs`
- Create: `src/NetSearch.App/Views/SettingsWindow.xaml`
- Create: `src/NetSearch.App/Views/SettingsWindow.xaml.cs`
- Modify: `src/NetSearch.App/MainWindow.xaml` (add a "Настройки" button)
- Modify: `src/NetSearch.App/ViewModels/MainViewModel.cs` (add `OpenSettingsCommand`, re-apply settings on close)

**Interfaces:**
- Consumes: `AppSettings`, `SettingsStore`, `MainViewModel`.
- Produces: `SettingsViewModel` with bindable `RootsText` (newline-separated), `AutoRefreshMinutes`, `ContentMaxMB`, `TextExtensionsText`, and a `Save()` method that writes via `SettingsStore.Save` and mutates the shared `AppSettings`.

- [ ] **Step 1: Add `SettingsViewModel`**

`src/NetSearch.App/ViewModels/SettingsViewModel.cs`:
```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using NetSearch.Core.Settings;

namespace NetSearch.App.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly AppSettings _settings;
    private readonly string _path;

    [ObservableProperty] private string _rootsText;
    [ObservableProperty] private int _autoRefreshMinutes;
    [ObservableProperty] private int _contentMaxMB;
    [ObservableProperty] private string _textExtensionsText;

    public SettingsViewModel(AppSettings settings, string path)
    {
        _settings = settings;
        _path = path;
        _rootsText = string.Join(Environment.NewLine, settings.Roots);
        _autoRefreshMinutes = settings.AutoRefreshMinutes;
        _contentMaxMB = (int)(settings.ContentMaxFileBytes / (1024 * 1024));
        _textExtensionsText = string.Join(", ", settings.TextExtensions);
    }

    public void Save()
    {
        _settings.Roots = RootsText
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
        _settings.AutoRefreshMinutes = Math.Max(0, AutoRefreshMinutes);
        _settings.ContentMaxFileBytes = Math.Max(1, ContentMaxMB) * 1024L * 1024L;
        _settings.TextExtensions = TextExtensionsText
            .Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim().TrimStart('.').ToLowerInvariant()).Where(s => s.Length > 0).ToList();
        SettingsStore.Save(_path, _settings);
    }
}
```

- [ ] **Step 2: Add `SettingsWindow` XAML + code-behind**

`src/NetSearch.App/Views/SettingsWindow.xaml`:
```xml
<Window x:Class="NetSearch.App.Views.SettingsWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="Настройки" Height="430" Width="560">
    <Grid Margin="12">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="*"/>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="Auto"/>
        </Grid.RowDefinitions>
        <TextBlock Grid.Row="0" Text="Сетевые пути (по одному на строку):" Margin="0,0,0,4"/>
        <TextBox Grid.Row="1" Text="{Binding RootsText}" AcceptsReturn="True"
                 VerticalScrollBarVisibility="Auto" TextWrapping="NoWrap"/>
        <StackPanel Grid.Row="2" Orientation="Horizontal" Margin="0,8,0,0">
            <TextBlock Text="Авто-обновление, мин (0 = выкл):" VerticalAlignment="Center" Margin="0,0,6,0"/>
            <TextBox Text="{Binding AutoRefreshMinutes}" Width="60"/>
        </StackPanel>
        <StackPanel Grid.Row="3" Orientation="Horizontal" Margin="0,8,0,0">
            <TextBlock Text="Макс. размер файла для поиска по содержимому, МБ:" VerticalAlignment="Center" Margin="0,0,6,0"/>
            <TextBox Text="{Binding ContentMaxMB}" Width="60"/>
        </StackPanel>
        <StackPanel Grid.Row="4" Orientation="Vertical" Margin="0,8,0,0">
            <TextBlock Text="Текстовые расширения (через запятую):"/>
            <TextBox Text="{Binding TextExtensionsText}"/>
        </StackPanel>
        <StackPanel Grid.Row="5" Orientation="Horizontal" HorizontalAlignment="Right" Margin="0,12,0,0">
            <Button Content="Сохранить" Width="90" Margin="0,0,8,0" Click="OnSave"/>
            <Button Content="Отмена" Width="90" Click="OnCancel"/>
        </StackPanel>
    </Grid>
</Window>
```

`src/NetSearch.App/Views/SettingsWindow.xaml.cs`:
```csharp
using System.Windows;
using NetSearch.App.ViewModels;

namespace NetSearch.App.Views;

public partial class SettingsWindow : Window
{
    private readonly SettingsViewModel _vm;

    public SettingsWindow(SettingsViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        _vm.Save();
        DialogResult = true;
        Close();
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
```

- [ ] **Step 3: Add `OpenSettingsCommand` to `MainViewModel`**

Add this command method inside `MainViewModel` (alongside the other `[RelayCommand]` methods):
```csharp
[RelayCommand]
private void OpenSettings()
{
    var vm = new SettingsViewModel(_settings, _settingsPath);
    var win = new Views.SettingsWindow(vm) { Owner = System.Windows.Application.Current.MainWindow };
    if (win.ShowDialog() == true)
    {
        ConfigureAutoRefresh();
        StatusText = "Настройки сохранены. Нажмите «Обновить» для переиндексации.";
    }
}
```

- [ ] **Step 4: Add a Настройки button to `MainWindow.xaml`**

In the search-bar `DockPanel` (Task 10 Step 5), add before the Обновить button:
```xml
<Button DockPanel.Dock="Right" Content="Настройки" Width="90" Margin="6,0,0,0"
        Command="{Binding OpenSettingsCommand}"/>
```

- [ ] **Step 5: Build and smoke-run**

Run: `dotnet build`
Expected: build succeeds.

Manual smoke test:
1. `dotnet run --project src/NetSearch.App`.
2. Click **Настройки**, add a local folder path, set auto-refresh to `1`, Save.
3. Click **Обновить** → entries appear. Wait ~1 min → auto-refresh re-runs (status updates).

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat(app): add settings window and auto-refresh wiring"
```

---

## Task 12: Packaging (self-contained publish)

**Files:**
- Create: `publish.ps1`
- Modify: `src/NetSearch.App/NetSearch.App.csproj` (publish properties)
- Create: `README.md`

**Interfaces:**
- Consumes: the whole app.
- Produces: a single self-contained `NetSearch.exe` under `publish/`.

- [ ] **Step 1: Add publish properties to the app csproj**

Add inside the existing `<PropertyGroup>` of `src/NetSearch.App/NetSearch.App.csproj`:
```xml
    <RuntimeIdentifier>win-x64</RuntimeIdentifier>
    <SelfContained>true</SelfContained>
    <PublishSingleFile>true</PublishSingleFile>
    <IncludeNativeLibrariesForSelfExtract>true</IncludeNativeLibrariesForSelfExtract>
    <EnableCompressionInSingleFile>true</EnableCompressionInSingleFile>
```

- [ ] **Step 2: Add publish script**

`publish.ps1`:
```powershell
dotnet publish src/NetSearch.App/NetSearch.App.csproj -c Release -o publish
Write-Host "Built publish/NetSearch.exe"
```

- [ ] **Step 3: Add README**

`README.md`:
```markdown
# NetSearch

Быстрый поиск файлов и папок по сетевым (SMB) дискам — аналог WizFile.
Индексирует заданные сетевые пути в локальную базу SQLite и ищет мгновенно
по имени (подстрока, маски `*?`, regex), с фильтрами по размеру/дате/типу и
поиском по содержимому в текущей выборке.

## Сборка
```
dotnet build
```

## Тесты
```
dotnet test
```

## Публикация (один .exe)
```
pwsh ./publish.ps1
```
Результат: `publish/NetSearch.exe`. Индекс и настройки хранятся в
`%LOCALAPPDATA%\NetSearch\`.

## Использование
1. Запустить `NetSearch.exe`.
2. **Настройки** → добавить сетевые пути (например `\\server\share`), задать
   интервал авто-обновления.
3. **Обновить** — построить индекс (первый раз дольше).
4. Печатать в строке поиска — результаты фильтруются мгновенно.
```

- [ ] **Step 4: Run full test suite and publish**

Run: `dotnet test`
Expected: all tests pass.

Run: `pwsh ./publish.ps1`
Expected: `publish/NetSearch.exe` exists and launches.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "chore: add self-contained publish and README"
```

---

## Self-Review Notes

- **Spec coverage:** multiple roots (Tasks 8, 11), SQLite + in-memory (Tasks 3, 10), crawl + error-skipping (Task 4), incremental + full reindex (Task 5), manual + scheduled refresh (Tasks 10, 11), name/wildcard/regex + filters (Task 6), on-demand content search over selection (Task 7), result actions/context menu/double-click (Task 10), long-path + offline-tolerant load + regex timeout + cancellation (Tasks 4, 6, 7, 10), `%LOCALAPPDATA%` storage (Tasks 8, 10), single-exe delivery (Task 12). All spec sections map to a task.
- **Parallelism note:** the spec's "crawl parallelism" is exposed as a setting but v1 crawls sequentially (documented in Task 4). This is an intentional, logged simplification, not a silent cap.
- **Type consistency:** `FileEntry`, `SearchQuery`/`SearchMode`/`EntryKind`, `IndexResult`, `CrawlResult`, `ContentSearchOptions`/`ContentMatch`, `IndexStore`/`IndexManager` signatures are used identically across producing and consuming tasks.
- **Offline-at-start tolerance:** `MainViewModel.LoadIndexIntoMemory` always loads the last saved index regardless of network availability; reindex failures surface in `StatusText` without clearing the existing index (Task 10).
