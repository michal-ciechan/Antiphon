namespace Antiphon.Tests.TestHelpers;

internal static class LiveFixtureText
{
    internal static async Task<string> ReadAsync(string path,
        Func<string, FileShare, Stream>? open = null, CancellationToken cancellationToken = default)
    {
        await using var stream = (open ?? Open)(path, FileShare.Read);
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync(cancellationToken);
    }

    private static Stream Open(string path, FileShare share) =>
        new FileStream(path, FileMode.Open, FileAccess.Read, share, 4096, useAsync: true);
}
