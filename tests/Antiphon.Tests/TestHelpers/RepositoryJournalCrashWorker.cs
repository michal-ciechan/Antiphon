using System.Diagnostics;
using System.Text.Json;
using Antiphon.Server.Infrastructure.Git;

namespace Antiphon.Tests.TestHelpers;

internal static class RepositoryJournalCrashWorker
{
    internal const string Marker = "ANTIPHON_C452_JOURNAL_WORKER";
    internal sealed record Request(string Repository, string Ready, string Cut);

    internal static async Task RunAsync(string encoded)
    {
        var request = JsonSerializer.Deserialize<Request>(encoded)
            ?? throw new InvalidOperationException("Missing journal worker request");
        var repository = Path.GetFullPath(request.Repository);
        var ready = Path.GetFullPath(request.Ready);
        var tempRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())) + Path.DirectorySeparatorChar;
        if (!repository.StartsWith(tempRoot, StringComparison.Ordinal)
            || !Path.GetFileName(repository).StartsWith("antiphon-c452-", StringComparison.Ordinal)
            || !ready.StartsWith(tempRoot, StringComparison.Ordinal)
            || request.Cut is not ("first" or "started" or "completed"))
            throw new InvalidOperationException("Unowned journal crash worker");

        async Task Pause(string temporary)
        {
            var marker = ready + ".tmp";
            await File.WriteAllTextAsync(marker, JsonSerializer.Serialize(new { temporary, worker = Environment.ProcessId }));
            File.Move(marker, ready);
            await Task.Delay(Timeout.InfiniteTimeSpan);
        }

        if (request.Cut == "first")
        {
            RepositoryChildJournal.BeforeReplaceForTests = Pause;
            await RepositoryChildJournal.BeginAsync(repository, CancellationToken.None);
        }
        else
        {
            var journal = await RepositoryChildJournal.BeginAsync(repository, CancellationToken.None);
            if (request.Cut == "started")
            {
                RepositoryChildJournal.BeforeReplaceForTests = Pause;
                await journal.StartedAsync(999999, DateTime.UtcNow.Ticks, CancellationToken.None);
            }
            else
            {
                var start = new ProcessStartInfo("pwsh") { UseShellExecute = false, CreateNoWindow = true };
                start.ArgumentList.Add("-NoProfile");
                start.ArgumentList.Add("-Command");
                start.ArgumentList.Add("Start-Sleep -Seconds 90");
                using var sleeper = Process.Start(start)!;
                await journal.StartedAsync(sleeper.Id, sleeper.StartTime.ToUniversalTime().Ticks, CancellationToken.None);
                sleeper.Kill(entireProcessTree: true);
                await sleeper.WaitForExitAsync();
                RepositoryChildJournal.BeforeReplaceForTests = Pause;
                await journal.StartedAsync(sleeper.Id, startTicks: null, CancellationToken.None);
            }
        }
        throw new InvalidOperationException("Journal crash barrier did not fire");
    }
}
