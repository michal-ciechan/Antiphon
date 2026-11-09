using Antiphon.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Shouldly;
using TUnit.Core;

namespace Antiphon.SessionRunner.Tests;

/// <summary>
/// CARD-1695 S1. Lexical admission of one segment under <c>&lt;AllowedCwd&gt;/agents</c>.
/// The production root in these cases is <c>/work</c>. Nothing here touches a live runner.
/// </summary>
[Category("Unit")]
public sealed class StandingRunnerCwdTests
{
    private const string Root = "/work";

    [Test]
    public void Agents_checkout_is_admitted()
    {
        const string checkout = "/work/agents/slides-server2";
        Expect(checkout, Root, windows: false, admitted: true, standing: true);
        Expect(checkout, Root, windows: true, admitted: true, standing: true);
        Expect(@"\work\agents\slides-server2", Root, windows: false, admitted: false, standing: false);
        Expect(@"\work\agents\slides-server2", Root, windows: true, admitted: true, standing: true);
        Expect("/WORK/agents/slides-server2", Root, windows: false, admitted: false, standing: false);
        Expect("/WORK/agents/slides-server2", Root, windows: true, admitted: true, standing: true);
        Expect("/work/Agents/slides-server2", Root, windows: false, admitted: false, standing: false);
        Expect("/work/Agents/slides-server2", Root, windows: true, admitted: true, standing: true);
        // The leaf is an opaque segment. Case there is not a separator, so both flags admit it.
        Expect("/work/agents/Slides-Server2", Root, windows: false, admitted: true, standing: true);
        Expect("/work/agents/Slides-Server2", Root, windows: true, admitted: true, standing: true);
        Expect("/work/agents/slides.server2", Root, windows: false, admitted: true, standing: true);
        Expect("/work/agents/slides.server2", Root, windows: true, admitted: true, standing: true);
    }

    [Test]
    public void Agents_root_nested_and_traversal_are_refused()
    {
        foreach (var cwd in new string?[]
                 {
                     "/work/agents",
                     "/work/agents/",
                     "/work/agents/slides-server2/extra",
                     "/work/agents/..",
                     "/work/agents/../worktrees",
                     "/work/repos/antiphon",
                     "/workspace/agents/x",
                     "",
                     "   ",
                     ".",
                     "..",
                     "/work/agents/.",
                     "/work/agents/../..",
                     null,
                 })
        {
            Expect(cwd, Root, windows: false, admitted: false, standing: false);
            Expect(cwd, Root, windows: true, admitted: false, standing: false);
        }

        // The Windows flag adds the worktree segment rules: trailing dot, trailing space, colon.
        Expect("/work/agents/slides-server2.", Root, windows: true, admitted: false, standing: false);
        Expect("/work/agents/slides-server2 ", Root, windows: true, admitted: false, standing: false);
        Expect("/work/agents/slides:server2", Root, windows: true, admitted: false, standing: false);
        Expect("/work/agents/slides-server2.", Root, windows: false, admitted: true, standing: true);
        Expect("/work/agents/slides-server2 ", Root, windows: false, admitted: true, standing: true);
    }

    [Test]
    public void Worktree_and_workspace_root_stay_admitted()
    {
        Expect("/work", Root, windows: false, admitted: true, standing: false);
        Expect("/work", Root, windows: true, admitted: true, standing: false);
        Expect("/work/worktrees/task-deadbeef", Root, windows: false, admitted: true, standing: false);
        Expect("/work/worktrees/task-deadbeef", Root, windows: true, admitted: true, standing: false);
        Expect("/WORK/worktrees/task", Root, windows: false, admitted: false, standing: false);
        Expect("/WORK/worktrees/task", Root, windows: true, admitted: true, standing: false);
        Expect("/work/worktrees/task/child", Root, windows: false, admitted: false, standing: false);
        Expect("/work/worktrees/..", Root, windows: false, admitted: false, standing: false);
        Expect(@"\work\worktrees\task", Root, windows: false, admitted: false, standing: false);
        Expect(@"C:\work\worktrees\task-deadbeef", @"C:\work", windows: true, admitted: true, standing: false);
        Expect(@"C:\work", @"C:\work\", windows: true, admitted: true, standing: false);
        Expect(@"C:\work\worktrees\task-deadbeef\src", @"C:\work", windows: true, admitted: false, standing: false);
    }

    [Test]
    public void Dispatcher_message_names_the_agents_checkout()
    {
        var dispatcher = NewDispatcher();
        var cwdRefusal = Should.Throw<PhoneHomeAdmissionException>(() =>
            dispatcher.RejectUnsupportedLaunch(Launch("/tmp/c1695-outside")));
        cwdRefusal.Message.StartsWith("cwd must be", StringComparison.Ordinal).ShouldBeTrue(cwdRefusal.Message);
        cwdRefusal.Message.Contains("/work/agents/<segment>", StringComparison.Ordinal).ShouldBeTrue(cwdRefusal.Message);
        cwdRefusal.Message.Contains("/work/worktrees/", StringComparison.Ordinal).ShouldBeTrue(cwdRefusal.Message);

        var herdrBackend = Should.Throw<PhoneHomeAdmissionException>(() =>
            dispatcher.RejectUnsupportedLaunch(Launch("/work") with { Backend = SessionBackends.Herdr }));
        herdrBackend.Message.Contains("cwd must be", StringComparison.Ordinal).ShouldBeFalse(herdrBackend.Message);

        var herdrPayload = Should.Throw<PhoneHomeAdmissionException>(() =>
            dispatcher.RejectUnsupportedLaunch(Launch("/work") with
            {
                Herdr = new HerdrLaunchOptions("none", "Antiphon", null, "slides"),
            }));
        herdrPayload.Message.Contains("cwd must be", StringComparison.Ordinal).ShouldBeFalse(herdrPayload.Message);
    }

    [Test]
    public void Dot_and_parent_segments_are_refused()
    {
        foreach (var cwd in new[]
                 {
                     "/work/agents/.",
                     "/work/agents/..",
                     "/work/agents/./slides-server2",
                     "/work/agents/../slides-server2",
                     "/work/agents/slides-server2/.",
                     "/work/agents/slides-server2/..",
                 })
        {
            Expect(cwd, Root, windows: false, admitted: false, standing: false);
            Expect(cwd, Root, windows: true, admitted: false, standing: false);
        }
    }

    [Test]
    public void Double_slash_empty_segment_is_refused()
    {
        foreach (var cwd in new[]
                 {
                     "/work/agents//slides-server2",
                     "/work//agents/slides-server2",
                     "/work/agents/slides-server2//extra",
                     "/work/agents///slides-server2",
                 })
        {
            Expect(cwd, Root, windows: false, admitted: false, standing: false);
            Expect(cwd, Root, windows: true, admitted: false, standing: false);
        }
    }

    [Test]
    public void Trailing_slash_on_the_checkout_is_refused()
    {
        Expect("/work/agents/slides-server2/", Root, windows: false, admitted: false, standing: false);
        Expect("/work/agents/slides-server2/", Root, windows: true, admitted: false, standing: false);
        Expect("/work/agents/slides-server2///", Root, windows: false, admitted: false, standing: false);
        Expect("/work/agents/", Root, windows: false, admitted: false, standing: false);
    }

    [Test]
    public void Linux_agents_directory_case_difference_is_refused()
    {
        Expect("/work/Agents/slides-server2", Root, windows: false, admitted: false, standing: false);
        Expect("/work/AGENTS/slides-server2", Root, windows: false, admitted: false, standing: false);
        Expect("/Work/agents/slides-server2", Root, windows: false, admitted: false, standing: false);
        Expect("/work/Agents/slides-server2", Root, windows: true, admitted: true, standing: true);
        Expect("/work/AGENTS/slides-server2", Root, windows: true, admitted: true, standing: true);
        Expect("/Work/agents/slides-server2", Root, windows: true, admitted: true, standing: true);
    }

    [Test]
    public void Very_long_segment_beyond_name_max_is_refused()
    {
        var beyond = "/work/agents/" + new string('a', 256);
        Expect(beyond, Root, windows: false, admitted: false, standing: false);
        Expect(beyond, Root, windows: true, admitted: false, standing: false);
    }

    [Test]
    public void Very_long_single_segment_within_name_max_is_admitted()
    {
        var within = "/work/agents/" + new string('b', 255);
        Expect(within, Root, windows: false, admitted: true, standing: true);
        Expect(within, Root, windows: true, admitted: true, standing: true);
    }

    [Test]
    public void Nul_and_control_characters_in_the_segment_are_refused()
    {
        foreach (var cwd in new[]
                 {
                     "/work/agents/slides\0server2",
                     "/work/agents/slides\nserver2",
                     "/work/agents/slides\rserver2",
                     "/work/agents/slides\tserver2",
                     "/work/agents/slides\u0001server2",
                     "/work/agents/slides\u007f",
                 })
        {
            Expect(cwd, Root, windows: false, admitted: false, standing: false);
            Expect(cwd, Root, windows: true, admitted: false, standing: false);
        }
    }

    [Test]
    public void Unicode_separator_and_dot_lookalikes_are_refused()
    {
        foreach (var cwd in new[]
                 {
                     "/work/agents/slides\uFF0Fx",
                     "/work/agents/\uFF0E\uFF0E",
                     "/work/agents/\u2025",
                     "/work/agents/slides\u2044x",
                     "/work/agents/slides\u2215x",
                     "/work/agents/slides\uFF3Cx",
                 })
        {
            Expect(cwd, Root, windows: false, admitted: false, standing: false);
            Expect(cwd, Root, windows: true, admitted: false, standing: false);
        }
    }

    [Test]
    public void Absolute_looking_segment_is_refused()
    {
        foreach (var cwd in new[]
                 {
                     "/work/agents/C:/Windows",
                     "/work/agents//etc/passwd",
                     "/etc/passwd",
                     "//work/agents/slides-server2",
                     "/work/agents/slides:server2",
                     "/workspace/agents/x",
                 })
        {
            Expect(cwd, Root, windows: false, admitted: false, standing: false);
            Expect(cwd, Root, windows: true, admitted: false, standing: false);
        }
    }

    [Test]
    public void Windows_style_separator_in_the_segment_is_refused()
    {
        Expect("/work/agents/foo\\bar", Root, windows: false, admitted: false, standing: false);
        Expect("/work/agents/foo\\bar", Root, windows: true, admitted: false, standing: false);
        Expect("/work/agents/foo\\..\\etc", Root, windows: false, admitted: false, standing: false);
    }

    [Test]
    public void Parent_segment_is_not_resolved_into_an_admitted_worktree()
    {
        // Resolving ".." would turn the first path into the admitted worktree
        // /work/worktrees/task-deadbeef. The check must refuse it instead.
        foreach (var cwd in new[]
                 {
                     "/work/agents/../worktrees/task-deadbeef",
                     "/work/agents/slides-server2/../slides-server2",
                     "/work/worktrees/../agents/slides-server2",
                     "/work/agents/..",
                 })
        {
            Expect(cwd, Root, windows: false, admitted: false, standing: false);
            Expect(cwd, Root, windows: true, admitted: false, standing: false);
        }
    }

    [Test]
    public void Absent_checkout_is_admitted_without_following_a_symlink()
    {
        var cwd = "/work/agents/c1695-absent-" + Guid.NewGuid().ToString("N");
        Directory.Exists(cwd).ShouldBeFalse(cwd);
        Expect(cwd, Root, windows: false, admitted: true, standing: true);
        Expect(cwd, Root, windows: true, admitted: true, standing: true);
    }

    private static void Expect(string? cwd, string root, bool windows, bool admitted, bool standing)
    {
        var label = (cwd ?? "<null>") + " windows=" + windows;
        RunnerWorkspaceAdmission.IsAdmitted(cwd, root, windows).ShouldBe(admitted, label);
        RunnerWorkspaceCwd.IsAdmitted(cwd, root, windows).ShouldBe(admitted, label);
        RunnerWorkspaceAdmission.IsStandingCheckout(cwd, root, windows).ShouldBe(standing, label);
    }

    private static PhoneHomeCommandDispatcher NewDispatcher() =>
        new(new IdleRuntime(), new PhoneHomeSettings
        {
            Enabled = true,
            AllowedCwd = Root,
            CapacityStatePath = Path.Combine("/tmp", "c1695-capacity-" + Guid.NewGuid().ToString("N")),
        });

    private static RunnerLaunchRequest Launch(string cwd) =>
        new(Guid.NewGuid(), "grok", [], new Dictionary<string, string>(), cwd, 80, 24);

    private sealed class IdleRuntime : IPhoneHomeRuntimeSurface
    {
        public RunnerCapabilitiesDto Capabilities() => throw new NotImplementedException();
        public string Health() => throw new NotImplementedException();
        public IReadOnlyList<RunnerSessionDto> List() => throw new NotImplementedException();
        public Task<RunnerSessionDto> GetAsync(Guid sessionId, CancellationToken ct) => throw new NotImplementedException();
        public Task<RunnerSessionDto> StartAsync(RunnerLaunchRequest request, CancellationToken ct) => throw new NotImplementedException();
        public RunnerBufferDto GetBuffer(Guid sessionId) => throw new NotImplementedException();
        public Task<RunnerSnapshotDto> GetSnapshotAsync(Guid sessionId, CancellationToken ct) => throw new NotImplementedException();
        public RunnerTranscriptDto GetTranscript(Guid sessionId) => throw new NotImplementedException();
        public Task SendInputAsync(Guid sessionId, string input, CancellationToken ct) => throw new NotImplementedException();
        public Task<RunnerConditionalInputResult> SendConditionalInputAsync(
            Guid sessionId, RunnerConditionalInputRequest request, CancellationToken ct) => throw new NotImplementedException();
        public Task ClearLiveBufferAsync(Guid sessionId, CancellationToken ct) => throw new NotImplementedException();
        public Task ResizeAsync(Guid sessionId, int cols, int rows, CancellationToken ct) => throw new NotImplementedException();
        public Task<RunnerKillGenerationResult> KillGenerationAsync(
            Guid sessionId, DateTime expectedAcceptedStartedAt, CancellationToken ct) => throw new NotImplementedException();
        public int OwnedSessionCount => 0;
    }
}
