using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Antiphon.Nightly;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// Unit 68 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md: the W10 node harnesses' C727_TEST_* seams
/// (Server2RollingStubOs) start no pwsh. c727-fake-http and c727-fake-verify are the fakes in process (C727Fakes, the port of the
/// deleted scripts/fixtures/c727-fake-http.ps1 and c727-fake-verify.ps1) on the harness's state and trace files; any other verifier
/// stub is the harness's executable adapter, started directly with --case/--manifest; the c727-fake-verify verb is the adapters'
/// fall-through to the fake. Every driver and C1008 scenario captured from scripts/deploy-server2.ps1, which started the PowerShell
/// fakes with pwsh exactly as the seams did, runs again through the seams on files and must match the capture. Nothing outside the
/// process is started except the adapter scripts this class writes (sh, or cmd on Windows).
/// </summary>
[Category("Unit")]
public sealed class Server2RollingStubTests
{
    // The stub's own manifests, which the capture generator left out (unit 65's gen-pins).
    private static readonly Regex FixtureOnly = new(@"^(host-jq-.*\.manifest\.json|temp-project-absent\.manifest\.json|temp-retirement-read\.manifest\.json) ");

    private static string Compare(Server2RollingCaptures.Capture capture)
    {
        using var world = Server2RollingCaptures.World(capture);
        var run = world.RunOnFiles(capture.Args);
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
        var files = Server2RollingCaptures.Files(world, run.RunId).Where(f => !FixtureOnly.IsMatch(f)).Select(Server2RollingCaptures.Normalize).ToArray();
        Check("files", string.Join("\n", capture.Files.Select(Server2RollingCaptures.Normalize)), string.Join("\n", files));
        return problems.Length == 0 ? "" : capture.Name + "\n" + problems;
    }

    [Test]
    public void Driver_and_recycle_captures_match_through_the_in_process_fakes_on_files()
    {
        var failures = Server2RollingCaptures.Driver.Concat(Server2RollingCaptures.C1008).Select(Compare).Where(x => x.Length > 0).ToArray();
        failures.ShouldBeEmpty(failures.Length + " differ:\n" + string.Join("\n", failures));
    }

    [Test]
    public void The_verb_answers_each_case_from_the_fake_on_the_state_file()
    {
        var root = Directory.CreateTempSubdirectory("u68-verb-");
        try
        {
            var state = Path.Combine(root.FullName, "state.json");
            var trace = Path.Combine(root.FullName, "trace.jsonl");
            var sha = new string('a', 40);
            File.WriteAllText(state, new JsonObject { ["scenario"] = "happy", ["sha"] = sha, ["incompleteRecycle"] = true, ["oldDeployed"] = false }.ToJsonString());
            var variables = new Dictionary<string, string> { ["C727_TEST_STATE"] = state, ["C727_TEST_TRACE"] = trace };
            (int Exit, string Out, string Err) Verb(string hostCase, string? manifest = null, string input = "")
            {
                var output = new StringWriter();
                var error = new StringWriter();
                var exit = C727FakeVerify.Run(new C727FakeVerifyOptions { Case = hostCase, Manifest = manifest }, n => variables.GetValueOrDefault(n)!, root.FullName,
                    new StringReader(input), output, error);
                return (exit, output.ToString().Replace("\r\n", "\n"), error.ToString().Replace("\r\n", "\n"));
            }
            Verb("recycle-discover").ShouldBe((0, "c100800000000000000000000000000000001\n", ""));
            Verb("rollout-lock", input: "release\n").ShouldBe((0, "C1008_LOCKED\n", ""));
            var manifest = Path.Combine(root.FullName, "deploy-parent.manifest.json");
            File.WriteAllText(manifest, new JsonObject { ["sourceSha"] = sha, ["runId"] = "c727aaaaaaaaaaaa", ["evidenceRoot"] = root.FullName }.ToJsonString());
            Verb("deploy-parent", manifest).ShouldBe((0, "", ""));
            JsonNode.Parse(File.ReadAllText(state))!["oldDeployed"]!.GetValue<bool>().ShouldBeTrue("the fake's save reaches the state file");
            var jq = Path.Combine(root.FullName, "host-jq.manifest.json");
            File.WriteAllText(jq, new JsonObject { ["sourceSha"] = sha, ["runId"] = "c727aaaaaaaaaaaa", ["mode"] = "check", ["phase"] = "deploy-temp", ["selectedPhase"] = "all", ["evidenceRoot"] = root.FullName }.ToJsonString());
            var proof = Verb("host-jq-prerequisite", jq);
            (proof.Exit, proof.Err).ShouldBe((0, ""));
            JsonNode.Parse(proof.Out)!["outcome"]!.GetValue<string>().ShouldBe("existing");
            Verb("deploy-parent").Exit.ShouldBe(1, "a case without its manifest is the fake's failed exit");
            File.ReadAllLines(trace).Select(l => JsonNode.Parse(l)!["kind"]!.GetValue<string>()).ShouldBe(new[] { "lock-request", "case", "prerequisite" });
            File.Delete(state);
            Verb("recycle-discover").Exit.ShouldBe(1, "no state file is the fake's failed exit");
        }
        finally { root.Delete(true); }
    }

    // A harness adapter is an executable: the seams pass --case and --manifest, read its streams and exit code, and hold the rollout
    // lock on its C1008_LOCKED line until Release writes a line.
    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public void An_adapter_is_started_directly_with_the_case_and_manifest()
    {
        var root = Directory.CreateTempSubdirectory("u68-adapter-");
        try
        {
            var log = Path.Combine(root.FullName, "argv.log");
            string adapter;
            if (OperatingSystem.IsWindows())
            {
                adapter = Path.Combine(root.FullName, "adapter.cmd");
                File.WriteAllText(adapter, "@echo off\r\n>>\"" + log + "\" echo %*\r\nif \"%2\"==\"rollout-lock\" (echo C1008_LOCKED& set /p line=& exit /b 0)\r\n"
                    + "if \"%2\"==\"recycle-discover\" (echo c1008-pending& exit /b 0)\r\n>&2 echo adapter-error\r\necho adapter-out %2\r\nexit /b 3\r\n");
            }
            else
            {
                adapter = Path.Combine(root.FullName, "adapter.sh");
                File.WriteAllText(adapter, "#!/bin/sh\nprintf '%s\\n' \"$*\" >> '" + log + "'\ncase \"$2\" in\n rollout-lock) echo C1008_LOCKED; read line; exit 0 ;;\n"
                    + " recycle-discover) echo c1008-pending; exit 0 ;;\nesac\necho adapter-error >&2\necho \"adapter-out $2\"\nexit 3\n");
                File.SetUnixFileMode(adapter, (UnixFileMode)493);
            }
            var stub = new Server2RollingStubOs(n => n == "C727_TEST_VERIFY_STUB" ? adapter : null!, new Server2RollingOs(root.FullName), root.FullName);
            var discover = stub.RecycleDiscover(new Server2RollingCall());
            (discover.Exit, discover.Stdout.Trim()).ShouldBe((0, "c1008-pending"));
            var manifest = Path.Combine(root.FullName, "deploy-parent.manifest.json");
            File.WriteAllText(manifest, "{}");
            var output = new StringWriter();
            var error = new StringWriter();
            stub.HostCase("deploy-parent", manifest, output, error).ShouldBe(3);
            (output.ToString().Trim(), error.ToString().Trim()).ShouldBe(("adapter-out deploy-parent", "adapter-error"));
            stub.EnterRolloutLock().Release();
            File.ReadAllLines(log).Select(l => l.Trim()).ShouldBe(new[]
            {
                "--case recycle-discover", "--case deploy-parent --manifest " + manifest, "--case rollout-lock",
            });
        }
        finally { root.Delete(true); }
    }

    // No seam starts pwsh any more, and the two PowerShell fakes are gone.
    [Test]
    public void No_seam_starts_pwsh()
    {
        var source = File.ReadAllText(Path.Combine(C590Harness.RepoRoot, "scripts", "lib", "Antiphon.Nightly", "Server2RollingStubOs.cs"));
        source.ShouldNotContain("new ProcessStartInfo(\"pwsh\")");
        source.ShouldNotContain("\"-File\"");
        foreach (var gone in new[] { "c727-fake-http.ps1", "c727-fake-verify.ps1" })
            File.Exists(Path.Combine(C590Harness.RepoRoot, "scripts", "fixtures", gone)).ShouldBeFalse(gone);
        foreach (var harness in new[] { "c1609-wrapper-cases.mjs", "c1609-wrapper-v2-cases.mjs", "c1008-recycle-real-cases.mjs" })
        {
            var code = File.ReadAllLines(Path.Combine(C590Harness.RepoRoot, "scripts", "fixtures", harness)).Where(l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal));
            code.Where(l => l.Contains(".ps1", StringComparison.Ordinal) || l.Contains("pwsh", StringComparison.Ordinal)).ShouldBeEmpty(harness);
        }
    }
}
