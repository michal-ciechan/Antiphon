// CARD-1784 (moved from scripts/lib/post-land-early-warning.ps1): the Linux post-land early warning's plan. The decision
// reads the changed paths, the shard manifest (tests/post-land-unit-shards.json under the product root), the receipt
// root and the impacted-classes selector. It plans one unit shard per namespace and one per Checkpoints initial
// letter, then integration shards of at most maxIntegrationClassesPerShard selected classes that the Windows lane does
// not owe. A shard's TRX under <receipt-root>/<tip> settles its failures; a new red is a failure that the nearest
// completed ancestor receipt does not name. The lock, the lane and the entry are in PostLandLane.cs.
// Plain C#: no PowerShell types. String comparisons the PowerShell made with -eq stay case-insensitive. ASCII only.
#nullable disable
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;

namespace Antiphon.Nightly
{
    /// <summary>The entry's options: antiphon-scripts post-land-early-warning --range ... (Child is the internal relaunch).</summary>
    public sealed class PostLandOptions
    {
        public string Range { get; set; }
        public string ReceiptRoot { get; set; }
        public string Repo { get; set; }
        public string Table { get; set; }
        public string FileList { get; set; }
        public string RunId { get; set; }
        public bool Plan { get; set; }
        public bool Force { get; set; }
        /// <summary>The detached lane: takes the lock and runs the shards. Only the run-mode parent sets it.</summary>
        public bool Child { get; set; }

        /// <summary>The CLI flags that reproduce these options (after the command name).</summary>
        public List<string> ToArguments()
        {
            var args = new List<string>();
            void Add(string flag, string value)
            {
                if (string.IsNullOrWhiteSpace(value)) return;
                args.Add(flag);
                args.Add(value);
            }
            if (Child) args.Add("--child");
            if (Plan) args.Add("--plan");
            Add("--range", Range);
            Add("--repo", Repo);
            Add("--receipt-root", ReceiptRoot);
            Add("--run-id", RunId);
            Add("--file-list", FileList);
            Add("--table", Table);
            if (Force) args.Add("--force");
            return args;
        }
    }

    /// <summary>What the selector is asked: the range or the file list, the table, and the changed paths already read.</summary>
    public sealed class PostLandSelectorRequest
    {
        public string Repo { get; set; }
        public string BaseSha { get; set; }
        public string TipSha { get; set; }
        public string FileList { get; set; }
        public string Table { get; set; }
        public IReadOnlyList<string> ChangedPaths { get; set; }
    }

    /// <summary>The selector's answer: its classes and the treenode filters the Windows lane owes.</summary>
    public sealed class PostLandSelection
    {
        public bool Ok { get; set; }
        public string Error { get; set; } = "";
        public List<string> Classes { get; set; } = new List<string>();
        public List<string> WindowsFilters { get; set; } = new List<string>();
    }

    /// <summary>The run-mode parent's request to start the detached lane.</summary>
    public sealed class PostLandChildLaunch
    {
        public PostLandOptions Options { get; set; }
        /// <summary>The CLI flags for Options (after the command name).</summary>
        public List<string> Arguments { get; set; }
        public string WorkingDirectory { get; set; }
        public string StdoutLog { get; set; }
        public string StderrLog { get; set; }
    }

    /// <summary>
    /// The seams. The CLI fills them from the environment (FromEnvironment); a test builds them directly, so nothing reads
    /// process-wide state while tests run side by side.
    /// </summary>
    public sealed class PostLandSettings
    {
        /// <summary>The checkout that holds tests/post-land-unit-shards.json and the scripts; --repo defaults to it.</summary>
        public string ProductRoot { get; set; }
        /// <summary>POST_LAND_WARNING_PLATFORM: empty means the host decides; "linux" admits run mode; anything else refuses it.</summary>
        public string Platform { get; set; }
        /// <summary>POST_LAND_WARNING_PLAN_DELAY_MS: a sleep after the skip checks and before the selector.</summary>
        public int PlanDelayMs { get; set; }
        /// <summary>POST_LAND_WARNING_RECLAIM_DELAY_MS: a sleep after a stale classification and before the reclaim rename.</summary>
        public int ReclaimDelayMs { get; set; }
        /// <summary>POST_LAND_WARNING_SHARD_BUDGET_MINUTES: replaces the manifest's shardBudgetMinutes.</summary>
        public double? ShardBudgetMinutes { get; set; }
        /// <summary>POST_LAND_WARNING_SLOT_SHIM: when set, the slot is not started; each command line is appended here.</summary>
        public string SlotShim { get; set; }
        /// <summary>POST_LAND_WARNING_SLOT_DELAY_MS: the shim's sleep per command.</summary>
        public int SlotDelayMs { get; set; }
        /// <summary>POST_LAND_WARNING_SLOT_TRX: the TRX the shim copies to a shard's --results-directory.</summary>
        public string SlotTrx { get; set; }
        /// <summary>POST_LAND_WARNING_SLOT_BUILD_EXIT and _SHARD_EXIT: the shim's exit codes (text; empty is 0).</summary>
        public string SlotBuildExit { get; set; }
        public string SlotShardExit { get; set; }
        /// <summary>
        /// The slot launcher's argv before "-Label post-land-early-warning -- command". Null is pwsh -File
        /// scripts/build-slot.ps1 under the product root. POST_LAND_WARNING_SLOT_SCRIPT sets it (SlotCommandFor).
        /// </summary>
        public IReadOnlyList<string> SlotCommand { get; set; }
        /// <summary>The impacted-classes selector. Null is ImpactedClasses.Run in this process (ImpactedSelector).</summary>
        public Func<PostLandSelectorRequest, PostLandSelection> Selector { get; set; }
        /// <summary>Starts the detached lane (run mode). The CLI relaunches itself with --child, under setsid where it exists.</summary>
        public Action<PostLandChildLaunch> LaunchChild { get; set; }

        /// <summary>A slot script path as a launcher: a .ps1 runs under pwsh -File, a .dll under dotnet, anything else directly.</summary>
        public static IReadOnlyList<string> SlotCommandFor(string slotScript)
        {
            if (slotScript.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase))
                return new[] { "pwsh", "-NoProfile", "-NonInteractive", "-File", slotScript };
            if (slotScript.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) return new[] { "dotnet", slotScript };
            return new[] { slotScript };
        }

        public static PostLandSettings FromEnvironment(Func<string, string> variable, string productRoot)
        {
            var slotScript = variable("POST_LAND_WARNING_SLOT_SCRIPT");
            return new PostLandSettings
            {
                ProductRoot = productRoot,
                Platform = variable("POST_LAND_WARNING_PLATFORM"),
                PlanDelayMs = PostLandEarlyWarning.IntOrZero(variable("POST_LAND_WARNING_PLAN_DELAY_MS")),
                ReclaimDelayMs = PostLandEarlyWarning.IntOrZero(variable("POST_LAND_WARNING_RECLAIM_DELAY_MS")),
                ShardBudgetMinutes = PostLandEarlyWarning.Minutes(variable("POST_LAND_WARNING_SHARD_BUDGET_MINUTES")),
                SlotShim = variable("POST_LAND_WARNING_SLOT_SHIM"),
                SlotDelayMs = PostLandEarlyWarning.IntOrZero(variable("POST_LAND_WARNING_SLOT_DELAY_MS")),
                SlotTrx = variable("POST_LAND_WARNING_SLOT_TRX"),
                SlotBuildExit = variable("POST_LAND_WARNING_SLOT_BUILD_EXIT"),
                SlotShardExit = variable("POST_LAND_WARNING_SLOT_SHARD_EXIT"),
                SlotCommand = string.IsNullOrWhiteSpace(slotScript) ? null : SlotCommandFor(slotScript),
            };
        }
    }

    /// <summary>One test class the census found: namespace, name and its single lane (Unit or Integration, or neither).</summary>
    public sealed class PostLandClass
    {
        public string Namespace { get; set; }
        public string Name { get; set; }
        public bool Unit { get; set; }
        public bool Integration { get; set; }
    }

    /// <summary>One planned shard; the lane and the outcome fill trx, the counts, exit and slow.</summary>
    public sealed class PostLandShard : IJsonWritable
    {
        public string Id { get; set; }
        public string Kind { get; set; }
        public string Filter { get; set; }
        public string Namespace { get; set; } = "";
        public string Letter { get; set; } = "";
        public List<PostLandClass> Classes { get; set; } = new List<PostLandClass>();
        public string Trx { get; set; }
        public int? Executed { get; set; }
        public int? Passed { get; set; }
        public int? Failed { get; set; }
        public int? Skipped { get; set; }
        public int? Exit { get; set; }
        public bool Slow { get; set; }

        public void WriteJson(Utf8JsonWriter writer)
        {
            writer.WriteStartObject();
            writer.WriteString("id", Id);
            writer.WriteString("kind", Kind);
            writer.WriteString("filter", Filter);
            writer.WriteString("namespace", Namespace);
            writer.WriteString("letter", Letter);
            writer.WriteStartArray("classes");
            foreach (var item in Classes)
            {
                writer.WriteStartObject();
                writer.WriteString("name", item.Name);
                writer.WriteString("namespace", item.Namespace);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteString("trx", Trx);
            PostLandEarlyWarning.WriteInt(writer, "executed", Executed);
            PostLandEarlyWarning.WriteInt(writer, "passed", Passed);
            PostLandEarlyWarning.WriteInt(writer, "failed", Failed);
            PostLandEarlyWarning.WriteInt(writer, "skipped", Skipped);
            PostLandEarlyWarning.WriteInt(writer, "exit", Exit);
            writer.WriteBoolean("slow", Slow);
            writer.WriteEndObject();
        }
    }

    /// <summary>A filter the Windows lane owes; passed stays false here.</summary>
    public sealed class PostLandOwedWindow
    {
        public string Filter { get; set; }
        public bool Passed { get; set; }
    }

    /// <summary>The plan, and once the lane ran, the receipt (&lt;receipt-root&gt;/&lt;tip&gt;.json).</summary>
    public sealed class PostLandPayload : IJsonWritable
    {
        public bool Ok { get; set; } = true;
        public string State { get; set; }
        public string Reason { get; set; }
        public string TipSha { get; set; }
        public string BaseSha { get; set; }
        public int Lane { get; set; } = 1;
        public bool Serial { get; set; } = true;
        public bool Slot { get; set; }
        public List<PostLandShard> Shards { get; set; } = new List<PostLandShard>();
        public List<PostLandOwedWindow> OwedWindows { get; set; } = new List<PostLandOwedWindow>();
        public List<string> NewReds { get; set; } = new List<string>();
        public List<string> Failures { get; set; } = new List<string>();
        public string PreviousTipSha { get; set; }
        public string SummaryLine { get; set; }
        public string Manifest { get; set; }
        public int? BuildExit { get; set; }
        public List<string> SlowShards { get; set; } = new List<string>();
        public string StartedAt { get; set; }
        public string FinishedAt { get; set; }

        public string Batch => BaseSha + ".." + TipSha;

        public void WriteJson(Utf8JsonWriter writer)
        {
            writer.WriteStartObject();
            writer.WriteBoolean("ok", Ok);
            writer.WriteString("state", State);
            writer.WriteString("reason", Reason);
            writer.WriteString("tipSha", TipSha);
            writer.WriteString("baseSha", BaseSha);
            writer.WriteString("batch", Batch);
            writer.WriteNumber("lane", Lane);
            writer.WriteBoolean("serial", Serial);
            writer.WriteBoolean("slot", Slot);
            writer.WriteStartArray("shards");
            foreach (var shard in Shards) shard.WriteJson(writer);
            writer.WriteEndArray();
            writer.WriteStartArray("owedWindows");
            foreach (var owed in OwedWindows)
            {
                writer.WriteStartObject();
                writer.WriteString("filter", owed.Filter);
                writer.WriteBoolean("passed", owed.Passed);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            PostLandEarlyWarning.WriteStrings(writer, "newReds", NewReds);
            PostLandEarlyWarning.WriteStrings(writer, "failures", Failures);
            writer.WriteString("previousTipSha", PreviousTipSha);
            writer.WriteString("summaryLine", SummaryLine);
            writer.WriteString("manifest", Manifest);
            PostLandEarlyWarning.WriteInt(writer, "buildExit", BuildExit);
            PostLandEarlyWarning.WriteStrings(writer, "slowShards", SlowShards);
            writer.WriteString("startedAt", StartedAt);
            writer.WriteString("finishedAt", FinishedAt);
            writer.WriteEndObject();
        }

        /// <summary>Indented JSON, non-ASCII unescaped.</summary>
        public string ToJson()
        {
            using var buffer = new MemoryStream();
            using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, Indented = true }))
                WriteJson(writer);
            return Encoding.UTF8.GetString(buffer.ToArray());
        }
    }

    /// <summary>tests/post-land-unit-shards.json (schema 1).</summary>
    public sealed class PostLandManifest
    {
        public string CheckpointsNamespace { get; set; } = "";
        public string CheckpointsFilter { get; set; } = "";
        public string NonCheckpointsFilter { get; set; } = "";
        public int? MaxIntegrationClassesPerShard { get; set; }
        public int CoalesceMinutes { get; set; }
        public double? ShardBudgetMinutes { get; set; }
    }

    /// <summary>One TRX: the failing Class.Method labels (short class name, first seen order) and the outcome counts.</summary>
    public sealed class PostLandTrx
    {
        public List<string> Failures { get; set; } = new List<string>();
        public int Executed { get; set; }
        public int Passed { get; set; }
        public int Failed { get; set; }
        public int Skipped { get; set; }
    }

    public sealed class PostLandDecision
    {
        public PostLandPayload Payload { get; set; }
        public int Exit { get; set; }
    }

    public static partial class PostLandEarlyWarning
    {
        public const string ManifestRelativePath = "tests/post-land-unit-shards.json";

        private static readonly Regex FullShaPattern = new Regex("^[0-9a-fA-F]{40}$", RegexOptions.CultureInvariant);
        private static readonly Regex DocMark = new Regex(@"docs/[A-Za-z0-9_./-]+\.md|[A-Za-z0-9_.-]+\.md", RegexOptions.CultureInvariant);
        private static readonly Regex ClassLine = new Regex(@"^public\s+(?:(?:sealed|static|partial|abstract)\s+)*class\s+([A-Za-z_][A-Za-z0-9_]*)\b", RegexOptions.CultureInvariant);
        private static readonly Regex FileNamespace = new Regex(@"^namespace\s+([A-Za-z0-9_.]+)\s*;", RegexOptions.CultureInvariant);
        private static readonly Regex BlockNamespace = new Regex(@"^namespace\s+([A-Za-z0-9_.]+)\s*\{", RegexOptions.CultureInvariant);
        private static readonly Regex RawStringRun = new Regex("\"{3,}", RegexOptions.CultureInvariant);
        private static readonly Regex WindowClass = new Regex(@"\(([A-Za-z_][A-Za-z0-9_]*)\*\)", RegexOptions.CultureInvariant);
        private static readonly JsonDocumentOptions Lenient = new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

        // ---- small readers ------------------------------------------------------------------------------------------

        internal static int IntOrZero(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return 0;
            var trimmed = text.Trim();
            if (int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)) return value;
            if (double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && number >= int.MinValue && number <= int.MaxValue)
                return (int)Math.Round(number, MidpointRounding.ToEven);
            return 0;
        }

        internal static double? Minutes(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : (double?)null;
        }

        internal static void WriteInt(Utf8JsonWriter writer, string name, int? value)
        {
            if (value == null) writer.WriteNull(name);
            else writer.WriteNumber(name, value.Value);
        }

        internal static void WriteStrings(Utf8JsonWriter writer, string name, IEnumerable<string> values)
        {
            writer.WriteStartArray(name);
            foreach (var value in values) writer.WriteStringValue(value);
            writer.WriteEndArray();
        }

        /// <summary>The JSON document in the file, or null when it is missing or does not parse.</summary>
        internal static JsonElement? ReadJson(string path)
        {
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(path), Lenient);
                return document.RootElement.Clone();
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is JsonException || e is ArgumentException)
            {
                return null;
            }
        }

        /// <summary>A property of an object, exact name first and then ignoring case (as ConvertFrom-Json read it).</summary>
        internal static bool TryProperty(JsonElement element, string name, out JsonElement value)
        {
            value = default;
            if (element.ValueKind != JsonValueKind.Object) return false;
            if (element.TryGetProperty(name, out value)) return true;
            foreach (var property in element.EnumerateObject())
            {
                if (!string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)) continue;
                value = property.Value;
                return true;
            }
            return false;
        }

        /// <summary>A scalar as text: a string as itself, a number as written, null as empty.</summary>
        internal static string Text(JsonElement value)
        {
            switch (value.ValueKind)
            {
                case JsonValueKind.String: return value.GetString();
                case JsonValueKind.Null:
                case JsonValueKind.Undefined: return "";
                case JsonValueKind.True: return "True";
                case JsonValueKind.False: return "False";
                default: return value.GetRawText();
            }
        }

        /// <summary>The non-blank texts of a value: each item of an array, or the scalar itself.</summary>
        internal static List<string> Strings(JsonElement value)
        {
            var list = new List<string>();
            if (value.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in value.EnumerateArray()) AddText(list, item);
            }
            else AddText(list, value);
            return list;
        }

        private static void AddText(List<string> list, JsonElement item)
        {
            if (item.ValueKind == JsonValueKind.Object || item.ValueKind == JsonValueKind.Array) return;
            var text = Text(item);
            if (!string.IsNullOrWhiteSpace(text)) list.Add(text);
        }

        private static bool IsState(JsonElement document, string state) =>
            TryProperty(document, "state", out var value) && string.Equals(Text(value), state, StringComparison.OrdinalIgnoreCase);

        // ---- processes --------------------------------------------------------------------------------------------

        /// <summary>Runs a process to its end with UTF-8 stdout and stderr captured. A start failure is exit 1.</summary>
        internal static (int Exit, string Stdout, string Stderr) RunProcess(string fileName, string workingDirectory, IEnumerable<string> arguments)
        {
            var start = new ProcessStartInfo(fileName)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = new UTF8Encoding(false),
                StandardErrorEncoding = new UTF8Encoding(false),
            };
            if (!string.IsNullOrWhiteSpace(workingDirectory)) start.WorkingDirectory = workingDirectory;
            foreach (var argument in arguments) start.ArgumentList.Add(argument ?? "");
            try
            {
                using var process = Process.Start(start);
                if (process == null) return (1, "", "process did not start");
                var stdout = process.StandardOutput.ReadToEndAsync();
                var stderr = process.StandardError.ReadToEndAsync();
                process.WaitForExit();
                return (process.ExitCode, stdout.GetAwaiter().GetResult(), stderr.GetAwaiter().GetResult());
            }
            catch (Win32Exception e)
            {
                return (1, "", e.Message);
            }
        }

        internal static (int Exit, string Stdout, string Stderr) Git(string repo, params string[] arguments) =>
            RunProcess("git", null, new[] { "-C", repo }.Concat(arguments));

        // ---- range and paths ----------------------------------------------------------------------------------------

        /// <summary>base..tip with neither side blank and no third dot; anything else is refused=bad-range.</summary>
        public static bool TryResolveRange(string range, out string baseRef, out string tipRef)
        {
            baseRef = "";
            tipRef = "";
            if (string.IsNullOrWhiteSpace(range) || range.Contains("...", StringComparison.Ordinal)) return false;
            var index = range.IndexOf("..", StringComparison.Ordinal);
            if (index < 1) return false;
            var left = range.Substring(0, index);
            var right = range.Substring(index + 2);
            if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right) || right.Contains("..", StringComparison.Ordinal)) return false;
            baseRef = left;
            tipRef = right;
            return true;
        }

        public static bool IsFullSha(string value) => value != null && FullShaPattern.IsMatch(value);

        /// <summary>A full commit id as given; anything else through git rev-parse --verify. Empty when git cannot.</summary>
        public static string ResolveSha(string repo, string value)
        {
            if (IsFullSha(value)) return value;
            var ran = Git(repo, "rev-parse", "--verify", value);
            return ran.Exit != 0 ? "" : ran.Stdout.Trim();
        }

        private static List<string> Lines(string text)
        {
            var paths = new List<string>();
            foreach (var line in Regex.Split(text, "\r?\n"))
            {
                var trimmed = line.Trim().Replace('\\', '/');
                if (!string.IsNullOrWhiteSpace(trimmed)) paths.Add(trimmed);
            }
            return paths;
        }

        /// <summary>The file list's lines, or git diff --name-only --diff-filter=ACMRTD base tip. Null with the refusal.</summary>
        public static List<string> ChangedPaths(string repo, string baseSha, string tipSha, string fileList, out string error)
        {
            error = "";
            if (!string.IsNullOrWhiteSpace(fileList))
            {
                if (!File.Exists(fileList))
                {
                    error = "refused=bad-file-list";
                    return null;
                }
                return Lines(File.ReadAllText(fileList));
            }
            var ran = Git(repo, "diff", "--name-only", "--diff-filter=ACMRTD", baseSha, tipSha);
            if (ran.Exit != 0)
            {
                error = "refused=git-diff";
                return null;
            }
            return Lines(ran.Stdout);
        }

        private static bool Skipped(string path)
        {
            var normalized = path.Replace('\\', '/');
            return normalized.Contains("/bin/", StringComparison.Ordinal) || normalized.Contains("/obj/", StringComparison.Ordinal);
        }

        /// <summary>Every docs/....md path and .md file name a tests/**/*.cs file names (bin and obj skipped).</summary>
        public static HashSet<string> PinnedDocMarks(string repo)
        {
            var marks = new HashSet<string>(StringComparer.Ordinal);
            var root = Path.Combine(repo, "tests");
            if (!Directory.Exists(root)) return marks;
            foreach (var file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
            {
                if (Skipped(file)) continue;
                foreach (Match match in DocMark.Matches(File.ReadAllText(file)))
                {
                    var value = match.Value.Replace('\\', '/').Trim();
                    if (string.IsNullOrWhiteSpace(value)) continue;
                    marks.Add(value);
                    var name = Path.GetFileName(value);
                    if (!string.IsNullOrWhiteSpace(name)) marks.Add(name);
                }
            }
            return marks;
        }

        /// <summary>
        /// True for no paths, or when every path is a docs/**.md file that no test names (by path or file name).
        /// </summary>
        public static bool IsDocsOnly(string repo, IEnumerable<string> paths)
        {
            var count = 0;
            var docs = new List<string>();
            foreach (var path in paths)
            {
                if (path == null) continue;
                var normalized = path.Replace('\\', '/').Trim();
                if (normalized.StartsWith("./", StringComparison.Ordinal)) normalized = normalized.Substring(2);
                if (string.IsNullOrWhiteSpace(normalized)) continue;
                count++;
                if (!(normalized.StartsWith("docs/", StringComparison.Ordinal) && normalized.EndsWith(".md", StringComparison.Ordinal))) return false;
                docs.Add(normalized);
            }
            if (count == 0) return true;
            var pinned = PinnedDocMarks(repo);
            foreach (var path in docs)
                if (pinned.Contains(path) || pinned.Contains(Path.GetFileName(path))) return false;
            return true;
        }

        // ---- the manifest and the class census ---------------------------------------------------------------------

        /// <summary>The schema-1 manifest, or null when it is missing, does not parse or names another schema.</summary>
        public static PostLandManifest ReadManifest(string path)
        {
            if (!File.Exists(path)) return null;
            var read = ReadJson(path);
            if (read == null) return null;
            var document = read.Value;
            if (!TryProperty(document, "schema", out var schema) || IntOrZero(Text(schema)) != 1) return null;
            string Field(string name) => TryProperty(document, name, out var value) ? Text(value) : "";
            int? Number(string name) => TryProperty(document, name, out var value) ? IntOrZero(Text(value)) : (int?)null;
            double? budget = null;
            if (TryProperty(document, "shardBudgetMinutes", out var budgetValue))
                budget = double.TryParse(Text(budgetValue), NumberStyles.Float, CultureInfo.InvariantCulture, out var minutes) ? minutes : 0.0;
            return new PostLandManifest
            {
                CheckpointsNamespace = Field("checkpointsNamespace"),
                CheckpointsFilter = Field("checkpointsFilter"),
                NonCheckpointsFilter = Field("nonCheckpointsFilter"),
                MaxIntegrationClassesPerShard = Number("maxIntegrationClassesPerShard"),
                CoalesceMinutes = Number("coalesceMinutes") ?? 0,
                ShardBudgetMinutes = budget,
            };
        }

        /// <summary>A line inside or on the edge of a raw string literal is fixture text, not a declaration.</summary>
        private static bool[] RawStringLines(string[] lines)
        {
            var inRaw = new bool[lines.Length];
            var open = false;
            for (var r = 0; r < lines.Length; r++)
            {
                var runs = RawStringRun.Matches(lines[r]).Count;
                if (open || runs > 0) inRaw[r] = true;
                if (runs % 2 == 1) open = !open;
            }
            return inRaw;
        }

        /// <summary>
        /// The test classes under tests/Antiphon.Tests (bin and obj skipped): each public class with a [Test] before the
        /// next class line, its namespace, and its lane from the Category attributes directly above it. File-walk
        /// order; a namespace and name pair counts once.
        /// </summary>
        public static List<PostLandClass> Classes(string repo)
        {
            var found = new List<PostLandClass>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var root = Path.Combine(repo, "tests", "Antiphon.Tests");
            if (!Directory.Exists(root)) return found;
            foreach (var file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
            {
                if (Skipped(file)) continue;
                var lines = File.ReadAllLines(file);
                var inRaw = RawStringLines(lines);
                var ns = "";
                for (var i = 0; i < lines.Length; i++)
                {
                    if (inRaw[i]) continue;
                    var trim = lines[i].Trim();
                    var fileMatch = FileNamespace.Match(trim);
                    if (fileMatch.Success) { ns = fileMatch.Groups[1].Value; continue; }
                    var blockMatch = BlockNamespace.Match(trim);
                    if (blockMatch.Success) { ns = blockMatch.Groups[1].Value; continue; }
                    var match = ClassLine.Match(trim);
                    if (!match.Success) continue;
                    var unit = false;
                    var integration = false;
                    for (var j = i - 1; j >= 0; j--)
                    {
                        if (inRaw[j]) break;
                        var earlier = lines[j].Trim();
                        if (earlier.Length == 0 || earlier.StartsWith("//", StringComparison.Ordinal)) continue;
                        if (earlier.StartsWith("[", StringComparison.Ordinal))
                        {
                            if (earlier.Contains("Category(\"Unit\")", StringComparison.Ordinal)) unit = true;
                            if (earlier.Contains("Category(\"Integration\")", StringComparison.Ordinal)) integration = true;
                            continue;
                        }
                        break;
                    }
                    var hasTest = false;
                    for (var k = i + 1; k < lines.Length; k++)
                    {
                        if (inRaw[k]) continue;
                        var body = lines[k].Trim();
                        if (ClassLine.IsMatch(body)) break;
                        if (body.Contains("[Test]", StringComparison.Ordinal) || body.Contains("[Test,", StringComparison.Ordinal)
                            || body.Contains("[TestAttribute", StringComparison.Ordinal))
                        {
                            hasTest = true;
                            break;
                        }
                    }
                    if (!hasTest) continue;
                    var name = match.Groups[1].Value;
                    if (!seen.Add(ns + "\n" + name)) continue;
                    found.Add(new PostLandClass { Namespace = ns, Name = name, Unit = unit && !integration, Integration = integration && !unit });
                }
            }
            return found;
        }

        /// <summary>/*/*/(A*)|(B*)/* over the names in the order given; empty for none.</summary>
        public static string ClassFilter(IEnumerable<string> names)
        {
            var parts = names.Where(name => !string.IsNullOrWhiteSpace(name)).Select(name => "(" + name + "*)").ToList();
            return parts.Count == 0 ? "" : "/*/*/" + string.Join("|", parts) + "/*";
        }

        // ---- the selector ----------------------------------------------------------------------------------------------

        /// <summary>
        /// The impacted-classes selector in this process (ImpactedClasses.Run with --json on the request's repository: the
        /// file list when there is one, else the range; the table when given), its output read as a selection.
        /// </summary>
        public static PostLandSelection ImpactedSelector(string productRoot, PostLandSelectorRequest request)
        {
            var fileList = !string.IsNullOrWhiteSpace(request.FileList);
            var options = new ImpactedClassesOptions
            {
                Repo = request.Repo, Json = true, SourceRoot = productRoot, CurrentDirectory = request.Repo,
                FileList = fileList ? request.FileList : null, Range = fileList ? null : request.BaseSha + ".." + request.TipSha,
                Table = string.IsNullOrWhiteSpace(request.Table) ? null : request.Table,
            };
            var stdout = new StringWriter();
            var stderr = new StringWriter();
            var exit = ImpactedClasses.Run(options, NightlyHost.Create(null, new WatchedLauncher(), stdout, stderr, null, null, null, null));
            return ParseSelection(exit, stdout.ToString(), stderr.ToString());
        }

        /// <summary>The selector's exit and output as a selection: its classes and each windowsFilters treenodeFilter.</summary>
        public static PostLandSelection ParseSelection(int exit, string stdout, string stderr)
        {
            if (exit != 0)
            {
                var message = (stderr ?? "").Trim();
                return new PostLandSelection { Ok = false, Error = string.IsNullOrWhiteSpace(message) ? "selector failed" : message };
            }
            var body = (stdout ?? "").Trim();
            var start = body.IndexOf('{');
            var end = body.LastIndexOf('}');
            if (start < 0 || end <= start) return new PostLandSelection { Ok = false, Error = "selector returned no JSON" };
            JsonElement document;
            try
            {
                using var parsed = JsonDocument.Parse(body.Substring(start, end - start + 1), Lenient);
                document = parsed.RootElement.Clone();
            }
            catch (JsonException)
            {
                return new PostLandSelection { Ok = false, Error = "selector JSON was not readable" };
            }
            var selection = new PostLandSelection { Ok = true };
            if (TryProperty(document, "classes", out var classes)) selection.Classes.AddRange(Strings(classes));
            if (TryProperty(document, "windowsFilters", out var windows) && windows.ValueKind != JsonValueKind.Null)
            {
                var rows = windows.ValueKind == JsonValueKind.Array ? windows.EnumerateArray().ToList() : new List<JsonElement> { windows };
                foreach (var row in rows)
                {
                    if (row.ValueKind == JsonValueKind.Null) continue;
                    selection.WindowsFilters.Add(TryProperty(row, "treenodeFilter", out var filter) ? Text(filter) : "");
                }
            }
            return selection;
        }

        /// <summary>The class names in the Windows filters' (Name*) operands.</summary>
        public static HashSet<string> WindowNames(IEnumerable<string> windowsFilters)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var filter in windowsFilters)
                foreach (Match match in WindowClass.Matches(filter ?? "")) names.Add(match.Groups[1].Value);
            return names;
        }

        public static List<PostLandOwedWindow> OwedWindows(IEnumerable<string> windowsFilters) =>
            windowsFilters.Where(filter => !string.IsNullOrWhiteSpace(filter)).Select(filter => new PostLandOwedWindow { Filter = filter, Passed = false }).ToList();

        // ---- shards ----------------------------------------------------------------------------------------------------

        private static PostLandShard Shard(string id, string kind, string filter, string ns, string letter, IEnumerable<PostLandClass> items) =>
            new PostLandShard
            {
                Id = id,
                Kind = kind,
                Filter = filter,
                Namespace = ns ?? "",
                Letter = letter ?? "",
                Classes = items.Select(item => new PostLandClass { Name = item.Name, Namespace = item.Namespace }).ToList(),
            };

        /// <summary>
        /// One unit shard per namespace and one per Checkpoints initial letter (ordinal key order: a letter repeated in
        /// file-walk order is one shard), then the selected integration classes the Windows lane does not owe, sorted by
        /// name and namespace and cut into shards of maxIntegrationClassesPerShard (8 when absent or below 1).
        /// </summary>
        public static List<PostLandShard> BuildShards(PostLandManifest manifest, IReadOnlyList<PostLandClass> classes, IEnumerable<string> selectorClasses, ISet<string> windowNames)
        {
            var shards = new List<PostLandShard>();
            var checkpoints = manifest.CheckpointsNamespace ?? "";
            var buckets = new Dictionary<string, (bool Checkpoints, string Namespace, string Letter, List<PostLandClass> Items)>(StringComparer.Ordinal);
            foreach (var item in classes)
            {
                if (item == null || !item.Unit) continue;
                var ns = item.Namespace ?? "";
                var name = item.Name ?? "";
                var inCheckpoints = string.Equals(ns, checkpoints, StringComparison.OrdinalIgnoreCase) && name.Length > 0;
                var letter = inCheckpoints ? name.Substring(0, 1) : "";
                var key = inCheckpoints ? "cp:" + letter : "ns:" + ns;
                if (!buckets.TryGetValue(key, out var bucket)) buckets[key] = bucket = (inCheckpoints, ns, letter, new List<PostLandClass>());
                bucket.Items.Add(item);
            }
            foreach (var key in buckets.Keys.OrderBy(key => key, StringComparer.Ordinal))
            {
                var bucket = buckets[key];
                if (bucket.Items.Count == 0) continue;
                if (bucket.Checkpoints)
                    shards.Add(Shard("unit-cp-" + bucket.Letter, "unit", (manifest.CheckpointsFilter ?? "").Replace("<Letter>", bucket.Letter, StringComparison.Ordinal),
                        checkpoints, bucket.Letter, bucket.Items));
                else
                    shards.Add(Shard("unit-ns-" + bucket.Namespace, "unit", (manifest.NonCheckpointsFilter ?? "").Replace("<namespace>", bucket.Namespace, StringComparison.Ordinal),
                        bucket.Namespace, null, bucket.Items));
            }
            var selected = new HashSet<string>(selectorClasses.Where(name => !string.IsNullOrWhiteSpace(name)), StringComparer.Ordinal);
            // Sort-Object Name, Namespace compared with the culture, ignoring case.
            var integration = classes
                .Where(item => item != null && item.Integration && selected.Contains(item.Name ?? "") && !windowNames.Contains(item.Name ?? ""))
                .OrderBy(item => item.Name, StringComparer.InvariantCultureIgnoreCase)
                .ThenBy(item => item.Namespace, StringComparer.InvariantCultureIgnoreCase)
                .ToList();
            var max = manifest.MaxIntegrationClassesPerShard ?? 8;
            if (max < 1) max = 8;
            var number = 0;
            for (var index = 0; index < integration.Count; index += max)
            {
                var chunk = integration.Skip(index).Take(max).ToList();
                number++;
                shards.Add(Shard("integration-" + number.ToString(CultureInfo.InvariantCulture), "integration", ClassFilter(chunk.Select(item => item.Name)), null, null, chunk));
            }
            return shards;
        }

        // ---- summary and payload -----------------------------------------------------------------------------------

        private static List<string> SortedNames(IEnumerable<string> names)
        {
            var list = names.Where(name => !string.IsNullOrWhiteSpace(name)).ToList();
            list.Sort(StringComparer.Ordinal);
            return list;
        }

        /// <summary>
        /// post-land tip=&lt;tip&gt; batch=&lt;base12&gt;..&lt;tip12&gt; then new-reds=N [names], new-reds=unknown no-trx=ids [names] or,
        /// for a failed build, new-reds=unknown build=N; then owed-windows=N.
        /// </summary>
        public static string Summary(string tip, string baseSha, IEnumerable<string> newReds, IEnumerable<string> unknownShards, int owedCount, int buildExit = -1)
        {
            tip ??= "";
            baseSha ??= "";
            var batch = (baseSha.Length >= 12 ? baseSha.Substring(0, 12) : baseSha) + ".." + (tip.Length >= 12 ? tip.Substring(0, 12) : tip);
            var owed = "owed-windows=" + owedCount.ToString(CultureInfo.InvariantCulture);
            var names = SortedNames(newReds ?? Enumerable.Empty<string>());
            var listed = names.Count > 0 ? " " + string.Join(",", names) : "";
            var unknown = (unknownShards ?? Enumerable.Empty<string>()).Where(id => !string.IsNullOrWhiteSpace(id)).ToList();
            var head = "post-land tip=" + tip + " batch=" + batch + " new-reds=";
            if (buildExit > 0) return head + "unknown build=" + buildExit.ToString(CultureInfo.InvariantCulture) + " " + owed;
            if (unknown.Count > 0) return head + "unknown no-trx=" + string.Join(",", unknown) + listed + " " + owed;
            return head + names.Count.ToString(CultureInfo.InvariantCulture) + listed + " " + owed;
        }

        private static PostLandPayload Payload(string state, string reason, string baseSha, string tipSha, string manifestPath, bool ok = true) =>
            new PostLandPayload
            {
                Ok = ok,
                State = state,
                Reason = reason,
                BaseSha = baseSha,
                TipSha = tipSha,
                SummaryLine = Summary(tipSha, baseSha, null, null, 0),
                Manifest = manifestPath,
            };

        // ---- TRX and receipts ----------------------------------------------------------------------------------------

        /// <summary>
        /// The TRX's results: Failed, Error, Timeout and Aborted are failures (labelled ShortClass.Method once each),
        /// NotExecuted and Skipped are skipped. Throws when the file is not XML.
        /// </summary>
        public static PostLandTrx ReadTrx(string path)
        {
            var trx = new PostLandTrx();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var document = new XmlDocument { XmlResolver = null };
            document.LoadXml(File.ReadAllText(path));
            var ns = new XmlNamespaceManager(document.NameTable);
            ns.AddNamespace("t", "http://microsoft.com/schemas/VisualStudio/TeamTest/2010");
            var tests = document.SelectNodes("//t:UnitTest", ns);
            if (tests == null || tests.Count == 0) tests = document.SelectNodes("//UnitTest");
            var byId = new Dictionary<string, (string Class, string Method)>(StringComparer.OrdinalIgnoreCase);
            foreach (XmlNode unit in tests)
            {
                var method = unit.SelectSingleNode("./t:TestMethod", ns) ?? unit.SelectSingleNode("./TestMethod");
                if (method == null) continue;
                byId[Attribute(unit, "id")] = (Attribute(method, "className"), Attribute(method, "name"));
            }
            var results = document.SelectNodes("//t:UnitTestResult", ns);
            if (results == null || results.Count == 0) results = document.SelectNodes("//UnitTestResult");
            foreach (XmlNode result in results)
            {
                trx.Executed++;
                var outcome = Attribute(result, "outcome");
                if (Is(outcome, "Passed")) trx.Passed++;
                else if (Is(outcome, "Failed") || Is(outcome, "Error") || Is(outcome, "Timeout") || Is(outcome, "Aborted"))
                {
                    trx.Failed++;
                    if (!byId.TryGetValue(Attribute(result, "testId"), out var info)) continue;
                    var dot = info.Class.LastIndexOf('.');
                    var shortName = dot >= 0 && dot < info.Class.Length - 1 ? info.Class.Substring(dot + 1) : info.Class;
                    var label = shortName + "." + info.Method;
                    if (seen.Add(label)) trx.Failures.Add(label);
                }
                else if (Is(outcome, "NotExecuted") || Is(outcome, "Skipped")) trx.Skipped++;
            }
            return trx;
        }

        private static bool Is(string value, string expected) => string.Equals(value, expected, StringComparison.OrdinalIgnoreCase);

        private static string Attribute(XmlNode node, string name) => node.Attributes?[name]?.Value ?? "";

        /// <summary>A receipt's failures, or its newReds when it has no failures field.</summary>
        public static List<string> ReceiptFailures(JsonElement receipt)
        {
            if (TryProperty(receipt, "failures", out var failures)) return Strings(failures);
            if (TryProperty(receipt, "newReds", out var newReds)) return Strings(newReds);
            return new List<string>();
        }

        private static bool IsAncestor(string repo, string ancestor, string tip)
        {
            var git = Path.Combine(repo, ".git");
            if (!Directory.Exists(git) && !File.Exists(git)) return false;
            return Git(repo, "merge-base", "--is-ancestor", ancestor, tip).Exit == 0;
        }

        private static string CommitDate(string repo, string sha)
        {
            var ran = Git(repo, "log", "-1", "--format=%cI", sha);
            return ran.Exit != 0 ? "" : ran.Stdout.Trim();
        }

        /// <summary>
        /// The completed receipt (&lt;sha&gt;.json, not the tip's) of the ancestor of tip with the latest committer date
        /// (compared as text), and its failures. Null tip when there is none.
        /// </summary>
        public static (string Tip, List<string> Failures) Previous(string repo, string receiptRoot, string tip)
        {
            string bestSha = null;
            var bestDate = "";
            var bestFailures = new List<string>();
            if (!Directory.Exists(receiptRoot)) return (null, bestFailures);
            foreach (var file in Directory.EnumerateFiles(receiptRoot, "*.json"))
            {
                var name = Path.GetFileNameWithoutExtension(file);
                if (!IsFullSha(name) || string.Equals(name, tip, StringComparison.OrdinalIgnoreCase)) continue;
                var read = ReadJson(file);
                if (read == null || !IsState(read.Value, "completed")) continue;
                if (!IsAncestor(repo, name, tip)) continue;
                var date = CommitDate(repo, name);
                if (bestSha == null || string.CompareOrdinal(date, bestDate) > 0)
                {
                    bestSha = name;
                    bestDate = date;
                    bestFailures = ReceiptFailures(read.Value);
                }
            }
            return (bestSha, bestFailures);
        }

        /// <summary>
        /// Reads each shard's TRX under &lt;receipt-root&gt;/&lt;tip&gt; (when that directory exists) into the payload: the counts,
        /// the failures, the new reds against the previous completed ancestor, and the summary line. A missing, unreadable
        /// or zero-executed TRX makes the shard unknown. Exit 1 for a planned run with new reds or an unknown shard.
        /// </summary>
        public static int CompleteOutcome(PostLandPayload payload, string repo, string receiptRoot)
        {
            var tipDir = Path.Combine(receiptRoot, payload.TipSha ?? "");
            var all = new List<string>();
            var unknown = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var ancestor = new HashSet<string>(StringComparer.Ordinal);
            string previous = null;
            if (Directory.Exists(tipDir))
            {
                var prior = Previous(repo, receiptRoot, payload.TipSha);
                previous = prior.Tip;
                foreach (var name in prior.Failures) ancestor.Add(name);
                foreach (var shard in payload.Shards)
                {
                    if (shard == null) continue;
                    var trxPath = Path.Combine(tipDir, shard.Id + ".trx");
                    shard.Trx = trxPath;
                    if (!File.Exists(trxPath))
                    {
                        unknown.Add(shard.Id);
                        continue;
                    }
                    PostLandTrx parsed;
                    try { parsed = ReadTrx(trxPath); }
                    catch (Exception e) when (e is XmlException || e is IOException || e is UnauthorizedAccessException || e is System.Xml.XPath.XPathException)
                    {
                        unknown.Add(shard.Id);
                        continue;
                    }
                    shard.Executed = parsed.Executed;
                    shard.Passed = parsed.Passed;
                    shard.Failed = parsed.Failed;
                    shard.Skipped = parsed.Skipped;
                    if (parsed.Executed == 0)
                    {
                        unknown.Add(shard.Id);
                        continue;
                    }
                    foreach (var label in parsed.Failures)
                    {
                        if (string.IsNullOrWhiteSpace(label) || !seen.Add(label)) continue;
                        all.Add(label);
                    }
                }
            }
            var sortedNew = SortedNames(all.Where(name => !ancestor.Contains(name)));
            payload.NewReds = sortedNew;
            payload.Failures = SortedNames(all);
            payload.PreviousTipSha = previous;
            payload.SummaryLine = Summary(payload.TipSha, payload.BaseSha, sortedNew, unknown, payload.OwedWindows.Count);
            if (!string.Equals(payload.State, "run", StringComparison.OrdinalIgnoreCase)) return 0;
            return unknown.Count > 0 || sortedNew.Count > 0 ? 1 : 0;
        }

        /// <summary>
        /// Writes &lt;receipt-root&gt;/&lt;tip&gt;.json, unless a completed receipt is already there and this one is not completed.
        /// </summary>
        public static void WriteReceipt(string receiptRoot, PostLandPayload payload)
        {
            Directory.CreateDirectory(receiptRoot);
            var path = Path.Combine(receiptRoot, payload.TipSha + ".json");
            if (File.Exists(path))
            {
                var existing = ReadJson(path);
                if (existing != null && IsState(existing.Value, "completed") && !string.Equals(payload.State, "completed", StringComparison.OrdinalIgnoreCase)) return;
            }
            File.WriteAllText(path, payload.ToJson() + "\n", new UTF8Encoding(false));
        }

        // ---- the decision ------------------------------------------------------------------------------------------

        public static string ManifestPath(PostLandSettings settings) =>
            Path.Combine(settings.ProductRoot, "tests", "post-land-unit-shards.json");

        /// <summary>
        /// The plan, in order: refused for an unreadable change list; skipped-docs for a docs-only change; refused for a
        /// bad manifest; skipped-busy while another owner holds lock.json; without force, skipped-hourly inside the
        /// coalesce window and skipped-completed for a tip already completed; refused when the selector fails; else run.
        /// ownRunId is the lane's own run id (null in plan mode): a lock it holds is not busy.
        /// </summary>
        public static PostLandDecision Decide(string repo, string receiptRoot, string baseSha, string tipSha, string fileList, string table, bool force,
            PostLandSettings settings, string ownRunId, TextWriter stderr)
        {
            var manifestPath = ManifestPath(settings);
            PostLandDecision Stop(string state, string reason, int exit, bool ok = true) =>
                new PostLandDecision { Payload = Payload(state, reason, baseSha, tipSha, manifestPath, ok), Exit = exit };

            var changed = ChangedPaths(repo, baseSha, tipSha, fileList, out var error);
            if (changed == null) return Stop("refused", error, 2, ok: false);
            if (IsDocsOnly(repo, changed)) return Stop("skipped-docs", "docs-only", 0);
            var manifest = ReadManifest(manifestPath);
            if (manifest == null) return Stop("refused", "refused=bad-manifest", 2, ok: false);
            if (LockKind(Path.Combine(receiptRoot, "lock.json"), ownRunId) == "busy") return Stop("skipped-busy", "lock-held", 0);
            if (!force && IsHourly(Path.Combine(receiptRoot, "last-start.json"), manifest.CoalesceMinutes)) return Stop("skipped-hourly", "within-60-minutes", 0);
            if (!force && IsCompleted(Path.Combine(receiptRoot, tipSha + ".json"))) return Stop("skipped-completed", "already-completed", 0);
            if (settings.PlanDelayMs > 0) System.Threading.Thread.Sleep(settings.PlanDelayMs);
            var request = new PostLandSelectorRequest { Repo = repo, BaseSha = baseSha, TipSha = tipSha, FileList = fileList, Table = table, ChangedPaths = changed };
            var selection = settings.Selector != null ? settings.Selector(request) : ImpactedSelector(settings.ProductRoot, request);
            if (!selection.Ok)
            {
                stderr.WriteLine(selection.Error);
                return Stop("refused", "refused=selector", 2, ok: false);
            }
            var classes = Classes(repo);
            var owed = OwedWindows(selection.WindowsFilters);
            var payload = Payload("run", "planned", baseSha, tipSha, manifestPath);
            payload.Shards = BuildShards(manifest, classes, selection.Classes, WindowNames(selection.WindowsFilters));
            payload.OwedWindows = owed;
            payload.SummaryLine = Summary(tipSha, baseSha, null, null, owed.Count);
            var exit = CompleteOutcome(payload, repo, receiptRoot);
            return new PostLandDecision { Payload = payload, Exit = exit };
        }
    }
}
