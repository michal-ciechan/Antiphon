using System.Diagnostics;
using System.Text.Json;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.TestHelpers;

[Category("Unit")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class TestTimingRecorderTests
{
    [Test]
    [Timeout(60_000)]
    public async Task C1514T_AsyncOwnership(CancellationToken cancellationToken)
    {
        var root = C1514Support.FreshRoot();
        using var session = C1514Support.Open(root);
        try
        {
            session.BeginCase("owner-a", "TimingProbe", "A", [], 0);
            session.BeginCase("owner-b", "TimingProbe", "B", [], 0);
            var gate = new Barrier(2);
            var left = Task.Run(() =>
            {
                TestTiming.BindTestId("owner-a");
                gate.SignalAndWait(cancellationToken);
                var scope = session.BeginPhase(TestPhaseKind.DbCreate, "clone");
                gate.SignalAndWait(cancellationToken);
                scope.Dispose();
            }, cancellationToken);
            var right = Task.Run(() =>
            {
                TestTiming.BindTestId("owner-b");
                gate.SignalAndWait(cancellationToken);
                gate.SignalAndWait(cancellationToken);
                using (session.BeginPhase(TestPhaseKind.Filesystem, "delete-tree"))
                {
                }
            }, cancellationToken);
            await Task.WhenAll(left, right);
            session.EndCase("owner-a", "Passed", C1514Support.Ms(1));
            session.EndCase("owner-b", "Passed", C1514Support.Ms(1));
        }
        finally
        {
            TestTiming.BindTestId(null);
        }

        var textA = C1514Support.Complete(session.ShardPath!, "owner-a").GetRawText();
        var textB = C1514Support.Complete(session.ShardPath!, "owner-b").GetRawText();
        textA.ShouldContain("db-create", Case.Sensitive, "owner-is-current-test");
        textA.ShouldNotContain("filesystem", Case.Sensitive, "owner-is-current-test");
        textB.ShouldContain("filesystem", Case.Sensitive, "owner-is-current-test");
        textB.ShouldNotContain("db-create", Case.Sensitive, "owner-is-current-test");
    }

    [Test]
    public void C1514T_OwnerFileNamesTheChunk()
    {
        TestTimingSession.GroupToken("g-own").ShouldBe("672d6f776e");
        var root = C1514Support.FreshRoot();
        using var session = TestTimingSession.TryOpen(new TestTimingConfig
        {
            Root = root,
            RunId = "run-own",
            GroupId = "g-own",
            Attempt = "1",
            Suite = "antiphon",
            ManualClock = true,
            Pid = 42,
            Nonce = "own1",
            AssemblyId = "asm1",
        });
        session.Enabled.ShouldBeTrue("owner-file-session-opens");
        var owner = Path.Combine(root, "own-672d6f776e-42-own1.txt");
        File.Exists(owner).ShouldBeTrue("owner-file-names-the-chunk");
        File.ReadAllText(owner).ShouldBe("cases-42-own1.jsonl\n");
        File.Exists(Path.Combine(root, "cases-42-own1.jsonl")).ShouldBeTrue("journal-name-stays-pid-nonce");
    }

    [Test]
    public void C1514T_IntervalAlgebra()
    {
        var root = C1514Support.FreshRoot();
        using var session = C1514Support.Open(root);
        session.BeginCase("case-a", "TimingProbe", "Algebra", [], 0);
        var db = session.OpenAt("case-a", TestPhaseKind.DbCreate, "clone", C1514Support.Ms(10), null);
        var nested = session.OpenAt("case-a", TestPhaseKind.DbCreate, "clone", C1514Support.Ms(15), db);
        session.CloseAt(nested, C1514Support.Ms(25));
        session.CloseAt(db, C1514Support.Ms(40));
        var process = session.OpenAt("case-a", TestPhaseKind.ProcessSpawn, "spawn", C1514Support.Ms(30), null);
        session.CloseAt(process, C1514Support.Ms(60));
        var fs = session.OpenAt("case-a", TestPhaseKind.Filesystem, "delete-tree", C1514Support.Ms(70), null);
        session.CloseAt(fs, C1514Support.Ms(80));
        var zero = session.OpenAt("case-a", TestPhaseKind.NetworkLoopback, "span", C1514Support.Ms(50), null);
        session.CloseAt(zero, C1514Support.Ms(50));
        var ours = session.OpenAt("case-a", TestPhaseKind.OurCode, "child", C1514Support.Ms(85), null);
        session.CloseAt(ours, C1514Support.Ms(95));
        session.EndCase("case-a", "Passed", C1514Support.Ms(100));

        var row = C1514Support.Complete(session.ShardPath!, "case-a");
        var exclusive = row.GetProperty("exclusive");
        exclusive.GetProperty("db").GetInt64().ShouldBe(20);
        exclusive.GetProperty("process").GetInt64().ShouldBe(20);
        exclusive.GetProperty("fs").GetInt64().ShouldBe(10);
        exclusive.GetProperty("network").GetInt64().ShouldBe(0);
        row.GetProperty("overlapMs").GetInt64().ShouldBe(10);
        row.GetProperty("remainderMs").GetInt64().ShouldBe(40);
        row.GetProperty("childUnionMs").GetInt64().ShouldBe(60);
        C1514Support.Envelope(row).ShouldBe(100, "exclusive-total-equals-envelope");
        row.GetRawText().ShouldContain("our-code");
    }

    [Test]
    [Arguments("unset")]
    [Arguments("blank")]
    [Arguments("invalid-metadata")]
    [Arguments("unwritable")]
    [Arguments("write-fault")]
    [Arguments("throwing-body")]
    [Arguments("cancelled-body")]
    public void C1514T_AbsentAndFailure(string mode)
    {
        switch (mode)
        {
            case "unset":
                Absent(null, null, "disabled-writes-nothing");
                using (TestTiming.BeginPhase(TestPhaseKind.DbCreate, "clone"))
                {
                }
                break;
            case "blank":
                Absent("   ", null, "disabled-writes-nothing");
                break;
            case "invalid-metadata":
                Absent(Path.Combine(Path.GetTempPath(), "c1514-meta-" + Guid.NewGuid().ToString("N")), "phase-metadata-missing", "phase-metadata-missing");
                break;
            case "unwritable":
                var file = Path.Combine(Path.GetTempPath(), "c1514-file-" + Guid.NewGuid().ToString("N"));
                File.WriteAllText(file, "x");
                var blocked = TestTimingSession.TryOpen(Config(file));
                blocked.Enabled.ShouldBeFalse("phase-sink-failed");
                blocked.FailureCode.ShouldBe("phase-sink-failed");
                blocked.ShardPath.ShouldBeNull();
                break;
            case "write-fault":
                WriteFault();
                break;
            case "throwing-body":
                BodyEscapes(new InvalidOperationException("kept"));
                break;
            case "cancelled-body":
                BodyEscapes(new OperationCanceledException("kept"));
                break;
            default:
                throw new InvalidOperationException("unknown mode " + mode);
        }
    }

    [Test]
    [Arguments("never-closed")]
    [Arguments("nested-overlap")]
    [Arguments("parallel-threads")]
    [Arguments("two-writers")]
    [Timeout(180_000)]
    public async Task C1514T_ClosureAndWriters(string mode, CancellationToken cancellationToken)
    {
        if (mode == "two-writers")
        {
            await TwoWriters(cancellationToken);
            return;
        }

        var root = C1514Support.FreshRoot();
        using var session = C1514Support.Open(root);
        if (mode == "never-closed")
        {
            session.BeginCase("case-a", "TimingProbe", "Open", [], 0);
            _ = session.OpenAt("case-a", TestPhaseKind.DbCreate, "clone", C1514Support.Ms(1), null);
            session.EndCase("case-a", "Passed", C1514Support.Ms(5));
            var row = C1514Support.Complete(session.ShardPath!, "case-a");
            row.GetProperty("incomplete").GetBoolean().ShouldBeTrue();
            row.GetProperty("phases").GetArrayLength().ShouldBe(1);
            return;
        }

        if (mode == "nested-overlap")
        {
            session.BeginCase("case-a", "TimingProbe", "Nest", [], 0);
            var outer = session.OpenAt("case-a", TestPhaseKind.DbCreate, "clone", 0, null);
            var inner = session.OpenAt("case-a", TestPhaseKind.DbCleanup, "drop", C1514Support.Ms(1), outer);
            session.CloseAt(inner, C1514Support.Ms(2));
            session.EndCase("case-a", "Passed", C1514Support.Ms(4));
            var phases = C1514Support.Complete(session.ShardPath!, "case-a").GetProperty("phases");
            phases.GetArrayLength().ShouldBe(2);
            phases[0].GetProperty("incomplete").GetBoolean().ShouldBeFalse();
            phases[1].GetProperty("incomplete").GetBoolean().ShouldBeTrue();
            return;
        }

        session.BeginCase("owner-a", "TimingProbe", "A", [], 0);
        session.BeginCase("owner-b", "TimingProbe", "B", [], 0);
        var gate = new Barrier(2);
        var left = Task.Run(() =>
        {
            TestTiming.BindTestId("owner-a");
            gate.SignalAndWait();
            using (session.BeginPhase(TestPhaseKind.DbCreate, "clone")) { }
        }, cancellationToken);
        var right = Task.Run(() =>
        {
            TestTiming.BindTestId("owner-b");
            gate.SignalAndWait();
            using (session.BeginPhase(TestPhaseKind.Filesystem, "delete-tree")) { }
        }, cancellationToken);
        try
        {
            await Task.WhenAll(left, right);
        }
        finally
        {
            TestTiming.BindTestId(null);
        }

        session.EndCase("owner-a", "Passed", C1514Support.Ms(1));
        session.EndCase("owner-b", "Passed", C1514Support.Ms(1));
        var textA = C1514Support.Complete(session.ShardPath!, "owner-a").GetRawText();
        var textB = C1514Support.Complete(session.ShardPath!, "owner-b").GetRawText();
        textA.ShouldContain("db-create");
        textA.ShouldNotContain("filesystem");
        textB.ShouldContain("filesystem");
        textB.ShouldNotContain("db-create");
    }

    [Test]
    public void C1514T_SecretsAndBounds()
    {
        var root = C1514Support.FreshRoot();
        using var session = C1514Support.Open(root);
        var bomb = new ThrowingToString();
        var described = TestTiming.DescribeArgument(bomb);
        described.ShouldBe(nameof(ThrowingToString));
        var uri = TestTiming.DescribeArgument(new Uri("https://user:Password=secret@example.invalid/sk-probe-should-not-leak"));
        uri.ShouldBe("Uri");
        TestTiming.NormalizeFreeText("sk-probe-should-not-leak").ShouldBe("omitted");
        TestTiming.NormalizeFreeText("Password=secret").ShouldBe("omitted");
        session.BeginCase("case-a", "TimingProbe", "Secrets", [described, uri], 0);
        for (var i = 0; i < TestTiming.SpanCap; i++)
        {
            var id = session.OpenAt("case-a", TestPhaseKind.DbCreate, "span", C1514Support.Ms(i), null);
            session.CloseAt(id, C1514Support.Ms(i));
        }

        var dropped = session.OpenAt("case-a", TestPhaseKind.DbCreate, "Password=secret", C1514Support.Ms(1), null);
        dropped.ShouldBe(0, "span-budget-recorded");
        session.EndCase("case-a", "Passed", C1514Support.Ms(TestTiming.SpanCap + 1));
        var row = C1514Support.Complete(session.ShardPath!, "case-a");
        row.GetProperty("droppedPhaseCount").GetInt32().ShouldBe(1, "span-budget-recorded");
        var bytes = C1514Support.ReadLiveText(session.ShardPath!);
        bytes.ShouldNotContain("sk-probe-should-not-leak", Case.Sensitive, "secret-canaries-absent");
        bytes.ShouldNotContain("Password=", Case.Sensitive, "secret-canaries-absent");
        bytes.ShouldNotContain("CANARY-tostring", Case.Sensitive, "secret-canaries-absent");
        bytes.ShouldContain(nameof(ThrowingToString), Case.Sensitive, "secret-canaries-absent");
    }

    private static void Absent(string? root, string? code, string message)
    {
        var sentinel = Path.Combine(Path.GetTempPath(), "c1514-absent-" + Guid.NewGuid().ToString("N"));
        var session = TestTimingSession.TryOpen(new TestTimingConfig
        {
            Root = root,
            RunId = code is null ? "run-a" : "",
            GroupId = "group-a",
            Attempt = "1",
            Suite = "suite-a",
        });
        session.Enabled.ShouldBeFalse(message);
        session.FailureCode.ShouldBe(code, message);
        session.ShardPath.ShouldBeNull(message);
        Directory.Exists(sentinel).ShouldBeFalse(message);
        if (root is not null && code == "phase-metadata-missing")
            Directory.Exists(root).ShouldBeFalse(message);
    }

    private static void WriteFault()
    {
        var root = C1514Support.FreshRoot();
        using var session = C1514Support.Open(root);
        session.FaultNextWrite();
        session.BeginCase("case-a", "TimingProbe", "Fault", [], 0);
        session.Enabled.ShouldBeFalse("sink-preserves-outcome");
        session.FailureCode.ShouldBe("phase-sink-failed", "sink-preserves-outcome");
        var kept = false;
        try
        {
            throw new InvalidOperationException("kept");
        }
        catch (InvalidOperationException ex)
        {
            kept = ex.Message == "kept";
        }

        kept.ShouldBeTrue("sink-preserves-outcome");
    }

    private static void BodyEscapes(Exception expected)
    {
        var root = C1514Support.FreshRoot();
        using var session = C1514Support.Open(root);
        session.BeginCase("case-a", "TimingProbe", "Body", [], 0);
        Exception? seen = null;
        try
        {
            using (session.BeginPhase(TestPhaseKind.ProcessWait, "wait"))
                throw expected;
        }
        catch (Exception ex) when (ex.GetType() == expected.GetType())
        {
            seen = ex;
        }

        seen.ShouldNotBeNull();
        seen!.Message.ShouldBe("kept", "sink-preserves-outcome");
        session.EndCase("case-a", "Failed", C1514Support.Ms(1));
        var row = C1514Support.Complete(session.ShardPath!, "case-a");
        row.GetProperty("outcome").GetString().ShouldBe("Failed");
    }

    private static async Task TwoWriters(CancellationToken cancellationToken)
    {
        var staged = C1514Support.StagedProbe();
        File.Exists(staged).ShouldBeTrue("timing probe must be staged by the parent build");
        var root = C1514Support.FreshRoot();
        var first = RunWriter(staged, root, "writera", cancellationToken);
        var second = RunWriter(staged, root, "writerb", cancellationToken);
        await Task.WhenAll(first, second);
        var left = Path.Combine(root, "cases-77-writera.jsonl");
        var right = Path.Combine(root, "cases-77-writerb.jsonl");
        File.Exists(left).ShouldBeTrue("writers-have-distinct-shards");
        File.Exists(right).ShouldBeTrue("writers-have-distinct-shards");
        var leftText = C1514Support.ReadLiveText(left);
        var rightText = C1514Support.ReadLiveText(right);
        leftText.ShouldContain("writera", Case.Sensitive, "writers-have-distinct-shards");
        leftText.ShouldNotContain("writerb", Case.Sensitive, "writers-have-distinct-shards");
        rightText.ShouldContain("writerb", Case.Sensitive, "writers-have-distinct-shards");
        rightText.ShouldNotContain("writera", Case.Sensitive, "writers-have-distinct-shards");
        leftText.ShouldContain("writer-case");
        rightText.ShouldContain("writer-case");
    }

    private static async Task RunWriter(string dll, string root, string nonce, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo("dotnet") { WorkingDirectory = root };
        start.ArgumentList.Add(dll);
        start.Environment[TestTiming.RootVariable] = root;
        start.Environment[TestTiming.RunIdVariable] = "run-a";
        start.Environment[TestTiming.GroupIdVariable] = "group-a";
        start.Environment[TestTiming.AttemptVariable] = "1";
        start.Environment[TestTiming.SuiteVariable] = "suite-a";
        start.Environment["C1514_WRITER"] = "1";
        start.Environment["C1514_WRITER_PID"] = "77";
        start.Environment["C1514_WRITER_NONCE"] = nonce;
        var ran = await C1514Support.RunProcess(start, cancellationToken);
        ran.ExitCode.ShouldBe(0, ran.Text);
    }

    private static TestTimingConfig Config(string root) => new()
    {
        Root = root,
        RunId = "run-a",
        GroupId = "group-a",
        Attempt = "1",
        Suite = "suite-a",
        ManualClock = true,
        Pid = 7,
        Nonce = "nonce1",
        AssemblyId = "asm1",
    };

    private sealed class ThrowingToString
    {
        public override string ToString() => throw new InvalidOperationException("CANARY-tostring");
    }
}
