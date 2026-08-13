# DotarelkaMusicBot

Discord music bot на .NET 10 с поддержкой SoundCloud и голосовым воспроизведением.

## Требования

- .NET 10 SDK
- FFmpeg в PATH
- yt-dlp в PATH (или укажите путь в `config.json` через `YtDlpExecutable`)
- Discord bot token
- SoundCloud Client ID (опционально; yt-dlp часто работает без client_id)

## Установка

1. Склонируйте репозиторий.
2. Скопируйте `config.example.json` в `config.json`.
3. Заполните поля `BotToken` и `CommandPrefix`; `SoundCloudClientId` необязателен.
	При необходимости укажите `YtDlpExecutable` (по умолчанию `yt-dlp`).

## Запуск

```bash
dotnet restore src/DotarelkaMusicBot/DotarelkaMusicBot.csproj
dotnet build src/DotarelkaMusicBot/DotarelkaMusicBot.csproj
dotnet run --project src/DotarelkaMusicBot/DotarelkaMusicBot.csproj
```

## Команды

- `!play <url|запрос>` — добавить трек или плейлист
- `!skip` — пропустить текущий трек
- `!stop` — остановить и очистить очередь
- `!leave` — отключиться от голосового канала
- `!queue` — показать очередь
- `!nowplaying` — показать текущий трек
- `!volume <0-100>` — изменить громкость
- `!help` — показать команды

## Примечания

- `config.json` игнорируется Git.
- Бот использует системный FFmpeg для декодирования аудиопотока.
- `SoundCloudClientId` необязателен — бот сначала попытается извлечь прямой audio URL через `yt-dlp`,
  затем упадёт обратно к SoundCloud API transcodings при необходимости.
