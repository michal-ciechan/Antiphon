// The live server2 bridge of the CARD-0590 stack verifier (unit 66 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md,
// W10 part 1). It replaced the live path of scripts/c590-real.ps1 (Test-C590LiveCase, Invoke-C590LiveCase and what they reach:
// the run id and source SHA, the receipt-filtered ssh, the CARD-0628/0737 opt-in Claude token refresh, the git credential smoke,
// the CARD-1609 private retirement tool bundle, the C994 and C1008 bridge contexts and the exports to scripts/c590-remote.sh) and
// scripts/c590-command.ps1 (Write-C590Result, with the CARD-1986 withdrawn-ref record kept over the coordinator's generic refusal;
// Invoke-C590 is C590Boundary). The CARD-1609 retirement coordinator is C1609Coordinator, its desktop proof C1609HostProof.
//
// DockerStackVerify.Run routes a case on LiveCases here (without ANTIPHON_C590_STUB) after the head checks the launcher made: the
// raw manifest (C994), its ConvertFrom-Json reading, the C994 cleanup context and the C1008 recycle context. Each case does what
// the script did, in its order, with the same refusals, lines and exit codes: a refusal the script threw is exit 1 and one stderr
// line ("verify-docker-stack: <reason>"); Write-C590Result writes <evidenceRoot>/c590-result.json, prints DIAGNOSIS= and ends the
// case with its exit code, after the finally that removes a copied GitHub token. Exports, remote commands, staged tool hashes and
// receipt lines are byte for byte the script's.
//
// Every external action goes through IC590BridgeWorld (ssh, scp, git, gh, bw and the coordinator's ssh) and IC1609Processes (the
// coordinator's bounded ssh, scp and git); files are IFileSystem's, the clock the host's. The CLI binds C590BridgeOs and
// C1609BoundedProcesses. Values are read as PowerShell 7 read them (RollingPs): names ignore case where the script's -contains,
// -eq and -in did and not where its -ccontains, -ceq and -cin did. Plain C#: no PowerShell types. ASCII only.
#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Antiphon.Nightly
{
    /// <summary>Write-C590Result's exit: the case is over with this code (finally blocks still run; no catch takes it).</summary>
    public sealed class C590Exit : Exception
    {
        /// <summary>The exit code.</summary>
        public C590Exit(int code) : base("exit " + code.ToString(CultureInfo.InvariantCulture))
        {
            Code = code;
        }

        /// <summary>The exit code.</summary>
        public int Code { get; }
    }

    /// <summary>The coordinator's ssh child: its three streams (input lines end with LF), exit and kill.</summary>
    public interface IC1609Duplex : IDisposable
    {
        /// <summary>The child's output (the protocol requests).</summary>
        TextReader Stdout { get; }

        /// <summary>The child's errors (read and discarded).</summary>
        TextReader Stderr { get; }

        /// <summary>The child's input (the protocol responses).</summary>
        TextWriter Stdin { get; }

        /// <summary>The child has exited.</summary>
        bool HasExited { get; }

        /// <summary>Its exit code.</summary>
        int ExitCode { get; }

        /// <summary>Waits for its exit.</summary>
        bool WaitForExit(int milliseconds);

        /// <summary>Stops it and its children.</summary>
        void Kill();
    }

    /// <summary>The bridge's external actions (production: C590BridgeOs; tests: fakes).</summary>
    public interface IC590BridgeWorld
    {
        /// <summary>
        /// Invoke-C590Ssh's child: ssh -o BatchMode=yes -o ConnectTimeout=30 -o ServerAliveInterval=30 -o ServerAliveCountMax=240
        /// mc@server2 &lt;remote&gt;, its output and errors handed to line one line at a time as they come (2&gt;&amp;1); the exit code.
        /// Throws when ssh cannot start.
        /// </summary>
        int Ssh(string remote, Action<string> line);

        /// <summary>
        /// $input | &amp; ssh -o BatchMode=yes -o ConnectTimeout=30 mc@server2 &lt;remote&gt;: the text and a newline on its input, its
        /// output discarded (the caller voided it); the exit code. Throws when ssh cannot start.
        /// </summary>
        int SshWithInput(string remote, string input);

        /// <summary>&amp; scp &lt;arguments&gt;: its output to the console unless discarded (| Out-Null); the exit code. Throws when scp cannot start.</summary>
        int Scp(IReadOnlyList<string> arguments, bool discardOutput);

        /// <summary>&amp; git -C &lt;repo&gt; rev-parse HEAD: its output lines joined by LF, or null when it printed none. Throws when git cannot start.</summary>
        string GitHead(string repo);

        /// <summary>&amp; gh auth token | Out-String: its output. Throws when gh cannot start.</summary>
        string GhToken();

        /// <summary>Get-Command bw: whether bw resolves.</summary>
        bool HasBw();

        /// <summary>&amp; bw get password &lt;item&gt; --nointeraction 2&gt;$null | Out-String with BW_SESSION set for it: its exit code and output.</summary>
        (int Exit, string Output) BwPassword(string item, string session);

        /// <summary>
        /// The retirement coordinator's child: ssh -o BatchMode=yes -o ConnectTimeout=30 -o ServerAliveInterval=30
        /// -o ServerAliveCountMax=240 mc@server2 &lt;remote&gt; with its three streams redirected, in workingDirectory when it is not
        /// empty. Throws when it cannot start.
        /// </summary>
        IC1609Duplex StartCoordinator(string remote, string workingDirectory);
    }

    /// <summary>What the bridge observes besides its host and files.</summary>
    public sealed class C590BridgeSeams
    {
        /// <summary>The external actions. Required.</summary>
        public IC590BridgeWorld World { get; init; }

        /// <summary>The coordinator's bounded children (ssh, scp, git). Required.</summary>
        public IC1609Processes Processes { get; init; }

        /// <summary>New ids (the run id, the tool stage, the phase nonce, scratch names). Default Guid.NewGuid.</summary>
        public Func<Guid> NewGuid { get; init; } = Guid.NewGuid;

        /// <summary>The process's current directory (the coordinator's start directory). Default Directory.GetCurrentDirectory.</summary>
        public Func<string> CurrentDirectory { get; init; } = Directory.GetCurrentDirectory;

        /// <summary>$HOME (the ~/.bw-session pickup). Default the user profile.</summary>
        public Func<string> Home { get; init; } = () => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }

    /// <summary>The CARD-0590 live server2 bridge.</summary>
    public static partial class C590Bridge
    {
        /// <summary>CARD-0628: the vault item of the server2 Claude token.</summary>
        public const string ClaudeTokenItem = "antiphon/server2/claude-oauth-token";

        /// <summary>CARD-0628: the server2 token file.</summary>
        public const string ClaudeTokenRemote = "/home/mc/antiphon-server2/secrets/claude_oauth_token";

        /// <summary>CARD-0628: the warning when no refresh can run.</summary>
        public const string RefreshSkipped = "C628 ClaudeOAuthTokenRefreshSkipped: refresh skipped, previous file unchanged";

        /// <summary>The tools a retirement or deploy case stages privately (scripts/, in this order).</summary>
        public static readonly string[] StagedTools = { "c590-remote.sh", "c1008-retirement.sh", "c1008-content-audit.sh", "c1008-volume-metadata.sh" };

        private static readonly string[] CacheCases =
        {
            "runner-cache-inventory", "runner-cache-fixture", "runner-cache-seed", "runner-cache-reset", "verify-runner-caches", "verify-runner-caches-retired",
            "runner-cache-prune-preview", "runner-cache-prune",
        };

        private static readonly string[] StagedCases = { "retire-temp-containers", "retire-temp-runner", "retire-temp-inventory", "deploy-parent", "deploy-temp-runner" };

        /// <summary>
        /// Assert-C1008BridgeContext: the recycle context's exact keys and values for its case (deploy-parent: antiphon-runner;
        /// retire-temp-runner: antiphon-runner-temp, which alone may carry a manual attestation, an expected proof digest or a
        /// cleanup operation). Anything else throws Server2RollingRefusal RecycleContextInvalid.
        /// </summary>
        public static void AssertC1008BridgeContext(object context, string hostCase)
        {
            void Refuse() => throw new Server2RollingRefusal("RecycleContextInvalid");
            if (context == null || !RollingPs.In(hostCase, "deploy-parent", "retire-temp-runner")) Refuse();
            object P(string name) => RollingPs.Prop(context, name);
            var keys = RollingPs.Names(context);
            var expected = new List<string> { "version", "project", "operationId", "dryRun", "resume", "projectId" };
            var retire = string.Equals(hostCase, "retire-temp-runner", StringComparison.Ordinal);
            if (keys.Contains("mode", StringComparer.Ordinal))
            {
                if (!retire || !RollingPs.Ceq(P("mode"), "manual") || !(P("attestationId") is string) || !RollingPs.CMatch(P("attestationId"), "^[0-9a-f]{64}$")) Refuse();
                expected.Add("mode");
                expected.Add("attestationId");
            }
            if (keys.Contains("expectedProofDigest", StringComparer.Ordinal))
            {
                if (!retire || !RollingPs.Eq(P("dryRun"), false) || !(P("expectedProofDigest") is string) || !RollingPs.CMatch(P("expectedProofDigest"), "^[0-9a-f]{64}$")) Refuse();
                expected.Add("expectedProofDigest");
            }
            if (RollingPs.Ceq(P("mode"), "manual") && !RollingPs.True(P("dryRun")) && !keys.Contains("expectedProofDigest", StringComparer.Ordinal)) Refuse();
            if (keys.Contains("cleanupOperationId", StringComparer.Ordinal))
            {
                if (!retire || !RollingPs.Eq(P("resume"), false) || !(P("cleanupOperationId") is string) || !RollingPs.CMatch(P("cleanupOperationId"), "^c994[0-9a-f]{32}$")) Refuse();
                expected.Add("cleanupOperationId");
            }
            if (keys.Length != expected.Count || keys.Any(k => !expected.Contains(k, StringComparer.Ordinal))) Refuse();
            var project = RollingPs.Eq(hostCase, "deploy-parent") ? "antiphon-runner" : "antiphon-runner-temp";
            var version = P("version");
            if (!RollingPs.IsInt(version) || !RollingPs.Eq(version, 1)
                || !string.Equals(RollingPs.Str(P("project")), project, StringComparison.Ordinal)
                || !RollingPs.CMatch(P("operationId"), "^c1008[0-9a-f]{32}$")
                || !(P("dryRun") is bool) || !(P("resume") is bool)
                || !RollingPs.CMatch(P("projectId"), "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$")
                || (RollingPs.True(P("dryRun")) && RollingPs.True(P("resume"))))
                Refuse();
        }

        /// <summary>
        /// Invoke-C590LiveCase: runs one live case against server2 and returns its exit code (the result's exit, 2 for a bridge
        /// refusal, 1 for a refusal the script threw; one stderr line then). manifest is the ConvertFrom-Json reading of rawManifest.
        /// </summary>
        public static int LiveCase(string hostCase, object manifest, string rawManifest, bool refreshClaudeToken, string checkout, NightlyHost host, IFileSystem files,
            C590BridgeSeams seams)
        {
            if (host == null) throw new ArgumentNullException(nameof(host));
            if (files == null) throw new ArgumentNullException(nameof(files));
            if (seams?.World == null || seams.Processes == null) throw new ArgumentNullException(nameof(seams));
            var session = new Session(checkout ?? "", host, files, seams, refreshClaudeToken);
            try
            {
                session.Run(hostCase ?? "", manifest, rawManifest);
                return 0;
            }
            catch (C590Exit exit)
            {
                return exit.Code;
            }
            catch (Exception e)
            {
                host.Error.WriteLine("verify-docker-stack: " + e.Message);
                return 1;
            }
        }

        /// <summary>One live case: the script's script-scope state.</summary>
        internal sealed partial class Session
        {
            private static readonly Regex SafeReceiptToken = new Regex("^(?:DIAGNOSIS=)?((?:Cache|Fixture|Npm|Nuget)[A-Z][A-Za-z0-9]{0,95})(?:\\s|$)");
            private static readonly Regex PassLine = new Regex("^PASS (?:F-[1-9]|PC-[0-9]{2})$");
            private static readonly Regex OriginMoved = new Regex("^RecycleOriginMoved reason=withdrawn-ref repo=[A-Za-z0-9._/-]+ ref=refs/[A-Za-z0-9._/-]+ old=[0-9a-f]{40} new=([0-9a-f]{40}|deleted)$");
            private static readonly Regex GenericRefusal = new Regex("^(RecycleGitAuditUnknown($| audit )|RemoteResultMissing exit=2$)");

            private readonly string _checkout;
            private readonly NightlyHost _host;
            private readonly IFileSystem _files;
            private readonly C590BridgeSeams _seams;
            private readonly IC590BridgeWorld _world;
            private readonly C590Boundary _boundary;
            private readonly C1609HostProof _proof;
            private readonly bool _refreshClaudeToken;

            internal Session(string checkout, NightlyHost host, IFileSystem files, C590BridgeSeams seams, bool refreshClaudeToken)
            {
                _checkout = checkout;
                _host = host;
                _files = files;
                _seams = seams;
                _world = seams.World;
                _boundary = new C590Boundary(host, files);
                _proof = new C1609HostProof(seams.Processes, files, () => host.Clock.GetUtcNow(), seams.NewGuid, host.Variables);
                _refreshClaudeToken = refreshClaudeToken;
            }

            private string Join(string first, string second) => _files.Path.Combine(first, second);

            private string Scripts(string name) => _files.Path.Combine(_files.Path.Combine(_checkout, "scripts"), name);

            /// <summary>
            /// Write-C590Result: the result, DIAGNOSIS= (print false: the proof exports, whose output their callers captured, so the line
            /// never showed), then the case ends with exitCode.
            /// </summary>
            internal Exception Result(string evidenceRoot, bool accepted, string diagnosis, int exitCode, bool print = true)
            {
                // The retirement coordinator's catch sites erase the proof exception. Preserve the closed diagnosis from this
                // invocation before writing its final result. Never replace an accepted result or another specific refusal.
                diagnosis ??= "";
                var moved = _proof.OriginMovedDiagnosis ?? "";
                if (!accepted && exitCode == 2 && GenericRefusal.IsMatch(diagnosis) && OriginMoved.IsMatch(moved)) diagnosis = moved;
                if (string.IsNullOrEmpty(evidenceRoot)) throw new C590Failure("Cannot bind argument to parameter 'EvidenceRoot' because it is an empty string.");
                _boundary.Result(evidenceRoot, accepted, diagnosis, exitCode, print: print);
                throw new C590Exit(exitCode);
            }

            private string StateDir()
            {
                var dir = _files.Path.Combine(_files.Path.Combine(_checkout, ".antiphon"), "c590-server2");
                _files.Directory.CreateDirectory(dir);
                return dir;
            }

            private string ReadRaw(string path)
            {
                var text = _files.File.ReadAllText(path);
                return text.Length == 0 ? null : text;
            }

            private static string NullMethod => "You cannot call a method on a null-valued expression.";

            private string RunId(object manifest)
            {
                if (RollingPs.Has(manifest, "runId") && RollingPs.True(RollingPs.Prop(manifest, "runId")))
                    return RollingPs.Str(RollingPs.Prop(manifest, "runId")).ToLowerInvariant();
                var path = Join(StateDir(), "run-id.txt");
                if (_files.File.Exists(path)) return (ReadRaw(path) ?? throw new C590Failure(NullMethod)).Trim();
                var id = _seams.NewGuid().ToString("N").Substring(0, 12);
                _files.File.WriteAllText(path, id, Encoding.ASCII);
                return id;
            }

            private string Sha(object manifest)
            {
                var value = RollingPs.Prop(manifest, "sourceSha");
                if (RollingPs.Has(manifest, "sourceSha") && RollingPs.True(value) && Regex.IsMatch(RollingPs.Str(value), "^[0-9a-f]{40}$", RegexOptions.IgnoreCase))
                    return RollingPs.Str(value);
                return (_world.GitHead(_checkout) ?? throw new C590Failure(NullMethod)).Trim();
            }

            /// <summary>Invoke-C590Ssh: the exit code; with a receipt, only diagnosis tokens and fixed fixture progress survive.</summary>
            internal int Ssh(string remote, string receiptPath = "")
            {
                var safe = new List<string>();
                var withReceipt = !string.IsNullOrEmpty(receiptPath);
                var code = _world.Ssh(remote, line =>
                {
                    // Never retain raw stderr, inspect output, package metadata or the remainder of a diagnosis.
                    if (!withReceipt || safe.Count >= 256) return;
                    line ??= "";
                    var token = SafeReceiptToken.Match(line);
                    if (token.Success) safe.Add(token.Groups[1].Value);
                    else if (PassLine.IsMatch(line)) safe.Add(line);
                });
                if (withReceipt)
                {
                    safe.Add("transport-exit=" + code.ToString(CultureInfo.InvariantCulture));
                    _files.File.WriteAllLines(receiptPath, safe);
                }
                return code;
            }

            private int Scp(params string[] arguments) => _world.Scp(arguments, discardOutput: false);

            private void Warning(string message) => _host.Out.WriteLine("WARNING: " + message);

            // ---- CARD-0628 D-1 / CARD-0737: the opt-in Claude token refresh ----

            private bool ClaudeTokenRefreshRequested(object manifest)
            {
                if (_refreshClaudeToken) return true;
                if (Regex.IsMatch(_host.Variables("ANTIPHON_REFRESH_CLAUDE_TOKEN") ?? "", "^(?i:1|true)$", RegexOptions.IgnoreCase)) return true;
                if (manifest == null) return false;
                return RollingPs.Has(manifest, "refreshClaudeToken")
                    && Regex.IsMatch(RollingPs.Str(RollingPs.Prop(manifest, "refreshClaudeToken")), "^(?i:1|true)$", RegexOptions.IgnoreCase);
            }

            // The vault read is opt-in. A normal deploy-parent does not call bw and does not replace the server2 token file. The token
            // goes over SSH STDIN into the server2 file at 0600: never argv, never echoed, never written to a desktop file.
            private void ClaudeTokenOnDeploy(object manifest)
            {
                if (!ClaudeTokenRefreshRequested(manifest))
                {
                    _host.Out.WriteLine("Claude token: keeping the existing server2 file (no vault refresh requested)");
                    return;
                }
                SendClaudeToken();
            }

            private bool SendClaudeToken()
            {
                var session = _host.Variables("BW_SESSION");
                if (string.IsNullOrEmpty(session))
                {
                    var pickup = Join(_seams.Home(), ".bw-session");
                    if (_files.File.Exists(pickup)) session = (ReadRaw(pickup) ?? throw new C590Failure(NullMethod)).Trim();
                }
                if (string.IsNullOrEmpty(session) || !_world.HasBw())
                {
                    Warning(RefreshSkipped);
                    return false;
                }
                var token = "";
                try
                {
                    var answer = _world.BwPassword(ClaudeTokenItem, session);
                    token = (answer.Output ?? "").Trim();
                    if (answer.Exit != 0 || token.Length == 0)
                    {
                        token = "";
                        Warning(RefreshSkipped);
                        return false;
                    }
                    var target = ClaudeTokenRemote;
                    var remote = "umask 077 && mkdir -p /home/mc/antiphon-server2/secrets && cat > '" + target + ".tmp' && chmod 0600 '" + target + ".tmp' && mv -f '" + target + ".tmp' '" + target + "'";
                    if (_world.SshWithInput(remote, token) != 0)
                    {
                        Warning("C628 ClaudeOAuthTokenNotDelivered: the SSH write failed; the previous token file, if any, is unchanged");
                        return false;
                    }
                    return true;
                }
                finally
                {
                    token = "";
                }
            }

            // ---- Invoke-C590LiveCase ----

            internal void Run(string hostCase, object manifest, string rawManifest)
            {
                object M(string name) => RollingPs.Prop(manifest, name);
                var root = RollingPs.Str(M("evidenceRoot"));
                if (root.Length == 0) throw new C590Failure("evidenceRoot is required");
                object cleanup = null;
                var cleanupExports = new List<string>();
                var cleanupOperation = "";
                if (string.Equals(hostCase, "retire-temp-containers", StringComparison.Ordinal) || RollingPs.HasExact(manifest, "tempContainerCleanup"))
                {
                    RollingPs.AssertC994RawManifest(rawManifest);
                    RollingPs.AssertC994BridgeContext(M("tempContainerCleanup"), hostCase);
                    cleanup = M("tempContainerCleanup");
                    var dry = RollingPs.True(RollingPs.Prop(cleanup, "dryRun")) ? "1" : "0";
                    var stamp = RollingPs.ConvertToC994RetiredInstant(RollingPs.Prop(cleanup, "retiredAt"));
                    cleanupExports.AddRange(new[]
                    {
                        "export C994_VERSION='1'", "export C994_OPERATION='" + RollingPs.Str(RollingPs.Prop(cleanup, "operationId")) + "'",
                        "export C994_PROJECT='" + RollingPs.Str(RollingPs.Prop(cleanup, "project")) + "'", "export C994_PROJECT_ID='" + RollingPs.Str(RollingPs.Prop(cleanup, "projectId")) + "'",
                        "export C994_DRY_RUN='" + dry + "'", "export C994_RETIRED_AT='" + stamp + "'",
                    });
                }
                var recycleContext = "";
                var recycleOperation = "";
                var recycleProjectId = "";
                var recycleDryRun = "0";
                var recycleResume = "0";
                var manualExports = new List<string>();
                if (RollingPs.Has(manifest, "recycle"))
                {
                    var recycle = M("recycle");
                    AssertC1008BridgeContext(recycle, hostCase);
                    object R(string name) => RollingPs.Prop(recycle, name);
                    if (RollingPs.HasExact(recycle, "cleanupOperationId")) cleanupOperation = RollingPs.Str(R("cleanupOperationId"));
                    recycleContext = "default";
                    recycleOperation = RollingPs.Str(R("operationId"));
                    recycleProjectId = RollingPs.Str(R("projectId"));
                    if (RollingPs.True(R("dryRun"))) recycleDryRun = "1";
                    if (RollingPs.True(R("resume"))) recycleResume = "1";
                    if (RollingPs.Ceq(R("mode"), "manual"))
                    {
                        if (!OperatorTokenReadable()) throw new C590Failure("ManualRetirementAuthority");
                        manualExports.AddRange(new[]
                        {
                            "export C1008_MODE='manual'",
                            "export C1008_MANUAL_AUTHORITY_FILE='/home/mc/antiphon-server2/manual-authority/" + recycleOperation + ".json'",
                            "export C1008_ATTESTATION_FILE='/home/mc/antiphon-server2/manual-authority/" + recycleOperation + "." + RollingPs.Str(R("attestationId")) + ".json'",
                        });
                    }
                    if (RollingPs.HasExact(recycle, "expectedProofDigest")) manualExports.Add("export C1008_EXPECTED_PROOF_DIGEST='" + RollingPs.Str(R("expectedProofDigest")) + "'");
                }
                if (RollingPs.In(hostCase, CacheCases))
                {
                    if (!RollingPs.Has(manifest, "sourceSha") || !RollingPs.CMatch(M("sourceSha"), "^[0-9a-f]{40}$")
                        || !RollingPs.Has(manifest, "runId") || !RollingPs.CMatch(M("runId"), "^[a-z0-9]{1,64}$"))
                        throw new C590Failure("CacheManifestInvalid");
                    var cacheRoot = _files.Path.GetFullPath(Join(_checkout, ".antiphon"));
                    var candidate = _files.Path.GetFullPath(root);
                    if (!candidate.StartsWith(cacheRoot + _files.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new C590Failure("CacheEvidencePathInvalid");
                    if (RollingPs.Has(manifest, "runnerId") && !RollingPs.In(RollingPs.Str(M("runnerId")), "server2", "server2-temp")) throw new C590Failure("CacheRunnerInvalid");
                    if (RollingPs.Eq(hostCase, "verify-runner-caches") && !RollingPs.Has(manifest, "runnerId")) throw new C590Failure("CacheRunnerInvalid");
                    if (RollingPs.Eq(hostCase, "runner-cache-prune") && (!RollingPs.Has(manifest, "previewRunId") || !RollingPs.CMatch(M("previewRunId"), "^c849[0-9a-f]{16}0$")))
                        throw new C590Failure("CachePreviewInvalid");
                }
                _files.Directory.CreateDirectory(root);
                var run = RunId(manifest);
                var sha = Sha(manifest);
                var project = "";
                var noBuild = "0";
                var minExecuted = "1";
                var checkpoint = hostCase;
                var broker = "0";
                if (RollingPs.Has(manifest, "project") && RollingPs.True(M("project"))) project = RollingPs.Str(M("project"));
                if (RollingPs.Has(manifest, "noBuild") && RollingPs.True(M("noBuild"))) noBuild = "1";
                if (RollingPs.Has(manifest, "minExecuted") && RollingPs.True(M("minExecuted"))) minExecuted = RollingPs.Str(M("minExecuted"));
                if (RollingPs.Has(manifest, "checkpoint") && RollingPs.True(M("checkpoint"))) checkpoint = RollingPs.Str(M("checkpoint"));
                if (RollingPs.Has(manifest, "broker") && RollingPs.True(M("broker"))) broker = "1";
                // CARD-0604 D-13. The branch the runner's checkout tracks and the production origin the host lane probes for runner
                // status, both manifest-supplied so a pre-land run can point the runner at the task branch.
                var branch = "master";
                var origin = "https://antiphon.desktop.codeperf.net";
                if (RollingPs.Has(manifest, "c604Branch") && RollingPs.True(M("c604Branch"))) branch = RollingPs.Str(M("c604Branch"));
                if (RollingPs.Has(manifest, "c604Origin") && RollingPs.True(M("c604Origin"))) origin = RollingPs.Str(M("c604Origin"));
                if (!Regex.IsMatch(branch, "^[A-Za-z0-9._/-]{1,200}$", RegexOptions.IgnoreCase)) throw new C590Failure("c604 branch rejected");
                if (!Regex.IsMatch(origin, "^https?://[A-Za-z0-9._:-]{1,200}$", RegexOptions.IgnoreCase)) throw new C590Failure("c604 origin rejected");
                if (!Regex.IsMatch(project, "^[A-Za-z0-9./_-]*$", RegexOptions.IgnoreCase)) throw new C590Failure("project path rejected");
                if (!Regex.IsMatch(checkpoint, "^[A-Za-z0-9_-]+$", RegexOptions.IgnoreCase)) throw new C590Failure("checkpoint name rejected");
                if (!Regex.IsMatch(run, "^[a-z0-9]+$", RegexOptions.IgnoreCase)) throw new C590Failure("run id rejected");
                if (!Regex.IsMatch(sha, "^[0-9a-f]{40}$", RegexOptions.IgnoreCase)) throw new C590Failure("source sha rejected");

                var tokenCopied = false;
                var tempRetiredAt = "";
                var runnerId = "";
                var previewRunId = "";
                var savedDonor = "";
                var coldSeed = "0";
                if (RollingPs.Has(manifest, "coldSeed"))
                {
                    if (!(M("coldSeed") is bool) || !RollingPs.Eq(hostCase, "runner-cache-seed")) throw new C590Failure("CacheColdModeInvalid");
                    if (RollingPs.True(M("coldSeed"))) coldSeed = "1";
                }
                if (RollingPs.Has(manifest, "savedDonor") && RollingPs.True(M("savedDonor")))
                {
                    if (!RollingPs.Eq(hostCase, "runner-cache-seed")) throw new C590Failure("CacheSavedDonorCaseInvalid");
                    savedDonor = RollingPs.Str(M("savedDonor"));
                    if (!Regex.IsMatch(savedDonor, "^/[A-Za-z0-9._/-]{1,500}$") || savedDonor.Contains("..", StringComparison.Ordinal) || savedDonor.Contains("//", StringComparison.Ordinal))
                        throw new C590Failure("CacheSavedDonorPathInvalid");
                }
                if (coldSeed == "1" && savedDonor.Length > 0) throw new C590Failure("CacheDonorSourceConflict");
                if (RollingPs.Has(manifest, "previewRunId")) previewRunId = RollingPs.Str(M("previewRunId"));
                if (previewRunId.Length > 0 && !Regex.IsMatch(previewRunId, "^c849[0-9a-f]{16}0$")) throw new C590Failure("CachePreviewInvalid");
                var expectAccepting = RollingPs.Has(manifest, "expectAccepting") && RollingPs.Eq(M("expectAccepting"), true) ? "1" : "0";
                if (RollingPs.Has(manifest, "runnerId") && RollingPs.True(M("runnerId"))) runnerId = RollingPs.Str(M("runnerId"));
                if (RollingPs.Eq(hostCase, "verify-runner-caches") && !RollingPs.In(runnerId, "server2", "server2-temp")) throw new C590Failure("runnerId rejected");
                if (runnerId.Length > 0 && !RollingPs.In(runnerId, "server2", "server2-temp")) throw new C590Failure("runnerId rejected");
                if (RollingPs.Eq(hostCase, "retire-temp-runner"))
                {
                    if (RollingPs.Has(manifest, "tempRetiredAt") && RollingPs.True(M("tempRetiredAt")))
                    {
                        // CARD-0780. ConvertFrom-Json returns ISO-8601 text as a date; restore the runner's ISO-8601 UTC form.
                        var raw = M("tempRetiredAt");
                        tempRetiredAt = raw is DateTime date ? date.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture) : RollingPs.Str(raw);
                    }
                    if (tempRetiredAt.Length > 0 && !Regex.IsMatch(tempRetiredAt, "^[0-9TZ:+.-]{10,40}$", RegexOptions.IgnoreCase)) throw new C590Failure("tempRetiredAt rejected");
                }
                try
                {
                    if (RollingPs.Eq(hostCase, "deploy-parent") && recycleDryRun != "1") ClaudeTokenOnDeploy(manifest);

                    if (RollingPs.Eq(hostCase, "git-credential-smoke"))
                    {
                        var ghToken = (_world.GhToken() ?? "").Trim();
                        if (ghToken.Length < 10) Result(root, false, "GitHubTokenUnavailable", 2);
                        var localToken = Join(StateDir(), "gh-token");
                        _files.File.WriteAllText(localToken, ghToken);
                        ghToken = "";
                        Ssh("mkdir -p /home/mc/antiphon-c590/secrets && chmod 700 /home/mc/antiphon-c590/secrets");
                        Scp("-o", "BatchMode=yes", localToken, "mc@server2:/home/mc/antiphon-c590/secrets/gh-token");
                        _files.File.Delete(localToken);
                        Ssh("chmod 600 /home/mc/antiphon-c590/secrets/gh-token");
                        tokenCopied = true;
                    }

                    if (RollingPs.Has(manifest, "filter") && RollingPs.True(M("filter")))
                    {
                        var filterPath = Join(StateDir(), "filter.txt");
                        _files.File.WriteAllText(filterPath, RollingPs.Str(M("filter")));
                        Scp("-o", "BatchMode=yes", filterPath, "mc@server2:/tmp/c590-filter-" + run + ".txt");
                        _files.File.Delete(filterPath);
                    }

                    var remoteScript = "/home/mc/antiphon-c590/c590-remote.sh";
                    if (RollingPs.CIn(hostCase, StagedCases))
                    {
                        // One private immutable bundle per invocation. No concurrent request can overwrite a running retirement's
                        // script or its current helpers.
                        var stage = "/home/mc/antiphon-c590/retirement-tools/" + _seams.NewGuid().ToString("N");
                        var prepare = "set -eu\numask 077\nparent=/home/mc/antiphon-c590/retirement-tools\n[ ! -L \"$parent\" ]\nmkdir -p -- \"$parent\"\n"
                            + "[ \"$(readlink -e -- \"$parent\")\" = \"$parent\" ]\n[ \"$(stat -c %u -- \"$parent\")\" = \"$(id -u)\" ]\nmode=\"$(stat -c %a -- \"$parent\")\"\n"
                            + "(( (8#$mode & 022) == 0 ))\nmkdir -- '" + stage + "'";
                        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(prepare));
                        if (Ssh("printf '%s' '" + encoded + "' | base64 -d | bash") != 0) Result(root, false, "RecycleReceiptUnavailable", 2);
                        var hashRows = new List<string>();
                        foreach (var name in StagedTools)
                        {
                            var local = Scripts(name);
                            using (var stream = _files.File.OpenRead(local))
                                hashRows.Add(Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant() + "  " + name);
                            if (Scp("-o", "BatchMode=yes", "-o", "ConnectTimeout=15", local, "mc@server2:" + stage + "/" + name) != 0) Result(root, false, "RecycleReceiptUnavailable", 2);
                        }
                        var seal = "set -eu\ncd '" + stage + "'\nprintf '%s\\n' '" + string.Join("\n", hashRows) + "' > tooling.sha256\n"
                            + "sha256sum -c tooling.sha256 >/dev/null\nchmod 0400 -- ./*.sh tooling.sha256\n";
                        encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(seal));
                        if (Ssh("printf '%s' '" + encoded + "' | base64 -d | bash") != 0) Result(root, false, "RecycleReceiptUnavailable", 2);
                        remoteScript = stage + "/c590-remote.sh";
                    }
                    else
                    {
                        Ssh("mkdir -p /home/mc/antiphon-c590");
                        Scp("-o", "BatchMode=yes", Scripts("c590-remote.sh"), "mc@server2:/home/mc/antiphon-c590/c590-remote.sh");
                    }
                    var remoteEntry = "bash '" + remoteScript + "'";
                    // CARD-1316. The shell resolver accepts the same positive integer. Reject a bad desktop override before it is
                    // quoted into the remote command.
                    var auditTimeoutExport = new List<string>();
                    var auditTimeout = _host.Variables("C1008_AUDIT_TIMEOUT_SECONDS");
                    if (!string.IsNullOrEmpty(auditTimeout))
                    {
                        if (!Regex.IsMatch(auditTimeout, "^[1-9][0-9]{0,6}$", RegexOptions.IgnoreCase)) throw new C590Failure("AuditTimeoutInvalid");
                        auditTimeoutExport.Add("export C1008_AUDIT_TIMEOUT_SECONDS='" + auditTimeout + "'");
                    }
                    // Unit 68: runner-cache-seed no longer copies scripts/c849-import-saved-donor.ps1 here; c590-remote.sh runs the
                    // c849-import-saved-donor command from server2's host-published CLI, mounted read-only into the runner image.
                    var exports = new List<string>();
                    exports.AddRange(cleanupExports);
                    exports.AddRange(manualExports);
                    exports.AddRange(new[]
                    {
                        "export C590_CASE='" + hostCase + "'",
                        "export C590_SHA='" + sha + "'",
                        "export C590_RUN='" + run + "'",
                        "export C590_PROJECT='" + project + "'",
                        "export C590_NOBUILD='" + noBuild + "'",
                        "export C590_MIN='" + minExecuted + "'",
                        "export C590_CP='" + checkpoint + "'",
                        "export C590_BROKER='" + broker + "'",
                        "export C590_FILTER_FILE='/tmp/c590-filter-" + run + ".txt'",
                        // CARD-0604: the branch the runner's own checkout tracks and the production origin the host lane probes for runner
                        // status. Never a secret: the phone-home shared secret is generated on server2 and never crosses this bridge.
                        "export C604_BRANCH='" + branch + "'",
                        "export C604_SERVER_ORIGIN='" + origin + "'",
                        "export C590_TEMP_RETIRED_AT='" + tempRetiredAt + "'",
                        "export C590_RUNNER_ID='" + runnerId + "'",
                        "export C590_PREVIEW_RUN='" + previewRunId + "'",
                        "export C590_SAVED_DONOR='" + savedDonor + "'",
                        "export C590_COLD_SEED='" + coldSeed + "'",
                        "export C590_EXPECT_ACCEPTING='" + expectAccepting + "'",
                        "export C1008_CONTEXT='" + recycleContext + "'",
                        "export C1008_OPERATION='" + recycleOperation + "'",
                        "export C1008_PROJECT_ID='" + recycleProjectId + "'",
                        "export C1008_DRY_RUN='" + recycleDryRun + "'",
                        "export C1008_RESUME='" + recycleResume + "'",
                        "export C1008_CLEANUP_OPERATION='" + cleanupOperation + "'",
                    });
                    exports.AddRange(auditTimeoutExport);
                    // Forward only a token bound to one operation id. Unset, 1, true, enabled, or a previous operation's token stays on
                    // the desktop and the remote refuses.
                    var applyGate = _host.Variables("ANTIPHON_VOLUME_RETIREMENT_APPLY") ?? "";
                    if (Regex.IsMatch(applyGate, "^enabled-c1008[0-9a-f]{32}$")) exports.Add("export ANTIPHON_VOLUME_RETIREMENT_APPLY='" + applyGate + "'");
                    int code;
                    if (string.Equals(hostCase, "retire-temp-runner", StringComparison.Ordinal) && recycleContext == "default")
                    {
                        var remote = string.Join("; ", exports.Concat(new[] { "export C1609_EXCHANGE='1'", "export C1609_EXCHANGE_PROOF_ROOT='/home/mc/antiphon-c590'", remoteEntry }));
                        code = Coordinate(remote, root, run, recycleOperation, recycleProjectId, Join(root, "remote-output.txt"));
                    }
                    else
                    {
                        var remote = string.Join("; ", exports.Concat(new[] { remoteEntry }));
                        code = Ssh(remote, Join(root, "remote-output.txt"));
                    }
                    var copied = _world.Scp(new[] { "-o", "BatchMode=yes", "-r", "mc@server2:/work/test-evidence/" + run + "/" + hostCase, root }, discardOutput: true);
                    if (copied != 0 && (recycleContext.Length > 0 || cleanup != null)) Result(root, false, "RecycleReceiptUnavailable", 2);
                    var resultPath = Join(Join(root, hostCase), C590Boundary.ResultFileName);
                    if (!_files.File.Exists(resultPath)) Result(root, false, "RemoteResultMissing exit=" + code.ToString(CultureInfo.InvariantCulture), 2);
                    // Get-Content -Raw reads an empty file as null, which ConvertFrom-Json passes on as no result.
                    var resultText = _files.File.ReadAllText(resultPath);
                    var result = resultText.Length == 0 ? null : RollingPs.ParseEnumerated(resultText);
                    var accepted = RollingPs.True(RollingPs.Prop(result, "accepted"));
                    if (code != 0 && accepted) Result(root, false, "RemoteTransportFailed", 2);
                    Result(root, accepted, RollingPs.Str(RollingPs.Prop(result, "diagnosis")), PlainValues.Int(RollingPs.Prop(result, "exit")));
                }
                finally
                {
                    if (tokenCopied) Ssh("rm -f /home/mc/antiphon-c590/secrets/gh-token /home/mc/antiphon-c590/askpass");
                }
            }

            // Get-RunnerOperatorToken: the operator token file (ANTIPHON_OPERATOR_TOKEN_FILE, else %LOCALAPPDATA%\Antiphon\operator-token)
            // exists and is not empty. Only its presence is checked; the value is never kept.
            private bool OperatorTokenReadable()
            {
                try
                {
                    var path = _host.Variables("ANTIPHON_OPERATOR_TOKEN_FILE");
                    if (string.IsNullOrWhiteSpace(path))
                    {
                        var local = _host.Variables("LOCALAPPDATA");
                        if (local == null) return false;
                        path = Join(Join(local, "Antiphon"), "operator-token");
                    }
                    return OperatorApi.ReadToken(_files, path, out _) != null;
                }
                catch (Exception) { return false; }
            }
        }
    }
}
