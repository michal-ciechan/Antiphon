using System.Text;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Checkpoints;

[Category("Unit")]
[ParallelLimiter<CheckpointToolSpawnLimit>]
public sealed class CheckpointFixtureGitEncodingTests : CheckpointTestBase
{
    [Test]
    [Arguments(437)]
    [Arguments(850)]
    [Timeout(30_000)]
    public void fixture_git_stdout_keeps_utf8_under_an_oem_default(int codePage, CancellationToken cancellationToken)
    {
        var root = Directory.CreateDirectory(Path.Combine(TempDir(), "repo")).FullName;
        CheckpointFixtureProcess.Git(root, "init", "-q");
        const string path = "dir/caf\u00e9.txt";
        Directory.CreateDirectory(Path.Combine(root, "dir"));
        File.WriteAllText(Path.Combine(root, path), "fixture");
        CheckpointFixtureProcess.Git(root, "add", "-A");

        var start = CheckpointFixtureProcess.CreateGitStartInfo(root, environment: null, "ls-files", "-z");
        // Simulate the decoder .NET would pick for an unset encoding on a legacy console.
        var oemEncoding = CodePagesEncodingProvider.Instance.GetEncoding(codePage)
            ?? throw new InvalidOperationException($"Code page {codePage} is required for the console encoding witness");
        start.StandardOutputEncoding ??= oemEncoding;
        var output = CheckpointFixtureProcess.Run(start, 10_000, cancellationToken);
        output.Exit.ShouldBe(0, output.Stderr);
        output.Stdout.ShouldBe(path + "\0");
    }
}
