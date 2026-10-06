# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and the project adheres to [Semantic Versioning](https://semver.org/).

## [Unreleased]

## [0.3.0] - 2026-07-29

### Added
- Universal Windows 10 1809+ installer for x86, x64 and Arm64.
- In-app updates through GitHub Releases with mandatory SHA-256 verification.
- Reproducible `IndexBench` for comparing index-update algorithms.

### Changed
- Network directory traversal is now streaming and no longer uses busy-wait spinning.
- A full SQLite refresh runs as a single atomic transaction and does not rewrite
  unchanged rows.
- The comparison snapshot stores only the required fields and a single composite
  key instead of two collections of full paths.

## [0.1.0] - 2026-06-18

### Added
- Search for files and folders on network (SMB) paths through a local SQLite index.
- Search modes: substring, wildcard (`*?`) and regular expressions.
- Filters by size, modified date and type/extensions; "files only" / "folders only".
- Quick type buttons: PDF, Word, Excel, Photos.
- Content search within the current result set.
- Automatic index refresh at an interval.
- Portable publish as a single self-contained `.exe`.
- Row context menu: open file, open folder, copy path.

[Unreleased]: https://github.com/denfry/NetSearch/compare/v0.3.0...HEAD
[0.3.0]: https://github.com/denfry/NetSearch/compare/v0.1.0...v0.3.0
[0.1.0]: https://github.com/denfry/NetSearch/releases/tag/v0.1.0
