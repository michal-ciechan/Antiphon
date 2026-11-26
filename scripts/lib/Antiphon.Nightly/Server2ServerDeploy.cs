// The server2 host Antiphon servers (unit 64 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md, W8 slice C): the
// CARD-1775 production server and the CARD-1742 staging server. They replaced scripts/deploy-server2-server.ps1 and
// scripts/deploy-server2-staging-server.ps1, which nothing started by path (docs/docker-stack.md, the CARD-1744 runbooks and the
// compose files' comments told the operator to run them on the server2 host shell). The CLI runs them as
//
//   antiphon-scripts deploy-server2-server [--what-if | --execute] [--project antiphon-server2-server] [--env-file <file>]
//       [--compose-file <file>] [--repo-root <dir>] [--expected-server-sha <sha>]
//   antiphon-scripts deploy-server2-staging-server [--what-if | --execute] [--project antiphon-server2-staging] [--env-file <file>]
//       [--compose-file <file>] [--repo-root <dir>]
//
// (scripts/antiphon <command> on the server2 host).
//
// Production (Server2ServerDeploy): default is --what-if. It prints the host-daemon command and the verify-only gate. --what-if
// does not call docker. --execute starts the project only when this process is on the server2 host daemon (not inside the runner
// container, not a sibling daemon), then runs the restart-linux-server gate verify-only, in this process (LinuxServerRestart.Run,
// its output discarded as the script discarded its child's). Before up, the env SOURCE_REVISION and git rev-parse HEAD of the
// compose build context must both equal --expected-server-sha. The env file is untracked, mode 0600, and outside the repo. This
// command never prints the password or the phone-home secret. The compose runs only as project antiphon-server2-server.
//
// Staging (Server2StagingDeploy): default is --what-if. It prints the host-daemon command and the acceptance URLs. --what-if does
// not call docker. --execute starts the project only on the server2 host daemon. The env file is untracked, mode 0600, and
// outside the repo. It holds ANTIPHON_STAGING_POSTGRES_IMAGE and POSTGRES_PASSWORD. This command never prints the password. The
// image must be postgres:16@sha256:<64 hex> or postgres@sha256:<64 hex>. The compose runs only as project
// antiphon-server2-staging. Acceptance is http://127.0.0.1:17420/health and http://127.0.0.1:17420/api/version (curl).
//
// Exit codes, both: 0 plan or ok, 2 refusal, 3 ambiguous-mode (one SERVER2-SERVER or SERVER2-STAGING line); 1 an I/O failure the
// scripts did not survive (one stderr line).
//
// Every step, refusal, reason and line is the scripts', in their order (captured from them before their deletion), with
// PowerShell's semantics where they decide: -match is a case-insensitive regex, -eq and -ne ignore case, -ceq and -cne do not,
// .Contains is ordinal; the env file's keys ignore case and a later key wins (Server2PostgresEnv, the same reader); a file that
// exists but cannot be read reads as empty after one stderr line (the script's .NET call failed with an error record and went
// on), and so does a gate config that cannot be written; the POSIX mode check refuses whenever OS is Windows_NT. docker,
// hostname, git and curl are found as Get-Command -CommandType Application found them (Resolve; the CLI binds OperatorToolOs.Find)
// and run on the host's launcher with their output captured and not printed. Files are IFileSystem's, the clock and the waits
// the host's. Plain C#: no PowerShell types. ASCII only.
#nullable disable
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.IO.Abstractions;
using System.Text.RegularExpressions;

namespace Antiphon.Nightly
{
    /// <summary>The deploy-server2-server command's options (deploy-server2-server.ps1's parameters in kebab case).</summary>
    public sealed class Server2ServerDeployOptions
    {
        /// <summary>The CLI's usage line.</summary>
        public const string Usage = "deploy-server2-server [--what-if | --execute] [--project " + Server2ServerDeploy.Project + "] [--env-file <file>]"
            + " [--compose-file <file>] [--repo-root <dir>] [--expected-server-sha <sha>]";

        /// <summary>--what-if: the plan (also the default without --execute).</summary>
        public bool WhatIf { get; init; }

        /// <summary>--execute: start the project on the server2 host daemon, then the verify-only gate.</summary>
        public bool Execute { get; init; }

        /// <summary>--project: the compose project (blank: antiphon-server2-server).</summary>
        public string Project { get; init; } = "";

        /// <summary>--env-file: the untracked mode-0600 env file outside the repo.</summary>
        public string EnvFile { get; init; } = "";

        /// <summary>--compose-file (blank: the repo's docker-compose.server2-server.yml).</summary>
        public string ComposeFile { get; init; } = "";

        /// <summary>--repo-root (blank: Checkout).</summary>
        public string RepoRoot { get; init; } = "";

        /// <summary>--expected-server-sha: the full SHA the env, the build context and the running server must carry.</summary>
        public string ExpectedServerSha { get; init; } = "";

        /// <summary>The checkout this command belongs to (the script's parent directory); the default repo root.</summary>
        public string Checkout { get; init; } = "";

        /// <summary>The flags after the command name; an unknown or valueless flag is a usage error. Values are kept as given.</summary>
        public static Server2ServerDeployOptions Parse(IReadOnlyList<string> arguments, string checkout)
        {
            var flags = NightlyArgs.Parse(arguments, new[] { "--project", "--env-file", "--compose-file", "--repo-root", "--expected-server-sha" },
                new[] { "--what-if", "--execute" });
            return new Server2ServerDeployOptions
            {
                WhatIf = flags.Switch("--what-if"),
                Execute = flags.Switch("--execute"),
                Project = flags.Value("--project", ""),
                EnvFile = flags.Value("--env-file", ""),
                ComposeFile = flags.Value("--compose-file", ""),
                RepoRoot = flags.Value("--repo-root", ""),
                ExpectedServerSha = flags.Value("--expected-server-sha", ""),
                Checkout = checkout ?? "",
            };
        }
    }

    /// <summary>The deploy-server2-staging-server command's options (deploy-server2-staging-server.ps1's parameters in kebab case).</summary>
    public sealed class Server2StagingDeployOptions
    {
        /// <summary>The CLI's usage line.</summary>
        public const string Usage = "deploy-server2-staging-server [--what-if | --execute] [--project " + Server2StagingDeploy.Project + "] [--env-file <file>]"
            + " [--compose-file <file>] [--repo-root <dir>]";

        /// <summary>--what-if: the plan (also the default without --execute).</summary>
        public bool WhatIf { get; init; }

        /// <summary>--execute: start the project on the server2 host daemon, then the acceptance.</summary>
        public bool Execute { get; init; }

        /// <summary>--project: the compose project (blank: antiphon-server2-staging).</summary>
        public string Project { get; init; } = "";

        /// <summary>--env-file: the untracked mode-0600 env file outside the repo.</summary>
        public string EnvFile { get; init; } = "";

        /// <summary>--compose-file (blank: the repo's docker-compose.server2-staging-server.yml).</summary>
        public string ComposeFile { get; init; } = "";

        /// <summary>--repo-root (blank: Checkout).</summary>
        public string RepoRoot { get; init; } = "";

        /// <summary>The checkout this command belongs to (the script's parent directory); the default repo root.</summary>
        public string Checkout { get; init; } = "";

        /// <summary>The flags after the command name; an unknown or valueless flag is a usage error. Values are kept as given.</summary>
        public static Server2StagingDeployOptions Parse(IReadOnlyList<string> arguments, string checkout)
        {
            var flags = NightlyArgs.Parse(arguments, new[] { "--project", "--env-file", "--compose-file", "--repo-root" }, new[] { "--what-if", "--execute" });
            return new Server2StagingDeployOptions
            {
                WhatIf = flags.Switch("--what-if"),
                Execute = flags.Switch("--execute"),
                Project = flags.Value("--project", ""),
                EnvFile = flags.Value("--env-file", ""),
                ComposeFile = flags.Value("--compose-file", ""),
                RepoRoot = flags.Value("--repo-root", ""),
                Checkout = checkout ?? "",
            };
        }
    }

    /// <summary>What the server2 server deploys observe outside their files, clock and launcher.</summary>
    public sealed class Server2ServerDeploySeams
    {
        /// <summary>
        /// Get-Command &lt;name&gt; -CommandType Application for docker, hostname, git and curl: the path, null when there is none.
        /// Null: OperatorToolOs.Find over the host's PATH and PATHEXT.
        /// </summary>
        public Func<string, string> Resolve { get; init; }

        /// <summary>
        /// [System.IO.File]::GetUnixFileMode(path) equals UserRead|UserWrite (asked only when OS is not Windows_NT); a mode that
        /// cannot be read is false. Default: this machine's.
        /// </summary>
        public Func<string, bool> OwnerReadWriteOnly { get; init; } = Server2PostgresDeploy.IsOwnerReadWriteOnly;

        /// <summary>
        /// [System.IO.File]::SetUnixFileMode(path, UserRead|UserWrite) on the production gate config (called only when OS is not
        /// Windows_NT); throws as the .NET call did. Default: this machine's.
        /// </summary>
        public Action<string> SetOwnerReadWrite { get; init; } = Server2HostCommon.SetOwnerReadWrite;

        /// <summary>The production gate's HTTP (LinuxServerRestart.Http() in the CLI). Required for production --execute.</summary>
        public LinuxServerHttp GateHttp { get; init; }
    }

    /// <summary>The pieces both server2 server deploys share with each other (and with Server2PostgresDeploy's functions).</summary>
    internal static class Server2HostCommon
    {
        public const string DockerEnv = "/.dockerenv";

        private static readonly Regex DevPasswordLine = new Regex(@"POSTGRES_PASSWORD:\s*(\S+)");

        public static void SetOwnerReadWrite(string path)
        {
            if (OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Unix file modes are not supported on Windows.");
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }

        // PowerShell's -eq and -ne on strings.
        public static bool PsEquals(string left, string right) => LinuxServerRestart.PsEquals(left, right);

        // Test-Path -LiteralPath: a file or a directory.
        public static bool Exists(IFileSystem files, string path) => files.File.Exists(path) || files.Directory.Exists(path);

        // The POSIX mode check: never on Windows ($env:OS -eq 'Windows_NT'), else the bits are exactly 0600.
        public static bool Mode0600(NightlyHost host, Server2ServerDeploySeams seams, string path)
        {
            if (PsEquals(host.Variables("OS"), "Windows_NT")) return false;
            return (seams.OwnerReadWriteOnly ?? Server2PostgresDeploy.IsOwnerReadWriteOnly)(path);
        }

        // [System.IO.File]::ReadAllText: a file the script could not read failed with an error record and read as empty.
        public static string ReadText(IFileSystem files, NightlyHost host, string path)
        {
            try
            {
                return files.File.ReadAllText(path);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                host.Error.WriteLine(e.Message);
                return "";
            }
        }

        // Read-*Env's [System.IO.File]::ReadAllLines, with ReadText's failure rule.
        public static Server2PostgresEnv ReadEnv(IFileSystem files, NightlyHost host, string path)
        {
            try
            {
                return Server2PostgresEnv.Parse(files.File.ReadAllLines(path));
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                host.Error.WriteLine(e.Message);
                return Server2PostgresEnv.Parse(Array.Empty<string>());
            }
        }

        // Get-*DevPassword: docker-compose.dev.yml's first POSTGRES_PASSWORD value under the root; '' without one.
        public static string DevPassword(IFileSystem files, NightlyHost host, string root)
        {
            var path = files.Path.Combine(root, "docker-compose.dev.yml");
            if (!Exists(files, path)) return "";
            var match = DevPasswordLine.Match(ReadText(files, host, path));
            return match.Success ? match.Groups[1].Value : "";
        }

        // Get-*ObservedLane: refuses inside a container before any docker call; then docker info's daemon name and hostname.
        public static string ObservedLane(NightlyHost host, IFileSystem files, Func<string, string> resolve)
        {
            if (Exists(files, DockerEnv)) return "nested";
            var docker = resolve("docker");
            if (docker == null) return "none";
            var daemon = Capture(host, docker, "info", "--format", "{{.Name}}").Text.Trim();
            var hostname = resolve("hostname");
            string own;
            if (hostname == null)
            {
                // The call operator's error record (the script went on with an empty name).
                host.Error.WriteLine("The term 'hostname' is not recognized as a name of a cmdlet, function, script file, or executable program.");
                own = "";
            }
            else own = Capture(host, hostname).Text.Trim();
            return Server2PostgresDeploy.Lane(daemon, own, Exists(files, DockerEnv));
        }

        // `& tool @arguments 2>$null | Out-String`: the tool's stdout and exit code (stderr discarded); a tool that cannot start is
        // its error line, no output and no exit code.
        public static (string Text, int? Exit) Capture(NightlyHost host, string tool, params string[] arguments)
        {
            try
            {
                var result = host.Launch(new NightlyLaunch(tool, arguments, "", CaptureOutput: true));
                return (result.StandardOutputText ?? "", result.ExitCode);
            }
            catch (Exception e) when (e is Win32Exception || e is InvalidOperationException || e is FileNotFoundException || e is UnauthorizedAccessException)
            {
                host.Error.WriteLine(e.Message);
                return ("", null);
            }
        }

        // A child whose output is redirected and not printed (compose up, curl): its exit code; one that cannot start is null after
        // its error line (the scripts' failed Start fell through to their next statement: the commands count it as a failure).
        public static int? Quiet(NightlyHost host, string tool, params string[] arguments) => Capture(host, tool, arguments).Exit;
    }

    /// <summary>The CARD-1775 server2 host production server (deploy-server2-server.ps1; see the file comment).</summary>
    public static class Server2ServerDeploy
    {
        /// <summary>The compose project.</summary>
        public const string Project = "antiphon-server2-server";

        /// <summary>The compose file's name in the repo.</summary>
        public const string ComposeName = "docker-compose.server2-server.yml";

        /// <summary>The serving container the gate names.</summary>
        public const string Container = "antiphon-server2-server-server-1";

        /// <summary>The gate's base URL.</summary>
        public const string GateBaseUrl = "http://127.0.0.1:17400";

        /// <summary>The gate config the deploy writes.</summary>
        public const string GateConfig = "/var/tmp/antiphon-server2-server.gate.config";

        /// <summary>The gate's lock.</summary>
        public const string GateLock = "/var/tmp/antiphon-server2-server.restart.lock";

        /// <summary>The bound on the gate's health-down retries, from the first call.</summary>
        public static readonly TimeSpan GateBound = TimeSpan.FromSeconds(60);

        /// <summary>The wait between the gate's health-down retries.</summary>
        public static readonly TimeSpan GateRetry = TimeSpan.FromSeconds(2);

        /// <summary>The desktop-win phone-home keys the env file must hold whenever it is given.</summary>
        public static readonly IReadOnlyList<string> DesktopWinEnvKeys = new[] { "PHONE_HOME_DESKTOP_WIN_SHARED_SECRET", "PHONE_HOME_DESKTOP_WIN_HOME" };

        /// <summary>The keys --execute requires besides the desktop-win ones, in the script's order.</summary>
        public static readonly IReadOnlyList<string> ExecuteEnvKeys = new[]
        {
            "ANTIPHON_PUBLIC_ORIGIN", "PHONE_HOME_SHARED_SECRET", "PHONE_HOME_HOST_WORKSPACE_ROOT", "KEYRING_CERT_FILE", "KEYRING_KEY_FILE", "OPERATOR_TOKEN_FILE",
        };

        /// <summary>The literals the compose must hold (Get-ServerComposeRefusal's list, in its order).</summary>
        public static readonly IReadOnlyList<string> ComposeNeeds = new[]
        {
            "name: antiphon-server2-server",
            "external: true",
            "name: antiphon-server2",
            "name: antiphon-server2-server_work",
            "name: antiphon-server2-server_server-state",
            "name: antiphon-server2-server_runner-state",
            "127.0.0.1:17400:8080",
            "Host=postgres;Port=5432",
            "  server-session-runner:",
            "SessionRunner__Herdr__Enabled: \"false\"",
            "PhoneHome__Enabled: \"false\"",
            "SessionRunner__Enabled: \"true\"",
            "SessionRunner__BaseUrl: http://server-session-runner:8080",
            "Hangfire__ServerEnabled: \"true\"",
            "Hangfire__Storage: \"${HANGFIRE_STORAGE:-InMemory}\"",
            "ZombieCensus__Enabled: \"false\"",
            "WorktreeResidue__Enabled: \"true\"",
            "WorktreeResidue__Execute: \"false\"",
            "WorktreeLegacyRelease__Enabled: \"false\"",
            "WorktreeLegacyRelease__Execute: \"false\"",
            "RunnerWorktreeRetention__Enabled: \"true\"",
            "RunnerWorktreeRetention__Execute: \"true\"",
            "Schedules__Enabled: \"true\"",
            "ChannelBridge__Enabled: \"${CHANNEL_BRIDGE_ENABLED:-false}\"",
            "Digest__Enabled: \"false\"",
            "AntiphonMessaging__BootstrapServers: \"${ANTIPHON_MESSAGING_BOOTSTRAP_SERVERS:-}\"",
            "AntiphonMessaging__ClientId: \"antiphon-server2-server\"",
            "AntiphonMessaging__ExpectedClusterId: \"${ANTIPHON_MESSAGING_EXPECTED_CLUSTER_ID:-}\"",
            "PhoneHomeRunner__Enabled: \"true\"",
            "PhoneHomeRunner__Runners__desktop-win__Enabled: \"true\"",
            "PhoneHomeRunner__Runners__desktop-win__AllowDelegatedTasks: \"true\"",
            "PHONE_HOME_DESKTOP_WIN_SHARED_SECRET:?PHONE_HOME_DESKTOP_WIN_SHARED_SECRET is required",
            "PHONE_HOME_DESKTOP_WIN_HOME:?PHONE_HOME_DESKTOP_WIN_HOME is required",
            "PhoneHomeRunner__Runners__desktop-win__RunnerWorkspace: C:/Antiphon/runner/work",
            "PhoneHomeRunner__Runners__desktop-win__RawExeAllowList__0: \"C:/Program Files/PowerShell/7/pwsh.exe\"",
            "HostLoadHistory__DirectRunnerKey: server2-direct",
            "Delegation__CheckInterpreterEnabled: \"true\"",
            "Delegation__DiagnoseEnabled: \"true\"",
            "Delegation__OutputDistillerEnabled: \"true\"",
            "Delegation__CheckInterpreterWorkingDirectory: /work/specialists/check-interpreter",
            "Delegation__DiagnoseWorkingDirectory: /work/specialists/diagnose",
            "Delegation__OutputDistillerWorkingDirectory: /work/specialists/output-distiller",
            "SessionRunner__VerifyExecutablesOnServer: \"false\"",
            "Delegation__OrchestratorInstructions__Path: /work/.antiphon/orchestrator-instructions.md",
            "CLAUDE_CONFIG_DIR: /state/claude",
            "AgentTui__KeyProtection__Mode: X509Certificate",
            "Operator__TokenPath: /state/operator-token",
            "runner-state:/state",
            "runner-state:/runner-state:ro",
            "user: \"1654:1654\"",
            "KEYRING_CERT_FILE:?KEYRING_CERT_FILE is required",
            "chmod 0600",
            "ForeignStateOwner",
            "InMemory",
        };

        // -notmatch '(?m)^name: antiphon-server2-server$': ignoring case; a CRLF file has no such line.
        private static readonly Regex ProjectLine = new Regex("(?m)^name: antiphon-server2-server$", RegexOptions.IgnoreCase);

        private sealed class DeployStop : Exception
        {
            public DeployStop(int code) => Code = code;

            public int Code { get; }
        }

        /// <summary>Get-ServerComposeRefusal: the first forbidden literal's reason, compose-invalid for a missing need, '' when it passes (all ordinal).</summary>
        public static string ComposeRefusal(string text)
        {
            text ??= "";
            if (text.Contains("antiphon_pgdata", StringComparison.Ordinal)) return "volume-forbidden";
            if (text.Contains("dind-data", StringComparison.Ordinal)) return "volume-forbidden";
            if (text.Contains("windmill", StringComparison.Ordinal)) return "volume-forbidden";
            if (text.Contains("0.0.0.0", StringComparison.Ordinal)) return "bind-any";
            if (text.Contains("17420", StringComparison.Ordinal)) return "port-forbidden";
            if (text.Contains("17280", StringComparison.Ordinal)) return "port-forbidden";
            if (text.Contains("17202", StringComparison.Ordinal)) return "port-forbidden";
            if (text.Contains("5433", StringComparison.Ordinal)) return "port-forbidden";
            if (text.Contains(":5432:", StringComparison.Ordinal)) return "port-forbidden";
            // The public origin, spelled in two parts so this file does not name it.
            if (text.Contains("codeperf." + "net", StringComparison.Ordinal)) return "phone-home-origin";
            if (text.Contains("privileged:", StringComparison.Ordinal)) return "privileged";
            if (text.Contains("docker.sock", StringComparison.Ordinal)) return "socket";
            foreach (var need in ComposeNeeds)
                if (!text.Contains(need, StringComparison.Ordinal)) return "compose-invalid";
            return "";
        }

        /// <summary>Get-ServerBuildContext: the compose file's directory (the build context '.'), from its full path.</summary>
        public static string BuildContext(IFileSystem files, string composePath)
        {
            if (files == null) throw new ArgumentNullException(nameof(files));
            return files.Path.GetDirectoryName(files.Path.GetFullPath(composePath)) ?? "";
        }

        /// <summary>
        /// Get-ServerHostCommand: the revision rule, the host-daemon command and the verify-only gate, joined by "\n" (one line of
        /// output, as Write-Output printed it). The gate is now the restart-linux-server command.
        /// </summary>
        public static string HostCommand(IFileSystem files, string envPath, string composePath, string sha)
        {
            var envArg = string.IsNullOrWhiteSpace(envPath) ? "<env-file>" : envPath;
            var shaArg = string.IsNullOrWhiteSpace(sha) ? "<expected-sha>" : sha;
            var revision = "before up: SOURCE_REVISION in the env file and git rev-parse HEAD of " + BuildContext(files, composePath) + " must equal " + shaArg;
            var composeUp = "docker compose --env-file " + envArg + " -f " + composePath + " -p " + Project + " up -d --build";
            var gate = "verify-only: scripts/antiphon restart-linux-server --config " + GateConfig + " --expected-server-sha " + shaArg;
            return revision + "\n" + composeUp + "\n" + gate;
        }

        /// <summary>The gate config's text: baseUrl, container and lockPath, each line ending "\n".</summary>
        public static string GateConfigText => "baseUrl=" + GateBaseUrl + "\n" + "container=" + Container + "\n" + "lockPath=" + GateLock + "\n";

        /// <summary>
        /// Runs the command: the plan (the default and --what-if), or --execute on the server2 host daemon. Every outcome is one
        /// "SERVER2-SERVER &lt;plan|ok|fail&gt; exit=&lt;code&gt; reason=&lt;reason&gt;" line on host.Out, after the plan; the exit code is the
        /// line's. An I/O failure the script did not survive is one host.Error line, exit 1.
        /// </summary>
        public static int Run(Server2ServerDeployOptions options, NightlyHost host, IFileSystem files, Server2ServerDeploySeams seams = null)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (host == null) throw new ArgumentNullException(nameof(host));
            if (files == null) throw new ArgumentNullException(nameof(files));
            seams ??= new Server2ServerDeploySeams();
            var resolve = seams.Resolve ?? new OperatorToolOs(host.Variables).Find;
            try
            {
                return Deploy(options, host, files, seams, resolve);
            }
            catch (DeployStop stop)
            {
                return stop.Code;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException || e is NotSupportedException
                || e is System.Security.SecurityException)
            {
                host.Error.WriteLine(e.Message);
                return 1;
            }
        }

        private static int Result(NightlyHost host, string kind, int code, string reason)
        {
            host.Out.WriteLine("SERVER2-SERVER " + kind + " exit=" + code.ToString(CultureInfo.InvariantCulture) + " reason=" + reason);
            return code;
        }

        private static void Fail(NightlyHost host, string reason, int code = 2)
        {
            Result(host, "fail", code, reason);
            throw new DeployStop(code);
        }

        private static int Deploy(Server2ServerDeployOptions options, NightlyHost host, IFileSystem files, Server2ServerDeploySeams seams, Func<string, string> resolve)
        {
            if (options.WhatIf && options.Execute) Fail(host, "ambiguous-mode", 3);
            var planOnly = !options.Execute;
            if (options.WhatIf) planOnly = true;
            var selected = Project;
            if (!string.IsNullOrWhiteSpace(options.Project)) selected = options.Project.Trim();
            if (!string.Equals(selected, Project, StringComparison.Ordinal)) Fail(host, "production-compose-wrong-project");
            var root = string.IsNullOrWhiteSpace(options.RepoRoot) ? options.Checkout ?? "" : options.RepoRoot;
            var composePath = options.ComposeFile ?? "";
            if (string.IsNullOrWhiteSpace(composePath)) composePath = files.Path.Combine(root, ComposeName);
            if (!Server2HostCommon.Exists(files, composePath)) Fail(host, "missing-compose");
            var composeText = Server2HostCommon.ReadText(files, host, composePath);
            if (!ProjectLine.IsMatch(composeText)) Fail(host, "production-compose-wrong-project");
            var composeReason = ComposeRefusal(composeText);
            if (composeReason.Length > 0) Fail(host, composeReason);

            var envFile = options.EnvFile ?? "";
            Server2PostgresEnv env = null;
            if (!string.IsNullOrWhiteSpace(envFile))
            {
                if (!Server2HostCommon.Exists(files, envFile)) Fail(host, "env-missing");
                if (Server2PostgresDeploy.InsideRepo(files, envFile, root)) Fail(host, "env-inside-repo");
                if (!Server2HostCommon.Mode0600(host, seams, envFile)) Fail(host, "env-mode");
                env = Server2HostCommon.ReadEnv(files, host, envFile);
                var password = env.Value("POSTGRES_PASSWORD");
                if (string.IsNullOrWhiteSpace(password)) Fail(host, "empty-password");
                var dev = Server2HostCommon.DevPassword(files, host, root);
                if (!string.IsNullOrWhiteSpace(dev) && string.Equals(password, dev, StringComparison.Ordinal)) Fail(host, "dev-password");
                var storage = env.Value("HANGFIRE_STORAGE");
                if (!string.IsNullOrWhiteSpace(storage) && !string.Equals(storage, "InMemory", StringComparison.Ordinal)
                    && !string.Equals(storage, "PostgreSql", StringComparison.Ordinal)) Fail(host, "hangfire-storage");
                foreach (var key in DesktopWinEnvKeys)
                    if (string.IsNullOrWhiteSpace(env.Value(key))) Fail(host, "env-missing");
            }
            else if (!planOnly) Fail(host, "env-missing");

            var sha = options.ExpectedServerSha ?? "";
            if (planOnly)
            {
                host.Out.WriteLine(HostCommand(files, envFile, composePath, sha));
                return Result(host, "plan", 0, "ok");
            }

            foreach (var key in ExecuteEnvKeys)
                if (string.IsNullOrWhiteSpace(env.Value(key))) Fail(host, "env-missing");
            foreach (var key in DesktopWinEnvKeys)
                if (string.IsNullOrWhiteSpace(env.Value(key))) Fail(host, "env-missing");
            foreach (var key in new[] { "KEYRING_CERT_FILE", "KEYRING_KEY_FILE", "OPERATOR_TOKEN_FILE" })
                if (!Server2HostCommon.Exists(files, env.Value(key))) Fail(host, "missing-certificate");
            if (!LinuxServerRestart.IsSha(sha)) Fail(host, "expected-sha");
            var revision = env.Value("SOURCE_REVISION").Trim().ToLowerInvariant();
            if (!string.Equals(revision, sha.ToLowerInvariant(), StringComparison.Ordinal)) Fail(host, "revision-mismatch");
            // The env names the revision; the build context is what up --build compiles. Both must be the expected SHA before any
            // docker call.
            var head = CheckoutHead(host, resolve, BuildContext(files, composePath));
            if (head.Length == 0) Fail(host, "head-unknown");
            if (!string.Equals(head, sha.ToLowerInvariant(), StringComparison.Ordinal)) Fail(host, "head-mismatch");
            if (Server2HostCommon.ObservedLane(host, files, resolve) != "host") Fail(host, "not-server2-host");
            var up = Up(host, resolve, envFile, composePath);
            if (up.Length > 0) Fail(host, up);
            var gated = Gate(host, files, seams, sha);
            if (gated.Length > 0) Fail(host, gated);
            return Result(host, "ok", 0, "ok");
        }

        // Get-ServerCheckoutHead: git -C <context> rev-parse --verify HEAD (stderr discarded): the lower-cased full SHA, '' otherwise.
        private static string CheckoutHead(NightlyHost host, Func<string, string> resolve, string context)
        {
            var git = resolve("git");
            if (git == null) return "";
            var (text, exit) = Server2HostCommon.Capture(host, git, "-C", context, "rev-parse", "--verify", "HEAD");
            if (exit != 0) return "";
            var head = text.Trim();
            if (!LinuxServerRestart.IsSha(head)) return "";
            return head.ToLowerInvariant();
        }

        // Invoke-ServerUp: docker compose ... up -d --build; both streams drained and not printed (compose output can repeat an
        // interpolated secret).
        private static string Up(NightlyHost host, Func<string, string> resolve, string envPath, string composePath)
        {
            var docker = resolve("docker");
            if (docker == null) return "missing-docker";
            var exit = Server2HostCommon.Quiet(host, docker, "compose", "--env-file", envPath, "-f", composePath, "-p", Project, "up", "-d", "--build");
            return exit != 0 ? "compose-up-failed" : "";
        }

        // Invoke-ServerGate: write the gate config (mode 0600 off Windows), then the restart-linux-server gate verify-only (no
        // --execute) with its output discarded: 0 is ok, 1 (health down) is retried every 2 s until 60 s from the first call, any
        // other exit is gate-unverified.
        private static string Gate(NightlyHost host, IFileSystem files, Server2ServerDeploySeams seams, string sha)
        {
            try
            {
                files.File.WriteAllText(GateConfig, GateConfigText);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                host.Error.WriteLine(e.Message);
            }
            if (!Server2HostCommon.PsEquals(host.Variables("OS"), "Windows_NT"))
            {
                try
                {
                    (seams.SetOwnerReadWrite ?? Server2HostCommon.SetOwnerReadWrite)(GateConfig);
                }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is PlatformNotSupportedException || e is ArgumentException)
                {
                    host.Error.WriteLine(e.Message);
                }
            }
            if (seams.GateHttp == null) throw new ArgumentNullException(nameof(seams), "the gate's HTTP seam is required");
            var quiet = new NightlyHost
            {
                Clock = host.Clock,
                Git = host.Git,
                Launcher = host.Launcher,
                Processes = host.Processes,
                Probe = host.Probe,
                Out = TextWriter.Null,
                Error = TextWriter.Null,
                Trace = host.Trace,
                Variables = host.Variables,
                UserVariables = host.UserVariables,
                Sleep = host.Sleep,
                Qualification = host.Qualification,
            };
            var gate = new LinuxServerRestartOptions { Config = GateConfig, ExpectedServerSha = sha };
            var gateSeams = new LinuxServerRestartSeams { Http = seams.GateHttp, Resolve = seams.Resolve };
            var deadline = host.UtcNow.Add(GateBound);
            while (true)
            {
                var exit = LinuxServerRestart.Run(gate, quiet, files, gateSeams);
                if (exit == 0) return "";
                if (exit != 1) return "gate-unverified";
                if (host.UtcNow >= deadline) return "gate-unverified";
                host.Sleep(GateRetry);
            }
        }
    }

    /// <summary>The CARD-1742 server2 host staging server (deploy-server2-staging-server.ps1; see the file comment).</summary>
    public static class Server2StagingDeploy
    {
        /// <summary>The compose project.</summary>
        public const string Project = "antiphon-server2-staging";

        /// <summary>The compose file's name in the repo.</summary>
        public const string ComposeName = "docker-compose.server2-staging-server.yml";

        /// <summary>The acceptance health URL.</summary>
        public const string HealthUrl = "http://127.0.0.1:17420/health";

        /// <summary>The acceptance version URL.</summary>
        public const string VersionUrl = "http://127.0.0.1:17420/api/version";

        /// <summary>The bound on the health retries, from the first call.</summary>
        public static readonly TimeSpan AcceptanceBound = TimeSpan.FromSeconds(60);

        /// <summary>The wait between the health retries.</summary>
        public static readonly TimeSpan AcceptanceRetry = TimeSpan.FromSeconds(2);

        /// <summary>The literals the compose must hold (Get-StagingComposeRefusal's list, in its order).</summary>
        public static readonly IReadOnlyList<string> ComposeNeeds = new[]
        {
            "name: antiphon-server2-staging",
            "name: antiphon-server2-staging_pgdata",
            "name: antiphon-server2-staging_server-state",
            "name: antiphon-server2-staging_work",
            "127.0.0.1:17420:8080",
            "Hangfire__ServerEnabled: \"true\"",
            "ZombieCensus__Enabled: \"false\"",
            "Schedules__Enabled: \"true\"",
            "Delegation__CheckInterpreterEnabled: \"true\"",
            "Delegation__DiagnoseEnabled: \"true\"",
            "Delegation__OutputDistillerEnabled: \"true\"",
            "Delegation__CheckInterpreterWorkingDirectory: /state/check-interpreter",
            "Delegation__DiagnoseWorkingDirectory: /state/diagnose",
            "Delegation__OutputDistillerWorkingDirectory: /state/output-distiller",
            "WorktreeResidue__Enabled: \"true\"",
            "WorktreeResidue__Execute: \"false\"",
            "WorktreeLegacyRelease__Enabled: \"false\"",
            "WorktreeLegacyRelease__Execute: \"false\"",
            "RunnerWorktreeRetention__Enabled: \"false\"",
            "RunnerWorktreeRetention__Execute: \"false\"",
            "ChannelBridge__Enabled: \"false\"",
            "Digest__Enabled: \"false\"",
            "SessionRunner__Enabled: \"false\"",
            "PhoneHomeRunner__Enabled: \"false\"",
            "AgentTui__KeyRingPath: /state/keyring",
            "Git__WorktreeBasePath: /work/worktrees",
            "Git__WorkspacePath: /work/repos",
            "Host=staging-postgres",
            "ANTIPHON_STAGING_POSTGRES_IMAGE:?",
            "POSTGRES_PASSWORD:?",
            "shared_preload_libraries=pg_stat_statements",
            "http://127.0.0.1:8080/health",
        };

        // PowerShell's -match: a case-insensitive regex.
        private static readonly Regex ProjectLine = new Regex("(?m)^name: antiphon-server2-staging$", RegexOptions.IgnoreCase);
        private static readonly Regex Alpine = new Regex("(?i)alpine", RegexOptions.IgnoreCase);
        private static readonly Regex DigestTag = new Regex("^postgres:16@sha256:[0-9a-f]{64}$", RegexOptions.IgnoreCase);
        private static readonly Regex RepoDigest = new Regex("^postgres@sha256:[0-9a-f]{64}$", RegexOptions.IgnoreCase);

        private static readonly (Regex Pattern, string Reason)[] Forbidden =
        {
            (new Regex("antiphon_pgdata", RegexOptions.IgnoreCase), "volume-forbidden"),
            (new Regex("dind-data", RegexOptions.IgnoreCase), "volume-forbidden"),
            (new Regex("antiphon-server2-postgres", RegexOptions.IgnoreCase), "volume-forbidden"),
            (new Regex("(?i)windmill", RegexOptions.IgnoreCase), "volume-forbidden"),
            (new Regex(@"0\.0\.0\.0", RegexOptions.IgnoreCase), "bind-any"),
            (new Regex("5433", RegexOptions.IgnoreCase), "port-forbidden"),
            (new Regex("17280", RegexOptions.IgnoreCase), "port-forbidden"),
            (new Regex("17202", RegexOptions.IgnoreCase), "port-forbidden"),
            (new Regex(":5432:", RegexOptions.IgnoreCase), "port-forbidden"),
            (new Regex("PhoneHome__ServerOrigin", RegexOptions.IgnoreCase), "phone-home-origin"),
            // The public origin, spelled in two parts so this file does not name it.
            (new Regex("codeperf" + @"\.net", RegexOptions.IgnoreCase), "phone-home-origin"),
        };

        private sealed class DeployStop : Exception
        {
            public DeployStop(int code) => Code = code;

            public int Code { get; }
        }

        /// <summary>Test-StagingImage: '' for postgres:16@sha256:&lt;64 hex&gt; or postgres@sha256:&lt;64 hex&gt; (ignoring case), else the refusal.</summary>
        public static string Image(string image)
        {
            if (string.IsNullOrWhiteSpace(image)) return "empty-image";
            if (Alpine.IsMatch(image)) return "image-alpine";
            if (DigestTag.IsMatch(image)) return "";
            if (RepoDigest.IsMatch(image)) return "";
            return "image-floating";
        }

        /// <summary>Get-StagingComposeRefusal: the first forbidden pattern's reason (ignoring case), compose-invalid for a missing need (ordinal), '' when it passes.</summary>
        public static string ComposeRefusal(string text)
        {
            text ??= "";
            foreach (var (pattern, reason) in Forbidden)
                if (pattern.IsMatch(text)) return reason;
            foreach (var need in ComposeNeeds)
                if (!text.Contains(need, StringComparison.Ordinal)) return "compose-invalid";
            return "";
        }

        /// <summary>Get-StagingHostCommand: the host-daemon command and the two acceptance lines, joined by "\n" (one line of output).</summary>
        public static string HostCommand(string envPath, string composePath)
        {
            var envArg = string.IsNullOrWhiteSpace(envPath) ? "<env-file>" : envPath;
            return "docker compose --env-file " + envArg + " -f " + composePath + " -p " + Project + " up -d"
                + "\n" + "acceptance: curl -fsS " + HealthUrl
                + "\n" + "acceptance: curl -fsS " + VersionUrl;
        }

        /// <summary>
        /// Runs the command: the plan (the default and --what-if), or --execute on the server2 host daemon. Every outcome is one
        /// "SERVER2-STAGING &lt;plan|ok|fail&gt; exit=&lt;code&gt; reason=&lt;reason&gt;" line on host.Out, after the plan; the exit code is the
        /// line's. An I/O failure the script did not survive is one host.Error line, exit 1.
        /// </summary>
        public static int Run(Server2StagingDeployOptions options, NightlyHost host, IFileSystem files, Server2ServerDeploySeams seams = null)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (host == null) throw new ArgumentNullException(nameof(host));
            if (files == null) throw new ArgumentNullException(nameof(files));
            seams ??= new Server2ServerDeploySeams();
            var resolve = seams.Resolve ?? new OperatorToolOs(host.Variables).Find;
            try
            {
                return Deploy(options, host, files, seams, resolve);
            }
            catch (DeployStop stop)
            {
                return stop.Code;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException || e is NotSupportedException
                || e is System.Security.SecurityException)
            {
                host.Error.WriteLine(e.Message);
                return 1;
            }
        }

        private static int Result(NightlyHost host, string kind, int code, string reason)
        {
            host.Out.WriteLine("SERVER2-STAGING " + kind + " exit=" + code.ToString(CultureInfo.InvariantCulture) + " reason=" + reason);
            return code;
        }

        private static void Fail(NightlyHost host, string reason, int code = 2)
        {
            Result(host, "fail", code, reason);
            throw new DeployStop(code);
        }

        private static int Deploy(Server2StagingDeployOptions options, NightlyHost host, IFileSystem files, Server2ServerDeploySeams seams, Func<string, string> resolve)
        {
            if (options.WhatIf && options.Execute) Fail(host, "ambiguous-mode", 3);
            var planOnly = !options.Execute;
            if (options.WhatIf) planOnly = true;
            var selected = Project;
            if (!string.IsNullOrWhiteSpace(options.Project)) selected = options.Project.Trim();
            if (!string.Equals(selected, Project, StringComparison.Ordinal)) Fail(host, "staging-compose-wrong-project");
            var root = string.IsNullOrWhiteSpace(options.RepoRoot) ? options.Checkout ?? "" : options.RepoRoot;
            var composePath = options.ComposeFile ?? "";
            if (string.IsNullOrWhiteSpace(composePath)) composePath = files.Path.Combine(root, ComposeName);
            if (!Server2HostCommon.Exists(files, composePath)) Fail(host, "missing-compose");
            var composeText = Server2HostCommon.ReadText(files, host, composePath);
            if (!ProjectLine.IsMatch(composeText)) Fail(host, "staging-compose-wrong-project");
            var composeReason = ComposeRefusal(composeText);
            if (composeReason.Length > 0) Fail(host, composeReason);

            var envFile = options.EnvFile ?? "";
            if (!string.IsNullOrWhiteSpace(envFile))
            {
                if (!Server2HostCommon.Exists(files, envFile)) Fail(host, "env-missing");
                if (Server2PostgresDeploy.InsideRepo(files, envFile, root)) Fail(host, "env-inside-repo");
                if (!Server2HostCommon.Mode0600(host, seams, envFile)) Fail(host, "env-mode");
                var env = Server2HostCommon.ReadEnv(files, host, envFile);
                var imageReason = Image(env.Value("ANTIPHON_STAGING_POSTGRES_IMAGE"));
                if (imageReason.Length > 0) Fail(host, imageReason);
                var password = env.Value("POSTGRES_PASSWORD");
                if (string.IsNullOrWhiteSpace(password)) Fail(host, "empty-password");
                var dev = Server2HostCommon.DevPassword(files, host, root);
                if (!string.IsNullOrWhiteSpace(dev) && string.Equals(password, dev, StringComparison.Ordinal)) Fail(host, "dev-password");
            }
            else if (!planOnly) Fail(host, "env-missing");

            if (planOnly)
            {
                host.Out.WriteLine(HostCommand(envFile, composePath));
                return Result(host, "plan", 0, "ok");
            }

            if (Server2HostCommon.ObservedLane(host, files, resolve) != "host") Fail(host, "not-server2-host");
            var up = Up(host, resolve, envFile, composePath);
            if (up.Length > 0) Fail(host, up);
            var accepted = Acceptance(host, resolve);
            if (accepted.Length > 0) Fail(host, accepted);
            return Result(host, "ok", 0, "ok");
        }

        // Invoke-StagingUp: docker compose ... up -d, its output redirected and not printed (now drained: the script never read it).
        private static string Up(NightlyHost host, Func<string, string> resolve, string envPath, string composePath)
        {
            var docker = resolve("docker");
            if (docker == null) return "missing-docker";
            var exit = Server2HostCommon.Quiet(host, docker, "compose", "--env-file", envPath, "-f", composePath, "-p", Project, "up", "-d");
            return exit != 0 ? "compose-up-failed" : "";
        }

        // Invoke-StagingCurl: curl -fsS --max-time 5 <url>, output read and not printed: '' on exit 0, else fail.
        private static string Curl(NightlyHost host, Func<string, string> resolve, string url)
        {
            var curl = resolve("curl");
            if (curl == null) return "acceptance-curl-missing";
            return Server2HostCommon.Quiet(host, curl, "-fsS", "--max-time", "5", url) == 0 ? "" : "fail";
        }

        // Invoke-StagingAcceptance: /health retried every 2 s while it is under 60 s from the first call, then /api/version once.
        private static string Acceptance(NightlyHost host, Func<string, string> resolve)
        {
            var deadline = host.UtcNow.Add(AcceptanceBound);
            var health = "fail";
            while (host.UtcNow < deadline)
            {
                health = Curl(host, resolve, HealthUrl);
                if (health.Length == 0) break;
                if (health == "acceptance-curl-missing") return health;
                host.Sleep(AcceptanceRetry);
            }
            if (health.Length > 0) return "acceptance-health";
            var version = Curl(host, resolve, VersionUrl);
            if (version == "acceptance-curl-missing") return version;
            if (version.Length > 0) return "acceptance-version";
            return "";
        }
    }
}
