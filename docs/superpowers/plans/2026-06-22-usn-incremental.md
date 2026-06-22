# USN Incremental Refresh (Phase 2) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.
>
> **Prerequisite:** Phase 1 (`2026-06-22-mft-bulk-scan.md`) is implemented and merged.

**Goal:** After a full MFT scan, keep a local NTFS root fresh by reading only the NTFS USN change journal — applying create/modify/delete/rename deltas by File Reference Number — and rescanning fully only when the journal is gone or has overflowed.

**Architecture:** Each entry row gains its `frn` (record number); each root stores the journal cursor `(usn_journal_id, usn_next)`. On refresh, if a valid cursor exists, `UsnJournal` reads changes from it; `IndexManager.ApplyUsnDeltas` deletes-by-frn and re-inserts changed FRNs from freshly read MFT records; a missing/rotated journal triggers a full scan instead.

**Tech Stack:** C# / .NET 9, `Microsoft.Data.Sqlite`, xUnit, Win32 P/Invoke (`DeviceIoControl` USN FSCTLs).

## Global Constraints

- All Phase 1 constraints carry over (net9.0 Core, OS guards, crawler fallback, `FromComponents` factory, parent-path semantics).
- FRN is normalised to the **low 48 bits** (record number) everywhere it is stored or compared, so MFT record numbers and USN file references match.
- USN_RECORD_V2 offsets (locked): `RecordLength@0x00 u32`, `FileReferenceNumber@0x08 u64`, `ParentFileReferenceNumber@0x10 u64`, `Usn@0x18 u64`, `Reason@0x28 u32`, `FileNameLength@0x38 u16`, `FileNameOffset@0x3A u16`, name at `0x3C`. READ_USN_JOURNAL output begins with an 8-byte next-USN.
- USN reason masks: `FILE_CREATE=0x100`, `FILE_DELETE=0x200`, `RENAME_OLD_NAME=0x1000`, `RENAME_NEW_NAME=0x2000`, `CLOSE=0x80000000`.
- A failed/invalid journal path falls back to a full MFT scan (never to a stale index).

---

### Task 1: Add `Frn` to the entry model

**Files:**
- Modify: `src/NetSearch.Core/Models/FileEntry.cs`
- Test: `tests/NetSearch.Core.Tests/FileEntryFrnTests.cs`

**Interfaces:**
- Produces: `FileEntry.Frn` (`long?`, default `null`); `FileEntry.FromComponents(int rootId, string name, string parentPath, bool isDir, long size, long modifiedUnix, long? frn = null)`.

- [ ] **Step 1: Write the failing test**

```csharp
using Xunit;
using NetSearch.Core.Models;

namespace NetSearch.Core.Tests;

public class FileEntryFrnTests
{
    [Fact]
    public void Frn_defaults_to_null_and_can_be_set()
    {
        Assert.Null(FileEntry.FromComponents(1, "a", @"C:\", false, 1, 2).Frn);
        Assert.Equal(42, FileEntry.FromComponents(1, "a", @"C:\", false, 1, 2, frn: 42).Frn);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~FileEntryFrnTests"`
Expected: FAIL — `Frn` / 7-arg overload not defined.

- [ ] **Step 3: Implement**

Add the property and extend the factory:

```csharp
public long? Frn { get; init; }
```

```csharp
public static FileEntry FromComponents(int rootId, string name, string parentPath, bool isDir, long size, long modifiedUnix, long? frn = null)
{
    return new FileEntry
    {
        RootId = rootId,
        Name = name,
        NameLower = name.ToLowerInvariant(),
        ParentPath = parentPath,
        IsDir = isDir,
        Size = size,
        Ext = isDir ? "" : Path.GetExtension(name).TrimStart('.').ToLowerInvariant(),
        Modified = modifiedUnix,
        Frn = frn,
    };
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter "FullyQualifiedName~FileEntryFrnTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/NetSearch.Core/Models/FileEntry.cs tests/NetSearch.Core.Tests/FileEntryFrnTests.cs
git commit -m "feat(usn): FileEntry carries optional FRN"
```

---

### Task 2: Schema + store methods for FRN and journal cursor

**Files:**
- Modify: `src/NetSearch.Core/Storage/IndexStore.cs`
- Test: `tests/NetSearch.Core.Tests/IndexStoreUsnTests.cs`

**Interfaces:**
- Produces:
  - `entries.frn INTEGER NULL` column (written by `BulkUpsert`).
  - `roots.usn_journal_id INTEGER`, `roots.usn_next INTEGER`.
  - `void IndexStore.SetUsnState(int rootId, long journalId, long nextUsn)`
  - `bool IndexStore.TryGetUsnState(int rootId, out long journalId, out long nextUsn)`
  - `void IndexStore.RemoveByFrn(int rootId, IEnumerable<long> frns)`

- [ ] **Step 1: Write the failing test**

```csharp
using Xunit;
using NetSearch.Core.Models;
using NetSearch.Core.Storage;

namespace NetSearch.Core.Tests;

public class IndexStoreUsnTests : IDisposable
{
    private readonly string _db = Path.Combine(Path.GetTempPath(), $"usn_{Guid.NewGuid():N}.db");

    [Fact]
    public void Usn_state_roundtrips_per_root()
    {
        using var s = new IndexStore(_db); s.Initialize();
        var id = s.UpsertRoot(@"C:\Data");
        Assert.False(s.TryGetUsnState(id, out _, out _));
        s.SetUsnState(id, journalId: 777, nextUsn: 12345);
        Assert.True(s.TryGetUsnState(id, out var j, out var n));
        Assert.Equal(777, j);
        Assert.Equal(12345, n);
    }

    [Fact]
    public void RemoveByFrn_deletes_matching_rows_only()
    {
        using var s = new IndexStore(_db); s.Initialize();
        var id = s.UpsertRoot(@"C:\Data");
        s.BulkUpsert(new[]
        {
            FileEntry.FromComponents(id, "a.txt", @"C:\Data", false, 1, 1, frn: 100),
            FileEntry.FromComponents(id, "b.txt", @"C:\Data", false, 1, 1, frn: 200),
        });
        s.RemoveByFrn(id, new long[] { 100 });
        var names = s.LoadAll().Select(e => e.Name).ToList();
        Assert.Equal(new[] { "b.txt" }, names);
    }

    public void Dispose()
    {
        if (File.Exists(_db)) File.Delete(_db);
        foreach (var x in new[] { "-wal", "-shm" }) if (File.Exists(_db + x)) File.Delete(_db + x);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~IndexStoreUsnTests"`
Expected: FAIL — methods/columns not defined.

- [ ] **Step 3: Implement migration + methods**

In `Initialize()`, after the `CREATE TABLE`/`DROP INDEX` block, add idempotent migrations:

```csharp
AddColumnIfMissing("entries", "frn", "INTEGER");
AddColumnIfMissing("roots", "usn_journal_id", "INTEGER NOT NULL DEFAULT 0");
AddColumnIfMissing("roots", "usn_next", "INTEGER NOT NULL DEFAULT 0");
```

Add helpers + methods:

```csharp
private void AddColumnIfMissing(string table, string column, string decl)
{
    using var info = _conn.CreateCommand();
    info.CommandText = $"PRAGMA table_info({table});";
    using var r = info.ExecuteReader();
    while (r.Read()) if (string.Equals(r.GetString(1), column, StringComparison.OrdinalIgnoreCase)) return;
    r.Close();
    Exec($"ALTER TABLE {table} ADD COLUMN {column} {decl};");
}

public void SetUsnState(int rootId, long journalId, long nextUsn)
{
    lock (_gate)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "UPDATE roots SET usn_journal_id=$j, usn_next=$n WHERE id=$id;";
        cmd.Parameters.AddWithValue("$j", journalId);
        cmd.Parameters.AddWithValue("$n", nextUsn);
        cmd.Parameters.AddWithValue("$id", rootId);
        cmd.ExecuteNonQuery();
    }
}

public bool TryGetUsnState(int rootId, out long journalId, out long nextUsn)
{
    lock (_gate)
    {
        journalId = 0; nextUsn = 0;
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT usn_journal_id, usn_next FROM roots WHERE id=$id;";
        cmd.Parameters.AddWithValue("$id", rootId);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return false;
        journalId = r.GetInt64(0); nextUsn = r.GetInt64(1);
        return journalId != 0;
    }
}

public void RemoveByFrn(int rootId, IEnumerable<long> frns)
{
    lock (_gate)
    {
        using var tx = _conn.BeginTransaction();
        using var cmd = _conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "DELETE FROM entries WHERE root_id=$r AND frn=$f;";
        var pr = cmd.Parameters.Add("$r", SqliteType.Integer);
        var pf = cmd.Parameters.Add("$f", SqliteType.Integer);
        pr.Value = rootId;
        foreach (var f in frns) { pf.Value = f; cmd.ExecuteNonQuery(); }
        tx.Commit();
    }
}
```

Update `BulkUpsert`'s SQL to include `frn` (insert and on-conflict update):

```csharp
cmd.CommandText = """
    INSERT INTO entries(root_id,name,name_lower,parent_path,is_dir,size,ext,modified,frn)
    VALUES($root,$name,$namel,$parent,$isdir,$size,$ext,$mod,$frn)
    ON CONFLICT(root_id,parent_path,name) DO UPDATE SET
      is_dir=excluded.is_dir, size=excluded.size,
      ext=excluded.ext, modified=excluded.modified, frn=excluded.frn;
    """;
// ...add parameter:
var pFrn = cmd.Parameters.Add("$frn", SqliteType.Integer);
// ...in the loop:
pFrn.Value = (object?)e.Frn ?? DBNull.Value;
```

- [ ] **Step 4: Run test to verify it passes (and suite green)**

Run: `dotnet test`
Expected: PASS — new tests green; existing `IndexStore`/`IndexManager` tests still pass (column is nullable, defaults preserved).

- [ ] **Step 5: Commit**

```bash
git add src/NetSearch.Core/Storage/IndexStore.cs tests/NetSearch.Core.Tests/IndexStoreUsnTests.cs
git commit -m "feat(usn): frn column, journal cursor, and frn-keyed delete"
```

---

### Task 3: Carry FRN through MFT assembly

**Files:**
- Modify: `src/NetSearch.Core/Native/MftEntryAssembler.cs`
- Test: `tests/NetSearch.Core.Tests/Native/MftEntryAssemblerFrnTests.cs`

**Interfaces:**
- `MftEntryAssembler.Assemble` now sets each `FileEntry.Frn` to the record number (its dictionary key), masked to 48 bits.

- [ ] **Step 1: Write the failing test**

```csharp
using Xunit;
using NetSearch.Core.Native;

namespace NetSearch.Core.Tests.Native;

public class MftEntryAssemblerFrnTests
{
    [Fact]
    public void Each_entry_keeps_its_record_number_as_frn()
    {
        var records = new Dictionary<long, ParsedMftRecord>
        {
            [5]  = new(true, 5, "", 0, 0),
            [10] = new(true, 5, "Data", 0, 0),
            [12] = new(false, 10, "a.txt", 1, 1),
        };
        var byName = MftEntryAssembler.Assemble(1, "C:", @"C:\Data", records)
            .ToDictionary(e => e.Name);
        Assert.Equal(12, byName["a.txt"].Frn);
        Assert.Equal(10, byName["Data"].Frn);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~MftEntryAssemblerFrnTests"`
Expected: FAIL — `Frn` is null.

- [ ] **Step 3: Implement**

In `MftEntryAssembler.Assemble`, pass the record number as `frn`:

```csharp
yield return FileEntry.FromComponents(
    rootId, r.Name, parentPath, r.IsDir, r.Size, r.ModifiedUnix, frn: rec & 0xFFFFFFFFFFFFL);
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter "FullyQualifiedName~MftEntryAssemblerFrnTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/NetSearch.Core/Native/MftEntryAssembler.cs tests/NetSearch.Core.Tests/Native/MftEntryAssemblerFrnTests.cs
git commit -m "feat(usn): MFT entries carry their record number as FRN"
```

---

### Task 4: USN record parser (pure)

**Files:**
- Create: `src/NetSearch.Core/Native/UsnRecordParser.cs`
- Test: `tests/NetSearch.Core.Tests/Native/UsnRecordParserTests.cs`

**Interfaces:**
- Produces:
  - `readonly record struct UsnChange(long Frn, long ParentFrn, string Name, uint Reason)`
  - `static (long NextUsn, IReadOnlyList<UsnChange> Changes) UsnRecordParser.Parse(ReadOnlySpan<byte> readOutput)`
  - reason helpers `UsnReason.IsDelete(uint)`, `UsnReason.Mask` constants.

- [ ] **Step 1: Write the failing test**

```csharp
using System.Text;
using Xunit;
using NetSearch.Core.Native;

namespace NetSearch.Core.Tests.Native;

public class UsnRecordParserTests
{
    private static byte[] Record(long frn, long parent, uint reason, string name)
    {
        var nm = Encoding.Unicode.GetBytes(name);
        int len = 0x3C + nm.Length;
        len = (len + 7) & ~7;
        var b = new byte[len];
        BitConverter.GetBytes((uint)len).CopyTo(b, 0x00);
        BitConverter.GetBytes((ushort)2).CopyTo(b, 0x04);  // major version
        BitConverter.GetBytes(frn).CopyTo(b, 0x08);
        BitConverter.GetBytes(parent).CopyTo(b, 0x10);
        BitConverter.GetBytes(reason).CopyTo(b, 0x28);
        BitConverter.GetBytes((ushort)nm.Length).CopyTo(b, 0x38);
        BitConverter.GetBytes((ushort)0x3C).CopyTo(b, 0x3A);
        nm.CopyTo(b, 0x3C);
        return b;
    }

    [Fact]
    public void Parses_next_usn_and_multiple_records()
    {
        var r1 = Record(100, 5, 0x100, "a.txt");      // create
        var r2 = Record(200, 5, 0x200, "gone.txt");   // delete
        var buf = new byte[8 + r1.Length + r2.Length];
        BitConverter.GetBytes(99999L).CopyTo(buf, 0);
        r1.CopyTo(buf, 8);
        r2.CopyTo(buf, 8 + r1.Length);

        var (next, changes) = UsnRecordParser.Parse(buf);

        Assert.Equal(99999, next);
        Assert.Equal(2, changes.Count);
        Assert.Equal(100, changes[0].Frn);
        Assert.Equal("a.txt", changes[0].Name);
        Assert.True(UsnReason.IsDelete(changes[1].Reason));
        Assert.Equal(200, changes[1].Frn);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~UsnRecordParserTests"`
Expected: FAIL — types not defined.

- [ ] **Step 3: Implement**

```csharp
using System.Buffers.Binary;
using System.Text;

namespace NetSearch.Core.Native;

public readonly record struct UsnChange(long Frn, long ParentFrn, string Name, uint Reason);

public static class UsnReason
{
    public const uint FileCreate = 0x100, FileDelete = 0x200,
        RenameOldName = 0x1000, RenameNewName = 0x2000, Close = 0x80000000;
    public static bool IsDelete(uint reason) => (reason & FileDelete) != 0;
}

public static class UsnRecordParser
{
    public static (long NextUsn, IReadOnlyList<UsnChange> Changes) Parse(ReadOnlySpan<byte> readOutput)
    {
        if (readOutput.Length < 8) return (0, Array.Empty<UsnChange>());
        long next = BinaryPrimitives.ReadInt64LittleEndian(readOutput);
        var changes = new List<UsnChange>();
        int pos = 8;
        while (pos + 0x3C <= readOutput.Length)
        {
            int len = (int)BinaryPrimitives.ReadUInt32LittleEndian(readOutput[pos..]);
            if (len < 0x3C || pos + len > readOutput.Length) break;
            long frn = BinaryPrimitives.ReadInt64LittleEndian(readOutput[(pos + 0x08)..]) & 0xFFFFFFFFFFFFL;
            long parent = BinaryPrimitives.ReadInt64LittleEndian(readOutput[(pos + 0x10)..]) & 0xFFFFFFFFFFFFL;
            uint reason = BinaryPrimitives.ReadUInt32LittleEndian(readOutput[(pos + 0x28)..]);
            int nameLen = BinaryPrimitives.ReadUInt16LittleEndian(readOutput[(pos + 0x38)..]);
            int nameOff = BinaryPrimitives.ReadUInt16LittleEndian(readOutput[(pos + 0x3A)..]);
            var name = Encoding.Unicode.GetString(readOutput.Slice(pos + nameOff, nameLen));
            changes.Add(new UsnChange(frn, parent, name, reason));
            pos += len;
        }
        return (next, changes);
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter "FullyQualifiedName~UsnRecordParserTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/NetSearch.Core/Native/UsnRecordParser.cs tests/NetSearch.Core.Tests/Native/UsnRecordParserTests.cs
git commit -m "feat(usn): pure USN_RECORD_V2 buffer parser"
```

---

### Task 5: Delta application logic

**Files:**
- Modify: `src/NetSearch.Core/Indexing/IndexManager.cs`
- Test: `tests/NetSearch.Core.Tests/IndexManagerUsnTests.cs`

**Interfaces:**
- Produces: `IndexManager.ApplyUsnDeltas(int rootId, IReadOnlyList<UsnChange> changes, Func<long, FileEntry?> readEntryByFrn)` — for each distinct FRN: delete it, then if it still exists (re-read returns non-null and not a delete-only change) re-insert. De-duplicates FRNs (a file may appear many times in one batch).

- [ ] **Step 1: Write the failing test**

```csharp
using Xunit;
using NetSearch.Core.Indexing;
using NetSearch.Core.Models;
using NetSearch.Core.Native;
using NetSearch.Core.Storage;

namespace NetSearch.Core.Tests;

public class IndexManagerUsnTests : IDisposable
{
    private readonly string _db = Path.Combine(Path.GetTempPath(), $"usnmgr_{Guid.NewGuid():N}.db");

    [Fact]
    public void Apply_inserts_created_updates_changed_and_removes_deleted()
    {
        using var store = new IndexStore(_db); store.Initialize();
        var id = store.UpsertRoot(@"C:\Data");
        store.BulkUpsert(new[]
        {
            FileEntry.FromComponents(id, "old.txt", @"C:\Data", false, 1, 1, frn: 10),
            FileEntry.FromComponents(id, "stay.txt", @"C:\Data", false, 1, 1, frn: 11),
        });
        var mgr = new IndexManager(store, () => new Crawler(), () => 1);

        var changes = new[]
        {
            new UsnChange(10, 5, "old.txt", UsnReason.FileDelete),        // delete frn 10
            new UsnChange(20, 5, "new.txt", UsnReason.FileCreate),        // create frn 20
        };
        // Metadata re-reader: frn 20 now exists; frn 10 is gone.
        FileEntry? Read(long frn) => frn == 20
            ? FileEntry.FromComponents(id, "new.txt", @"C:\Data", false, 7, 9, frn: 20)
            : null;

        mgr.ApplyUsnDeltas(id, changes, Read);

        var names = store.LoadAll().Select(e => e.Name).OrderBy(n => n).ToList();
        Assert.Equal(new[] { "new.txt", "stay.txt" }, names);
    }

    public void Dispose()
    {
        if (File.Exists(_db)) File.Delete(_db);
        foreach (var x in new[] { "-wal", "-shm" }) if (File.Exists(_db + x)) File.Delete(_db + x);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~IndexManagerUsnTests"`
Expected: FAIL — `ApplyUsnDeltas` not defined.

- [ ] **Step 3: Implement**

```csharp
public void ApplyUsnDeltas(int rootId, IReadOnlyList<NetSearch.Core.Native.UsnChange> changes,
    Func<long, Models.FileEntry?> readEntryByFrn)
{
    var frns = changes.Select(c => c.Frn).Distinct().ToList();
    // Old rows for every touched FRN go first (handles delete + rename-away cleanly).
    _store.RemoveByFrn(rootId, frns);

    var fresh = new List<Models.FileEntry>();
    foreach (var frn in frns)
    {
        var entry = readEntryByFrn(frn);
        if (entry is not null) fresh.Add(entry);
    }
    if (fresh.Count > 0) _store.BulkUpsert(fresh);
    _store.SetRootIndexed(rootId, _clock());
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter "FullyQualifiedName~IndexManagerUsnTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/NetSearch.Core/Indexing/IndexManager.cs tests/NetSearch.Core.Tests/IndexManagerUsnTests.cs
git commit -m "feat(usn): apply journal deltas by FRN (delete-then-reinsert)"
```

---

### Task 6: USN journal interop + single-record reader (manual verification)

**Files:**
- Create: `src/NetSearch.Core/Native/UsnJournal.cs`
- Modify: `src/NetSearch.Core/Native/NativeMethods.cs` (add FSCTL codes)
- Modify: `src/NetSearch.Core/Native/NtfsVolume.cs` (add `ReadRecord(long recordNumber)`)

**Interfaces:**
- Produces:
  - `bool UsnJournal.TryQuery(NtfsVolume vol, out long journalId, out long nextUsn)`
  - `(long NextUsn, IReadOnlyList<UsnChange> Changes) UsnJournal.Read(NtfsVolume vol, long journalId, long startUsn)`
  - `bool UsnJournal.JournalMatches(NtfsVolume vol, long expectedJournalId)`
  - `byte[] NtfsVolume.ReadRecord(long recordNumber)` (one MFT FILE record by number, for the metadata re-reader)

No automated test (needs admin + live volume). Verified in Task 7's checklist.

- [ ] **Step 1: Add FSCTL codes + the record reader**

Add to `NativeMethods`:

```csharp
public const uint FSCTL_QUERY_USN_JOURNAL = 0x000900F4;
public const uint FSCTL_READ_USN_JOURNAL  = 0x000900BB;
```

Add to `NtfsVolume`:

```csharp
public byte[] ReadRecord(long recordNumber)
{
    long byteOffset = MftStartLcn * BytesPerCluster + recordNumber * BytesPerFileRecordSegment;
    long lcn = byteOffset / BytesPerCluster;
    int within = (int)(byteOffset % BytesPerCluster);
    var clusters = ReadClusters(lcn, (BytesPerFileRecordSegment + BytesPerCluster - 1) / BytesPerCluster + 1);
    return clusters.AsSpan(within, BytesPerFileRecordSegment).ToArray();
}
```

- [ ] **Step 2: Implement the journal wrapper**

```csharp
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace NetSearch.Core.Native;

[SupportedOSPlatform("windows")]
public static class UsnJournal
{
    public static bool TryQuery(NtfsVolume vol, out long journalId, out long nextUsn)
    {
        journalId = 0; nextUsn = 0;
        var outBuf = new byte[80]; // USN_JOURNAL_DATA_V0/V1
        if (!vol.DeviceControl(NativeMethods.FSCTL_QUERY_USN_JOURNAL, Array.Empty<byte>(), outBuf, out _))
            return false;
        journalId = BitConverter.ToInt64(outBuf, 0x00); // UsnJournalID
        nextUsn   = BitConverter.ToInt64(outBuf, 0x08); // NextUsn
        return journalId != 0;
    }

    public static bool JournalMatches(NtfsVolume vol, long expectedJournalId)
        => TryQuery(vol, out var id, out _) && id == expectedJournalId;

    public static (long NextUsn, IReadOnlyList<UsnChange> Changes) Read(NtfsVolume vol, long journalId, long startUsn)
    {
        // READ_USN_JOURNAL_DATA_V0: StartUsn, ReasonMask, ReturnOnlyOnClose, Timeout, BytesToWaitFor, UsnJournalID
        var input = new byte[40];
        BitConverter.GetBytes(startUsn).CopyTo(input, 0x00);
        BitConverter.GetBytes(0xFFFFFFFFu).CopyTo(input, 0x08); // ReasonMask = all
        BitConverter.GetBytes(journalId).CopyTo(input, 0x20);
        var outBuf = new byte[64 * 1024];
        if (!vol.DeviceControl(NativeMethods.FSCTL_READ_USN_JOURNAL, input, outBuf, out var returned))
            return (startUsn, Array.Empty<UsnChange>());
        return UsnRecordParser.Parse(outBuf.AsSpan(0, (int)returned));
    }
}
```

Add a thin `DeviceControl` passthrough to `NtfsVolume` (input/output byte arrays) so `UsnJournal` need not duplicate P/Invoke:

```csharp
public bool DeviceControl(uint code, byte[] input, byte[] output, out uint returned)
{
    var inHandle = System.Runtime.InteropServices.GCHandle.Alloc(input, System.Runtime.InteropServices.GCHandleType.Pinned);
    try
    {
        var inPtr = input.Length == 0 ? IntPtr.Zero : inHandle.AddrOfPinnedObject();
        return NativeMethods.DeviceIoControl(_handle, code, inPtr, (uint)input.Length, output, (uint)output.Length, out returned, IntPtr.Zero);
    }
    finally { inHandle.Free(); }
}
```

- [ ] **Step 3: Build to confirm compilation**

Run: `dotnet build`
Expected: build succeeds (0 errors).

- [ ] **Step 4: Commit**

```bash
git add src/NetSearch.Core/Native/UsnJournal.cs src/NetSearch.Core/Native/NativeMethods.cs src/NetSearch.Core/Native/NtfsVolume.cs
git commit -m "feat(usn): USN journal query/read interop and single-record reader"
```

---

### Task 7: Wire incremental refresh + manual verification

**Files:**
- Modify: `src/NetSearch.Core/Native/MftEnumerator.cs` (expose `ReadEntryByFrn` helper + after full scan, persist `(journalId,nextUsn)`)
- Modify: `src/NetSearch.App/ViewModels/MainViewModel.cs` (incremental-first refresh for MFT roots)
- Create: `docs/superpowers/manual-checks/usn-incremental.md`

**Interfaces:**
- Consumes: `UsnJournal`, `IndexStore.{TryGetUsnState,SetUsnState}`, `IndexManager.ApplyUsnDeltas`, `NtfsVolume.ReadRecord`, `MftRecordParser`, `MftEntryAssembler` path logic.
- Produces: `FileEntry? MftEnumerator.ReadEntryByFrn(NtfsVolume vol, int rootId, string volumeRoot, string rootFilter, long frn)` — read one MFT record and assemble a single entry (or null if absent/outside root).

- [ ] **Step 1: Add the single-FRN reader to `MftEnumerator`**

```csharp
[SupportedOSPlatform("windows")]
public FileEntry? ReadEntryByFrn(NtfsVolume vol, int rootId, string volumeRoot, string rootFilter, long frn)
{
    var bytes = vol.ReadRecord(frn);
    if (!MftRecordParser.TryParse(bytes, 512, out var r)) return null;
    var records = new Dictionary<long, ParsedMftRecord> { [frn] = r };
    // Resolve the parent path by reading ancestor directory records on demand.
    void EnsureParent(long rec)
    {
        var cur = records[rec].ParentRecordNumber;
        while (cur != 5 && !records.ContainsKey(cur))
        {
            var pb = vol.ReadRecord(cur);
            if (!MftRecordParser.TryParse(pb, 512, out var pr)) break;
            records[cur] = pr; cur = pr.ParentRecordNumber;
        }
    }
    EnsureParent(frn);
    return MftEntryAssembler.Assemble(rootId, volumeRoot, rootFilter, records).FirstOrDefault();
}
```

- [ ] **Step 2: Persist journal cursor after a full MFT scan**

The full-scan caller (added in Phase 1, Task 10) records the cursor once the snapshot is committed. In `MainViewModel.RefreshAsync`, after a successful full MFT `UpdateRootWith`, query and store the journal state:

```csharp
using (var vol = NetSearch.Core.Native.NtfsVolume.Open(char.ToUpperInvariant(path[0])))
    if (NetSearch.Core.Native.UsnJournal.TryQuery(vol, out var jid, out var nUsn))
        _store.SetUsnState(id, jid, nUsn);
```

- [ ] **Step 3: Make refresh incremental-first for MFT roots**

Replace the MFT branch in `RefreshAsync` with: if a stored cursor exists and the journal still matches, apply deltas; otherwise do a full scan and (re)store the cursor.

```csharp
if (backend == NetSearch.Core.Native.IndexBackend.Mft && OperatingSystem.IsWindows())
{
    var letter = char.ToUpperInvariant(path[0]);
    var volumeRoot = letter + ":";
    var filter = path.TrimEnd('\\', '/');
    using var vol = NetSearch.Core.Native.NtfsVolume.Open(letter);
    var mft = new NetSearch.Core.Native.MftEnumerator();

    if (_store.TryGetUsnState(id, out var jid, out var nextUsn)
        && NetSearch.Core.Native.UsnJournal.JournalMatches(vol, jid))
    {
        var (newNext, changes) = NetSearch.Core.Native.UsnJournal.Read(vol, jid, nextUsn);
        mgr.ApplyUsnDeltas(id, changes, frn => mft.ReadEntryByFrn(vol, id, volumeRoot, filter, frn));
        _store.SetUsnState(id, jid, newNext);
    }
    else
    {
        mgr.UpdateRootWith(id, path, (onBatch, ct2, prog) => { mft.Enumerate(id, path, onBatch, ct2, prog); return 0; }, token, progress);
        if (NetSearch.Core.Native.UsnJournal.TryQuery(vol, out var fjid, out var fnext))
            _store.SetUsnState(id, fjid, fnext);
    }
}
```

(The surrounding `try/catch` from Phase 1 still downgrades any failure to `mgr.UpdateRoot` — the crawler.)

- [ ] **Step 4: Build + full suite**

Run: `dotnet build && dotnet test`
Expected: build 0 errors; all tests pass.

- [ ] **Step 5: Manual verification checklist**

Create `docs/superpowers/manual-checks/usn-incremental.md`:

```markdown
# Manual check — USN incremental refresh (requires Administrator)

1. As Administrator, index a local NTFS root (full MFT scan). Confirm the status/counts.
2. Create a new file under the root, edit an existing file's content, delete a third, rename a fourth.
3. Click Обновить. The refresh should be near-instant (no full rescan) and reflect:
   - new file present with correct size/date,
   - edited file's size/date updated,
   - deleted file gone,
   - renamed file shows the new name/path, old name absent.
4. Disable + re-enable the volume's journal (`fsutil usn deletejournal /d C:` then re-create), then Обновить
   → app detects the journal mismatch and performs a full rescan (no stale rows).
5. Run not-as-admin → crawler path, everything still works.
```

- [ ] **Step 6: Commit**

```bash
git add src/NetSearch.Core/Native/MftEnumerator.cs src/NetSearch.App/ViewModels/MainViewModel.cs docs/superpowers/manual-checks/usn-incremental.md
git commit -m "feat(usn): incremental-first refresh with journal-mismatch full rescan"
```

---

## Self-Review

**Spec coverage:** `entries.frn` + `roots.usn_*` schema (Task 2) ✓; FRN carried from MFT (Tasks 1,3) ✓; USN parse (Task 4) ✓; journal interop + single-record re-read (Task 6) ✓; delete/create/modify/rename via delete-then-reinsert (Task 5) ✓; rescan on journal gap/rotation (Task 7) ✓; crawler fallback preserved (Task 7 note) ✓; verification gap documented (Tasks 6–7) ✓.

**Placeholder scan:** No TBD/TODO; every step has concrete code or an exact command.

**Type consistency:** `UsnChange`, `UsnReason.*`, `UsnRecordParser.Parse`, `UsnJournal.{TryQuery,Read,JournalMatches}`, `NtfsVolume.{ReadRecord,DeviceControl}`, `IndexStore.{SetUsnState,TryGetUsnState,RemoveByFrn}`, `IndexManager.ApplyUsnDeltas`, `MftEnumerator.ReadEntryByFrn`, `FileEntry.Frn` are used identically across tasks and match Phase 1's `MftEntryAssembler.Assemble` / `MftEnumerator.Enumerate` signatures.
```
