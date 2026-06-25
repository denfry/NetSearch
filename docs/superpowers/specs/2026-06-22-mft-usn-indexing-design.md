# MFT / USN-journal indexing for local NTFS roots

- **Date:** 2026-06-22
- **Status:** Approved (design)
- **Branch:** `feature/mft-usn-indexing`

## Goal

Index local NTFS drives the way WizFile / Everything do: read the volume's Master File
Table (MFT) directly instead of walking the directory tree, and keep the index fresh by
reading the NTFS USN change journal. A full scan of a whole volume drops from minutes of
per-directory I/O to a single sequential read of the MFT; subsequent refreshes process only
what changed.

This is an additional enumeration backend, selected per root. It never replaces the existing
parallel `Crawler`: network paths, non-NTFS volumes, and unprivileged runs keep using the
crawler unchanged.

## Decisions (locked)

1. **Full metadata parity** — parse the raw `$MFT` so MFT-sourced entries carry name, full
   path, **size, and modified date**, exactly like the crawler. The Size/Modified columns and
   the size/date filters keep working for local roots.
2. **Auto-detect + transparent fallback** — use the MFT path only when the process is elevated
   *and* the root is a local fixed NTFS volume; otherwise fall back to `Crawler`. Running
   unprivileged behaves exactly as today, just without the speedup.
3. **Bulk MFT + USN incremental, both in this feature**, sequenced: bulk scan first, then USN
   incremental.

## Approach chosen (MFT read)

Full parse of the raw `$MFT`: read MFT record 0, follow its `$DATA` run list to read the
entire MFT sequentially, parse every FILE record. One coherent mechanism that yields name +
path + size + date in a single pass. (`FSCTL_ENUM_USN_DATA` was considered but gives no size
or dates, so size/date would still force reading MFT records — no advantage over a full parse.)

## Architecture

New layer `src/NetSearch.Core/Native/`. Pure parsing logic is separated from P/Invoke so the
hard, bug-prone parts are unit-tested without admin rights or a live volume.

| File | Kind | Responsibility |
|---|---|---|
| `NativeMethods.cs` | interop | P/Invoke: `CreateFile`, `DeviceIoControl`, `ReadFile`, `SetFilePointerEx`, handle/`SafeHandle` types, FSCTL codes, structs. |
| `NtfsVolume.cs` | interop | Open `\\.\X:`; `FSCTL_GET_NTFS_VOLUME_DATA` (record size, bytes/cluster, MFT start LCN); raw cluster reads. |
| `DataRunParser.cs` | **pure** | Decode a non-resident attribute run list → ordered `(lcn, clusterCount)` extents (handles fragmented `$MFT`). |
| `MftRecordParser.cs` | **pure** | Parse one FILE record: `FILE` signature, **update-sequence (fixup) array**, attribute walk — `$STANDARD_INFORMATION` (modified time), `$FILE_NAME` (parent FRN + name, prefer Win32 over DOS 8.3), `$DATA` (size: resident length or non-resident real-size). Emits `MftRecord(frn, inUse, isDir, parentFrn, name, size, modifiedUnix)`. |
| `PathBuilder.cs` | **pure** | From the `frn → (name, parentFrn, isDir)` map, build full paths by walking the parent chain to the volume root (record #5). Memoizes directory paths; drops orphans / cycles. |
| `MftEnumerator.cs` | interop+glue | Orchestrates: open volume → read `$MFT` → parse records → build paths → filter to the requested root subtree → emit `FileEntry` batches via the same `onBatch` contract as `Crawler`. |
| `UsnJournal.cs` | interop (+pure parser) | `FSCTL_QUERY_USN_JOURNAL`, `FSCTL_READ_USN_JOURNAL`; `USN_RECORD_V2` parsing split into a pure function. |
| `WindowsEnvironment.cs` | interop | Probes: process elevation; volume file system == NTFS, drive type == fixed, path is drive-letter rooted (not UNC). |

### Conversion to the existing model

`MftEnumerator` produces `FileEntry` via `FileEntry.FromComponents(rootId, name, parentPath,
isDir, size, modifiedUnix)` — the same factory the crawler now uses, so downstream storage,
search, and the in-memory snapshot are unchanged. FILETIME (100 ns since 1601) is converted to
Unix seconds.

## Data flow

### Full scan (bulk MFT)
1. Open the volume, read `FSCTL_GET_NTFS_VOLUME_DATA`.
2. Read MFT record 0, parse its `$DATA` run list → MFT extents; read the MFT sequentially in
   chunks of `BytesPerFileRecordSegment`.
3. Parse each in-use FILE record → `MftRecord`.
4. `PathBuilder` reconstructs full paths; keep only entries under the requested root.
5. Emit `FileEntry` batches into the existing `IndexManager` snapshot diff (same upsert/remove).
6. On success, `FSCTL_QUERY_USN_JOURNAL` → persist `(usn_journal_id, usn_next)` for the volume.

### Incremental (USN)
1. On refresh, if `(usn_journal_id, usn_next)` is stored and still valid, `FSCTL_READ_USN_JOURNAL`
   from `usn_next` → change records (create / delete / rename / data-extend / etc.).
2. For created/modified FRNs, re-read that single MFT record by number (low 48 bits of the FRN)
   to get current size + modified without opening the file. Upsert.
3. For deletes, remove by FRN (stored on the row). For renames, update path from the
   `RENAME_OLD_NAME` / `RENAME_NEW_NAME` pair.
4. Advance and persist `usn_next`.
5. If the journal is gone, overflowed, or its `usn_journal_id` changed → full MFT rescan.

## Schema changes (migrations, idempotent)

- `entries.frn INTEGER` — nullable; set only for MFT-sourced rows; enables USN deltas by FRN.
- `roots.usn_journal_id INTEGER`, `roots.usn_next INTEGER` — per-root journal cursor.

Applied via `ALTER TABLE … ADD COLUMN` guarded by a `PRAGMA table_info` check, alongside the
existing `CREATE TABLE IF NOT EXISTS` in `IndexStore.Initialize`.

## Strategy selection & fallback

`IndexManager` (or a small `IndexStrategySelector`) decides per root:

> **local fixed NTFS volume AND process elevated → MFT; otherwise → `Crawler`.**

UNC / network paths always use `Crawler` (unchanged). Any failure on the MFT path — not
elevated, open denied, not NTFS, parse anomaly, journal unavailable — is logged and **falls back
transparently to `Crawler` for that root**. The app is never worse off than today; the MFT path
is pure upside when conditions allow. The selector takes injected probes so it is unit-tested
without touching real volumes.

## Error handling

- All native calls check return codes / `Marshal.GetLastWin32Error`; handles are `SafeHandle`s.
- The MFT/USN backend is wrapped so any exception downgrades that root to the crawler rather
  than failing the whole refresh.
- Corrupt / `BAAD` / zeroed FILE records are skipped, not fatal.
- Non-Windows: the native types are guarded with `[SupportedOSPlatform("windows")]` and a
  runtime OS check; on non-Windows the selector always returns `Crawler`.

## Testing strategy

Pure logic — fully unit-tested, no admin or volume needed:
- `DataRunParser`: known run-list byte sequences → expected extents (incl. negative deltas,
  multi-run, sparse holes).
- `MftRecordParser`: hand-built FILE records — resident vs non-resident `$DATA` size, fixup
  application, directory flag, DOS-namespace name skipped in favour of Win32, multiple
  `$FILE_NAME` attributes.
- `PathBuilder`: synthetic FRN maps → expected full paths; root, nested, orphan, cycle.
- `UsnJournal` record parser: synthetic `USN_RECORD_V2` buffers (create/delete/rename reasons).
- `IndexStrategySelector`: injected `(isElevated, fileSystem, driveType, isUnc)` → expected
  choice and fallback.
- Schema migration: `frn` / `usn_*` columns added idempotently; old DBs upgrade cleanly.

### Verification gap (explicit)

Reading a raw volume handle needs Administrator rights and a live disk — unavailable in the test
runner. The interop layer (`NtfsVolume`, `MftEnumerator` live read, `UsnJournal` live read) is
therefore verified manually by the user under an elevated process against a real NTFS drive,
following a written checklist (index `C:`, compare entry count and spot-check size/date against
the crawler; modify a file and confirm the USN refresh reflects it). The transparent fallback
keeps the app correct even if the native path has a latent bug.

## Risks

- Raw MFT parse correctness across NTFS variants: 1024 vs 4096-byte records, fragmented `$MFT`,
  attribute lists for highly fragmented files, compressed/sparse sizes, hard links, reparse
  points. Mitigated by targeted parser tests + fallback.
- USN edge cases: journal disabled, wrap-around/overflow, journal recreated (ID change).
- Verification gap above.

## Phasing (both phases ship in this feature, sequenced with commits)

1. **Bulk MFT full scan** — `Native/` parsers + `MftEnumerator` + `IndexStrategySelector` +
   crawler fallback + all pure-logic unit tests. Delivers the instant full/initial scan.
2. **USN incremental** — `frn` / `usn_*` schema + `UsnJournal` + delta application + rescan-on-gap.
   Delivers near-instant refreshes.

## Out of scope (YAGNI)

- Non-NTFS fast paths (ReFS/exFAT/FAT) — always crawler.
- A privileged background service/helper — elevation is detected, not requested for the user.
- Watching for live changes continuously (FileSystemWatcher / real-time) — refresh-driven only.
- Parsing alternate data streams, security descriptors, or any attribute beyond
  `$STANDARD_INFORMATION` / `$FILE_NAME` / `$DATA`.
