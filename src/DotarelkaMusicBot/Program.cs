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

var ytDlp = new YtDlpService(config.YtDlpExecutable);
var soundCloud = new SoundCloudSource(ytDlp, string.IsNullOrWhiteSpace(config.SoundCloudClientId) ? null : config.SoundCloudClientId);
var musicManager = new MusicManager(soundCloud, client);
var commandHandler = new CommandHandler(musicManager, config.CommandPrefix);
var bot = new DiscordBot(client, commandHandler);
// Configure FFmpeg audio channels (1 = mono, 2 = stereo)
DotarelkaMusicBot.Services.FFmpegService.DefaultChannels = config.AudioChannels;

await bot.StartAsync();

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (s, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

try
{
    await Task.Delay(-1, cts.Token);
}
catch (OperationCanceledException)
{
    Console.WriteLine("Shutting down...");
    try { await musicManager.ShutdownAsync(); } catch { }
    try { await client.DisconnectAsync(); } catch { }
}