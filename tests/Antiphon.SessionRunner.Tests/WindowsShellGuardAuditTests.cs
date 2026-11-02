using Shouldly;
using TUnit.Core;

namespace Antiphon.SessionRunner.Tests;

/// <summary>
/// CARD-1652. Off Windows <c>Environment.SystemDirectory</c> is empty, so a hard-coded command
/// interpreter becomes the bare name and the child either fails to start (a Windows-dependent red)
/// or exits at once (a vacuous green for a test that only asserts something did NOT happen).
/// Invariant: every command-interpreter literal in this project's sources is behind an
/// <c>OperatingSystem.IsWindows()</c> guard on its own or a nearby line, or is a reviewed site
/// listed below with the reason the Linux lane cannot reach it as a spawn.
/// </summary>
[Category("Unit")]
public sealed class WindowsShellGuardAuditTests
{
    private static readonly string Interpreter = "cmd" + ".exe";

    /// <summary>Lines above a literal that may carry its guard (a ternary or an exception filter).</summary>
    private const int GuardWindow = 6;

    /// <summary>
    /// Reviewed sites, keyed by file name and the trimmed source line. Each one is a string the test
    /// never spawns, or a spawn behind a method- or fixture-level skip this line scan cannot see.
    /// </summary>
    private static readonly (string File, string Line, string Reason)[] Reviewed =
    [
    ];

    [Test]
    public void Every_command_interpreter_literal_is_behind_a_windows_guard()
    {
        var offenders = new List<string>();
        foreach (var path in SourceFiles())
            offenders.AddRange(Scan(Path.GetFileName(path), File.ReadAllLines(path)));

        offenders.ShouldBeEmpty(
            "C1652_UNGUARDED_INTERPRETER: guard with OperatingSystem.IsWindows(), use HerdrTestProcess.ShellPath, "
            + "or review the site in WindowsShellGuardAuditTests.Reviewed:\n" + string.Join("\n", offenders));
    }

    [Test]
    public void Every_reviewed_site_still_exists_exactly_once()
    {
        var files = SourceFiles().ToDictionary(Path.GetFileName, File.ReadAllLines, StringComparer.Ordinal);
        var stale = Reviewed
            .Where(r => !files.TryGetValue(r.File, out var lines)
                || lines.Count(l => string.Equals(l.Trim(), r.Line, StringComparison.Ordinal)) != 1)
            .Select(r => r.File + ": " + r.Line)
            .ToList();
        stale.ShouldBeEmpty("C1652_STALE_REVIEWED_SITE:\n" + string.Join("\n", stale));
        Reviewed.ShouldAllBe(r => r.Reason.Length > 0);
    }

    [Test]
    public void Scan_flags_an_unguarded_literal_and_accepts_guarded_or_commented_ones()
    {
        var bare = "private static string Cmd => Path.Combine(Environment.SystemDirectory, \"" + Interpreter + "\");";
        Scan("Probe.cs", ["    " + bare]).ShouldHaveSingleItem().ShouldContain("Probe.cs:1:");
        Scan("Probe.cs", ["var exe = OperatingSystem.IsWindows()", "    ? Path.Combine(Environment.SystemDirectory, \"" + Interpreter + "\")", "    : \"/bin/sh\";"])
            .ShouldBeEmpty();
        Scan("Probe.cs", ["// launches " + Interpreter + " on Windows", "/// <c>" + Interpreter + "</c>"]).ShouldBeEmpty();
        Scan("Probe.cs", ["Run(HerdrTestProcess.ShellPath); // " + Interpreter + " on Windows"]).ShouldBeEmpty();
        Scan("Probe.cs", ["var s = \"http://x\"; Start(\"" + Interpreter + "\");"]).ShouldHaveSingleItem();
        var far = new List<string> { "if (OperatingSystem.IsWindows()) return;" };
        far.AddRange(Enumerable.Repeat("x();", GuardWindow));
        far.Add(bare);
        Scan("Probe.cs", far).ShouldHaveSingleItem("a guard outside the window does not count");
    }

    internal static IReadOnlyList<string> Scan(string file, IReadOnlyList<string> lines)
    {
        var offenders = new List<string>();
        for (var i = 0; i < lines.Count; i++)
        {
            var code = CodePart(lines[i]);
            if (code.IndexOf(Interpreter, StringComparison.OrdinalIgnoreCase) < 0)
                continue;
            var guarded = false;
            for (var j = Math.Max(0, i - GuardWindow); j <= i && !guarded; j++)
                guarded = CodePart(lines[j]).Contains("OperatingSystem.IsWindows()", StringComparison.Ordinal);
            if (guarded)
                continue;
            var trimmed = lines[i].Trim();
            if (Reviewed.Any(r => r.File == file && r.Line == trimmed))
                continue;
            offenders.Add($"{file}:{i + 1}: {trimmed}");
        }
        return offenders;
    }

    /// <summary>The line without a trailing <c>//</c> comment; quotes are tracked so a URL survives.</summary>
    private static string CodePart(string line)
    {
        var trimmed = line.TrimStart();
        if (trimmed.StartsWith("//", StringComparison.Ordinal) || trimmed.StartsWith('*'))
            return "";
        var inString = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (inString && c == '\\') { i++; continue; }
            if (c == '"') inString = !inString;
            else if (!inString && c == '/' && i + 1 < line.Length && line[i + 1] == '/')
                return line[..i];
        }
        return line;
    }

    private static IEnumerable<string> SourceFiles()
    {
        var project = Path.Combine(RepoRoot, "tests", "Antiphon.SessionRunner.Tests");
        var bin = Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar;
        var obj = Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar;
        return Directory.EnumerateFiles(project, "*.cs", SearchOption.AllDirectories)
            .Where(p => !p.Contains(bin, StringComparison.Ordinal) && !p.Contains(obj, StringComparison.Ordinal))
            // This file names the interpreter only as data (the reviewed keys and the scanner probes).
            .Where(p => Path.GetFileName(p) != nameof(WindowsShellGuardAuditTests) + ".cs")
            .Append(Path.Combine(RepoRoot, "tests", "Shared", "HerdrTestProcess.cs"))
            .Order(StringComparer.Ordinal);
    }

    private static string RepoRoot
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Antiphon.sln")))
                dir = dir.Parent;
            return dir?.FullName
                ?? throw new DirectoryNotFoundException("Could not locate repo root (Antiphon.sln) from test base dir.");
        }
    }
}
