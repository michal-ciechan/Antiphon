using System.Text;
using Antiphon.Nightly;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-0727 and the cards since, unit 65 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md: the deploy-server2 command
/// (Antiphon.Nightly.Server2RollingDeploy), which replaced scripts/deploy-server2.ps1, in process on Server2RollingWorld (the C# port
/// of the script's offline fakes). The captured tables pin what the script did for every driver scenario of the deleted
/// scripts/test-deploy-server2.ps1 and for the entry cases: the same exit, lines, trace and evidence.
/// </summary>
[Category("Unit")]
public sealed class Server2RollingDeployTests
{
    private static string Compare(Server2RollingCaptures.Capture capture)
    {
        using var world = Server2RollingCaptures.World(capture);
        var run = world.Run(capture.Args);
        var problems = new StringBuilder();
        void Check(string what, string expected, string actual)
        {
            if (!string.Equals(expected, actual, StringComparison.Ordinal))
                problems.AppendLine("  " + what + ":\n    expected: " + expected.Replace("\n", "\\n") + "\n    actual:   " + actual.Replace("\n", "\\n"));
        }
        Check("exit", capture.Exit.ToString(), run.Exit.ToString());
        Check("stdout", Server2RollingCaptures.Normalize(capture.Stdout), Server2RollingCaptures.Normalize(run.Stdout));
        Check("stderr", Server2RollingCaptures.Normalize(capture.Stderr), Server2RollingCaptures.Normalize(run.Stderr));
        var trace = Server2RollingCaptures.Collapse(run.Trace.Select(Server2RollingCaptures.Signature));
        Check("trace", string.Join("\n", capture.Trace), string.Join("\n", trace));
        var files = Server2RollingCaptures.Files(world, run.RunId).Select(Server2RollingCaptures.Normalize).ToArray();
        Check("files", string.Join("\n", capture.Files.Select(Server2RollingCaptures.Normalize)), string.Join("\n", files));
        Console.WriteLine("CAPTURE " + capture.Name + " exit=" + run.Exit + " trace=" + trace.Length + " files=" + files.Length + (problems.Length == 0 ? " same" : " differs"));
        return problems.Length == 0 ? "" : capture.Name + "\n" + problems;
    }

    [Test]
    public void Driver_and_entry_cases_match_the_captured_script()
    {
        Server2RollingCaptures.Driver.Length.ShouldBe(92);
        var failures = Server2RollingCaptures.Driver.Select(Compare).Where(x => x.Length > 0).ToArray();
        failures.ShouldBeEmpty(failures.Length + " of " + Server2RollingCaptures.Driver.Length + " differ:\n" + string.Join("\n", failures));
    }

    // The W10 node harnesses' seams (Server2RollingStubOs, the script's C727_TEST_* switches): bound only when a stub is named, with
    // the variables' timings and the fixture state's clockMs as the temp container retirement's clock. No stub is started here.
    [Test]
    public void Stub_world_binds_the_C727_timings_and_fixture_clock()
    {
        Server2RollingStubOs.Wanted(_ => null).ShouldBeFalse();
        Server2RollingStubOs.Wanted(n => n == "C727_TEST_STATE" ? "state.json" : null).ShouldBeFalse("state alone is no stub");
        Server2RollingStubOs.Wanted(n => n == "C727_TEST_HTTP_STUB" ? Server2RollingStubOs.FakeHttp : null).ShouldBeTrue();
        Server2RollingStubOs.Wanted(n => n == "C727_TEST_VERIFY_STUB" ? "verify.mjs" : null).ShouldBeTrue();
        // Unit 68: the HTTP fake is the one in process; a path (the deleted c727-fake-http.ps1) is a usage error.
        Should.Throw<NightlyUsageException>(() => new Server2RollingStubOs(n => n == "C727_TEST_HTTP_STUB" ? "c727-fake-http.ps1" : null,
            new Server2RollingOs(Path.GetTempPath())));
        var root = Directory.CreateTempSubdirectory("u65-stub-");
        try
        {
            var state = Path.Combine(root.FullName, "state.json");
            File.WriteAllText(state, "{\"scenario\":\"c1008\",\"clockMs\":1000}");
            var variables = new Dictionary<string, string>
            {
                ["C727_TEST_VERIFY_STUB"] = "verify.mjs", ["C727_TEST_STATE"] = state, ["C727_TEST_WAIT_MS"] = "100", ["C727_TEST_POLL_MS"] = "5",
                ["C727_TEST_LAND_WAIT_MS"] = "7",
            };
            var stub = new Server2RollingStubOs(n => variables.GetValueOrDefault(n)!, new Server2RollingOs(root.FullName));
            (stub.Timing.WaitMs, stub.Timing.PollMs, stub.Timing.LandWaitMs, stub.Timing.StubbedReads).ShouldBe((100, 5, 7, false));
            stub.C994Now().ShouldBe(DateTime.UnixEpoch.AddMilliseconds(1000));
            stub.C994Advance(5);
            stub.C994Now().ShouldBe(DateTime.UnixEpoch.AddMilliseconds(1005));
            System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(state))!["scenario"]!.GetValue<string>().ShouldBe("c1008");
            // No clockMs: the host's clock (null), and the advance writes nothing.
            File.WriteAllText(state, "{\"scenario\":\"c1008\"}");
            stub.C994Now().ShouldBeNull();
            stub.C994Advance(5);
            File.ReadAllText(state).ShouldBe("{\"scenario\":\"c1008\"}");
            // Without C727_TEST_STATE there is no fixture clock; with the HTTP stub the reads are stubbed.
            variables.Remove("C727_TEST_STATE");
            variables["C727_TEST_HTTP_STUB"] = Server2RollingStubOs.FakeHttp;
            var reads = new Server2RollingStubOs(n => variables.GetValueOrDefault(n)!, new Server2RollingOs(root.FullName));
            reads.C994Now().ShouldBeNull();
            reads.Timing.StubbedReads.ShouldBeTrue();
        }
        finally { root.Delete(true); }
    }

    // The CARD-1008 state shape: recycle, preview, resume, retirement proof, temp container cleanup and census cases.
    [Test]
    public void Recycle_retirement_and_census_cases_match_the_captured_script()
    {
        Server2RollingCaptures.C1008.Length.ShouldBe(37);
        var failures = Server2RollingCaptures.C1008.Select(Compare).Where(x => x.Length > 0).ToArray();
        failures.ShouldBeEmpty(failures.Length + " of " + Server2RollingCaptures.C1008.Length + " differ:\n" + string.Join("\n", failures));
    }
}
