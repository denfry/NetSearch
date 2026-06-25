# Local Disks tab, disk cleaner, app icon, and installer (NetSearch v0.2)

- **Date:** 2026-06-22
- **Status:** Approved (design)
- **Branch:** `feature/mft-usn-indexing` (builds on the local-NTFS MFT engine landed here)

## Goal

Grow NetSearch from a network-file search tool into a local-disk space manager as well,
and make it shippable as an installed Windows app. Four features, one combined design:

1. **Local Disks tab** — a WizTree-style view of what is filling each local drive: a
   size-sorted, drill-down folder tree plus a treemap.
2. **Disk cleaner** — a safe, preview-first cleaner for temp files, the Recycle Bin,
   browser/app caches, and system/dev caches.
3. **App icon** — a designed multi-resolution `.ico` wired into the exe, window, and installer.
4. **Installer** — an Inno Setup `Setup.exe` (shortcuts, uninstaller, admin install).

The first two reuse the MFT enumeration engine already in `NetSearch.Core/Native`
(`MftEnumerator` emits `FileEntry` with name/parent/size/dir for a whole NTFS volume in one
sequential MFT read). The non-elevated / non-NTFS path reuses the existing `Crawler`.

## Decisions (locked)

1. **One window, three tabs** — the current single search view becomes tab **Поиск**
   (unchanged); new tabs **Локальные диски** (usage) and **Очистка** (cleaner) are added via a
   `TabControl`. Existing theme/styles are reused.
2. **App stays `asInvoker`** — network search never demands admin. The Disks and Очистка tabs
   detect `IEnvironmentProbe.IsWindowsElevated`; when not elevated they show a banner with a
   **«Перезапустить от имени администратора»** button that relaunches the process with the
   `runas` verb. Elevated → full speed and all categories enabled.
3. **Disk-usage sizes are logical (real) size in v1**, not allocated/on-disk size. Simpler and
   close enough for "what is filling the disk"; allocated-size is a noted v2 refinement.
4. **Treemap layout lives in `NetSearch.Core` as a pure function** (squarified treemap:
   rect + weights → sub-rects), so the geometry is unit-tested; the App only renders rectangles.
5. **Cleaner is allow-list only and preview-first** — see the non-negotiable safety model below.
6. **Installer is Inno Setup**, per-machine (Program Files), output `NetSearch-Setup-0.2.0.exe`.
   The app **Version bumps to `0.2.0`**.

---

## Feature 1 — Local Disks tab (WizTree-style)

### Architecture (`src/NetSearch.Core/DiskUsage/`)

Pure model/aggregation/layout is separated from the App's rendering and from the
privileged scan, so the load-bearing logic is unit-tested without admin or a live volume.

| File | Kind | Responsibility |
|---|---|---|
| `DriveProbe.cs` | interop-lite | List fixed local drives via `DriveInfo` → `(letter, label, fileSystem, totalBytes, freeBytes)`. |
| `FolderNode.cs` | **pure** | Tree node: `Name`, `FullPath`, `IsDir`, aggregated `SizeBytes`, `FileCount`, `Children`, `Parent`. |
| `FolderTreeBuilder.cs` | **pure** | Fold a stream of `FileEntry` batches into a `FolderNode` tree by walking each entry's `ParentPath`; sum sizes/counts up to the root. Children kept sorted by size desc. |
| `DiskUsageScanner.cs` | glue | Scan one drive: choose `MftEnumerator` (elevated + fixed NTFS, via the existing `IndexStrategySelector` rule) else a recursive directory walk; stream batches into `FolderTreeBuilder`; report `IProgress`. Returns the root `FolderNode`. |
| `Treemap.cs` | **pure** | Squarified-treemap layout: `Squarify(Rect bounds, IReadOnlyList<double> weights) → IReadOnlyList<Rect>`. Deterministic, no UI types (own `Rect` struct). |
| `SizeFormatter.cs` | **pure** | Bytes → `"1.4 GB"` etc. (reused by the cleaner; consolidates any existing size text). |

`DiskUsageScanner` reuses `MftEnumerator.Enumerate(rootId, rootPath, onBatch, ct, progress)`
directly — no SQLite involved; the usage tree is an in-memory snapshot, discarded when the
user rescans or switches drives. The recursive-walk fallback mirrors `Crawler`'s traversal but
emits straight into the builder.

### App (tab **Локальные диски**)

- **Drive bar:** one selectable chip per fixed drive showing used/total and a fill bar;
  selecting a drive (or pressing «Сканировать») runs the scan with the shared progress bar.
- **Left — tree-list:** a `TreeView` over `FolderNode` (HierarchicalDataTemplate), columns
  Name · Size · % of parent · Files, sorted by size desc, with an inline proportion bar.
  Expanding a node lazily realizes children (already in memory).
- **Right — treemap:** a `Canvas`/`ItemsControl` of colored `Rectangle`s from `Treemap.Squarify`
  over the current node's children; color by depth/type; label when the rect is large enough;
  tooltip = path + size. Clicking a rect drills in.
- **Drill-down + breadcrumb:** selecting a folder in either view sets it as the treemap root and
  scrolls the tree to it; a breadcrumb navigates back up.
- **Context menu:** Открыть папку · Показать в Поиске · Отправить в Очистку (folder cleaner is
  out of scope — this jumps to the Очистка tab; see YAGNI).

### Data flow

1. On tab open: `DriveProbe` lists drives → drive bar.
2. Select drive → `DiskUsageScanner.Scan` (background `Task`, cancelable via the existing Cancel
   button pattern) → `FolderNode` root, with progress reported.
3. Bind tree-list to the root; feed root's children to `Treemap.Squarify` for the treemap.
4. Drill-down re-runs only `Squarify` on the chosen node (no rescan).

---

## Feature 2 — Disk cleaner (tab **Очистка**)

### Safety model (non-negotiable, baked in)

- **Allow-list only.** Each category resolves to a fixed set of known-safe locations/patterns.
  No arbitrary or user-supplied path scanning. **Never** touches Documents, Desktop, Downloads,
  Pictures, source trees, or any user content directory.
- **Preview-first, opt-in.** Flow is always **Scan → preview (per-category totals + expandable
  per-item list, every category a checkbox, default selection = the safe ones) → «Очистить
  выбранное» → confirm dialog stating total size and item count.** Nothing is deleted without an
  explicit click.
- **Recycle Bin where feasible.** User-scope deletions (temp, browser caches, thumbnails) go via
  `SHFileOperationW` with `FOF_ALLOWUNDO` so they are recoverable. Large system caches that are
  impractical to recycle are permanent and **labeled «без корзины»**.
- **Non-fatal skips.** Locked/in-use/denied files are skipped and reported, never aborting the run.
- **Admin gating.** Categories needing admin are disabled with the relaunch banner when the
  process is not elevated.

### Architecture (`src/NetSearch.Core/Cleaning/`)

| File | Kind | Responsibility |
|---|---|---|
| `CleanItem.cs` | **pure** | `(Path, SizeBytes, IsDir)` candidate for deletion. |
| `CleanCategory.cs` | **pure** | `Id`, `DisplayName`, `Description`, `NeedsAdmin`, `UsesRecycleBin`, `DefaultSelected`. |
| `ICleanProvider.cs` | iface | `CleanCategory Category { get; }` + `IEnumerable<CleanItem> Scan(CancellationToken)`. |
| `Providers/TempFilesProvider.cs` | provider | `%TEMP%`, `%TMP%`, `C:\Windows\Temp` (admin). Recycle Bin. |
| `Providers/RecycleBinProvider.cs` | provider | Reports current Recycle Bin size (`SHQueryRecycleBin`); cleans via `SHEmptyRecycleBin`. |
| `Providers/BrowserCacheProvider.cs` | provider | Edge/Chrome/Firefox `Cache`/`Code Cache`/`GPUCache`, plus `thumbcache_*.db` / `iconcache_*.db`. Recycle Bin. |
| `Providers/SystemDevCacheProvider.cs` | provider | `SoftwareDistribution\Download` + Delivery Optimization (admin), crash dumps `*.dmp` + `WER`, old `*.log`, dev caches (`npm-cache`, `~/.nuget/packages` http-cache, pip cache). Permanent, labeled. |
| `CleanScanner.cs` | glue | Run selected providers' `Scan` → `CleanScanResult` (per-category items + totals). |
| `CleanExecutor.cs` | interop+glue | Delete selected items: `SHFileOperationW`(`FOF_ALLOWUNDO`) when the provider uses the bin, permanent delete otherwise; Recycle Bin via `SHEmptyRecycleBin`. Returns `(freedBytes, deletedCount, skipped[])`. |
| `Native/ShellFileOps.cs` | interop | P/Invoke for `SHFileOperationW`, `SHEmptyRecycleBinW`, `SHQueryRecycleBinW` + flag constants. |

Providers are independently testable: each `Scan` is pointed at a temp sandbox root in tests
(via injected base paths) so no real system location is read or deleted during tests.

### App (tab **Очистка**)

Category list (checkboxes, size + count per category, «нужны права администратора» / «без
корзины» badges) → «Сканировать» → results populate → «Очистить выбранное» → confirm → progress →
summary («Освобождено 3.1 GB, пропущено 4 файла»). The same elevation banner as the Disks tab.

---

## Feature 3 — App icon

A designed mark (magnifier over a stacked-disk motif, matching the existing 🔍 NetSearch
identity) authored as vector geometry and rendered to a multi-image `NetSearch.ico`
(16/24/32/48/64/256 px) committed at the repo root (next to the `.sln`).

- **Generation:** a small **pure-.NET** build-time helper (`tools/IconGen/`) draws each size with
  GDI+ (`System.Drawing`, Windows-only, already available) and writes the `.ico` container
  directly (ICONDIR + per-size entries; 256 px stored PNG-compressed). No external image tools or
  network. The helper is run once to produce the committed `.ico`; it stays in-repo for
  reproducibility but is not part of the app build.
- **Wiring:** `<ApplicationIcon>NetSearch.ico</ApplicationIcon>` in `NetSearch.App.csproj` (sets
  the exe icon); `Icon="..."` on `MainWindow` (taskbar/window); `SetupIconFile` in the installer.

If the GDI+ ICO assembly proves fiddly, the fallback is to render the PNGs with GDI+ and assemble
the ICO bytes by hand (the format is a fixed header + directory + image blobs) — still no deps.

---

## Feature 4 — Installer (Inno Setup)

An `installer/netsearch.iss` packaging the published single-file self-contained `NetSearch.exe`.

- **Behavior:** per-machine install to `Program Files\NetSearch`; Start-Menu shortcut + optional
  desktop shortcut; uninstaller; app icon; optional «Запустить NetSearch» on finish;
  `PrivilegesRequired=admin` (consistent with the app's needs).
- **Build pipeline:** `publish.ps1` is extended to (1) `dotnet publish -c Release` (existing
  single-file output), (2) locate `ISCC.exe` (Inno Setup compiler) — checking PATH and the
  default install dir, and printing a `winget install JRSoftware.InnoSetup` hint if absent — then
  (3) compile `installer/netsearch.iss`, emitting `dist/NetSearch-Setup-0.2.0.exe`. The version
  is read from the csproj so the filename and installer metadata stay in sync.
- The portable single-file `.exe` remains available as before; the installer is an added artifact.

---

## Cross-cutting: elevation relaunch

A shared helper (App layer) relaunches the current executable elevated:
`ProcessStartInfo { FileName = <process path>, UseShellExecute = true, Verb = "runas" }`, then
shuts down the current instance. Triggered by the banner button on either new tab. If the UAC
prompt is declined (`Win32Exception` 1223), the app stays running unelevated and the banner
remains. `IEnvironmentProbe.IsWindowsElevated` (already present) drives banner visibility and
category/admin gating.

## Error handling

- All native calls (`SHFileOperationW`, recycle-bin queries, volume reads) check return codes;
  failures degrade gracefully (a category that throws on scan is reported empty, not fatal).
- Disk scan reuses the existing MFT→Crawler transparent fallback; a non-NTFS or unprivileged
  drive simply uses the slower recursive walk.
- Cleaner deletion never aborts on a single locked file; skips are collected and surfaced.
- Everything Windows-only is guarded with `[SupportedOSPlatform("windows")]`; Core stays
  build-clean.

## Testing strategy

Pure / sandboxed logic — fully unit-tested (xUnit, alongside the existing 39 in
`NetSearch.Core.Tests`), no admin or real system paths:

- `Treemap.Squarify`: known weights/bounds → expected sub-rect counts, full-area coverage,
  aspect-ratio sanity, single/empty inputs.
- `FolderTreeBuilder`: synthetic `FileEntry` batches → expected aggregated sizes, counts, and
  size-desc child ordering; nested dirs; root handling.
- `SizeFormatter`: byte thresholds → expected strings.
- Cleaner providers: each `Scan` against a temp sandbox tree (injected base paths) → expected
  items and sizes; `CleanExecutor` deletes from a sandbox → asserts freed bytes, gone files, and
  that locked/denied entries are skipped (never touches real system locations).
- `IconGen`: produced `.ico` parses to the expected image count/sizes (header validation).

### Verification gap (explicit)

The live privileged paths — raw-volume MFT scan, `SHEmptyRecycleBin`, deleting real system
caches under elevation, and the produced `Setup.exe` install/uninstall — need admin and a real
machine, unavailable to the test runner. These are verified manually by the user under elevation,
following a written checklist: scan `C:` and spot-check the tree/treemap totals against Explorer;
run the cleaner, confirm previewed items match and freed-space is reported; install via
`Setup.exe`, confirm shortcuts/icon/uninstall. The preview-first + allow-list + Recycle-Bin model
keeps the cleaner safe even if a provider has a latent bug.

## Phasing (sequenced, each with tests + commit)

1. **Icon** — `tools/IconGen` + committed `NetSearch.ico` + csproj/window wiring. Small, unblocks
   the installer and gives the new tabs/window a real icon.
2. **Tab shell + Local Disks tab** — `TabControl` refactor of `MainWindow`; `DiskUsage/` Core
   (`FolderNode`, `FolderTreeBuilder`, `Treemap`, `SizeFormatter`, `DiskUsageScanner`, `DriveProbe`)
   + tests; the tree-list + treemap UI; elevation banner + relaunch helper.
3. **Disk cleaner** — `Cleaning/` Core (providers, scanner, executor, shell interop) + tests; the
   Очистка tab UI; reuse of the elevation banner.
4. **Installer** — `installer/netsearch.iss`; `publish.ps1` extension; version bump to `0.2.0`;
   README install-section update.

## Out of scope (YAGNI)

- Allocated/on-disk (cluster-rounded) sizes, hard-link de-duplication, NTFS compression display —
  logical size only in v1.
- Cleaning arbitrary user-chosen folders, "duplicate finder", or a registry cleaner — allow-list
  categories only.
- Auto-update / delta updates (Velopack/Squirrel), code signing, MSI/MSIX — Inno `Setup.exe` only.
- A background service or scheduled auto-clean — manual, on-demand only.
- Treemap beyond one drill level rendered at a time (the tree-list covers deep navigation).
