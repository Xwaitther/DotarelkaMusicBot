using DSharpPlus;
using DSharpPlus.EventArgs;

namespace DotarelkaMusicBot.Discord;

internal sealed class DiscordBot
{
    private readonly DiscordClient _client;
    private readonly CommandHandler _commandHandler;

    public DiscordBot(DiscordClient client, CommandHandler commandHandler)
    {
        _client = client;
        _commandHandler = commandHandler;
        _client.Ready += OnReady;
        _client.ClientErrored += OnClientError;
        _client.MessageCreated += async (s, e) => await _commandHandler.HandleMessageAsync(s, e);
    }

    public Task StartAsync() => _client.ConnectAsync();

    private Task OnReady(DiscordClient sender, ReadyEventArgs e)
    {
        Console.WriteLine($"Bot connected as {sender.CurrentUser.Username}");
        return Task.CompletedTask;
    }

    private Task OnClientError(DiscordClient sender, ClientErrorEventArgs e)
    {
        Console.Error.WriteLine($"Discord client error: {e.Exception}");
        return Task.CompletedTask;
    }
}
