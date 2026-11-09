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

    [Test]
    public void PrefixSiblingOfTheTempParentIsRefused()
    {
        var scratch = Directory.CreateTempSubdirectory("c1568-prefix-").FullName;
        var temp = Path.Combine(scratch, "root");
        var evilParent = temp + "-evil";
        var evil = Path.Combine(evilParent, "antiphon-audit-sibling");
        var sentinel = Path.Combine(evil, "sentinel");
        try
        {
            Directory.CreateDirectory(temp);
            Directory.CreateDirectory(evilParent);
            Directory.CreateDirectory(evil);
            File.WriteAllText(sentinel, "keep");
            var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            var normalizedTemp = Path.TrimEndingDirectorySeparator(Path.GetFullPath(temp));
            var parent = Path.GetDirectoryName(Path.GetFullPath(evil));
            parent.ShouldBe(Path.TrimEndingDirectorySeparator(Path.GetFullPath(evilParent)));
            string.Equals(parent, normalizedTemp, comparison).ShouldBeFalse();
            parent!.StartsWith(normalizedTemp, comparison).ShouldBeTrue();

            var error = Should.Throw<IOException>(() => AuditGitFixture.CleanupOwnedRoot(evil, temp));
            error.Message.ShouldBe("Audit cleanup escaped its owned temp root.");
            File.ReadAllText(sentinel).ShouldBe("keep");
            Directory.Exists(evil).ShouldBeTrue();
        }
        finally
        {
            if (Directory.Exists(scratch))
                Directory.Delete(scratch, recursive: true);
        }
    }

    [Test]
    public async Task PathOutsideTheTempRootIsRefused()
    {
        var outsideParent = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "c1568-outside-" + Guid.NewGuid().ToString("N"));
        var evil = Path.Combine(outsideParent, "antiphon-audit-outside");
        var sentinel = Path.Combine(evil, "sentinel");
        var fixture = new AuditGitFixture(evil);
        try
        {
            Directory.CreateDirectory(evil);
            await File.WriteAllTextAsync(sentinel, "outside");
            var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            var temp = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath()));
            var parent = Path.GetDirectoryName(Path.GetFullPath(evil));
            string.Equals(parent, temp, comparison).ShouldBeFalse();
            Path.GetFileName(evil).StartsWith("antiphon-audit-", StringComparison.Ordinal).ShouldBeTrue();

            var error = await Should.ThrowAsync<IOException>(() => fixture.RunAsync(() => Task.CompletedTask));
            error.Message.ShouldBe("Audit cleanup escaped its owned temp root.");
            (await File.ReadAllTextAsync(sentinel)).ShouldBe("outside");
            Directory.Exists(evil).ShouldBeTrue();
        }
        finally
        {
            if (Directory.Exists(outsideParent))
                Directory.Delete(outsideParent, recursive: true);
        }
    }

    [Test]
    public async Task MisnamedTempSiblingIsRefused()
    {
        var owned = new AuditGitFixture();
        var evil = Path.Combine(Path.GetDirectoryName(owned.Root)!, "root-evil-" + Guid.NewGuid().ToString("N"));
        var sentinel = Path.Combine(evil, "sentinel");
        var aimed = new AuditGitFixture(evil);
        try
        {
            Directory.CreateDirectory(evil);
            await File.WriteAllTextAsync(sentinel, "keep");
            var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            var temp = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath()));
            string.Equals(Path.GetDirectoryName(Path.GetFullPath(evil)), temp, comparison).ShouldBeTrue();
            Path.GetFileName(evil).StartsWith("antiphon-audit-", StringComparison.Ordinal).ShouldBeFalse();
            Path.GetFileName(evil).StartsWith("root-evil-", StringComparison.Ordinal).ShouldBeTrue();

            var error = await Should.ThrowAsync<IOException>(() => aimed.RunAsync(() => Task.CompletedTask));
            error.Message.ShouldBe("Audit cleanup escaped its owned temp root.");
            (await File.ReadAllTextAsync(sentinel)).ShouldBe("keep");
            Directory.Exists(owned.Root).ShouldBeTrue();
        }
        finally
        {
            if (Directory.Exists(evil))
                Directory.Delete(evil, recursive: true);
            if (Directory.Exists(owned.Root))
                ReadOnlyDirectory.Delete(owned.Root);
        }
    }

    [Test]
    public async Task AbsentOwnedRootIsANoOp()
    {
        var owned = Directory.CreateTempSubdirectory("antiphon-audit-").FullName;
        Directory.Delete(owned);
        var fixture = new AuditGitFixture(owned);
        await fixture.RunAsync(() => Task.CompletedTask);
        Directory.Exists(owned).ShouldBeFalse();
    }

    [Test]
    public async Task AbsentOutsideRootIsStillRefused()
    {
        var outsideParent = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "c1568-absent-" + Guid.NewGuid().ToString("N"));
        var evil = Path.Combine(outsideParent, "antiphon-audit-missing");
        var fixture = new AuditGitFixture(evil);
        try
        {
            Directory.Exists(evil).ShouldBeFalse();
            var error = await Should.ThrowAsync<IOException>(() => fixture.RunAsync(() => Task.CompletedTask));
            error.Message.ShouldBe("Audit cleanup escaped its owned temp root.");
            Directory.Exists(evil).ShouldBeFalse();
        }
        finally
        {
            if (Directory.Exists(outsideParent))
                Directory.Delete(outsideParent, recursive: true);
        }
    }
}
