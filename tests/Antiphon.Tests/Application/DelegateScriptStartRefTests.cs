using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0613 V-1/R-1. The delegate command in process (Antiphon.Nightly.DelegateCli, which replaced scripts/delegate.ps1 in
/// unit 28 of the 2026-11-22 plan): what an orchestrator types
/// has to arrive as an exact selector, and the combinations the server can never admit have to be
/// refused before any POST — a queued task with a base nobody asked for is the failure this card
/// exists to stop.
/// </summary>
[Category("Integration")]
public sealed class DelegateScriptStartRefTests
{
    private const string FullSha = "2be807d0ed2633a7a9bd9846c3755e1f0019fbd6";

    [Test]
    [Arguments("feat/card-task-ac840fc8")]
    [Arguments("commit-tag")]
    [Arguments(FullSha)]
    public async Task C613_StartRefPostsExactSelector(string selector)
    {
        using var server = new DelegateCreateStubApi();
        var run = await DelegateScriptRunner.RunAsync(server.BaseUrl,
            "create", "--role", "Code", "--worktree", "--start-ref", selector, "--goal", "continue the sibling's work");

        run.ExitCode.ShouldBe(0, run.Output);
        var body = server.LastBody.ShouldNotBeNull().RootElement;
        body.GetProperty("worktreeBaseRequestedRef").GetString().ShouldBe(selector);
        body.GetProperty("workspace").GetString().ShouldBe("Worktree");
        // A start ref selects a BASE. It must never invent a merge destination.
        body.TryGetProperty("mergeTargetRef", out _).ShouldBeFalse();
        body.TryGetProperty("repairSourceTaskId", out _).ShouldBeFalse();
        server.RequestCount.ShouldBe(1);
    }

    [Test]
    public async Task C613_OmittedStartRefSendsNoSelector()
    {
        using var server = new DelegateCreateStubApi();
        var run = await DelegateScriptRunner.RunAsync(server.BaseUrl,
            "create", "--role", "Code", "--worktree", "--goal", "ordinary work");

        run.ExitCode.ShouldBe(0, run.Output);
        server.LastBody.ShouldNotBeNull().RootElement
            .TryGetProperty("worktreeBaseRequestedRef", out _).ShouldBeFalse();
    }

    [Test]
    [Arguments("shared", "create", "--role", "Code", "--shared")]
    [Arguments("read-only", "create", "--role", "Code", "--read-only")]
    [Arguments("on-agent", "create", "--role", "Code", "--worktree", "--on-agent", "1234abcd")]
    [Arguments("standing-agent", "create", "--role", "Code", "--worktree", "--agent", "nightwatch")]
    public async Task C613_InvalidStartRefStopsBeforePost(string shape, params string[] leading)
    {
        using var server = new DelegateCreateStubApi();
        var args = new List<string>(leading);
        args.AddRange(["--start-ref", FullSha, "--goal", $"refused: {shape}"]);

        var run = await DelegateScriptRunner.RunAsync(server.BaseUrl, args.ToArray());

        run.ExitCode.ShouldNotBe(0, run.Output);
        run.Output.ShouldContain("worktree_start_ref_mode");
        server.RequestCount.ShouldBe(0, "a locally refusable shape must never reach the server");
        server.LastBody.ShouldBeNull();
    }

    [Test]
    [Arguments("repair-source")]
    [Arguments("source-landing")]
    public async Task C613_StartRefRefusesCompetingStructuredBases(string shape)
    {
        using var server = new DelegateCreateStubApi();
        var owner = Guid.NewGuid().ToString("D");
        var args = shape == "repair-source"
            ? new[] { "create", "--role", "Code", "--worktree", "--repair-source", owner, "--start-ref", FullSha, "--goal", "x" }
            : ["create", "--role", "Mutation", "--worktree", "--source-landing", owner, "--start-ref", FullSha, "--goal", "x"];

        var run = await DelegateScriptRunner.RunAsync(server.BaseUrl, args);

        run.ExitCode.ShouldNotBe(0, run.Output);
        run.Output.ShouldContain("worktree_start_ref_mode");
        server.RequestCount.ShouldBe(0);
    }

    [Test]
    public async Task C613_ServerRefusalPassesThrough()
    {
        // The script does not second-guess the selector's CONTENT; it transports it and surfaces
        // the server's field-specific refusal verbatim.
        using var server = new DelegateCreateStubApi(
            """{"code":"worktree_start_ref_invalid","title":"One or more validation errors occurred."}""", 422);
        var run = await DelegateScriptRunner.RunAsync(server.BaseUrl,
            "create", "--role", "Code", "--worktree", "--start-ref", "  padded  ", "--goal", "refused upstream");

        run.ExitCode.ShouldNotBe(0);
        run.Output.ShouldContain("worktree_start_ref_invalid");
        server.RequestCount.ShouldBe(1);
        server.LastBody.ShouldNotBeNull().RootElement
            .GetProperty("worktreeBaseRequestedRef").GetString().ShouldBe("  padded  ");
    }
}
