using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Infrastructure.Git;

namespace Antiphon.Tests.TestHelpers;

internal sealed class ControlledTaskProgressGit : TaskProgressGit
{
    public List<string[]> Trace { get; } = [];
    public Func<string, IReadOnlyList<string>, Task<LandingGitResult?>>? BeforeCommand { get; set; }

    public ControlledTaskProgressGit(IRepositoryMutationLease? leases = null) : base(leases) { }

    public override async Task<LandingGitResult> RunAsync(string repository, IReadOnlyList<string> arguments, CancellationToken ct)
    {
        Trace.Add(arguments.ToArray());
        if (BeforeCommand is not null && await BeforeCommand(repository, arguments) is { } injected)
            return injected;
        return await base.RunAsync(repository, arguments, ct);
    }
}
