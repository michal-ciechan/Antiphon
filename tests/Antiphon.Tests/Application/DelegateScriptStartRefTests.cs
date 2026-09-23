using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0613 V-1/R-1. The REAL <c>scripts/delegate.ps1</c> under pwsh: what an orchestrator types
/// has to arrive as an exact selector, and the combinations the server can never admit have to be
/// refused before any POST — a queued task with a base nobody asked for is the failure this card
/// exists to stop.
/// </summary>
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
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
            "-Role", "Code", "-Worktree", "-StartRef", selector, "-Goal", "continue the sibling's work");

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
            "-Role", "Code", "-Worktree", "-Goal", "ordinary work");

        run.ExitCode.ShouldBe(0, run.Output);
        server.LastBody.ShouldNotBeNull().RootElement
            .TryGetProperty("worktreeBaseRequestedRef", out _).ShouldBeFalse();
    }

    [Test]
    [Arguments("no-worktree", "-Role", "Code")]
    [Arguments("shared", "-Role", "Code", "-Shared")]
    [Arguments("read-only", "-Role", "Code", "-ReadOnly")]
    [Arguments("on-agent", "-Role", "Code", "-Worktree", "-OnAgent", "1234abcd")]
    [Arguments("standing-agent", "-Role", "Code", "-Worktree", "-Agent", "nightwatch")]
    public async Task C613_InvalidStartRefStopsBeforePost(string shape, params string[] leading)
    {
        using var server = new DelegateCreateStubApi();
        var args = new List<string>(leading);
        args.AddRange(["-StartRef", FullSha, "-Goal", $"refused: {shape}"]);

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
            ? new[] { "-Role", "Code", "-Worktree", "-RepairSource", owner, "-StartRef", FullSha, "-Goal", "x" }
            : ["-Role", "Mutation", "-Worktree", "-SourceLanding", owner, "-StartRef", FullSha, "-Goal", "x"];

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
            "-Role", "Code", "-Worktree", "-StartRef", "  padded  ", "-Goal", "refused upstream");

        run.ExitCode.ShouldNotBe(0);
        run.Output.ShouldContain("worktree_start_ref_invalid");
        server.RequestCount.ShouldBe(1);
        server.LastBody.ShouldNotBeNull().RootElement
            .GetProperty("worktreeBaseRequestedRef").GetString().ShouldBe("  padded  ");
    }
}
