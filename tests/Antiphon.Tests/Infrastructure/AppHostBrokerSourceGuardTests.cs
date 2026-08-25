using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Infrastructure;

/// <summary>
/// CARD-0185 — the AppHost must never re-commit a broker hostname, and the tracked defaults
/// stay on the local Redpanda. Reads files relative to the repo root the way
/// <c>AntiphonAppFixture.EnsureClientBundleIsCurrent</c> does.
/// </summary>
public class AppHostBrokerSourceGuardTests
{
    [Test]
    public async Task AppHost_does_not_hardcode_a_broker_hostname_in_WithEnvironment()
    {
        var source = await File.ReadAllTextAsync(Path.Combine(RepoRoot, "Antiphon.AppHost", "Program.cs"));
        var calls = Regex.Matches(
            source,
            @"WithEnvironment\s*\(\s*""AntiphonMessaging__BootstrapServers""\s*,\s*(?<arg>""[^""]*""|[A-Za-z_][A-Za-z0-9_]*)");

        calls.Count.ShouldBeGreaterThan(0,
            "the opt-in WithEnvironment call should still exist; deleting it would also hide a re-committed hostname.");

        foreach (Match call in calls)
        {
            var arg = call.Groups["arg"].Value.Trim();
            Regex.IsMatch(arg, @"[""']\w+:\d{4,5}[""']").ShouldBeFalse(
                $"WithEnvironment(\"AntiphonMessaging__BootstrapServers\", ...) must take an identifier, not a hostname literal. Found: {arg}");
        }
    }

    [Test]
    public async Task Tracked_defaults_keep_the_local_redpanda()
    {
        var server = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(RepoRoot, "server", "appsettings.json"))
            .Build();
        server["AntiphonMessaging:BootstrapServers"].ShouldBe("localhost:19092");

        var fakeSettingsPath = Path.Combine(
            RepoRoot, "src", "Antiphon.Messaging.FakeGateway", "appsettings.json");
        var fakeSettings = new ConfigurationBuilder().AddJsonFile(fakeSettingsPath).Build();
        var fromJson = fakeSettings["AntiphonMessaging:BootstrapServers"];
        if (fromJson is not null)
        {
            fromJson.ShouldBe("localhost:19092");
        }
        else
        {
            // appsettings.json here exists only to turn Microsoft.AspNetCore down (CARD-0043);
            // the host default in Program.cs is the bootstrap.
            var program = await File.ReadAllTextAsync(
                Path.Combine(RepoRoot, "src", "Antiphon.Messaging.FakeGateway", "Program.cs"));
            program.ShouldContain(@"?? ""localhost:19092""");
        }
    }

    private static string RepoRoot
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Antiphon.sln")))
                dir = dir.Parent;

            return dir?.FullName
                ?? throw new DirectoryNotFoundException(
                    "Could not locate repo root (Antiphon.sln) from test base dir.");
        }
    }
}
