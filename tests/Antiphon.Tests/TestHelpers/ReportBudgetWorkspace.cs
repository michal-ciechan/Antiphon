using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Files;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Antiphon.Tests.TestHelpers;

/// <summary>
/// Owned Git cases for CARD-1605. The template is seeded once per host; each case is a copy.
/// Deadline witnesses construct the store at production defaults. The custody helper's 30-second
/// override is a different fixture.
/// </summary>
internal sealed class ReportBudgetWorkspace : IAsyncDisposable
{
    private static readonly SemaphoreSlim TemplateLock = new(1, 1);
    private static string? _template;

    public string Root { get; }
    public string Main => Path.Combine(Root, "main");

    private ReportBudgetWorkspace(string root) => Root = root;

    public static async Task<ReportBudgetWorkspace> CreateAsync()
    {
        var template = await TemplateAsync();
        var root = Path.Combine(CasesRoot(), Guid.NewGuid().ToString("N"));
        CopyTree(template, Path.Combine(root, "main"));
        return new ReportBudgetWorkspace(root);
    }

    public AgentReportStore CreateStore(GitProcessGate? gate = null, GitSettings? gitSettings = null,
        ILogger<AgentReportStore>? logger = null)
    {
        var git = new GitWorkspaceService(
            NullLogger<GitWorkspaceService>.Instance,
            gate,
            gitSettings is null ? null : Options.Create(gitSettings));
        return new AgentReportStore(
            git, Options.Create(new DelegationSettings()), logger ?? NullLogger<AgentReportStore>.Instance);
    }

    public AgentTask Task(string raw, Guid? id = null) => new()
    {
        Id = id ?? Guid.NewGuid(),
        Title = "report budget",
        Goal = "fixture",
        Result = raw,
        WorkingDirectory = Main,
        RepoPath = Main,
        ReplyTo = AgentTaskReplyTo.Session,
        Role = AgentTaskRole.Code,
        Status = AgentTaskStatus.Succeeded,
    };

    public string Expected(AgentTask task)
    {
        var bytes = new UTF8Encoding(false, true).GetBytes(task.Result ?? "");
        var digest = Convert.ToHexStringLower(SHA256.HashData(bytes));
        return Path.Combine(Main, ".antiphon", "reports", task.Id.ToString("D"), digest + ".md");
    }

    public string CreateDelayedGit(string entryMarker, double delaySeconds = 2.5)
    {
        var real = ResolveGitExecutable();
        var path = Path.Combine(Root, "delayed-git.sh");
        var once = entryMarker + ".once";
        var script = "#!/bin/sh\n"
            + "if [ ! -f " + Quote(once) + " ]; then\n"
            + "  printf '%s\\n' entered > " + Quote(entryMarker) + "\n"
            + "  : > " + Quote(once) + "\n"
            + "  sleep " + delaySeconds.ToString(CultureInfo.InvariantCulture) + "\n"
            + "fi\n"
            + "exec " + Quote(real) + " \"$@\"\n";
        File.WriteAllText(path, script.Replace("\r\n", "\n"));
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return path;
    }

    // Match ScratchGitRepo and CheckpointFixtureProcess: the host resolves PATH and git.exe.
    public static string ResolveGitExecutable() => "git";

    public async ValueTask DisposeAsync()
    {
        if (!Directory.Exists(Root)) return;
        foreach (var file in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories))
            File.SetAttributes(file, FileAttributes.Normal);
        Directory.Delete(Root, true);
        await ValueTask.CompletedTask;
    }

    private static async Task<string> TemplateAsync()
    {
        await TemplateLock.WaitAsync();
        try
        {
            if (_template is not null && Directory.Exists(Path.Combine(_template, ".git")))
                return _template;
            var template = Path.Combine(HostRoot(), "template");
            if (Directory.Exists(template)) Directory.Delete(template, true);
            Directory.CreateDirectory(template);
            await GitAsync(template, "init", "-b", "main");
            await File.WriteAllTextAsync(Path.Combine(template, "seed.txt"), "benign fixture");
            await File.WriteAllTextAsync(Path.Combine(template, ".gitignore"), ".antiphon/\n");
            await GitAsync(template, "add", "seed.txt", ".gitignore");
            await GitAsync(template, "-c", "user.name=Fixture", "-c", "user.email=fixture@example.invalid",
                "commit", "-m", "seed");
            _template = template;
            return template;
        }
        finally { TemplateLock.Release(); }
    }

    private static async Task GitAsync(string cwd, params string[] args)
    {
        var result = await ScratchGitRepo.GitInAsync(cwd, args);
        if (!result.Ok)
            throw new InvalidOperationException("git " + string.Join(' ', args) + ": " + result.StdErr);
    }

    private static string HostRoot()
    {
        var checkout = new DirectoryInfo(AppContext.BaseDirectory);
        while (checkout is not null && !File.Exists(Path.Combine(checkout.FullName, "Antiphon.sln")))
            checkout = checkout.Parent;
        var root = Path.Combine(checkout?.FullName ?? throw new InvalidOperationException("checkout missing"),
            ".antiphon", "test-output", "card-1605");
        Directory.CreateDirectory(root);
        return root;
    }

    private static string CasesRoot()
    {
        var root = Path.Combine(HostRoot(), "cases");
        Directory.CreateDirectory(root);
        return root;
    }

    private static void CopyTree(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source))
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: true);
        foreach (var dir in Directory.EnumerateDirectories(source))
            CopyTree(dir, Path.Combine(destination, Path.GetFileName(dir)));
    }

    private static string Quote(string value) => "'" + value.Replace("'", "'\\''", StringComparison.Ordinal) + "'";
}
