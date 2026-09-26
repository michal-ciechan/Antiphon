using System.Diagnostics;
using Shouldly;

namespace Antiphon.Tests.TestHelpers;

/// <summary>
/// A real git repo plus an isolated worktree root, both deleted on dispose. Worktree/merge-back
/// behaviour is tested against REAL git because that is where its bugs live — "refusing to fetch
/// into checked-out branch" is not something a fake would ever say.
/// </summary>
public sealed class ScratchGitRepo : IDisposable
{
    public string Path { get; }
    public string WorktreeRoot { get; }
    private string? _bareOrigin;

    public ScratchGitRepo(string prefix = "antiphon-deleg-wt")
    {
        Path = Directory.CreateTempSubdirectory($"{prefix}-repo").FullName;
        WorktreeRoot = Directory.CreateTempSubdirectory($"{prefix}-trees").FullName;
        GitAsync("init", "-b", "master").GetAwaiter().GetResult();
        GitAsync("config", "user.email", "test@antiphon.local").GetAwaiter().GetResult();
        GitAsync("config", "user.name", "Delegation Tests").GetAwaiter().GetResult();
    }

    public async Task CommitFileAsync(string relativePath, string content)
    {
        await File.WriteAllTextAsync(System.IO.Path.Combine(Path, relativePath), content);
        await GitAsync("add", ".");
        await GitAsync("commit", "-m", $"edit {relativePath}");
    }

    public async Task GitAsync(params string[] args) =>
        (await GitInAsync(Path, args)).Ok.ShouldBeTrue($"git {string.Join(' ', args)} must succeed");

    public async Task<string> GitReadAsync(params string[] args)
    {
        var result = await GitInAsync(Path, args);
        result.Ok.ShouldBeTrue($"git {string.Join(' ', args)} must succeed");
        return result.StdOut;
    }

    public sealed record GitResult(bool Ok, string StdOut, string StdErr = "");

    public static async Task<GitResult> GitInAsync(string dir, params string[] args) =>
        await GitInAsync(dir, env: null, args);

    public static async Task<GitResult> GitInAsync(string dir, IReadOnlyDictionary<string, string>? env, params string[] args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = dir,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        if (env is not null)
        {
            foreach (var (key, value) in env)
                psi.Environment[key] = value;
        }
        using var p = Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEndAsync();
        var stderr = p.StandardError.ReadToEndAsync();
        // A stuck git (credential prompt, lock) must not hang the fixture. 60s covers init,
        // commit and a local push; a timeout is a failed command, not a silent success.
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        try
        {
            await p.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            try { p.Kill(entireProcessTree: true); } catch (Exception) { /* already gone */ }
            return new GitResult(false, "", "git timed out after 60s");
        }
        return new GitResult(p.ExitCode == 0, await stdout, await stderr);
    }

    public async Task AddBareOriginAsync()
    {
        _bareOrigin = Directory.CreateTempSubdirectory("c527-origin").FullName;
        (await GitInAsync(_bareOrigin, "init", "--bare")).Ok.ShouldBeTrue();
        await GitAsync("remote", "add", "origin", _bareOrigin);
        await GitAsync("push", "-u", "origin", "master");
    }

    public async Task InstallFailingPreCommitHookAsync(string message)
    {
        var hooks = System.IO.Path.Combine(Path, ".git", "hooks");
        Directory.CreateDirectory(hooks);
        var hook = System.IO.Path.Combine(hooks, "pre-commit");
        var escaped = message.Replace("\"", "\\\"");
        var script = "#!/bin/sh\necho \"" + escaped + "\" >&2\nexit 1\n";
        await File.WriteAllTextAsync(hook, script.Replace("\r\n", "\n"));
        // Linux git ignores a hook that is not executable. Windows git runs it without that bit,
        // and SetUnixFileMode is unsupported there.
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(
                hook,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                | UnixFileMode.GroupRead | UnixFileMode.GroupExecute
                | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        }
    }

    public void Dispose()
    {
        foreach (var dir in new[] { WorktreeRoot, Path, _bareOrigin })
        {
            if (dir is null) continue;
            try
            {
                // git object files are read-only on Windows; strip attributes or Delete throws.
                foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
                    File.SetAttributes(file, FileAttributes.Normal);
                Directory.Delete(dir, recursive: true);
            }
            catch (IOException) { /* best effort */ }
            catch (UnauthorizedAccessException) { /* best effort */ }
        }
    }
}
