using System.Text.Json;
using Antiphon.Agents.Pty;
using Shouldly;
using TUnit.Core;
using TUnit.Core.Exceptions;

namespace Antiphon.Agents.Pty.Tests;

/// <summary>
/// CARD-0306 S0: prove against the real interactive Claude TUI that a per-launch
/// <c>--settings</c> file with <c>remoteControlAtStartup: false</c> beats the org/unset
/// auto-connect default, and that <c>/remote-control</c> still arms afterwards.
///
/// Interactive TUI, not <c>claude -p</c>. Isolated cwd + <c>--session-id</c> +
/// <c>--dangerously-skip-permissions</c>. Does not write <c>~/.claude/settings.json</c>
/// and does not submit a model turn. Headed, opt-in (<c>ANTIPHON_HEADED_TESTS=1</c>),
/// <c>[Explicit]</c>. S1 is blocked on case 2 of this canary.
///
/// Bridge evidence is the live trio Claude actually writes: the footer <c>/rc</c> badge,
/// <c>~/.claude/sessions/&lt;pid&gt;.json</c> <c>bridgeSessionId</c> (CARD-0292's probe),
/// and the JSONL <c>type==bridge-session</c> record when the file has been flushed.
/// A throwaway cwd does not always flush JSONL before the first user turn (measured
/// 2026-09-01); the badge and per-pid file are the launch-time signals.
/// </summary>
[NotInParallel("Headed")]
[Category("Headed")]
[Category("HeadedCanary")]
[Explicit]
[ParallelLimiter<ProcessSpawnLimit>]
public class ClaudeRemoteControlAtStartupCanaryTests
{
    private const string OffSettingsJson = "{\"remoteControlAtStartup\":false}";
    private static readonly TimeSpan OverlayObserve = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan BridgeAppear = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan ArmWait = TimeSpan.FromSeconds(45);

    /// <summary>
    /// Case 1 — control: current Antiphon-like argv, no <c>--settings</c>. Auto-connect must
    /// still be the default on this machine. If this skips/fails, the card's premise has moved.
    /// </summary>
    [Test]
    public async Task Control_auto_connects_a_bridge_session_before_the_first_user()
    {
        ClSession.SkipIfNotEligible();
        using var sandbox = CanarySandbox.Create(writeOffSettings: false);

        await using var runner = new PtyAgentRunner("modern");
        await LaunchUntilReadyAsync(runner, sandbox, extraArgs: []);
        try
        {
            var evidence = await WaitForBridgeAsync(runner, sandbox, BridgeAppear);
            LogEvidence("CONTROL", sandbox, evidence);

            if (!evidence.Armed)
            {
                throw new SkipTestException(
                    "control run has no /rc badge, no sessions/*.json bridgeSessionId, and no "
                    + "JSONL bridge-session — auto-connect is no longer the default on this "
                    + "machine and CARD-0306's premise has moved. Screen:\n"
                    + runner.SnapshotScreen());
            }

            evidence.BridgeId.ShouldNotBeNullOrWhiteSpace(
                "auto-connect must allocate a real bridgeSessionId (badge-only is logged, not enough)");
            if (evidence.JsonlPath is not null)
            {
                var userIndex = FirstUserIndex(evidence.JsonlPath);
                if (userIndex >= 0 && evidence.JsonlLineIndex is { } line)
                    line.ShouldBeLessThan(userIndex, "auto-connect must land before the first user record");
            }
        }
        finally
        {
            await ExitAsync(runner);
        }
    }

    /// <summary>
    /// Case 2 + case 4 — same argv plus <c>--settings &lt;off.json&gt;</c>. No bridge for at
    /// least 8 s after ready. TUI still reaches ready and the composer still accepts input.
    /// JSONL startup kinds are logged when the file exists; their absence on a throwaway cwd
    /// is not a fail (case 4: crash or missing composer only).
    /// </summary>
    [Test]
    public async Task Settings_file_prevents_auto_connect_and_keeps_startup_records()
    {
        ClSession.SkipIfNotEligible();
        using var sandbox = CanarySandbox.Create(writeOffSettings: true);

        await using var runner = new PtyAgentRunner("modern");
        var extra = new[] { "--settings", sandbox.SettingsPath! };
        await LaunchUntilReadyAsync(runner, sandbox, extra);
        try
        {
            var evidence = await ObserveAsync(runner, sandbox, OverlayObserve);
            LogEvidence("OVERLAY", sandbox, evidence);

            evidence.Armed.ShouldBeFalse(
                "remoteControlAtStartup:false via --settings <file> must beat auto-connect. "
                + FormatEvidence(evidence)
                + "\nScreen:\n" + runner.SnapshotScreen());

            if (evidence.JsonlPath is not null)
            {
                var types = ReadTypes(evidence.JsonlPath);
                Console.WriteLine($"OVERLAY TYPES: {string.Join(",", types)}");
                types.ShouldContain("permission-mode",
                    "overlay must not replace user/project settings — control always writes permission-mode");
                types.ShouldContain("atis-latch",
                    "overlay must not replace user/project settings — control always writes atis-latch");
            }
            else
            {
                Console.WriteLine("OVERLAY JSONL: <not flushed — throwaway cwd; composer is the case-4 gate>");
            }

            var token = "RC-OFF-" + Guid.NewGuid().ToString("N")[..8];
            await runner.WriteAsync(token);
            (await runner.WaitForScreenAsync(
                s => ComposerDeliveryEvidence.FragmentIsVisible(s, token),
                TimeSpan.FromSeconds(8)))
                .ShouldBeTrue(
                    "overlay session must still have a working composer. Screen:\n"
                    + runner.SnapshotScreen());
        }
        finally
        {
            await ExitAsync(runner);
        }
    }

    /// <summary>
    /// Case 3 — overlay off, then type <c>/remote-control</c> + Enter. The bridge must appear
    /// (opt-in still works). Do not assert the management menu — that is the already-armed shape.
    /// </summary>
    [Test]
    public async Task Remote_control_slash_command_still_arms_after_settings_off()
    {
        ClSession.SkipIfNotEligible();
        using var sandbox = CanarySandbox.Create(writeOffSettings: true);

        await using var runner = new PtyAgentRunner("modern");
        var extra = new[] { "--settings", sandbox.SettingsPath! };
        await LaunchUntilReadyAsync(runner, sandbox, extra);
        try
        {
            var before = await ObserveAsync(runner, sandbox, OverlayObserve);
            LogEvidence("ARM-BEFORE", sandbox, before);
            before.Armed.ShouldBeFalse(
                "/remote-control arming is only meaningful if auto-connect is already off. "
                + FormatEvidence(before)
                + "\nScreen:\n" + runner.SnapshotScreen());

            await SubmitRemoteControlAsync(runner);

            var after = await WaitForSessionBridgeAsync(runner, sandbox, ArmWait);
            LogEvidence("ARM-AFTER", sandbox, after);
            Console.WriteLine("ARM SCREEN:\n" + runner.SnapshotScreen());

            ManagementMenuPresent(runner.SnapshotScreen()).ShouldBeFalse(
                "management menu means the session was already bridged — /remote-control did not "
                + "arm, it opened Disconnect/Continue. Screen:\n" + runner.SnapshotScreen());

            after.SessionFileBridgeId.ShouldNotBeNullOrWhiteSpace(
                "/remote-control after remoteControlAtStartup:false must still allocate a "
                + "bridgeSessionId in ~/.claude/sessions/<pid>.json. A /rc badge alone is not "
                + "enough — this repo's /rc-status skill makes the slash autocomplete contain "
                + "the substring. If this is red, do not ApplyOff on RC-wanted launches. "
                + FormatEvidence(after));
        }
        finally
        {
            await ExitAsync(runner);
        }
    }

    private static async Task LaunchUntilReadyAsync(
        PtyAgentRunner runner,
        CanarySandbox sandbox,
        string[] extraArgs)
    {
        var args = new List<string> { "--dangerously-skip-permissions", "--session-id", sandbox.SessionId };
        args.AddRange(extraArgs);
        var (app, launchArgs) = ClSession.BuildLaunch(ClSession.ResolveOrThrow(), args.ToArray());
        await runner.StartAsync(
            app, launchArgs, cwd: sandbox.Dir, cols: 120, rows: 30, env: ClSession.HeadedSafeEnv());

        var ready = await new ClaudeReadyDetector().WaitAsync(runner);
        if (!ready)
            throw new SkipTestException("real Claude TUI did not reach a ready state. Screen:\n"
                + runner.SnapshotScreen());

        var blocking = await ClaudeBlockingPromptDetector.ClearStartupTrustPromptAsync(
            _ => Task.FromResult(runner.SnapshotScreen()),
            async (data, _) => await runner.WriteAsync(data));
        if (blocking.Outcome == ClaudeStartupBlockOutcome.TrustNotCleared)
            throw new SkipTestException("trust dialog did not clear. Screen:\n" + runner.SnapshotScreen());
        if (blocking.Outcome == ClaudeStartupBlockOutcome.TrustCleared)
        {
            var settled = await new ClaudeReadyDetector
            {
                MinTotalWait = TimeSpan.Zero,
                MaxWait = TimeSpan.FromSeconds(30),
            }.WaitAsync(runner);
            if (!settled)
                throw new SkipTestException("TUI did not settle after answering the trust dialog. Screen:\n"
                    + runner.SnapshotScreen());
        }
    }

    private static async Task ExitAsync(PtyAgentRunner runner)
    {
        try { await runner.SendLineAsync("/exit"); }
        catch { /* already dead */ }
        await Task.WhenAny(runner.Exited, Task.Delay(TimeSpan.FromSeconds(5)));
        await runner.KillAsync(TimeSpan.FromSeconds(2));
    }

    private static bool ManagementMenuPresent(string screen) =>
        screen.Contains("Disconnect this session", StringComparison.Ordinal)
        && screen.Contains("Esc to continue", StringComparison.Ordinal);

    /// <summary>
    /// Submit <c>/remote-control</c> past the slash-command autocomplete. A single Enter
    /// often accepts the dropdown instead of executing (same race as
    /// <c>ClaudeLocalCommandCanaryTests</c>); a second Enter executes the selected item.
    /// </summary>
    private static async Task SubmitRemoteControlAsync(PtyAgentRunner runner)
    {
        await runner.WriteAsync("/remote-control");
        (await runner.WaitForScreenAsync(
            s => ComposerDeliveryEvidence.FragmentIsVisible(s, "/remote-control"),
            TimeSpan.FromSeconds(8)))
            .ShouldBeTrue("/remote-control must render in the composer before Enter. Screen:\n"
                + runner.SnapshotScreen());
        await Task.Delay(800);
        await runner.WriteAsync("\r");
        await Task.Delay(500);
        if (SlashAutocompleteOpen(runner.SnapshotScreen())
            || ComposerDeliveryEvidence.FragmentIsVisible(runner.SnapshotScreen(), "/remote-control"))
        {
            await runner.WriteAsync("\r");
        }
    }

    private static bool SlashAutocompleteOpen(string screen) =>
        screen.Contains("/launch-remote", StringComparison.Ordinal)
        || screen.Contains("/rc-status", StringComparison.Ordinal);

    /// <summary>
    /// Footer badge Claude paints when the bridge is live. Must not match this repo's
    /// <c>/rc-status</c> slash-command (the autocomplete lists it whenever <c>/r</c> is typed).
    /// </summary>
    private static bool RcBadgePresent(string screen)
    {
        for (var i = 0; i < screen.Length - 2; i++)
        {
            if (screen[i] != '/' || screen[i + 1] != 'r' || screen[i + 2] != 'c')
                continue;
            var after = i + 3;
            if (after < screen.Length && (char.IsLetterOrDigit(screen[after]) || screen[after] == '-'))
                continue;
            if (i > 0 && !char.IsWhiteSpace(screen[i - 1]))
                continue;
            return true;
        }

        return false;
    }

    private static async Task<BridgeEvidence> WaitForBridgeAsync(
        PtyAgentRunner runner, CanarySandbox sandbox, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        BridgeEvidence last;
        do
        {
            last = SnapshotEvidence(runner, sandbox);
            if (last.Armed)
                return last;
            await Task.Delay(400);
        } while (DateTime.UtcNow < deadline);

        return last;
    }

    /// <summary>
    /// Case 3 must wait for the per-pid <c>bridgeSessionId</c>, not the footer badge.
    /// <c>/remote-control</c> paints <c>/rc connecting…</c> before the id is written, and
    /// returning on the badge races the handshake.
    /// </summary>
    private static async Task<BridgeEvidence> WaitForSessionBridgeAsync(
        PtyAgentRunner runner, CanarySandbox sandbox, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        BridgeEvidence last;
        do
        {
            last = SnapshotEvidence(runner, sandbox);
            if (!string.IsNullOrWhiteSpace(last.SessionFileBridgeId) || last.JsonlBridgeId is not null)
                return last;
            await Task.Delay(400);
        } while (DateTime.UtcNow < deadline);

        return last;
    }

    private static async Task<BridgeEvidence> ObserveAsync(
        PtyAgentRunner runner, CanarySandbox sandbox, TimeSpan window)
    {
        var deadline = DateTime.UtcNow + window;
        BridgeEvidence last;
        do
        {
            last = SnapshotEvidence(runner, sandbox);
            if (last.Armed)
                return last;
            await Task.Delay(400);
        } while (DateTime.UtcNow < deadline);

        return last;
    }

    private static BridgeEvidence SnapshotEvidence(PtyAgentRunner runner, CanarySandbox sandbox)
    {
        var screen = runner.SnapshotScreen();
        var badge = RcBadgePresent(screen);
        var session = ReadSessionFile(sandbox.SessionId, sandbox.Dir);
        var jsonl = FindSessionJsonl(sandbox.SessionId);
        BridgeHit? jsonlBridge = jsonl is null ? null : FindBridge(jsonl);
        var bridgeId = session.BridgeId ?? jsonlBridge?.Id;
        var armed = badge
            || !string.IsNullOrWhiteSpace(session.BridgeId)
            || jsonlBridge is not null;
        return new BridgeEvidence(
            armed,
            badge,
            session.Found,
            session.BridgeId,
            jsonl,
            jsonlBridge?.Id,
            jsonlBridge?.LineIndex,
            bridgeId);
    }

    private static void LogEvidence(string label, CanarySandbox sandbox, BridgeEvidence e)
    {
        Console.WriteLine(
            $"{label} session={sandbox.SessionId} cwd={sandbox.Dir} armed={e.Armed} "
            + $"badge={e.Badge} sessionFile={e.SessionFileFound} sessionBridge={e.SessionFileBridgeId ?? "<none>"} "
            + $"jsonl={e.JsonlPath ?? "<none>"} jsonlBridge={e.JsonlBridgeId ?? "<none>"}");
        if (e.JsonlPath is not null)
            Console.WriteLine($"{label} TYPES: {string.Join(",", ReadTypes(e.JsonlPath))}");
    }

    private static string FormatEvidence(BridgeEvidence e) =>
        $"armed={e.Armed} badge={e.Badge} sessionFile={e.SessionFileFound} "
        + $"sessionBridge={e.SessionFileBridgeId ?? "<none>"} jsonl={e.JsonlPath ?? "<none>"} "
        + $"jsonlBridge={e.JsonlBridgeId ?? "<none>"}";

    private static string? FindSessionJsonl(string sessionId)
    {
        var projects = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "projects");
        if (!Directory.Exists(projects))
            return null;
        return Directory
            .EnumerateFiles(projects, $"{sessionId}.jsonl", SearchOption.AllDirectories)
            .FirstOrDefault();
    }

    private static (bool Found, string? BridgeId) ReadSessionFile(string sessionId, string cwd)
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "sessions");
        if (!Directory.Exists(dir))
            return (false, null);

        foreach (var path in Directory.EnumerateFiles(dir, "*.json"))
        {
            JsonDocument doc;
            try { doc = JsonDocument.Parse(File.ReadAllText(path)); }
            catch (IOException) { continue; }
            catch (JsonException) { continue; }
            catch (UnauthorizedAccessException) { continue; }
            using (doc)
            {
                var root = doc.RootElement;
                var fileSession = root.TryGetProperty("sessionId", out var sid) ? sid.GetString() : null;
                var fileCwd = root.TryGetProperty("cwd", out var c) ? c.GetString() : null;
                var match = string.Equals(fileSession, sessionId, StringComparison.OrdinalIgnoreCase)
                    || (fileCwd is not null
                        && string.Equals(
                            Path.GetFullPath(fileCwd).TrimEnd('\\'),
                            Path.GetFullPath(cwd).TrimEnd('\\'),
                            StringComparison.OrdinalIgnoreCase));
                if (!match)
                    continue;

                var id = root.TryGetProperty("bridgeSessionId", out var bridge)
                    && bridge.ValueKind == JsonValueKind.String
                    ? bridge.GetString()
                    : null;
                return (true, string.IsNullOrWhiteSpace(id) ? null : id);
            }
        }

        return (false, null);
    }

    private static List<string> ReadTypes(string jsonlPath)
    {
        var types = new List<string>();
        foreach (var line in ReadLinesShared(jsonlPath))
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;
            JsonDocument doc;
            try { doc = JsonDocument.Parse(line); }
            catch (JsonException) { continue; }
            using (doc)
            {
                if (doc.RootElement.TryGetProperty("type", out var type) && type.GetString() is { } t)
                    types.Add(t);
            }
        }

        return types;
    }

    private static BridgeHit? FindBridge(string jsonlPath)
    {
        var index = -1;
        foreach (var line in ReadLinesShared(jsonlPath))
        {
            index++;
            if (string.IsNullOrWhiteSpace(line))
                continue;
            JsonDocument doc;
            try { doc = JsonDocument.Parse(line); }
            catch (JsonException) { continue; }
            using (doc)
            {
                var root = doc.RootElement;
                if (!root.TryGetProperty("type", out var type) || type.GetString() != "bridge-session")
                    continue;
                var id = root.TryGetProperty("bridgeSessionId", out var bridge)
                    ? bridge.GetString()
                    : null;
                return new BridgeHit(id, index);
            }
        }

        return null;
    }

    private static int FirstUserIndex(string jsonlPath)
    {
        var index = -1;
        foreach (var line in ReadLinesShared(jsonlPath))
        {
            index++;
            if (string.IsNullOrWhiteSpace(line))
                continue;
            JsonDocument doc;
            try { doc = JsonDocument.Parse(line); }
            catch (JsonException) { continue; }
            using (doc)
            {
                if (doc.RootElement.TryGetProperty("type", out var type) && type.GetString() == "user")
                    return index;
            }
        }

        return -1;
    }

    private static List<string> ReadLinesShared(string jsonlPath)
    {
        var lines = new List<string>();
        using var stream = new FileStream(
            jsonlPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        while (reader.ReadLine() is { } line)
            lines.Add(line);
        return lines;
    }

    private sealed record BridgeHit(string? Id, int LineIndex);

    private sealed record BridgeEvidence(
        bool Armed,
        bool Badge,
        bool SessionFileFound,
        string? SessionFileBridgeId,
        string? JsonlPath,
        string? JsonlBridgeId,
        int? JsonlLineIndex,
        string? BridgeId);

    /// <summary>
    /// Throwaway cwd + optional off-settings file. Pre-trusts the directory in
    /// <c>~/.claude.json</c> (not <c>~/.claude/settings.json</c>) so the trust dialog does not
    /// confound the auto-connect measurement. Disposed by deleting the directory.
    /// </summary>
    private sealed class CanarySandbox : IDisposable
    {
        public string Dir { get; }
        public string SessionId { get; }
        public string? SettingsPath { get; }

        private CanarySandbox(string dir, string sessionId, string? settingsPath)
        {
            Dir = dir;
            SessionId = sessionId;
            SettingsPath = settingsPath;
        }

        public static CanarySandbox Create(bool writeOffSettings)
        {
            var dir = Directory.CreateTempSubdirectory("antiphon-rc-off-canary-").FullName;
            File.WriteAllText(
                Path.Combine(dir, "CLAUDE.md"),
                "Throwaway CARD-0306 canary working directory. Do not enable remote control.\n");
            var sessionId = Guid.NewGuid().ToString("D");
            string? settingsPath = null;
            if (writeOffSettings)
            {
                settingsPath = Path.Combine(dir, "remote-control-off.json");
                File.WriteAllText(settingsPath, OffSettingsJson);
            }

            Trust(dir);
            return new CanarySandbox(dir, sessionId, settingsPath);
        }

        /// <summary>
        /// Same both-spellings write as <c>ClaudeTrustPromptCanaryTests.UntrustedDirectory.Trust</c>.
        /// </summary>
        private static void Trust(string cwd)
        {
            var configPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude.json");
            if (!File.Exists(configPath))
                return;

            var node = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(configPath))?.AsObject();
            if (node is null)
                return;

            if (node["projects"] is not System.Text.Json.Nodes.JsonObject projects)
            {
                projects = new System.Text.Json.Nodes.JsonObject();
                node["projects"] = projects;
            }

            foreach (var key in new[] { cwd, cwd.Replace('\\', '/') })
            {
                if (projects[key] is not System.Text.Json.Nodes.JsonObject project)
                {
                    project = new System.Text.Json.Nodes.JsonObject();
                    projects[key] = project;
                }

                project["hasTrustDialogAccepted"] = true;
                project["hasCompletedProjectOnboarding"] = true;
                project["projectOnboardingSeenCount"] = 1;
            }

            File.WriteAllText(configPath, node.ToJsonString(
                new JsonSerializerOptions { WriteIndented = true }));
        }

        public void Dispose()
        {
            try
            {
                foreach (var path in Directory.EnumerateFileSystemEntries(Dir, "*", SearchOption.AllDirectories))
                {
                    try { File.SetAttributes(path, FileAttributes.Normal); }
                    catch { /* dir or gone */ }
                }

                Directory.Delete(Dir, recursive: true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
