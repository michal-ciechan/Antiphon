using Antiphon.Server.Application.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Antiphon.Tests.TestHelpers;

internal sealed class ControlledGitWorkspaceService : GitWorkspaceService
{
    public bool FailStatus { get; set; }
    public bool FailLog { get; set; }

    public ControlledGitWorkspaceService() : base(NullLogger<GitWorkspaceService>.Instance) { }

    public override Task<GitStrictList<GitChange>> TryGetChangesAsync(string workingDirectory, CancellationToken ct) =>
        FailStatus
            ? Task.FromResult(new GitStrictList<GitChange>(false, [], 128))
            : base.TryGetChangesAsync(workingDirectory, ct);

    public override Task<GitStrictList<GitCommit>> TryGetRecentCommitsAsync(
        string workingDirectory, int limit, CancellationToken ct) =>
        FailLog
            ? Task.FromResult(new GitStrictList<GitCommit>(false, [], 128))
            : base.TryGetRecentCommitsAsync(workingDirectory, limit, ct);
}
