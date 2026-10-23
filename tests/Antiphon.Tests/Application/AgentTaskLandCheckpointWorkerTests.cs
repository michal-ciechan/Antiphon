using Antiphon.Server.Application.Services;
using Antiphon.Server.Infrastructure.Git;
using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
[Category("Slow")]
public sealed class AgentTaskLandCheckpointWorkerTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task C665_WorkerDeathKeepsRecordedTreeAcrossActualRegistrationDrop(bool dropped)
    {
        await using var h = new LandingSafetyHarness();
        await h.InitializeAsync();
        var source = await h.AddSourceAsync();
        var ready = Path.Combine(h.Fixture.Root, "removal-ready.json");
        var start = new System.Diagnostics.ProcessStartInfo("dotnet")
        {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
        };
        foreach (var arg in new[] { typeof(LandingSafetyHarness).Assembly.Location, "--treenode-filter",
            "/*/*/AgentTaskLandCheckpointWorkerTests/C665_*" }) start.ArgumentList.Add(arg);
        start.Environment[LandingRemovalCrashWorker.Marker] = System.Text.Json.JsonSerializer.Serialize(
            new LandingRemovalCrashWorker.Request(h.Fixture.Root, h.Fixture.TaskId,
                dropped ? "C665-after-drop" : "C665-before-drop", ready));
        start.Environment["ANTIPHON_C448_TEST_CONNECTION"] = h.Schema.ConnectionString;
        using var worker = System.Diagnostics.Process.Start(start)!;
        var output = worker.StandardOutput.ReadToEndAsync();
        var error = worker.StandardError.ReadToEndAsync();
        try
        {
            using var budget = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            while (!File.Exists(ready) && !worker.HasExited) await Task.Delay(100, budget.Token);
            File.Exists(ready).ShouldBeTrue(worker.HasExited ? await error : "real removal boundary not reached");
            var op = (await h.OperationAsync()).ShouldNotBeNull();
            op.Phase.ShouldBe(LandPhase.CleanupStarted);
            var aside = WorktreeSetAside.SetAsidePath(h.Fixture.Source);
            var record = WorktreeSetAside.Read(op.CommonDirectory, h.Fixture.Source).ShouldNotBeNull();
            record.SetAsidePath.ShouldBe(aside);
            record.GitDirectory.ShouldBe(op.GitDirectory);
            Directory.Exists(h.Fixture.Source).ShouldBeFalse();
            Directory.Exists(op.GitDirectory).ShouldBe(!dropped);
            var rows = await h.Fixture.Git.RegistrationsAsync(h.Fixture.Repository, default);
            rows.Any(r => LandingGit.PathsEqual(r.Path, h.Fixture.Source)).ShouldBe(!dropped);
            (await File.ReadAllTextAsync(Path.Combine(aside, "feature.txt"))).ShouldBe("valuable feature\n");
            worker.Kill(entireProcessTree: true);
            await worker.WaitForExitAsync(budget.Token);
            await h.RestartServicesAsync();
            await h.RunAsync();
            var recovered = (await h.OperationAsync()).ShouldNotBeNull();
            recovered.Id.ShouldBe(op.Id);
            recovered.RemoteConfirmedAt.ShouldBe(op.RemoteConfirmedAt);
            if (!dropped)
            {
                // Recovery restores the intact tree, then respects the previously spent command
                // slot. A new request is needed to authorize another registration drop.
                recovered.LastReason.ShouldBe("cleanup_command_slot_spent");
                Directory.Exists(op.GitDirectory).ShouldBeTrue();
                (await File.ReadAllTextAsync(Path.Combine(h.Fixture.Source, "feature.txt"))).ShouldBe("valuable feature\n");
                WorktreeSetAside.Read(op.CommonDirectory, h.Fixture.Source).ShouldBeNull();
                await h.RepostAsync();
                await h.RunAsync();
                recovered = (await h.OperationAsync()).ShouldNotBeNull();
            }
            recovered.Cleanup.ShouldBe(LandCleanupStatus.Complete, recovered.LastReason);
            Directory.Exists(aside).ShouldBeFalse();
            Directory.Exists(h.Fixture.Source).ShouldBeFalse();
            Directory.Exists(op.GitDirectory).ShouldBeFalse();
            WorktreeSetAside.Read(op.CommonDirectory, h.Fixture.Source).ShouldBeNull();
            (await h.Fixture.RequiredAsync(h.Fixture.Repository, "rev-parse", op.RecoveryRefPrefix + "/source")).Trim().ShouldBe(source);
            await h.Fixture.AssertRemoteSourceAsync();
        }
        finally
        {
            if (!worker.HasExited) worker.Kill(entireProcessTree: true);
            await worker.WaitForExitAsync();
            await Task.WhenAll(output, error);
        }
    }

    // Cleanup intentionally catches ordinary failures. Cancellation models an interruption that
    // escapes the coordinator; async continuations may surface it as TaskCanceledException.
    // Each caller also requires its exact hook to have fired. Worker-death rows exercise abrupt exit.
}
