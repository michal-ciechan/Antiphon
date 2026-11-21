using Antiphon.Nightly;
using Antiphon.Tests.Scripts.Nightly;
using Antiphon.Tests.TestHelpers;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-0599 V-4 (harness half). Each method runs the matching <c>Test-C599_*</c> case of
/// <c>scripts/test-release-gate.ps1</c> against the PRODUCTION prerequisite probe, the production
/// native-classification rule, the production child-environment builder and the production MTP
/// argument builder; since W2-nightly S11 the probe, the classification and the argument builder are
/// NightlyTestsPlan, and their rows run in-process (C599_PrerequisiteProbe, C599_ExecutionArtifactsArgv).
///
/// <para>The decisive behaviours: a missing Playwright browser or a missing client bundle is red
/// or incomplete and is named, never a skip; <c>e2e</c> classifies as native so it serialises with
/// every other native suite; headed, distiller and live-Telegram variables are cleared for an E2E
/// child; and the executor launches only the eligible class roster with fresh TRX plus discovery
/// and execution diagnostics.</para>
///
/// <para>The real isolated fixtures - owned app, database, runner and browser - are
/// <c>ReleaseGateIsolationTests</c> in <c>Antiphon.E2E</c>; this class covers the harness side.</para>
/// </summary>
[Category("Integration")]
public sealed class ReleaseGateE2EEvidenceTests
{
    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public Task C599_Prerequisites() => RunCaseAsync("C599_Prerequisites", 2,
        "C599 Prerequisites the rc profile requires e2e before any prerequisite check",
        "C599 Prerequisites a failed prerequisite is a named red reason");

    /// <summary>The prerequisite rows of C599_Prerequisites: the probe, the native classification and the install launch.</summary>
    [Test]
    public void C599_PrerequisiteProbe()
    {
        using var fixture = NightlyFixture.Create("c599-prereq");
        var rows = new NightlyCaseRows();
        var logRoot = Directory.CreateDirectory(fixture.PathOf("logs")).FullName;
        // Missing generated playwright script: red, and named.
        var fakeRepo = Directory.CreateDirectory(fixture.PathOf("repo")).FullName;
        var installs = 0;
        var noBrowser = NightlyTestsPlan.E2EPrerequisites(fakeRepo, logRoot, null, Path.Combine(fakeRepo, "client", "dist", "index.html"), _ => { installs++; return 0; });
        rows.Check(!noBrowser.Ok && installs == 0, "C599 Prerequisites a missing playwright script is not ok");
        var names = noBrowser.Rows.Select(r => NightlyPlanCases.Str(r, "name")).ToArray();
        rows.Check(names.Contains("playwright-script"), "C599 Prerequisites the missing browser installer is named", string.Join(",", names));
        rows.Check(names.Contains("client-bundle"), "C599 Prerequisites the missing client bundle is named", string.Join(",", names));
        // New-C599Policy's suites: antiphon and e2e declare project and assembly, client does not.
        var e2e = new PolicySuite { Id = "e2e", Name = "Antiphon.E2E", Project = "tests/Antiphon.E2E/Antiphon.E2E.csproj", Assembly = "Antiphon.E2E", Mode = "manual" };
        var antiphon = new PolicySuite { Id = "antiphon", Name = "Antiphon.Tests", Project = "tests/Antiphon.Tests/Antiphon.Tests.csproj", Assembly = "Antiphon.Tests", Mode = "unattended" };
        var client = new PolicySuite { Id = "client", Name = "client", Mode = "unattended" };
        rows.Check(NightlyTestsPlan.SuiteIsNative(e2e, "e2e"), "C599 Prerequisites e2e is classified native so it serialises");
        rows.Check(NightlyTestsPlan.SuiteIsNative(antiphon, "antiphon"), "C599 Prerequisites antiphon is still classified native");
        rows.Check(!NightlyTestsPlan.SuiteIsNative(client, "client"), "C599 Prerequisites the client suite is not native");
        // The executor must install through the GENERATED playwright.ps1.
        var output = Directory.CreateDirectory(Path.Combine(fakeRepo, "tests", "Antiphon.E2E", "bin", "Debug", "net9.0")).FullName;
        var installer = Path.Combine(output, "playwright.ps1");
        File.WriteAllText(installer, "# generated");
        NightlyLaunch? launch = null;
        var installed = NightlyTestsPlan.E2EPrerequisites(fakeRepo, logRoot, null, "", l => { launch = l; return 0; });
        rows.Check(installed.Ok && launch != null && launch.Arguments.Contains(installer) && launch.Arguments.Contains("-File") && launch.Custody == NightlyCustody.Owned,
            "C599 Prerequisites the executor uses the generated playwright.ps1", launch?.CommandText ?? "no launch");
        rows.Check(launch != null && launch.Arguments.SkipWhile(a => a != installer).Skip(1).SequenceEqual(new[] { "install", "chromium" })
            && launch.LogPath == Path.Combine(logRoot, "e2e-playwright-install.log"), "C599 Prerequisites the executor installs chromium", launch?.CommandText ?? "no launch");
        rows.ShouldAllPass(8);
    }

    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public Task C599_EnvironmentIsolation() => RunCaseAsync("C599_EnvironmentIsolation", 6,
        "C599 EnvironmentIsolation ANTIPHON_HEADED_TESTS is cleared for an e2e child",
        "C599 EnvironmentIsolation ANTIPHON_DISTILLER_APPLY_CANARY is cleared for an e2e child",
        "C599 EnvironmentIsolation ANTIPHON_TG_TEST_TOKEN is cleared for an e2e child",
        "C599 EnvironmentIsolation an unrelated variable survives",
        "C599 EnvironmentIsolation the broker opt-in IS set for messaging");

    /// <summary>
    /// The executor rows of C599_ExecutionArtifacts, in-process since W2-nightly S12 (they read nightly-tests-impl.ps1's
    /// text; the chunk runner is NightlyChunkRunner now): run one native chunk and watch it wait, refuse and record.
    /// </summary>
    [Test]
    [NotInParallel("QualificationTimingTests")]
    public void C599_ExecutionArtifacts()
    {
        var rows = new NightlyCaseRows();
        using (var world = new NightlyChunkWorld(prefix: "c599-artifacts"))
        {
            var waited = new List<NightlyLaunchResult>();
            world.Launcher.Cleanup = result => { waited.Add(result); return true; };
            var outcome = NightlyChunkRunner.Run(world.Context(), world.Suite(), NightlyChunkWorld.Chunk());
            rows.Check(outcome.Error == null && waited.Count == 1 && world.Launcher.Launches.Count == 1, "C599 ExecutionArtifacts the executor waits for owned cleanup",
                outcome.Error?.Message ?? "waited=" + waited.Count);
            var row = outcome.Row;
            rows.Check(row != null && row.Contains("requiredUidCount") && row.Contains("excludedUidCount") && row.Contains("censusDigest") && row.Contains("exclusions"),
                "C599 ExecutionArtifacts the suite row records the expanded census", row == null ? "no row" : string.Join(",", row.Keys.Cast<object>()));
        }
        using (var held = new NightlyChunkWorld(prefix: "c599-artifacts"))
        {
            held.Launcher.Cleanup = _ => false;
            var outcome = NightlyChunkRunner.Run(held.Context(), held.Suite(), NightlyChunkWorld.Chunk());
            rows.Check(outcome.Error?.Message == "owned-child still running" && outcome.Row == null, "C599 ExecutionArtifacts an unjoined owned child is an error",
                outcome.Error?.Message ?? "accepted");
        }
        rows.ShouldAllPass(3, "C599 ExecutionArtifacts the executor waits for owned cleanup", "C599 ExecutionArtifacts an unjoined owned child is an error",
            "C599 ExecutionArtifacts the suite row records the expanded census");
    }

    /// <summary>The argv rows of C599_ExecutionArtifacts (NightlyTestsPlan.NativeExecutionArguments).</summary>
    [Test]
    public void C599_ExecutionArtifactsArgv()
    {
        using var fixture = NightlyFixture.Create("c599-argv");
        var rows = new NightlyCaseRows();
        var trx = fixture.PathOf("e2e-all.trx");
        var diag = fixture.PathOf("e2e-exec");
        var nativeArgs = NightlyTestsPlan.NativeExecutionArguments(trx, diag, ["Antiphon.E2E.SmokeE2ETests", "Antiphon.E2E.BoardE2ETests"], "",
            "/*/Antiphon.E2E/(SmokeE2ETests*)|(BoardE2ETests*)/*", "");
        rows.Check(nativeArgs.Contains("--report-trx"), "C599 ExecutionArtifacts the executor requests a fresh TRX");
        rows.Check(nativeArgs.Contains("--diagnostic"), "C599 ExecutionArtifacts the executor requests MTP diagnostics");
        rows.Check(nativeArgs.Contains("--diagnostic-output-directory"), "C599 ExecutionArtifacts the executor pins the diagnostic directory");
        var filter = NightlyPlanCases.Filter(nativeArgs) ?? "";
        rows.Check(filter.Contains("SmokeE2ETests", StringComparison.OrdinalIgnoreCase) && filter.Contains("BoardE2ETests", StringComparison.OrdinalIgnoreCase),
            "C599 ExecutionArtifacts the executor launches only the eligible roster", filter);
        // The TRX filename reaching MTP must be a basename with the directory carried separately, or the runner writes
        // the report where the census never reads it.
        var nested = NightlyTestsPlan.NativeExecutionArguments(Path.Combine(fixture.PathOf("sub"), "x.trx"), diag, [], "", "", "");
        var name = nested[Array.IndexOf(nested, "--report-trx-filename") + 1];
        var directory = nested[Array.IndexOf(nested, "--results-directory") + 1];
        rows.Check(string.Equals(name, "x.trx", StringComparison.OrdinalIgnoreCase), "C599 ExecutionArtifacts the trx filename is passed as a basename", name);
        rows.Check(directory.EndsWith("sub", StringComparison.OrdinalIgnoreCase), "C599 ExecutionArtifacts the trx directory is passed separately", directory);
        var message = NightlyPlanCases.Refusal(() => NightlyTestsPlan.NativeExecutionArguments(trx, "", [], "", "", "")) ?? "";
        rows.Check(message.Contains("diagnostic directory", StringComparison.OrdinalIgnoreCase), "C599 ExecutionArtifacts a missing diagnostic directory is refused", message);
        rows.ShouldAllPass(7);
    }

    private static Task RunCaseAsync(string caseName, int expectedRows, params string[] requiredRows) =>
        ScriptHarness.RunHarnessCaseAsync("test-release-gate.ps1", "C599", caseName, expectedRows, requiredRows);
}
