using System.Diagnostics;
using Antiphon.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Shouldly;

namespace Antiphon.SessionRunner.Tests;

/// <summary>
/// CARD-1677 F-1. A scratch bare origin, a runner repository cloned from it and a temp allowed root whose
/// <c>worktrees</c> directory holds <c>task-&lt;8 hex&gt;</c> mirrors. Every path is under a fresh
/// <c>c1677-ret-&lt;32 hex&gt;</c> temp directory: no test ever names a real worktree root. Git argv of the
/// engine is recorded through the service's process-start seam, and <see cref="Faults"/> scripts failures.
/// </summary>
internal sealed class C1677RetentionScratch : IDisposable
{
    private const string Ignore = ".antiphon/\nbin/\nobj/\n";

    private C1677RetentionScratch(string root)
    {
        Root = root.Replace('\\', '/');
        OriginGit = Root + "/origin.git";
        Seed = Root + "/seed";
        Repository = Root + "/repos/antiphon";
        Work = Root + "/work";
    }

    public string Root { get; }
    public string OriginGit { get; }
    public string Seed { get; }
    public string Repository { get; }
    public string Work { get; }
    public string Worktrees => Work + "/worktrees";
    public string Retiring => Work + "/worktrees-retiring";
    public List<(string Cwd, string[] Args)> Argv { get; } = [];
    public List<RetentionFault> Faults { get; } = [];
    public FakeHolderScan Holders { get; } = new();
    public Func<string, ulong?>? Device { get; set; }
    private RunnerWorkspaceService? _service;

    public sealed record MirrorTree(string Path, string Branch, Guid TaskId, string Head);

    public static C1677RetentionScratch Create()
    {
        var scratch = new C1677RetentionScratch(Path.Combine(Path.GetTempPath(), "c1677-ret-" + Guid.NewGuid().ToString("N")));
        scratch.Build();
        return scratch;
    }

    private void Build()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(Worktrees);
        Git(Root, "init", "--bare", "--initial-branch=main", OriginGit);
        Git(Root, "init", "--initial-branch=main", Seed);
        Identity(Seed);
        File.WriteAllText(Seed + "/README.md", "c1677");
        File.WriteAllText(Seed + "/.gitignore", Ignore);
        File.WriteAllText(Seed + "/a", "a");
        Git(Seed, "add", "-A");
        Git(Seed, "commit", "-m", "base");
        Git(Seed, "remote", "add", "origin", OriginGit);
        Git(Seed, "push", "origin", "main");
        Directory.CreateDirectory(Root + "/repos");
        Git(Root, "clone", OriginGit, Repository);
        Identity(Repository);
    }

    public static Guid TaskOf(string id8) => Guid.Parse(id8 + "-0000-4000-8000-000000001677");

    /// <summary>A clean, pushed mirror on <c>feat/card-task-&lt;id8&gt;</c> (tracking ref fetched) at <paramref name="path"/>.</summary>
    public MirrorTree Mirror(string id8 = "a1677001", string? path = null, string? branch = null)
    {
        branch ??= "feat/card-task-" + id8;
        path ??= Worktrees + "/task-" + id8;
        PushBranch(branch, id8);
        Git(Repository, "fetch", "origin", "refs/heads/" + branch + ":refs/remotes/origin/" + branch);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        Git(Repository, "worktree", "add", "-b", branch, path, "refs/remotes/origin/" + branch);
        return new MirrorTree(path, branch, TaskOf(id8), Git(path, "rev-parse", "HEAD").Trim());
    }

    private void PushBranch(string branch, string marker)
    {
        if (GitExit(Seed, "rev-parse", "-q", "--verify", "refs/heads/" + branch).Exit == 0)
            return;
        Git(Seed, "checkout", "-q", "-b", branch, "main");
        File.WriteAllText(Seed + "/" + marker + ".txt", marker + " " + branch);
        Git(Seed, "add", "-A");
        Git(Seed, "commit", "-q", "-m", branch);
        Git(Seed, "push", "-q", "origin", branch);
        Git(Seed, "checkout", "-q", "main");
    }

    /// <summary>A registration whose directory was deleted: <c>git worktree prune</c> would drop it.</summary>
    public string StaleRegistration(string id8)
    {
        var m = Mirror(id8);
        Directory.Delete(m.Path, recursive: true);
        Registered(m.Path).ShouldBeTrue("the stale registration is a precondition");
        return m.Path;
    }

    public RunnerWorkspaceService Service()
    {
        if (_service is not null)
            return _service;
        var emptyConfig = Root + "/empty.gitconfig";
        if (!File.Exists(emptyConfig))
            File.WriteAllText(emptyConfig, "");
        _service = new RunnerWorkspaceService(Repository, Work, OriginGit, psi =>
        {
            psi.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
            if (!psi.Environment.ContainsKey("GIT_CONFIG_GLOBAL"))
                psi.Environment["GIT_CONFIG_GLOBAL"] = emptyConfig;
            psi.Environment["GIT_TERMINAL_PROMPT"] = "0";
            var cwd = psi.WorkingDirectory.Replace('\\', '/');
            var args = psi.ArgumentList.ToArray();
            lock (Argv)
                Argv.Add((cwd, args));
            RetentionFault? fault;
            lock (Faults)
            {
                fault = Faults.FirstOrDefault(item => item.Remaining > 0 && item.Match(cwd, Strip(args)));
                if (fault is not null)
                    fault.Remaining--;
            }

            if (fault is null)
                return Process.Start(psi);
            fault.Hits++;
            var shell = new ProcessStartInfo
            {
                FileName = "/bin/sh", WorkingDirectory = Root, UseShellExecute = false,
                RedirectStandardOutput = true, RedirectStandardError = true,
            };
            shell.ArgumentList.Add("-c");
            shell.ArgumentList.Add("sleep \"$3\"; printf '%s' \"$1\" >&2; exit \"$2\"");
            shell.ArgumentList.Add("fault");
            shell.ArgumentList.Add(fault.Stderr);
            shell.ArgumentList.Add(fault.ExitCode.ToString(System.Globalization.CultureInfo.InvariantCulture));
            shell.ArgumentList.Add(fault.SleepSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture));
            return Process.Start(shell);
        }, publishedBranch: "main");
        return _service;
    }

    public RunnerWorkspaceRetention Engine(string? retiringRoot = null) =>
        new(Service(), Holders, Device, retiringRoot ?? Retiring);

    public static PhoneHomeWorkspaceRetainRequest Request(MirrorTree m, string mode = WorkspaceRetainModes.Execute,
        Guid? taskId = null, string? branch = null, string? path = null) =>
        new(taskId ?? m.TaskId, path ?? m.Path, branch ?? m.Branch, mode, Guid.NewGuid());

    /// <summary>The argv without the inspection runner's global options (<c>--no-replace-objects</c>, <c>--git-dir=</c>).</summary>
    public static string[] Strip(string[] args) =>
        args.SkipWhile(arg => arg.StartsWith('-')).ToArray();

    public List<(string Cwd, string[] Args)> Calls(string verb, string? sub = null)
    {
        lock (Argv)
            return Argv.Select(call => (call.Cwd, Args: Strip(call.Args)))
                .Where(call => call.Args.Length > 0 && call.Args[0] == verb
                    && (sub is null || (call.Args.Length > 1 && call.Args[1] == sub)))
                .ToList();
    }

    private static readonly string[] MutatingWorktree = ["move", "remove", "prune", "add", "lock", "unlock", "repair"];
    private static readonly string[] MutatingVerbsList = ["fetch", "push", "branch", "reset", "clean", "checkout", "update-ref", "gc", "rm"];

    public List<string> MutatingVerbs()
    {
        lock (Argv)
            return Argv.Select(call => Strip(call.Args))
                .Where(args => args.Length > 0 && ((args[0] == "worktree" && args.Length > 1 && MutatingWorktree.Contains(args[1]))
                    || MutatingVerbsList.Contains(args[0])))
                .Select(args => string.Join(' ', args))
                .ToList();
    }

    public bool Registered(string path)
    {
        var expected = "worktree " + System.IO.Path.GetFullPath(path).Replace('\\', '/').TrimEnd('/');
        return Git(Repository, "worktree", "list", "--porcelain").Replace("\r\n", "\n").Split('\n').Contains(expected);
    }

    public List<string> RetiringEntries() =>
        Directory.Exists(Retiring) ? Directory.EnumerateFileSystemEntries(Retiring).Select(e => e.Replace('\\', '/')).ToList() : [];

    /// <summary>F-1's refusal block: exact reason, the tree untouched at its path, registered, nothing retiring, no git mutation.</summary>
    public void Kept(PhoneHomeWorkspaceRetainResponse response, string reason, MirrorTree m, SortedDictionary<string, string> before)
    {
        response.Verdict.ShouldBe(WorkspaceRetentionCodes.Kept, response.Detail);
        response.Reason.ShouldBe(reason, response.Detail);
        Directory.Exists(m.Path).ShouldBeTrue("the tree stays at its path");
        Tree(m.Path).ShouldBe(before, "the tree content is unchanged");
        Registered(m.Path).ShouldBeTrue("the registration stays");
        RetiringEntries().ShouldBeEmpty("nothing was moved aside");
        MutatingVerbs().ShouldBeEmpty("no git mutation ran");
    }

    /// <summary>A commit in the mirror pushed by URL: live origin has it, refs/remotes/origin does not.</summary>
    public string PushByUrl(MirrorTree m)
    {
        CommitIn(m.Path, "by-url.txt");
        Git(m.Path, "push", "-q", OriginGit, "HEAD:refs/heads/" + m.Branch);
        return Git(m.Path, "rev-parse", "HEAD").Trim();
    }

    public void CommitIn(string path, string file)
    {
        File.WriteAllText(path + "/" + file, file);
        Git(path, "add", file);
        Git(path, "commit", "-q", "-m", file);
    }

    public void ResetOriginBehind(MirrorTree m) => Git(OriginGit, "update-ref", "refs/heads/" + m.Branch, m.Head + "~1");

    public void DeleteOriginBranch(MirrorTree m) => Git(OriginGit, "update-ref", "-d", "refs/heads/" + m.Branch);

    public void Decoy(MirrorTree m) => Git(OriginGit, "update-ref", "refs/heads/x/refs/heads/" + m.Branch, m.Head);

    /// <summary>A descendant of HEAD pushed from the seed and never fetched by the runner repository.</summary>
    public void AdvanceOriginFromSide(MirrorTree m)
    {
        Git(Seed, "checkout", "-q", m.Branch);
        CommitIn(Seed, "side.txt");
        Git(Seed, "push", "-q", "origin", m.Branch);
        Git(Seed, "checkout", "-q", "main");
    }

    public static SortedDictionary<string, string> Tree(string directory) => C0824SlotScratch.Tree(directory);

    public static void Plant(string directory, string relative, string text = "x") => C0824SlotScratch.Plant(directory, relative, text);

    private static void Identity(string repository)
    {
        Git(repository, "config", "user.email", "c1677@localhost");
        Git(repository, "config", "user.name", "c1677");
    }

    public static string Git(string workingDirectory, params string[] args) => C0824SlotScratch.Git(workingDirectory, args);

    public static (int Exit, string Stdout, string Stderr) GitExit(string workingDirectory, params string[] args) =>
        C0824SlotScratch.GitExit(workingDirectory, args);


    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Root))
                Directory.Delete(Root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}

/// <summary>CARD-1677 F-1. A scripted git failure matched on cwd and the verb after global options.</summary>
internal sealed class RetentionFault(Func<string, string[], bool> match, string stderr, int remaining = 1, int exitCode = 1, int sleepSeconds = 0)
{
    public Func<string, string[], bool> Match { get; } = match;
    public string Stderr { get; } = stderr;
    public int ExitCode { get; } = exitCode;
    public int SleepSeconds { get; } = sleepSeconds;
    public int Remaining { get; set; } = remaining;
    public int Hits { get; set; }

    public static RetentionFault In(string cwd, string verb, string stderr, int remaining = 1, int exitCode = 1) =>
        new((dir, args) => dir == cwd && args.Length > 0 && args[0] == verb, stderr, remaining, exitCode);

    public static RetentionFault InSub(string cwd, string verb, string sub, string stderr, int exitCode = 128) =>
        new((dir, args) => dir == cwd && args.Length > 1 && args[0] == verb && args[1] == sub, stderr, 1, exitCode);

    /// <summary>Holds the next matching call for <paramref name="seconds"/> (the ls-remote budget witness).</summary>
    public static RetentionFault Sleep(string cwd, string verb, int seconds) =>
        new((dir, args) => dir == cwd && args.Length > 0 && args[0] == verb, "", 1, 0, seconds);
}

/// <summary>CARD-1677 F-1. A scripted fail-closed holder scan.</summary>
internal sealed class FakeHolderScan : IWorkspaceHolderScan
{
    public Func<string, HolderScan> Next { get; set; } = _ => new HolderScan(true, []);
    public int Calls { get; private set; }

    public HolderScan Scan(string path)
    {
        Calls++;
        return Next(path);
    }
}
