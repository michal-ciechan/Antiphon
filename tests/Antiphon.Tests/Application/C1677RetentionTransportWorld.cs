using System.Diagnostics;
using Antiphon.Server.Domain.Enums;
using Antiphon.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Shouldly;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-1677 F-4. The server half is the real retention service on the <see cref="RemoteWorkspacePreparerTests.Rig"/>
/// (phone-home host, ClientWebSocket, real <c>PhoneHomeRunnerClient</c>). Its peer hands every retention frame to a
/// real runner <see cref="PhoneHomeCommandDispatcher"/> whose runtime surface runs a real
/// <see cref="RunnerWorkspaceRetention"/> over a scratch bare origin, runner clone and temp worktree root, so the
/// recipient evidence is the on-disk tree and its git registration. No path here names a real worktree root.
/// </summary>
internal sealed class C1677RetentionTransportWorld : IAsyncDisposable
{
    private readonly List<string[]> _argv = [];

    private C1677RetentionTransportWorld(string root)
    {
        Root = root.Replace('\\', '/');
        OriginGit = Root + "/origin.git";
        Seed = Root + "/seed";
        Clone = Root + "/repos/antiphon";
        Work = Root + "/work";
    }

    public string Root { get; }
    public string OriginGit { get; }
    public string Seed { get; }
    public string Clone { get; }
    public string Work { get; }
    public string Worktrees => Work + "/worktrees";
    public string Retiring => Work + "/worktrees-retiring";
    public C1677RetentionServerTests.World Server { get; private set; } = null!;
    public PhoneHomeCommandDispatcher Runner { get; private set; } = null!;
    public RunnerWorkspaceRetention Engine { get; private set; } = null!;

    /// <summary>Held before a WorkspaceRetain frame reaches the runner (the busy-recipient witness).</summary>
    public Task? Hold { get; set; }

    /// <summary>The runner acts on the next WorkspaceRetain but its reply is lost.</summary>
    public bool DropNextReply { get; set; }

    /// <summary>The runner acts on the next WorkspaceDropOutputs but its reply is lost.</summary>
    public bool DropNextOutputReply { get; set; }

    /// <summary>The runner crashes once right after phase A's move, before phase B.</summary>
    public bool CrashAfterMoveOnce { get; set; }

    public static async Task<C1677RetentionTransportWorld> StartAsync(bool outputDrop = false)
    {
        var world = new C1677RetentionTransportWorld(Path.Combine(Path.GetTempPath(), "c1677-transport-" + Guid.NewGuid().ToString("N")));
        world.BuildRepository();
        world.Server = await C1677RetentionServerTests.World.StartAsync(outputDrop: outputDrop);
        world.Server.Settings.WorktreeRoot = world.Worktrees;
        world.Server.Settings.RetiringRoot = world.Retiring;
        world.StartRunner(outputDrop);
        world.Server.Rig.Peer.Reply = world.Answer;
        return world;
    }

    private void StartRunner(bool outputDrop)
    {
        var emptyConfig = Root + "/empty.gitconfig";
        File.WriteAllText(emptyConfig, "");
        var service = new RunnerWorkspaceService(Clone, Work, OriginGit, psi =>
        {
            psi.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
            if (!psi.Environment.ContainsKey("GIT_CONFIG_GLOBAL"))
                psi.Environment["GIT_CONFIG_GLOBAL"] = emptyConfig;
            psi.Environment["GIT_TERMINAL_PROMPT"] = "0";
            lock (_argv)
                _argv.Add(psi.ArgumentList.SkipWhile(arg => arg.StartsWith('-')).ToArray());
            return Process.Start(psi);
        }, publishedBranch: "main");
        Engine = new RunnerWorkspaceRetention(service, new ProcWorkspaceSlotProcessProbe(), retiringRoot: Retiring)
        {
            OutputDropLedgerRoot = outputDrop ? Root + "/output-drop-ledger" : null,
        };
        Engine.PhaseCut = phase =>
        {
            if (phase == "moved" && CrashAfterMoveOnce)
            {
                CrashAfterMoveOnce = false;
                throw new IOException("runner crashed between phase A and phase B");
            }
        };
        var settings = new PhoneHomeSettings
        {
            Enabled = true, RunnerId = Server.Rig.RunnerId, AllowedCwd = Work, RunnerRepository = Clone,
            RunnerCloneSource = OriginGit, AllowedCloneSources = [OriginGit], Capacity = 2,
            CapacityStatePath = Root + "/capacity",
            OutputDropMode = outputDrop ? RunnerOutputDropMode.Execute : RunnerOutputDropMode.Off,
        };
        Runner = new PhoneHomeCommandDispatcher(new RetentionRuntime(Engine, outputDrop), settings);
    }

    private PhoneHomeFrame? Answer(PhoneHomeFrame frame)
    {
        if (frame.Kind != PhoneHomeFrameKind.Request
            || frame.Operation is not (PhoneHomeOperation.WorkspaceRetain
                or PhoneHomeOperation.WorkspaceRetentionInventory
                or PhoneHomeOperation.WorkspaceDropOutputs))
            return null;
        if (frame.Operation == PhoneHomeOperation.WorkspaceRetain)
            Hold?.GetAwaiter().GetResult();
        var reply = Runner.DispatchAsync(frame, CancellationToken.None).GetAwaiter().GetResult();
        if (frame.Operation == PhoneHomeOperation.WorkspaceRetain && DropNextReply)
        {
            DropNextReply = false;
            // The runner acted; the reply never arrives in a readable form.
            return new PhoneHomeFrame(PhoneHomeFrameKind.Error, frame.Epoch, frame.RequestId, frame.Operation,
                ErrorCode: "transport_lost", ErrorDetail: "reply lost", StatusCode: 503);
        }
        if (frame.Operation == PhoneHomeOperation.WorkspaceDropOutputs && DropNextOutputReply)
        {
            DropNextOutputReply = false;
            return new PhoneHomeFrame(PhoneHomeFrameKind.Error, frame.Epoch, frame.RequestId, frame.Operation,
                ErrorCode: "transport_lost", ErrorDetail: "reply lost", StatusCode: 503);
        }
        return reply;
    }

    public Task TickAsync(CancellationToken ct) => Server.TickAsync(ct);

    /// <summary>A clean, pushed runner mirror and its finished server task (Execute is on).</summary>
    public async Task<(Guid Id, string Path)> SeedMirroredTaskAsync(string id8 = "a1677001", TimeSpan? completedAgo = null)
    {
        var branch = "feat/card-task-" + id8;
        var path = Worktrees + "/task-" + id8;
        Git(Seed, "checkout", "-q", "-b", branch, "main");
        File.WriteAllText(Seed + "/" + id8 + ".txt", id8);
        Git(Seed, "add", "-A");
        Git(Seed, "commit", "-q", "-m", branch);
        Git(Seed, "push", "-q", "origin", branch);
        Git(Seed, "checkout", "-q", "main");
        Git(Clone, "fetch", "-q", "origin", "refs/heads/" + branch + ":refs/remotes/origin/" + branch);
        Git(Clone, "worktree", "add", "-q", "-b", branch, path, "refs/remotes/origin/" + branch);
        var id = Guid.Parse(id8 + "-0000-4000-8000-000000001677");
        await Server.Rig.SeedAsync(task =>
        {
            task.Id = id;
            task.RootTaskId = id;
            task.Status = AgentTaskStatus.Succeeded;
            task.CompletedAt = Server.Rig.Now - (completedAgo ?? TimeSpan.FromHours(24));
            task.RemoteWorktreePath = path;
            task.WorktreeBranch = branch;
            task.Result = "done";
        });
        return (id, path);
    }

    public bool Registered(string path) =>
        Git(Clone, "worktree", "list", "--porcelain").Replace("\r\n", "\n").Split('\n').Contains("worktree " + path);

    /// <summary>
    /// Publish ignore rules, then plant exactly one secret so another protected name cannot mask it.
    /// </summary>
    public void PublishIgnoredSecret(string path, string branch, string relative)
    {
        Git(path, "config", "user.email", "c1677@localhost");
        Git(path, "config", "user.name", "c1677");
        File.AppendAllText(path + "/.gitignore", "node_modules/\ndist/\n");
        Git(path, "add", ".gitignore");
        Git(path, "commit", "-q", "-m", "ignore disposable trees");
        Git(path, "push", "-q", OriginGit, "HEAD:refs/heads/" + branch);
        // The runner's unpushed check reads origin's remote-tracking ref, not the bare URL just pushed.
        Git(Clone, "fetch", "origin", "refs/heads/" + branch + ":refs/remotes/origin/" + branch);
        WriteSecret(path, relative, "preserve-secret\n");
        Git(path, "status", "--porcelain", "--untracked-files=all").Trim().ShouldBeEmpty();
    }

    private static void WriteSecret(string root, string relative, string text)
    {
        var full = Path.Combine(root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, text);
    }

    public int Moves(string path)
    {
        lock (_argv)
            return _argv.Count(args => args.Length > 2 && args[0] == "worktree" && args[1] == "move" && args[2] == path);
    }

    public int Removes()
    {
        lock (_argv)
            return _argv.Count(args => args.Length > 1 && args[0] == "worktree" && args[1] == "remove");
    }

    public List<string> RetiringEntries() =>
        Directory.Exists(Retiring) ? Directory.EnumerateFileSystemEntries(Retiring).ToList() : [];

    private void BuildRepository()
    {
        Directory.CreateDirectory(Worktrees);
        Directory.CreateDirectory(Root + "/repos");
        Git(Root, "init", "-q", "--bare", "--initial-branch=main", OriginGit);
        Git(Root, "init", "-q", "--initial-branch=main", Seed);
        Git(Seed, "config", "user.email", "c1677@localhost");
        Git(Seed, "config", "user.name", "c1677");
        File.WriteAllText(Seed + "/README.md", "c1677 transport");
        File.WriteAllText(Seed + "/.gitignore", ".antiphon/\nbin/\nobj/\n");
        Git(Seed, "add", "-A");
        Git(Seed, "commit", "-q", "-m", "base");
        Git(Seed, "remote", "add", "origin", OriginGit);
        Git(Seed, "push", "-q", "origin", "main");
        Git(Root, "clone", "-q", OriginGit, Clone);
    }

    private static string Git(string workingDirectory, params string[] args) => C0824SlotTransportWorld.Git(workingDirectory, args);

    public async ValueTask DisposeAsync()
    {
        await Server.DisposeAsync();
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>A runtime surface whose only real members are the retention phases and inventory.</summary>
    private sealed class RetentionRuntime(RunnerWorkspaceRetention engine, bool outputDrop) : IPhoneHomeRuntimeSurface
    {
        public Task<WorkspaceRetainStep> RetainWorkspaceCheckAndMoveAsync(PhoneHomeWorkspaceRetainRequest request, CancellationToken ct) =>
            engine.CheckAndMoveAsync(request, ct);
        public Task<PhoneHomeWorkspaceRetainResponse> RetainWorkspaceCompleteAsync(WorkspaceRetainStep step, CancellationToken ct) =>
            engine.CompleteAsync(step, ct);
        public Task<PhoneHomeWorkspaceRetentionInventoryResponse> WorkspaceRetentionInventoryAsync(CancellationToken ct) =>
            engine.InventoryAsync(ct);
        public Task<WorkspaceOutputDropStep> CheckWorkspaceOutputsAsync(PhoneHomeWorkspaceOutputDropRequest request, CancellationToken ct) =>
            engine.CheckOutputsAsync(request, ct);
        public Task<PhoneHomeWorkspaceOutputDropResponse> ApplyWorkspaceOutputsAsync(WorkspaceOutputDropStep step, CancellationToken ct) =>
            engine.ApplyOutputsAsync(step, ct);
        public int OwnedSessionCount => 0;
        public RunnerCapabilitiesDto Capabilities()
        {
            List<string> features = [RunnerCapabilityFeatures.WorkspaceRetentionV1];
            if (outputDrop)
                features.Add(RunnerCapabilityFeatures.WorkspaceOutputDropV1);
            return new("InboxConhost", "inbox", "test", false, Features: features);
        }
        public string Health() => "Healthy";
        public IReadOnlyList<RunnerSessionDto> List() => [];
        public Task<RunnerSessionDto> GetAsync(Guid sessionId, CancellationToken ct) => throw new NotSupportedException();
        public Task<RunnerSessionDto> StartAsync(RunnerLaunchRequest request, CancellationToken ct) => throw new NotSupportedException();
        public RunnerBufferDto GetBuffer(Guid sessionId) => throw new NotSupportedException();
        public Task<RunnerSnapshotDto> GetSnapshotAsync(Guid sessionId, CancellationToken ct) => throw new NotSupportedException();
        public RunnerTranscriptDto GetTranscript(Guid sessionId) => throw new NotSupportedException();
        public Task SendInputAsync(Guid sessionId, string input, CancellationToken ct) => throw new NotSupportedException();
        public Task<RunnerConditionalInputResult> SendConditionalInputAsync(Guid sessionId, RunnerConditionalInputRequest request, CancellationToken ct) => throw new NotSupportedException();
        public Task ClearLiveBufferAsync(Guid sessionId, CancellationToken ct) => throw new NotSupportedException();
        public Task ResizeAsync(Guid sessionId, int cols, int rows, CancellationToken ct) => throw new NotSupportedException();
        public Task<RunnerKillGenerationResult> KillGenerationAsync(Guid sessionId, DateTime expectedAcceptedStartedAt, CancellationToken ct) => throw new NotSupportedException();
    }
}
