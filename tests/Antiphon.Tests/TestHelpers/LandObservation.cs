using Antiphon.Server.Infrastructure.Git;

namespace Antiphon.Tests.TestHelpers;

/// <summary>
/// The step that pins a remote observation, in either form LandingGit uses: a fetch into the unique
/// observation ref (<c>fetch … &lt;remoteRef&gt;:&lt;pin&gt;</c>) when the advertised commit is not local, or a
/// create-only <c>update-ref -m "antiphon observe &lt;remoteRef&gt;" &lt;pin&gt; &lt;sha&gt; &lt;zero&gt;</c> when it is
/// (LandingGit.PinObservationAsync). Tests that inject faults into, count, or race against the
/// observation use this instead of matching "fetch" alone, so they keep exercising the real step.
/// </summary>
internal static class LandObservation
{
    /// <summary>True for an observation pin step, optionally of <paramref name="remoteRef"/> only.</summary>
    public static bool IsPinStep(IReadOnlyList<string> args, string? remoteRef = null)
    {
        if (args.Count == 0) return false;
        if (args[0] == "fetch")
            return remoteRef is null || args.Any(a => a.StartsWith(remoteRef + ":", StringComparison.Ordinal));
        if (args[0] != "update-ref" || args.Count != 6 || args[1] != "-m"
            || !args[2].StartsWith(LandingGit.ObservationMessagePrefix, StringComparison.Ordinal))
            return false;
        return remoteRef is null || args[2] == LandingGit.ObservationMessagePrefix + remoteRef;
    }

    /// <summary>The pin a pin step creates, or null.</summary>
    public static string? Pin(IReadOnlyList<string> args) =>
        !IsPinStep(args) ? null
        : args[0] == "update-ref" ? args[3]
        : args.Select(a => a.Split(':', 2)).Where(p => p.Length == 2 && p[1].StartsWith("refs/antiphon/land/", StringComparison.Ordinal))
            .Select(p => p[1]).FirstOrDefault();
}
