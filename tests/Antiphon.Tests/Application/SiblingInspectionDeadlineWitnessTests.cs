using Antiphon.Server.Application.Settings;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-1749: a missing <c>IOptions&lt;GitSettings&gt;</c> must use the same inspection
/// deadline as <see cref="GitSettings.WorktreeBaseInspectionTimeoutSeconds"/>, not a second literal.
/// </summary>
[Category("Unit")]
public sealed class SiblingInspectionDeadlineWitnessTests
{
    [Test]
    public void C1749_NullSettingsFallbackIsTheGitSettingsDefault()
    {
        var root = DelegateScriptRunner.RepoRoot;
        var settings = File.ReadAllText(Path.Combine(root, "server", "Application", "Settings", "GitSettings.cs"));
        var land = File.ReadAllText(Path.Combine(root, "server", "Application", "Services", "AgentTaskLandService.cs"));
        var methodAt = land.IndexOf("CollectUnlandedSiblingsAsync(", StringComparison.Ordinal);
        methodAt.ShouldBeGreaterThanOrEqualTo(0);
        var methodEnd = land.IndexOf("IsHeldBehindSharedWriter(", methodAt, StringComparison.Ordinal);
        methodEnd.ShouldBeGreaterThan(methodAt);
        var method = Normalize(land[methodAt..methodEnd]);
        var initializer = TokenAfter(Normalize(settings),
            "public int WorktreeBaseInspectionTimeoutSeconds { get; set; } = ", ';');
        var fallback = TokenAfter(method, "_gitSettings?.WorktreeBaseInspectionTimeoutSeconds ?? ", ')');
        var shared = initializer.Length > 0
            && char.IsAsciiLetter(initializer[0])
            && fallback == "GitSettings." + initializer;
        shared.ShouldBe(true,
            $"null GitSettings sibling deadline '{fallback}' diverges from the GitSettings default '{initializer}'");
        Normalize(settings).ShouldContain($"public const int {initializer} = 5;");
        method.ShouldContain("Math.Max(1, _gitSettings?.WorktreeBaseInspectionTimeoutSeconds ??");
        new GitSettings().WorktreeBaseInspectionTimeoutSeconds.ShouldBe(5);
    }

    private static string TokenAfter(string text, string marker, char end)
    {
        var at = text.IndexOf(marker, StringComparison.Ordinal);
        at.ShouldBeGreaterThanOrEqualTo(0, marker);
        var start = at + marker.Length;
        var stop = text.IndexOf(end, start);
        stop.ShouldBeGreaterThan(start);
        return text[start..stop].Trim();
    }

    private static string Normalize(string text) =>
        string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
