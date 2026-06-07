using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Antiphon.Server.Application.Dtos;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// Full-stack tests for <c>/api/filesystem/browse</c> through the real application composition
/// (real Postgres testcontainer, real filesystem) over HTTP — verifying the endpoint is wired,
/// JSON is camel-cased, and real directories are normalized to forward slashes. Because the
/// host's real drives/dirs vary, these assert shape and a self-created temp tree rather than
/// fixed contents. Exact-contract assertions live in <see cref="FileSystemBrowseMockedTests"/>.
/// </summary>
[NotInParallel]
[ClassDataSource<AntiphonWebAppFactory>(Shared = SharedType.PerTestSession)]
public class FileSystemBrowseApiTests
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private readonly AntiphonWebAppFactory _factory;
    private readonly List<string> _tempDirs = [];

    public FileSystemBrowseApiTests(AntiphonWebAppFactory factory) => _factory = factory;

    [Before(Test)]
    public Task ResetAsync() => _factory.ResetAsync();

    [After(Test)]
    public Task CleanupAsync()
    {
        foreach (var d in _tempDirs)
        {
            try { if (Directory.Exists(d)) Directory.Delete(d, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        _tempDirs.Clear();
        return Task.CompletedTask;
    }

    private async Task<DirectoryBrowseResponse> BrowseAsync(string path)
    {
        using var client = _factory.CreateClient();
        var resp = await client.GetAsync($"/api/filesystem/browse?path={Uri.EscapeDataString(path)}");
        resp.StatusCode.ShouldBe(HttpStatusCode.OK);
        return (await resp.Content.ReadFromJsonAsync<DirectoryBrowseResponse>(Json))!;
    }

    [Test]
    public async Task empty_path_returns_real_drive_roots()
    {
        var result = await BrowseAsync("");

        result.IsDrivesListing.ShouldBeTrue();
        result.Suggestions.ShouldNotBeEmpty();
        // Drive roots look like "C:/", "D:/" — forward-slash, never backslash.
        result.Suggestions.ShouldAllBe(s => s.EndsWith(":/") && !s.Contains('\\'));
    }

    [Test]
    public async Task lists_real_child_directory_with_forward_slashes()
    {
        var parent = Path.Combine(Path.GetTempPath(), "antiphon-browse-" + Guid.NewGuid().ToString("N"));
        var child = Path.Combine(parent, "childdir");
        Directory.CreateDirectory(child);
        _tempDirs.Add(parent);

        // Native backslash path with a trailing separator, as a Windows user types it. The
        // endpoint normalizes and lists children — proving the backend is correct, so a missing
        // UI hint can only be a client-side bug.
        var result = await BrowseAsync(parent + Path.DirectorySeparatorChar);

        var expectedChild = parent.Replace('\\', '/').TrimEnd('/') + "/childdir";
        result.Suggestions.ShouldContain(s => s.Equals(expectedChild, StringComparison.OrdinalIgnoreCase));
        result.Suggestions.ShouldAllBe(s => !s.Contains('\\'));
    }

    [Test]
    public async Task missing_path_reports_not_exists()
    {
        var missing = Path.Combine(Path.GetTempPath(), "antiphon-nope-" + Guid.NewGuid().ToString("N"));

        var result = await BrowseAsync(missing);

        result.Exists.ShouldBeFalse();
        result.IsDrivesListing.ShouldBeFalse();
    }
}
