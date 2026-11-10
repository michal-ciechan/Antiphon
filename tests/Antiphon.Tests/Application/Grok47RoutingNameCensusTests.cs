using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0600: live grok-4.7 routing tests must not keep retired grok_4_6 member names.
/// A method-scoped mutation PC binds the exact name, so a stale name reads as the retired pin.
/// </summary>
[Category("Unit")]
public sealed class Grok47RoutingNameCensusTests
{
    private static readonly string[] Subjects =
    [
        "tests/Antiphon.Tests/Application/GrokLaunchArgsTests.cs",
        "tests/Antiphon.Tests/Application/ModelAvailabilityCreateTests.cs",
        "tests/Antiphon.Tests/Application/ApiErrorRecoveryServiceTests.cs",
    ];

    private static readonly string[] RequiredMembers =
    [
        "Grok47Catalog",
        "every_emitted_effort_is_in_the_grok_4_7_catalog",
        "grok_4_7_and_an_unset_model_keep_the_table_including_xhigh",
        "Create_Grok_against_a_grok_4_7_hold_is_409_model_disabled",
        "Create_Claude_succeeds_while_grok_4_7_is_held",
        "Grok_402_stub_writes_a_fallback_hold_for_grok_4_7_and_never_enqueues",
    ];

    [Test]
    public void live_grok_routing_members_name_grok_4_7()
    {
        var root = RepoRoot();
        var stale = new List<string>();
        var present = new HashSet<string>(StringComparer.Ordinal);
        foreach (var relative in Subjects)
        {
            var path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
            File.Exists(path).ShouldBeTrue(relative);
            var lines = File.ReadAllLines(path);
            for (var i = 0; i < lines.Length; i++)
            {
                if (IsComment(lines[i]))
                    continue;

                if (lines[i].Contains("grok_4_6", StringComparison.Ordinal)
                    || lines[i].Contains("Grok46", StringComparison.Ordinal))
                {
                    stale.Add(relative + ":" + (i + 1));
                }

                foreach (var member in RequiredMembers)
                {
                    if (lines[i].Contains(member, StringComparison.Ordinal))
                        present.Add(member);
                }
            }
        }

        stale.ShouldBeEmpty("stale grok_4_6 member names: " + string.Join(", ", stale));
        var missing = RequiredMembers.Where(member => !present.Contains(member)).ToArray();
        missing.ShouldBeEmpty("missing grok_4_7 members: " + string.Join(", ", missing));
    }

    private static bool IsComment(string line)
    {
        var trimmed = line.TrimStart();
        return trimmed.StartsWith("//", StringComparison.Ordinal)
            || trimmed.StartsWith("*", StringComparison.Ordinal)
            || trimmed.StartsWith("/*", StringComparison.Ordinal);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Antiphon.sln")))
            dir = dir.Parent;
        return dir?.FullName
            ?? throw new DirectoryNotFoundException("Could not locate repo root (Antiphon.sln).");
    }
}
