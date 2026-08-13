using DotarelkaMusicBot.Services;

var ytDlp = new YtDlpService();

var info = await ytDlp.GetInfoAsync(
    "https://soundcloud.com/brawl-stars-873983974/zhenshhina-ya-ne-tanczuyu-bass",
    CancellationToken.None);

Console.WriteLine($"ID: {info.Id}");
Console.WriteLine($"Название: {info.Title}");
Console.WriteLine($"Автор: {info.Uploader}");
Console.WriteLine($"Длительность: {info.Duration}");
Console.WriteLine($"Audio URL: {info.Url}");

return;