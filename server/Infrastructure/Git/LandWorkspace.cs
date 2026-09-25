using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Infrastructure.Git;

/// <summary>CARD-0688 S1 seam: not implemented yet (red-first commit).</summary>
public sealed class LandWorkspace(ILandingGit git, IOptions<GitSettings> settings, TimeProvider clock) : ILandWorkspace
{
    private readonly ILandingGit _git = git;
    private readonly GitSettings _settings = settings.Value;
    private readonly TimeProvider _clock = clock;

    public string PathFor(string commonDirectory) => throw new NotImplementedException("CARD-0688 S1");

    public Task<LandWorkspaceState> EnsureAsync(string repository, string path, string sha,
        Func<string, IReadOnlyList<string>, CancellationToken, Task<LandingGitResult>>? mutate, CancellationToken ct)
        => throw new NotImplementedException("CARD-0688 S1");

    public IReadOnlyList<LandingHeadFile> ScanHeadFiles(string commonDirectory) => throw new NotImplementedException("CARD-0688 S1");
}
