# DotarelkaMusicBot

Discord music bot на .NET 10 с поддержкой SoundCloud и голосовым воспроизведением.

## Требования

- .NET 10 SDK
- FFmpeg в PATH
- Discord bot token
- SoundCloud Client ID

## Установка

1. Склонируйте репозиторий.
2. Скопируйте `config.example.json` в `config.json`.
3. Заполните поля `BotToken`, `SoundCloudClientId`, `CommandPrefix`.

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
- Для `SoundCloudClientId` используйте рабочий client_id от SoundCloud API.
