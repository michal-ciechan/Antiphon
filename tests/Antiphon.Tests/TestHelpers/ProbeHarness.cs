using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Antiphon.Messaging;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Tests.TestHelpers;

/// <summary>
/// CARD-0418 F-3 parent. Owns the durable state that must SURVIVE a killed child — the database, the
/// outbound store, the fixture directory and the producer evidence sink — and launches one
/// <c>Antiphon.ChannelOutbound.Probe</c> per cut against it.
///
/// <para>Kills are by recorded PID and are checked before they are made: the harness kills only a
/// process it started and still owns, and only after the child has written the marker naming the
/// barrier it reached. Its own leftovers are the only teardown targets.</para>
/// </summary>
internal sealed class ProbeHarness(ChannelOutboundWorld world) : IAsyncDisposable
{
    private readonly List<Process> _children = [];
    private readonly string _fixtureRoot = Path.Combine(world.Root, "probe");

    public string EvidencePath => Path.Combine(_fixtureRoot, "producer-evidence.jsonl");

    public sealed record ProbeRun(int ExitCode, bool Killed, string StandardOutput, string StandardError, int Pid);

    /// <summary>
    /// Runs one probe to completion, or — when <paramref name="dieAt"/> names a barrier — until it
    /// parks there, at which point this kills it.
    /// </summary>
    public async Task<ProbeRun> RunAsync(
        CancellationToken ct,
        string action,
        string? dieAt = null,
        string producerMode = "accept",
        long promptSequence = 1)
    {
        Directory.CreateDirectory(_fixtureRoot);
        if (action == "pump")
            await ClearLeasesAsync(ct);
        var nonce = Guid.NewGuid().ToString("N");
        var markerDir = Path.Combine(_fixtureRoot, "markers-" + nonce);
        Directory.CreateDirectory(markerDir);

        var reply = ChannelOutboundWorld.MarkdownReply();
        var config = new
        {
            connectionString = world.ConnectionString,
            storeRoot = world.StoreRoot,
            markerDirectory = markerDir,
            producerEvidencePath = EvidencePath,
            nonce,
            action,
            dieAtBarrier = dieAt,
            pauseBeforeIntentCommit = false,
            channelId = world.ChannelX,
            projectId = world.ProjectP,
            converterAgentId = world.AgentC,
            profileName = ChannelOutboundWorld.ProfileName,
            promptFile = "prompts/pdf.md",
            timeoutSeconds = 600,
            replyJson = JsonSerializer.Serialize(reply, MessagingJson.Options),
            promptSequence,
            producerMode,
        };

        var configPath = Path.Combine(_fixtureRoot, "config-" + nonce + ".json");
        await File.WriteAllTextAsync(configPath, JsonSerializer.Serialize(config), ct);

        var probeDll = ProbePath();
        var start = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = _fixtureRoot,
        };
        start.ArgumentList.Add(probeDll);
        start.ArgumentList.Add("--config");
        start.ArgumentList.Add(configPath);

        var child = Process.Start(start) ?? throw new InvalidOperationException("probe did not start");
        lock (_children)
            _children.Add(child);
        var pid = child.Id;
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        var outTask = PumpAsync(child.StandardOutput, stdout);
        var errTask = PumpAsync(child.StandardError, stderr);

        var killed = false;
        if (dieAt is not null)
        {
            var marker = Path.Combine(markerDir, dieAt + ".marker");
            await WaitForAsync(
                () => File.Exists(marker) || child.HasExited,
                TimeSpan.FromSeconds(90),
                $"probe never reached barrier '{dieAt}'");
            if (!File.Exists(marker))
            {
                throw new InvalidOperationException(
                    $"probe exited before barrier '{dieAt}': {stdout}{stderr}");
            }

            // The marker names the pid that wrote it. Kill only that process, and only if it is
            // still the one this harness started.
            var recorded = (await File.ReadAllTextAsync(marker, ct)).Trim().Split(' ');
            recorded[0].ShouldBeNonce(nonce);
            int.Parse(recorded[^1]).ShouldEqualPid(pid);
            if (!child.HasExited)
            {
                child.Kill(entireProcessTree: true);
                killed = true;
            }
        }
        else if (producerMode == "accept-then-park")
        {
            // The park happens INSIDE the producer, after the acceptance is durable.
            await WaitForAsync(
                () => AcceptedRecords().Count > 0 || child.HasExited,
                TimeSpan.FromSeconds(90),
                "probe never reached producer acceptance");
            if (!child.HasExited)
            {
                child.Kill(entireProcessTree: true);
                killed = true;
            }
        }

        await child.WaitForExitAsync(ct).WaitAsync(TimeSpan.FromSeconds(90), ct);
        await Task.WhenAll(outTask, errTask).WaitAsync(TimeSpan.FromSeconds(30), ct);
        return new ProbeRun(killed ? -1 : child.ExitCode, killed, stdout.ToString(), stderr.ToString(), pid);
    }

    /// <summary>Every acceptance any probe ever made, including probes that never returned.</summary>
    public IReadOnlyList<JsonElement> AcceptedRecords()
    {
        if (!File.Exists(EvidencePath))
            return [];
        return File.ReadAllLines(EvidencePath)
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .Select(l => JsonDocument.Parse(l).RootElement.Clone())
            .ToList();
    }

    public async Task<ChannelOutboundDelivery> SingleDeliveryAsync()
    {
        await using var db = world.NewContext();
        return await db.ChannelOutboundDeliveries.AsNoTracking().SingleAsync();
    }

    /// <summary>
    /// Puts the delivery where a finished worker would leave it: Converting behind a Succeeded task,
    /// with a real output manifest on disk. The worker itself is not what these cuts are about.
    /// </summary>
    public async Task MoveToConvertedAsync(Guid deliveryId, CancellationToken ct)
    {
        var taskId = Guid.NewGuid();
        await using (var db = world.NewContext())
        {
            db.AgentTasks.Add(new AgentTask
            {
                Id = taskId,
                RootTaskId = taskId,
                Title = "Outbound conversion",
                Goal = "Convert.",
                Kind = AgentTaskKind.Worker,
                Role = AgentTaskRole.Custom,
                ModelLevel = AgentModelLevel.Medium,
                Workspace = WorkspaceMode.Shared,
                WorkingDirectory = world.ConverterWorkspace,
                RepoPath = world.ConverterWorkspace,
                Status = AgentTaskStatus.Succeeded,
                CreatedAt = DateTime.UtcNow,
                CompletedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync(ct);
            await db.ChannelOutboundDeliveries.Where(d => d.Id == deliveryId)
                .ExecuteUpdateAsync(u => u
                    .SetProperty(d => d.State, ChannelOutboundDeliveryState.Converting)
                    .SetProperty(d => d.ConversionTaskId, taskId)
                    .SetProperty(d => d.LeaseExpiresAt, (DateTime?)null)
                    .SetProperty(d => d.LeaseOwner, (string?)null), ct);
        }

        await world.WriteWorkerOutputAsync(deliveryId, "unchanged");
    }

    /// <summary>
    /// Expires every lease so the next probe can claim.
    ///
    /// <para>A probe that is KILLED leaves its lease behind with 30 real seconds still on it, and
    /// these tests advance no clock the child can see — the child runs on <c>TimeProvider.System</c>
    /// by construction, because a crash-recovery probe with a fake clock is not a crash-recovery
    /// probe. Expiring the lease is how the parent says "time passed" without sleeping through it.
    /// It only makes the row MORE claimable, so it can only make a duplicate-send failure easier to
    /// catch, never harder; the lease rule itself is asserted against the real claim query in
    /// <c>ChannelOutboundStorageTests.A_second_pump_waits_for_the_lease_then_resumes_the_same_work</c>.</para>
    /// </summary>
    private async Task ClearLeasesAsync(CancellationToken ct)
    {
        await using var db = world.NewContext();
        await db.ChannelOutboundDeliveries
            .ExecuteUpdateAsync(u => u
                .SetProperty(d => d.LeaseExpiresAt, (DateTime?)null)
                .SetProperty(d => d.LeaseOwner, (string?)null), ct);
    }

    /// <summary>Clears a lease and returns a sealed delivery to Ready for the next cut.</summary>
    public async Task ResetToReadyAsync(Guid deliveryId, CancellationToken ct)
    {
        await using var db = world.NewContext();
        await db.ChannelOutboundDeliveries.Where(d => d.Id == deliveryId)
            .ExecuteUpdateAsync(u => u
                .SetProperty(d => d.State, ChannelOutboundDeliveryState.Ready)
                .SetProperty(d => d.LeaseExpiresAt, (DateTime?)null)
                .SetProperty(d => d.LeaseOwner, (string?)null), ct);
    }

    private static string ProbePath()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "outbound-probe", "Antiphon.ChannelOutbound.Probe.dll");
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                "The F-3 probe was not copied beside the tests. Build Antiphon.Tests (the "
                + "CopyOutboundProbe target puts it in outbound-probe/).", path);
        }

        return path;
    }

    private static async Task PumpAsync(StreamReader reader, StringBuilder sink)
    {
        string? line;
        while ((line = await reader.ReadLineAsync()) is not null)
            sink.AppendLine(line);
    }

    private static async Task WaitForAsync(Func<bool> condition, TimeSpan budget, string message)
    {
        var deadline = DateTime.UtcNow + budget;
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
                return;
            await Task.Delay(50);
        }

        throw new TimeoutException(message);
    }

    public async ValueTask DisposeAsync()
    {
        List<Process> children;
        lock (_children)
        {
            children = [.. _children];
            _children.Clear();
        }

        foreach (var child in children)
        {
            try
            {
                if (!child.HasExited)
                    child.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception) { }
            finally
            {
                child.Dispose();
            }
        }

        await Task.CompletedTask;
    }
}

internal static class ProbeAssertions
{
    public static void ShouldBeNonce(this string actual, string expected)
    {
        if (!string.Equals(actual, expected, StringComparison.Ordinal))
            throw new InvalidOperationException($"probe marker nonce {actual} is not this launch's {expected}");
    }

    public static void ShouldEqualPid(this int actual, int expected)
    {
        if (actual != expected)
            throw new InvalidOperationException($"probe marker pid {actual} is not the process this harness started ({expected})");
    }
}
