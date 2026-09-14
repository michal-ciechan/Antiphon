using Antiphon.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.SessionRunner.Tests;

[NotInParallel("SessionLiveness")]
[ParallelLimiter<ProcessSpawnLimit>]
public class RemoteControlConditionalInputTests
{
    private static string Cmd => Path.Combine(Environment.SystemDirectory, "cmd.exe");

    [Test]
    public async Task C514_Input_uses_microsecond_accepted_generation()
    {
        var logRoot = TestSessionLogRoot.Create("c514-gen");
        await using var runtime = Runtime(logRoot);
        var sessionId = Guid.NewGuid();
        var generation = SessionGeneration.Normalize(DateTime.UtcNow);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await runtime.StartAsync(Launch(sessionId, generation), cts.Token);
        var snapshot = runtime.GetSnapshot(sessionId);

        var written = await runtime.SendConditionalInputAsync(
            sessionId,
            new RunnerConditionalInputRequest(generation, snapshot.LastSequence, "echo ok\r"),
            cts.Token);
        written.Outcome.ShouldBe(ConditionalInputOutcomes.Written);
        runtime.SnapshotBackendWrites().ShouldContain(w => w.SessionId == sessionId && w.Input.Contains("echo ok", StringComparison.Ordinal));

        var mismatch = SessionGeneration.Normalize(generation.AddTicks(SessionGeneration.MicrosecondTicks));
        var refused = await runtime.SendConditionalInputAsync(
            sessionId,
            new RunnerConditionalInputRequest(mismatch, snapshot.LastSequence, "echo no\r"),
            cts.Token);
        refused.Outcome.ShouldBe(ConditionalInputOutcomes.GenerationMismatch);
        runtime.SnapshotBackendWrites().Count(w => w.Input.Contains("echo no", StringComparison.Ordinal)).ShouldBe(0);
        await runtime.KillAsync(sessionId, TimeSpan.FromSeconds(5), cts.Token);
    }

    [Test]
    public async Task C514_Stale_sequence_refuses_with_zero_backend_bytes()
    {
        var logRoot = TestSessionLogRoot.Create("c514-seq");
        await using var runtime = Runtime(logRoot);
        var sessionId = Guid.NewGuid();
        var generation = SessionGeneration.Normalize(DateTime.UtcNow);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await runtime.StartAsync(Launch(sessionId, generation), cts.Token);
        var snapshot = runtime.GetSnapshot(sessionId);
        var refused = await runtime.SendConditionalInputAsync(
            sessionId,
            new RunnerConditionalInputRequest(generation, snapshot.LastSequence + 50, "stale\r"),
            cts.Token);
        refused.Outcome.ShouldBe(ConditionalInputOutcomes.StaleObservation);
        runtime.SnapshotBackendWrites().ShouldBeEmpty();
        await runtime.KillAsync(sessionId, TimeSpan.FromSeconds(5), cts.Token);
    }

    [Test]
    public async Task C514_Replacement_cannot_enter_between_validation_and_write()
    {
        var logRoot = TestSessionLogRoot.Create("c514-gate");
        await using var runtime = Runtime(logRoot);
        var sessionId = Guid.NewGuid();
        var generationA = SessionGeneration.Normalize(DateTime.UtcNow);
        var generationB = SessionGeneration.Next(generationA, DateTime.UtcNow.AddMinutes(1));
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        await runtime.StartAsync(Launch(sessionId, generationA), cts.Token);
        var snapshot = runtime.GetSnapshot(sessionId);
        var held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var proceed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        runtime.ConditionalInputBeforeWrite = async (_, _) =>
        {
            held.TrySetResult();
            await proceed.Task.WaitAsync(cts.Token);
        };

        var write = runtime.SendConditionalInputAsync(
            sessionId,
            new RunnerConditionalInputRequest(generationA, snapshot.LastSequence, "hold\r"),
            cts.Token);
        await held.Task.WaitAsync(cts.Token);
        var replace = runtime.StartAsync(Launch(sessionId, generationB), cts.Token);
        await Task.Delay(200, cts.Token);
        replace.IsCompleted.ShouldBeFalse();
        proceed.TrySetResult();
        var result = await write;
        result.Outcome.ShouldBe(ConditionalInputOutcomes.Written);
        await replace;
        runtime.Get(sessionId).AcceptedStartedAt.ShouldBe(generationB);
        await runtime.KillAsync(sessionId, TimeSpan.FromSeconds(5), cts.Token);
    }

    [Test]
    public async Task C514_Nonwritable_destinations_have_distinct_refusals()
    {
        var logRoot = TestSessionLogRoot.Create("c514-dest");
        await using var runtime = Runtime(logRoot);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var missing = await runtime.SendConditionalInputAsync(
            Guid.NewGuid(),
            new RunnerConditionalInputRequest(DateTime.UtcNow, 0, "x"),
            cts.Token);
        missing.Outcome.ShouldBe(ConditionalInputOutcomes.Missing);

        var sessionId = Guid.NewGuid();
        var generation = SessionGeneration.Normalize(DateTime.UtcNow);
        await runtime.StartAsync(Launch(sessionId, generation), cts.Token);
        await runtime.KillAsync(sessionId, TimeSpan.FromSeconds(5), cts.Token);
        var snapshot = runtime.GetSnapshot(sessionId);
        var exited = await runtime.SendConditionalInputAsync(
            sessionId,
            new RunnerConditionalInputRequest(generation, snapshot.LastSequence, "x"),
            cts.Token);
        exited.Outcome.ShouldBe(ConditionalInputOutcomes.Exited);
        runtime.SnapshotBackendWrites().ShouldBeEmpty();
    }

    private static SessionRunnerRuntime Runtime(string logRoot) =>
        new(Options.Create(new SessionRunnerSettings
        {
            SessionLogPath = logRoot,
            PtyHostLingerHours = 0.02,
            CpuWatchdogEnabled = false,
        }), NullLogger<SessionRunnerRuntime>.Instance);

    private static RunnerLaunchRequest Launch(Guid sessionId, DateTime generation) =>
        new(sessionId, Cmd, ["/d", "/q", "/k", "@echo off & prompt $G"],
            new Dictionary<string, string>(), Path.GetTempPath(), 80, 24,
            AcceptedStartedAt: generation);
}
