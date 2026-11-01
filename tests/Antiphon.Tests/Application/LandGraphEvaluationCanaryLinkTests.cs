using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

public sealed partial class LandGraphEvaluationTests
{
    // The copy worker refuses a tracked docs directory link before it writes through it.
    [Test]
    [Timeout(300_000)]
    public async Task C1483_TrackedDocsLinkRefusesBeforeWrite()
    {
        var scratch = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "c1483-out-" + Guid.NewGuid().ToString("N")[..12])).FullName;
        var outside = Path.Combine(scratch, "outside");
        Directory.CreateDirectory(outside);
        var outsideFile = Path.Combine(outside, "o.md");
        await File.WriteAllTextAsync(outsideFile, "outside-original");
        var before = await File.ReadAllBytesAsync(outsideFile);
        var listing = Names(outside);
        try
        {
            await using var repo = await AuditRepo.CreateAsync(new()
            {
                ["README.md"] = "# root\n",
                ["App/Program.cs"] = "System.Console.WriteLine();\n",
                ["App/App.csproj"] = SdkProject(""),
            }, links: new() { ["docs"] = outside });

            var run = await repo.RunToolAsync();

            run.ExitCode.ShouldBe(2, run.Text);
            run.Text.ShouldContain("canary destination escapes through a link", Case.Sensitive);
            run.Lines.ShouldNotContain("approved", run.Text);
            (await File.ReadAllBytesAsync(outsideFile)).ShouldBe(before);
            Names(outside).ShouldBe(listing);
            Directory.Exists(CanaryDirectory(run)).ShouldBeFalse(run.Text);
        }
        finally
        {
            if (Directory.Exists(scratch)) StartRefGit.DeleteDirectory(scratch);
        }
    }

    // A tracked file link and a directory link that stay inside the repository still approve.
    [Test]
    [Timeout(300_000)]
    public async Task C1483_InsideFileAndDirectoryLinksApprove()
    {
        await using var repo = await AuditRepo.CreateAsync(new()
        {
            ["README.md"] = "# root\n",
            ["docs/notes.md"] = "# notes\n",
            ["App/Program.cs"] = "System.Console.WriteLine();\n",
            ["App/sub/Helper.cs"] = "class Helper { }\n",
            ["App/App.csproj"] = SdkProject(""),
        }, links: new() { ["App/alias.cs"] = "Program.cs", ["App/extra"] = "sub" });

        var run = await repo.RunToolAsync();

        run.ExitCode.ShouldBe(0, run.Text);
        run.Lines.ShouldContain("approved", run.Text);
        run.Text.ShouldNotContain("refused:");
        run.Text.ShouldNotContain("evaluation-failed:");
        run.Text.ShouldNotContain("canary source became a link");
        repo.AssertUntouched();
        Directory.Exists(CanaryDirectory(run)).ShouldBeFalse(run.Text);
    }

    // Classification saw an ordinary file. The temp marker replaces it with a link before the worker re-check.
    [Test]
    [Timeout(300_000)]
    public async Task C1483_SourceRewrittenToLinkBeforeCopyRefuses()
    {
        var scratch = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "c1483-race-" + Guid.NewGuid().ToString("N")[..12])).FullName;
        var secret = Path.Combine(scratch, "secret.txt");
        await File.WriteAllTextAsync(secret, "secret-bytes");
        var before = await File.ReadAllBytesAsync(secret);
        try
        {
            await using var repo = await AuditRepo.CreateAsync(new()
            {
                ["README.md"] = "# root\n",
                ["docs/notes.md"] = "# notes\n",
                ["App/Program.cs"] = "System.Console.WriteLine();\n",
                ["App/App.csproj"] = SdkProject(""),
            });
            await File.WriteAllTextAsync(
                Path.Combine(repo.Root, ".antiphon-landgraph-copy-race"),
                "App/Program.cs|" + secret);

            var run = await repo.RunToolAsync();

            run.ExitCode.ShouldBe(2, run.Text);
            run.Text.ShouldContain("canary source became a link: ", Case.Sensitive);
            run.Lines.ShouldNotContain("approved", run.Text);
            (await File.ReadAllBytesAsync(secret)).ShouldBe(before);
            var source = repo.Full("App/Program.cs");
            File.GetAttributes(source).HasFlag(FileAttributes.ReparsePoint).ShouldBeTrue(source);
            new FileInfo(source).LinkTarget.ShouldBe(secret);
            Names(scratch).ShouldBe(new[] { "secret.txt" });
        }
        finally
        {
            if (Directory.Exists(scratch)) StartRefGit.DeleteDirectory(scratch);
        }
    }

    private static string[] Names(string directory) =>
        Directory.GetFileSystemEntries(directory).Select(Path.GetFileName)!.Order(StringComparer.Ordinal).ToArray()!;

    private static string CanaryDirectory(ToolRun run)
    {
        var line = run.Lines.Single(item => item.StartsWith("canary=", StringComparison.Ordinal));
        return line["canary=".Length..];
    }
}
