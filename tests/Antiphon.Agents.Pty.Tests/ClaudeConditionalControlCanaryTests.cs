using Antiphon.Agents.Pty;
using Antiphon.SessionRunner.Contracts;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Agents.Pty.Tests;

/// <summary>
/// CARD-1517/1518 S0: one control byte against an owned native Claude session.
/// A failed qualification never authorizes another key or a fabricated turn boundary.
/// </summary>
[NotInParallel("Headed")]
[Category("Headed")]
[Category("OptIn")]
[Category("HeadedCanary")]
[Explicit]
[ParallelLimiter<ProcessSpawnLimit>]
public class ClaudeConditionalControlCanaryTests
{
    [Test]
    public async Task C1517_ToolCtrlCLeavesSessionAlive()
    {
        await using var live = await ClaudeConditionalControlSession.StartAsync(permission: false);
        await live.SubmitAsync("Use Bash to run exactly node control-probe.cjs in the foreground. "
            + "Do not run it in the background or run any other command. Wait for it to finish.");
        await live.WaitAsync(() => live.HasToolCall && File.Exists(live.StartedPath),
            TimeSpan.FromMinutes(2), "the owned tool must actually start before Ctrl+C");
        live.CaptureToolProcess();
        await live.RequireEmptyComposerAsync(working: true);
        var before = live.ReadParts().Count;
        live.Log("before-control", "tool_started=true byte=03");
        await live.Runner.WriteAsync("\x03");
        await live.RequireBoundaryAsync(before);
        live.Runner.Exited.IsCompleted.ShouldBeFalse("one Ctrl+C must leave Claude alive");
        await live.WaitAsync(() => live.ToolHasExited, TimeSpan.FromSeconds(30),
            "Ctrl+C must stop the owned tool, not leave it running behind the next prompt");
        File.Exists(live.CompletedPath).ShouldBeFalse("the long tool must be interrupted before completion");
        await live.ConfirmNextPromptAsync();
    }

    [Test]
    public async Task C1518_PermissionEscapeCancelsWithoutToolExecution()
    {
        await using var live = await ClaudeConditionalControlSession.StartAsync(permission: true);
        await live.SubmitAsync("Use Bash to run exactly node permission-probe.cjs. "
            + "This file contains exactly: " + ClaudeConditionalControlSession.PermissionProbeSource
            + " It only writes started.txt and completed.txt in this disposable test directory. "
            + "I authorize those two harmless writes. Do not use another tool or command.");
        await live.WaitAsync(() => live.HasToolCall
                && ClaudeBlockingPromptDetector.Detect(live.Runner.SnapshotScreen())?.Kind
                    == ClaudeBlockingPromptKind.ToolPermission,
            TimeSpan.FromMinutes(2), "a real Bash permission dialog must be current");
        File.Exists(live.StartedPath).ShouldBeFalse("the approval-gated child must not have started");
        File.Exists(live.CompletedPath).ShouldBeFalse("approval-gated write must still be absent");
        var screen = live.Runner.SnapshotScreen();
        screen.Contains("Esc to cancel", StringComparison.OrdinalIgnoreCase).ShouldBeTrue(
            "the current dialog must explicitly advertise cancellation");
        live.Log("before-control", "dialog=ToolPermission tool_started=false byte=1b");
        var before = live.ReadParts().Count;
        await live.Runner.WriteAsync("\x1b");
        await live.RequireBoundaryAsync(before);
        await live.WaitAsync(() => !ClaudeBlockingPromptDetector.IsBlocked(live.Runner.SnapshotScreen()),
            TimeSpan.FromSeconds(30), "one Esc must clear the current dialog");
        live.Runner.Exited.IsCompleted.ShouldBeFalse("dialog cancellation must leave Claude alive");
        File.Exists(live.StartedPath).ShouldBeFalse("Esc must never approve or launch the gated child");
        File.Exists(live.CompletedPath).ShouldBeFalse("Esc must never perform the gated write");
        await live.ConfirmNextPromptAsync();
        File.Exists(live.StartedPath).ShouldBeFalse("the next prompt must not retry the cancelled tool");
        File.Exists(live.CompletedPath).ShouldBeFalse();
        live.ReadParts().Count(p => p.Kind == TranscriptKinds.ToolCall).ShouldBe(1,
            "the model must not replace the cancelled operation with another tool call");
    }
}
