// CARD-1987: the read-only pg_stat_statements rate over a window. It replaced scripts/pg-qps.ps1 (unit 46 of
// docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md, W8 slice A; nothing started the script by path); the CLI runs it as
//
//   antiphon-scripts pg-qps [--seconds <10..3600>] [--container <name> | --connection-env <variable>] [--database <name>]
//       [--user <name>] [--top <1..1000>] [--json] [--signal-file <file>]
//
// (scripts\antiphon.cmd pg-qps on the desktop, scripts/antiphon pg-qps on sh). It takes one counter snapshot (the section of
// server/Infrastructure/Data/PgStatStatementsSnapshot.sql before "-- @@split@@", tagged "-- rt.diag.pgss"), writes "ready" to
// --signal-file, waits --seconds, takes a second snapshot, fetches the text of every statement that ran in the window (the
// section after the split, at most 500 query ids per query), and prints the rates: one text summary (or one JSON line with
// --json) of the totals, the operations (a statement's leading "-- <tag>" line, "tx.<verb>", "redacted-utility" for anything
// naming a password, role, user, connection, secret or token, else "untagged") and the top statements by calls and by time.
// It never resets statistics and every query runs read-only.
//
// The database: by default the AppHost user secret ConnectionStrings:DefaultConnection, pinned to the running server2
// container that publishes it (DesktopPostgres, through the antiphon-server2 Docker context); --container a local or test
// container (DesktopPostgres, plain docker); --connection-env the connection string in that environment variable, which goes
// to a psql child only as PG* variables (the seams' Psql) and is never printed. No secret, connection string or child
// diagnostic is ever printed.
//
// Exit 0 done; 2 bad arguments ("bad arguments" on stdout; NightlyUsageException for a malformed command line); 3 the database
// could not be reached, the extension is not loaded or a snapshot came back empty (a resolution or Docker reason line, then
// "database unreachable or extension not loaded"); 4 partial (statistics were reset or entries deallocated in the window: the
// totals are printed without statements); 1 the signal file could not be written. The waits and the window clock are the
// host's (Sleep, Clock), docker the host's launcher, files IFileSystem's, lines host.Out's. Plain C#: no PowerShell types.
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
using System.Text;
using System.Text.RegularExpressions;

namespace Antiphon.Nightly
{
    /// <summary>The pg-qps command's options (pg-qps.ps1's parameters).</summary>
    public sealed class PgQpsOptions
    {
        /// <summary>The CLI's usage line.</summary>
        public const string Usage = "pg-qps [--seconds <10..3600>] [--container <name> | --connection-env <variable>] [--database antiphon] [--user antiphon]"
            + " [--top <1..1000>] [--json] [--signal-file <file>]";

        /// <summary>The window between the two snapshots.</summary>
        public int Seconds { get; init; } = 60;

        /// <summary>A local or test PostgreSQL container instead of the AppHost's database (empty: the AppHost's).</summary>
        public string Container { get; init; } = "";

        /// <summary>The environment variable holding a connection string for a psql child (empty: Docker).</summary>
        public string ConnectionEnv { get; init; } = "";

        /// <summary>The database for --container (and the --connection-env default).</summary>
        public string Database { get; init; } = "antiphon";

        /// <summary>The user for --container (and the --connection-env default).</summary>
        public string User { get; init; } = "antiphon";

        /// <summary>How many statements each top list holds.</summary>
        public int Top { get; init; } = 20;

        /// <summary>One JSON line instead of the text summary.</summary>
        public bool Json { get; init; }

        /// <summary>A file that gets "ready" once the first snapshot is taken (empty: none).</summary>
        public string SignalFile { get; init; } = "";

        /// <summary>The checkout whose server/Infrastructure/Data/PgStatStatementsSnapshot.sql is read.</summary>
        public string RepoRoot { get; init; } = "";

        /// <summary>The arguments after the command name; repoRoot is the checkout the CLI was built in.</summary>
        public static PgQpsOptions Parse(IReadOnlyList<string> arguments, string repoRoot)
        {
            var flags = NightlyArgs.Parse(arguments, new[] { "--seconds", "--container", "--connection-env", "--database", "--user", "--top", "--signal-file" },
                new[] { "--json" });
            return new PgQpsOptions
            {
                Seconds = flags.Int("--seconds", 60),
                Container = flags.Value("--container", ""),
                ConnectionEnv = flags.Value("--connection-env", ""),
                Database = flags.Value("--database", "antiphon"),
                User = flags.Value("--user", "antiphon"),
                Top = flags.Int("--top", 20),
                Json = flags.Switch("--json"),
                SignalFile = flags.Value("--signal-file", ""),
                RepoRoot = repoRoot ?? "",
            };
        }
    }

    /// <summary>What one psql child answered: its exit code and stdout.</summary>
    public sealed record PgQpsPsqlResult(int ExitCode, string Output);

    /// <summary>The pg-qps seams beyond the host.</summary>
    public sealed class PgQpsSeams
    {
        /// <summary>
        /// The --connection-env query: psql -X -A -t -v ON_ERROR_STOP=1 with this process's environment plus the settings (PGHOST,
        /// PGPORT, PGUSER, PGDATABASE, PGPASSWORD, PGOPTIONS), the SQL on stdin; stderr is read and dropped. Null when psql did
        /// not start or timed out. Production: PgQps.Psql.
        /// </summary>
        public Func<IReadOnlyDictionary<string, string>, string, PgQpsPsqlResult> Psql { get; init; }
    }

    /// <summary>The pg_stat_statements rate (pg-qps.ps1).</summary>
    public static class PgQps
    {
        /// <summary>Exit 0: the rates were printed.</summary>
        public const int Done = 0;

        /// <summary>Exit 1: the signal file could not be written.</summary>
        public const int Failed = 1;

        /// <summary>Exit 2: bad arguments.</summary>
        public const int BadArguments = 2;

        /// <summary>Exit 3: the database could not be reached or the extension is not loaded.</summary>
        public const int Unreachable = 3;

        /// <summary>Exit 4: the window was partial (a reset or deallocation).</summary>
        public const int Partial = 4;

        /// <summary>The tag on every query this tool sends.</summary>
        public const string Tag = "-- rt.diag.pgss";

        /// <summary>The fixed line of exit 3.</summary>
        public const string UnreachableLine = "database unreachable or extension not loaded";

        /// <summary>How many query ids one text query names.</summary>
        public const int TextBatch = 500;

        private const string Split = "-- @@split@@";
        private static readonly Regex Name = new Regex(@"\A[A-Za-z0-9_]+\z", RegexOptions.CultureInvariant);
        private static readonly Regex ContainerName = new Regex(@"\A[A-Za-z0-9][A-Za-z0-9_.-]*\z", RegexOptions.CultureInvariant);
        private static readonly Regex Variable = new Regex(@"\A[A-Za-z_][A-Za-z0-9_]*\z", RegexOptions.CultureInvariant);
        private static readonly Regex QueryId = new Regex(@"\A-?[0-9]+\z", RegexOptions.CultureInvariant);
        private static readonly Regex Sensitive = new Regex(@"\b(password|alter\s+(role|user)|create\s+(role|user)|connection|secret|token)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex Tagged = new Regex(@"^-- ([a-z0-9.\-]{1,64})(\r?\n|$)", RegexOptions.CultureInvariant);
        private static readonly Regex Transaction = new Regex(@"^(begin|commit|rollback|release|savepoint)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex Operation = new Regex(@"^[a-z0-9.\-]{1,64}$", RegexOptions.CultureInvariant);
        private static readonly Regex Space = new Regex(@"\s+", RegexOptions.CultureInvariant);

        private sealed class UnreachableException : Exception
        {
            public UnreachableException(string detail) : base(detail ?? "") => Detail = detail;

            public string Detail { get; }
        }

        private sealed record Row(string VersionNum, string Version, long Dealloc, string StatsReset, string QueryId, long Calls, double Ms, long Rows);

        private sealed class Totals
        {
            public long Calls;
            public double Ms;
            public long Rows;
        }

        private sealed record Statement(string Label, long Calls, double Ms, long Rows, double Mean, double Qps, double MsPer, double RowsPer);

        /// <summary>Takes the two snapshots and prints the rates to host.Out. Returns the exit code.</summary>
        public static int Run(PgQpsOptions options, NightlyHost host, IFileSystem files, PgQpsSeams seams)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (host == null) throw new ArgumentNullException(nameof(host));
            if (files == null) throw new ArgumentNullException(nameof(files));
            if (seams == null) throw new ArgumentNullException(nameof(seams));
            if (!Valid(options))
            {
                host.Out.WriteLine("bad arguments");
                return BadArguments;
            }
            try
            {
                return Measure(options, host, files, seams);
            }
            catch (UnreachableException e)
            {
                if (!string.IsNullOrEmpty(e.Detail)) host.Out.WriteLine(e.Detail);
                host.Out.WriteLine(UnreachableLine);
                return Unreachable;
            }
        }

        private static bool Valid(PgQpsOptions o)
        {
            if (o.Seconds < 10 || o.Seconds > 3600) return false;
            if (o.Top < 1 || o.Top > 1000) return false;
            if (!Name.IsMatch(o.Database ?? "") || !Name.IsMatch(o.User ?? "")) return false;
            var useEnv = !string.IsNullOrEmpty(o.ConnectionEnv);
            if (useEnv && !string.IsNullOrEmpty(o.Container)) return false;
            if (!string.IsNullOrEmpty(o.Container) && !ContainerName.IsMatch(o.Container)) return false;
            if (useEnv && !Variable.IsMatch(o.ConnectionEnv)) return false;
            return true;
        }

        private static int Measure(PgQpsOptions options, NightlyHost host, IFileSystem files, PgQpsSeams seams)
        {
            var (counters, text) = SqlParts(options.RepoRoot, files);
            var query = string.IsNullOrEmpty(options.ConnectionEnv) ? DockerQuery(options, host) : EnvironmentQuery(options, host, seams);

            var started = host.Clock.GetTimestamp();
            var first = Rows(query(counters));
            if (first.Count == 0) throw new UnreachableException(null);
            if (!string.IsNullOrEmpty(options.SignalFile))
            {
                try
                {
                    files.File.WriteAllText(options.SignalFile, "ready");
                }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException || e is NotSupportedException)
                {
                    host.Error.WriteLine("pg-qps: the signal file could not be written: " + e.Message);
                    return Failed;
                }
            }
            host.Sleep(TimeSpan.FromSeconds(options.Seconds));
            var snapshotStarted = host.Clock.GetTimestamp();
            var secondRaw = query(counters);
            var snapshotMs = (int)host.Clock.GetElapsedTime(snapshotStarted).TotalMilliseconds;
            var elapsed = host.Clock.GetElapsedTime(started).TotalSeconds;
            var second = Rows(secondRaw);
            if (second.Count == 0) throw new UnreachableException(null);

            var meta = second[second.Count - 1];
            var beforeMeta = first[0];
            var partial = false;
            var reason = "";
            if (meta.Dealloc > beforeMeta.Dealloc)
            {
                partial = true;
                reason = "dealloc";
            }
            else if (!string.Equals(meta.StatsReset, beforeMeta.StatsReset, StringComparison.OrdinalIgnoreCase))
            {
                partial = true;
                reason = "stats-reset";
            }

            var before = ById(first);
            var after = ById(second);
            if (!partial && before.Any(pair => !after.TryGetValue(pair.Key, out var now) || now.Calls < pair.Value.Calls))
            {
                partial = true;
                reason = "stats-reset";
            }

            if (elapsed <= 0) elapsed = options.Seconds;
            int.TryParse(meta.VersionNum, NumberStyles.Integer, CultureInfo.InvariantCulture, out var versionNum);

            var operations = new Dictionary<string, Totals>(StringComparer.Ordinal);
            var statements = new List<Statement>();
            var total = new Totals();
            if (!partial)
            {
                var need = after.Where(pair => pair.Value.Calls - (before.TryGetValue(pair.Key, out var was) ? was.Calls : 0) > 0).Select(pair => pair.Key).ToList();
                var texts = Texts(text, need, query);
                foreach (var id in need)
                {
                    before.TryGetValue(id, out var was);
                    var now = after[id];
                    var calls = Math.Max(0, now.Calls - (was?.Calls ?? 0));
                    var ms = Math.Max(0, now.Ms - (was?.Ms ?? 0));
                    var rows = Math.Max(0, now.Rows - (was?.Rows ?? 0));
                    var label = Label(texts.TryGetValue(id, out var sql) ? sql : "");
                    var operation = OperationOf(label);
                    if (!operations.TryGetValue(operation, out var totals)) operations[operation] = totals = new Totals();
                    totals.Calls += calls;
                    totals.Ms += ms;
                    totals.Rows += rows;
                    total.Calls += calls;
                    total.Ms += ms;
                    total.Rows += rows;
                    statements.Add(new Statement(label, calls, ms, rows, calls > 0 ? ms / calls : 0, calls / elapsed, ms / elapsed, rows / elapsed));
                }
            }

            var names = operations.Keys.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ThenBy(n => n, StringComparer.Ordinal).ToList();
            var byCalls = statements.OrderByDescending(s => s.Calls).ThenBy(s => s.Label, StringComparer.OrdinalIgnoreCase).Take(options.Top).ToList();
            var byTime = statements.OrderByDescending(s => s.Ms).ThenBy(s => s.Label, StringComparer.OrdinalIgnoreCase).Take(options.Top).ToList();
            if (options.Json)
            {
                var json = new StringBuilder("{");
                json.Append("\"windowSeconds\":").Append(Num(elapsed)).Append(',');
                json.Append(partial ? "\"partial\":true," : "\"partial\":false,");
                json.Append("\"reason\":\"").Append(Escape(reason)).Append("\",");
                json.Append("\"total\":{\"qps\":").Append(Num(total.Calls / elapsed)).Append(",\"dbMsPerSecond\":").Append(Num(total.Ms / elapsed))
                    .Append(",\"rowsPerSecond\":").Append(Num(total.Rows / elapsed)).Append(",\"calls\":").Append(Int(total.Calls))
                    .Append(",\"meanMs\":").Append(Num(total.Calls > 0 ? total.Ms / total.Calls : 0)).Append("},");
                json.Append("\"operations\":[");
                json.Append(string.Join(",", names.Select(name =>
                {
                    var item = operations[name];
                    return "{\"operation\":\"" + Escape(name) + "\",\"calls\":" + Int(item.Calls) + ",\"qps\":" + Num(item.Calls / elapsed) + ",\"totalMs\":" + Num(item.Ms)
                        + ",\"msPerSecond\":" + Num(item.Ms / elapsed) + ",\"rows\":" + Int(item.Rows) + ",\"rowsPerSecond\":" + Num(item.Rows / elapsed)
                        + ",\"meanMs\":" + Num(item.Calls > 0 ? item.Ms / item.Calls : 0) + "}";
                })));
                json.Append("],\"topByCalls\":[").Append(Statements(byCalls)).Append("],\"topByTime\":[").Append(Statements(byTime));
                json.Append("],\"snapshotMs\":").Append(snapshotMs.ToString(CultureInfo.InvariantCulture)).Append(",\"serverVersionNum\":")
                    .Append(versionNum.ToString(CultureInfo.InvariantCulture)).Append(",\"serverVersion\":\"").Append(Escape(meta.Version)).Append("\"}");
                host.Out.WriteLine(json.ToString());
            }
            else
            {
                host.Out.WriteLine("windowSeconds=" + Num(elapsed) + " qps=" + Num(total.Calls / elapsed) + " dbMsPerSecond=" + Num(total.Ms / elapsed)
                    + " rowsPerSecond=" + Num(total.Rows / elapsed) + " partial=" + (partial ? "True" : "False") + " reason=" + reason);
                foreach (var name in names)
                    host.Out.WriteLine("operation=" + name + " calls=" + Int(operations[name].Calls) + " qps=" + Num(operations[name].Calls / elapsed));
                foreach (var item in byCalls) host.Out.WriteLine("topCalls label=" + item.Label + " calls=" + Int(item.Calls));
            }
            return partial ? Partial : Done;
        }

        // The counter and text queries, each tagged.
        private static (string Counters, string Text) SqlParts(string repoRoot, IFileSystem files)
        {
            var path = files.Path.Combine(repoRoot ?? "", "server", "Infrastructure", "Data", "PgStatStatementsSnapshot.sql");
            if (!files.File.Exists(path)) throw new UnreachableException(null);
            var raw = files.File.ReadAllText(path);
            var at = raw.IndexOf(Split, StringComparison.Ordinal);
            if (at < 0) throw new UnreachableException(null);
            return (Tag + "\n" + raw.Substring(0, at).Trim(), Tag + "\n" + raw.Substring(at + Split.Length).Trim());
        }

        // The default and --container target: DesktopPostgres (the pinned server2 container, or the named one), read-only.
        private static Func<string, string> DockerQuery(PgQpsOptions options, NightlyHost host)
        {
            DesktopPostgres target;
            try
            {
                target = DesktopPostgres.Resolve(host, options.Container, options.User, options.Database);
            }
            catch (InvalidOperationException e)
            {
                throw new UnreachableException(e.Message);
            }
            return sql =>
            {
                string output;
                try
                {
                    output = target.Query(sql);
                }
                catch (InvalidOperationException e)
                {
                    throw new UnreachableException(e.Message);
                }
                if (string.IsNullOrWhiteSpace(output)) throw new UnreachableException(null);
                return output;
            };
        }

        // --connection-env: the variable's connection string as PG* settings of a psql child, read-only.
        private static Func<string, string> EnvironmentQuery(PgQpsOptions options, NightlyHost host, PgQpsSeams seams)
        {
            var raw = host.Variables(options.ConnectionEnv);
            if (string.IsNullOrEmpty(raw)) throw new UnreachableException(null);
            var map = ConnectionSettings(raw);
            string Setting(string first, string second, string fallback) =>
                map.TryGetValue(first, out var value) ? value : second != null && map.TryGetValue(second, out value) ? value : fallback;
            var hostName = Setting("host", "server", "");
            if (hostName.Length == 0) throw new UnreachableException(null);
            var settings = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["PGHOST"] = hostName,
                ["PGPORT"] = Setting("port", null, "5432"),
                ["PGUSER"] = Setting("username", "user", options.User),
                ["PGDATABASE"] = Setting("database", "dbname", options.Database),
                ["PGPASSWORD"] = Setting("password", null, ""),
                ["PGOPTIONS"] = "-c default_transaction_read_only=on",
            };
            if (seams.Psql == null) throw new InvalidOperationException("pg-qps --connection-env needs a psql seam.");
            return sql =>
            {
                var result = seams.Psql(settings, sql);
                if (result == null || result.ExitCode != 0 || string.IsNullOrWhiteSpace(result.Output)) throw new UnreachableException(null);
                return result.Output;
            };
        }

        /// <summary>
        /// A connection string as lower-case keys: "key=value;..." (trimmed) when it has a ';', else libpq "key=value ..." split
        /// at unquoted whitespace; a value in single quotes loses them. A later key wins.
        /// </summary>
        public static IReadOnlyDictionary<string, string> ConnectionSettings(string raw)
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            static string Unquote(string value) =>
                value.Length >= 2 && value.StartsWith('\'') && value.EndsWith('\'') ? value.Substring(1, value.Length - 2) : value;
            if (raw.IndexOf(';') >= 0)
            {
                foreach (var part in raw.Split(';'))
                {
                    var eq = part.IndexOf('=');
                    if (eq < 1) continue;
                    map[part.Substring(0, eq).Trim().ToLowerInvariant()] = Unquote(part.Substring(eq + 1).Trim());
                }
                return map;
            }
            var token = new StringBuilder();
            var inQuote = false;
            for (var i = 0; i <= raw.Length; i++)
            {
                var done = i == raw.Length;
                if (done || (!inQuote && char.IsWhiteSpace(raw[i])))
                {
                    if (token.Length > 0)
                    {
                        var text = token.ToString();
                        var eq = text.IndexOf('=');
                        if (eq > 0) map[text.Substring(0, eq).ToLowerInvariant()] = Unquote(text.Substring(eq + 1));
                        token.Clear();
                    }
                    continue;
                }
                if (raw[i] == '\'') inQuote = !inQuote;
                token.Append(raw[i]);
            }
            return map;
        }

        // The counter rows: VersionNum|Version|Dealloc|StatsReset|QueryId|Calls|TotalExecMs|Rows; a short line is skipped and
        // a number that does not parse is 0.
        private static List<Row> Rows(string text)
        {
            var parsed = new List<Row>();
            foreach (var line in text.Split('\n'))
            {
                var trimmed = line.Trim();
                if (trimmed.Length == 0) continue;
                var f = trimmed.Split('|');
                if (f.Length < 8) continue;
                long.TryParse(f[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var dealloc);
                long.TryParse(f[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out var calls);
                double.TryParse(f[6], NumberStyles.Float, CultureInfo.InvariantCulture, out var ms);
                long.TryParse(f[7], NumberStyles.Integer, CultureInfo.InvariantCulture, out var rows);
                parsed.Add(new Row(f[0], f[1], dealloc, f[3], f[4], calls, ms, rows));
            }
            return parsed;
        }

        // The rows by query id, in first-seen order; a later row of the same id wins.
        private static Dictionary<string, Row> ById(List<Row> rows)
        {
            var map = new Dictionary<string, Row>(StringComparer.Ordinal);
            foreach (var row in rows)
                if (row.QueryId.Length > 0) map[row.QueryId] = row;
            return map;
        }

        // The statement texts by query id (base64 in the second column); a line that does not decode is skipped.
        private static Dictionary<string, string> Texts(string textSql, List<string> ids, Func<string, string> query)
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            var valid = ids.Where(id => QueryId.IsMatch(id)).ToList();
            for (var start = 0; start < valid.Count; start += TextBatch)
            {
                var raw = query(textSql.Replace("/*IDS*/", string.Join(",", valid.Skip(start).Take(TextBatch))));
                foreach (var line in raw.Split('\n'))
                {
                    var trimmed = line.Trim();
                    if (trimmed.Length == 0) continue;
                    var f = trimmed.Split('|');
                    if (f.Length < 2 || f[0].Length == 0) continue;
                    try
                    {
                        map[f[0]] = Encoding.UTF8.GetString(Convert.FromBase64String(f[1]));
                    }
                    catch (FormatException)
                    {
                    }
                }
            }
            return map;
        }

        /// <summary>A statement's label: its "-- &lt;tag&gt;" line, "redacted-utility", "tx.&lt;verb&gt;", or its text on one line (120 characters at most).</summary>
        public static string Label(string query)
        {
            if (string.IsNullOrEmpty(query)) return "untagged";
            if (Sensitive.IsMatch(query)) return "redacted-utility";
            var tag = Tagged.Match(query);
            if (tag.Success) return tag.Groups[1].Value;
            var flat = Space.Replace(query, " ").Trim();
            var transaction = Transaction.Match(flat);
            if (transaction.Success) return "tx." + transaction.Groups[1].Value.ToLowerInvariant();
            if (flat.Length > 120) return flat.Substring(0, 120);
            return flat.Length == 0 ? "untagged" : flat;
        }

        /// <summary>A label's operation: a tag or redacted-utility as it is, anything else "untagged".</summary>
        public static string OperationOf(string label)
        {
            if (string.Equals(label, "redacted-utility", StringComparison.OrdinalIgnoreCase)) return label;
            return Operation.IsMatch(label) ? label : "untagged";
        }

        private static string Statements(List<Statement> items) => string.Join(",", items.Select(item =>
            "{\"label\":\"" + Escape(item.Label) + "\",\"calls\":" + Int(item.Calls) + ",\"qps\":" + Num(item.Qps) + ",\"totalMs\":" + Num(item.Ms)
            + ",\"msPerSecond\":" + Num(item.MsPer) + ",\"rows\":" + Int(item.Rows) + ",\"rowsPerSecond\":" + Num(item.RowsPer) + ",\"meanMs\":" + Num(item.Mean) + "}"));

        private static string Escape(string s) =>
            s == null ? "" : s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t");

        private static string Num(double n) => n.ToString("0.########", CultureInfo.InvariantCulture);

        private static string Int(long n) => n.ToString(CultureInfo.InvariantCulture);

        /// <summary>
        /// The production Psql seam: psql -X -A -t -v ON_ERROR_STOP=1 from PATH with this process's environment plus the
        /// settings, the SQL on stdin (UTF-8), stdout read as UTF-8 and stderr read and dropped. Null when psql cannot start or
        /// runs over DesktopPostgres.TimeoutMilliseconds (its tree is killed).
        /// </summary>
        public static PgQpsPsqlResult Psql(IReadOnlyDictionary<string, string> settings, string sql)
        {
            var utf8 = new UTF8Encoding(false);
            var start = new ProcessStartInfo("psql")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardInputEncoding = utf8,
                StandardOutputEncoding = utf8,
                StandardErrorEncoding = utf8,
            };
            foreach (var argument in new[] { "-X", "-A", "-t", "-v", "ON_ERROR_STOP=1" }) start.ArgumentList.Add(argument);
            foreach (var pair in settings ?? new Dictionary<string, string>()) start.Environment[pair.Key] = pair.Value ?? "";
            using var process = new Process { StartInfo = start };
            try
            {
                if (!process.Start()) return null;
            }
            catch (Exception e) when (e is Win32Exception || e is InvalidOperationException || e is IOException)
            {
                return null;
            }
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            try
            {
                process.StandardInput.Write(sql ?? "");
                process.StandardInput.Close();
            }
            catch (IOException)
            {
                // psql exited before reading its input; its exit code answers.
            }
            if (!process.WaitForExit(DesktopPostgres.TimeoutMilliseconds))
            {
                try { process.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) { }
                process.WaitForExit();
                return null;
            }
            process.WaitForExit();
            error.GetAwaiter().GetResult();
            return new PgQpsPsqlResult(process.ExitCode, output.GetAwaiter().GetResult());
        }
    }
}
