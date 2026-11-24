// CARD-1886: record a Postgres JIT window without resetting statistics. It replaced scripts/postgres-jit-window.ps1 (unit 46 of
// docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md, W8 slice A; nothing started the script by path); the CLI runs it as
//
//   antiphon-scripts postgres-jit-window [--execute --container <name> --out-dir <dir>] [--database antiphon] [--user postgres]
//       [--minutes <1..30>] [--interval-seconds <5..10>]
//
// (scripts\antiphon.cmd postgres-jit-window on the desktop, scripts/antiphon postgres-jit-window on sh). Without --execute it
// prints the plan and starts nothing. --execute needs --container and --out-dir: it writes statements-start.json, samples
// `docker stats --no-stream` of the container every --interval-seconds for --minutes into docker-stats.txt, writes
// statements-end.json and window.json, and prints "CARD-1886 JIT window written under <dir>". Each snapshot is
// pg_stat_statements_info plus the current database's statement rows: identity, calls, rows, total_exec_time,
// total_plan_time, block counters and every jit_ column the view has. It never resets statistics; the queries run read-only in
// the named container (DesktopPostgres: docker exec ... psql -c, 30 s each), docker stats on the host's launcher (30 s).
//
// Exit 0 the plan or the window written; 1 a query, a docker stats sample or a file write failed (one host.Error line); usage
// errors (a missing --container or --out-dir with --execute, a value out of range, a database or user that is not a plain
// identifier) are NightlyUsageException (the CLI's exit 2). The clock and the waits are the host's, files IFileSystem's.
// Plain C#: no PowerShell types. ASCII only.
#nullable disable
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Antiphon.Nightly
{
    /// <summary>The postgres-jit-window command's options (postgres-jit-window.ps1's parameters).</summary>
    public sealed class PostgresJitWindowOptions
    {
        /// <summary>The CLI's usage line.</summary>
        public const string Usage = "postgres-jit-window [--execute --container <name> --out-dir <dir>] [--database antiphon] [--user postgres]"
            + " [--minutes <1..30>] [--interval-seconds <5..10>]";

        /// <summary>Record the window; false prints the plan only.</summary>
        public bool Execute { get; init; }

        /// <summary>The PostgreSQL container (required with Execute).</summary>
        public string Container { get; init; } = "";

        /// <summary>The directory the window is written under (required with Execute).</summary>
        public string OutDir { get; init; } = "";

        /// <summary>The database whose statements are recorded.</summary>
        public string Database { get; init; } = "antiphon";

        /// <summary>The database user.</summary>
        public string User { get; init; } = "postgres";

        /// <summary>How long docker stats is sampled.</summary>
        public int Minutes { get; init; } = 10;

        /// <summary>The wait between two docker stats samples.</summary>
        public int IntervalSeconds { get; init; } = 10;

        /// <summary>The arguments after the command name.</summary>
        public static PostgresJitWindowOptions Parse(IReadOnlyList<string> arguments)
        {
            var flags = NightlyArgs.Parse(arguments, new[] { "--container", "--out-dir", "--database", "--user", "--minutes", "--interval-seconds" }, new[] { "--execute" });
            return new PostgresJitWindowOptions
            {
                Execute = flags.Switch("--execute"),
                Container = flags.Value("--container", ""),
                OutDir = flags.Value("--out-dir", ""),
                Database = flags.Value("--database", "antiphon"),
                User = flags.Value("--user", "postgres"),
                Minutes = flags.Int("--minutes", 10),
                IntervalSeconds = flags.Int("--interval-seconds", 10),
            };
        }
    }

    /// <summary>The JIT window recorder (postgres-jit-window.ps1).</summary>
    public static class PostgresJitWindow
    {
        /// <summary>The plan lines printed without --execute.</summary>
        public static readonly IReadOnlyList<string> Plan = new[]
        {
            "CARD-1886 JIT window plan. No docker call.",
            "After run 97150a0b ends: scripts\\antiphon.cmd postgres-jit-window --execute --container <name> --out-dir <dir> (scripts/antiphon on sh)",
            "Records pg_stat_statements_info, total_exec_time, total_plan_time, calls, rows, block counters, and jit_ columns. Does not reset statistics.",
        };

        /// <summary>The jit_ columns pg_stat_statements has, in column order.</summary>
        public const string JitColumnsSql = "SELECT attname\n"
            + "FROM pg_attribute\n"
            + "WHERE attrelid = 'pg_stat_statements'::regclass\n"
            + "  AND attnum > 0\n"
            + "  AND NOT attisdropped\n"
            + "  AND attname LIKE 'jit_%'\n"
            + "ORDER BY attnum;";

        /// <summary>The statement columns every snapshot records before the jit_ columns.</summary>
        public static readonly IReadOnlyList<string> StatementColumns = new[]
        {
            "dbid::text AS dbid", "userid::text AS userid", "queryid::text AS queryid", "toplevel", "calls", "rows", "total_exec_time", "total_plan_time",
            "shared_blks_hit", "shared_blks_read", "shared_blks_dirtied", "shared_blks_written", "temp_blks_read", "temp_blks_written",
        };

        /// <summary>The docker stats format of one sample.</summary>
        public const string StatsFormat = "{{.Name}} {{.CPUPerc}} {{.MemPerc}}";

        private static readonly Regex Identifier = new Regex(@"\A[A-Za-z_][A-Za-z0-9_]*\z", RegexOptions.CultureInvariant);
        private static readonly Regex JitColumn = new Regex(@"\Ajit_[a-z0-9_]+\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>One snapshot: the info row and the current database's statements with the given jit_ columns, as one JSON value.</summary>
        public static string SnapshotSql(IReadOnlyList<string> jitColumns)
        {
            var select = string.Join(", ", StatementColumns.Concat(jitColumns ?? Array.Empty<string>()));
            return "SELECT json_build_object(\n"
                + "  'recordedAt', clock_timestamp(),\n"
                + "  'postmasterStartedAt', pg_postmaster_start_time(),\n"
                + "  'database', current_database(),\n"
                + "  'info', (SELECT row_to_json(i) FROM pg_stat_statements_info i),\n"
                + "  'statements', COALESCE((SELECT json_agg(row_to_json(s)) FROM (\n"
                + "    SELECT " + select + "\n"
                + "    FROM pg_stat_statements\n"
                + "    WHERE dbid = (SELECT oid FROM pg_database WHERE datname = current_database())\n"
                + "  ) s), '[]'::json)\n"
                + ");";
        }

        /// <summary>Prints the plan, or records the window. Returns the exit code (0 done, 1 failed); usage errors throw NightlyUsageException.</summary>
        public static int Run(PostgresJitWindowOptions options, NightlyHost host, IFileSystem files)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (host == null) throw new ArgumentNullException(nameof(host));
            if (files == null) throw new ArgumentNullException(nameof(files));
            if (!options.Execute)
            {
                foreach (var line in Plan) host.Out.WriteLine(line);
                return 0;
            }
            if (string.IsNullOrWhiteSpace(options.Container)) throw new NightlyUsageException("--container is required with --execute");
            if (string.IsNullOrWhiteSpace(options.OutDir)) throw new NightlyUsageException("--out-dir is required with --execute");
            if (options.Minutes < 1 || options.Minutes > 30) throw new NightlyUsageException("--minutes must be from 1 through 30");
            if (options.IntervalSeconds < 5 || options.IntervalSeconds > 10) throw new NightlyUsageException("--interval-seconds must be from 5 through 10");
            if (!Identifier.IsMatch(options.Database ?? "")) throw new NightlyUsageException("--database must be a plain identifier");
            if (!Identifier.IsMatch(options.User ?? "")) throw new NightlyUsageException("--user must be a plain identifier");
            try
            {
                Record(options, host, files);
                return 0;
            }
            catch (Exception e) when (e is InvalidOperationException || e is IOException || e is UnauthorizedAccessException || e is ArgumentException
                || e is NotSupportedException)
            {
                host.Error.WriteLine("postgres-jit-window: " + e.Message);
                return 1;
            }
        }

        private static void Record(PostgresJitWindowOptions options, NightlyHost host, IFileSystem files)
        {
            files.Directory.CreateDirectory(options.OutDir);
            var started = host.UtcNow;
            var database = DesktopPostgres.Resolve(host, options.Container, options.User, options.Database);
            var jitColumns = new List<string>();
            foreach (var line in database.Query(JitColumnsSql).Split('\n'))
            {
                var trimmed = line.Trim();
                if (trimmed.Length == 0) continue;
                if (!JitColumn.IsMatch(trimmed)) throw new InvalidOperationException("unexpected jit column name");
                jitColumns.Add(trimmed);
            }
            WriteSnapshot(files, options.OutDir, "statements-start.json", database.Query(SnapshotSql(jitColumns)));

            var statsPath = files.Path.Combine(options.OutDir, "docker-stats.txt");
            var deadline = host.UtcNow.AddMinutes(options.Minutes);
            do
            {
                var stamp = host.UtcNow.ToString("o", CultureInfo.InvariantCulture);
                files.File.AppendAllText(statsPath, stamp + " " + Sample(host, options.Container) + "\n", Encoding.ASCII);
                if (host.UtcNow >= deadline) break;
                host.Sleep(TimeSpan.FromSeconds(options.IntervalSeconds));
            }
            while (host.UtcNow < deadline);

            WriteSnapshot(files, options.OutDir, "statements-end.json", database.Query(SnapshotSql(jitColumns)));
            var note = new JsonObject
            {
                ["container"] = options.Container,
                ["database"] = options.Database,
                ["minutes"] = options.Minutes,
                ["intervalSeconds"] = options.IntervalSeconds,
                ["startedAt"] = started.ToString("o", CultureInfo.InvariantCulture),
                ["endedAt"] = host.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                ["jitColumns"] = new JsonArray(jitColumns.Select(column => (JsonNode)JsonValue.Create(column)).ToArray()),
            };
            files.File.WriteAllText(files.Path.Combine(options.OutDir, "window.json"), note.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n",
                Encoding.ASCII);
            host.Out.WriteLine("CARD-1886 JIT window written under " + options.OutDir);
        }

        // One docker stats line of the container: its stdout lines joined by spaces.
        private static string Sample(NightlyHost host, string container)
        {
            NightlyLaunchResult result;
            try
            {
                result = host.Launch(new NightlyLaunch("docker", new[] { "stats", "--no-stream", "--format", StatsFormat, container }, "",
                    DesktopPostgres.TimeoutMilliseconds, CaptureOutput: true));
            }
            catch (Exception e) when (e is Win32Exception || e is IOException || e is UnauthorizedAccessException || e is InvalidOperationException)
            {
                throw new InvalidOperationException("docker stats failed");
            }
            if (result.TimedOut || result.ExitCode != 0) throw new InvalidOperationException("docker stats failed");
            return string.Join(" ", (result.StandardOutputText ?? "").TrimEnd('\n').Split('\n').Select(line => line.TrimEnd('\r')));
        }

        private static void WriteSnapshot(IFileSystem files, string outDir, string name, string json) =>
            files.File.WriteAllText(files.Path.Combine(outDir, name), json + "\n", Encoding.ASCII);
    }
}
