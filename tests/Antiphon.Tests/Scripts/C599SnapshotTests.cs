using System.Diagnostics;
using Antiphon.Nightly;
using Antiphon.Tests.Scripts.Nightly;
using Antiphon.Tests.TestHelpers;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

// CARD-1731: the evidence-path cases (G-287 containment on the production resolver, ReleaseAuthority.ResolveEvidencePath),
// in process. Unit 15 of 2026-11-22-remaining-ps1-to-csharp.md deleted scripts/test-release-gate.ps1 and the fixture it
// dot-sourced for them (scripts/fixtures/c1731-evidence-path.ps1); these are its Test-C1731_EvidenceRefusals and
// Test-C1731_EvidenceWalk with their row names. Unit 14 had moved the C599A matrices in process (ReleaseGateFixture's
// Save/Restore), so the PowerShell snapshot helpers and their two cases (C1731_SnapshotIsolation, C1731_SnapshotWork,
// scripts/fixtures/c1731-snapshot.ps1) went with them.
[Category("Integration")]
[Category("Slow")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class C599SnapshotTests
{
    [Test]
    public void Evidence_path_refusals_match_the_seeded_set()
    {
        // Each G-287 shape refuses, and every accepted path is the normalized full path. No acceptance cache: the second
        // call must see a component that became a link.
        using var fx = NightlyFixture.Create("c1731-evidence");
        var rows = new NightlyCaseRows();
        var candidate = EvidenceRoot(fx);
        string Full(string relative) => Path.GetFullPath(Path.Combine(Path.GetFullPath(candidate).TrimEnd('\\', '/'), relative));
        var ordinary = Full("evidence/suite.trx");
        var missing = Full("evidence/missing.trx");
        var missingParent = Full("no/such/file.trx");
        var hard = Full("evidence/hard.trx");
        var cases = new (string Name, string Relative, string Expected)[]
        {
            ("ordinary", "evidence/suite.trx", ordinary), ("missing-file", "evidence/missing.trx", missing), ("missing-parent", "no/such/file.trx", missingParent),
            ("blank", "   ", ""), ("empty", "", ""), ("absolute", ordinary, ""), ("parent-escape", "../outside/escape.trx", ""),
            ("deep-escape", "evidence/../../outside/escape.trx", ""), ("dotdot-inside", "evidence/../evidence/suite.trx", ordinary),
            ("dot-segment", "./evidence/./suite.trx", ordinary), ("escape-link", "escape-link/escape.trx", ""), ("inside-link", "inside-link/kept.trx", ""),
            ("mid-link", "evidence/mid-link/x.trx", ""), ("file-link", "evidence/filelink.trx", ""), ("broken-link", "broken-link", ""),
            ("broken-child", "broken-link/file.trx", ""), ("hard-link", "evidence/hard.trx", hard), ("root-dot", ".", ""),
        };
        var got = cases.ToDictionary(c => c.Name, c => ReleaseAuthority.ResolveEvidencePath(candidate, c.Relative) ?? "");
        rows.Check(got["ordinary"] == ordinary, "C1731 ordinary relative evidence resolves under the candidate root", got["ordinary"]);
        rows.Check(got["missing-file"] == missing && got["missing-parent"] == missingParent, "C1731 missing evidence resolves when no component is a reparse point",
            got["missing-file"] + " " + got["missing-parent"]);
        rows.Check(got["blank"] == "" && got["empty"] == "", "C1731 blank and empty evidence refuses", got["blank"] + "/" + got["empty"]);
        rows.Check(got["absolute"] == "", "C1731 absolute evidence refuses", got["absolute"]);
        rows.Check(got["parent-escape"] == "" && got["deep-escape"] == "" && got["root-dot"] == "", "C1731 parent-relative escape refuses",
            got["parent-escape"] + " " + got["deep-escape"]);
        rows.Check(got["dotdot-inside"] == ordinary && got["dot-segment"] == ordinary, "C1731 a path that normalizes inside the root resolves to that file",
            got["dotdot-inside"] + " " + got["dot-segment"]);
        rows.Check(got["escape-link"] == "", "C1731 a linked directory pointing outside refuses", got["escape-link"]);
        rows.Check(got["inside-link"] == "", "C1731 a linked directory whose target stays inside the root still refuses", got["inside-link"]);
        rows.Check(got["mid-link"] == "", "C1731 a link in a middle path component refuses", got["mid-link"]);
        rows.Check(got["file-link"] == "", "C1731 a linked file refuses", got["file-link"]);
        rows.Check(got["broken-link"] == "" && got["broken-child"] == "", "C1731 a broken link refuses", got["broken-link"] + " " + got["broken-child"]);
        rows.Check(got["hard-link"] == hard, "C1731 a hard link resolves", got["hard-link"]);
        var differ = cases.Where(c => got[c.Name] != c.Expected).Select(c => c.Name + " got=[" + got[c.Name] + "] expected=[" + c.Expected + "]").ToArray();
        rows.Check(differ.Length == 0, "C1731 seeded evidence results match the recorded set", string.Join("; ", differ));

        var fresh = Path.Combine(candidate, "fresh");
        Directory.CreateDirectory(fresh);
        File.WriteAllText(Path.Combine(fresh, "file.trx"), "f");
        var before = ReleaseAuthority.ResolveEvidencePath(candidate, "fresh/file.trx") ?? "";
        File.Delete(Path.Combine(fresh, "file.trx"));
        Directory.Delete(fresh);
        EvidenceLinks.Directory(fresh, fx.PathOf("outside"));
        var after = ReleaseAuthority.ResolveEvidencePath(candidate, "fresh/escape.trx") ?? "";
        rows.Check(before == Full("fresh/file.trx") && after == "", "C1731 an accepted path refuses after that component becomes a link", "before=" + before + " after=" + after);
        rows.ShouldAllPass(14,
            "C1731 ordinary relative evidence resolves under the candidate root",
            "C1731 missing evidence resolves when no component is a reparse point",
            "C1731 blank and empty evidence refuses",
            "C1731 absolute evidence refuses",
            "C1731 parent-relative escape refuses",
            "C1731 a path that normalizes inside the root resolves to that file",
            "C1731 a linked directory pointing outside refuses",
            "C1731 a linked directory whose target stays inside the root still refuses",
            "C1731 a link in a middle path component refuses",
            "C1731 a linked file refuses",
            "C1731 a broken link refuses",
            "C1731 a hard link resolves",
            "C1731 seeded evidence results match the recorded set",
            "C1731 an accepted path refuses after that component becomes a link");
    }

    [Test]
    public void Evidence_path_walk_finishes_within_fifteen_seconds()
    {
        // 611 ordinary resolutions is the 2026-11-06 ledger path-walk count. The cmdlet walk measured 138.2s at 9e7ff34988;
        // the ceiling is 15s, about 100x the 0.153s attribute-walk sample (n=1, provisional).
        using var fx = NightlyFixture.Create("c1731-walk");
        var rows = new NightlyCaseRows();
        var candidate = EvidenceRoot(fx);
        var paths = Enumerable.Range(1, 13).Select(i => "evidence/suite-" + i.ToString("d3", System.Globalization.CultureInfo.InvariantCulture) + ".trx").ToArray();
        var watch = Stopwatch.StartNew();
        for (var i = 0; i < 47; i++)
            foreach (var path in paths) ReleaseAuthority.ResolveEvidencePath(candidate, path);
        var ms = watch.Elapsed.TotalMilliseconds;
        rows.Check(ms < 15000, "C1731 six hundred eleven ordinary resolutions finish within fifteen seconds", "elapsedMs=" + (int)ms);
        rows.ShouldAllPass(1, "C1731 six hundred eleven ordinary resolutions finish within fifteen seconds");
    }

    // New-C1731EvidenceRoot: a candidate root with ordinary evidence, an outside directory, a linked directory pointing
    // outside, one pointing inside, a broken one, a hard link, a linked file and a link in a middle component.
    private static string EvidenceRoot(NightlyFixture fx)
    {
        var candidate = fx.PathOf("candidate");
        Directory.CreateDirectory(Path.Combine(candidate, "evidence"));
        Directory.CreateDirectory(fx.PathOf("outside"));
        Directory.CreateDirectory(Path.Combine(candidate, "real-dir"));
        Directory.CreateDirectory(fx.PathOf("break-target"));
        File.WriteAllText(Path.Combine(candidate, "evidence", "suite.trx"), "trx");
        File.WriteAllText(fx.PathOf("outside", "escape.trx"), "out");
        File.WriteAllText(Path.Combine(candidate, "real-dir", "kept.trx"), "in");
        File.WriteAllText(Path.Combine(candidate, "evidence", "hard-source.trx"), "hard");
        for (var i = 1; i <= 13; i++)
            File.WriteAllText(Path.Combine(candidate, "evidence", "suite-" + i.ToString("d3", System.Globalization.CultureInfo.InvariantCulture) + ".trx"), "trx");
        EvidenceLinks.Directory(Path.Combine(candidate, "escape-link"), fx.PathOf("outside"));
        EvidenceLinks.Directory(Path.Combine(candidate, "inside-link"), Path.Combine(candidate, "real-dir"));
        EvidenceLinks.Directory(Path.Combine(candidate, "broken-link"), fx.PathOf("break-target"));
        Directory.Delete(fx.PathOf("break-target"));
        NativeHardLink.Create(Path.Combine(candidate, "evidence", "hard.trx"), Path.Combine(candidate, "evidence", "hard-source.trx"));
        EvidenceLinks.File(Path.Combine(candidate, "evidence", "filelink.trx"), Path.Combine(candidate, "evidence", "suite.trx"));
        Directory.CreateDirectory(Path.Combine(candidate, "evidence", "mid-real"));
        File.WriteAllText(Path.Combine(candidate, "evidence", "mid-real", "x.trx"), "mid");
        EvidenceLinks.Directory(Path.Combine(candidate, "evidence", "mid-link"), Path.Combine(candidate, "evidence", "mid-real"));
        return candidate;
    }
}

/// <summary>
/// The release-gate cases' links (New-C1731DirectoryLink, New-C1731FileLink, New-C599AEvidenceLink): a symbolic link, or on
/// Windows - where creating one may need a privilege - a junction (mklink /J, a cmd.exe child). A linked file that cannot
/// be a symbolic link is a junction to a sibling directory named filelink-target: still a reparse point.
/// </summary>
internal static class EvidenceLinks
{
    /// <summary>A directory link: a symbolic link, else (Windows) a junction.</summary>
    public static void Directory(string link, string target)
    {
        try
        {
            System.IO.Directory.CreateSymbolicLink(link, target);
            return;
        }
        catch (Exception e) when (OperatingSystem.IsWindows() && (e is IOException || e is UnauthorizedAccessException)) { }
        Junction(link, target);
    }

    /// <summary>A file link: a symbolic link, else (Windows) a junction to &lt;parent&gt;\filelink-target.</summary>
    public static void File(string link, string target)
    {
        try
        {
            System.IO.File.CreateSymbolicLink(link, target);
            return;
        }
        catch (Exception e) when (OperatingSystem.IsWindows() && (e is IOException || e is UnauthorizedAccessException)) { }
        var directory = Path.Combine(Path.GetDirectoryName(link)!, "filelink-target");
        System.IO.Directory.CreateDirectory(directory);
        Junction(link, directory);
    }

    /// <summary>New-C599AEvidenceLink: a junction on Windows (no privilege needed), a symbolic link elsewhere.</summary>
    public static void Owned(string link, string target)
    {
        if (OperatingSystem.IsWindows()) Junction(link, target);
        else System.IO.Directory.CreateSymbolicLink(link, target);
    }

    private static void Junction(string link, string target)
    {
        var start = new ProcessStartInfo("cmd.exe") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (var argument in new[] { "/d", "/c", "mklink", "/J", link, target }) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new IOException("cmd.exe did not start for mklink /J");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0) throw new IOException("mklink /J " + link + " failed: " + output.Result + error);
    }
}
