using System.IO.Abstractions.TestingHelpers;
using System.Text.RegularExpressions;
using Antiphon.Nightly;
using Antiphon.Tests.Application;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// The watch-qual command (WatchQual.Run), in-process. It replaced scripts/orchestration/watch-qual.ps1 (plan
/// 2026-11-22-remaining-ps1-to-csharp.md, unit 6). These cases replaced the CARD-1842 S5 driver
/// scripts/test-watch-qual-eta.ps1 (the ETA line) and the watch-qual rows of the C487 smoke
/// scripts/test-orchestration-watchers.ps1 (failures, partial TRX, re-arm, completion, heartbeat, the sleep clamp); its other
/// rows (trx-failures, watch-tasks, watch-master) moved to OrchestrationWatcherTests in unit 25, and the driver is deleted. The log directory, state file, heartbeat and timing root are in a MockFileSystem;
/// the clock is a FakeTimeProvider and the host's Sleep advances it, so no case waits.
/// Not ported: the status object's property and parameter lists and the rc-qualification-status.ps1 wrapper's AST
/// (PowerShell mechanics; the status is C# since unit 7, and StatusReadersDoNotReadEta keeps the ETA out of it), and the
/// helpers' 5.1 parse and $PSScriptRoot-in-param-defaults checks for watch-qual (PowerShell mechanics; the ASCII check
/// stays).
/// </summary>
[Category("Unit")]
public sealed class WatchQualTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);

    private static readonly string Synthetic = """
        <?xml version="1.0" encoding="utf-8"?>
        <TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
          <Results>
            <UnitTestResult testId="synthetic-failed" testName="Synthetic.WindowsInputFails" duration="00:00:01.2500000" outcome="OUTCOME" />
            <UnitTestResult testId="synthetic-passed" testName="Synthetic.Passes" duration="00:00:00.1000000" outcome="Passed" />
          </Results>
          <TestDefinitions>
            <UnitTest id="synthetic-failed"><TestMethod className="Synthetic.WindowsInputTests" name="WindowsInputFails" /></UnitTest>
            <UnitTest id="synthetic-passed"><TestMethod className="Synthetic.WindowsInputTests" name="Passes" /></UnitTest>
          </TestDefinitions>
          <ResultSummary outcome="Failed"><Counters total="2" passed="1" failed="1" /></ResultSummary>
        </TestRun>
        """;

    // The synthetic TRX of the C487 smoke (orchestration-synthetic.trx) with this outcome on its failing result.
    private static string Trx(string outcome = "Failed") => Synthetic.Replace("OUTCOME", outcome, StringComparison.Ordinal);

    /// <summary>One watcher's world: the files, the clock, the writers and the sleeps it was asked for.</summary>
    private sealed class World
    {
        public World()
        {
            var temp = Files.Path.GetTempPath();
            Logs = Files.Path.Combine(temp, "logs");
            Files.Directory.CreateDirectory(Logs);
            var scratch = Files.Path.Combine(temp, "scratch");
            Files.Directory.CreateDirectory(scratch);
            State = Files.Path.Combine(scratch, "qual.seen.txt");
            Heartbeat = Files.Path.Combine(scratch, "heartbeat.json");
        }

        public MockFileSystem Files { get; } = new();

        public FakeTimeProvider Clock { get; } = new(Start);

        public StringWriter Out { get; private set; } = new();

        public StringWriter Error { get; private set; } = new();

        public List<TimeSpan> Sleeps { get; } = [];

        /// <summary>Called with the sleep's number (from 1) before the clock moves on by the sleep.</summary>
        public Action<int>? OnSleep { get; set; }

        public string Logs { get; }

        public string State { get; }

        public string Heartbeat { get; }

        public DateTimeOffset Now => Clock.GetUtcNow();

        public void WriteTrx(string name, string text, int ageSeconds = 60)
        {
            var path = Files.Path.Combine(Logs, name);
            Files.File.WriteAllText(path, text);
            Files.File.SetLastWriteTimeUtc(path, Now.UtcDateTime.AddSeconds(-ageSeconds));
        }

        /// <summary>A heartbeat whose utc is this many minutes old.</summary>
        public void Beat(int minutesOld, bool hang, string extra = "") =>
            Files.File.WriteAllText(Heartbeat, "{\"utc\":\"" + Now.AddMinutes(-minutesOld).ToString("o") + "\",\"hangSuspected\":" + (hang ? "true" : "false") + extra + "}");

        public WatchQualOptions Options(bool once = true, string timingRoot = "", int maxMinutes = 28, int pollSeconds = 60, int minFileAgeSeconds = 0,
            string? logDir = null) => new()
        {
            LogDir = logDir ?? Logs,
            StateFile = State,
            Heartbeat = Heartbeat,
            Once = once,
            TimingRoot = timingRoot,
            MaxMinutes = maxMinutes,
            PollSeconds = pollSeconds,
            MinFileAgeSeconds = minFileAgeSeconds,
        };

        /// <summary>One watch (a re-arm when called again): its exit code and stdout lines. Error keeps its stderr.</summary>
        public (int Exit, string[] Lines) Run(WatchQualOptions options)
        {
            Out = new StringWriter();
            Error = new StringWriter();
            var host = QualificationEtaTests.Host(Out, Error, Clock, span =>
            {
                Sleeps.Add(span);
                OnSleep?.Invoke(Sleeps.Count);
                Clock.Advance(span);
            });
            var exit = WatchQual.Run(options, host, Files);
            return (exit, Lines(Out));
        }

        public string[] StateLines() =>
            Files.File.Exists(State) ? Files.File.ReadAllLines(State).Where(line => line.Length > 0).ToArray() : [];
    }

    private static string[] Lines(StringWriter writer) =>
        writer.ToString().Split('\n').Select(line => line.TrimEnd('\r')).Where(line => line.Length > 0).ToArray();

    // Two outstanding chunks and one settled chunk: outstanding=2 completed=1 pointSeconds=300 (one wall, ratio 1).
    private static string NewTimingRoot(MockFileSystem files)
    {
        var root = QualificationEtaTests.NewRoot(files);
        QualificationEtaTests.WritePlan(files, root, "antiphon", """{"chunk":"a","plannedSeconds":100},{"chunk":"b","plannedSeconds":200},{"chunk":"c","plannedSeconds":50}""");
        QualificationEtaTests.WriteTiming(files, root, "antiphon", "c", "complete", "2026-11-10T11:00:00.000Z", "2026-11-10T11:01:00.000Z", "50");
        return root;
    }

    private static string Repo(params string[] parts) => Path.Combine(new[] { DelegateScriptRunner.RepoRoot }.Concat(parts).ToArray());

    [Test]
    public void OncePrintsOneEtaLine()
    {
        var world = new World();
        var root = NewTimingRoot(world.Files);
        world.Beat(0, hang: false);
        var before = QualificationEtaTests.TreeStamp(world.Files, root);
        var (exit, lines) = world.Run(world.Options(timingRoot: root));
        exit.ShouldBe(0);
        var eta = lines.Where(line => line.StartsWith("ETA", StringComparison.Ordinal)).ToArray();
        eta.Length.ShouldBe(1, string.Join(" | ", lines));
        var parsed = QualificationEtaTests.Parse(eta[0]);
        (parsed.Outstanding + "/" + parsed.Running + "/" + parsed.Completed + "/" + parsed.Unpriced + "/" + parsed.PointSeconds + "/" + parsed.P95)
            .ShouldBe("2/0/1/0/300/unavailable");
        // The watcher's line is the read-only ETA's line for the same root at the same instant.
        eta[0].ShouldBe(QualificationEta.Read(world.Files, root, Start.UtcDateTime, "").Line);
        QualificationEtaTests.TreeStamp(world.Files, root).ShouldBe(before, "timing root changed");
        lines.ShouldBe(new[] { eta[0], "watch-qual window ended; re-arm" }, "an empty log directory and a fresh heartbeat print only the ETA and the window line");
    }

    [Test]
    public void OmittedTimingRootStaysSilent()
    {
        var world = new World();
        var root = NewTimingRoot(world.Files);
        world.Beat(0, hang: false);
        // Control: the same watcher prints the line when the root is passed, so silence below is not a missing feature.
        world.Run(world.Options(timingRoot: root)).Lines.Count(line => line.StartsWith("ETA", StringComparison.Ordinal)).ShouldBe(1, "control eta lines");
        var (_, lines) = world.Run(world.Options());
        lines.Where(line => line.Contains("ETA", StringComparison.Ordinal)).ShouldBeEmpty("eta without a root");
        lines.Count(line => line.StartsWith("watch-qual window ended", StringComparison.Ordinal)).ShouldBe(1, "window line");
    }

    [Test]
    public void EtaErrorKeepsWatching()
    {
        var world = new World();
        world.Beat(0, hang: false);
        var (exit, lines) = world.Run(world.Options(timingRoot: world.Files.Path.Combine(world.Logs, "no-timing-root")));
        exit.ShouldBe(0);
        lines.ShouldBe(new[] { "ETA-ERROR timing-root-missing " + world.Files.Path.Combine(world.Logs, "no-timing-root"), "watch-qual window ended; re-arm" });
    }

    // StatusObjectHasNoEta: the status readers (RcQualificationStatus and its record reader, unit 7; they replaced
    // rc-qualification-status.ps1) and the heartbeat neither name the ETA command nor carry an eta field.
    [Test]
    public void StatusReadersDoNotReadEta()
    {
        foreach (var path in new[] { "scripts/lib/Antiphon.Nightly/RcQualificationStatus.cs", "scripts/lib/Antiphon.Nightly/RcQualificationRecords.cs",
                     "scripts/lib/Antiphon.Scripts/RcQualificationStatusCommand.cs", "scripts/lib/Antiphon.Nightly/QualificationHeartbeat.cs" })
        {
            var text = File.ReadAllText(Repo(path.Split('/')));
            text.Contains("qualification-eta", StringComparison.Ordinal).ShouldBeFalse(path + " reads the ETA");
            text.Contains("QualificationEta", StringComparison.Ordinal).ShouldBeFalse(path + " reads the ETA");
        }
        Regex.IsMatch(File.ReadAllText(Repo("scripts", "lib", "Antiphon.Nightly", "RcQualificationStatus.cs")), @"(?i)\[""eta""\]").ShouldBeFalse("the status object has an eta field");
        using var fixture = Nightly.NightlyFixture.Create("eta-status");
        var status = RcQualificationStatus.Observe(new RcQualificationStatusOptions
        {
            CandidateId = "rc-eta", RunId = "run-eta", StateRoot = fixture.PathOf("state"), CoordinationRoot = fixture.PathOf("coord"),
        }, fixture.Host(), new System.IO.Abstractions.FileSystem());
        status.ContainsKey("eta").ShouldBeFalse("the status object has an eta field");
        status["state"]!.AsObject().ContainsKey("eta").ShouldBeFalse("the status state has an eta field");
    }

    [Test]
    public void ReleaseGatesKeepsLastActivityRefusal()
    {
        var flat = Regex.Replace(File.ReadAllText(Repo("docs", "release-gates.md")), @"\s+", " ");
        const string refusal = "`lastActivityAt` is a recorded transition, **not a continuous heartbeat**; its age does not prove a hang or supply an ETA.";
        const string sentence = "The batch ETA is the one line from `scripts/antiphon watch-qual` when `--timing-root` is passed (`scripts/antiphon qualification-eta"
            + " --timing-root <dir>` prints it once). This status reader does not walk a timing root, and lastActivityAt does not supply an ETA.";
        flat.ShouldContain(refusal, customMessage: "lastActivityAt refusal is missing");
        flat.ShouldContain(sentence, customMessage: "watch-qual ETA sentence is missing");
        flat.ShouldContain(refusal + " " + sentence, customMessage: "watch-qual ETA sentence does not follow the refusal");
    }

    [Test]
    public void ReportsTimeoutAbortedAndErrorAsFailures()
    {
        foreach (var outcome in new[] { "Timeout", "Aborted", "Error" })
        {
            var world = new World();
            world.Beat(0, hang: false);
            world.WriteTrx(outcome + ".trx", Trx(outcome));
            world.Run(world.Options()).Lines.ShouldContain("FAILURES " + outcome + ".trx: 1 failed :: WindowsInputTests 1",
                "qualification reports " + outcome + " as a failure");
        }
        var passed = new World();
        passed.Beat(0, hang: false);
        passed.WriteTrx("green.trx", Trx("Passed"));
        var (_, lines) = passed.Run(passed.Options());
        lines.Where(line => line.StartsWith("FAILURES", StringComparison.Ordinal)).ShouldBeEmpty("a green TRX has no failure line");
        passed.StateLines().ShouldBe(new[] { passed.Files.Path.Combine(passed.Logs, "green.trx") }, "a green TRX is still recorded");
    }

    [Test]
    public void ReportsFailuresPartialTrxStaleAndHang()
    {
        var world = new World();
        world.WriteTrx("synthetic.trx", Trx(), ageSeconds: 60);
        world.WriteTrx("incomplete.trx", "<TestRun", ageSeconds: 30);
        var stamp = Start.AddMinutes(-10).ToString("o");
        world.Files.File.WriteAllText(world.Heartbeat, "{\"utc\":\"" + stamp + "\",\"runId\":\"synthetic\",\"supervisorPid\":1,\"trx\":1,\"hangSuspected\":true,\"idleMinutes\":10}");
        var (exit, lines) = world.Run(world.Options());
        exit.ShouldBe(0);
        lines.Length.ShouldBe(5, string.Join(" | ", lines));
        lines[0].ShouldBe("FAILURES synthetic.trx: 1 failed :: WindowsInputTests 1", "qualification reports failures");
        lines[1].ShouldStartWith("TRX-PARSE-ERROR incomplete.trx: ", customMessage: "qualification reports partial TRX");
        lines[2].ShouldBe("HANG-SUSPECTED heartbeat " + stamp + " runId=synthetic idleMinutes=10", "qualification reports suspected hang");
        lines[3].ShouldBe("HEARTBEAT-STALE " + stamp + " runId=synthetic supervisorPid=1 trx=1", "qualification reports stale heartbeat");
        lines[4].ShouldBe("watch-qual window ended; re-arm");
        world.StateLines().ShouldBe(new[] { world.Files.Path.Combine(world.Logs, "synthetic.trx") }, "only the complete TRX is recorded");
    }

    [Test]
    public void RearmDeduplicatesCompletedTrxAndRetriesPartial()
    {
        var world = new World();
        world.Beat(0, hang: false);
        world.WriteTrx("synthetic.trx", Trx(), ageSeconds: 60);
        world.WriteTrx("incomplete.trx", "<TestRun", ageSeconds: 30);
        world.Run(world.Options()).Lines.ShouldContain("FAILURES synthetic.trx: 1 failed :: WindowsInputTests 1");
        var (_, rearm) = world.Run(world.Options());
        rearm.Where(line => line.StartsWith("FAILURES synthetic.trx", StringComparison.Ordinal)).ShouldBeEmpty("re-arm reports a completed TRX again");
        rearm.Count(line => line.StartsWith("TRX-PARSE-ERROR incomplete.trx", StringComparison.Ordinal)).ShouldBe(1, "re-arm retries the partial TRX");
        world.WriteTrx("incomplete.trx", Trx(), ageSeconds: 1);
        var (_, repaired) = world.Run(world.Options());
        repaired.ShouldContain("FAILURES incomplete.trx: 1 failed :: WindowsInputTests 1", "reports repaired partial TRX on next poll");
        repaired.Where(line => line.StartsWith("TRX-PARSE-ERROR", StringComparison.Ordinal)).ShouldBeEmpty();
        world.StateLines().ShouldBe(new[] { world.Files.Path.Combine(world.Logs, "synthetic.trx"), world.Files.Path.Combine(world.Logs, "incomplete.trx") });
    }

    [Test]
    public void ObservesCompletionWithoutWritingHeartbeat()
    {
        var world = new World();
        world.Beat(10, hang: true);
        world.WriteTrx("synthetic.trx", Trx());
        world.Files.File.WriteAllText(world.Files.Path.Combine(world.Logs, "summary.json"), "{}");
        string Others() => string.Join("\n", world.Files.AllFiles.Where(path => path != world.State).OrderBy(path => path, StringComparer.Ordinal)
            .Select(path => path + "=" + Convert.ToHexString(world.Files.File.ReadAllBytes(path)) + ":" + world.Files.File.GetLastWriteTimeUtc(path).Ticks));
        var before = Others();
        var (exit, lines) = world.Run(world.Options(once: false, maxMinutes: 5));
        exit.ShouldBe(0);
        lines.ShouldBe(new[] { "FAILURES synthetic.trx: 1 failed :: WindowsInputTests 1", "RUN-ENDED summary.json present in " + world.Logs },
            "completion ends the watch before the heartbeat is read and without the window line");
        world.Sleeps.ShouldBeEmpty("completion does not wait for the next poll");
        Others().ShouldBe(before, "only the state file is written");
    }

    [Test]
    public void AcceptsRepositoryObservedAtUtcHeartbeat()
    {
        var world = new World();
        world.Files.File.WriteAllText(world.Heartbeat, "{\"observedAtUtc\":\"" + Start.ToString("o") + "\",\"hangSuspected\":false}");
        var (_, lines) = world.Run(world.Options());
        lines.ShouldBe(new[] { "watch-qual window ended; re-arm" }, "qualification accepts repository observedAtUtc heartbeat");
    }

    [Test]
    public void ClampsSleepToRemainingWindow()
    {
        var world = new World();
        world.Beat(0, hang: false);
        var (_, lines) = world.Run(world.Options(once: false, maxMinutes: 1, pollSeconds: 300));
        world.Sleeps.ToArray().ShouldBe(new[] { TimeSpan.FromMinutes(1) }, "watch-qual clamps sleep to remaining window");
        lines.Last().ShouldBe("watch-qual window ended; re-arm");

        var polls = new World();
        polls.Beat(0, hang: false);
        polls.Run(polls.Options(once: false, maxMinutes: 1, pollSeconds: 25));
        polls.Sleeps.ToArray().ShouldBe(new[] { TimeSpan.FromSeconds(25), TimeSpan.FromSeconds(25), TimeSpan.FromSeconds(10) }, "polls every PollSeconds, the last sleep clamped");
    }

    [Test]
    public void HeartbeatAlertsReemitAfterRecovery()
    {
        var world = new World();
        world.Beat(10, hang: true);
        world.OnSleep = number =>
        {
            if (number == 2) world.Beat(0, hang: false);
            else if (number == 3) world.Beat(10, hang: true);
        };
        var (_, lines) = world.Run(world.Options(once: false, maxMinutes: 5));
        world.Sleeps.Count.ShouldBe(5, "five polls in a five-minute window");
        lines.Count(line => line.StartsWith("HEARTBEAT-STALE ", StringComparison.Ordinal)).ShouldBe(2, string.Join(" | ", lines));
        lines.Count(line => line.StartsWith("HANG-SUSPECTED ", StringComparison.Ordinal)).ShouldBe(2, string.Join(" | ", lines));
    }

    [Test]
    public void HeartbeatIssuesPrintOncePerChange()
    {
        var world = new World();
        var missingLogs = world.Files.Path.Combine(world.Logs, "not-yet");
        world.OnSleep = number =>
        {
            if (number == 2) world.Files.File.WriteAllText(world.Heartbeat, "{not json");
            else if (number == 4) world.Beat(0, hang: false);
            else if (number == 5) world.Files.File.Delete(world.Heartbeat);
        };
        var (_, lines) = world.Run(world.Options(once: false, maxMinutes: 6, logDir: missingLogs));
        lines.Count(line => line == "LOG-DIR-MISSING " + missingLogs).ShouldBe(6, "every poll names a missing log directory");
        lines.Count(line => line == "HEARTBEAT-MISSING " + world.Heartbeat).ShouldBe(2, "missing once, and again after a good read");
        lines.Count(line => line.StartsWith("HEARTBEAT-ERROR ", StringComparison.Ordinal)).ShouldBe(1, "the same error once");
    }

    [Test]
    public void YoungTrxWaitsForMinFileAge()
    {
        var world = new World();
        world.Beat(0, hang: false);
        world.WriteTrx("young.trx", Trx(), ageSeconds: 2);
        var firstPoll = "";
        world.OnSleep = number => { if (number == 1) firstPoll = world.Out.ToString(); };
        var (_, lines) = world.Run(world.Options(once: false, maxMinutes: 2, minFileAgeSeconds: 5));
        firstPoll.ShouldNotContain("FAILURES", customMessage: "a TRX younger than the minimum age waits");
        lines.Count(line => line == "FAILURES young.trx: 1 failed :: WindowsInputTests 1").ShouldBe(1, "the next poll reports it once");
    }

    [Test]
    public void OptionsAndUnreadableStateFile()
    {
        var options = WatchQualOptions.Parse(["--log-dir", "logs", "--state-file", "seen.txt", "--heartbeat", "beat.json"]);
        (options.PollSeconds, options.MaxMinutes, options.MinFileAgeSeconds, options.HeartbeatStaleMinutes, options.TimingRoot, options.Once)
            .ShouldBe((60, 28, 5, 5, "", false));
        Should.Throw<NightlyUsageException>(() => WatchQualOptions.Parse(["--log-dir", "logs", "--state-file", "seen.txt"])).Message.ShouldBe("missing --heartbeat");
        foreach (var (flag, value) in new[] { ("--poll-seconds", "0"), ("--poll-seconds", "301"), ("--max-minutes", "30"), ("--min-file-age-seconds", "-1"),
                     ("--min-file-age-seconds", "301"), ("--heartbeat-stale-minutes", "0"), ("--heartbeat-stale-minutes", "61") })
            Should.Throw<NightlyUsageException>(() => WatchQualOptions.Parse(["--log-dir", "l", "--state-file", "s", "--heartbeat", "h", flag, value]))
                .Message.ShouldStartWith(flag + " expects ");

        var world = new World();
        // Another process holds the state file without read sharing.
        world.Files.AddFile(world.State, new MockFileData("seen\n") { AllowedFileShare = FileShare.None });
        var (exit, lines) = world.Run(world.Options());
        exit.ShouldBe(1, "a state file that cannot be read stops the watch");
        lines.ShouldBeEmpty();
        Lines(world.Error).ShouldHaveSingleItem().ShouldStartWith("watch-qual: state-file-unreadable ");
    }

    // The helpers' ASCII rule, for the ported sources; and no PowerShell twin comes back.
    [Test]
    public void PortedSourcesAreAsciiAndHaveNoTwin()
    {
        foreach (var path in new[] { "scripts/lib/Antiphon.Nightly/QualificationEta.cs", "scripts/lib/Antiphon.Nightly/WatchQual.cs",
                     "scripts/lib/Antiphon.Scripts/QualificationEtaCommand.cs", "scripts/lib/Antiphon.Scripts/WatchQualCommand.cs" })
            File.ReadAllBytes(Repo(path.Split('/'))).Count(b => b > 127).ShouldBe(0, path + " is ASCII-only");
        foreach (var path in new[] { "scripts/qualification-eta.ps1", "scripts/orchestration/watch-qual.ps1" })
            File.Exists(Repo(path.Split('/'))).ShouldBeFalse(path + " is ported to C#");
    }
}
