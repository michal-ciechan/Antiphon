using Antiphon.Checkpoints;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Checkpoints;

// CARD-1313 S4. The timing shard is a durable journal, not a test verdict.
[Category("Unit")]
[NotInParallel]
public sealed class TimingJournalTests : CheckpointTestBase
{
    [Test]
    public async Task C1313_RootCustody()
    {
        Should.Throw<TimingRootException>(() => TimingJournal.RequireComponent("..")).Message.ShouldBe("path-component-refused");
        Should.Throw<TimingRootException>(() => TimingJournal.RequireComponent("a/b")).Message.ShouldBe("path-component-refused");
        Should.Throw<TimingRootException>(() => TimingJournal.Resolve(new TimingRootRequest
        {
            Explicit = Fresh("explicit"),
            RunId = "..",
            Attempt = "1",
        })).Message.ShouldBe("path-component-refused");

        var output = Fresh("output");
        Should.Throw<TimingRootException>(() => TimingJournal.Resolve(new TimingRootRequest
        {
            Explicit = Path.Combine(output, "timings"),
            Hosted = true,
            Output = output,
            Checkout = output,
            Cleanup = output,
            RunId = "run",
            Attempt = "1",
        })).Message.ShouldBe("root-outside-custody");

        var outside = Fresh("outside");
        TimingJournal.Resolve(new TimingRootRequest
        {
            Explicit = outside,
            Hosted = true,
            Output = output,
            Checkout = output,
            Cleanup = output,
            RunId = "run",
            Attempt = "1",
        }).ShouldBe(Path.GetFullPath(outside));

        var git = Fresh("git");
        TimingJournal.Resolve(new TimingRootRequest { GitCommonDirectory = git, RunId = "run", Attempt = "1" })
            .ShouldBe(Path.Combine(Path.GetFullPath(git), "antiphon", "test-timings"));
        TimingJournal.Resolve(new TimingRootRequest { Environment = outside, GitCommonDirectory = git, RunId = "run", Attempt = "1" })
            .ShouldBe(Path.GetFullPath(outside));

        var timing = Fresh("history");
        var owned = Fresh("owned-output");
        await TimingJournal.Open(timing).AppendAsync(Event("kept", "run-started", "run"));
        Directory.Delete(owned, recursive: true);
        TimingJournal.Read(timing).Events.Count.ShouldBe(1, "history-survives-output");
        var disposable = TimingJournal.DisposableOutputs(owned, timing);
        disposable.ToArray().ShouldBe(new[] { Path.GetFullPath(owned) });
        disposable.Contains(Path.GetFullPath(timing)).ShouldBeFalse("history-survives-output");
    }

    [Test]
    public async Task C1313_ReplayIdentity()
    {
        var root = Fresh("replay");
        var journal = TimingJournal.Open(root);
        var first = Event("same", "set-completed", "run");
        (await journal.AppendAsync(first)).Sequence.ShouldBe(1);
        var replay = await journal.AppendAsync(Event("same", "set-completed", "run"));
        replay.Idempotent.ShouldBeTrue("replay-once");
        TimingJournal.Read(root).Events.Count.ShouldBe(1, "replay-once");

        var conflict = await Should.ThrowAsync<TimingIntegrityException>(() => journal.AppendAsync(Event("same", "set-completed", "run", outcome: "failed")));
        conflict.Message.ShouldBe("event-conflict");

        var reopened = TimingJournal.Open(root);
        (await reopened.AppendAsync(Event("next", "set-completed", "run"))).Sequence.ShouldBe(2, "sequence-resumed");

        var left = Fresh("shard-a");
        var right = Fresh("shard-b");
        await TimingJournal.Open(left).AppendAsync(Event("left", "set-completed", "left"));
        await TimingJournal.Open(right).AppendAsync(Event("right", "set-completed", "right"));
        TimingJournal.Read(left, right).Events.Count.ShouldBe(2);

        var shared = Fresh("serialized");
        var gate = TimingJournal.Open(shared);
        var firstWrite = gate.AppendAsync(Event("a", "set-completed", "shared"));
        var secondWrite = gate.AppendAsync(Event("b", "set-completed", "shared"));
        await Task.WhenAll(firstWrite, secondWrite);
        var sequences = TimingJournal.Read(shared).Events.Select(item => item.Sequence).OrderBy(item => item).ToArray();
        sequences.ShouldBe([1, 2], "serialized-shard");
    }

    [Test]
    public async Task C1313_RecoveryHandoffs()
    {
        var spoolRoot = Fresh("spool");
        var spool = TimingJournal.Open(spoolRoot);
        spool.Fault = TimingFault.AfterSpoolBeforeAppend;
        var pending = Event("spooled", "set-completed", "run");
        var interrupted = await Should.ThrowAsync<TimingPersistenceException>(() => spool.AppendAsync(pending));
        interrupted.Message.ShouldContain("append-not-started");
        Directory.GetFiles(Path.Combine(spoolRoot, "spool"), "*.json").Length.ShouldBe(1, "recoverable-handoff");
        await spool.ImportSpoolAsync();
        TimingJournal.Read(spoolRoot).Events.Select(item => item.EventId).ToArray().ShouldBe(new[] { "spooled" }, "recoverable-handoff");

        var flushRoot = Fresh("flush");
        var flush = TimingJournal.Open(flushRoot);
        flush.Fault = TimingFault.AfterFlushBeforeAck;
        var acked = await Should.ThrowAsync<TimingPersistenceException>(() => flush.AppendAsync(Event("flushed", "run-started", "run")));
        acked.Message.ShouldContain("ack-not-returned");
        File.ReadAllText(Directory.GetFiles(flushRoot, "events.jsonl", SearchOption.AllDirectories).Single())
            .Contains("\"eventId\":\"flushed\"", StringComparison.Ordinal).ShouldBeTrue("flush-before-ack");

        var unfinished = Fresh("open");
        await TimingJournal.Open(unfinished).AppendAsync(Event("start-only", "run-started", "run"));
        TimingJournal.AttemptStatus(TimingJournal.Read(unfinished).Events).ShouldBe("unfinished", "unfinished-not-success");

        var faulted = Fresh("fault");
        var journal = TimingJournal.Open(faulted);
        journal.Fault = TimingFault.AfterFlushBeforeAck;
        var driver = new FakeDriver();
        driver.When(CheckpointFixtures.IsBuild, (_, _) => Task.FromResult(new DriverResult(0, "", "")));
        driver.When(CheckpointFixtures.IsRun, (request, _) =>
        {
            CheckpointFixtures.WriteResults(CheckpointFixtures.TrxFile(request), ("Antiphon.Tests.Sample.alpha", "Failed"));
            return Task.FromResult(new DriverResult(1, "", ""));
        });
        var working = Fresh("work");
        Directory.CreateDirectory(Path.Combine(working, "tests/Antiphon.Tests"));
        var result = await new RowRunner(driver, new FakePlatform()).RunAsync(new RowRequest
        {
            Name = "CP-fault",
            Project = "tests/Antiphon.Tests",
            OutputPath = "bin-ex/",
            Filter = "/*/*/Sample/*",
            ResultsDirectory = Fresh("results"),
            WorkingDirectory = working,
            MinExecuted = 1,
            Commit = new string('a', 40),
            TimingRoot = faulted,
            TimingJournal = journal,
        }, TextWriter.Null, CancellationToken.None);
        result.ExitCode.ShouldBe(ExitCodes.FailedTests, "persistence-not-test-outcome");
        result.State.ShouldBe("red");
    }

    [Test]
    public void C1313_TornTail()
    {
        var root = Fresh("torn");
        var dir = Path.Combine(root, "runs", "2026-10-16", "run", "1");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "events.jsonl");
        var good = System.Text.Json.JsonSerializer.Serialize(Event("whole", "set-completed", "run"), TimingJournal.Json);
        File.WriteAllText(path, good + "\n{\"eventKind\":");
        var torn = TimingJournal.Read(root);
        torn.Events.Count.ShouldBe(1);
        torn.TornTails.ShouldBe(1);
        torn.Warnings.ShouldContain("torn-tail");
        torn.CorruptLines.ShouldBeEmpty();

        File.WriteAllText(path, good + "\n{not-json}\n");
        var corrupt = TimingJournal.Read(root);
        corrupt.Events.Count.ShouldBe(1);
        corrupt.TornTails.ShouldBe(0);
        corrupt.CorruptLines.Count.ShouldBe(1, "corrupt-not-truncated");
    }

    private string Fresh(string name)
    {
        var path = Path.Combine(TempDir(), name);
        Directory.CreateDirectory(path);
        return path;
    }

    private static TimingEvent Event(string id, string kind, string run, string outcome = "passed") => new()
    {
        EventId = id,
        EventKind = kind,
        RunId = run,
        Attempt = 1,
        SetKind = kind == "run-started" ? "run" : "checkpoint-row",
        SetId = "CP-1",
        Producer = "tool",
        Outcome = outcome,
        StartedAt = new DateTimeOffset(2026, 10, 16, 0, 0, 0, TimeSpan.Zero),
    };
}
