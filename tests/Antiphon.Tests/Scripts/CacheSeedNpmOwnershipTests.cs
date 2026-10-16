using System.Diagnostics;
using System.Text;
using Antiphon.Tests.Application;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

// Runs the production seed and writer dispatch with native Linux uid/mode semantics.
// Docker transport and unrelated donor/import checks are modeled; npm and deletion are real.
[Category("Integration")]
public sealed class CacheSeedNpmOwnershipTests
{
    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public void Volume_seed_verifies_as_writer_and_removes_npm_logs()
    {
        var output = Run("success");
        output.ShouldContain("ROOT_CONTROL=permission-denied");
        output.ShouldContain("NPM_LOG_OWNER=1654:1654");
        output.ShouldContain("SEED_EXIT=0");
        output.ShouldContain("RESULT=true:");
        output.ShouldContain("READY=present");
        output.ShouldContain("NPM_CACHE=absent");
        output.ShouldContain("ROOT_LEFTOVERS=0");
        output.ShouldNotContain("ROOT_CREATED=yes");
    }

    [Test]
    [Arguments("verify")]
    [Arguments("cleanup")]
    [ParallelLimiter<ProcessSpawnLimit>]
    public void Volume_seed_failure_cleans_stage_and_never_accepts(string fault)
    {
        var output = Run(fault);
        output.ShouldContain("NPM_LOG_OWNER=1654:1654");
        output.ShouldContain("FAULT=" + fault);
        output.ShouldContain("SEED_EXIT=2");
        output.ShouldContain("RESULT=false:NpmStagedIntegrityFailed");
        output.ShouldContain("READY=absent");
        output.ShouldContain("STAGES=0");
        output.ShouldContain("ROOT_LEFTOVERS=0");
        output.ShouldNotContain("RESULT=true:");
        output.ShouldNotContain("ROOT_CREATED=yes");
    }

    private static string Run(string fault)
    {
        var repo = DelegateScriptRunner.RepoRoot;
        var source = File.ReadAllText(Path.Combine(repo, "scripts", "c590-remote.sh"))
            .Replace("\r\n", "\n");
        var functions = new[] { "c849_seed", "c849_seed_failure", "c849_payload_fs",
            "c849_fixture_helper_acquire", "c849_fixture_helper_exec" };
        var script = string.Join('\n', functions.Select(name => Extract(source, name))) +
            "\nfault='" + fault + "'\n" + File.ReadAllText(Path.Combine(repo, "tests",
                "Antiphon.Tests", "Scripts", "Fixtures", "cache-seed-npm-ownership.sh"));

        // WSL's native /tmp preserves ownership; Windows-mounted repository paths do not.
        // Root is used only by this isolated test to create/drop to uid 1654 and model old npm.
        var start = OperatingSystem.IsWindows()
            ? new ProcessStartInfo("wsl.exe") { ArgumentList = { "-u", "root", "-e", "bash", "-s" } }
            : new ProcessStartInfo("bash") { ArgumentList = { "-s" } };
        start.UseShellExecute = false;
        start.RedirectStandardInput = true;
        start.RedirectStandardOutput = true;
        start.RedirectStandardError = true;
        start.StandardInputEncoding = new UTF8Encoding(false);
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        process.StandardInput.Write(script.Replace("\r\n", "\n"));
        process.StandardInput.Close();
        if (!process.WaitForExit(60_000))
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit();
            throw new TimeoutException("Native npm ownership harness did not finish in 60s.");
        }
        var output = stdout.GetAwaiter().GetResult();
        var errors = stderr.GetAwaiter().GetResult();
        Console.WriteLine(output);
        Console.WriteLine(errors);
        process.ExitCode.ShouldBe(0, output + errors);
        output.ShouldContain("HARNESS_COMPLETE");
        return output;
    }

    private static string Extract(string source, string name)
    {
        var lines = source.Split('\n');
        var first = Array.FindIndex(lines, line => line == name + "() {");
        first.ShouldBeGreaterThanOrEqualTo(0, name);
        var last = Array.FindIndex(lines, first + 1, line => line == "}");
        last.ShouldBeGreaterThan(first, name);
        return string.Join('\n', lines[first..(last + 1)]);
    }
}
