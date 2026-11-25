// The am-service gateway deploy (unit 50 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md, W8 slice C; it replaced
// scripts/deploy-am-service.ps1, which nothing started by path). The CLI runs it as
//
//   antiphon-scripts deploy-am-service --ssh-target <user@host> [--deploy] [--what-if | --confirm | --no-confirm]
//       [--skip-real-traffic-check] [--timeout-sec 180]
//
// It deploys the source-built am-service gateway to one explicit SSH target (no default, no environment fallback), and only
// /home/mc/antiphon-messaging, its messaging-service Compose service and the am-service container (never the desktop Slack
// sidecar). The archive manifest is derived only from src/Antiphon.Messaging.Service/Dockerfile's COPY sources; unsupported COPY
// syntax refuses before the archive or the upload. Without --deploy (and with --deploy --what-if) the run is a read-only preflight.
// --deploy is the explicit production opt-in and asks for confirmation on the console, as the script's ShouldProcess with
// ConfirmImpact High did; --no-confirm (the script's -Confirm:$false) skips the question, and a non-interactive Deploy-role brief
// must explicitly authorize the deployment before using it. In order, as the script:
//
// 1. The target check (before any I/O): "Configuration: --ssh-target is required; ..." or "Configuration: invalid --ssh-target; ...".
// 2. The preflight: the server identity (GET <ANTIPHON_API>/api/channels/consumer, X-Antiphon-Task-Token from ANTIPHON_TASK_TOKEN),
//    the repository default group (server/appsettings.json), the gateway's effective settings, the managed Compose override, the
//    broker cluster identity of both listeners, the four identities, the server version provenance (/api/version), the local ssh,
//    scp and tar, the Dockerfile manifest and SHA-256, the remote Compose build projection and the previous container facts.
//    It ends "REMOTE DEPLOY VERDICT: ok" (or "... failed <message>" and the failure).
// 3. With --deploy and approval (else "What if: ..." or "Deployment not approved; read-only preflight completed."): the preflight
//    again, the server identity unchanged, the validated archive (bin, obj and bin-* excluded), the managed override persisted
//    (a marked existing one backed up first) and its merge verified, the upload, the remote replacement, build and recreate (the
//    previous build/src kept as a backup; no rollback), the technical verification (the container, /api/channels with five
//    attempts, the migration history, a redacted startup-log scan), the monitor readiness and sampled offsets, a new image, the
//    server identity unchanged; the transient archives are removed. It ends with its own verdict line.
//
// The last line is always "REMOTE DEPLOY VERDICT: ok" (exit 0) or "REMOTE DEPLOY VERDICT: failed <first failure, whitespace runs
// collapsed>" (exit 1); usage 2 (the CLI). Compose environments, tokens and container logs are never printed: every remote answer
// is narrowed to validated fields first. The real traffic round trip stays manual and operator-initiated through the Family
// conversation; an agent never sends test traffic to it. ssh, scp and tar run on the host's launcher (resolved on PATH as
// PowerShell's Get-Command did), HTTP, the tokens, the temporary directory, the resolution and the confirmation answer are
// AmServiceDeploySeams, the clock and ANTIPHON_* variables the host's, files IFileSystem's. Plain C#: no PowerShell types. ASCII
// only. Never name a retired chat destination or chat id here (CARD-1261).
#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Antiphon.Nightly
{
    /// <summary>The deploy-am-service command's options (deploy-am-service.ps1's parameters in kebab case).</summary>
    public sealed class AmServiceDeployOptions
    {
        /// <summary>The CLI's usage line.</summary>
        public const string Usage = "deploy-am-service --ssh-target <user@host> [--deploy] [--what-if] [--confirm | --no-confirm] [--skip-real-traffic-check] "
            + "[--timeout-sec 180]";

        private static readonly string[] Valued = { "--ssh-target", "--timeout-sec" };
        private static readonly string[] Switches = { "--deploy", "--skip-real-traffic-check", "--what-if", "--confirm", "--no-confirm" };

        /// <summary>The one user@host destination exactly as given; null when the flag was not given (the target check refuses it).</summary>
        public string SshTarget { get; init; }

        /// <summary>The explicit production opt-in: after the preflight, replace build/src and recreate am-service.</summary>
        public bool Deploy { get; init; }

        /// <summary>Report the real traffic check as explicitly skipped (it stays manual either way).</summary>
        public bool SkipRealTrafficCheck { get; init; }

        /// <summary>With Deploy: name the write and do not perform it (-WhatIf).</summary>
        public bool WhatIf { get; init; }

        /// <summary>With Deploy: ask before the write (-Confirm; also the default for this high-impact write).</summary>
        public bool Confirm { get; init; }

        /// <summary>With Deploy: do not ask (-Confirm:$false); only for an explicitly authorized deployment.</summary>
        public bool NoConfirm { get; init; }

        /// <summary>How long the verification waits for the container to run, in seconds (10-3600).</summary>
        public int TimeoutSec { get; init; } = 180;

        /// <summary>The checkout whose src/ and server/ are deployed and read.</summary>
        public string RepoRoot { get; init; } = "";

        /// <summary>The flags after the command name; the checkout comes from the CLI. A timeout outside 10-3600 is a usage error.</summary>
        public static AmServiceDeployOptions Parse(IReadOnlyList<string> arguments, string repoRoot)
        {
            var flags = NightlyArgs.Parse(arguments, Valued, Switches);
            var timeout = flags.Int("--timeout-sec", 180);
            if (timeout < 10 || timeout > 3600)
                throw new NightlyUsageException("--timeout-sec must be between 10 and 3600, not " + timeout.ToString(CultureInfo.InvariantCulture));
            if (flags.Switch("--confirm") && flags.Switch("--no-confirm"))
                throw new NightlyUsageException("--confirm and --no-confirm exclude each other");
            return new AmServiceDeployOptions
            {
                SshTarget = flags.Value("--ssh-target"),
                Deploy = flags.Switch("--deploy"),
                SkipRealTrafficCheck = flags.Switch("--skip-real-traffic-check"),
                WhatIf = flags.Switch("--what-if"),
                Confirm = flags.Switch("--confirm"),
                NoConfirm = flags.Switch("--no-confirm"),
                TimeoutSec = timeout,
                RepoRoot = repoRoot ?? "",
            };
        }
    }

    /// <summary>One HTTP answer: the status code and the body text.</summary>
    public sealed record AmServiceHttpAnswer(int StatusCode, string Body);

    /// <summary>One GET with these headers (Invoke-WebRequest's seam): a transport failure or a non-success status throws.</summary>
    public delegate AmServiceHttpAnswer AmServiceHttp(string url, IReadOnlyDictionary<string, string> headers);

    /// <summary>The deploy's seams beyond the host.</summary>
    public sealed class AmServiceDeploySeams
    {
        /// <summary>The server's HTTP (production: AmServiceDeploy.Http()). Required.</summary>
        public AmServiceHttp Http { get; init; }

        /// <summary>A fresh 32-hex token (Guid "N"): the upload, the staging directory, the backups and the local archive name.</summary>
        public Func<string> Token { get; init; } = () => Guid.NewGuid().ToString("N");

        /// <summary>The local directory the archive is created in ([IO.Path]::GetTempPath()).</summary>
        public Func<string> TempDirectory { get; init; } = Path.GetTempPath;

        /// <summary>Get-Command's application for ssh, scp and tar: its path, null when there is none.</summary>
        public Func<string, string> Resolve { get; init; } = OwnedLauncher.FindApplication;

        /// <summary>One answer to the confirmation question; null when no interactive answer can be read (then nothing is written).</summary>
        public Func<string> ReadLine { get; init; }
    }

    /// <summary>One Dockerfile COPY source: its normalized relative path and the logical line that named it first.</summary>
    public sealed record AmServiceManifestEntry(string Path, int Line);

    /// <summary>One logical Dockerfile line (continuations joined) and the physical line it starts on.</summary>
    public sealed record AmServiceDockerfileLine(int Line, string Text);

    /// <summary>The validated local archive and the entries tar listed.</summary>
    public sealed record AmServiceArchive(string Path, IReadOnlyList<string> Entries);

    /// <summary>What one deploy pass observed (the script's result object).</summary>
    public sealed class AmServiceDeployResult
    {
        /// <summary>The destination.</summary>
        public string SshTarget { get; set; }

        /// <summary>The local Dockerfile's SHA-256, lower-case hex.</summary>
        public string DockerfileHash { get; set; }

        /// <summary>The Dockerfile-derived archive manifest.</summary>
        public IReadOnlyList<AmServiceManifestEntry> Manifest { get; set; }

        /// <summary>The remote Compose build context.</summary>
        public string RemoteContext { get; set; }

        /// <summary>The remote Compose build Dockerfile.</summary>
        public string RemoteDockerfile { get; set; }

        /// <summary>The previous container's "id sha256:image status".</summary>
        public string PreviousContainer { get; set; }

        /// <summary>The previous Compose service status, "unavailable" or "unparseable".</summary>
        public string ComposeStatus { get; set; }

        /// <summary>The retained remote source backup (a deploy pass).</summary>
        public string BackupPath { get; set; }

        /// <summary>The recorded migrations (a deploy pass).</summary>
        public int MigrationCount { get; set; }

        /// <summary>The registered adapters /api/channels named (a deploy pass).</summary>
        public IReadOnlyList<string> AdapterNames { get; set; } = Array.Empty<string>();

        /// <summary>The managed Compose override.</summary>
        public string OverridePath { get; set; }

        /// <summary>The retained backup of a marked existing override (a deploy pass), else null.</summary>
        public string OverrideBackupPath { get; set; }

        /// <summary>The running container's "id sha256:image status" (a deploy pass).</summary>
        public string RunningContainer { get; set; }
    }

    /// <summary>The am-service deploy (deploy-am-service.ps1's body; see the file comment).</summary>
    public static class AmServiceDeploy
    {
        /// <summary>The preflight (and the deploy, when asked and approved) completed.</summary>
        public const int Ok = 0;

        /// <summary>A refusal or failure.</summary>
        public const int Failed = 1;

        /// <summary>The prefix of the verdict lines.</summary>
        public const string VerdictPrefix = "REMOTE DEPLOY VERDICT: ";

        /// <summary>The only remote directory this deploy touches.</summary>
        public const string RemoteRoot = "/home/mc/antiphon-messaging";

        /// <summary>The remote Compose build context.</summary>
        public const string RemoteContext = RemoteRoot + "/build/src";

        /// <summary>The Dockerfile, relative to the build context (src/ locally).</summary>
        public const string DockerfileRelative = "Antiphon.Messaging.Service/Dockerfile";

        /// <summary>
        /// The managed override's first line. The servers' existing managed overrides carry this exact text (the remote check
        /// compares it), so it keeps the name of the deleted script that wrote them first.
        /// </summary>
        public const string OverrideMarker = "# managed-by: scripts/deploy-am-service.ps1 CARD-0410 - do not hand-edit";

        /// <summary>ShouldProcess's action for the write.</summary>
        public const string WriteAction = "replace build/src and recreate am-service";

        /// <summary>The line when --skip-real-traffic-check was given.</summary>
        public const string SkippedTrafficLine = "Real traffic check explicitly skipped; the round-trip check is manual and operator-initiated through the Family "
            + "conversation, and an agent never sends test traffic to it.";

        /// <summary>The line otherwise.</summary>
        public const string RequiredTrafficLine = "Real traffic check remains required: the round-trip check is manual and operator-initiated through the Family "
            + "conversation, and an agent never sends test traffic to it.";

        /// <summary>The line when --deploy was not approved (what-if or a "no").</summary>
        public const string NotApprovedLine = "Deployment not approved; read-only preflight completed.";

        /// <summary>The remote archive the replacement extracts: /tmp/am-service-src-&lt;token&gt;.tgz.</summary>
        public const string UploadPrefix = "/tmp/am-service-src-";

        // Assert-AmServiceSshTarget: one user@host (SSH alias, DNS hostname or IPv4), never trimmed or reinterpreted.
        private static readonly Regex Target = new Regex(
            @"\A[A-Za-z0-9_][A-Za-z0-9_.-]*@[A-Za-z0-9](?:[A-Za-z0-9-]*[A-Za-z0-9])?(?:\.[A-Za-z0-9](?:[A-Za-z0-9-]*[A-Za-z0-9])?)*\z",
            RegexOptions.CultureInvariant);

        // PowerShell's -match and -notmatch ignore case; -cmatch and -cnotmatch do not.
        private const RegexOptions Ci = RegexOptions.CultureInvariant | RegexOptions.IgnoreCase;
        private static readonly Regex Name = new Regex("^[A-Za-z0-9][A-Za-z0-9._-]{0,248}$", RegexOptions.CultureInvariant);
        private static readonly Regex BrokerHost = new Regex("^[A-Za-z0-9][A-Za-z0-9.-]*$", Ci);
        private static readonly Regex Bootstrap = new Regex("^[A-Za-z0-9.-]+:[0-9]+(,[A-Za-z0-9.-]+:[0-9]+)*$", Ci);
        private static readonly Regex OverrideName = new Regex(@"^(docker-compose|compose)\.override\.(yml|yaml)$", Ci);
        private static readonly Regex Cluster = new Regex("^[A-Za-z0-9]([A-Za-z0-9._-]*[A-Za-z0-9])?$", Ci);
        private static readonly Regex Fact = new Regex("^[0-9a-f]{12,64} sha256:[0-9a-f]{12,64} (created|running|paused|restarting|removing|exited|dead)$",
            RegexOptions.CultureInvariant);
        private static readonly Regex ComposeState = new Regex("^(created|running|paused|restarting|removing|exited|dead)$", Ci);
        private static readonly Regex Sha = new Regex("^[a-f0-9]{40}$", Ci);
        private static readonly Regex MigrationRow = new Regex(@"^\d{14}_.+$", Ci);
        private static readonly Regex Copy = new Regex(@"^(?i:COPY)\s+(.*)$", Ci);
        private static readonly Regex Continuation = new Regex(@"(?<!\\)\\\s*$", Ci);
        private static readonly Regex FromOption = new Regex("^(?i:--from)=.+$", Ci);
        private static readonly Regex Whitespace = new Regex(@"\s+", Ci);
        private static readonly Regex[] Unsafe = { new Regex(@"\$\{", Ci), new Regex(@"\$[A-Za-z_]", Ci), new Regex(@"[*?\[\]]", Ci) };
        private static readonly Regex[] Absolute = { new Regex(@"^(?:[A-Za-z]:)?[\\/]", Ci), new Regex("^[A-Za-z]:", Ci) };

        private static readonly JsonDocumentOptions SettingsJson = new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };

        /// <summary>
        /// The command: the target check, the preflight, then with --deploy the approval and the deploy pass. Every failure is
        /// caught; the last line is the verdict. Returns 0 or 1.
        /// </summary>
        public static int Run(AmServiceDeployOptions options, NightlyHost host, IFileSystem files, AmServiceDeploySeams seams)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (host == null) throw new ArgumentNullException(nameof(host));
            if (files == null) throw new ArgumentNullException(nameof(files));
            if (seams?.Http == null) throw new ArgumentNullException(nameof(seams));
            Exception failure = null;
            try
            {
                AssertSshTarget(options.SshTarget);
                var root = options.RepoRoot;
                Deploy(root, false, options.SkipRealTrafficCheck, options.TimeoutSec, options.SshTarget, host, files, seams);
                if (options.Deploy)
                {
                    if (ShouldProcess(options, host, seams, options.SshTarget + ":" + RemoteRoot))
                        Deploy(root, true, options.SkipRealTrafficCheck, options.TimeoutSec, options.SshTarget, host, files, seams);
                    else host.Out.WriteLine(NotApprovedLine);
                }
            }
            catch (Exception e)
            {
                failure = e;
            }
            if (failure != null)
            {
                host.Out.WriteLine(VerdictPrefix + "failed " + Collapse(failure.Message));
                return Failed;
            }
            host.Out.WriteLine(VerdictPrefix + "ok");
            return Ok;
        }

        /// <summary>
        /// Invoke-AmServiceDeployment: one pass (performDeploy false: the read-only preflight), then "REMOTE DEPLOY VERDICT: ok"; on
        /// a failure "REMOTE DEPLOY VERDICT: failed &lt;message&gt;" and the failure is rethrown.
        /// </summary>
        public static AmServiceDeployResult Deploy(string repositoryRoot, bool performDeploy, bool skipTrafficCheck, int pollTimeoutSec, string sshTarget,
            NightlyHost host, IFileSystem files, AmServiceDeploySeams seams)
        {
            if (host == null) throw new ArgumentNullException(nameof(host));
            if (files == null) throw new ArgumentNullException(nameof(files));
            if (seams?.Http == null) throw new ArgumentNullException(nameof(seams));
            try
            {
                var result = new Pass(host, files, seams, sshTarget).Run(repositoryRoot, performDeploy, skipTrafficCheck, pollTimeoutSec);
                host.Out.WriteLine(VerdictPrefix + "ok");
                return result;
            }
            catch (Exception e)
            {
                host.Out.WriteLine(VerdictPrefix + "failed " + e.Message);
                throw;
            }
        }

        /// <summary>Assert-AmServiceSshTarget: a blank target or anything but one user@host refuses, before any I/O.</summary>
        public static void AssertSshTarget(string sshTarget)
        {
            if (string.IsNullOrWhiteSpace(sshTarget))
                throw new InvalidOperationException("Configuration: --ssh-target is required; specify one user@host destination.");
            // Do not trim or reinterpret input. IPv6, port suffixes and SSH options are outside this narrow interface; an explicitly
            // configured SSH alias can carry those settings.
            if (!Target.IsMatch(sshTarget))
                throw new InvalidOperationException("Configuration: invalid --ssh-target; expected one user@host (SSH alias, DNS hostname or IPv4), without "
                    + "whitespace, options, paths or port suffixes.");
        }

        /// <summary>
        /// ConvertTo-DockerfileLogicalLines: the file's lines, blank and comment lines skipped outside a continuation, a trailing
        /// unescaped backslash joining the next line; an unterminated continuation refuses.
        /// </summary>
        public static IReadOnlyList<AmServiceDockerfileLine> LogicalLines(IFileSystem files, string dockerfilePath)
        {
            if (files == null) throw new ArgumentNullException(nameof(files));
            if (!files.File.Exists(dockerfilePath)) throw new InvalidOperationException("Dockerfile not found: " + dockerfilePath);
            var result = new List<AmServiceDockerfileLine>();
            var pending = "";
            var start = 0;
            var number = 0;
            using (var reader = new StringReader(files.File.ReadAllText(dockerfilePath)))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    number++;
                    var trimmed = line.Trim();
                    if (pending.Length == 0 && (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith("#", StringComparison.Ordinal))) continue;
                    if (pending.Length == 0) start = number;
                    if (Continuation.IsMatch(trimmed))
                    {
                        pending += Continuation.Replace(trimmed, "").TrimEnd() + " ";
                        continue;
                    }
                    var text = (pending + trimmed).Trim();
                    pending = "";
                    if (text.Length > 0) result.Add(new AmServiceDockerfileLine(start, text));
                }
            }
            if (pending.Length > 0) throw new InvalidOperationException("Dockerfile line " + Number(start) + " has an unterminated continuation.");
            return result;
        }

        /// <summary>
        /// Get-AmServiceDockerfileManifest: every local COPY source (COPY --from lines skipped), de-duplicated ignoring case, in
        /// order. JSON-array syntax, another option, a variable or glob, an absolute or parent-traversing source, a source outside
        /// the context or a missing source refuses; so does a Dockerfile with none.
        /// </summary>
        public static IReadOnlyList<AmServiceManifestEntry> Manifest(IFileSystem files, string dockerfilePath, string contextRoot)
        {
            if (files == null) throw new ArgumentNullException(nameof(files));
            var context = files.Path.GetFullPath(contextRoot);
            if (!files.Directory.Exists(context)) throw new InvalidOperationException("Docker context root not found: " + context);
            var prefix = context.TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
            var entries = new List<AmServiceManifestEntry>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var logical in LogicalLines(files, dockerfilePath))
            {
                var match = Copy.Match(logical.Text);
                if (!match.Success) continue;
                var at = "Dockerfile COPY line " + Number(logical.Line);
                var body = match.Groups[1].Value.Trim();
                if (body.StartsWith("[", StringComparison.Ordinal)) throw new InvalidOperationException(at + " uses unsupported JSON-array syntax.");
                var tokens = Whitespace.Split(body).Where(token => token.Length > 0).ToList();
                if (tokens.Count == 0) throw new InvalidOperationException(at + " has no arguments.");
                if (FromOption.IsMatch(tokens[0])) continue;
                if (tokens[0].StartsWith("--", StringComparison.Ordinal)) throw new InvalidOperationException(at + " has unrecognised option '" + tokens[0] + "'.");
                if (tokens.Count < 2) throw new InvalidOperationException(at + " needs a source and destination.");
                foreach (var source in tokens.Take(tokens.Count - 1))
                {
                    if (Unsafe.Any(pattern => pattern.IsMatch(source))) throw new InvalidOperationException(at + " has unsafe variable or glob '" + source + "'.");
                    if (Absolute.Any(pattern => pattern.IsMatch(source))) throw new InvalidOperationException(at + " has absolute source '" + source + "'.");
                    var normalized = source.Replace('\\', '/').TrimEnd('/');
                    if (normalized.Length == 0) throw new InvalidOperationException(at + " has an empty source.");
                    if (normalized.Split('/').Contains("..")) throw new InvalidOperationException(at + " has parent traversal '" + source + "'.");
                    var candidate = files.Path.GetFullPath(files.Path.Combine(context, normalized));
                    if (!(string.Equals(candidate, context, StringComparison.OrdinalIgnoreCase) || candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
                        throw new InvalidOperationException(at + " resolves outside context: '" + source + "'.");
                    if (!files.File.Exists(candidate) && !files.Directory.Exists(candidate))
                        throw new InvalidOperationException(at + " source does not exist: '" + source + "'.");
                    if (seen.Add(normalized)) entries.Add(new AmServiceManifestEntry(normalized, logical.Line));
                }
            }
            if (entries.Count == 0) throw new InvalidOperationException("No local Dockerfile COPY sources were found.");
            return entries;
        }

        /// <summary>
        /// New-AmServiceArchive: tar -czf &lt;bare name&gt; in the temporary directory (a bare name has no colon, which GNU tar would
        /// read as host:path), bin, obj and bin-* excluded at every depth, -C the context, the manifest's paths; then the listing's
        /// checks (ListArchive). A failed create or check removes the file.
        /// </summary>
        public static AmServiceArchive CreateArchive(NightlyHost host, IFileSystem files, AmServiceDeploySeams seams, string contextRoot,
            IReadOnlyList<AmServiceManifestEntry> manifest)
        {
            if (host == null) throw new ArgumentNullException(nameof(host));
            if (files == null) throw new ArgumentNullException(nameof(files));
            if (seams == null) throw new ArgumentNullException(nameof(seams));
            var directory = seams.TempDirectory();
            var name = "am-service-src-" + seams.Token() + ".tgz";
            var path = files.Path.Combine(directory, name);
            var arguments = new List<string> { "-czf", name, "--exclude=bin", "--exclude=*/bin", "--exclude=obj", "--exclude=*/obj", "--exclude=bin-*", "--exclude=*/bin-*", "-C", contextRoot };
            arguments.AddRange(manifest.Select(entry => entry.Path));
            var created = Native(host, seams, "tar", arguments, directory);
            if (created.ExitCode != 0)
            {
                Remove(files, path);
                throw new InvalidOperationException("Could not create deployment archive (tar exit " + Number(created.ExitCode) + ").");
            }
            try
            {
                return new AmServiceArchive(path, ListArchive(host, files, seams, path, manifest));
            }
            catch
            {
                Remove(files, path);
                throw;
            }
        }

        /// <summary>
        /// Test-AmServiceArchive: tar -tzf &lt;bare name&gt; in the archive's directory (leading "." and "/" trimmed from each line); a
        /// bin, obj or bin-* segment refuses, and every manifest path must be an entry or contain one.
        /// </summary>
        public static IReadOnlyList<string> ListArchive(NightlyHost host, IFileSystem files, AmServiceDeploySeams seams, string archivePath,
            IReadOnlyList<AmServiceManifestEntry> manifest)
        {
            if (files == null) throw new ArgumentNullException(nameof(files));
            var listed = Native(host, seams, "tar", new[] { "-tzf", files.Path.GetFileName(archivePath) }, files.Path.GetDirectoryName(archivePath));
            var lines = listed.Lines.Select(line => line.TrimStart('.', '/')).ToList();
            if (listed.ExitCode != 0) throw new InvalidOperationException("Could not list deployment archive (tar exit " + Number(listed.ExitCode) + ").");
            foreach (var line in lines)
                if (line.TrimEnd('/').Split('/').Any(IsBuildOutput))
                    throw new InvalidOperationException("Deployment archive contains forbidden build output: " + line);
            foreach (var source in manifest)
            {
                var path = source.Path.TrimEnd('/');
                if (!lines.Any(line => string.Equals(line.TrimEnd('/'), path, StringComparison.OrdinalIgnoreCase) || line.StartsWith(path + "/", StringComparison.Ordinal)))
                    throw new InvalidOperationException("Deployment archive is missing COPY source '" + path + "' from line " + Number(source.Line) + ".");
            }
            return lines;
        }

        /// <summary>
        /// Get-AmServiceMigrationIds: the base names of the directory's *.cs files that are not hidden, without *.Designer.cs and
        /// *ModelSnapshot.cs, sorted and unique ignoring case.
        /// </summary>
        public static IReadOnlyList<string> MigrationIds(IFileSystem files, string directory)
        {
            if (files == null) throw new ArgumentNullException(nameof(files));
            if (!files.Directory.Exists(directory)) throw new InvalidOperationException("Migration directory not found: " + directory);
            var comparer = StringComparer.CurrentCultureIgnoreCase;
            return files.Directory.EnumerateFiles(directory, "*.cs")
                .Where(path => (files.File.GetAttributes(path) & FileAttributes.Hidden) == 0)
                .Select(path => files.Path.GetFileName(path))
                .Where(name => !name.EndsWith(".Designer.cs", StringComparison.OrdinalIgnoreCase) && !name.EndsWith("ModelSnapshot.cs", StringComparison.OrdinalIgnoreCase))
                .Select(name => files.Path.GetFileNameWithoutExtension(name))
                .Distinct(comparer)
                .OrderBy(id => id, comparer)
                .ToList();
        }

        /// <summary>
        /// ConvertFrom-AmServiceJsonOutput: ssh merges rpk and python diagnostics into the output, so the whole trimmed text is read
        /// as JSON, else the slice from its first "{" to its last "}". Empty or unparseable text refuses.
        /// </summary>
        public static JsonNode ParseJsonOutput(string text)
        {
            var trimmed = (text ?? "").Trim();
            if (trimmed.Length == 0) throw new InvalidOperationException("JSON output was empty.");
            try { return Json(trimmed); }
            catch (Exception e) when (IsJsonFailure(e)) { }
            var start = trimmed.IndexOf('{');
            var end = trimmed.LastIndexOf('}');
            if (start >= 0 && end > start)
            {
                try { return Json(trimmed.Substring(start, end - start + 1)); }
                catch (Exception e) when (IsJsonFailure(e)) { }
            }
            throw new InvalidOperationException("JSON output was not parseable.");
        }

        /// <summary>
        /// Assert-AmServiceBrokerIdentity: rpk cluster info through each listener (the Compose service redpanda; am-redpanda is only
        /// the container name, and rpk's stderr is discarded) must name one exact cluster.
        /// </summary>
        public static void AssertBrokerIdentity(NightlyHost host, AmServiceDeploySeams seams, string sshTarget, string serverBrokers, string gatewayBrokers)
        {
            if (host == null) throw new ArgumentNullException(nameof(host));
            new Pass(host, null, seams, sshTarget).BrokerIdentity(serverBrokers, gatewayBrokers);
        }

        /// <summary>ConvertTo-AmServiceContainerFact: "&lt;id&gt; sha256:&lt;image&gt; &lt;status&gt;" or a refusal.</summary>
        public static string ContainerFact(string text)
        {
            if (!Fact.IsMatch(text ?? "")) throw new InvalidOperationException("Invalid container/image/status projection.");
            return text;
        }

        // ---- the remote commands, exactly as the script sent them ----

        /// <summary>The gateway settings projection: the effective settings, or (merged) the Compose environment's two groups only.</summary>
        public static string GatewaySettingsCommand(bool merged) =>
            "cd " + RemoteRoot + " && python3 - <<'C0410_GATEWAY_SETTINGS'\n"
            + "import json,subprocess\n"
            + "d=json.loads(subprocess.check_output([\"docker\",\"compose\",\"config\",\"--format\",\"json\"]))\n"
            + "env=d[\"services\"][\"messaging-service\"].get(\"environment\",{})\n"
            + "profile=json.loads(subprocess.check_output([\"docker\",\"exec\",\"am-service\",\"cat\",\"/app/appsettings.json\"])).get(\"Kafka\",{})\n"
            + "keys=[\"AntiphonConsumerGroup\",\"ExpectedAntiphonConsumerGroup\",\"InboundTopic\",\"ConsumerGroup\",\"BootstrapServers\"]\n"
            + "merged=\"" + (merged ? "merged" : "effective") + "\" == \"merged\"\n"
            + "print(json.dumps({k:env.get(\"Kafka__\"+k,None if merged and k in keys[:2] else profile.get(k)) for k in keys}))\n"
            + "C0410_GATEWAY_SETTINGS";

        /// <summary>The managed override preflight: one Compose base file, no other override, and the override unmarked or ours.</summary>
        public static string OverrideCommand =>
            "cd " + RemoteRoot + " && python3 - <<'C0410_OVERRIDE'\n"
            + "import json,pathlib\n"
            + "bases=[p for p in [\"compose.yaml\",\"compose.yml\",\"docker-compose.yaml\",\"docker-compose.yml\"] if pathlib.Path(p).is_file()]\n"
            + "if len(bases)!=1: raise SystemExit(43)\n"
            + "base=bases[0]; stem,ext=base.rsplit(\".\",1); override=stem+\".override.\"+ext\n"
            + "others=[p for p in [\"compose.override.yaml\",\"compose.override.yml\",\"docker-compose.override.yaml\",\"docker-compose.override.yml\"] if p!=override and pathlib.Path(p).exists()]\n"
            + "if others: raise SystemExit(43)\n"
            + "p=pathlib.Path(override); exists=p.exists()\n"
            + "marker=\"" + OverrideMarker + "\"\n"
            + "managed=not exists or p.read_text().splitlines()[0]==marker\n"
            + "print(json.dumps({\"name\":override,\"exists\":exists,\"managed\":managed}))\n"
            + "C0410_OVERRIDE";

        /// <summary>The broker metadata probe through one listener.</summary>
        public static string BrokerProbeCommand(string listener) =>
            "cd " + RemoteRoot + " && docker compose exec -T redpanda rpk cluster info -X brokers=" + listener + " --format json 2>/dev/null";

        /// <summary>The remote Compose build projection: the context (absolute) and the Dockerfile only.</summary>
        public static string ProjectionCommand =>
            "cd " + RemoteRoot + " && docker compose config --no-interpolate --format json messaging-service | python3 -c 'import json, os, sys; "
            + "d=json.load(sys.stdin); b=d[\"services\"][\"messaging-service\"][\"build\"]; c=b[\"context\"]; print(json.dumps({\"context\":os.path.normpath(c if "
            + "os.path.isabs(c) else os.path.join(\"" + RemoteRoot + "\",c)),\"dockerfile\":b[\"dockerfile\"]}))'";

        /// <summary>The safe container facts: the inspect projection, then the Compose service status as JSON.</summary>
        public static string FactsCommand =>
            "docker inspect --format '{{.Id}} {{.Image}} {{.State.Status}}' am-service && cd " + RemoteRoot + " && docker compose ps --format json messaging-service";

        /// <summary>The running image identity.</summary>
        public const string RunningCommand = "docker inspect --format '{{.Id}} {{.Image}} {{.State.Status}}' am-service";

        /// <summary>The monitor readiness read: the body, then the HTTP status on the last line.</summary>
        public const string MonitorCommand = "curl -sS --max-time 15 -w '\\n%{http_code}' http://localhost:18090/health/inbound-unconsumed";

        /// <summary>The read-only group offsets (never a consume).</summary>
        public static string GroupDescribeCommand(string group) =>
            "cd " + RemoteRoot + " && docker compose exec -T redpanda rpk group describe " + group + " 2>/dev/null";

        /// <summary>The managed override's content: the marker and the two environment keys.</summary>
        public static string OverrideText(string group) =>
            OverrideMarker + "\nservices:\n  messaging-service:\n    environment:\n      Kafka__AntiphonConsumerGroup: '" + group
            + "'\n      Kafka__ExpectedAntiphonConsumerGroup: '" + group + "'\n";

        /// <summary>The override write: a marked existing override is copied to its backup first.</summary>
        public static string PersistScript(string overridePath, string overrideBackup, string group)
        {
            var script = "set -eu\n";
            if (overrideBackup != null) script += "cp -- '" + overridePath + "' '" + overrideBackup + "'\n";
            return script + "cat > '" + overridePath + "' <<'C0410_MANAGED'\n" + OverrideText(group) + "C0410_MANAGED";
        }

        /// <summary>The replacement: extract to staging, check every source and the Dockerfile hash, keep build/src as the backup, build, recreate.</summary>
        public static string ReplaceScript(string upload, string stage, string backup, string hash, IReadOnlyList<AmServiceManifestEntry> manifest)
        {
            var checks = string.Join("\n", (manifest ?? Array.Empty<AmServiceManifestEntry>()).Select(entry => "test -e '" + stage + "/" + entry.Path + "'"));
            return "set -eu\narchive='" + upload + "'\nstaging='" + stage + "'\ncurrent='" + RemoteContext + "'\nbackup='" + backup + "'\n"
                + "cleanup() { rm -f -- \"$archive\"; }\ntrap cleanup EXIT HUP INT TERM\nrm -rf -- \"$staging\"\nmkdir -p \"$staging\"\n"
                + "tar xzf \"$archive\" -C \"$staging\"\n" + checks + "\n"
                + "actual_hash=$(sha256sum \"$staging/" + DockerfileRelative + "\" | awk '{print $1}')\ntest \"$actual_hash\" = '" + hash + "'\n"
                + "test -d \"$current\"\nmv \"$current\" \"$backup\"\nmv \"$staging\" \"$current\"\ncd '" + RemoteRoot + "'\n"
                + "docker compose build messaging-service\ndocker compose up -d --no-deps messaging-service";
        }

        /// <summary>
        /// The technical verification: the container running within pollTimeoutSec (exit 41), /api/channels within five attempts two
        /// seconds apart (exit 44), the migration history, and the redacted startup-log scan (exit 42).
        /// </summary>
        public static string VerifyScript(int pollTimeoutSec) =>
            "set -eu\ncd '" + RemoteRoot + "'\ndeadline=$(($(date +%s) + " + Number(pollTimeoutSec) + "))\n"
            + "while [ \"$(docker inspect --format '{{.State.Running}}' am-service 2>/dev/null || true)\" != true ]; do [ \"$(date +%s)\" -lt \"$deadline\" ] || exit 41; sleep 2; done\n"
            + "attempt=1\nwhile :; do\n"
            + "  if channels=$(curl --connect-timeout 2 --max-time 3 -fsS http://localhost:18090/api/channels 2>/dev/null); then\n"
            + "    printf '%s\\n' \"$channels\"\n    break\n  fi\n"
            + "  [ \"$attempt\" -lt 5 ] || exit 44\n  sleep 2\n  attempt=$((attempt + 1))\ndone\n"
            + "docker compose exec -T am-postgres sh -c 'psql -X -At -v ON_ERROR_STOP=1 -U \"$POSTGRES_USER\" -d \"$POSTGRES_DB\" -c '\\''SELECT \"MigrationId\" FROM "
            + "\"__EFMigrationsHistory\" ORDER BY \"MigrationId\";'\\'''\n"
            + "if docker logs --since 5m --tail 100 am-service 2>&1 | grep -Eiq 'Unhandled exception|fail:|crit:'; then exit 42; fi";

        /// <summary>The transient upload's removal.</summary>
        public static string CleanupCommand(string upload) => "rm -f -- '" + upload + "'";

        /// <summary>
        /// The production HTTP (Invoke-WebRequest -UseBasicParsing -TimeoutSec 15): the system proxy settings, redirects followed,
        /// a non-success status throws.
        /// </summary>
        public static AmServiceHttp Http()
        {
            var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            return (url, headers) =>
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                foreach (var header in headers ?? new Dictionary<string, string>()) request.Headers.TryAddWithoutValidation(header.Key, header.Value);
                using var response = client.SendAsync(request).GetAwaiter().GetResult();
                response.EnsureSuccessStatusCode();
                return new AmServiceHttpAnswer((int)response.StatusCode, response.Content.ReadAsStringAsync().GetAwaiter().GetResult() ?? "");
            };
        }

        // ShouldProcess with ConfirmImpact High: --what-if names the write and declines; --no-confirm approves; otherwise the console
        // question (Enter is Yes, as PowerShell's default). No answer to read is not an approval.
        private static bool ShouldProcess(AmServiceDeployOptions options, NightlyHost host, AmServiceDeploySeams seams, string target)
        {
            var operation = "Performing the operation \"" + WriteAction + "\" on target \"" + target + "\".";
            if (options.WhatIf)
            {
                host.Out.WriteLine("What if: " + operation);
                return false;
            }
            if (options.NoConfirm) return true;
            host.Out.WriteLine();
            host.Out.WriteLine("Confirm");
            host.Out.WriteLine("Are you sure you want to perform this action?");
            host.Out.WriteLine(operation);
            while (true)
            {
                host.Out.Write("[Y] Yes  [A] Yes to All  [N] No  [L] No to All  [?] Help (default is \"Y\"): ");
                host.Out.Flush();
                var answer = seams.ReadLine?.Invoke();
                if (answer == null)
                {
                    host.Out.WriteLine();
                    throw new InvalidOperationException("The deployment confirmation could not be read (no interactive input); nothing was written. "
                        + "Answer the question on a console, or pass --no-confirm only when the deployment is explicitly authorized.");
                }
                switch (answer.Trim().ToLowerInvariant())
                {
                    case "":
                    case "y":
                    case "yes":
                    case "a":
                    case "yes to all":
                        return true;
                    case "n":
                    case "no":
                    case "l":
                    case "no to all":
                        return false;
                    case "?":
                        host.Out.WriteLine("Y - Continue with only the next step of the operation.");
                        host.Out.WriteLine("A - Continue with all the steps of the operation.");
                        host.Out.WriteLine("N - Skip this operation and proceed with the next operation.");
                        host.Out.WriteLine("L - Skip this operation and all subsequent operations.");
                        break;
                }
            }
        }

        private static string Collapse(string message)
        {
            var detail = Whitespace.Replace(message ?? "", " ").Trim();
            return detail.Length == 0 ? "unknown error" : detail;
        }

        private static bool IsBuildOutput(string segment) =>
            string.Equals(segment, "bin", StringComparison.OrdinalIgnoreCase) || string.Equals(segment, "obj", StringComparison.OrdinalIgnoreCase)
            || segment.StartsWith("bin-", StringComparison.OrdinalIgnoreCase);

        private static void Remove(IFileSystem files, string path)
        {
            try
            {
                if (files.File.Exists(path)) files.File.Delete(path);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                // Remove-Item -ErrorAction SilentlyContinue.
            }
        }

        private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

        // One native command as `& tool @arguments 2>&1` ran it: resolved on PATH, this console's stdin, stdout and stderr merged in
        // arrival order, one string per line.
        private static (int ExitCode, IReadOnlyList<string> Lines) Native(NightlyHost host, AmServiceDeploySeams seams, string tool, IReadOnlyList<string> arguments,
            string workingDirectory)
        {
            var file = seams.Resolve?.Invoke(tool) ?? tool;
            var result = host.Launch(new NightlyLaunch(file, arguments, workingDirectory ?? "", CaptureOutput: true));
            return (result.ExitCode, Lines(result.LogText));
        }

        private static IReadOnlyList<string> Lines(string text)
        {
            if (string.IsNullOrEmpty(text)) return Array.Empty<string>();
            var body = text.EndsWith("\n", StringComparison.Ordinal) ? text.Substring(0, text.Length - 1) : text;
            return body.Split('\n');
        }

        // ---- ConvertFrom-Json, as the script read its answers: blank text is null; anything else is strict JSON, read by exact key ----

        private static JsonNode Json(string text, JsonDocumentOptions options = default)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            var node = JsonNode.Parse(text, documentOptions: options);
            Materialize(node);
            return node;
        }

        // A duplicate key surfaces here, inside the caller's parse, not at a later read.
        private static void Materialize(JsonNode node)
        {
            if (node is JsonObject map) foreach (var pair in map) Materialize(pair.Value);
            else if (node is JsonArray list) foreach (var item in list) Materialize(item);
        }

        private static bool IsJsonFailure(Exception e) => e is JsonException || e is ArgumentException || e is InvalidOperationException || e is FormatException;

        private static JsonNode Prop(JsonNode node, string name) => node is JsonObject map && map.TryGetPropertyValue(name, out var value) ? value : null;

        // @($value): an array's items, else the value itself (null included).
        private static IReadOnlyList<JsonNode> Items(JsonNode node) => node is JsonArray list ? list.ToList() : new List<JsonNode> { node };

        // [string]$value.
        private static string Str(JsonNode node)
        {
            switch (node)
            {
                case null:
                    return "";
                case JsonArray list:
                    return string.Join(" ", list.Select(Str));
                case JsonObject:
                    return "";
                case JsonValue value:
                    switch (value.GetValueKind())
                    {
                        case JsonValueKind.String: return value.GetValue<string>();
                        case JsonValueKind.True: return "True";
                        case JsonValueKind.False: return "False";
                        case JsonValueKind.Number: return NumberText(value);
                        default: return "";
                    }
                default:
                    return "";
            }
        }

        private static string NumberText(JsonValue value)
        {
            var raw = value.ToJsonString();
            if (long.TryParse(raw, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var whole)) return whole.ToString(CultureInfo.InvariantCulture);
            if (raw.IndexOfAny(new[] { '.', 'e', 'E' }) < 0) return raw;
            return double.Parse(raw, NumberStyles.Float, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture);
        }

        // $value -eq $true for a scalar: true, the text "True" in any case, or the number 1.
        private static bool IsTrue(JsonNode node)
        {
            if (node is not JsonValue value) return false;
            switch (value.GetValueKind())
            {
                case JsonValueKind.True: return true;
                case JsonValueKind.String: return string.Equals(value.GetValue<string>(), "True", StringComparison.OrdinalIgnoreCase);
                case JsonValueKind.Number: return double.TryParse(value.ToJsonString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && number == 1;
                default: return false;
            }
        }

        private static bool IsNumber(JsonNode node) => node is JsonValue value && value.GetValueKind() == JsonValueKind.Number;

        private static double Double(JsonNode node) => double.Parse(node.ToJsonString(), NumberStyles.Float, CultureInfo.InvariantCulture);

        // [long]$number: a fraction rounds to even; out of range is null.
        private static long? Long(JsonNode node)
        {
            if (!IsNumber(node)) return null;
            if (long.TryParse(node.ToJsonString(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var whole)) return whole;
            var value = Double(node);
            if (double.IsNaN(value) || value < long.MinValue || value >= long.MaxValue) return null;
            return Convert.ToInt64(value);
        }

        // [int]$port: a number (a fraction rounds to even) or an integer text; anything else is null.
        private static int? Port(JsonNode node)
        {
            if (node is not JsonValue value) return null;
            if (value.GetValueKind() == JsonValueKind.String)
                return int.TryParse(value.GetValue<string>(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
            if (value.GetValueKind() != JsonValueKind.Number) return null;
            var number = Double(value);
            if (double.IsNaN(number) || number < int.MinValue || number > int.MaxValue) return null;
            return Convert.ToInt32(number);
        }

        // ---- one pass of Invoke-AmServiceDeploymentCore ----

        private sealed record Identity(string ConsumerGroup, string InboundTopic, string Brokers);

        private sealed record Gateway(string Watched, string Expected, string InboundTopic, string ConsumerGroup, string BootstrapServers);

        private sealed record Override(string Path, bool Exists);

        private sealed class Pass
        {
            private readonly NightlyHost _host;
            private readonly IFileSystem _files;
            private readonly AmServiceDeploySeams _seams;
            private readonly string _target;

            public Pass(NightlyHost host, IFileSystem files, AmServiceDeploySeams seams, string target)
            {
                _host = host;
                _files = files;
                _seams = seams ?? throw new ArgumentNullException(nameof(seams));
                _target = target;
            }

            private void Say(string line) => _host.Out.WriteLine(line);

            public AmServiceDeployResult Run(string repositoryRoot, bool performDeploy, bool skipTrafficCheck, int pollTimeoutSec)
            {
                AssertSshTarget(_target);
                Say("Remote target: " + _target + ":" + RemoteRoot);
                var context = _files.Path.Combine(repositoryRoot, "src");
                var dockerfile = _files.Path.Combine(context, "Antiphon.Messaging.Service", "Dockerfile");
                var migrationDirectory = _files.Path.Combine(context, "Antiphon.Messaging.Service", "Migrations");
                var identity = ServerIdentity();
                var settings = Json(_files.File.ReadAllText(_files.Path.Combine(repositoryRoot, "server", "appsettings.json")), SettingsJson);
                var repositoryDefault = Str(Prop(Prop(settings, "AntiphonMessaging"), "ConsumerGroup"));
                var gateway = GatewaySettings(false);
                if (!string.Equals(gateway.InboundTopic, identity.InboundTopic, StringComparison.Ordinal))
                    throw new InvalidOperationException("Inbound topic mismatch between server and gateway.");
                if (string.Equals(gateway.ConsumerGroup, identity.ConsumerGroup, StringComparison.Ordinal))
                    throw new InvalidOperationException("Server inbound group equals gateway outbound group.");
                var managed = ManagedOverride();
                BrokerIdentity(identity.Brokers, gateway.BootstrapServers);
                Say("repository default: " + repositoryDefault);
                Say("server: " + identity.ConsumerGroup);
                if (!string.Equals(identity.ConsumerGroup, repositoryDefault, StringComparison.Ordinal))
                    Say("server override (differs from repository default " + repositoryDefault + ")");
                Say("gateway watched: " + gateway.Watched + "; expected: " + gateway.Expected);
                Say("proposed: Kafka__AntiphonConsumerGroup=" + identity.ConsumerGroup + " Kafka__ExpectedAntiphonConsumerGroup=" + identity.ConsumerGroup);
                Say("outbound group unchanged: " + gateway.ConsumerGroup);
                VersionProvenance();
                foreach (var command in new[] { "ssh", "scp", "tar" })
                    if (_seams.Resolve?.Invoke(command) == null) throw new InvalidOperationException("Required local command not found: " + command);
                var manifest = Manifest(_files, dockerfile, context);
                var hash = Convert.ToHexString(SHA256.HashData(_files.File.ReadAllBytes(dockerfile))).ToLowerInvariant();
                Say("Dockerfile SHA-256: " + hash);
                Say("Dockerfile-derived source entries (" + Number(manifest.Count) + "): " + string.Join(", ", manifest.Select(entry => entry.Path)));
                var projectionText = string.Join("\n", Ssh(ProjectionCommand, "remote Compose build projection"));
                JsonNode projection;
                try { projection = Json(projectionText); }
                catch (Exception e) when (IsJsonFailure(e)) { throw new InvalidOperationException("Remote Compose build projection was not parseable JSON."); }
                var remoteContext = Str(Prop(projection, "context"));
                var remoteDockerfile = Str(Prop(projection, "dockerfile"));
                if (!string.Equals(remoteContext, RemoteContext, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(remoteDockerfile, DockerfileRelative, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Remote Compose build contract changed.");
                Say("Remote Compose build projection: context=" + remoteContext + "; dockerfile=" + remoteDockerfile);
                var facts = Ssh(FactsCommand, "remote safe container facts");
                if (facts.Count == 0) throw new InvalidOperationException("Container identity unavailable.");
                var previous = ContainerFact(facts[0].Trim());
                var composeStatus = facts.Count > 1 ? ComposeStatus(facts[1]) : "unavailable";
                Say("Previous am-service container/image/status: " + previous);
                Say("Previous Compose service status: " + composeStatus);
                var result = new AmServiceDeployResult
                {
                    SshTarget = _target,
                    DockerfileHash = hash,
                    Manifest = manifest,
                    RemoteContext = remoteContext,
                    RemoteDockerfile = remoteDockerfile,
                    PreviousContainer = previous,
                    ComposeStatus = composeStatus,
                    OverridePath = managed.Path,
                };
                if (!performDeploy) return result;
                if (ServerIdentity() != identity) throw new InvalidOperationException("Server identity change between preview and write refuses deployment.");
                return Write(result, identity, gateway, managed, manifest, hash, context, migrationDirectory, skipTrafficCheck, pollTimeoutSec);
            }

            private AmServiceDeployResult Write(AmServiceDeployResult result, Identity identity, Gateway gateway, Override managed,
                IReadOnlyList<AmServiceManifestEntry> manifest, string hash, string context, string migrationDirectory, bool skipTrafficCheck, int pollTimeoutSec)
            {
                AmServiceArchive archive = null;
                var upload = UploadPrefix + _seams.Token() + ".tgz";
                var stage = RemoteRoot + "/build/.src-stage-" + _seams.Token();
                var backup = RemoteContext + ".bak-" + Stamp() + "-" + _seams.Token().Substring(0, 8);
                result.BackupPath = backup;
                var uploaded = false;
                try
                {
                    archive = CreateArchive(_host, _files, _seams, context, manifest);
                    Say("Validated deployment archive: " + Number(archive.Entries.Count) + " paths; bin/obj/bin-* excluded.");
                    var overrideBackup = managed.Path + ".bak-" + Stamp() + "-" + _seams.Token().Substring(0, 8);
                    if (managed.Exists) result.OverrideBackupPath = overrideBackup;
                    Runner("ssh", new[] { _target, PersistScript(managed.Path, managed.Exists ? overrideBackup : null, identity.ConsumerGroup) }, "persist managed override");
                    var merged = GatewaySettings(true);
                    if (!string.Equals(merged.Watched, identity.ConsumerGroup, StringComparison.Ordinal) || !string.Equals(merged.Expected, identity.ConsumerGroup, StringComparison.Ordinal))
                        throw new InvalidOperationException("Merged docker compose config lacks matching watched/expected groups.");
                    if (!string.Equals(merged.ConsumerGroup, gateway.ConsumerGroup, StringComparison.Ordinal) || !string.Equals(merged.InboundTopic, gateway.InboundTopic, StringComparison.Ordinal)
                        || !string.Equals(merged.BootstrapServers, gateway.BootstrapServers, StringComparison.Ordinal))
                        throw new InvalidOperationException("Merged docker compose config changed unrelated gateway settings.");
                    Say("Managed override: " + managed.Path + "; retained override backup: " + result.OverrideBackupPath);
                    Runner("scp", new[] { archive.Path, _target + ":" + upload }, "archive upload");
                    uploaded = true;
                    try
                    {
                        Ssh(ReplaceScript(upload, stage, backup, hash, manifest), "remote archive replacement/build/recreate");
                    }
                    catch (Exception e)
                    {
                        throw new InvalidOperationException("Remote replacement/build/recreate failed; retained source backup: " + backup + "; staging path: " + stage + ". " + e.Message);
                    }
                    Say("Retained source backup: " + backup);
                    IReadOnlyList<string> remoteVerify;
                    try
                    {
                        remoteVerify = Ssh(VerifyScript(pollTimeoutSec), "remote technical verification");
                    }
                    catch (InvalidOperationException e) when (e.Message == "remote technical verification failed (exit 44).")
                    {
                        throw new InvalidOperationException("HTTP readiness was not confirmed at /api/channels after five attempts (exit 44).");
                    }
                    if (remoteVerify.Count < 1) throw new InvalidOperationException("Remote technical verification returned no channel JSON.");
                    IReadOnlyList<JsonNode> channels;
                    try { channels = Items(Json(remoteVerify[0])); }
                    catch (Exception e) when (IsJsonFailure(e)) { throw new InvalidOperationException("The am-service /api/channels response was not parseable channel JSON."); }
                    var names = channels.Select(AdapterName).ToList();
                    var remoteMigrations = remoteVerify.Skip(1).Where(line => MigrationRow.IsMatch(line)).ToList();
                    var sourceMigrations = MigrationIds(_files, migrationDirectory);
                    var missing = sourceMigrations.Where(id => !remoteMigrations.Contains(id, StringComparer.OrdinalIgnoreCase)).ToList();
                    if (missing.Count > 0) throw new InvalidOperationException("Remote am-postgres migration history is missing: " + string.Join(", ", missing));
                    result.MigrationCount = remoteMigrations.Count;
                    result.AdapterNames = names;
                    Monitor(identity);
                    var running = Ssh(RunningCommand, "running image identity").ToList();
                    if (running.Count > 0) running[0] = ContainerFact(running[0].Trim());
                    if (running.Count == 0 || string.Equals(Field(running[0], 1), Field(result.PreviousContainer, 1), StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("Running image ID did not change from PreviousContainer.");
                    result.RunningContainer = running[0];
                    Say("Running am-service container/image/status: " + running[0]);
                    if (ServerIdentity() != identity) throw new InvalidOperationException("Server identity changed during deployment verification.");
                    Say("Endpoint verified: " + Number(names.Count) + " registered adapter(s): " + string.Join(", ", names));
                    Say("Migration comparison verified: " + Number(sourceMigrations.Count) + " source, " + Number(remoteMigrations.Count) + " recorded.");
                    Say("Startup-log scan verified: last 100 lines, no startup exception (redacted scan).");
                    Say(skipTrafficCheck ? SkippedTrafficLine : RequiredTrafficLine);
                    return result;
                }
                finally
                {
                    if (archive != null) Remove(_files, archive.Path);
                    if (uploaded)
                    {
                        try { Ssh(CleanupCommand(upload), "remote transient archive cleanup"); }
                        catch (Exception) { Say("WARNING: remote transient archive cleanup could not be confirmed."); }
                    }
                }
            }

            private string Stamp() => _host.UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);

            // ($text -split ' ')[index]; null past the end.
            private static string Field(string text, int index)
            {
                var fields = (text ?? "").Split(' ');
                return index < fields.Length ? fields[index] : null;
            }

            private static string AdapterName(JsonNode channel)
            {
                var name = Prop(channel, "channel");
                if (name != null) return Str(name);
                name = Prop(channel, "name");
                return name != null ? Str(name) : "unnamed";
            }

            private static string ComposeStatus(string line)
            {
                JsonNode compose;
                try { compose = Json(line); }
                catch (Exception e) when (IsJsonFailure(e)) { return "unparseable"; }
                // ConvertFrom-Json enumerates an array: one item is that item, none is nothing.
                if (compose is JsonArray list) compose = list.Count == 1 ? list[0] : null;
                var state = Prop(compose, "State");
                return state != null && ComposeState.IsMatch(Str(state)) ? Str(state) : "unavailable";
            }

            private IReadOnlyList<string> Ssh(string command, string operation) => Runner("ssh", new[] { _target, command }, operation);

            // Invoke-AmServiceRunner: a non-zero exit is "<operation> failed (exit <n>)."; never the output.
            private IReadOnlyList<string> Runner(string tool, IReadOnlyList<string> arguments, string operation)
            {
                var result = Native(_host, _seams, tool, arguments, "");
                if (result.ExitCode != 0) throw new InvalidOperationException(operation + " failed (exit " + Number(result.ExitCode) + ").");
                return result.Lines;
            }

            private AmServiceHttpAnswer Get(string path)
            {
                var api = _host.Variables("ANTIPHON_API");
                if (string.IsNullOrEmpty(api)) api = "http://localhost:17202";
                var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                var token = _host.Variables("ANTIPHON_TASK_TOKEN");
                if (!string.IsNullOrEmpty(token)) headers["X-Antiphon-Task-Token"] = token;
                return _seams.Http(api.TrimEnd('/') + path, headers);
            }

            // Get-AmServiceServerIdentity: the bridge's inbound group, topic and broker listeners, each validated.
            private Identity ServerIdentity()
            {
                AmServiceHttpAnswer response;
                try { response = Get("/api/channels/consumer"); }
                catch (Exception) { throw new InvalidOperationException("Server identity endpoint unavailable."); }
                if (response == null || response.StatusCode != 200) throw new InvalidOperationException("Server identity endpoint unavailable (requires HTTP 200).");
                JsonNode body;
                try { body = Json(response.Body); }
                catch (Exception e) when (IsJsonFailure(e)) { throw new InvalidOperationException("Server identity endpoint returned invalid JSON."); }
                if (!IsTrue(Prop(body, "enabled"))) throw new InvalidOperationException("Server identity reports disabled bridge.");
                foreach (var key in new[] { "consumerGroup", "inboundTopic" })
                    if (!Name.IsMatch(Str(Prop(body, key)))) throw new InvalidOperationException("Server identity has invalid " + key + ".");
                var brokers = new List<string>();
                foreach (var broker in Items(Prop(body, "brokers")))
                {
                    var host = Str(Prop(broker, "host"));
                    var port = Port(Prop(broker, "port"));
                    if (!BrokerHost.IsMatch(host) || port == null || port < 1 || port > 65535) throw new InvalidOperationException("Server identity has invalid broker host/port.");
                    brokers.Add(host + ":" + Number(port.Value));
                }
                if (brokers.Count == 0) throw new InvalidOperationException("Server identity has no broker addresses.");
                return new Identity(Str(Prop(body, "consumerGroup")), Str(Prop(body, "inboundTopic")), string.Join(",", brokers));
            }

            // Get-AmServiceGatewaySettings: validated before output or shell use; no other returned field is carried forward.
            private Gateway GatewaySettings(bool merged)
            {
                JsonNode body;
                try { body = ParseJsonOutput(string.Join("\n", Ssh(GatewaySettingsCommand(merged), "gateway settings projection"))); }
                catch (Exception) { throw new InvalidOperationException("Gateway settings projection failed."); }
                foreach (var key in new[] { "AntiphonConsumerGroup", "InboundTopic", "ConsumerGroup" })
                    if (!Name.IsMatch(Str(Prop(body, key)))) throw new InvalidOperationException("Gateway settings missing or invalid " + key + ".");
                var expected = Prop(body, "ExpectedAntiphonConsumerGroup");
                if (expected != null && !Name.IsMatch(Str(expected))) throw new InvalidOperationException("Gateway expected group is invalid.");
                var bootstrap = Str(Prop(body, "BootstrapServers"));
                if (!Bootstrap.IsMatch(bootstrap)) throw new InvalidOperationException("Gateway broker listener is invalid.");
                return new Gateway(Str(Prop(body, "AntiphonConsumerGroup")), expected == null ? null : Str(expected), Str(Prop(body, "InboundTopic")),
                    Str(Prop(body, "ConsumerGroup")), bootstrap);
            }

            // Get-AmServiceOverride: the ordinary override name of the one Compose base file, unmarked only when absent.
            private Override ManagedOverride()
            {
                JsonNode body;
                try { body = ParseJsonOutput(string.Join("\n", Ssh(OverrideCommand, "managed override preflight"))); }
                catch (Exception) { throw new InvalidOperationException("Compose base/override ambiguity or unreadable override refuses deployment."); }
                var name = Str(Prop(body, "name"));
                if (!OverrideName.IsMatch(name)) throw new InvalidOperationException("Invalid managed override filename.");
                if (!IsTrue(Prop(body, "managed"))) throw new InvalidOperationException("Unmarked existing " + name + " refuses before any write.");
                return new Override(RemoteRoot + "/" + name, IsTrue(Prop(body, "exists")));
            }

            public void BrokerIdentity(string serverBrokers, string gatewayBrokers)
            {
                var ids = new List<string>();
                foreach (var listener in new[] { serverBrokers, gatewayBrokers })
                {
                    var raw = string.Join("\n", Ssh(BrokerProbeCommand(listener), "broker metadata probe"));
                    JsonNode body;
                    try { body = ParseJsonOutput(raw); }
                    catch (Exception) { throw new InvalidOperationException("Broker metadata probe failed."); }
                    // Redpanda's live cluster_name is redpanda.<uuid>; the comparison stays exact.
                    var name = Str(Prop(body, "cluster_name"));
                    if (name.Length < 1 || name.Length > 249 || !Cluster.IsMatch(name)) throw new InvalidOperationException("Broker metadata has no cluster identity.");
                    ids.Add(name);
                }
                if (!string.Equals(ids[0], ids[1], StringComparison.Ordinal)) throw new InvalidOperationException("Server/gateway broker cluster identity mismatch.");
                Say("Broker listener metadata identifies the same cluster.");
            }

            private void VersionProvenance()
            {
                try
                {
                    var version = Get("/api/version");
                    var sha = Str(Prop(Json(version.Body), "version"));
                    if (version.StatusCode != 200 || !Sha.IsMatch(sha)) throw new InvalidOperationException("invalid");
                    Say("Server version provenance: " + sha);
                }
                catch (Exception)
                {
                    throw new InvalidOperationException("Server version provenance unavailable.");
                }
            }

            // Assert-AmServiceMonitor: the readiness route Ready for the server's group and topic, fresh, with committed offsets that
            // the broker's sampled current offsets are not behind.
            private void Monitor(Identity identity)
            {
                var lines = Ssh(MonitorCommand, "monitor readiness read");
                if (lines.Count < 2 || !string.Equals(lines[lines.Count - 1], "200", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Monitor readiness requires HTTP 200 (missing/degraded route).");
                JsonNode body;
                try { body = Json(string.Join("\n", lines.Take(lines.Count - 1))); }
                catch (Exception e) when (IsJsonFailure(e)) { throw new InvalidOperationException("Monitor readiness is not parseable."); }
                if (!string.Equals(Str(Prop(body, "state")), "Ready", StringComparison.Ordinal)) throw new InvalidOperationException("Monitor readiness state is not Ready.");
                if (!string.Equals(Str(Prop(body, "watchedGroup")), identity.ConsumerGroup, StringComparison.Ordinal)
                    || !string.Equals(Str(Prop(body, "expectedGroup")), identity.ConsumerGroup, StringComparison.Ordinal))
                    throw new InvalidOperationException("Monitor readiness group mismatch.");
                if (!string.Equals(Str(Prop(body, "topic")), identity.InboundTopic, StringComparison.Ordinal)) throw new InvalidOperationException("Monitor readiness topic mismatch.");
                var age = Prop(body, "ageSeconds");
                if (!IsNumber(age) || Double(age) < 0 || Double(age) > 130) throw new InvalidOperationException("Monitor readiness observation is stale or missing ageSeconds.");
                var partitions = Items(Prop(body, "partitions"));
                if (partitions.Count == 0 || partitions.Any(partition => !string.Equals(Str(Prop(partition, "status")), "CommittedOffset", StringComparison.Ordinal)
                    || !(Long(Prop(partition, "committedNextOffset")) >= 0)))
                    throw new InvalidOperationException("Monitor readiness contains unknown offsets.");
                var raw = string.Join("\n", Ssh(GroupDescribeCommand(identity.ConsumerGroup), "read-only group offsets"));
                // rpk's table: TOPIC PARTITION CURRENT-OFFSET LOG-START-OFFSET LOG-END-OFFSET LAG ...
                foreach (var partition in partitions)
                {
                    var pattern = new Regex(@"(?m)^\s*" + Regex.Escape(identity.InboundTopic) + @"\s+" + Regex.Escape(Str(Prop(partition, "partition"))) + @"\s+([0-9]+)\s+", Ci);
                    var match = pattern.Match(raw);
                    if (!match.Success) throw new InvalidOperationException("Broker group describe lacks numeric current offsets for a monitored partition.");
                    if (long.Parse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture) < Long(Prop(partition, "committedNextOffset")))
                        throw new InvalidOperationException("Broker sampled offset is behind monitor evidence.");
                }
                Say("Monitor build marker verified: readiness route Ready; sampled offsets numeric; ageSeconds=" + Str(age) + ".");
            }
        }
    }
}
