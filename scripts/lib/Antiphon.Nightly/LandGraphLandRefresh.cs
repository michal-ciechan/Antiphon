// CARD-1753: refresh the land-graph records for one landing batch, then notify, as an antiphon-scripts command:
//   land-graph-land-refresh --base <sha> [--repo <dir>] [--card <id>] [--note-path <file>]
// It replaced scripts/land-graph-land-refresh.ps1 (deleted 2026-11-23, unit 11 of
// docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md). Run it before scripts/antiphon delegate land. It never asks. A
// bare land-graph-audit --update stays forbidden; --land-refresh on the audit is only for this command. The owner
// document is the land-graph refresh rule of docs/orchestration-loop.md (#land-graph-refresh).
//
// Steps, each refusal one "LAND-GRAPH-NOTICE action=refuse reason=<reason>" line and exit 1: the base must name a commit
// (bad-base); the audit's --check (land-graph-audit in process, its tool output captured, 900 s) exit 0 is
// "action=current" (exit 0); a property or construct the audit cannot approve (property-or-construct), a malformed
// manifest (malformed-manifest) and any other check failure than a record mismatch (unapproved-check) refuse. The
// selected build-definition rows of the base and HEAD (git ls-tree -r -z) give the batch's changed records. A manifest
// that does not match the base warns "action=warn reason=inherited-mismatch" (exit 0) when the batch changed no record,
// and refuses outside-batch when it did. Then a clean worktree (dirty-worktree), the printed pins (the tool's
// --print-pins; pin-printer-missing, pin-printer-failed, invalid-pins), every changed pin owned by a batch record
// (outside-batch), something to refresh (unapproved-check); the batch's pins are staged, --update --land-refresh
// re-records (update-failed restores the staged pins), the three land-graph files are committed (commit-failed), and a
// second --check must pass (check-after-refresh). The notice ("action=refresh", old and new digest, count and pins
// blob, the changed records, "card-note: recorded") goes to stdout and --note-path, and is posted to the card's
// discussion; a failed post appends "card-note: post-failed" and is not a failure. Any other failure is
// "land-graph refresh failed" on stderr, exit 1.
// git and dotnet run on the host's launcher (Watched custody, output captured, 120 s) with the caller's environment and
// GIT_TERMINAL_PROMPT=0; files on IFileSystem; the card note on the HTTP transport. The audit and the pin printer are
// LandGraphRefreshSteps, which a test replaces. The CLI reads ANTIPHON_API and ANTIPHON_TASK_TOKEN and passes them in;
// this core reads no environment. Plain C#: no PowerShell types. ASCII only.
#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Antiphon.Nightly
{
    /// <summary>The land-graph-land-refresh command's options.</summary>
    public sealed class LandGraphLandRefreshOptions
    {
        /// <summary>The CLI's usage line.</summary>
        public const string Usage = "land-graph-land-refresh --base <sha> [--repo <dir>] [--card <id>] [--note-path <file>]";

        private static readonly string[] Valued = { "--base", "--repo", "--card", "--note-path" };

        /// <summary>--base: the landing batch's base commit.</summary>
        public string Base { get; init; } = "";

        /// <summary>--repo: the batch's checkout.</summary>
        public string RepoRoot { get; init; } = "";

        /// <summary>--card: the card whose discussion gets the notice; empty posts nothing.</summary>
        public string Card { get; init; } = "";

        /// <summary>--note-path: a file that also receives the notice (or the refusal line).</summary>
        public string NotePath { get; init; } = "";

        /// <summary>The ANTIPHON_API value the caller read: the board's base URL for the card note.</summary>
        public string Api { get; init; } = "";

        /// <summary>The ANTIPHON_TASK_TOKEN value the caller read: sent as X-Antiphon-Task-Token when set.</summary>
        public string TaskToken { get; init; } = "";

        /// <summary>The caller's environment, which every child gets with GIT_TERMINAL_PROMPT=0. Null: this process's.</summary>
        public IReadOnlyDictionary<string, string> Environment { get; init; }

        /// <summary>The arguments after the command name; NightlyUsageException for a malformed line or a missing --base.</summary>
        public static LandGraphLandRefreshOptions Parse(IReadOnlyList<string> arguments, string defaultRepoRoot, string api, string taskToken,
            IReadOnlyDictionary<string, string> environment)
        {
            var flags = NightlyArgs.Parse(arguments, Valued, Array.Empty<string>());
            return new LandGraphLandRefreshOptions
            {
                Base = flags.Required("--base"),
                RepoRoot = flags.Value("--repo", "") is { Length: > 0 } repo ? repo : defaultRepoRoot ?? "",
                Card = flags.Value("--card", ""),
                NotePath = flags.Value("--note-path", ""),
                Api = api ?? "",
                TaskToken = taskToken ?? "",
                Environment = environment,
            };
        }
    }

    /// <summary>One step's answer: its exit code and its output; Missing when the pin printer could not be resolved.</summary>
    public sealed record LandGraphStep(int Code, string Text, bool Missing = false);

    /// <summary>The refresh's audit and pin printer. A null member is the production step.</summary>
    public sealed class LandGraphRefreshSteps
    {
        /// <summary>
        /// One audit with the options the refresh chose (--check, or --update --land-refresh; output captured, 900 s): its
        /// exit code (124 when it timed out, with no text) and its text. Production: LandGraphAuditRun.Run in this process.
        /// </summary>
        public Func<LandGraphAuditRunOptions, LandGraphStep> Audit { get; init; }

        /// <summary>The pin printer for the repository: the tool's --print-pins rows on stdout; Missing when there is no tool.</summary>
        public Func<string, LandGraphStep> PrintPins { get; init; }
    }

    /// <summary>The land-graph refresh of one landing batch (CARD-1753).</summary>
    public static class LandGraphLandRefresh
    {
        /// <summary>The land-graph commit's subject.</summary>
        public const string CommitSubject = "chore: refresh land-graph records for the landing batch";

        /// <summary>Each audit's wait.</summary>
        public const int AuditTimeoutMilliseconds = 900000;

        /// <summary>Each git and pin-printer wait.</summary>
        public const int ChildTimeoutMilliseconds = 120000;

        /// <summary>
        /// The refresh: lines (the notice or the refusal) to host.Out and NotePath, "land-graph refresh failed" to host.Error.
        /// http posts the card note. steps null (or a null member): the production audit and pin printer. 0 current,
        /// refreshed or warned; 1 refused or failed.
        /// </summary>
        public static int Run(LandGraphLandRefreshOptions options, NightlyHost host, IFileSystem files, NightlyReportHttp http, LandGraphRefreshSteps steps = null)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (host == null) throw new ArgumentNullException(nameof(host));
            if (files == null) throw new ArgumentNullException(nameof(files));
            if (http == null) throw new ArgumentNullException(nameof(http));
            var refresh = new Refresh(options, host, files, http, steps);
            try
            {
                return refresh.Execute();
            }
            catch (RefreshStop stop)
            {
                return stop.Code;
            }
            // The script's whole body was one try: any other failure is the one stderr line.
            catch (Exception)
            {
                host.Error.WriteLine("land-graph refresh failed");
                return 1;
            }
        }

        /// <summary>
        /// Test-DefinitionPath: a build-definition record the land graph selects (any *proj extension, .props, .targets,
        /// solutions and filters, .rsp, Directory.Build.*, *proj.user, global.json, nuget.config, packages.lock.json,
        /// packages.config and Directory.Packages.props).
        /// </summary>
        public static bool IsDefinitionPath(string path)
        {
            var name = Path.GetFileName(path ?? "");
            var extension = Path.GetExtension(path ?? "").ToLowerInvariant();
            if (extension.EndsWith("proj", StringComparison.Ordinal)) return true;
            if (extension == ".props" || extension == ".targets" || extension == ".sln" || extension == ".slnx" || extension == ".slnf" || extension == ".rsp") return true;
            if (name.StartsWith("Directory.Build.", StringComparison.Ordinal)) return true;
            if (name.EndsWith("proj.user", StringComparison.Ordinal)) return true;
            var lower = name.ToLowerInvariant();
            return lower == "global.json" || lower == "nuget.config" || lower == "packages.lock.json" || lower == "packages.config" || lower == "directory.packages.props";
        }

        /// <summary>The selected rows' digest (SHA-256 of the rows, each followed by NUL, upper-case hex) and count.</summary>
        public static (string Digest, int Count) Fingerprint(IReadOnlyList<string> rows)
        {
            rows ??= Array.Empty<string>();
            var payload = string.Join("\0", rows) + "\0";
            return (Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))), rows.Count);
        }

        /// <summary>The JSON string of a card note body: quote, backslash, LF, CR and tab escaped, other control characters a space.</summary>
        public static string JsonString(string value)
        {
            var builder = new StringBuilder("\"");
            foreach (var ch in value ?? "")
            {
                if (ch == '"') builder.Append("\\\"");
                else if (ch == '\\') builder.Append("\\\\");
                else if (ch == '\n') builder.Append("\\n");
                else if (ch == '\r') builder.Append("\\r");
                else if (ch == '\t') builder.Append("\\t");
                else if (ch < 32) builder.Append(' ');
                else builder.Append(ch);
            }
            return builder.Append('"').ToString();
        }

        // One pins file: its lines in order and the hash per path TAB selector; null when a line is malformed or repeated.
        private sealed record PinEntry(string Path, string Selector, string Hash, string Key);

        private sealed record PinFile(IReadOnlyList<PinEntry> Lines, IReadOnlyDictionary<string, string> Map);

        private static PinFile ReadPins(string text)
        {
            var lines = new List<PinEntry>();
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var line in Lf(text).Split('\n'))
            {
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#", StringComparison.Ordinal)) continue;
                var fields = line.Split('\t');
                if (fields.Length != 3 || fields[0].Length == 0 || fields[1].Length == 0 || fields[2].Length != 64) return null;
                var key = fields[0] + "\t" + fields[1];
                if (map.ContainsKey(key)) return null;
                map[key] = fields[2];
                lines.Add(new PinEntry(fields[0], fields[1], fields[2], key));
            }
            return new PinFile(lines, map);
        }

        private static string Lf(string text) => (text ?? "").Replace("\r\n", "\n").Replace("\r", "\n");

        // The answer of one refusal (or of an early success): the code Run returns.
        private sealed class RefreshStop : Exception
        {
            public RefreshStop(int code) : base("land-graph refresh stopped with exit " + code.ToString(CultureInfo.InvariantCulture)) => Code = code;

            public int Code { get; }
        }

        private sealed class Refresh
        {
            private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);
            private readonly LandGraphLandRefreshOptions _options;
            private readonly NightlyHost _host;
            private readonly IFileSystem _files;
            private readonly NightlyReportHttp _http;
            private readonly Func<LandGraphAuditRunOptions, LandGraphStep> _audit;
            private readonly Func<string, LandGraphStep> _printPins;
            private readonly Dictionary<string, string> _environment;
            private string _repo;

            public Refresh(LandGraphLandRefreshOptions options, NightlyHost host, IFileSystem files, NightlyReportHttp http, LandGraphRefreshSteps steps)
            {
                _options = options;
                _host = host;
                _files = files;
                _http = http;
                _audit = steps?.Audit ?? ProductionAudit;
                _printPins = steps?.PrintPins ?? ProductionPins;
                if (options.Environment != null)
                {
                    _environment = new Dictionary<string, string>(StringComparer.Ordinal);
                    foreach (var pair in options.Environment)
                        if (!string.Equals(pair.Key, "GIT_TERMINAL_PROMPT", StringComparison.OrdinalIgnoreCase)) _environment[pair.Key] = pair.Value;
                    _environment["GIT_TERMINAL_PROMPT"] = "0";
                }
            }

            public int Execute()
            {
                _repo = _files.Path.GetFullPath(string.IsNullOrEmpty(_options.RepoRoot) ? "." : _options.RepoRoot);
                var manifestRelative = LandGraphAuditRun.ManifestRelative;
                var evaluationRelative = LandGraphAuditRun.EvaluationRelative;
                var pinsRelative = LandGraphAuditRun.PinsRelative;

                if (Git("rev-parse", "--verify", _options.Base + "^{commit}").Code != 0) Refuse("bad-base");

                var first = _audit(AuditOptions(update: false));
                if (first.Code == 0)
                {
                    WriteOut("LAND-GRAPH-NOTICE action=current\n");
                    throw new RefreshStop(0);
                }
                var checkText = first.Text ?? "";
                if (checkText.Contains("-Update cannot approve it", StringComparison.Ordinal)) Refuse("property-or-construct");
                foreach (var phrase in new[] { "manifest missing", "manifest must be two lines", "manifest digest is not 64 uppercase hex characters", "manifest count is not a decimal integer" })
                    if (checkText.Contains(phrase, StringComparison.Ordinal)) Refuse("malformed-manifest");
                var recordMismatch = new[] { "manifest does not match the selected graph", "(changed pin)", "(missing pin)", "(stale pin)" }
                    .Any(phrase => checkText.Contains(phrase, StringComparison.Ordinal));
                if (!recordMismatch) Refuse("unapproved-check");

                var baseRows = SelectedRows(_options.Base);
                var headRows = SelectedRows("HEAD");
                var baseFingerprint = Fingerprint(baseRows);
                var headFingerprint = Fingerprint(headRows);
                var baseMap = RowMap(baseRows);
                var headMap = RowMap(headRows);
                var changed = new HashSet<string>(StringComparer.Ordinal);
                foreach (var pair in baseMap)
                    if (!headMap.TryGetValue(pair.Key, out var headRow) || !string.Equals(headRow, pair.Value, StringComparison.Ordinal)) changed.Add(pair.Key);
                foreach (var path in headMap.Keys)
                    if (!baseMap.ContainsKey(path)) changed.Add(path);

                var manifestPath = _files.Path.Combine(_repo, manifestRelative);
                if (!_files.File.Exists(manifestPath)) Refuse("malformed-manifest");
                var manifestLines = _files.File.ReadAllLines(manifestPath);
                if (manifestLines.Length < 2) Refuse("malformed-manifest");
                var recordedDigest = manifestLines[0].Trim();
                if (!int.TryParse(manifestLines[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var recordedCount)) Refuse("malformed-manifest");
                var baseMatches = string.Equals(baseFingerprint.Digest, recordedDigest, StringComparison.Ordinal) && baseFingerprint.Count == recordedCount;
                if (!baseMatches)
                {
                    if (changed.Count > 0) Refuse("outside-batch");
                    WriteOut("LAND-GRAPH-NOTICE action=warn reason=inherited-mismatch\n");
                    throw new RefreshStop(0);
                }

                var porcelain = Git("status", "--porcelain");
                if (porcelain.Code != 0 || !string.IsNullOrWhiteSpace(porcelain.Text)) Refuse("dirty-worktree");

                var pinsPath = _files.Path.Combine(_repo, pinsRelative);
                var existingText = _files.File.Exists(pinsPath) ? _files.File.ReadAllText(pinsPath, Utf8) : "";
                var printed = _printPins(_repo);
                if (printed.Missing) Refuse("pin-printer-missing");
                if (printed.Code != 0) Refuse("pin-printer-failed");
                var existingPins = ReadPins(existingText);
                var printedPins = ReadPins(printed.Text ?? "");
                if (existingPins == null || printedPins == null) Refuse("invalid-pins");

                var union = new HashSet<string>(existingPins.Map.Keys, StringComparer.Ordinal);
                union.UnionWith(printedPins.Map.Keys);
                var pinChanged = false;
                foreach (var key in union)
                {
                    var same = existingPins.Map.TryGetValue(key, out var oldHash) && printedPins.Map.TryGetValue(key, out var newHash)
                        && string.Equals(oldHash, newHash, StringComparison.Ordinal);
                    if (same) continue;
                    pinChanged = true;
                    if (!changed.Contains(key.Split('\t')[0])) Refuse("outside-batch");
                }
                if (changed.Count == 0 && !pinChanged) Refuse("unapproved-check");

                byte[] originalPins = null;
                var wrotePins = false;
                if (pinChanged)
                {
                    var builder = new StringBuilder();
                    foreach (var entry in existingPins.Lines)
                    {
                        if (changed.Contains(entry.Path)) continue;
                        builder.Append(entry.Path).Append('\t').Append(entry.Selector).Append('\t').Append(entry.Hash).Append('\n');
                    }
                    var batchLines = printedPins.Lines.Where(entry => changed.Contains(entry.Path)).Select(entry => entry.Path + "\t" + entry.Selector + "\t" + entry.Hash).ToArray();
                    Array.Sort(batchLines, StringComparer.Ordinal);
                    foreach (var line in batchLines) builder.Append(line).Append('\n');
                    var proposed = builder.ToString();
                    if (!string.Equals(proposed, Lf(existingText), StringComparison.Ordinal))
                    {
                        if (_files.File.Exists(pinsPath)) originalPins = _files.File.ReadAllBytes(pinsPath);
                        _files.File.WriteAllText(pinsPath, proposed, Utf8);
                        wrotePins = true;
                    }
                }

                var oldPins = Git("rev-parse", "HEAD:" + pinsRelative);
                if (oldPins.Code != 0) Refuse("unapproved-check");

                var update = _audit(AuditOptions(update: true));
                if (update.Code != 0)
                {
                    if (wrotePins && originalPins != null) _files.File.WriteAllBytes(pinsPath, originalPins);
                    Refuse("update-failed");
                }

                if (Git("commit", "-m", CommitSubject, "--", manifestRelative, evaluationRelative, pinsRelative).Code != 0) Refuse("commit-failed");

                if (_audit(AuditOptions(update: false)).Code != 0) Refuse("check-after-refresh");

                var newPins = Git("rev-parse", "HEAD:" + pinsRelative);
                if (newPins.Code != 0) Refuse("unapproved-check");
                var changedPaths = changed.ToArray();
                Array.Sort(changedPaths, StringComparer.Ordinal);
                var notice = new StringBuilder();
                notice.Append("LAND-GRAPH-NOTICE action=refresh\n");
                notice.Append("old-digest=").Append(recordedDigest).Append('\n');
                notice.Append("new-digest=").Append(headFingerprint.Digest).Append('\n');
                notice.Append("old-count=").Append(recordedCount.ToString(CultureInfo.InvariantCulture)).Append('\n');
                notice.Append("new-count=").Append(headFingerprint.Count.ToString(CultureInfo.InvariantCulture)).Append('\n');
                notice.Append("old-pins=").Append(oldPins.Text.Trim()).Append('\n');
                notice.Append("new-pins=").Append(newPins.Text.Trim()).Append('\n');
                foreach (var path in changedPaths) notice.Append("changed-records:").Append(path).Append('\n');
                notice.Append("card-note: recorded\n");
                var noticeText = notice.ToString();
                if (!SendCardNote(noticeText)) noticeText += "card-note: post-failed\n";
                WriteOut(noticeText);
                return 0;
            }

            private void WriteOut(string text)
            {
                _host.Out.Write(text);
                if (string.IsNullOrWhiteSpace(_options.NotePath)) return;
                var parent = _files.Path.GetDirectoryName(_options.NotePath);
                if (!string.IsNullOrEmpty(parent)) _files.Directory.CreateDirectory(parent);
                _files.File.WriteAllText(_options.NotePath, text, Utf8);
            }

            private void Refuse(string reason)
            {
                WriteOut("LAND-GRAPH-NOTICE action=refuse reason=" + reason + "\n");
                throw new RefreshStop(1);
            }

            // One git call: its exit code (124 when it timed out) and its stdout.
            private LandGraphStep Git(params string[] arguments)
            {
                var run = _host.Launch(new NightlyLaunch("git", arguments, _repo, TimeoutMilliseconds: ChildTimeoutMilliseconds, Environment: _environment, CaptureOutput: true));
                return run.TimedOut ? new LandGraphStep(124, "") : new LandGraphStep(run.ExitCode, run.StandardOutputText ?? "");
            }

            // Get-SelectedRows: the tree's ls-tree rows whose path is a build definition, ordinal sorted.
            private string[] SelectedRows(string tree)
            {
                var run = Git("ls-tree", "-r", "-z", tree);
                if (run.Code != 0) Refuse("unapproved-check");
                var rows = new List<string>();
                foreach (var row in run.Text.Split('\0'))
                {
                    if (string.IsNullOrEmpty(row)) continue;
                    var tab = row.IndexOf('\t');
                    if (tab < 1) continue;
                    if (IsDefinitionPath(row.Substring(tab + 1))) rows.Add(row);
                }
                var array = rows.ToArray();
                Array.Sort(array, StringComparer.Ordinal);
                return array;
            }

            private static Dictionary<string, string> RowMap(IEnumerable<string> rows)
            {
                var map = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var row in rows)
                {
                    var tab = row.IndexOf('\t');
                    if (tab < 1) continue;
                    map[row.Substring(tab + 1)] = row;
                }
                return map;
            }

            // Send-CardNote: true without a card (nothing to post); false without an API or when the post fails.
            private bool SendCardNote(string body)
            {
                if (string.IsNullOrWhiteSpace(_options.Card)) return true;
                if (string.IsNullOrWhiteSpace(_options.Api)) return false;
                var headers = new Dictionary<string, string>(StringComparer.Ordinal);
                if (!string.IsNullOrEmpty(_options.TaskToken)) headers["X-Antiphon-Task-Token"] = _options.TaskToken;
                try
                {
                    var response = _http("POST", _options.Api.TrimEnd('/') + "/api/cards/" + _options.Card + "/discussion", headers, "{\"body\":" + JsonString(body) + "}");
                    return response != null && response.Status >= 200 && response.Status < 300;
                }
                // A failed note is not a failure of the refresh: any transport error is post-failed.
                catch (Exception)
                {
                    return false;
                }
            }

            // The audit the refresh runs: --check, or --update --land-refresh (the pins it staged may differ from HEAD), on
            // this repository, the tool's output captured and bounded by the audit's wait.
            private LandGraphAuditRunOptions AuditOptions(bool update) => new LandGraphAuditRunOptions
            {
                RepoRoot = _repo,
                Check = !update,
                Update = update,
                LandRefresh = update,
                Environment = _environment,
                CaptureOutput = true,
                TimeoutMilliseconds = AuditTimeoutMilliseconds,
            };

            // The audit in this process (land-graph-audit), its tool's output and its own lines captured; a timeout is 124
            // with no text.
            private LandGraphStep ProductionAudit(LandGraphAuditRunOptions options)
            {
                using var capture = new StringWriter(CultureInfo.InvariantCulture);
                var captured = new NightlyHost
                {
                    Clock = _host.Clock,
                    Git = _host.Git,
                    Launcher = _host.Launcher,
                    Processes = _host.Processes,
                    Probe = _host.Probe,
                    Out = capture,
                    Error = capture,
                    Trace = _host.Trace,
                    Variables = _host.Variables,
                    Sleep = _host.Sleep,
                    Qualification = _host.Qualification,
                };
                var result = LandGraphAuditRun.Run(options, captured, _files);
                return result.TimedOut ? new LandGraphStep(124, "") : new LandGraphStep(result.ExitCode, capture.ToString());
            }

            // Get-PinText: the default tool (rebuilt when stale) prints the repository's target pins.
            private LandGraphStep ProductionPins(string repo)
            {
                var tool = LandAuditTool.Resolve(repo, "", noRebuild: false, _host, _files);
                if (tool.Error != null) return new LandGraphStep(1, "", Missing: true);
                var run = _host.Launch(new NightlyLaunch("dotnet", new[] { "exec", tool.Path, "--print-pins", "--repo", repo }, repo,
                    TimeoutMilliseconds: ChildTimeoutMilliseconds, Environment: _environment, CaptureOutput: true));
                return run.TimedOut ? new LandGraphStep(124, "") : new LandGraphStep(run.ExitCode, run.StandardOutputText ?? "");
            }
        }
    }
}
