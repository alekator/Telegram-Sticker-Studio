# StickerForge Studio

Локальный кроссплатформенный редактор стикерпаков Telegram. При первой установке или запуске допускается однократная загрузка необходимых компонентов; после их установки все основные функции должны работать без интернета. Облако, AI, телеметрия и обязательная авторизация не используются. Сейчас заложен только M0: минимальное окно Avalonia, структура решения и исследование. Импорт, редактор и экспорт пока отсутствуют.

Для разработки нужен .NET 10 SDK и доступ к NuGet при первом восстановлении:

```sh
dotnet restore StickerForge.sln
dotnet build StickerForge.sln -c Release --no-restore
dotnet test StickerForge.sln -c Release --no-build --no-restore
dotnet run --project src/StickerForge.Desktop
```

Self-contained сборки и Dependency Manager ещё не реализованы. Обязательные компоненты предпочтительно включать в дистрибутив; загрузка компонентов — предусмотренная архитектурой возможность, а не работающая функция M0. Документы: [продукт](docs/PRODUCT_SPEC.md), [архитектура](docs/ARCHITECTURE.md), [Telegram](docs/TELEGRAM_FORMATS.md), [лицензии](docs/LICENSING.md), [M1.1](docs/ROADMAP.md), [ADR](docs/ADR/0001-architecture.md). CI описывает сборку на трёх ОС, но ещё не подтверждает успешный запуск.

В текущем тестовом проекте один пустой тест из шаблона: успешный запуск тестов ещё не подтверждает работу редактора. CI проверяет сборку и тесты, но не открывает окно; `macos-latest` сейчас использует ARM64, Intel и остальные архитектуры требуют отдельной проверки.

Для ручной проверки окна на Windows выполните `dotnet run --project src/StickerForge.Desktop -c Release` в интерактивном сеансе. Должно открыться окно «StickerForge Studio» с текстом «StickerForge Studio — M0»; закройте его обычным способом.
