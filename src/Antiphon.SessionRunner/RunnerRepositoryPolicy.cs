namespace Antiphon.SessionRunner;

public sealed record RunnerRepositoryPolicy(
    string PrimaryPath,
    string PrimaryCloneSource,
    string RepositoriesRoot,
    IReadOnlyList<string> AllowedCloneSources,
    bool ProbeSecondaryPushAccess);
