using System.Diagnostics;
using System.Text;
using Antiphon.LiveLane;
using Antiphon.Scripts;
using Antiphon.Tests.Application;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// W2 slice S22: a fixture repository for the live lane's fingerprint, receipt and status tests (the in-process successors
/// of scripts/test-live-lane-fingerprint.ps1 and test-live-lane-receipt.ps1). git runs with a fixed author, committer and
/// date, no hooks, no signing and no line-ending conversion, so a commit's id depends only on its tree, message and parent.
/// </summary>
internal sealed class LiveLaneRepo
{
    private static readonly UTF8Encoding Utf8 = new(false);

    public LiveLaneRepo(string root)
    {
        Root = root;
        Directory.CreateDirectory(root);
        Hooks = Path.Combine(Path.GetDirectoryName(root)!, ".no-hooks");
        Directory.CreateDirectory(Hooks);
        Git("init", "-q", "-b", "main");
    }

    public string Root { get; }
    public string Hooks { get; }

    public string Git(params string[] arguments) => RunGit(Root, Hooks, arguments);

    /// <summary>git in any directory with the fixture's identity, dates and settings.</summary>
    public static string RunGit(string directory, string hooks, params string[] arguments)
    {
        var start = new ProcessStartInfo("git") { WorkingDirectory = directory, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var argument in new[] { "-c", "user.name=Fixture", "-c", "user.email=fixture@example.invalid", "-c", "commit.gpgsign=false", "-c", "core.hooksPath=" + hooks,
                     "-c", "core.autocrlf=false" }.Concat(arguments)) start.ArgumentList.Add(argument);
        start.Environment["GIT_AUTHOR_DATE"] = "2026-11-01T00:00:00Z";
        start.Environment["GIT_COMMITTER_DATE"] = "2026-11-01T00:00:00Z";
        using var process = Process.Start(start)!;
        var error = process.StandardError.ReadToEndAsync();
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0) throw new InvalidOperationException("git " + string.Join(' ', arguments) + ": " + output + error.Result);
        return output.Trim();
    }

    public void Put(string path, string text)
    {
        var full = Path.Combine(Root, path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, text, Utf8);
    }

    public void Drop(string path) => File.Delete(Path.Combine(Root, path));

    public string Read(string path) => File.ReadAllText(Path.Combine(Root, path));

    /// <summary>Every change of the working tree staged, then committed.</summary>
    public string Commit(string message = "fixture")
    {
        Git("add", "--all");
        return CommitIndex(message);
    }

    /// <summary>The index committed as staged (a mode, a symbolic link or a gitlink the working tree cannot carry).</summary>
    public string CommitIndex(string message = "fixture")
    {
        Git("commit", "-q", "--allow-empty", "-m", message);
        return Git("rev-parse", "HEAD");
    }

    public void Checkout(string sha)
    {
        Git("checkout", "-q", "-f", "--detach", sha);
        Git("clean", "-q", "-fdx");
    }

    /// <summary>A symbolic link entry (mode 120000) at path naming target, staged without touching the working tree.</summary>
    public void StageLink(string path, string target)
    {
        var file = Path.Combine(Path.GetDirectoryName(Root)!, "link-target-" + Guid.NewGuid().ToString("N")[..8] + ".txt");
        File.WriteAllText(file, target, Utf8);
        var blob = Git("hash-object", "-w", file);
        Git("update-index", "--add", "--cacheinfo", "120000," + blob + "," + path);
    }

    /// <summary>A gitlink entry (mode 160000) at path naming commit.</summary>
    public void StageGitlink(string path, string commit) => Git("update-index", "--add", "--cacheinfo", "160000," + commit + "," + path);
}

/// <summary>The scratch directory of one test, removed afterwards.</summary>
internal sealed class LaneScratch : IDisposable
{
    public LaneScratch(string prefix)
    {
        Root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), prefix + Guid.NewGuid().ToString("N")[..12]);
        Directory.CreateDirectory(Root);
    }

    public string Root { get; }

    public string Path(params string[] parts) => System.IO.Path.Combine(new[] { Root }.Concat(parts).ToArray());

    public void Dispose()
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories)) File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(Root, true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}

/// <summary>The CLI's reads (the nightly core's git and rc project graph), fresh per call as the CLI makes them.</summary>
internal static class LiveLaneTestSources
{
    public static LiveLaneSources Create() => LiveLaneNightlySources.Create();

    public static string RepoRoot => DelegateScriptRunner.RepoRoot;
}

/// <summary>The lane roster and manifest the live lane's attestation, receipt and status tests share.</summary>
internal static class LiveLaneLaneFixture
{
    // The lane roster, written out here as an independent oracle: the six S1 core uids (LiveClaudeCore.All) in the
    // manifest's namespace.class.method(arguments) form.
    public static readonly string[] Roster =
    [
        "Antiphon.Agents.Pty.Tests.ClaudeSubmitContractLiveTests.Submitting_via_two_writes_completes_a_turn(claude)",
        "Antiphon.Agents.Pty.Tests.ClaudeRemoteControlAtStartupCanaryTests.Control_auto_connects_a_bridge_session_before_the_first_user",
        "Antiphon.Agents.Pty.Tests.ClaudeRemoteControlAtStartupCanaryTests.Settings_file_prevents_auto_connect_and_keeps_startup_records",
        "Antiphon.Agents.Pty.Tests.ClaudeRemoteControlAtStartupCanaryTests.Remote_control_slash_command_still_arms_after_settings_off",
        "Antiphon.Agents.Pty.Tests.ClaudeRemoteControlMenuCanaryTests.C514_Idle_menu_clearance_preserves_bridge_and_converts_prompt",
        "Antiphon.Tests.Agents.ClaudeDialogWatchdogCanaryTests.C680_LiveNudgeRefusesPermissionAndQuestionDialogs",
    ];

    /// <summary>The lane manifest of a roster: namespace.class.method(arguments) split into one core row each.</summary>
    public static LiveLaneMap Manifest(IEnumerable<string> roster)
    {
        var core = new List<object>();
        foreach (var uid in roster)
        {
            var match = System.Text.RegularExpressions.Regex.Match(uid, @"^(.+)\.([^.]+)\.([^.(]+)(\((.*)\))?$");
            var arguments = match.Groups[4].Success ? match.Groups[5].Value.Split(',').Cast<object>().ToList() : new List<object>();
            core.Add(new LiveLaneMap(("project", "Pty.Tests"), ("namespace", match.Groups[1].Value), ("class", match.Groups[2].Value), ("method", match.Groups[3].Value),
                ("arguments", arguments)));
        }
        return new LiveLaneMap(("format", "antiphon-live-lanes-v1"), ("core", core), ("gates", new List<object>()), ("additiveContract", new List<object>()));
    }
}
