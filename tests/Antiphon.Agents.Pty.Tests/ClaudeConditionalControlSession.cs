using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Antiphon.Agents.Pty;
using Antiphon.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Shouldly;
using TUnit.Core.Exceptions;

namespace Antiphon.Agents.Pty.Tests;

/// <summary>Portable, opt-in native fixture. Never attaches to a production session.</summary>
internal sealed class ClaudeConditionalControlSession : IAsyncDisposable
{
    public const string PermissionProbeSource =
        "const fs=require('fs');fs.writeFileSync('started.txt',String(process.pid));"
        + "fs.writeFileSync('completed.txt','permission was granted');";
    private readonly string _cwd = Directory.CreateTempSubdirectory("antiphon-c1517-").FullName;
    private readonly string _sessionId = Guid.NewGuid().ToString("D");
    private string? _transcript;
    private Process? _tool;
    private DateTime? _toolStart;
    private bool _seeded;
    public PtyAgentRunner Runner { get; } = new("modern");
    public string StartedPath => Path.Combine(_cwd, "started.txt");
    public string CompletedPath => Path.Combine(_cwd, "completed.txt");
    public bool HasToolCall => ReadParts().Any(p => p.Kind == TranscriptKinds.ToolCall && p.ToolName == "Bash");
    public bool ToolHasExited => _tool?.HasExited == true;

    public static async Task<ClaudeConditionalControlSession> StartAsync(bool permission)
    {
        if (Environment.GetEnvironmentVariable(ClSession.EnvFlag) != "1")
            throw new SkipTestException($"Set {ClSession.EnvFlag}=1 for native Claude qualification");
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsWindows())
            throw new SkipTestException("Qualification supports Linux UnixPty and Windows modern ConPTY");
        var executable = ResolveExecutable(OperatingSystem.IsWindows() ? "claude.exe" : "claude");
        ResolveExecutable(OperatingSystem.IsWindows() ? "node.exe" : "node");
        var version = await VersionAsync(executable);
        var live = new ClaudeConditionalControlSession();
        try
        {
            File.WriteAllText(Path.Combine(live._cwd, "control-probe.cjs"),
                "const fs=require('fs');fs.writeFileSync('started.txt',String(process.pid));"
                + "setTimeout(()=>{fs.writeFileSync('completed.txt','completed');},180000);");
            File.WriteAllText(Path.Combine(live._cwd, "permission-probe.cjs"),
                PermissionProbeSource);
            var settings = Path.Combine(live._cwd, "settings.json");
            File.WriteAllText(settings, JsonSerializer.Serialize(new
            {
                remoteControlAtStartup = false,
                disableAllHooks = true,
                permissions = new
                {
                    allow = permission ? Array.Empty<string>() : new[] { "Bash(node control-probe.cjs)" },
                    ask = new[] { "Bash(node permission-probe.cjs)" },
                },
            }));
            // Existing exact-key trust seam; never answer a startup dialog with Enter.
            var trust = ClaudeProjectTrust.Seed(live._cwd);
            live._seeded = trust.Outcome == ClaudeProjectTrustOutcome.Seeded;
            (trust.Outcome is ClaudeProjectTrustOutcome.Seeded or ClaudeProjectTrustOutcome.AlreadyTrusted)
                .ShouldBeTrue("an existing Claude configuration is required; no login/configuration is created");
            var env = ClSession.HeadedSafeEnv();
            // The isolated model must not inherit this delegate's server authority.
            env["ANTIPHON_TASK_TOKEN"] = "";
            env["ANTIPHON_TASK_ID"] = "";
            env["CLAUDECODE"] = "";
            await live.Runner.StartAsync(executable,
                ["--session-id", live._sessionId, "--model", "haiku", "--permission-mode", "default",
                    "--setting-sources", "", "--settings", settings, "--strict-mcp-config", "--tools", "Bash"],
                cwd: live._cwd, env: env, cols: 120, rows: 40);
            live.Runner.Backend!.Backend.ShouldBe(OperatingSystem.IsWindows()
                ? PtyBackend.ModernConPty : PtyBackend.UnixPty, "backend fallback cannot qualify a key");
            live.Log("launch", $"cli={version} backend={live.Runner.Backend.Backend} pid={live.Runner.Pid}");
            await live.RequireEmptyComposerAsync(working: false);
            return live;
        }
        catch
        {
            await live.DisposeAsync();
            throw;
        }
    }

    public async Task SubmitAsync(string prompt)
    {
        await RequireEmptyComposerAsync(working: false);
        await Runner.SendLineAsync(prompt);
        await WaitAsync(() => ReadParts().Any(p => p.Kind == TranscriptKinds.UserPrompt && p.Text == prompt),
            TimeSpan.FromSeconds(30), "the complete submitted prompt must appear in the native transcript");
    }

    public async Task RequireEmptyComposerAsync(bool working)
    {
        string? previous = null;
        await WaitAsync(() =>
        {
            var current = Runner.SnapshotScreen();
            var empty = !ClaudeBlockingPromptDetector.IsBlocked(current)
                && ClaudeScreen.IsWorking(current) == working
                && ClaudeScreen.TryReadComposer(current, out var content)
                && ClaudeScreen.ComposerContentIsEmpty(content);
            var settled = empty && previous is not null && ClaudeScreen.IsSettled(previous, current);
            previous = empty ? current : null;
            return settled;
        }, TimeSpan.FromSeconds(30), "two settled readable empty composers are required; no draft-clearing key is allowed");
    }

    public async Task RequireBoundaryAsync(int before)
    {
        try
        {
            await WaitAsync(() => ReadParts().Skip(before).Any(p =>
                    TranscriptKinds.IsInterruptPrompt(p.Kind, p.Text)
                    || p.Kind == TranscriptKinds.TurnEnd && p.StopReason == "cancelled"),
                TimeSpan.FromSeconds(30), "unsupported: the single control byte produced no native interrupted boundary");
        }
        finally
        {
            LogRows();
            Log("after-control", $"session_alive={!Runner.Exited.IsCompleted}");
        }
    }

    public async Task ConfirmNextPromptAsync()
    {
        var nonce = "C1517-NEXT-" + Guid.NewGuid().ToString("N");
        var prompt = $"Reply with exactly {nonce}. Use no tools.";
        await SubmitAsync(prompt); // Refuses a restored draft; never sends another Esc or Enter to clear one.
        await WaitAsync(() =>
        {
            var rows = ReadParts();
            var index = rows.FindLastIndex(p => p.Kind == TranscriptKinds.UserPrompt && p.Text == prompt);
            return index >= 0 && rows.Skip(index + 1).Any(p => p.Kind == TranscriptKinds.AssistantText
                    && p.Text?.Trim() == nonce)
                && rows.Skip(index + 1).Any(p => p.Kind == TranscriptKinds.TurnEnd && p.StopReason == "end_turn");
        }, TimeSpan.FromMinutes(2), "the next full UserPrompt, exact response and real turn end must all be present");
        Runner.Exited.IsCompleted.ShouldBeFalse();
        LogRows();
        Log("receipt", "full_user_prompt=true assistant_nonce=true turn_end=true");
    }

    public void CaptureToolProcess()
    {
        var pid = int.Parse(File.ReadAllText(StartedPath));
        _tool = Process.GetProcessById(pid);
        _toolStart = _tool.StartTime;
        _tool.HasExited.ShouldBeFalse("the marker must identify a live owned probe");
        Log("tool", $"pid={pid} start={_toolStart:O}");
    }

    public List<TranscriptPart> ReadParts() => ReadLines().SelectMany(TranscriptNormalizer.Normalize).ToList();

    private List<string> ReadLines()
    {
        if (_transcript is null)
        {
            var config = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
            var projects = Path.Combine(string.IsNullOrWhiteSpace(config)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude") : config,
                "projects");
            if (Directory.Exists(projects))
                _transcript = Directory.EnumerateDirectories(projects)
                    .Select(d => Path.Combine(d, _sessionId + ".jsonl")).FirstOrDefault(File.Exists);
        }
        if (_transcript is null) return [];
        using var stream = new FileStream(_transcript, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        List<string> lines = [];
        while (reader.ReadLine() is { } line)
        {
            try { using var parsed = JsonDocument.Parse(line); lines.Add(line); }
            catch (JsonException) { break; } // A partial final append is observed again on the next read.
        }
        return lines;
    }

    private void LogRows()
    {
        var lines = ReadLines();
        var nativeKinds = lines.Select(line =>
        {
            using var parsed = JsonDocument.Parse(line);
            return parsed.RootElement.TryGetProperty("type", out var type) ? type.GetString() : "unknown";
        });
        Log("native-order", string.Join(" > ", nativeKinds));
        Log("normalized-order", string.Join(" > ", ReadParts().Select(p => p.Kind)));
        // Only metadata, never screens, arbitrary tool arguments or provider credentials.
        Log("transcript", _transcript ?? "not-created");
    }

    public void Log(string phase, string facts) => Console.WriteLine($"CLAUDE-CONTROL session={_sessionId} phase={phase} {facts}");

    public async Task WaitAsync(Func<bool> condition, TimeSpan timeout, string failure)
    {
        var start = Stopwatch.GetTimestamp();
        while (Stopwatch.GetElapsedTime(start) < timeout)
        {
            Runner.Exited.IsCompleted.ShouldBeFalse("owned Claude exited before the expected observation");
            if (condition()) return;
            await Task.Delay(250);
        }
        var screen = Runner.SnapshotScreen();
        var readable = ClaudeScreen.TryReadComposer(screen, out var composer);
        Log("observation-timeout", $"modal={ClaudeBlockingPromptDetector.Detect(screen)?.Kind.ToString() ?? "none"} "
            + $"working={ClaudeScreen.IsWorking(screen)} composer_readable={readable} composer_length={composer.Length} "
            + $"probe_started={File.Exists(StartedPath)} probe_completed={File.Exists(CompletedPath)}");
        LogRows();
        throw new System.TimeoutException(failure);
    }

    private static string ResolveExecutable(string name) =>
        (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)
            .Where(d => !string.IsNullOrWhiteSpace(d)).Select(d => Path.Combine(d, name)).FirstOrDefault(File.Exists)
        ?? throw new InvalidOperationException($"Required native qualification executable is absent: {name}");

    private static async Task<string> VersionAsync(string executable)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(executable)
            {
                RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false,
            },
        };
        process.StartInfo.ArgumentList.Add("--version");
        process.Start();
        using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var stdout = process.StandardOutput.ReadToEndAsync(limit.Token);
        var stderr = process.StandardError.ReadToEndAsync(limit.Token);
        try
        {
            await Task.WhenAll(process.WaitForExitAsync(limit.Token), stdout, stderr);
            process.ExitCode.ShouldBe(0, "native Claude version probe must succeed");
            var match = Regex.Match(await stdout, @"\b\d+\.\d+\.\d+\b");
            match.Success.ShouldBeTrue("native Claude version must be observable");
            return match.Value;
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_tool is not null && !_tool.HasExited && _tool.StartTime == _toolStart)
            {
                _tool.Kill(entireProcessTree: true);
                await _tool.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            }
            (await Runner.KillAsync(TimeSpan.FromSeconds(10))).ShouldBeTrue("the fixture must join its owned Claude process");
        }
        finally
        {
            _tool?.Dispose();
            await Runner.DisposeAsync();
        }
        if (_seeded) ClaudeProjectTrust.Remove(_cwd).ShouldBeTrue("remove only the fixture's exact trust key");
        Directory.Delete(_cwd, recursive: true);
    }
}
