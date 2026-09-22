using System.Diagnostics;
using System.Text.Json;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Tests.TestHelpers;

internal static class CheckCompactionFixture
{
    internal static string CreateRoot()
    {
        var root = Path.GetFullPath(Path.Combine(
            ".antiphon", "acceptance", "card-0079", "crash-" + Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "owner"), Environment.ProcessId.ToString());
        return root;
    }

    internal static (Process Process, Task<string> Output, Task<string> Error) Start(CrashWorkerRequest request)
    {
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
        start.Environment[CheckCompactionCrashWorker.Marker] = JsonSerializer.Serialize(request);
        var process = Process.Start(start) ?? throw new InvalidOperationException("Crash worker did not start.");
        return (process, process.StandardOutput.ReadToEndAsync(), process.StandardError.ReadToEndAsync());
    }

    internal static async Task WaitForHeldAsync(string root, int count, TimeSpan budget)
    {
        var until = DateTime.UtcNow + budget;
        while (Directory.GetFiles(root, "held-*").Length < count && DateTime.UtcNow < until)
            await Task.Delay(25);
    }

    internal static async Task DrainAsync(string root, Process process, Task<string> output, Task<string> error)
    {
        if (!process.HasExited)
            process.Kill(entireProcessTree: true);
        await process.WaitForExitAsync();
        await File.WriteAllTextAsync(Path.Combine(root, $"{process.Id}.stdout.log"), await output);
        await File.WriteAllTextAsync(Path.Combine(root, $"{process.Id}.stderr.log"), await error);
        process.Dispose();
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
