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

    public static async Task<GitResult> GitInAsync(string dir, params string[] args)
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
        using var p = Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEndAsync();
        var stderr = p.StandardError.ReadToEndAsync();
        await p.WaitForExitAsync();
        return new GitResult(p.ExitCode == 0, await stdout, await stderr);
    }

    public void Dispose()
    {
        foreach (var dir in new[] { WorktreeRoot, Path })
        {
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
