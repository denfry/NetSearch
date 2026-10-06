# Contributing to NetSearch

Thanks for your interest. Bug reports, ideas and pull requests are welcome.

## Getting started

1. Install the [.NET SDK 9](https://dotnet.microsoft.com/download).
2. Build and run the tests:
   ```bash
   dotnet build
   dotnet test
   ```
3. Run the app: `dotnet run --project src/NetSearch.App`.

## Layout

- `src/NetSearch.Core`: indexing, search and storage. No UI dependencies;
  covered by tests.
- `src/NetSearch.App`: the WPF application (MVVM).
- `tests/NetSearch.Core.Tests`: unit tests for the core.

## Pull requests

- Branch from `master` and keep each PR focused on a single change.
- Accompany new logic in `NetSearch.Core` with tests.
- Make sure `dotnet build` and `dotnet test` pass before submitting.
- Write commits in the [Conventional Commits](https://www.conventionalcommits.org/)
  style (`feat:`, `fix:`, `docs:`, `chore:`, ...).

## Releases

Releases are published automatically when a `vX.Y.Z` tag is pushed (see
`.github/workflows/release.yml`): the portable `.exe` files and installer are
built and attached to a GitHub Release. Remember to update `CHANGELOG.md`.
