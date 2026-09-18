using Antiphon.Server.Application.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Antiphon.Tests.TestHelpers;

/// <summary>CARD-0527. Spy seam over <see cref="GitWorkspaceService.RunAsync"/>.</summary>
public sealed class RecordingGitWorkspaceService : GitWorkspaceService
{
    public List<string> Verbs { get; } = [];
    /// <summary>Every recorded invocation's full argument vector (CARD-0527 recipe pins).</summary>
    public List<string[]> Calls { get; } = [];
    public Func<string[], Task>? BeforeRun { get; set; }
    public Func<string[], (int Code, string Stdout, string Stderr)?>? OverrideRun { get; set; }

    public int PostCommitInspectionFailures { get; private set; }

    public void FailPostCommitInspection(string inspection)
    {
        OverrideRun = args =>
        {
            if (!Verbs.Contains("commit") || !(inspection == "sha"
                    ? args[0] == "rev-parse" && args.Contains("HEAD")
                    : args[0] == "diff-tree")) return null;
            PostCommitInspectionFailures++;
            return (128, "", "post-commit inspection unavailable");
        };
    }

    public RecordingGitWorkspaceService() : base(NullLogger<GitWorkspaceService>.Instance) { }

    protected internal override async Task<(int Code, string Stdout, string Stderr)> RunAsync(
        string workingDirectory, CancellationToken ct, params string[] args)
    {
        await RecordAsync(args);
        if (OverrideRun?.Invoke(args) is { } result) return result;
        return await base.RunAsync(workingDirectory, ct, args);
    }

    protected internal override async Task<(int Code, string Stdout, string Stderr)> RunWithInputAsync(
        string workingDirectory, string input, CancellationToken ct, params string[] args)
    {
        await RecordAsync(args);
        if (OverrideRun?.Invoke(args) is { } result) return result;
        return await base.RunWithInputAsync(workingDirectory, input, ct, args);
    }

    private async Task RecordAsync(string[] args)
    {
        Calls.Add(args);
        if (args.Length > 0)
            Verbs.Add(args[0]);
        if (BeforeRun is not null)
            await BeforeRun(args);
    }
}
