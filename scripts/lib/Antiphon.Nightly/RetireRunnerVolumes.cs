// The CARD-1609 temp volume retirement entry (unit 67 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md, W10 part 2):
// what scripts/retire-runner-volumes.ps1 did, with the manual Preview/Apply of scripts/c1609-manual.ps1 it loaded and the operator
// token reader of scripts/lib/runner-operator-token.ps1, as the retire-runner-volumes command. The CLI runs it as
//
//   antiphon-scripts retire-runner-volumes --close --operation-id <c1008 id> --expected-journal-digest <sha256> --reason-file <file>
//     [--authority-file <file>]
//   antiphon-scripts retire-runner-volumes (--preview | --dry-run | --apply) --manual --operation-id <c1008 id>
//     --retired-runner-id server2-temp --project-id <uuid> --volume-name <four temp names, comma separated> [--template]
//     [--attestation-file <file>] [--expected-proof-digest <sha256>] [--resume] [--authority-file <file>] [--evidence-directory <dir>]
//
// Close is observation only and audited (c1609_close_host on server2: no volume delete or drain command); Preview, DryRun and Apply
// are the authenticated public manual entry to the same protected host gate retire-temp uses. Exactly one of --close, --dry-run,
// --apply and --preview; --force is always refused; the old local-file bypass (C1609_RETIREMENT_ROOT, C1609_VOLUME_CENSUS_FILE)
// is refused. Every step, refusal and exit is the scripts': a refusal prints "TempRetirementCloseRefused: <reason>" or
// "TempRetirementManualRefused: <reason>" on stderr and exits 2 (the scripts' Fail-Close and Fail-C1609Manual), a remote Close
// refusal crosses only as one of its closed words; a thrown error is exit 1 and "retire-runner-volumes: <message>". Close prints
// the host's trimmed answer. The manual entry writes its packet's answer, the request (request.json) and the copied receipt
// (manifest.json) and proof (proof.md) under <evidence>/manual-retire-<UTC>-<nonce>/ and prints C1609_MANUAL_TEMPLATE <dir> or
// C1609_MANUAL state=<Ready|Completed> operation=<id> digest=<receipt sha256> evidence=<dir>. Its retire-temp-runner case runs the
// stack verifier IN PROCESS (DockerStackVerify and its live server2 bridge; the script started scripts/verify-docker-stack.ps1).
// --sha is accepted and unused, as the script's -Sha was.
//
// The operator token (ANTIPHON_OPERATOR_TOKEN_FILE, else %LOCALAPPDATA%\Antiphon\operator-token) is read through IFileSystem, is
// never printed, and only the runner status request carries it. ssh, scp, the status request, git and the verifier are the world's
// (IRetireRunnerVolumesWorld); files are IFileSystem's, the clock the host's. Plain C#: no PowerShell types. ASCII only.
#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Antiphon.Nightly
{
    /// <summary>The retire-runner-volumes command's options (retire-runner-volumes.ps1's parameters in kebab case).</summary>
    public sealed class RetireRunnerVolumesOptions
    {
        /// <summary>The CLI's usage line.</summary>
        public const string Usage = "retire-runner-volumes (--close --operation-id <id> --expected-journal-digest <sha256> --reason-file <file>"
            + " | --preview | --dry-run | --apply --manual --operation-id <id> --retired-runner-id server2-temp --project-id <uuid> --volume-name <a,b,c,d>"
            + " [--template] [--attestation-file <file>] [--expected-proof-digest <sha256>] [--resume] [--evidence-directory <dir>]) [--authority-file <file>] [--force] [--sha <sha>]";

        /// <summary>-Close.</summary>
        public bool Close { get; init; }

        /// <summary>-DryRun (the manual Preview).</summary>
        public bool DryRun { get; init; }

        /// <summary>-Apply.</summary>
        public bool Apply { get; init; }

        /// <summary>-Preview.</summary>
        public bool Preview { get; init; }

        /// <summary>-Force (always refused).</summary>
        public bool Force { get; init; }

        /// <summary>-Manual.</summary>
        public bool Manual { get; init; }

        /// <summary>-Template.</summary>
        public bool Template { get; init; }

        /// <summary>-Resume.</summary>
        public bool Resume { get; init; }

        /// <summary>-OperationId.</summary>
        public string OperationId { get; init; } = "";

        /// <summary>-ExpectedJournalDigest (Close).</summary>
        public string ExpectedJournalDigest { get; init; } = "";

        /// <summary>-ReasonFile (Close).</summary>
        public string ReasonFile { get; init; } = "";

        /// <summary>-AuthorityFile (a delegated grant).</summary>
        public string AuthorityFile { get; init; } = "";

        /// <summary>-Sha (accepted, unused).</summary>
        public string Sha { get; init; } = "";

        /// <summary>-RetiredRunnerId (manual).</summary>
        public string RetiredRunnerId { get; init; } = "";

        /// <summary>-ProjectId (manual).</summary>
        public string ProjectId { get; init; } = "";

        /// <summary>-VolumeName (manual): the names as given (the flag's value split at commas).</summary>
        public IReadOnlyList<string> VolumeName { get; init; } = Array.Empty<string>();

        /// <summary>-ExpectedProofDigest (manual Apply).</summary>
        public string ExpectedProofDigest { get; init; } = "";

        /// <summary>-AttestationFile (manual).</summary>
        public string AttestationFile { get; init; } = "";

        /// <summary>-EvidenceDirectory (manual; default &lt;checkout&gt;/.antiphon/rolling-server2).</summary>
        public string EvidenceDirectory { get; init; } = "";

        /// <summary>The checkout (the scripts' repository: c1008-retirement.sh, c590-remote.sh, its HEAD and default evidence root).</summary>
        public string Checkout { get; init; } = "";

        /// <summary>The arguments after the command name; checkout is the CLI's own.</summary>
        public static RetireRunnerVolumesOptions Parse(IReadOnlyList<string> arguments, string checkout)
        {
            var flags = NightlyArgs.Parse(arguments,
                new[] { "--operation-id", "--expected-journal-digest", "--reason-file", "--authority-file", "--sha", "--retired-runner-id", "--project-id", "--volume-name",
                    "--expected-proof-digest", "--attestation-file", "--evidence-directory" },
                new[] { "--close", "--dry-run", "--apply", "--preview", "--force", "--manual", "--template", "--resume" });
            var names = flags.Value("--volume-name");
            return new RetireRunnerVolumesOptions
            {
                Close = flags.Switch("--close"),
                DryRun = flags.Switch("--dry-run"),
                Apply = flags.Switch("--apply"),
                Preview = flags.Switch("--preview"),
                Force = flags.Switch("--force"),
                Manual = flags.Switch("--manual"),
                Template = flags.Switch("--template"),
                Resume = flags.Switch("--resume"),
                OperationId = flags.Value("--operation-id", ""),
                ExpectedJournalDigest = flags.Value("--expected-journal-digest", ""),
                ReasonFile = flags.Value("--reason-file", ""),
                AuthorityFile = flags.Value("--authority-file", ""),
                Sha = flags.Value("--sha", ""),
                RetiredRunnerId = flags.Value("--retired-runner-id", ""),
                ProjectId = flags.Value("--project-id", ""),
                VolumeName = names == null ? Array.Empty<string>() : names.Split(','),
                ExpectedProofDigest = flags.Value("--expected-proof-digest", ""),
                AttestationFile = flags.Value("--attestation-file", ""),
                EvidenceDirectory = flags.Value("--evidence-directory", ""),
                Checkout = checkout ?? "",
            };
        }
    }

    /// <summary>The ssh child of Invoke-Server2Bash: its exit code and streams, or that it ran past its deadline (and was stopped).</summary>
    public sealed class RetireRunnerVolumesChild
    {
        /// <summary>A finished child.</summary>
        public RetireRunnerVolumesChild(int exit, string stdout, string stderr)
        {
            Exit = exit;
            Stdout = stdout ?? "";
            Stderr = stderr ?? "";
        }

        /// <summary>A child stopped at its deadline.</summary>
        public static RetireRunnerVolumesChild Deadline { get; } = new RetireRunnerVolumesChild(-1, "", "") { TimedOut = true };

        /// <summary>Its exit code.</summary>
        public int Exit { get; }

        /// <summary>Its standard output.</summary>
        public string Stdout { get; }

        /// <summary>Its standard error.</summary>
        public string Stderr { get; }

        /// <summary>It ran past the deadline.</summary>
        public bool TimedOut { get; private init; }
    }

    /// <summary>The retirement entry's external actions (production: RetireRunnerVolumesOs; tests: fakes).</summary>
    public interface IRetireRunnerVolumesWorld
    {
        /// <summary>
        /// Invoke-Server2Bash's child: ssh -o BatchMode=yes -o ConnectTimeout=15 mc@server2 bash -s with script on its input, within
        /// timeoutMs. Throws when ssh cannot start or its input cannot be written.
        /// </summary>
        RetireRunnerVolumesChild Server2Bash(string script, int timeoutMs);

        /// <summary>
        /// Copy-C1609ManualEvidence's child: scp -o BatchMode=yes -o ConnectTimeout=15 mc@server2:&lt;remote&gt; &lt;destination&gt;; true
        /// when it exited 0 within 30 s and its streams drained within 2 s more. False or a throw is a failed copy.
        /// </summary>
        bool Scp(string remote, string destination);

        /// <summary>
        /// The runner status request (Invoke-RestMethod -Method Get, the X-Antiphon-Operator-Token header, 30 s): the response body. A
        /// throw (transport, status, deadline) is a failed request.
        /// </summary>
        string RunnerStatus(string url, string token);

        /// <summary>&amp; git -C &lt;repo&gt; rev-parse HEAD: its output lines (none: empty).</summary>
        IReadOnlyList<string> GitHead(string repo);

        /// <summary>The stack verifier for one case and its manifest, in process; its exit code (its output on the host's streams).</summary>
        int Verify(string hostCase, string manifest);
    }

    /// <summary>The CARD-1609 retire-runner-volumes entry (Close and the public manual Preview/Apply).</summary>
    public static class RetireRunnerVolumes
    {
        /// <summary>The four temp-private targets, in the host's order.</summary>
        public static readonly IReadOnlyList<string> Targets = new[]
        {
            "antiphon-runner-temp_runner-state", "antiphon-runner-temp_dind-data", "antiphon-runner-temp_runner-tmp", "antiphon-runner-temp_work",
        };

        /// <summary>The c590-remote.sh functions both packets carry after scripts/c1008-retirement.sh, in this order.</summary>
        public static readonly IReadOnlyList<string> PacketFunctions = new[] { "c1008_volume", "c1008_rollout_lock", "c1008_lock", "c849_lock" };

        /// <summary>The words a remote Close refusal may cross with (c1609_close_host and the rollout/cache locks in its packet).</summary>
        public static readonly IReadOnlyList<string> RemoteReasons = new[]
        {
            "Authority", "CacheMaintenanceBusy", "CensusDrift", "CensusUnknown", "ChildrenUnknown", "Context", "Daemon", "DigestMismatch", "JournalUnknown",
            "OwnerActive", "ReasonFile", "RecycleReceiptUnavailable", "RecycleRolloutBusy", "Targets", "TempRetirementOwnerActive",
        };

        /// <summary>The message PowerShell threw for a method call on a null value (git printed nothing).</summary>
        public const string NullMethod = "You cannot call a method on a null-valued expression.";

        private static readonly Regex Actor = new Regex("^[A-Za-z0-9][A-Za-z0-9._:/@-]{0,255}$");

        /// <summary>Test-OperationId.</summary>
        public static bool IsOperationId(string value) => Regex.IsMatch(value ?? "", "^c1008[0-9a-f]{32}$");

        /// <summary>
        /// Get-CloseRemoteReason: the last "TempRetirementCloseRefused: &lt;reason&gt;" line of the host's errors, when its reason is one
        /// of RemoteReasons or "TempVolumesRetained &lt;one temp target&gt;"; null otherwise.
        /// </summary>
        public static string RemoteReason(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            var found = Regex.Matches(text, "(?m)^TempRetirementCloseRefused:[ \\t]*([^\\r\\n]*)\\r?$");
            if (found.Count == 0) return null;
            var reason = found[found.Count - 1].Groups[1].Value;
            if (RemoteReasons.Contains(reason, StringComparer.Ordinal)) return reason;
            if (Regex.IsMatch(reason, "\\ATempVolumesRetained antiphon-runner-temp_(?:dind-data|runner-state|runner-tmp|work)\\z")) return reason;
            return null;
        }

        /// <summary>The packet's payload: c1008-retirement.sh, a newline and the PacketFunctions taken from c590-remote.sh (null: one is missing).</summary>
        public static string Packet(string coordinator, string remote)
        {
            var functions = new List<string>();
            foreach (var name in PacketFunctions)
            {
                var match = Regex.Match(remote ?? "", "(?ms)^" + name + "\\(\\) \\{\\n.*?^\\}");
                if (!match.Success) return null;
                functions.Add(match.Value);
            }
            return (coordinator ?? "") + "\n" + string.Join("\n", functions);
        }

        private static string Base64(string text) => Convert.ToBase64String(Encoding.UTF8.GetBytes(text ?? ""));

        private static string Hex(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

        /// <summary>
        /// New-C1609CloseScript's body after its checks: the heredoc that runs c1609_close_host on server2 with the packet (base64), its
        /// SHA-256 as C1609_CLOSE_TOOLING_DIGEST, and the trimmed reason (base64).
        /// </summary>
        public static string CloseScript(string operationId, string expectedProofDigest, string reason, string actor, string authority, string packet)
        {
            var bytes = Encoding.UTF8.GetBytes(packet ?? "");
            var remote = string.Join("\n", new[]
            {
                "set -euo pipefail",
                "umask 077",
                "SERVER2_ROOT='/home/mc/antiphon-server2'",
                "C1008_OPERATION='" + operationId + "'",
                "C1008_PROJECT=antiphon-runner-temp",
                "C1008_LOCK_HELD=0",
                "C849_LOCK_HELD=0",
                "LANE=host",
                "C1609_CLOSE_TOOLING_DIGEST='" + Hex(bytes) + "'",
                "eval \"$(printf '%s' '" + Convert.ToBase64String(bytes) + "' | base64 -d)\"",
                "require_lane() { [ \"$1\" = host ]; }",
                "write_result() { printf 'TempRetirementCloseRefused: %s\\n' \"$2\" >&2; exit \"$3\"; }",
                "c1609_custody \"$SERVER2_ROOT\" dir || exit 2",
                "for p in \"$SERVER2_ROOT/locks\" \"$SERVER2_ROOT/locks/rollout.lock\" \"$SERVER2_ROOT/locks/cache-maintenance.lock\"; do",
                "  if [ -e \"$p\" ] || [ -L \"$p\" ]; then",
                "    if [ -d \"$p\" ]; then c1609_custody \"$p\" dir; else c1609_custody \"$p\" file; fi || exit 2",
                "  fi",
                "done",
                "C1609_CLOSE_DOCKER_EXECUTABLE=\"$(command -v docker)\"",
                "[[ \"$C1609_CLOSE_DOCKER_EXECUTABLE\" = /* ]] || exit 2",
                "docker() { command timeout --kill-after=2s 10s \"$C1609_CLOSE_DOCKER_EXECUTABLE\" \"$@\"; }",
                "c1008_lock",
                "c1609_close_host '" + expectedProofDigest + "' '" + actor + "' '" + authority + "' '" + Base64(reason) + "'",
            });
            return "timeout --kill-after=5s 75s bash <<'C1609_CLOSE_PACKET_EOF'\n" + remote + "\nC1609_CLOSE_PACKET_EOF\n";
        }

        // The manual packet's literal body (c1609-manual.ps1's single-quoted here-string); __OP__, __PACKET__ and __REQUEST__ are filled in.
        private static readonly string[] ManualBody =
        {
            "set -euo pipefail",
            "umask 077",
            "SERVER2_ROOT='/home/mc/antiphon-server2'",
            "C1008_OPERATION='__OP__'",
            "C1008_PROJECT=antiphon-runner-temp",
            "C1008_TARGETS=(antiphon-runner-temp_runner-state antiphon-runner-temp_dind-data antiphon-runner-temp_runner-tmp antiphon-runner-temp_work)",
            "C1008_LOCK_HELD=0; C849_LOCK_HELD=0; LANE=host",
            "eval \"$(printf '%s' '__PACKET__' | base64 -d)\"",
            "require_lane() { [ \"$1\" = host ]; }",
            "write_result() { exit \"$3\"; }",
            "c1609_custody \"$SERVER2_ROOT\" dir || exit 2",
            "for p in \"$SERVER2_ROOT/locks\" \"$SERVER2_ROOT/locks/rollout.lock\" \"$SERVER2_ROOT/locks/cache-maintenance.lock\"; do",
            "    if [ -e \"$p\" ] || [ -L \"$p\" ]; then",
            "        if [ -d \"$p\" ]; then c1609_custody \"$p\" dir; else c1609_custody \"$p\" file; fi || exit 2",
            "    fi",
            "done",
            "C1609_MANUAL_DOCKER_EXECUTABLE=\"$(command -v docker)\"",
            "[[ \"$C1609_MANUAL_DOCKER_EXECUTABLE\" = /* ]]",
            "docker() { command timeout --kill-after=2s 10s \"$C1609_MANUAL_DOCKER_EXECUTABLE\" \"$@\"; }",
            "c1008_lock",
            "request=\"$(printf '%s' '__REQUEST__' | base64 -d)\"",
            "phase=\"$(printf '%s' \"$request\" | jq -er '.phase|select(.==\"Preview\" or .==\"Apply\")')\"",
            "fingerprints=\"$(c1609_target_snapshot)\"",
            "preview_digest=null",
            "expected=\"$(printf '%s' \"$request\" | jq -r .expectedDigest)\"",
            "if [ \"$phase\" = Apply ]; then",
            "    file=\"$SERVER2_ROOT/retirement/$C1008_OPERATION/receipt.json\"",
            "    c1609_read_canonical \"$file\" >/dev/null",
            "    [[ \"$expected\" =~ ^[0-9a-f]{64}$ ]]",
            "    [ \"$(sha256sum \"$file\" | cut -d' ' -f1)\" = \"$expected\" ]",
            "    if [ \"$(printf '%s' \"$request\" | jq -r .resume)\" = true ]; then",
            "        nonce=\"$(jq -er '.phaseNonce|select(test(\"^[0-9a-f]{32}$\"))' \"$file\")\"",
            "        preview=\"$SERVER2_ROOT/retirement/$C1008_OPERATION/preview-$nonce.json\"",
            "        c1609_read_canonical \"$preview\" >/dev/null",
            "        fingerprints=\"$(jq -cS .fingerprints \"$preview\")\"",
            "        preview_digest=\"$(sha256sum \"$preview\" | cut -d' ' -f1)\"",
            "    else",
            "        jq -e '.schema==2 and .state==\"Ready\" and .consumed==false and .mode==\"manual\"' \"$file\" >/dev/null",
            "        preview_digest=\"$expected\"",
            "    fi",
            "fi",
            "targets=\"$(printf '%s\\n' \"${C1008_TARGETS[@]}\" | jq -R . | jq -sc .)\"",
            "now=\"$(date -u +%Y-%m-%dT%H:%M:%SZ)\"",
            "if [ \"$(printf '%s' \"$request\" | jq -r .template)\" = true ]; then",
            "    jq -cnS --arg op \"$C1008_OPERATION\" --arg phase \"$phase\" --arg now \"$now\" \\",
            "      --arg digest \"$preview_digest\" --argjson fingerprints \"$fingerprints\" --argjson targets \"$targets\" \\",
            "      '{schema:1,mode:\"manual\",operationId:$op,phase:$phase,actor:\"\",authorityRef:\"\",reason:\"\",utc:$now,",
            "        targets:$targets,fingerprints:$fingerprints,previewDigest:(if $digest==\"null\" then null else $digest end)}'",
            "    exit 0",
            "fi",
            "authority_root=\"$SERVER2_ROOT/manual-authority\"",
            "[ ! -L \"$authority_root\" ]",
            "mkdir -p -- \"$authority_root\"",
            "c1609_custody \"$authority_root\" dir",
            "attestation=\"$(printf '%s' \"$request\" | jq -er .attestation | base64 -d)\"",
            "attestation=\"$(printf '%s' \"$attestation\" | jq -cS .)\"",
            "id=\"$(printf '%s\\n' \"$attestation\" | sha256sum | cut -d' ' -f1)\"",
            "[[ \"$id\" =~ ^[0-9a-f]{64}$ ]]",
            "file=\"$authority_root/$C1008_OPERATION.$id.json\"",
            "if [ -e \"$file\" ] || [ -L \"$file\" ]; then",
            "    [ \"$(c1609_read_canonical \"$file\")\" = \"$attestation\" ]",
            "else",
            "    printf '%s\\n' \"$attestation\" | c1609_persist \"$file\"",
            "fi",
            "grant=\"$authority_root/$C1008_OPERATION.json\"",
            "actor=\"$(printf '%s' \"$request\" | jq -r .actor)\"",
            "authority=\"$(printf '%s' \"$request\" | jq -r .authorityRef)\"",
            "if [ -e \"$grant\" ] || [ -L \"$grant\" ]; then",
            "    c1609_read_canonical \"$grant\" >/dev/null",
            "    jq -e --arg actor \"$actor\" --arg authority \"$authority\" --arg op \"$C1008_OPERATION\" \\",
            "      --argjson targets \"$targets\" '.schema==1 and .actor==$actor and .authorityRef==$authority and",
            "        .operationId==$op and (.targets|sort)==($targets|sort)' \"$grant\" >/dev/null",
            "fi",
            "issued=\"$(date -u -d \"$now\" +%s)\"",
            "expires=\"$(date -u -d \"@$((issued + 900))\" +%Y-%m-%dT%H:%M:%SZ)\"",
            "grant_body=\"$(jq -cnS --arg actor \"$actor\" --arg authority \"$authority\" --arg op \"$C1008_OPERATION\" \\",
            "  --arg now \"$now\" --arg expires \"$expires\" --argjson targets \"$targets\" \\",
            "  '{schema:1,actor:$actor,authorityRef:$authority,operationId:$op,issuedAt:$now,expiresAt:$expires,targets:$targets}')\"",
            "grant_digest=\"$(printf '%s\\n' \"$grant_body\" | sha256sum | cut -d' ' -f1)\"",
            "history=\"$authority_root/$C1008_OPERATION.grant.$grant_digest.json\"",
            "if [ -e \"$history\" ] || [ -L \"$history\" ]; then",
            "    [ \"$(c1609_read_canonical \"$history\")\" = \"$grant_body\" ]",
            "else",
            "    printf '%s\\n' \"$grant_body\" | c1609_persist \"$history\"",
            "fi",
            "printf '%s\\n' \"$grant_body\" | c1609_persist \"$grant\"",
            "C1008_MODE=manual; C1008_MANUAL_AUTHORITY_FILE=\"$grant\"; C1008_ATTESTATION_FILE=\"$file\"",
            "digest=\"$preview_digest\"; [ \"$digest\" != null ] || digest=''",
            "c1609_manual_attestation \"$phase\" \"$fingerprints\" \"$digest\" >/dev/null",
            "jq -cnS --arg id \"$id\" '{attestationId:$id}'",
        };

        /// <summary>
        /// New-C1609ManualPacket's text after its tooling check: the heredoc whose host half (under the same locks) answers the unsigned
        /// template, or persists the attestation and a 15-minute grant and answers {"attestationId":...}. request is the compact JSON
        /// request (operationId, phase, actor, authorityRef, template, resume, expectedDigest, attestation); no secret is in it.
        /// </summary>
        public static string ManualPacket(string operationId, string packet, string request)
        {
            var body = string.Join("\n", ManualBody).Replace("__OP__", operationId ?? "").Replace("__PACKET__", Base64(packet)).Replace("__REQUEST__", Base64(request));
            return "timeout --kill-after=5s 75s bash <<'C1609_MANUAL_PACKET_EOF'\n" + body + "\nC1609_MANUAL_PACKET_EOF\n";
        }

        /// <summary>Runs one invocation: exit 0 done, 2 refused (one stderr line), 1 a thrown error. Usage errors throw NightlyUsageException.</summary>
        public static int Run(RetireRunnerVolumesOptions options, NightlyHost host, IFileSystem files, IRetireRunnerVolumesWorld world, Func<Guid> newGuid = null)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (host == null) throw new ArgumentNullException(nameof(host));
            if (files == null) throw new ArgumentNullException(nameof(files));
            if (world == null) throw new ArgumentNullException(nameof(world));
            try
            {
                new Entry(options, host, files, world, newGuid ?? Guid.NewGuid).Run();
                return 0;
            }
            catch (C590Exit exit)
            {
                return exit.Code;
            }
            catch (Exception e) when (e is C590Failure || e is IOException || e is UnauthorizedAccessException || e is ArgumentException || e is NotSupportedException)
            {
                host.Error.WriteLine("retire-runner-volumes: " + e.Message);
                return 1;
            }
        }

        /// <summary>
        /// Invoke-Server2Bash: the host's output when ssh exited 0 with a non-blank answer. Otherwise the run ends with exit 2 and
        /// "TempRetirementCloseRefused: &lt;reason&gt;": a recognised remote reason when ssh failed, else JournalUnknown (also when it ran
        /// past the deadline). A child that cannot start or take its input throws.
        /// </summary>
        public static string Server2Bash(IRetireRunnerVolumesWorld world, TextWriter error, string script, int timeoutMs = 30000)
        {
            var child = world.Server2Bash(script, timeoutMs);
            if (child.TimedOut) throw FailClose(error, "JournalUnknown");
            if (child.Exit == 0 && !string.IsNullOrWhiteSpace(child.Stdout)) return child.Stdout;
            var remote = RemoteReason(child.Stderr);
            if (child.Exit != 0 && remote != null) throw FailClose(error, remote);
            throw FailClose(error, "JournalUnknown");
        }

        /// <summary>Fail-Close: the refusal line and the end of the run with exit 2.</summary>
        internal static C590Exit FailClose(TextWriter error, string reason)
        {
            error.WriteLine("TempRetirementCloseRefused: " + reason);
            return new C590Exit(2);
        }

        private sealed class Entry
        {
            private readonly RetireRunnerVolumesOptions _o;
            private readonly NightlyHost _host;
            private readonly IFileSystem _files;
            private readonly IRetireRunnerVolumesWorld _world;
            private readonly Func<Guid> _newGuid;

            internal Entry(RetireRunnerVolumesOptions options, NightlyHost host, IFileSystem files, IRetireRunnerVolumesWorld world, Func<Guid> newGuid)
            {
                _o = options;
                _host = host;
                _files = files;
                _world = world;
                _newGuid = newGuid;
            }

            private C590Exit FailClose(string reason) => RetireRunnerVolumes.FailClose(_host.Error, reason);

            // Fail-C1609Manual.
            private C590Exit FailManual(string reason)
            {
                _host.Error.WriteLine("TempRetirementManualRefused: " + reason);
                return new C590Exit(2);
            }

            private string Scripts(string name) => _files.Path.Combine(_o.Checkout, "scripts", name);

            // Test-Reparse.
            private bool Reparse(string path)
            {
                if (!_files.File.Exists(path) && !_files.Directory.Exists(path)) return false;
                return (_files.File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
            }

            private bool Leaf(string path) => !string.IsNullOrEmpty(path) && _files.File.Exists(path);

            // Get-RunnerOperatorToken: the trimmed token; throws (never naming its value) when the file is missing or empty.
            private string Token()
            {
                var path = _host.Variables("ANTIPHON_OPERATOR_TOKEN_FILE");
                if (string.IsNullOrWhiteSpace(path))
                {
                    var local = _host.Variables("LOCALAPPDATA");
                    if (string.IsNullOrEmpty(local)) throw new C590Failure("Cannot bind argument to parameter 'Path' because it is null.");
                    path = _files.Path.Combine(_files.Path.Combine(local, "Antiphon"), "operator-token");
                }
                var token = OperatorApi.ReadToken(_files, path, out var refusal);
                if (token == null) throw new C590Failure(refusal);
                return token;
            }

            // Get-CloseAuthority: the operator credential is required; a delegated grant names its actor and authority reference.
            private (string Actor, string Authority) Authority()
            {
                try { Token(); }
                catch (Exception e) when (!(e is C590Exit)) { throw FailClose("Authority"); }
                if (!string.IsNullOrWhiteSpace(_o.AuthorityFile))
                {
                    if (!Leaf(_o.AuthorityFile) || Reparse(_o.AuthorityFile)) throw FailClose("Authority");
                    object grant;
                    try { grant = RollingPs.ParseEnumerated(_files.File.ReadAllText(_o.AuthorityFile)); }
                    catch (Exception e) when (!(e is C590Exit)) { throw FailClose("Authority"); }
                    object G(string name) => RollingPs.Prop(grant, name);
                    if (!RollingPs.Eq(G("schema"), 1) || !Actor.IsMatch(RollingPs.Str(G("actor"))) || !Actor.IsMatch(RollingPs.Str(G("authorityRef")))
                        || !string.Equals(RollingPs.Str(G("operationId")), _o.OperationId, StringComparison.Ordinal))
                        throw FailClose("Authority");
                    return (RollingPs.Str(G("actor")), RollingPs.Str(G("authorityRef")));
                }
                return ("operator", "operator-token");
            }

            internal void Run()
            {
                if (_o.Force) throw FailClose("ForceRefused");
                var modes = new[] { _o.Close, _o.DryRun, _o.Apply, _o.Preview }.Count(m => m);
                if (modes != 1) throw FailClose("ModeExclusive");
                // Retire the old local-file test bypass as well as the census-only Preview.
                if (!string.IsNullOrEmpty(_host.Variables("C1609_RETIREMENT_ROOT")) || !string.IsNullOrEmpty(_host.Variables("C1609_VOLUME_CENSUS_FILE")))
                    throw FailClose("LocalJournalOverrideRefused");
                if (!_o.Close)
                {
                    Manual();
                    return;
                }
                if (_o.Manual || _o.Template || _o.Resume) throw FailClose("ModeExclusive");
                if (!IsOperationId(_o.OperationId)) throw FailClose("OperationId");
                if (!Regex.IsMatch(_o.ExpectedJournalDigest ?? "", "^[0-9a-f]{64}$")) throw FailClose("Digest");
                if (string.IsNullOrWhiteSpace(_o.ReasonFile) || !Leaf(_o.ReasonFile) || Reparse(_o.ReasonFile)) throw FailClose("ReasonFile");
                var reason = _files.File.ReadAllText(_o.ReasonFile).Trim();
                if (string.IsNullOrWhiteSpace(reason) || reason.Length > 4096) throw FailClose("ReasonFile");
                var auth = Authority();
                var remote = NewCloseScript(reason, auth.Actor, auth.Authority);
                string text;
                try
                {
                    text = Server2Bash(_world, _host.Error, remote, 90000);
                }
                catch (Exception e) when (!(e is C590Exit)) { throw FailClose("JournalUnknown"); }
                _host.Out.WriteLine(text.Trim());
            }

            // New-C1609CloseScript.
            private string NewCloseScript(string reason, string actor, string authority)
            {
                if (!IsOperationId(_o.OperationId) || !Regex.IsMatch(_o.ExpectedJournalDigest ?? "", "^[0-9a-f]{64}$")) throw FailClose("Context");
                if (string.IsNullOrWhiteSpace(reason) || reason.Length > 4096) throw FailClose("ReasonFile");
                reason = reason.Trim();
                var packet = Packet(_files.File.ReadAllText(Scripts("c1008-retirement.sh")), _files.File.ReadAllText(Scripts("c590-remote.sh")));
                if (packet == null) throw FailClose("CurrentTooling");
                if (!Actor.IsMatch(actor ?? "") || !Actor.IsMatch(authority ?? "")) throw FailClose("Authority");
                return CloseScript(_o.OperationId, _o.ExpectedJournalDigest, reason, actor, authority, packet);
            }

            // New-C1609ManualPacket.
            private string NewManualPacket(string phase, string actor, string authority, string attestation)
            {
                var packet = Packet(_files.File.ReadAllText(Scripts("c1008-retirement.sh")), _files.File.ReadAllText(Scripts("c590-remote.sh")));
                if (packet == null) throw FailManual("CurrentTooling");
                var request = RollingPs.Json(RollingPs.Object(("operationId", _o.OperationId), ("phase", phase), ("actor", actor), ("authorityRef", authority),
                    ("template", _o.Template), ("resume", _o.Resume), ("expectedDigest", _o.ExpectedProofDigest ?? ""), ("attestation", attestation ?? "")));
                return ManualPacket(_o.OperationId, packet, request);
            }

            // Invoke-C1609ManualRetirement (scripts/c1609-manual.ps1).
            private void Manual()
            {
                if (!_o.Manual) throw FailManual("ManualRequired");
                var preview = _o.Preview || _o.DryRun;
                if (!IsOperationId(_o.OperationId)) throw FailManual("OperationId");
                if (!string.Equals(_o.RetiredRunnerId, "server2-temp", StringComparison.Ordinal)
                    || !Regex.IsMatch(_o.ProjectId ?? "", "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"))
                    throw FailManual("Identity");
                var names = _o.VolumeName ?? Array.Empty<string>();
                if (names.Count != 4 || names.Distinct(StringComparer.OrdinalIgnoreCase).Count() != 4 || !SameSet(Targets, names)) throw FailManual("Targets");
                if (_o.Resume && !_o.Apply) throw FailManual("ResumeMode");
                if (_o.Apply && !Regex.IsMatch(_o.ExpectedProofDigest ?? "", "^[0-9a-f]{64}$")) throw FailManual("Digest");
                var auth = Authority();
                var phase = preview ? "Preview" : "Apply";
                var encoded = "";
                if (!_o.Template)
                {
                    var file = _o.AttestationFile ?? "";
                    if (!Leaf(file) || Reparse(file) || _files.FileInfo.New(file).Length > 1048576) throw FailManual("Attestation");
                    try
                    {
                        var text = _files.File.ReadAllText(file);
                        RollingPs.AssertC994RawManifest(text);
                        var value = RollingPs.ParseEnumerated(text);
                        object V(string name) => RollingPs.Prop(value, name);
                        if (!RollingPs.Ceq(V("actor"), auth.Actor) || !RollingPs.Ceq(V("authorityRef"), auth.Authority) || !RollingPs.Ceq(V("operationId"), _o.OperationId)
                            || !RollingPs.Ceq(V("phase"), phase) || !RollingPs.Ceq(V("mode"), "manual") || string.IsNullOrWhiteSpace(RollingPs.Str(V("reason"))))
                            throw new C590Failure("attestation");
                        encoded = Base64(text);
                    }
                    catch (Exception e) when (!(e is C590Exit)) { throw FailManual("Attestation"); }
                }
                var evidence = string.IsNullOrEmpty(_o.EvidenceDirectory) ? _files.Path.Combine(_o.Checkout, ".antiphon", "rolling-server2") : _files.Path.GetFullPath(_o.EvidenceDirectory);
                _files.Directory.CreateDirectory(evidence);
                var root = _files.Path.Combine(evidence, "manual-retire-" + _host.Clock.GetUtcNow().UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture)
                    + "-" + _newGuid().ToString("N"));
                _files.Directory.CreateDirectory(root);
                string response;
                object prepared;
                try
                {
                    response = Server2Bash(_world, _host.Error, NewManualPacket(phase, auth.Actor, auth.Authority, encoded), 90000);
                    RollingPs.AssertC994RawManifest(response);
                    prepared = RollingPs.ParseEnumerated(response);
                }
                catch (Exception e) when (!(e is C590Exit)) { throw FailManual("HostPreparation"); }
                if (_o.Template)
                {
                    _files.File.WriteAllText(_files.Path.Combine(root, "attestation-template.json"), response, new UTF8Encoding(false));
                    _files.File.WriteAllText(_files.Path.Combine(root, "proof.md"), "Unsigned manual attestation template. No Ready proof or deletion authority was issued.\n");
                    _host.Out.WriteLine("C1609_MANUAL_TEMPLATE " + root);
                    return;
                }
                var attestationId = RollingPs.Prop(prepared, "attestationId");
                if (!Regex.IsMatch(RollingPs.Str(attestationId), "^[0-9a-f]{64}$")) throw FailManual("HostPreparation");
                var url = OperatorApi.Base(_host.Variables("ANTIPHON_API")) + "/api/session-runners/server2-temp/status";
                string retired;
                try
                {
                    var body = _world.RunnerStatus(url, Token());
                    object status;
                    try { status = string.IsNullOrWhiteSpace(body) ? null : RollingPs.ParseEnumerated(body); }
                    catch (RollingJsonException) { status = body; }
                    retired = RollingPs.ConvertToC994RetiredInstant(RollingPs.Prop(status, "retiredAt"));
                }
                catch (Exception e) when (!(e is C590Exit)) { throw FailManual("RunnerStatus"); }
                var context = RollingPs.Object(("version", 1), ("project", "antiphon-runner-temp"), ("operationId", _o.OperationId), ("dryRun", preview),
                    ("resume", _o.Resume), ("projectId", _o.ProjectId), ("mode", "manual"), ("attestationId", RollingPs.Str(attestationId)));
                if (_o.Apply) context.Add("expectedProofDigest", _o.ExpectedProofDigest);
                var runId = "c1609manual" + _newGuid().ToString("N");
                var head = _world.GitHead(_o.Checkout) ?? Array.Empty<string>();
                if (head.Count == 0) throw new C590Failure(NullMethod);
                object sourceSha = head.Count == 1 ? head[0].Trim() : head.Select(l => (object)l.Trim()).ToArray();
                var manifest = RollingPs.Object(("evidenceRoot", root), ("runId", runId), ("sourceSha", sourceSha), ("tempRetiredAt", retired), ("recycle", context));
                var manifestPath = _files.Path.Combine(root, "request.json");
                _files.File.WriteAllText(manifestPath, RollingPs.Json(manifest, 20), new UTF8Encoding(false));
                if (_world.Verify("retire-temp-runner", manifestPath) != 0) throw FailManual("ContentOrLiveGuard");
                Copy("receipt.json", _files.Path.Combine(root, "manifest.json"));
                Copy("proof.md", _files.Path.Combine(root, "proof.md"));
                var state = preview ? "Ready" : "Completed";
                try
                {
                    var text = _files.File.ReadAllText(_files.Path.Combine(root, "manifest.json"));
                    RollingPs.AssertC994RawManifest(text);
                    var record = RollingPs.ParseEnumerated(text);
                    object R(string name) => RollingPs.Prop(record, name);
                    var consumed = R("consumed");
                    var targets = RollingPs.Items(R("targets")).Select(RollingPs.Str).ToArray();
                    if (!RollingPs.Eq(R("schema"), 2) || !RollingPs.Ceq(R("operationId"), _o.OperationId) || !RollingPs.Ceq(R("mode"), "manual")
                        || !RollingPs.Ceq(R("project"), "antiphon-runner-temp") || !RollingPs.Ceq(R("state"), state)
                        || !(consumed is bool) || !RollingPs.Eq(consumed, _o.Apply) || targets.Length != 4 || !SameSet(Targets, targets))
                        throw new C590Failure("receipt");
                }
                catch (Exception e) when (!(e is C590Exit)) { throw FailManual("ReceiptUnavailable"); }
                string digest;
                using (var stream = _files.File.OpenRead(_files.Path.Combine(root, "manifest.json")))
                    digest = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
                _host.Out.WriteLine("C1609_MANUAL state=" + state + " operation=" + _o.OperationId + " digest=" + digest + " evidence=" + root);
            }

            // Copy-C1609ManualEvidence: the host's retirement file to the local evidence; any failure is ReceiptUnavailable.
            private void Copy(string name, string destination)
            {
                bool copied;
                try { copied = _world.Scp("/home/mc/antiphon-server2/retirement/" + _o.OperationId + "/" + name, destination); }
                catch (Exception e) when (!(e is C590Exit)) { copied = false; }
                if (!copied) throw FailManual("ReceiptUnavailable");
            }

            // Compare-Object (sorted) -CaseSensitive found no difference: the same names, each as often.
            private static bool SameSet(IEnumerable<string> expected, IEnumerable<string> actual) =>
                expected.OrderBy(n => n, StringComparer.Ordinal).SequenceEqual(actual.OrderBy(n => n, StringComparer.Ordinal), StringComparer.Ordinal);
        }
    }

    /// <summary>The c1609-read-packet command's options.</summary>
    public sealed class C1609ReadPacketOptions
    {
        /// <summary>The CLI's usage line.</summary>
        public const string Usage = "c1609-read-packet (--operation <c1008 id> | --clearable) [--send]";

        /// <summary>The Ready proof's operation (a proof read).</summary>
        public string Operation { get; init; } = "";

        /// <summary>The deploy-temp clearable census instead of a proof read.</summary>
        public bool Clearable { get; init; }

        /// <summary>Run the packet on server2 (the Close transport) and print its answer, instead of printing the packet.</summary>
        public bool Send { get; init; }

        /// <summary>The checkout (its scripts/c1008-retirement.sh and scripts/c590-remote.sh).</summary>
        public string Checkout { get; init; } = "";

        /// <summary>The arguments after the command name; checkout is the CLI's own.</summary>
        public static C1609ReadPacketOptions Parse(IReadOnlyList<string> arguments, string checkout)
        {
            var flags = NightlyArgs.Parse(arguments, new[] { "--operation" }, new[] { "--clearable", "--send" });
            return new C1609ReadPacketOptions { Operation = flags.Value("--operation", ""), Clearable = flags.Switch("--clearable"), Send = flags.Switch("--send"), Checkout = checkout ?? "" };
        }
    }

    /// <summary>
    /// The CARD-1609 retirement read packet (scripts/c1609-read-packet.ps1, unit 67): RollingPs.NewC1609RetirementReadPacket, the copy
    /// the deploy-server2 command builds its retirement census from, printed for the W10 node harnesses; with --send it runs on
    /// server2 through Invoke-Server2Bash and its answer is printed, as the harness adapter did with retire-runner-volumes.ps1's
    /// transport. Exit 0; 1 and "c1609-read-packet: &lt;reason&gt;" for a refused packet (RecycleContextInvalid, TempRetirementJournalUnknown);
    /// 2 and "TempRetirementCloseRefused: &lt;reason&gt;" for a refused send.
    /// </summary>
    public static class C1609ReadPacket
    {
        /// <summary>Runs the command.</summary>
        public static int Run(C1609ReadPacketOptions options, NightlyHost host, IFileSystem files, IRetireRunnerVolumesWorld world)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (host == null) throw new ArgumentNullException(nameof(host));
            if (files == null) throw new ArgumentNullException(nameof(files));
            string packet;
            try
            {
                packet = RollingPs.NewC1609RetirementReadPacket(files.File.ReadAllText(files.Path.Combine(options.Checkout, "scripts", "c1008-retirement.sh")),
                    files.File.ReadAllText(files.Path.Combine(options.Checkout, "scripts", "c590-remote.sh")), options.Operation, options.Clearable);
            }
            catch (Exception e) when (e is Server2RollingRefusal || e is IOException || e is UnauthorizedAccessException || e is ArgumentException)
            {
                host.Error.WriteLine("c1609-read-packet: " + e.Message);
                return 1;
            }
            if (!options.Send)
            {
                host.Out.Write(packet);
                return 0;
            }
            if (world == null) throw new ArgumentNullException(nameof(world));
            try
            {
                host.Out.WriteLine(RetireRunnerVolumes.Server2Bash(world, host.Error, packet));
                return 0;
            }
            catch (C590Exit exit)
            {
                return exit.Code;
            }
            catch (Exception e)
            {
                // Invoke-Server2Bash's child could not start or take its input: the adapter's throw.
                host.Error.WriteLine("c1609-read-packet: " + e.Message);
                return 1;
            }
        }
    }
}
