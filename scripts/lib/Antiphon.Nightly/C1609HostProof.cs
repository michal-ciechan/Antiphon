// The desktop's origin proof for a server2 temp volume retirement (unit 66 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md,
// W10 part 1: the live server2 bridge). It replaced scripts/c1609-host-proof.ps1 (CARD-1609, the versioned origin-only proof writer,
// with its CARD-1693 snapshot retry and MAX_PATH custody, the CARD-1986 dependency selection and withdrawn-ref record and the
// CARD-1989 configurable control window) and the parts of scripts/c1008-host-proof.ps1 (CARD-1607) the bridge used: the SHA-256 text
// digest, the origin scrub, the host-ls-remote diagnosis, the private origin line reader and the v2 dispatch of the writer. The v1
// writer of c1008-host-proof.ps1 (Write-C1008HostBundle, Invoke-C1008Git and the "c1008-host-proof 1" body) had no caller (the bridge
// always wrote v2) and was not ported.
//
// The retirement coordinator (C1609Coordinator) calls WriteOriginProof for each private origin a proof request names, CopyOriginProof
// for an origin that shares a graph already proven, and AssertLiveOriginProof for each origin a control request names. Every child
// (git, and the coordinator's ssh and scp) goes through IC1609Processes; production binds C1609BoundedProcesses (Invoke-C1609BoundedProcess:
// a deadline, a 2 s drain after exit, 8 MiB per stream or 1 GiB to an output file, and a start directory under MAX_PATH). Files are
// IFileSystem's, the wall clock the caller's (the proof's created and completed seconds), elapsed time a Stopwatch.
//
// A refusal is a C1609Refusal whose message is the script's fixed token or diagnosis: the writer and the live check throw
// "RecycleGitAuditUnknown audit check=host-ls-remote status=2 repo=<repo>" (or the bare "RecycleGitAuditUnknown" for an unsafe repo)
// or the withdrawn-ref record "RecycleOriginMoved reason=withdrawn-ref repo=... ref=... old=... new=...", and leave the fixed reason
// token in HostProofReason or LiveProofReason. No URL, credential or Git stderr crosses this boundary. Plain C#: no PowerShell types.
// ASCII only.
#nullable disable
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Antiphon.Nightly
{
    /// <summary>A refusal the PowerShell threw as a string: its message is the token or diagnosis.</summary>
    public sealed class C1609Refusal : Exception
    {
        /// <summary>The token or diagnosis.</summary>
        public C1609Refusal(string message, string site = C1609HostProof.Site) : base(message)
        {
            Site = site;
        }

        /// <summary>The source that refused (a receipt's failure-script): C1609HostProof.Site or C1609Coordinator.Site.</summary>
        public string Site { get; }
    }

    /// <summary>One bounded child (Invoke-C1609BoundedProcess's parameters).</summary>
    public sealed class C1609ProcessCall
    {
        /// <summary>The executable (git, ssh or scp), found on PATH.</summary>
        public string FileName { get; init; } = "";

        /// <summary>Its arguments, one each.</summary>
        public IReadOnlyList<string> Arguments { get; init; } = Array.Empty<string>();

        /// <summary>The deadline in milliseconds (default 30 s).</summary>
        public int TimeoutMs { get; init; } = 30000;

        /// <summary>The requested working directory (empty: the current one); see C1609BoundedProcesses.StartDirectory.</summary>
        public string WorkingDirectory { get; init; } = "";

        /// <summary>Text written to its input, which is then closed.</summary>
        public string InputText { get; init; } = "";

        /// <summary>A new file its output is written to (CreateNew), instead of being kept.</summary>
        public string OutputFile { get; init; } = "";

        /// <summary>Keep the output (when no output file is named).</summary>
        public bool CaptureOutput { get; init; }

        /// <summary>Git's environment: the repository-redirecting variables removed, prompts and lazy fetch off.</summary>
        public bool GitEnvironment { get; init; }
    }

    /// <summary>A bounded child's exit code and its kept output.</summary>
    public sealed class C1609ProcessResult
    {
        /// <summary>The result.</summary>
        public C1609ProcessResult(int exitCode, string stdout)
        {
            ExitCode = exitCode;
            Stdout = stdout ?? "";
        }

        /// <summary>The exit code.</summary>
        public int ExitCode { get; }

        /// <summary>The kept output (empty unless captured).</summary>
        public string Stdout { get; }
    }

    /// <summary>The children of the proof and its coordinator (production: C1609BoundedProcesses; tests: fakes).</summary>
    public interface IC1609Processes
    {
        /// <summary>Runs one child to its end. Throws C1609Refusal deadline, drain or output-bound, or the start failure.</summary>
        C1609ProcessResult Run(C1609ProcessCall call);
    }

    /// <summary>Invoke-C1609BoundedProcess: one child within its deadline, its output read in bounded chunks.</summary>
    public sealed class C1609BoundedProcesses : IC1609Processes
    {
        private static readonly string[] GitRedirects =
        {
            "GIT_DIR", "GIT_WORK_TREE", "GIT_INDEX_FILE", "GIT_OBJECT_DIRECTORY", "GIT_ALTERNATE_OBJECT_DIRECTORIES", "GIT_CONFIG_PARAMETERS", "GIT_CONFIG_COUNT",
        };

        private readonly IFileSystem _files;
        private readonly Func<string> _currentDirectory;
        private readonly IReadOnlyDictionary<string, string> _environment;

        /// <summary>
        /// The production runner. currentDirectory is the process's (Directory.GetCurrentDirectory) unless given; an environment,
        /// when given, is laid over each child's (a null value removes the variable) and its PATH finds the executable.
        /// </summary>
        public C1609BoundedProcesses(IFileSystem files, Func<string> currentDirectory = null, IReadOnlyDictionary<string, string> environment = null)
        {
            _files = files ?? throw new ArgumentNullException(nameof(files));
            _currentDirectory = currentDirectory ?? Directory.GetCurrentDirectory;
            _environment = environment;
        }

        /// <summary>
        /// Get-C1609StartDirectory: Windows cannot start a process whose working directory is 260 characters or more, so a requested
        /// directory (or, with none, the current one) that long becomes the temp path; a shorter requested one stays; else empty.
        /// </summary>
        public static string StartDirectory(string requested, string current, string tempPath)
        {
            var effective = !string.IsNullOrEmpty(requested) ? requested : current ?? "";
            if (effective.Length >= 260) return (tempPath ?? "").TrimEnd('\\', '/');
            return !string.IsNullOrEmpty(requested) ? requested : "";
        }

        /// <summary>This runner's start directory for a requested one.</summary>
        public string StartDirectoryFor(string requested) => StartDirectory(requested, _currentDirectory(), _files.Path.GetTempPath());

        private string Resolve(string fileName)
        {
            if (_environment == null || !_environment.TryGetValue("PATH", out var path) || path == null || fileName.IndexOfAny(new[] { '/', '\\' }) >= 0)
                return fileName;
            var extensions = OperatingSystem.IsWindows() ? new[] { ".exe", "" } : new[] { "" };
            foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
                foreach (var extension in extensions)
                {
                    var candidate = Path.Combine(directory, fileName + extension);
                    if (File.Exists(candidate)) return candidate;
                }
            return fileName;
        }

        /// <summary>Starts a child with the given streams redirected, in the start directory, with the overlay environment.</summary>
        public ProcessStartInfo StartInfo(string fileName, IEnumerable<string> arguments, string workingDirectory, bool input)
        {
            var start = new ProcessStartInfo(Resolve(fileName))
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = input,
            };
            var directory = StartDirectoryFor(workingDirectory);
            if (directory.Length > 0) start.WorkingDirectory = directory;
            foreach (var argument in arguments ?? Array.Empty<string>()) start.ArgumentList.Add(argument ?? "");
            if (_environment != null)
                foreach (var pair in _environment)
                {
                    if (pair.Value == null) start.Environment.Remove(pair.Key);
                    else start.Environment[pair.Key] = pair.Value;
                }
            return start;
        }

        /// <inheritdoc />
        public C1609ProcessResult Run(C1609ProcessCall call)
        {
            if (call == null) throw new ArgumentNullException(nameof(call));
            var inputText = call.InputText ?? "";
            if (call.TimeoutMs <= 0 || inputText.Length > 16777216) throw new C1609Refusal("deadline");
            var start = StartInfo(call.FileName, call.Arguments, call.WorkingDirectory, input: true);
            if (call.GitEnvironment)
            {
                foreach (var key in GitRedirects) start.Environment.Remove(key);
                start.Environment["GIT_TERMINAL_PROMPT"] = "0";
                start.Environment["GIT_NO_LAZY_FETCH"] = "1";
                start.Environment["GIT_NO_REPLACE_OBJECTS"] = "1";
            }
            var process = new Process { StartInfo = start };
            var started = false;
            var watch = Stopwatch.StartNew();
            var output = new StringBuilder();
            StreamWriter writer = null;
            long? exitedAt = null;
            try
            {
                if (!string.IsNullOrEmpty(call.OutputFile))
                    writer = new StreamWriter(_files.File.Open(call.OutputFile, FileMode.CreateNew, FileAccess.Write, FileShare.None), new UTF8Encoding(false));
                process.Start();
                started = true;
                var stdin = process.StandardInput.WriteAsync(inputText);
                var inputClosed = false;
                var streams = new[]
                {
                    new BoundedStream(process.StandardOutput, keep: true),
                    new BoundedStream(process.StandardError, keep: false),
                };
                foreach (var s in streams) s.Next();
                while (!inputClosed || streams.Any(s => !s.Done))
                {
                    if (watch.ElapsedMilliseconds >= call.TimeoutMs) throw new C1609Refusal("deadline");
                    if (!inputClosed && stdin.IsCompleted)
                    {
                        stdin.GetAwaiter().GetResult();
                        process.StandardInput.Close();
                        inputClosed = true;
                    }
                    if (process.HasExited)
                    {
                        exitedAt ??= watch.ElapsedMilliseconds;
                        if (watch.ElapsedMilliseconds - exitedAt.Value > 2000) throw new C1609Refusal("drain");
                    }
                    foreach (var s in streams)
                    {
                        if (s.Done || !s.Task.IsCompleted) continue;
                        var n = s.Task.GetAwaiter().GetResult();
                        if (n == 0) { s.Done = true; continue; }
                        s.Count += n;
                        var cap = s.Keep && writer != null ? 1073741824L : 8388608L;
                        if (s.Count > cap) throw new C1609Refusal("output-bound");
                        if (s.Keep && writer != null) writer.Write(s.Buffer, 0, n);
                        else if (s.Keep && call.CaptureOutput) output.Append(s.Buffer, 0, n);
                        s.Next();
                    }
                    Thread.Sleep(5);
                }
                var remaining = Math.Max(1, call.TimeoutMs - (int)watch.ElapsedMilliseconds);
                if (!process.WaitForExit(remaining)) throw new C1609Refusal("deadline");
                return new C1609ProcessResult(process.ExitCode, output.ToString());
            }
            finally
            {
                if (started && !process.HasExited) { process.Kill(true); process.WaitForExit(2000); }
                writer?.Dispose();
                process.Dispose();
            }
        }

        /// <summary>One redirected stream read in 4096-character chunks (the coordinator reads its ssh the same way).</summary>
        public sealed class BoundedStream
        {
            /// <summary>A stream; keep says whether its text is kept.</summary>
            public BoundedStream(TextReader reader, bool keep)
            {
                Reader = reader;
                Keep = keep;
            }

            /// <summary>The reader.</summary>
            public TextReader Reader { get; }

            /// <summary>Whether its text is kept (standard output).</summary>
            public bool Keep { get; }

            /// <summary>The chunk buffer.</summary>
            public char[] Buffer { get; } = new char[4096];

            /// <summary>The pending read.</summary>
            public Task<int> Task { get; private set; }

            /// <summary>End of stream seen.</summary>
            public bool Done { get; set; }

            /// <summary>Characters read so far.</summary>
            public long Count { get; set; }

            /// <summary>Starts the next read.</summary>
            public void Next() => Task = Reader.ReadAsync(Buffer, 0, Buffer.Length);
        }
    }

    /// <summary>One private origin line of a retirement inventory (Read-C1008PrivateOriginLines).</summary>
    public sealed class C1008OriginRow
    {
        /// <summary>The repository, relative to the work volume.</summary>
        public string Repo { get; init; } = "";

        /// <summary>The scrubbed origin URL.</summary>
        public string Origin { get; init; } = "";

        /// <summary>SHA-256 of the origin.</summary>
        public string OriginHash { get; init; } = "";

        /// <summary>SHA-256 of repo, LF, origin: the proof file's name.</summary>
        public string ProofName { get; init; } = "";
    }

    /// <summary>An object a renewal must prove published (objectId and type).</summary>
    public sealed class C1609RequiredObject
    {
        /// <summary>An object.</summary>
        public C1609RequiredObject(string objectId, string type)
        {
            ObjectId = objectId;
            Type = type;
        }

        /// <summary>The 40-hex object id.</summary>
        public string ObjectId { get; }

        /// <summary>commit, tag, tree or blob.</summary>
        public string Type { get; }
    }

    /// <summary>Write-C1609HostOriginProof's parameters.</summary>
    public sealed class C1609ProofRequest
    {
        /// <summary>The approved origin URL (an approved, scrubbed https, ssh or git@ origin).</summary>
        public string OriginUrl { get; init; } = "";

        /// <summary>The repository relative to the work volume.</summary>
        public string RepoRelative { get; init; } = "";

        /// <summary>The c1008 operation id.</summary>
        public string OperationId { get; init; } = "";

        /// <summary>The directory the proof and its bundle are written to.</summary>
        public string ProofDirectory { get; init; } = "";

        /// <summary>The phase's start in Unix seconds.</summary>
        public long CreatedUnix { get; init; }

        /// <summary>host-proof.log: one line per proof or failed attempt.</summary>
        public string LogPath { get; init; } = "";

        /// <summary>The phase nonce (32 hex).</summary>
        public string PhaseNonce { get; init; } = "";

        /// <summary>The inventory digest (64 hex).</summary>
        public string InventoryDigest { get; init; } = "";

        /// <summary>The approved origins.</summary>
        public IReadOnlyList<string> ApprovedOrigins { get; init; } = Array.Empty<string>();

        /// <summary>The network budget in milliseconds (default 600 s).</summary>
        public int NetworkBudgetMs { get; init; } = 600000;

        /// <summary>The objects a renewal must prove.</summary>
        public IReadOnlyList<C1609RequiredObject> RequiredObjects { get; init; } = Array.Empty<C1609RequiredObject>();

        /// <summary>The previous v2 proof file a renewal starts from (empty: none).</summary>
        public string PreviousProofFile { get; init; } = "";

        /// <summary>Fetch the graph again even when the advertisement is unchanged (a renewal).</summary>
        public bool FreshGraph { get; init; }
    }

    /// <summary>One live check's timing record (the coordinator's coordinator-controls.jsonl origin row).</summary>
    public sealed class C1609Timing
    {
        /// <summary>Each step: step, elapsedMs, outcome.</summary>
        public List<(string Step, long ElapsedMs, string Outcome)> Steps { get; } = new List<(string, long, string)>();

        /// <summary>refused or passed.</summary>
        public string Outcome { get; set; } = "refused";

        /// <summary>The refusal's reason token.</summary>
        public string Reason { get; set; } = "";

        /// <summary>The step running (complete when passed).</summary>
        public string Step { get; set; } = "origin";

        /// <summary>The budget.</summary>
        public int LimitMs { get; set; }

        /// <summary>Charged time.</summary>
        public long ElapsedMs { get; set; }

        /// <summary>Scratch cleanup time (not charged).</summary>
        public long CleanupMs { get; set; }
    }

    /// <summary>The desktop origin proof (scripts/c1609-host-proof.ps1 and the bridge's parts of scripts/c1008-host-proof.ps1).</summary>
    public sealed class C1609HostProof
    {
        /// <summary>A refusal's site (a receipt's failure-script) for this source.</summary>
        public const string Site = "C1609HostProof.cs";

        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);
        private static readonly string[] Needles = { "token", "password", "secret", "ghp_", "gho_", "ghu_", "ghs_", "ghr_", "github_pat_" };
        private static readonly string[] GitPolicy =
        {
            "-c", "core.hooksPath=/dev/null", "-c", "protocol.allow=never", "-c", "protocol.https.allow=always",
            "-c", "protocol.ssh.allow=always", "-c", "http.followRedirects=false", "-c", "credential.interactive=false",
        };

        private static readonly Regex Token = new Regex("^[a-z][a-z-]{0,31}$");

        private readonly IC1609Processes _processes;
        private readonly IFileSystem _files;
        private readonly Func<DateTimeOffset> _utcNow;
        private readonly Func<Guid> _newGuid;
        private readonly Func<string, string> _variables;

        /// <summary>The proof over its children, files, wall clock, ids and variables (ANTIPHON_RETIRE_CONTROL_WINDOW_MS).</summary>
        public C1609HostProof(IC1609Processes processes, IFileSystem files, Func<DateTimeOffset> utcNow, Func<Guid> newGuid = null, Func<string, string> variables = null)
        {
            _processes = processes ?? throw new ArgumentNullException(nameof(processes));
            _files = files ?? throw new ArgumentNullException(nameof(files));
            _utcNow = utcNow ?? throw new ArgumentNullException(nameof(utcNow));
            _newGuid = newGuid ?? Guid.NewGuid;
            _variables = variables ?? Environment.GetEnvironmentVariable;
        }

        /// <summary>$script:C1986OriginMovedDiagnosis: the withdrawn-ref record of the last writer or live check (empty: none).</summary>
        public string OriginMovedDiagnosis { get; set; } = "";

        /// <summary>$script:C1609LiveProofReason: the last live check's reason token (empty: none or passed).</summary>
        public string LiveProofReason { get; set; } = "";

        /// <summary>$script:C1609HostProofReason: the last writer attempt's reason token.</summary>
        public string HostProofReason { get; set; } = "";

        /// <summary>The children.</summary>
        public IC1609Processes Processes => _processes;

        // ---- scripts/c1008-host-proof.ps1 ----

        /// <summary>Get-C1008Sha256: lower-case hex SHA-256 of the text's UTF-8 bytes.</summary>
        public static string Sha256(string text) => Convert.ToHexString(SHA256.HashData(Utf8.GetBytes(text ?? ""))).ToLowerInvariant();

        /// <summary>Get-FileHash -Algorithm SHA256, lower case.</summary>
        public string FileSha256(string path)
        {
            using var stream = _files.File.OpenRead(path);
            return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        }

        /// <summary>
        /// ConvertTo-C1008ScrubbedOrigin: null for an empty URL, a control or space character, or a credential needle after the
        /// userinfo is dropped (a userinfo with a colon or a GitHub token prefix is dropped); else the URL without that userinfo.
        /// </summary>
        public static string ScrubOrigin(string originUrl)
        {
            if (string.IsNullOrEmpty(originUrl)) return null;
            foreach (var ch in originUrl)
                if (ch <= 32 || ch == 127) return null;
            var scrubbed = originUrl;
            var schemeIdx = originUrl.IndexOf("://", StringComparison.Ordinal);
            if (schemeIdx >= 0)
            {
                var after = originUrl.Substring(schemeIdx + 3);
                var at = after.IndexOf('@');
                if (at >= 0)
                {
                    var userinfo = after.Substring(0, at);
                    var rest = after.Substring(at + 1);
                    var strip = userinfo.IndexOf(':') >= 0;
                    if (!strip)
                        foreach (var prefix in new[] { "ghp_", "gho_", "ghu_", "ghs_", "ghr_", "github_pat_" })
                            if (userinfo.IndexOf(prefix, StringComparison.Ordinal) >= 0) { strip = true; break; }
                    if (strip) scrubbed = originUrl.Substring(0, schemeIdx) + "://" + rest;
                }
            }
            var lower = scrubbed.ToLowerInvariant();
            foreach (var needle in Needles)
                if (lower.IndexOf(needle, StringComparison.Ordinal) >= 0) return null;
            return scrubbed;
        }

        /// <summary>Get-C1008HostLsRemoteDiagnosis: the refusal that names a safe repo and the status, else the bare RecycleGitAuditUnknown.</summary>
        public static string LsRemoteDiagnosis(string repo, int status)
        {
            if (status < 0) status = 1;
            repo ??= "";
            var lower = repo.ToLowerInvariant();
            var safe = Regex.IsMatch(repo, "^[A-Za-z0-9._/-]+$") && repo.IndexOf("..", StringComparison.Ordinal) < 0
                && repo.IndexOf("//", StringComparison.Ordinal) < 0 && !repo.StartsWith("/", StringComparison.Ordinal);
            if (safe)
                foreach (var needle in Needles)
                    if (lower.IndexOf(needle, StringComparison.Ordinal) >= 0) { safe = false; break; }
            if (!safe) return "RecycleGitAuditUnknown";
            return "RecycleGitAuditUnknown audit check=host-ls-remote status=" + status.ToString(CultureInfo.InvariantCulture) + " repo=" + repo;
        }

        /// <summary>Test-C1008SafeAuditDiagnosis.</summary>
        public static bool SafeAuditDiagnosis(string diagnosis) => RollingPs.TestC1008SafeAuditDiagnosis(diagnosis);

        private static readonly Regex OriginLine = new Regex("^C1008_PRIVATE_ORIGIN repo=([A-Za-z0-9._/-]+) origin=(\\S+) originHash=([0-9a-f]{64}) proofName=([0-9a-f]{64})$");

        /// <summary>
        /// Read-C1008PrivateOriginLines: each C1008_PRIVATE_ORIGIN line (CR trimmed, empty lines skipped) whose repo is safe, whose
        /// origin is already scrubbed and whose hash and proof name are its digests, with no proof name twice. Anything else throws
        /// C1609Refusal RecycleGitAuditUnknown (this source's site). A blank text is no rows.
        /// </summary>
        public static IReadOnlyList<C1008OriginRow> ReadPrivateOriginLines(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return Array.Empty<C1008OriginRow>();
            var rows = new List<C1008OriginRow>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var raw in text.Split('\n'))
            {
                var line = raw.TrimEnd('\r');
                if (line.Length == 0) continue;
                var match = OriginLine.Match(line);
                if (!match.Success) throw new C1609Refusal("RecycleGitAuditUnknown");
                var repo = match.Groups[1].Value;
                var origin = match.Groups[2].Value;
                var originHash = match.Groups[3].Value;
                var proofName = match.Groups[4].Value;
                if (repo.IndexOf("..", StringComparison.Ordinal) >= 0 || repo.IndexOf("//", StringComparison.Ordinal) >= 0 || repo.StartsWith("/", StringComparison.Ordinal))
                    throw new C1609Refusal("RecycleGitAuditUnknown");
                var lowerRepo = repo.ToLowerInvariant();
                foreach (var needle in Needles)
                    if (lowerRepo.IndexOf(needle, StringComparison.Ordinal) >= 0) throw new C1609Refusal("RecycleGitAuditUnknown");
                var scrubbed = ScrubOrigin(origin);
                if (string.IsNullOrEmpty(scrubbed) || !string.Equals(scrubbed, origin, StringComparison.Ordinal)) throw new C1609Refusal("RecycleGitAuditUnknown");
                if (!string.Equals(Sha256(scrubbed), originHash, StringComparison.Ordinal) || !string.Equals(Sha256(repo + "\n" + scrubbed), proofName, StringComparison.Ordinal))
                    throw new C1609Refusal("RecycleGitAuditUnknown");
                // The script's @{} key check ignored case.
                if (!seen.Add(proofName)) throw new C1609Refusal("RecycleGitAuditUnknown");
                rows.Add(new C1008OriginRow { Repo = repo, Origin = scrubbed, OriginHash = originHash, ProofName = proofName });
            }
            return rows;
        }

        // ---- scripts/c1609-host-proof.ps1 ----

        /// <summary>Invoke-C1609HostGit: git with the fixed policy options; its output, else (any failure) the repo's host-ls-remote refusal.</summary>
        public string HostGit(string repo, IEnumerable<string> gitArgs, int timeoutMs = 30000, string workingDirectory = "", string inputText = "", string outputFile = "")
        {
            try
            {
                var arguments = new List<string>(GitPolicy);
                arguments.AddRange(gitArgs);
                var result = _processes.Run(new C1609ProcessCall
                {
                    FileName = "git",
                    Arguments = arguments,
                    TimeoutMs = timeoutMs,
                    WorkingDirectory = workingDirectory ?? "",
                    InputText = inputText ?? "",
                    OutputFile = outputFile ?? "",
                    CaptureOutput = true,
                    GitEnvironment = true,
                });
                if (result.ExitCode != 0) throw new C1609Refusal("git");
                return result.Stdout;
            }
            catch (Exception)
            {
                throw new C1609Refusal(LsRemoteDiagnosis(repo, 2));
            }
        }

        private static readonly Regex TypeLine = new Regex("^([0-9a-f]{40}) (commit|tag|tree|blob)$");
        private static readonly Regex PeeledCommit = new Regex("^([0-9a-f]{40}) commit$");
        private static readonly Regex Oid = new Regex("^[0-9a-f]{40}$");
        private static readonly Regex RootRef = new Regex("\trefs/(heads/|antiphon/salvage/)");
        private static readonly string[] ObjectTypes = { "commit", "tag", "tree", "blob" };

        private static int Elapsed(Stopwatch watch) => (int)watch.ElapsedMilliseconds;

        private static IEnumerable<string> Lines(string text) => text.TrimEnd('\n').Split('\n');

        /// <summary>Assert-C1609RequiredObjects: every required object exists with its type and is reachable from the advertisement.</summary>
        public void AssertRequiredObjects(IReadOnlyList<C1609RequiredObject> requiredObjects, IReadOnlyList<string> advertisement, string scratch, string repo, int timeoutMs)
        {
            if (requiredObjects == null || requiredObjects.Count == 0) return;
            var expected = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var row in requiredObjects)
                if (row == null || row.ObjectId == null || !Oid.IsMatch(row.ObjectId) || row.Type == null || !ObjectTypes.Contains(row.Type, StringComparer.Ordinal)
                    || !expected.TryAdd(row.ObjectId, row.Type))
                    throw new C1609Refusal("required-object");
            var watch = Stopwatch.StartNew();
            var types = HostGit(repo, new[] { "--git-dir", scratch, "cat-file", "--batch-check=%(objectname) %(objecttype)" }, timeoutMs, scratch,
                string.Join("\n", expected.Keys) + "\n");
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var line in Lines(types))
            {
                var match = TypeLine.Match(line);
                if (!match.Success || !expected.TryGetValue(match.Groups[1].Value, out var type) || !string.Equals(type, match.Groups[2].Value, StringComparison.Ordinal)
                    || !seen.Add(match.Groups[1].Value))
                    throw new C1609Refusal("object-type");
            }
            if (seen.Count != expected.Count) throw new C1609Refusal("object-coverage");
            var advertised = new HashSet<string>(StringComparer.Ordinal);
            var roots = new List<string>();
            foreach (var line in advertisement)
            {
                advertised.Add(line.Substring(0, 40));
                if (RootRef.IsMatch(line)) roots.Add(line.Substring(0, 40));
            }
            var commits = new HashSet<string>(StringComparer.Ordinal);
            var trees = new HashSet<string>(StringComparer.Ordinal);
            foreach (var pair in expected)
            {
                switch (pair.Value)
                {
                    case "commit":
                        commits.Add(pair.Key);
                        break;
                    case "tree":
                        if (!advertised.Contains(pair.Key)) trees.Add(pair.Key);
                        break;
                    case "blob":
                        if (!advertised.Contains(pair.Key)) throw new C1609Refusal("unpublished-blob");
                        break;
                    case "tag":
                        {
                            if (!advertised.Contains(pair.Key)) throw new C1609Refusal("unpublished-tag");
                            var remaining = timeoutMs - Elapsed(watch);
                            if (remaining <= 0) throw new C1609Refusal("deadline");
                            var peeled = HostGit(repo, new[] { "--git-dir", scratch, "cat-file", "--batch-check=%(objectname) %(objecttype)" }, remaining, scratch,
                                pair.Key + "^{commit}\n").Trim();
                            var match = PeeledCommit.Match(peeled);
                            if (match.Success) commits.Add(match.Groups[1].Value);
                            else if (!string.Equals(peeled, pair.Key + "^{commit} missing", StringComparison.Ordinal)) throw new C1609Refusal("tag-peel");
                            break;
                        }
                }
            }
            if (commits.Count > 0)
            {
                var remaining = timeoutMs - Elapsed(watch);
                if (remaining <= 0) throw new C1609Refusal("deadline");
                var revisions = new List<string>(commits);
                foreach (var root in roots) revisions.Add("^" + root);
                var count = HostGit(repo, new[] { "--git-dir", scratch, "rev-list", "--count", "--stdin" }, remaining, scratch, string.Join("\n", revisions) + "\n");
                if (!string.Equals(count.Trim(), "0", StringComparison.Ordinal)) throw new C1609Refusal("unpublished-commit");
            }
            if (trees.Count > 0)
            {
                var remaining = timeoutMs - Elapsed(watch);
                if (remaining <= 0) throw new C1609Refusal("deadline");
                var file = _files.Path.Combine(scratch, "required-trees");
                if (_files.File.Exists(file)) _files.File.Delete(file);
                HostGit(repo, new[] { "--git-dir", scratch, "rev-list", "--objects", "--no-object-names", "--filter=blob:none", "--missing=error", "--stdin" }, remaining, scratch,
                    string.Join("\n", roots) + "\n", file);
                foreach (var line in _files.File.ReadLines(file))
                {
                    if (!Oid.IsMatch(line)) throw new C1609Refusal("tree-graph");
                    trees.Remove(line);
                }
                if (trees.Count > 0) throw new C1609Refusal("unpublished-tree");
            }
        }

        private static readonly Regex AdvertisementLine = new Regex("^[0-9a-f]{40}\trefs/[^\\s]+$");
        private static readonly Regex HeadOrTag = new Regex("^refs/(heads|tags)/[^\\s]+$");
        private static readonly Regex Salvage = new Regex("^refs/antiphon/salvage/[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}/([0-9a-f]{40})$");
        private static readonly Regex Credential = new Regex("gh[pousr]_|github_pat_|token|password|secret", RegexOptions.IgnoreCase);

        /// <summary>
        /// Read-C1609Advertisement: the ref lines (oid TAB ref), each a head, a tag or a salvage ref naming its own object, no ref twice,
        /// no credential word, at least one head; sorted ordinally. Anything else throws advertisement.
        /// </summary>
        public static string[] ReadAdvertisement(string text)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var rows = new List<string>();
            var lines = (text ?? "").Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i].TrimEnd('\r');
                if (line.Length == 0 && i == lines.Length - 1) continue;
                if (!AdvertisementLine.IsMatch(line)) throw new C1609Refusal("advertisement");
                var reference = line.Substring(41);
                if (!HeadOrTag.IsMatch(reference))
                {
                    var match = Salvage.Match(reference);
                    if (!match.Success || !string.Equals(match.Groups[1].Value, line.Substring(0, 40), StringComparison.Ordinal)) throw new C1609Refusal("advertisement");
                }
                if (!seen.Add(reference)) throw new C1609Refusal("advertisement");
                if (Credential.IsMatch(line.ToLowerInvariant())) throw new C1609Refusal("advertisement");
                rows.Add(line);
            }
            if (rows.Count == 0 || !rows.Any(r => r.Contains("\trefs/heads/", StringComparison.Ordinal))) throw new C1609Refusal("advertisement");
            var array = rows.ToArray();
            Array.Sort(array, StringComparer.Ordinal);
            return array;
        }

        /// <summary>Assert-C1609CommitRoots: every head and salvage root of the advertisement is a commit in the scratch repository.</summary>
        public void AssertCommitRoots(IReadOnlyList<string> advertisement, string scratch, string repo, int timeoutMs)
        {
            var roots = new HashSet<string>(StringComparer.Ordinal);
            foreach (var line in advertisement)
                if (RootRef.IsMatch(line)) roots.Add(line.Substring(0, 40));
            var types = HostGit(repo, new[] { "--git-dir", scratch, "cat-file", "--batch-check=%(objectname) %(objecttype)" }, timeoutMs, scratch, string.Join("\n", roots) + "\n");
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var line in Lines(types))
            {
                var match = PeeledCommit.Match(line);
                if (!match.Success || !roots.Contains(match.Groups[1].Value) || !seen.Add(match.Groups[1].Value)) throw new C1609Refusal("origin-type");
            }
            if (seen.Count != roots.Count) throw new C1609Refusal("origin-type");
        }

        private static readonly Regex BundleLine = new Regex("^([0-9a-f]{40}) ([^\\s]+)$");

        /// <summary>Assert-C1609BundleAdvertisement: a bundle's refs (HEAD aside) are exactly the advertisement's unpeeled rows.</summary>
        public static void AssertBundleAdvertisement(string text, IReadOnlyList<string> advertisement)
        {
            var rows = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var line in Lines(text ?? ""))
            {
                var match = BundleLine.Match(line);
                if (!match.Success || !seen.Add(match.Groups[2].Value)) throw new C1609Refusal("bundle-refs");
                if (string.Equals(match.Groups[2].Value, "HEAD", StringComparison.Ordinal)) continue;
                rows.Add(match.Groups[1].Value + "\t" + match.Groups[2].Value);
            }
            var actual = ReadAdvertisement(string.Join("\n", rows) + "\n");
            var expected = advertisement.Where(a => !a.EndsWith("^{}", StringComparison.Ordinal));
            if (!string.Equals(string.Join("\n", actual), string.Join("\n", expected), StringComparison.Ordinal)) throw new C1609Refusal("bundle-refs");
        }

        /// <summary>Get-C1986Remaining: min(cap, budget - elapsed), or deadline when none is left.</summary>
        public static int Remaining(Stopwatch watch, int budgetMs, int cap = 300000)
        {
            var remaining = Math.Min(cap, budgetMs - Elapsed(watch));
            if (remaining <= 0) throw new C1609Refusal("deadline");
            return remaining;
        }

        /// <summary>Set-C1986ScratchRefs: the scratch repository's refs become exactly the advertisement's unpeeled rows.</summary>
        public void SetScratchRefs(IReadOnlyList<string> advertisement, string scratch, string repo, Stopwatch watch, int budgetMs)
        {
            var desired = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var line in advertisement)
                if (!line.EndsWith("^{}", StringComparison.Ordinal)) desired[line.Substring(41)] = line.Substring(0, 40);
            var current = HostGit(repo, new[] { "--git-dir", scratch, "for-each-ref", "--format=%(refname)" }, Remaining(watch, budgetMs), scratch);
            var updates = new List<string>();
            foreach (var reference in current.Split('\n'))
                if (reference.Length > 0 && !desired.ContainsKey(reference)) updates.Add("delete " + reference);
            foreach (var pair in desired) updates.Add("update " + pair.Key + " " + pair.Value);
            HostGit(repo, new[] { "--git-dir", scratch, "update-ref", "--stdin" }, Remaining(watch, budgetMs), scratch, string.Join("\n", updates) + "\n");
        }

        // Sort-Object's default comparison (the current culture, ignoring case), stable as LINQ's OrderBy is.
        private static readonly StringComparer SortObject = StringComparer.Create(CultureInfo.CurrentCulture, true);

        /// <summary>
        /// Select-C1986DependencyAdvertisement: the advertisement rows a renewal binds: every tag, the first covering root (master,
        /// main, then the rest by name) of each required commit (or a tag's peeled commit), the first root that holds each unadvertised
        /// required tree, and a head when none was selected. No required object: the whole advertisement.
        /// </summary>
        public IReadOnlyList<string> SelectDependencies(IReadOnlyList<C1609RequiredObject> requiredObjects, IReadOnlyList<string> advertisement, string scratch, string repo,
            Stopwatch watch, int budgetMs)
        {
            if (requiredObjects == null || requiredObjects.Count == 0) return advertisement;
            var selected = new HashSet<string>(StringComparer.Ordinal);
            var commits = new HashSet<string>(StringComparer.Ordinal);
            foreach (var line in advertisement)
                if (line.Contains("\trefs/tags/", StringComparison.Ordinal)) selected.Add(line.Substring(41));
            foreach (var row in requiredObjects)
            {
                if (string.Equals(row.Type, "commit", StringComparison.Ordinal)) commits.Add(row.ObjectId);
                else if (string.Equals(row.Type, "tag", StringComparison.Ordinal))
                {
                    var peeled = HostGit(repo, new[] { "--git-dir", scratch, "cat-file", "--batch-check=%(objectname) %(objecttype)" }, Remaining(watch, budgetMs), scratch,
                        row.ObjectId + "^{commit}\n").Trim();
                    var match = PeeledCommit.Match(peeled);
                    if (match.Success) commits.Add(match.Groups[1].Value);
                }
            }
            if (commits.Count > 0)
            {
                var rootRows = advertisement.Where(l => RootRef.IsMatch(l))
                    .OrderBy(l => string.Equals(l.Substring(41), "refs/heads/master", StringComparison.Ordinal) ? 0 : string.Equals(l.Substring(41), "refs/heads/main", StringComparison.Ordinal) ? 1 : 2)
                    .ThenBy(l => l.Substring(41), SortObject)
                    .ToArray();
                var cover = new Dictionary<string, int>(StringComparer.Ordinal);
                for (var i = 0; i < rootRows.Length; i++)
                {
                    var oid = rootRows[i].Substring(0, 40);
                    if (!cover.ContainsKey(oid)) cover[oid] = i;
                }
                var file = _files.Path.Combine(scratch, "c1986-commits");
                if (_files.File.Exists(file)) _files.File.Delete(file);
                HostGit(repo, new[] { "--git-dir", scratch, "rev-list", "--topo-order", "--parents", "--stdin" }, Remaining(watch, budgetMs), scratch,
                    string.Join("\n", cover.Keys) + "\n", file);
                foreach (var line in _files.File.ReadLines(file))
                {
                    if (!Regex.IsMatch(line, "^[0-9a-f]{40}( [0-9a-f]{40})*$")) throw new C1609Refusal("commit-graph");
                    var ids = line.Split(' ');
                    var oid = ids[0];
                    if (!cover.TryGetValue(oid, out var rank)) throw new C1609Refusal("commit-graph");
                    if (commits.Remove(oid)) selected.Add(rootRows[rank].Substring(41));
                    for (var p = 1; p < ids.Length; p++)
                    {
                        var parent = ids[p];
                        if (!cover.TryGetValue(parent, out var known) || rank < known) cover[parent] = rank;
                    }
                }
                if (commits.Count > 0) throw new C1609Refusal("unpublished-commit");
            }
            var trees = new HashSet<string>(StringComparer.Ordinal);
            foreach (var row in requiredObjects)
                if (string.Equals(row.Type, "tree", StringComparison.Ordinal) && !advertisement.Any(a => string.Equals(a.Substring(0, 40), row.ObjectId, StringComparison.Ordinal)))
                    trees.Add(row.ObjectId);
            foreach (var line in advertisement)
            {
                if (trees.Count == 0) break;
                if (!RootRef.IsMatch(line)) continue;
                var file = _files.Path.Combine(scratch, "c1986-trees");
                if (_files.File.Exists(file)) _files.File.Delete(file);
                HostGit(repo, new[] { "--git-dir", scratch, "rev-list", "--objects", "--no-object-names", "--filter=blob:none", "--missing=error", line.Substring(0, 40) },
                    Remaining(watch, budgetMs), scratch, "", file);
                foreach (var oid in _files.File.ReadLines(file))
                    if (trees.Remove(oid)) selected.Add(line.Substring(41));
            }
            if (trees.Count > 0) throw new C1609Refusal("unpublished-tree");
            // v2 requires a head even when all protected commits are held by salvage.
            if (!selected.Any(s => s.StartsWith("refs/heads/", StringComparison.Ordinal)))
            {
                var head = advertisement.First(a => a.Contains("\trefs/heads/", StringComparison.Ordinal));
                selected.Add(head.Substring(41));
            }
            return advertisement.Where(a => selected.Contains(a.Substring(41))).ToArray();
        }

        /// <summary>Stop-C1986WithdrawnRef: records the withdrawn-ref diagnosis (an unsafe ref name redacted) and throws withdrawn-ref.</summary>
        public void StopWithdrawnRef(string repo, string reference, string old, string current)
        {
            if (!Regex.IsMatch(reference ?? "", "^refs/[A-Za-z0-9._/-]+$")) reference = "refs/redacted";
            OriginMovedDiagnosis = "RecycleOriginMoved reason=withdrawn-ref repo=" + repo + " ref=" + reference + " old=" + old + " new=" + current;
            throw new C1609Refusal("withdrawn-ref");
        }

        /// <summary>
        /// Assert-C1986PublicationRefs: each expected unpeeled ref still points at its object, or (not exact) a head moved forward to a
        /// descendant; anything else is a withdrawn ref.
        /// </summary>
        public void AssertPublicationRefs(IReadOnlyList<string> expected, IReadOnlyList<string> current, string scratch, string repo, Stopwatch watch, int budgetMs, bool exact = false)
        {
            var live = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var line in current) live[line.Substring(41)] = line.Substring(0, 40);
            foreach (var line in expected)
            {
                if (line.EndsWith("^{}", StringComparison.Ordinal)) continue;
                var reference = line.Substring(41);
                var old = line.Substring(0, 40);
                var now = live.TryGetValue(reference, out var tip) ? tip : "deleted";
                if (string.Equals(old, now, StringComparison.Ordinal)) continue;
                if (!exact && !string.Equals(now, "deleted", StringComparison.Ordinal) && reference.StartsWith("refs/heads/", StringComparison.Ordinal))
                {
                    var count = HostGit(repo, new[] { "--git-dir", scratch, "rev-list", "--count", old, "^" + now }, Remaining(watch, budgetMs), scratch);
                    if (string.Equals(count.Trim(), "0", StringComparison.Ordinal)) continue;
                }
                StopWithdrawnRef(repo, reference, old, now);
            }
        }

        /// <summary>
        /// Write-C1609HostOriginProof: proves the origin into the proof directory (the bundle and the v2 proof file), retrying a
        /// snapshot move up to five attempts inside the one network budget; each failed attempt logs its reason token. The attempts
        /// share one scratch repository, removed at the end. Throws the attempt's refusal.
        /// </summary>
        public void WriteOriginProof(C1609ProofRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            var budget = Stopwatch.StartNew();
            OriginMovedDiagnosis = "";
            var scratch = _files.Path.Combine(_files.Path.GetTempPath(), "c1609-host-" + _newGuid().ToString("N"));
            try
            {
                for (var attempt = 1; ; attempt++)
                {
                    HostProofReason = "";
                    var remaining = request.NetworkBudgetMs - Elapsed(budget);
                    try
                    {
                        Attempt(request, remaining, scratch);
                        return;
                    }
                    catch (C1609Refusal)
                    {
                        var reason = HostProofReason ?? "";
                        if (!Token.IsMatch(reason)) reason = "other";
                        if (!string.IsNullOrEmpty(request.LogPath) && Regex.IsMatch(request.RepoRelative ?? "", "^[A-Za-z0-9._/-]+$"))
                        {
                            try
                            {
                                _files.File.AppendAllText(request.LogPath, "repo=" + request.RepoRelative + " attempt=" + attempt.ToString(CultureInfo.InvariantCulture) + " reason=" + reason + "\n", Utf8);
                            }
                            catch (Exception) { }
                        }
                        if (!string.Equals(reason, "snapshot", StringComparison.Ordinal) || attempt >= 5 || request.NetworkBudgetMs - Elapsed(budget) <= 0) throw;
                    }
                }
            }
            finally
            {
                if (_files.Directory.Exists(scratch)) DeleteTree(_files, scratch);
            }
        }

        private void Attempt(C1609ProofRequest r, int networkBudgetMs, string scratch)
        {
            var bundle = "";
            var proofFile = "";
            var bundleCreated = false;
            try
            {
                var repo = r.RepoRelative ?? "";
                var origin = r.OriginUrl ?? "";
                if (!Regex.IsMatch(r.OperationId ?? "", "^c1008[0-9a-f]{32}$") || !Regex.IsMatch(r.PhaseNonce ?? "", "^[0-9a-f]{32}$")
                    || !Regex.IsMatch(r.InventoryDigest ?? "", "^[0-9a-f]{64}$") || !Regex.IsMatch(repo, "^[A-Za-z0-9._/-]+$")
                    || repo.Contains("..", StringComparison.Ordinal) || repo.Contains("//", StringComparison.Ordinal) || repo.StartsWith("/", StringComparison.Ordinal))
                    throw new C1609Refusal("context");
                var allowed = new HashSet<string>(r.ApprovedOrigins ?? Array.Empty<string>(), StringComparer.Ordinal);
                if (!allowed.Contains(origin)) throw new C1609Refusal("origin");
                var scrubbed = ScrubOrigin(origin);
                if (string.IsNullOrEmpty(scrubbed) || !string.Equals(scrubbed, origin, StringComparison.Ordinal)) throw new C1609Refusal("origin");
                if (!Regex.IsMatch(origin, "^git@[A-Za-z0-9.-]+:[A-Za-z0-9._/-]+$"))
                {
                    if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri)
                        || !(string.Equals(uri.Scheme, "https", StringComparison.Ordinal) || string.Equals(uri.Scheme, "ssh", StringComparison.Ordinal))
                        || uri.Query.Length > 0 || uri.Fragment.Length > 0
                        || (string.Equals(uri.Scheme, "https", StringComparison.Ordinal) && uri.UserInfo.Length > 0)
                        || (string.Equals(uri.Scheme, "ssh", StringComparison.Ordinal) && !string.Equals(uri.UserInfo, "git", StringComparison.Ordinal))
                        || Regex.IsMatch(origin, "[\\x00-\\x20\\x7f]"))
                        throw new C1609Refusal("origin");
                }
                if (!_files.Directory.Exists(r.ProofDirectory) || IsReparse(_files.DirectoryInfo.New(r.ProofDirectory).Attributes)) throw new C1609Refusal("custody");
                var now = _utcNow().ToUnixTimeSeconds();
                if (r.CreatedUnix < 0 || r.CreatedUnix > now + 60 || now - r.CreatedUnix > 600) throw new C1609Refusal("clock");
                if (networkBudgetMs <= 0 || networkBudgetMs > 600000) throw new C1609Refusal("deadline");
                var watch = Stopwatch.StartNew();
                // The caller owns the scratch path and removes it. Only the first attempt creates it, and only a fresh repository may
                // take a previous bundle: a retry fetches into the refs it already holds.
                if (string.IsNullOrEmpty(scratch) || !Regex.IsMatch(_files.Path.GetFileName(scratch), "^c1609-host-[0-9a-f]{32}$")) throw new C1609Refusal("custody");
                var fresh = !_files.Directory.Exists(scratch);
                if (fresh) _files.Directory.CreateDirectory(scratch);
                foreach (var leftover in new[] { "c1609-proof.bundle", "required-trees" })
                {
                    var path = _files.Path.Combine(scratch, leftover);
                    if (_files.File.Exists(path)) _files.File.Delete(path);
                }
                var originHash = Sha256(origin);
                var name = Sha256(repo + "\n" + origin);
                bundle = _files.Path.Combine(r.ProofDirectory, name + ".bundle");
                proofFile = _files.Path.Combine(r.ProofDirectory, name);
                if (_files.File.Exists(bundle) || _files.File.Exists(proofFile)) throw new C1609Refusal("existing");
                if (fresh) HostGit(repo, new[] { "init", "-q", "--bare", "--template=", scratch }, workingDirectory: scratch);
                var common = new[] { "--git-dir", scratch };
                var cap = Math.Min(30000, networkBudgetMs - Elapsed(watch));
                if (cap <= 0) throw new C1609Refusal("deadline");
                var lsRemote = new[] { "ls-remote", origin, "refs/heads/*", "refs/tags/*", "refs/antiphon/salvage/*" };
                var before = ReadAdvertisement(HostGit(repo, common.Concat(lsRemote), cap, scratch));
                var reuse = false;
                string[] old = null;
                IReadOnlyList<string> dependencies = null;
                if (!string.IsNullOrEmpty(r.PreviousProofFile))
                {
                    old = _files.File.ReadAllLines(r.PreviousProofFile);
                    if (old.Length < 12 || !string.Equals(old[0], "c1008-host-proof 2", StringComparison.Ordinal) || !string.Equals(old[1], "operation " + r.OperationId, StringComparison.Ordinal)
                        || !string.Equals(old[2], "origin " + originHash, StringComparison.Ordinal) || !Regex.IsMatch(old[7], "^bundle [0-9a-f]{64}$")
                        || IsReparse(_files.FileInfo.New(r.PreviousProofFile).Attributes)
                        || !string.Equals(FileSha256(r.PreviousProofFile + ".bundle"), old[7].Substring(7), StringComparison.Ordinal))
                        throw new C1609Refusal("previous-proof");
                    var oldAdvertisement = ReadAdvertisement(string.Join("\n", old.Skip(11)) + "\n");
                    if (!string.Equals(old[3], "repo " + repo, StringComparison.Ordinal) || !Regex.IsMatch(old[5], "^nonce [0-9a-f]{32}$")
                        || !Regex.IsMatch(old[6], "^inventory [0-9a-f]{64}$")
                        || !string.Equals(old[8], "advertisement " + Sha256(string.Join("\n", oldAdvertisement) + "\n"), StringComparison.Ordinal)
                        || !string.Equals(old[9], "count " + oldAdvertisement.Length.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal))
                        throw new C1609Refusal("previous-proof");
                    // Select dependencies in the OLD graph before fetching a replacement. Otherwise a rewind could silently choose a
                    // different covering ref.
                    var priorBundle = _files.Path.Combine(scratch, "c1986-previous.bundle");
                    _files.File.Copy(r.PreviousProofFile + ".bundle", priorBundle, true);
                    var oldRefs = HostGit(repo, common.Concat(new[] { "bundle", "unbundle", priorBundle }), Remaining(watch, networkBudgetMs), scratch);
                    AssertBundleAdvertisement(oldRefs, oldAdvertisement);
                    SetScratchRefs(oldAdvertisement, scratch, repo, watch, networkBudgetMs);
                    dependencies = SelectDependencies(r.RequiredObjects, oldAdvertisement, scratch, repo, watch, networkBudgetMs);
                    reuse = !r.FreshGraph && fresh && string.Equals(string.Join("\n", oldAdvertisement), string.Join("\n", before), StringComparison.Ordinal);
                }
                cap = Math.Min(300000, networkBudgetMs - Elapsed(watch));
                if (cap <= 0) throw new C1609Refusal("deadline");
                // CARD-1693: Git reads and writes a bundle only inside the short scratch directory (a desktop proof directory is long
                // enough that every bundle lock is past MAX_PATH for Git for Windows without core.longpaths). .NET moves the bytes.
                var staged = _files.Path.Combine(scratch, "c1609-proof.bundle");
                if (reuse)
                {
                    _files.File.Copy(r.PreviousProofFile + ".bundle", staged, false);
                    if (!string.Equals(FileSha256(staged), old[7].Substring(7), StringComparison.Ordinal)) throw new C1609Refusal("previous-proof");
                    var bundleRefs = HostGit(repo, common.Concat(new[] { "bundle", "unbundle", staged }), cap, scratch);
                    AssertBundleAdvertisement(bundleRefs, before);
                    var updates = before.Where(b => !b.EndsWith("^{}", StringComparison.Ordinal)).Select(b => "update " + b.Substring(41) + " " + b.Substring(0, 40));
                    HostGit(repo, common.Concat(new[] { "update-ref", "--stdin" }), cap, scratch, string.Join("\n", updates) + "\n");
                }
                else
                {
                    HostGit(repo, common.Concat(new[]
                    {
                        "fetch", "-q", "--prune", "--no-tags", "--no-write-fetch-head", "--no-recurse-submodules", origin,
                        "+refs/heads/*:refs/heads/*", "+refs/tags/*:refs/tags/*", "+refs/antiphon/salvage/*:refs/antiphon/salvage/*",
                    }), cap, scratch);
                }
                var actual = ReadAdvertisement(HostGit(repo, common.Concat(new[] { "show-ref", "--dereference" }), workingDirectory: scratch).Replace(" ", "\t"));
                if (!string.Equals(string.Join("\n", before), string.Join("\n", actual), StringComparison.Ordinal)) throw new C1609Refusal("snapshot");
                cap = Math.Min(300000, networkBudgetMs - Elapsed(watch));
                if (cap <= 0) throw new C1609Refusal("deadline");
                AssertCommitRoots(before, scratch, repo, cap);
                if (!string.IsNullOrEmpty(r.PreviousProofFile)) AssertPublicationRefs(dependencies, before, scratch, repo, watch, networkBudgetMs);
                cap = Math.Min(300000, networkBudgetMs - Elapsed(watch));
                if (cap <= 0) throw new C1609Refusal("deadline");
                AssertRequiredObjects(r.RequiredObjects, before, scratch, repo, cap);
                var proven = SelectDependencies(r.RequiredObjects, before, scratch, repo, watch, networkBudgetMs);
                if (!string.Equals(string.Join("\n", proven), string.Join("\n", before), StringComparison.Ordinal))
                {
                    SetScratchRefs(proven, scratch, repo, watch, networkBudgetMs);
                    reuse = false;
                }
                AssertRequiredObjects(r.RequiredObjects, proven, scratch, repo, Remaining(watch, networkBudgetMs));
                cap = Math.Min(300000, networkBudgetMs - Elapsed(watch));
                if (cap <= 0) throw new C1609Refusal("deadline");
                bundleCreated = true;
                if (!reuse) HostGit(repo, common.Concat(new[] { "bundle", "create", staged, "--all" }), cap, scratch);
                _files.File.Move(staged, bundle);
                cap = Math.Min(30000, networkBudgetMs - Elapsed(watch));
                if (cap <= 0) throw new C1609Refusal("deadline");
                var after = ReadAdvertisement(HostGit(repo, common.Concat(lsRemote), cap, scratch));
                if (!string.Equals(string.Join("\n", before), string.Join("\n", after), StringComparison.Ordinal)) throw new C1609Refusal("snapshot");
                var completed = _utcNow().ToUnixTimeSeconds();
                if (Math.Abs((completed - now) * 1000 - watch.ElapsedMilliseconds) > 60000) throw new C1609Refusal("clock");
                var bundleHash = FileSha256(bundle);
                var advertisement = string.Join("\n", proven) + "\n";
                var header = new[]
                {
                    "c1008-host-proof 2", "operation " + r.OperationId, "origin " + originHash, "repo " + repo,
                    "created " + now.ToString(CultureInfo.InvariantCulture), "nonce " + r.PhaseNonce, "inventory " + r.InventoryDigest, "bundle " + bundleHash,
                    "advertisement " + Sha256(advertisement), "count " + proven.Count.ToString(CultureInfo.InvariantCulture), "completed " + completed.ToString(CultureInfo.InvariantCulture),
                };
                WriteNew(proofFile, Utf8.GetBytes(string.Join("\n", header) + "\n" + advertisement));
                _files.File.AppendAllText(r.LogPath, "repo=" + repo + " refs=" + proven.Count.ToString(CultureInfo.InvariantCulture) + "\n", Utf8);
            }
            catch (Exception e) when (!(e is C590Exit))
            {
                // No URL, credential or raw Git stderr crosses this boundary. The reason is a fixed token.
                var reason = e is C1609Refusal ? e.Message : "";
                HostProofReason = reason.StartsWith("RecycleGitAuditUnknown", StringComparison.Ordinal) ? "git" : Token.IsMatch(reason) ? reason : "other";
                // A failed attempt's bundle is never a proof; remove only the file this attempt wrote.
                if (bundleCreated && bundle.Length > 0 && _files.File.Exists(bundle) && !_files.File.Exists(proofFile))
                {
                    try { _files.File.Delete(bundle); } catch (Exception) { }
                }
                if (string.Equals(reason, "withdrawn-ref", StringComparison.Ordinal)) throw new C1609Refusal(OriginMovedDiagnosis);
                throw new C1609Refusal(LsRemoteDiagnosis(r.RepoRelative, 2));
            }
        }

        /// <summary>
        /// Copy-C1609HostOriginProof: a second repo of an origin whose graph is already proven takes a copy of that proof (its repo
        /// line rewritten, the bundle copied). Anything amiss throws RecycleGitAuditUnknown.
        /// </summary>
        public void CopyOriginProof(string directory, string sourceName, string repo, string origin)
        {
            if (!Regex.IsMatch(sourceName ?? "", "^[0-9a-f]{64}$") || !Regex.IsMatch(repo ?? "", "^[A-Za-z0-9._/-]+$")) throw new C1609Refusal("RecycleGitAuditUnknown");
            var source = _files.Path.Combine(directory, sourceName);
            var lines = _files.File.ReadAllLines(source);
            if (lines.Length < 12 || !string.Equals(lines[0], "c1008-host-proof 2", StringComparison.Ordinal) || !string.Equals(lines[2], "origin " + Sha256(origin), StringComparison.Ordinal))
                throw new C1609Refusal("RecycleGitAuditUnknown");
            var name = Sha256(repo + "\n" + origin);
            lines[3] = "repo " + repo;
            _files.File.Copy(source + ".bundle", _files.Path.Combine(directory, name + ".bundle"), false);
            WriteNew(_files.Path.Combine(directory, name), Utf8.GetBytes(string.Join("\n", lines) + "\n"));
        }

        private void WriteNew(string path, byte[] bytes)
        {
            using var file = _files.File.Open(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            file.Write(bytes, 0, bytes.Length);
            file.Flush(true);
        }

        private static bool IsReparse(FileAttributes attributes) => (attributes & FileAttributes.ReparsePoint) != 0;

        /// <summary>Assert-C1609LiveScratchCustody: the live scratch is named c1609-live-&lt;32 hex&gt; and is not a reparse point.</summary>
        public void AssertLiveScratchCustody(string scratch)
        {
            if (!Regex.IsMatch(_files.Path.GetFileName(scratch ?? ""), "^c1609-live-[0-9a-f]{32}$")) throw new C1609Refusal("custody");
            if (_files.Directory.Exists(scratch) && IsReparse(_files.DirectoryInfo.New(scratch).Attributes)) throw new C1609Refusal("custody");
        }

        /// <summary>
        /// Get-C1609ControlWindowMs: ANTIPHON_RETIRE_CONTROL_WINDOW_MS (3000-8000) or 8000. Any other value throws control-window.
        /// </summary>
        public static int ControlWindowMs(Func<string, string> variables)
        {
            var value = variables?.Invoke("ANTIPHON_RETIRE_CONTROL_WINDOW_MS");
            if (string.IsNullOrEmpty(value)) return 8000;
            // Cap: leave two seconds for the host's 10-second read and three for its 11-second fence. Floor: one origin's steady
            // controls measured on server2 (at most 2.55 s) must fit with margin (CARD-1989 Review c4d03262).
            if (!Regex.IsMatch(value, "^[3-8][0-9]{3}$") || int.Parse(value, CultureInfo.InvariantCulture) > 8000) throw new C1609Refusal("control-window");
            return int.Parse(value, CultureInfo.InvariantCulture);
        }

        /// <summary>This proof's control window (its variables).</summary>
        public int ControlWindowMs() => ControlWindowMs(_variables);

        /// <summary>Remove-Item -Recurse -Force -ErrorAction SilentlyContinue: the tree removed, read-only files included; failures ignored.</summary>
        public static void DeleteTree(IFileSystem files, string path)
        {
            try
            {
                if (!files.Directory.Exists(path)) return;
                foreach (var file in files.Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                {
                    try { files.File.SetAttributes(file, FileAttributes.Normal); } catch (Exception) { }
                }
                files.Directory.Delete(path, true);
            }
            catch (Exception) { }
        }

        private void RemoveLiveScratch(string scratch)
        {
            if (!_files.Directory.Exists(scratch)) return;
            try { _files.Directory.Delete(scratch, true); }
            catch (Exception) { DeleteTree(_files, scratch); }
        }

        private T ControlStep<T>(C1609Timing timing, Stopwatch watch, int budgetMs, string step, Func<T> action)
        {
            var started = watch.ElapsedMilliseconds;
            var outcome = "refused";
            timing.Step = step;
            try
            {
                if (started >= budgetMs) throw new C1609Refusal("control-deadline");
                var result = action();
                if (watch.ElapsedMilliseconds > budgetMs) throw new C1609Refusal("control-deadline");
                outcome = "passed";
                return result;
            }
            catch (Exception)
            {
                timing.Step = step;
                throw;
            }
            finally
            {
                timing.Steps.Add((step, watch.ElapsedMilliseconds - started, outcome));
            }
        }

        /// <summary>
        /// Assert-C1609LiveOriginProof: the control-stream's final read of one origin after the renewed graph was staged. The proof
        /// file and bundle must still be the v2 proof of this operation, origin and repo, and the origin's live advertisement must still
        /// hold every expected ref (a head may only move forward, proven in a scratch repository); each step is charged to the budget
        /// (default the control window, at most 8000 ms). Git runs in its own short scratch directory. Throws the repo's refusal or the
        /// withdrawn-ref record, its reason token in LiveProofReason and timing.Reason (control-deadline when the budget ran out).
        /// </summary>
        public void AssertLiveOriginProof(string originUrl, string repoRelative, string operationId, string proofFile, IReadOnlyList<string> approvedOrigins,
            int? networkBudgetMs = null, C1609Timing timing = null)
        {
            var budgetMs = networkBudgetMs ?? ControlWindowMs();
            LiveProofReason = "";
            OriginMovedDiagnosis = "";
            timing ??= new C1609Timing();
            timing.Steps.Clear();
            timing.Outcome = "refused";
            timing.Reason = "";
            timing.Step = "origin";
            timing.LimitMs = budgetMs;
            var watch = Stopwatch.StartNew();
            var scratch = _files.Path.Combine(_files.Path.GetTempPath(), "c1609-live-" + _newGuid().ToString("N"));
            try
            {
                AssertLiveScratchCustody(scratch);
                var allowed = new HashSet<string>(approvedOrigins ?? Array.Empty<string>(), StringComparer.Ordinal);
                if (!allowed.Contains(originUrl ?? "") || !string.Equals(ScrubOrigin(originUrl), originUrl, StringComparison.Ordinal) || budgetMs <= 0 || budgetMs > 8000)
                    throw new C1609Refusal("origin");
                var old = ControlStep(timing, watch, budgetMs, "proof-read", () => _files.File.ReadAllLines(proofFile));
                if (old.Length < 12 || !string.Equals(old[0], "c1008-host-proof 2", StringComparison.Ordinal)
                    || !string.Equals(old[1], "operation " + operationId, StringComparison.Ordinal)
                    || !string.Equals(old[2], "origin " + Sha256(originUrl), StringComparison.Ordinal)
                    || !string.Equals(old[3], "repo " + repoRelative, StringComparison.Ordinal)
                    || !Regex.IsMatch(old[5], "^nonce [0-9a-f]{32}$") || !Regex.IsMatch(old[6], "^inventory [0-9a-f]{64}$")
                    || !Regex.IsMatch(old[7], "^bundle [0-9a-f]{64}$")
                    || IsReparse(_files.FileInfo.New(proofFile).Attributes))
                    throw new C1609Refusal("proof");
                var bundleHash = ControlStep(timing, watch, budgetMs, "bundle-hash", () => FileSha256(proofFile + ".bundle"));
                if (!string.Equals(bundleHash, old[7].Substring(7), StringComparison.Ordinal)) throw new C1609Refusal("proof");
                var expected = ControlStep(timing, watch, budgetMs, "expected-parse", () => ReadAdvertisement(string.Join("\n", old.Skip(11)) + "\n"));
                if (!string.Equals(old[8], "advertisement " + Sha256(string.Join("\n", expected) + "\n"), StringComparison.Ordinal)
                    || !string.Equals(old[9], "count " + expected.Length.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal))
                    throw new C1609Refusal("proof");
                _files.Directory.CreateDirectory(scratch);
                AssertLiveScratchCustody(scratch);
                var lsRemote = new[] { "ls-remote", originUrl, "refs/heads/*", "refs/tags/*", "refs/antiphon/salvage/*" };
                var text = ControlStep(timing, watch, budgetMs, "ls-remote", () => HostGit(repoRelative, lsRemote, Remaining(watch, budgetMs), scratch));
                var advertisement = ControlStep(timing, watch, budgetMs, "live-parse", () => ReadAdvertisement(text));
                var live = new Dictionary<string, string>(StringComparer.Ordinal);
                var changed = new List<string>();
                ControlStep(timing, watch, budgetMs, "compare", () =>
                {
                    foreach (var line in advertisement) live[line.Substring(41)] = line.Substring(0, 40);
                    foreach (var line in expected)
                    {
                        if (line.EndsWith("^{}", StringComparison.Ordinal)) continue;
                        var reference = line.Substring(41);
                        var oldTip = line.Substring(0, 40);
                        var newTip = live.TryGetValue(reference, out var tip) ? tip : "deleted";
                        if (string.Equals(oldTip, newTip, StringComparison.Ordinal)) continue;
                        if (string.Equals(newTip, "deleted", StringComparison.Ordinal) || !reference.StartsWith("refs/heads/", StringComparison.Ordinal))
                            StopWithdrawnRef(repoRelative, reference, oldTip, newTip);
                        changed.Add("+" + reference + ":" + reference);
                    }
                    return 0;
                });
                if (changed.Count > 0)
                {
                    // Only moved dependencies need a graph read: an origin that moves during the control window refuses closed with
                    // step=moved-ref (CARD-1992).
                    ControlStep(timing, watch, budgetMs, "moved-ref", () =>
                    {
                        var common = new[] { "--git-dir", scratch };
                        HostGit(repoRelative, new[] { "init", "-q", "--bare", "--template=", scratch }, Remaining(watch, budgetMs), scratch);
                        var staged = _files.Path.Combine(scratch, "c1986-live.bundle");
                        _files.File.Copy(proofFile + ".bundle", staged, false);
                        var bundleRefs = HostGit(repoRelative, common.Concat(new[] { "bundle", "unbundle", staged }), Remaining(watch, budgetMs), scratch);
                        AssertBundleAdvertisement(bundleRefs, expected);
                        SetScratchRefs(expected, scratch, repoRelative, watch, budgetMs);
                        HostGit(repoRelative, common.Concat(new[] { "fetch", "-q", "--no-tags", "--no-write-fetch-head", "--no-recurse-submodules", originUrl }).Concat(changed),
                            Remaining(watch, budgetMs), scratch);
                        var fetched = ReadAdvertisement(HostGit(repoRelative, common.Concat(new[] { "show-ref", "--dereference" }), Remaining(watch, budgetMs), scratch).Replace(" ", "\t"));
                        var observed = expected.Select(e => (live.TryGetValue(e.Substring(41), out var tip) ? tip : "") + "\t" + e.Substring(41)).ToArray();
                        AssertPublicationRefs(observed, fetched, scratch, repoRelative, watch, budgetMs, exact: true);
                        AssertPublicationRefs(expected, fetched, scratch, repoRelative, watch, budgetMs);
                        var after = ReadAdvertisement(HostGit(repoRelative, lsRemote, Remaining(watch, budgetMs), scratch));
                        AssertPublicationRefs(fetched, after, scratch, repoRelative, watch, budgetMs, exact: true);
                        return 0;
                    });
                }
                timing.Outcome = "passed";
                timing.Step = "complete";
            }
            catch (Exception e) when (!(e is C590Exit))
            {
                var reason = e is C1609Refusal ? e.Message : "";
                LiveProofReason = reason.StartsWith("RecycleGitAuditUnknown", StringComparison.Ordinal) ? "git" : Token.IsMatch(reason) ? reason : "other";
                // A Git child killed at the remaining budget, or the shared remaining-time check, is the control deadline at whichever
                // step was running.
                if (watch.ElapsedMilliseconds >= budgetMs && (LiveProofReason == "git" || LiveProofReason == "deadline")) LiveProofReason = "control-deadline";
                timing.Reason = LiveProofReason;
                if (string.Equals(reason, "withdrawn-ref", StringComparison.Ordinal)) throw new C1609Refusal(OriginMovedDiagnosis);
                throw new C1609Refusal(LsRemoteDiagnosis(repoRelative, 2));
            }
            finally
            {
                // Charged control time ends with the last proof step; cleanup is recorded but never charged and never turns a
                // successful proof into a refusal.
                timing.ElapsedMs = watch.ElapsedMilliseconds;
                try { RemoveLiveScratch(scratch); } catch (Exception) { }
                timing.CleanupMs = watch.ElapsedMilliseconds - timing.ElapsedMs;
                timing.Steps.Add(("cleanup", timing.CleanupMs, "complete"));
            }
        }
    }
}
