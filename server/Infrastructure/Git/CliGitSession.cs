using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;

namespace Antiphon.Server.Infrastructure.Git;

/// <summary>
/// CARD-1704 S1: the CLI backend's session, a null object. Every read is not handled, so
/// <see cref="LandingGit"/> issues today's exact git command through its virtual <c>RunAsync</c>.
/// </summary>
public sealed class CliGitSession : IGitSession
{
    private static readonly GitAnswer<LandingGitResult> Cli = GitAnswer<LandingGitResult>.NotHandled("cli");

    public string Backend => LandGitBackendNames.Cli;
    public GitAnswer<LandingGitResult> ReadCommit(string repository, string revision) => Cli;
    public GitAnswer<LandingGitResult> ReadAncestry(string repository, string ancestor, string descendant) => Cli;
    public GitAnswer<LandingGitResult> ReadRef(string repository, string fullRef) => Cli;
    public GitAnswer<LandingGitResult> ReadSymbolicRef(string repository, string name) => Cli;
    public GitAnswer<LandingGitResult> ReadGitDirectory(string repository) => Cli;
    public GitAnswer<LandingGitResult> ReadConfig(string repository, string key) => Cli;
    public GitAnswer<LandingGitResult> ReadTreeZ(string repository, string treeish) => Cli;
    public GitAnswer<LandingGitResult> ReadDiffTreeZ(string repository, string from, string to) => Cli;
    public GitAnswer<LandingGitResult> ReadWorktreeStatus(string repository, bool untracked) => Cli;
    public void Dispose() { }
}
