using Antiphon.SessionRunner.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.SessionRunner.Tests;

public class ClaudeDialogInputGuardTests
{
    private const string Clear = "────────────────────\n❯ \n────────────────────\n? for shortcuts";
    private const string Modal = "Do you want to proceed?\n❯ 1. Yes\n2. No\nEnter to confirm · Esc to cancel";

    [Test]
    public async Task C680_EveryWriteRechecksTheCurrentDialog()
    {
        await using var f = new Fixture();
        f.Session.RenderDialogScreenForTest(Modal);
        (await f.SendAsync("body")).Outcome.ShouldBe(ConditionalInputOutcomes.ModalBlocked);
        f.Child.Inputs.ShouldBeEmpty();
        f.Session.RenderDialogScreenForTest(Clear);
        (await f.SendAsync("body")).Outcome.ShouldBe(ConditionalInputOutcomes.Written);
        f.Child.Inputs.ShouldBe(["body"]);
        f.Session.RenderDialogScreenForTest(Modal);
        (await f.SendAsync("\r")).Outcome.ShouldBe(ConditionalInputOutcomes.ModalBlocked);
        f.Child.Inputs.ShouldBe(["body"], "the Enter and retry must leave zero bytes");
        (await f.SendAsync("\r")).Outcome.ShouldBe(ConditionalInputOutcomes.ModalBlocked);
        f.Session.RenderDialogScreenForTest(Clear);
        f.Runtime.ConditionalInputBeforeWrite = (_, _) =>
        {
            f.Session.RenderDialogScreenForTest(Modal);
            return Task.CompletedTask;
        };
        (await f.SendAsync("race")).Outcome.ShouldBe(ConditionalInputOutcomes.StaleObservation);
        f.Child.Inputs.ShouldBe(["body"]);
        f.Runtime.ConditionalInputBeforeWrite = null;
        f.Session.RenderDialogScreenForTest("partial");
        (await f.SendAsync("unknown")).Outcome.ShouldBe(ConditionalInputOutcomes.ObservationUnknown);
        var request = await f.RequestAsync("old-generation");
        (await f.Runtime.SendConditionalInputAsync(f.Id, request with
        { ExpectedAcceptedStartedAt = f.Generation.AddSeconds(-1) }, CancellationToken.None))
            .Outcome.ShouldBe(ConditionalInputOutcomes.GenerationMismatch);
        f.Child.Inputs.ShouldBe(["body"]);
    }

    [Test]
    public async Task C680_InputProofInvalidatesOnWriteFailureAndAdoption()
    {
        await using var f = new Fixture();
        var before = await f.RequestAsync("stale-after-interactive");
        await f.Runtime.SendInputAsync(f.Id, "interactive", CancellationToken.None);
        (await f.Runtime.SendConditionalInputAsync(f.Id, before, CancellationToken.None))
            .Outcome.ShouldBe(ConditionalInputOutcomes.StaleObservation);
        f.Child.Inputs.ShouldBe(["interactive"]);
        var snapshot = await f.Runtime.GetSnapshotAsync(f.Id, CancellationToken.None);
        snapshot.Dialog.ShouldNotBeNull();
        var revision = snapshot.Dialog.InputRevision;
        f.Child.Fail = true;
        await Should.ThrowAsync<IOException>(() => f.Runtime.SendInputAsync(f.Id, "failed", CancellationToken.None));
        snapshot = await f.Runtime.GetSnapshotAsync(f.Id, CancellationToken.None);
        snapshot.Dialog!.InputRevision.ShouldBeGreaterThan(revision);
        snapshot.Dialog.InputHistoryKnown.ShouldBeFalse();
        f.Child.Fail = false;
        (await f.SendAsync("after-failed")).Outcome.ShouldBe(ConditionalInputOutcomes.ObservationUnknown);
        await using var adopted = new Fixture(adopted: true);
        (await adopted.SendAsync("after-adoption")).Outcome.ShouldBe(ConditionalInputOutcomes.ObservationUnknown);
        adopted.Child.Inputs.ShouldBeEmpty();
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "c680-" + Guid.NewGuid().ToString("N"));
        public Guid Id { get; } = Guid.NewGuid();
        public DateTime Generation { get; } = SessionGeneration.Normalize(DateTime.UtcNow);
        public SessionRunnerRuntime Runtime { get; }
        public SessionRunnerRuntime.RunnerSession Session { get; }
        public Child Child { get; } = new();
        public Fixture(bool adopted = false)
        {
            var settings = new SessionRunnerSettings { SessionLogPath = _root, CpuWatchdogEnabled = false };
            Runtime = new(Options.Create(settings), NullLogger<SessionRunnerRuntime>.Instance);
            Session = new(Id, settings, new SessionRunnerEventHub(), NullLogger.Instance);
            Session.BindDialogChildForTest(Child, Generation, Clear, adopted);
            Runtime.Track(Session);
        }
        public async Task<RunnerConditionalInputRequest> RequestAsync(string input)
        {
            var s = await Runtime.GetSnapshotAsync(Id, CancellationToken.None);
            return new(Generation, s.LastSequence, input, true, s.Dialog?.InputRevision ?? 0, s.Dialog?.RuntimeEpoch);
        }
        public async Task<RunnerConditionalInputResult> SendAsync(string input) =>
            await Runtime.SendConditionalInputAsync(Id, await RequestAsync(input), CancellationToken.None);
        public async ValueTask DisposeAsync()
        {
            await Runtime.DisposeAsync();
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }
    }

    private sealed class Child : ISessionChild
    {
        public List<string> Inputs { get; } = [];
        public bool Fail { get; set; }
        public Task WriteAsync(string input, CancellationToken ct)
        {
            if (Fail) throw new IOException("controlled backend failure");
            Inputs.Add(input);
            return Task.CompletedTask;
        }
        public Task<ChildStarted> LaunchAsync(RunnerLaunchRequest request, CancellationToken ct) => throw new NotSupportedException();
        public Task ResizeAsync(int cols, int rows, CancellationToken ct) => Task.CompletedTask;
        public Task<bool> KillAsync(CancellationToken ct) => Task.FromResult(false);
        public Task<ChildScreen?> ReadScreenAsync(CancellationToken ct) => Task.FromResult<ChildScreen?>(null);
        public event Action<ChildExit>? Exited { add { } remove { } }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
