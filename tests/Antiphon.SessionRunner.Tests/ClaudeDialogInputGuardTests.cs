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
        await using var late = new Fixture();
        var raceTailer = new Tailer(late.Id) { BeforeObservation = () => late.Session.RenderDialogScreenForTest(Modal) };
        late.Session.BindChildForTest(late.Child, raceTailer, late.Generation);
        (await late.SendAsync("late evidence read race")).Outcome.ShouldBe(ConditionalInputOutcomes.StaleObservation);
        late.Child.Inputs.ShouldBeEmpty();
        await using var replacement = new Fixture();
        var replacementChild = new Child();
        var replacementSession = new SessionRunnerRuntime.RunnerSession(replacement.Id,
            replacement.Settings, new SessionRunnerEventHub(), NullLogger.Instance);
        replacementSession.BindDialogChildForTest(replacementChild, replacement.Generation, Clear);
        replacement.Runtime.ConditionalInputBeforeWrite = (_, _) =>
        {
            replacement.Runtime.ReplaceTrackedSessionForTest(replacement.Session, replacementSession);
            return Task.CompletedTask;
        };
        (await replacement.SendAsync("replaced-object")).Outcome.ShouldBe(ConditionalInputOutcomes.StaleObservation);
        replacement.Child.Inputs.ShouldBeEmpty();
        replacementChild.Inputs.ShouldBeEmpty();
        await replacement.Session.DisposeAsync();
        await using var startup = new Fixture();
        startup.Session.RenderDialogScreenForTest("Quick safety check: Is this a project you created or one you trust?\n❯ 1. Yes, I trust this folder\n2. No, exit\nEnter to confirm · Esc to cancel");
        var trust = await startup.RequestAsync("1");
        (await startup.Runtime.SendConditionalInputAsync(startup.Id, trust, CancellationToken.None))
            .Outcome.ShouldBe(ConditionalInputOutcomes.ModalBlocked);
        (await startup.Runtime.SendConditionalInputAsync(startup.Id, trust with { DialogIntent = ClaudeDialogInputIntent.StartupTrust }, CancellationToken.None))
            .Outcome.ShouldBe(ConditionalInputOutcomes.Written);
        var arbitrary = await startup.RequestAsync("arbitrary task text");
        (await startup.Runtime.SendConditionalInputAsync(startup.Id, arbitrary with { DialogIntent = ClaudeDialogInputIntent.StartupTrust }, CancellationToken.None))
            .Outcome.ShouldBe(ConditionalInputOutcomes.ModalBlocked);
        startup.Child.Inputs.ShouldBe(["1"]);
        startup.Session.RenderDialogScreenForTest("Use Fable 5.1 at high effort by default?\n> Keep xhigh\nSwitch Fable 5.1 to high effort");
        var effort = await startup.RequestAsync("\r");
        (await startup.Runtime.SendConditionalInputAsync(startup.Id, effort with { DialogIntent = ClaudeDialogInputIntent.StartupEffortSwitch }, CancellationToken.None))
            .Outcome.ShouldBe(ConditionalInputOutcomes.ModalBlocked);
        (await startup.Runtime.SendConditionalInputAsync(startup.Id, effort with { DialogIntent = ClaudeDialogInputIntent.StartupEffortKeep }, CancellationToken.None))
            .Outcome.ShouldBe(ConditionalInputOutcomes.Written);
        startup.Child.Inputs.ShouldBe(["1", "\r"]);
        f.Session.RenderDialogScreenForTest(Clear);
        var epochRequest = await f.RequestAsync("wrong-epoch");
        (await f.Runtime.SendConditionalInputAsync(f.Id, epochRequest with
        { ExpectedRuntimeEpoch = Guid.NewGuid() }, CancellationToken.None))
            .Outcome.ShouldBe(ConditionalInputOutcomes.StaleObservation);
        (await f.Runtime.SendConditionalInputAsync(f.Id, epochRequest with
        { ExpectedInputRevision = null }, CancellationToken.None))
            .Outcome.ShouldBe(ConditionalInputOutcomes.StaleObservation);
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

        await using var proof = new Fixture();
        var tailer = new Tailer(proof.Id);
        proof.Session.BindChildForTest(proof.Child, tailer, proof.Generation);
        await proof.Runtime.GetSnapshotAsync(proof.Id, CancellationToken.None);
        proof.Clock.Now += TimeSpan.FromMilliseconds(500);
        await proof.Runtime.GetSnapshotAsync(proof.Id, CancellationToken.None);
        var body = new string('a', 220) + " required complete tail";
        (await proof.SendAsync(body)).Outcome.ShouldBe(ConditionalInputOutcomes.Written);
        (await proof.SendAsync("\r")).Outcome.ShouldBe(ConditionalInputOutcomes.Written);
        tailer.Prompt(body[..200]);
        (await proof.Runtime.GetSnapshotAsync(proof.Id, CancellationToken.None)).Dialog!.SubmittedInput.ShouldBeNull();
        tailer.Prompt(body);
        var matched = (await proof.Runtime.GetSnapshotAsync(proof.Id, CancellationToken.None)).Dialog!;
        matched.SubmittedInput.ShouldNotBeNull();
        matched.SubmittedInput.SubmitRevision.ShouldBe(matched.InputRevision);
        matched.SubmittedInput.TranscriptFloor.ShouldBe(0);
        await proof.Runtime.SendInputAsync(proof.Id, "later draft", CancellationToken.None);
        (await proof.Runtime.GetSnapshotAsync(proof.Id, CancellationToken.None)).Dialog!.SubmittedInput.ShouldBeNull();

        // A single empty frame never arms positive custody, even with a complete receipt.
        await using var single = new Fixture();
        var singleTailer = new Tailer(single.Id);
        single.Session.BindChildForTest(single.Child, singleTailer, single.Generation);
        await single.SendAsync(body);
        await single.SendAsync("\r");
        singleTailer.Prompt(body);
        (await single.Runtime.GetSnapshotAsync(single.Id, CancellationToken.None)).Dialog!.SubmittedInput.ShouldBeNull();
    }

    [Test]
    public async Task AdoptedIdleClearScreenIsTyped()
    {
        await using var adopted = new Fixture(adopted: true);
        var tailer = new Tailer(adopted.Id);
        tailer.EndTurn();
        adopted.Session.BindChildForTest(adopted.Child, tailer, adopted.Generation);
        (await adopted.SendAsync("adopted-idle-notice")).Outcome.ShouldBe(ConditionalInputOutcomes.Written);
        adopted.Child.Inputs.ShouldBe(["adopted-idle-notice"]);
    }

    [Test]
    public async Task AdoptedBlockedScreenStaysModalBlocked()
    {
        await using var adopted = new Fixture(adopted: true);
        var tailer = new Tailer(adopted.Id);
        tailer.EndTurn();
        adopted.Session.BindChildForTest(adopted.Child, tailer, adopted.Generation);
        adopted.Session.RenderDialogScreenForTest(Modal);
        (await adopted.SendAsync("into-dialog")).Outcome.ShouldBe(ConditionalInputOutcomes.ModalBlocked);
        adopted.Child.Inputs.ShouldBeEmpty();
    }

    [Test]
    public async Task AdoptedMidTurnIsNotTyped()
    {
        await using var adopted = new Fixture(adopted: true);
        var tailer = new Tailer(adopted.Id);
        tailer.Assistant("still working");
        adopted.Session.BindChildForTest(adopted.Child, tailer, adopted.Generation);
        (await adopted.SendAsync("mid-turn")).Outcome.ShouldBe(ConditionalInputOutcomes.ObservationUnknown);
        adopted.Child.Inputs.ShouldBeEmpty();
    }

    [Test]
    public async Task UnknownScreenWithoutIdleTranscriptIsNotTyped()
    {
        await using var adopted = new Fixture(adopted: true);
        var tailer = new Tailer(adopted.Id);
        tailer.Assistant("partial turn");
        adopted.Session.BindChildForTest(adopted.Child, tailer, adopted.Generation);
        adopted.Session.RenderDialogScreenForTest("partial");
        (await adopted.SendAsync("unreadable")).Outcome.ShouldBe(ConditionalInputOutcomes.ObservationUnknown);
        adopted.Child.Inputs.ShouldBeEmpty();
    }

    [Test]
    public async Task UncertainInputStaysWithheldWhenTheTranscriptIsIdle()
    {
        await using var session = new Fixture();
        session.Child.Fail = true;
        await Should.ThrowAsync<IOException>(() => session.Runtime.SendInputAsync(session.Id, "failed", CancellationToken.None));
        session.Child.Fail = false;
        var tailer = new Tailer(session.Id);
        tailer.EndTurn();
        session.Session.BindChildForTest(session.Child, tailer, session.Generation);
        session.Session.RenderDialogScreenForTest(Clear);
        (await session.SendAsync("after-uncertain")).Outcome.ShouldBe(ConditionalInputOutcomes.ObservationUnknown);
        session.Child.Inputs.ShouldBeEmpty();
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "c680-" + Guid.NewGuid().ToString("N"));
        public Guid Id { get; } = Guid.NewGuid();
        public DateTime Generation { get; } = SessionGeneration.Normalize(DateTime.UtcNow);
        public SessionRunnerRuntime Runtime { get; }
        public SessionRunnerRuntime.RunnerSession Session { get; }
        public Child Child { get; } = new();
        public SessionRunnerSettings Settings { get; }
        public Clock Clock { get; } = new();
        public Fixture(bool adopted = false)
        {
            var settings = new SessionRunnerSettings { SessionLogPath = _root, CpuWatchdogEnabled = false };
            Settings = settings;
            Runtime = new(Options.Create(settings), NullLogger<SessionRunnerRuntime>.Instance, timeProvider: Clock);
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

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class Tailer(Guid id) : ITranscriptTailer
    {
        public Action? BeforeObservation { get; init; }
        public Task<TerminalTranscriptObservation> ObserveTerminalSeatAsync(CancellationToken ct)
        {
            BeforeObservation?.Invoke();
            return Task.FromResult(new TerminalTranscriptObservation(TerminalTranscriptReadStatus.Unbound, TerminalTranscriptVerdict.Unknown));
        }
        private RunnerTranscriptDto _snapshot = new(id, [], 0);
        public string? BoundTranscriptPath => "/owned/conversation.jsonl";
        public string? NativeBindingIdentity => "native-file-conversation";
        public string? BindHow => "exact";
        public string? UnboundReason => null;
        public void Prompt(string text) => _snapshot = new(id,
            [new(id, 1, TranscriptKinds.UserPrompt, "native-prompt", null, DateTimeOffset.UtcNow,
                "user", text, null, null, null, null, null)], 1);
        public void EndTurn() => _snapshot = new(id,
            [new(id, 1, TranscriptKinds.TurnEnd, null, null, DateTimeOffset.UtcNow,
                null, null, null, null, null, null, TranscriptKinds.StopReasons.EndTurn)], 1);
        public void Assistant(string text) => _snapshot = new(id,
            [new(id, 1, TranscriptKinds.AssistantText, null, null, DateTimeOffset.UtcNow,
                "assistant", text, null, null, null, null, null)], 1);
        public RunnerTranscriptDto Snapshot() => _snapshot;
        public void Start() { }
        public void NotifyChildExited() { }
        public void NotifyClaimRevoked(string path, Guid newOwner) { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
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
