# Music RPC

**Твоя музыка — в профиле Discord. Windows и Linux, без токенов аккаунта.**

[Скачать последнюю версию](https://github.com/Chekpoinss/music-rpc/releases/latest) · [Сообщить об ошибке](https://github.com/Chekpoinss/music-rpc/issues)

Discord Rich Presence для **AyuGram / Telegram** и **Apple Music**: название трека, исполнитель, обложка и время воспроизведения.

## Платформы

| Платформа | Источник | Интерфейс | Статус |
| --- | --- | --- | --- |
| Windows 10 2004+ / Windows 11 x64 | Системные медиасессии Windows; приблизительный таймер Telegram | Окно настроек и трей | Проверено с AyuGram и настольным Discord |
| Linux x64 | MPRIS через playerctl; Discord Unix IPC | GTK 4 и консоль | Автоматические тесты MPRIS и IPC; совместимость зависит от плеера и окружения |

Apple Music на Windows читается из настольного приложения. На Linux поддерживается MPRIS-совместимый сторонний клиент, например Cider; отдельного нативного клиента Apple Music для Linux проект не предоставляет. Совместимость зависит от версии клиента и его медиаданных.

## Настройка Discord

1. На [Discord Developer Portal](https://discord.com/developers/applications) создай приложение и скопируй **General Information → Application ID**. Один ID подходит для обоих источников.
2. Запусти настольный Discord и включи **Activity Privacy → Share your detected activities** (название настройки может отличаться).
3. Укажи Application ID в Music RPC. Бот, токен аккаунта и Client Secret не нужны.

Активность называется **Telegram** или **Apple Music** в зависимости от плеера, независимо от имени приложения в Developer Portal. На паузе, после остановки или закрытия плеера статус очищается. Переподключение после ошибки выполняется автоматически.

## Windows

Скачай `music-rpc-windows-x64.zip` из [последнего релиза](https://github.com/Chekpoinss/music-rpc/releases/latest), распакуй и открой `MusicRpc.exe`. Он автономный: отдельно устанавливать Python или .NET не нужно.

Вставь Application ID и нажми **Сохранить и подключить**. Можно выбрать источники и приоритет, если они играют одновременно. Крестик сворачивает окно в трей; двойной щелчок по значку открывает настройки. Полное завершение: меню значка → **Выход**.

Автозапуск включается только галочкой и сохранением. Перед включением перенеси `.exe` в постоянную папку. Для удаления отключи автозапуск, сохрани настройки, выйди и удали файл.

Таймер **Telegram / AyuGram в Windows приблизительный**, без чтения интерфейса. Он работает при закрытом или свёрнутом окне: новый трек начинает отсчёт с момента обнаружения, пауза сохраняет время, продолжение возобновляет его. Программа не учитывает перемотку и скорость воспроизведения. Если запустить Music RPC посреди песни, отсчёт начнётся с нуля.

Длительность берётся из медиаданных или каталога обложек. По её окончании отсчёт начинается заново, чтобы приблизительно отображать повтор трека. Это расчётный повтор: без позиции плеера нельзя определить фактическое начало лупа после перемотки. Если длительность неизвестна, таймер скрывается; название, исполнитель и обложка остаются. Если сведения о длительности пришли позже, сохраняется время с первого обнаружения трека.

Apple Music в Windows и Linux-клиент используют доступную системную позицию.

## Linux

Скачай `music-rpc-linux-x64.tar.gz` из [последнего релиза](https://github.com/Chekpoinss/music-rpc/releases/latest). В архиве — автономный .NET-бинарник, GTK-интерфейс и установщик без `sudo`.

Нужны Linux x86_64 с glibc, Python 3, GTK 4, PyGObject и `playerctl`. Плеер и настольный Discord должны работать в той же пользовательской сессии D-Bus.

```sh
# Fedora
sudo dnf install playerctl python3-gobject gtk4
# Debian / Ubuntu
sudo apt install playerctl python3-gi gir1.2-gtk-4.0
# Arch Linux
sudo pacman -S playerctl python-gobject gtk4
```

Распакуй архив и установи:

```sh
tar -xzf music-rpc-linux-x64.tar.gz
cd music-rpc-linux-x64
sh install.sh
~/.local/bin/music-rpc-gui
```

Дальше открывай **Music RPC** через меню приложений. Вставь Application ID, выбери источники, приоритет и нажми **Сохранить и подключить**. В окне доступны остановка, журнал, выбор Apple Music MPRIS-плеера и отключение поиска обложек. Закрытие окна останавливает передачу статуса; трея в Linux пока нет.

Для запуска без окна с сохранёнными в GUI настройками: `~/.local/bin/music-rpc-background`. Журнал фонового запуска — `~/.config/music-rpc/background.log` (или каталог внутри `$XDG_CONFIG_HOME`). Автозапуск установщик не включает. GUI и фоновый процесс следует запускать по отдельности: одновременно работает один экземпляр CLI.

Можно использовать консоль без установки GUI:

```sh
./music-rpc --application-id YOUR_APPLICATION_ID
./music-rpc --help
./music-rpc --diagnose
./music-rpc --priority telegram
./music-rpc --priority apple
./music-rpc --no-artwork
playerctl --list-all
./music-rpc --apple-player EXACT_MPRIS_PLAYER_NAME
```

ID сохраняется. В следующий раз достаточно `./music-rpc`; завершение — Ctrl+C. Остальные параметры CLI действуют для текущего запуска. Для удаления установленной программы останови её и выполни `sh uninstall.sh` из распакованного архива; настройки останутся.

AyuGram, Telegram и Cider распознаются по имени MPRIS-плеера. Браузеры по умолчанию игнорируются. `--apple-player` позволяет явно выбрать конкретную MPRIS-сессию стороннего клиента или браузера; убедись, что это нужный источник. При выборе общей браузерной сессии её следующие вкладки могут тоже отображаться как Apple Music.

Обычные пути Discord IPC, а также распространённые пути Flatpak/Snap проверяются автоматически. Доступ к сокету и пользовательскому D-Bus всё равно зависит от контейнеризации и дистрибутива. Полная проверка всех комбинаций ещё не выполнена.

## Обложки и ограничения

- В Windows обложка ищется по названию трека и исполнителю: сначала Apple, затем Deezer. Принимается совпадение названия и исполнителя; если длительность известна, допускается разница не более 5 секунд. Ответы кэшируются, частота запросов ограничена.
- В Linux сначала используется публичный HTTPS URL обложки из MPRIS, затем тот же поиск по каталогам. Локальный `file://` не отправляется в Discord. `--no-artwork` отключает поиск в каталогах, оставляя публичную обложку от плеера.
- Внешним каталогам отправляются название трека и исполнитель. Discord получает активность, ссылку на обложку и ссылку на найденный трек. Аудиофайлы, локальные обложки, переписки и база Telegram не загружаются.
- Изменённые теги от ботов, редкие треки и нестандартные версии могут приводить к отсутствующей или другой обложке. Из встроенных тегов аудиофайлов Telegram картинка сейчас не извлекается.
- Голосовые сообщения или видео могут попасть в ту же медиасессию Telegram. Если это нежелательно, приостанови передачу или отключи источник в Windows.
- Воспроизведение на телефоне и браузерный Discord не поддерживаются. Apple Music в браузере Windows тоже не распознаётся.

## Настройки и диагностика

| ОС | Настройки и журнал |
| --- | --- |
| Windows | `%LOCALAPPDATA%/MusicRpc/settings.json`, `music-rpc.log` |
| Linux | `$XDG_CONFIG_HOME/music-rpc/`, по умолчанию `~/.config/music-rpc/` |

Windows-автозапуск хранится в `HKCU/Software/Microsoft/Windows/CurrentVersion/Run`, значение `MusicRpc`.

`MusicRpc.exe --diagnose` в Windows создаёт `diagnostics.json` рядом с `.exe`; Linux `--diagnose` печатает JSON в терминал. Диагностика не подключается к Discord. В Windows она также проверяет каталоги обложек. Диагностика содержит текущие названия медиа и пути/идентификаторы плееров: удали личные данные перед публикацией в issue.

## Сборка

Нужен .NET SDK 10. Windows GUI собирается на Windows; Core и Linux CLI — на обеих ОС.

```powershell
# Windows GUI + автономный exe
./build.ps1
# Windows-проверки
dotnet run --project tests/MusicRpc.Tests.csproj -c Release
```

```sh
# Linux CLI + автономный бинарник (Linux x64)
sh build-linux.sh
# Общие проверки; на Linux дополнительно проверяется Unix IPC
dotnet run --project tests/Portable/MusicRpc.Portable.Tests.csproj -c Release
```

Сборки: `dist/MusicRpc.exe` и `dist/linux/` (CLI, GUI и установщик). GitHub Actions проверяет Windows и Ubuntu, включая обмен Discord IPC через тестовые серверы и настоящий playerctl против временного MPRIS-плеера. Успешные запуски сохраняют артефакты обеих платформ. Это не заменяет ручную проверку конкретных версий AyuGram, Apple Music/Cider и Discord.

## Структура и участие

- `src/MusicRpc.Core` — общая логика, `IMediaReader`, MPRIS и Discord IPC. Общие исходники `Media.cs`, `Settings.cs`, `Catalog.cs`, `DiscordRpc.cs` пока находятся в корне и подключаются в Core.
- `MusicRpc.csproj`, `MainForm.cs`, `WindowsMediaReader.cs` — Windows GUI и системные API.
- `src/MusicRpc.Linux` — Linux CLI; `packaging/linux` — GTK 4 GUI и установка.
- `tests` — Windows и переносимые проверки; `.github/workflows` — CI.

[Как внести вклад](CONTRIBUTING.md) · [Планы](docs/ROADMAP.md) · [Лицензия MIT](LICENSE)

## Источники

- [Discord RPC](https://docs.discord.com/developers/topics/rpc)
- [MPRIS](https://specifications.freedesktop.org/mpris-spec/latest/) и [playerctl](https://github.com/altdesktop/playerctl)
- [AyuGram Desktop](https://github.com/AyuGram/AyuGramDesktop)
- [Apple Search API](https://performance-partners.apple.com/search-api)
- [NextFire/apple-music-discord-rpc](https://github.com/NextFire/apple-music-discord-rpc) — пример для macOS, присланный при создании проекта; его код не включён в Music RPC.

