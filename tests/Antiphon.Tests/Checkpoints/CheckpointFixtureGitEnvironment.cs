namespace Antiphon.Tests.Checkpoints;

// CARD-1199 S-B1. This pass-through still hands the owned hostile config to the
// unhardened fixture Git route. It does not clear the caller's Git environment.
internal sealed class CheckpointFixtureGitEnvironment : IDisposable
{
    private readonly Dictionary<string, string> _child;

    private CheckpointFixtureGitEnvironment(string scratch, string hostileConfig)
    {
        Scratch = scratch;
        HostileConfig = hostileConfig;
        _child = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["GIT_CONFIG_GLOBAL"] = hostileConfig,
            ["GIT_CONFIG_NOSYSTEM"] = "1",
        };
    }

    public string Scratch { get; }

    public string HostileConfig { get; }

    public static CheckpointFixtureGitEnvironment Create(string scratch)
    {
        Directory.CreateDirectory(scratch);
        var hostile = Path.Combine(scratch, "hostile.gitconfig");
        File.WriteAllText(hostile, """
            [core]
            	autocrlf = true
            [antiphon]
            	sentinel = c1199-hostile
            """);
        return new CheckpointFixtureGitEnvironment(scratch, hostile);
    }

    public void Git(string repository, params string[] args) =>
        CheckpointFixtureProcess.Git(repository, _child, args);

    public void Dispose()
    {
    }
}
