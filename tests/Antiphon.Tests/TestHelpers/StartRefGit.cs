using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using TUnit.Core.Exceptions;

namespace Antiphon.Tests.TestHelpers;

/// <summary>
/// CARD-0666. Git fixtures for start-ref availability: a repository with a local bare origin, and
/// a <see cref="SilentOrigin"/> that accepts a git:// connection and never answers. Every git call
/// here is bounded, and nothing depends on the host OS or a network beyond loopback.
/// </summary>
internal static class StartRefGit
{
    private static readonly TimeSpan GitTimeout = TimeSpan.FromSeconds(60);

    public static async Task SkipIfGitUnavailableAsync()
    {
        try
        {
            await GitAsync(Environment.CurrentDirectory, "--version");
        }
        catch (Exception ex)
        {
            throw new SkipTestException($"git is required for start-ref integration tests: {ex.Message}");
        }
    }

    public static string NewRoot() => Path.Combine(Path.GetTempPath(), $"antiphon-startref-{Guid.NewGuid():N}");

    /// <summary>A repository with one commit and, unless <paramref name="withOrigin"/> is false, a bare origin.</summary>
    public static async Task<(string Repo, string Worktrees, string Origin)> CreateRepoAsync(string root, bool withOrigin = true)
    {
        var repo = Path.Combine(root, "repo");
        var worktrees = Path.Combine(root, "worktrees");
        var origin = Path.Combine(root, "origin.git");
        Directory.CreateDirectory(repo);
        Directory.CreateDirectory(worktrees);
        await GitAsync(repo, "init", "--quiet");
        await CommitAsync(repo, "README.md", "# Test Repo");
        if (withOrigin)
        {
            Directory.CreateDirectory(origin);
            await GitAsync(origin, "init", "--quiet", "--bare");
            await GitAsync(repo, "remote", "add", "origin", origin);
            await GitAsync(repo, "push", "--quiet", "origin", "HEAD:refs/heads/master");
        }
        return (repo, worktrees, origin);
    }

    /// <summary>
    /// Another clone pushes two commits to a task branch and returns the FIRST: reachable on origin,
    /// not a branch tip, and never fetched into <paramref name="repo"/>.
    /// </summary>
    public static async Task<string> PushOriginOnlyCommitAsync(string root, string origin)
    {
        var other = Path.Combine(root, "other-" + Guid.NewGuid().ToString("N")[..8]);
        await GitAsync(root, "clone", "--quiet", origin, other);
        var sha = await CommitAsync(other, "task.txt", "one");
        await CommitAsync(other, "task.txt", "two");
        await GitAsync(other, "push", "--quiet", "origin", "HEAD:refs/heads/feat/card-task-0badc0de");
        return sha;
    }

    public static async Task<bool> ResolvesAsync(string repo, string revision) =>
        (await TryGitAsync(repo, "rev-parse", "--verify", "--quiet", revision + "^{commit}")).ExitCode == 0;

    public static async Task<string> CommitAsync(string repo, string file, string content)
    {
        await File.WriteAllTextAsync(Path.Combine(repo, file), content);
        await GitAsync(repo, "add", file);
        await GitAsync(repo, "-c", "user.email=test@antiphon.dev", "-c", "user.name=Antiphon Test",
            "commit", "--quiet", "-m", content);
        return (await GitAsync(repo, "rev-parse", "HEAD")).Trim();
    }

    public static async Task<string> GitAsync(string workingDirectory, params string[] arguments)
    {
        var result = await TryGitAsync(workingDirectory, arguments);
        if (result.ExitCode != 0)
            throw new InvalidOperationException(
                $"git {string.Join(" ", arguments)} failed with exit code {result.ExitCode}: {result.Stderr}");
        return result.Stdout;
    }

    public static async Task<(int ExitCode, string Stdout, string Stderr)> TryGitAsync(
        string workingDirectory, params string[] arguments)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        psi.Environment["GIT_TERMINAL_PROMPT"] = "0";
        foreach (var argument in arguments)
            psi.ArgumentList.Add(argument);

        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start git.");
        using var cts = new CancellationTokenSource(GitTimeout);
        var stdoutTask = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var stderrTask = process.StandardError.ReadToEndAsync(CancellationToken.None);
        try
        {
            await process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { /* best-effort */ }
            throw new System.TimeoutException($"git {string.Join(" ", arguments)} timed out after {GitTimeout.TotalSeconds:0}s");
        }
        return (process.ExitCode, await stdoutTask, await stderrTask);
    }

    public static void DeleteDirectory(string path)
    {
        if (!Directory.Exists(path))
            return;
        try
        {
            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(path, recursive: true);
        }
        catch
        {
            // Best-effort cleanup.
        }
    }

    /// <summary>
    /// A loopback git:// origin that accepts connections and never answers, so a fetch against it
    /// hangs until its caller's budget kills it. <see cref="Accepted"/> counts connection attempts:
    /// zero proves a code path made no network call.
    /// </summary>
    public sealed class SilentOrigin : IAsyncDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly List<TcpClient> _held = [];
        private readonly CancellationTokenSource _stop = new();
        private readonly Task _accepting;
        private int _accepted;

        public SilentOrigin()
        {
            _listener.Start();
            _accepting = AcceptAsync();
        }

        public string Url => $"git://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/silent.git";
        public int Accepted => Volatile.Read(ref _accepted);

        public async Task WaitForConnectionAsync(TimeSpan within)
        {
            var deadline = DateTime.UtcNow + within;
            while (Accepted == 0 && DateTime.UtcNow < deadline)
                await Task.Delay(50);
        }

        private async Task AcceptAsync()
        {
            try
            {
                while (!_stop.IsCancellationRequested)
                {
                    var client = await _listener.AcceptTcpClientAsync(_stop.Token);
                    lock (_held) _held.Add(client);
                    Interlocked.Increment(ref _accepted);
                }
            }
            catch (Exception ex) when (ex is OperationCanceledException or SocketException or ObjectDisposedException)
            {
            }
        }

        public async ValueTask DisposeAsync()
        {
            await _stop.CancelAsync();
            _listener.Stop();
            try { await _accepting; } catch { /* stopped */ }
            lock (_held)
            {
                foreach (var client in _held) client.Dispose();
                _held.Clear();
            }
            _stop.Dispose();
        }
    }
}
