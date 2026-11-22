using System.Diagnostics;

namespace Antiphon.Tests.TestHelpers;

internal static class LiveFixtureText
{
    private const int SharingViolation = unchecked((int)0x80070020);
    private const int LockViolation = unchecked((int)0x80070021);

    internal static async Task<string> ReadPublishedAsync(string path, Func<string, FileShare, Stream>? open = null,
        CancellationToken cancellationToken = default)
    {
        var clock = Stopwatch.StartNew();
        IOException? lastReadError = null;
        while (clock.ElapsedMilliseconds < 5_000)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var text = await ReadAsync(path, open, cancellationToken);
                if (!string.IsNullOrWhiteSpace(text)) return text;
            }
            catch (IOException error) when (error is FileNotFoundException || error.HResult is SharingViolation or LockViolation)
            {
                lastReadError = error;
            }

            await Task.Delay(10, cancellationToken);
        }

        throw new TimeoutException($"Fixture marker did not publish content within 5 seconds: {path}", lastReadError);
    }

    internal static async Task<string> ReadAsync(string path,
        Func<string, FileShare, Stream>? open = null, CancellationToken cancellationToken = default)
    {
        // Windows sharing is reciprocal: the reader must share the existing writer's access.
        await using var stream = (open ?? Open)(path, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync(cancellationToken);
    }

    private static Stream Open(string path, FileShare share) =>
        new FileStream(path, FileMode.Open, FileAccess.Read, share, 4096, useAsync: true);
}
