using System.Diagnostics;
using System.Text;

namespace Antiphon.Server.Infrastructure.Git;

/// <summary>
/// Redirected transport for the future native root owner. These are real streams, never
/// private Process fields. S3b adds root-handle wait and job-membership operations.
/// </summary>
internal sealed class WindowsOwnedGitStreams : IDisposable
{
    internal StreamWriter StandardInput { get; }
    internal StreamReader StandardOutput { get; }
    internal StreamReader StandardError { get; }
    private bool _disposed;

    internal WindowsOwnedGitStreams(Stream input, Stream output, Stream error, ProcessStartInfo start)
    {
        // Git's text transport is BOM-free even if an explicitly supplied UTF8Encoding has a BOM.
        var encoding = start.StandardInputEncoding ?? new UTF8Encoding(false);
        if (encoding.CodePage == Encoding.UTF8.CodePage) encoding = new UTF8Encoding(false);
        StandardInput = new StreamWriter(input, encoding, leaveOpen: true) { AutoFlush = true };
        StandardOutput = new StreamReader(output, start.StandardOutputEncoding ?? Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        StandardError = new StreamReader(error, start.StandardErrorEncoding ?? Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true, leaveOpen: true);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { StandardInput.Dispose(); }
        finally
        {
            try { StandardOutput.Dispose(); }
            finally { StandardError.Dispose(); }
        }
        // The launch owns underlying parent pipe handles, including constructor failure paths.
    }
}
