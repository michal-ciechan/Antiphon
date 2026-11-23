using System.IO.Abstractions;
using Antiphon.Nightly;
using Antiphon.Tests.Scripts.Nightly;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// The land-graph-audit and land-graph-precheck commands' own steps (LandGraphAuditRun; they replaced
/// scripts/audit-land-verification-graph.ps1 and scripts/audit-land-verification-precheck.ps1, unit 11 of
/// 2026-11-22-remaining-ps1-to-csharp.md): the tool's command lines, the opening checks the audit runs itself when the tool
/// is not current, and how the tool's exit code is passed on. git, dotnet and the tool are a recording launcher; the
/// end-to-end runs over the real tool are LandGraphEvaluationTests (RunGraphScriptAsync) and LandGraphLandRefreshTests.
/// </summary>
[Category("Unit")]
public sealed class LandGraphCommandTests
{
    private const string Head = "0123456789abcdef0123456789abcdef01234567";
    private const string DefaultDll = "tools/Antiphon.LandGraphAudit/bin-land-audit/Release/net10.0/Antiphon.LandGraphAudit.dll";

    [Test]
    public void AuditOptionsAreTheFormerScriptParametersInKebabCase()
    {
        var options = LandGraphAuditRunOptions.Parse(["--update", "--land-refresh", "--tool-path", "t.dll", "--evaluation-budget-seconds", "120"], "repo",
            new Dictionary<string, string> { ["PATH"] = "p" });
        options.Update.ShouldBeTrue();
        options.LandRefresh.ShouldBeTrue();
        options.Check.ShouldBeFalse();
        options.ToolPath.ShouldBe("t.dll");
        options.EvaluationBudgetSeconds.ShouldBe(120);
        options.RepoRoot.ShouldBe("repo");
        options.Environment!["PATH"].ShouldBe("p");
        options.CaptureOutput.ShouldBeFalse();
        options.TimeoutMilliseconds.ShouldBe(0);
        LandGraphAuditRunOptions.Parse(["--repo", "other"], "repo", null).RepoRoot.ShouldBe("other");
        var none = LandGraphAuditRunOptions.Parse([], "repo", null);
        (none.Check || none.Update || none.LandRefresh).ShouldBeFalse();
        none.EvaluationBudgetSeconds.ShouldBe(0);
        Should.Throw<NightlyUsageException>(() => LandGraphAuditRunOptions.Parse(["-Check"], "repo", null));
        Should.Throw<NightlyUsageException>(() => LandGraphAuditRunOptions.Parse(["--evaluation-budget-seconds", "soon"], "repo", null)).Message
            .ShouldContain("--evaluation-budget-seconds");
        Should.Throw<NightlyUsageException>(() => LandGraphAuditRunOptions.Parse(["--witness-clock", "1"], "repo", null)).Message.ShouldBe("unknown flag --witness-clock");

        var precheck = LandGraphPrecheckRunOptions.Parse(["--update", "--manifest-path", "m.txt", "--tool-path", "t.dll"], "repo");
        precheck.Update.ShouldBeTrue();
        precheck.ManifestPath.ShouldBe("m.txt");
        precheck.ToolPath.ShouldBe("t.dll");
        precheck.RepoRoot.ShouldBe("repo");
        Should.Throw<NightlyUsageException>(() => LandGraphPrecheckRunOptions.Parse(["--land-refresh"], "repo"));
    }

    [Test]
    public void ACurrentToolRunsTheWholeAuditOnThisProcesssStreams()
    {
        using var fixture = NightlyFixture.Create("land-graph-command");
        var dll = WriteFixtureTool(fixture.Root);
        var launcher = new RecordingLauncher(_ => new NightlyLaunchResult { ExitCode = 0 });
        var environment = new Dictionary<string, string> { ["PATH"] = "p", ["MSBuildSDKsPath"] = "x", ["MSBUILD_EXE_PATH"] = "y" };
        var host = fixture.Host(launcher: launcher, processes: new FakeProcessTable { CurrentPid = 4242 });
        var result = LandGraphAuditRun.Run(new LandGraphAuditRunOptions { RepoRoot = fixture.Root + Path.DirectorySeparatorChar, Check = true, Environment = environment }, host,
            new FileSystem());

        result.ShouldBe(new LandGraphAuditResult(0, false));
        fixture.Error.ToString().ShouldBeEmpty();
        var launch = launcher.Launches.ShouldHaveSingleItem();
        launch.FileName.ShouldBe("dotnet");
        launch.Arguments.ShouldBe(new[]
        {
            dll, "audit", "--repo", fixture.Root, "--tool-path", dll, "--evaluation-budget-seconds", "0", "--parent-pid", "4242", "--check",
        });
        launch.WorkingDirectory.ShouldBe(fixture.Root);
        launch.CaptureOutput.ShouldBeFalse("the tool writes to this process's own streams");
        launch.TimeoutMilliseconds.ShouldBe(0);
        launch.Environment!.Keys.ShouldBe(new[] { "PATH" }, "MSBuild* names never reach the evaluator");
    }

    [Test]
    [Arguments(1, "")]
    [Arguments(2, "audit refused: land graph audit tool failed (exit 2)")]
    [Arguments(64, "audit refused: land graph audit tool failed (exit 64)")]
    public void OnlyTheToolsExitZeroApproves(int exitCode, string line)
    {
        using var fixture = NightlyFixture.Create("land-graph-command");
        WriteFixtureTool(fixture.Root);
        var launcher = new RecordingLauncher(_ => new NightlyLaunchResult { ExitCode = exitCode });
        var result = LandGraphAuditRun.Run(new LandGraphAuditRunOptions { RepoRoot = fixture.Root, Update = true }, fixture.Host(launcher: launcher), new FileSystem());
        result.ExitCode.ShouldBe(1);
        fixture.Error.ToString().Trim().ShouldBe(line);
        launcher.Launches.Single().Arguments.ShouldContain("--update");
    }

    [Test]
    public void AToolThatDoesNotStartRefuses()
    {
        using var fixture = NightlyFixture.Create("land-graph-command");
        WriteFixtureTool(fixture.Root);
        var launcher = new RecordingLauncher(_ => throw new System.ComponentModel.Win32Exception(2, "The system cannot find the file specified."));
        var result = LandGraphAuditRun.Run(new LandGraphAuditRunOptions { RepoRoot = fixture.Root, Check = true }, fixture.Host(launcher: launcher), new FileSystem());
        result.ExitCode.ShouldBe(1);
        fixture.Error.ToString().Trim().ShouldBe("audit refused: land graph audit tool did not start: The system cannot find the file specified.");
    }

    [Test]
    public void TheLandRefreshHopCapturesTheToolAndBoundsIt()
    {
        using var fixture = NightlyFixture.Create("land-graph-command");
        WriteFixtureTool(fixture.Root);
        var launcher = new RecordingLauncher(_ => new NightlyLaunchResult { ExitCode = 1, StandardOutputText = "manifest does not match the selected graph\n", StandardErrorText = "audit refused: x\n" });
        var options = new LandGraphAuditRunOptions { RepoRoot = fixture.Root, Update = true, LandRefresh = true, CaptureOutput = true, TimeoutMilliseconds = 900000 };
        LandGraphAuditRun.Run(options, fixture.Host(launcher: launcher), new FileSystem()).ShouldBe(new LandGraphAuditResult(1, false));
        fixture.Out.ToString().ShouldBe("manifest does not match the selected graph\naudit refused: x\n");
        var launch = launcher.Launches.Single();
        launch.CaptureOutput.ShouldBeTrue();
        launch.TimeoutMilliseconds.ShouldBe(900000);
        launch.Arguments.TakeLast(2).ShouldBe(new[] { "--update", "--land-refresh" });

        using var timedOut = NightlyFixture.Create("land-graph-command");
        WriteFixtureTool(timedOut.Root);
        var killed = new RecordingLauncher(_ => new NightlyLaunchResult { ExitCode = 1, TimedOut = true });
        LandGraphAuditRun.Run(new LandGraphAuditRunOptions { RepoRoot = timedOut.Root, Check = true, CaptureOutput = true, TimeoutMilliseconds = 900000 },
            timedOut.Host(launcher: killed), new FileSystem()).ShouldBe(new LandGraphAuditResult(1, true));
        timedOut.Error.ToString().Trim().ShouldBe("audit refused: land graph audit tool exceeded 900 s");
    }

    [Test]
    public void AMissingToolIsReportedOnlyAfterTheOpeningChecks()
    {
        using var fixture = NightlyFixture.Create("land-graph-command");
        var absent = Path.Combine(fixture.Root, "absent.dll");
        var launcher = Git(clean: true);
        var result = LandGraphAuditRun.Run(new LandGraphAuditRunOptions { RepoRoot = fixture.Root, Check = true, ToolPath = absent, Environment = new Dictionary<string, string>() },
            fixture.Host(launcher: launcher), new FileSystem());
        result.ExitCode.ShouldBe(1);
        fixture.Error.ToString().ShouldBe("built evaluator missing: " + absent + Environment.NewLine + "audit refused: land graph pre-check refused" + Environment.NewLine);
        launcher.Lines.ShouldBe(new[]
        {
            "EXEC git rev-parse HEAD",
            "EXEC git rev-parse HEAD",
            "EXEC git diff --quiet HEAD -- . :(exclude)server/Application/Services/LandVerificationBuildGraph.manifest.txt"
                + " :(exclude)server/Application/Services/LandVerificationBuildGraph.evaluation.txt",
        });
        foreach (var launch in launcher.Launches)
        {
            launch.WorkingDirectory.ShouldBe(fixture.Root);
            launch.TimeoutMilliseconds.ShouldBe(LandGraphAuditRun.GitTimeoutMilliseconds);
            launch.CaptureOutput.ShouldBeTrue();
            launch.Environment.ShouldNotBeNull();
        }
    }

    [Test]
    public void ADirtyTreeRefusesBeforeAnyRebuild()
    {
        using var fixture = NightlyFixture.Create("land-graph-command");
        WriteToolProject(fixture.Root);
        var launcher = Git(clean: false);
        var result = LandGraphAuditRun.Run(new LandGraphAuditRunOptions { RepoRoot = fixture.Root, Update = true, LandRefresh = true }, fixture.Host(launcher: launcher), new FileSystem());
        result.ExitCode.ShouldBe(1);
        fixture.Error.ToString().Trim().ShouldBe("audit refused: source differs from HEAD; commit source and pins before auditing");
        launcher.Lines.Last().ShouldEndWith(" :(exclude)server/Application/Services/LandVerificationBuildGraph.pins.txt", Case.Sensitive,
            "pending pins are allowed only with --update --land-refresh");
        launcher.Launches.ShouldAllBe(launch => launch.FileName == "git");
        File.Exists(Path.Combine(fixture.Root, DefaultDll)).ShouldBeFalse();
    }

    [Test]
    public void AStaleToolIsRebuiltAfterTheOpeningChecksAndStartsAtThePrecheck()
    {
        using var fixture = NightlyFixture.Create("land-graph-command");
        WriteToolProject(fixture.Root);
        var launcher = new RecordingLauncher(launch =>
        {
            if (launch.FileName == "git") return GitAnswer(launch, clean: true);
            var arguments = launch.Arguments.ToArray();
            if (arguments[0] == "build")
            {
                var output = arguments[Array.IndexOf(arguments, "-o") + 1];
                Directory.CreateDirectory(output);
                File.WriteAllText(Path.Combine(output, LandAuditTool.DllName), "built");
            }
            return new NightlyLaunchResult { ExitCode = 0 };
        });
        var result = LandGraphAuditRun.Run(new LandGraphAuditRunOptions { RepoRoot = fixture.Root, Check = true, EvaluationBudgetSeconds = 120 }, fixture.Host(launcher: launcher),
            new FileSystem());
        result.ExitCode.ShouldBe(0, fixture.Error.ToString());
        fixture.Error.ToString().ShouldContain("land audit tool: rebuilding (missing)");
        var dll = Path.Combine(fixture.Root, DefaultDll.Replace('/', Path.DirectorySeparatorChar));
        launcher.Lines.Select(line => line.Split(' ')[1] + " " + line.Split(' ')[2]).ShouldBe(new[]
        {
            "git rev-parse", "git rev-parse", "git diff", "dotnet build", "dotnet " + dll,
        });
        launcher.Launches.Last().Arguments.TakeLast(3).ShouldBe(new[] { "--check", "--head", Head });
        launcher.Launches.Last().Arguments.ShouldContain("120");
    }

    [Test]
    [Arguments("both", "audit refused: Specify -Check or -Update, not both.")]
    [Arguments("land-refresh", "audit refused: -LandRefresh requires -Update.")]
    [Arguments("git-fails", "audit refused: git failed: fatal: not a git repository")]
    [Arguments("head-moved", "audit refused: HEAD changed during audit")]
    [Arguments("git-hangs", "audit refused: subprocess exceeded 300 s: git")]
    public void TheOpeningChecksRefuseWithTheAuditsMessages(string variant, string line)
    {
        using var fixture = NightlyFixture.Create("land-graph-command");
        WriteToolProject(fixture.Root);
        var heads = 0;
        var launcher = new RecordingLauncher(launch =>
        {
            launch.FileName.ShouldBe("git");
            return variant switch
            {
                "git-fails" => new NightlyLaunchResult { ExitCode = 128, StandardErrorText = "fatal: not a git repository\n" },
                "head-moved" => new NightlyLaunchResult { ExitCode = 0, StandardOutputText = (heads++ == 0 ? Head : new string('f', 40)) + "\n" },
                "git-hangs" => new NightlyLaunchResult { ExitCode = 1, TimedOut = true },
                _ => GitAnswer(launch, clean: true),
            };
        });
        var options = new LandGraphAuditRunOptions
        {
            RepoRoot = fixture.Root,
            Check = variant == "both",
            Update = variant == "both",
            LandRefresh = variant == "land-refresh",
        };
        LandGraphAuditRun.Run(options, fixture.Host(launcher: launcher), new FileSystem()).ExitCode.ShouldBe(1);
        fixture.Error.ToString().Trim().ShouldBe(line);
        if (variant is "both" or "land-refresh") launcher.Launches.ShouldBeEmpty();
    }

    [Test]
    public void ThePrecheckPassesTheToolsOutputAndCodesThrough()
    {
        using var fixture = NightlyFixture.Create("land-graph-command");
        var dll = WriteFixtureTool(fixture.Root);
        var launcher = new RecordingLauncher(_ => new NightlyLaunchResult
        {
            ExitCode = 2,
            StandardOutputText = "digest=ABC count=3\n\n",
            StandardErrorText = "git failed: x\n",
        });
        var code = LandGraphAuditRun.Precheck(new LandGraphPrecheckRunOptions { RepoRoot = fixture.Root, Update = true, ManifestPath = "relative.txt" },
            fixture.Host(launcher: launcher), new FileSystem());
        code.ShouldBe(2);
        fixture.Out.ToString().ShouldBe("digest=ABC count=3" + Environment.NewLine);
        fixture.Error.ToString().ShouldBe("git failed: x\n");
        var launch = launcher.Launches.Single();
        launch.Arguments.ShouldBe(new[] { dll, "precheck", "--repo", fixture.Root, "--update", "--manifest-path", "relative.txt" });
        launch.WorkingDirectory.ShouldBe("", "a relative --manifest-path is this process's");
        launch.CaptureOutput.ShouldBeTrue();
        launch.Environment.ShouldBeNull();

        var check = new RecordingLauncher(_ => new NightlyLaunchResult { ExitCode = 0 });
        LandGraphAuditRun.Precheck(new LandGraphPrecheckRunOptions { RepoRoot = fixture.Root }, fixture.Host(launcher: check), new FileSystem()).ShouldBe(0);
        check.Launches.Single().Arguments.Skip(4).ShouldBe(new[] { "--check" });
    }

    [Test]
    [Arguments(3)]
    [Arguments(64)]
    public void ThePrecheckRefusesAnyOtherExit(int exitCode)
    {
        using var fixture = NightlyFixture.Create("land-graph-command");
        WriteFixtureTool(fixture.Root);
        var launcher = new RecordingLauncher(_ => new NightlyLaunchResult { ExitCode = exitCode, StandardErrorText = "unknown command 'precheck'\n" });
        LandGraphAuditRun.Precheck(new LandGraphPrecheckRunOptions { RepoRoot = fixture.Root }, fixture.Host(launcher: launcher), new FileSystem()).ShouldBe(1);
        fixture.Error.ToString().ShouldBe("unknown command 'precheck'\n" + "land graph pre-check tool failed (exit " + exitCode + ")" + Environment.NewLine);
    }

    [Test]
    public void ThePrecheckRefusesBadUsageAMissingToolAndAToolThatDoesNotStart()
    {
        using var fixture = NightlyFixture.Create("land-graph-command");
        var none = new RecordingLauncher();
        LandGraphAuditRun.Precheck(new LandGraphPrecheckRunOptions { RepoRoot = fixture.Root, Check = true, Update = true }, fixture.Host(launcher: none), new FileSystem()).ShouldBe(1);
        fixture.Error.ToString().Trim().ShouldBe("Specify -Check or -Update, not both.");

        using var missing = NightlyFixture.Create("land-graph-command");
        LandGraphAuditRun.Precheck(new LandGraphPrecheckRunOptions { RepoRoot = missing.Root }, missing.Host(launcher: none), new FileSystem()).ShouldBe(1);
        missing.Error.ToString().Trim().ShouldBe("built evaluator missing: " + Path.Combine(missing.Root, DefaultDll.Replace('/', Path.DirectorySeparatorChar)));
        none.Launches.ShouldBeEmpty();

        using var failing = NightlyFixture.Create("land-graph-command");
        WriteFixtureTool(failing.Root);
        var throwing = new RecordingLauncher(_ => throw new System.ComponentModel.Win32Exception(2, "no dotnet"));
        LandGraphAuditRun.Precheck(new LandGraphPrecheckRunOptions { RepoRoot = failing.Root }, failing.Host(launcher: throwing), new FileSystem()).ShouldBe(1);
        failing.Error.ToString().Trim().ShouldBe("land graph pre-check tool did not start: no dotnet");
    }

    // The commands' core touches the machine only through the host (launcher, clock, sleep, writers) and IFileSystem: no
    // console, no ambient environment, no process start or file call of its own (the CLI entries bind the production host).
    [Test]
    [Arguments("scripts/lib/Antiphon.Nightly/LandAuditTool.cs")]
    [Arguments("scripts/lib/Antiphon.Nightly/LandGraphAuditRun.cs")]
    [Arguments("scripts/lib/Antiphon.Nightly/LandGraphLandRefresh.cs")]
    public void TheCoreUsesOnlyTheHostSeams(string relative)
    {
        var source = File.ReadAllText(Path.Combine(DelegateScriptRunner.RepoRoot, relative));
        source.All(static c => c <= 127).ShouldBeTrue(relative + " must stay ASCII");
        source.ShouldContain("unit 11 of", Case.Sensitive, "the header names the plan unit it came from");
        foreach (var banned in new[] { @"\bConsole\.", @"Environment\.Exit", @"GetEnvironmentVariable", @"Process\.Start", @"new ProcessStartInfo", @"Thread\.Sleep",
                     @"(?<![\w.])File\.(Exists|Read|Write|Open|Delete|Move|Copy|Append|Replace)", @"(?<![\w.])Directory\.(Exists|Create|Delete|Move|Get|Enumerate)", @"\bHttpClient\b" })
            System.Text.RegularExpressions.Regex.IsMatch(source, banned).ShouldBeFalse(relative + ": " + banned);
    }

    // A fixture checkout without the tool's project but with its DLL: the commands use that DLL as given.
    private static string WriteFixtureTool(string root)
    {
        var dll = Path.Combine(root, DefaultDll.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(dll)!);
        File.WriteAllText(dll, "tool");
        return dll;
    }

    // A checkout with the tool's project and no built DLL: the default tool must be rebuilt.
    private static void WriteToolProject(string root)
    {
        var project = Path.Combine(root, "tools", "Antiphon.LandGraphAudit", "Antiphon.LandGraphAudit.csproj");
        Directory.CreateDirectory(Path.GetDirectoryName(project)!);
        File.WriteAllText(project, "<Project />");
    }

    private static RecordingLauncher Git(bool clean) => new(launch =>
    {
        launch.FileName.ShouldBe("git");
        return GitAnswer(launch, clean);
    });

    // rev-parse HEAD answers Head; diff --quiet answers clean (0) or dirty (1).
    private static NightlyLaunchResult GitAnswer(NightlyLaunch launch, bool clean) => launch.Arguments[0] switch
    {
        "rev-parse" => new NightlyLaunchResult { ExitCode = 0, StandardOutputText = Head + "\n" },
        "diff" => new NightlyLaunchResult { ExitCode = clean ? 0 : 1 },
        _ => throw new InvalidOperationException("unexpected git " + string.Join(' ', launch.Arguments)),
    };
}
