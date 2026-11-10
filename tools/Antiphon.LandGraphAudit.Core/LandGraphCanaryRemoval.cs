using System.Text.RegularExpressions;

namespace Antiphon.LandGraphAudit;

/// <summary>
/// Guarded removal of one recorded land-graph canary directory: the former
/// scripts/lib/land-graph-canary-removal.ps1 (Remove-RecordedCanary), with the same guards in the same order.
/// <see cref="LandGraphAuditGate"/> calls it on the kill, tool-watchdog, timeout and exception paths. It is not a
/// command or a script parameter and it never exits the process: a successful audit exit is only the gate after
/// both passes approve. String tests are the script's: <c>.EndsWith('/')</c> and <c>.IndexOf("`n")</c> are the
/// current-culture string overloads, <c>-cnotmatch</c> is a case-sensitive regex, paths compare ordinally.
/// </summary>
internal static class LandGraphCanaryRemoval
{
    /// <summary>Guard names, as the script marked them (# guard:name), for <see cref="Remove"/>'s test seam.</summary>
    internal static readonly string[] Guards = ["repo-separator", "repo-directory", "name", "parent", "rebuilt", "repo-root"];

    /// <param name="recordPath">The canary record file the evaluator wrote (one line, the canary directory).</param>
    /// <param name="repositoryRoot">The audited repository; the canary must be its sibling.</param>
    /// <param name="stderr">Receives "canary delete failed: ..." when the delete itself fails.</param>
    /// <param name="disabledGuard">Tests only: skips the one named guard, to prove each guard is necessary.</param>
    internal static void Remove(string? recordPath, string? repositoryRoot, TextWriter stderr, string? disabledGuard = null)
    {
        bool Guard(string name) => !string.Equals(disabledGuard, name, StringComparison.Ordinal);
        recordPath ??= "";
        repositoryRoot ??= "";
        if (string.IsNullOrWhiteSpace(recordPath)) return;
        if (string.IsNullOrWhiteSpace(repositoryRoot)) return;
        // GetDirectoryName("repo/") is the repository, so a trailing separator would
        // treat a canary-named directory inside the repo as a sibling of it.
        if (Guard("repo-separator") && (EndsWith(repositoryRoot, "/") || EndsWith(repositoryRoot, "\\"))) return;
        if (Guard("repo-directory") && !Directory.Exists(repositoryRoot)) return;
        if (!File.Exists(recordPath)) return;
        var recordedRaw = PsNet.Call("ReadAllText", 1, () => File.ReadAllText(recordPath));
        if (Ps.IndexOf(recordedRaw, "\n") >= 0 || Ps.IndexOf(recordedRaw, "\r") >= 0) return;
        var recorded = recordedRaw.Trim();
        if (string.IsNullOrWhiteSpace(recorded)) return;
        var hasTrailingSeparator = EndsWith(recorded, "/") || EndsWith(recorded, "\\");
        var stripped = recorded.TrimEnd('/', '\\');
        if (string.IsNullOrWhiteSpace(stripped)) return;
        var strippedFull = PsNet.Call("GetFullPath", 1, () => Path.GetFullPath(stripped));
        var name = Path.GetFileName(strippedFull);
        if (Guard("name") && !Regex.IsMatch(name, @"\Aantiphon-landgraph-canary-[0-9a-f]{32}\z")) return;
        var parent = Path.GetDirectoryName(strippedFull);
        var expectedParent = PsNet.Call("GetFullPath", 1, () => Path.GetFullPath(Path.GetDirectoryName(repositoryRoot) ?? ""));
        if (Guard("parent") && !string.Equals(parent, expectedParent, StringComparison.Ordinal)) return;
        var roundTrip = PsNet.Call("GetFullPath", 1, () => Path.GetFullPath(Path.Combine(parent ?? "", name)));
        if (Guard("rebuilt") && (hasTrailingSeparator || !string.Equals(strippedFull, roundTrip, StringComparison.Ordinal))) return;
        if (Guard("repo-root") && string.Equals(strippedFull, PsNet.Call("GetFullPath", 1, () => Path.GetFullPath(repositoryRoot)), StringComparison.Ordinal)) return;
        if (!Directory.Exists(strippedFull)) return;
        var link = (PsNet.Call("GetAttributes", 1, () => File.GetAttributes(strippedFull)) & FileAttributes.ReparsePoint) != 0;
        try
        {
            if (link) PsNet.Call("Delete", 2, () => Directory.Delete(strippedFull, false));
            else PsNet.Call("Delete", 2, () => Directory.Delete(strippedFull, true));
        }
        catch (AuditRefusal failed)
        {
            stderr.WriteLine("canary delete failed: " + failed.Message);
        }
    }

    /// <summary><c>$text.EndsWith('x')</c>: the string overload, current culture.</summary>
    private static bool EndsWith(string text, string value) => text.EndsWith(value, StringComparison.CurrentCulture);
}
