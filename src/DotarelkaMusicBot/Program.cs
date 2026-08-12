using DotarelkaMusicBot.Services;

var ytDlp = new YtDlpService();

var info = await ytDlp.GetInfoAsync(
    "ССЫЛКА_НА_SOUNDCLOUD_ТРЕК",
    CancellationToken.None);

Console.WriteLine($"ID: {info.Id}");
Console.WriteLine($"Название: {info.Title}");
Console.WriteLine($"Автор: {info.Uploader}");
Console.WriteLine($"Длительность: {info.Duration}");
Console.WriteLine($"Audio URL: {info.Url}");

return;