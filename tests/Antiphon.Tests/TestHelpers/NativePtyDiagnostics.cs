using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Antiphon.Server.Application.Interfaces;

namespace Antiphon.Tests.TestHelpers;

// Test-owned sessions only. Capture before teardown destroys the screen and host log.
// Observation is best effort and never substitutes for the recipient receipt assertion.
internal static class NativePtyDiagnostics
{
    internal static async Task CaptureAsync(ISessionRunnerClient client, Guid sessionId,
        string label, string? logRoot = null)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        try
        {
            var session = (await client.ListAsync(deadline.Token)).SingleOrDefault(s => s.SessionId == sessionId);
            var snapshot = await client.GetSnapshotAsync(sessionId, deadline.Token);
            Console.WriteLine("PTY_DIAGNOSTIC " + JsonSerializer.Serialize(new
            {
                label, sessionId, session, snapshot.LastSequence, snapshot.AcceptedStartedAt,
                snapshot.Dialog, rawTail = Tail(snapshot.RawOutput), screen = Tail(snapshot.RenderedScreen),
            }));
            if (session?.Pid is > 0)
            {
                try
                {
                    using var process = Process.GetProcessById(session.Pid.Value);
                    Console.WriteLine($"PTY_DIAGNOSTIC child={process.Id} exited={process.HasExited} handles={process.HandleCount}");
                }
                catch (Exception error) { Console.WriteLine($"PTY_DIAGNOSTIC child unavailable: {error.GetType().Name}"); }
            }
            if (logRoot is not null)
            {
                var path = Path.Combine(logRoot, "pty-hosts", "logs", $"{sessionId:N}.log");
                if (File.Exists(path))
                {
                    await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                        FileShare.ReadWrite | FileShare.Delete, 4096, useAsync: true);
                    stream.Seek(Math.Max(0, stream.Length - 16_384), SeekOrigin.Begin);
                    using var reader = new StreamReader(stream, Encoding.UTF8);
                    Console.WriteLine("PTY_DIAGNOSTIC host-log " + await reader.ReadToEndAsync(deadline.Token));
                }
            }
        }
        catch (Exception error)
        {
            Console.WriteLine($"PTY_DIAGNOSTIC {label} session={sessionId} unavailable={error.GetType().Name}: {error.Message}");
        }
    }

    private static string Tail(string value) => value.Length <= 4096 ? value : value[^4096..];
}
