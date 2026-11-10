using System.Text.Json;
using Antiphon.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Shouldly;
using TUnit.Core;

namespace Antiphon.SessionRunner.Tests;

/// <summary>
/// CARD-1695 S1 lexical admission, plus CARD-1825 witnesses for the standing-name allow-list,
/// launch-time symlink refusal, and a filesystem-root <c>AllowedCwd</c>. The production root
/// in the lexical cases is <c>/work</c>. Nothing here touches a live runner.
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

        // A trailing dot stays a Windows-only refusal. A space is outside the standing-name
        // allow-list, so both flags refuse it (CARD-1825). A colon is outside that list too.
        Expect("/work/agents/slides-server2.", Root, windows: true, admitted: false, standing: false);
        Expect("/work/agents/slides-server2 ", Root, windows: true, admitted: false, standing: false);
        Expect("/work/agents/slides:server2", Root, windows: true, admitted: false, standing: false);
        Expect("/work/agents/slides-server2.", Root, windows: false, admitted: true, standing: true);
        Expect("/work/agents/slides-server2 ", Root, windows: false, admitted: false, standing: false);
    }

    [Test]
    public void Agent_name_allow_list_refuses_metacharacters_leading_marks_and_dot_runs()
    {
        foreach (var name in new[]
                 {
                     "$(id)",
                     "`id`",
                     "~",
                     "~root",
                     "-x",
                     "-rf",
                     ".hidden",
                     "...",
                     "....",
                     "a b",
                     "x*",
                     "'x'",
                     "slides-server2 ",
                     "_leading",
                 })
        {
            var cwd = "/work/agents/" + name;
            Expect(cwd, Root, windows: false, admitted: false, standing: false);
            Expect(cwd, Root, windows: true, admitted: false, standing: false);
        }

        foreach (var name in new[] { "a", "Z", "9agent", "slides_server2", "slides-server2", "slides.server2" })
        {
            var cwd = "/work/agents/" + name;
            Expect(cwd, Root, windows: false, admitted: true, standing: true);
            Expect(cwd, Root, windows: true, admitted: true, standing: true);
        }
    }

    [Test]
    public void Windows_device_names_are_refused_only_on_the_windows_flag()
    {
        foreach (var name in new[] { "CON", "con", "PRN", "AUX", "NUL", "COM1", "com9", "LPT1", "Lpt9", "NUL.txt", "CON.agent" })
        {
            var cwd = "/work/agents/" + name;
            Expect(cwd, Root, windows: false, admitted: true, standing: true);
            Expect(cwd, Root, windows: true, admitted: false, standing: false);
        }

        foreach (var name in new[] { "console", "COM10", "COM", "LPT", "PRINTER" })
        {
            var cwd = "/work/agents/" + name;
            Expect(cwd, Root, windows: false, admitted: true, standing: true);
            Expect(cwd, Root, windows: true, admitted: true, standing: true);
        }
    }

    [Test]
    public void Worktree_names_keep_the_wider_segment_rule()
    {
        foreach (var cwd in new[]
                 {
                     "/work/worktrees/$(id)",
                     "/work/worktrees/~root",
                     "/work/worktrees/-x",
                     "/work/worktrees/CON",
                 })
        {
            Expect(cwd, Root, windows: false, admitted: true, standing: false);
            Expect(cwd, Root, windows: true, admitted: true, standing: false);
        }

        // A trailing dot is the existing Windows worktree refusal. The agents allow-list must not move it.
        Expect("/work/worktrees/...", Root, windows: false, admitted: true, standing: false);
        Expect("/work/worktrees/...", Root, windows: true, admitted: false, standing: false);
    }

    [Test]
    public void Symlinked_or_non_directory_standing_checkout_is_refused_at_launch()
    {
        var root = Path.Combine("/tmp", "c1825-link-" + Guid.NewGuid().ToString("N"));
        var agentsRoot = Path.Combine("/tmp", "c1825-agentslink-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(agentsRoot);
        var linked = Path.Combine(root, "agents", "linked-agent");
        var agentsLink = Path.Combine(agentsRoot, "agents");
        try
        {
            var agents = Path.Combine(root, "agents");
            var real = Path.Combine(agents, "real-agent");
            var outside = Path.Combine(root, "outside");
            Directory.CreateDirectory(real);
            Directory.CreateDirectory(outside);
            var dispatcher = NewDispatcher(root);

            Should.NotThrow(() => dispatcher.RejectUnsupportedLaunch(Launch(real)));
            var absentWorktree = Path.Combine(root, "worktrees", "task-absent");
            Should.NotThrow(() => dispatcher.RejectUnsupportedLaunch(Launch(absentWorktree)));

            Directory.CreateSymbolicLink(linked, outside);
            var linkRefusal = Should.Throw<PhoneHomeAdmissionException>(() =>
                dispatcher.RejectUnsupportedLaunch(Launch(linked)));
            linkRefusal.Message.Contains("symlink", StringComparison.OrdinalIgnoreCase).ShouldBeTrue(linkRefusal.Message);
            linkRefusal.Message.Contains("cwd must be", StringComparison.Ordinal).ShouldBeFalse(linkRefusal.Message);

            var fileLeaf = Path.Combine(agents, "file-agent");
            File.WriteAllText(fileLeaf, "not-a-directory");
            Should.Throw<PhoneHomeAdmissionException>(() => dispatcher.RejectUnsupportedLaunch(Launch(fileLeaf)));

            var absent = Path.Combine(agents, "absent-agent");
            Should.Throw<PhoneHomeAdmissionException>(() => dispatcher.RejectUnsupportedLaunch(Launch(absent)));

            // The leaf inside the link target is a real directory. lstat of that leaf follows
            // the agents directory, so only an lstat of the agents directory itself refuses this.
            var target = Path.Combine(agentsRoot, "target-agents");
            Directory.CreateDirectory(Path.Combine(target, "ok-agent"));
            Directory.CreateSymbolicLink(agentsLink, target);
            var agentsRefusal = Should.Throw<PhoneHomeAdmissionException>(() =>
                NewDispatcher(agentsRoot).RejectUnsupportedLaunch(Launch(Path.Combine(agentsLink, "ok-agent"))));
            agentsRefusal.Message.Contains("agents", StringComparison.OrdinalIgnoreCase).ShouldBeTrue(agentsRefusal.Message);
            agentsRefusal.Message.Contains("symlink", StringComparison.OrdinalIgnoreCase).ShouldBeTrue(agentsRefusal.Message);
            agentsRefusal.Message.Contains("cwd must be", StringComparison.Ordinal).ShouldBeFalse(agentsRefusal.Message);
        }
        finally
        {
            if (File.Exists(linked) || Directory.Exists(linked))
                File.Delete(linked);
            if (File.Exists(agentsLink) || Directory.Exists(agentsLink))
                File.Delete(agentsLink);
            Directory.Delete(root, recursive: true);
            Directory.Delete(agentsRoot, recursive: true);
        }
    }

    [Test]
    public void Filesystem_root_allowed_cwd_is_refused_at_startup()
    {
        Should.NotThrow(() => EnabledSettings("/work").Validate());
        Should.NotThrow(() => EnabledSettings("C:/Antiphon/runner/work").Validate());
        var relative = Should.Throw<InvalidOperationException>(() => EnabledSettings("work").Validate());
        relative.Message.Contains("POSIX absolute", StringComparison.Ordinal).ShouldBeTrue(relative.Message);

        foreach (var root in new[] { "/", "", "   ", "//", "///", "C:/", @"C:\" })
        {
            var ex = Should.Throw<InvalidOperationException>(() => EnabledSettings(root).Validate(), root);
            ex.Message.Contains("PhoneHome:AllowedCwd", StringComparison.Ordinal).ShouldBeTrue(ex.Message);
            ex.Message.Contains("filesystem root", StringComparison.Ordinal).ShouldBeTrue(ex.Message);
        }
    }

    [Test]
    public void Root_normalising_allowed_cwd_spellings_are_refused()
    {
        Should.NotThrow(() => EnabledSettings("/work/agents").Validate());
        Should.NotThrow(() => EnabledSettings("C:/Antiphon/runner/work/").Validate());

        foreach (var root in new[]
                 {
                     "/.", "/./", "/../", "/work/..", "/./work",
                     "C:/.", @"C:\.", "C:/Antiphon/..", @"C:\Antiphon\..", "C:/./", "C:/..",
                     "//?/C:/",
                 })
        {
            var ex = Should.Throw<InvalidOperationException>(() => EnabledSettings(root).Validate(), root);
            ex.Message.Contains("PhoneHome:AllowedCwd", StringComparison.Ordinal).ShouldBeTrue(ex.Message);
            ex.Message.Contains("filesystem root", StringComparison.Ordinal).ShouldBeTrue(ex.Message);
        }
    }

    [Test]
    public void Windows_mixed_case_standing_leaf_is_refused()
    {
        // Linux keeps both spellings: they are different directories. Windows folds case,
        // so only the lowercase leaf is one directory and the other spelling is refused.
        Expect("/work/agents/Foo", Root, windows: false, admitted: true, standing: true);
        Expect("/work/agents/foo", Root, windows: false, admitted: true, standing: true);
        Expect("/work/agents/Foo", Root, windows: true, admitted: false, standing: false);
        Expect("/work/agents/foo", Root, windows: true, admitted: true, standing: true);
        Expect("/WORK/agents/foo", Root, windows: true, admitted: true, standing: true);
        Expect("/WORK/agents/Foo", Root, windows: true, admitted: false, standing: false);
    }

    [Test]
    public async Task Standing_symlink_swapped_before_start_is_refused()
    {
        var root = Path.Combine("/tmp", "c1834-swap-" + Guid.NewGuid().ToString("N"));
        var agents = Path.Combine(root, "agents");
        var kept = Path.Combine(agents, "kept-agent");
        var leaf = Path.Combine(agents, "real-agent");
        var outside = Path.Combine(root, "outside");
        Directory.CreateDirectory(kept);
        Directory.CreateDirectory(leaf);
        Directory.CreateDirectory(outside);
        try
        {
            var keptRuntime = new IdleRuntime();
            var keptReply = await DispatchStanding(keptRuntime, root, kept, swapLeaf: null, swapTarget: null);
            keptRuntime.StartCalls.ShouldBe(1);
            keptReply.ErrorDetail.ShouldBe("start reached");

            var swapped = new IdleRuntime();
            var reply = await DispatchStanding(swapped, root, leaf, leaf, outside);
            swapped.StartCalls.ShouldBe(0);
            reply.Kind.ShouldBe(PhoneHomeFrameKind.Error);
            reply.ErrorDetail!.Contains("symlink", StringComparison.OrdinalIgnoreCase).ShouldBeTrue(reply.ErrorDetail);
            reply.ErrorDetail.Contains("cwd must be", StringComparison.Ordinal).ShouldBeFalse(reply.ErrorDetail);
        }
        finally
        {
            var link = new DirectoryInfo(leaf);
            if (link.Exists && link.LinkTarget is not null)
                File.Delete(leaf);
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
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

    private static PhoneHomeSettings EnabledSettings(string allowedCwd) => new()
    {
        Enabled = true,
        ServerOrigin = "https://example.test",
        AllowedCwd = allowedCwd,
    };

    private static PhoneHomeCommandDispatcher NewDispatcher(string? allowedCwd = null) =>
        new(new IdleRuntime(), new PhoneHomeSettings
        {
            Enabled = true,
            AllowedCwd = allowedCwd ?? Root,
            CapacityStatePath = Path.Combine("/tmp", "c1695-capacity-" + Guid.NewGuid().ToString("N")),
        });

    private static Task<PhoneHomeFrame> DispatchStanding(
        IdleRuntime runtime, string allowedCwd, string cwd, string? swapLeaf, string? swapTarget)
    {
        IProviderAuthProbe? probe = swapLeaf is null
            ? new SignedInProbe()
            : new SwapLeafProbe(swapLeaf, swapTarget!);
        var dispatcher = new PhoneHomeCommandDispatcher(runtime, new PhoneHomeSettings
        {
            Enabled = true,
            AllowedCwd = allowedCwd,
            CapacityStatePath = Path.Combine("/tmp", "c1834-capacity-" + Guid.NewGuid().ToString("N")),
        }, probe);
        var frame = new PhoneHomeFrame(
            PhoneHomeFrameKind.Request, 1, Guid.NewGuid(), PhoneHomeOperation.Launch,
            JsonSerializer.SerializeToElement(Launch(cwd), PhoneHomeFraming.Json));
        return dispatcher.DispatchAsync(frame, CancellationToken.None);
    }

    private static RunnerLaunchRequest Launch(string cwd) =>
        new(Guid.NewGuid(), "grok", [], new Dictionary<string, string>(), cwd, 80, 24);

    private sealed class IdleRuntime : IPhoneHomeRuntimeSurface
    {
        public RunnerCapabilitiesDto Capabilities() => throw new NotImplementedException();
        public string Health() => throw new NotImplementedException();
        public int StartCalls { get; private set; }
        public IReadOnlyList<RunnerSessionDto> List() => [];
        public Task<RunnerSessionDto> GetAsync(Guid sessionId, CancellationToken ct) => throw new NotImplementedException();
        public Task<RunnerSessionDto> StartAsync(RunnerLaunchRequest request, CancellationToken ct)
        {
            StartCalls++;
            throw new InvalidOperationException("start reached");
        }
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

    private sealed class SignedInProbe : IProviderAuthProbe
    {
        public Task<RunnerProviderAuthDto> ProbeAsync(string provider, CancellationToken ct) =>
            Task.FromResult(new RunnerProviderAuthDto(provider, true, "test", null, DateTimeOffset.UtcNow, null));
    }

    private sealed class SwapLeafProbe(string leaf, string outside) : IProviderAuthProbe
    {
        public Task<RunnerProviderAuthDto> ProbeAsync(string provider, CancellationToken ct)
        {
            Directory.Delete(leaf, recursive: true);
            Directory.CreateSymbolicLink(leaf, outside);
            return Task.FromResult(new RunnerProviderAuthDto(provider, true, "test", null, DateTimeOffset.UtcNow, null));
        }
    }
}
