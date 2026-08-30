using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Shouldly;
using TUnit.Core;
using TUnit.Core.Exceptions;

namespace Antiphon.Agents.Pty.Tests;

/// <summary>
/// CARD-0247 S0: pins Claude Code's <c>PreToolUse</c> <c>additionalContext</c> contract against the
/// real CLI (currently 2.1.251). The nudge design rests on one measured fact: a hook that returns
/// <c>permissionDecision: allow</c> plus <c>additionalContext</c> lets the tool run AND puts the
/// text in the model's context. A CLI upgrade that drops either half must fail here, not in
/// production.
///
/// <para>Print-mode (<c>claude -p --settings</c>), not the TUI: that is the shape of the Plan-pass
/// probe. Throwaway settings + Node hook; the repo's <c>.claude/settings.json</c> is never touched.
/// </para>
///
/// <para>Headed, opt-in (<c>ANTIPHON_HEADED_TESTS=1</c>) and <c>[Explicit]</c>: spends real Haiku
/// turns. Node (v24.6.0 on this machine) must be on PATH — the hook is a Node process, matching
/// the plan's latency choice.</para>
///
/// <para>Also measured against 2.1.251 (2026-08-30): main-context JSONL already contains this
/// <c>tool_use_id</c> when <c>PreToolUse</c> fires; subagent calls carry <c>agent_id</c> and
/// <c>agent_type</c>; <c>additionalContext</c> is <b>turn-scoped</b> (an unconfounded
/// <c>--resume</c> with the hook removed answers NONE when the codeword never appeared in
/// assistant text).</para>
/// </summary>
[NotInParallel("Headed")]
[Category("Headed")]
[Category("HeadedCanary")]
[Category("Card0247")]
[Explicit]
[ParallelLimiter<ProcessSpawnLimit>]
public class ClaudeHookAdditionalContextCanaryTests
{
    private static readonly string[] RequiredStdinFields =
    [
        "session_id",
        "transcript_path",
        "cwd",
        "permission_mode",
        "prompt_id",
        "hook_event_name",
        "tool_name",
        "tool_input",
        "tool_use_id",
    ];

    /// <summary>
    /// The load-bearing pin: the tool actually runs, the model sees the codeword, the stdin JSON
    /// carries the §1.1 field list, and <c>agent_id</c> is absent on a main-context call.
    /// </summary>
    [Test]
    public async Task PreToolUse_additionalContext_reaches_the_model_without_blocking()
    {
        ClSession.SkipIfNotEligible();
        SkipIfNodeMissing();

        using var sandbox = new HookCanarySandbox();
        var env = HeadedHookEnv(sandbox);

        var turn1Prompt =
            $"Read the file named probe.txt in the working directory using the Read tool "
            + $"(do not guess its contents). After you have read it, reply with the file's contents "
            + $"on one line, then on the next line any CODEWORD you were told about. "
            + $"If you were not told a codeword, write NONE.";

        var turn1 = await RunPrintAsync(
            sandbox.Dir,
            env,
            TimeSpan.FromMinutes(3),
            "--dangerously-skip-permissions",
            "--strict-mcp-config",
            "--settings", sandbox.SettingsPath,
            "--session-id", sandbox.SessionId,
            "--model", "haiku",
            "--allowedTools", "Read",
            "--max-turns", "8",
            "--output-format", "json",
            "-p",
            turn1Prompt);

        Console.WriteLine($"TURN1 EXIT: {turn1.ExitCode}");
        Console.WriteLine($"TURN1 STDERR:\n{TrimForLog(turn1.Stderr)}");
        Console.WriteLine($"TURN1 STDOUT:\n{TrimForLog(turn1.Stdout)}");

        turn1.ExitCode.ShouldBe(0, "claude -p must succeed. stderr:\n" + turn1.Stderr);
        var turn1Text = ReadResultText(turn1.Stdout);
        Console.WriteLine($"TURN1 RESULT:\n{turn1Text}");

        var hookRecords = ReadHookLog(sandbox.LogPath);
        Console.WriteLine($"HOOK LOG ({hookRecords.Count} record(s)):\n{File.ReadAllText(sandbox.LogPath)}");
        hookRecords.Count.ShouldBeGreaterThan(0,
            "the Read must have fired PreToolUse (empty hook log = call blocked or hook not invoked)");

        var stdin = hookRecords[0].Stdin;
        foreach (var field in RequiredStdinFields)
            AssertRequiredField(stdin, field);

        stdin.GetProperty("hook_event_name").GetString().ShouldBe("PreToolUse");
        stdin.GetProperty("tool_name").GetString().ShouldBe("Read",
            "the canary prompt asks for the Read tool; a different tool is a prompt miss, not a contract change");
        stdin.GetProperty("cwd").GetString()!
            .Replace('/', '\\')
            .Equals(sandbox.Dir.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)
            .ShouldBeTrue($"cwd should be the sandbox. got: {stdin.GetProperty("cwd").GetString()}");
        stdin.GetProperty("tool_input").GetRawText()
            .ShouldContain("probe.txt", Case.Insensitive, "tool_input must name the probe file");
        HasNonEmpty(stdin, "agent_id")
            .ShouldBeFalse("main-context PreToolUse must not carry agent_id (plan §1.1: only inside a subagent)");

        // Env inheritance is what S2's orchestrator discriminator will read — pin it here cheaply.
        hookRecords[0].InheritedTaskId.ShouldBe(sandbox.TaskIdToken,
            "the hook process must inherit the CLI environment (plan §1.1)");

        Console.WriteLine(
            $"TOOL_USE_ALREADY_IN_TRANSCRIPT: {hookRecords[0].ToolUseAlreadyInTranscript?.ToString() ?? "null"} "
            + "(plan §8: whether JSONL contains this tool_use_id before PreToolUse fires)");
        Console.WriteLine($"CLAUDE_PROJECT_DIR: {hookRecords[0].ClaudeProjectDir ?? "<unset>"}");

        turn1Text.Contains(sandbox.ProbeBody, StringComparison.Ordinal).ShouldBeTrue(
            "the model must echo the probe file — proof the Read was not blocked. result:\n" + turn1Text);
        turn1Text.Contains(sandbox.Codeword, StringComparison.Ordinal).ShouldBeTrue(
            "additionalContext must reach the model in the same turn (the fact the nudge rests on). result:\n"
            + turn1Text);
    }

    /// <summary>
    /// Plan §8: does <c>additionalContext</c> survive a later user turn? Turn 1 must NOT ask the
    /// model to repeat the codeword — otherwise the assistant reply itself is a second copy and a
    /// <c>--resume</c> "what was the codeword?" is confounded (measured: one run said NONE, the
    /// next repeated the codeword from its own previous answer).
    /// </summary>
    [Test]
    public async Task additionalContext_survival_on_resume_is_not_confounded_by_assistant_text()
    {
        ClSession.SkipIfNotEligible();
        SkipIfNodeMissing();

        using var sandbox = new HookCanarySandbox();
        var env = HeadedHookEnv(sandbox);

        var turn1Prompt =
            "Read the file named probe.txt in the working directory using the Read tool "
            + "(do not guess its contents). Reply with ONLY the file's contents on one line. "
            + "Do not mention any other words, labels, or codewords you were told.";

        var turn1 = await RunPrintAsync(
            sandbox.Dir,
            env,
            TimeSpan.FromMinutes(3),
            "--dangerously-skip-permissions",
            "--strict-mcp-config",
            "--settings", sandbox.SettingsPath,
            "--session-id", sandbox.SessionId,
            "--model", "haiku",
            "--allowedTools", "Read",
            "--max-turns", "8",
            "--output-format", "json",
            "-p",
            turn1Prompt);

        Console.WriteLine($"SURVIVAL TURN1 EXIT: {turn1.ExitCode}");
        Console.WriteLine($"SURVIVAL TURN1 STDERR:\n{TrimForLog(turn1.Stderr)}");
        Console.WriteLine($"SURVIVAL TURN1 STDOUT:\n{TrimForLog(turn1.Stdout)}");
        turn1.ExitCode.ShouldBe(0, "survival turn 1 must succeed. stderr:\n" + turn1.Stderr);

        var turn1Text = ReadResultText(turn1.Stdout);
        Console.WriteLine($"SURVIVAL TURN1 RESULT:\n{turn1Text}");
        File.Exists(sandbox.LogPath).ShouldBeTrue("the Read must have fired the hook");
        var hookCountAfterTurn1 = ReadHookLog(sandbox.LogPath).Count;
        hookCountAfterTurn1.ShouldBeGreaterThan(0);
        turn1Text.Contains(sandbox.ProbeBody, StringComparison.Ordinal).ShouldBeTrue(
            "turn 1 must show the Read ran. result:\n" + turn1Text);
        if (turn1Text.Contains(sandbox.Codeword, StringComparison.Ordinal))
        {
            throw new SkipTestException(
                "model leaked the codeword into the assistant reply; survival would be confounded. result:\n"
                + turn1Text);
        }

        var turn2 = await RunPrintAsync(
            sandbox.Dir,
            env,
            TimeSpan.FromMinutes(2),
            "--dangerously-skip-permissions",
            "--strict-mcp-config",
            "--resume", sandbox.SessionId,
            "--model", "haiku",
            "--max-turns", "1",
            "--output-format", "json",
            "-p",
            "What CODEWORD were you told about? Reply with the exact codeword only, or NONE if you "
            + "were not told one. Do not use any tools.");

        Console.WriteLine($"SURVIVAL TURN2 EXIT: {turn2.ExitCode}");
        Console.WriteLine($"SURVIVAL TURN2 STDERR:\n{TrimForLog(turn2.Stderr)}");
        Console.WriteLine($"SURVIVAL TURN2 STDOUT:\n{TrimForLog(turn2.Stdout)}");
        turn2.ExitCode.ShouldBe(0, "survival resume must succeed. stderr:\n" + turn2.Stderr);

        var turn2Text = ReadResultText(turn2.Stdout);
        Console.WriteLine($"SURVIVAL TURN2 RESULT:\n{turn2Text}");
        var hookCountAfterTurn2 = File.Exists(sandbox.LogPath) ? ReadHookLog(sandbox.LogPath).Count : 0;
        hookCountAfterTurn2.ShouldBe(hookCountAfterTurn1,
            "turn 2 omitted --settings; the hook must not re-inject");

        var survived = turn2Text.Contains(sandbox.Codeword, StringComparison.Ordinal);
        Console.WriteLine(
            survived
                ? "ADDITIONALCONTEXT_SURVIVES_NEXT_TURN: yes"
                : "ADDITIONALCONTEXT_SURVIVES_NEXT_TURN: no (turn-scoped only)");
        turn2Text.ShouldNotBeNullOrWhiteSpace();
        // Measured 2026-08-30 / CLI 2.1.251, unconfounded (turn 1 did not repeat the codeword).
        survived.ShouldBeFalse(
            "additionalContext does not survive into the next user turn. result:\n" + turn2Text);
    }

    /// <summary>
    /// Bonus: plan §1.1 claims <c>agent_id</c> is present inside a subagent. Skip (do not fail S0)
    /// if this prompt does not actually spawn one — that is a model-choice flake, not a contract miss.
    /// </summary>
    [Test]
    public async Task Subagent_PreToolUse_carries_agent_id()
    {
        ClSession.SkipIfNotEligible();
        SkipIfNodeMissing();

        using var sandbox = new HookCanarySandbox();
        var env = HeadedHookEnv(sandbox);

        var prompt =
            "Do not read probe.txt yourself. Use the Agent tool to spawn a general-purpose subagent "
            + "whose only job is to Read probe.txt with the Read tool and return its contents. "
            + "Then reply with those contents.";

        var run = await RunPrintAsync(
            sandbox.Dir,
            env,
            TimeSpan.FromMinutes(4),
            "--dangerously-skip-permissions",
            "--strict-mcp-config",
            "--settings", sandbox.SettingsPath,
            "--session-id", sandbox.SessionId,
            "--model", "haiku",
            "--allowedTools", "Read,Agent",
            "--max-turns", "12",
            "--output-format", "json",
            "-p",
            prompt);

        Console.WriteLine($"SUBAGENT EXIT: {run.ExitCode}");
        Console.WriteLine($"SUBAGENT STDERR:\n{TrimForLog(run.Stderr)}");
        Console.WriteLine($"SUBAGENT STDOUT:\n{TrimForLog(run.Stdout)}");
        Console.WriteLine($"HOOK LOG:\n{(File.Exists(sandbox.LogPath) ? File.ReadAllText(sandbox.LogPath) : "<missing>")}");

        if (run.ExitCode != 0)
            throw new SkipTestException("subagent prompt did not complete; not an S0 failure. stderr:\n" + run.Stderr);

        var records = File.Exists(sandbox.LogPath) ? ReadHookLog(sandbox.LogPath) : [];
        var withAgent = records.Where(r => HasNonEmpty(r.Stdin, "agent_id")).ToList();
        if (withAgent.Count == 0)
        {
            throw new SkipTestException(
                "the model never invoked a subagent (no PreToolUse stdin carried agent_id). "
                + "Main-context absence is pinned by the other test; presence is bonus.");
        }

        var id = withAgent[0].Stdin.GetProperty("agent_id").GetString();
        Console.WriteLine($"AGENT_ID PRESENT: {id}");
        id.ShouldNotBeNullOrWhiteSpace();
        withAgent[0].Stdin.TryGetProperty("agent_type", out var agentType)
            .ShouldBeTrue("plan §1.1: subagent PreToolUse also carries agent_type");
        Console.WriteLine($"AGENT_TYPE: {agentType}");
    }

    private static Dictionary<string, string> HeadedHookEnv(HookCanarySandbox sandbox)
    {
        var env = ClSession.HeadedSafeEnv();
        env["ANTIPHON_HOOK_LOG"] = sandbox.LogPath;
        env["ANTIPHON_HOOK_CODEWORD"] = sandbox.Codeword;
        env["ANTIPHON_TASK_ID"] = sandbox.TaskIdToken;
        return env;
    }

    private static void SkipIfNodeMissing()
    {
        if (HookCanarySandbox.ResolveNode() is null)
            throw new SkipTestException("node.exe not on PATH; CARD-0247 S0 requires Node for the PreToolUse hook");
    }

    private static void AssertRequiredField(JsonElement stdin, string name)
    {
        stdin.TryGetProperty(name, out var value)
            .ShouldBeTrue($"hook stdin missing '{name}'. keys: {KeysOf(stdin)}");
        if (name == "tool_input")
        {
            value.ValueKind.ShouldBe(JsonValueKind.Object, $"hook stdin '{name}' should be an object");
            return;
        }

        value.ValueKind.ShouldBe(JsonValueKind.String, $"hook stdin '{name}' should be a string");
        value.GetString().ShouldNotBeNullOrWhiteSpace($"hook stdin '{name}' is empty");
    }

    private static bool HasNonEmpty(JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out var value))
            return false;
        return value.ValueKind switch
        {
            JsonValueKind.Null or JsonValueKind.Undefined => false,
            JsonValueKind.String => !string.IsNullOrWhiteSpace(value.GetString()),
            _ => true,
        };
    }

    private static string KeysOf(JsonElement obj)
        => obj.ValueKind == JsonValueKind.Object
            ? string.Join(",", obj.EnumerateObject().Select(p => p.Name))
            : obj.ValueKind.ToString();

    private static List<HookLogRecord> ReadHookLog(string path)
    {
        var records = new List<HookLogRecord>();
        if (!File.Exists(path))
            return records;
        foreach (var line in File.ReadAllLines(path))
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement.Clone();
            var stdin = root.GetProperty("stdin").Clone();
            bool? already = null;
            if (root.TryGetProperty("toolUseAlreadyInTranscript", out var flag)
                && flag.ValueKind is JsonValueKind.True or JsonValueKind.False)
                already = flag.GetBoolean();
            string? taskId = null;
            string? projectDir = null;
            if (root.TryGetProperty("inherited", out var inherited) && inherited.ValueKind == JsonValueKind.Object)
            {
                taskId = inherited.TryGetProperty("ANTIPHON_TASK_ID", out var t) ? t.GetString() : null;
                projectDir = inherited.TryGetProperty("CLAUDE_PROJECT_DIR", out var d) ? d.GetString() : null;
            }

            records.Add(new HookLogRecord(stdin, already, taskId, projectDir));
        }

        return records;
    }

    private static string ReadResultText(string stdout)
    {
        using var doc = ParseJsonOutput(stdout);
        if (doc.RootElement.TryGetProperty("result", out var result) && result.GetString() is { } text)
            return text;
        return stdout;
    }

    private static JsonDocument ParseJsonOutput(string stdout)
    {
        var trimmed = stdout.Trim();
        try
        {
            return JsonDocument.Parse(trimmed);
        }
        catch (JsonException)
        {
            // PTY/wrapper noise: take the outermost JSON object.
            var start = trimmed.IndexOf('{');
            var end = trimmed.LastIndexOf('}');
            if (start >= 0 && end > start)
                return JsonDocument.Parse(trimmed[start..(end + 1)]);
            throw new InvalidOperationException(
                "claude -p --output-format json did not emit JSON. stdout:\n" + stdout);
        }
    }

    private static async Task<PrintRun> RunPrintAsync(
        string cwd,
        IDictionary<string, string> env,
        TimeSpan timeout,
        params string[] extraArgs)
    {
        var (app, args) = ClSession.BuildLaunch(ClSession.ResolveOrThrow(), extraArgs);
        var psi = new ProcessStartInfo
        {
            FileName = app,
            WorkingDirectory = cwd,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        foreach (var kv in env)
        {
            if (string.IsNullOrEmpty(kv.Value))
                psi.Environment.Remove(kv.Key);
            else
                psi.Environment[kv.Key] = kv.Value;
        }

        using var process = new Process { StartInfo = psi };
        if (!process.Start())
            throw new InvalidOperationException("failed to start claude");

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        using var cts = new CancellationTokenSource(timeout);
        try
        {
            await process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { /* already gone */ }
            var so = "";
            var se = "";
            try { so = await stdoutTask; } catch { /* killed */ }
            try { se = await stderrTask; } catch { /* killed */ }
            throw new System.TimeoutException(
                $"claude -p timed out after {timeout}. stdout:\n{so}\nstderr:\n{se}");
        }

        return new PrintRun(process.ExitCode, await stdoutTask, await stderrTask);
    }

    private static string TrimForLog(string s)
        => s.Length <= 4000 ? s : s[..4000] + "…";

    private sealed record PrintRun(int ExitCode, string Stdout, string Stderr);

    private sealed record HookLogRecord(
        JsonElement Stdin,
        bool? ToolUseAlreadyInTranscript,
        string? InheritedTaskId,
        string? ClaudeProjectDir);

    /// <summary>
    /// Throwaway cwd + settings + Node hook. Disposed by deleting the directory; never writes into
    /// the repo's <c>.claude/settings.json</c>.
    /// </summary>
    private sealed class HookCanarySandbox : IDisposable
    {
        public string Dir { get; }
        public string ProbePath { get; }
        public string ProbeBody { get; }
        public string SettingsPath { get; }
        public string ScriptPath { get; }
        public string LogPath { get; }
        public string Codeword { get; }
        public string SessionId { get; }
        public string TaskIdToken { get; }
        public string NodePath { get; }

        public HookCanarySandbox()
        {
            NodePath = ResolveNode()
                ?? throw new InvalidOperationException("node.exe not on PATH");
            Dir = Directory.CreateTempSubdirectory("antiphon-hook-canary-").FullName;
            ProbePath = Path.Combine(Dir, "probe.txt");
            ProbeBody = "PROBE-BODY " + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
            File.WriteAllText(ProbePath, ProbeBody + Environment.NewLine);
            ScriptPath = Path.Combine(Dir, "hook.cjs");
            LogPath = Path.Combine(Dir, "hook-stdin.jsonl");
            SettingsPath = Path.Combine(Dir, "settings.json");
            Codeword = "ZEBRA-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
            SessionId = Guid.NewGuid().ToString("D");
            TaskIdToken = "canary-s0-" + Guid.NewGuid().ToString("N")[..8];

            File.WriteAllText(ScriptPath, HookScript);
            File.WriteAllText(SettingsPath, BuildSettingsJson(NodePath, ScriptPath));
            File.WriteAllText(
                Path.Combine(Dir, "CLAUDE.md"),
                "Throwaway canary working directory. Follow the user prompt exactly.\n");
        }

        public static string? ResolveNode()
        {
            var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (var dir in pathEnv.Split(Path.PathSeparator))
            {
                if (string.IsNullOrWhiteSpace(dir))
                    continue;
                var candidate = Path.Combine(dir, "node.exe");
                if (File.Exists(candidate))
                    return candidate;
            }

            return null;
        }

        public void Dispose()
        {
            try
            {
                // User-level SessionStart hooks (memsearch) drop a hidden .memsearch dir into cwd
                // that blocks a plain Directory.Delete.
                foreach (var path in Directory.EnumerateFileSystemEntries(Dir, "*", SearchOption.AllDirectories))
                {
                    try { File.SetAttributes(path, FileAttributes.Normal); } catch { /* dir or gone */ }
                }
                Directory.Delete(Dir, recursive: true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        private static string BuildSettingsJson(string nodePath, string scriptPath)
        {
            var command =
                $"\"{nodePath.Replace('\\', '/')}\" \"{scriptPath.Replace('\\', '/')}\"";
            var settings = new JsonObject
            {
                ["hooks"] = new JsonObject
                {
                    ["PreToolUse"] = new JsonArray
                    {
                        new JsonObject
                        {
                            ["matcher"] = "Read|Bash",
                            ["hooks"] = new JsonArray
                            {
                                new JsonObject
                                {
                                    ["type"] = "command",
                                    ["command"] = command,
                                    ["timeout"] = 15,
                                },
                            },
                        },
                    },
                },
            };
            return settings.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        }

        private const string HookScript =
            """
            'use strict';
            const fs = require('fs');
            const chunks = [];
            process.stdin.setEncoding('utf8');
            process.stdin.on('data', c => chunks.push(c));
            process.stdin.on('end', () => {
              const raw = chunks.join('');
              let parsed = null;
              try { parsed = JSON.parse(raw); } catch {}
              let toolUseAlreadyInTranscript = null;
              if (parsed && parsed.transcript_path && parsed.tool_use_id) {
                try {
                  if (fs.existsSync(parsed.transcript_path)) {
                    const tail = fs.readFileSync(parsed.transcript_path, 'utf8');
                    toolUseAlreadyInTranscript = tail.includes(parsed.tool_use_id);
                  } else {
                    toolUseAlreadyInTranscript = false;
                  }
                } catch { toolUseAlreadyInTranscript = null; }
              }
              const record = {
                receivedAt: new Date().toISOString(),
                toolUseAlreadyInTranscript,
                inherited: {
                  ANTIPHON_TASK_ID: process.env.ANTIPHON_TASK_ID || null,
                  CLAUDE_PROJECT_DIR: process.env.CLAUDE_PROJECT_DIR || null,
                },
                stdin: parsed ?? { _unparsed: raw },
              };
              const logPath = process.env.ANTIPHON_HOOK_LOG;
              if (logPath) fs.appendFileSync(logPath, JSON.stringify(record) + '\n');
              const codeword = process.env.ANTIPHON_HOOK_CODEWORD || 'MISSING';
              process.stdout.write(JSON.stringify({
                hookSpecificOutput: {
                  hookEventName: 'PreToolUse',
                  permissionDecision: 'allow',
                  additionalContext: 'CODEWORD ' + codeword,
                },
              }));
              process.exit(0);
            });
            """;
    }
}
