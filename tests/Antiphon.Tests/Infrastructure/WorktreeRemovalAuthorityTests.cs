using Antiphon.Server.Application.Settings;
using Antiphon.Server.Infrastructure.Git;
using Antiphon.Tests.TestHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Infrastructure;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class WorktreeRemovalAuthorityTests
{
    [Test]
    [Arguments(".antiphon/report.md")]
    [Arguments(".claude/settings.local.json")]
    [Arguments("bin-private/irreplaceable.txt")]
    [Arguments("untracked.txt")]
    [Arguments("keep.txt")]
    public async Task C448_V24_LegacyRemovalCannotEraseTaskContents(string relative)
    {
        await using var fixture = new LandingGitFixture();
        await fixture.InitializeAsync();
        var file = Path.Combine(fixture.Source, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        await File.WriteAllTextAsync(file, "opaque user work\n");
        var manager = new WorktreeManager(Options.Create(new GitSettings
        {
            WorktreeBasePath = Path.Combine(fixture.Root, "trees"),
        }), TimeProvider.System, NullLogger<WorktreeManager>.Instance);

        var result = await manager.TryRemoveAsync(fixture.Repository, fixture.Source, "master", CancellationToken.None);

        File.Exists(file).ShouldBeTrue("removal without typed task authority must preserve the sentinel");
        (await File.ReadAllTextAsync(file)).ShouldBe("opaque user work\n");
        result.IsClean.ShouldBeFalse();
        (await fixture.RequiredAsync(fixture.Repository, "rev-parse", fixture.SourceRef)).Trim().ShouldBe(fixture.SeedSha);
        (await fixture.RequiredAsync(fixture.Repository, "worktree", "list", "--porcelain")).ShouldContain(fixture.Source.Replace('\\', '/'));
        await fixture.AssertRemoteSourceAsync();
    }
}
