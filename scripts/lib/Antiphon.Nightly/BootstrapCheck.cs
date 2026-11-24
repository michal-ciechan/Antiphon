// CARD-0088 S2, CARD-0254, CARD-0408 V-30: the read-only bootstrap self-check of a desktop checkout. It replaced
// scripts/bootstrap-check.ps1 (deleted 2026-11-24, unit 38 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md);
// nothing started that script by path, so it keeps no launcher. The CLI runs it as
//
//   antiphon-scripts bootstrap-check [--no-stack]
//
// (scripts\antiphon.cmd bootstrap-check on Windows, scripts/antiphon bootstrap-check on sh). Phase A: the agent context
// (AgentContextCheck, in this process), the toolchain (dotnet --version against global.json's sdk.version and rollForward,
// node >= 20, npm, the pwsh app-exec alias, the Docker daemon within 12 s, a TUI wrapper on PATH), the Windows convention
// directories, the antiphon-postgres container and the antiphon_pgdata volume, TCP 127.0.0.1:17280 (with pg_isready when Docker
// answers), the two logon Scheduled Tasks, an LLM provider key's presence (user-secrets, then the gitignored overlays, then the
// environment; a value is never printed), the Data Protection key ring, the repository's .claude/skills and the user's
// CLAUDE.md, the client bundle's staleness and the tracked config (Git:DefaultBranch, Git:WorkspacePath). Phase B (skipped by
// --no-stack): the projects' card-file warnings (one GET <ANTIPHON_API>/api/projects; never a repair) and verify-dev-stack
// --skip-browser in this process, its lines passed through. With --no-stack the Docker network create probe runs instead (the
// network is removed again): the only side effect.
//
// Each check prints "  [PASS|WARN|FAIL|SKIP] <name, 28 wide> <detail>", then the remedy of a WARN or FAIL, one indented line each.
// The exit code is the number of FAILs; a WARN never counts. Usage errors are NightlyUsageException (the CLI's exit 2). The
// machine is IBootstrapCheckPlatform (production BootstrapCheckOs); the environment (LOCALAPPDATA, WINDIR, USERPROFILE,
// ANTIPHON_API, Llm__Providers__anthropic__ApiKey, Llm__Providers__openai__ApiKey, PATH) is the host's Variables. JSON keys
// match exactly. Plain C#: no PowerShell types. ASCII only.
#nullable disable
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;

namespace Antiphon.Nightly
{
    /// <summary>The bootstrap-check command's options (bootstrap-check.ps1 -NoStack).</summary>
    public sealed class BootstrapCheckOptions
    {
        /// <summary>The CLI's usage line.</summary>
        public const string Usage = "bootstrap-check [--no-stack]";

        /// <summary>Skip Phase B (the live stack); probe Docker's network create instead.</summary>
        public bool NoStack { get; init; }

        /// <summary>The checkout that is checked.</summary>
        public string RepoRoot { get; init; } = "";

        /// <summary>The flags after the command name; the checkout comes from the CLI.</summary>
        public static BootstrapCheckOptions Parse(IReadOnlyList<string> arguments, string repoRoot)
        {
            var flags = NightlyArgs.Parse(arguments, Array.Empty<string>(), new[] { "--no-stack" });
            if (string.IsNullOrEmpty(repoRoot)) throw new NightlyUsageException("no checkout found to check");
            return new BootstrapCheckOptions { NoStack = flags.Switch("--no-stack"), RepoRoot = repoRoot };
        }
    }

    /// <summary>One native command: it started, it ran past its time limit (and was stopped), its exit code and its output.</summary>
    /// <remarks>Output is stdout then stderr, line by line, joined with "\n" and trimmed; for a command that did not start, the reason.</remarks>
    public sealed record BootstrapCommandResult(bool Started, bool TimedOut, int ExitCode, string Output)
    {
        /// <summary>It ran to the end and exited 0.</summary>
        public bool Succeeded => Started && !TimedOut && ExitCode == 0;
    }

    /// <summary>The machine the bootstrap check reads.</summary>
    public interface IBootstrapCheckPlatform
    {
        /// <summary>The file system.</summary>
        IFileSystem Files { get; }

        /// <summary>Get-Command: the first name found on PATH, as a full path; null when none is.</summary>
        string FindCommand(IReadOnlyList<string> names);

        /// <summary>Runs a command (workingDirectory null: this process's) and waits at most timeoutSec (0: no limit).</summary>
        BootstrapCommandResult Run(string file, IReadOnlyList<string> arguments, string workingDirectory, int timeoutSec);

        /// <summary>A TCP connect to host:port succeeds within the time.</summary>
        bool TcpOpen(string host, int port, int timeoutMs);

        /// <summary>Get-ScheduledTask -TaskName: the task is registered (enabled or not); false when it cannot be read.</summary>
        bool TaskRegistered(string name);

        /// <summary>One GET within the timeout: the answer; throws when nothing answers.</summary>
        NightlyReportHttpResponse Get(string url, TimeSpan timeout);

        /// <summary>verify-dev-stack --skip-browser on this checkout, its lines on the host's output: its exit code.</summary>
        int VerifyDevStack(NightlyHost host);
    }

    /// <summary>The bootstrap self-check (bootstrap-check.ps1's body).</summary>
    public static class BootstrapCheck
    {
        /// <summary>The AppHost's logon task.</summary>
        public const string AppHostTask = "Antiphon AppHost";

        /// <summary>The SessionRunner's logon task.</summary>
        public const string RunnerTask = "Antiphon Session Runner";

        /// <summary>The Windows convention directories (docs/bootstrap.md).</summary>
        public static readonly IReadOnlyList<string> ConventionDirectories = new[]
        {
            @"C:\Antiphon\worktrees",
            @"C:\logs\antiphon\session-runner",
            @"C:\logs\antiphon\check-interpreter",
            @"C:\logs\antiphon\diagnose",
            @"C:\logs\antiphon\output-distiller",
        };

        private const string Pass = "PASS";
        private const string Warn = "WARN";
        private const string Fail = "FAIL";
        private const string Skip = "SKIP";
        private const string Compose = "docker compose -f docker-compose.dev.yml up -d";

        private static readonly Regex UserSecretsKey = new Regex(@"^Llm:Providers:(anthropic|openai):ApiKey\s*=\s*\S",
            RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.CultureInvariant);

        private static readonly JsonDocumentOptions Lenient = new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

        /// <summary>Runs every check; the number of FAILs.</summary>
        public static int Run(BootstrapCheckOptions options, NightlyHost host, IBootstrapCheckPlatform platform)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (host == null) throw new ArgumentNullException(nameof(host));
            if (platform == null) throw new ArgumentNullException(nameof(platform));
            var files = platform.Files;
            var root = options.RepoRoot;
            var report = new Report(host.Out);
            string Variable(string name) => host.Variables?.Invoke(name);

            host.Out.WriteLine("See docs/bootstrap.md for the bootstrap checklist this command checks.");
            host.Out.WriteLine("");
            host.Out.WriteLine(options.NoStack ? "Antiphon bootstrap check (Phase A only; --no-stack)" : "Antiphon bootstrap check (Phase A + live stack)");
            host.Out.WriteLine("");

            // CARD-0254: the agent context's byte budget, from this shared front door.
            int agentContext;
            try
            {
                agentContext = AgentContextCheck.Run(new AgentContextCheckOptions { Repo = root }, host, files);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                host.Error.WriteLine("AGENTS.md could not be read: " + e.Message);
                agentContext = 1;
            }
            if (agentContext == 0) report.Check("agent context", Pass, "AGENTS.md byte budget and section report passed");
            else report.Check("agent context", Fail, "AGENTS.md violated its byte budget or could not be read",
                "Run scripts\\antiphon.cmd check-agent-context (scripts/antiphon on sh) and restore the 24,576-byte target.");

            // ---- 1. Toolchain ----
            var globalJsonPath = files.Path.Combine(root, "global.json");
            string pinnedSdk = null;
            var rollForward = "latestMinor";
            using (var globalJson = ReadJson(files, globalJsonPath))
            {
                if (globalJson != null && Child(globalJson.RootElement, "sdk") is JsonElement sdk)
                {
                    pinnedSdk = Scalar(Child(sdk, "version"));
                    var policy = Scalar(Child(sdk, "rollForward"));
                    if (!Blank(policy)) rollForward = policy;
                }
            }

            var dotnet = platform.FindCommand(new[] { "dotnet.exe", "dotnet" });
            if (dotnet == null)
            {
                report.Check("toolchain / dotnet", Fail, "dotnet not on PATH", "Install the .NET SDK pinned by global.json, then reopen the shell.");
            }
            else if (Blank(pinnedSdk))
            {
                report.Check("toolchain / dotnet", Fail, "global.json missing or unreadable", "Expected " + globalJsonPath);
            }
            else
            {
                // In the checkout, so global.json chooses the SDK.
                var sdkRun = platform.Run(dotnet, new[] { "--version" }, root, 0);
                if (sdkRun.Succeeded && SdkSatisfiesGlobalJson(sdkRun.Output, pinnedSdk, rollForward))
                    report.Check("toolchain / dotnet", Pass, sdkRun.Output + " satisfies global.json " + pinnedSdk + " (" + rollForward + ")");
                else
                    report.Check("toolchain / dotnet", Fail, (Blank(sdkRun.Output) ? "no version" : sdkRun.Output) + " does not satisfy global.json " + pinnedSdk + " (" + rollForward + ")",
                        "Install the SDK version named in global.json (rollForward: latestMinor).");
            }

            var node = platform.FindCommand(new[] { "node.exe", "node" });
            if (node == null)
            {
                report.Check("toolchain / node", Fail, "node not on PATH", "Install Node.js 20 or newer.");
            }
            else
            {
                var nodeRun = platform.Run(node, new[] { "--version" }, null, 0);
                var digits = Regex.Match(nodeRun.Output ?? "", @"(\d+)", RegexOptions.CultureInvariant);
                var major = digits.Success && int.TryParse(digits.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) ? parsed : -1;
                if (nodeRun.Succeeded && major >= 20) report.Check("toolchain / node", Pass, nodeRun.Output + " (>= 20)");
                else report.Check("toolchain / node", Fail, nodeRun.Output + " (need >= 20)", "Install Node.js 20 or newer.");
            }

            var npm = platform.FindCommand(new[] { "npm.cmd", "npm.exe", "npm" });
            if (npm == null)
            {
                report.Check("toolchain / npm", Fail, "npm not on PATH", "Install Node.js 20+ (includes npm) and reopen the shell.");
            }
            else
            {
                var npmRun = platform.Run(npm, new[] { "--version" }, null, 0);
                if (npmRun.Succeeded && !Blank(npmRun.Output)) report.Check("toolchain / npm", Pass, npmRun.Output);
                else report.Check("toolchain / npm", Fail, "npm did not report a version", "Repair the Node.js install so npm --version works.");
            }

            var localAppData = Variable("LOCALAPPDATA");
            var windir = Variable("WINDIR");
            var pwshAlias = Blank(localAppData) ? null : files.Path.Combine(localAppData, @"Microsoft\WindowsApps\pwsh.exe");
            var ps51 = Blank(windir) ? null : files.Path.Combine(windir, @"System32\WindowsPowerShell\v1.0\powershell.exe");
            if (pwshAlias != null && Exists(files, pwshAlias))
                report.Check("toolchain / pwsh", Pass, "app-exec alias " + pwshAlias);
            else if (ps51 != null && Exists(files, ps51))
                report.Check("toolchain / pwsh", Warn, "only Windows PowerShell 5.1 found",
                    "Install PowerShell 7 and keep the version-independent %LOCALAPPDATA%\\Microsoft\\WindowsApps\\pwsh.exe alias. Do not pin a WindowsApps Microsoft.PowerShell_<version> path.");
            else
                report.Check("toolchain / pwsh", Warn, "pwsh app-exec alias not found",
                    "Install PowerShell 7 via winget and enable the app-exec alias %LOCALAPPDATA%\\Microsoft\\WindowsApps\\pwsh.exe.");

            var docker = platform.FindCommand(new[] { "docker.exe", "docker" });
            var dockerUp = false;
            if (docker == null)
            {
                report.Check("toolchain / docker", Fail, "docker not on PATH", "Install Docker Desktop and confirm docker version shows a Server section.");
            }
            else
            {
                var version = platform.Run(docker, new[] { "version" }, null, 12);
                if (version.TimedOut)
                {
                    report.Check("toolchain / docker", Fail, "docker version timed out (12s)",
                        "Docker Desktop / the engine is not answering. Start it (docker-desktop skill or restart.cmd).");
                }
                else if (Lines(version.Output).Any(line => line.IndexOf("Server:", StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    dockerUp = true;
                    report.Check("toolchain / docker", Pass, "daemon answering");
                }
                else
                {
                    report.Check("toolchain / docker", Fail, "docker daemon not answering", "Start Docker Desktop and wait until docker version prints a Server section.");
                }
            }

            var tuis = new List<string>();
            if (platform.FindCommand(new[] { "claude.exe", "claude" }) != null) tuis.Add("claude.exe");
            if (platform.FindCommand(new[] { "grok.exe", "grok" }) != null) tuis.Add("grok.exe");
            if (tuis.Count > 0) report.Check("toolchain / tui", Pass, string.Join(", ", tuis));
            else report.Check("toolchain / tui", Warn, "neither claude.exe nor grok.exe on PATH",
                "Install and log in at least one TUI wrapper; agent sessions authenticate through it.");

            // ---- 2. Windows convention directories ----
            foreach (var directory in ConventionDirectories)
            {
                if (Exists(files, directory)) report.Check("dirs", Pass, directory);
                else report.Check("dirs", Warn, "missing " + directory, "mkdir " + directory);
            }

            // ---- 3. Docker postgres and volume (and HNS when Phase B is skipped) ----
            var skipPostgres = DevPostgresCheck.SkipLocal(host);
            if (skipPostgres)
            {
                report.Check("docker / postgres", Skip, DevPostgresCheck.SkipReason);
                report.Check("docker / volume", Skip, DevPostgresCheck.SkipReason);
            }
            else if (!dockerUp)
            {
                report.Check("docker / postgres", Fail, "cannot inspect antiphon-postgres (daemon down)", "Start Docker Desktop, then: " + Compose);
                report.Check("docker / volume", Fail, "cannot inspect antiphon_pgdata (daemon down)", "Start Docker Desktop, then: " + Compose);
            }
            else
            {
                var inspect = platform.Run(docker, new[] { "inspect", "--format", "{{.State.Status}}|{{if .State.Health}}{{.State.Health.Status}}{{else}}no-health{{end}}", "antiphon-postgres" },
                    null, 0);
                if (inspect.Succeeded && Regex.IsMatch(inspect.Output ?? "", @"^running\|healthy$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                    report.Check("docker / postgres", Pass, "antiphon-postgres running + healthy");
                else
                    report.Check("docker / postgres", Fail, "antiphon-postgres " + (Blank(inspect.Output) ? "not found" : inspect.Output), Compose);

                var volume = platform.Run(docker, new[] { "volume", "inspect", "antiphon_pgdata" }, null, 0);
                if (volume.Succeeded) report.Check("docker / volume", Pass, "antiphon_pgdata exists");
                else report.Check("docker / volume", Fail, "antiphon_pgdata volume not found", Compose + "  (project name antiphon + volume pgdata)");
            }

            if (options.NoStack)
            {
                if (!dockerUp)
                {
                    report.Check("docker / hns", Warn, "skipped (docker daemon down)");
                }
                else
                {
                    // verify-dev-stack owns this probe in Phase B. A create that answered in time passed; the network is removed again.
                    var network = "bootstrap-hns-" + Random.Shared.Next().ToString(CultureInfo.InvariantCulture);
                    var create = platform.Run(docker, new[] { "network", "create", network }, null, 8);
                    if (create.Started && !create.TimedOut)
                    {
                        platform.Run(docker, new[] { "network", "rm", network }, null, 0);
                        report.Check("docker / hns", Pass, "docker network create completed");
                    }
                    else
                    {
                        report.Check("docker / hns", Warn, "docker network create hung or failed", "Restart Docker Desktop (Windows HNS is broken when network create hangs).");
                    }
                }
            }

            // ---- 4. Database TCP (and pg_isready) ----
            if (skipPostgres)
            {
                report.Check("database / localhost:17280", Skip, DevPostgresCheck.SkipReason);
                var result = DevPostgresCheck.Probe(host, files, root, simpleMode: false, platform.TcpOpen);
                report.Check("database", result.Ok ? Pass : Fail, result.Detail);
            }
            else
            {
                var tcpOpen = platform.TcpOpen("127.0.0.1", 17280, 2000);
                var composeFile = files.Path.Combine(root, "docker-compose.dev.yml");
                var ready = "";
                if (dockerUp && Exists(files, composeFile))
                {
                    var isReady = platform.Run(docker, new[] { "compose", "-f", composeFile, "exec", "-T", "postgres", "pg_isready", "-U", "antiphon", "-d", "antiphon" }, null, 0);
                    if (isReady.Succeeded) ready = "; pg_isready ok";
                    else if (!Blank(isReady.Output)) ready = "; pg_isready: " + Regex.Replace(isReady.Output, @"\s+", " ");
                    else ready = "; pg_isready failed";
                }
                if (tcpOpen) report.Check("database", Pass, "TCP localhost:17280 open" + ready);
                else report.Check("database", Fail, "TCP localhost:17280 closed" + ready, Compose + "  (postgres publishes 17280)");
            }

            // ---- 5. Scheduled Tasks (WARN only) ----
            var appHostTask = platform.TaskRegistered(AppHostTask);
            var runnerTask = platform.TaskRegistered(RunnerTask);
            if (appHostTask && runnerTask)
            {
                report.Check("scheduled tasks", Pass, AppHostTask + " + " + RunnerTask + " registered");
            }
            else
            {
                var missing = new List<string>();
                if (!appHostTask) missing.Add(AppHostTask);
                if (!runnerTask) missing.Add(RunnerTask);
                string remedy;
                if (runnerTask) remedy = "scripts\\antiphon.cmd install-autostart --apphost-only   (use --apphost-only so a live session-runner is not killed by re-register)";
                else if (appHostTask) remedy = "scripts\\antiphon.cmd install-autostart   (full install; --apphost-only leaves the runner task alone)";
                else remedy = "scripts\\antiphon.cmd install-autostart   (re-register kills a running session-runner; afterwards use --apphost-only to refresh only the AppHost task)";
                report.Check("scheduled tasks", Warn, "not registered: " + string.Join(", ", missing), remedy);
            }

            // ---- 6. Secrets presence (a value is never printed) ----
            string secretSource = null;
            if (dotnet != null && UserSecretsKey.IsMatch(platform.Run(dotnet, new[] { "user-secrets", "list", "--id", "antiphon-server" }, null, 0).Output ?? ""))
                secretSource = "dotnet user-secrets (id antiphon-server)";
            if (secretSource == null)
            {
                foreach (var overlay in new[] { "appsettings.Development.json", "appsettings.Production.json" })
                {
                    using var config = ReadJson(files, files.Path.Combine(root, "server", overlay));
                    if (config != null && HasProviderKey(config.RootElement))
                    {
                        secretSource = "gitignored " + overlay;
                        break;
                    }
                }
            }
            if (secretSource == null)
            {
                foreach (var name in new[] { "Llm__Providers__anthropic__ApiKey", "Llm__Providers__openai__ApiKey" })
                {
                    if (Blank(Variable(name))) continue;
                    secretSource = "env " + name;
                    break;
                }
            }
            if (secretSource != null)
                report.Check("secrets", Pass, "Llm provider ApiKey present via " + secretSource);
            else
                report.Check("secrets", Warn, "no Llm:Providers anthropic/openai ApiKey in user-secrets, overlay json, or env",
                    "Preferred: dotnet user-secrets set \"Llm:Providers:anthropic:ApiKey\" <value> --id antiphon-server. A TUI-only deployment can run without workflow LLM keys; Settings UI is the other path.");

            var keyRing = Blank(localAppData) ? null : files.Path.Combine(localAppData, @"Antiphon\DataProtection-Keys");
            if (keyRing != null && Exists(files, keyRing))
                report.Check("secrets / keyring", Pass, "default KeyRingPath exists: " + keyRing);
            else
                report.Check("secrets / keyring", Pass, "default KeyRingPath not present (" + keyRing + ") - fresh ring is expected until managed TUI secrets are entered; DB not queried");

            // ---- 7. Claude-side ----
            if (Exists(files, files.Path.Combine(root, ".claude", "skills")))
                report.Check("claude / repo skills", Pass, ".claude/skills/ present");
            else
                report.Check("claude / repo skills", Fail, ".claude/skills/ missing", "Broken clone. Re-clone the repo so tracked .claude/skills ships with it.");

            var profile = Variable("USERPROFILE");
            var homeClaude = Blank(profile) ? @".claude\CLAUDE.md" : files.Path.Combine(profile, @".claude\CLAUDE.md");
            if (!Blank(profile) && Exists(files, homeClaude))
                report.Check("claude / user CLAUDE.md", Pass, homeClaude);
            else
                report.Check("claude / user CLAUDE.md", Warn, homeClaude + " missing",
                    "New operator may not have synced claude-home. Optional for product boot; needed for this operator's global skills and policy.");

            // ---- 8. Client bundle staleness (the rule of AntiphonAppFixture.EnsureClientBundleIsCurrent) ----
            ClientBundle(files, root, report);

            // ---- 9. Tracked-config sanity (WARN) ----
            string branch = null;
            var git = platform.FindCommand(new[] { "git.exe", "git" });
            if (git != null)
            {
                var head = platform.Run(git, new[] { "-C", root, "rev-parse", "--abbrev-ref", "HEAD" }, null, 0);
                if (head.Succeeded) branch = head.Output;
            }
            using (var appsettings = ReadJson(files, files.Path.Combine(root, "server", "appsettings.json")))
            {
                if (appsettings == null || appsettings.RootElement.ValueKind != JsonValueKind.Object)
                {
                    report.Check("config / DefaultBranch", Warn, "server/appsettings.json missing or unreadable");
                    report.Check("config / WorkspacePath", Warn, "server/appsettings.json missing or unreadable");
                }
                else
                {
                    var gitSection = Child(appsettings.RootElement, "Git");
                    var configBranch = Scalar(gitSection == null ? null : Child(gitSection.Value, "DefaultBranch")) ?? "";
                    var workspace = Scalar(gitSection == null ? null : Child(gitSection.Value, "WorkspacePath")) ?? "";
                    if (Blank(branch))
                        report.Check("config / DefaultBranch", Warn, "could not read git rev-parse --abbrev-ref HEAD");
                    else if (string.Equals(configBranch, branch, StringComparison.OrdinalIgnoreCase))
                        report.Check("config / DefaultBranch", Pass, "Git:DefaultBranch=" + configBranch + " matches HEAD");
                    else
                        report.Check("config / DefaultBranch", Warn, "Git:DefaultBranch=" + configBranch + " != HEAD " + branch,
                            "Tracked server/appsettings.json Git:DefaultBranch should match the repo default branch (master).");

                    if (Blank(workspace))
                        report.Check("config / WorkspacePath", Pass, "Git:WorkspacePath is empty");
                    else if (Exists(files, workspace))
                        report.Check("config / WorkspacePath", Pass, "Git:WorkspacePath exists: " + workspace);
                    else
                        report.Check("config / WorkspacePath", Warn, "Git:WorkspacePath does not exist: " + workspace,
                            "Set Git:WorkspacePath to \"\" or an existing path. A leftover D:\\ path is the usual miss.");
                }
            }

            // ---- Phase B: the live stack (or skip) ----
            host.Out.WriteLine("");
            if (options.NoStack)
            {
                host.Out.WriteLine("Phase B skipped (--no-stack): stack health was not checked.");
            }
            else
            {
                host.Out.WriteLine("Phase B: verify-dev-stack --skip-browser");
                var api = Variable("ANTIPHON_API");
                var line = CardFiles(Blank(api) ? "http://localhost:17202" : api.TrimEnd('/'), platform);
                report.Check("card-files", line.Status, line.Detail, line.Remedy);
                StackHealth(host, platform, report);
            }

            host.Out.WriteLine("");
            host.Out.WriteLine("Summary: " + Num(report.Passes) + " PASS, " + Num(report.Warnings) + " WARN, " + Num(report.Failures) + " FAIL");
            host.Out.WriteLine("Exit code: " + Num(report.Failures));
            return report.Failures;
        }

        /// <summary>
        /// Test-SdkSatisfiesGlobalJson: the installed SDK satisfies the pinned version under the roll-forward policy (absent:
        /// latestPatch). disable is the same version; latestMajor and major any later one; latestMinor and minor a later one of the
        /// same major; latestFeature, feature, latestPatch and patch a later one of the same major and minor. A version that does not
        /// parse never satisfies.
        /// </summary>
        public static bool SdkSatisfiesGlobalJson(string installed, string pinned, string rollForward)
        {
            if (Blank(installed) || Blank(pinned)) return false;
            if (!Version.TryParse(installed, out var a) || !Version.TryParse(pinned, out var b)) return false;
            var policy = Blank(rollForward) ? "latestPatch" : rollForward;
            switch (policy.ToLowerInvariant())
            {
                case "disable": return string.Equals(installed, pinned, StringComparison.OrdinalIgnoreCase);
                case "latestmajor":
                case "major": return a >= b;
                case "latestminor":
                case "minor": return a.Major == b.Major && a >= b;
                default: return a.Major == b.Major && a.Minor == b.Minor && a >= b;
            }
        }

        /// <summary>
        /// CARD-0408 V-30: the projects' card-file setup warnings, read with one GET of api/projects. Each warning WARNs; a project
        /// without the field (an older server) WARNs as unavailable; an unreadable answer WARNs. It never writes anything.
        /// </summary>
        public static BootstrapCheckLine CardFiles(string api, IBootstrapCheckPlatform platform)
        {
            if (platform == null) throw new ArgumentNullException(nameof(platform));
            try
            {
                var answer = platform.Get(api + "/api/projects", TimeSpan.FromSeconds(10));
                if (!OperatorApi.Succeeded(answer)) throw new InvalidOperationException(OperatorApi.Failure(answer));
                using var document = JsonDocument.Parse(answer.Body ?? "");
                var projects = document.RootElement.ValueKind == JsonValueKind.Array ? document.RootElement.EnumerateArray().ToArray() : new[] { document.RootElement };
                var warnings = new List<string>();
                var unavailable = false;
                foreach (var project in projects)
                {
                    if (!(Child(project, "cardFileWarnings") is JsonElement field) || field.ValueKind == JsonValueKind.Null)
                    {
                        unavailable = true;
                        continue;
                    }
                    var items = field.ValueKind == JsonValueKind.Array ? field.EnumerateArray().ToArray() : new[] { field };
                    // Where-Object { $_ }: an empty string, false and null are no warning.
                    foreach (var item in items)
                    {
                        var text = item.ValueKind == JsonValueKind.False ? null : Scalar(item);
                        if (!string.IsNullOrEmpty(text)) warnings.Add(text);
                    }
                }
                if (warnings.Count > 0)
                    return new BootstrapCheckLine(Warn, string.Join(", ", warnings), "Review docs/card-file-privacy.md; this check does not repair ignores or publish files.");
                if (unavailable)
                    return new BootstrapCheckLine(Warn, "Publication status unavailable from this server.", "Review docs/card-file-privacy.md.");
                return new BootstrapCheckLine(Pass, "No project card-file setup warnings; publication still requires explicit opt-in.", "");
            }
            catch (Exception e) when (e is HttpRequestException || e is OperationCanceledException || e is IOException || e is InvalidOperationException
                || e is JsonException || e is UriFormatException || e is ArgumentException)
            {
                return new BootstrapCheckLine(Warn, "Project card-file readiness could not be inspected.", "Read-only warning; retry when the API is available.");
            }
        }

        // verify-dev-stack --skip-browser in this process, each line passed through and kept: on a failure the remedy is its
        // verdict (from "Stack NOT healthy" or "check(s) failed" on), else its last eight lines.
        private static void StackHealth(NightlyHost host, IBootstrapCheckPlatform platform, Report report)
        {
            var lines = new List<string>();
            int code;
            using (var tee = new LineTee(host.Out, lines))
            {
                try
                {
                    code = platform.VerifyDevStack(DevStack.WithOutput(host, tee));
                }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is InvalidOperationException || e is Win32Exception
                    || e is HttpRequestException || e is PlatformNotSupportedException)
                {
                    tee.WriteLine("verify-dev-stack failed: " + e.Message);
                    code = 1;
                }
                tee.Flush();
            }
            if (code == 0)
            {
                report.Check("stack health", Pass, "verify-dev-stack --skip-browser exited 0");
                return;
            }
            var summary = new List<string>();
            var capture = false;
            foreach (var line in lines)
            {
                if (line.Contains("Stack NOT healthy", StringComparison.OrdinalIgnoreCase) || line.Contains("check(s) failed", StringComparison.OrdinalIgnoreCase)) capture = true;
                if (capture) summary.Add(line);
            }
            if (summary.Count == 0) summary.AddRange(lines.Skip(Math.Max(0, lines.Count - 8)));
            var remedy = string.Join("\n", summary);
            if (Blank(remedy)) remedy = "verify-dev-stack exited " + Num(code);
            report.Check("stack health", Fail, "verify-dev-stack --skip-browser exited " + Num(code), remedy);
        }

        // client/dist/index.html against the newest client/src file that is not a test or a story.
        private static void ClientBundle(IFileSystem files, string root, Report report)
        {
            var clientDir = files.Path.Combine(root, "client");
            var index = files.Path.Combine(clientDir, "dist", "index.html");
            var source = files.Path.Combine(clientDir, "src");
            if (!Exists(files, index))
            {
                report.Check("client bundle", Warn, "client/dist/index.html missing", "Run npm run build in client/. E2E serves this bundle and hard-fails when it is absent.");
                return;
            }
            if (!Exists(files, source))
            {
                report.Check("client bundle", Pass, "client/dist/index.html present (no client/src to compare)");
                return;
            }
            var builtAt = files.File.GetLastWriteTimeUtc(index);
            string newest = null;
            var newestAt = DateTime.MinValue;
            IEnumerable<string> sources;
            try
            {
                sources = files.Directory.EnumerateFiles(source, "*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true }).ToArray();
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                sources = Array.Empty<string>();
            }
            foreach (var file in sources)
            {
                var name = files.Path.GetFileName(file);
                if (name.EndsWith(".test.ts", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".test.tsx", StringComparison.OrdinalIgnoreCase)
                    || name.EndsWith(".stories.tsx", StringComparison.OrdinalIgnoreCase)) continue;
                var written = files.File.GetLastWriteTimeUtc(file);
                if (written > builtAt && written > newestAt)
                {
                    newest = file;
                    newestAt = written;
                }
            }
            if (newest == null)
            {
                report.Check("client bundle", Pass, "client/dist/index.html is current (" + Universal(builtAt) + ")");
                return;
            }
            var relative = newest.StartsWith(clientDir, StringComparison.OrdinalIgnoreCase) ? newest.Substring(clientDir.Length).TrimStart('\\', '/') : newest;
            report.Check("client bundle", Warn, "stale: dist " + Universal(builtAt) + ", but " + relative + " changed " + Universal(newestAt), "Run npm run build in client/.");
        }

        // Llm:Providers:anthropic:ApiKey or Llm:Providers:openai:ApiKey, a non-blank string.
        private static bool HasProviderKey(JsonElement config)
        {
            if (!(Child(config, "Llm") is JsonElement llm) || !(Child(llm, "Providers") is JsonElement providers)) return false;
            foreach (var name in new[] { "anthropic", "openai" })
                if (Child(providers, name) is JsonElement provider && !Blank(Scalar(Child(provider, "ApiKey")))) return true;
            return false;
        }

        // The file as JSON (comments and trailing commas allowed); null when it is missing or does not parse.
        private static JsonDocument ReadJson(IFileSystem files, string path)
        {
            try
            {
                if (!files.File.Exists(path)) return null;
                return JsonDocument.Parse(files.File.ReadAllText(path), Lenient);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is JsonException)
            {
                return null;
            }
        }

        // A property of an object, matched exactly; null otherwise.
        private static JsonElement? Child(JsonElement element, string name) =>
            element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) ? value : (JsonElement?)null;

        // A string's value, a number's or boolean's text; null for anything else.
        private static string Scalar(JsonElement? element)
        {
            if (element == null) return null;
            switch (element.Value.ValueKind)
            {
                case JsonValueKind.String: return element.Value.GetString();
                case JsonValueKind.Number: return element.Value.GetRawText();
                case JsonValueKind.True: return "True";
                case JsonValueKind.False: return "False";
                default: return null;
            }
        }

        private static bool Exists(IFileSystem files, string path) => files.File.Exists(path) || files.Directory.Exists(path);

        private static bool Blank(string value) => string.IsNullOrWhiteSpace(value);

        private static IEnumerable<string> Lines(string text) => (text ?? "").Split('\n').Select(line => line.TrimEnd('\r'));

        private static string Universal(DateTime value) => value.ToString("u", CultureInfo.InvariantCulture);

        private static string Num(int value) => value.ToString(CultureInfo.InvariantCulture);

        // Write-Check: one line, then the remedy's lines for a WARN or FAIL; the counts.
        private sealed class Report
        {
            private readonly TextWriter _out;

            public Report(TextWriter output) => _out = output;

            public int Passes { get; private set; }

            public int Warnings { get; private set; }

            public int Failures { get; private set; }

            public void Check(string name, string status, string detail, string remedy = "")
            {
                if (status == Pass) Passes++;
                else if (status == Warn) Warnings++;
                else if (status == Fail) Failures++;
                _out.WriteLine(string.Format(CultureInfo.InvariantCulture, "  [{0}] {1,-28} {2}", status, name, detail));
                if (string.IsNullOrEmpty(remedy) || status == Pass) return;
                foreach (var line in remedy.Split('\n'))
                    if (line.Trim().Length > 0) _out.WriteLine("           " + line);
            }
        }

        // Passes every line to the inner writer and keeps it.
        private sealed class LineTee : TextWriter
        {
            private readonly TextWriter _inner;
            private readonly List<string> _lines;
            private readonly StringBuilder _pending = new StringBuilder();

            public LineTee(TextWriter inner, List<string> lines)
            {
                _inner = inner;
                _lines = lines;
            }

            public override Encoding Encoding => Encoding.UTF8;

            public override void Write(char value)
            {
                if (value == '\n')
                {
                    Emit();
                    return;
                }
                if (value != '\r') _pending.Append(value);
            }

            public override void Flush()
            {
                if (_pending.Length > 0) Emit();
                _inner.Flush();
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing && _pending.Length > 0) Emit();
                base.Dispose(disposing);
            }

            private void Emit()
            {
                var line = _pending.ToString();
                _pending.Clear();
                _lines.Add(line);
                _inner.WriteLine(line);
            }
        }
    }

    /// <summary>One check's status (PASS, WARN, FAIL or SKIP), detail and remedy.</summary>
    public sealed record BootstrapCheckLine(string Status, string Detail, string Remedy);

    /// <summary>The production IBootstrapCheckPlatform: this machine.</summary>
    public sealed class BootstrapCheckOs : IBootstrapCheckPlatform
    {
        private static readonly HttpClient Client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        private readonly NightlyHost _host;
        private readonly Func<NightlyHost, int> _verifyDevStack;

        /// <summary>host gives PATH; verifyDevStack runs verify-dev-stack --skip-browser on a host (the CLI binds the AppHost platform).</summary>
        public BootstrapCheckOs(NightlyHost host, IFileSystem files, Func<NightlyHost, int> verifyDevStack)
        {
            _host = host ?? throw new ArgumentNullException(nameof(host));
            Files = files ?? throw new ArgumentNullException(nameof(files));
            _verifyDevStack = verifyDevStack ?? throw new ArgumentNullException(nameof(verifyDevStack));
        }

        /// <inheritdoc />
        public IFileSystem Files { get; }

        /// <inheritdoc />
        public string FindCommand(IReadOnlyList<string> names)
        {
            var directories = (_host.Variables("PATH") ?? "").Split(Path.PathSeparator).Where(directory => !string.IsNullOrWhiteSpace(directory)).ToArray();
            foreach (var name in names)
            {
                foreach (var directory in directories)
                {
                    string candidate;
                    try { candidate = Path.Combine(directory.Trim().Trim('"'), name); }
                    catch (ArgumentException) { continue; }
                    if (File.Exists(candidate)) return candidate;
                }
            }
            return null;
        }

        /// <inheritdoc />
        public BootstrapCommandResult Run(string file, IReadOnlyList<string> arguments, string workingDirectory, int timeoutSec)
        {
            var start = new ProcessStartInfo(file)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };
            if (!string.IsNullOrEmpty(workingDirectory)) start.WorkingDirectory = workingDirectory;
            foreach (var argument in arguments) start.ArgumentList.Add(argument);
            Process process;
            try
            {
                process = Process.Start(start);
            }
            catch (Exception e) when (e is Win32Exception || e is InvalidOperationException || e is IOException)
            {
                return new BootstrapCommandResult(false, false, -1, e.Message);
            }
            if (process == null) return new BootstrapCommandResult(false, false, -1, "the command did not start: " + file);
            using (process)
            {
                var output = new List<string>();
                var errors = new List<string>();
                var gate = new object();
                process.OutputDataReceived += (_, e) => { if (e.Data != null) lock (gate) output.Add(e.Data); };
                process.ErrorDataReceived += (_, e) => { if (e.Data != null) lock (gate) errors.Add(e.Data); };
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                if (!process.WaitForExit(timeoutSec > 0 ? timeoutSec * 1000 : Timeout.Infinite))
                {
                    try { process.Kill(entireProcessTree: true); }
                    catch (Exception e) when (e is InvalidOperationException || e is Win32Exception || e is NotSupportedException) { }
                    lock (gate) return new BootstrapCommandResult(true, true, -1, Joined(output, errors));
                }
                process.WaitForExit();
                lock (gate) return new BootstrapCommandResult(true, false, process.ExitCode, Joined(output, errors));
            }
        }

        /// <inheritdoc />
        public bool TcpOpen(string host, int port, int timeoutMs) => DevPostgresCheck.TcpOpen(host, port, timeoutMs);

        /// <inheritdoc />
        public bool TaskRegistered(string name)
        {
            if (!OperatingSystem.IsWindows()) return false;
            var state = AppHostTasks.Read(name).State;
            return state == "Enabled" || state == "Disabled";
        }

        /// <inheritdoc />
        public NightlyReportHttpResponse Get(string url, TimeSpan timeout)
        {
            using var deadline = new CancellationTokenSource(timeout);
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            using var response = Client.SendAsync(request, deadline.Token).GetAwaiter().GetResult();
            var body = response.Content.ReadAsStringAsync(deadline.Token).GetAwaiter().GetResult();
            return new NightlyReportHttpResponse((int)response.StatusCode, body ?? "");
        }

        /// <inheritdoc />
        public int VerifyDevStack(NightlyHost host) => _verifyDevStack(host);

        // Get-NativeOutput's 2>&1: the lines joined with "\n", trimmed (stdout first; the two streams are read apart).
        private static string Joined(List<string> output, List<string> errors) => string.Join("\n", output.Concat(errors)).Trim();
    }
}
