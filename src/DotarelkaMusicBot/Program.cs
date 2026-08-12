using DotarelkaMusicBot.Services;

var ytDlp = new YtDlpService();

var json = await ytDlp.GetInfoJsonAsync(
    "https://soundcloud.com/brawl-stars-873983974/zhenshhina-ya-ne-tanczuyu-bass",
    CancellationToken.None);

Console.WriteLine(json);

return;