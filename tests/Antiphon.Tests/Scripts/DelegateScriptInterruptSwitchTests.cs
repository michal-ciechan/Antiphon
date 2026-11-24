using System.Text.Json;
using Antiphon.Nightly;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-0491 V-4. --interrupt is refine-only and the two statuses print apart. The delegate command (Antiphon.Nightly.DelegateCli,
/// which replaced scripts/delegate.ps1 in unit 28 of the 2026-11-22 plan) runs in process on a recording transport.
/// </summary>
[Category("Unit")]
public sealed class DelegateScriptInterruptSwitchTests
{
    private const string TaskId = "11111111-1111-1111-1111-111111111111";

    [Test]
    public void Interrupt_is_a_refine_only_switch()
    {
        foreach (var args in new[]
                 {
                     new[] { "create", "--role", "Code", "--goal", "x", "--interrupt" },
                     new[] { "reply", TaskId, "the answer", "--interrupt" },
                     new[] { "continue", TaskId, "--interrupt" },
                 })
        {
            var http = Answer("""{"status":"Working"}""");
            var run = DelegateCommandRun.Run(http.Transport, args);
            run.Exit.ShouldBe(2, run.All);
            run.Error.ShouldContain("--interrupt does not apply to " + args[0]);
            http.Calls.ShouldBeEmpty();
        }

        var refine = Answer("""{"status":"Working"}""");
        DelegateCommandRun.Run(refine.Transport, ["refine", TaskId, "steer", "--interrupt"]).Exit.ShouldBe(0);
    }

    [Test]
    public void Interrupt_posts_the_flag_and_a_fresh_request_id()
    {
        var http = Answer("""{"status":"Working","interruptWritten":"written","refinementDelivered":"queued"}""");
        DelegateCommandRun.Run(http.Transport, ["refine", TaskId, "steer it", "--interrupt"]).Exit.ShouldBe(0);
        DelegateCommandRun.Run(http.Transport, ["refine", TaskId, "steer it", "--interrupt"]).Exit.ShouldBe(0);
        DelegateCommandRun.Run(http.Transport, ["refine", TaskId, "no interrupt"]).Exit.ShouldBe(0);

        http.Calls.Select(call => call.Method + " " + call.Path).Distinct().ShouldBe([$"POST /api/agent-tasks/{TaskId}/refine"]);
        var bodies = http.Calls.Select(call => JsonDocument.Parse(call.Body!).RootElement).ToList();
        bodies[0].GetProperty("message").GetString().ShouldBe("steer it");
        bodies[0].GetProperty("interruptCurrentTurn").GetBoolean().ShouldBeTrue();
        bodies[2].GetProperty("interruptCurrentTurn").GetBoolean().ShouldBeFalse();
        var ids = bodies.Select(body => Guid.Parse(body.GetProperty("requestId").GetString()!)).ToList();
        ids.Distinct().Count().ShouldBe(3, "each refine sends a fresh request id");
    }

    [Test]
    public void Interrupt_output_prints_interruptWritten_and_refinementDelivered_on_separate_lines()
    {
        var http = Answer("""{"status":"Working","interruptWritten":"interrupt-written-status","refinementDelivered":"refinement-delivered-status"}""");
        var run = DelegateCommandRun.Run(http.Transport, ["refine", TaskId, "steer it", "--interrupt"]);

        run.Exit.ShouldBe(0, run.All);
        run.Output.Replace("\r\n", "\n").TrimEnd('\n').Split('\n').ShouldBe([
            $"Refined task {TaskId}. The message will land between its turns; its report will note it.",
            "interrupt-written-status",
            "refinement-delivered-status",
        ]);

        var plain = Answer("""{"status":"Queued","interruptWritten":"x","refinementDelivered":"y"}""");
        var queued = DelegateCommandRun.Run(plain.Transport, ["refine", TaskId, "steer it"]);
        queued.Output.Trim().ShouldBe($"Refined task {TaskId} before dispatch - folded into its brief.");
    }

    private static RecordingOperatorHttp Answer(string body) => new(_ => new NightlyReportHttpResponse(200, body));
}
