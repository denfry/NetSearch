# Вклад в NetSearch

Спасибо за интерес к проекту! Любые баг-репорты, идеи и PR приветствуются.

## С чего начать

1. Установите [.NET SDK 9](https://dotnet.microsoft.com/download).
2. Соберите и прогоните тесты:
   ```bash
   dotnet build
   dotnet test
   ```
3. Запуск приложения: `dotnet run --project src/NetSearch.App`.

## Структура

- `src/NetSearch.Core` — индексация, поиск, хранилище. Без зависимостей от UI,
  покрыто тестами.
- `src/NetSearch.App` — WPF-приложение (MVVM).
- `tests/NetSearch.Core.Tests` — модульные тесты ядра.

## Pull requests

- Ветвитесь от `master`, держите PR сфокусированным на одной задаче.
- Новую логику в `NetSearch.Core` сопровождайте тестами.
- Перед отправкой убедитесь, что `dotnet build` и `dotnet test` проходят.
- Коммиты — в стиле [Conventional Commits](https://www.conventionalcommits.org/)
  (`feat:`, `fix:`, `docs:`, `chore:` …).

## Релизы

Релизы выпускаются автоматически при пуше тега `vX.Y.Z` (см.
`.github/workflows/release.yml`): собирается портативный `.exe` и публикуется
в GitHub Releases. Не забудьте обновить `CHANGELOG.md`.
