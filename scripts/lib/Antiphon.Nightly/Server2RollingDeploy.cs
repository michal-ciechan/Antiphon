// The CARD-0727 rolling server2 runner deploy (unit 65 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md, W8 slice C,
// part 3). It replaced scripts/deploy-server2.ps1, its drivers scripts/test-deploy-server2.ps1 and test-deploy-server2-jq.ps1 and
// the Linux output witness scripts/fixtures/deploy-server2-output-witness.ps1. Run from the desktop after Review, never on server2:
//
//   antiphon-scripts deploy-server2 --rolling [--sha <40 hex>] [--phase <phase>] [--saved-donor <path>] [--project-id <id>]
//       [--wait-idle-minutes <1-10080>] [--land-wait-minutes <0-1440>] [--dry-run] [--recycle-volumes]
//       [--resume-recycle <c1008 id>] [--retirement-proof <c1008 id>]
//
// (scripts/antiphon deploy-server2 ...). The phases are the script's: all (deploy-temp, drain-old, redeploy-old, drain-temp,
// retire-temp in that order), each of those alone, check-census (read-only: no host jq, rollout lock, SSH, verify, POST or host
// case), check-host-jq and provision-host-jq.
//
// Every phase, step, refusal, line and exit code is the script's, in its order (captured from it before its deletion and pinned
// in the tests), with PowerShell's semantics where they decide (RollingPs). Exit 0 complete, 2 a refusal ("Rolling deploy
// stopped: <reason>", or the ANTIPHON_API and operator-token lines), 1 a refusal the script threw before its main block (one
// stderr line: the argument combinations, ShaRequired, CurrentToolingIdentityUnavailable, CacheSavedDonorPathInvalid); a
// malformed command line is a usage error, exit 2.
//
// ANTIPHON_API is required (CARD-1777: no localhost default; an unset or blank value exits 2 before any operator-token read or
// docker call). The operator token is read from the owner-only file (ANTIPHON_OPERATOR_TOKEN_FILE, else
// %LOCALAPPDATA%\Antiphon\operator-token; OperatorApi.ReadToken, the logic of scripts/lib/runner-operator-token.ps1, which unit 67
// deleted with its last W10 loaders) and is never printed. The task token (ANTIPHON_TASK_TOKEN) is sent when set.
//
// Every external action goes through IServer2RollingWorld: the runner and census HTTP, ssh mc@server2 (the host jq helper, the
// FD8 rollout lock, the recycle receipt discovery, the temp retirement census and the temp container census), the
// verify-docker-stack host cases and git. The CLI binds Server2RollingOs. Since unit 66 a host case is the verify-docker-stack command
// in this process (DockerStackVerify: the head and the live server2 bridge, C590Bridge), where the script and unit 65 started
// pwsh -NoProfile -File scripts/verify-docker-stack.ps1 -Case <case> -Manifest <path>. Files are IFileSystem's, the clock, waits
// and stopwatches the host's. Plain C#: no PowerShell types. ASCII only.
#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Antiphon.Nightly
{
    /// <summary>A refusal the script threw as a string ("throw 'Reason'"): its message is the reason.</summary>
    public sealed class Server2RollingRefusal : Exception
    {
        /// <summary>The reason.</summary>
        public Server2RollingRefusal(string message) : base(message) { }
    }

    /// <summary>The deploy-server2 command's options (deploy-server2.ps1's parameters in kebab case).</summary>
    public sealed class Server2RollingOptions
    {
        /// <summary>The CLI's usage line.</summary>
        public const string Usage = "deploy-server2 --rolling [--sha <40 hex>] [--phase <phase>] [--saved-donor <path>] [--project-id <id>]"
            + " [--wait-idle-minutes <1-10080>] [--land-wait-minutes <0-1440>] [--dry-run] [--recycle-volumes] [--resume-recycle <c1008 id>]"
            + " [--retirement-proof <c1008 id>]";

        /// <summary>The phases (-Phase's ValidateSet; matched ignoring case, kept as written).</summary>
        public static readonly string[] Phases =
        {
            "all", "deploy-temp", "drain-old", "redeploy-old", "drain-temp", "retire-temp", "check-census", "check-host-jq", "provision-host-jq",
        };

        /// <summary>--rolling: required (the script's mandatory switch).</summary>
        public bool Rolling { get; init; }

        /// <summary>--sha: the source SHA (40 hex, any case); blank only for retire-temp, which uses the checkout's HEAD.</summary>
        public string Sha { get; init; } = "";

        /// <summary>--phase (default all).</summary>
        public string Phase { get; init; } = "all";

        /// <summary>--saved-donor: a saved cache donor path on server2 (deploy-temp refuses it: maintenance first).</summary>
        public string SavedDonor { get; init; } = "";

        /// <summary>--project-id: the recycle census project (given: resolved by id only); null when not given.</summary>
        public string ProjectId { get; init; }

        /// <summary>--wait-idle-minutes (1-10080, default 480).</summary>
        public int WaitIdleMinutes { get; init; } = 480;

        /// <summary>--land-wait-minutes (0-1440, default 30): how long a census waits for a land bound to the recycled runner.</summary>
        public int LandWaitMinutes { get; init; } = 30;

        /// <summary>--dry-run: preview a recycle (redeploy-old) or a retirement (retire-temp).</summary>
        public bool DryRun { get; init; }

        /// <summary>--recycle-volumes: redeploy-old (or all) recycles the main runner's volumes.</summary>
        public bool RecycleVolumes { get; init; }

        /// <summary>--resume-recycle: the c1008 operation to resume; null when not given.</summary>
        public string ResumeRecycle { get; init; }

        /// <summary>--retirement-proof: the c1008 operation whose Ready retirement proof retire-temp applies; null when not given.</summary>
        public string RetirementProof { get; init; }

        /// <summary>The checkout (the script's parent directory): the evidence root's parent and the verifier's.</summary>
        public string Checkout { get; init; } = "";

        /// <summary>The flags after the command name. A value the script's parameter validation refused is a usage error.</summary>
        public static Server2RollingOptions Parse(IReadOnlyList<string> arguments, string checkout)
        {
            var flags = NightlyArgs.Parse(arguments,
                new[] { "--sha", "--phase", "--saved-donor", "--project-id", "--wait-idle-minutes", "--land-wait-minutes", "--resume-recycle", "--retirement-proof" },
                new[] { "--rolling", "--dry-run", "--recycle-volumes" });
            if (!flags.Switch("--rolling")) throw new NightlyUsageException("--rolling is required");
            var sha = flags.Value("--sha", "");
            if (flags.Has("--sha") && !Regex.IsMatch(sha, "^[0-9a-fA-F]{40}$", RegexOptions.IgnoreCase))
                throw new NightlyUsageException("--sha expects 40 hexadecimal digits, not '" + sha + "'");
            var phase = flags.Value("--phase", "all");
            if (!Phases.Contains(phase, StringComparer.OrdinalIgnoreCase))
                throw new NightlyUsageException("--phase expects one of " + string.Join(", ", Phases) + ", not '" + phase + "'");
            var wait = flags.Int("--wait-idle-minutes", 480);
            if (wait < 1 || wait > 10080) throw new NightlyUsageException("--wait-idle-minutes expects 1 to 10080, not " + wait);
            var land = flags.Int("--land-wait-minutes", 30);
            if (land < 0 || land > 1440) throw new NightlyUsageException("--land-wait-minutes expects 0 to 1440, not " + land);
            string Operation(string flag)
            {
                if (!flags.Has(flag)) return null;
                var value = flags.Value(flag, "");
                if (!Regex.IsMatch(value, "^c1008[0-9a-f]{32}$", RegexOptions.IgnoreCase))
                    throw new NightlyUsageException(flag + " expects c1008 and 32 hexadecimal digits, not '" + value + "'");
                return value;
            }
            return new Server2RollingOptions
            {
                Rolling = true,
                Sha = sha,
                Phase = phase,
                SavedDonor = flags.Value("--saved-donor", ""),
                ProjectId = flags.Has("--project-id") ? flags.Value("--project-id", "") : null,
                WaitIdleMinutes = wait,
                LandWaitMinutes = land,
                DryRun = flags.Switch("--dry-run"),
                RecycleVolumes = flags.Switch("--recycle-volumes"),
                ResumeRecycle = Operation("--resume-recycle"),
                RetirementProof = Operation("--retirement-proof"),
                Checkout = checkout ?? "",
            };
        }
    }

    /// <summary>One child's result: its exit code and its two streams.</summary>
    public sealed class Server2RollingChild
    {
        /// <summary>A result; null streams read as empty.</summary>
        public Server2RollingChild(int exit, string stdout, string stderr)
        {
            Exit = exit;
            Stdout = stdout ?? "";
            Stderr = stderr ?? "";
        }

        /// <summary>The exit code.</summary>
        public int Exit { get; }

        /// <summary>Its standard output.</summary>
        public string Stdout { get; }

        /// <summary>Its standard error.</summary>
        public string Stderr { get; }
    }

    /// <summary>An HTTP answer: the status and the body text.</summary>
    public sealed class Server2RollingHttpAnswer
    {
        /// <summary>An answer; a null body reads as empty.</summary>
        public Server2RollingHttpAnswer(int status, string body)
        {
            Status = status;
            Body = body ?? "";
        }

        /// <summary>The status code.</summary>
        public int Status { get; }

        /// <summary>The body.</summary>
        public string Body { get; }
    }

    /// <summary>A child the world could not run to its end: why.</summary>
    public enum Server2RollingFault
    {
        /// <summary>It could not start (or its input could not be written).</summary>
        Start,

        /// <summary>It passed its deadline (and was stopped).</summary>
        Deadline,
    }

    /// <summary>A child the world could not run to its end.</summary>
    public sealed class Server2RollingChildException : Exception
    {
        /// <summary>The fault and the reason.</summary>
        public Server2RollingChildException(Server2RollingFault fault, string message, Exception inner = null) : base(message, inner)
        {
            Fault = fault;
        }

        /// <summary>Why the child did not end normally.</summary>
        public Server2RollingFault Fault { get; }
    }

    /// <summary>What one rolling run is: the fields a manifest or a fixture reads.</summary>
    public sealed class Server2RollingCall
    {
        /// <summary>The source SHA (lower case).</summary>
        public string Sha { get; init; } = "";

        /// <summary>The run id (c727 and 12 hex).</summary>
        public string RunId { get; init; } = "";

        /// <summary>The phase the command was asked for (-Phase as written).</summary>
        public string SelectedPhase { get; init; } = "";

        /// <summary>The phase running now.</summary>
        public string Phase { get; init; } = "";

        /// <summary>check or provision (host jq only).</summary>
        public string Mode { get; init; } = "";

        /// <summary>The evidence root.</summary>
        public string EvidenceRoot { get; init; } = "";

        /// <summary>The retirement proof operation (temp retirement read only; empty: the clearable census).</summary>
        public string Operation { get; init; } = "";
    }

    /// <summary>The FD8 rollout admission lock, held until released.</summary>
    public interface IServer2RollingLock
    {
        /// <summary>Writes release, closes the input and waits 5 s (then stops it); disposes it.</summary>
        void Release();
    }

    /// <summary>The external actions of the rolling deploy (the CLI binds Server2RollingOs; tests their fakes).</summary>
    public interface IServer2RollingWorld
    {
        /// <summary>
        /// Invoke-HostJq's child: ssh -o BatchMode=yes -o ConnectTimeout=15 mc@server2 bash -s -- &lt;mode&gt; with
        /// scripts/server2-host-jq.sh (LF) on its input, 30 s for check and 180 s for provision. Throws Server2RollingRefusal
        /// HostJqTransportTimeout, HostJqTransportUnavailable or HostJqTransportCustodyUnknown as the script did.
        /// </summary>
        Server2RollingChild HostJq(Server2RollingCall call);

        /// <summary>
        /// Enter-RolloutAdmissionLock: ssh to server2's flock on /home/mc/antiphon-server2/locks/rollout.lock (FD 8), the first
        /// line C1008_LOCKED within 70 s. Throws Server2RollingRefusal RecycleRolloutBusy, or the start failure's own exception.
        /// </summary>
        IServer2RollingLock EnterRolloutLock();

        /// <summary>A runner request (Invoke-WebRequest -SkipHttpErrorCheck): the answer; throws when no answer came.</summary>
        Server2RollingHttpAnswer RunnerHttp(string method, string url, IReadOnlyDictionary<string, string> headers, string body);

        /// <summary>A census read (GET, -TimeoutSec 60, -SkipHttpErrorCheck): the answer; throws (typed) when no answer came.</summary>
        Server2RollingHttpAnswer ReadHttp(string path, string url, IReadOnlyDictionary<string, string> headers);

        /// <summary>
        /// Assert-NoIncompleteRecycle's ssh: the c1008 receipts on server2 for this SHA that are not completed (30 s). Throws
        /// Server2RollingChildException (Start: the script's own failure; Deadline: stopped).
        /// </summary>
        Server2RollingChild RecycleDiscover(Server2RollingCall call);

        /// <summary>
        /// Get-TempRetirementCensusRaw's ssh: the temp retirement census (call.Operation empty) or a retirement proof, the read
        /// packet on its input (30 s overall). Throws on any failure to run it (the caller refuses TempRetirementJournalUnknown);
        /// a packet that cannot be built throws its own Server2RollingRefusal.
        /// </summary>
        Server2RollingChild TempRetirementRead(Server2RollingCall call);

        /// <summary>Assert-TempProjectAbsent's ssh: docker ps -aq for the temp compose project (30 s). Throws on any failure to run it.</summary>
        Server2RollingChild TempProjectCensus(Server2RollingCall call);

        /// <summary>
        /// Invoke-HostCase's verifier (the script's pwsh -NoProfile -File scripts/verify-docker-stack.ps1 -Case &lt;case&gt; -Manifest
        /// &lt;path&gt;; since unit 66 the verify-docker-stack command in process), its output to output and its errors to error; the exit code.
        /// </summary>
        int HostCase(string hostCase, string manifestPath, TextWriter output, TextWriter error);

        /// <summary>git -C &lt;repo&gt; &lt;arguments&gt;. Throws when git cannot start.</summary>
        Server2RollingChild Git(string repo, IReadOnlyList<string> arguments);
    }

    /// <summary>The test timings deploy-server2.ps1 read from C727_TEST_* (none in production).</summary>
    public sealed class Server2RollingTiming
    {
        /// <summary>C727_TEST_WAIT_MS: the runner waits' and drain-temp's deadline in milliseconds instead of minutes.</summary>
        public int? WaitMs { get; init; }

        /// <summary>C727_TEST_POLL_MS: the waits' poll in milliseconds instead of 30 s.</summary>
        public int? PollMs { get; init; }

        /// <summary>C727_TEST_LAND_WAIT_MS: the bound-land wait's deadline in milliseconds (when --land-wait-minutes is above 0).</summary>
        public int? LandWaitMs { get; init; }

        /// <summary>C727_TEST_HTTP_STUB was set: the census read retry and the drift restart wait PollMs (when set) instead of 1 s and 30 s.</summary>
        public bool StubbedReads { get; init; }
    }

    /// <summary>What the rolling deploy observes besides its host and files.</summary>
    public sealed class Server2RollingSeams
    {
        /// <summary>The external actions. Required.</summary>
        public IServer2RollingWorld World { get; init; }

        /// <summary>New ids (the run id, the operation ids). Default Guid.NewGuid.</summary>
        public Func<Guid> NewGuid { get; init; } = Guid.NewGuid;

        /// <summary>Get-C994Now: the temp container retirement's clock. Null: the host's clock.</summary>
        public Func<DateTime> C994Now { get; init; }

        /// <summary>The fixture clock's advance after a temp container retirement poll (the script's clockMs). Null: nothing.</summary>
        public Action<int> C994Advance { get; init; }

        /// <summary>The test timings (none in production).</summary>
        public Server2RollingTiming Timing { get; init; } = new Server2RollingTiming();

        /// <summary>The installed host jq path the proof must name (the script's literal; a fixture's private destination).</summary>
        public string HostJqDestination { get; init; } = "/usr/local/bin/jq";
    }

    /// <summary>The rolling server2 runner deploy (CARD-0727 and the cards since).</summary>
    public static partial class Server2RollingDeploy
    {
        /// <summary>The installed jq the host-jq proof admits (CARD-1025).</summary>
        public const string HostJqVersion = "jq-1.7.1";

        /// <summary>jq 1.7.1's linux-amd64 digest.</summary>
        public const string HostJqDigest = "5942c9b0934e510ee61eb3e30273f1b3fe2590df93933a93d7c58b81d19c8ff5";

        /// <summary>The four temp private volumes.</summary>
        public static readonly string[] TempVolumes =
            { "antiphon-runner-temp_work", "antiphon-runner-temp_runner-tmp", "antiphon-runner-temp_dind-data", "antiphon-runner-temp_runner-state" };

        /// <summary>Runs the command. Returns the exit code; a malformed command line was refused by Parse.</summary>
        public static int Run(Server2RollingOptions options, NightlyHost host, IFileSystem files, Server2RollingSeams seams)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (host == null) throw new ArgumentNullException(nameof(host));
            if (files == null) throw new ArgumentNullException(nameof(files));
            if (seams?.World == null) throw new ArgumentNullException(nameof(seams));
            return new Session(options, host, files, seams).Run();
        }

        /// <summary>Assert-RecycleContext: a recycle context's exact keys and values (also run on raw JSON by the tests).</summary>
        public static void AssertRecycleContext(object context)
        {
            if (context == null) throw new Server2RollingRefusal("RecycleContextInvalid");
            var keys = RollingPs.Names(context);
            var expected = new List<string> { "version", "project", "operationId", "dryRun", "resume", "projectId" };
            object P(string name) => RollingPs.Prop(context, name);
            if (keys.Contains("cleanupOperationId", StringComparer.Ordinal))
            {
                if (!RollingPs.Ceq(P("project"), "antiphon-runner-temp") || !RollingPs.Eq(P("resume"), false)
                    || !(P("cleanupOperationId") is string) || !RollingPs.CMatch(P("cleanupOperationId"), "^c994[0-9a-f]{32}$"))
                    throw new Server2RollingRefusal("RecycleContextInvalid");
                expected.Add("cleanupOperationId");
            }
            if (keys.Contains("expectedProofDigest", StringComparer.Ordinal))
            {
                if (!RollingPs.Ceq(P("project"), "antiphon-runner-temp") || !RollingPs.Eq(P("dryRun"), false)
                    || !(P("expectedProofDigest") is string) || !RollingPs.CMatch(P("expectedProofDigest"), "^[0-9a-f]{64}$"))
                    throw new Server2RollingRefusal("RecycleContextInvalid");
                expected.Add("expectedProofDigest");
            }
            if (keys.Length != expected.Count || keys.Any(k => !expected.Contains(k, StringComparer.Ordinal))) throw new Server2RollingRefusal("RecycleContextInvalid");
            var version = P("version");
            if (!RollingPs.Eq(version, 1) || !RollingPs.IsInt(version)
                || !RollingPs.CIn(P("project"), "antiphon-runner", "antiphon-runner-temp")
                || !RollingPs.CMatch(P("operationId"), "^c1008[0-9a-f]{32}$")
                || !(P("dryRun") is bool) || !(P("resume") is bool)
                || !RollingPs.CMatch(P("projectId"), "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$")
                || (RollingPs.True(P("dryRun")) && RollingPs.True(P("resume"))))
                throw new Server2RollingRefusal("RecycleContextInvalid");
        }

        /// <summary>Assert-ZeroCounters: sessions, runnerSessions and queuedTasks present, whole, not negative and zero.</summary>
        public static void AssertZeroCounters(object status, string runnerId) => Session.AssertZeroCounters(status, runnerId);

        /// <summary>Assert-TempSeedCounters: the temp counters before a seed (runnerSessions may be null only with the slot offline).</summary>
        public static void AssertTempSeedCounters(object status) => Session.AssertTempSeedCounters(status);

        /// <summary>Assert-TempCleanupStatus: a retired, idle, offline temp runner redirected to server2; its retirement instant.</summary>
        public static string AssertTempCleanupStatus(object status, string expectedRetiredAt = "") => Session.AssertTempCleanupStatus(status, expectedRetiredAt);

        /// <summary>Assert-TempCleanupMain: the main runner available, eligible, accepting, not draining or retired (and at sha when asked).</summary>
        public static void AssertTempCleanupMain(object status, bool requireSha, string sha) => Session.AssertTempCleanupMain(status, requireSha, sha);

        /// <summary>
        /// Assert-RetiredTempCounters: the retired temp runner's counters (a null runnerSessions only offline with the temp project absent:
        /// assertTempProjectAbsent is Assert-TempProjectAbsent). Returns the retirement instant.
        /// </summary>
        public static string AssertRetiredTempCounters(object status, Action assertTempProjectAbsent) => Session.AssertRetiredTempCounters(status, assertTempProjectAbsent);

        /// <summary>
        /// Assert-TempRetirementClearable's reading of the census text: no temp private volume and every retirement journal closed
        /// (TempVolumesRetained, TempRetirementOwnerActive, TempRetirementUnresolved, else TempRetirementJournalUnknown).
        /// </summary>
        public static void CheckTempRetirementCensus(string raw) => Session.CheckTempRetirementCensus(raw);

        /// <summary>
        /// One runner request as a phase makes it (Invoke-RunnerRequest: a POST holds the FD8 rollout lock; the temp runner's
        /// /drain/clear rechecks the retirement census, or with holdLift that the runner is this build and not retired), on its own.
        /// Returns the answer as ConvertFrom-Json read it; a refusal throws Server2RollingRefusal.
        /// </summary>
        public static object ProbeRunnerRequest(string method, string runnerId, string suffix, PlainObject body, bool holdLift, string sha, string evidenceRoot,
            NightlyHost host, IFileSystem files, Server2RollingSeams seams)
        {
            var session = new Session(new Server2RollingOptions { Rolling = true, Sha = sha, Phase = "deploy-temp", Checkout = evidenceRoot }, host, files, seams);
            return session.ProbeRequest(method, runnerId, suffix, body, holdLift, evidenceRoot);
        }

        /// <summary>The verbatim reason of a refusal the script threw before its main block (exit 1, one stderr line).</summary>
        private sealed class EarlyRefusal : Exception
        {
            public EarlyRefusal(string message) : base(message) { }
        }

        /// <summary>One run: the script's script-scope state.</summary>
        private sealed partial class Session
        {
            private readonly Server2RollingOptions _o;
            private readonly NightlyHost _host;
            private readonly IFileSystem _files;
            private readonly Server2RollingSeams _seams;
            private readonly IServer2RollingWorld _world;
            private readonly Server2RollingTiming _timing;
            private readonly bool _recycleProjectIdWasProvided;
            private readonly string _requestedRecycleProjectId;
            private readonly string _phase;
            private string _sha;
            private string _api;
            private readonly Dictionary<string, string> _headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            private string _repoRoot;
            private string _runId;
            private string _evidenceRoot;

            public Session(Server2RollingOptions options, NightlyHost host, IFileSystem files, Server2RollingSeams seams)
            {
                _o = options;
                _host = host;
                _files = files;
                _seams = seams;
                _world = seams.World;
                _timing = seams.Timing ?? new Server2RollingTiming();
                _recycleProjectIdWasProvided = options.ProjectId != null;
                _requestedRecycleProjectId = options.ProjectId;
                _phase = options.Phase ?? "all";
                _sha = options.Sha ?? "";
            }

            internal object ProbeRequest(string method, string runnerId, string suffix, PlainObject body, bool holdLift, string evidenceRoot)
            {
                _api = (_host.Variables("ANTIPHON_API") ?? "").TrimEnd('/');
                _sha = (_sha ?? "").ToLowerInvariant();
                _evidenceRoot = evidenceRoot;
                _runId = "c727" + NewId().Substring(0, 12);
                return InvokeRunnerRequest(method, runnerId, suffix, body, holdLift);
            }

            private bool PhaseIs(string name) => string.Equals(_phase, name, StringComparison.OrdinalIgnoreCase);

            private bool PhaseIn(params string[] names) => names.Any(PhaseIs);

            private bool Resume => !string.IsNullOrEmpty(_o.ResumeRecycle);

            private bool Proof => !string.IsNullOrEmpty(_o.RetirementProof);

            private DateTime UtcNow => _host.UtcNow;

            private DateTime C994Now() => _seams.C994Now?.Invoke() ?? _host.UtcNow;

            private void Sleep(int milliseconds) => _host.Sleep(TimeSpan.FromMilliseconds(milliseconds));

            private long Timestamp() => _host.Clock.GetTimestamp();

            private long ElapsedMs(long start) => (long)_host.Clock.GetElapsedTime(start).TotalMilliseconds;

            private string NewId() => _seams.NewGuid().ToString("N");

            // Join-Path: the provider's separators ('/' becomes '\' on Windows).
            private static string Join(string parent, string child)
            {
                var joined = Path.Join(parent, child);
                return OperatingSystem.IsWindows() ? joined.Replace('/', '\\') : joined;
            }

            // Set-Content: the text and one line end, UTF-8 without a BOM (-Encoding utf8 and the default) or ASCII ('?' for others).
            private void SetContent(string path, string text, bool ascii = false)
            {
                var content = (text ?? "") + Environment.NewLine;
                if (ascii)
                {
                    var bytes = content.Select(c => c < 128 ? (byte)c : (byte)'?').ToArray();
                    _files.File.WriteAllBytes(path, bytes);
                }
                else _files.File.WriteAllText(path, content, new UTF8Encoding(false));
            }

            private void Out(string line) => _host.Out.WriteLine(line);

            public int Run()
            {
                try
                {
                    var entry = Entry();
                    if (entry.HasValue) return entry.Value;
                }
                catch (Exception e) when (!(e is OutOfMemoryException))
                {
                    // An EarlyRefusal is the script's throw before its main try; anything else failed there too (exit 1, one line).
                    _host.Error.WriteLine(e.Message);
                    return 1;
                }
                try
                {
                    return Main();
                }
                catch (Exception e) when (!(e is OutOfMemoryException))
                {
                    _host.Error.WriteLine("Rolling deploy stopped: " + e.Message);
                    return 2;
                }
            }

            // Everything before the script's main try: the argument checks, the SHA, ANTIPHON_API, the tokens and the evidence root.
            private int? Entry()
            {
                if (_o.ResumeRecycle != null && !Regex.IsMatch(_o.ResumeRecycle, "^c1008[0-9a-f]{32}$")) throw new EarlyRefusal("RecycleContextInvalid");
                if ((_o.DryRun || Resume) && !PhaseIn("redeploy-old", "retire-temp")) throw new EarlyRefusal("RecycleContextInvalid");
                if (_o.DryRun && Resume) throw new EarlyRefusal("RecycleContextInvalid");
                if (Proof && (!PhaseIs("retire-temp") || _o.DryRun || Resume)) throw new EarlyRefusal("RecycleContextInvalid");
                if (_o.RecycleVolumes && !PhaseIn("redeploy-old", "all")) throw new EarlyRefusal("RecycleContextInvalid");
                _repoRoot = _o.Checkout;
                if (string.IsNullOrWhiteSpace(_sha))
                {
                    if (!PhaseIs("retire-temp")) throw new EarlyRefusal("ShaRequired");
                    // Current source identity is transport provenance only for retirement. No old checkout, Compose render or image
                    // tag is resolved from a creating SHA.
                    Server2RollingChild head;
                    try { head = _world.Git(_repoRoot, new[] { "rev-parse", "HEAD" }); }
                    catch (Exception e) { throw new EarlyRefusal(e.Message); }
                    if (head.Stderr.Length > 0) _host.Error.Write(head.Stderr);
                    var lines = head.Stdout.Replace("\r\n", "\n").Split('\n');
                    var output = lines.Length > 0 && lines[lines.Length - 1].Length == 0 ? lines.Take(lines.Length - 1).ToArray() : lines;
                    if (head.Stdout.Length == 0) throw new EarlyRefusal("You cannot call a method on a null-valued expression.");
                    var value = string.Join(" ", output).Trim();
                    // Assigning to the validated -Sha variable re-runs its ValidatePattern.
                    if (!Regex.IsMatch(value, "^[0-9a-fA-F]{40}$", RegexOptions.IgnoreCase))
                        throw new EarlyRefusal("The variable cannot be validated because the value " + value + " is not a valid value for the Sha variable.");
                    _sha = value;
                    if (head.Exit != 0 || !Regex.IsMatch(_sha, "^[0-9a-f]{40}$")) throw new EarlyRefusal("CurrentToolingIdentityUnavailable");
                }
                var api = _host.Variables("ANTIPHON_API");
                if (string.IsNullOrWhiteSpace(api))
                {
                    _host.Error.WriteLine("ANTIPHON_API is required. Set it to the control-plane origin before any rolling phase. See docs/docker-stack.md.");
                    return 2;
                }
                _api = api.TrimEnd('/');
                var task = _host.Variables("ANTIPHON_TASK_TOKEN");
                if (!string.IsNullOrWhiteSpace(task)) _headers[OperatorApi.TaskTokenHeader] = task;
                // Resolve before a case runs. Never print this value, headers, or an exception request object.
                if (!PhaseIs("check-census"))
                {
                    string token = null;
                    try
                    {
                        var path = _host.Variables("ANTIPHON_OPERATOR_TOKEN_FILE");
                        if (string.IsNullOrWhiteSpace(path))
                        {
                            var local = _host.Variables("LOCALAPPDATA");
                            if (local == null) throw new InvalidOperationException("LOCALAPPDATA is not set");
                            path = Join(Join(local, "Antiphon"), "operator-token");
                        }
                        token = OperatorApi.ReadToken(_files, path, out _);
                    }
                    catch (Exception) { token = null; }
                    if (token == null)
                    {
                        _host.Error.WriteLine("OperatorTokenMissing: the owner-only token file is absent or empty.");
                        return 2;
                    }
                    _headers[OperatorApi.OperatorTokenHeader] = token;
                }
                _sha = _sha.ToLowerInvariant();
                var donor = _o.SavedDonor ?? "";
                if (donor.Length > 0 && (!Regex.IsMatch(donor, "^/[A-Za-z0-9._/-]{1,500}$") || donor.Contains("..") || donor.Contains("//")))
                    throw new EarlyRefusal("CacheSavedDonorPathInvalid");
                _runId = "c727" + NewId().Substring(0, 12);
                _evidenceRoot = Join(_repoRoot, ".antiphon/rolling-server2/" + _runId);
                _files.Directory.CreateDirectory(_evidenceRoot);
                if (PhaseIs("retire-temp"))
                {
                    // Informational only. This line does not change a refusal. The longest derived path is the proof directory: evidence
                    // root, exchange-<32 hex>, then c1008-proof-<operation>-<10 unix digits>-<32 hex>.
                    var leaf = "c1008-proof-c1008" + new string('f', 32) + "-" + new string('9', 10) + "-" + new string('f', 32);
                    var sep = Path.DirectorySeparatorChar;
                    var longest = _repoRoot + sep + ".antiphon" + sep + "rolling-server2" + sep + ("c727" + new string('f', 12)) + sep
                        + ("exchange-" + new string('f', 32)) + sep + leaf;
                    Out("path-length export-root=" + _repoRoot.Length + " longest-derived=" + longest.Length);
                }
                return null;
            }

            private int Main()
            {
                if (PhaseIs("check-census"))
                {
                    // Read-only: no host jq, rollout lock, SSH, verify, POST or host case.
                    PlainObject resolvedProject = null;
                    string entryProjectId = null;
                    foreach (var runner in new[] { "server2", "server2-temp" })
                    {
                        StartRecycleReceipt(_phase, runner);
                        if (resolvedProject == null)
                        {
                            entryProjectId = GetRecycleProjectId();
                            resolvedProject = (PlainObject)_recycleReceipt["project"];
                            Out("RECYCLE_PROJECT id=" + entryProjectId + " name=" + RollingPs.Str(resolvedProject["name"]) + " resolvedBy=" + RollingPs.Str(resolvedProject["resolvedBy"]));
                        }
                        else
                        {
                            _recycleReceipt["project"] = resolvedProject;
                            _resolvedRecycleProjectId = entryProjectId;
                        }
                        var census = (CensusReport)AssertRecycleTaskCensus(runner, entryProjectId, report: true);
                        Out("RECYCLE_CENSUS runner=" + runner + " open=" + census.Open + " boundOpen=" + census.BoundOpen + " landPending=" + census.LandPending
                            + " boundLandPending=" + census.BoundLandPending + " scopes=" + census.Scopes + " elapsedMs=" + census.ElapsedMs);
                    }
                    Out("Census check complete: " + _evidenceRoot);
                    return 0;
                }
                if (_recycleProjectIdWasProvided && !TestRecycleGuid(_requestedRecycleProjectId)) throw new Server2RollingRefusal("RecycleProjectUnresolved cause=InvalidId");
                if (PhaseIn("check-host-jq", "provision-host-jq"))
                {
                    InvokeHostJq(PhaseIs("check-host-jq") ? "check" : "provision", _phase);
                    Out("Host jq qualified: " + _evidenceRoot);
                    return 0;
                }
                var phases = PhaseIs("all") ? new[] { "deploy-temp", "drain-old", "redeploy-old", "drain-temp", "retire-temp" } : new[] { _phase };
                foreach (var name in phases) InvokePhase(name);
                Out("Rolling deploy complete: " + _evidenceRoot);
                return 0;
            }

            // ---- Invoke-HostJq ----

            private static readonly string[] HostJqRequired =
                { "schema", "lane", "mode", "lookupPath", "path", "version", "digest", "uid", "gid", "permissions", "trueExit", "falseExit", "installed", "outcome" };

            private void InvokeHostJq(string mode, string executingPhase)
            {
                var destination = _seams.HostJqDestination ?? "/usr/local/bin/jq";
                if (mode == "provision")
                {
                    // No worktree override: privileged provisioning uses the clean canonical source.
                    try
                    {
                        string Git(params string[] args)
                        {
                            var r = _world.Git(_repoRoot, args);
                            if (r.Exit != 0) throw new InvalidOperationException("git");
                            return GitText(r.Stdout);
                        }
                        var top = Git("rev-parse", "--show-toplevel");
                        var gitDir = Git("rev-parse", "--path-format=absolute", "--git-dir");
                        var commonDir = Git("rev-parse", "--path-format=absolute", "--git-common-dir");
                        if (!RollingPs.Eq(Path.GetFullPath(top), Path.GetFullPath(_repoRoot)) || !RollingPs.Eq(gitDir, commonDir)) throw new InvalidOperationException("git");
                        var head = _world.Git(_repoRoot, new[] { "rev-parse", "HEAD" });
                        if (head.Exit != 0 || !RollingPs.Ceq(GitText(head.Stdout), _sha)) throw new InvalidOperationException("git");
                        var dirt = _world.Git(_repoRoot, new[] { "status", "--porcelain", "--untracked-files=all" });
                        if (dirt.Exit != 0 || !string.IsNullOrWhiteSpace(string.Join("\n", GitLines(dirt.Stdout)))) throw new InvalidOperationException("git");
                    }
                    catch (Exception) { throw new Server2RollingRefusal("HostJqCanonicalSourceRequired"); }
                }
                var result = _world.HostJq(new Server2RollingCall
                {
                    Sha = _sha, RunId = _runId, SelectedPhase = _phase, Phase = executingPhase, Mode = mode, EvidenceRoot = _evidenceRoot,
                });
                var hostJqExit = result.Exit;
                var hostJqStdout = result.Stdout;
                var hostJqStderr = result.Stderr;
                if (hostJqExit != 0)
                {
                    // Retain the found path for this specific refusal; never copy raw remote diagnostics.
                    if (hostJqExit == 2)
                    {
                        PlainObject pathRefusal = null;
                        try
                        {
                            using (var document = JsonDocument.Parse(hostJqStdout))
                            {
                                var root = document.RootElement;
                                if (root.ValueKind != JsonValueKind.Object) throw new FormatException("shape");
                                var names = root.EnumerateObject().Select(p => p.Name).ToArray();
                                var required = new[] { "schema", "lane", "mode", "lookupPath", "path", "reason" };
                                if (names.Length != 6 || names.Distinct(StringComparer.Ordinal).Count() != 6
                                    || required.Any(r => !names.Contains(r, StringComparer.Ordinal))) throw new FormatException("shape");
                                foreach (var key in new[] { "lane", "mode", "lookupPath", "path", "reason" })
                                    if (root.GetProperty(key).ValueKind != JsonValueKind.String) throw new FormatException("shape");
                                if (!root.GetProperty("schema").TryGetInt64(out var number) || number != 1) throw new FormatException("shape");
                                var candidate = RollingPs.ParseEnumerated(hostJqStdout) as PlainObject;
                                if (candidate == null) throw new FormatException("shape");
                                if (!RollingPs.Ceq(candidate["lane"], "host") || !RollingPs.Ceq(candidate["mode"], mode)
                                    || !RollingPs.Ceq(candidate["reason"], "HostJqPathUnapproved")
                                    || !RollingPs.CMatch(candidate["lookupPath"], @"^/[^\x00-\x1f]{1,4095}$")
                                    || !RollingPs.CMatch(candidate["path"], @"^/[^\x00-\x1f]{1,4095}$")
                                    || RollingPs.Ceq(candidate["path"], destination)) throw new FormatException("shape");
                                pathRefusal = candidate;
                            }
                        }
                        catch (Exception) { pathRefusal = null; }
                        if (pathRefusal != null)
                        {
                            var observation = RollingPs.Object(("schema", 1), ("qualified", false), ("lane", "host"), ("mode", mode),
                                ("reason", "HostJqPathUnapproved"), ("lookupPath", pathRefusal["lookupPath"]), ("path", pathRefusal["path"]),
                                ("sourceSha", _sha), ("runId", _runId), ("selectedPhase", _phase), ("phase", executingPhase),
                                ("observedAtUtc", UtcNow.ToString("o", CultureInfo.InvariantCulture)), ("sshExit", hostJqExit));
                            try { WriteNew(Join(_evidenceRoot, "host-jq-" + executingPhase + "-refused.json"), RollingPs.Json(observation)); }
                            catch (Exception) { throw new Server2RollingRefusal("HostJqReceiptUnavailable"); }
                            throw new Server2RollingRefusal("HostJqPathUnapproved");
                        }
                    }
                    // Only fixed helper diagnoses cross this boundary, never raw remote stderr.
                    var diagnosis = hostJqStderr.Trim();
                    if (new[] { "HostJqMissing", "HostJqInvalid", "HostJqWrongLane", "HostJqLaneUnavailable" }.Contains(diagnosis, StringComparer.Ordinal))
                        throw new Server2RollingRefusal(diagnosis);
                    throw new Server2RollingRefusal("HostJqRemoteRefused");
                }
                PlainObject proof;
                try
                {
                    using (var document = JsonDocument.Parse(hostJqStdout))
                    {
                        var root = document.RootElement;
                        if (root.ValueKind != JsonValueKind.Object) throw new FormatException("shape");
                        var names = root.EnumerateObject().Select(p => p.Name).ToArray();
                        if (names.Length != HostJqRequired.Length || names.Distinct(StringComparer.Ordinal).Count() != HostJqRequired.Length
                            || HostJqRequired.Any(r => !names.Contains(r, StringComparer.Ordinal))) throw new FormatException("shape");
                        foreach (var key in new[] { "schema", "uid", "gid", "trueExit", "falseExit" })
                        {
                            var value = root.GetProperty(key);
                            if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt64(out _)) throw new FormatException("shape");
                        }
                        foreach (var key in new[] { "lane", "mode", "lookupPath", "path", "version", "digest", "permissions", "outcome" })
                            if (root.GetProperty(key).ValueKind != JsonValueKind.String) throw new FormatException("shape");
                        var installedKind = root.GetProperty("installed").ValueKind;
                        if (installedKind != JsonValueKind.True && installedKind != JsonValueKind.False) throw new FormatException("shape");
                    }
                    proof = RollingPs.ParseEnumerated(hostJqStdout) as PlainObject;
                    if (proof == null) throw new FormatException("shape");
                    object P(string name) => proof[name];
                    var installed = RollingPs.True(P("installed"));
                    if (!RollingPs.Eq(P("schema"), 1) || !RollingPs.Ceq(P("lane"), "host") || !RollingPs.Ceq(P("mode"), mode)
                        || !RollingPs.CMatch(P("lookupPath"), @"^/[^\r\n]+$")
                        || !RollingPs.Ceq(P("path"), destination)
                        || !RollingPs.CMatch(P("path"), @"^/[^\r\n]+$") || string.IsNullOrWhiteSpace(RollingPs.Str(P("version")))
                        || RollingPs.Match(P("version"), @"[\r\n]") || !RollingPs.CMatch(P("digest"), "^[0-9a-f]{64}$")
                        || RollingPs.LessThan(P("uid"), 0) || RollingPs.LessThan(P("gid"), 0) || !RollingPs.CMatch(P("permissions"), "^[0-7]{3,4}$")
                        || !RollingPs.Eq(P("trueExit"), 0) || !RollingPs.Eq(P("falseExit"), 1)
                        || (installed && (mode != "provision" || !RollingPs.Ceq(P("outcome"), "installed")))
                        || (!installed && !RollingPs.Ceq(P("outcome"), "existing"))) throw new FormatException("shape");
                    if (installed && (!RollingPs.Ceq(P("path"), destination) || !RollingPs.Ceq(P("version"), HostJqVersion)
                        || !RollingPs.Ceq(P("digest"), HostJqDigest) || !RollingPs.Eq(P("uid"), 0) || !RollingPs.Eq(P("gid"), 0)
                        || !RollingPs.Ceq(P("permissions"), "755"))) throw new FormatException("shape");
                }
                catch (Exception) { throw new Server2RollingRefusal("HostJqProofInvalid"); }
                var receipt = new PlainObject();
                foreach (var entry in proof.Entries) receipt[entry.Key] = entry.Value;
                receipt["sourceSha"] = _sha;
                receipt["runId"] = _runId;
                receipt["selectedPhase"] = _phase;
                receipt["phase"] = executingPhase;
                receipt["mode"] = mode;
                receipt["observedAtUtc"] = UtcNow.ToString("o", CultureInfo.InvariantCulture);
                receipt["sshExit"] = hostJqExit;
                // CreateNew refuses stale/blocked paths; successful close precedes admission/banner.
                try { WriteNew(Join(_evidenceRoot, "host-jq-" + executingPhase + ".json"), RollingPs.Json(receipt)); }
                catch (Exception) { throw new Server2RollingRefusal("HostJqReceiptUnavailable"); }
            }

            // [IO.File]::Open(CreateNew), the UTF-8 bytes, Flush($true).
            private void WriteNew(string path, string text)
            {
                using var file = _files.File.Open(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                var bytes = Encoding.UTF8.GetBytes(text);
                file.Write(bytes, 0, bytes.Length);
                file.Flush(true);
            }

            // A native command's output as PowerShell assigned it: its lines, the last line end dropped.
            private static string[] GitLines(string stdout)
            {
                var text = (stdout ?? "").Replace("\r\n", "\n");
                if (text.EndsWith("\n", StringComparison.Ordinal)) text = text.Substring(0, text.Length - 1);
                return text.Length == 0 ? Array.Empty<string>() : text.Split('\n');
            }

            private static string GitText(string stdout) => string.Join(" ", GitLines(stdout));

            // ---- runner requests ----

            private object InvokeRunnerRequest(string method, string runnerId, string suffix = "", PlainObject body = null, bool holdLift = false)
            {
                IServer2RollingLock held = null;
                try
                {
                    if (method == "POST") held = EnterRolloutAdmissionLock();
                    var tempClear = method == "POST" && runnerId == "server2-temp" && suffix == "/drain/clear";
                    if (tempClear && !holdLift)
                    {
                        // The earlier preflight is advisory. Reobserve while this exact POST owns FD8, before the retirement epoch can be
                        // cleared.
                        AssertTempRetirementClearable();
                    }
                    else if (tempClear)
                    {
                        // Lifting this run's deployment hold: the container it just deployed owns the temp volumes. Reobserve under FD8
                        // that it is not retired and is this build.
                        var s = InvokeRunnerRequestCore("GET", runnerId, "/status");
                        if (s == null || RollingPs.True(RollingPs.Prop(s, "retiredAt"))) throw new Server2RollingRefusal("TempRunnerRetiredDuringHold");
                        if (!RollingPs.Ceq(RollingPs.Str(RollingPs.Prop(s, "buildVersion")), _sha)) throw new Server2RollingRefusal("TempHoldLiftBuildMismatch");
                    }
                    return InvokeRunnerRequestCore(method, runnerId, suffix, body);
                }
                finally
                {
                    held?.Release();
                }
            }

            private IServer2RollingLock EnterRolloutAdmissionLock() => _world.EnterRolloutLock();

            private object InvokeRunnerRequestCore(string method, string runnerId, string suffix = "", PlainObject body = null)
            {
                var path = "/api/session-runners/" + Uri.EscapeDataString(runnerId) + suffix;
                Server2RollingHttpAnswer response;
                try { response = _world.RunnerHttp(method, _api + path, _headers, body == null ? null : RollingPs.Json(body)); }
                catch (Exception) { throw new Server2RollingRefusal("RunnerApiUnavailable " + runnerId + suffix); }
                if (response.Status == 404 && method == "GET") return null;
                if (response.Status < 200 || response.Status >= 300) throw new Server2RollingRefusal("RunnerApiFailed " + runnerId + suffix + " HTTP " + response.Status);
                if (string.IsNullOrWhiteSpace(response.Body)) return null;
                return RollingPs.ParseEnumerated(response.Body);
            }

            private object GetRunnerStatus(string runnerId) => InvokeRunnerRequest("GET", runnerId, "/status");

            // Same-SHA health cannot hide an unfinished operation or a lost receipt copy.
            private void AssertNoIncompleteRecycle()
            {
                Server2RollingChild result;
                try { result = _world.RecycleDiscover(Call("redeploy-old")); }
                catch (Server2RollingChildException e) when (e.Fault == Server2RollingFault.Deadline) { throw new Server2RollingRefusal("RecycleReceiptUnavailable"); }
                catch (Server2RollingChildException e) { throw new InvalidOperationException(e.Message, e.InnerException); }
                if (result.Exit != 0) throw new Server2RollingRefusal("RecycleReceiptUnavailable");
                if (!string.IsNullOrWhiteSpace(result.Stdout)) throw new Server2RollingRefusal("RecycleResumeRequired");
            }

            private Server2RollingCall Call(string phase, string operation = "") => new Server2RollingCall
            {
                Sha = _sha, RunId = _runId, SelectedPhase = _phase, Phase = phase, EvidenceRoot = _evidenceRoot, Operation = operation ?? "",
            };

            internal static void AssertZeroCounters(object status, string runnerId)
            {
                if (status == null) throw new Server2RollingRefusal("RunnerStatusMissing " + runnerId);
                foreach (var name in new[] { "sessions", "runnerSessions", "queuedTasks" })
                {
                    if (!RollingPs.Has(status, name) || RollingPs.Prop(status, name) == null) throw new Server2RollingRefusal("RunnerCounterUnknown " + runnerId + " " + name);
                    var value = RollingPs.Prop(status, name);
                    if (!RollingPs.IsInt(value) || RollingPs.LessThan(value, 0)) throw new Server2RollingRefusal("RunnerCounterUnknown " + runnerId + " " + name);
                    if (!RollingPs.Eq(value, 0)) throw new Server2RollingRefusal("RunnerBusy " + runnerId + " " + name);
                }
            }

            private static PlainObject Body(params (string Name, object Value)[] entries) => RollingPs.Object(entries);

            // ---- the temp runner's counters and the main runner ----

            private string AssertRetiredTempCounters(object status) => AssertRetiredTempCounters(status, AssertTempProjectAbsent);

            internal static string AssertRetiredTempCounters(object status, Action assertTempProjectAbsent)
            {
                object P(string n) => RollingPs.Prop(status, n);
                if (status == null || !(P("draining") is bool) || !RollingPs.Eq(P("draining"), true) || !RollingPs.True(P("retiredAt")))
                    throw new Server2RollingRefusal("TempRunnerNotRetired");
                var stamp = P("retiredAt") is DateTime date ? date.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture) : RollingPs.Str(P("retiredAt"));
                if (!Regex.IsMatch(stamp, @"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d{1,7})?(Z|[+-]\d{2}:\d{2})$")
                    || !DateTimeOffset.TryParse(stamp, out var parsed)) throw new Server2RollingRefusal("TempRunnerNotRetired");
                if (!(P("retireWhenIdle") is bool) || !RollingPs.Eq(P("retireWhenIdle"), true) || !RollingPs.Ceq(RollingPs.Str(P("redirectTo")), "server2"))
                    throw new Server2RollingRefusal("TempRunnerDrainConflict");
                if (!(P("acceptingNewWork") is bool) || !RollingPs.Eq(P("acceptingNewWork"), false)) throw new Server2RollingRefusal("TempRunnerDrainConflict");
                var copy = RollingPs.Copy(status, 20);
                if (!RollingPs.Has(status, "runnerSessions")) throw new Server2RollingRefusal("RunnerCounterUnknown server2-temp runnerSessions");
                if (P("runnerSessions") == null)
                {
                    foreach (var key in new[] { "available", "dispatchEligible", "acceptingNewWork" })
                        if (!(P(key) is bool) || !RollingPs.Eq(P(key), false)) throw new Server2RollingRefusal("RunnerCounterUnknown server2-temp runnerSessions");
                    try { assertTempProjectAbsent(); }
                    catch (Exception e) when (RollingPs.Eq(e.Message, "TempContainersRemain"))
                    {
                        throw new Server2RollingRefusal("RunnerCounterUnknown server2-temp runnerSessions");
                    }
                    if (copy is PlainObject plain) plain["runnerSessions"] = 0;
                }
                AssertZeroCounters(copy, "server2-temp");
                return parsed.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);
            }

            internal static void AssertTempSeedCounters(object status)
            {
                if (status == null) throw new Server2RollingRefusal("RunnerStatusMissing server2-temp");
                object P(string n) => RollingPs.Prop(status, n);
                // An absent temp container has no live inventory, whether its placeholder is retired or cleared. The phase confirms host
                // absence before changing the slot.
                var offline = RollingPs.Eq(P("available"), false) && RollingPs.Eq(P("dispatchEligible"), false) && RollingPs.Eq(P("acceptingNewWork"), false);
                foreach (var name in new[] { "sessions", "runnerSessions", "queuedTasks" })
                {
                    var value = P(name);
                    if (!RollingPs.Has(status, name) || value == null)
                    {
                        if (name == "runnerSessions" && offline && RollingPs.Has(status, name)) continue;
                        throw new Server2RollingRefusal("RunnerCounterUnknown server2-temp " + name);
                    }
                    if (!RollingPs.IsInt(value)) throw new Server2RollingRefusal("RunnerCounterUnknown server2-temp " + name);
                    if (!RollingPs.Eq(value, 0)) throw new Server2RollingRefusal("RunnerBusy server2-temp " + name);
                }
            }

            // Cleanup admission does not normalize inventory or authorize volume deletion.
            internal static string AssertTempCleanupStatus(object status, string expectedRetiredAt = "")
            {
                if (status == null) throw new Server2RollingRefusal("RunnerStatusMissing server2-temp");
                object P(string n) => RollingPs.Prop(status, n);
                foreach (var name in new[] { "sessions", "queuedTasks", "runnerSessions" })
                {
                    if (!RollingPs.HasExact(status, name)) throw new Server2RollingRefusal("RunnerCounterUnknown server2-temp " + name);
                    var value = P(name);
                    if (name == "runnerSessions" && value == null) continue;
                    if (!RollingPs.IsInt(value) || RollingPs.LessThan(value, 0)) throw new Server2RollingRefusal("RunnerCounterUnknown server2-temp " + name);
                    if (!RollingPs.Eq(value, 0)) throw new Server2RollingRefusal("RunnerBusy server2-temp " + name);
                }
                foreach (var name in new[] { "draining", "retireWhenIdle" })
                    if (!(P(name) is bool) || !RollingPs.Eq(P(name), true)) throw new Server2RollingRefusal("TempRunnerNotRetired");
                foreach (var name in new[] { "available", "dispatchEligible", "acceptingNewWork" })
                {
                    if (!(P(name) is bool) || !RollingPs.Eq(P(name), false))
                    {
                        if (P("runnerSessions") == null) throw new Server2RollingRefusal("RunnerCounterUnknown server2-temp runnerSessions");
                        throw new Server2RollingRefusal("TempRunnerNotRetired");
                    }
                }
                if (!(P("redirectTo") is string) || !RollingPs.Ceq(P("redirectTo"), "server2")) throw new Server2RollingRefusal("TempRunnerDrainConflict");
                var stamp = RollingPs.ConvertToC994RetiredInstant(P("retiredAt"));
                if (!string.IsNullOrEmpty(expectedRetiredAt) && !RollingPs.Ceq(stamp, expectedRetiredAt)) throw new Server2RollingRefusal("TempRunnerRetirementChanged");
                return stamp;
            }

            private void AssertTempCleanupMain(object status, bool requireSha = false) => AssertTempCleanupMain(status, requireSha, _sha);

            internal static void AssertTempCleanupMain(object status, bool requireSha, string sha)
            {
                if (status == null) throw new Server2RollingRefusal("OldRunnerNotAcceptingNewWork");
                object P(string n) => RollingPs.Prop(status, n);
                foreach (var name in new[] { "available", "dispatchEligible", "acceptingNewWork" })
                    if (!(P(name) is bool) || !RollingPs.Eq(P(name), true)) throw new Server2RollingRefusal("OldRunnerNotAcceptingNewWork");
                if (!(P("draining") is bool) || !RollingPs.Eq(P("draining"), false)
                    || !RollingPs.HasExact(status, "retiredAt") || P("retiredAt") != null
                    || (requireSha && !RollingPs.Ceq(RollingPs.Str(P("buildVersion")), sha))) throw new Server2RollingRefusal("OldRunnerNotAcceptingNewWork");
            }

            private PlainObject InvokeTempContainerCleanup(object status, string projectId, string expectedRetiredAt = "", bool preview = false)
            {
                var stamp = AssertTempCleanupStatus(status, expectedRetiredAt);
                AssertTempCleanupMain(GetRunnerStatus("server2"));
                WaitRecycleTaskCensus("server2-temp", projectId);
                var operation = "c994" + NewId();
                var context = RollingPs.Object(("version", 1), ("operationId", operation), ("project", "antiphon-runner-temp"),
                    ("projectId", projectId), ("dryRun", preview), ("retiredAt", stamp));
                // The cleanup's verifier output is the function's own output: the caller keeps or voids it, never prints it.
                InvokeHostCase("retire-temp-containers", cleanup: context, output: TextWriter.Null);
                var receiptPath = Join(_evidenceRoot, "retire-temp-containers/temp-containers.json");
                var hashPath = Join(_evidenceRoot, "retire-temp-containers/temp-containers.sha256");
                if (!_files.File.Exists(receiptPath) || !_files.File.Exists(hashPath)) throw new Server2RollingRefusal("TempContainerReceiptUnavailable");
                var raw = _files.File.ReadAllText(receiptPath);
                RollingPs.AssertC994RawManifest(raw);
                var receipt = RollingPs.ParseEnumerated(raw);
                string digest;
                using (var stream = _files.File.OpenRead(receiptPath))
                    digest = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream));
                var storedHash = _files.File.ReadAllText(hashPath).Trim();
                object R(string n) => RollingPs.Prop(receipt, n);
                if (!Regex.IsMatch(storedHash, "^[0-9a-fA-F]{64}$") || !string.Equals(digest, storedHash, StringComparison.OrdinalIgnoreCase)
                    || !RollingPs.Eq(R("schema"), 1) || !RollingPs.Ceq(RollingPs.Str(R("sourceSha")), _sha)
                    || !RollingPs.Ceq(RollingPs.Str(R("runId")), _runId) || !RollingPs.Ceq(RollingPs.Str(R("operationId")), operation)
                    || !RollingPs.Ceq(RollingPs.Str(R("project")), "antiphon-runner-temp") || !RollingPs.Ceq(RollingPs.Str(R("projectId")), projectId)
                    || !RollingPs.Ceq(RollingPs.ConvertToC994RetiredInstant(R("retiredAt")), stamp)
                    || !(R("dryRun") is bool) || !RollingPs.Eq(R("dryRun"), preview)
                    || !(R("candidates") is object[]) || !(R("removals") is object[])
                    || (preview && !RollingPs.Ceq(R("outcome"), "preview"))
                    || (!preview && (!RollingPs.Ceq(R("outcome"), "completed") || !(R("finalCensus") is object[] final) || final.Length != 0)))
                    throw new Server2RollingRefusal("TempContainerReceiptUnavailable");
                return receipt as PlainObject ?? throw new Server2RollingRefusal("TempContainerReceiptUnavailable");
            }

            private PlainObject WaitTempContainerRetirement(DateTime deadline, string projectId)
            {
                var stamp = "";
                var pollMs = _timing.PollMs ?? 30000;
                while (true)
                {
                    if (C994Now() >= deadline) throw new Server2RollingRefusal("TempContainerExitTimeout");
                    var status = GetRunnerStatus("server2-temp");
                    AssertTempCleanupMain(GetRunnerStatus("server2"), requireSha: true);
                    if (status == null) throw new Server2RollingRefusal("TempRunnerStatusMissing");
                    object P(string n) => RollingPs.Prop(status, n);
                    if (RollingPs.True(P("retiredAt")))
                    {
                        var observed = RollingPs.ConvertToC994RetiredInstant(P("retiredAt"));
                        if (stamp.Length > 0 && !RollingPs.Ceq(stamp, observed)) throw new Server2RollingRefusal("TempRunnerRetirementChanged");
                        stamp = observed;
                        // A connected retirement acknowledgement is an observation, never a deletion authority. All other malformed fields
                        // refuse immediately.
                        var copy = RollingPs.Copy(status, 30);
                        foreach (var name in new[] { "available", "dispatchEligible", "acceptingNewWork" })
                        {
                            if (!(RollingPs.Prop(copy, name) is bool)) throw new Server2RollingRefusal("TempRunnerNotRetired");
                            ((PlainObject)copy)[name] = false;
                        }
                        AssertTempCleanupStatus(copy, stamp);
                        if (Equals(P("available"), false) && Equals(P("dispatchEligible"), false) && Equals(P("acceptingNewWork"), false))
                        {
                            try
                            {
                                var receipt = InvokeTempContainerCleanup(status, projectId, stamp);
                                if (C994Now() >= deadline) throw new Server2RollingRefusal("TempContainerExitTimeout");
                                return receipt;
                            }
                            catch (Exception e) when (e.Message.IndexOf("TempContainerStillRunning", StringComparison.OrdinalIgnoreCase) >= 0) { }
                        }
                    }
                    if (C994Now() >= deadline) throw new Server2RollingRefusal("TempContainerExitTimeout");
                    Sleep(pollMs);
                    _seams.C994Advance?.Invoke(pollMs);
                }
            }

            private object WaitRunnerStatus(string runnerId, Func<object, bool> ready, int minutes, string diagnosis)
            {
                var deadline = UtcNow.AddMinutes(minutes);
                var pollMs = 30000;
                if (_timing.WaitMs.HasValue) deadline = UtcNow.AddMilliseconds(_timing.WaitMs.Value);
                if (_timing.PollMs.HasValue) pollMs = _timing.PollMs.Value;
                while (true)
                {
                    var status = GetRunnerStatus(runnerId);
                    if (status != null && ready(status)) return status;
                    if (UtcNow >= deadline) throw new Server2RollingRefusal(diagnosis);
                    Sleep(pollMs);
                }
            }

            // ---- Invoke-HostCase ----

            private static bool TestHostCaseRefusalDiagnosis(string diagnosis)
            {
                if (diagnosis.Length > 300) return false;
                if (diagnosis.IndexOf(" stderr=", StringComparison.OrdinalIgnoreCase) >= 0) return RollingPs.TestC1008SafeAuditDiagnosis(diagnosis);
                return Regex.IsMatch(diagnosis, @"\A[A-Za-z][A-Za-z0-9 =._/:,-]{0,299}\z");
            }

            private void InvokeHostCase(string hostCase, string tempRetiredAt = "", string runnerId = "", PlainObject recycle = null,
                PlainObject cleanup = null, TextWriter output = null)
            {
                var manifest = RollingPs.Object(("evidenceRoot", _evidenceRoot), ("sourceSha", _sha), ("runId", _runId), ("c604Branch", "master"));
                if (cleanup != null) { RollingPs.AssertC994BridgeContext(cleanup, hostCase); manifest["tempContainerCleanup"] = cleanup; }
                if (!string.IsNullOrEmpty(tempRetiredAt)) manifest["tempRetiredAt"] = tempRetiredAt;
                if (!string.IsNullOrEmpty(runnerId)) manifest["runnerId"] = runnerId;
                if (recycle != null) { AssertRecycleContext(recycle); manifest["recycle"] = recycle; }
                if (hostCase == "runner-cache-seed" && !string.IsNullOrEmpty(_o.SavedDonor)) manifest["savedDonor"] = _o.SavedDonor;
                var manifestPath = Join(_evidenceRoot, hostCase + ".manifest.json");
                SetContent(manifestPath, RollingPs.Json(manifest, 20), ascii: true);
                var exit = _world.HostCase(hostCase, manifestPath, output ?? _host.Out, _host.Error);
                if (exit == 0) return;
                var diagnosis = "";
                var resultFile = Join(_evidenceRoot, hostCase + "/c590-result.json");
                if (_files.File.Exists(resultFile))
                {
                    try
                    {
                        var result = RollingPs.ParseEnumerated(_files.File.ReadAllText(resultFile));
                        var text = RollingPs.Prop(result, "diagnosis");
                        if (RollingPs.Ceq(text, "TempContainerStillRunning")) diagnosis = " TempContainerStillRunning";
                        else if (RollingPs.Eq(RollingPs.Prop(result, "accepted"), false) && TestHostCaseRefusalDiagnosis(RollingPs.Str(text)))
                            diagnosis = " diagnosis=" + RollingPs.Str(text);
                    }
                    catch (Exception) { }
                }
                // CARD-1693: retire-temp-runner reports its refusal from the exchange it ran (no case directory).
                if (diagnosis.Length == 0)
                {
                    string[] exchanges;
                    try
                    {
                        exchanges = _files.Directory.Exists(_evidenceRoot)
                            ? _files.Directory.GetDirectories(_evidenceRoot, "exchange-*")
                                .OrderByDescending(d => _files.Directory.GetLastWriteTimeUtc(d)).Take(1).ToArray()
                            : Array.Empty<string>();
                    }
                    catch (Exception) { exchanges = Array.Empty<string>(); }
                    if (exchanges.Length == 1)
                    {
                        try
                        {
                            var result = RollingPs.ParseEnumerated(_files.File.ReadAllText(Join(exchanges[0], "c590-result.json")));
                            var text = RollingPs.Prop(result, "diagnosis");
                            if (RollingPs.Eq(RollingPs.Prop(result, "accepted"), false) && TestHostCaseRefusalDiagnosis(RollingPs.Str(text)))
                                diagnosis = " diagnosis=" + RollingPs.Str(text);
                        }
                        catch (Exception) { }
                    }
                }
                throw new Server2RollingRefusal("HostCaseFailed " + hostCase + " exit=" + exit + diagnosis);
            }

            // ---- the temp retirement census and the temp container census ----

            // Read-only census of the four temp private volumes and every retirement journal. It creates no receipt and removes
            // nothing. Failure is before any drain POST.
            private string GetTempRetirementCensusRaw(string proofOperation = "")
            {
                Server2RollingChild result;
                try { result = _world.TempRetirementRead(Call(_currentPhase, proofOperation)); }
                catch (Server2RollingRefusal) { throw; }
                catch (Exception) { throw new Server2RollingRefusal("TempRetirementJournalUnknown"); }
                if (result.Exit != 0 || string.IsNullOrWhiteSpace(result.Stdout)) throw new Server2RollingRefusal("TempRetirementJournalUnknown");
                return result.Stdout;
            }

            private void AssertTempRetirementClearable() => CheckTempRetirementCensus(GetTempRetirementCensusRaw());

            internal static void CheckTempRetirementCensus(string raw)
            {
                object census;
                try { RollingPs.AssertC994RawManifest(raw); census = RollingPs.ParseEnumerated(raw); }
                catch (Exception) { throw new Server2RollingRefusal("TempRetirementJournalUnknown"); }
                if (RollingPs.HasExact(census, "error"))
                {
                    var error = RollingPs.Prop(census, "error");
                    if (RollingPs.CIn(error, "TempVolumesRetained", "TempRetirementOwnerActive", "TempRetirementUnresolved")) throw new Server2RollingRefusal(RollingPs.Str(error));
                    throw new Server2RollingRefusal("TempRetirementJournalUnknown");
                }
                if (!(RollingPs.Prop(census, "volumes") is object[]) || !(RollingPs.Prop(census, "journals") is object[])
                    || !RollingPs.Ceq(string.Join(",", RollingPs.Sorted(RollingPs.Names(census))), "journals,volumes"))
                    throw new Server2RollingRefusal("TempRetirementJournalUnknown");
                foreach (var name in RollingPs.Items(RollingPs.Prop(census, "volumes")))
                {
                    if (!(name is string) || !RollingPs.CIn(name, TempVolumes)) throw new Server2RollingRefusal("TempRetirementJournalUnknown");
                    if (RollingPs.In(name, TempVolumes)) throw new Server2RollingRefusal("TempVolumesRetained " + RollingPs.Str(name));
                }
                foreach (var journal in RollingPs.Items(RollingPs.Prop(census, "journals")))
                {
                    if (journal == null) continue;
                    object J(string n) => RollingPs.Prop(journal, n);
                    int schema;
                    try { schema = PlainValues.Int(J("schema")); }
                    catch (Exception) { throw new Server2RollingRefusal("TempRetirementJournalUnknown"); }
                    if (schema != 2) throw new Server2RollingRefusal("TempRetirementJournalUnknown");
                    var owner = RollingPs.Str(J("owner"));
                    if (!string.IsNullOrWhiteSpace(owner)) throw new Server2RollingRefusal("TempRetirementOwnerActive");
                    int effects = 0, intents = 0;
                    if (J("effects") != null) effects = PlainValues.Int(J("effects"));
                    if (J("intents") != null) intents = PlainValues.Int(J("intents"));
                    var state = RollingPs.Str(J("state"));
                    var closed = false;
                    if (J("closed") != null) closed = PsBool(J("closed"));
                    if (RollingPs.In(state, "Ready", "Refused", "Pending"))
                    {
                        if (effects != 0 || intents != 0) throw new Server2RollingRefusal("TempRetirementUnresolved " + state);
                    }
                    else if (RollingPs.In(state, "Completed", "Closed")) { }
                    else if (RollingPs.In(state, "Applying", "Partial", "PartialRefused"))
                    {
                        if (!closed) throw new Server2RollingRefusal("TempRetirementUnresolved " + state);
                    }
                    else throw new Server2RollingRefusal("TempRetirementJournalUnknown");
                }
            }

            // [bool]$value: a string converts by its length, a number by zero, as LanguagePrimitives does.
            private static bool PsBool(object value) => RollingPs.True(value is object[] items && items.Length == 1 ? items[0] : value);

            // Read-only host census before a retired slot can be cleared. Keep stopped containers in the census: any leftover may
            // reconnect after the clear.
            private void AssertTempProjectAbsent()
            {
                string ids;
                try
                {
                    var result = _world.TempProjectCensus(Call(_currentPhase));
                    if (result.Exit != 0) throw new InvalidOperationException("census");
                    ids = result.Stdout;
                    SetContent(Join(_evidenceRoot, "temp-project-containers.txt"), ids, ascii: true);
                }
                catch (Exception) { throw new Server2RollingRefusal("TempContainerCensusUnavailable"); }
                if (!string.IsNullOrWhiteSpace(ids)) throw new Server2RollingRefusal("TempContainersRemain");
            }

            // ---- Invoke-Phase ----

            private string _currentPhase = "";

            private static bool Ready(object s, string field, string sha) =>
                RollingPs.Eq(RollingPs.Prop(s, field), true) && RollingPs.Eq(RollingPs.Str(RollingPs.Prop(s, "buildVersion")), sha);

            private void InvokePhase(string name)
            {
                _currentPhase = name;
                InvokeHostJq("check", name);
                if (new[] { "redeploy-old", "drain-temp", "retire-temp" }.Contains(name, StringComparer.OrdinalIgnoreCase))
                {
                    var censusRunner = string.Equals(name, "redeploy-old", StringComparison.OrdinalIgnoreCase) ? "server2" : "server2-temp";
                    StartRecycleReceipt(name, censusRunner);
                }
                switch (name.ToLowerInvariant())
                {
                    case "deploy-temp": DeployTemp(); break;
                    case "drain-old": DrainOld(); break;
                    case "redeploy-old": RedeployOld(); break;
                    case "drain-temp": DrainTemp(); break;
                    case "retire-temp": RetireTemp(); break;
                }
            }

            private static object P(object o, string name) => RollingPs.Prop(o, name);

            private void DeployTemp()
            {
                if (!string.IsNullOrEmpty(_o.SavedDonor)) throw new Server2RollingRefusal("TempSavedDonorRequiresMaintenance");
                var s = GetRunnerStatus("server2-temp");
                if (s == null) throw new Server2RollingRefusal("TempRunnerStatusMissing");
                // An offline slot may still report the last container's SHA. It needs a fresh container even when that retained version
                // matches this rollout.
                if (!RollingPs.Eq(RollingPs.Str(P(s, "buildVersion")), _sha) || !RollingPs.Eq(P(s, "available"), true))
                {
                    AssertTempSeedCounters(s);
                    if (RollingPs.True(P(s, "draining")) && !RollingPs.Eq(RollingPs.Str(P(s, "redirectTo")), "server2")) throw new Server2RollingRefusal("TempRunnerDrainConflict");
                    var old = GetRunnerStatus("server2");
                    if (old == null || !RollingPs.Eq(P(old, "dispatchEligible"), true) || !RollingPs.Eq(P(old, "acceptingNewWork"), true)
                        || !RollingPs.Eq(P(old, "draining"), false) || RollingPs.True(P(old, "retiredAt"))) throw new Server2RollingRefusal("OldRunnerRedirectNotEligible");
                    AssertTempProjectAbsent();
                    // Read-only. The host recheck runs under c1008_rollout_lock (FD 8). This wrapper does not hold that lock across the
                    // host case: a nested flock on the same file deadlocks.
                    AssertTempRetirementClearable();
                    if (RollingPs.True(P(s, "retiredAt")))
                    {
                        if (!RollingPs.Eq(P(s, "available"), false) || !RollingPs.Eq(P(s, "dispatchEligible"), false)
                            || !RollingPs.Eq(P(s, "acceptingNewWork"), false)) throw new Server2RollingRefusal("TempRunnerDrainConflict");
                        const string clearReason = "CARD-0948 retired temp placeholder reactivation";
                        InvokeRunnerRequest("POST", "server2-temp", "/drain/clear", Body(("reason", clearReason)));
                        SetContent(Join(_evidenceRoot, "temp-retirement-clear.json"),
                            RollingPs.Json(RollingPs.Object(("runnerId", "server2-temp"), ("retiredAt", P(s, "retiredAt")), ("clearReason", clearReason))), ascii: true);
                        s = GetRunnerStatus("server2-temp");
                        AssertTempSeedCounters(s);
                    }
                    if (RollingPs.True(P(s, "draining")) && !RollingPs.Eq(RollingPs.Str(P(s, "redirectTo")), "server2")) throw new Server2RollingRefusal("TempRunnerDrainConflict");
                    if (!RollingPs.True(P(s, "draining")) || RollingPs.True(P(s, "retireWhenIdle")))
                    {
                        InvokeRunnerRequest("POST", "server2-temp", "/drain",
                            Body(("reason", "CARD-0948 temp deployment hold"), ("redirectTo", "server2"), ("retireWhenIdle", false)));
                        s = GetRunnerStatus("server2-temp");
                        AssertTempSeedCounters(s);
                    }
                    if (RollingPs.True(P(s, "retiredAt"))) throw new Server2RollingRefusal("TempRunnerRetiredDuringHold");
                    if (!RollingPs.True(P(s, "draining")) || !RollingPs.Eq(RollingPs.Str(P(s, "redirectTo")), "server2") || RollingPs.True(P(s, "retireWhenIdle")))
                        throw new Server2RollingRefusal("TempRunnerDrainConflict");
                    InvokeHostCase("runner-cache-seed");
                    InvokeHostCase("deploy-temp-runner");
                }
                WaitRunnerStatus("server2-temp", x => Ready(x, "dispatchEligible", _sha), 5, "TempRunnerNotEligible");
                InvokeHostCase("verify-runner-caches", runnerId: "server2-temp");
                s = GetRunnerStatus("server2-temp");
                if (s == null || RollingPs.True(P(s, "retiredAt"))) throw new Server2RollingRefusal("TempRunnerRetiredDuringHold");
                if (RollingPs.True(P(s, "draining")))
                {
                    if (!RollingPs.Eq(RollingPs.Str(P(s, "redirectTo")), "server2") || RollingPs.True(P(s, "retireWhenIdle"))) throw new Server2RollingRefusal("TempRunnerDrainConflict");
                    InvokeRunnerRequest("POST", "server2-temp", "/drain/clear", Body(("reason", "CARD-0849 cache verification passed")), holdLift: true);
                }
                WaitRunnerStatus("server2-temp", x => Ready(x, "acceptingNewWork", _sha), 5, "TempRunnerNotAcceptingNewWork");
            }

            private void DrainOld()
            {
                var temp = GetRunnerStatus("server2-temp");
                if (temp == null || !RollingPs.True(P(temp, "acceptingNewWork")) || !RollingPs.Eq(RollingPs.Str(P(temp, "buildVersion")), _sha))
                    throw new Server2RollingRefusal("TempRunnerNotAcceptingNewWork");
                var s = GetRunnerStatus("server2");
                if (s == null) throw new Server2RollingRefusal("OldRunnerStatusMissing");
                if (!RollingPs.True(P(s, "draining")))
                {
                    InvokeRunnerRequest("POST", "server2", "/drain",
                        Body(("reason", "CARD-0727 rolling upgrade"), ("redirectTo", "server2-temp"), ("retireWhenIdle", false)));
                }
                else if (!RollingPs.Eq(RollingPs.Str(P(s, "redirectTo")), "server2-temp") || RollingPs.True(P(s, "retireWhenIdle")))
                {
                    throw new Server2RollingRefusal("OldRunnerDrainConflict");
                }
                WaitRunnerStatus("server2", x => RollingPs.True(P(x, "draining")) && P(x, "sessions") != null && P(x, "runnerSessions") != null
                    && P(x, "queuedTasks") != null && RollingPs.Eq(P(x, "sessions"), 0) && RollingPs.Eq(P(x, "runnerSessions"), 0)
                    && RollingPs.Eq(P(x, "queuedTasks"), 0), _o.WaitIdleMinutes, "OldRunnerStillBusy");
            }

            private void RedeployOld()
            {
                var s = GetRunnerStatus("server2");
                if (s == null) throw new Server2RollingRefusal("OldRunnerStatusMissing");
                var atSha = RollingPs.Eq(RollingPs.Str(P(s, "buildVersion")), _sha);
                if (atSha && !Resume && !_o.DryRun) AssertNoIncompleteRecycle();
                if (atSha && !Resume && (!RollingPs.Eq(P(s, "available"), true) || !RollingPs.Eq(P(s, "dispatchEligible"), true)))
                    throw new Server2RollingRefusal("RecycleResumeRequired");
                var preservedStore = RollingPs.Str(P(s, "runnerStoreId"));
                if (!atSha || Resume || _o.DryRun)
                {
                    if (Resume && P(s, "runnerSessions") == null && P(s, "available") is bool && RollingPs.Eq(P(s, "available"), false)
                        && P(s, "dispatchEligible") is bool && RollingPs.Eq(P(s, "dispatchEligible"), false))
                    {
                        // Only the host's operation journal can prove its own prior stop/removal. This admits the request to that proof; it
                        // never authorizes deletion here.
                        var zero = RollingPs.Copy(s, 30);
                        if (zero is PlainObject plain) plain["runnerSessions"] = 0;
                        AssertZeroCounters(zero, "server2");
                    }
                    else AssertZeroCounters(s, "server2");
                    if (!(P(s, "draining") is bool) || !RollingPs.Eq(P(s, "draining"), true) || !RollingPs.Ceq(RollingPs.Str(P(s, "redirectTo")), "server2-temp")
                        || !(P(s, "retireWhenIdle") is bool) || !RollingPs.Eq(P(s, "retireWhenIdle"), false) || RollingPs.True(P(s, "retiredAt")))
                        throw new Server2RollingRefusal("OldRunnerDrainConflict");
                    if (!(P(s, "acceptingNewWork") is bool) || !RollingPs.Eq(P(s, "acceptingNewWork"), false)) throw new Server2RollingRefusal("RecycleRoutingActive");
                    var counterpart = GetRunnerStatus("server2-temp");
                    if (counterpart == null || !(P(counterpart, "acceptingNewWork") is bool) || !RollingPs.Eq(P(counterpart, "acceptingNewWork"), true))
                        throw new Server2RollingRefusal("TempRunnerNotAcceptingNewWork");
                    var projectId = GetRecycleProjectId();
                    WaitRecycleTaskCensus("server2", projectId);
                    // Default redeploy-old replaces the runner and keeps its volumes. --dry-run previews a recycle. --recycle-volumes and
                    // --resume-recycle are the only non-preview requests, and the host gate still requires enabled-<operation-id> before
                    // any stop.
                    var wantsRecycle = _o.RecycleVolumes || Resume || _o.DryRun;
                    if (wantsRecycle)
                    {
                        var operation = Resume ? _o.ResumeRecycle : "c1008" + NewId();
                        var context = RollingPs.Object(("version", 1), ("project", "antiphon-runner"), ("operationId", operation),
                            ("dryRun", _o.DryRun), ("resume", Resume), ("projectId", projectId));
                        InvokeHostCase("deploy-parent", recycle: context);
                        if (_o.DryRun) return;
                    }
                    else
                    {
                        Out("C1008_RECYCLE skipped=redeploy-old reason=not-requested");
                        InvokeHostCase("deploy-parent");
                    }
                }
                WaitRunnerStatus("server2", x => Ready(x, "dispatchEligible", _sha), 5, "OldRunnerNotDispatchEligible");
                InvokeHostCase("verify-runner-caches", runnerId: "server2");
                s = GetRunnerStatus("server2");
                if (!RollingPs.Ceq(RollingPs.Str(P(s, "runnerStoreId")), preservedStore)) throw new Server2RollingRefusal("RecycleRunnerStoreChanged");
                if (RollingPs.True(P(s, "draining")))
                {
                    if (!RollingPs.Eq(RollingPs.Str(P(s, "redirectTo")), "server2-temp") || RollingPs.True(P(s, "retireWhenIdle"))) throw new Server2RollingRefusal("OldRunnerDrainConflict");
                    InvokeRunnerRequest("POST", "server2", "/drain/clear", Body(("reason", "CARD-0727 rolling upgrade complete")));
                }
                WaitRunnerStatus("server2", x => Ready(x, "acceptingNewWork", _sha), 5, "OldRunnerNotAcceptingNewWork");
            }

            private void DrainTemp()
            {
                var deadline = C994Now().AddMinutes(_o.WaitIdleMinutes);
                if (_timing.WaitMs.HasValue) deadline = C994Now().AddMilliseconds(_timing.WaitMs.Value);
                var old = GetRunnerStatus("server2");
                AssertTempCleanupMain(old, requireSha: true);
                var s = GetRunnerStatus("server2-temp");
                if (s == null) throw new Server2RollingRefusal("TempRunnerStatusMissing");
                if (!RollingPs.True(P(s, "retiredAt")) && !RollingPs.True(P(s, "draining")))
                {
                    InvokeRunnerRequest("POST", "server2-temp", "/drain",
                        Body(("reason", "CARD-0727 rolling upgrade complete"), ("redirectTo", "server2"), ("retireWhenIdle", true)));
                }
                else if (!RollingPs.True(P(s, "retiredAt")) && (!RollingPs.Eq(RollingPs.Str(P(s, "redirectTo")), "server2") || !RollingPs.True(P(s, "retireWhenIdle"))))
                {
                    throw new Server2RollingRefusal("TempRunnerDrainConflict");
                }
                var projectId = GetRecycleProjectId();
                WaitTempContainerRetirement(deadline, projectId);
            }

            private void RetireTemp()
            {
                var s = GetRunnerStatus("server2-temp");
                var projectId = GetRecycleProjectId();
                PlainObject cleanup = null;
                if (!Resume && !Proof)
                {
                    cleanup = InvokeTempContainerCleanup(s, projectId, preview: _o.DryRun);
                    if (_o.DryRun && RollingPs.Count(cleanup["candidates"]) > 0)
                    {
                        Out("C994_PREVIEW volumeProof=pending-confirmed-absence auditPending=true");
                        return;
                    }
                    s = GetRunnerStatus("server2-temp");
                    AssertTempCleanupStatus(s, RollingPs.ConvertToC994RetiredInstant(cleanup["retiredAt"]));
                }
                var retiredAt = AssertRetiredTempCounters(s);
                AssertTempProjectAbsent();
                var old = GetRunnerStatus("server2");
                AssertTempCleanupMain(old);
                projectId = GetRecycleProjectId();
                WaitRecycleTaskCensus("server2-temp", projectId);
                var operation = Resume ? _o.ResumeRecycle : Proof ? _o.RetirementProof : "c1008" + NewId();
                var context = RollingPs.Object(("version", 1), ("project", "antiphon-runner-temp"), ("operationId", operation),
                    ("dryRun", _o.DryRun), ("resume", Resume), ("projectId", projectId));
                if (Proof)
                {
                    var rawProof = GetTempRetirementCensusRaw(_o.RetirementProof);
                    object proof;
                    try
                    {
                        RollingPs.AssertC994RawManifest(rawProof);
                        proof = RollingPs.ParseEnumerated(rawProof);
                        if (!RollingPs.Ceq(string.Join(",", RollingPs.Sorted(RollingPs.Names(proof))), "digest,mode,operationId,state")
                            || !RollingPs.Ceq(P(proof, "operationId"), _o.RetirementProof) || !RollingPs.Ceq(P(proof, "state"), "Ready")
                            || !RollingPs.Ceq(P(proof, "mode"), "scripted") || !(P(proof, "digest") is string)
                            || !RollingPs.CMatch(P(proof, "digest"), "^[0-9a-f]{64}$")) throw new FormatException("proof");
                    }
                    catch (Exception) { throw new Server2RollingRefusal("TempRetirementProofUnknown"); }
                    context.Add("expectedProofDigest", P(proof, "digest"));
                }
                if (cleanup != null) context.Add("cleanupOperationId", cleanup["operationId"]);
                // The host's dryRun mode performs the complete read-only content Preview. Normal retirement consumes that same operation's
                // Ready record; Resume keeps its existing intent-reconciliation path.
                if (!Resume && !Proof)
                {
                    var previewContext = new PlainObject();
                    foreach (var entry in context.Entries) previewContext.Add(entry.Key, entry.Value);
                    previewContext["dryRun"] = true;
                    InvokeHostCase("retire-temp-runner", tempRetiredAt: retiredAt, recycle: previewContext);
                    Out("C1609_RETIREMENT state=Ready operation=" + operation);
                }
                if (_o.DryRun) return;
                InvokeHostCase("retire-temp-runner", tempRetiredAt: retiredAt, recycle: context);
            }
        }
    }
}
