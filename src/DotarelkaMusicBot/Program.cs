using DSharpPlus;
using DSharpPlus.VoiceNext;
using Microsoft.Extensions.Logging;
using DotarelkaMusicBot.Configuration;
using DotarelkaMusicBot.SoundCloud;
using DotarelkaMusicBot.Music;
using DotarelkaMusicBot.Discord;
using DotarelkaMusicBot.Services;

var config = await BotConfig.LoadAsync("config.json");
FFmpegService.EnsureAvailable();

var discordConfig = new DiscordConfiguration
{
    Token = config.BotToken,
    TokenType = TokenType.Bot,
    Intents = DiscordIntents.Guilds | DiscordIntents.GuildMessages | DiscordIntents.GuildVoiceStates | DiscordIntents.MessageContents,
    AutoReconnect = true,
    MinimumLogLevel = LogLevel.Information,
};

using var client = new DiscordClient(discordConfig);
client.UseVoiceNext();

var soundCloud = new SoundCloudSource(string.IsNullOrWhiteSpace(config.SoundCloudClientId) ? null : config.SoundCloudClientId);
var musicManager = new MusicManager(soundCloud, client);
var commandHandler = new CommandHandler(musicManager, config.CommandPrefix);
var bot = new DiscordBot(client, commandHandler);

await bot.StartAsync();
await Task.Delay(-1);
