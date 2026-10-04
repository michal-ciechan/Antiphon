using System.Text;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class EvidenceGitFixtureTests
{
    [Test]
    public async Task C1051_DisposeAsync_RemovesReadOnlyGitObject()
    {
        var fixture = await EvidenceGitFixture.CreateAsync();
        var root = fixture.Root;
        try
        {
            var oid = await fixture.PutAsync("read-only.txt", Encoding.UTF8.GetBytes(Guid.NewGuid().ToString("N")));
            var ownedObject = Path.Combine(fixture.Repo, ".git", "objects", oid[..2], oid[2..]);
            File.Exists(ownedObject).ShouldBeTrue("c1051-local-git-object-exists");
            File.SetAttributes(ownedObject, File.GetAttributes(ownedObject) | FileAttributes.ReadOnly);
            File.GetAttributes(ownedObject).HasFlag(FileAttributes.ReadOnly)
                .ShouldBeTrue("c1051-git-object-read-only");

            await Should.NotThrowAsync(async () => await fixture.DisposeAsync(), "c1051-dispose-read-only");
            Directory.Exists(root).ShouldBeFalse("c1051-owned-root-removed");
            await Should.NotThrowAsync(async () => await fixture.DisposeAsync(), "c1051-missing-root-tolerated");
            Directory.Exists(root).ShouldBeFalse("c1051-owned-root-removed");
        }
        finally
        {
            if (Directory.Exists(root)) StartRefGit.DeleteDirectory(root);
        }
    }
}
