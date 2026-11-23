// Reports (default) or deletes clearly attributable Codex test residue. It replaced the body of
// scripts/cleanup-codex-test-residue.ps1 (unit 21 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md), which stays as
// the thin launcher of the Windmill schedule u/lndcobra/antiphon_codex_residue_cleanup (Monday 09:30 Europe/London; report-only
// until the owner changes the rollout; never a Windows Scheduled Task); the CLI runs it as `antiphon-scripts
// cleanup-codex-test-residue`.
//
// The state_5.sqlite reader is read-only (python's sqlite3 with mode=ro, which still sees WAL-committed changes); the only
// mutation is `codex delete --force <thread-id>`, and only with --execute. --max-per-run refuses (exit 4) rather than truncates.
// --input-json classifies a saved threads dump and forces --execute off. The report (logs/codex-test-residue/<stamp>.json
// unless --report-path) is built from an allow-list of fields and never dumps the raw rows. A row is a candidate only when every
// rule passes: a fixed test cwd under the temp root (or the synthetic stub provider), source cli or exec, strictly older than
// --older-than-hours, no live codex cwd (an explicit --cd on a live codex command line, or --live-cwds-json) or writer lock
// (thread-writer-locks, or --lock-ids-json), not archived or pinned; vscode rows, rows outside the temp root and protected
// rollouts never are. Ad-hoc agent probes are reported apart (UNATTRIBUTED) for a human decision. Exit 0 done; 1 the threads
// could not be read; 4 over the cap; 5 a delete failed; usage errors are NightlyUsageException (the CLI's exit 2). The
// reader, the live command lines and the delete run on ICodexResidueSeams (production: CodexTestResidue.Production). Plain C#:
// no PowerShell types. ASCII only.
#nullable disable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO.Abstractions;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Antiphon.Nightly
{
    /// <summary>The cleanup-codex-test-residue command's options (cleanup-codex-test-residue.ps1's parameters, in kebab case).</summary>
    public sealed class CodexTestResidueOptions
    {
        /// <summary>The CLI's usage line.</summary>
        public const string Usage = "cleanup-codex-test-residue [--older-than-hours 24] [--execute] [--max-per-run 10] [--report-path <dir>] [--input-json <file>]"
            + " [--codex-home <dir>] [--state-db-path <file>] [--codex-exe codex] [--live-cwds-json <file>] [--lock-ids-json <file>] [--now <timestamp>]";

        /// <summary>A thread must be strictly older than this many hours.</summary>
        public int OlderThanHours { get; init; } = 24;

        /// <summary>Delete the candidates (absent: report only).</summary>
        public bool Execute { get; init; }

        /// <summary>More candidates than this refuses the delete.</summary>
        public int MaxPerRun { get; init; } = 10;

        /// <summary>The report directory.</summary>
        public string ReportPath { get; init; } = "";

        /// <summary>A saved threads dump to classify instead of the database; forces Execute off.</summary>
        public string InputJson { get; init; } = "";

        /// <summary>The Codex state root.</summary>
        public string CodexHome { get; init; } = "";

        /// <summary>The threads database.</summary>
        public string StateDbPath { get; init; } = "";

        /// <summary>The Codex executable the delete runs.</summary>
        public string CodexExe { get; init; } = "codex";

        /// <summary>A JSON array of live codex cwds instead of the live command lines; empty: read them.</summary>
        public string LiveCwdsJson { get; init; } = "";

        /// <summary>A JSON array of writer-locked thread ids instead of thread-writer-locks; empty: read them.</summary>
        public string LockIdsJson { get; init; } = "";

        /// <summary>The time the ages are measured from (UTC); null: the host's clock.</summary>
        public DateTime? Now { get; init; }

        /// <summary>The temp root the test cwds live under (Path.GetTempPath()).</summary>
        public string TempRoot { get; init; } = "";

        /// <summary>
        /// The arguments after the command name. codexHomeVariable (CODEX_HOME) and userProfile choose the default Codex home,
        /// repoRoot the default report directory (logs/codex-test-residue), tempRoot the temp root; the CLI passes them.
        /// </summary>
        public static CodexTestResidueOptions Parse(IReadOnlyList<string> arguments, string codexHomeVariable, string userProfile, string repoRoot, string tempRoot)
        {
            var flags = NightlyArgs.Parse(arguments,
                new[] { "--older-than-hours", "--max-per-run", "--report-path", "--input-json", "--codex-home", "--state-db-path", "--codex-exe", "--live-cwds-json", "--lock-ids-json", "--now" },
                new[] { "--execute" });
            var older = flags.Int("--older-than-hours", 24);
            var cap = flags.Int("--max-per-run", 10);
            if (older < 0) throw new NightlyUsageException("--older-than-hours must be non-negative.");
            if (cap < 1) throw new NightlyUsageException("--max-per-run must be at least 1.");
            var home = flags.Value("--codex-home", "");
            if (string.IsNullOrEmpty(home)) home = codexHomeVariable;
            if (string.IsNullOrEmpty(home)) home = System.IO.Path.Combine(userProfile ?? "", ".codex");
            home = System.IO.Path.GetFullPath(home);
            var database = flags.Value("--state-db-path", "");
            var report = flags.Value("--report-path", "");
            DateTime? now = null;
            var nowText = flags.Value("--now", "");
            if (nowText.Length > 0)
            {
                now = CodexTestResidue.UtcDate(JsonValue.Create(nowText));
                if (now == null) throw new NightlyUsageException("Could not parse --now '" + nowText + "' as a timestamp.");
            }
            var exe = flags.Value("--codex-exe", "");
            return new CodexTestResidueOptions
            {
                OlderThanHours = older,
                Execute = flags.Switch("--execute"),
                MaxPerRun = cap,
                ReportPath = report.Length > 0 ? report : System.IO.Path.Combine(repoRoot ?? "", "logs", "codex-test-residue"),
                InputJson = flags.Value("--input-json", ""),
                CodexHome = home,
                StateDbPath = database.Length > 0 ? database : System.IO.Path.Combine(home, "state_5.sqlite"),
                CodexExe = exe.Length > 0 ? exe : "codex",
                LiveCwdsJson = flags.Value("--live-cwds-json", ""),
                LockIdsJson = flags.Value("--lock-ids-json", ""),
                Now = now,
                TempRoot = tempRoot ?? "",
            };
        }
    }

    /// <summary>The machine the cleanup reads and, with --execute, changes.</summary>
    public interface ICodexResidueSeams
    {
        /// <summary>The threads table of the database, read-only, as a JSON array of rows; throws when it cannot be read.</summary>
        string ReadThreads(string stateDbPath);

        /// <summary>The command lines of the live codex processes.</summary>
        IReadOnlyList<string> CodexCommandLines();

        /// <summary>Runs `codexExe delete --force id` and returns its output lines (stdout and stderr).</summary>
        IReadOnlyList<string> Delete(string codexExe, string id);
    }

    /// <summary>One delete the run attempted.</summary>
    public sealed record CodexResidueMutation(string Id, string Action, bool Ok, string DeletedLine);

    /// <summary>What a run found and did (the script's -PassThru record).</summary>
    public sealed class CodexTestResidueResult
    {
        /// <summary>The exit code.</summary>
        public int ExitCode { get; init; }

        /// <summary>The report file; null when the run stopped before choosing one.</summary>
        public string ReportPath { get; init; }

        /// <summary>The candidates.</summary>
        public int CandidateCount { get; init; }

        /// <summary>The rows skipped.</summary>
        public int SkipCount { get; init; }

        /// <summary>The ad-hoc probe rows.</summary>
        public int UnattributedCount { get; init; }

        /// <summary>The candidates' ids, oldest first.</summary>
        public IReadOnlyList<string> CandidateIds { get; init; } = Array.Empty<string>();

        /// <summary>The probe rows' ids, oldest first.</summary>
        public IReadOnlyList<string> UnattributedIds { get; init; } = Array.Empty<string>();

        /// <summary>The deletes attempted.</summary>
        public IReadOnlyList<CodexResidueMutation> Mutations { get; init; } = Array.Empty<CodexResidueMutation>();
    }

    /// <summary>The Codex test residue cleanup (cleanup-codex-test-residue.ps1).</summary>
    public static class CodexTestResidue
    {
        /// <summary>The read-only reader python runs (`python -c Reader &lt;db&gt;`): mode=ro still observes WAL-committed changes.</summary>
        public const string Reader = @"import json, pathlib, sqlite3, sys
path = pathlib.Path(sys.argv[1]).resolve()
uri = path.as_uri() + '?mode=ro'
con = sqlite3.connect(uri, uri=True)
try:
    columns = {r[1] for r in con.execute('PRAGMA table_info(threads)')}
    wanted = ['id', 'cwd', 'source', 'model_provider', 'created_at', 'rollout_path', 'archived', 'is_pinned', 'first_user_message']
    select = [c if c in columns else 'NULL AS ' + c for c in wanted]
    rows = [dict(zip(wanted, row)) for row in con.execute('SELECT ' + ', '.join(select) + ' FROM threads')]
    print(json.dumps(rows))
finally:
    con.close()
";

        private static readonly Regex[] ProbePatterns =
        {
            new Regex(@"^claude\\.+\\scratchpad(?:\\|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
            new Regex(@"^codex-tui-probe-[^\\]*(?:\\|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
            new Regex(@"^codexprobe[^\\]*(?:\\|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
            new Regex(@"^cx0108-probe[^\\]*(?:\\|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
        };

        private static readonly Regex[] TestCwdPatterns =
        {
            new Regex(@"^antiphon-codex-canary[^\\]*(?:\\|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
            new Regex(@"^antiphon-codex-roundtrip[^\\]*(?:\\|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
            new Regex(@"^codex-brunner-[^\\]+\\cwd$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
        };

        private static readonly Regex ProtectedRollout = new Regex(@"(^|\\)(remote_control_enrollments|thread_sections|archived_sessions)(\\|$)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly Regex QuotedCd = new Regex("(?:^|\\s)--cd\\s+\"([^\"]+)\"", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly Regex BareCd = new Regex(@"(?:^|\s)--cd\s+([^\s]+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly Regex LockName = new Regex("[0-9a-f]{8}-[0-9a-f-]{27,}", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly Regex DeletedSession = new Regex(@"^Deleted session\b", RegexOptions.CultureInvariant);

        /// <summary>The production seams: python on the host's launcher, the live codex processes' command lines, codex delete.</summary>
        public static ICodexResidueSeams Production(NightlyHost host) => new CodexResidueOs(host ?? throw new ArgumentNullException(nameof(host)));

        /// <summary>Classifies the threads, prints the sections on host.Out, deletes with --execute and writes the report.</summary>
        public static CodexTestResidueResult Run(CodexTestResidueOptions options, NightlyHost host, IFileSystem files, ICodexResidueSeams seams)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (host == null) throw new ArgumentNullException(nameof(host));
            if (files == null) throw new ArgumentNullException(nameof(files));
            if (seams == null) throw new ArgumentNullException(nameof(seams));
            return new Pass(options, host, files, seams).Go();
        }

        /// <summary>ConvertTo-UtcDate: an epoch (seconds or milliseconds) or a timestamp (a zone-less one is UTC); null otherwise.</summary>
        public static DateTime? UtcDate(JsonNode value)
        {
            if (value == null) return null;
            var text = Str(value);
            if (text.Length == 0) return null;
            if (long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var number))
            {
                var epoch = DateTime.SpecifyKind(new DateTime(1970, 1, 1), DateTimeKind.Utc);
                if (number > 100000000000) return epoch.AddMilliseconds(number);
                if (number > 1000000000) return epoch.AddSeconds(number);
            }
            if (value is JsonValue json && json.GetValueKind() != JsonValueKind.String) return null;
            return DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed) ? parsed.UtcDateTime : (DateTime?)null;
        }

        // [string]$value: a string as is, a boolean True or False, a number its JSON text, null empty.
        internal static string Str(JsonNode node)
        {
            if (node == null) return "";
            if (node is JsonValue value)
            {
                switch (value.GetValueKind())
                {
                    case JsonValueKind.String: return value.GetValue<string>();
                    case JsonValueKind.True: return "True";
                    case JsonValueKind.False: return "False";
                    case JsonValueKind.Null: return "";
                }
            }
            return node.ToJsonString();
        }

        // Normalize-PathText: trimmed, without the \\?\ prefix or a trailing separator.
        internal static string Normalize(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return "";
            var value = path.Trim();
            if (value.StartsWith(@"\\?\", StringComparison.Ordinal)) value = value.Substring(4);
            return value.TrimEnd('\\', '/');
        }

        // Test-PathUnder: the root itself or below it (a backslash separator), ignoring case.
        internal static bool Under(string path, string root)
        {
            var normal = Normalize(path);
            var rootText = Normalize(root) + "\\";
            return normal.StartsWith(rootText, StringComparison.OrdinalIgnoreCase) || normal.Equals(rootText.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
        }

        private static bool Bool(JsonNode node)
        {
            if (node == null) return false;
            if (node is JsonValue value && value.GetValueKind() == JsonValueKind.True) return true;
            if (node is JsonValue other && other.GetValueKind() == JsonValueKind.False) return false;
            var text = Str(node).Trim();
            return text == "1" || string.Equals(text, "true", StringComparison.OrdinalIgnoreCase);
        }

        private static bool Truthy(JsonNode node)
        {
            if (node == null) return false;
            if (node is JsonValue value)
            {
                switch (value.GetValueKind())
                {
                    case JsonValueKind.String: return value.GetValue<string>().Length > 0;
                    case JsonValueKind.True: return true;
                    case JsonValueKind.False:
                    case JsonValueKind.Null: return false;
                    case JsonValueKind.Number: return value.GetValue<double>() != 0;
                }
            }
            return true;
        }

        private static string Relative(string cwd, string tempRoot)
        {
            var root = Normalize(tempRoot);
            var normal = Normalize(cwd);
            if (!Under(normal, root)) return null;
            return normal.Substring(root.Length).TrimStart('\\');
        }

        private static JsonNode Field(JsonNode row, string name) => row is JsonObject fields ? fields[name] : null;

        private sealed class Entry
        {
            public string Id;
            public string Cwd;
            public string Source;
            public string ModelProvider;
            public string CreatedAt;
            public double AgeHours;
            public string RolloutPath;
            public bool RolloutExists;
            public bool Archived;
            public bool Pinned;
            public string Reason;
            public bool Candidate;
            public string FirstUserMessage;
            public bool HasMessage;

            public JsonObject ToJson()
            {
                var json = new JsonObject
                {
                    ["id"] = Id,
                    ["cwd"] = Cwd,
                    ["source"] = Source,
                    ["modelProvider"] = ModelProvider,
                    ["createdAt"] = CreatedAt,
                    ["ageHours"] = AgeHours,
                    ["rolloutPath"] = RolloutPath,
                    ["rolloutExists"] = RolloutExists,
                    ["archived"] = Archived,
                    ["pinned"] = Pinned,
                    ["reason"] = Reason,
                    ["candidate"] = Candidate,
                };
                if (HasMessage) json["firstUserMessage"] = FirstUserMessage;
                return json;
            }
        }

        private sealed class Pass
        {
            private readonly CodexTestResidueOptions _options;
            private readonly NightlyHost _host;
            private readonly IFileSystem _files;
            private readonly ICodexResidueSeams _seams;
            private readonly List<Entry> _candidates = new List<Entry>();
            private readonly List<Entry> _skipped = new List<Entry>();
            private readonly List<Entry> _unattributed = new List<Entry>();
            private readonly List<CodexResidueMutation> _mutations = new List<CodexResidueMutation>();
            private bool _execute;
            private DateTime _now;
            private int _threadCount;
            private int _liveCwdCount;
            private int _lockIdCount;
            private string _reportFile;

            public Pass(CodexTestResidueOptions options, NightlyHost host, IFileSystem files, ICodexResidueSeams seams)
            {
                _options = options;
                _host = host;
                _files = files;
                _seams = seams;
            }

            public CodexTestResidueResult Go()
            {
                _execute = _options.Execute;
                if (_options.InputJson.Length > 0 && _execute)
                {
                    _host.Out.WriteLine("--input-json is set; --execute is ignored (dry run only).");
                    _execute = false;
                }
                _now = _options.Now ?? _host.UtcNow;
                if (!_files.Directory.Exists(_options.ReportPath)) _files.Directory.CreateDirectory(_options.ReportPath);
                _reportFile = _files.Path.Combine(_options.ReportPath, _host.UtcNow.ToString("yyyy-MM-dd-HHmmss", CultureInfo.InvariantCulture) + ".json");

                IReadOnlyList<JsonNode> rows;
                try
                {
                    rows = Rows();
                }
                catch (Exception e) when (e is InvalidOperationException || e is JsonException || e is System.IO.IOException || e is UnauthorizedAccessException
                    || e is System.ComponentModel.Win32Exception)
                {
                    _host.Out.WriteLine(e.Message);
                    return Complete(1);
                }
                _threadCount = rows.Count;
                HashSet<string> live;
                HashSet<string> locks;
                try
                {
                    live = LiveCwds();
                    locks = LockIds();
                }
                catch (Exception e) when (e is InvalidOperationException || e is JsonException || e is System.IO.IOException || e is UnauthorizedAccessException)
                {
                    // The script stopped here without a report.
                    _host.Error.WriteLine(e.Message);
                    return new CodexTestResidueResult { ExitCode = 1 };
                }
                _liveCwdCount = live.Count;
                _lockIdCount = locks.Count;
                Classify(rows, live, locks);

                _host.Out.WriteLine("=== CANDIDATES (" + _candidates.Count + ") ===");
                foreach (var row in _candidates)
                    _host.Out.WriteLine(row.Id + "  " + row.Source + "  " + row.Cwd + "  age=" + row.AgeHours.ToString(CultureInfo.InvariantCulture) + "h");
                _host.Out.WriteLine("=== UNATTRIBUTED (" + _unattributed.Count + ") ===");
                foreach (var row in _unattributed) _host.Out.WriteLine(row.Id + "  " + row.Cwd + "  " + row.FirstUserMessage);
                _host.Out.WriteLine("=== SKIPPED (" + _skipped.Count + ") ===");
                _host.Out.WriteLine("candidates=" + _candidates.Count + " unattributed=" + _unattributed.Count + " skipped=" + _skipped.Count + " execute=" + (_execute ? "True" : "False"));

                if (_execute)
                {
                    if (_candidates.Count > _options.MaxPerRun)
                    {
                        _host.Out.WriteLine("Refusing to delete " + _candidates.Count + " candidates; --max-per-run is " + _options.MaxPerRun + ". The cap refuses, it does not truncate.");
                        return Complete(4);
                    }
                    var failures = 0;
                    foreach (var row in _candidates)
                    {
                        try
                        {
                            var output = _seams.Delete(_options.CodexExe, row.Id) ?? Array.Empty<string>();
                            var text = string.Join(Environment.NewLine, output.Select(line => line ?? ""));
                            var deleted = Regex.Split(text, "\r?\n").FirstOrDefault(line => DeletedSession.IsMatch(line));
                            var ok = deleted != null;
                            if (ok && _options.InputJson.Length == 0 && Rows().Any(remaining => Str(Field(remaining, "id")) == row.Id))
                            {
                                ok = false;
                                text = "Deleted session line was present but threads row remained.";
                            }
                            _mutations.Add(new CodexResidueMutation(row.Id, "delete", ok, deleted ?? ""));
                            if (ok) _host.Out.WriteLine("delete " + row.Id + " ok");
                            else
                            {
                                failures++;
                                _host.Out.WriteLine("delete " + row.Id + " FAILED " + text.Substring(0, Math.Min(180, text.Length)));
                            }
                        }
                        catch (Exception e)
                        {
                            failures++;
                            _mutations.Add(new CodexResidueMutation(row.Id, "delete", false, ""));
                            _host.Out.WriteLine("delete " + row.Id + " FAILED " + e.Message);
                        }
                    }
                    if (failures > 0) return Complete(5);
                }
                return Complete(0);
            }

            // Get-ThreadRows.
            private IReadOnlyList<JsonNode> Rows()
            {
                if (_options.InputJson.Length > 0)
                {
                    if (!_files.File.Exists(_options.InputJson)) throw new InvalidOperationException("--input-json not found: " + _options.InputJson);
                    var raw = JsonNode.Parse(_files.File.ReadAllText(_options.InputJson));
                    if (raw == null) return Array.Empty<JsonNode>();
                    if (raw is JsonArray array) return array.ToArray();
                    if (raw is JsonObject obj)
                    {
                        if (obj.ContainsKey("threads")) return Items(obj["threads"]);
                        if (obj.ContainsKey("data")) return Items(obj["data"]);
                    }
                    return new[] { raw };
                }
                if (!_files.File.Exists(_options.StateDbPath)) throw new InvalidOperationException("state_5.sqlite not found: " + _options.StateDbPath);
                var text = (_seams.ReadThreads(_options.StateDbPath) ?? "").Trim();
                if (text.Length == 0 || text == "[]") return Array.Empty<JsonNode>();
                return Items(JsonNode.Parse(text));
            }

            private static IReadOnlyList<JsonNode> Items(JsonNode node) => node is JsonArray array ? array.ToArray() : new[] { node };

            // Get-LiveCodexCwds: only an explicit --cd is positive evidence of a live cwd; no guessed cwd makes a row eligible.
            private HashSet<string> LiveCwds()
            {
                var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (_options.LiveCwdsJson.Length > 0)
                {
                    if (!_files.File.Exists(_options.LiveCwdsJson)) throw new InvalidOperationException("--live-cwds-json not found: " + _options.LiveCwdsJson);
                    foreach (var cwd in Items(JsonNode.Parse(_files.File.ReadAllText(_options.LiveCwdsJson))))
                    {
                        var normal = Normalize(Str(cwd));
                        if (normal.Length > 0) set.Add(normal);
                    }
                    return set;
                }
                foreach (var command in _seams.CodexCommandLines() ?? Array.Empty<string>())
                {
                    var quoted = QuotedCd.Match(command ?? "");
                    if (quoted.Success) set.Add(Normalize(quoted.Groups[1].Value));
                    else
                    {
                        var bare = BareCd.Match(command ?? "");
                        if (bare.Success) set.Add(Normalize(bare.Groups[1].Value));
                    }
                }
                return set;
            }

            // Get-WriterLockIds.
            private HashSet<string> LockIds()
            {
                var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (_options.LockIdsJson.Length > 0)
                {
                    if (!_files.File.Exists(_options.LockIdsJson)) throw new InvalidOperationException("--lock-ids-json not found: " + _options.LockIdsJson);
                    foreach (var id in Items(JsonNode.Parse(_files.File.ReadAllText(_options.LockIdsJson))))
                    {
                        var text = Str(id);
                        if (text.Length > 0) set.Add(text);
                    }
                    return set;
                }
                var lockRoot = _files.Path.Combine(_options.CodexHome, "thread-writer-locks");
                if (_files.Directory.Exists(lockRoot))
                {
                    foreach (var file in _files.Directory.EnumerateFiles(lockRoot, "*", System.IO.SearchOption.AllDirectories))
                    {
                        var match = LockName.Match(_files.Path.GetFileName(file));
                        if (match.Success) set.Add(match.Value);
                    }
                }
                return set;
            }

            private void Classify(IReadOnlyList<JsonNode> rows, HashSet<string> live, HashSet<string> locks)
            {
                var tempRoot = Normalize(_options.TempRoot);
                foreach (var row in rows)
                {
                    if (row == null) continue;
                    var id = Str(Field(row, "id"));
                    var cwd = Normalize(Str(Field(row, "cwd")));
                    var source = Str(Field(row, "source")).ToLowerInvariant();
                    var provider = Str(Field(row, "model_provider")).ToLowerInvariant();
                    var rollout = Normalize(Str(Field(row, "rollout_path")));
                    var created = UtcDate(Field(row, "created_at"));
                    var age = created == null ? -1.0 : (_now - created.Value).TotalHours;

                    // Ad-hoc probes typed by agent sessions: reported for a human decision, never a candidate.
                    var relative = Relative(cwd, tempRoot);
                    if (relative != null && ProbePatterns.Any(pattern => pattern.IsMatch(relative)))
                    {
                        _unattributed.Add(Allowlisted(row, age, "UNATTRIBUTED-PROBE", false, true));
                        continue;
                    }

                    var reasons = new List<string>();
                    // C1: only the fixed test cwd prefixes, or the synthetic-only stub provider.
                    var testCwd = relative != null && TestCwdPatterns.Any(pattern => pattern.IsMatch(relative));
                    if (!(testCwd || provider == "stub")) reasons.Add("not-test-cwd-or-stub");
                    // C2: desktop is always the user, never a test candidate.
                    if (source != "cli" && source != "exec") reasons.Add("source=" + source);
                    // C3: strictly older than the age gate.
                    if (created == null) reasons.Add("no-created-at");
                    else if (age <= _options.OlderThanHours) reasons.Add("fresh");
                    // C4: an exact known live cwd or writer lock is an exclusion.
                    if (cwd.Length > 0 && live.Contains(cwd)) reasons.Add("LIVE-CODEX-CWD");
                    if (id.Length > 0 && locks.Contains(id)) reasons.Add("THREAD-WRITER-LOCK");
                    // C5: archive and pin are human decisions.
                    if (Bool(Field(row, "archived"))) reasons.Add("archived");
                    if (Bool(Field(row, "is_pinned"))) reasons.Add("pinned");
                    // Never-candidate construction: these duplicate C1/C2 so a future broadening of the allow-list fails closed.
                    if (source == "vscode") reasons.Add("VSCODE-NEVER");
                    if (!Under(cwd, tempRoot) && provider != "stub") reasons.Add("outside-temp-NEVER");
                    if (ProtectedRollout.IsMatch(rollout)) reasons.Add("protected-rollout-NEVER");

                    var candidate = reasons.Count == 0;
                    var entry = Allowlisted(row, age, string.Join(", ", reasons), candidate, false);
                    (candidate ? _candidates : _skipped).Add(entry);
                }
                Sort(_candidates);
                Sort(_skipped);
                Sort(_unattributed);
            }

            // Sort-Object createdAt (stable).
            private static void Sort(List<Entry> entries)
            {
                var sorted = entries.OrderBy(entry => entry.CreatedAt, StringComparer.OrdinalIgnoreCase).ToList();
                entries.Clear();
                entries.AddRange(sorted);
            }

            // Get-AllowlistedRow: the allow-listed fields only; the first user message only for a probe.
            private Entry Allowlisted(JsonNode row, double age, string reason, bool candidate, bool includeMessage)
            {
                var created = Field(row, "created_at");
                var date = UtcDate(created);
                var rollout = Field(row, "rollout_path");
                var rolloutPath = Str(rollout);
                return new Entry
                {
                    Id = Str(Field(row, "id")),
                    Cwd = Normalize(Str(Field(row, "cwd"))),
                    Source = Str(Field(row, "source")),
                    ModelProvider = Str(Field(row, "model_provider")),
                    CreatedAt = date == null ? Str(created) : date.Value.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
                    AgeHours = Math.Round(age, 3),
                    RolloutPath = Normalize(rolloutPath),
                    RolloutExists = Truthy(rollout) && (_files.File.Exists(rolloutPath) || _files.Directory.Exists(rolloutPath)),
                    Archived = Bool(Field(row, "archived")),
                    Pinned = Bool(Field(row, "is_pinned")),
                    Reason = reason,
                    Candidate = candidate,
                    FirstUserMessage = includeMessage ? Str(Field(row, "first_user_message")) : null,
                    HasMessage = includeMessage,
                };
            }

            // Complete-Run: the report (a failure to write it is a warning), then the result.
            private CodexTestResidueResult Complete(int code)
            {
                if (_reportFile != null)
                {
                    try
                    {
                        var report = new JsonObject
                        {
                            ["generatedAt"] = _host.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                            ["now"] = _now.ToString("o", CultureInfo.InvariantCulture),
                            ["olderThanHours"] = _options.OlderThanHours,
                            ["execute"] = _execute,
                            ["maxPerRun"] = _options.MaxPerRun,
                            ["inputJson"] = _options.InputJson.Length > 0,
                            ["threadCount"] = _threadCount,
                            ["liveCwdCount"] = _liveCwdCount,
                            ["writerLockCount"] = _lockIdCount,
                            ["candidateCount"] = _candidates.Count,
                            ["skipCount"] = _skipped.Count,
                            ["unattributedCount"] = _unattributed.Count,
                            ["candidates"] = new JsonArray(_candidates.Select(entry => (JsonNode)entry.ToJson()).ToArray()),
                            ["skipped"] = new JsonArray(_skipped.Select(entry => (JsonNode)entry.ToJson()).ToArray()),
                            ["unattributed"] = new JsonArray(_unattributed.Select(entry => (JsonNode)entry.ToJson()).ToArray()),
                            ["mutations"] = new JsonArray(_mutations.Select(mutation => (JsonNode)new JsonObject
                            {
                                ["id"] = mutation.Id,
                                ["action"] = mutation.Action,
                                ["ok"] = mutation.Ok,
                                ["deletedLine"] = mutation.DeletedLine,
                            }).ToArray()),
                            ["exitCode"] = code,
                        };
                        _files.File.WriteAllText(_reportFile, OperatorApi.Pretty(report) + Environment.NewLine);
                    }
                    catch (Exception e) when (e is System.IO.IOException || e is UnauthorizedAccessException)
                    {
                        _host.Out.WriteLine("warning: failed to write report: " + e.Message);
                    }
                }
                return new CodexTestResidueResult
                {
                    ExitCode = code,
                    ReportPath = _reportFile,
                    CandidateCount = _candidates.Count,
                    SkipCount = _skipped.Count,
                    UnattributedCount = _unattributed.Count,
                    CandidateIds = _candidates.Select(entry => entry.Id).ToArray(),
                    UnattributedIds = _unattributed.Select(entry => entry.Id).ToArray(),
                    Mutations = _mutations.ToArray(),
                };
            }
        }

        // The production machine: python on the host's launcher (its stdout read), the live codex processes' command lines
        // (QualificationOs), and codex delete on the host's launcher (its output read).
        private sealed class CodexResidueOs : ICodexResidueSeams
        {
            private readonly NightlyHost _host;

            public CodexResidueOs(NightlyHost host) => _host = host;

            public string ReadThreads(string stateDbPath)
            {
                var run = _host.Launch(new NightlyLaunch("python", new[] { "-c", Reader, stateDbPath }, "", CaptureOutput: true));
                if (run.ExitCode != 0) throw new InvalidOperationException("Python could not read state_5.sqlite read-only.");
                return run.StandardOutputText ?? "";
            }

            public IReadOnlyList<string> CodexCommandLines()
            {
                var os = new QualificationOs();
                var lines = new List<string>();
                foreach (var process in Process.GetProcessesByName("codex"))
                {
                    using (process)
                    {
                        var line = os.CommandLine(process.Id);
                        if (line != null) lines.Add(line);
                    }
                }
                return lines;
            }

            public IReadOnlyList<string> Delete(string codexExe, string id)
            {
                var run = _host.Launch(new NightlyLaunch(codexExe, new[] { "delete", "--force", id }, "", CaptureOutput: true));
                return Regex.Split((run.LogText ?? "").TrimEnd('\n'), "\r?\n");
            }
        }
    }
}
