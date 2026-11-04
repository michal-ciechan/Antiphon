namespace Antiphon.Tests.TestHelpers;

/// <summary>
/// Moves a ref on the fixture's bare remote to a new commit that exists only there: it is made in
/// the separate <see cref="LandingGitFixture.Observer"/> clone and pushed, so the land's repository
/// does not hold it. An observation of such a tip must fetch, which is what the read-then-fetch
/// race tests exercise; a tip the land already holds is pinned locally without a fetch
/// (LandingGit.PinObservationAsync). Runs through its own FixtureGit, so it is safe to call from
/// inside the fixture's BeforeCommand/AfterCommand hooks (no re-entry).
/// </summary>
internal static class RemoteOnlyMovement
{
    public static async Task<string> AdvanceAsync(LandingGitFixture fixture, string fullRef, string message)
    {
        var git = new LandingGitFixture.FixtureGit(Path.Combine(fixture.Root, "home"), fixture.TaskId);
        async Task<string> Run(params string[] args)
        {
            var result = await git.RunAsync(fixture.Observer, args, CancellationToken.None);
            if (!result.Succeeded) throw new InvalidOperationException($"remote_only_move_failed:{args[0]}:{result.ExitCode}");
            return result.Output.Trim();
        }
        await Run("fetch", "--no-tags", "origin", $"+{fullRef}:refs/remote-only/tip");
        var tip = await Run("rev-parse", "refs/remote-only/tip");
        var tree = await Run("rev-parse", tip + "^{tree}");
        var commit = await Run("commit-tree", tree, "-p", tip, "-m", message);
        await Run("push", "origin", $"{commit}:{fullRef}");
        return commit;
    }
}
