using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class LandGraphAuditCleanupTests
{
    [Test]
    public async Task ReadOnlyGitObjectsAreDeleted()
    {
        var fixture = new AuditGitFixture();
        await fixture.RunAsync(async () =>
        {
            await StartRefGit.CreateRepoAsync(fixture.Root, withOrigin: false);
            var objects = Directory.GetFiles(Path.Combine(fixture.Root, "repo", ".git", "objects"), "*", SearchOption.AllDirectories);
            objects.Length.ShouldBeGreaterThan(0);
            foreach (var path in objects)
            {
                File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.ReadOnly);
                File.GetAttributes(path).HasFlag(FileAttributes.ReadOnly).ShouldBeTrue();
            }
        });
        Directory.Exists(fixture.Root).ShouldBeFalse();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task LinksRefuseBeforeChangingOutsideAttributes(bool replaceRoot)
    {
        var fixture = new AuditGitFixture();
        var outside = Directory.CreateTempSubdirectory("c1538-outside-").FullName;
        var sentinel = Path.Combine(outside, "sentinel");
        await File.WriteAllTextAsync(sentinel, "outside bytes");
        File.SetAttributes(sentinel, File.GetAttributes(sentinel) | FileAttributes.ReadOnly);
        var attributes = File.GetAttributes(sentinel);
        if (replaceRoot) Directory.Delete(fixture.Root);
        using var link = DirectoryLink.TryCreate(replaceRoot ? fixture.Root : Path.Combine(fixture.Root, "link"), outside);
        try
        {
            link.ShouldNotBeNull("the fixture needs a real directory link");
            await Should.ThrowAsync<IOException>(() => fixture.RunAsync(() => Task.CompletedTask));
            File.Exists(sentinel).ShouldBeTrue();
            (await File.ReadAllTextAsync(sentinel)).ShouldBe("outside bytes");
            File.GetAttributes(sentinel).ShouldBe(attributes);
            DirectoryLink.IsLink(link!.Path).ShouldBeTrue("refusal retains the link for its owner");
        }
        finally
        {
            link?.Dispose();
            if (Directory.Exists(fixture.Root)) ReadOnlyDirectory.Delete(fixture.Root);
            ReadOnlyDirectory.Delete(outside);
        }
    }

    [Test]
    public async Task BodyFailureSurvivesSuccessfulCleanup()
    {
        var fixture = new AuditGitFixture();
        var bodyError = new InvalidOperationException("body witness");
        var observed = await Should.ThrowAsync<InvalidOperationException>(() => fixture.RunAsync(() => throw bodyError));
        observed.ShouldBeSameAs(bodyError);
        Directory.Exists(fixture.Root).ShouldBeFalse();
    }

    [Test]
    public async Task BodyAndCleanupFailuresAreBothRetained()
    {
        var fixture = new AuditGitFixture();
        var outside = Directory.CreateTempSubdirectory("c1538-outside-").FullName;
        using var link = DirectoryLink.TryCreate(Path.Combine(fixture.Root, "link"), outside);
        var bodyError = new InvalidOperationException("body witness");
        try
        {
            link.ShouldNotBeNull("the fixture needs a real directory link");
            var observed = await Should.ThrowAsync<AggregateException>(() => fixture.RunAsync(() => throw bodyError));
            observed.InnerExceptions.Count.ShouldBe(2);
            observed.InnerExceptions[0].ShouldBeSameAs(bodyError);
            observed.InnerExceptions[1].ShouldBeOfType<IOException>();
            Directory.Exists(fixture.Root).ShouldBeTrue("a persistent cleanup refusal must remain visible");
        }
        finally
        {
            link?.Dispose();
            ReadOnlyDirectory.Delete(fixture.Root);
            ReadOnlyDirectory.Delete(outside);
        }
    }
}
