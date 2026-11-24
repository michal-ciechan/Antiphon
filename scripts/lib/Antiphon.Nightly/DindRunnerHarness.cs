// CARD-0604 local harness (evidence tooling, not product; Docker Desktop only). It replaced scripts/verify-card0604-dind-runner.ps1
// (unit 47 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md, W8 slice B; nothing started the script by path). The
// CLI runs it as
//
//   antiphon-scripts verify-card0604-dind-runner --image <tag> [--build] [--containment] [--port 18298] [--evidence-root <dir>]
//
// It boots the privileged session-testing image with its nested dockerd and grades D-1, D-3, D-4, D-5 and D-8's plumbing without
// server2, GitHub or the production server: steps 1-11 are Cut A, --containment adds Cut B's steps 12-15 (V-28, cgroup v2 half).
// It never binds 17202-17205 (a production port is refused before anything else, exit 2), never touches a server2 resource, and
// names only its own container c604-<stamp> and volume c604-<stamp>-dind (removed with `rm -f` / `volume rm -f` by name, never a
// prune or a project sweep). Each step's docker command and output go to <evidence-root>/<stamp>/<name>.txt (evidence root
// default .antiphon/c604-harness under the checkout). The throwaway key (ssh-keygen, empty passphrase), phone-home secret and git
// identity are written there, registered nowhere, mounted read-only and deleted at the end. Claude is probed as uid 1654 against a
// fresh store with no credential of any kind (CARD-0628 G-4): version 2.1.327 and the signed-out verdict. The launch grade is the
// HTTP status of POST /sessions (2xx), never its elapsed time (CARD-1178), and /tmp/state is uid 1654's before the POST.
//
// Lines: "C604 HARNESS NOTE: <reason>" (when there is one), "C604 HARNESS: image=... nested=... egress=... loopback=...
// launchMs=... key=... restart=... refusal=... containment=... claude=... claudeAuth=...", "C604 HARNESS EXIT CODE: <n>".
// Exit 0 every graded observation is ok, 1 one or more are not, 2 setup failed (refused port, no Docker, failed build or run,
// unhealthy container). A setup failure after the container started still runs step 10's three refusals and step 11's cleanup,
// as the script's finally block did. docker, ssh-keygen and git run on the host's launcher; HTTP, the secret bytes and the
// session id are DindRunnerSeams; the clock and waits are the host's, files IFileSystem's. Plain C#: no PowerShell types. ASCII only.
#nullable disable
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;

namespace Antiphon.Nightly
{
    /// <summary>The verify-card0604-dind-runner command's options (verify-card0604-dind-runner.ps1's parameters).</summary>
    public sealed class DindRunnerOptions
    {
        /// <summary>The CLI's usage line.</summary>
        public const string Usage = "verify-card0604-dind-runner --image <tag> [--build] [--containment] [--port 18298] [--evidence-root .antiphon/c604-harness]";

        /// <summary>The evidence root when none is given, under the checkout.</summary>
        public const string DefaultEvidenceRoot = ".antiphon/c604-harness";

        /// <summary>The image to boot (built first with Build).</summary>
        public string Image { get; init; } = "";

        /// <summary>Build the session-testing target of docker/session-runner-grok/Dockerfile at HEAD first.</summary>
        public bool Build { get; init; }

        /// <summary>Grade Cut B's containment (steps 12-15).</summary>
        public bool Containment { get; init; }

        /// <summary>The host port the runner's 8080 is published on.</summary>
        public int Port { get; init; } = 18298;

        /// <summary>Where each run's evidence directory goes (relative: under the checkout).</summary>
        public string EvidenceRoot { get; init; } = DefaultEvidenceRoot;

        /// <summary>The checkout: the build context and the base of a relative evidence root.</summary>
        public string Checkout { get; init; } = "";

        /// <summary>The arguments after the command name; checkout is the CLI's own.</summary>
        public static DindRunnerOptions Parse(IReadOnlyList<string> arguments, string checkout)
        {
            var flags = NightlyArgs.Parse(arguments, new[] { "--image", "--port", "--evidence-root" }, new[] { "--build", "--containment" });
            return new DindRunnerOptions
            {
                Image = flags.Required("--image"),
                Build = flags.Switch("--build"),
                Containment = flags.Switch("--containment"),
                Port = flags.Int("--port", 18298),
                EvidenceRoot = flags.Value("--evidence-root", DefaultEvidenceRoot),
                Checkout = checkout ?? "",
            };
        }
    }

    /// <summary>One HTTP answer of the harness: the status code and the body text.</summary>
    public sealed record DindHttpAnswer(int Status, string Body);

    /// <summary>One request to the runner (method, URL, JSON body or null, timeout); a transport failure throws.</summary>
    public delegate DindHttpAnswer DindHttp(string method, string url, string body, TimeSpan timeout);

    /// <summary>The harness's seams beyond the host: the runner's HTTP, the throwaway secret's bytes and the session id.</summary>
    public sealed class DindRunnerSeams
    {
        /// <summary>The runner's HTTP (production: DindRunnerHarness.Http).</summary>
        public DindHttp Http { get; init; }

        /// <summary>The 32 bytes of the throwaway phone-home secret.</summary>
        public Func<byte[]> Secret { get; init; } = () => RandomNumberGenerator.GetBytes(32);

        /// <summary>The POST /sessions session id.</summary>
        public Func<string> SessionId { get; init; } = () => Guid.NewGuid().ToString();
    }

    /// <summary>The CARD-0604 Docker Desktop harness.</summary>
    public static class DindRunnerHarness
    {
        /// <summary>The local stack's ports: refused before anything runs.</summary>
        public static readonly IReadOnlyList<int> ForbiddenPorts = new[] { 17202, 17203, 17204, 17205 };

        /// <summary>The pinned Claude Code version the image must carry (CARD-0628).</summary>
        public const string ClaudeVersion = "2.1.327";

        /// <summary>The Dockerfile of the image, relative to the checkout.</summary>
        public const string Dockerfile = "docker/session-runner-grok/Dockerfile";

        /// <summary>Step 13: the helpers' owners and modes, then the sudo grant.</summary>
        public const string CustodyScript =
            "stat -c %U:%G:%a /usr/local/bin/antiphon-custody-enter /usr/local/bin/antiphon-custody-kill /etc/sudoers.d/antiphon-custody; sudo -n -l";

        /// <summary>
        /// Step 15 (CARD-1614): executions counted inside namespaces, on v2 or on both v1 controllers (pids and freezer); a
        /// surviving cgroup would fail the next execution's "exists and is empty" precondition for a reason nobody could see.
        /// </summary>
        public const string ResidueScript =
            "source /usr/local/lib/antiphon-custody-common.sh || exit 1; ns=$(custody_read_namespace) || exit 1; "
            + "if [ -f /sys/fs/cgroup/cgroup.controllers ]; then custody_count_execution_trees /sys/fs/cgroup/antiphon-custody \"$ns\" || exit 1; "
            + "else custody_count_execution_trees /sys/fs/cgroup/pids/antiphon-custody \"$ns\" || exit 1; printf \"controller=freezer\\n\"; "
            + "custody_count_execution_trees /sys/fs/cgroup/freezer/antiphon-custody \"$ns\" || exit 1; fi";

        /// <summary>
        /// Step 6: a nested mapped port answers on the runner's own loopback. `docker exec -u 1654:1654` grants no supplementary
        /// groups, so the probe runs as 1654:1656 (group docker-nested), the closest credentials docker exec can express.
        /// </summary>
        public const string LoopbackScript = "set -e; id=$(docker run -d -p 127.0.0.1::80 nginx:alpine); "
            + "p=$(docker port $id 80 | head -n1 | sed \"s/.*://\"); sleep 2; curl -fsS -o /dev/null -w %{http_code} http://127.0.0.1:$p/; "
            + "echo; docker rm -f \"$id\" >/dev/null";

        /// <summary>Runs the harness. Returns the exit code; usage errors throw NightlyUsageException.</summary>
        public static int Run(DindRunnerOptions options, NightlyHost host, IFileSystem files, DindRunnerSeams seams)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (host == null) throw new ArgumentNullException(nameof(host));
            if (files == null) throw new ArgumentNullException(nameof(files));
            if (seams?.Http == null) throw new ArgumentNullException(nameof(seams));
            if (string.IsNullOrEmpty(options.Image)) throw new NightlyUsageException("missing --image");
            return new DindRun(options, host, files, seams).Execute();
        }

        /// <summary>The production HTTP: no proxy, the request's own timeout, a JSON body as application/json.</summary>
        public static DindHttp Http()
        {
            var client = new HttpClient(new SocketsHttpHandler { UseProxy = false }) { Timeout = Timeout.InfiniteTimeSpan };
            return (method, url, body, timeout) =>
            {
                using var cancel = new CancellationTokenSource(timeout);
                using var request = new HttpRequestMessage(new HttpMethod(method), url);
                if (body != null) request.Content = new StringContent(body, Encoding.UTF8, "application/json");
                using var response = client.SendAsync(request, cancel.Token).GetAwaiter().GetResult();
                var text = response.Content.ReadAsStringAsync(cancel.Token).GetAwaiter().GetResult();
                return new DindHttpAnswer((int)response.StatusCode, text ?? "");
            };
        }

        private sealed record DockerResult(int ExitCode, string Output);

        // Exit-Harness inside the guarded steps: the summary is printed, then the refusals and cleanup still run.
        private sealed class HarnessExit : Exception
        {
            public HarnessExit(int code) : base("exit " + code.ToString(CultureInfo.InvariantCulture)) { Code = code; }

            public int Code { get; }
        }

        private sealed class DindRun
        {
            private const RegexOptions Match = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
            private readonly DindRunnerOptions _o;
            private readonly NightlyHost _host;
            private readonly IFileSystem _files;
            private readonly DindRunnerSeams _seams;
            private string _evidenceDir = "";
            private string _container = "";
            private string _volume = "";

            // Graded observations: "unknown" survives a partial run so the result line stays legible.
            private string _nested = "unknown";
            private string _egress = "unknown";
            private string _loopback = "unknown";
            private int _launchMs = -1;
            private int _launchStatus;
            private string _keyState = "unknown";
            private string _restartState = "unknown";
            private string _refusalState = "unknown";
            private string _claudeVersion = "unknown";
            private string _claudeAuth = "unknown";
            private string _containmentState;

            public DindRun(DindRunnerOptions options, NightlyHost host, IFileSystem files, DindRunnerSeams seams)
            {
                _o = options;
                _host = host;
                _files = files;
                _seams = seams;
                _containmentState = options.Containment ? "unknown" : "skipped";
            }

            private string Url(string path) => "http://127.0.0.1:" + _o.Port.ToString(CultureInfo.InvariantCulture) + path;

            public int Execute()
            {
                if (ForbiddenPorts.Contains(_o.Port))
                {
                    _host.Out.WriteLine("C604 HARNESS NOTE: refusing production port " + _o.Port.ToString(CultureInfo.InvariantCulture) + " (17202-17205 are the local stack).");
                    _host.Out.WriteLine("C604 HARNESS EXIT CODE: 2");
                    return 2;
                }
                var stamp = _host.UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
                _container = "c604-" + stamp;
                _volume = "c604-" + stamp + "-dind";
                var evidenceRoot = string.IsNullOrEmpty(_o.EvidenceRoot) ? DindRunnerOptions.DefaultEvidenceRoot : _o.EvidenceRoot;
                var rootPath = _files.Path.IsPathRooted(evidenceRoot) ? evidenceRoot : _files.Path.Combine(_o.Checkout, evidenceRoot);
                _evidenceDir = _files.Path.Combine(rootPath, stamp);
                _files.Directory.CreateDirectory(_evidenceDir);

                if (Docker(new[] { "version", "--format", "{{.Server.Version}}" }, "docker-version.txt").ExitCode != 0)
                    return Summary(2, "Docker daemon is not reachable. Start Docker Desktop and retry.");

                // Step 1: the optional build, in the foreground, from the checkout's HEAD.
                if (_o.Build)
                {
                    var head = _host.RunGit(_o.Checkout, "rev-parse", "HEAD").Output.Trim();
                    _host.Out.WriteLine("C604 HARNESS: building " + _o.Image + " (session-testing) from " + head);
                    var build = Docker(new[] { "build", "-f", Dockerfile, "--target", "session-testing", "--build-arg", "SOURCE_REVISION=" + head, "-t", _o.Image, _o.Checkout },
                        "docker-build.txt", _o.Checkout);
                    if (build.ExitCode != 0) return Summary(2, "docker build failed; see " + _evidenceDir + "/docker-build.txt");
                }

                // Step 2: throwaway credentials, registered nowhere.
                var keyPath = _files.Path.Combine(_evidenceDir, "throwaway_key");
                var secretPath = _files.Path.Combine(_evidenceDir, "throwaway_secret");
                Launch("ssh-keygen", new[] { "-t", "ed25519", "-N", "", "-C", "c604-harness-throwaway", "-f", keyPath });
                if (!_files.File.Exists(keyPath)) return Summary(2, "ssh-keygen did not produce a throwaway key.");
                _files.File.WriteAllText(secretPath, string.Concat(_seams.Secret().Select(b => b.ToString("x2", CultureInfo.InvariantCulture))), Encoding.ASCII);
                // CARD-0631: the runner git identity is a mounted file the entrypoint refuses to boot without.
                var identityPath = _files.Path.Combine(_evidenceDir, "throwaway_gitconfig");
                _files.File.WriteAllText(identityPath, "[user]\n\tname = c604-harness\n\temail = c604-harness@localhost\n", Encoding.ASCII);
                if (Docker(new[] { "volume", "create", _volume }, "volume-create.txt").ExitCode != 0) return Summary(2, "docker volume create failed.");

                var started = false;
                var exit = -1;
                try
                {
                    try
                    {
                        Steps(keyPath, secretPath, identityPath, ref started);
                    }
                    finally
                    {
                        Refusals(keyPath, secretPath);
                        Cleanup(started, keyPath, secretPath, identityPath);
                    }
                }
                catch (HarnessExit e)
                {
                    exit = e.Code;
                }
                if (exit >= 0) return exit;

                // Elapsed time stays in the summary. The grade is the HTTP status.
                var graded = new SortedDictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
                {
                    ["nested"] = _nested == "yes",
                    ["egress"] = _egress == "yes",
                    ["loopback"] = _loopback == "200",
                    ["launch"] = _launchStatus >= 200 && _launchStatus < 300,
                    ["key"] = _keyState == "ok",
                    ["restart"] = _restartState == "ok",
                    ["refusal"] = _refusalState == "ok",
                    ["claude"] = _claudeVersion == ClaudeVersion,
                    ["claudeAuth"] = _claudeAuth == "logged-out",
                };
                if (_o.Containment) graded["containment"] = _containmentState == "ok";
                WriteEvidence("graded.txt", string.Join("\n", graded.Select(pair => pair.Key + "=" + (pair.Value ? "True" : "False"))));
                var failed = graded.Where(pair => !pair.Value).Select(pair => pair.Key).ToArray();
                if (failed.Length > 0) return Summary(1, "observations not ok: " + string.Join(",", failed));
                return Summary(0, "");
            }

            private void Steps(string keyPath, string secretPath, string identityPath, ref bool started)
            {
                // Step 3: the privileged container.
                var run = Docker(new[]
                {
                    "run", "-d", "--privileged", "--init", "--restart", "unless-stopped", "--name", _container,
                    "-p", _o.Port.ToString(CultureInfo.InvariantCulture) + ":8080",
                    "-v", _volume + ":/var/lib/docker",
                    "--tmpfs", "/run/antiphon",
                    "-v", keyPath + ":/run/secrets/antiphon-deploy-key:ro",
                    "-v", secretPath + ":/run/secrets/phone-home:ro",
                    "-v", identityPath + ":/run/antiphon/gitconfig:ro",
                    "-e", "ANTIPHON_DEPLOY_KEY_SOURCE=/run/secrets/antiphon-deploy-key",
                    "-e", "PhoneHome__SecretPath=/run/secrets/phone-home",
                    "-e", "ANTIPHON_DOCKERD_LOG_DIR=/tmp/state/logs",
                    "-e", "SessionRunner__SessionLogPath=/tmp/state/session-runner",
                    "-e", "SessionRunner__PtyHostDir=/tmp/antiphon-pty-hosts",
                    "-e", "Serilog__LogPath=/tmp/state/runner-logs",
                    "-e", "CLAUDE_CONFIG_DIR=/tmp/state/claude",
                    "-e", "PhoneHome__ClaudeHome=/tmp/state/claude",
                    "-e", "PhoneHome__Enabled=false",
                    "-e", "SessionRunner__Herdr__Enabled=false",
                    "-e", "TMPDIR=/tmp",
                    _o.Image,
                }, "docker-run.txt");
                if (run.ExitCode != 0) Exit(2, "docker run failed; see " + _evidenceDir + "/docker-run.txt");
                started = true;

                if (!WaitHealthy(TimeSpan.FromSeconds(90), TimeSpan.FromMilliseconds(500)))
                {
                    Docker(new[] { "logs", _container }, "container-logs-unhealthy.txt");
                    Exit(2, "container never became healthy within 90s; see " + _evidenceDir + "/container-logs-unhealthy.txt");
                }

                // Step 4: the daemon is this container's own, not a sibling.
                var info = Docker(new[] { "exec", _container, "docker", "info", "--format", "{{.Name}} {{.Driver}} {{.CgroupVersion}}" }, "nested-info.txt");
                var hostname = Docker(new[] { "exec", _container, "hostname" }, "nested-hostname.txt");
                var daemonName = Regex.Split(info.Output, @"\s+")[0];
                var ownHostname = hostname.Output.Trim();
                _nested = info.ExitCode == 0 && daemonName.Length > 0 && string.Equals(daemonName, ownHostname, StringComparison.OrdinalIgnoreCase) ? "yes" : "no";

                // Step 5: nested pull, NAT and TLS egress.
                var zen = Docker(new[] { "exec", _container, "docker", "run", "--rm", "alpine:3.20", "wget", "-qO-", "https://api.github.com/zen" }, "nested-egress.txt");
                _egress = zen.ExitCode == 0 && zen.Output.Trim().Length > 0 ? "yes" : "no";

                // Step 6: a nested mapped port answers on the runner's own loopback, as the app uid.
                var loop = Docker(new[] { "exec", "-u", "1654:1656", _container, "sh", "-c", LoopbackScript }, "nested-loopback.txt");
                var loopMatch = Regex.Match(loop.Output, @"(?m)^(\d{3})\s*$", Match);
                _loopback = loopMatch.Success ? loopMatch.Groups[1].Value : "no";

                // CARD-1178: /tmp/state must be uid 1654's, or SessionLogPath cannot be created and POST /sessions answers 500.
                var stateDir = Docker(new[] { "exec", _container, "sh", "-c", "mkdir -p /tmp/state && chown 1654:1654 /tmp/state" }, "state-dir.txt");
                if (stateDir.ExitCode != 0) Exit(2, "could not prepare /tmp/state for uid 1654; see " + _evidenceDir + "/state-dir.txt");

                // Step 7: a real session launch through the runner's own POST /sessions.
                var body = new JsonObject
                {
                    ["sessionId"] = _seams.SessionId(),
                    ["exe"] = "/bin/bash",
                    ["args"] = new JsonArray(),
                    ["env"] = new JsonObject(),
                    ["cwd"] = "/tmp",
                    ["cols"] = 120,
                    ["rows"] = 30,
                }.ToJsonString();
                var postStarted = _host.UtcNow;
                string launchBody;
                try
                {
                    var response = _seams.Http("POST", Url("/sessions"), body, TimeSpan.FromSeconds(180));
                    _launchStatus = response.Status;
                    launchBody = response.Body ?? "";
                }
                catch (Exception e) when (IsTransport(e))
                {
                    launchBody = e.Message;
                }
                _launchMs = (int)(_host.UtcNow - postStarted).TotalMilliseconds;
                WriteEvidence("post-sessions.txt", "status=" + _launchStatus.ToString(CultureInfo.InvariantCulture) + " elapsedMs="
                    + _launchMs.ToString(CultureInfo.InvariantCulture) + "\n" + launchBody);

                // Step 7b (CARD-0628): the pinned claude as the app uid against a fresh store, with no credential of any kind.
                // docker exec does not see the entrypoint's exports, so HOME and the store are named explicitly.
                Docker(new[] { "exec", _container, "sh", "-c", "mkdir -p /tmp/state/claude && chown 1654:1654 /tmp/state/claude" }, "claude-store-create.txt");
                string[] claudeEnv = { "-u", "1654:1654", "-e", "HOME=/home/app", "-e", "CLAUDE_CONFIG_DIR=/tmp/state/claude", "-e", "DISABLE_AUTOUPDATER=1" };
                var claudeVer = Docker(new[] { "exec" }.Concat(claudeEnv).Concat(new[] { _container, "claude", "--version" }).ToArray(), "claude-version.txt");
                var claudeVerMatch = Regex.Match(claudeVer.Output, @"(\d+\.\d+\.\d+)");
                _claudeVersion = claudeVer.ExitCode == 0 && claudeVerMatch.Success ? claudeVerMatch.Groups[1].Value : "no";
                var claudeStatus = Docker(new[] { "exec" }.Concat(claudeEnv).Concat(new[] { _container, "claude", "auth", "status", "--json" }).ToArray(), "claude-auth-status.txt");
                _claudeAuth = claudeStatus.ExitCode == 1 && Regex.IsMatch(claudeStatus.Output, "\"loggedIn\"\\s*:\\s*false", Match) ? "logged-out" : "unexpected";

                // Step 8: the deploy key is materialised for the app uid, and ssh resolves the config.
                var stat = Docker(new[] { "exec", "-u", "1654:1654", _container, "stat", "-c", "%u %a", "/run/antiphon/deploy-key" }, "deploy-key-stat.txt");
                var sshG = Docker(new[] { "exec", "-u", "1654:1654", _container, "ssh", "-F", "/etc/antiphon/ssh_config", "-G", "github.com" }, "ssh-config-resolved.txt");
                var keyOk = stat.ExitCode == 0 && string.Equals(stat.Output.Trim(), "1654 400", StringComparison.OrdinalIgnoreCase);
                var sshOk = sshG.ExitCode == 0
                    && Regex.IsMatch(sshG.Output, @"(?m)^hostname ssh\.github\.com", Match)
                    && Regex.IsMatch(sshG.Output, @"(?m)^port 443", Match)
                    && Regex.IsMatch(sshG.Output, @"(?m)^identitiesonly yes", Match)
                    && Regex.IsMatch(sshG.Output, @"(?im)^identityfile /run/antiphon/deploy-key", Match);
                _keyState = keyOk && sshOk ? "ok" : "no";

                // Step 9: a dead daemon takes the container down and the policy brings it back; the nested store is a named
                // volume, so the images pulled above must still be there (a cheap restart, not a full re-pull).
                var imagesBefore = ImageSet(Docker(new[] { "exec", _container, "docker", "images", "-q" }, "nested-images-before-restart.txt"));
                var beforeCount = RestartCount(Docker(new[] { "inspect", "-f", "{{.RestartCount}}", _container }, "restart-count-before.txt"));
                Docker(new[] { "exec", _container, "sh", "-c", "kill -TERM $(pidof dockerd)" }, "kill-dockerd.txt");
                var restartDeadline = _host.UtcNow.AddSeconds(90);
                var restarted = false;
                while (_host.UtcNow < restartDeadline)
                {
                    if (RestartCount(Docker(new[] { "inspect", "-f", "{{.RestartCount}}", _container }, "")) > beforeCount)
                    {
                        restarted = true;
                        break;
                    }
                    _host.Sleep(TimeSpan.FromSeconds(2));
                }
                var backHealthy = restarted && WaitHealthy(TimeSpan.FromSeconds(90), TimeSpan.FromSeconds(2));
                var retained = ImageSet(Docker(new[] { "exec", _container, "docker", "images", "-q" }, "nested-images-after-restart.txt"));
                var imagesRetained = imagesBefore.Length > 0 && string.Equals(retained, imagesBefore, StringComparison.OrdinalIgnoreCase);
                _restartState = restarted && backHealthy && imagesRetained ? "ok" : "no";
                WriteEvidence("restart-summary.txt", "restarted=" + Bool(restarted) + " backHealthy=" + Bool(backHealthy) + " imagesRetained=" + Bool(imagesRetained)
                    + "\nbefore=" + imagesBefore + "\nafter=" + retained);

                // Steps 12-15: Cut B containment, V-28 (cgroup v2 here; server2 measures the v1 freezer path through the same
                // probe, CP-15). Graded, so an unmeasured containment fails the harness.
                if (_o.Containment) Containment();
            }

            private void Containment()
            {
                // Step 12: the runner advertises the Linux backend at all, else the probe inside the image failed.
                var capabilities = Docker(new[] { "exec", "--user", "1654:1654", _container, "curl", "-fsS", "http://127.0.0.1:8080/capabilities" }, "containment-capabilities.txt");
                // Step 13: the helpers are root-owned and the sudo grant is exactly two commands.
                var custody = Docker(new[] { "exec", "--user", "1654:1654", _container, "/bin/sh", "-c", CustodyScript }, "containment-custody.txt");
                // Step 14: the four measurements, as uid 1654 against the real helpers.
                var probe = Docker(new[] { "exec", "--user", "1654:1654", _container, "/usr/local/bin/antiphon-custody-containment-probe" }, "containment-probe.txt");
                // Step 15: nothing is left behind.
                var residue = Docker(new[] { "exec", "--user", "1654:1654", _container, "bash", "-c", ResidueScript }, "containment-residue.txt");

                var ownershipOk = Regex.IsMatch(custody.Output, "root:root:755", Match) && Regex.IsMatch(custody.Output, "root:root:440", Match);
                var grantOk = Regex.IsMatch(custody.Output, "antiphon-custody-enter", Match) && Regex.IsMatch(custody.Output, "antiphon-custody-kill", Match);
                var advertised = Regex.IsMatch(capabilities.Output, "linux-cgroup-v1", Match) && !Regex.IsMatch(capabilities.Output, "windows-job-v1", Match);
                var probeOk = probe.ExitCode == 0 && Regex.IsMatch(probe.Output, "containment=ok", Match);
                var residueOk = residue.ExitCode == 0
                    && Regex.IsMatch(residue.Output, "(?m)^status=ok$", Match)
                    && Regex.IsMatch(residue.Output, "(?m)^current=0$", Match)
                    && !Regex.IsMatch(residue.Output, "(?m)^current=[1-9]", Match)
                    && !Regex.IsMatch(residue.Output, "(?m)^foreign=[1-9]", Match)
                    && !Regex.IsMatch(residue.Output, "(?m)^legacy=[1-9]", Match)
                    && !Regex.IsMatch(residue.Output, "(?m)^status=read-error$", Match);
                _containmentState = advertised && ownershipOk && grantOk && probeOk && residueOk ? "ok" : "no";
                WriteEvidence("containment.txt", "advertised=" + Bool(advertised) + "\nownership=" + Bool(ownershipOk) + "\ngrant=" + Bool(grantOk)
                    + "\nprobe=" + Bool(probeOk) + "\nresidue=" + Bool(residueOk));
            }

            // Step 10: the three refusals, each a fresh throwaway container of the image (exit 3 and the named reason).
            private void Refusals(string keyPath, string secretPath)
            {
                var noKey = Docker(new[]
                {
                    "run", "--rm", "--privileged", "--init",
                    "-e", "ANTIPHON_DEPLOY_KEY_SOURCE=/run/secrets/antiphon-deploy-key",
                    "-e", "PhoneHome__SecretPath=/run/secrets/phone-home",
                    _o.Image,
                }, "refusal-no-key.txt");
                var noSecret = Docker(new[]
                {
                    "run", "--rm", "--privileged", "--init",
                    "-v", keyPath + ":/run/secrets/antiphon-deploy-key:ro",
                    "-e", "ANTIPHON_DEPLOY_KEY_SOURCE=/run/secrets/antiphon-deploy-key",
                    "-e", "PhoneHome__SecretPath=/run/secrets/phone-home",
                    _o.Image,
                }, "refusal-no-secret.txt");
                var notPrivileged = Docker(new[]
                {
                    "run", "--rm", "--init",
                    "-v", keyPath + ":/run/secrets/antiphon-deploy-key:ro",
                    "-v", secretPath + ":/run/secrets/phone-home:ro",
                    "-e", "ANTIPHON_DEPLOY_KEY_SOURCE=/run/secrets/antiphon-deploy-key",
                    "-e", "PhoneHome__SecretPath=/run/secrets/phone-home",
                    _o.Image,
                }, "refusal-not-privileged.txt");
                var refusalOk = noKey.ExitCode == 3 && Regex.IsMatch(noKey.Output, "DeployKeyMissing", Match)
                    && noSecret.ExitCode == 3 && Regex.IsMatch(noSecret.Output, "PhoneHomeSecretMissing", Match)
                    && notPrivileged.ExitCode == 3 && Regex.IsMatch(notPrivileged.Output, "NotPrivileged", Match);
                _refusalState = refusalOk ? "ok" : "no";
            }

            // Step 11: this run's own container and volume, and nothing else; then the throwaway files.
            private void Cleanup(bool started, string keyPath, string secretPath, string identityPath)
            {
                if (started)
                {
                    Docker(new[] { "logs", "--tail", "400", _container }, "container-logs.txt");
                    Docker(new[] { "rm", "-f", _container }, "docker-rm.txt");
                }
                Docker(new[] { "volume", "rm", "-f", _volume }, "volume-rm.txt");
                foreach (var throwaway in new[] { keyPath, keyPath + ".pub", secretPath, identityPath })
                    if (_files.File.Exists(throwaway)) _files.File.Delete(throwaway);
            }

            private bool WaitHealthy(TimeSpan within, TimeSpan pause)
            {
                var deadline = _host.UtcNow + within;
                while (_host.UtcNow < deadline)
                {
                    try
                    {
                        var health = _seams.Http("GET", Url("/health"), null, TimeSpan.FromSeconds(5));
                        if (health.Status >= 200 && health.Status < 300) return true;
                    }
                    catch (Exception e) when (IsTransport(e))
                    {
                    }
                    _host.Sleep(pause);
                }
                return false;
            }

            private static bool IsTransport(Exception e) =>
                e is HttpRequestException || e is OperationCanceledException || e is InvalidOperationException || e is IOException || e is UriFormatException;

            private static string ImageSet(DockerResult images) =>
                string.Join(",", Regex.Split(images.Output, "\r?\n").Where(line => line.Trim().Length > 0).OrderBy(line => line, StringComparer.OrdinalIgnoreCase));

            private static int RestartCount(DockerResult inspect) =>
                int.TryParse(inspect.Output.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var count) ? count : 0;

            private static string Bool(bool value) => value ? "True" : "False";

            private void Exit(int code, string reason)
            {
                Summary(code, reason);
                throw new HarnessExit(code);
            }

            private int Summary(int code, string reason)
            {
                if (!string.IsNullOrEmpty(reason)) _host.Out.WriteLine("C604 HARNESS NOTE: " + reason);
                _host.Out.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "C604 HARNESS: image={0} nested={1} egress={2} loopback={3} launchMs={4} key={5} restart={6} refusal={7} containment={8} claude={9} claudeAuth={10}",
                    _o.Image, _nested, _egress, _loopback, _launchMs, _keyState, _restartState, _refusalState, _containmentState, _claudeVersion, _claudeAuth));
                _host.Out.WriteLine("C604 HARNESS EXIT CODE: " + code.ToString(CultureInfo.InvariantCulture));
                return code;
            }

            private void WriteEvidence(string name, string text) =>
                _files.File.WriteAllText(_files.Path.Combine(_evidenceDir, name), text + "\n", Encoding.ASCII);

            // docker with its stdout and stderr lines together; a docker that cannot start answers -1 with the reason.
            private DockerResult Docker(string[] arguments, string evidence, string workingDirectory = "")
            {
                var result = Launch("docker", arguments, workingDirectory);
                if (!string.IsNullOrEmpty(evidence))
                    WriteEvidence(evidence, "docker " + string.Join(" ", arguments) + "\n--- exit " + result.ExitCode.ToString(CultureInfo.InvariantCulture) + " ---\n" + result.Output);
                return result;
            }

            private DockerResult Launch(string fileName, string[] arguments, string workingDirectory = "")
            {
                try
                {
                    var result = _host.Launch(new NightlyLaunch(fileName, arguments, workingDirectory ?? "", CaptureOutput: true));
                    return new DockerResult(result.ExitCode, result.LogText ?? "");
                }
                catch (Exception e) when (e is Win32Exception || e is InvalidOperationException || e is IOException || e is UnauthorizedAccessException)
                {
                    return new DockerResult(-1, fileName + " could not start: " + e.Message + "\n");
                }
            }
        }
    }
}
