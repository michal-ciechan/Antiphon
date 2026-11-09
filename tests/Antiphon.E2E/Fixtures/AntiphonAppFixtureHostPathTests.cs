using System.Text.Json;
using Antiphon.Agents.Pty;
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

        await using var boot = await BootAsync();
        var configuration = boot.Fixture.Services.GetRequiredService<IConfiguration>();
        var inherited = keys
            .Select(key => (Key: key, Value: configuration[key]))
            .Where(pair => pair.Value is null || !IsInside(pair.Value, boot.Workspace))
            .Select(pair => $"{pair.Key}={pair.Value}")
            .ToList();
        inherited.ShouldBeEmpty($"settings outside the fixture workspace {boot.Workspace}");
    }

    [Test]
    public async Task Fixture_boot_creates_no_backslash_named_entry_and_teardown_removes_the_interpreter_directory()
    {
        string interpreterDirectory;
        await using (var boot = await BootAsync())
        {
            boot.Created().ShouldBeEmpty("a fixture boot created backslash-named entries in the test output");
            interpreterDirectory = boot.InterpreterDirectory;
            IsInside(interpreterDirectory, boot.Workspace).ShouldBeTrue(
                $"check interpreter directory {interpreterDirectory} is outside {boot.Workspace}");
            Directory.Exists(interpreterDirectory).ShouldBeTrue(interpreterDirectory);
        }

        Directory.Exists(interpreterDirectory).ShouldBeFalse(
            "fixture teardown left the check interpreter directory behind");
    }

    /// <summary>
    /// CARD-1797: provisioning the check interpreter seeds Claude trust for its directory. Each boot
    /// has a new <c>&lt;workspace&gt;/check-interpreter</c>, so a seed into the runner user's real
    /// <c>.claude.json</c> adds one more entry per E2E boot. The user's config here is a temp
    /// <c>CLAUDE_CONFIG_DIR</c>, never the real file; the seed must go to the fixture's own config.
    /// </summary>
    [Test]
    public async Task Fixture_boot_leaves_the_user_claude_config_byte_identical_and_seeds_its_own()
    {
        using var user = new UserClaudeConfig();
        await using (var boot = await BootAsync())
        {
            File.ReadAllBytes(user.ConfigPath).ShouldBe(user.Original,
                $"a fixture boot rewrote the user's Claude config {user.ConfigPath}");

            // Redirected, not dropped: the interpreter's first launch still skips the trust dialog.
            var configuration = boot.Fixture.Services.GetRequiredService<IConfiguration>();
            var owned = configuration[OwnedClaudeConfigKey];
            owned.ShouldNotBeNull(OwnedClaudeConfigKey);
            IsInside(owned, boot.Workspace).ShouldBeTrue($"{OwnedClaudeConfigKey}={owned} is outside {boot.Workspace}");
            ClaudeProjectTrust.IsTrusted(boot.InterpreterDirectory, owned).ShouldBeTrue(
                $"the check interpreter directory was not seeded in the fixture's config {owned}");
        }

        File.ReadAllBytes(user.ConfigPath).ShouldBe(user.Original,
            "fixture teardown rewrote the user's Claude config");
    }

    private const string OwnedClaudeConfigKey = "Delegation:SpecialistClaudeConfigPath";

    /// <summary>
    /// A stand-in for the runner user's Claude config: <c>CLAUDE_CONFIG_DIR</c> points at a temp
    /// directory holding a real-shaped <c>.claude.json</c> for the duration of the test. The class
    /// is <c>[NotInParallel]</c>, so no other test observes the variable.
    /// </summary>
    private sealed class UserClaudeConfig : IDisposable
    {
        private const string Variable = "CLAUDE_CONFIG_DIR";
        private readonly string? _previous = Environment.GetEnvironmentVariable(Variable);
        private readonly string _directory =
            Path.Combine(Path.GetTempPath(), "antiphon-e2e-user-claude-" + Guid.NewGuid().ToString("N"));

        public UserClaudeConfig()
        {
            Directory.CreateDirectory(_directory);
            ConfigPath = ClaudeProjectTrust.DefaultConfigPath(_directory);
            File.WriteAllText(ConfigPath,
                "{\n  \"numStartups\": 3,\n  \"projects\": {\n    \"/home/runner/repo\": { \"hasTrustDialogAccepted\": true }\n  }\n}\n");
            Original = File.ReadAllBytes(ConfigPath);
            Environment.SetEnvironmentVariable(Variable, _directory);
            ClaudeProjectTrust.DefaultConfigPath().ShouldBe(ConfigPath);
        }

        public string ConfigPath { get; }
        public byte[] Original { get; }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable(Variable, _previous);
            Directory.Delete(_directory, recursive: true);
        }
    }

    /// <summary>
    /// Boots a fixture and waits for the check interpreter it provisions in the background, so the
    /// census cannot race it. Disposal tears the fixture down and then removes any backslash-named
    /// entry the boot created: a red run must not leave the residue that breaks the next build.
    /// </summary>
    private static async Task<FixtureBoot> BootAsync()
    {
        var roots = OutputRoots();
        var boot = new FixtureBoot(new AntiphonAppFixture(), roots, BackslashEntries(roots));
        try
        {
            await boot.Fixture.InitializeAsync();
            boot.Workspace = Path.GetFullPath(boot.Fixture.OwnedWorkspace);
            boot.InterpreterDirectory = await WaitForInterpreterDirectoryAsync(boot.Fixture);
            return boot;
        }
        catch
        {
            await boot.DisposeAsync();
            throw;
        }
    }

    private sealed class FixtureBoot(AntiphonAppFixture fixture, string[] roots, HashSet<string> before) : IAsyncDisposable
    {
        public AntiphonAppFixture Fixture { get; } = fixture;
        public string Workspace { get; set; } = "";
        public string InterpreterDirectory { get; set; } = "";

        public List<string> Created() => BackslashEntries(roots).Except(before).ToList();

        public async ValueTask DisposeAsync()
        {
            try
            {
                await Fixture.DisposeAsync();
            }
            finally
            {
                foreach (var entry in Created())
                {
                    if (Directory.Exists(entry))
                        Directory.Delete(entry, recursive: true);
                    else if (File.Exists(entry))
                        File.Delete(entry);
                }
            }
        }
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
