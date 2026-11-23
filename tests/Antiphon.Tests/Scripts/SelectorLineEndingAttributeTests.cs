using System.Diagnostics;
using Antiphon.Tests.Infrastructure;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

// C1313: the selector identity (TestSelection.SelectorFiles) is hashed as LF text, and the running selector embeds
// these sources (Antiphon.Nightly.csproj) to compare with a candidate's blobs. A Windows qualification clone leaves
// core.autocrlf at the machine default, and text=auto then checks the files out as CRLF unless eol=lf is set: the
// comparison normalises line endings, and eol=lf keeps the embedded bytes the committed ones too. Until 2026-11-22 the
// identity was scripts/select-test-rosters.ps1 and scripts/lib/test-selection.ps1 (now *.ps1 eol=lf covers every script).
[Category("Unit")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class SelectorLineEndingAttributeTests
{
    // TestSelection.SelectorFiles, as literals the derived impact index reads.
    private static readonly string[] LiveLfScripts =
    [
        "scripts/lib/Antiphon.Nightly/TestSelection.cs",
        "scripts/lib/Antiphon.Nightly/TestSelectionCatalog.cs",
        "scripts/lib/Antiphon.Nightly/TestSelectionGit.cs",
        "scripts/lib/Antiphon.Nightly/TestSelectionJson.cs",
    ];

    // ImpactedClasses.SelectorFiles (unit 4 of the remaining-ps1 plan): the release evidence binds these embedded sources
    // to their committed blobs byte for byte, so the working files must be the LF blobs.
    private static readonly string[] ImpactedLfSources =
    [
        "scripts/lib/Antiphon.Nightly/ImpactedClasses.cs",
        "scripts/lib/Antiphon.Nightly/ImpactedClassesInputs.cs",
        "scripts/lib/Antiphon.Nightly/ReleaseImpactEvidence.cs",
    ];

    private const string PreservedBytes =
        "tests/Antiphon.Tests/Infrastructure/Fixtures/c504-deploy-am-service-baseline.ps1";

    [Test]
    public async Task C1313_live_scripts_are_forced_lf()
    {
        LiveLfScripts.ShouldBe(Antiphon.Nightly.TestSelection.SelectorFiles, "c1313-eol: the selector identity");
        ImpactedLfSources.ShouldBe(Antiphon.Nightly.ImpactedClasses.SelectorFiles, "c1313-eol: the impacted-classes identity");
        string[] lf = [.. LiveLfScripts, .. ImpactedLfSources];
        var root = DockerStackDocuments.RepoRoot;
        var fields = (await Git(root, ["check-attr", "-z", "eol", "--", .. lf]))
            .Split('\0', StringSplitOptions.RemoveEmptyEntries);
        fields.Length.ShouldBe(lf.Length * 3, "c1313-eol: complete git check-attr response");
        var problems = new List<string>();
        for (var i = 0; i < lf.Length; i++)
        {
            var offset = i * 3;
            fields[offset].ShouldBe(lf[i]);
            fields[offset + 1].ShouldBe("eol");
            if (!string.Equals(fields[offset + 2], "lf", StringComparison.Ordinal))
                problems.Add(lf[i] + " eol=" + fields[offset + 2]);
        }

        problems.ShouldBeEmpty(string.Join("\n", problems));

        var preserved = (await Git(root, "check-attr", "-z", "text", "eol", "--", PreservedBytes))
            .Split('\0', StringSplitOptions.RemoveEmptyEntries);
        preserved.ShouldBe([PreservedBytes, "text", "unset", PreservedBytes, "eol", "unset"]);
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
