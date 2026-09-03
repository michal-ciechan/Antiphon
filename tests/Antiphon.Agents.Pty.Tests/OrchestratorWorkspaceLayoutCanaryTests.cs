using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Shouldly;
using TUnit.Core;
using TUnit.Core.Exceptions;

namespace Antiphon.Agents.Pty.Tests;

/// <summary>
/// CARD-0251 S0: pins the four load-bearing CLI facts the sibling-folder orchestrator
/// workspace rests on. A CLI upgrade that starts walking above <c>.git</c> (Codex/Grok) or
/// stops walking ancestors (Claude) or silently drops a sibling <c>@</c> import must fail
/// here, not in a live orchestrator.
///
/// <para>The two Claude probes spend a Haiku turn and are headed/<c>[Explicit]</c>. Codex
/// <c>debug prompt-input</c> and Grok <c>inspect --json</c> are offline oracles and run
/// whenever the CLI is on the machine.</para>
/// </summary>
[Category("Card0251")]
[ParallelLimiter<ProcessSpawnLimit>]
public class OrchestratorWorkspaceLayoutCanaryTests
{
    private const string NestedParentClaude = "ORCHWALK-4412";
    private const string NestedParentAgents = "ORCHAGENTS-5150";
    private const string NestedRepoAgents = "REPOWALK-9931";
    private const string SiblingOrchClaude = "SIBORCH-2201";
    private const string SiblingRepoAgents = "SIBREPO-7788";

    private const string CodewordPrompt =
        "List every codeword of the form WORD-NNNN that appears anywhere in your instructions or context. "
        + "Reply with the codewords only, comma-separated. If none, reply NONE.";

    /// <summary>
    /// Nested <c>&lt;orch&gt;\source\repo</c>: a session started in the checkout also sees the
    /// parent's <c>CLAUDE.md</c>. That is why CARD-0251 ships sibling-folder tooling, not
    /// parent-folder tooling.
    /// </summary>
    [Test]
    [Explicit]
    [Category("Headed")]
    [Category("HeadedCanary")]
    [NotInParallel("Headed")]
    public async Task Claude_loads_parent_CLAUDE_md_from_a_nested_checkout()
    {
        ClSession.SkipIfNotEligible();
        using var layout = NestedLayoutSandbox.Create();

        var run = await RunClaudePrintAsync(layout.Repo, TimeSpan.FromMinutes(3), CodewordPrompt);
        Console.WriteLine($"WALK EXIT: {run.ExitCode}");
        Console.WriteLine($"WALK STDERR:\n{TrimForLog(run.Stderr)}");
        Console.WriteLine($"WALK STDOUT:\n{TrimForLog(run.Stdout)}");
        run.ExitCode.ShouldBe(0, "claude -p must succeed. stderr:\n" + run.Stderr);

        var text = ReadClaudeResultText(run.Stdout);
        Console.WriteLine($"WALK RESULT:\n{text}");
        text.ShouldContain(NestedParentClaude,
            customMessage: "Claude walks ancestors: the parent's CLAUDE.md must be visible inside the nested checkout. result:\n" + text);
        text.ShouldContain(NestedRepoAgents,
            customMessage: "the checkout's own AGENTS.md must still load. result:\n" + text);
    }

    /// <summary>
    /// Sibling <c>@../repo/AGENTS.md</c> is dropped until
    /// <c>projects["&lt;orch, forward slashes&gt;"].hasClaudeMdExternalIncludesApproved</c>
    /// is true. A backslash project key is a miss — the same trap CARD-0251's setup must not
    /// write.
    /// </summary>
    [Test]
    [Explicit]
    [Category("Headed")]
    [Category("HeadedCanary")]
    [NotInParallel("Headed")]
    public async Task Claude_sibling_import_is_dropped_without_the_forward_slash_approval()
    {
        ClSession.SkipIfNotEligible();
        using var layout = SiblingLayoutSandbox.Create();
        using var claudeJson = new ClaudeJsonMutation();

        var dropped = await ProbeSiblingAsync(layout, claudeJson, phase: "no-flag", mutate: null);
        dropped.ShouldContain(SiblingOrchClaude, customMessage: "the orchestrator CLAUDE.md itself must load. result:\n" + dropped);
        dropped.ShouldNotContain(SiblingRepoAgents,
            customMessage: "without the approval flag the sibling @ import is silently dropped. result:\n" + dropped);

        var backslash = await ProbeSiblingAsync(layout, claudeJson, phase: "backslash-key",
            mutate: () => claudeJson.SetExternalIncludes(layout.Orch, approved: true, forwardSlashKey: false));
        backslash.ShouldContain(SiblingOrchClaude, customMessage: "backslash-key phase must still load the orch file. result:\n" + backslash);
        backslash.ShouldNotContain(SiblingRepoAgents,
            customMessage: "the project key is the forward-slash form; a backslash key must not approve the import. result:\n" + backslash);

        var approved = await ProbeSiblingAsync(layout, claudeJson, phase: "forward-slash-key",
            mutate: () => claudeJson.SetExternalIncludes(layout.Orch, approved: true, forwardSlashKey: true));
        approved.ShouldContain(SiblingOrchClaude, customMessage: "approved phase must load the orch file. result:\n" + approved);
        approved.ShouldContain(SiblingRepoAgents,
            customMessage: "forward-slash hasClaudeMdExternalIncludesApproved must resolve the sibling @ import. result:\n" + approved);
    }

    /// <summary>
    /// Codex <c>debug prompt-input</c> (offline): a nested checkout is bounded at <c>.git</c>,
    /// so the parent's <c>AGENTS.md</c> is not in the rendered prompt.
    /// </summary>
    [Test]
    public async Task Codex_prompt_input_is_bounded_at_the_nested_checkout_git_root()
    {
        var exe = ResolveCodexExe();
        if (exe is null)
            throw new SkipTestException("codex.exe / npm shim not found; cannot run the offline Codex oracle");

        using var layout = NestedLayoutSandbox.Create();
        var (exit, stdout, stderr) = await RunProcessAsync(
            exe, layout.Repo, TimeSpan.FromSeconds(45), "debug", "prompt-input");
        Console.WriteLine($"CODEX EXIT: {exit}");
        Console.WriteLine($"CODEX STDERR:\n{TrimForLog(stderr)}");
        exit.ShouldBe(0, "codex debug prompt-input must succeed. stderr:\n" + stderr);

        stdout.ShouldContain(NestedRepoAgents,
            customMessage: "Codex must load the checkout AGENTS.md. prompt:\n" + TrimForLog(stdout));
        stdout.ShouldNotContain(NestedParentAgents,
            customMessage: "Codex must not walk above the checkout git root. prompt:\n" + TrimForLog(stdout));
        stdout.ShouldNotContain(NestedParentClaude,
            customMessage: "Codex ignores CLAUDE.md and must not see the parent's either. prompt:\n" + TrimForLog(stdout));
    }

    /// <summary>
    /// Grok <c>inspect --json</c> (offline): <c>projectRoot</c> is the nested checkout, and
    /// project-scope instruction files are only the checkout's — not the parent's.
    /// </summary>
    [Test]
    public async Task Grok_inspect_is_bounded_at_the_nested_checkout_git_root()
    {
        if (!File.Exists(GkSession.GrokExePath))
            throw new SkipTestException($"grok.exe not found at {GkSession.GrokExePath}");

        using var layout = NestedLayoutSandbox.Create();
        var (exit, stdout, stderr) = await RunProcessAsync(
            GkSession.GrokExePath, layout.Repo, TimeSpan.FromSeconds(45), "inspect", "--json");
        Console.WriteLine($"GROK EXIT: {exit}");
        Console.WriteLine($"GROK STDERR:\n{TrimForLog(stderr)}");
        exit.ShouldBe(0, "grok inspect --json must succeed. stderr:\n" + stderr);

        using var doc = ParseJsonObject(stdout);
        var root = doc.RootElement;
        root.TryGetProperty("projectRoot", out var projectRoot).ShouldBeTrue("inspect JSON carries projectRoot");
        projectRoot.ValueKind.ShouldNotBe(JsonValueKind.Null,
            "a nested git checkout must report itself as projectRoot, not null (that is the non-repo parent shape)");
        PathsEqual(projectRoot.GetString()!, layout.Repo)
            .ShouldBeTrue($"projectRoot should be the checkout. got: {projectRoot.GetString()}");

        var projectPaths = new List<string>();
        foreach (var instr in root.GetProperty("projectInstructions").EnumerateArray())
        {
            if (!string.Equals(instr.GetProperty("scope").GetString(), "project", StringComparison.OrdinalIgnoreCase))
                continue;
            var path = instr.GetProperty("path").GetString();
            if (!string.IsNullOrEmpty(path))
                projectPaths.Add(path);
        }

        projectPaths.ShouldNotBeEmpty("the checkout's AGENTS.md / CLAUDE.md must appear as project instructions");
        foreach (var path in projectPaths)
        {
            IsUnder(path, layout.Repo).ShouldBeTrue(
                $"project instruction '{path}' must sit inside the checkout, not the parent orch folder");
        }

        var joined = string.Join('\n', projectPaths);
        IsUnder(Path.Combine(layout.Orch, "AGENTS.md"), layout.Repo).ShouldBeFalse();
        joined.Contains(Path.Combine(layout.Orch, "AGENTS.md"), StringComparison.OrdinalIgnoreCase)
            .ShouldBeFalse("parent AGENTS.md must not be a project instruction inside the nested checkout");
        joined.Contains(Path.Combine(layout.Orch, "CLAUDE.md"), StringComparison.OrdinalIgnoreCase)
            .ShouldBeFalse("parent CLAUDE.md must not be a project instruction inside the nested checkout");
    }

    private static async Task<string> ProbeSiblingAsync(
        SiblingLayoutSandbox layout,
        ClaudeJsonMutation claudeJson,
        string phase,
        Action? mutate)
    {
        claudeJson.RestoreOriginal();
        mutate?.Invoke();
        var run = await RunClaudePrintAsync(layout.Orch, TimeSpan.FromMinutes(3), CodewordPrompt);
        Console.WriteLine($"IMPORT {phase} EXIT: {run.ExitCode}");
        Console.WriteLine($"IMPORT {phase} STDERR:\n{TrimForLog(run.Stderr)}");
        Console.WriteLine($"IMPORT {phase} STDOUT:\n{TrimForLog(run.Stdout)}");
        run.ExitCode.ShouldBe(0, $"claude -p ({phase}) must succeed. stderr:\n" + run.Stderr);
        var text = ReadClaudeResultText(run.Stdout);
        Console.WriteLine($"IMPORT {phase} RESULT:\n{text}");
        return text;
    }

    private static async Task<PrintRun> RunClaudePrintAsync(string cwd, TimeSpan timeout, string prompt)
    {
        var extra = new[]
        {
            "--dangerously-skip-permissions",
            "--strict-mcp-config",
            "--model", "haiku",
            "--max-turns", "1",
            "--output-format", "json",
            "-p",
            prompt,
        };
        var (app, args) = ClSession.BuildLaunch(ClSession.ResolveOrThrow(), extra);
        return await RunCapturedAsync(app, cwd, timeout, args, ClSession.HeadedSafeEnv());
    }

    private static async Task<(int Exit, string Stdout, string Stderr)> RunProcessAsync(
        string fileName, string cwd, TimeSpan timeout, params string[] args)
    {
        var run = await RunCapturedAsync(fileName, cwd, timeout, args, env: null);
        return (run.ExitCode, run.Stdout, run.Stderr);
    }

    private static async Task<PrintRun> RunCapturedAsync(
        string fileName,
        string cwd,
        TimeSpan timeout,
        IReadOnlyList<string> args,
        IDictionary<string, string>? env)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
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
        if (env is not null)
        {
            foreach (var kv in env)
            {
                if (string.IsNullOrEmpty(kv.Value))
                    psi.Environment.Remove(kv.Key);
                else
                    psi.Environment[kv.Key] = kv.Value;
            }
        }

        using var process = new Process { StartInfo = psi };
        if (!process.Start())
            throw new InvalidOperationException("failed to start " + fileName);

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
                $"{fileName} timed out after {timeout}. stdout:\n{so}\nstderr:\n{se}");
        }

        return new PrintRun(process.ExitCode, await stdoutTask, await stderrTask);
    }

    private static string? ResolveCodexExe()
    {
        var explicitPath = Environment.GetEnvironmentVariable("ANTIPHON_CODEX_EXE");
        if (!string.IsNullOrWhiteSpace(explicitPath) && File.Exists(explicitPath))
            return explicitPath;

        var npmRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "npm");
        if (Directory.Exists(npmRoot))
        {
            var vendorRoot = Path.Combine(npmRoot, "node_modules", "@openai");
            if (Directory.Exists(vendorRoot))
            {
                foreach (var platform in Directory.EnumerateDirectories(vendorRoot, "codex-win32-*"))
                {
                    var vendor = Path.Combine(platform, "vendor");
                    if (!Directory.Exists(vendor)) continue;
                    foreach (var triple in Directory.EnumerateDirectories(vendor))
                    {
                        var exe = Path.Combine(triple, "bin", "codex.exe");
                        if (File.Exists(exe)) return exe;
                    }
                }
            }

            var nested = Path.Combine(npmRoot, "node_modules", "@openai", "codex", "node_modules", "@openai");
            if (Directory.Exists(nested))
            {
                foreach (var platform in Directory.EnumerateDirectories(nested, "codex-win32-*"))
                {
                    var vendor = Path.Combine(platform, "vendor");
                    if (!Directory.Exists(vendor)) continue;
                    foreach (var triple in Directory.EnumerateDirectories(vendor))
                    {
                        var exe = Path.Combine(triple, "bin", "codex.exe");
                        if (File.Exists(exe)) return exe;
                    }
                }
            }
        }

        return CxSession.ResolveCli();
    }

    private static string ReadClaudeResultText(string stdout)
    {
        try
        {
            using var doc = ParseJsonObject(stdout);
            if (doc.RootElement.TryGetProperty("result", out var result) && result.GetString() is { } text)
                return text;
        }
        catch (JsonException)
        {
            // Fall through: some wrappers emit the answer as plain text.
        }

        return stdout;
    }

    private static JsonDocument ParseJsonObject(string stdout)
    {
        var trimmed = stdout.Trim();
        try
        {
            return JsonDocument.Parse(trimmed);
        }
        catch (JsonException)
        {
            var start = trimmed.IndexOf('{');
            var end = trimmed.LastIndexOf('}');
            if (start >= 0 && end > start)
                return JsonDocument.Parse(trimmed[start..(end + 1)]);
            throw new InvalidOperationException("expected JSON. stdout:\n" + stdout);
        }
    }

    private static bool PathsEqual(string a, string b)
    {
        var na = Path.GetFullPath(a).TrimEnd('\\', '/');
        var nb = Path.GetFullPath(b).TrimEnd('\\', '/');
        return string.Equals(na, nb, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsUnder(string candidate, string root)
    {
        var fullCandidate = Path.GetFullPath(candidate).TrimEnd('\\', '/');
        var fullRoot = Path.GetFullPath(root).TrimEnd('\\', '/');
        if (string.Equals(fullCandidate, fullRoot, StringComparison.OrdinalIgnoreCase))
            return true;
        return fullCandidate.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static string TrimForLog(string s)
        => s.Length <= 4000 ? s : s[..4000] + "…";

    private sealed record PrintRun(int ExitCode, string Stdout, string Stderr);

    /// <summary>
    /// Throwaway nested layout matching the CARD-0251 plan §1 probe: parent is not a git
    /// repo; <c>source\repo</c> is.
    /// </summary>
    private sealed class NestedLayoutSandbox : IDisposable
    {
        public string Root { get; }
        public string Orch { get; }
        public string Repo { get; }

        private NestedLayoutSandbox(string root, string orch, string repo)
        {
            Root = root;
            Orch = orch;
            Repo = repo;
        }

        public static NestedLayoutSandbox Create()
        {
            var root = Directory.CreateTempSubdirectory("antiphon-0251-nested-").FullName;
            var orch = Path.Combine(root, "orch");
            var repo = Path.Combine(orch, "source", "repo");
            Directory.CreateDirectory(repo);
            File.WriteAllText(Path.Combine(orch, "CLAUDE.md"), NestedParentClaude + Environment.NewLine);
            File.WriteAllText(Path.Combine(orch, "AGENTS.md"), NestedParentAgents + Environment.NewLine);
            File.WriteAllText(Path.Combine(repo, "AGENTS.md"), NestedRepoAgents + Environment.NewLine);
            File.WriteAllText(Path.Combine(repo, "CLAUDE.md"), "@AGENTS.md" + Environment.NewLine);
            GitInit(repo);
            return new NestedLayoutSandbox(root, orch, repo);
        }

        public void Dispose() => BestEffortDelete(Root);
    }

    private sealed class SiblingLayoutSandbox : IDisposable
    {
        public string Root { get; }
        public string Orch { get; }
        public string Repo { get; }

        private SiblingLayoutSandbox(string root, string orch, string repo)
        {
            Root = root;
            Orch = orch;
            Repo = repo;
        }

        public static SiblingLayoutSandbox Create()
        {
            var root = Directory.CreateTempSubdirectory("antiphon-0251-sib-").FullName;
            var orch = Path.Combine(root, "orch");
            var repo = Path.Combine(root, "repo");
            Directory.CreateDirectory(orch);
            Directory.CreateDirectory(repo);
            var import = "@../repo/AGENTS.md";
            File.WriteAllText(Path.Combine(orch, "CLAUDE.md"),
                SiblingOrchClaude + Environment.NewLine + import + Environment.NewLine);
            File.WriteAllText(Path.Combine(repo, "AGENTS.md"), SiblingRepoAgents + Environment.NewLine);
            File.WriteAllText(Path.Combine(repo, "CLAUDE.md"), "@AGENTS.md" + Environment.NewLine);
            GitInit(repo);
            return new SiblingLayoutSandbox(root, orch, repo);
        }

        public void Dispose() => BestEffortDelete(Root);
    }

    /// <summary>
    /// Mutates the operator's real <c>~/.claude.json</c> for the import-gate probe and
    /// restores the original bytes on dispose. Scratch keys are never left behind on a
    /// normal test exit.
    /// </summary>
    private sealed class ClaudeJsonMutation : IDisposable
    {
        private readonly string _path;
        private readonly string? _original;

        public ClaudeJsonMutation()
        {
            _path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude.json");
            _original = File.Exists(_path) ? File.ReadAllText(_path) : null;
        }

        public void RestoreOriginal()
        {
            if (_original is not null)
                File.WriteAllText(_path, _original);
        }

        public void SetExternalIncludes(string directory, bool approved, bool forwardSlashKey)
        {
            var node = JsonNode.Parse(_original ?? "{}")?.AsObject()
                ?? new JsonObject();
            if (node["projects"] is not JsonObject projects)
            {
                projects = new JsonObject();
                node["projects"] = projects;
            }

            var full = Path.GetFullPath(directory).TrimEnd('\\', '/');
            var key = forwardSlashKey ? full.Replace('\\', '/') : full;
            if (projects[key] is not JsonObject project)
            {
                project = new JsonObject();
                projects[key] = project;
            }

            project["hasClaudeMdExternalIncludesApproved"] = approved;
            project["hasTrustDialogAccepted"] = true;
            project["hasCompletedProjectOnboarding"] = true;
            File.WriteAllText(_path, node.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }

        public void Dispose() => RestoreOriginal();
    }

    private static void GitInit(string dir)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = dir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add("init");
        psi.ArgumentList.Add("-b");
        psi.ArgumentList.Add("master");
        psi.ArgumentList.Add("-q");
        using var p = Process.Start(psi) ?? throw new InvalidOperationException("git init failed to start");
        p.WaitForExit();
        if (p.ExitCode != 0)
            throw new InvalidOperationException("git init failed in " + dir);
    }

    private static void BestEffortDelete(string dir)
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(dir, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
