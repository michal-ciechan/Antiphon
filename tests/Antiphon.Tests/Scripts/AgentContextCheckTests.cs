using System.IO.Abstractions.TestingHelpers;
using System.Text;
using Antiphon.Nightly;
using Antiphon.Tests.Application;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-0254: the check-agent-context command (AgentContextCheck.Run), in process on a MockFileSystem. It replaced
/// scripts/check-agent-context.ps1 (unit 25 of the 2026-11-22 plan), which had no test of its own; the bootstrap-check command
/// (BootstrapCheck, unit 38) runs it in process. The last case reads the real AGENTS.md, as bootstrap-check does.
/// </summary>
[Category("Unit")]
public sealed class AgentContextCheckTests
{
    private sealed record ContextCheckRun(int Exit, string Output, string Error);

    private static ContextCheckRun Check(MockFileSystem files, string repo)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var exit = AgentContextCheck.Run(AgentContextCheckOptions.Parse(["--repo", repo], null), NightlyHost.Production(output, error), files);
        return new ContextCheckRun(exit, output.ToString().Replace("\r\n", "\n"), error.ToString().Trim());
    }

    private static (MockFileSystem Files, string Repo) Checkout(byte[]? agents)
    {
        var files = new MockFileSystem();
        var repo = files.Path.Combine(files.Path.GetTempPath(), "checkout");
        files.AddDirectory(repo);
        if (agents != null) files.AddFile(files.Path.Combine(repo, "AGENTS.md"), new MockFileData(agents));
        return (files, repo);
    }

    [Test]
    public void Within_the_target_prints_the_sizes_and_each_section_and_passes()
    {
        var text = "# Agents\n\nintro\n\n## First\n\nalpha\n\n## Second café\n\nbeta\n";
        var (files, repo) = Checkout(Encoding.UTF8.GetBytes(text));
        var run = Check(files, repo);

        run.Exit.ShouldBe(0, run.Error);
        var size = Encoding.UTF8.GetByteCount(text);
        run.Output.ShouldStartWith("AGENTS.md raw UTF-8 bytes: " + size + "\nDelivery target: <= 24576 bytes\nHard ceiling:    <= 30720 bytes\n\nSection sizes (raw UTF-8 bytes):\n");
        // "## First\n\nalpha\n\n" is 17 bytes and "## Second café\n\nbeta\n" 22 (the e-acute is two): Section left-aligned, Bytes right-aligned.
        run.Output.ShouldContain("\n\nSection     Bytes\n-------     -----\nFirst          17\nSecond café    22\n\n");
        run.Output.TrimEnd('\n').Split('\n').Last().ShouldBe("PASS: AGENTS.md is within the 24576-byte delivery target.");
        run.Error.ShouldBeEmpty();
    }

    [Test]
    public void Over_the_target_and_over_the_ceiling_fail_with_the_overage()
    {
        var over = "## Big\n" + new string('x', AgentContextCheck.TargetBytes);
        var (files, repo) = Checkout(Encoding.UTF8.GetBytes(over));
        var target = Check(files, repo);
        target.Exit.ShouldBe(1);
        var size = Encoding.UTF8.GetByteCount(over);
        target.Error.ShouldBe("AGENTS.md is " + size + " raw UTF-8 bytes: above the 24576-byte delivery target by " + (size - 24576) + " bytes (still below the 30720-byte hard ceiling).");
        target.Output.ShouldNotContain("PASS");

        var huge = "## Huge\n" + new string('x', AgentContextCheck.HardCeilingBytes);
        (files, repo) = Checkout(Encoding.UTF8.GetBytes(huge));
        var ceiling = Check(files, repo);
        ceiling.Exit.ShouldBe(1);
        ceiling.Error.ShouldBe("AGENTS.md is " + Encoding.UTF8.GetByteCount(huge) + " raw UTF-8 bytes: above the 30720-byte hard ceiling by "
            + (Encoding.UTF8.GetByteCount(huge) - 30720) + " bytes.");
    }

    [Test]
    public void Missing_invalid_utf8_or_sectionless_files_fail()
    {
        var (files, repo) = Checkout(null);
        var missing = Check(files, repo);
        (missing.Exit, missing.Error).ShouldBe((1, "AGENTS.md was not found at " + files.Path.Combine(repo, "AGENTS.md") + "."));

        (files, repo) = Checkout([0x23, 0x23, 0x20, 0xC3, 0x28]);
        var invalid = Check(files, repo);
        invalid.Exit.ShouldBe(1);
        invalid.Error.ShouldStartWith("AGENTS.md is not valid UTF-8: ");
        invalid.Output.ShouldBeEmpty("nothing is reported for bytes that do not decode");

        (files, repo) = Checkout(Encoding.UTF8.GetBytes("# Title only\n\nno level-two section\n"));
        var flat = Check(files, repo);
        (flat.Exit, flat.Error).ShouldBe((1, "AGENTS.md has no level-two sections to report."));
    }

    [Test]
    public void The_repo_defaults_to_the_cli_checkout()
    {
        AgentContextCheckOptions.Parse([], "checkout").Repo.ShouldBe("checkout");
        AgentContextCheckOptions.Parse(["--repo", "other"], "checkout").Repo.ShouldBe("other");
        Should.Throw<NightlyUsageException>(() => AgentContextCheckOptions.Parse([], null)).Message.ShouldContain("--repo");
    }

    [Test]
    public void The_real_AGENTS_md_is_within_its_budget()
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var exit = AgentContextCheck.Run(new AgentContextCheckOptions { Repo = DelegateScriptRunner.RepoRoot }, NightlyHost.Production(output, error),
            new System.IO.Abstractions.FileSystem());
        exit.ShouldBe(0, error.ToString());
        File.ReadAllBytes(Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", "lib", "Antiphon.Nightly", "AgentContextCheck.cs")).Count(b => b > 127).ShouldBe(0, "ASCII only");
        File.Exists(Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", "check-agent-context.ps1")).ShouldBeFalse("the check is a command");
        File.Exists(Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", "bootstrap-check.ps1")).ShouldBeFalse("bootstrap-check is a command");
        File.ReadAllText(Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", "lib", "Antiphon.Nightly", "BootstrapCheck.cs"))
            .ShouldContain("AgentContextCheck.Run(new AgentContextCheckOptions { Repo = root }, host, files)");
    }
}
