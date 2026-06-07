using System.IO.Abstractions.TestingHelpers;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Infrastructure.FileSystem;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// Unit tests for <see cref="DirectoryBrowseService"/>. These use a real
/// <see cref="FileSystemDirectoryLister"/> over an in-memory
/// <see cref="MockFileSystem"/> so listing + path normalization get genuine coverage,
/// a <see cref="FakeTimeProvider"/> to drive the TTL cache deterministically, and a
/// fake drive provider (MockFileSystem does not reliably surface drive roots).
/// </summary>
[Category("Unit")]
public class DirectoryBrowseServiceTests
{
    private sealed class FakeDriveProvider : IDriveProvider
    {
        private readonly IReadOnlyList<string> _roots;
        public FakeDriveProvider(params string[] roots) => _roots = roots;
        public IReadOnlyList<string> GetDriveRoots() => _roots;
    }

    /// <summary>Delegating lister that counts how many times <see cref="ListDirectories"/> is called.</summary>
    private sealed class CountingDirectoryLister : IDirectoryLister
    {
        private readonly IDirectoryLister _inner;
        public int ListDirectoriesCallCount { get; private set; }

        public CountingDirectoryLister(IDirectoryLister inner) => _inner = inner;

        public bool DirectoryExists(string path) => _inner.DirectoryExists(path);

        public IReadOnlyList<string> ListDirectories(string parentDir)
        {
            ListDirectoriesCallCount++;
            return _inner.ListDirectories(parentDir);
        }
    }

    private static FileSystemDirectoryLister RealLister(MockFileSystem fs) =>
        new(fs, NullLogger<FileSystemDirectoryLister>.Instance);

    [Test]
    public async Task empty_input_returns_drive_roots()
    {
        var fs = new MockFileSystem();
        var drives = new FakeDriveProvider("C:/", "D:/");
        var service = new DirectoryBrowseService(RealLister(fs), drives, new FakeTimeProvider());

        var result = await service.BrowseAsync("", CancellationToken.None);

        result.IsDrivesListing.ShouldBeTrue();
        result.Suggestions.ShouldBe(["C:/", "D:/"]);
    }

    [Test]
    public async Task prefix_returns_matching_child_directories()
    {
        var fs = new MockFileSystem();
        fs.AddDirectory(@"C:\src");
        fs.AddDirectory(@"C:\srv");
        fs.AddDirectory(@"C:\other");
        var service = new DirectoryBrowseService(
            RealLister(fs), new FakeDriveProvider("C:/"), new FakeTimeProvider());

        var result = await service.BrowseAsync("C:/sr", CancellationToken.None);

        result.Suggestions.ShouldBe(["C:/src", "C:/srv"]);
        result.Suggestions.ShouldNotContain("C:/other");
    }

    [Test]
    public async Task partial_leaf_matches_substring_within_child_name()
    {
        // Repro for the reported bug: typing "C:/src/lea" surfaced nothing because the old code
        // prefix-filtered child paths, and "C:/src/torquay-leander" does not start with ".../lea".
        // Fuzzy/partial matching on the leaf segment must now surface it.
        var fs = new MockFileSystem();
        fs.AddDirectory(@"C:\src\torquay-leander");
        fs.AddDirectory(@"C:\src\other");
        var service = new DirectoryBrowseService(
            RealLister(fs), new FakeDriveProvider("C:/"), new FakeTimeProvider());

        var result = await service.BrowseAsync("C:/src/lea", CancellationToken.None);

        result.Suggestions.ShouldContain("C:/src/torquay-leander");
        result.Suggestions.ShouldNotContain("C:/src/other");
    }

    [Test]
    public async Task trailing_slash_lists_children_of_that_directory()
    {
        var fs = new MockFileSystem();
        fs.AddDirectory(@"C:\src\alpha");
        fs.AddDirectory(@"C:\src\beta");
        fs.AddDirectory(@"C:\other");
        var service = new DirectoryBrowseService(
            RealLister(fs), new FakeDriveProvider("C:/"), new FakeTimeProvider());

        // "C:/src/" means "descend into src" — its children, not siblings of src under C:/.
        var result = await service.BrowseAsync("C:/src/", CancellationToken.None);

        result.Suggestions.ShouldBe(["C:/src/alpha", "C:/src/beta"]);
        result.Suggestions.ShouldNotContain("C:/src");
        result.Suggestions.ShouldNotContain("C:/other");
    }

    [Test]
    public async Task existing_path_reports_exists_true()
    {
        var fs = new MockFileSystem();
        fs.AddDirectory(@"C:\src");
        var service = new DirectoryBrowseService(
            RealLister(fs), new FakeDriveProvider("C:/"), new FakeTimeProvider());

        var result = await service.BrowseAsync("C:/src", CancellationToken.None);

        result.Exists.ShouldBeTrue();
    }

    [Test]
    public async Task missing_path_reports_exists_false()
    {
        var fs = new MockFileSystem();
        fs.AddDirectory(@"C:\src");
        var service = new DirectoryBrowseService(
            RealLister(fs), new FakeDriveProvider("C:/"), new FakeTimeProvider());

        var result = await service.BrowseAsync("C:/nope", CancellationToken.None);

        result.Exists.ShouldBeFalse();
    }

    [Test]
    public async Task normalizes_backslashes_and_drive_case()
    {
        var fs = new MockFileSystem();
        fs.AddDirectory(@"C:\src");
        var service = new DirectoryBrowseService(
            RealLister(fs), new FakeDriveProvider("C:/"), new FakeTimeProvider());

        var result = await service.BrowseAsync(@"c:\src", CancellationToken.None);

        result.NormalizedPath.ShouldBe("C:/src");
    }

    [Test]
    public async Task caches_within_ttl_and_refreshes_after()
    {
        var fs = new MockFileSystem();
        fs.AddDirectory(@"C:\src\alpha");
        var fakeTime = new FakeTimeProvider();
        var service = new DirectoryBrowseService(RealLister(fs), new FakeDriveProvider("C:/"), fakeTime);

        // First call populates the cache for parent "C:/src".
        var first = await service.BrowseAsync("C:/src/", CancellationToken.None);
        first.Suggestions.ShouldContain("C:/src/alpha");

        // Mutate the filesystem, then call again well within the 15s TTL → stale cache hit.
        fs.AddDirectory(@"C:\src\beta");
        fakeTime.Advance(TimeSpan.FromSeconds(5));
        var withinTtl = await service.BrowseAsync("C:/src/", CancellationToken.None);
        withinTtl.Suggestions.ShouldContain("C:/src/alpha");
        withinTtl.Suggestions.ShouldNotContain("C:/src/beta");

        // Advance past the TTL → cache refreshes and now sees both.
        fakeTime.Advance(TimeSpan.FromSeconds(16));
        var afterTtl = await service.BrowseAsync("C:/src/", CancellationToken.None);
        afterTtl.Suggestions.ShouldContain("C:/src/alpha");
        afterTtl.Suggestions.ShouldContain("C:/src/beta");
    }

    [Test]
    public async Task reads_filesystem_once_within_ttl()
    {
        var fs = new MockFileSystem();
        fs.AddDirectory(@"C:\src\alpha");
        var counting = new CountingDirectoryLister(RealLister(fs));
        var fakeTime = new FakeTimeProvider();
        var service = new DirectoryBrowseService(counting, new FakeDriveProvider("C:/"), fakeTime);

        // Three calls for the same parent within the TTL → only one disk read.
        await service.BrowseAsync("C:/src/", CancellationToken.None);
        await service.BrowseAsync("C:/src/", CancellationToken.None);
        await service.BrowseAsync("C:/src/", CancellationToken.None);
        counting.ListDirectoriesCallCount.ShouldBe(1);

        // Past the TTL the cache is refreshed → a second disk read.
        fakeTime.Advance(TimeSpan.FromSeconds(16));
        await service.BrowseAsync("C:/src/", CancellationToken.None);
        counting.ListDirectoriesCallCount.ShouldBe(2);
    }
}
