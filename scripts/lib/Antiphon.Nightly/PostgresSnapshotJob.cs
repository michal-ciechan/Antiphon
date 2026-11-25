// The daily plain Postgres snapshot's Windmill side (unit 53 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md, W8
// slice D). It replaced the bodies of scripts/windmill/invoke-postgres-snapshot.ps1 (u/lndcobra/antiphon_postgres_snapshot, 07:50)
// and scripts/windmill/invoke-postgres-snapshot-stale.ps1 (u/lndcobra/antiphon_postgres_snapshot_stale, 08:00), which stay as thin
// launchers because the jobs start their runner copies by path, and scripts/windmill/job-status.ps1, the status-line library both
// dot-sourced (schema scripts/windmill/job-status.schema.json). The CLI commands:
//
//   postgres-snapshot-job [--runner-dir <dir>] [--record-dir <dir>] [--status-path <file>]
//   postgres-snapshot-stale [--status-path <file>] [--dump-dir <dir>] [--record-dir <dir>] [--max-age-hours 26]
//   postgres-snapshot-target <desktop|server2> [--runner-dir <dir>]
//   job-status [--job <name>] [--icon <word>] [--status ok|warn|fail|stale] [--detail <text>] [--record-path <file>] [--send]
//       [--chat-id <id>] [--sender-root <dir>] [--stale-check] [--status-file <file>] [--dump-dir <dir>] [--max-age-hours 26]
//
// The job reads the one word in &lt;runner&gt;\snapshot-target (desktop or server2; anything else fails closed, target-invalid), resolves
// the pinned postgres-snapshot.ps1 (the four source literals and a SOURCE-SHA match, as before), runs it as Windows PowerShell's
// child with -Plain -Execute -OneDriveCopy -StagingPath C:\Users\lndco\antiphon-snapshot-staging -Target &lt;word&gt;, prints its
// lines (secrets filtered), writes job-status\db-backup.json and posts one line ("DB backup: OK 0.7 GB +cloud src=server2", "DB
// backup: FAIL exit 9 source-mismatch src=server2") through ClaudeBot's tg-send.cs. Every line and exit is the scripts' (captured
// from them before their bodies were replaced). Processes run on the host's launcher; the clock and environment are the host's;
// files are the real disk. Plain C#: no PowerShell types. ASCII only. No secrets on stdout.
#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Antiphon.Nightly
{
    /// <summary>One job status record (job, icon, status and an optional detail).</summary>
    public sealed record JobStatusRecord(string Job, string Icon, string Status, string Detail);

    /// <summary>Where the job-status line goes: the chat, ClaudeBot's checkout and the dotnet to run tg-send.cs with.</summary>
    public sealed class JobStatusSender
    {
        /// <summary>Mike's DM (job-status.ps1's default).</summary>
        public const string DefaultChatId = "8738110514";

        /// <summary>ClaudeBot's checkout (job-status.ps1's default).</summary>
        public const string DefaultSenderRoot = @"C:\src\ClaudeBot";

        private readonly NightlyHost _host;
        private readonly Func<string, string> _resolve;
        private readonly Func<string> _tempDirectory;
        private bool _sent;

        /// <summary>A sender on the host's launcher; resolve is Get-Command's (null: production), tempDirectory the temp path's.</summary>
        public JobStatusSender(NightlyHost host, Func<string, string> resolve = null, Func<string> tempDirectory = null)
        {
            _host = host ?? throw new ArgumentNullException(nameof(host));
            _resolve = resolve ?? OwnedLauncher.FindApplication;
            _tempDirectory = tempDirectory ?? Path.GetTempPath;
        }

        /// <summary>
        /// Send-JobStatusLine: posts the line once per process through dotnet run &lt;senderRoot&gt;\scripts\tg-send.cs -- --to &lt;chat&gt;
        /// --text-file &lt;tmp&gt;, in senderRoot. 2 for a second call, 1 for a blank or secret line, else tg-send's exit. Its stderr
        /// passes through; its stdout (tg-send's "produced -&gt; ..." receipt) is not printed.
        /// </summary>
        public int Send(string line, string chatId = DefaultChatId, string senderRoot = DefaultSenderRoot)
        {
            if (_sent) return 2;
            if (string.IsNullOrWhiteSpace(line)) return 1;
            if (JobStatus.IsSecret(line)) return 1;
            _sent = true;
            var dir = Path.Combine(_tempDirectory(), "tg-job-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                var file = Path.Combine(dir, "msg.txt");
                File.WriteAllText(file, line, new UTF8Encoding(false));
                var dotnet = _resolve("dotnet");
                if (string.IsNullOrWhiteSpace(dotnet)) dotnet = @"C:\Program Files\dotnet\dotnet.exe";
                var sender = Path.Combine(senderRoot, "scripts", "tg-send.cs");
                var result = _host.Launch(new NightlyLaunch(dotnet, new[] { "run", sender, "--", "--to", chatId, "--text-file", file }, senderRoot, CaptureOutput: true));
                foreach (var errorLine in TextLines(result.StandardErrorText)) _host.Error.WriteLine(errorLine);
                return result.ExitCode;
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch (Exception) { /* -ErrorAction SilentlyContinue */ }
            }
        }

        private static IEnumerable<string> TextLines(string text)
        {
            if (string.IsNullOrEmpty(text)) return Array.Empty<string>();
            var body = text.EndsWith("\n", StringComparison.Ordinal) ? text.Substring(0, text.Length - 1) : text;
            return body.Split('\n');
        }
    }

    /// <summary>job-status.ps1's functions: the record, its line, its JSON file, the snapshot size and the freshness checks.</summary>
    public static class JobStatus
    {
        /// <summary>The desktop's backup root.</summary>
        public const string BackupRoot = @"C:\Users\lndco\antiphon-backups";

        /// <summary>The job-status record directory.</summary>
        public const string RecordDir = BackupRoot + @"\job-status";

        /// <summary>The plain snapshot's status file.</summary>
        public const string StatusPath = BackupRoot + @"\snapshot-status.json";

        private static readonly string[] Allowed = { "ok", "warn", "fail", "stale" };
        private static readonly Dictionary<string, string> Labels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["db-backup"] = "DB backup" };
        private const RegexOptions Ci = RegexOptions.CultureInvariant | RegexOptions.IgnoreCase;
        private static readonly Regex Secret = new Regex(@"password\s*=|postgres://|Host\s*=", Ci);
        private static readonly Regex StampPattern = new Regex(@"^(\d{4})(\d{2})(\d{2})T(\d{2})(\d{2})(\d{2})Z$", Ci);
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        internal static bool IsSecret(string text) => Secret.IsMatch(text ?? "");

        /// <summary>New-JobStatusRecord: null when a field is not allowed (status, job and icon shapes, a quote, a line break or a secret).</summary>
        public static JobStatusRecord NewRecord(string job, string icon, string status, string detail = "")
        {
            detail ??= "";
            if (!Allowed.Contains(status ?? "", StringComparer.OrdinalIgnoreCase)) return null;
            if (!Regex.IsMatch(job ?? "", "^[a-z0-9][a-z0-9-]{0,40}$", Ci)) return null;
            if (!Regex.IsMatch(icon ?? "", "^[A-Za-z0-9]{1,8}$", Ci)) return null;
            if (Regex.IsMatch(detail, "[\\r\\n\"\\\\]", Ci) || detail.Length > 120) return null;
            if (Secret.IsMatch(detail)) return null;
            return new JobStatusRecord(job, icon, status, string.IsNullOrWhiteSpace(detail) ? null : detail);
        }

        /// <summary>Format-JobStatusLine: "&lt;label&gt;: &lt;icon&gt;[ &lt;detail&gt;]" (db-backup is "DB backup").</summary>
        public static string FormatLine(JobStatusRecord record)
        {
            var label = record.Job ?? "";
            if (Labels.TryGetValue(label, out var known)) label = known;
            var line = label + ": " + record.Icon;
            if (!string.IsNullOrWhiteSpace(record.Detail)) line += " " + record.Detail;
            return line;
        }

        /// <summary>ConvertTo-JobStatusJson: the record's one-line JSON (the fields are already free of quotes and backslashes).</summary>
        public static string ToJson(JobStatusRecord record)
        {
            var json = "{\"job\":\"" + record.Job + "\",\"icon\":\"" + record.Icon + "\",\"status\":\"" + record.Status + "\"";
            if (record.Detail != null) json += ",\"detail\":\"" + record.Detail + "\"";
            return json + "}";
        }

        /// <summary>Write-JobStatusRecord: written through &lt;path&gt;.partial and renamed into an existing directory; false when refused.</summary>
        public static bool WriteRecord(JobStatusRecord record, string path)
        {
            if (record == null || string.IsNullOrWhiteSpace(path)) return false;
            var dir = Path.GetDirectoryName(path);
            if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir)) return false;
            var json = ToJson(record);
            if (Secret.IsMatch(json)) return false;
            var partial = path + ".partial";
            File.WriteAllText(partial, json, Utf8NoBom);
            if (File.Exists(path) || Directory.Exists(path)) File.Delete(path);
            File.Move(partial, path);
            return !File.Exists(partial) && File.Exists(path);
        }

        /// <summary>Format-SnapshotSize: tenths of a GB from 100 MiB up, tenths of a MB from 1 MiB, else bytes.</summary>
        public static string FormatSize(long bytes)
        {
            var culture = CultureInfo.InvariantCulture;
            if (bytes >= 100L * 1024 * 1024) return string.Format(culture, "{0:0.0} GB", bytes / (double)(1L << 30));
            if (bytes >= 1024 * 1024) return string.Format(culture, "{0:0.0} MB", bytes / (double)(1L << 20));
            return string.Format(culture, "{0} B", bytes);
        }

        /// <summary>
        /// New-PlainBackupCloudRecord: OK "&lt;size&gt; +cloud", WARN "&lt;size&gt; no cloud copy" (missing or failed) or OK "&lt;size&gt;",
        /// each with " src=&lt;target&gt;" when a target is given; null for a target that is not 1-16 lower-case letters or digits.
        /// </summary>
        public static JobStatusRecord CloudRecord(long bytes, string oneDrive = "", string target = "")
        {
            var size = FormatSize(bytes);
            var source = "";
            if (!string.IsNullOrEmpty(target))
            {
                if (!Regex.IsMatch(target, "^[a-z0-9]{1,16}$", RegexOptions.CultureInvariant)) return null;
                source = " src=" + target;
            }
            if (string.Equals(oneDrive, "ok", StringComparison.OrdinalIgnoreCase)) return NewRecord("db-backup", "OK", "ok", size + " +cloud" + source);
            if (string.Equals(oneDrive, "missing", StringComparison.OrdinalIgnoreCase) || string.Equals(oneDrive, "failed", StringComparison.OrdinalIgnoreCase))
                return NewRecord("db-backup", "WARN", "warn", size + " no cloud copy" + source);
            return NewRecord("db-backup", "OK", "ok", size + source);
        }

        /// <summary>ConvertFrom-SnapshotUtcStamp: yyyyMMddTHHmmssZ as a UTC time; null when it is not one.</summary>
        public static DateTime? ParseStamp(string stamp)
        {
            var match = StampPattern.Match(stamp ?? "");
            if (!match.Success) return null;
            try
            {
                var part = new int[6];
                for (var i = 0; i < 6; i++) part[i] = int.Parse(match.Groups[i + 1].Value, NumberStyles.None, CultureInfo.InvariantCulture);
                return new DateTime(part[0], part[1], part[2], part[3], part[4], part[5], DateTimeKind.Utc);
            }
            catch (Exception) { return null; }
        }

        /// <summary>Test-SnapshotStatusFresh: an exit-0 plain status whose utc is under maxAgeHours old (and not over an hour ahead).</summary>
        public static bool StatusFresh(string path, DateTime nowUtc, int maxAgeHours = 26)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return false;
            JsonElement? status;
            try { status = StatusJson.Read(path); } catch (Exception) { return false; }
            if (!StatusJson.TryInt(StatusJson.Get(status, "exit"), out var exitCode)) return false;
            if (exitCode != 0) return false;
            if (!string.Equals(StatusJson.Str(StatusJson.Get(status, "mode")), "plain", StringComparison.OrdinalIgnoreCase)) return false;
            var when = ParseStamp(StatusJson.Str(StatusJson.Get(status, "utc")));
            if (when == null) return false;
            var age = (DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc) - when.Value).TotalHours;
            if (age >= maxAgeHours) return false;
            if (age < -1) return false;
            return true;
        }

        /// <summary>Test-SnapshotDumpFresh: the newest antiphon-&lt;utc&gt;.dump in dir was written under maxAgeHours ago.</summary>
        public static bool DumpFresh(string dir, DateTime nowUtc, int maxAgeHours = 26)
        {
            if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir)) return false;
            DateTime? newest = null;
            FileInfo[] files;
            try { files = new DirectoryInfo(dir).GetFiles(); } catch (Exception) { return false; }
            foreach (var file in files)
            {
                if ((file.Attributes & FileAttributes.Hidden) != 0) continue;
                if (!Regex.IsMatch(file.Name, PostgresSnapshot.PlainDumpPattern, Ci)) continue;
                if (newest == null || file.LastWriteTimeUtc > newest.Value) newest = file.LastWriteTimeUtc;
            }
            if (newest == null) return false;
            var age = (DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc) - newest.Value).TotalHours;
            if (age >= maxAgeHours) return false;
            if (age < -1) return false;
            return true;
        }
    }

    /// <summary>ConvertFrom-Json and PowerShell's [int], [int64] and [string] casts over a status file's values.</summary>
    internal static class StatusJson
    {
        /// <summary>The file (UTF-8) as one JSON object; null for a blank file; throws JsonException when it does not parse.</summary>
        public static JsonElement? Read(string path)
        {
            var text = File.ReadAllText(path, Encoding.UTF8);
            if (string.IsNullOrWhiteSpace(text)) return null;
            using var document = JsonDocument.Parse(text);
            return document.RootElement.Clone();
        }

        public static JsonElement? Get(JsonElement? node, string name)
        {
            if (node == null || node.Value.ValueKind != JsonValueKind.Object) return null;
            foreach (var property in node.Value.EnumerateObject())
                if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) return property.Value;
            return null;
        }

        public static bool Has(JsonElement? node, string name) =>
            node != null && node.Value.ValueKind == JsonValueKind.Object && node.Value.EnumerateObject().Any(property => property.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

        public static string Str(JsonElement? node) => SnapshotJson.Str(node);

        /// <summary>[int]: absent or null 0, a boolean 1 or 0, a whole or rounded number, a numeric string; false when PowerShell would throw.</summary>
        public static bool TryInt(JsonElement? node, out int value)
        {
            value = 0;
            if (!TryLong(node, out var wide)) return false;
            if (wide < int.MinValue || wide > int.MaxValue) return false;
            value = (int)wide;
            return true;
        }

        /// <summary>[int64], as TryInt.</summary>
        public static bool TryLong(JsonElement? node, out long value)
        {
            value = 0;
            if (node == null) return true;
            var element = node.Value;
            switch (element.ValueKind)
            {
                case JsonValueKind.Null: return true;
                case JsonValueKind.True: value = 1; return true;
                case JsonValueKind.False: value = 0; return true;
                case JsonValueKind.Number:
                    if (element.TryGetInt64(out value)) return true;
                    return Round(element.GetDouble(), out value);
                case JsonValueKind.String:
                    var text = element.GetString().Trim();
                    if (text.Length == 0) return true;
                    if (long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value)) return true;
                    if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                        && long.TryParse(text.Substring(2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out value)) return true;
                    if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)) return Round(number, out value);
                    return false;
                default: return false;
            }
        }

        private static bool Round(double number, out long value)
        {
            value = 0;
            if (double.IsNaN(number) || double.IsInfinity(number)) return false;
            var rounded = Math.Round(number, MidpointRounding.ToEven);
            if (rounded < long.MinValue || rounded > long.MaxValue) return false;
            value = (long)rounded;
            return true;
        }
    }

    /// <summary>The postgres-snapshot-job command's options (Invoke-PlainSnapshotJob's parameters).</summary>
    public sealed class PostgresSnapshotJobOptions
    {
        /// <summary>The CLI's usage line.</summary>
        public const string Usage = "postgres-snapshot-job [--runner-dir <dir>] [--record-dir C:\\Users\\lndco\\antiphon-backups\\job-status] "
            + "[--status-path C:\\Users\\lndco\\antiphon-backups\\snapshot-status.json]";

        /// <summary>The runner copy's directory (snapshot-target, SOURCE-SHA and its own postgres-snapshot.ps1 candidate).</summary>
        public string RunnerDir { get; init; } = "";

        /// <summary>Where db-backup.json goes.</summary>
        public string RecordDir { get; init; } = JobStatus.RecordDir;

        /// <summary>The plain snapshot's status file.</summary>
        public string StatusPath { get; init; } = JobStatus.StatusPath;

        /// <summary>The flags; a blank --runner-dir means the current directory (the launcher passes its own directory).</summary>
        public static PostgresSnapshotJobOptions Parse(IReadOnlyList<string> arguments, string currentDirectory)
        {
            var flags = NightlyArgs.Parse(arguments, new[] { "--runner-dir", "--record-dir", "--status-path" }, Array.Empty<string>());
            var runner = flags.Value("--runner-dir", "");
            return new PostgresSnapshotJobOptions
            {
                RunnerDir = string.IsNullOrWhiteSpace(runner) ? currentDirectory ?? "" : runner,
                RecordDir = NonBlank(flags.Value("--record-dir"), JobStatus.RecordDir),
                StatusPath = NonBlank(flags.Value("--status-path"), JobStatus.StatusPath),
            };
        }

        internal static string NonBlank(string value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value;
    }

    /// <summary>The job's seams beyond the host: the candidate list, the child's file system root and the sender's.</summary>
    public sealed class PostgresSnapshotJobSeams
    {
        /// <summary>The checkouts' postgres-snapshot.ps1 the resolver tries, before and after the runner copy's own (null: the script's list).</summary>
        public IReadOnlyList<string> Candidates { get; init; }

        /// <summary>Get-Command's application (dotnet for the sender).</summary>
        public Func<string, string> Resolve { get; init; } = OwnedLauncher.FindApplication;

        /// <summary>The temporary directory for the sender's message file.</summary>
        public Func<string> TempDirectory { get; init; } = Path.GetTempPath;

        /// <summary>The chat the line goes to.</summary>
        public string ChatId { get; init; } = JobStatusSender.DefaultChatId;

        /// <summary>ClaudeBot's checkout.</summary>
        public string SenderRoot { get; init; } = JobStatusSender.DefaultSenderRoot;
    }

    /// <summary>The Windmill job and its helpers (invoke-postgres-snapshot.ps1 and invoke-postgres-snapshot-stale.ps1).</summary>
    public static class PostgresSnapshotJob
    {
        /// <summary>The staging the scheduled child uses (the profile directory's parents pass CARD-1413; Local\antiphon does not).</summary>
        public const string Staging = @"C:\Users\lndco\antiphon-snapshot-staging";

        /// <summary>The main checkout's launcher, the first candidate.</summary>
        public const string MainCheckoutScript = @"C:\src\Antiphon\scripts\postgres-snapshot.ps1";

        /// <summary>The CARD-1831 worktree's launcher, the last candidate.</summary>
        public const string WorktreeScript = @"C:\Antiphon\worktrees\card-task-300d724e\scripts\postgres-snapshot.ps1";

        /// <summary>The literals a candidate must contain (the plain switch, the decision's name and date, the OneDrive switch).</summary>
        public static readonly IReadOnlyList<string> SourceLiterals = new[] { "[switch]$Plain", "SnapshotPlainDecision", "2026-11-10", "[switch]$OneDriveCopy" };

        private static readonly Regex TargetText = new Regex("^(desktop|server2)(\\r?\\n)?\\z", RegexOptions.CultureInvariant);
        private static readonly Regex Pin = new Regex("^[0-9a-f]{64}$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        private static readonly Regex FailLine = new Regex("^SNAPSHOT fail exit=([0-9]+) reason=([a-z-]{1,40})( [a-z0-9-]+=[^ =]*)*$", RegexOptions.CultureInvariant);
        private static readonly Regex AnyFail = new Regex("^SNAPSHOT fail ", RegexOptions.CultureInvariant);
        private static readonly Regex Unsafe = new Regex(@"password\s*=|postgres://|Bearer |token=", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        /// <summary>Get-SnapshotTarget: desktop or server2 (exactly, at most one trailing line break), else ''.</summary>
        public static string ReadTarget(string dir)
        {
            var path = Path.Combine(dir ?? "", "snapshot-target");
            if (!File.Exists(path)) return "";
            var match = TargetText.Match(File.ReadAllText(path));
            return match.Success ? match.Groups[1].Value : "";
        }

        /// <summary>
        /// Set-SnapshotTarget: writes the word (ASCII, no BOM, no line break) into &lt;dir&gt;\snapshot-target and reads it back. The lines
        /// are the function's; the answer is the word, or '' when refused or the read-back differs.
        /// </summary>
        public static string WriteTarget(string word, string dir, TextWriter output)
        {
            if (!(string.Equals(word, "desktop", StringComparison.Ordinal) || string.Equals(word, "server2", StringComparison.Ordinal)))
            {
                output.WriteLine("SNAPSHOT TARGET refused (desktop or server2)");
                return "";
            }
            if (!Directory.Exists(dir))
            {
                output.WriteLine("SNAPSHOT TARGET refused (runner directory missing)");
                return "";
            }
            File.WriteAllText(Path.Combine(dir, "snapshot-target"), word, new ASCIIEncoding());
            var read = ReadTarget(dir);
            if (!string.Equals(read, word, StringComparison.Ordinal))
            {
                output.WriteLine("SNAPSHOT TARGET mismatch");
                return "";
            }
            output.WriteLine("SNAPSHOT TARGET " + read);
            return read;
        }

        /// <summary>The script's candidate list: the main checkout, the runner copy, the CARD-1831 worktree.</summary>
        public static IReadOnlyList<string> DefaultCandidates(string runnerDir) => new[] { MainCheckoutScript, Path.Combine(runnerDir, "postgres-snapshot.ps1"), WorktreeScript };

        /// <summary>
        /// Resolve-PlainSnapshotScript: the first candidate whose text holds the four literals and whose SHA-256 equals &lt;runner&gt;\SOURCE-SHA
        /// (64 hex digits, trimmed, any case); its path and hash, or ('', '').
        /// </summary>
        public static (string Path, string Sha) ResolveScript(IReadOnlyList<string> candidates, string pinFile)
        {
            if (string.IsNullOrWhiteSpace(pinFile) || !File.Exists(pinFile)) return ("", "");
            var pinned = File.ReadAllText(pinFile).Trim().ToLowerInvariant();
            if (!Pin.IsMatch(pinned)) return ("", "");
            foreach (var path in candidates ?? Array.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) continue;
                var text = File.ReadAllText(path);
                if (!SourceLiterals.All(literal => text.Contains(literal, StringComparison.Ordinal))) continue;
                string hash;
                using (var stream = File.OpenRead(path)) hash = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
                if (hash != pinned) continue;
                return (path, hash);
            }
            return ("", "");
        }

        /// <summary>Write-SafeSnapshotText: each line of text without a secret, up to max lines ('' text: none).</summary>
        public static IReadOnlyList<string> SafeText(string text, int max = 80)
        {
            var lines = new List<string>();
            if (string.IsNullOrWhiteSpace(text)) return lines;
            foreach (var line in Regex.Split(text, "\r?\n"))
            {
                if (Unsafe.IsMatch(line)) continue;
                lines.Add(line);
                if (lines.Count >= max) break;
            }
            return lines;
        }

        /// <summary>The child's arguments (Get-PlainSnapshotChildArguments).</summary>
        public static IReadOnlyList<string> ChildArguments(string snapshot, string target) => new[]
        {
            "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", snapshot, "-Plain", "-Execute", "-OneDriveCopy", "-StagingPath", Staging, "-Target", target,
        };

        /// <summary>Get-PlainSnapshotFailDetail: "exit &lt;n&gt;[ &lt;reason&gt;] src=&lt;target&gt;", the reason from the child's own SNAPSHOT fail line for that exit.</summary>
        public static string FailDetail(int code, string text, string target)
        {
            var reason = "";
            foreach (var line in Regex.Split(text ?? "", "\r?\n"))
            {
                var match = FailLine.Match(line);
                if (match.Success)
                {
                    if (long.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var exit) && exit == code) reason = match.Groups[2].Value;
                    break;
                }
                if (AnyFail.IsMatch(line)) break;
            }
            var detail = "exit " + code.ToString(CultureInfo.InvariantCulture);
            if (reason != "") detail += " " + reason;
            return detail + " src=" + target;
        }

        /// <summary>
        /// Invoke-PlainSnapshotJob: the job's lines (printed once the job is done, as the script printed them) and exit: the child's
        /// code, 9 target-invalid, 1 missing-script, or the send's code when the child passed and the send did not.
        /// </summary>
        public static int Run(PostgresSnapshotJobOptions options, NightlyHost host, PostgresSnapshotJobSeams seams = null)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (host == null) throw new ArgumentNullException(nameof(host));
            seams ??= new PostgresSnapshotJobSeams();
            var lines = new List<string>();
            int exit;
            try
            {
                exit = Job(options, host, seams, lines);
            }
            catch (Exception e)
            {
                // The script's terminating errors (ErrorActionPreference Stop) ended it before its lines were printed.
                host.Error.WriteLine("postgres-snapshot-job: " + e.Message.Replace("\r", " ").Replace("\n", " "));
                return 1;
            }
            foreach (var line in lines) host.Out.WriteLine(line);
            return exit;
        }

        private static int Job(PostgresSnapshotJobOptions options, NightlyHost host, PostgresSnapshotJobSeams seams, List<string> output)
        {
            if (!Directory.Exists(options.RecordDir) && !File.Exists(options.RecordDir)) Directory.CreateDirectory(options.RecordDir);
            var recordPath = Path.Combine(options.RecordDir, "db-backup.json");
            var code = 1;
            var target = ReadTarget(options.RunnerDir);
            var detail = "";
            if (target == "")
            {
                // Fail closed: a missing or unknown word must not pick a database.
                code = 9;
                output.Add("SNAPSHOT fail exit=9 reason=target-invalid");
                detail = "exit 9 target-invalid src=none";
            }
            else
            {
                output.Add("snapshot-target=" + target);
                var resolved = ResolveScript(seams.Candidates ?? DefaultCandidates(options.RunnerDir), Path.Combine(options.RunnerDir, "SOURCE-SHA"));
                if (string.IsNullOrWhiteSpace(resolved.Path))
                {
                    output.Add("SNAPSHOT fail exit=1 reason=missing-script");
                    detail = "exit 1 missing-script src=" + target;
                }
                else
                {
                    output.Add("snapshot-script=" + resolved.Path);
                    if (!string.IsNullOrWhiteSpace(resolved.Sha)) output.Add("source-sha=" + resolved.Sha);
                    var powershell = Path.Combine(host.Variables("SystemRoot") ?? "", "System32", "WindowsPowerShell", "v1.0", "powershell.exe");
                    var ran = host.Launch(new NightlyLaunch(powershell, ChildArguments(resolved.Path, target), "", CaptureOutput: true));
                    code = ran.ExitCode;
                    output.AddRange(SafeText(ran.StandardOutputText));
                    if (code != 0)
                    {
                        output.AddRange(SafeText(ran.StandardErrorText, 40));
                        detail = FailDetail(code, ran.StandardOutputText, target);
                    }
                }
            }

            JobStatusRecord record = null;
            if (code == 0)
            {
                long bytes = -1;
                JsonElement? status = null;
                if (File.Exists(options.StatusPath) || Directory.Exists(options.StatusPath))
                {
                    try
                    {
                        status = StatusJson.Read(options.StatusPath);
                        if (!StatusJson.TryInt(StatusJson.Get(status, "exit"), out var statusExit)) throw new FormatException("exit");
                        if (statusExit == 0 && string.Equals(StatusJson.Str(StatusJson.Get(status, "mode")), "plain", StringComparison.OrdinalIgnoreCase))
                        {
                            if (!StatusJson.TryLong(StatusJson.Get(status, "bytes"), out bytes)) throw new FormatException("bytes");
                        }
                    }
                    catch (Exception) { bytes = -1; }
                }
                if (bytes >= 0)
                {
                    var oneDrive = StatusJson.Has(status, "oneDrive") ? StatusJson.Str(StatusJson.Get(status, "oneDrive")) : "";
                    record = JobStatus.CloudRecord(bytes, oneDrive, target);
                }
                if (record == null) detail = "exit 0 src=" + target;
            }
            record ??= JobStatus.NewRecord("db-backup", "FAIL", "fail", detail);
            if (record == null)
            {
                output.Add("JOB STATUS bad-record");
                return 1;
            }
            JobStatus.WriteRecord(record, recordPath);
            var statusLine = JobStatus.FormatLine(record);
            output.Add(statusLine);
            var sent = new JobStatusSender(host, seams.Resolve, seams.TempDirectory).Send(statusLine, seams.ChatId, seams.SenderRoot);
            output.Add("telegram=" + sent.ToString(CultureInfo.InvariantCulture));
            return sent != 0 && code == 0 ? sent : code;
        }

        /// <summary>
        /// invoke-postgres-snapshot-stale.ps1: "JOB STATUS fresh" (exit 0) when a plain success or a plain dump is under 26 hours old;
        /// else the STALE record in &lt;recordDir&gt;\db-backup-stale.json, its line, and the send's exit.
        /// </summary>
        public static int Stale(PostgresSnapshotStaleOptions options, NightlyHost host, PostgresSnapshotJobSeams seams = null)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (host == null) throw new ArgumentNullException(nameof(host));
            seams ??= new PostgresSnapshotJobSeams();
            try
            {
                var now = host.UtcNow;
                var fresh = JobStatus.StatusFresh(options.StatusPath, now, options.MaxAgeHours) || JobStatus.DumpFresh(options.DumpDir, now, options.MaxAgeHours);
                if (fresh)
                {
                    host.Out.WriteLine("JOB STATUS fresh");
                    return 0;
                }
                var record = JobStatus.NewRecord("db-backup", "STALE", "stale");
                if (!Directory.Exists(options.RecordDir) && !File.Exists(options.RecordDir)) Directory.CreateDirectory(options.RecordDir);
                JobStatus.WriteRecord(record, Path.Combine(options.RecordDir, "db-backup-stale.json"));
                var line = JobStatus.FormatLine(record);
                host.Out.WriteLine(line);
                return new JobStatusSender(host, seams.Resolve, seams.TempDirectory).Send(line, seams.ChatId, seams.SenderRoot);
            }
            catch (Exception e)
            {
                host.Error.WriteLine("postgres-snapshot-stale: " + e.Message.Replace("\r", " ").Replace("\n", " "));
                return 1;
            }
        }
    }

    /// <summary>The postgres-snapshot-stale command's options (the stale script's fixed paths, overridable for tests).</summary>
    public sealed class PostgresSnapshotStaleOptions
    {
        /// <summary>The CLI's usage line.</summary>
        public const string Usage = "postgres-snapshot-stale [--status-path <file>] [--dump-dir <dir>] [--record-dir <dir>] [--max-age-hours 26]";

        /// <summary>The plain snapshot's status file.</summary>
        public string StatusPath { get; init; } = JobStatus.StatusPath;

        /// <summary>The plain dumps' directory.</summary>
        public string DumpDir { get; init; } = JobStatus.BackupRoot;

        /// <summary>Where db-backup-stale.json goes.</summary>
        public string RecordDir { get; init; } = JobStatus.RecordDir;

        /// <summary>The freshness window.</summary>
        public int MaxAgeHours { get; init; } = 26;

        /// <summary>The flags (blank values keep the defaults).</summary>
        public static PostgresSnapshotStaleOptions Parse(IReadOnlyList<string> arguments)
        {
            var flags = NightlyArgs.Parse(arguments, new[] { "--status-path", "--dump-dir", "--record-dir", "--max-age-hours" }, Array.Empty<string>());
            return new PostgresSnapshotStaleOptions
            {
                StatusPath = PostgresSnapshotJobOptions.NonBlank(flags.Value("--status-path"), JobStatus.StatusPath),
                DumpDir = PostgresSnapshotJobOptions.NonBlank(flags.Value("--dump-dir"), JobStatus.BackupRoot),
                RecordDir = PostgresSnapshotJobOptions.NonBlank(flags.Value("--record-dir"), JobStatus.RecordDir),
                MaxAgeHours = flags.Int("--max-age-hours", 26),
            };
        }
    }

    /// <summary>The job-status command (job-status.ps1 run as a script) and its options.</summary>
    public sealed class JobStatusOptions
    {
        /// <summary>The CLI's usage line.</summary>
        public const string Usage = "job-status [--job <name>] [--icon <word>] [--status ok|warn|fail|stale] [--detail <text>] [--record-path <file>] [--send] "
            + "[--chat-id <id>] [--sender-root <dir>] [--stale-check] [--status-file <file>] [--dump-dir <dir>] [--max-age-hours 26]";

        /// <summary>-Job.</summary>
        public string Job { get; init; } = "";

        /// <summary>-Icon.</summary>
        public string Icon { get; init; } = "";

        /// <summary>-Status.</summary>
        public string Status { get; init; } = "";

        /// <summary>-Detail.</summary>
        public string Detail { get; init; } = "";

        /// <summary>-RecordPath.</summary>
        public string RecordPath { get; init; } = "";

        /// <summary>-Send.</summary>
        public bool Send { get; init; }

        /// <summary>-ChatId.</summary>
        public string ChatId { get; init; } = JobStatusSender.DefaultChatId;

        /// <summary>-SenderRoot.</summary>
        public string SenderRoot { get; init; } = JobStatusSender.DefaultSenderRoot;

        /// <summary>-StaleCheck.</summary>
        public bool StaleCheck { get; init; }

        /// <summary>-StatusFile (blank: the snapshot's status file).</summary>
        public string StatusFile { get; init; } = JobStatus.StatusPath;

        /// <summary>-DumpDir (blank: the backup root).</summary>
        public string DumpDir { get; init; } = JobStatus.BackupRoot;

        /// <summary>-MaxAgeHours.</summary>
        public int MaxAgeHours { get; init; } = 26;

        /// <summary>The stale record's directory (fixed in the script).</summary>
        public string RecordDir { get; init; } = JobStatus.RecordDir;

        /// <summary>The flags.</summary>
        public static JobStatusOptions Parse(IReadOnlyList<string> arguments)
        {
            var flags = NightlyArgs.Parse(arguments,
                new[] { "--job", "--icon", "--status", "--detail", "--record-path", "--chat-id", "--sender-root", "--status-file", "--dump-dir", "--max-age-hours" },
                new[] { "--send", "--stale-check" });
            return new JobStatusOptions
            {
                Job = flags.Value("--job", ""),
                Icon = flags.Value("--icon", ""),
                Status = flags.Value("--status", ""),
                Detail = flags.Value("--detail", ""),
                RecordPath = flags.Value("--record-path", ""),
                Send = flags.Switch("--send"),
                ChatId = flags.Value("--chat-id", JobStatusSender.DefaultChatId),
                SenderRoot = flags.Value("--sender-root", JobStatusSender.DefaultSenderRoot),
                StaleCheck = flags.Switch("--stale-check"),
                StatusFile = PostgresSnapshotJobOptions.NonBlank(flags.Value("--status-file"), JobStatus.StatusPath),
                DumpDir = PostgresSnapshotJobOptions.NonBlank(flags.Value("--dump-dir"), JobStatus.BackupRoot),
                MaxAgeHours = flags.Int("--max-age-hours", 26),
            };
        }

        /// <summary>
        /// job-status.ps1 as a script: with --stale-check the stale check (fresh exit 0; else the STALE record, its line and, with
        /// --send, the send's exit); otherwise the record's line (exit 0, or the send's), "JOB STATUS bad-record" exit 2 when refused.
        /// </summary>
        public static int Run(JobStatusOptions options, NightlyHost host, PostgresSnapshotJobSeams seams = null)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (host == null) throw new ArgumentNullException(nameof(host));
            seams ??= new PostgresSnapshotJobSeams();
            try
            {
                var sender = new JobStatusSender(host, seams.Resolve, seams.TempDirectory);
                if (options.StaleCheck)
                {
                    var now = host.UtcNow;
                    if (JobStatus.StatusFresh(options.StatusFile, now, options.MaxAgeHours) || JobStatus.DumpFresh(options.DumpDir, now, options.MaxAgeHours))
                    {
                        host.Out.WriteLine("JOB STATUS fresh");
                        return 0;
                    }
                    var stale = JobStatus.NewRecord("db-backup", "STALE", "stale");
                    if (!Directory.Exists(options.RecordDir) && !File.Exists(options.RecordDir)) Directory.CreateDirectory(options.RecordDir);
                    JobStatus.WriteRecord(stale, Path.Combine(options.RecordDir, "db-backup-stale.json"));
                    var staleLine = JobStatus.FormatLine(stale);
                    host.Out.WriteLine(staleLine);
                    return options.Send ? sender.Send(staleLine, options.ChatId, options.SenderRoot) : 0;
                }
                var made = JobStatus.NewRecord(options.Job, options.Icon, options.Status, options.Detail);
                if (made == null)
                {
                    host.Out.WriteLine("JOB STATUS bad-record");
                    return 2;
                }
                if (!string.IsNullOrWhiteSpace(options.RecordPath))
                {
                    var parent = Path.GetDirectoryName(options.RecordPath);
                    if (!string.IsNullOrWhiteSpace(parent) && !Directory.Exists(parent) && !File.Exists(parent)) Directory.CreateDirectory(parent);
                    JobStatus.WriteRecord(made, options.RecordPath);
                }
                var line = JobStatus.FormatLine(made);
                host.Out.WriteLine(line);
                return options.Send ? sender.Send(line, options.ChatId, options.SenderRoot) : 0;
            }
            catch (Exception e)
            {
                host.Error.WriteLine("job-status: " + e.Message.Replace("\r", " ").Replace("\n", " "));
                return 1;
            }
        }
    }
}
