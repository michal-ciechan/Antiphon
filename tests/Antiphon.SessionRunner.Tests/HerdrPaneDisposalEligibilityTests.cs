using Shouldly;
using TUnit.Core;
using TUnit.Core.Exceptions;
using LiveFixture = Antiphon.SessionRunner.Tests.HerdrPaneDisposalGuardedLiveTests.LiveFixture;

namespace Antiphon.SessionRunner.Tests;

/// <summary>
/// CARD-0610: the installed-Herdr pane disposal fixture must stand aside on a machine that has no
/// per-user Herdr, and it must decide that BEFORE it writes a config or starts a process. These
/// drive <see cref="LiveFixture.EnsureEligible"/> directly with owned values, so no environment
/// variable is read or written and no process is ever launched — including the placeholder file,
/// which exists only to make <c>File.Exists</c> true and is never executed.
/// </summary>
[Category("Unit")]
public sealed class HerdrPaneDisposalEligibilityTests
{
    // Every rejected opt-in spelling: absent, blank, the explicit off value, and a truthy-looking
    // string that is not the exact "1" the headed lane agrees on.
    private static readonly string?[] RejectedOptIns = [null, "", "0", "true"];

    [Test]
    public void Requires_explicit_opt_in()
    {
        var (placeholder, absent) = OwnedPaths();
        try
        {
            foreach (var optIn in RejectedOptIns)
            {
                // An installed binary is not on its own a licence to launch it.
                Should.Throw<SkipTestException>(() => LiveFixture.EnsureEligible(optIn, placeholder))
                    .Message.ShouldContain(HerdrLiveSession.EnvFlag);
                Should.Throw<SkipTestException>(() => LiveFixture.EnsureEligible(optIn, absent))
                    .Message.ShouldContain(HerdrLiveSession.EnvFlag);
            }
        }
        finally { CleanUp(placeholder); }
    }

    [Test]
    public void Skips_when_opted_in_but_binary_is_missing()
    {
        var (placeholder, absent) = OwnedPaths();
        try
        {
            var skip = Should.Throw<SkipTestException>(() => LiveFixture.EnsureEligible("1", absent));
            skip.Message.ShouldContain(absent);
            skip.Message.ShouldContain("not found");
        }
        finally { CleanUp(placeholder); }
    }

    [Test]
    public void Accepts_opt_in_with_existing_binary()
    {
        var (placeholder, _) = OwnedPaths();
        try
        {
            // The positive arm: without it the gate could reject everything forever and the six
            // live cases would be permanently, silently skipped.
            Should.NotThrow(() => LiveFixture.EnsureEligible("1", placeholder));
        }
        finally { CleanUp(placeholder); }
    }

    /// <summary>An owned existing file and an owned path that is guaranteed not to exist.</summary>
    private static (string Placeholder, string Absent) OwnedPaths()
    {
        var root = Path.Combine(Path.GetTempPath(), "antiphon-c610-eligibility-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var placeholder = Path.Combine(root, "herdr.exe");
        File.WriteAllText(placeholder, "not an executable; never started");
        return (placeholder, Path.Combine(root, "absent", "herdr.exe"));
    }

    private static void CleanUp(string placeholder)
    {
        var root = Path.GetDirectoryName(placeholder)!;
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }
}
