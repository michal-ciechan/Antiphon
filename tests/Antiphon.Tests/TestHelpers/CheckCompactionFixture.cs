using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Tests.TestHelpers;

internal static class CheckCompactionFixture
{
    internal const string RootPrefix = "antiphon-c79-crash-";
    internal const string KeepRootVariable = "ANTIPHON_C79_KEEP_CRASH_ROOT";

    // CARD-1494: a child host takes 8-26 s to start on a loaded runner, so the parent waits on
    // conditions instead of a fixed window. The child budget outlasts ready plus prompt.
    internal static readonly TimeSpan ReadyBudget = TimeSpan.FromSeconds(90);
    internal static readonly TimeSpan PromptBudget = TimeSpan.FromSeconds(60);
    internal static readonly TimeSpan ChildBudget = TimeSpan.FromSeconds(180);
    internal static readonly TimeSpan ExitBudget = ChildBudget + ReadyBudget;
    internal static readonly TimeSpan StopGrace = TimeSpan.FromSeconds(10);

    internal static string OwnedPrefix => Path.Combine(Path.GetFullPath(Path.GetTempPath()), RootPrefix);

    // A per-test temp root, never under the build output that a checkpoint run seals (CARD-1494).
    internal static CrashRoot CreateRoot()
    {
        var root = Directory.CreateTempSubdirectory(RootPrefix).FullName;
        File.WriteAllText(Path.Combine(root, "owner"), Environment.ProcessId.ToString());
        return new CrashRoot(root);
    }

    internal static CrashWorker Start(CrashWorkerRequest request)
    {
        request.OwnedRoot = OwnedPrefix;
        var start = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Environment.CurrentDirectory,
        };
        start.ArgumentList.Add(typeof(CheckCompactionCrashWorker).Assembly.Location);
        start.ArgumentList.Add("--treenode-filter");
        start.ArgumentList.Add("/*/*/CheckCompactionCrashTests/Resume_reservation_survives_crash_without_a_second_launch");
        start.ArgumentList.Add("--results-directory");
        start.ArgumentList.Add(request.Root);
        start.Environment[CheckCompactionCrashWorker.Marker] = JsonSerializer.Serialize(request);
        return new CrashWorker(request.Root, start);
    }

    internal static async Task WaitForHeldAsync(string root, int count, params CrashWorker[] workers)
    {
        var until = DateTime.UtcNow + ReadyBudget;
        while (Directory.GetFiles(root, "held-*").Length < count)
        {
            foreach (var worker in workers)
                worker.ThrowIfExited($"{count} held workers");
            if (DateTime.UtcNow >= until)
                throw new TimeoutException($"Only {Directory.GetFiles(root, "held-*").Length} of {count} crash workers held within {ReadyBudget}."
                    + string.Concat(workers.Select(worker => Environment.NewLine + worker.Describe())));
            await Task.Delay(25);
        }
    }

    internal static async Task<ConfirmedSeat> SeedConfirmedAsync(string connectionString)
    {
        var accepted = SessionGeneration.Normalize(new DateTime(2026, 9, 14, 22, 50, 7, DateTimeKind.Utc));
        var promptAt = accepted.AddMinutes(1);
        var now = promptAt.AddSeconds(4).AddMinutes(10);
        var sessionId = Guid.NewGuid();
        var agentId = Guid.NewGuid();
        var episodeId = Guid.NewGuid();
        var checkId = Guid.NewGuid();
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(connectionString));
        db.Agents.Add(new Agent
        {
            Id = agentId,
            Name = "check",
            Slug = "check-" + agentId.ToString("N")[..8],
            WorkingDirectory = Path.GetTempPath(),
            Kind = AgentKind.ClaudeCode,
            AlwaysOn = true,
            Status = AgentStatus.Running,
            StandingSpecialistRole = AgentTaskRole.Check,
            StandingSpecialistOwnerId = agentId,
            PersistentSessionId = sessionId.ToString("D"),
            CreatedAt = accepted,
            UpdatedAt = accepted,
        });
        db.AgentSessions.Add(new AgentSession
        {
            Id = sessionId,
            StandingAgentId = agentId,
            DefinitionName = "claude",
            AgentKind = AgentKind.ClaudeCode,
            Status = SessionStatus.Running,
            Cwd = Path.GetTempPath(),
            CreatedAt = accepted,
            StartedAt = accepted,
            LastSeenAt = now,
        });
        db.AgentSupervisionStates.Add(new AgentSupervisionState
        {
            AgentId = agentId,
            ActiveCompactionRecoveryId = episodeId,
            UpdatedAt = now,
        });
        db.CheckCompactionRecoveries.Add(new CheckCompactionRecovery
        {
            Id = episodeId,
            PhysicalAgentId = agentId,
            SessionId = sessionId,
            AcceptedStartedAt = accepted,
            BoundaryIdentity = "boundary-1",
            NativeContinuationIdentity = "cont-1",
            BoundaryCreatedAt = promptAt.AddSeconds(3),
            ContinuationCreatedAt = promptAt.AddSeconds(4),
            ConfiguredThresholdMinutes = 10,
            DetectedAt = promptAt.AddSeconds(4),
            State = CheckCompactionRecoveryState.Confirmed,
            ObservationBindingIdentity = "bind-1",
            ObservationTranscriptRevision = 1,
            ObservationOutputRevision = 1,
        });
        db.AgentTasks.Add(new AgentTask
        {
            Id = checkId,
            RootTaskId = checkId,
            Title = "check",
            Goal = "look",
            Role = AgentTaskRole.Check,
            Kind = AgentTaskKind.Worker,
            Status = AgentTaskStatus.Succeeded,
            AgentId = agentId,
            AgentSessionId = sessionId,
            WorkingDirectory = Path.GetTempPath(),
            CreatedAt = promptAt,
            CompletedAt = promptAt,
        });
        db.TranscriptEntries.AddRange(
            new TranscriptEntry
            {
                Id = Guid.NewGuid(), AgentSessionId = sessionId, Sequence = 10,
                Kind = TranscriptKinds.TurnEnd, Timestamp = accepted, CreatedAt = accepted,
            },
            new TranscriptEntry
            {
                Id = Guid.NewGuid(), AgentSessionId = sessionId, Sequence = 11,
                Kind = TranscriptKinds.UserPrompt, Text = "check " + checkId.ToString("D"),
                Timestamp = promptAt, CreatedAt = promptAt,
            },
            new TranscriptEntry
            {
                Id = Guid.NewGuid(), AgentSessionId = sessionId, Sequence = 12,
                Kind = TranscriptKinds.CompactBoundary, Text = "Context compacted (auto)",
                Timestamp = promptAt.AddSeconds(3), CreatedAt = promptAt.AddSeconds(3), Uuid = "boundary-1",
            },
            new TranscriptEntry
            {
                Id = Guid.NewGuid(), AgentSessionId = sessionId, Sequence = 13,
                Kind = TranscriptKinds.UserPrompt,
                Text = TranscriptKinds.CompactionContinuationPromptPrefix + " summary",
                Timestamp = promptAt.AddSeconds(4), CreatedAt = promptAt.AddSeconds(4), Uuid = "cont-1",
            });
        await db.SaveChangesAsync();
        return new ConfirmedSeat(connectionString, sessionId, agentId, episodeId, accepted);
    }

    internal sealed record ConfirmedSeat(
        string ConnectionString, Guid SessionId, Guid AgentId, Guid EpisodeId, DateTime Accepted);
}

/// <summary>CARD-1494: the crash-worker root, deleted when the test ends unless kept for triage.</summary>
internal sealed class CrashRoot(string fullPath) : IAsyncDisposable
{
    public string FullPath { get; } = fullPath;

    public ValueTask DisposeAsync()
    {
        if (Environment.GetEnvironmentVariable(CheckCompactionFixture.KeepRootVariable) == "1")
        {
            Console.WriteLine($"CARD-1494 kept crash-worker root {FullPath}");
            return ValueTask.CompletedTask;
        }

        try
        {
            Directory.Delete(FullPath, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.WriteLine($"CARD-1494 could not delete crash-worker root {FullPath}: {ex.Message}");
        }

        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// CARD-1494: one crash-worker child. Disposal writes stop, waits, then kills and drains it, so
/// a failed wait or assertion cannot leave an orphan writing into its root.
/// </summary>
internal sealed class CrashWorker : IAsyncDisposable
{
    private readonly string _root;
    private readonly StringBuilder _output = new();
    private readonly StringBuilder _error = new();
    private bool _drained;

    public CrashWorker(string root, ProcessStartInfo start)
    {
        _root = root;
        Process = new Process { StartInfo = start };
        Process.OutputDataReceived += (_, e) => Append(_output, e.Data);
        Process.ErrorDataReceived += (_, e) => Append(_error, e.Data);
        if (!Process.Start())
            throw new InvalidOperationException("Crash worker did not start.");
        Process.BeginOutputReadLine();
        Process.BeginErrorReadLine();
    }

    public Process Process { get; }

    public string Output => Snapshot(_output);

    public string Error => Snapshot(_error);

    public Task WaitForHeldAsync() =>
        WaitUntilAsync(() => Task.FromResult(File.Exists(Path.Combine(_root, $"held-{Process.Id}"))),
            CheckCompactionFixture.ReadyBudget, "the held boundary");

    // Ready means the scanner finished its first notification pass.
    public Task WaitForReadyAsync() =>
        WaitUntilAsync(() => Task.FromResult(ScanCount() > 0), CheckCompactionFixture.ReadyBudget, "the first notification scan");

    public async Task WaitUntilAsync(Func<Task<bool>> condition, TimeSpan budget, string what, Func<Task<string>>? describe = null)
    {
        var until = DateTime.UtcNow + budget;
        while (!await condition())
        {
            ThrowIfExited(what);
            if (DateTime.UtcNow >= until)
            {
                var state = describe is null ? "" : Environment.NewLine + await describe();
                throw new TimeoutException($"Crash worker never reached {what} within {budget}.{state}{Environment.NewLine}{Describe()}");
            }

            await Task.Delay(100);
        }
    }

    public async Task WaitForExitAsync()
    {
        using var budget = new CancellationTokenSource(CheckCompactionFixture.ExitBudget);
        try
        {
            await Process.WaitForExitAsync(budget.Token);
        }
        catch (OperationCanceledException)
        {
            throw new TimeoutException($"Crash worker did not exit within {CheckCompactionFixture.ExitBudget}.{Environment.NewLine}{Describe()}");
        }
    }

    public void ThrowIfExited(string what)
    {
        if (!Process.HasExited)
            return;
        Process.WaitForExit(); // flushes the redirected stderr into Describe
        throw new InvalidOperationException($"Crash worker exited with {Process.ExitCode} before {what}.{Environment.NewLine}{Describe()}");
    }

    public string Describe() =>
        $"crash worker {Process.Id} root {_root} scans {ScanCount()}{Environment.NewLine}stderr:{Environment.NewLine}{Error}";

    public async Task StopAsync()
    {
        if (_drained)
            return;
        await File.WriteAllTextAsync(Path.Combine(_root, "stop"), "stop");
        using var grace = new CancellationTokenSource(CheckCompactionFixture.StopGrace);
        try
        {
            await Process.WaitForExitAsync(grace.Token);
        }
        catch (OperationCanceledException)
        {
        }

        await DrainAsync();
    }

    // Kills the child if it still runs (the committed-cut crash), waits and keeps its output.
    public async Task DrainAsync()
    {
        if (_drained)
            return;
        _drained = true;
        if (!Process.HasExited)
            Process.Kill(entireProcessTree: true);
        await Process.WaitForExitAsync();
        await File.WriteAllTextAsync(Path.Combine(_root, $"{Process.Id}.stdout.log"), Output);
        await File.WriteAllTextAsync(Path.Combine(_root, $"{Process.Id}.stderr.log"), Error);
        Process.Dispose();
    }

    public async ValueTask DisposeAsync() => await StopAsync();

    private int ScanCount()
    {
        var scans = Path.Combine(_root, "scans.txt");
        return File.Exists(scans) ? File.ReadAllLines(scans).Length : 0;
    }

    private static void Append(StringBuilder buffer, string? line)
    {
        if (line is null)
            return;
        lock (buffer)
            buffer.AppendLine(line);
    }

    private static string Snapshot(StringBuilder buffer)
    {
        lock (buffer)
            return buffer.ToString();
    }
}
