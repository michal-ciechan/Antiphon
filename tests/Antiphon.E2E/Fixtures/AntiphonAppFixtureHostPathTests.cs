using System.Text.Json;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace Antiphon.E2E.Fixtures;

/// <summary>
/// CARD-1795: <c>server/appsettings.json</c> carries Windows absolute paths. On Linux a fixture boot
/// that inherits one reads it as a relative name and creates a directory literally called
/// <c>C:\logs\antiphon\check-interpreter</c> inside the test output, which fails checkpoint cleanup
/// (<c>output-ledger-mismatch</c>) and the next E2E build (<c>MSB3552</c>).
/// </summary>
[NotInParallel]
[ParallelLimiter<ProcessSpawnLimit>]
[Category("OptIn")]
public class AntiphonAppFixtureHostPathTests
{
    private static readonly TimeSpan ProvisionBudget = TimeSpan.FromSeconds(60);

    [Test]
    public async Task Every_windows_path_setting_resolves_inside_the_fixture_workspace()
    {
        var keys = WindowsPathKeys();
        // The two the fixture inherited before CARD-1795; any new one must be overridden too.
        keys.ShouldContain("Delegation:CheckInterpreterWorkingDirectory");
        keys.ShouldContain("ZombieCensus:SessionLogPath");

        var fixture = new AntiphonAppFixture();
        try
        {
            await fixture.InitializeAsync();
            var workspace = Path.GetFullPath(fixture.OwnedWorkspace);
            var configuration = fixture.Services.GetRequiredService<IConfiguration>();
            var inherited = keys
                .Select(key => (Key: key, Value: configuration[key]))
                .Where(pair => pair.Value is null || !IsInside(pair.Value, workspace))
                .Select(pair => $"{pair.Key}={pair.Value}")
                .ToList();
            inherited.ShouldBeEmpty($"settings outside the fixture workspace {workspace}");
        }
        finally
        {
            await fixture.DisposeAsync();
        }
    }

    [Test]
    public async Task Fixture_boot_creates_no_backslash_named_entry_and_teardown_removes_the_interpreter_directory()
    {
        var roots = OutputRoots();
        var before = BackslashEntries(roots);
        var fixture = new AntiphonAppFixture();
        string? interpreterDirectory = null;
        List<string> created = [];
        try
        {
            await fixture.InitializeAsync();
            var workspace = Path.GetFullPath(fixture.OwnedWorkspace);

            // The boot provisions the check interpreter in the background; its row is saved only
            // after the working directory has been created, so the census below cannot race it.
            interpreterDirectory = await WaitForInterpreterDirectoryAsync(fixture);
            created = BackslashEntries(roots).Except(before).ToList();

            created.ShouldBeEmpty("a fixture boot created backslash-named entries in the test output");
            IsInside(interpreterDirectory, workspace).ShouldBeTrue(
                $"check interpreter directory {interpreterDirectory} is outside {workspace}");
            Directory.Exists(Path.GetFullPath(interpreterDirectory)).ShouldBeTrue(interpreterDirectory);
        }
        finally
        {
            await fixture.DisposeAsync();
            // A red run must not leave the residue that breaks the next build in this output.
            foreach (var entry in created)
            {
                if (Directory.Exists(entry))
                    Directory.Delete(entry, recursive: true);
                else if (File.Exists(entry))
                    File.Delete(entry);
            }
        }

        Directory.Exists(Path.GetFullPath(interpreterDirectory!)).ShouldBeFalse(
            "fixture teardown left the check interpreter directory behind");
    }

    private static async Task<string> WaitForInterpreterDirectoryAsync(AntiphonAppFixture fixture)
    {
        var deadline = DateTime.UtcNow + ProvisionBudget;
        while (true)
        {
            await using (var scope = fixture.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var directory = await db.Agents.AsNoTracking()
                    .Where(agent => agent.Slug == CheckInterpreterProvisioner.DefaultSlug)
                    .Select(agent => agent.WorkingDirectory)
                    .FirstOrDefaultAsync();
                if (!string.IsNullOrWhiteSpace(directory))
                    return directory;
            }

            if (DateTime.UtcNow >= deadline)
                throw new TimeoutException(
                    $"the check interpreter was not provisioned within {ProvisionBudget.TotalSeconds:0} s");
            await Task.Delay(TimeSpan.FromMilliseconds(200));
        }
    }

    /// <summary>Every string value in server/appsettings.json that is a Windows absolute path.</summary>
    private static List<string> WindowsPathKeys()
    {
        var path = Path.Combine(AntiphonAppFixture.FindRepositoryRoot(), "server", "appsettings.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path),
            new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        var keys = new List<string>();
        Collect(document.RootElement, null, keys);
        return keys;
    }

    private static void Collect(JsonElement element, string? prefix, List<string> keys)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                    Collect(property.Value, prefix is null ? property.Name : prefix + ":" + property.Name, keys);
                break;
            case JsonValueKind.Array:
                var index = 0;
                foreach (var item in element.EnumerateArray())
                    Collect(item, prefix + ":" + index++, keys);
                break;
            case JsonValueKind.String when prefix is not null && IsWindowsAbsolute(element.GetString()!):
                keys.Add(prefix);
                break;
        }
    }

    private static bool IsWindowsAbsolute(string value) =>
        value.Length >= 3 && char.IsAsciiLetter(value[0]) && value[1] == ':' && value[2] is '\\' or '/';

    private static bool IsInside(string candidate, string root)
    {
        if (IsWindowsAbsolute(candidate) && !OperatingSystem.IsWindows())
            return false;
        var full = Path.GetFullPath(candidate).TrimEnd(Path.DirectorySeparatorChar);
        var prefix = root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return full.StartsWith(prefix, comparison);
    }

    // A relative setting resolves against the process working directory; the build output is the
    // directory the checkpoint ledger owns. Both are the test host's dll directory in practice.
    private static string[] OutputRoots() =>
        new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory }
            .Select(path => Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    private static readonly EnumerationOptions Recursive = new() { RecurseSubdirectories = true, IgnoreInaccessible = true };

    private static HashSet<string> BackslashEntries(IEnumerable<string> roots) =>
        roots.SelectMany(root => Directory.EnumerateFileSystemEntries(root, "*", Recursive))
            .Where(entry => Path.GetFileName(entry).Contains('\\'))
            .ToHashSet(StringComparer.Ordinal);
}
