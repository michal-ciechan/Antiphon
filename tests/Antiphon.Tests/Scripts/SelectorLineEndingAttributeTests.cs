using System.Diagnostics;
using Antiphon.Tests.Infrastructure;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

// C1313 reads these working-tree scripts and throws when any byte is CR.
// scripts/test-test-selection.ps1 New-C1313SelectorCandidate and New-C1313CrlfScriptRoot.
// A Windows qualification clone leaves core.autocrlf at the machine default, and
// text=auto then checks the files out as CRLF unless eol=lf is set.
[Category("Unit")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class SelectorLineEndingAttributeTests
{
    private static readonly string[] LiveLfScripts =
    [
        "scripts/select-test-rosters.ps1",
        "scripts/lib/test-selection.ps1",
        "scripts/lib/nightly-common.ps1",
        "scripts/lib/nightly-policy.ps1",
        "scripts/lib/checkpoint-process-runner.ps1",
        "scripts/lib/checkpoint-file-reader.ps1",
    ];

    private const string PreservedBytes =
        "tests/Antiphon.Tests/Infrastructure/Fixtures/c504-deploy-am-service-baseline.ps1";

    [Test]
    public async Task C1313_live_scripts_are_forced_lf()
    {
        var root = DockerStackDocuments.RepoRoot;
        var fields = (await Git(root, ["check-attr", "-z", "eol", "--", .. LiveLfScripts]))
            .Split('\0', StringSplitOptions.RemoveEmptyEntries);
        fields.Length.ShouldBe(LiveLfScripts.Length * 3, "c1313-eol: complete git check-attr response");
        var problems = new List<string>();
        for (var i = 0; i < LiveLfScripts.Length; i++)
        {
            var offset = i * 3;
            fields[offset].ShouldBe(LiveLfScripts[i]);
            fields[offset + 1].ShouldBe("eol");
            if (!string.Equals(fields[offset + 2], "lf", StringComparison.Ordinal))
                problems.Add(LiveLfScripts[i] + " eol=" + fields[offset + 2]);
        }

        problems.ShouldBeEmpty(string.Join("\n", problems));

        var preserved = (await Git(root, "check-attr", "-z", "text", "eol", "--", PreservedBytes))
            .Split('\0', StringSplitOptions.RemoveEmptyEntries);
        preserved.ShouldBe([PreservedBytes, "text", "unset", PreservedBytes, "eol", "unspecified"]);
    }

    private static async Task<string> Git(string root, params string[] arguments)
    {
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = root,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var key in new[] { "GIT_DIR", "GIT_WORK_TREE", "GIT_INDEX_FILE" })
            start.Environment.Remove(key);
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("git did not start");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try
        {
            await process.WaitForExitAsync(deadline.Token);
        }
        finally
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            await Task.WhenAll(stdout, stderr);
        }

        process.ExitCode.ShouldBe(0, "c1313-eol-git: " + string.Join(" ", arguments) + "\n" + await stderr);
        return await stdout;
    }
}
