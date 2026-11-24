using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0644 V-2. The delegate command (Antiphon.Nightly.DelegateCli, which replaced scripts/delegate.ps1 in unit 28 of
/// the 2026-11-22 plan) in process against a loopback stub. An
/// omitted workspace switch stays omitted on the wire - the server's fresh default (V-1) is what
/// gives it meaning, and an existing-agent pin must stay distinguishable from an explicit
/// <c>--worktree</c>. Two workspace switches are ambiguous and never POST. StartRef and Interim
/// Code no longer demand a spelled-out <c>--worktree</c>: the default already is one.
/// </summary>
[Category("Integration")]
public sealed class DelegateScriptWorkspaceDefaultTests
{
    private const string FullSha = "2be807d0ed2633a7a9bd9846c3755e1f0019fbd6";
    private static readonly Guid Subject = Guid.Parse("c6440000-1111-2222-3333-4444444444aa");
    private static readonly Guid Baseline = Guid.Parse("c6440000-1111-2222-3333-4444444444bb");

    [Test]
    public async Task DefaultUsesServerDefault()
    {
        foreach (var (row, args) in new (string, string[])[]
                 {
                     ("worker", ["--role", "Code"]),
                     ("orchestrator", ["--orchestrator"]),
                 })
        {
            using var server = new DelegateCreateStubApi();
            var run = await DelegateScriptRunner.RunAsync(server.BaseUrl, ["create", .. args, "--goal", "fresh work"]);

            run.ExitCode.ShouldBe(0, $"{row}: {run.Output}");
            server.RequestCount.ShouldBe(1, row);
            server.LastBody.ShouldNotBeNull(row).RootElement
                .TryGetProperty("workspace", out _).ShouldBeFalse(row + ": omission is left to the server's fresh default");
        }
    }

    [Test]
    public async Task ExplicitModesRoundTrip()
    {
        foreach (var (switchName, expected) in new[]
                 {
                     ("--worktree", "Worktree"),
                     ("--shared", "Shared"),
                     ("--read-only", "ReadOnly"),
                 })
        {
            using var server = new DelegateCreateStubApi();
            var run = await DelegateScriptRunner.RunAsync(server.BaseUrl,
                "create", "--role", "Review", switchName, "--goal", "explicit " + expected);

            run.ExitCode.ShouldBe(0, $"{switchName}: {run.Output}");
            server.RequestCount.ShouldBe(1, switchName);
            server.LastBody.ShouldNotBeNull(switchName).RootElement
                .GetProperty("workspace").GetString().ShouldBe(expected, switchName);
        }
    }

    [Test]
    public async Task ConflictingSwitchesNeverPost()
    {
        foreach (var pair in new[]
                 {
                     new[] { "--worktree", "--shared" },
                     new[] { "--shared", "--read-only" },
                     new[] { "--worktree", "--read-only" },
                     new[] { "--worktree", "--shared", "--read-only" },
                 })
        {
            var row = string.Join(" ", pair);
            using var server = new DelegateCreateStubApi();
            var run = await DelegateScriptRunner.RunAsync(server.BaseUrl,
                ["create", "--role", "Code", .. pair, "--goal", "ambiguous"]);

            run.ExitCode.ShouldNotBe(0, $"{row}: {run.Output}");
            run.Output.ShouldContain("workspace_switch_conflict", Case.Sensitive, row);
            server.RequestCount.ShouldBe(0, row + ": an ambiguous workspace must never reach the server");
        }
    }

    [Test]
    public async Task StartRefAcceptsOmittedWorktree()
    {
        using var server = new DelegateCreateStubApi();
        var run = await DelegateScriptRunner.RunAsync(server.BaseUrl,
            "create", "--role", "Code", "--start-ref", FullSha, "--goal", "continue from a retained commit");

        run.ExitCode.ShouldBe(0, run.Output);
        server.RequestCount.ShouldBe(1);
        var body = server.LastBody.ShouldNotBeNull().RootElement;
        body.GetProperty("worktreeBaseRequestedRef").GetString().ShouldBe(FullSha);
        body.GetProperty("workspace").GetString().ShouldBe("Worktree");

        // The explicit incompatible modes are still refused locally.
        foreach (var mode in new[] { "--shared", "--read-only" })
        {
            using var refusing = new DelegateCreateStubApi();
            var refused = await DelegateScriptRunner.RunAsync(refusing.BaseUrl,
                "create", "--role", "Code", mode, "--start-ref", FullSha, "--goal", "refused");
            refused.ExitCode.ShouldNotBe(0, refused.Output);
            refused.Output.ShouldContain("worktree_start_ref_mode");
            refusing.RequestCount.ShouldBe(0, mode);
        }
    }

    [Test]
    public async Task InterimCodeAcceptsOmittedWorktree()
    {
        var temp = Directory.CreateTempSubdirectory("c644-selection").FullName;
        var selectionFile = Path.Combine(temp, "c644-selection.json");
        await File.WriteAllTextAsync(selectionFile,
            $$"""{"artifactPath":"docs/plans/c644.md","artifactCommitSha":"{{FullSha}}","section":"S1"}""");
        string[] interim =
        [
            "--verification-round", "Interim", "--verification-subject", Subject.ToString("D"),
            "--verification-baseline-outcome", Baseline.ToString("D"), "--verification-selection-file", selectionFile,
        ];

        using var server = new DelegateCreateStubApi();
        var run = await DelegateScriptRunner.RunAsync(server.BaseUrl, ["create", "--role", "Code", .. interim, "--goal", "interim code"]);

        run.ExitCode.ShouldBe(0, run.Output);
        server.RequestCount.ShouldBe(1);
        var body = server.LastBody.ShouldNotBeNull().RootElement;
        body.GetProperty("verificationRound").GetString().ShouldBe("Interim");
        body.GetProperty("workspace").GetString().ShouldBe("Worktree");

        // Interim Code with explicit Shared, and Interim Review without -ReadOnly, stay refused.
        foreach (var (row, args) in new (string, string[])[]
                 {
                     ("code-shared", ["--role", "Code", "--shared"]),
                     ("review-default", ["--role", "Review"]),
                 })
        {
            using var refusing = new DelegateCreateStubApi();
            var refused = await DelegateScriptRunner.RunAsync(refusing.BaseUrl, ["create", .. args, .. interim, "--goal", "refused"]);
            refused.ExitCode.ShouldNotBe(0, $"{row}: {refused.Output}");
            refused.Output.ShouldContain("verification_round_role", Case.Sensitive, row);
            refusing.RequestCount.ShouldBe(0, row);
        }

        try { Directory.Delete(temp, recursive: true); } catch (IOException) { }
    }

    [Test]
    public async Task PinsRemainDistinguishable()
    {
        foreach (var (row, args, field) in new (string, string[], string)[]
                 {
                     ("standing-agent", ["--role", "Code", "--agent", "nightwatch"], "agent"),
                     ("on-agent", ["--role", "Code", "--on-agent", "1234abcd"], "followUpOnTask"),
                 })
        {
            using var server = new DelegateCreateStubApi();
            var run = await DelegateScriptRunner.RunAsync(server.BaseUrl, ["create", .. args, "--goal", "existing process"]);

            run.ExitCode.ShouldBe(0, $"{row}: {run.Output}");
            server.RequestCount.ShouldBe(1, row);
            var body = server.LastBody.ShouldNotBeNull(row).RootElement;
            body.TryGetProperty(field, out _).ShouldBeTrue(row);
            body.TryGetProperty("workspace", out _).ShouldBeFalse(
                row + ": an existing-agent selection must not be turned into an explicit -Worktree");
        }

        // -Runner is an explicit Worktree shape and still says so.
        using (var server = new DelegateCreateStubApi())
        {
            var run = await DelegateScriptRunner.RunAsync(server.BaseUrl,
                "create", "--role", "Code", "--kind", "Grok", "--runner", "server2", "--goal", "remote");
            run.ExitCode.ShouldBe(0, run.Output);
            server.LastBody.ShouldNotBeNull().RootElement.GetProperty("workspace").GetString().ShouldBe("Worktree");
        }
    }
}
