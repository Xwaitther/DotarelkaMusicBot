namespace DotarelkaMusicBot.Utils;

internal static class Guard
{
    public static void NotNull(object? value, string message)
    {
        if (value is null)
            Fail(message);
    }

    public static void NotNullOrWhitespace(string? value, string message)
    {
        if (string.IsNullOrWhiteSpace(value))
            Fail(message);
    }

    private static void Fail(string message)
    {
        Console.Error.WriteLine(message);
        Environment.Exit(1);
    }
}
