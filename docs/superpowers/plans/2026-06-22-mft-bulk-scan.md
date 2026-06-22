# MFT Bulk Scan (Phase 1) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Index local fixed NTFS roots by reading the raw `$MFT` (name + path + size + modified date in one sequential pass), selected automatically when elevated, with transparent fallback to the existing `Crawler`.

**Architecture:** A new `src/NetSearch.Core/Native/` layer splits *pure parsing* (data-run decoding, FILE-record parsing, path reconstruction, entry assembly, strategy selection) from *Win32 interop* (volume open, raw reads). `IndexManager` picks a backend per root via `IndexStrategySelector`; `MftEnumerator` emits `FileEntry` batches through the exact same `onBatch` contract as `Crawler`, so storage/search are untouched. Any MFT failure downgrades that root to `Crawler`.

**Tech Stack:** C# / .NET 9, `Microsoft.Data.Sqlite`, xUnit, Win32 P/Invoke (`DeviceIoControl`, `CreateFile`).

## Global Constraints

- Target framework: `net9.0` for `NetSearch.Core` (no `-windows`); native code guarded with `[SupportedOSPlatform("windows")]` + runtime OS checks. Verbatim from spec.
- Pure-logic units MUST be unit-tested without admin or a live volume; interop units are verified manually by the user under an elevated process.
- `FileEntry` is produced only via `FileEntry.FromComponents(int rootId, string name, string parentPath, bool isDir, long size, long modifiedUnix)` — the same factory the crawler uses.
- Parent-path form must match `Path.GetDirectoryName` semantics so both engines agree: volume root `C:` → `C:\`; nested dir → no trailing separator (`C:\Users`).
- The MFT path must NEVER make the app worse: every failure path falls back to `Crawler`.
- NTFS facts (locked, used across tasks): MFT record header is 48 bytes; signature `FILE` = bytes `46 49 4C 45`; header flags bit `0x01`=in use, `0x02`=directory; root directory is **record number 5**; FILETIME epoch offset to Unix = `116444736000000000` ticks; attribute terminator type = `0xFFFFFFFF`; attribute types `$STANDARD_INFORMATION`=`0x10`, `$FILE_NAME`=`0x30`, `$DATA`=`0x80`; `$FILE_NAME` namespaces `0`=POSIX, `1`=Win32, `2`=DOS, `3`=Win32&DOS.

---

### Task 1: Native types + FILETIME conversion

**Files:**
- Create: `src/NetSearch.Core/Native/MftTypes.cs`
- Test: `tests/NetSearch.Core.Tests/Native/MftTimeTests.cs`

**Interfaces:**
- Produces:
  - `readonly record struct ParsedMftRecord(bool IsDir, long ParentRecordNumber, string Name, long Size, long ModifiedUnix)`
  - `readonly record struct MftNode(string Name, long ParentRecordNumber, bool IsDir)`
  - `readonly record struct DataRun(long Lcn, long ClusterCount)` (`Lcn == -1` ⇒ sparse hole)
  - `enum IndexBackend { Mft, Crawler }`
  - `readonly record struct VolumeInfo(string FileSystem, bool IsFixed, bool IsUnc, char DriveLetter)`
  - `static long MftTime.FileTimeToUnixSeconds(long fileTime)`

- [ ] **Step 1: Write the failing test**

```csharp
using Xunit;
using NetSearch.Core.Native;

namespace NetSearch.Core.Tests.Native;

public class MftTimeTests
{
    [Fact]
    public void FileTime_for_unix_epoch_is_zero()
        => Assert.Equal(0, MftTime.FileTimeToUnixSeconds(116444736000000000L));

    [Fact]
    public void FileTime_one_day_after_epoch_is_86400()
        => Assert.Equal(86400, MftTime.FileTimeToUnixSeconds(116444736000000000L + 86400L * 10_000_000L));

    [Fact]
    public void Nonpositive_filetime_maps_to_zero()
        => Assert.Equal(0, MftTime.FileTimeToUnixSeconds(0));
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~MftTimeTests"`
Expected: FAIL — `MftTime`/`NetSearch.Core.Native` does not exist (build error).

- [ ] **Step 3: Write minimal implementation**

```csharp
namespace NetSearch.Core.Native;

public readonly record struct ParsedMftRecord(bool IsDir, long ParentRecordNumber, string Name, long Size, long ModifiedUnix);

public readonly record struct MftNode(string Name, long ParentRecordNumber, bool IsDir);

public readonly record struct DataRun(long Lcn, long ClusterCount);

public enum IndexBackend { Mft, Crawler }

public readonly record struct VolumeInfo(string FileSystem, bool IsFixed, bool IsUnc, char DriveLetter);

public static class MftTime
{
    private const long EpochOffsetTicks = 116444736000000000L; // 1601-01-01 → 1970-01-01

    public static long FileTimeToUnixSeconds(long fileTime)
        => fileTime <= EpochOffsetTicks ? 0 : (fileTime - EpochOffsetTicks) / 10_000_000L;
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter "FullyQualifiedName~MftTimeTests"`
Expected: PASS (3 tests).

- [ ] **Step 5: Commit**

```bash
git add src/NetSearch.Core/Native/MftTypes.cs tests/NetSearch.Core.Tests/Native/MftTimeTests.cs
git commit -m "feat(mft): native value types and FILETIME→Unix conversion"
```

---

### Task 2: Data-run list parser

**Files:**
- Create: `src/NetSearch.Core/Native/DataRunParser.cs`
- Test: `tests/NetSearch.Core.Tests/Native/DataRunParserTests.cs`

**Interfaces:**
- Consumes: `DataRun` (Task 1).
- Produces: `static IReadOnlyList<DataRun> DataRunParser.Parse(ReadOnlySpan<byte> runList)`.

Run-list encoding: each run starts with a header byte; low nibble = byte-count of the *length* field, high nibble = byte-count of the *offset* field. Length is unsigned; offset is a **signed delta** added to the running LCN. Header `0x00` terminates. Offset field length `0` ⇒ sparse hole (`Lcn = -1`, running LCN unchanged).

- [ ] **Step 1: Write the failing test**

```csharp
using Xunit;
using NetSearch.Core.Native;

namespace NetSearch.Core.Tests.Native;

public class DataRunParserTests
{
    [Fact]
    public void Single_run()
    {
        // 0x21: len field 1 byte, offset field 2 bytes; length=0x08; offset=0x0010
        var runs = DataRunParser.Parse(new byte[] { 0x21, 0x08, 0x10, 0x00, 0x00 });
        Assert.Single(runs);
        Assert.Equal(8, runs[0].ClusterCount);
        Assert.Equal(0x10, runs[0].Lcn);
    }

    [Fact]
    public void Second_run_offset_is_signed_delta_from_previous()
    {
        // run1: len 0x08 @ lcn +0x20 ; run2: len 0x04 @ delta -0x10 (0xF0 sign-extended)
        var runs = DataRunParser.Parse(new byte[] { 0x11, 0x08, 0x20, 0x11, 0x04, 0xF0, 0x00 });
        Assert.Equal(2, runs.Count);
        Assert.Equal(0x20, runs[0].Lcn);
        Assert.Equal(0x10, runs[1].Lcn); // 0x20 + (-0x10)
    }

    [Fact]
    public void Sparse_hole_has_lcn_minus_one_and_keeps_running_lcn()
    {
        // run1 @ +0x20 ; sparse run (offset len 0) length 0x03 ; run3 len 0x02 delta +0x05
        var runs = DataRunParser.Parse(new byte[] { 0x11, 0x08, 0x20, 0x01, 0x03, 0x11, 0x02, 0x05, 0x00 });
        Assert.Equal(3, runs.Count);
        Assert.Equal(-1, runs[1].Lcn);
        Assert.Equal(0x25, runs[2].Lcn); // 0x20 + 0x05, hole did not move base
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~DataRunParserTests"`
Expected: FAIL — `DataRunParser` not defined.

- [ ] **Step 3: Write minimal implementation**

```csharp
namespace NetSearch.Core.Native;

public static class DataRunParser
{
    public static IReadOnlyList<DataRun> Parse(ReadOnlySpan<byte> runList)
    {
        var runs = new List<DataRun>();
        var pos = 0;
        long lcn = 0;
        while (pos < runList.Length)
        {
            var header = runList[pos++];
            if (header == 0) break;
            int lenSize = header & 0x0F;
            int offSize = (header >> 4) & 0x0F;
            if (lenSize == 0 || pos + lenSize + offSize > runList.Length) break;

            long length = 0;
            for (var i = 0; i < lenSize; i++) length |= (long)runList[pos++] << (8 * i);

            if (offSize == 0)
            {
                runs.Add(new DataRun(-1, length)); // sparse: LCN base unchanged
                continue;
            }

            long delta = 0;
            for (var i = 0; i < offSize; i++) delta |= (long)runList[pos++] << (8 * i);
            // sign-extend the offset field
            var signBit = 1L << (offSize * 8 - 1);
            if ((delta & signBit) != 0) delta -= signBit << 1;

            lcn += delta;
            runs.Add(new DataRun(lcn, length));
        }
        return runs;
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter "FullyQualifiedName~DataRunParserTests"`
Expected: PASS (3 tests).

- [ ] **Step 5: Commit**

```bash
git add src/NetSearch.Core/Native/DataRunParser.cs tests/NetSearch.Core.Tests/Native/DataRunParserTests.cs
git commit -m "feat(mft): non-resident data-run list parser"
```

---

### Task 3: FILE-record parser test builder

A reusable helper that assembles a valid FILE record (header + fixups + attributes) so the parser tests are readable. Folded with its first consumer test in Task 4, but built first as its own commit because every later parser test depends on it.

**Files:**
- Create: `tests/NetSearch.Core.Tests/Native/MftRecordBuilder.cs`

**Interfaces:**
- Produces a test-only builder:
  - `new MftRecordBuilder(int recordSize = 1024, int bytesPerSector = 512)`
  - `.AsDirectory()` / `.InUse(bool)` / `.BaseRecord(long)`
  - `.StandardInformation(long modifiedFileTime)`
  - `.FileName(long parentRecordNumber, string name, byte ns)`  (ns: 1=Win32, 2=DOS, 3=Win32&DOS)
  - `.DataResident(long size)` / `.DataNonResident(long realSize)`
  - `byte[] Build()` — writes attributes, sets `FirstAttributeOffset`/`BytesInUse`, then applies the update-sequence (fixup) array.

- [ ] **Step 1: Create the builder (no test of its own; exercised by Task 4)**

```csharp
using System.Text;

namespace NetSearch.Core.Tests.Native;

// Builds a minimal but structurally valid NTFS FILE record for parser tests.
internal sealed class MftRecordBuilder
{
    private readonly int _recordSize, _sectorSize;
    private bool _isDir, _inUse = true;
    private long _baseRecord;
    private readonly List<byte[]> _attrs = new();

    public MftRecordBuilder(int recordSize = 1024, int bytesPerSector = 512)
    { _recordSize = recordSize; _sectorSize = bytesPerSector; }

    public MftRecordBuilder AsDirectory() { _isDir = true; return this; }
    public MftRecordBuilder InUse(bool v) { _inUse = v; return this; }
    public MftRecordBuilder BaseRecord(long r) { _baseRecord = r; return this; }

    public MftRecordBuilder StandardInformation(long modifiedFileTime)
    {
        var content = new byte[0x30];
        BitConverter.GetBytes(modifiedFileTime).CopyTo(content, 0x08); // altered/last-write
        _attrs.Add(Attr(0x10, content));
        return this;
    }

    public MftRecordBuilder FileName(long parentRecordNumber, string name, byte ns = 1)
    {
        var nmeBytes = Encoding.Unicode.GetBytes(name);
        var content = new byte[0x42 + nmeBytes.Length];
        BitConverter.GetBytes(parentRecordNumber & 0xFFFFFFFFFFFFL).CopyTo(content, 0x00);
        content[0x40] = (byte)name.Length;
        content[0x41] = ns;
        nmeCopy(nmeBytes, content);
        _attrs.Add(Attr(0x30, content));
        return this;

        static void nmeCopy(byte[] src, byte[] dst) => src.CopyTo(dst, 0x42);
    }

    public MftRecordBuilder DataResident(long size)
    {
        _attrs.Add(Attr(0x80, new byte[(int)size], residentValueLength: (uint)size));
        return this;
    }

    public MftRecordBuilder DataNonResident(long realSize)
    {
        // Non-resident header (no data runs needed for size): RealSize at content-header 0x30.
        var header = new byte[0x40];
        BitConverter.GetBytes(realSize).CopyTo(header, 0x30);
        _attrs.Add(Attr(0x80, header, nonResident: true));
        return this;
    }

    public byte[] Build()
    {
        var rec = new byte[_recordSize];
        Encoding.ASCII.GetBytes("FILE").CopyTo(rec, 0);
        int sectors = _recordSize / _sectorSize;
        ushort usaOffset = 0x30, usaCount = (ushort)(sectors + 1);
        BitConverter.GetBytes(usaOffset).CopyTo(rec, 0x04);
        BitConverter.GetBytes(usaCount).CopyTo(rec, 0x06);
        BitConverter.GetBytes((ushort)1).CopyTo(rec, 0x10);  // sequence number
        BitConverter.GetBytes((ushort)(_baseRecord == 0 ? 1 : 0)).CopyTo(rec, 0x12);
        ushort flags = (ushort)((_inUse ? 0x01 : 0) | (_isDir ? 0x02 : 0));
        BitConverter.GetBytes(flags).CopyTo(rec, 0x16);
        BitConverter.GetBytes(_baseRecord).CopyTo(rec, 0x20);

        int firstAttr = usaOffset + usaCount * 2;
        firstAttr = (firstAttr + 7) & ~7; // 8-byte align
        BitConverter.GetBytes((ushort)firstAttr).CopyTo(rec, 0x14);

        int pos = firstAttr;
        foreach (var a in _attrs) { a.CopyTo(rec, pos); pos += a.Length; }
        BitConverter.GetBytes(0xFFFFFFFFu).CopyTo(rec, pos); // terminator
        pos += 4;
        BitConverter.GetBytes((uint)pos).CopyTo(rec, 0x18);  // BytesInUse

        // Update sequence array: USN + saved per-sector tail bytes, then stamp sector tails.
        ushort usn = 0xBEEF;
        BitConverter.GetBytes(usn).CopyTo(rec, usaOffset);
        for (var i = 0; i < sectors; i++)
        {
            int tail = (i + 1) * _sectorSize - 2;
            // save real bytes into USA, write USN into the tail
            rec[usaOffset + 2 + i * 2] = rec[tail];
            rec[usaOffset + 2 + i * 2 + 1] = rec[tail + 1];
            rec[tail] = (byte)(usn & 0xFF);
            rec[tail + 1] = (byte)(usn >> 8);
        }
        return rec;
    }

    private static byte[] Attr(uint type, byte[] content, bool nonResident = false, uint residentValueLength = 0)
    {
        if (nonResident)
        {
            // `content` is the 0x40 non-resident header carrying RealSize at offset 0x30.
            var a = (byte[])content.Clone();
            BitConverter.GetBytes(type).CopyTo(a, 0x00);
            BitConverter.GetBytes((uint)a.Length).CopyTo(a, 0x04);
            a[0x08] = 1; // non-resident
            a[0x09] = 0; // unnamed
            return a;
        }
        var r = new byte[0x18 + content.Length];
        BitConverter.GetBytes(type).CopyTo(r, 0x00);
        BitConverter.GetBytes((uint)r.Length).CopyTo(r, 0x04);
        r[0x08] = 0; // resident
        r[0x09] = 0; // unnamed
        BitConverter.GetBytes(residentValueLength == 0 ? (uint)content.Length : residentValueLength).CopyTo(r, 0x10);
        BitConverter.GetBytes((ushort)0x18).CopyTo(r, 0x14); // value offset
        content.CopyTo(r, 0x18);
        return r;
    }
}
```

- [ ] **Step 2: Build the test project to confirm the helper compiles**

Run: `dotnet build tests/NetSearch.Core.Tests`
Expected: build succeeds (helper is unused so far — a warning is fine).

- [ ] **Step 3: Commit**

```bash
git add tests/NetSearch.Core.Tests/Native/MftRecordBuilder.cs
git commit -m "test(mft): FILE-record builder for parser tests"
```

---

### Task 4: FILE-record parser

**Files:**
- Create: `src/NetSearch.Core/Native/MftRecordParser.cs`
- Test: `tests/NetSearch.Core.Tests/Native/MftRecordParserTests.cs`

**Interfaces:**
- Consumes: `ParsedMftRecord`, `MftTime` (Task 1); `MftRecordBuilder` (Task 3).
- Produces: `static bool MftRecordParser.TryParse(Span<byte> record, int bytesPerSector, out ParsedMftRecord result)`. Returns `false` for non-`FILE`, not-in-use, or extension (non-zero base) records. Applies fixups in place.

- [ ] **Step 1: Write the failing tests**

```csharp
using Xunit;
using NetSearch.Core.Native;
using NetSearch.Core.Tests.Native;

namespace NetSearch.Core.Tests.Native;

public class MftRecordParserTests
{
    private const long Mod = 116444736000000000L + 100L * 10_000_000L; // epoch +100s

    [Fact]
    public void Parses_file_with_resident_data_size_name_and_parent()
    {
        var rec = new MftRecordBuilder()
            .StandardInformation(Mod)
            .FileName(parentRecordNumber: 5, "Report.txt", ns: 1)
            .DataResident(42)
            .Build();

        Assert.True(MftRecordParser.TryParse(rec, 512, out var r));
        Assert.False(r.IsDir);
        Assert.Equal(5, r.ParentRecordNumber);
        Assert.Equal("Report.txt", r.Name);
        Assert.Equal(42, r.Size);
        Assert.Equal(100, r.ModifiedUnix);
    }

    [Fact]
    public void Uses_real_size_for_non_resident_data()
    {
        var rec = new MftRecordBuilder()
            .StandardInformation(Mod).FileName(5, "big.bin", 1).DataNonResident(1_000_000).Build();
        Assert.True(MftRecordParser.TryParse(rec, 512, out var r));
        Assert.Equal(1_000_000, r.Size);
    }

    [Fact]
    public void Directory_has_zero_size_and_isdir_true()
    {
        var rec = new MftRecordBuilder().AsDirectory()
            .StandardInformation(Mod).FileName(5, "Docs", 1).Build();
        Assert.True(MftRecordParser.TryParse(rec, 512, out var r));
        Assert.True(r.IsDir);
        Assert.Equal(0, r.Size);
        Assert.Equal("Docs", r.Name);
    }

    [Fact]
    public void Prefers_win32_name_over_dos_short_name()
    {
        var rec = new MftRecordBuilder()
            .StandardInformation(Mod)
            .FileName(5, "LONGNA~1", ns: 2)            // DOS 8.3 first
            .FileName(5, "LongName.txt", ns: 1)        // Win32 second
            .DataResident(1).Build();
        Assert.True(MftRecordParser.TryParse(rec, 512, out var r));
        Assert.Equal("LongName.txt", r.Name);
    }

    [Fact]
    public void Rejects_not_in_use_and_extension_records()
    {
        var free = new MftRecordBuilder().InUse(false).StandardInformation(Mod).FileName(5, "x", 1).Build();
        Assert.False(MftRecordParser.TryParse(free, 512, out _));

        var ext = new MftRecordBuilder().BaseRecord(7).StandardInformation(Mod).FileName(5, "x", 1).DataResident(1).Build();
        Assert.False(MftRecordParser.TryParse(ext, 512, out _));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~MftRecordParserTests"`
Expected: FAIL — `MftRecordParser` not defined.

- [ ] **Step 3: Write minimal implementation**

```csharp
using System.Buffers.Binary;
using System.Text;

namespace NetSearch.Core.Native;

public static class MftRecordParser
{
    private const uint Signature = 0x454C4946; // "FILE" little-endian
    private const uint AttrEnd = 0xFFFFFFFF;
    private const uint StandardInformation = 0x10, FileName = 0x30, Data = 0x80;

    public static bool TryParse(Span<byte> record, int bytesPerSector, out ParsedMftRecord result)
    {
        result = default;
        if (record.Length < 48) return false;
        if (BinaryPrimitives.ReadUInt32LittleEndian(record) != Signature) return false;

        ushort usaOffset = BinaryPrimitives.ReadUInt16LittleEndian(record[0x04..]);
        ushort usaCount = BinaryPrimitives.ReadUInt16LittleEndian(record[0x06..]);
        ushort flags = BinaryPrimitives.ReadUInt16LittleEndian(record[0x16..]);
        if ((flags & 0x01) == 0) return false;                 // not in use
        if (BinaryPrimitives.ReadInt64LittleEndian(record[0x20..]) != 0) return false; // extension record

        ApplyFixups(record, usaOffset, usaCount, bytesPerSector);

        bool isDir = (flags & 0x02) != 0;
        long size = 0, modified = 0, parent = 0;
        string? name = null; byte bestNs = 255;

        int pos = BinaryPrimitives.ReadUInt16LittleEndian(record[0x14..]);
        while (pos + 8 <= record.Length)
        {
            uint type = BinaryPrimitives.ReadUInt32LittleEndian(record[pos..]);
            if (type == AttrEnd) break;
            int len = (int)BinaryPrimitives.ReadUInt32LittleEndian(record[(pos + 4)..]);
            if (len <= 0 || pos + len > record.Length) break;
            byte nonResident = record[pos + 0x08];
            byte nameLen = record[pos + 0x09];

            if (type == StandardInformation && nonResident == 0)
            {
                int vo = BinaryPrimitives.ReadUInt16LittleEndian(record[(pos + 0x14)..]);
                modified = MftTime.FileTimeToUnixSeconds(
                    BinaryPrimitives.ReadInt64LittleEndian(record[(pos + vo + 0x08)..]));
            }
            else if (type == FileName && nonResident == 0)
            {
                int vo = BinaryPrimitives.ReadUInt16LittleEndian(record[(pos + 0x14)..]);
                int c = pos + vo;
                long p = BinaryPrimitives.ReadInt64LittleEndian(record[c..]) & 0xFFFFFFFFFFFFL;
                byte fnLen = record[c + 0x40];
                byte ns = record[c + 0x41];
                // Prefer Win32(1)/Win32&DOS(3) over POSIX(0) over DOS(2).
                int rank = ns switch { 1 => 0, 3 => 0, 0 => 1, _ => 2 };
                int bestRank = bestNs switch { 1 => 0, 3 => 0, 0 => 1, 255 => 9, _ => 2 };
                if (rank < bestRank)
                {
                    name = Encoding.Unicode.GetString(record.Slice(c + 0x42, fnLen * 2));
                    parent = p; bestNs = ns;
                }
            }
            else if (type == Data && nameLen == 0)
            {
                if (nonResident == 0)
                    size = BinaryPrimitives.ReadUInt32LittleEndian(record[(pos + 0x10)..]);
                else
                    size = BinaryPrimitives.ReadInt64LittleEndian(record[(pos + 0x30)..]);
            }
            pos += len;
        }

        if (name is null) return false;
        result = new ParsedMftRecord(isDir, parent, name, isDir ? 0 : size, modified);
        return true;
    }

    private static void ApplyFixups(Span<byte> record, int usaOffset, int usaCount, int bytesPerSector)
    {
        if (usaCount < 1) return;
        int sectors = usaCount - 1;
        for (var i = 0; i < sectors; i++)
        {
            int tail = (i + 1) * bytesPerSector - 2;
            int src = usaOffset + 2 + i * 2;
            if (tail + 2 > record.Length || src + 2 > record.Length) break;
            record[tail] = record[src];
            record[tail + 1] = record[src + 1];
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~MftRecordParserTests"`
Expected: PASS (5 tests).

- [ ] **Step 5: Commit**

```bash
git add src/NetSearch.Core/Native/MftRecordParser.cs tests/NetSearch.Core.Tests/Native/MftRecordParserTests.cs
git commit -m "feat(mft): FILE-record parser (fixups, name/size/date attributes)"
```

---

### Task 5: Path reconstruction

**Files:**
- Create: `src/NetSearch.Core/Native/PathBuilder.cs`
- Test: `tests/NetSearch.Core.Tests/Native/PathBuilderTests.cs`

**Interfaces:**
- Consumes: `MftNode` (Task 1).
- Produces: `static IReadOnlyDictionary<long, string> PathBuilder.BuildDirectoryPaths(string volumeRoot, IReadOnlyDictionary<long, MftNode> nodes)` — maps each resolvable **directory** record number to the parent-path string used for its children (root `#5` → `volumeRoot + "\"`). Orphans/cycles are omitted.

- [ ] **Step 1: Write the failing test**

```csharp
using Xunit;
using NetSearch.Core.Native;

namespace NetSearch.Core.Tests.Native;

public class PathBuilderTests
{
    [Fact]
    public void Root_and_nested_directories_resolve_to_parent_path_form()
    {
        var nodes = new Dictionary<long, MftNode>
        {
            [5]  = new("", 5, true),                 // volume root
            [10] = new("Users", 5, true),
            [11] = new("Me", 10, true),
            [12] = new("note.txt", 11, false),       // a file — not a directory path
        };

        var dirs = PathBuilder.BuildDirectoryPaths("C:", nodes);

        Assert.Equal(@"C:\", dirs[5]);
        Assert.Equal(@"C:\Users", dirs[10]);
        Assert.Equal(@"C:\Users\Me", dirs[11]);
        Assert.False(dirs.ContainsKey(12)); // files are not directory paths
    }

    [Fact]
    public void Orphans_and_cycles_are_dropped()
    {
        var nodes = new Dictionary<long, MftNode>
        {
            [5]  = new("", 5, true),
            [20] = new("Ghost", 999, true),  // parent missing → orphan
            [30] = new("A", 31, true),       // cycle 30↔31
            [31] = new("B", 30, true),
        };
        var dirs = PathBuilder.BuildDirectoryPaths("C:", nodes);
        Assert.True(dirs.ContainsKey(5));
        Assert.False(dirs.ContainsKey(20));
        Assert.False(dirs.ContainsKey(30));
        Assert.False(dirs.ContainsKey(31));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~PathBuilderTests"`
Expected: FAIL — `PathBuilder` not defined.

- [ ] **Step 3: Write minimal implementation**

```csharp
using System.IO;

namespace NetSearch.Core.Native;

public static class PathBuilder
{
    private const long RootRecord = 5;

    public static IReadOnlyDictionary<long, string> BuildDirectoryPaths(
        string volumeRoot, IReadOnlyDictionary<long, MftNode> nodes)
    {
        var result = new Dictionary<long, string>();
        var resolving = new HashSet<long>();

        string? Resolve(long rec)
        {
            if (rec == RootRecord) return volumeRoot + Path.DirectorySeparatorChar; // "C:\"
            if (result.TryGetValue(rec, out var cached)) return cached;
            if (!nodes.TryGetValue(rec, out var node) || !node.IsDir) return null;
            if (!resolving.Add(rec)) return null; // cycle

            var parentPath = Resolve(node.ParentRecordNumber);
            resolving.Remove(rec);
            if (parentPath is null) return null;

            var full = Path.Combine(parentPath, node.Name);
            result[rec] = full;
            return full;
        }

        foreach (var rec in nodes.Keys) Resolve(rec);
        result[RootRecord] = volumeRoot + Path.DirectorySeparatorChar;
        return result;
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter "FullyQualifiedName~PathBuilderTests"`
Expected: PASS (2 tests).

- [ ] **Step 5: Commit**

```bash
git add src/NetSearch.Core/Native/PathBuilder.cs tests/NetSearch.Core.Tests/Native/PathBuilderTests.cs
git commit -m "feat(mft): reconstruct directory paths from parent-FRN chain"
```

---

### Task 6: Entry assembler (records + paths → FileEntry, filtered to root)

**Files:**
- Create: `src/NetSearch.Core/Native/MftEntryAssembler.cs`
- Test: `tests/NetSearch.Core.Tests/Native/MftEntryAssemblerTests.cs`

**Interfaces:**
- Consumes: `ParsedMftRecord`, `MftNode`, `PathBuilder`, `FileEntry.FromComponents`.
- Produces: `static IEnumerable<FileEntry> MftEntryAssembler.Assemble(int rootId, string volumeRoot, string rootFilter, IReadOnlyDictionary<long, ParsedMftRecord> records)` — yields a `FileEntry` for every record whose full path is under `rootFilter` (case-insensitive), directories included.

- [ ] **Step 1: Write the failing test**

```csharp
using Xunit;
using NetSearch.Core.Native;
using NetSearch.Core.Models;

namespace NetSearch.Core.Tests.Native;

public class MftEntryAssemblerTests
{
    [Fact]
    public void Builds_entries_under_the_requested_root_only()
    {
        var records = new Dictionary<long, ParsedMftRecord>
        {
            [5]  = new(true, 5, "", 0, 0),
            [10] = new(true, 5, "Users", 0, 100),
            [11] = new(true, 10, "Me", 0, 100),
            [12] = new(false, 11, "note.txt", 7, 200),
            [40] = new(false, 5, "pagefile.sys", 999, 0), // outside C:\Users\Me
        };

        var entries = MftEntryAssembler
            .Assemble(rootId: 3, volumeRoot: "C:", rootFilter: @"C:\Users\Me", records)
            .ToList();

        Assert.Contains(entries, e => e.Name == "note.txt" && e.Size == 7
            && e.ParentPath == @"C:\Users\Me" && e.FullPath == @"C:\Users\Me\note.txt");
        Assert.Contains(entries, e => e.Name == "Me" && e.IsDir);
        Assert.DoesNotContain(entries, e => e.Name == "pagefile.sys");
    }

    [Fact]
    public void Sibling_directory_sharing_a_name_prefix_is_excluded()
    {
        var records = new Dictionary<long, ParsedMftRecord>
        {
            [5]  = new(true, 5, "", 0, 0),
            [10] = new(true, 5, "Me", 0, 0),
            [11] = new(false, 10, "in.txt", 1, 0),
            [20] = new(true, 5, "MeToo", 0, 0),    // shares the "Me" prefix
            [21] = new(false, 20, "out.txt", 1, 0),
        };

        var entries = MftEntryAssembler.Assemble(1, "C:", @"C:\Me", records).ToList();

        Assert.Contains(entries, e => e.Name == "in.txt");
        Assert.DoesNotContain(entries, e => e.Name == "out.txt");
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~MftEntryAssemblerTests"`
Expected: FAIL — `MftEntryAssembler` not defined.

- [ ] **Step 3: Write minimal implementation**

```csharp
using NetSearch.Core.Models;

namespace NetSearch.Core.Native;

public static class MftEntryAssembler
{
    public static IEnumerable<FileEntry> Assemble(
        int rootId, string volumeRoot, string rootFilter,
        IReadOnlyDictionary<long, ParsedMftRecord> records)
    {
        var nodes = records.ToDictionary(
            kv => kv.Key, kv => new MftNode(kv.Value.Name, kv.Value.ParentRecordNumber, kv.Value.IsDir));
        var dirPaths = PathBuilder.BuildDirectoryPaths(volumeRoot, nodes);

        var sep = System.IO.Path.DirectorySeparatorChar;
        foreach (var (rec, r) in records)
        {
            if (rec == 5) continue; // the volume root itself is not an entry
            if (!dirPaths.TryGetValue(r.ParentRecordNumber, out var parentPath)) continue;

            var full = System.IO.Path.Combine(parentPath, r.Name);
            // Prefix match on a separator boundary so "C:\Me" does not capture "C:\MeToo".
            bool underRoot = full.Equals(rootFilter, StringComparison.OrdinalIgnoreCase)
                || full.StartsWith(rootFilter + sep, StringComparison.OrdinalIgnoreCase);
            if (!underRoot) continue;

            yield return FileEntry.FromComponents(rootId, r.Name, parentPath, r.IsDir, r.Size, r.ModifiedUnix);
        }
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter "FullyQualifiedName~MftEntryAssemblerTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/NetSearch.Core/Native/MftEntryAssembler.cs tests/NetSearch.Core.Tests/Native/MftEntryAssemblerTests.cs
git commit -m "feat(mft): assemble FileEntry rows from parsed records, filtered to root"
```

---

### Task 7: Backend strategy selector

**Files:**
- Create: `src/NetSearch.Core/Native/IEnvironmentProbe.cs`
- Create: `src/NetSearch.Core/Native/IndexStrategySelector.cs`
- Test: `tests/NetSearch.Core.Tests/Native/IndexStrategySelectorTests.cs`

**Interfaces:**
- Consumes: `VolumeInfo`, `IndexBackend` (Task 1).
- Produces:
  - `interface IEnvironmentProbe { bool IsWindowsElevated { get; } bool TryGetVolume(string rootPath, out VolumeInfo info); }`
  - `static IndexBackend IndexStrategySelector.Select(string rootPath, IEnvironmentProbe probe)`

- [ ] **Step 1: Write the failing test**

```csharp
using Xunit;
using NetSearch.Core.Native;

namespace NetSearch.Core.Tests.Native;

public class IndexStrategySelectorTests
{
    private sealed class FakeProbe : IEnvironmentProbe
    {
        public bool IsWindowsElevated { get; init; }
        public VolumeInfo? Volume { get; init; }
        public bool TryGetVolume(string rootPath, out VolumeInfo info)
        { info = Volume ?? default; return Volume is not null; }
    }

    [Fact]
    public void Mft_when_elevated_local_fixed_ntfs()
    {
        var probe = new FakeProbe { IsWindowsElevated = true, Volume = new VolumeInfo("NTFS", true, false, 'C') };
        Assert.Equal(IndexBackend.Mft, IndexStrategySelector.Select(@"C:\Data", probe));
    }

    [Theory]
    [InlineData(false, "NTFS", true, false)]  // not elevated
    [InlineData(true,  "exFAT", true, false)] // not NTFS
    [InlineData(true,  "NTFS", false, false)] // not fixed
    [InlineData(true,  "NTFS", true,  true)]  // UNC
    public void Crawler_otherwise(bool elevated, string fs, bool fixedDrive, bool unc)
    {
        var probe = new FakeProbe { IsWindowsElevated = elevated, Volume = new VolumeInfo(fs, fixedDrive, unc, 'C') };
        Assert.Equal(IndexBackend.Crawler, IndexStrategySelector.Select(@"C:\Data", probe));
    }

    [Fact]
    public void Crawler_when_volume_unknown()
        => Assert.Equal(IndexBackend.Crawler,
            IndexStrategySelector.Select(@"\\srv\share", new FakeProbe { IsWindowsElevated = true }));
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~IndexStrategySelectorTests"`
Expected: FAIL — types not defined.

- [ ] **Step 3: Write minimal implementation**

```csharp
// IEnvironmentProbe.cs
namespace NetSearch.Core.Native;

public interface IEnvironmentProbe
{
    bool IsWindowsElevated { get; }
    bool TryGetVolume(string rootPath, out VolumeInfo info);
}
```

```csharp
// IndexStrategySelector.cs
namespace NetSearch.Core.Native;

public static class IndexStrategySelector
{
    public static IndexBackend Select(string rootPath, IEnvironmentProbe probe)
    {
        if (!OperatingSystem.IsWindows() || !probe.IsWindowsElevated) return IndexBackend.Crawler;
        if (!probe.TryGetVolume(rootPath, out var v)) return IndexBackend.Crawler;
        if (v.IsUnc || !v.IsFixed) return IndexBackend.Crawler;
        return string.Equals(v.FileSystem, "NTFS", StringComparison.OrdinalIgnoreCase)
            ? IndexBackend.Mft : IndexBackend.Crawler;
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter "FullyQualifiedName~IndexStrategySelectorTests"`
Expected: PASS (6 cases).

- [ ] **Step 5: Commit**

```bash
git add src/NetSearch.Core/Native/IEnvironmentProbe.cs src/NetSearch.Core/Native/IndexStrategySelector.cs tests/NetSearch.Core.Tests/Native/IndexStrategySelectorTests.cs
git commit -m "feat(mft): per-root backend selection with NTFS/elevation/UNC rules"
```

---

### Task 8: Win32 interop + volume reader (manual verification)

**Files:**
- Create: `src/NetSearch.Core/Native/NativeMethods.cs`
- Create: `src/NetSearch.Core/Native/NtfsVolume.cs`

**Interfaces:**
- Produces:
  - `NtfsVolume.Open(char driveLetter) : NtfsVolume` (IDisposable; throws on failure)
  - `int NtfsVolume.BytesPerFileRecordSegment { get; }`, `int BytesPerCluster { get; }`, `long MftStartLcn { get; }`
  - `byte[] NtfsVolume.ReadClusters(long lcn, int clusterCount)`

No automated test (needs admin + live volume). Verified in Task 10's manual checklist.

- [ ] **Step 1: Add P/Invoke surface**

```csharp
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace NetSearch.Core.Native;

[SupportedOSPlatform("windows")]
internal static partial class NativeMethods
{
    public const uint GENERIC_READ = 0x80000000;
    public const uint FILE_SHARE_READ = 0x1, FILE_SHARE_WRITE = 0x2;
    public const uint OPEN_EXISTING = 3;
    public const uint FSCTL_GET_NTFS_VOLUME_DATA = 0x00090064;

    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    public static partial SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr sec, uint disp, uint flags, IntPtr template);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DeviceIoControl(SafeFileHandle h, uint code, IntPtr inBuf, uint inSize,
        byte[] outBuf, uint outSize, out uint returned, IntPtr overlapped);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetFilePointerEx(SafeFileHandle h, long distance, out long newPointer, uint method);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ReadFile(SafeFileHandle h, byte[] buffer, uint toRead, out uint read, IntPtr overlapped);
}
```

- [ ] **Step 2: Implement the volume reader**

```csharp
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace NetSearch.Core.Native;

[SupportedOSPlatform("windows")]
public sealed class NtfsVolume : IDisposable
{
    private readonly SafeFileHandle _handle;
    public int BytesPerFileRecordSegment { get; }
    public int BytesPerCluster { get; }
    public long MftStartLcn { get; }

    private NtfsVolume(SafeFileHandle h, int recSeg, int cluster, long mftLcn)
    { _handle = h; BytesPerFileRecordSegment = recSeg; BytesPerCluster = cluster; MftStartLcn = mftLcn; }

    public static NtfsVolume Open(char driveLetter)
    {
        var h = NativeMethods.CreateFile($@"\\.\{char.ToUpperInvariant(driveLetter)}:",
            NativeMethods.GENERIC_READ, NativeMethods.FILE_SHARE_READ | NativeMethods.FILE_SHARE_WRITE,
            IntPtr.Zero, NativeMethods.OPEN_EXISTING, 0, IntPtr.Zero);
        if (h.IsInvalid) throw new IOException($"open volume {driveLetter}: failed", Marshal.GetLastWin32Error());

        var buf = new byte[512];
        if (!NativeMethods.DeviceIoControl(h, NativeMethods.FSCTL_GET_NTFS_VOLUME_DATA, IntPtr.Zero, 0,
                buf, (uint)buf.Length, out _, IntPtr.Zero))
        { var e = Marshal.GetLastWin32Error(); h.Dispose(); throw new IOException("NTFS volume data failed", e); }

        // NTFS_VOLUME_DATA_BUFFER offsets:
        long bytesPerCluster = BitConverter.ToInt32(buf, 0x18);   // BytesPerCluster
        long mftStartLcn = BitConverter.ToInt64(buf, 0x20);        // MftStartLcn
        int bytesPerRecord = BitConverter.ToInt32(buf, 0x40);      // BytesPerFileRecordSegment
        return new NtfsVolume(h, bytesPerRecord, (int)bytesPerCluster, mftStartLcn);
    }

    public byte[] ReadClusters(long lcn, int clusterCount)
    {
        long offset = lcn * BytesPerCluster;
        if (!NativeMethods.SetFilePointerEx(_handle, offset, out _, 0))
            throw new IOException("seek failed", Marshal.GetLastWin32Error());
        int total = clusterCount * BytesPerCluster;
        var buffer = new byte[total];
        var chunk = new byte[BytesPerCluster];  // volume reads must stay cluster-aligned
        int done = 0;
        while (done < total)
        {
            if (!NativeMethods.ReadFile(_handle, chunk, (uint)BytesPerCluster, out var read, IntPtr.Zero) || read == 0)
                throw new IOException("read failed", Marshal.GetLastWin32Error());
            Buffer.BlockCopy(chunk, 0, buffer, done, (int)read);
            done += (int)read;
        }
        return buffer;
    }

    public void Dispose() => _handle.Dispose();
}
```

- [ ] **Step 3: Build to confirm interop compiles**

Run: `dotnet build src/NetSearch.Core`
Expected: build succeeds (0 errors).

- [ ] **Step 4: Commit**

```bash
git add src/NetSearch.Core/Native/NativeMethods.cs src/NetSearch.Core/Native/NtfsVolume.cs
git commit -m "feat(mft): Win32 interop and raw NTFS volume cluster reader"
```

---

### Task 9: MFT enumerator + Windows environment probe (manual verification)

**Files:**
- Create: `src/NetSearch.Core/Native/MftEnumerator.cs`
- Create: `src/NetSearch.Core/Native/WindowsEnvironmentProbe.cs`

**Interfaces:**
- Consumes: `NtfsVolume`, `MftRecordParser`, `MftEntryAssembler`, `DataRunParser`, `FileEntry`, `CrawlProgress`.
- Produces:
  - `void MftEnumerator.Enumerate(int rootId, string rootPath, Action<IReadOnlyList<FileEntry>> onBatch, CancellationToken ct, IProgress<CrawlProgress>? progress = null)` — same shape as `Crawler.Crawl`’s callback contract.
  - `WindowsEnvironmentProbe : IEnvironmentProbe`.

- [ ] **Step 1: Implement the enumerator**

```csharp
using System.Runtime.Versioning;
using NetSearch.Core.Indexing;
using NetSearch.Core.Models;

namespace NetSearch.Core.Native;

[SupportedOSPlatform("windows")]
public sealed class MftEnumerator
{
    private readonly int _batchSize;
    public MftEnumerator(int batchSize = 4096) => _batchSize = Math.Max(1, batchSize);

    public void Enumerate(int rootId, string rootPath, Action<IReadOnlyList<FileEntry>> onBatch,
        CancellationToken ct, IProgress<CrawlProgress>? progress = null)
    {
        var driveLetter = char.ToUpperInvariant(rootPath[0]);
        var volumeRoot = driveLetter + ":";
        using var vol = NtfsVolume.Open(driveLetter);

        // MFT record 0 describes $MFT; follow its $DATA runs to read the whole table.
        var rec0 = vol.ReadClusters(vol.MftStartLcn,
            Math.Max(1, vol.BytesPerFileRecordSegment / vol.BytesPerCluster));
        var extents = ReadMftExtents(rec0, vol.BytesPerFileRecordSegment);

        var records = new Dictionary<long, ParsedMftRecord>();
        long recordNumber = 0;
        int recSize = vol.BytesPerFileRecordSegment;
        foreach (var run in extents)
        {
            ct.ThrowIfCancellationRequested();
            if (run.Lcn < 0) { recordNumber += run.ClusterCount * vol.BytesPerCluster / recSize; continue; }
            var data = vol.ReadClusters(run.Lcn, (int)run.ClusterCount);
            for (var off = 0; off + recSize <= data.Length; off += recSize, recordNumber++)
            {
                if (MftRecordParser.TryParse(data.AsSpan(off, recSize), 512, out var r))
                    records[recordNumber] = r;
            }
            progress?.Report(new CrawlProgress(records.Count, volumeRoot));
        }

        var batch = new List<FileEntry>(_batchSize);
        foreach (var e in MftEntryAssembler.Assemble(rootId, volumeRoot, NormalizeRoot(rootPath), records))
        {
            ct.ThrowIfCancellationRequested();
            batch.Add(e);
            if (batch.Count >= _batchSize) { onBatch(batch); batch = new List<FileEntry>(_batchSize); }
        }
        if (batch.Count > 0) onBatch(batch);
        progress?.Report(new CrawlProgress(records.Count, volumeRoot));
    }

    private static string NormalizeRoot(string rootPath) => rootPath.TrimEnd('\\', '/');

    private static IReadOnlyList<DataRun> ReadMftExtents(byte[] rec0, int recSize)
    {
        // Parse record 0's unnamed non-resident $DATA and decode its run list.
        var span = rec0.AsSpan(0, recSize);
        MftRecordParser.TryParse(span, 512, out _); // applies fixups in place
        int pos = BitConverter.ToUInt16(rec0, 0x14);
        while (pos + 8 <= recSize)
        {
            uint type = BitConverter.ToUInt32(rec0, pos);
            if (type == 0xFFFFFFFF) break;
            int len = (int)BitConverter.ToUInt32(rec0, pos + 4);
            byte nonResident = rec0[pos + 0x08];
            byte nameLen = rec0[pos + 0x09];
            if (type == 0x80 && nameLen == 0 && nonResident == 1)
            {
                int runOff = BitConverter.ToUInt16(rec0, pos + 0x20);
                return DataRunParser.Parse(rec0.AsSpan(pos + runOff, len - runOff));
            }
            if (len <= 0) break;
            pos += len;
        }
        return Array.Empty<DataRun>();
    }
}
```

- [ ] **Step 2: Implement the environment probe**

```csharp
using System.IO;
using System.Runtime.Versioning;
using System.Security.Principal;

namespace NetSearch.Core.Native;

[SupportedOSPlatform("windows")]
public sealed class WindowsEnvironmentProbe : IEnvironmentProbe
{
    public bool IsWindowsElevated
    {
        get
        {
            using var id = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
        }
    }

    public bool TryGetVolume(string rootPath, out VolumeInfo info)
    {
        info = default;
        if (string.IsNullOrWhiteSpace(rootPath) || rootPath.StartsWith(@"\\")) { info = new VolumeInfo("", false, true, '\0'); return true; }
        var root = Path.GetPathRoot(Path.GetFullPath(rootPath));
        if (string.IsNullOrEmpty(root)) return false;
        try
        {
            var di = new DriveInfo(root);
            info = new VolumeInfo(di.DriveFormat, di.DriveType == DriveType.Fixed, false, root[0]);
            return true;
        }
        catch { return false; }
    }
}
```

- [ ] **Step 3: Build to confirm compilation**

Run: `dotnet build`
Expected: build succeeds (0 errors).

- [ ] **Step 4: Commit**

```bash
git add src/NetSearch.Core/Native/MftEnumerator.cs src/NetSearch.Core/Native/WindowsEnvironmentProbe.cs
git commit -m "feat(mft): MFT enumerator and Windows elevation/volume probe"
```

---

### Task 10: Wire backend selection into indexing + manual verification

**Files:**
- Modify: `src/NetSearch.Core/Indexing/IndexManager.cs` (add an MFT-capable update path)
- Modify: `src/NetSearch.App/ViewModels/MainViewModel.cs:156-165` (choose backend per root, fall back on error)
- Test: `tests/NetSearch.Core.Tests/IndexManagerMftTests.cs`
- Create: `docs/superpowers/manual-checks/mft-bulk-scan.md`

**Interfaces:**
- Consumes: `IndexStrategySelector`, `MftEnumerator`, `WindowsEnvironmentProbe`, existing `IndexManager.UpdateRoot`.
- Produces: `IndexResult IndexManager.UpdateRootWith(int rootId, string rootPath, Func<Action<IReadOnlyList<FileEntry>>, CancellationToken, IProgress<CrawlProgress>?, int> enumerate, CancellationToken ct, IProgress<CrawlProgress>? progress)` — the existing snapshot diff parameterised by an enumeration delegate (the `int` return is the crawl/enumeration count). `UpdateRoot` keeps its current signature and delegates to it using the crawler.

- [ ] **Step 1: Write the failing test (diff still works when the enumeration is supplied externally)**

```csharp
using Xunit;
using NetSearch.Core.Indexing;
using NetSearch.Core.Models;
using NetSearch.Core.Storage;

namespace NetSearch.Core.Tests;

public class IndexManagerMftTests : IDisposable
{
    private readonly string _db = Path.Combine(Path.GetTempPath(), $"mft_{Guid.NewGuid():N}.db");

    [Fact]
    public void UpdateRootWith_applies_an_arbitrary_enumeration_like_the_crawler()
    {
        using var store = new IndexStore(_db); store.Initialize();
        var id = store.UpsertRoot(@"C:\Data");
        var mgr = new IndexManager(store, () => new Indexing.Crawler(), () => 1);

        // Emit two entries through the same onBatch contract the crawler uses.
        int Enumerate(Action<IReadOnlyList<FileEntry>> onBatch, CancellationToken ct, IProgress<CrawlProgress>? p)
        {
            onBatch(new[]
            {
                FileEntry.FromComponents(id, "a.txt", @"C:\Data", false, 5, 10),
                FileEntry.FromComponents(id, "b.txt", @"C:\Data", false, 9, 20),
            });
            return 2;
        }

        var result = mgr.UpdateRootWith(id, @"C:\Data", Enumerate, CancellationToken.None, null);

        Assert.Equal(2, result.Added);
        Assert.Equal(2, store.LoadAll().Count);
    }

    public void Dispose()
    {
        if (File.Exists(_db)) File.Delete(_db);
        foreach (var s in new[] { "-wal", "-shm" }) if (File.Exists(_db + s)) File.Delete(_db + s);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~IndexManagerMftTests"`
Expected: FAIL — `UpdateRootWith` not defined.

- [ ] **Step 3: Refactor `IndexManager` to expose the diff over a supplied enumeration**

In `IndexManager.cs`, extract the body of `UpdateRoot` into `UpdateRootWith` and keep `UpdateRoot` as a crawler-backed caller:

```csharp
public IndexResult UpdateRoot(int rootId, string rootPath, CancellationToken ct, IProgress<CrawlProgress>? progress = null)
    => UpdateRootWith(rootId, rootPath,
        (onBatch, token, prog) => _crawlerFactory().Crawl(rootId, rootPath, onBatch, token, prog).Count,
        ct, progress);

public IndexResult UpdateRootWith(int rootId, string rootPath,
    Func<Action<IReadOnlyList<FileEntry>>, CancellationToken, IProgress<CrawlProgress>?, int> enumerate,
    CancellationToken ct, IProgress<CrawlProgress>? progress = null)
{
    var existing = _store.LoadByRoot(rootId).ToDictionary(e => e.FullPath, StringComparer.OrdinalIgnoreCase);
    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var added = 0; var updated = 0;

    enumerate(batch =>
    {
        foreach (var e in batch)
        {
            seen.Add(e.FullPath);
            if (!existing.TryGetValue(e.FullPath, out var old)) added++;
            else if (old.Size != e.Size || old.Modified != e.Modified || old.IsDir != e.IsDir) updated++;
        }
        _store.BulkUpsert(batch);
    }, ct, progress);

    var removedIds = existing.Where(kv => !seen.Contains(kv.Key)).Select(kv => kv.Value.Id).ToList();
    _store.RemoveByIds(removedIds);
    _store.SetRootIndexed(rootId, _clock());
    return new IndexResult(added, updated, removedIds.Count, 0);
}
```

- [ ] **Step 4: Run test to verify it passes (and the suite is green)**

Run: `dotnet test`
Expected: PASS — `IndexManagerMftTests` green, all prior tests still pass.

- [ ] **Step 5: Wire backend choice + fallback in `MainViewModel.RefreshAsync`**

Replace the per-root loop body (currently `MainViewModel.cs:156-165`) with:

```csharp
await Task.Run(() =>
{
    var probe = new NetSearch.Core.Native.WindowsEnvironmentProbe();
    var mgr = new IndexManager(_store, () => new Crawler(parallelism: _settings.CrawlParallelism));
    foreach (var path in _settings.Roots)
    {
        token.ThrowIfCancellationRequested();
        var id = _store.UpsertRoot(path);
        var backend = NetSearch.Core.Native.IndexStrategySelector.Select(path, probe);
        try
        {
            if (backend == NetSearch.Core.Native.IndexBackend.Mft && OperatingSystem.IsWindows())
            {
                var mft = new NetSearch.Core.Native.MftEnumerator();
                mgr.UpdateRootWith(id, path,
                    (onBatch, ct2, prog) => { mft.Enumerate(id, path, onBatch, ct2, prog); return 0; },
                    token, progress);
            }
            else
            {
                mgr.UpdateRoot(id, path, token, progress);
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception) // MFT path failed → transparent fallback to the crawler
        {
            mgr.UpdateRoot(id, path, token, progress);
        }
    }
}, token);
```

- [ ] **Step 6: Build + full suite**

Run: `dotnet build && dotnet test`
Expected: build 0 errors; all tests pass.

- [ ] **Step 7: Write the manual verification checklist**

Create `docs/superpowers/manual-checks/mft-bulk-scan.md`:

```markdown
# Manual check — MFT bulk scan (requires Administrator)

1. Add a local root (e.g. `C:\Users\<you>\Documents`) in Settings.
2. Run NetSearch **as Administrator**. Click Обновить.
   - Expect the scan to finish far faster than an unprivileged run; status shows the indexed count.
3. Spot-check 5 files in the grid: Name, Path, Size, and Modified date match Explorer.
4. Compare totals: run once as admin (MFT) and once normally (crawler) on the same root; counts should be within a small delta (open handles / transient files).
5. Filters: size and date filters return sensible results for the MFT-indexed root.
6. Fallback: run **not** as admin → app still works (crawler), no errors.
7. Non-NTFS / UNC root → always crawler, no errors.
```

- [ ] **Step 8: Commit**

```bash
git add src/NetSearch.Core/Indexing/IndexManager.cs src/NetSearch.App/ViewModels/MainViewModel.cs tests/NetSearch.Core.Tests/IndexManagerMftTests.cs docs/superpowers/manual-checks/mft-bulk-scan.md
git commit -m "feat(mft): select MFT backend per root with crawler fallback"
```

---

## Self-Review

**Spec coverage:** Native layer (Tasks 1–9) ✓; full size/date parity (Tasks 1,4,6) ✓; auto-detect + fallback (Tasks 7,10) ✓; strategy rules incl. UNC/non-NTFS (Task 7) ✓; `FileEntry.FromComponents` reuse (Task 6) ✓; parent-path semantics (Task 5) ✓; verification gap documented (Tasks 8–10) ✓; OS guards (Tasks 7–9) ✓. USN incremental is Phase 2 (separate plan) — out of scope here, by design.

**Placeholder scan:** No TBD/TODO. The one prose note (Task 8 `ReadFile` offset) is an implementation caution with explicit instructions, not a missing step.

**Type consistency:** `ParsedMftRecord`, `MftNode`, `DataRun`, `VolumeInfo`, `IndexBackend`, `IEnvironmentProbe.{IsWindowsElevated, TryGetVolume}`, `IndexStrategySelector.Select`, `MftEntryAssembler.Assemble`, `MftEnumerator.Enumerate`, `IndexManager.UpdateRootWith` are used identically across tasks.
