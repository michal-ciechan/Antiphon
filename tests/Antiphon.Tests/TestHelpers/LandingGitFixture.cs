using System.Diagnostics;
using Shouldly;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Infrastructure.Git;

namespace Antiphon.Tests.TestHelpers;

/// <summary>All remotes, checkouts, Git configuration and sentinels are fixture-owned.</summary>
internal sealed class LandingGitFixture : IAsyncDisposable
{
    public string Root { get; }
    public string Repository => Path.Combine(Root, "canonical");
    public string Source => Path.Combine(Root, "trees", "source");
    public string Remote => Path.Combine(Root, "remote.git");
    public string Observer => Path.Combine(Root, "observer");
    /// <summary>The default keeps the historical full-id shape; CARD-0824 S2 passes the production short form.</summary>
    public string SourceRef => "refs/heads/" + (SourceBranch ?? $"feat/card-task-{TaskId:N}");
    public string? SourceBranch { get; }
    public string TargetRef => "refs/heads/master";
    public Guid TaskId { get; }
    public FixtureGit Git { get; }
    public string SeedSha { get; internal set; } = "";
    public LandSourceCoordinates Coordinates => new(TaskId, Repository, Source, SourceRef, TargetRef);
    public string ObservationRef => $"refs/antiphon/land/{TaskId:N}/{Guid.NewGuid():N}/remote-observed";

    public LandingGitFixture(string? root = null, Guid? taskId = null, string? sourceBranch = null)
    {
        SourceBranch = sourceBranch;
        Root = root ?? Path.Combine(Path.GetTempPath(), "antiphon-c448-" + Guid.NewGuid().ToString("N"));
        TaskId = taskId ?? Guid.NewGuid();
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(Repository);
        Directory.CreateDirectory(Remote);
        Directory.CreateDirectory(Path.Combine(Root, "home"));
        Git = new FixtureGit(Path.Combine(Root, "home"), TaskId);
    }

    /// <summary>
    /// Another template of the default fixture (token <c>task</c>, value <c>seed</c>) to clone instead, for a test
    /// class that always extends the fixture the same way. <see cref="FromTemplate"/> says whether it was used.
    /// </summary>
    internal Func<Task<GitTreeTemplate>>? TemplateSource { get; set; }

    /// <summary>True when <see cref="InitializeAsync"/> cloned a template rather than building with git.</summary>
    internal bool FromTemplate { get; private set; }

    /// <summary>
    /// Builds the fixture's repositories. With the default source branch they are cloned from a template built
    /// once per process (<see cref="GitTreeTemplate"/>: the task id is a token, so branch names, the committed
    /// <c>fixture-owner.txt</c> and the seed commit come out as a fresh build would make them), which saves the
    /// ~10 git processes (~1.5 s) a build takes. <c>ANTIPHON_TEST_GIT_TEMPLATE=off</c> builds every time.
    /// </summary>
    public async Task InitializeAsync()
    {
        if (GitTreeTemplate.Enabled && SourceBranch is null)
        {
            GitTreeTemplate? template = null;
            try
            {
                template = await (TemplateSource ?? TemplateAsync)();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"git-template disabled for this fixture: {ex.GetType().Name}: {ex.Message}");
            }
            if (template is not null)
            {
                var clone = template.CloneTo(Root, new Dictionary<string, string> { ["task"] = TaskId.ToString("N") });
                SeedSha = clone.Map(template.Values["seed"]);
                FromTemplate = true;
                return;
            }
        }
        await BuildAsync();
    }

    /// <summary>The process-wide template of a default fixture (token <c>task</c>, value <c>seed</c>).</summary>
    internal static Task<GitTreeTemplate> TemplateAsync() =>
        GitTreeTemplate.BuildOnceAsync("landing-git-fixture", ["task"], async (root, tokens) =>
        {
            var builder = new LandingGitFixture(root, Guid.ParseExact(tokens["task"], "N"));
            await builder.BuildAsync();
            return new() { ["seed"] = builder.SeedSha };
        }, cacheVersion: CacheVersion.Value);

    /// <summary>What a built template depends on: this test assembly's build (the fixture code) and git's version.</summary>
    internal static string TemplateCacheVersion => CacheVersion.Value;

    private static readonly Lazy<string> CacheVersion = new(() =>
    {
        var start = new ProcessStartInfo("git", "--version") { RedirectStandardOutput = true, UseShellExecute = false };
        using var process = Process.Start(start)!;
        var version = process.StandardOutput.ReadToEnd().Trim();
        process.WaitForExit();
        return typeof(LandingGitFixture).Assembly.ManifestModule.ModuleVersionId.ToString("N") + "|" + version;
    });

    /// <summary>The fixture built with git processes, never from the template.</summary>
    internal async Task BuildAsync()
    {
        await RequiredAsync(Repository, "init", "-b", "master");
        await RequiredAsync(Remote, "init", "--bare");
        // The bare remote stands in for a hosted one: no `git maintenance run --auto` after every
        // receive (three extra processes per land; a real host does its own housekeeping).
        await RequiredAsync(Remote, "config", "receive.autogc", "false");
        await File.WriteAllTextAsync(Path.Combine(Repository, "keep.txt"), "seed\n");
        await File.WriteAllTextAsync(Path.Combine(Repository, "fixture-owner.txt"), TaskId.ToString("N") + "\n");
        await File.WriteAllTextAsync(Path.Combine(Repository, ".gitignore"), ".antiphon/\n.claude/\nbin-*/\nobj/\n");
        await RequiredAsync(Repository, "add", ".");
        await RequiredAsync(Repository, "commit", "-m", "seed");
        SeedSha = (await RequiredAsync(Repository, "rev-parse", "HEAD")).Trim();
        await RequiredAsync(Repository, "remote", "add", "origin", Remote);
        await RequiredAsync(Repository, "push", "origin", TargetRef);
        await RequiredAsync(Repository, "worktree", "add", "-b", SourceRef[11..], Source, "HEAD");
        await RequiredAsync(Repository, "push", "origin", SourceRef);
        await RequiredAsync(Root, "clone", "--no-hardlinks", "--branch", "master", "--", Remote, Observer);
        Git.Trace.Clear();
        Git.Commands.Clear();
    }

    /// <summary>CARD-0711: an independent clone commits <paramref name="name"/>.txt and pushes it to <c>master</c>.</summary>
    public Task<string> PushIndependentAsync(string name) =>
        PushIndependentAsync(name, name + ".txt", name + "\n");

    /// <summary>Same stranger push, with the committed path and content chosen by the caller.</summary>
    public async Task<string> PushIndependentAsync(string name, string path, string content)
    {
        var reader = new FixtureGit(Path.Combine(Root, "home"), TaskId);
        var fetched = await reader.RunAsync(Observer, ["fetch", "origin", TargetRef], CancellationToken.None);
        fetched.Succeeded.ShouldBeTrue(fetched.Diagnostic);
        var based = await reader.RunAsync(Observer, ["reset", "--hard", "FETCH_HEAD"], CancellationToken.None);
        based.Succeeded.ShouldBeTrue(based.Diagnostic);
        var full = Path.Combine(Observer, path.Replace('/', Path.DirectorySeparatorChar));
        var parent = Path.GetDirectoryName(full);
        if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
        await File.WriteAllTextAsync(full, content);
        var add = await reader.RunAsync(Observer, ["add", "--", path], CancellationToken.None);
        add.Succeeded.ShouldBeTrue(add.Diagnostic);
        var commit = await reader.RunAsync(Observer, ["commit", "-m", name], CancellationToken.None);
        commit.Succeeded.ShouldBeTrue(commit.Diagnostic);
        var push = await reader.RunAsync(Observer, ["push", "origin", "HEAD:" + TargetRef], CancellationToken.None);
        push.Succeeded.ShouldBeTrue(push.Diagnostic);
        var head = await reader.RunAsync(Observer, ["rev-parse", "HEAD"], CancellationToken.None);
        head.Succeeded.ShouldBeTrue(head.Diagnostic);
        return head.Output.Trim();
    }

    public async Task<string> RequiredAsync(string path, params string[] arguments)
    {
        var result = await Git.RunAsync(path, arguments, CancellationToken.None);
        if (!result.Succeeded) throw new InvalidOperationException($"fixture_git_failed:{arguments[0]}:{result.ExitCode}");
        return result.Output;
    }

    public async Task AssertRemoteSourceAsync()
    {
        var reader = new FixtureGit(Path.Combine(Root, "home"), TaskId); // Independent of fault-injected production I/O.
        var actual = await reader.RunAsync(Remote, ["rev-parse", "--verify", SourceRef + "^{commit}"], CancellationToken.None);
        actual.Succeeded.ShouldBeTrue("the pre-published fixture remote source must not be deleted by landing/refusal/recovery");
        actual.Output.Trim().ShouldBe(SeedSha, "the pre-published remote source must retain its exact original commit");
        var observedRef = "refs/antiphon-observer/source/" + Guid.NewGuid().ToString("N");
        var fetch = await reader.RunAsync(Observer, ["fetch", "--no-tags", Remote, SourceRef + ":" + observedRef], CancellationToken.None);
        fetch.Succeeded.ShouldBeTrue("an independent observer must still fetch the protected remote source");
        var observed = await reader.RunAsync(Observer, ["rev-parse", "--verify", observedRef], CancellationToken.None);
        observed.Succeeded.ShouldBeTrue();
        observed.Output.Trim().ShouldBe(SeedSha);
    }

    public async Task CaptureAsync(string kind)
    {
        if (!LandingEvidence.Enabled) return;
        var contents = new List<object>();
        if (Directory.Exists(Source))
            foreach (var file in Directory.EnumerateFiles(Source, "*", SearchOption.AllDirectories))
            {
                string digest;
                try { digest = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(await File.ReadAllBytesAsync(file))); }
                catch (IOException) { digest = "unreadable"; }
                catch (UnauthorizedAccessException) { digest = "inaccessible"; }
                contents.Add(new { path = Path.GetRelativePath(Source, file), digest });
            }
        var reader = new LandingGit();
        var refs = await reader.RunAsync(Repository, ["show-ref"], CancellationToken.None);
        var remote = await reader.RunAsync(Remote, ["show-ref"], CancellationToken.None);
        var registration = await reader.RunAsync(Repository, ["worktree", "list", "--porcelain", "-z"], CancellationToken.None);
        var index = new List<object>();
        foreach (var directory in new[] { Source, Repository })
        {
            if (!Directory.Exists(directory)) continue;
            var admin = await reader.RunAsync(directory, ["rev-parse", "--absolute-git-dir"], CancellationToken.None);
            var path = Path.Combine(admin.Output.Trim(), "index");
            string? digest = null;
            try { if (admin.Succeeded && File.Exists(path)) digest = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(await File.ReadAllBytesAsync(path))); }
            catch (IOException) { digest = "unreadable"; }
            var state = await reader.RunAsync(directory, ["status", "--porcelain=v1", "-z", "--untracked-files=all", "--ignored", "--ignore-submodules=none"], CancellationToken.None);
            var head = await reader.RunAsync(directory, ["rev-parse", "--verify", "HEAD"], CancellationToken.None);
            var symbolic = await reader.RunAsync(directory, ["symbolic-ref", "-q", "HEAD"], CancellationToken.None);
            var active = new List<string>();
            if (admin.Succeeded)
                foreach (var marker in new[] { "MERGE_HEAD", "CHERRY_PICK_HEAD", "REVERT_HEAD", "rebase-merge", "rebase-apply", "sequencer" })
                    if (Path.Exists(Path.Combine(admin.Output.Trim(), marker))) active.Add(marker);
            index.Add(new { directory, gitDirectory = admin.Output, digest, state.Output, state.ExitCode,
                head = head.Output, headExit = head.ExitCode, symbolic = symbolic.Output, symbolicExit = symbolic.ExitCode, active });
        }
        LandingEvidence.Write(TaskId, kind, new { directoryExists = Directory.Exists(Source), contents, index, registrations = registration.Output, registrationExit = registration.ExitCode,
            localRefs = refs.Output, localQueryExit = refs.ExitCode, remoteRefs = remote.Output, remoteQueryExit = remote.ExitCode });
    }

    public async ValueTask DisposeAsync()
    {
        var full = Path.GetFullPath(Root);
        var temp = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(temp, StringComparison.OrdinalIgnoreCase)
            || !Path.GetFileName(full).StartsWith("antiphon-c448-", StringComparison.Ordinal))
            throw new InvalidOperationException("Fixture disposal escaped owned root");
        await CaptureAsync("terminal_image");
        // Fixtures with junctions must remove their own link before this final disposal.
        // A process the fixture's runner started (a PtyHost's own child host) can still be exiting, holding an image
        // it loaded from the tree, when its parent has already been reaped: retry for a short while. A process that
        // never exits still fails the delete, so a real leak stays visible.
        var deadline = DateTime.UtcNow.AddSeconds(5);
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                foreach (var file in Directory.EnumerateFiles(full, "*", SearchOption.AllDirectories))
                    File.SetAttributes(file, FileAttributes.Normal);
                Directory.Delete(full, true);
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException && DateTime.UtcNow < deadline
                && Directory.Exists(full))
            {
                await Task.Delay(Math.Min(100 * attempt, 500));
            }
        }
    }

    internal class FixtureGit(string home, Guid taskId) : LandingGit
    {
        public List<string[]> Trace { get; } = [];
        public string? HooksPathOverride { get; set; }
        /// <summary>CARD-0688: every command with the directory it ran in.</summary>
        public List<(string Directory, string[] Arguments)> Commands { get; } = [];
        public List<string> IndexLockProbes { get; } = [];
        public override Task<LandingIndexLockObservation> InspectIndexLockAsync(string checkout, CancellationToken ct)
        {
            IndexLockProbes.Add(checkout);
            return base.InspectIndexLockAsync(checkout, ct);
        }
        public Func<IReadOnlyList<string>, Task>? BeforeObservedCommand { get; set; }
        public Func<string, IReadOnlyList<string>, Task<LandingGitResult?>>? BeforeCommand { get; set; }
        public Func<string, IReadOnlyList<string>, LandingGitResult, Task>? AfterCommand { get; set; }
        public Func<string, Task>? BeforeUnregister { get; set; }
        public Func<string, Task>? AfterUnregister { get; set; }
        public List<string> RegistrationDrops { get; } = [];
        private readonly System.Collections.Concurrent.ConcurrentDictionary<int, byte> _children = new();

        /// <summary>CARD-1436: the index-lock census sees only this fixture's own git children, never the host's.</summary>
        protected override GitIndexLock.IProcessSource GitProcesses => new ChildProcesses(_children);

        protected override Process? StartProcess(ProcessStartInfo start)
        {
            TestTiming.RemovePhaseVariables(start);
            int? argc = start.ArgumentList.Count > 0
                ? start.ArgumentList.Count
                : string.IsNullOrEmpty(start.Arguments) ? 0 : null;
            var exe = string.IsNullOrEmpty(start.FileName) ? "omitted" : Path.GetFileName(start.FileName);
            // The fixture's "process-wait git <command>" span; the server's own "git <verb>" span may sit inside it.
            for (var span = System.Diagnostics.Activity.Current; span is not null; span = span.Parent)
                if (span.DisplayName.StartsWith("process-wait", StringComparison.Ordinal))
                {
                    span.SetTag("git.spawned", true);
                    break;
                }
            using var spawn = TestTiming.BeginProcess(exe, argc, "process");
            var child = base.StartProcess(start);
            if (child is not null) _children.TryAdd(child.Id, 0);
            return child;
        }

        private sealed class ChildProcesses(System.Collections.Concurrent.ConcurrentDictionary<int, byte> children)
            : GitIndexLock.IProcessSource
        {
            public IReadOnlyList<GitIndexLock.ICensusProcess> GitProcesses()
            {
                var live = new List<GitIndexLock.ICensusProcess>();
                foreach (var pid in children.Keys)
                {
                    try { live.Add(new GitIndexLock.HostProcess(Process.GetProcessById(pid))); }
                    catch (ArgumentException) { children.TryRemove(pid, out _); }
                }
                return live;
            }
        }

        protected override void ConfigureProcess(ProcessStartInfo start)
        {
            start.Environment["HOME"] = home;
            start.Environment["GIT_EDITOR"] = ":";
            start.Environment["GIT_SEQUENCE_EDITOR"] = ":";
            start.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
            start.Environment["GIT_CONFIG_GLOBAL"] = Path.Combine(home, "empty-config");
            start.Environment["GIT_AUTHOR_NAME"] = "C448 Fixture";
            start.Environment["GIT_AUTHOR_EMAIL"] = "fixture@example.invalid";
            start.Environment["GIT_COMMITTER_NAME"] = "C448 Fixture";
            start.Environment["GIT_COMMITTER_EMAIL"] = "fixture@example.invalid";
            start.Environment["GIT_CONFIG_COUNT"] = "3";
            start.Environment["GIT_CONFIG_KEY_0"] = "commit.gpgSign";
            start.Environment["GIT_CONFIG_VALUE_0"] = "false";
            start.Environment["GIT_CONFIG_KEY_1"] = "credential.helper";
            start.Environment["GIT_CONFIG_VALUE_1"] = "";
            start.Environment["GIT_CONFIG_KEY_2"] = "core.hooksPath";
            start.Environment["GIT_CONFIG_VALUE_2"] = HooksPathOverride ?? Path.Combine(home, "no-hooks");
        }

        public override async Task<LandingGitResult> RunAsync(string repository, IReadOnlyList<string> arguments, CancellationToken ct)
        {
            using var wait = TestTiming.BeginPhase(TestPhaseKind.ProcessWait, "wait");
            // The trace span (not the timing label, a CARD-1514 contract) is named by subcommand and
            // covers start to exit; "git.spawned" marks calls that started a process, the rest were
            // answered in process or by a hook.
            if (System.Diagnostics.Activity.Current is { } span && span.DisplayName == "process-wait wait")
            {
                span.DisplayName = "process-wait git " + (arguments.Count > 0 ? arguments[0] : "?");
                if (TestTrace.StacksEnabled)
                {
                    span.SetTag("code.stack", TestTrace.CallerFrames());
                    span.SetTag("git.args", string.Join(" ", arguments.Take(4)));
                }
            }
            Trace.Add(arguments.ToArray());
            Commands.Add((repository, arguments.ToArray()));
            if (BeforeCommand is not null && await BeforeCommand(repository, arguments) is { } injected) return injected;
            if (BeforeObservedCommand is not null) await BeforeObservedCommand(arguments);
            LandingEvidence.Write(taskId, "git_start", new { repository, arguments });
            var result = await base.RunAsync(repository, arguments, ct);
            LandingEvidence.Write(taskId, "git_exit", new { arguments, result.ExitCode, result.RebaseHeadSha });
            if (AfterCommand is not null) await AfterCommand(repository, arguments, result);
            return result;
        }

        protected override async Task<(int ExitCode, byte[] Output)> RunGitInputBytesAsync(
            string repository, IReadOnlyList<string> arguments, byte[] input, CancellationToken ct)
        {
            Trace.Add(arguments.ToArray());
            Commands.Add((repository, arguments.ToArray()));
            if (BeforeCommand is not null && await BeforeCommand(repository, arguments) is { } injected)
                return (injected.ExitCode, System.Text.Encoding.UTF8.GetBytes(injected.Output));
            var result = await base.RunGitInputBytesAsync(repository, arguments, input, ct);
            if (AfterCommand is not null)
                await AfterCommand(repository, arguments,
                    new LandingGitResult(result.ExitCode, "", result.ExitCode == 0 ? "" : $"git_exit_{result.ExitCode}"));
            return result;
        }

        /// <summary>
        /// Production drops the registration with plain file I/O (CARD-0665 review 0c0b9a4e); it is
        /// traced and hookable under this vector so fault cuts at registration removal keep their place.
        /// </summary>
        public static string[] UnregisterVector(string worktreePath) => ["worktree", "remove", "--registration-only", worktreePath];

        public override async Task<LandingGitResult> UnregisterWorktreeAsync(string repository, string worktreePath,
            string gitDirectory, CancellationToken ct)
        {
            var arguments = UnregisterVector(worktreePath);
            Trace.Add(arguments);
            if (BeforeUnregister is not null) await BeforeUnregister(gitDirectory);
            if (BeforeCommand is not null && await BeforeCommand(repository, arguments) is { } injected) return injected;
            if (BeforeObservedCommand is not null) await BeforeObservedCommand(arguments);
            LandingEvidence.Write(taskId, "git_start", new { repository, arguments });
            var result = await base.UnregisterWorktreeAsync(repository, worktreePath, gitDirectory, ct);
            if (result.Succeeded)
            {
                Directory.Exists(gitDirectory).ShouldBeFalse("the real administrative entry was dropped");
                // This independent test oracle is not a production cleanup command. Keep its
                // real registration proof outside the land's measured operation scope (V-18).
                var observer = new FixtureGit(home, taskId);
                var observed = await observer.RunAsync(repository, ["worktree", "list", "--porcelain", "-z"], ct);
                observed.Succeeded.ShouldBeTrue();
                ParseRegistrations(observed.Output).ShouldNotContain(r => PathsEqual(r.Path, worktreePath));
                RegistrationDrops.Add(worktreePath);
                if (AfterUnregister is not null) await AfterUnregister(gitDirectory);
            }
            LandingEvidence.Write(taskId, "git_exit", new { arguments, result.ExitCode, result.Diagnostic });
            if (AfterCommand is not null) await AfterCommand(repository, arguments, result);
            return result;
        }

        public override async Task<LandingGitResult> RunOwnedAsync(string repository, IReadOnlyList<string> arguments,
            Func<int, long, CancellationToken, Task> started, CancellationToken ct)
        {
            Trace.Add(arguments.ToArray());
            Commands.Add((repository, arguments.ToArray()));
            if (BeforeCommand is not null && await BeforeCommand(repository, arguments) is { } injected) return injected;
            if (BeforeObservedCommand is not null) await BeforeObservedCommand(arguments);
            LandingEvidence.Write(taskId, "owned_git_start", new { repository, arguments });
            var result = await base.RunOwnedAsync(repository, arguments, started, ct);
            LandingEvidence.Write(taskId, "owned_git_exit", new { arguments, result.ExitCode, result.RebaseHeadSha });
            if (AfterCommand is not null) await AfterCommand(repository, arguments, result);
            return result;
        }
    }
}
