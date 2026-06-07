using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Antiphon.Server.Application.Dtos;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// HTTP-level tests for <c>/api/filesystem/browse</c> with mocked filesystem dependencies, so
/// the exact suggestion contract is asserted independent of the host machine. Uses the shared
/// <see cref="MockedFileSystemWebAppFactory"/> (one host for the whole session); state is reset
/// before each test via <see cref="MockedFileSystemWebAppFactory.ResetAsync"/>.
/// </summary>
[NotInParallel]
[ClassDataSource<MockedFileSystemWebAppFactory>(Shared = SharedType.PerTestSession)]
public class FileSystemBrowseMockedTests
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private readonly MockedFileSystemWebAppFactory _factory;

    public FileSystemBrowseMockedTests(MockedFileSystemWebAppFactory factory) => _factory = factory;

    [Before(Test)]
    public Task ResetAsync() => _factory.ResetAsync();

    private async Task<DirectoryBrowseResponse> BrowseAsync(string path)
    {
        using var client = _factory.CreateClient();
        var resp = await client.GetAsync($"/api/filesystem/browse?path={Uri.EscapeDataString(path)}");
        resp.StatusCode.ShouldBe(HttpStatusCode.OK);
        return (await resp.Content.ReadFromJsonAsync<DirectoryBrowseResponse>(Json))!;
    }

    [Test]
    public async Task empty_path_lists_drive_roots()
    {
        _factory.Drives.Roots.AddRange(["C:/", "D:/"]);

        var result = await BrowseAsync("");

        result.IsDrivesListing.ShouldBeTrue();
        result.Suggestions.ShouldBe(["C:/", "D:/"]);
    }

    [Test]
    public async Task backslash_prefix_is_normalized_and_prefix_filtered()
    {
        _factory.Drives.Roots.Add("C:/");
        _factory.Lister.AddDirectory("C:/", "C:/src");
        _factory.Lister.AddDirectory("C:/", "C:/srv");
        _factory.Lister.AddDirectory("C:/", "C:/other");

        // Backslash input is exactly the real-world case the UI mishandled: the server
        // normalizes it and returns forward-slash suggestions filtered by the typed prefix.
        var result = await BrowseAsync(@"C:\sr");

        result.NormalizedPath.ShouldBe("C:/sr");
        result.Suggestions.ShouldBe(["C:/src", "C:/srv"]);
    }

    [Test]
    public async Task trailing_slash_lists_children_of_that_directory()
    {
        _factory.Drives.Roots.Add("C:/");
        _factory.Lister.AddDirectory("C:/src", "C:/src/alpha");
        _factory.Lister.AddDirectory("C:/src", "C:/src/beta");

        var result = await BrowseAsync("C:/src/");

        result.Suggestions.ShouldBe(["C:/src/alpha", "C:/src/beta"]);
    }

    [Test]
    public async Task reset_clears_cached_listing()
    {
        _factory.Drives.Roots.Add("C:/");
        _factory.Lister.AddDirectory("C:/src", "C:/src/alpha");

        var first = await BrowseAsync("C:/src/");
        first.Suggestions.ShouldBe(["C:/src/alpha"]);

        // Add a sibling and re-query within the 15s TTL → stale cache hit (beta not visible yet).
        _factory.Lister.AddDirectory("C:/src", "C:/src/beta");
        var cached = await BrowseAsync("C:/src/");
        cached.Suggestions.ShouldBe(["C:/src/alpha"]);

        // ResetAsync (what runs between tests) clears the cache and the fakes; re-seed and the
        // fresh listing is read from disk again.
        await _factory.ResetAsync();
        _factory.Drives.Roots.Add("C:/");
        _factory.Lister.AddDirectory("C:/src", "C:/src/alpha");
        _factory.Lister.AddDirectory("C:/src", "C:/src/beta");

        var afterReset = await BrowseAsync("C:/src/");
        afterReset.Suggestions.ShouldBe(["C:/src/alpha", "C:/src/beta"]);
    }
}
