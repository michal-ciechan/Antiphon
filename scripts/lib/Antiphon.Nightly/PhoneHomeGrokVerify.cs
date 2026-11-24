// CARD-0490 V-7: the isolated Antiphon + phone-home Grok container harness (unit 36 of
// docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md; it replaced scripts/verify-phone-home-grok.ps1, which an
// operator ran on the desktop; docker-compose.runner-grok.yml only names it in a comment, no container runs it). The CLI runs
// it as
//
//   antiphon-scripts verify-phone-home-grok --evidence-root <dir> [--configuration-file .antiphon/card0490-live.json]
//       [--placement local-docker|server2] [--write-config-only] [--skip-container] [--skip-turn] [--hold-seconds 180]
//       [--turn-timeout-seconds 600]
//
// It never binds 17202-17205. The configuration (allocated loopback ports, a run root under <checkout>/.antiphon, a
// throwaway copy of the primary GROK_HOME's auth.json, config.toml and version.json, the secret file) is written first, with
// any existing configuration file overlaid; the blockers go to <evidence>/blockers.txt. --write-config-only stops there: exit
// 2 when a container blocker remains, else 0. Otherwise: an isolated Postgres container, an isolated local SessionRunner
// (never 17204, never the dead port 1) and an isolated server are built and started, the pinned standing Grok agent is created
// or switched to Grok, docker-compose.runner-grok.yml is started with PhoneHome__ServerOrigin injected at launch
// (PHONE_HOME_SERVER_ORIGIN: host.docker.internal for local-docker, the desktop's Tailscale IPv4 for server2), the runner is
// held until dispatchEligible, and one canary turn (PHONE_HOME_OK_<nonce>) is proven from the transcript into
// <evidence>/v7-turn.json. Everything this run started is stopped and removed at the end (the compose project, the processes,
// the Postgres container, the server2 firewall rule, every bin-card0490-v7* output). Exit 0 done, 2 blockers remain, 1 a
// failure (one stderr line). Plain C#: no PowerShell types. ASCII only.
#nullable disable
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Antiphon.Nightly
{
    /// <summary>The verify-phone-home-grok command's options (verify-phone-home-grok.ps1's parameters).</summary>
    public sealed class PhoneHomeGrokVerifyOptions
    {
        /// <summary>The CLI's usage line.</summary>
        public const string Usage = "verify-phone-home-grok --evidence-root <dir> [--configuration-file .antiphon/card0490-live.json] [--placement local-docker|server2]"
            + " [--write-config-only] [--skip-container] [--skip-turn] [--hold-seconds 180] [--turn-timeout-seconds 600]";

        /// <summary>The placements.</summary>
        public static readonly IReadOnlyList<string> Placements = new[] { "local-docker", "server2" };

        /// <summary>The live configuration (relative: to the working directory).</summary>
        public string ConfigurationFile { get; init; } = ".antiphon/card0490-live.json";

        /// <summary>Where the blockers and the evidence go.</summary>
        public string EvidenceRoot { get; init; } = "";

        /// <summary>The placement when given (it then overrides the configuration's); null: the configuration's, else local-docker.</summary>
        public string Placement { get; init; }

        /// <summary>Allocate and write the configuration only.</summary>
        public bool WriteConfigOnly { get; init; }

        /// <summary>Bring up the isolated server, not the container.</summary>
        public bool SkipContainer { get; init; }

        /// <summary>No canary turn.</summary>
        public bool SkipTurn { get; init; }

        /// <summary>How long to wait for a dispatchEligible runner (0: no wait).</summary>
        public int HoldSeconds { get; init; } = 180;

        /// <summary>How long the canary turn may take.</summary>
        public int TurnTimeoutSeconds { get; init; } = 600;

        /// <summary>The checkout (compose file, Dockerfile, fixtures, .antiphon run roots, the builds).</summary>
        public string RepoRoot { get; init; } = "";

        /// <summary>The directory a relative configuration file is read against.</summary>
        public string WorkingDirectory { get; init; } = "";

        /// <summary>The flags after the command name; the checkout and the working directory come from the CLI.</summary>
        public static PhoneHomeGrokVerifyOptions Parse(IReadOnlyList<string> arguments, string repoRoot, string workingDirectory)
        {
            var flags = NightlyArgs.Parse(arguments, new[] { "--configuration-file", "--evidence-root", "--placement", "--hold-seconds", "--turn-timeout-seconds" },
                new[] { "--write-config-only", "--skip-container", "--skip-turn" });
            string placement = null;
            if (flags.Has("--placement"))
            {
                var given = flags.Value("--placement", "");
                placement = Placements.FirstOrDefault(name => name.Equals(given, StringComparison.OrdinalIgnoreCase))
                    ?? throw new NightlyUsageException("--placement expects local-docker or server2");
            }
            return new PhoneHomeGrokVerifyOptions
            {
                ConfigurationFile = flags.Value("--configuration-file", ".antiphon/card0490-live.json"),
                EvidenceRoot = flags.Required("--evidence-root"),
                Placement = placement,
                WriteConfigOnly = flags.Switch("--write-config-only"),
                SkipContainer = flags.Switch("--skip-container"),
                SkipTurn = flags.Switch("--skip-turn"),
                HoldSeconds = flags.Int("--hold-seconds", 180),
                TurnTimeoutSeconds = flags.Int("--turn-timeout-seconds", 600),
                RepoRoot = repoRoot ?? "",
                WorkingDirectory = workingDirectory ?? "",
            };
        }
    }

    /// <summary>A command's end: its exit code, its stdout and its stderr (both empty unless captured).</summary>
    public sealed record PhoneHomeGrokRun(int ExitCode, string Output, string Error);

    /// <summary>An isolated process this run started (dotnet &lt;dll&gt;).</summary>
    public interface IPhoneHomeGrokChild : IDisposable
    {
        /// <summary>Its process id.</summary>
        int Id { get; }

        /// <summary>It has exited.</summary>
        bool HasExited { get; }

        /// <summary>Its exit code once exited.</summary>
        int ExitCode { get; }

        /// <summary>Stop-Process -Force, then up to 5 s for the exit (best effort).</summary>
        void Stop();
    }

    /// <summary>A JSON request that failed: the answer's body, else the transport's message.</summary>
    public sealed class PhoneHomeGrokHttpException : Exception
    {
        /// <summary>The failure's detail.</summary>
        public PhoneHomeGrokHttpException(string detail) : base(detail) { }
    }

    /// <summary>Everything the harness observes or changes outside its own memory.</summary>
    public interface IPhoneHomeGrokPlatform
    {
        /// <summary>The file system.</summary>
        IFileSystem Files { get; }

        /// <summary>A free loopback TCP port (bound to port 0, read, released).</summary>
        int FreeLoopbackPort();

        /// <summary>
        /// Runs a command to its end with the variables added to this process's environment. capture: stdout and stderr read
        /// into the result; otherwise both are this process's. Throws when the command cannot start.
        /// </summary>
        PhoneHomeGrokRun Run(string file, IReadOnlyList<string> arguments, IReadOnlyDictionary<string, string> environment, bool capture);

        /// <summary>Starts dotnet &lt;dll&gt; in the directory with the variables added, no window. Throws when it cannot start.</summary>
        IPhoneHomeGrokChild StartDll(string dll, string workingDirectory, IReadOnlyDictionary<string, string> environment);

        /// <summary>One GET: the status code, 0 when nothing answered within the timeout.</summary>
        int Status(string url, int timeoutSec);

        /// <summary>One request (Accept: application/json; a non-null body is JSON): the answer's body. Throws PhoneHomeGrokHttpException.</summary>
        string Request(string method, string url, string jsonBody, int timeoutSec);
    }

    /// <summary>The CARD-0490 V-7 harness (verify-phone-home-grok.ps1's body).</summary>
    public static class PhoneHomeGrokVerify
    {
        /// <summary>The Grok version the image pins (docker/session-runner-grok/Dockerfile's GROK_VERSION).</summary>
        public const string GrokVersion = "1.0.41";

        /// <summary>The production ports a configuration never names: 17202-17205.</summary>
        public static readonly IReadOnlyList<int> ForbiddenPorts = new[] { 17202, 17203, 17204, 17205 };

        /// <summary>The desktop's Tailscale IPv4 when tailscale cannot answer.</summary>
        public const string FallbackDesktopHost = "100.79.51.37";

        /// <summary>The compose service.</summary>
        public const string Service = "session-runner-grok";

        // The Grok version probe inside the container, as uid 1654 (never root: CARD-1174).
        private static readonly string[] GrokVersionProbe = "exec -T --user 1654:1654 session-runner-grok grok --version".Split(' ');

        private static readonly Regex GrokPin = new Regex(@"1\.0\.41", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        private static readonly Regex TailscaleIp = new Regex(@"^100\.\d+\.\d+\.\d+$", RegexOptions.CultureInvariant);
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);
        private static readonly JsonSerializerOptions Indented = new JsonSerializerOptions { WriteIndented = true };

        private sealed class Failure : Exception
        {
            public Failure(string message) : base(message) { }
        }

        /// <summary>Exit 0 done, 2 blockers remain, 1 a failure (one line on host.Error after everything started is stopped).</summary>
        public static int Run(PhoneHomeGrokVerifyOptions options, NightlyHost host, IPhoneHomeGrokPlatform platform)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (host == null) throw new ArgumentNullException(nameof(host));
            if (platform == null) throw new ArgumentNullException(nameof(platform));
            try
            {
                return new Harness(options, host, platform).Run();
            }
            catch (Exception e) when (e is Failure || e is IOException || e is UnauthorizedAccessException || e is JsonException || e is Win32Exception
                || e is InvalidOperationException || e is UriFormatException || e is FormatException || e is ArgumentException)
            {
                host.Error.WriteLine("verify-phone-home-grok: " + e.Message);
                return 1;
            }
        }

        private sealed class Harness
        {
            private readonly PhoneHomeGrokVerifyOptions _options;
            private readonly NightlyHost _host;
            private readonly IPhoneHomeGrokPlatform _platform;
            private readonly IFileSystem _files;
            private readonly string _repo;
            private readonly string _composeFile;
            private readonly Dictionary<string, string> _composeEnvironment = new Dictionary<string, string>(StringComparer.Ordinal);
            private JsonObject _config;
            private string _configPath;
            private IPhoneHomeGrokChild _server;
            private IPhoneHomeGrokChild _runner;
            private string _serverDll;
            private string _serverOutDir;
            private string _pgPass;
            private string _composeProject;

            public Harness(PhoneHomeGrokVerifyOptions options, NightlyHost host, IPhoneHomeGrokPlatform platform)
            {
                _options = options;
                _host = host;
                _platform = platform;
                _files = platform.Files;
                _repo = options.RepoRoot;
                _composeFile = _files.Path.Combine(_repo, "docker-compose.runner-grok.yml");
            }

            private TextWriter Out => _host.Out;

            private string Repo(params string[] parts) => _files.Path.Combine(new[] { _repo }.Concat(parts).ToArray());

            public int Run()
            {
                AssertComposeDoesNotHardcodeOrigin();
                _files.Directory.CreateDirectory(_options.EvidenceRoot);
                _configPath = _files.Path.IsPathRooted(_options.ConfigurationFile)
                    ? _options.ConfigurationFile
                    : _files.Path.Combine(_options.WorkingDirectory, _options.ConfigurationFile);

                _config = NewIsolatedLiveConfig();
                if (_files.File.Exists(_configPath) && JsonNode.Parse(_files.File.ReadAllText(_configPath)) is JsonObject existing) Merge(_config, existing);
                if (_options.Placement != null) _config["placement"] = _options.Placement;
                else if (string.IsNullOrWhiteSpace(S("placement"))) _config["placement"] = "local-docker";
                SetPlacementOrigin(S("placement"));
                if (Blank(_config["runnerPort"])) _config["runnerPort"] = AllocatePort();
                _config["localRunnerOrigin"] = "http://127.0.0.1:" + S("runnerPort");
                var primary = PrimaryGrokHome();
                _config["grokHomeSource"] = primary;
                if (string.Equals(_files.Path.GetFullPath(S("oauthMount")), primary, StringComparison.OrdinalIgnoreCase))
                    _config["oauthMount"] = Repo(".antiphon", "card0490-live-" + S("runId"), "grok-home");
                if (string.IsNullOrWhiteSpace(S("grokSessionsMount")))
                    _config["grokSessionsMount"] = _files.Path.Combine(_files.Path.GetDirectoryName(S("oauthMount")) ?? "", "grok-sessions");
                foreach (var directory in new[] { S("oauthMount"), _files.Path.Combine(S("oauthMount"), "sessions"), S("grokSessionsMount") })
                    _files.Directory.CreateDirectory(directory);

                AssertNotProductionPort(_config["serverPort"], "serverPort");
                AssertNotProductionPort(_config["postgresPort"], "postgresPort");
                AssertNotProductionPort(_config["runnerPort"], "runnerPort");
                AssertNotProductionOrigin(S("serverOrigin"), "serverOrigin");
                AssertNotProductionOrigin(S("phoneHomeServerOrigin"), "phoneHomeServerOrigin");
                AssertNotProductionOrigin(S("localRunnerOrigin"), "localRunnerOrigin");
                _config["hostWorkspaceRoot"] = _files.Path.GetFullPath(S("hostWorkspaceRoot"));
                _config["localRunnerOrigin"] = "http://127.0.0.1:" + S("runnerPort");

                var copied = CopyThrowawayGrokHome(S("grokHomeSource"), S("oauthMount"));
                Out.WriteLine("Throwaway GROK_HOME copy from grokHomeSource path (contents not logged): copiedAuth=" + (copied ? "True" : "False") + " mount=" + S("oauthMount"));
                WriteConfig();
                Out.WriteLine("Wrote isolated live config: " + _configPath);
                Out.WriteLine("placement=" + S("placement") + " phoneHomeServerOrigin=" + S("phoneHomeServerOrigin") + " (PhoneHome__ServerOrigin / PHONE_HOME_SERVER_ORIGIN)");

                var blockers = ContainerBlockers();
                var qemuBlockers = QemuBlockers();
                var blockerPath = _files.Path.Combine(_options.EvidenceRoot, "blockers.txt");
                var all = blockers.Concat(qemuBlockers.Select(blocker => "qemu: " + blocker)).ToList();
                _files.File.WriteAllText(blockerPath, string.Concat(all.Select(line => line + Environment.NewLine)), Utf8);

                if (_options.WriteConfigOnly)
                {
                    Out.WriteLine("WriteConfigOnly: isolated ports allocated; container not started.");
                    if (blockers.Count > 0)
                    {
                        Out.WriteLine("Remaining V-7 container blockers:");
                        foreach (var blocker in blockers) Out.WriteLine(" - " + blocker);
                        return 2;
                    }
                    if (qemuBlockers.Count > 0)
                    {
                        Out.WriteLine("QEMU ordinary-lane blockers (do not block the isolated Grok container):");
                        foreach (var blocker in qemuBlockers) Out.WriteLine(" - " + blocker);
                    }
                    return 0;
                }
                return Live(blockers, qemuBlockers, blockerPath);
            }

            // ---- the configuration ----

            private string S(string key) => Text(_config[key]);

            private int AllocatePort()
            {
                var port = _platform.FreeLoopbackPort();
                if (ForbiddenPorts.Contains(port)) throw new Failure("Allocated a forbidden production port " + port.ToString(CultureInfo.InvariantCulture) + ".");
                return port;
            }

            private string PrimaryGrokHome()
            {
                var fromEnvironment = _host.Variables("GROK_HOME");
                if (!string.IsNullOrWhiteSpace(fromEnvironment)) return _files.Path.GetFullPath(fromEnvironment);
                return _files.Path.GetFullPath(_files.Path.Combine(_host.Variables("USERPROFILE") ?? "", ".grok"));
            }

            private JsonObject NewIsolatedLiveConfig()
            {
                var serverPort = AllocatePort();
                var postgresPort = AllocatePort();
                var runnerPort = AllocatePort();
                var runId = Guid.NewGuid().ToString("N").Substring(0, 12);
                var runRoot = Repo(".antiphon", "card0490-live-" + runId);
                var workspace = _files.Path.GetFullPath(_files.Path.Combine(runRoot, "work"));
                var state = _files.Path.GetFullPath(_files.Path.Combine(runRoot, "state"));
                var grokHome = _files.Path.GetFullPath(_files.Path.Combine(runRoot, "grok-home"));
                var grokSessions = _files.Path.GetFullPath(_files.Path.Combine(runRoot, "grok-sessions"));
                var localRunnerLogs = _files.Path.GetFullPath(_files.Path.Combine(runRoot, "local-runner-logs"));
                var secretFile = _files.Path.Combine(runRoot, "phone-home.secret");
                foreach (var directory in new[] { workspace, state, grokHome, grokSessions, localRunnerLogs, _files.Path.GetDirectoryName(secretFile) })
                    _files.Directory.CreateDirectory(directory);
                _files.File.WriteAllText(secretFile, Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"), Encoding.ASCII);
                var agentId = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";
                var exampleFile = Repo("tests", "fixtures", "card0490-linux", "card0490-live.example.json");
                if (_files.File.Exists(exampleFile) && JsonNode.Parse(_files.File.ReadAllText(exampleFile)) is JsonObject example && Truthy(example["standingAgentId"]))
                    agentId = Text(example["standingAgentId"]);
                return new JsonObject
                {
                    ["serverPort"] = serverPort,
                    ["postgresPort"] = postgresPort,
                    ["runnerPort"] = runnerPort,
                    ["localRunnerOrigin"] = "http://127.0.0.1:" + runnerPort.ToString(CultureInfo.InvariantCulture),
                    ["serverOrigin"] = "http://127.0.0.1:" + serverPort.ToString(CultureInfo.InvariantCulture),
                    ["phoneHomeServerOrigin"] = "http://host.docker.internal:" + serverPort.ToString(CultureInfo.InvariantCulture),
                    ["placement"] = "local-docker",
                    ["desktopReachableHost"] = null,
                    ["runnerId"] = "grok-linux",
                    ["standingAgentId"] = agentId,
                    ["hostWorkspaceRoot"] = workspace,
                    ["grokHomeSource"] = PrimaryGrokHome(),
                    ["oauthMount"] = grokHome,
                    ["grokSessionsMount"] = grokSessions,
                    ["stateRoot"] = state,
                    ["localRunnerLogPath"] = localRunnerLogs,
                    ["secretFile"] = secretFile,
                    ["image"] = new JsonObject
                    {
                        ["dockerfile"] = "docker/session-runner-grok/Dockerfile",
                        ["digest"] = null,
                        ["grokVersion"] = GrokVersion,
                    },
                    ["assetLock"] = "tests/fixtures/card0490-linux/assets.lock.json",
                    ["runId"] = runId,
                };
            }

            // Merge-LiveConfig: every property of the existing file that is not null, empty, 0 or false replaces (or adds) its own.
            private static void Merge(JsonObject target, JsonObject overlay)
            {
                foreach (var property in overlay.ToList())
                {
                    if (Blank(property.Value) || IsZeroOrFalse(property.Value)) continue;
                    target[property.Key] = property.Value.DeepClone();
                }
            }

            private void SetPlacementOrigin(string placement)
            {
                _config["placement"] = placement;
                var port = IntOf(_config["serverPort"], "serverPort").ToString(CultureInfo.InvariantCulture);
                if (placement.Equals("server2", StringComparison.OrdinalIgnoreCase))
                {
                    var hostName = S("desktopReachableHost");
                    if (string.IsNullOrWhiteSpace(hostName)) hostName = DesktopReachableHost();
                    _config["desktopReachableHost"] = hostName;
                    _config["phoneHomeServerOrigin"] = "http://" + hostName + ":" + port;
                    if (S("phoneHomeServerOrigin").IndexOf("host.docker.internal", StringComparison.OrdinalIgnoreCase) >= 0)
                        throw new Failure("server2 placement must not use host.docker.internal.");
                }
                else
                {
                    _config["phoneHomeServerOrigin"] = "http://host.docker.internal:" + port;
                }
            }

            // The desktop's Tailscale IPv4 (tailscale ip -4, its first line), else the known address.
            private string DesktopReachableHost()
            {
                try
                {
                    var run = _platform.Run("tailscale", new[] { "ip", "-4" }, null, capture: true);
                    var first = (run.Output ?? "").Replace("\r\n", "\n").Split('\n').FirstOrDefault() ?? "";
                    if (TailscaleIp.IsMatch(first.Trim())) return first.Trim();
                }
                catch (Exception e) when (e is Win32Exception || e is InvalidOperationException || e is IOException)
                {
                    // tailscale is not installed or did not answer: the known address.
                }
                return FallbackDesktopHost;
            }

            private static void AssertNotProductionPort(JsonNode port, string name)
            {
                if (Blank(port)) return;
                var number = IntOf(port, name);
                if (ForbiddenPorts.Contains(number))
                    throw new Failure("Configuration names a production service port (" + name + "=" + number.ToString(CultureInfo.InvariantCulture) + ").");
            }

            private static void AssertNotProductionOrigin(string origin, string name)
            {
                if (string.IsNullOrWhiteSpace(origin)) return;
                if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri)) throw new Failure(name + " is not an absolute URI (" + origin + ").");
                if (ForbiddenPorts.Contains(uri.Port)) throw new Failure(name + " points at a production service port (" + origin + ").");
            }

            private bool CopyThrowawayGrokHome(string sourceHome, string destinationHome)
            {
                var source = _files.Path.GetFullPath(sourceHome);
                var destination = _files.Path.GetFullPath(destinationHome);
                if (string.Equals(source, destination, StringComparison.OrdinalIgnoreCase))
                    throw new Failure("Throwaway GROK_HOME must not be the primary store (" + source + ").");
                _files.Directory.CreateDirectory(destination);
                if (!_files.File.Exists(_files.Path.Combine(source, "auth.json"))) return false;
                // Never auth.json.lock, bin/ or sessions/ of the live home.
                foreach (var name in new[] { "auth.json", "config.toml", "version.json" })
                {
                    var from = _files.Path.Combine(source, name);
                    if (_files.File.Exists(from)) _files.File.Copy(from, _files.Path.Combine(destination, name), true);
                }
                // sessions/ exists so compose can overlay a writable bind on the read-only parent.
                _files.Directory.CreateDirectory(_files.Path.Combine(destination, "sessions"));
                return _files.File.Exists(_files.Path.Combine(destination, "auth.json"));
            }

            private void WriteConfig()
            {
                var directory = _files.Path.GetDirectoryName(_configPath);
                if (!string.IsNullOrEmpty(directory)) _files.Directory.CreateDirectory(directory);
                _files.File.WriteAllText(_configPath, _config.ToJsonString(Indented) + Environment.NewLine, Utf8);
            }

            private void AssertComposeDoesNotHardcodeOrigin()
            {
                if (!_files.File.Exists(_composeFile)) throw new Failure("Compose file not found: " + _composeFile);
                var text = _files.File.ReadAllText(_composeFile);
                if (Matches(text, @"PhoneHome__ServerOrigin:\s*http"))
                    throw new Failure("docker-compose.runner-grok.yml hardcodes PhoneHome__ServerOrigin; it must read PHONE_HOME_SERVER_ORIGIN.");
                if (!Matches(text, "PHONE_HOME_SERVER_ORIGIN")) throw new Failure("docker-compose.runner-grok.yml must interpolate PHONE_HOME_SERVER_ORIGIN.");
                if (Matches(text, @"host\.docker\.internal:1720[2-5]")) throw new Failure("docker-compose.runner-grok.yml must not name production Aspire ports.");
                if (!Matches(text, @"(?s)PHONE_HOME_GROK_HOME.*?read_only:\s*true")) throw new Failure("PHONE_HOME_GROK_HOME must be a read-only bind mount.");
                if (!Matches(text, "PHONE_HOME_GROK_SESSIONS"))
                    throw new Failure("docker-compose.runner-grok.yml must interpolate PHONE_HOME_GROK_SESSIONS for writable session files.");
            }

            private List<string> ContainerBlockers()
            {
                var blockers = new List<string>();
                var primary = PrimaryGrokHome();
                var mount = _files.Path.GetFullPath(S("oauthMount"));
                if (string.Equals(primary, mount, StringComparison.OrdinalIgnoreCase))
                    blockers.Add("oauthMount is the primary GROK_HOME; copy auth.json into a throwaway directory and mount that read-only.");
                if (!_files.File.Exists(_files.Path.Combine(mount, "auth.json")))
                    blockers.Add("Grok OAuth copy missing: expected auth.json in throwaway oauthMount (" + mount + "), copied from primary GROK_HOME (env GROK_HOME or %USERPROFILE%\\.grok)."
                        + " Do not bind-mount the primary store. Mount the copy read-only at /state/grok via PHONE_HOME_GROK_HOME. Not XAI_API_KEY.");
                var dockerfile = _files.File.ReadAllText(Repo("docker", "session-runner-grok", "Dockerfile"));
                if (!GrokPin.IsMatch(dockerfile) || !Matches(dockerfile, "grok"))
                    blockers.Add("docker/session-runner-grok/Dockerfile does not install Grok " + GrokVersion + "; the image cannot complete a provider turn until grok is pinned into the image.");
                if (Matches(dockerfile, @"(?im)^\s*ENV\s+PhoneHome__ServerOrigin\b"))
                    blockers.Add("docker/session-runner-grok/Dockerfile must not ENV PhoneHome__ServerOrigin.");
                return blockers;
            }

            private List<string> QemuBlockers()
            {
                var blockers = new List<string>();
                var assetLock = Repo("tests", "fixtures", "card0490-linux", "assets.lock.json");
                if (!_files.File.Exists(assetLock)) blockers.Add("Asset lock missing: tests/fixtures/card0490-linux/assets.lock.json");
                else if (Matches(_files.File.ReadAllText(assetLock), "pending-operator-pin"))
                    blockers.Add("tests/fixtures/card0490-linux/assets.lock.json still has pending-operator-pin; replace qemu/qemu-img/bootDisk sha256 pins before the QEMU ordinary lane / PC-28-31.");
                return blockers;
            }

            // ---- the live run ----

            private int Live(List<string> blockers, List<string> qemuBlockers, string blockerPath)
            {
                var runId = S("runId");
                var pgName = "antiphon-card0490-pg-" + runId;
                _composeProject = "card0490-" + runId;
                var pgStarted = false;
                try
                {
                    _pgPass = "card0490_" + Guid.NewGuid().ToString("N").Substring(0, 12);
                    Out.WriteLine("Starting isolated Postgres on port " + S("postgresPort") + " (" + pgName + ")");
                    var postgres = _platform.Run("docker", new[]
                    {
                        "run", "--rm", "-d", "--name", pgName, "-e", "POSTGRES_PASSWORD=" + _pgPass, "-e", "POSTGRES_USER=antiphon",
                        "-e", "POSTGRES_DB=antiphon_card0490", "-p", S("postgresPort") + ":5432", "postgres:16-alpine",
                    }, null, capture: false);
                    if (postgres.ExitCode != 0) throw new Failure("docker run postgres failed (exit " + postgres.ExitCode.ToString(CultureInfo.InvariantCulture) + ").");
                    pgStarted = true;
                    var ready = false;
                    for (var i = 0; i < 30; i++)
                    {
                        if (_platform.Run("docker", new[] { "exec", pgName, "pg_isready", "-U", "antiphon", "-d", "antiphon_card0490" }, null, capture: true).ExitCode == 0)
                        {
                            ready = true;
                            break;
                        }
                        _host.Sleep(TimeSpan.FromSeconds(1));
                    }
                    if (!ready) throw new Failure("Isolated Postgres did not become ready.");

                    var runnerOutDir = Repo("src", "Antiphon.SessionRunner", "bin-card0490-v7-runner");
                    Out.WriteLine("Building isolated Antiphon.SessionRunner to " + runnerOutDir);
                    if (_platform.Run("dotnet", new[] { "build", Repo("src", "Antiphon.SessionRunner", "Antiphon.SessionRunner.csproj"), "--property:OutputPath=bin-card0490-v7-runner/", "--nologo" },
                            null, capture: false).ExitCode != 0)
                        throw new Failure("Isolated local runner build failed.");
                    var runnerDll = _files.Path.Combine(runnerOutDir, "Antiphon.SessionRunner.dll");
                    if (string.IsNullOrWhiteSpace(S("localRunnerLogPath")))
                        _config["localRunnerLogPath"] = _files.Path.Combine(_files.Path.GetDirectoryName(S("stateRoot")) ?? "", "local-runner-logs");
                    _files.Directory.CreateDirectory(S("localRunnerLogPath"));
                    Out.WriteLine("Starting isolated local SessionRunner at " + S("localRunnerOrigin") + " (not 17204, not dead-port 1)");
                    _runner = _platform.StartDll(runnerDll, runnerOutDir, new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["ASPNETCORE_URLS"] = S("localRunnerOrigin"),
                        ["SessionRunner__SessionLogPath"] = S("localRunnerLogPath"),
                        ["PhoneHome__Enabled"] = "false",
                        ["Serilog__LogPath"] = S("localRunnerLogPath"),
                    });
                    WaitHealth(S("localRunnerOrigin") + "/health", _runner, 30, "Isolated local SessionRunner");
                    Out.WriteLine("Isolated local SessionRunner healthy: " + S("localRunnerOrigin") + "/health");

                    _serverOutDir = Repo("server", "bin-card0490-v7");
                    Out.WriteLine("Building isolated Antiphon.Server to " + _serverOutDir);
                    if (_platform.Run("dotnet", new[] { "build", Repo("server", "Antiphon.Server.csproj"), "--property:OutputPath=bin-card0490-v7/", "--nologo" }, null, capture: false).ExitCode != 0)
                        throw new Failure("Isolated server build failed.");
                    _serverDll = _files.Path.Combine(_serverOutDir, "Antiphon.Server.dll");
                    StartServer();

                    var origin = S("serverOrigin");
                    var standingId = S("standingAgentId");
                    JsonNode agent;
                    try { agent = Json("GET", origin + "/api/agents/" + standingId, null, 30); }
                    catch (Failure) { agent = null; }
                    if (!Truthy(agent))
                    {
                        Out.WriteLine("Creating standing Grok agent in workspace " + S("hostWorkspaceRoot"));
                        var created = Json("POST", origin + "/api/agents", new JsonObject
                        {
                            ["name"] = "phone-home-grok",
                            ["workingDirectory"] = S("hostWorkspaceRoot"),
                            ["createWorkingDirectory"] = true,
                            ["alwaysOn"] = false,
                            ["details"] = "CARD-0490 V-7 isolated Grok phone-home canary",
                        }, 30);
                        agent = Json("PATCH", origin + "/api/agents/" + Field(created, "id"), GrokPatch(created), 30);
                        if (!string.Equals(Field(agent, "id"), standingId, StringComparison.OrdinalIgnoreCase))
                        {
                            Out.WriteLine("Standing agent id " + Field(agent, "id") + " differs from configured " + standingId
                                + "; restarting isolated server before the container so epoch stays 1.");
                            _config["standingAgentId"] = Field(agent, "id");
                            WriteConfig();
                            _server?.Stop();
                            _server?.Dispose();
                            _server = null;
                            _host.Sleep(TimeSpan.FromSeconds(2));
                            StartServer();
                            agent = Json("GET", origin + "/api/agents/" + S("standingAgentId"), null, 30);
                        }
                    }
                    else if (!string.Equals(Field(agent, "kind"), "Grok", StringComparison.OrdinalIgnoreCase) && !string.Equals(Field(agent, "kind"), "4", StringComparison.OrdinalIgnoreCase))
                    {
                        agent = Json("PATCH", origin + "/api/agents/" + Field(agent, "id"), GrokPatch(agent), 30);
                    }
                    Out.WriteLine("Standing Grok agent id=" + Field(agent, "id") + " kind=" + Field(agent, "kind") + " cwd=" + Field(agent, "workingDirectory"));

                    var server2 = S("placement").Equals("server2", StringComparison.OrdinalIgnoreCase);
                    if (server2) OpenFirewall();

                    _composeEnvironment["PHONE_HOME_SERVER_ORIGIN"] = S("phoneHomeServerOrigin");
                    _composeEnvironment["PHONE_HOME_WORKSPACE"] = S("hostWorkspaceRoot");
                    _composeEnvironment["PHONE_HOME_STATE"] = S("stateRoot");
                    _composeEnvironment["PHONE_HOME_GROK_HOME"] = S("oauthMount");
                    _composeEnvironment["PHONE_HOME_GROK_SESSIONS"] = S("grokSessionsMount");
                    _composeEnvironment["PHONE_HOME_SECRET_FILE"] = S("secretFile");

                    Out.WriteLine("docker compose config (origin injected, no published runner port)");
                    if (Compose(false, "config").ExitCode != 0) throw new Failure("docker compose config failed.");
                    if (_options.SkipContainer)
                    {
                        Out.WriteLine("SkipContainer: isolated server is up; grok container not started.");
                    }
                    else if (server2)
                    {
                        Out.WriteLine("server2 placement: start the container on server2 with PHONE_HOME_SERVER_ORIGIN=" + S("phoneHomeServerOrigin")
                            + " (desktop Tailscale). host.docker.internal on server2 is server2 itself.");
                    }
                    else if (blockers.Count > 0)
                    {
                        Out.WriteLine("Not starting the Grok container until blockers are cleared:");
                        foreach (var blocker in blockers) Out.WriteLine(" - " + blocker);
                    }
                    else
                    {
                        Out.WriteLine("Starting phone-home Grok container with PhoneHome__ServerOrigin=" + _composeEnvironment["PHONE_HOME_SERVER_ORIGIN"]);
                        if (Compose(false, "up", "-d", "--build").ExitCode != 0) throw new Failure("docker compose up failed.");
                        Out.WriteLine("CARD-0490 container started against isolated origin. Queue the canary through the isolated API; do not use 17202.");
                    }

                    var statusPath = _files.Path.Combine(_options.EvidenceRoot, "runner-status.json");
                    JsonNode status = null;
                    var live = blockers.Count == 0 && !_options.SkipContainer && !server2;
                    if (_options.HoldSeconds > 0 && live)
                    {
                        var deadline = _host.UtcNow.AddSeconds(_options.HoldSeconds);
                        var statusUrl = origin + "/api/session-runners/" + S("runnerId") + "/status";
                        Out.WriteLine("Holding " + _options.HoldSeconds.ToString(CultureInfo.InvariantCulture) + " s for dispatchEligible phone-home runner at " + statusUrl);
                        while (_host.UtcNow < deadline)
                        {
                            try
                            {
                                status = Json("GET", statusUrl, null, 3);
                                _files.File.WriteAllText(statusPath, Pretty(status), Utf8);
                                if (IsTrue(Get(status, "dispatchEligible")) && IsTrue(Get(status, "available")))
                                {
                                    Out.WriteLine("Runner available=true dispatchEligible=true store=" + Field(status, "runnerStoreId") + " boot=" + Field(status, "processBootId")
                                        + " epoch=" + Field(status, "epoch"));
                                    break;
                                }
                                Out.WriteLine("Runner available=" + Field(status, "available") + " dispatchEligible=" + Field(status, "dispatchEligible") + " epoch=" + Field(status, "epoch")
                                    + " heartbeat=" + Field(status, "lastHeartbeatUtc"));
                            }
                            catch (Failure e)
                            {
                                Out.WriteLine("Runner status not ready: " + e.Message);
                            }
                            _host.Sleep(TimeSpan.FromSeconds(2));
                        }
                        if (status == null || !IsTrue(Get(status, "dispatchEligible")))
                            throw new Failure("Phone-home runner did not become dispatchEligible within " + _options.HoldSeconds.ToString(CultureInfo.InvariantCulture) + " s. See " + statusPath);
                    }

                    if (!_options.SkipTurn && live) Turn(origin, status);

                    if (qemuBlockers.Count > 0) Out.WriteLine("QEMU ordinary-lane blockers written to " + blockerPath + " (container V-7 is independent).");
                    if (blockers.Count > 0)
                    {
                        Out.WriteLine("V-7 remaining blockers written to " + blockerPath);
                        return 2;
                    }
                    Out.WriteLine("Evidence root: " + _options.EvidenceRoot);
                    return 0;
                }
                finally
                {
                    CleanUp(pgName, pgStarted);
                }
            }

            private void StartServer()
            {
                Out.WriteLine("Starting isolated Antiphon.Server at " + S("serverOrigin") + " SessionRunner__BaseUrl=" + S("localRunnerOrigin") + " StandingAgentId=" + S("standingAgentId"));
                _server = _platform.StartDll(_serverDll, _serverOutDir, ServerEnvironment());
                WaitHealth(S("serverOrigin") + "/health", _server, 90, "Isolated server");
                Out.WriteLine("Isolated server healthy: " + S("serverOrigin") + "/health");
            }

            private Dictionary<string, string> ServerEnvironment()
            {
                var secret = _files.File.ReadAllText(S("secretFile")).Trim();
                return new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["ConnectionStrings__DefaultConnection"] = "Host=127.0.0.1;Port=" + S("postgresPort") + ";Database=antiphon_card0490;Username=antiphon;Password=" + _pgPass,
                    ["ASPNETCORE_URLS"] = "http://0.0.0.0:" + S("serverPort"),
                    ["SessionRunner__BaseUrl"] = S("localRunnerOrigin"),
                    ["SessionRunner__Enabled"] = "true",
                    ["Agents__DefaultDefinition"] = "grok",
                    ["Delegation__CheckInterpreterEnabled"] = "false",
                    ["Delegation__DiagnoseEnabled"] = "false",
                    ["Delegation__OutputDistillerEnabled"] = "false",
                    ["Hangfire__ServerEnabled"] = "false",
                    ["AgentTui__ImportProfilesOnStartup"] = "false",
                    ["PhoneHomeRunner__Enabled"] = "true",
                    ["PhoneHomeRunner__AllowedRunnerId"] = S("runnerId"),
                    ["PhoneHomeRunner__StandingAgentId"] = S("standingAgentId"),
                    // CARD-0604: this harness is the CARD-0490 canary - one pinned cardless agent against its own isolated server,
                    // and nothing else. The delegated-task pool is the production server2 runner's shape; asserting false here
                    // keeps the two lanes from drifting together.
                    ["PhoneHomeRunner__AllowDelegatedTasks"] = "false",
                    ["PhoneHomeRunner__HostWorkspaceRoot"] = S("hostWorkspaceRoot"),
                    ["PhoneHomeRunner__RunnerWorkspace"] = "/work",
                    ["PhoneHomeRunner__ChildGrokHome"] = "/state/grok",
                    ["PhoneHomeRunner__CallbackOrigin"] = S("phoneHomeServerOrigin"),
                    ["PhoneHomeRunner__SharedSecret"] = secret,
                };
            }

            private void WaitHealth(string url, IPhoneHomeGrokChild child, int seconds, string name)
            {
                for (var i = 0; i < seconds; i++)
                {
                    if (_platform.Status(url, 2) == 200) return;
                    if (child != null && child.HasExited)
                        throw new Failure(name + " exited early (code " + child.ExitCode.ToString(CultureInfo.InvariantCulture) + ").");
                    _host.Sleep(TimeSpan.FromSeconds(1));
                }
                throw new Failure(name + " /health did not return 200 on " + url + ".");
            }

            private static JsonObject GrokPatch(JsonNode agent)
            {
                var assign = Field(agent, "assignmentPolicy");
                if (string.IsNullOrWhiteSpace(assign) || assign == "0") assign = "AutoPick";
                return new JsonObject
                {
                    ["name"] = Get(agent, "name")?.DeepClone(),
                    ["workingDirectory"] = Get(agent, "workingDirectory")?.DeepClone(),
                    ["details"] = Get(agent, "details")?.DeepClone(),
                    ["defaultWorkflowTemplateId"] = Get(agent, "defaultWorkflowTemplateId")?.DeepClone(),
                    ["assignmentPolicy"] = assign,
                    ["alwaysOn"] = false,
                    ["kind"] = "Grok",
                    ["sessionBackend"] = "PtyHost",
                };
            }

            private string FirewallRule => "CARD-0490-v7-" + S("serverPort");

            private void OpenFirewall()
            {
                try
                {
                    var run = _platform.Run("netsh", new[]
                    {
                        "advfirewall", "firewall", "add", "rule", "name=" + FirewallRule, "dir=in", "action=allow", "protocol=TCP",
                        "localport=" + S("serverPort"), "remoteip=100.64.0.0/10",
                    }, null, capture: true);
                    if (run.ExitCode == 0) Out.WriteLine("Opened Tailscale inbound firewall rule " + FirewallRule + " for port " + S("serverPort"));
                    else Out.WriteLine("Could not add Tailscale firewall rule " + FirewallRule + " (elevation may be required): " + (run.Output + run.Error).Trim());
                }
                catch (Exception e) when (e is Win32Exception || e is InvalidOperationException || e is IOException)
                {
                    Out.WriteLine("Could not add Tailscale firewall rule " + FirewallRule + " (elevation may be required): " + e.Message);
                }
            }

            private void Turn(string origin, JsonNode status)
            {
                if (status == null || !IsTrue(Get(status, "dispatchEligible"))) throw new Failure("V-7 turn requires dispatchEligible=true before start.");
                var evidenceRoot = _options.EvidenceRoot;
                var nonce = Guid.NewGuid().ToString("N").Substring(0, 8);
                var marker = "PHONE_HOME_OK_" + nonce;
                var prompt = "Reply with " + marker + " and do not use tools.";
                Out.WriteLine("Starting pinned standing Grok with fresh=true (first launch; no native history yet)");
                var agentUrl = origin + "/api/agents/" + S("standingAgentId");
                var started = Json("POST", agentUrl + "/start", new JsonObject { ["fresh"] = true }, 60);
                string sessionId = null;
                if (Truthy(Get(started, "liveSession")) && Truthy(Get(Get(started, "liveSession"), "id"))) sessionId = Field(Get(started, "liveSession"), "id");
                else if (Truthy(Get(started, "persistentSessionId"))) sessionId = Field(started, "persistentSessionId");
                if (string.IsNullOrWhiteSpace(sessionId)) throw new Failure("Start did not return a session id: " + (started?.ToJsonString() ?? "null"));
                string generation = null;
                if (Truthy(Get(started, "liveSession")) && Truthy(Get(Get(started, "liveSession"), "startedAt"))) generation = Field(Get(started, "liveSession"), "startedAt");
                Out.WriteLine("Queued standing start session " + sessionId + " generation=" + generation + "; waiting for liveSession.status=Running");

                var idleDeadline = _host.UtcNow.AddSeconds(Math.Min(_options.TurnTimeoutSeconds, 180));
                var readyForPrompt = false;
                while (_host.UtcNow < idleDeadline)
                {
                    var detail = Json("GET", agentUrl, null, 30);
                    var working = IsTrue(Get(detail, "working"));
                    string sessionStatus = null;
                    string failureReason = null;
                    var liveSession = Get(detail, "liveSession");
                    if (Truthy(liveSession))
                    {
                        sessionId = Field(liveSession, "id");
                        generation = Field(liveSession, "startedAt");
                        sessionStatus = Field(liveSession, "status");
                        if (Truthy(Get(liveSession, "failureReason"))) failureReason = Field(liveSession, "failureReason");
                    }
                    if (Is(sessionStatus, "Failed") || Is(sessionStatus, "3"))
                    {
                        RunnerDiagnostics();
                        throw new Failure("Standing start failed (session " + sessionId + "): " + failureReason);
                    }
                    if ((Is(sessionStatus, "Running") || Is(sessionStatus, "1")) && !working)
                    {
                        readyForPrompt = true;
                        break;
                    }
                    Out.WriteLine("liveSession.status=" + sessionStatus + " working=" + (working ? "True" : "False") + " failure=" + failureReason);
                    _host.Sleep(TimeSpan.FromSeconds(3));
                }
                if (!readyForPrompt)
                {
                    RunnerDiagnostics();
                    throw new Failure("Session " + sessionId + " did not become Running+idle within the rules wait. Not queueing a turn into a failed or still-starting host.");
                }

                var attemptFloor = _host.UtcNow;
                var queued = Json("POST", origin + "/api/sessions/" + sessionId + "/messages", new JsonObject { ["body"] = prompt, ["mode"] = "WhenIdle" }, 60);
                string queueId = null;
                var lastQueued = Get(queued, "messages") is JsonArray messages && messages.Count > 0 ? messages[messages.Count - 1] : null;
                if (Truthy(lastQueued) && Truthy(Get(lastQueued, "id"))) queueId = Field(lastQueued, "id");
                else if (Truthy(Get(queued, "id"))) queueId = Field(queued, "id");
                Out.WriteLine("Queued canary queueId=" + queueId + " session=" + sessionId);

                JsonNode userPrompt = null, assistant = null, turnEnd = null, transcript = null;
                var turnDeadline = _host.UtcNow.AddSeconds(_options.TurnTimeoutSeconds);
                while (_host.UtcNow < turnDeadline)
                {
                    transcript = Json("GET", origin + "/api/sessions/" + sessionId + "/transcript", null, 30);
                    foreach (var entry in Get(transcript, "entries") as JsonArray ?? new JsonArray())
                    {
                        var kind = Field(entry, "kind");
                        var text = Field(entry, "text");
                        DateTime? timestamp = null;
                        if (Truthy(Get(entry, "timestamp")) && DateTimeOffset.TryParse(Field(entry, "timestamp"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
                            timestamp = parsed.UtcDateTime;
                        if (Is(kind, "UserPrompt") && text.Length > 0 && (text.Contains(prompt, StringComparison.Ordinal) || text.Contains(marker, StringComparison.Ordinal)))
                        {
                            if (timestamp == null || timestamp.Value >= attemptFloor.AddSeconds(-5)) userPrompt = entry;
                        }
                        if (Is(kind, "AssistantText") && text.Contains(marker, StringComparison.Ordinal)) assistant = entry;
                        if (Is(kind, "TurnEnd") && userPrompt != null) turnEnd = entry;
                    }
                    if (userPrompt != null && assistant != null && turnEnd != null) break;
                    _host.Sleep(TimeSpan.FromSeconds(4));
                }
                if (userPrompt == null || assistant == null || turnEnd == null)
                {
                    _files.File.WriteAllText(_files.Path.Combine(evidenceRoot, "transcript-incomplete.json"), Pretty(transcript), Utf8);
                    RunnerDiagnostics();
                    throw new Failure("V-7 turn incomplete. userPrompt=" + (userPrompt != null ? "yes" : "no") + " assistantNonce=" + (assistant != null ? "yes" : "no")
                        + " turnEnd=" + (turnEnd != null ? "yes" : "no") + ".");
                }

                string updatesRelative = null;
                var sessionsMount = S("grokSessionsMount");
                if (_files.Directory.Exists(sessionsMount))
                {
                    var first = _files.Directory.EnumerateFiles(sessionsMount, "updates.jsonl", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true })
                        .FirstOrDefault();
                    if (first != null) updatesRelative = "sessions/" + first.Substring(sessionsMount.Length).TrimStart('\\', '/').Replace('\\', '/');
                }
                string imageId = null;
                try { imageId = FirstLine(Compose(true, "images", "-q", Service).Output); }
                catch (Exception e) when (e is Win32Exception || e is InvalidOperationException || e is IOException) { }
                string grokVersion = null;
                try { grokVersion = (Compose(true, GrokVersionProbe).Output ?? "").Trim(); }
                catch (Exception e) when (e is Win32Exception || e is InvalidOperationException || e is IOException) { }
                var sourceSha = (_host.RunGit(_repo, "rev-parse", "HEAD").Output ?? "").Trim();

                var evidence = new JsonObject
                {
                    ["sourceSha"] = sourceSha,
                    ["imageDigest"] = imageId,
                    ["grokVersion"] = grokVersion,
                    ["runnerId"] = S("runnerId"),
                    ["runnerStoreId"] = Get(status, "runnerStoreId")?.DeepClone(),
                    ["processBootId"] = Get(status, "processBootId")?.DeepClone(),
                    ["epoch"] = Get(status, "epoch")?.DeepClone(),
                    ["sessionId"] = sessionId,
                    ["acceptedGeneration"] = generation,
                    ["queueId"] = queueId,
                    ["attemptFloorUtc"] = attemptFloor.ToString("o", CultureInfo.InvariantCulture),
                    ["submittedBodyHash"] = Sha256(prompt),
                    ["nonce"] = nonce,
                    ["userPrompt"] = new JsonObject
                    {
                        ["uuid"] = Get(userPrompt, "uuid")?.DeepClone(),
                        ["sequence"] = Get(userPrompt, "sequence")?.DeepClone(),
                        ["textHash"] = Sha256(Field(userPrompt, "text")),
                        ["timestamp"] = Get(userPrompt, "timestamp")?.DeepClone(),
                    },
                    ["assistantText"] = new JsonObject
                    {
                        ["uuid"] = Get(assistant, "uuid")?.DeepClone(),
                        ["sequence"] = Get(assistant, "sequence")?.DeepClone(),
                        ["hasNonce"] = true,
                    },
                    ["turnEnd"] = new JsonObject
                    {
                        ["uuid"] = Get(turnEnd, "uuid")?.DeepClone(),
                        ["sequence"] = Get(turnEnd, "sequence")?.DeepClone(),
                        ["kind"] = Get(turnEnd, "kind")?.DeepClone(),
                    },
                    ["linuxUpdatesJsonl"] = updatesRelative,
                    ["localRunnerOrigin"] = S("localRunnerOrigin"),
                };
                var evidencePath = _files.Path.Combine(evidenceRoot, "v7-turn.json");
                _files.File.WriteAllText(evidencePath, Pretty(evidence), Utf8);
                Out.WriteLine("V-7 turn confirmed. Evidence: " + evidencePath);
                try
                {
                    Json("POST", agentUrl + "/stop", new JsonObject(), 30);
                    Out.WriteLine("Stopped standing session " + sessionId + " generation=" + generation);
                }
                catch (Failure e)
                {
                    Out.WriteLine("Stop after V-7 turn: " + e.Message);
                }
            }

            private void RunnerDiagnostics()
            {
                var evidenceRoot = _options.EvidenceRoot;
                var logPath = _files.Path.Combine(evidenceRoot, "container-logs.txt");
                try
                {
                    var logs = Compose(true, "logs", "--no-color", "--tail", "400");
                    _files.File.WriteAllText(logPath, (logs.Output ?? "") + (logs.Error ?? ""), Utf8);
                    Out.WriteLine("Wrote container logs to " + logPath);
                }
                catch (Exception e) when (e is Win32Exception || e is InvalidOperationException || e is IOException || e is UnauthorizedAccessException)
                {
                    Out.WriteLine("Could not capture container logs: " + e.Message);
                }
                CopyFiles(_files.Path.Combine(S("stateRoot"), "session-runner", "pty-hosts", "logs"), evidenceRoot);
                try
                {
                    var ptyCopy = _files.Path.Combine(evidenceRoot, "pty-hosts-tmp");
                    _files.Directory.CreateDirectory(ptyCopy);
                    Compose(true, "cp", Service + ":/tmp/antiphon-pty-hosts", ptyCopy);
                    var listing = Compose(true, "exec", "-T", Service, "sh", "-c", "ls -la /tmp/antiphon-pty* /tmp/CoreFxPipe_* 2>/dev/null; echo TMPDIR=$TMPDIR; echo tmp=$(ls -la /tmp | head)");
                    _files.File.WriteAllText(_files.Path.Combine(evidenceRoot, "container-tmp.txt"), listing.Output ?? "", Utf8);
                }
                catch (Exception e) when (e is Win32Exception || e is InvalidOperationException || e is IOException || e is UnauthorizedAccessException)
                {
                    // Optional diagnostics.
                }
                CopyFiles(_files.Path.Combine(S("stateRoot"), "runner-logs"), evidenceRoot);
            }

            private void CopyFiles(string from, string to)
            {
                if (!_files.Directory.Exists(from)) return;
                foreach (var file in _files.Directory.EnumerateFiles(from))
                {
                    try { _files.File.Copy(file, _files.Path.Combine(to, _files.Path.GetFileName(file)), true); }
                    catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { }
                }
            }

            // Everything this run started goes: the compose project (once its variables were set), the isolated processes, the
            // Postgres container, the server2 firewall rule and every bin-card0490-v7* build output under the checkout.
            private void CleanUp(string pgName, bool pgStarted)
            {
                if (_composeEnvironment.Count > 0)
                {
                    try { Compose(true, "down", "--remove-orphans"); }
                    catch (Exception e) when (e is Win32Exception || e is InvalidOperationException || e is IOException) { }
                }
                foreach (var child in new[] { _server, _runner })
                {
                    if (child == null) continue;
                    try
                    {
                        if (!child.HasExited) child.Stop();
                    }
                    catch (Exception e) when (e is Win32Exception || e is InvalidOperationException) { }
                    child.Dispose();
                }
                _server = null;
                _runner = null;
                if (pgStarted)
                {
                    try { _platform.Run("docker", new[] { "rm", "-f", pgName }, null, capture: true); }
                    catch (Exception e) when (e is Win32Exception || e is InvalidOperationException || e is IOException) { }
                }
                if (S("placement").Equals("server2", StringComparison.OrdinalIgnoreCase) && !Blank(_config["serverPort"]))
                {
                    try { _platform.Run("netsh", new[] { "advfirewall", "firewall", "delete", "rule", "name=" + FirewallRule }, null, capture: true); }
                    catch (Exception e) when (e is Win32Exception || e is InvalidOperationException || e is IOException) { }
                }
                List<string> outputs;
                try
                {
                    outputs = _files.Directory.EnumerateDirectories(_repo, "bin-card0490-v7*",
                        new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint }).ToList();
                }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
                {
                    outputs = new List<string>();
                }
                foreach (var directory in outputs)
                {
                    try
                    {
                        if (_files.Directory.Exists(directory)) _files.Directory.Delete(directory, true);
                    }
                    catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { }
                }
            }

            private PhoneHomeGrokRun Compose(bool capture, params string[] arguments)
            {
                var all = new List<string> { "compose", "-f", _composeFile, "-p", _composeProject };
                all.AddRange(arguments);
                return _platform.Run("docker", all, _composeEnvironment, capture);
            }

            // Invoke-IsolatedJson: one request; its JSON answer (null when empty), or a Failure naming the method, the URL and the detail.
            private JsonNode Json(string method, string url, JsonNode body, int timeoutSec)
            {
                string text;
                try
                {
                    text = _platform.Request(method, url, body?.ToJsonString(), timeoutSec);
                }
                catch (PhoneHomeGrokHttpException e)
                {
                    throw new Failure(method + " " + url + " failed: " + e.Message);
                }
                if (string.IsNullOrWhiteSpace(text)) return null;
                try
                {
                    return JsonNode.Parse(text);
                }
                catch (JsonException e)
                {
                    throw new Failure(method + " " + url + " failed: the answer is not JSON (" + e.Message + ")");
                }
            }
        }

        // ---- values ----

        private static bool Matches(string text, string pattern) =>
            Regex.IsMatch(text ?? "", pattern, RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        private static JsonNode Get(JsonNode node, string key) => node is JsonObject obj && obj.TryGetPropertyValue(key, out var value) ? value : null;

        private static string Field(JsonNode node, string key) => Text(Get(node, key));

        private static bool Is(string value, string expected) => string.Equals(value, expected, StringComparison.OrdinalIgnoreCase);

        // [string] of a value: a string, a number's text, True or False, "" for null, an object's JSON.
        private static string Text(JsonNode node) => Kind(node) switch
        {
            JsonValueKind.Null => "",
            JsonValueKind.Undefined => "",
            JsonValueKind.String => node.GetValue<string>(),
            JsonValueKind.True => "True",
            JsonValueKind.False => "False",
            _ => node.ToJsonString(),
        };

        private static bool Blank(JsonNode node) => Kind(node) == JsonValueKind.Null || (Kind(node) == JsonValueKind.String && Text(node).Length == 0);

        private static bool IsZeroOrFalse(JsonNode node) =>
            Kind(node) == JsonValueKind.False || (Kind(node) == JsonValueKind.Number && decimal.TryParse(Text(node), NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && number == 0);

        private static bool IsTrue(JsonNode node) => Kind(node) == JsonValueKind.True || (Kind(node) == JsonValueKind.String && Is(Text(node), "true"));

        // PowerShell truthiness of a value: null, "", 0 and false are false.
        private static bool Truthy(JsonNode node)
        {
            if (node == null) return false;
            return Kind(node) switch
            {
                JsonValueKind.Null => false,
                JsonValueKind.False => false,
                JsonValueKind.True => true,
                JsonValueKind.String => Text(node).Length > 0,
                JsonValueKind.Number => !IsZeroOrFalse(node),
                _ => true,
            };
        }

        private static JsonValueKind Kind(JsonNode node) => node?.GetValueKind() ?? JsonValueKind.Null;

        private static int IntOf(JsonNode node, string name)
        {
            if (int.TryParse(Text(node), NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)) return number;
            throw new Failure(name + " is not a port number (" + Text(node) + ").");
        }

        private static string Pretty(JsonNode node) => (node == null ? "null" : node.ToJsonString(Indented)) + Environment.NewLine;

        private static string FirstLine(string text) => (text ?? "").Replace("\r\n", "\n").Split('\n').Select(line => line.Trim()).FirstOrDefault(line => line.Length > 0);

        private static string Sha256(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text ?? ""))).ToLowerInvariant();

        // ---- production ----

        /// <summary>The production platform: the real file system, loopback sockets, processes and HttpClient.</summary>
        public sealed class Machine : IPhoneHomeGrokPlatform
        {
            /// <summary>The platform over the file system.</summary>
            public Machine(IFileSystem files) => Files = files ?? throw new ArgumentNullException(nameof(files));

            /// <inheritdoc />
            public IFileSystem Files { get; }

            /// <inheritdoc />
            public int FreeLoopbackPort()
            {
                var listener = new TcpListener(IPAddress.Loopback, 0);
                listener.Start();
                try { return ((IPEndPoint)listener.LocalEndpoint).Port; }
                finally { listener.Stop(); }
            }

            /// <inheritdoc />
            public PhoneHomeGrokRun Run(string file, IReadOnlyList<string> arguments, IReadOnlyDictionary<string, string> environment, bool capture)
            {
                var info = new ProcessStartInfo(file) { UseShellExecute = false, RedirectStandardOutput = capture, RedirectStandardError = capture };
                foreach (var argument in arguments ?? Array.Empty<string>()) info.ArgumentList.Add(argument ?? "");
                foreach (var pair in environment ?? new Dictionary<string, string>()) info.Environment[pair.Key] = pair.Value;
                if (capture)
                {
                    info.StandardOutputEncoding = Utf8;
                    info.StandardErrorEncoding = Utf8;
                }
                using var process = Process.Start(info) ?? throw new InvalidOperationException("the command did not start: " + file);
                var error = capture ? process.StandardError.ReadToEndAsync() : null;
                var output = capture ? process.StandardOutput.ReadToEnd() : "";
                process.WaitForExit();
                return new PhoneHomeGrokRun(process.ExitCode, output, error?.Result ?? "");
            }

            /// <inheritdoc />
            public IPhoneHomeGrokChild StartDll(string dll, string workingDirectory, IReadOnlyDictionary<string, string> environment)
            {
                var info = new ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = workingDirectory ?? "" };
                info.ArgumentList.Add(dll);
                foreach (var pair in environment ?? new Dictionary<string, string>()) info.Environment[pair.Key] = pair.Value;
                return new Child(Process.Start(info) ?? throw new InvalidOperationException("dotnet did not start: " + dll));
            }

            /// <inheritdoc />
            public int Status(string url, int timeoutSec)
            {
                try
                {
                    using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(timeoutSec) };
                    using var answer = client.GetAsync(url).GetAwaiter().GetResult();
                    return (int)answer.StatusCode;
                }
                catch (Exception e) when (e is HttpRequestException || e is TaskCanceledException || e is InvalidOperationException)
                {
                    return 0;
                }
            }

            /// <inheritdoc />
            public string Request(string method, string url, string jsonBody, int timeoutSec)
            {
                try
                {
                    using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(timeoutSec) };
                    using var request = new HttpRequestMessage(new HttpMethod(method), url);
                    request.Headers.Accept.ParseAdd("application/json");
                    if (jsonBody != null) request.Content = new StringContent(jsonBody, Utf8, "application/json");
                    using var answer = client.SendAsync(request).GetAwaiter().GetResult();
                    var body = answer.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                    if (!answer.IsSuccessStatusCode)
                        throw new PhoneHomeGrokHttpException(string.IsNullOrWhiteSpace(body) ? "HTTP " + ((int)answer.StatusCode).ToString(CultureInfo.InvariantCulture) + " " + answer.ReasonPhrase : body);
                    return body;
                }
                catch (Exception e) when (e is HttpRequestException || e is TaskCanceledException || e is InvalidOperationException || e is UriFormatException)
                {
                    throw new PhoneHomeGrokHttpException(e.Message);
                }
            }

            private sealed class Child : IPhoneHomeGrokChild
            {
                private readonly Process _process;

                public Child(Process process) => _process = process;

                public int Id => _process.Id;

                public bool HasExited => _process.HasExited;

                public int ExitCode => _process.ExitCode;

                public void Stop()
                {
                    try { _process.Kill(); }
                    catch (InvalidOperationException) { }
                    try { _process.WaitForExit(5000); }
                    catch (InvalidOperationException) { }
                }

                public void Dispose() => _process.Dispose();
            }
        }
    }
}
