using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Files;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;

namespace Antiphon.Tests.TestHelpers;

internal sealed class ReportTempWorkspace : IDisposable
{
    public string Path { get; } = Directory.CreateTempSubdirectory("antiphon-c419-").FullName;
    public void Dispose() => Directory.Delete(Path, true);
}

/// <summary>Every mutation/removal is confined to one disposable, persistent-root Git fixture.</summary>
internal sealed class ReportWorkspace : IAsyncDisposable
{
    public string Root { get; }
    public string Main => Path.Combine(Root, "main space é");
    public string Worktree => Path.Combine(Root, "worktree");
    public ReportWorkspace()
    {
        var checkout = new DirectoryInfo(AppContext.BaseDirectory);
        while (checkout is not null && !File.Exists(Path.Combine(checkout.FullName, "Antiphon.sln"))) checkout = checkout.Parent;
        Root = Path.Combine(checkout?.FullName ?? throw new InvalidOperationException("checkout missing"),
            ".antiphon", "test-output", "card-0419", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Main);
    }
    public async Task InitializeAsync(bool worktree = false)
    {
        await GitAsync(Main, "init", "-b", "main");
        await File.WriteAllTextAsync(Path.Combine(Main, "seed.txt"), "benign fixture");
        await File.WriteAllTextAsync(Path.Combine(Main, ".gitignore"), ".antiphon/\n");
        await GitAsync(Main, "add", "seed.txt", ".gitignore");
        await GitAsync(Main, "-c", "user.name=Fixture", "-c", "user.email=fixture@example.invalid", "commit", "-m", "seed");
        if (worktree) await GitAsync(Main, "worktree", "add", "-b", "delegate", Worktree);
    }
    public AgentReportStore Store(DelegationSettings? settings = null) => new(
        new GitWorkspaceService(NullLogger<GitWorkspaceService>.Instance),
        Options.Create(settings ?? new()), NullLogger<AgentReportStore>.Instance);
    public AgentTask Task(string raw, Guid? id = null) => new()
    {
        Id = id ?? Guid.NewGuid(), Title = "report custody", Goal = "fixture", Result = raw,
        WorkingDirectory = Directory.Exists(Worktree) ? Worktree : Main, RepoPath = Main,
        WorktreePath = Directory.Exists(Worktree) ? Worktree : null,
        ReplyTo = AgentTaskReplyTo.Session, Role = AgentTaskRole.Code, Status = AgentTaskStatus.Succeeded,
    };
    public string Expected(AgentTask task) => Path.Combine(Main, ".antiphon", "reports", task.Id.ToString("D"),
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(task.Result!))) + ".md");
    public async Task<string> GitAsync(string cwd, params string[] args)
    {
        AssertOwned(cwd);
        var psi = new ProcessStartInfo("git") { WorkingDirectory = cwd, RedirectStandardOutput = true,
            RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (var arg in args) psi.ArgumentList.Add(arg);
        using var process = Process.Start(psi)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var error = await stderr;
        process.ExitCode.ShouldBe(0, error);
        return await stdout;
    }
    public async Task RemoveWorktreeAsync()
    {
        AssertOwned(Worktree);
        await GitAsync(Main, "worktree", "remove", "--force", Worktree);
    }
    public ValueTask DisposeAsync()
    {
        AssertOwned(Main);
        if (Directory.Exists(Root))
        {
            foreach (var file in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories)) File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(Root, true);
        }
        return ValueTask.CompletedTask;
    }
    private void AssertOwned(string path) => Path.GetFullPath(path).StartsWith(Root + Path.DirectorySeparatorChar,
        StringComparison.OrdinalIgnoreCase).ShouldBeTrue("fixture owns path before process/removal");
}
