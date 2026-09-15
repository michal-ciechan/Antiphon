using Antiphon.Server.Application.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Antiphon.Tests.TestHelpers;

public sealed class RecordingGitWorkspaceService() : GitWorkspaceService(NullLogger<GitWorkspaceService>.Instance)
{
    public List<string> Verbs { get; } = [];
    public Func<string[], Task>? BeforeRun { get; set; }

    protected internal override async Task<(int Code, string Stdout, string Stderr)> RunAsync(
        string workingDirectory, CancellationToken ct, params string[] args)
    {
        var command = args.SkipWhile(a => a.StartsWith("--", StringComparison.Ordinal)).ToArray();
        Verbs.Add(command[0]);
        if (BeforeRun is not null) await BeforeRun(command);
        return await base.RunAsync(workingDirectory, ct, args);
    }

    protected internal override Task<(int Code, string Stdout, string Stderr)> RunWithInputAsync(
        string workingDirectory, string input, string[] args, CancellationToken ct)
    {
        Verbs.Add(args[0]);
        return base.RunWithInputAsync(workingDirectory, input, args, ct);
    }
}
