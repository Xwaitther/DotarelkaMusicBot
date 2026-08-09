using DSharpPlus;
using DSharpPlus.EventArgs;

namespace DotarelkaMusicBot.Discord;

internal sealed class CommandHandler
{
    private readonly Music.MusicManager _musicManager;
    private readonly string _prefix;

    public CommandHandler(Music.MusicManager musicManager, string prefix)
    {
        _musicManager = musicManager;
        _prefix = prefix;
    }

    public async Task HandleMessageAsync(DiscordClient sender, MessageCreateEventArgs e)
    {
        if (e.Author.IsBot || string.IsNullOrWhiteSpace(e.Message.Content) || !e.Message.Content.StartsWith(_prefix, StringComparison.OrdinalIgnoreCase))
            return;

        var content = e.Message.Content[_prefix.Length..].Trim();
        if (string.IsNullOrWhiteSpace(content))
            return;

        var parts = content.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        var command = parts[0].ToLowerInvariant();
        var argument = parts.Length > 1 ? parts[1].Trim() : string.Empty;

        try
        {
            switch (command)
            {
                case "play":
                    await _musicManager.HandlePlayCommandAsync(e, argument);
                    break;
                case "skip":
                    await _musicManager.HandleSkipCommandAsync(e);
                    break;
                case "stop":
                    await _musicManager.HandleStopCommandAsync(e);
                    break;
                case "leave":
                    await _musicManager.HandleLeaveCommandAsync(e);
                    break;
                case "queue":
                    await _musicManager.HandleQueueCommandAsync(e);
                    break;
                case "nowplaying":
                    await _musicManager.HandleNowPlayingCommandAsync(e);
                    break;
                case "volume":
                    await _musicManager.HandleVolumeCommandAsync(e, argument);
                    break;
                case "pause":
                    await _musicManager.HandlePauseCommandAsync(e);
                    break;
                case "resume":
                    await _musicManager.HandleResumeCommandAsync(e);
                    break;
                case "help":
                    await _musicManager.HandleHelpCommandAsync(e, _prefix);
                    break;
                default:
                    break;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Unhandled command exception: {ex}");
            await e.Message.RespondAsync("❌ Произошла ошибка при выполнении команды.");
        }
    }
}
