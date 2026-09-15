using Antiphon.Server.Application.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Antiphon.Tests.TestHelpers;

/// <summary>CARD-0527. Spy seam over <see cref="GitWorkspaceService.RunAsync"/>.</summary>
public sealed class RecordingGitWorkspaceService : GitWorkspaceService
{
    public List<string> Verbs { get; } = [];
    public Func<string[], Task>? BeforeRun { get; set; }
    public int? ForcedCheckIgnoreExit { get; set; }
    public int? ForcedDiffCachedExit { get; set; }

    public RecordingGitWorkspaceService() : base(NullLogger<GitWorkspaceService>.Instance) { }

    protected internal override async Task<(int Code, string Stdout, string Stderr)> RunAsync(
        string workingDirectory, CancellationToken ct, params string[] args)
    {
        await RecordAsync(args);
        if (ForcedDiffCachedExit is int cached
            && args.Length >= 2
            && args[0] == "diff"
            && args.Contains("--cached"))
        {
            return (cached, "", "fatal: diff --cached failed");
        }

        return await base.RunAsync(workingDirectory, ct, args);
    }

    protected internal override async Task<(int Code, string Stdout, string Stderr)> RunWithInputAsync(
        string workingDirectory, string input, CancellationToken ct, params string[] args)
    {
        await RecordAsync(args);
        if (ForcedCheckIgnoreExit is int ignore && args.Length > 0 && args[0] == "check-ignore")
            return (ignore, "", "fatal: check-ignore failed");
        return await base.RunWithInputAsync(workingDirectory, input, ct, args);
    }

    private async Task RecordAsync(string[] args)
    {
        if (args.Length > 0)
            Verbs.Add(args[0]);
        if (BeforeRun is not null)
            await BeforeRun(args);
    }
}
