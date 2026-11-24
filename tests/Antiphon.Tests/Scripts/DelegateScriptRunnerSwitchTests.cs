using System.Text.Json;
using Antiphon.Nightly;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

// CARD-0604 D-15. --runner is refused locally, before any POST, for the shapes the runner has no design for. Refusing in the
// command keeps the operator from waiting on a 422 round trip for a combination that could never have been admitted. The
// delegate command (Antiphon.Nightly.DelegateCli, which replaced scripts/delegate.ps1 in unit 28 of the 2026-11-22 plan) runs
// in process on a recording transport.
[Category("Unit")]
public sealed class DelegateScriptRunnerSwitchTests
{
    [Test]
    public void Runner_switch_refuses_shared_and_pins()
    {
        // Each refused combination names itself, so the operator is told which flag lost; every refusal is exit 2 before the
        // body is posted.
        foreach (var (extra, needle) in new (string[], string)[]
                 {
                     (["--shared"], "--runner requires a Worktree workspace; --shared and --read-only are refused."),
                     (["--read-only"], "--runner requires a Worktree workspace"),
                     (["--on-agent", "1234abcd"], "--runner cannot be combined with --on-agent or --agent: those continue an existing process."),
                     (["--agent", "nightwatch"], "continue an existing process"),
                     // CARD-0604 Cut B: --source-landing on a runner is supported, but only as a Mutation. Any other role would
                     // reserve an execution the remote lane has no design for.
                     (["--source-landing", Guid.NewGuid().ToString("D")], "--runner with --source-landing requires --role Mutation (CARD-0604 D-19)."),
                 })
        {
            var http = Created();
            var run = DelegateCommandRun.Run(http.Transport, ["create", "--role", "Code", "--goal", "remote", "--runner", "server2", .. extra]);
            run.Exit.ShouldBe(2, run.All);
            run.Error.ShouldContain(needle);
            run.All.ShouldNotContain("--runner cannot be combined with --source-landing", customMessage: "the Cut A blanket refusal is superseded by Cut B");
            http.Calls.ShouldBeEmpty();
        }

        var mutation = Created();
        var allowed = DelegateCommandRun.Run(mutation.Transport,
            ["create", "--role", "Mutation", "--goal", "post-land", "--runner", "server2", "--source-landing", Guid.NewGuid().ToString("D")]);
        allowed.Exit.ShouldBe(0, allowed.All);
        mutation.Calls.ShouldHaveSingleItem();
    }

    [Test]
    public void Runner_switch_sends_a_worktree_task()
    {
        var http = Created();
        var run = DelegateCommandRun.Run(http.Transport, ["create", "--role", "Code", "--goal", "remote", "--runner", "server2"]);

        run.Exit.ShouldBe(0, run.All);
        using var body = JsonDocument.Parse(http.Calls.Single().Body!);
        body.RootElement.GetProperty("runnerId").GetString().ShouldBe("server2");
        // A remote task is always Worktree: the mirror design has nothing else to fast-forward.
        body.RootElement.GetProperty("workspace").GetString().ShouldBe("Worktree");

        // local and desktop name this desktop: the workspace stays the caller's.
        var desktop = Created();
        DelegateCommandRun.Run(desktop.Transport, ["create", "--role", "Code", "--goal", "here", "--shared", "--runner", " Desktop "]).Exit.ShouldBe(0);
        using var local = JsonDocument.Parse(desktop.Calls.Single().Body!);
        local.RootElement.GetProperty("runnerId").GetString().ShouldBe("desktop");
        local.RootElement.GetProperty("workspace").GetString().ShouldBe("Shared");
    }

    [Test]
    public void Runner_is_a_create_only_parameter()
    {
        // A --runner on a land or reply call would be silently ignored otherwise.
        foreach (var args in new[]
                 {
                     new[] { "land", "11111111", "--runner", "server2" },
                     new[] { "reply", "11111111", "answer", "--runner", "server2" },
                     new[] { "status", "11111111", "--runner", "server2" },
                 })
        {
            var http = Created();
            var run = DelegateCommandRun.Run(http.Transport, args);
            run.Exit.ShouldBe(2, run.All);
            run.Error.ShouldContain("--runner does not apply to " + args[0]);
            http.Calls.ShouldBeEmpty();
        }
    }

    private static RecordingOperatorHttp Created() =>
        new(_ => new NightlyReportHttpResponse(201, DelegateCommandRun.Created()));
}
