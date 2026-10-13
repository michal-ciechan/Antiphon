using System.Diagnostics;
using System.Reflection;
using Antiphon.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;

namespace Antiphon.SessionRunner.Tests;

internal sealed class CompletedCardRunnerFixture : IAsyncDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "c1032-fence-" + Guid.NewGuid().ToString("N"));
    public string Checkout => Path.Combine(Root, "work", "worktrees", "task-deadbeef");
    public string State => Path.Combine(Root, "state", "retirements");
    public string Repository => Path.Combine(Root, "repository");
    public string StoreId { get; } = Guid.NewGuid().ToString("D");
    public SessionRunnerSettings Settings { get; }
    public SessionRunnerRuntime Runtime { get; private set; }
    public dynamic Gate { get; private set; } = null!;
    public int Effects { get; private set; }
    public Func<Task>? EffectBarrier { get; set; }
    public string Sha { get; }
    private readonly List<SessionRunnerRuntime> _runtimes = [];
    private readonly List<Process> _children = [];

    public CompletedCardRunnerFixture()
    {
        Directory.CreateDirectory(Checkout);
        Directory.CreateDirectory(Repository);
        Directory.CreateDirectory(Path.Combine(Root, "outside"));
        File.WriteAllText(Path.Combine(Root, "outside", "sentinel"), "preserve");
        Git(Repository, "init", "--initial-branch=main");
        Git(Repository, "config", "user.name", "c1032");
        Git(Repository, "config", "user.email", "c1032@localhost");
        File.WriteAllText(Path.Combine(Repository, "source"), "source");
        Git(Repository, "add", "source");
        Git(Repository, "commit", "-m", "fixture");
        Sha = Git(Repository, "rev-parse", "HEAD").Trim();
        Git(Repository, "branch", "feat/card-task-deadbeef");
        Settings = new() { SessionLogPath = Path.Combine(Root, "logs") };
        Runtime = NewRuntime();
    }

    // Reflection lets the committed tests execute on the pre-slice assembly: absence of the
    // concrete production fence is an assertion failure, rather than a compilation failure.
    public void InstallGate(bool restart = false)
    {
        var assembly = typeof(SessionRunnerRuntime).Assembly;
        var journalType = assembly.GetType("Antiphon.SessionRunner.RunnerWorkspaceRemovalJournal");
        journalType.ShouldNotBeNull("S3 requires a durable runner retirement journal");
        var gateType = assembly.GetType("Antiphon.SessionRunner.RunnerWorkspaceUseGate");
        gateType.ShouldNotBeNull("S3 requires shared workspace admission");
        var journal = Activator.CreateInstance(journalType!, State, StoreId)!;
        Gate = Activator.CreateInstance(gateType!, journal)!;
        if (restart) Runtime = NewRuntime();
        Bind(Runtime);
    }

    public void Bind(object service) => service.GetType().GetProperty("WorkspaceUseGate",
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!
        .SetValue(service, (object)Gate);

    public SessionRunnerRuntime NewRuntime(HerdrClient? herdr = null)
    {
        var runtime = new SessionRunnerRuntime(Options.Create(Settings), NullLogger<SessionRunnerRuntime>.Instance, herdr);
        runtime.CreationEffectForTest = async (_, _) =>
        {
            Effects++;
            if (EffectBarrier is { } barrier) await barrier();
            throw new FenceEffectException();
        };
        _runtimes.Add(runtime);
        return runtime;
    }

    public RunnerLaunchRequest Launch(Guid id, string? path = null) => new(id,
        HerdrTestProcess.ShellPath, HerdrTestProcess.InteractiveArgs, new Dictionary<string, string>(),
        path ?? Checkout, 80, 24);

    public CompletedCardWorkspaceCommand Command() => new(1, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
        Guid.NewGuid(), Guid.NewGuid(), "CardDone",
        new(Guid.NewGuid(), 1, Guid.NewGuid(), Guid.NewGuid(), "fixture-runner", StoreId, Repository,
            Repository, Path.Combine(Repository, ".git"), Checkout, "refs/heads/feat/card-task-deadbeef", Sha),
        new(Sha, "fixture", "refs/heads/main", Sha, "Contained", "fixture-proof", Sha, null),
        "report", "artifacts", "policy", [], []);

    public Task<bool> Retire(CompletedCardWorkspaceCommand? command = null, Func<Task>? effect = null) =>
        (Task<bool>)Gate.TryRetireAsync(command ?? Command(), effect ?? (() => Task.CompletedTask), CancellationToken.None);

    public Process Child()
    {
        var child = HerdrTestProcess.StartDummy();
        _children.Add(child);
        return child;
    }

    public void SeedManifest(Process child, Guid id) => new Antiphon.PtyHost.Protocol.PtyHostManifest
    {
        SessionId = id, HostPid = child.Id, HostStartTimeUtc = child.StartTime.ToUniversalTime(),
        CreatedAtUtc = DateTime.UtcNow, Cwd = Checkout, PipeName = "c1032-no-host-pipe",
    }.SaveAtomic(Antiphon.PtyHost.Protocol.PtyHostManifest.PathFor(Settings.PtyHostManifestDir, id));

    public HerdrPaneSidecar SeedSidecar(Process child, Guid id, string pane = "fixture:p1")
    {
        var sidecar = new HerdrPaneSidecar
        {
            SessionId = id, WorkspaceKey = "fixture", WorkspaceId = "fixture", TabId = "fixture:t1",
            PaneId = pane, ChildPid = child.Id, ChildStartedAtUtc = child.StartTime.ToUniversalTime(),
            LaunchedAtUtc = child.StartTime.ToUniversalTime(), Cwd = Checkout, Origin = HerdrPaneOrigins.Attached,
        };
        sidecar.SaveAtomic(HerdrPaneSidecar.PathFor(Settings.SessionLogPath, id));
        return sidecar;
    }

    public RunnerWorkspaceService Workspace()
    {
        var config = Path.Combine(Root, "empty.gitconfig");
        File.WriteAllText(config, "");
        var service = new RunnerWorkspaceService(Repository, Path.Combine(Root, "work"), Repository, psi =>
        {
            psi.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
            psi.Environment["GIT_CONFIG_GLOBAL"] = config;
            return Process.Start(psi);
        });
        Bind(service);
        return service;
    }

    public static string Git(string cwd, params string[] args)
    {
        var psi = new ProcessStartInfo("git") { WorkingDirectory = cwd, RedirectStandardOutput = true,
            RedirectStandardError = true, UseShellExecute = false };
        psi.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
        psi.Environment["GIT_CONFIG_GLOBAL"] = OperatingSystem.IsWindows() ? "NUL" : "/dev/null";
        foreach (var arg in args) psi.ArgumentList.Add(arg);
        using var process = Process.Start(psi)!;
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0) throw new IOException(error);
        return output;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var runtime in _runtimes) await runtime.DisposeAsync();
        foreach (var child in _children)
        {
            if (!child.HasExited) child.Kill(entireProcessTree: true);
            await child.WaitForExitAsync();
            child.Dispose();
        }
        Directory.Delete(Root, recursive: true);
    }
}

internal sealed class FenceEffectException : Exception;
