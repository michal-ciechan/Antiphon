using System.Diagnostics;
using System.IO.Abstractions;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Antiphon.Nightly;
using Antiphon.Tests.Application;
using Antiphon.Tests.Checkpoints;
using Antiphon.Tests.Scripts.Nightly;
using Antiphon.Tests.TestHelpers;
using Antiphon.TestSupport;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-0589 V-5, CARD-0800, CARD-0845, CARD-1048, CARD-1627, CARD-1981: the build-slot command (Antiphon.Nightly.BuildSlot)
/// in process, since unit 34 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md moved scripts/build-slot.ps1's
/// body to C# and deleted its driver scripts/test-build-slot.ps1. The rows keep the driver's case names. A scripted broker
/// (the c589 slot shim's answers) and a recording child stand in for the runner's /build-slots and the wrapped command, on
/// one timeline, with a fake clock; nothing reaches a runner. The literal-argv, interruption and Windows PowerShell rows start
/// real children (the script harness host's cli-echo, powershell.exe) through the production BuildSlotProcesses; the
/// broker-contract row runs against the real BuildSlotRoutes in process. The thin launcher's own rows (its raw command line,
/// an in-process PowerShell caller's arrays and location, exit 9) are BuildSlotLauncherTests.
/// Dropped with the driver (PowerShell mechanics of a .ps1 target, not the slot): named-parameter binding and the exit code of
/// throw, binding errors and normal completion inside a pwsh -File child, and the raw pwsh module-path baseline.
/// </summary>
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class BuildSlotScriptTests
{
    private const string Lease = "c5890000-0000-4000-8000-000000000001";

    private static string Harness => Path.Combine(AppContext.BaseDirectory, "script-harness-host", "Antiphon.ScriptHarnessHost.dll");

    /// <summary>One slot's world: the scripted broker, the recording child and the timeline they share.</summary>
    private sealed class SlotWorld : IDisposable
    {
        private readonly object _gate = new();
        private int _posts;

        public SlotWorld(params string[] answers)
        {
            Answers = answers.Length == 0 ? ["granted"] : answers;
            Processes.Start(Holder, HolderStart);
            Processes.Start(4242, HolderStart.AddMinutes(1));
            Directory.CreateDirectory(Bin);
            var executables = OperatingSystem.IsWindows()
                ? new[] { "dotnet.exe", "bash.exe", "pwsh.exe", "powershell.exe", "npm.cmd", "npm.ps1" }
                : new[] { "dotnet", "bash", "pwsh", "sh" };
            foreach (var name in executables) File.WriteAllText(Path.Combine(Bin, name), "");
            Variables["PATH"] = Bin;
            Variables["PATHEXT"] = ".COM;.EXE;.BAT;.CMD";
            // A dead endpoint as well as the scripted broker: nothing here may reach a real runner.
            Variables["ANTIPHON_BUILD_SLOTS_URL"] = "http://127.0.0.1:1/build-slots";
        }

        public const int Holder = 31337;
        public static readonly DateTime HolderStart = new(2026, 11, 24, 9, 0, 0, DateTimeKind.Utc);

        public NightlyFixture Fixture { get; } = NightlyFixture.Create("build-slot");
        public FakeTimeProvider Clock { get; } = new(NightlyFixture.DefaultNow);
        public FakeProcessTable Processes { get; } = new() { CurrentPid = 4242 };
        public Dictionary<string, string?> Variables { get; } = new(StringComparer.Ordinal);
        public string[] Answers { get; set; }
        public List<string> Calls { get; } = [];
        public List<string> Bodies { get; } = [];
        public List<BuildSlotChild> Children { get; } = [];
        public Func<BuildSlotChild, int> Child { get; set; } = _ => 0;
        public IBuildSlotChildren? RealChildren { get; set; }
        public Func<BuildSlotTemp?>? LeaseTemp { get; set; }
        public Func<TimeSpan, CancellationToken, bool>? RenewWait { get; set; }
        public CancellationToken Cancellation { get; set; }
        public string Bin => Fixture.PathOf("bin");

        public void Log(string line)
        {
            lock (_gate) Calls.Add(line);
        }

        public string[] Timeline()
        {
            lock (_gate) return Calls.ToArray();
        }

        public BuildSlotHost Host() => new()
        {
            Out = Fixture.Out,
            Error = Fixture.Error,
            Clock = Clock,
            Sleep = Clock.Advance,
            Http = Send,
            Children = RealChildren ?? new Recording(this),
            Processes = Processes,
            Variables = name => Variables.TryGetValue(name, out var value) ? value : null,
            Files = new FileSystem(),
            Windows = OperatingSystem.IsWindows(),
            LeaseTemp = LeaseTemp,
            RenewWait = RenewWait ?? ((span, token) => !token.WaitHandle.WaitOne(TimeSpan.FromMilliseconds(5))),
            Cancellation = Cancellation,
        };

        /// <summary>build-slot as the launcher calls it, with this test's holder.</summary>
        public int Run(params string[] arguments) =>
            BuildSlot.Run(BuildSlotOptions.Parse(["--holder-pid", Holder.ToString(), .. arguments], Fixture.Root), Host());

        public string Order() => string.Join(",", Timeline().Select(line =>
            line.StartsWith("SLOT POST ", StringComparison.Ordinal) ? "POST"
            : line.StartsWith("SLOT DELETE ", StringComparison.Ordinal) ? "DELETE"
            : line.StartsWith("CMD ", StringComparison.Ordinal) ? "CMD" : null).Where(item => item != null));

        public string Command() => Timeline().Single(line => line.StartsWith("CMD ", StringComparison.Ordinal));

        public string[] Lines => Fixture.Out.ToString().Replace("\r\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries);

        public int LineCount(string pattern) => Lines.Count(line => System.Text.RegularExpressions.Regex.IsMatch(line, pattern));

        // The c589 slot shim's answers, one per POST (the last repeats); what the real routes send (BuildSlotRoutes).
        private NightlyReportHttpResponse Send(string method, string uri, IReadOnlyDictionary<string, string> headers, string body)
        {
            if (method == "DELETE")
            {
                Log("SLOT DELETE " + uri[(uri.LastIndexOf('/') + 1)..]);
                return new(204, "");
            }
            if (method == "POST" && uri.EndsWith("/renew", StringComparison.Ordinal))
            {
                Log("SLOT RENEW " + uri);
                return new(204, "");
            }
            using var request = JsonDocument.Parse(body);
            lock (_gate) Bodies.Add(body);
            var index = Interlocked.Increment(ref _posts) - 1;
            Log("SLOT POST label=" + request.RootElement.GetProperty("label").GetString() + " pid=" + request.RootElement.GetProperty("pid").GetInt32() + " uri=" + uri);
            switch (Answers[Math.Min(index, Answers.Length - 1)])
            {
                case "renew_granted":
                    return new(200, "{\"leaseId\":\"" + Lease + "\",\"maxCpuCount\":3,\"occupied\":1,\"budget\":2,\"expiresAtUtc\":\"2026-09-25T08:00:00Z\",\"unlimited\":false,\"renewEverySeconds\":1}");
                case "granted":
                    return new(200, "{\"leaseId\":\"" + Lease + "\",\"maxCpuCount\":3,\"occupied\":1,\"budget\":2,\"expiresAtUtc\":\"2026-09-25T08:00:00Z\",\"unlimited\":false}");
                case "unlimited":
                    return new(200, "{\"leaseId\":null,\"maxCpuCount\":5,\"occupied\":0,\"budget\":2,\"expiresAtUtc\":null,\"unlimited\":true}");
                case "busy":
                    return new(409, "{\"type\":\"build_slot_busy\",\"title\":\"build_slot_busy\",\"status\":409,\"occupied\":2,\"budget\":2,\"queuePosition\":1,\"retryAfterMs\":10}");
                case "memory_floor":
                    return new(409, "{\"type\":\"build_slot_memory_floor\",\"title\":\"build_slot_memory_floor\",\"status\":409,\"availableMb\":1000,\"floorMb\":6144,\"queuePosition\":1,\"retryAfterMs\":10}");
                case "notfound":
                    return new(404, "");
                case "deadline_unreachable":
                    // The first answer arrives after the wait's deadline (1.1 s of a 1 s wait).
                    Clock.Advance(TimeSpan.FromSeconds(1.1));
                    Log("FIRST_FAILURE_AT " + Clock.GetTimestamp());
                    throw new HttpRequestException("connection refused (scripted)");
                case "deadline_notfound":
                    Clock.Advance(TimeSpan.FromSeconds(1.1));
                    Log("FIRST_FAILURE_AT " + Clock.GetTimestamp());
                    return new(404, "");
                default:
                    throw new HttpRequestException("connection refused (scripted)");
            }
        }

        public void Dispose() => Fixture.Dispose();

        private sealed class Recording(SlotWorld world) : IBuildSlotChildren
        {
            public int Run(BuildSlotChild child)
            {
                lock (world._gate) world.Children.Add(child);
                if (world.Answers.Any(answer => answer.StartsWith("deadline_", StringComparison.Ordinal))) world.Log("CMD_AT " + world.Clock.GetTimestamp());
                world.Log("CMD " + Path.GetFileNameWithoutExtension(child.FileName) + (child.Arguments.Count == 0 ? "" : " " + string.Join(" ", child.Arguments)));
                return world.Child(child);
            }
        }
    }

    private static string Exe(string name) => OperatingSystem.IsWindows() ? name + ".exe" : name;

    [Test]
    public void C589_WrapperRunsUnderLease()
    {
        using var w = new SlotWorld { Child = _ => 7 };
        w.Run("--label", "mutation-shard-1", "--", "dotnet", "build", "tests/X", "--nologo").ShouldBe(7, "C589 WrapperRunsUnderLease propagates the command exit code");
        w.Order().ShouldBe("POST,CMD,DELETE", "C589 WrapperRunsUnderLease runs the command between grant and release");
        w.Timeline().Count(line => line == "SLOT DELETE " + Lease).ShouldBe(1);
        w.Command().ShouldBe("CMD dotnet build tests/X --nologo -maxcpucount:3", "C589 WrapperRunsUnderLease applies the grant -maxcpucount to dotnet build");
        w.Children.Single().FileName.ShouldBe(Path.Combine(w.Bin, Exe("dotnet")));
        w.Children.Single().WorkingDirectory.ShouldBe(w.Fixture.Root);
        w.Children.Single().CompilerGrant.ShouldBe(3);
        (w.LineCount("^BUILD SLOT granted lease=" + Lease + @" waited=\d+s maxcpucount=3$"), w.LineCount("^BUILD SLOT released lease=" + Lease + @" held=\d+s$"))
            .ShouldBe((1, 1), "C589 WrapperRunsUnderLease prints the granted and released lines");
        w.Timeline().Count(line => line.StartsWith("SLOT POST label=mutation-shard-1 pid=" + SlotWorld.Holder + " ", StringComparison.Ordinal))
            .ShouldBe(1, "C589 WrapperRunsUnderLease asks under its -Label for the launcher's process");
        using var body = JsonDocument.Parse(w.Bodies.Single());
        body.RootElement.GetProperty("processStartUtc").GetString().ShouldBe(SlotWorld.HolderStart.ToString("o"), "the holder's own start time");
        body.RootElement.GetProperty("sessionId").ValueKind.ShouldBe(JsonValueKind.Null);

        using var ns = new SlotWorld();
        ns.Run("--label", "operator", "--no-slot", "--", "dotnet", "build", "tests/X").ShouldBe(0);
        (ns.Order(), ns.Command(), ns.LineCount("^BUILD SLOT skipped by -NoSlot$"), ns.Children.Single().CompilerGrant)
            .ShouldBe(("CMD", "CMD dotnet build tests/X", 1, 0), "C589 WrapperRunsUnderLease -NoSlot runs the command unchanged without asking");
    }

    [Test]
    public void C589_WrapperMaxCpuCountRules()
    {
        (string[] In, string Out, string Row)[] cases =
        [
            (["dotnet", "build", "x"], "dotnet build x -maxcpucount:3", "dotnet build gets the count"),
            (["dotnet", "test", "x", "--", "--treenode-filter", "/*/*/A/*"], "dotnet test x -maxcpucount:3 -- --treenode-filter /*/*/A/*", "dotnet test gets it before --"),
            (["/usr/share/dotnet/dotnet", "publish", "x"], "/usr/share/dotnet/dotnet publish x -maxcpucount:3", "a dotnet path gets the count"),
            ([@"C:\Program Files\dotnet\dotnet.exe", "build"], @"C:\Program Files\dotnet\dotnet.exe build -maxcpucount:3", "dotnet.exe gets the count"),
            (["dotnet", "pack"], "dotnet pack -maxcpucount:3", "dotnet pack gets the count"),
            (["dotnet", "msbuild", "x.proj"], "dotnet msbuild x.proj -maxcpucount:3", "dotnet msbuild gets the count"),
            (["dotnet", "run", "--project", "tests/X"], "dotnet run --project tests/X", "dotnet run is left alone because it forwards the switch to the program"),
            (["dotnet", "run", "--project", "tests/X", "--no-build", "--", "--x"], "dotnet run --project tests/X --no-build -- --x", "dotnet run --no-build is left alone"),
            (["dotnet", "build", "-m:2", "x"], "dotnet build -m:2 x", "an explicit -m:2 wins"),
            (["dotnet", "build", "x", "-maxCpuCount"], "dotnet build x -maxCpuCount", "an explicit bare -maxcpucount wins"),
            (["dotnet", "build", "/m", "x"], "dotnet build /m x", "an explicit /m wins"),
            (["dotnet", "build", "--maxcpucount:4", "x"], "dotnet build --maxcpucount:4 x", "an explicit --maxcpucount:4 wins"),
            (["dotnet", "test", "x", "--", "-m:9"], "dotnet test x -maxcpucount:3 -- -m:9", "a -m after -- belongs to the program"),
            (["pwsh", "-File", "x.ps1"], "pwsh -File x.ps1", "a non-dotnet command is left alone"),
            (["dotnet"], "dotnet", "a bare dotnet is left alone"),
        ];
        foreach (var (input, expected, row) in cases)
            string.Join(" ", BuildSlotCommandLine.AddMaxCpuCount(input, 3)).ShouldBe(expected, "C589 WrapperMaxCpuCountRules " + row);
        string.Join(" ", BuildSlotCommandLine.AddMaxCpuCount(["dotnet", "build"], 0)).ShouldBe("dotnet build", "no grant, no switch");
        BuildSlotCommandLine.IsDotnetRunWithBuild(["dotnet", "run", "--", "--no-build"]).ShouldBeTrue("a --no-build after -- belongs to the program");
        BuildSlotCommandLine.IsDotnetRunWithBuild(["dotnet", "run", "--NO-BUILD"]).ShouldBeFalse();

        using var w = new SlotWorld();
        w.Run("--label", "run", "--", "dotnet", "run", "--project", "tests/X", "--", "--treenode-filter", "/*/*/A/*").ShouldBe(0);
        (w.Command(), w.LineCount("^BUILD SLOT note: dotnet run hands -maxcpucount to the program, .* maxcpucount=3$"))
            .ShouldBe(("CMD dotnet run --project tests/X -- --treenode-filter /*/*/A/*", 1), "C589 WrapperMaxCpuCountRules a wrapped dotnet run is leased, unchanged and says why");
    }

    [Test]
    public void C589_WrapperReleasesOnFailure()
    {
        using var w = new SlotWorld { Child = _ => 1 };
        w.Run("--label", "red", "--", "dotnet", "build", "tests/X").ShouldBe(1, "C589 WrapperReleasesOnFailure exit code 1");
        (w.Order(), w.LineCount("^BUILD SLOT released lease=")).ShouldBe(("POST,CMD,DELETE", 1), "C589 WrapperReleasesOnFailure releases the lease after a failing command");

        // A child that cannot start is exit 1 with one line, and the lease is still released.
        using var gone = new SlotWorld { Child = _ => throw new System.ComponentModel.Win32Exception(2, "The system cannot find the file specified") };
        gone.Run("--label", "gone", "--", "dotnet", "build").ShouldBe(1);
        gone.Order().ShouldBe("POST,CMD,DELETE");
        gone.Fixture.Error.ToString().ShouldStartWith("build-slot: cannot start ");
    }

    [Test]
    public void C589_WrapperTimeout()
    {
        using var w = new SlotWorld("busy");
        w.Variables["C589_SLOT_RETRY_MS"] = "50";
        w.Run("--label", "late", "--slot-wait-minutes", "0.0166667", "--", "dotnet", "build", "tests/X").ShouldBe(4, "C589 WrapperTimeout exit code 4");
        (w.Timeline().Count(line => line.StartsWith("CMD ", StringComparison.Ordinal) || line.StartsWith("SLOT DELETE ", StringComparison.Ordinal)),
                w.Timeline().Count(line => line.StartsWith("SLOT POST ", StringComparison.Ordinal)) >= 2)
            .ShouldBe((0, true), "C589 WrapperTimeout runs nothing and releases nothing");
        (w.LineCount("^BUILD SLOT waiting label=late position=1 occupied=2/2 elapsed=0m$") >= 1, w.LineCount("^BUILD SLOT timeout after 1s position=1$"))
            .ShouldBe((true, 1), "C589 WrapperTimeout prints the waiting and timeout lines");
        w.LineCount("^BUILD SLOT waiting ").ShouldBe(1, "the same reason prints once a minute, not on every answer");

        // C589_SLOT_WAIT_SECONDS replaces the minutes (the c590 and c1192 harnesses set it); a memory-floor refusal waits too.
        using var floor = new SlotWorld("memory_floor");
        floor.Variables["C589_SLOT_WAIT_SECONDS"] = "2";
        floor.Run("--label", "floor", "--", "dotnet", "build").ShouldBe(4);
        floor.LineCount("^BUILD SLOT waiting label=floor reason=memory_floor available=1000MB floor=6144MB position=1 elapsed=0m$").ShouldBe(1);
        floor.LineCount("^BUILD SLOT timeout after 2s position=1$").ShouldBe(1);
        floor.Clock.GetUtcNow().ShouldBeGreaterThanOrEqualTo(NightlyFixture.DefaultNow.AddSeconds(2));
        floor.Clock.GetUtcNow().ShouldBeLessThan(NightlyFixture.DefaultNow.AddSeconds(3), "the 250 ms floor of the broker's retry hint");
    }

    [Test]
    public void C589_WrapperUnreachableAtDeadline()
    {
        foreach (var (answer, last) in new[] { ("deadline_unreachable,unreachable", "no answer"), ("deadline_notfound,notfound", "http 404") })
        {
            using var w = new SlotWorld(answer.Split(','));
            w.Variables["C589_SLOT_WAIT_SECONDS"] = "1";
            w.Variables["C589_SLOT_GRACE_SECONDS"] = "0.4";
            w.Variables["C589_SLOT_RETRY_MS"] = "10";
            var code = w.Run("--label", "late-unreachable", "--", "dotnet", "build", "tests/X");
            (code, w.LineCount("^BUILD SLOT unleased reason=runner_unreachable maxcpucount=4 last=" + last + "$"), w.LineCount("^BUILD SLOT timeout "))
                .ShouldBe((0, 1, 0), "C589 WrapperUnreachableAtDeadline " + answer + " fails open after grace");
            var first = long.Parse(w.Timeline().Single(line => line.StartsWith("FIRST_FAILURE_AT ", StringComparison.Ordinal)).Split(' ')[1]);
            var command = long.Parse(w.Timeline().Single(line => line.StartsWith("CMD_AT ", StringComparison.Ordinal)).Split(' ')[1]);
            var grace = (command - first) / (double)w.Clock.TimestampFrequency;
            (grace >= 0.4, System.Text.RegularExpressions.Regex.IsMatch(w.Order(), "^POST,(POST,)+CMD$"), w.Command())
                .ShouldBe((true, true, "CMD dotnet build tests/X -maxcpucount:4"), "C589 WrapperUnreachableAtDeadline " + answer + " never runs unleased early");
        }
    }

    [Test]
    public void C589_WrapperUnreachable()
    {
        using var w = new SlotWorld("unreachable");
        w.Variables["C589_SLOT_GRACE_SECONDS"] = "0";
        w.Run("--label", "offline", "--", "dotnet", "build", "tests/X").ShouldBe(0);
        w.LineCount("^BUILD SLOT unleased reason=runner_unreachable maxcpucount=4 last=no answer$").ShouldBe(1, "C589 WrapperUnreachable prints the unleased line");
        (w.Order(), w.Command()).ShouldBe(("POST,CMD", "CMD dotnet build tests/X -maxcpucount:4"), "C589 WrapperUnreachable runs with the fallback -maxcpucount 4 and releases nothing");

        // A runner that answers busy and then goes away is still unleased after the grace, not a timeout.
        using var gone = new SlotWorld("busy", "notfound");
        gone.Variables["C589_SLOT_GRACE_SECONDS"] = "0";
        gone.Run("--label", "gone", "--", "dotnet", "build", "tests/X").ShouldBe(0);
        (gone.LineCount("^BUILD SLOT unleased reason=runner_unreachable maxcpucount=4 last=http 404$"), gone.Order())
            .ShouldBe((1, "POST,POST,CMD"), "C589 WrapperUnreachable a runner that stops answering mid-wait falls back unleased");

        using var un = new SlotWorld("unlimited");
        un.Run("--label", "disabled", "--", "dotnet", "build", "tests/X").ShouldBe(0);
        (un.LineCount("^BUILD SLOT unlimited maxcpucount=5$"), un.Order(), un.Command())
            .ShouldBe((1, "POST,CMD", "CMD dotnet build tests/X -maxcpucount:5"), "C589 WrapperUnreachable a disabled broker answers unlimited with its cpu count and holds nothing");
    }

    [Test]
    public void C589_WrapperAsciiOnly()
    {
        foreach (var file in new[]
                 {
                     "scripts/build-slot.ps1", "scripts/lib/build-driver-environment.ps1", "scripts/fixtures/c589-slot-shim.ps1",
                     "scripts/lib/Antiphon.Nightly/BuildSlot.cs", "scripts/lib/Antiphon.Nightly/LinuxCompilerMeasure.cs", "scripts/lib/Antiphon.Scripts/BuildSlotCommands.cs",
                 })
            File.ReadAllBytes(Path.Combine(DelegateScriptRunner.RepoRoot, file)).Count(b => b > 127).ShouldBe(0, "C589 WrapperAsciiOnly " + file + " is ASCII-only");
        foreach (var gone in new[] { "scripts/test-build-slot.ps1", "scripts/lib/build-slot.ps1", "scripts/fixtures/c589-command-shim.ps1", "scripts/measure-linux-shared-compiler.ps1", "scripts/lib/linux-compiler-evidence.ps1" })
            File.Exists(Path.Combine(DelegateScriptRunner.RepoRoot, gone)).ShouldBeFalse(gone + " moved to C#");
    }

    [Test]
    public void Wrapper_renews_a_renew_mode_grant_while_the_command_runs()
    {
        using var w = new SlotWorld("renew_granted");
        w.Child = _ =>
        {
            // The command runs until the broker has seen two renewals.
            var until = DateTime.UtcNow.AddSeconds(30);
            while (w.Timeline().Count(line => line.StartsWith("SLOT RENEW ", StringComparison.Ordinal)) < 2 && DateTime.UtcNow < until) Thread.Sleep(5);
            return 0;
        };
        w.Run("--label", "renew", "--", "dotnet", "build", "tests/X").ShouldBe(0);
        var calls = w.Timeline();
        var delete = Array.FindIndex(calls, line => line.StartsWith("SLOT DELETE ", StringComparison.Ordinal));
        (delete > 0, calls.Take(delete).Count(line => line == "SLOT RENEW http://127.0.0.1:1/build-slots/" + Lease + "/renew") >= 2, calls.Skip(delete).Any(line => line.StartsWith("SLOT RENEW", StringComparison.Ordinal)))
            .ShouldBe((true, true, false), "C589 WrapperRenews renews twice before release, and never after it");

        using var pid = new SlotWorld("granted");
        pid.Run("--label", "pid", "--", "dotnet", "build", "tests/X").ShouldBe(0);
        pid.Timeline().Count(line => line.StartsWith("SLOT RENEW ", StringComparison.Ordinal)).ShouldBe(0, "C589 WrapperRenews pid grant has no renewals");
    }

    [Test]
    public void C800_WrapperPassesWildcardArgvLiterally()
    {
        using var w = new SlotWorld { Child = _ => 3 };
        File.WriteAllText(w.Fixture.PathOf("run.trx"), "");
        File.WriteAllText(w.Fixture.PathOf("old.trx"), "");
        string[] argv = ["dotnet", "run", "--project", "tests/X", "--no-build", "--", "--treenode-filter", "*/*/*[Category=Unit]", "--report-trx-filename", "*.trx"];
        w.Run(["--label", "c800-argv", "--", .. argv]).ShouldBe(3, "C800 WrapperPassesWildcardArgvLiterally propagates exit 3");
        w.Order().ShouldBe("POST,CMD,DELETE", "C800 WrapperPassesWildcardArgvLiterally runs the command between grant and release");
        w.Children.Single().Arguments.ShouldBe(argv.Skip(1), "C800 WrapperPassesWildcardArgvLiterally every token arrives literally");
        w.Children.Single().WorkingDirectory.ShouldBe(w.Fixture.Root, "C800 WrapperPassesWildcardArgvLiterally the child runs in the caller's directory");
    }

    [Test]
    public void C800_WrapperStartsUnitFilterWithinDeadline()
    {
        using var w = new SlotWorld();
        const string filter = "/*/*/*/*[Category=Unit]";
        string[] argv = ["dotnet", "run", "--project", "tests/Antiphon.Tests", "--no-build", "--property:OutputPath=bin-c800/", "--", "--treenode-filter", filter,
            "--report-trx", "--report-trx-filename", "run.trx", "--results-directory", w.Fixture.Root];
        w.Run(["--label", "c800-unit", "--", .. argv]).ShouldBe(0, "C800 WrapperStartsUnitFilterWithinDeadline exits");
        w.Children.Single().Arguments.ShouldContain(filter, "C800 WrapperStartsUnitFilterWithinDeadline passes the Unit filter literally");
        w.Order().ShouldBe("POST,CMD,DELETE", "C800 WrapperStartsUnitFilterWithinDeadline releases the lease");
        w.LineCount("^BUILD SLOT note:").ShouldBe(0, "dotnet run --no-build needs no note");
    }

    [Test]
    public void C800_WrapperForwardsScriptTokens()
    {
        using var w = new SlotWorld { Child = _ => 6 };
        var script = w.Fixture.PathOf("shim.ps1");
        File.WriteAllText(script, "");
        w.Run("--no-slot", "--", script, "--treenode-filter", "*.trx", "", "-Switch:$false").ShouldBe(6, "C800 WrapperForwardsScriptTokens propagates exit 6 from a script command");
        var child = w.Children.Single();
        child.FileName.ShouldBe(Path.Combine(w.Bin, Exe("pwsh")), "a .ps1 target runs under pwsh");
        child.Arguments.ShouldBe(["-NoProfile", "-NonInteractive", "-File", script, "--treenode-filter", "*.trx", "", "-Switch:$false"],
            "C800 WrapperForwardsScriptTokens a script command receives its tokens unchanged, an empty one included");
        child.Environment.ContainsKey("PSModulePath").ShouldBeFalse("PowerShell 7 keeps the caller's module path");
    }

    [Test]
    public void C800_WrapperLaunchesNativeExecutableLiterally()
    {
        using var w = new SlotWorld();
        var directory = w.Fixture.PathOf("caller dir");
        Directory.CreateDirectory(directory);
        foreach (var name in new[] { "run.trx", "old.trx", "x" }) File.WriteAllText(Path.Combine(directory, name), "");
        var output = new StringWriter();
        w.RealChildren = new BuildSlotProcesses(null, output, CancellationToken.None);
        w.Variables["PATH"] = Environment.GetEnvironmentVariable("PATH");
        string[] tokens = ["*.trx", "?", "*/*/[Category=Unit]", "", "a b", "single ' and double \" quote", @"folder\", "$([IO.File]::WriteAllText('bad','x'))", "-m:2"];
        var options = new BuildSlotOptions
        {
            Label = "c800-native", Command = ["dotnet", "exec", Harness, "cli-echo", .. tokens], WorkingDirectory = directory,
            Environment = new Dictionary<string, string> { ["CLI_ECHO_EXIT"] = "0" },
        };
        BuildSlot.Run(options, w.Host()).ShouldBe(0, "C800 WrapperLaunchesNativeExecutableLiterally exits zero\n" + output);
        using var echo = JsonDocument.Parse(output.ToString().Split('\n').Single(line => line.TrimStart().StartsWith('{')));
        echo.RootElement.GetProperty("arguments").EnumerateArray().Select(item => item.GetString()).ShouldBe(tokens,
            "C800 WrapperLaunchesNativeExecutableLiterally passes wildcard argv unchanged");
        var observed = Path.GetFullPath(echo.RootElement.GetProperty("workingDirectory").GetString()!).TrimEnd(Path.DirectorySeparatorChar);
        string.Equals(observed, directory, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)
            .ShouldBeTrue("C800 WrapperLaunchesNativeExecutableLiterally keeps the caller directory: " + observed);
        File.Exists(Path.Combine(directory, "bad")).ShouldBeFalse("no text is evaluated");
        w.Order().ShouldBe("POST,DELETE");

        // The child's own exit code is the answer.
        var failing = new BuildSlotOptions { Label = "c800-exit", NoSlot = true, Command = ["dotnet", "exec", Harness, "cli-echo"], WorkingDirectory = directory,
            Environment = new Dictionary<string, string> { ["CLI_ECHO_EXIT"] = "23" } };
        BuildSlot.Run(failing, w.Host()).ShouldBe(23);
    }

    // The cross-platform half of the C800 interrupt row: a cancelled slot kills the running child's tree, answers 130 and still
    // releases the lease, after the command.
    [Test]
    public async Task Interrupt_kills_the_child_tree_and_releases_the_lease()
    {
        using var cancellation = new CancellationTokenSource();
        using var w = new SlotWorld { Cancellation = cancellation.Token };
        var output = new StringWriter();
        var synchronized = TextWriter.Synchronized(output);
        string Text()
        {
            lock (synchronized) return output.ToString();
        }
        w.RealChildren = new BuildSlotProcesses(null, synchronized, cancellation.Token);
        w.Variables["PATH"] = Environment.GetEnvironmentVariable("PATH");
        var never = w.Fixture.PathOf("never-created");
        var options = new BuildSlotOptions
        {
            Label = "interrupt", Command = ["dotnet", "exec", Harness, "cli-echo"], WorkingDirectory = w.Fixture.Root,
            Environment = new Dictionary<string, string> { ["CLI_ECHO_HOLD"] = never },
        };
        var run = Task.Run(() => BuildSlot.Run(options, w.Host()));
        var until = DateTime.UtcNow.AddSeconds(60);
        while (!Text().Contains("\"pid\":", StringComparison.Ordinal) && DateTime.UtcNow < until && !run.IsCompleted) await Task.Delay(50);
        using var echo = JsonDocument.Parse(Text().Split('\n').Single(line => line.TrimStart().StartsWith('{')));
        var child = echo.RootElement.GetProperty("pid").GetInt32();
        var clock = Stopwatch.StartNew();
        await cancellation.CancelAsync();
        (await run.WaitAsync(TimeSpan.FromSeconds(10))).ShouldBe(BuildSlot.Interrupted);
        clock.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(5), "the slot answers within 5 s of the interruption");
        Alive(child).ShouldBeFalse("the child is gone");
        w.Order().ShouldBe("POST,DELETE", "the lease is released after the command");
        w.LineCount("^BUILD SLOT released lease=").ShouldBe(1);
    }

    private static bool Alive(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    [NativeLinuxOnly("Linux SIGINT behavior only")]
    [Test]
    public async Task C800_WrapperInterruptKillsChildAndReleasesLease()
    {
        if (!OperatingSystem.IsLinux()) throw new TUnit.Core.Exceptions.SkipTestException("Linux SIGINT behavior only");
        using var broker = LoopbackBuildSlots.Start();
        using var fixture = NightlyFixture.Create("c800-interrupt");
        var pidFile = fixture.PathOf("child.pid");
        var cli = Path.Combine(AppContext.BaseDirectory, "antiphon-scripts", "Antiphon.Scripts.dll");
        // The TUnit host ignores SIGINT, and its children inherit that disposition. Perl resets it before exec, keeping the
        // CLI's pid and literal argv.
        var start = new ProcessStartInfo("perl") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = fixture.Root };
        foreach (var argument in new[] { "-e", "$SIG{INT} = \"DEFAULT\"; exec @ARGV", "--", "dotnet", "exec", cli, "build-slot", "--label", "c800-interrupt", "--",
                     "sh", "-c", "echo $$ > \"$1\"; exec sleep 20", "sh", pidFile })
            start.ArgumentList.Add(argument);
        start.Environment["ANTIPHON_BUILD_SLOTS_URL"] = broker.Url;
        using var process = Process.Start(start) ?? throw new InvalidOperationException("the CLI did not start");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        try
        {
            var until = DateTime.UtcNow.AddSeconds(30);
            while (!File.Exists(pidFile) && DateTime.UtcNow < until) await Task.Delay(100);
            await Task.Delay(200);
            var child = int.Parse(File.ReadAllText(pidFile).Trim());
            Kill(process.Id, 2).ShouldBe(0);
            var quick = process.WaitForExit(5000);
            var clock = Stopwatch.StartNew();
            while (Directory.Exists("/proc/" + child) && clock.Elapsed < TimeSpan.FromSeconds(5)) await Task.Delay(100);
            quick.ShouldBeTrue("C800 WrapperInterruptKillsChildAndReleasesLease the wrapper exits within 5 s of SIGINT");
            Directory.Exists("/proc/" + child).ShouldBeFalse("C800 WrapperInterruptKillsChildAndReleasesLease the child is gone within 5 s");
            string.Join(",", broker.Calls()).ShouldBe("POST,DELETE", "C800 WrapperInterruptKillsChildAndReleasesLease the lease is released after the command\n"
                + await stdout + await stderr);
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
    }

    [DllImport("libc", EntryPoint = "kill", SetLastError = true)]
    private static extern int Kill(int pid, int signal);

    [Test]
    public void C845_ScriptTargetsRunUnderPwshWithTheirTokensVerbatim()
    {
        // The .ps1 target is a child process: pwsh -NoProfile -NonInteractive -File <script> and the tokens as given, from an
        // absolute path or a path relative to the caller's directory with spaces.
        using var w = new SlotWorld { Child = _ => 9 };
        var driver = w.Fixture.PathOf("pushed folder", "driver folder");
        Directory.CreateDirectory(driver);
        var target = Path.Combine(driver, "target.ps1");
        File.WriteAllText(target, "");
        string[] values = ["-Wildcard", "*/*/[Category=Unit]", "-Empty", "", "-InputPath", "inputs/sentinel.txt", "-Trailing", @"folder\", "-Quotes",
            "single ' and double \" quote", "-Literal", "$([IO.File]::WriteAllText('x','bad'));$literal", "-Spaces", "folder with spaces", "-Switch:$false", "-MinExecuted", "3"];
        var options = BuildSlotOptions.Parse(["--holder-pid", SlotWorld.Holder.ToString(), "--label", "relative", "--", "./driver folder/target.ps1", .. values],
            w.Fixture.PathOf("pushed folder"));
        BuildSlot.Run(options, w.Host()).ShouldBe(9, "C845 WrapperReleasesLeaseAfterScriptFailure exit9 returns the failure exit");
        var child = w.Children.Single();
        child.Arguments.ShouldBe(["-NoProfile", "-NonInteractive", "-File", target, .. values], "C845 WrapperPreservesScriptPathsAndValues preserves literal values");
        child.WorkingDirectory.ShouldBe(w.Fixture.PathOf("pushed folder"), "C845 WrapperPreservesScriptPathsAndValues uses caller location");
        w.Order().ShouldBe("POST,CMD,DELETE", "C845 WrapperReleasesLeaseAfterScriptFailure exit9 releases the granted lease in order");

        using var absolute = new SlotWorld();
        absolute.Run("--label", "absolute", "--", target, "-Name", "CP-1").ShouldBe(0);
        absolute.Children.Single().Arguments.ShouldBe(["-NoProfile", "-NonInteractive", "-File", target, "-Name", "CP-1"]);

        // An unresolved target runs nothing, answers 1 with one line, and still releases the lease in order.
        using var missing = new SlotWorld();
        missing.Run("--label", "unresolved", "--", missing.Fixture.PathOf("missing-unique.ps1"), "-x").ShouldBe(1);
        (missing.Order(), missing.Children.Count).ShouldBe(("POST,DELETE", 0), "C845 WrapperReleasesLeaseAfterScriptFailure unresolved releases the granted lease in order");
        missing.Fixture.Error.ToString().ShouldContain("is not a command, script or executable on PATH");
    }

    [Test]
    public void Commands_resolve_as_get_command_did()
    {
        using var w = new SlotWorld();
        var files = new FileSystem();
        string? Variable(string name) => w.Variables.TryGetValue(name, out var value) ? value : null;
        BuildSlotTarget? Resolve(params string[] command) => BuildSlotTarget.Resolve(command, w.Fixture.Root, Variable, OperatingSystem.IsWindows(), files);

        Resolve("dotnet", "build")!.ShouldSatisfyAllConditions(
            target => target.FileName.ShouldBe(Path.Combine(w.Bin, Exe("dotnet"))),
            target => target.Kind.ShouldBe(BuildSlotTargetKind.Application),
            target => target.Arguments.ShouldBe(["build"]));
        Resolve("no-such-tool").ShouldBeNull();
        Resolve("").ShouldBeNull();
        // An earlier PATH directory wins; in one directory a script wins over an application, as Get-Command ranked them.
        var early = w.Fixture.PathOf("early");
        Directory.CreateDirectory(early);
        File.WriteAllText(Path.Combine(early, "dotnet.ps1"), "");
        w.Variables["PATH"] = early + Path.PathSeparator + w.Bin;
        Resolve("dotnet")!.Kind.ShouldBe(BuildSlotTargetKind.Script);
        w.Variables["PATH"] = w.Bin;
        if (OperatingSystem.IsWindows())
        {
            Resolve("npm", "ci")!.ShouldSatisfyAllConditions(
                target => target.Kind.ShouldBe(BuildSlotTargetKind.Script, "npm.ps1 beside npm.cmd"),
                target => target.Arguments.ShouldBe(["-NoProfile", "-NonInteractive", "-File", Path.Combine(w.Bin, "npm.ps1"), "ci"]));
            Resolve("npm.cmd", "ci")!.ShouldSatisfyAllConditions(
                target => target.Kind.ShouldBe(BuildSlotTargetKind.Batch),
                target => target.FileName.ShouldBe(Path.Combine(w.Bin, "npm.cmd")));
            Resolve(Path.Combine(w.Bin, "dotnet"))!.FileName.ShouldBe(Path.Combine(w.Bin, "dotnet.exe"), "a path without its extension tries PATHEXT");
            Resolve("DOTNET.EXE")!.FileName.ShouldNotBeNull();
        }
        // A path-like token resolves against the caller's directory, not PATH.
        File.WriteAllText(w.Fixture.PathOf("local.ps1"), "");
        Resolve("./local.ps1")!.Resolved.ShouldBe(w.Fixture.PathOf("local.ps1"));
        Resolve("local.ps1").ShouldBeNull("a bare name never runs from the current directory");
    }

    [Test]
    public void C1627_WrapperCapUnenforcedWarns()
    {
        const string warning = "^BUILD SLOT warning: maxcpucount=3 is not applied to bash: a dotnet build inside it runs at the default node count unless it passes -maxcpucount:\\$ANTIPHON_BUILD_SLOT_MAXCPUCOUNT; lease each build separately, never one lease around a loop$";
        using var w = new SlotWorld();
        w.Run("--label", "mutants", "--", "bash", "loop.sh").ShouldBe(0);
        w.LineCount(warning).ShouldBe(1, "C1627 WrapperCapUnenforced a wrapped bash loop prints the BUILD SLOT warning");
        w.Children.Single().Environment["ANTIPHON_BUILD_SLOT_MAXCPUCOUNT"].ShouldBe("3", "C1627 WrapperCapUnenforced a wrapped bash loop sees the grant in ANTIPHON_BUILD_SLOT_MAXCPUCOUNT");
        (w.Order(), w.Command()).ShouldBe(("POST,CMD,DELETE", "CMD bash loop.sh"), "C1627 WrapperCapUnenforced a wrapped bash loop runs unchanged under the lease");

        using var dn = new SlotWorld();
        dn.Run("--label", "build", "--", "dotnet", "build", "tests/X").ShouldBe(0);
        (dn.Command(), dn.LineCount("^BUILD SLOT warning"), dn.Children.Single().Environment.ContainsKey("ANTIPHON_BUILD_SLOT_MAXCPUCOUNT"))
            .ShouldBe(("CMD dotnet build tests/X -maxcpucount:3", 0, false), "C1627 WrapperCapUnenforced dotnet build keeps its argv and environment and gets no warning");

        using var ns = new SlotWorld();
        ns.Run("--label", "operator", "--no-slot", "--", "bash", "loop.sh").ShouldBe(0);
        (ns.LineCount("^BUILD SLOT warning"), ns.Children.Single().Environment.ContainsKey("ANTIPHON_BUILD_SLOT_MAXCPUCOUNT"))
            .ShouldBe((0, false), "C1627 WrapperCapUnenforced -NoSlot exports no grant and prints no warning");
        BuildSlotCommandLine.CapWarning(["dotnet.exe", "build"], 3).ShouldBeNull();
        BuildSlotCommandLine.CapWarning(["/bin/bash", "x"], 0).ShouldBeNull();
    }

    [Test]
    public void C1048_WindowsPowerShellStartsWithoutTheCallersModulePath()
    {
        if (!OperatingSystem.IsWindows()) throw new TUnit.Core.Exceptions.SkipTestException("Windows PowerShell 5.1 qualification requires Windows");
        using var w = new SlotWorld();
        foreach (var executable in new[] { "powershell.exe", "powershell", Path.Combine(w.Bin, "powershell.exe"), Path.Combine(w.Bin, "PoWeRsHeLl.ExE") })
        {
            w.Children.Clear();
            w.Run("--label", "c1048", "--", executable, "-NoProfile", "-File", "probe.ps1").ShouldBe(0);
            var environment = w.Children.Single().Environment;
            (environment.ContainsKey("PSModulePath") && environment["PSModulePath"] == null).ShouldBeTrue("C1048 WindowsPowerShell " + executable + " starts without PSModulePath");
        }
        w.Children.Clear();
        w.Run("--label", "c1048-pwsh", "--", "pwsh", "-NoProfile", "-File", "probe.ps1").ShouldBe(0);
        w.Children.Single().Environment.ContainsKey("PSModulePath").ShouldBeFalse("C1048 Pwsh module path matches baseline: PowerShell 7 keeps it");
    }

    // CARD-1048's real witness: Windows PowerShell 5.1 started under the slot by a PowerShell 7 parent (its module path in
    // the environment) still loads Get-FileHash from its own modules.
    [Test]
    public void C1048_WrapperWindowsPowerShellGetFileHash()
    {
        if (!OperatingSystem.IsWindows()) throw new TUnit.Core.Exceptions.SkipTestException("Windows PowerShell 5.1 qualification requires Windows");
        using var w = new SlotWorld();
        var input = w.Fixture.PathOf("input.txt");
        File.WriteAllBytes(input, "abc"u8.ToArray());
        var output = new StringWriter();
        w.RealChildren = new BuildSlotProcesses(null, output, CancellationToken.None);
        w.Variables["PATH"] = Environment.GetEnvironmentVariable("PATH");
        var injected = w.Fixture.PathOf("pwsh7-modules");
        static string Quoted(string value) => "'" + value.Replace("'", "''") + "'";
        // -Command joins every later argument into the command text, so the paths are literals in it.
        var probe = "$ErrorActionPreference = 'Stop'; $h = Get-FileHash -LiteralPath " + Quoted(input) + " -Algorithm SHA256; "
            + "[Console]::Out.WriteLine('PAYLOAD ' + $PSVersionTable.PSVersion.Major + ' ' + $PSVersionTable.PSVersion.Minor + ' ' + $PSVersionTable.PSEdition + ' ' + $h.Hash + ' ' "
            + "+ ((Get-Command Get-FileHash).Module.Path.StartsWith((Join-Path $PSHOME 'Modules'), [StringComparison]::OrdinalIgnoreCase)) + ' ' + (($env:PSModulePath -split ';') -contains "
            + Quoted(injected) + "))";
        var options = new BuildSlotOptions
        {
            Label = "c1048-winps", Command = ["powershell.exe", "-NoProfile", "-NonInteractive", "-Command", probe], WorkingDirectory = w.Fixture.Root,
            Environment = new Dictionary<string, string> { ["PSModulePath"] = injected + ";" + Environment.GetEnvironmentVariable("PSModulePath") },
        };
        BuildSlot.Run(options, w.Host()).ShouldBe(0, "C1048 WindowsPowerShell exits zero\n" + output);
        var payload = output.ToString().Split('\n').Select(line => line.Trim()).Single(line => line.StartsWith("PAYLOAD ", StringComparison.Ordinal)).Split(' ');
        payload[1..].ShouldBe(["5", "1", "Desktop", "BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD", "True", "False"],
            "C1048 WindowsPowerShell runs 5.1 Desktop, hash matches, uses Windows modules, without the parent's path");
        w.Order().ShouldBe("POST,DELETE", "C1048 WindowsPowerShell releases lease");
    }

    [NativeLinuxOnly("CARD-1981 the SDK temp leak was observed on Linux only")]
    [Test]
    public void C1981_LeaseTempDirConfinesSdkScratch()
    {
        if (!OperatingSystem.IsLinux()) throw new TUnit.Core.Exceptions.SkipTestException("CARD-1981 the SDK temp leak was observed on Linux only");
        using var w = new SlotWorld();
        // The production lease directory (scripts/lib/LeaseTempDirectory.cs, as the CLI binds it).
        var type = typeof(Antiphon.Checkpoints.ProcessDriver).Assembly.GetType("Antiphon.Build.LeaseTempDirectory", throwOnError: true)!;
        w.LeaseTemp = () =>
        {
            var handle = (IDisposable?)type.GetMethod("TryCreate")!.Invoke(null, null);
            return handle == null ? null : new BuildSlotTemp((string)handle.GetType().GetProperty("DirectoryPath")!.GetValue(handle)!, handle);
        };
        // A dotnet on PATH that records its temp variables and makes the SDK's empty six-character scratch directory in TMPDIR.
        var log = w.Fixture.PathOf("child.log");
        var dotnet = Path.Combine(w.Bin, "dotnet");
        File.WriteAllText(dotnet, "#!/bin/sh\nprintf 'TMPDIR %s\\nTEMP %s\\nTMP %s\\n' \"$TMPDIR\" \"$TEMP\" \"$TMP\" > \"$C1981_LOG\"\n"
            + "dir=\"${TMPDIR:-/tmp}/$(head -c 64 /dev/urandom | tr -dc 'A-Za-z0-9' | head -c 6)\"\nmkdir \"$dir\" && printf 'SDKDIR %s\\n' \"$dir\" >> \"$C1981_LOG\"\n");
        File.SetUnixFileMode(dotnet, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        w.Variables["PATH"] = w.Bin + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH");
        w.RealChildren = new BuildSlotProcesses(null, new StringWriter(), CancellationToken.None);
        var before = SixCharTemps();
        var parent = Environment.GetEnvironmentVariable("TMPDIR");
        var options = new BuildSlotOptions { Label = "c1981-lease", Command = ["dotnet", "build"], WorkingDirectory = w.Fixture.Root,
            Environment = new Dictionary<string, string> { ["C1981_LOG"] = log } };
        BuildSlot.Run(options, w.Host()).ShouldBe(0);
        var values = File.ReadAllLines(log).Select(line => line.Split(' ', 2)).ToDictionary(parts => parts[0], parts => parts.Length > 1 ? parts[1] : "");
        System.Text.RegularExpressions.Regex.IsMatch(values["TMPDIR"], "^/tmp/aslot-[0-9a-f]{8}$").ShouldBeTrue("C1981 LeaseTempDirConfinesSdkScratch child TMPDIR is a short owned lease path: " + values["TMPDIR"]);
        (values["TEMP"] != values["TMPDIR"] && values["TMP"] != values["TMPDIR"]).ShouldBeTrue("C1981 LeaseTempDirConfinesSdkScratch TEMP and TMP stay off the lease path");
        Environment.GetEnvironmentVariable("TMPDIR").ShouldBe(parent, "C1981 LeaseTempDirConfinesSdkScratch parent TMPDIR is unchanged");
        (Directory.Exists(values["SDKDIR"]), Directory.Exists(values["TMPDIR"])).ShouldBe((false, false), "C1981 LeaseTempDirConfinesSdkScratch the sdk scratch and the lease directory are gone");
        SixCharTemps().Except(before).ShouldBeEmpty("C1981 LeaseTempDirConfinesSdkScratch parent /tmp gains no six-character directory");
    }

    private static string[] SixCharTemps() =>
        Directory.EnumerateDirectories("/tmp").Select(Path.GetFileName).Where(name => name != null && System.Text.RegularExpressions.Regex.IsMatch(name, "^[A-Za-z0-9]{6}$"))
            .Select(name => name!).ToArray();

    [Test]
    public void Options_take_the_launcher_flags_and_keep_the_command_verbatim()
    {
        var options = BuildSlotOptions.Parse(["--label", "x", "--slot-wait-minutes", "2.5", "--no-slot", "--holder-pid", "77", "--", "dotnet", "build", "-m:2", "--", "", "--label"], "C:/w");
        (options.Label, options.WaitMinutes, options.NoSlot, options.HolderPid, options.WorkingDirectory).ShouldBe(("x", 2.5, true, (int?)77, "C:/w"));
        options.Command.ShouldBe(["dotnet", "build", "-m:2", "--", "", "--label"]);
        var bare = BuildSlotOptions.Parse(["dotnet", "test"], "");
        (bare.Label, bare.WaitMinutes, bare.NoSlot, bare.HolderPid).ShouldBe(("", 45.0, false, (int?)null));
        bare.Command.ShouldBe(["dotnet", "test"], "the first argument that is not a flag starts the command");
        foreach (var (arguments, message) in new (string[], string)[]
                 {
                     ([], "missing command"), (["--label", "x"], "missing command"), (["--label", "x", "--"], "missing command"),
                     (["--lable", "x", "--", "dotnet"], "unknown flag --lable"), (["-Label", "x", "--", "dotnet"], "unknown flag -Label"),
                     (["--label", "a", "--label", "b", "--", "dotnet"], "repeated flag --label"), (["--no-slot", "--no-slot", "--", "dotnet"], "repeated flag --no-slot"),
                     (["--slot-wait-minutes", "soon", "--", "dotnet"], "--slot-wait-minutes expects"), (["--slot-wait-minutes", "-1", "--", "dotnet"], "--slot-wait-minutes expects"),
                     (["--holder-pid", "0", "--", "dotnet"], "--holder-pid expects"), (["--label"], "missing value for --label"),
                 })
            Should.Throw<NightlyUsageException>(() => BuildSlotOptions.Parse(arguments, "")).Message.ShouldStartWith(message);
        using var w = new SlotWorld();
        Should.Throw<NightlyUsageException>(() => BuildSlot.Run(new BuildSlotOptions(), w.Host()));
        w.Timeline().ShouldBeEmpty("a missing command asks nothing");
    }

    [Test]
    public void The_endpoint_and_the_request_name_the_host_and_the_holder()
    {
        BuildSlotBroker.Endpoint(_ => null, windows: true).ShouldBe("http://localhost:17204/build-slots");
        BuildSlotBroker.Endpoint(_ => null, windows: false).ShouldBe("http://127.0.0.1:8080/build-slots");
        BuildSlotBroker.Endpoint(name => name == "ANTIPHON_BUILD_SLOTS_URL" ? " http://build-slots:8080/build-slots/ " : null, windows: false).ShouldBe("http://build-slots:8080/build-slots");
        BuildSlotBroker.RequestBody(12, null, "a \"b\"", null, "t-1").ShouldBe("{\"pid\":12,\"processStartUtc\":null,\"label\":\"a \\\"b\\\"\",\"sessionId\":null,\"taskId\":\"t-1\"}");
        BuildSlotBroker.Span(59.9).ShouldBe("59s");
        BuildSlotBroker.Span(2700).ShouldBe("45m");

        using var w = new SlotWorld();
        w.Variables["ANTIPHON_SESSION_ID"] = "s-1";
        w.Variables["ANTIPHON_TASK_ID"] = " ";
        w.Run("--label", "ids", "--", "dotnet", "build").ShouldBe(0);
        using var body = JsonDocument.Parse(w.Bodies.Single());
        (body.RootElement.GetProperty("sessionId").GetString(), body.RootElement.GetProperty("taskId").ValueKind).ShouldBe(("s-1", JsonValueKind.Null));
        w.Timeline()[0].ShouldEndWith(" uri=http://127.0.0.1:1/build-slots");

        // Without --holder-pid the lease names this process; a blank label is the command's first two tokens.
        using var own = new SlotWorld();
        BuildSlot.Run(BuildSlotOptions.Parse(["--", "dotnet", "build", "x"], own.Fixture.Root), own.Host()).ShouldBe(0);
        own.Timeline()[0].ShouldStartWith("SLOT POST label=dotnet build pid=4242 ");
    }

    // The client against the real broker routes (BuildSlotRoutes in process, renew mode): grant, renewals, release.
    [Test]
    public async Task The_client_holds_and_releases_a_lease_on_the_real_routes()
    {
        await using var broker = await BuildSlotBrokerFixture.StartAsync(budget: 1, cpu: 6, renewEvery: 1);
        using var w = new SlotWorld();
        var transport = NightlyReport.HttpTransport(broker.Handler);
        var host = w.Host();
        var real = new BuildSlotHost
        {
            Out = host.Out, Error = host.Error, Clock = TimeProvider.System, Sleep = Thread.Sleep, Http = transport, Children = host.Children, Processes = host.Processes,
            Variables = name => name == "ANTIPHON_BUILD_SLOTS_URL" ? "http://slots.test/build-slots" : w.Variables.GetValueOrDefault(name), Files = host.Files, Windows = host.Windows,
            RenewWait = host.RenewWait,
        };
        var held = 0;
        w.Child = _ =>
        {
            held = broker.Broker.List().Leases.Count;
            return 0;
        };
        BuildSlot.Run(BuildSlotOptions.Parse(["--holder-pid", SlotWorld.Holder.ToString(), "--label", "real-routes", "--", "dotnet", "build"], w.Fixture.Root), real).ShouldBe(0);
        held.ShouldBe(1, "the lease is held while the command runs");
        broker.Broker.List().Leases.ShouldBeEmpty("released after the command");
        w.Command().ShouldBe("CMD dotnet build -maxcpucount:6");
        w.LineCount(@"^BUILD SLOT granted lease=[0-9a-f-]{36} waited=\d+s maxcpucount=6$").ShouldBe(1);
        w.LineCount(@"^BUILD SLOT released lease=[0-9a-f-]{36} held=\d+s$").ShouldBe(1);
    }

    // In-process callers: the runner the CLI binds captures the slot's lines and the command's output when asked to.
    [Test]
    public void A_capturing_runner_writes_the_slot_lines_and_the_output_to_the_capture()
    {
        using var fixture = NightlyFixture.Create("build-slot-runner");
        var runner = BuildSlot.Runner(new BuildSlotPlatform(), fixture.Out, fixture.Error);
        var capture = new StringWriter();
        var options = new BuildSlotOptions
        {
            Label = "capture", NoSlot = true, Command = ["dotnet", "exec", Harness, "cli-echo", "captured"], WorkingDirectory = fixture.Root,
        };
        runner(options, capture).ShouldBe(0, capture.ToString());
        capture.ToString().ShouldContain("BUILD SLOT skipped by -NoSlot");
        capture.ToString().ShouldContain("\"arguments\":[\"captured\"]");
        fixture.Out.ToString().ShouldBeEmpty("nothing reached the runner's own output");
    }
}

/// <summary>
/// A loopback /build-slots for a test that runs the CLI as a separate process: POST grants one lease (maxCpuCount 2),
/// DELETE releases it; every request is recorded as POST or DELETE.
/// </summary>
internal sealed class LoopbackBuildSlots : IDisposable
{
    private readonly HttpListener _listener = new();
    private readonly List<string> _calls = [];
    private readonly Task _loop;

    private LoopbackBuildSlots(int port)
    {
        Url = "http://127.0.0.1:" + port + "/build-slots";
        _listener.Prefixes.Add("http://127.0.0.1:" + port + "/");
        _listener.Start();
        _loop = Task.Run(Serve);
    }

    public string Url { get; }

    public static LoopbackBuildSlots Start()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return new LoopbackBuildSlots(port);
    }

    public string[] Calls()
    {
        lock (_calls) return _calls.ToArray();
    }

    private async Task Serve()
    {
        while (_listener.IsListening)
        {
            HttpListenerContext context;
            try { context = await _listener.GetContextAsync(); }
            catch (Exception e) when (e is HttpListenerException || e is ObjectDisposedException || e is InvalidOperationException) { return; }
            lock (_calls) _calls.Add(context.Request.HttpMethod);
            var body = context.Request.HttpMethod == "POST"
                ? "{\"leaseId\":\"c5890000-0000-4000-8000-000000000002\",\"maxCpuCount\":2,\"occupied\":1,\"budget\":1,\"expiresAtUtc\":null,\"unlimited\":false}"
                : "";
            context.Response.StatusCode = context.Request.HttpMethod == "POST" ? 200 : 204;
            var bytes = Encoding.UTF8.GetBytes(body);
            if (bytes.Length > 0)
            {
                context.Response.ContentType = "application/json";
                await context.Response.OutputStream.WriteAsync(bytes);
            }
            context.Response.Close();
        }
    }

    public void Dispose()
    {
        _listener.Stop();
        _listener.Close();
        try { _loop.Wait(TimeSpan.FromSeconds(5)); }
        catch (AggregateException) { }
    }
}
