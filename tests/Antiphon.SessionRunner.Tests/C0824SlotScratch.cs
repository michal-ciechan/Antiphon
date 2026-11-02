using System.Diagnostics;
using System.Security.Cryptography;
using Antiphon.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Microsoft.Extensions.Time.Testing;

namespace Antiphon.SessionRunner.Tests;

/// <summary>
/// CARD-0824 S1. A scratch origin, runner clone and allowed root for the workspace slot pool.
/// Copied from <c>RunnerWorkspaceBindingTests.Scratch</c> (private there). Origin has <c>main</c>
/// (the published branch) and three task branches; C deletes project Q (stale-output residue).
/// </summary>
internal sealed class C0824SlotScratch : IDisposable
{
    public const string BranchA = "feat/card-task-aaaaaaaa";
    public const string BranchB = "feat/card-task-bbbbbbbb";
    public const string BranchC = "feat/card-task-cccccccc";
    public static readonly Guid TaskA = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000001");
    public static readonly Guid TaskB = Guid.Parse("bbbbbbbb-0000-4000-8000-000000000002");
    public static readonly Guid TaskC = Guid.Parse("cccccccc-0000-4000-8000-000000000003");
    public static readonly DateTimeOffset T0 = new(2026, 11, 2, 10, 0, 0, TimeSpan.Zero);
    public static readonly DateTime OldMtime = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private const string Ignore = """
        bin/
        obj/
        node_modules/
        client/dist/
        .antiphon/
        bin-*/
        .playwright/
        TestResults/
        *.log
        *.trx
        nested/
        scratch-ro/
        """;

    private C0824SlotScratch(string root)
    {
        Root = root;
        Origin = Path.Combine(root, "origin");
        Clone = Path.Combine(root, "repo");
        Work = Path.Combine(root, "work").Replace('\\', '/');
    }

    public string Root { get; }
    public string Origin { get; }
    public string Clone { get; }
    public string Work { get; }
    public string Worktrees => Work + "/worktrees";
    public string Base { get; private set; } = "";
    public string ShaA { get; private set; } = "";
    public string ShaB { get; private set; } = "";
    public string ShaC { get; private set; } = "";
    public FakeTimeProvider Clock { get; } = new(T0);
    public List<string[]> Argv { get; } = [];
    public List<GitFault> Faults { get; } = [];
    public FakeDisk Disk { get; } = new();
    public FakeProcesses Processes { get; } = new();
    public FakeFileSystem FileSystem { get; } = new();

    public static C0824SlotScratch Create()
    {
        var scratch = new C0824SlotScratch(Path.Combine(Path.GetTempPath(), "c0824-ws-" + Guid.NewGuid().ToString("N")));
        scratch.Build();
        return scratch;
    }

    private void Build()
    {
        Directory.CreateDirectory(Origin);
        Directory.CreateDirectory(Work);
        Git(Origin, "init", "--initial-branch=main");
        Git(Origin, "config", "user.email", "c0824@localhost");
        Git(Origin, "config", "user.name", "c0824");
        File.WriteAllText(Path.Combine(Origin, "README.md"), "c0824");
        File.WriteAllText(Path.Combine(Origin, ".gitignore"), Ignore.Replace("\r\n", "\n") + "\n");
        Directory.CreateDirectory(Path.Combine(Origin, "src", "P"));
        Directory.CreateDirectory(Path.Combine(Origin, "src", "Q"));
        File.WriteAllText(Path.Combine(Origin, "src", "P", "P.csproj"), "<Project />");
        File.WriteAllText(Path.Combine(Origin, "src", "Q", "Q.csproj"), "<Project />");
        Git(Origin, "add", "-A");
        Git(Origin, "commit", "-m", "base");
        Base = Git(Origin, "rev-parse", "HEAD").Trim();
        ShaA = Branch(BranchA, "a.txt", "task a");
        ShaB = Branch(BranchB, "b.txt", "task b");
        Git(Origin, "checkout", "-b", BranchC, Base);
        Git(Origin, "rm", "-r", "-q", "src/Q");
        Git(Origin, "commit", "-m", "delete project Q");
        ShaC = Git(Origin, "rev-parse", "HEAD").Trim();
        Git(Origin, "checkout", "main");
        Git(Root, "clone", Origin, Clone);
        Git(Clone, "config", "user.email", "c0824@localhost");
        Git(Clone, "config", "user.name", "c0824");
    }

    private string Branch(string branch, string file, string text)
    {
        Git(Origin, "checkout", "-b", branch, "main");
        File.WriteAllText(Path.Combine(Origin, file), text);
        Git(Origin, "add", file);
        Git(Origin, "commit", "-m", branch);
        var sha = Git(Origin, "rev-parse", "HEAD").Trim();
        Git(Origin, "checkout", "main");
        return sha;
    }

    public static PhoneHomeWorkspaceSlotSettings Settings(Action<PhoneHomeWorkspaceSlotSettings>? edit = null)
    {
        var settings = new PhoneHomeWorkspaceSlotSettings { Enabled = true, Headroom = 2, MaxOverflowSlots = 2 };
        edit?.Invoke(settings);
        return settings;
    }

    public RunnerWorkspaceService Service()
    {
        var emptyConfig = Path.Combine(Root, "empty.gitconfig");
        if (!File.Exists(emptyConfig))
            File.WriteAllText(emptyConfig, "");
        var service = new RunnerWorkspaceService(Clone, Work, Origin, psi =>
        {
            psi.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
            psi.Environment["GIT_CONFIG_GLOBAL"] = emptyConfig;
            psi.Environment["GIT_TERMINAL_PROMPT"] = "0";
            var args = psi.ArgumentList.ToArray();
            lock (Argv)
                Argv.Add([psi.WorkingDirectory.Replace('\\', '/'), .. args]);
            GitFault? fault;
            lock (Faults)
            {
                fault = Faults.FirstOrDefault(item => item.Remaining > 0 && item.Match(psi.WorkingDirectory.Replace('\\', '/'), args));
                if (fault is not null)
                    fault.Remaining--;
            }

            if (fault is not null)
            {
                fault.Hits++;
                var shell = new ProcessStartInfo
                {
                    FileName = "/bin/sh",
                    WorkingDirectory = Root,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                };
                shell.ArgumentList.Add("-c");
                shell.ArgumentList.Add("printf '%s' \"$1\" >&2; exit \"$2\"");
                shell.ArgumentList.Add("fault");
                shell.ArgumentList.Add(fault.Stderr);
                shell.ArgumentList.Add(fault.ExitCode.ToString(System.Globalization.CultureInfo.InvariantCulture));
                return Process.Start(shell);
            }

            return Process.Start(psi);
        }, publishedBranch: "main");
        service.BindRecipient("runner-a", Guid.Parse("11111111-1111-1111-1111-111111111111"));
        return service;
    }

    public RunnerWorkspaceSlotPool Pool(PhoneHomeWorkspaceSlotSettings? settings = null, int capacity = 2,
        RunnerWorkspaceService? service = null, IWorkspaceSlotProcessProbe? processes = null)
    {
        var owner = service ?? Service();
        var pool = new RunnerWorkspaceSlotPool(owner, settings ?? Settings(), capacity, Clock, FileSystem, processes ?? Processes, Disk);
        owner.SlotPool = pool;
        return pool;
    }

    public static WorkspaceSlotLeaseRequest Request(Guid task, string branch, string sha, bool cold = false, Guid? preparation = null) =>
        new(task, preparation ?? Guid.NewGuid(), branch, sha, cold);

    public WorkspaceSlotLeaseRequest RequestA(bool cold = false) => Request(TaskA, BranchA, ShaA, cold);
    public WorkspaceSlotLeaseRequest RequestB(bool cold = false) => Request(TaskB, BranchB, ShaB, cold);
    public WorkspaceSlotLeaseRequest RequestC(bool cold = false) => Request(TaskC, BranchC, ShaC, cold);

    public static Task<PhoneHomeWorkspaceSlotReleaseResponse> Release(RunnerWorkspaceSlotPool pool, WorkspaceSlotLease lease, Guid task) =>
        pool.ReleaseAsync(new PhoneHomeWorkspaceSlotReleaseRequest(lease.SlotId, lease.Generation, task), CancellationToken.None);

    public string Slot(string leaf) => Worktrees + "/" + leaf;

    /// <summary>An extra task (id f00000NN, branch feat/card-task-f00000NN) at the base commit.</summary>
    public WorkspaceSlotLeaseRequest Extra(int n)
    {
        var shortId = "f" + n.ToString("x7", System.Globalization.CultureInfo.InvariantCulture);
        var branch = "feat/card-task-" + shortId;
        if (GitExit(Origin, "rev-parse", "-q", "--verify", "refs/heads/" + branch).Exit != 0)
            Git(Origin, "branch", branch, Base);
        return Request(Guid.Parse(shortId + "-0000-4000-8000-" + n.ToString("x12", System.Globalization.CultureInfo.InvariantCulture)), branch, Base);
    }

    /// <summary>Leases n extra tasks in order and returns the leases.</summary>
    public async Task<List<WorkspaceSlotLease>> LeaseExtras(RunnerWorkspaceSlotPool pool, int count, int first = 1)
    {
        var leases = new List<WorkspaceSlotLease>();
        for (var i = first; i < first + count; i++)
            leases.Add(await pool.LeaseAsync(Extra(i), CancellationToken.None));
        return leases;
    }

    public static Guid ExtraTask(int n) =>
        Guid.Parse("f" + n.ToString("x7", System.Globalization.CultureInfo.InvariantCulture) + "-0000-4000-8000-" + n.ToString("x12", System.Globalization.CultureInfo.InvariantCulture));

    public static void Plant(string directory, string relative, string text = "x", DateTime? mtime = null)
    {
        var path = Path.Combine(directory, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        if (mtime is { } stamp)
            File.SetLastWriteTimeUtc(path, stamp);
    }

    /// <summary>Sorted relative path to sha256 and mtime, excluding the <c>.git</c> file.</summary>
    public static SortedDictionary<string, string> Tree(string directory)
    {
        var tree = new SortedDictionary<string, string>(StringComparer.Ordinal);
        if (!Directory.Exists(directory))
            return tree;
        foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(directory, file).Replace('\\', '/');
            if (relative == ".git" || relative.StartsWith(".git/", StringComparison.Ordinal) || relative.Contains("/.git/", StringComparison.Ordinal))
                continue;
            var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file)));
            tree[relative] = hash + "@" + File.GetLastWriteTimeUtc(file).Ticks;
        }

        return tree;
    }

    public static string GitDir(string worktree) => Git(worktree, "rev-parse", "--path-format=absolute", "--git-dir").Trim();

    public void Dispose()
    {
        try
        {
            foreach (var directory in Directory.EnumerateDirectories(Root, "*", SearchOption.AllDirectories))
                File.SetAttributes(directory, FileAttributes.Directory);
            foreach (var file in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(Root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    public static string Git(string workingDirectory, params string[] args)
    {
        var (exit, stdout, stderr) = GitExit(workingDirectory, args);
        if (exit != 0)
            throw new InvalidOperationException($"git {string.Join(' ', args)} failed: {stderr}");
        return stdout;
    }

    public static (int Exit, string Stdout, string Stderr) GitExit(string workingDirectory, params string[] args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);
        psi.Environment["GIT_TERMINAL_PROMPT"] = "0";
        psi.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
        using var process = Process.Start(psi)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, stdout.GetAwaiter().GetResult(), stderr);
    }
}

/// <summary>A scripted git failure: the matched call runs <c>sh</c> printing the stderr and exiting.</summary>
internal sealed class GitFault(Func<string, string[], bool> match, string stderr, int remaining = 1, int exitCode = 1)
{
    public Func<string, string[], bool> Match { get; } = match;
    public string Stderr { get; } = stderr;
    public int ExitCode { get; } = exitCode;
    public int Remaining { get; set; } = remaining;
    public int Hits { get; set; }

    public static GitFault In(string cwd, string verb, string stderr, int remaining = 1, int exitCode = 1) =>
        new((dir, args) => dir == cwd && args.Length > 0 && args[0] == verb, stderr, remaining, exitCode);
}

internal sealed class FakeDisk : IWorkspaceSlotDiskProbe
{
    public const long GiB = 1024L * 1024 * 1024;
    public long Free { get; set; } = 500 * GiB;
    public long Total { get; set; } = 1000 * GiB;
    public Dictionary<string, long> Bytes { get; } = new(StringComparer.Ordinal);
    public long DefaultBytes { get; set; } = GiB;

    public (long FreeBytes, long TotalBytes) Volume(string path) => (Free, Total);

    public long Measure(string path) =>
        Bytes.TryGetValue(path.Replace('\\', '/').TrimEnd('/'), out var bytes) ? bytes : DefaultBytes;
}

internal sealed class FakeProcesses : IWorkspaceSlotProcessProbe
{
    public Func<string, IReadOnlyList<WorkspaceSlotHolder>> Holders { get; set; } = _ => [];
    public int Calls { get; private set; }

    IReadOnlyList<WorkspaceSlotHolder> IWorkspaceSlotProcessProbe.Holders(string path)
    {
        Calls++;
        return Holders(path);
    }
}

internal sealed class FakeFileSystem : IWorkspaceSlotFileSystem
{
    private readonly PhysicalWorkspaceSlotFileSystem _inner = new();
    public Func<string, Exception?> CopyFault { get; set; } = _ => null;

    public void CreateDirectory(string path) => _inner.CreateDirectory(path);

    public void CopyVerified(string source, string destination)
    {
        if (CopyFault(source.Replace('\\', '/')) is { } fault)
            throw fault;
        _inner.CopyVerified(source, destination);
    }
}
