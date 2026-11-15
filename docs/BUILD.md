# Сборка FrameLab

[← Главная страница проекта](../README.md)

## Требования

- Windows x64 для запуска WPF-приложения и проверки системных функций.
- .NET 8 SDK для сборки.
- Inno Setup 6 для установщика; CI использует 6.4.3.
- Интернет для первого восстановления NuGet-пакетов и получения внешних компонентов сборки.

Готовые файлы официального релиза включают .NET Runtime. Пользователю SDK не нужен.

## Сборка из корня репозитория

В PowerShell сначала загрузите PresentMon: он встраивается **во время публикации**, поэтому загрузка должна предшествовать `dotnet publish`.

```powershell
New-Item -ItemType Directory -Force installer/tools | Out-Null
Invoke-WebRequest 'https://github.com/GameTechDev/PresentMon/releases/download/v2.6.0/PresentMon-2.6.0-x64.exe' -OutFile 'installer/tools/PresentMon-2.6.0-x64.exe'
Invoke-WebRequest 'https://raw.githubusercontent.com/GameTechDev/PresentMon/v2.6.0/LICENSE.txt' -OutFile 'installer/tools/PresentMon-LICENSE.txt'

dotnet test tests/Dota2Optimizer.Tests -c Release
dotnet publish src/Dota2Optimizer.App -c Release -r win-x64 -o artifacts/publish
iscc /DAppVersion=1.7.1 installer/Dota2Optimizer.iss
```

`iscc` должен быть доступен в PATH; иначе укажите полный путь к `ISCC.exe`.

| Результат | Расположение |
|---|---|
| Исполняемый файл | `artifacts/publish/FrameLab.exe` |
| Установщик | `installer/Output/FrameLab-Setup.exe` |

Без скачанного PresentMon приложение собирается, но замер FPS через него не будет обеспечен встроенной копией. Синтетическая сцена не зависит от PresentMon.

## Версия и публикация

Для новой версии согласованно обновите:

1. `Version` в [Directory.Build.props](../Directory.Build.props).
2. `APP_VERSION` в [workflow сборки](../.github/workflows/build.yml).
3. [Описание релиза](../.github/release-notes.md) и раздел «Что нового» в README.
4. Номер версии в примере `iscc` выше.

GitHub Actions запускает тесты, публикует exe и собирает установщик на Windows. Новый номер версии в `main` создаёт релиз; повторный пуш с тем же номером не заменяет уже опубликованные файлы. Тег вида `vX.Y.Z` задаёт версию сборки явно.

Сайт публикуется отдельным [workflow GitHub Pages](../.github/workflows/pages.yml). Перед публикацией скрипт подставляет версию из workflow сборки. Галереи скриншотов на главной странице сайта нет.

## Структура репозитория

| Путь | Назначение |
|---|---|
| `src/Dota2Optimizer.App/` | WPF-интерфейс, страницы, темы, модели представления и помощник для операций с правами администратора. |
| `src/Dota2Optimizer.Core/` | Сканирование, игровые настройки, твики Windows, резервные копии, откат и измерения. |
| `tests/Dota2Optimizer.Tests/` | Тесты xUnit для логики приложения. |
| `installer/` | Скрипт и ресурсы Inno Setup. |
| `site/` | Статический сайт и гайды. |
| `.github/` | Сборка, публикация сайта и описание релиза. |
| `docs/` | Документация разработчика и сценарии проверки. |

Имена проектов `Dota2Optimizer` сохранены для совместимости. Название продукта и исполняемого файла — **FrameLab**.

Порядок ручной проверки системных изменений описан в [TESTING.md](TESTING.md). Успешная сборка не заменяет проверку запуска WPF и отката на Windows.
