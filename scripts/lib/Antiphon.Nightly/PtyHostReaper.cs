// CARD-0204: finds - and with --execute, kills - Antiphon.PtyHost processes that a test host leaked onto the always-on production
// session-runner. It replaced scripts/reap-orphaned-pty-hosts.ps1 (deleted 2026-11-24, unit 38 of
// docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md; nothing started it by path); the CLI runs it as
// `antiphon-scripts reap-orphaned-pty-hosts`. DRY RUN BY DEFAULT: without --execute it only prints the census and the verdict per
// host.
//
// A host is a positive orphan only when EVERY rule below holds. Age alone is never evidence (CARD-0203): a host outlives its child
// by design, and an idle session's sequence does not move.
//
//   R1  no AgentSessions row in the production database for the manifest's session id (the database must ANSWER: an unreachable
//       database aborts the run; "could not look" is not "no row")
//   R2  the manifest's hostPid is alive, is Antiphon.PtyHost.exe, and its process start matches the manifest's hostStartTimeUtc
//       within --pid-reuse-tolerance-sec (pid-reuse guard)
//   R3  the manifest's childPid is alive and is the process the shape rule expects
//   R4  the manifest records no exit (a host lingering after its child exited leaves on its own)
//   R5  the launch has one of the shapes a test host produces:
//         test-raw-check-interpreter : exe is cmd.exe AND cwd is --check-interpreter-dir (AntiphonWebAppFactory's test-raw
//                                      definition, launched by CheckInterpreterProvisioner at host startup)
//         test-raw-diagnose          : exe is cmd.exe AND cwd is --diagnose-dir (the same definition, DiagnoseProvisioner)
//         test-raw-output-distiller  : exe is cmd.exe AND cwd is --output-distiller-dir (the same, OutputDistillerProvisioner)
//         kind-test-temp-dir         : cwd is under %TEMP%\antiphon-kind-test* (AgentTaskAgentKindTests, dispatched by a factory
//                                      host before the shared-schema isolation of 2026-08-20)
//   R6  for the cmd.exe shapes, the ansi log holds only cmd's banner (nothing was ever typed): at most
//       --max-banner-only-ansi-bytes (cmd's banner is about 164 bytes)
//   R7  the manifest is older than --min-age-minutes (a test still running is left to finish)
//   R8  the runner lists the session as Running, so the kill goes THROUGH the runner (POST /sessions/{id}/kill -> the host kills
//       its child -> exit -> Shutdown ack -> the host exits, the manifest is deleted) - never a bare process kill
//
// Anything with a database row is printed under "protected" and never touched, whatever else is true of it. Anything failing a rule
// is printed with the rules that failed. --limit kills the oldest positives first. The seams have no way to stop a process other
// than the runner's kill request.
//
// Manifests are <session-log-path>\pty-hosts\manifests\*.json (PtyHostManifest: camelCase keys, matched exactly; the session id a
// GUID). Exit codes: 0 a dry run completed, or every kill was verified (the host pid gone); 1 a kill request failed or a host is
// still alive afterwards; 2 a prerequisite could not be read (the runner did not answer GET /sessions, the database did not
// answer, the manifest directory is missing, a manifest cannot be read, the process table cannot be read). Usage errors are
// NightlyUsageException (the CLI's exit 2). The runner, the database, the process table and the liveness check are
// IPtyHostReaperSeams (production: PtyHostReaper.Production, Windows); manifests and ansi logs are IFileSystem. Plain C#: no
// PowerShell types. ASCII only.
#nullable disable
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO.Abstractions;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;

namespace Antiphon.Nightly
{
    /// <summary>The reap-orphaned-pty-hosts command's options (reap-orphaned-pty-hosts.ps1's parameters, in kebab case).</summary>
    public sealed class PtyHostReaperOptions
    {
        /// <summary>The CLI's usage line.</summary>
        public const string Usage = "reap-orphaned-pty-hosts [--execute] [--limit 0] [--runner-url http://localhost:17204]"
            + " [--session-log-path C:\\logs\\antiphon\\session-runner] [--check-interpreter-dir C:\\logs\\antiphon\\check-interpreter]"
            + " [--diagnose-dir C:\\logs\\antiphon\\diagnose] [--output-distiller-dir C:\\logs\\antiphon\\output-distiller] [--pg-container <name>]"
            + " [--pg-user antiphon] [--pg-database antiphon] [--min-age-minutes 30] [--pid-reuse-tolerance-sec 5] [--max-banner-only-ansi-bytes 512]"
            + " [--kill-verify-seconds 20]";

        /// <summary>Kill every positive orphan through the runner (absent: report only).</summary>
        public bool Execute { get; init; }

        /// <summary>The session runner, without a trailing slash.</summary>
        public string RunnerUrl { get; init; } = "http://localhost:17204";

        /// <summary>The session runner's log root (pty-hosts\manifests).</summary>
        public string SessionLogPath { get; init; } = @"C:\logs\antiphon\session-runner";

        /// <summary>The check interpreter's working directory (test-raw-check-interpreter).</summary>
        public string CheckInterpreterDir { get; init; } = @"C:\logs\antiphon\check-interpreter";

        /// <summary>The diagnose seat's working directory (test-raw-diagnose).</summary>
        public string DiagnoseDir { get; init; } = @"C:\logs\antiphon\diagnose";

        /// <summary>The output distiller's working directory (test-raw-output-distiller).</summary>
        public string OutputDistillerDir { get; init; } = @"C:\logs\antiphon\output-distiller";

        /// <summary>An explicit container override. Empty resolves the AppHost database on server2.</summary>
        public string PgContainer { get; init; } = "";

        /// <summary>The Postgres user.</summary>
        public string PgUser { get; init; } = "antiphon";

        /// <summary>The Postgres database.</summary>
        public string PgDatabase { get; init; } = "antiphon";

        /// <summary>A manifest younger than this is left alone (R7).</summary>
        public int MinAgeMinutes { get; init; } = 30;

        /// <summary>The pid-reuse tolerance in seconds (R2).</summary>
        public int PidReuseToleranceSec { get; init; } = 5;

        /// <summary>The largest ansi log that holds only cmd's banner (R6).</summary>
        public int MaxBannerOnlyAnsiBytes { get; init; } = 512;

        /// <summary>How long a killed host has to disappear.</summary>
        public int KillVerifySeconds { get; init; } = 20;

        /// <summary>With --execute, kill at most this many (the oldest first); 0 is all.</summary>
        public int Limit { get; init; }

        /// <summary>The arguments after the command name.</summary>
        public static PtyHostReaperOptions Parse(IReadOnlyList<string> arguments)
        {
            var flags = NightlyArgs.Parse(arguments,
                new[] { "--runner-url", "--session-log-path", "--check-interpreter-dir", "--diagnose-dir", "--output-distiller-dir", "--pg-container", "--pg-user",
                    "--pg-database", "--min-age-minutes", "--pid-reuse-tolerance-sec", "--max-banner-only-ansi-bytes", "--kill-verify-seconds", "--limit" },
                new[] { "--execute" });
            string Text(string flag, string fallback)
            {
                var value = flags.Value(flag, "");
                return value.Length > 0 ? value : fallback;
            }
            return new PtyHostReaperOptions
            {
                Execute = flags.Switch("--execute"),
                RunnerUrl = Text("--runner-url", "http://localhost:17204").TrimEnd('/'),
                SessionLogPath = Text("--session-log-path", @"C:\logs\antiphon\session-runner"),
                CheckInterpreterDir = Text("--check-interpreter-dir", @"C:\logs\antiphon\check-interpreter"),
                DiagnoseDir = Text("--diagnose-dir", @"C:\logs\antiphon\diagnose"),
                OutputDistillerDir = Text("--output-distiller-dir", @"C:\logs\antiphon\output-distiller"),
                PgContainer = flags.Value("--pg-container", ""),
                PgUser = Text("--pg-user", "antiphon"),
                PgDatabase = Text("--pg-database", "antiphon"),
                MinAgeMinutes = flags.Int("--min-age-minutes", 30),
                PidReuseToleranceSec = flags.Int("--pid-reuse-tolerance-sec", 5),
                MaxBannerOnlyAnsiBytes = flags.Int("--max-banner-only-ansi-bytes", 512),
                KillVerifySeconds = flags.Int("--kill-verify-seconds", 20),
                Limit = flags.Int("--limit", 0),
            };
        }
    }

    /// <summary>One process of the table: Win32_Process's ProcessId, Name (with .exe) and CreationDate (UTC; null when unknown).</summary>
    public sealed record PtyHostProcess(int ProcessId, string Name, DateTime? CreationDate);

    /// <summary>The machine the reaper reads and, with --execute, asks the runner to change.</summary>
    public interface IPtyHostReaperSeams
    {
        /// <summary>One request without a body (GET the runner's sessions, POST a kill): the answer; throws when nothing answers in time.</summary>
        NightlyReportHttpResponse Http(string method, string uri, TimeSpan timeout);

        /// <summary>Read-only PostgreSQL query. An empty container selects the AppHost database.</summary>
        ZombieDatabaseAnswer Query(string container, string user, string database, string sql);

        /// <summary>Every process; throws when the table cannot be read.</summary>
        IReadOnlyList<PtyHostProcess> Processes();

        /// <summary>True while the process runs.</summary>
        bool IsAlive(int pid);

        /// <summary>How many Antiphon.PtyHost processes run now (0 when they cannot be counted).</summary>
        int LivePtyHosts();
    }

    /// <summary>One manifest's verdict.</summary>
    public sealed class PtyHostVerdict
    {
        /// <summary>The manifest's session id.</summary>
        public string SessionId { get; init; } = "";

        /// <summary>The R5 shape that matched, or empty.</summary>
        public string Rule { get; init; } = "";

        /// <summary>The manifest's age in whole minutes.</summary>
        public int AgeMin { get; init; }

        /// <summary>The manifest's host pid.</summary>
        public int HostPid { get; init; }

        /// <summary>The manifest's child pid (0 when it has none).</summary>
        public int ChildPid { get; init; }

        /// <summary>The child's executable name.</summary>
        public string Exe { get; init; } = "";

        /// <summary>The child's working directory.</summary>
        public string Cwd { get; init; } = "";

        /// <summary>The database has the session (R1): never touched.</summary>
        public bool Protected { get; init; }

        /// <summary>Every rule held.</summary>
        public bool Orphan { get; init; }

        /// <summary>The rules that failed, "; " separated.</summary>
        public string Reasons { get; init; } = "";
    }

    /// <summary>The run's exit code, the verdicts and the sessions a kill was requested for.</summary>
    public sealed class PtyHostReaperResult
    {
        /// <summary>See the file comment.</summary>
        public int ExitCode { get; init; }

        /// <summary>One per manifest, in manifest order.</summary>
        public IReadOnlyList<PtyHostVerdict> Verdicts { get; init; } = Array.Empty<PtyHostVerdict>();

        /// <summary>The session ids POST /sessions/{id}/kill was sent for, in order.</summary>
        public IReadOnlyList<string> KillRequests { get; init; } = Array.Empty<string>();
    }

    /// <summary>The orphaned pty-host census and reaper (reap-orphaned-pty-hosts.ps1's body).</summary>
    public static class PtyHostReaper
    {
        /// <summary>How long the runner's GET /sessions may take.</summary>
        public static readonly TimeSpan RunnerDeadline = TimeSpan.FromSeconds(15);

        /// <summary>How long one kill request may take.</summary>
        public static readonly TimeSpan KillDeadline = TimeSpan.FromSeconds(30);

        private static readonly Regex SessionRow = new Regex("^[0-9a-f-]{36}$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex KindTestDirectory = new Regex(@"\\antiphon-kind-test[^\\]*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>The production seams (Windows): HTTP, docker on the host's launcher, the Win32 process table and the host's liveness check.</summary>
        public static IPtyHostReaperSeams Production(NightlyHost host) => new PtyHostReaperOs(host ?? throw new ArgumentNullException(nameof(host)));

        /// <summary>Reads the runner, the manifests, the database and the process table, prints the verdicts and, with --execute, kills.</summary>
        public static PtyHostReaperResult Run(PtyHostReaperOptions options, NightlyHost host, IFileSystem files, IPtyHostReaperSeams seams)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (host == null) throw new ArgumentNullException(nameof(host));
            if (files == null) throw new ArgumentNullException(nameof(files));
            if (seams == null) throw new ArgumentNullException(nameof(seams));
            var output = host.Out;

            // ---- prerequisites: the runner and the database must both ANSWER ----
            var runnerStatus = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var answer = seams.Http("GET", options.RunnerUrl + "/sessions", RunnerDeadline);
                if (!OperatorApi.Succeeded(answer)) throw new InvalidOperationException(OperatorApi.Failure(answer));
                using var document = JsonDocument.Parse(answer.Body ?? "");
                var sessions = document.RootElement.ValueKind == JsonValueKind.Array ? document.RootElement.EnumerateArray().ToArray() : new[] { document.RootElement };
                foreach (var session in sessions)
                    runnerStatus[Text(session, "sessionId")] = Text(session, "status");
            }
            catch (Exception e) when (e is HttpRequestException || e is OperationCanceledException || e is System.IO.IOException || e is InvalidOperationException
                || e is JsonException || e is UriFormatException)
            {
                output.WriteLine("ABORT: session-runner at " + options.RunnerUrl + " did not answer GET /sessions: " + e.Message);
                return Abort();
            }

            var manifestDir = files.Path.Combine(options.SessionLogPath, "pty-hosts", "manifests");
            if (!files.Directory.Exists(manifestDir))
            {
                output.WriteLine("ABORT: the manifest directory " + manifestDir + " does not exist (check --session-log-path).");
                return Abort();
            }
            var manifests = new List<Manifest>();
            foreach (var path in files.Directory.GetFiles(manifestDir).Where(path => path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                .OrderBy(path => files.Path.GetFileName(path), StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    manifests.Add(Manifest.Read(files, path));
                }
                catch (Exception e) when (e is System.IO.IOException || e is UnauthorizedAccessException || e is JsonException || e is FormatException)
                {
                    output.WriteLine("ABORT: the manifest " + path + " cannot be read: " + e.Message);
                    return Abort();
                }
            }
            if (manifests.Count == 0)
            {
                output.WriteLine("No manifests under " + manifestDir + " - nothing to do.");
                return new PtyHostReaperResult { ExitCode = 0 };
            }

            // Each id is a GUID (Manifest.Read refuses anything else), so the list is safe to quote.
            var sql = "select \"Id\" from \"AgentSessions\" where \"Id\" in (" + string.Join(",", manifests.Select(m => "'" + m.SessionId + "'")) + ");";
            ZombieDatabaseAnswer rows;
            try
            {
                rows = seams.Query(options.PgContainer, options.PgUser, options.PgDatabase, sql);
            }
            catch (Exception e) when (e is Win32Exception || e is System.IO.IOException || e is InvalidOperationException)
            {
                output.WriteLine("ABORT: the database did not answer: " + e.Message);
                return Abort();
            }
            if (rows == null || rows.ExitCode != 0)
            {
                output.WriteLine("ABORT: the database did not answer (exit " + Num(rows?.ExitCode ?? 1) + ").");
                return Abort();
            }
            var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var line in (rows.Output ?? "").Split('\n'))
            {
                var id = line.Trim();
                if (SessionRow.IsMatch(id)) known.Add(id);
            }

            var processes = new Dictionary<int, PtyHostProcess>();
            try
            {
                foreach (var process in seams.Processes() ?? Array.Empty<PtyHostProcess>()) processes[process.ProcessId] = process;
            }
            catch (Exception e) when (e is PlatformNotSupportedException || e is Win32Exception || e is InvalidOperationException || e is DllNotFoundException
                || e is EntryPointNotFoundException)
            {
                output.WriteLine("ABORT: the process table could not be read: " + e.Message);
                return Abort();
            }

            // ---- verdicts ----
            var now = host.UtcNow;
            var verdicts = manifests.Select(m => Verdict(options, m, known, processes, runnerStatus, files, now)).ToList();
            var protectedRows = verdicts.Where(v => v.Protected).ToList();
            var orphans = verdicts.Where(v => v.Orphan).ToList();
            var undecided = verdicts.Where(v => !v.Protected && !v.Orphan).ToList();

            output.WriteLine("Manifests: " + Num(manifests.Count) + "   protected (DB row): " + Num(protectedRows.Count) + "   positive orphans: " + Num(orphans.Count)
                + "   not touched: " + Num(undecided.Count));
            output.WriteLine("");
            output.WriteLine("--- protected (have an AgentSessions row; never touched) ---");
            Table(output, new[] { "SessionId", "Exe", "Cwd", "AgeMin" },
                protectedRows.OrderBy(v => v.Cwd, StringComparer.OrdinalIgnoreCase).Select(v => new[] { v.SessionId, v.Exe, v.Cwd, Num(v.AgeMin) }));
            output.WriteLine("--- not touched (no DB row, but a rule failed) ---");
            Table(output, new[] { "SessionId", "Exe", "Cwd", "AgeMin", "Reasons" }, undecided.Select(v => new[] { v.SessionId, v.Exe, v.Cwd, Num(v.AgeMin), v.Reasons }));
            output.WriteLine("--- positive orphans ---");
            foreach (var group in orphans.GroupBy(v => v.Rule).OrderBy(g => g.Key, StringComparer.Ordinal))
                output.WriteLine(string.Format(CultureInfo.InvariantCulture, "  {0,-30} {1}", group.Key, group.Count()));
            Table(output, new[] { "SessionId", "Rule", "AgeMin", "HostPid", "ChildPid" },
                Oldest(orphans).Select(v => new[] { v.SessionId, v.Rule, Num(v.AgeMin), Num(v.HostPid), Num(v.ChildPid) }));

            if (!options.Execute)
            {
                output.WriteLine("Dry run: nothing was killed. Re-run with --execute to kill the positive orphans through the runner.");
                return new PtyHostReaperResult { ExitCode = 0, Verdicts = verdicts };
            }

            // ---- execute: through the runner only ----
            var requests = new List<string>();
            var failed = 0;
            var killed = 0;
            var toKill = Oldest(orphans).ToList();
            if (options.Limit > 0 && toKill.Count > options.Limit)
            {
                output.WriteLine(string.Format(CultureInfo.InvariantCulture, "--limit {0}: killing the {0} oldest of {1} positive orphans this run.", options.Limit, toKill.Count));
                toKill = toKill.Take(options.Limit).ToList();
            }
            foreach (var orphan in toKill)
            {
                requests.Add(orphan.SessionId);
                try
                {
                    var answer = seams.Http("POST", options.RunnerUrl + "/sessions/" + orphan.SessionId + "/kill", KillDeadline);
                    if (!OperatorApi.Succeeded(answer)) throw new InvalidOperationException(OperatorApi.Failure(answer));
                }
                catch (Exception e) when (e is HttpRequestException || e is OperationCanceledException || e is System.IO.IOException || e is InvalidOperationException
                    || e is UriFormatException)
                {
                    output.WriteLine("KILL FAILED  " + orphan.SessionId + "  POST /kill: " + e.Message);
                    failed++;
                    continue;
                }
                var deadline = host.UtcNow.AddSeconds(options.KillVerifySeconds);
                var gone = false;
                while (host.UtcNow < deadline)
                {
                    if (!seams.IsAlive(orphan.HostPid))
                    {
                        gone = true;
                        break;
                    }
                    host.Sleep(TimeSpan.FromMilliseconds(500));
                }
                if (gone)
                {
                    killed++;
                    output.WriteLine("killed       " + orphan.SessionId + "  " + orphan.Rule + "  host " + Num(orphan.HostPid) + " gone");
                }
                else
                {
                    failed++;
                    output.WriteLine("STILL ALIVE  " + orphan.SessionId + "  host " + Num(orphan.HostPid) + " after " + Num(options.KillVerifySeconds) + "s");
                }
            }

            output.WriteLine("");
            output.WriteLine("Killed and verified: " + Num(killed) + "   failed: " + Num(failed) + "   protected: " + Num(protectedRows.Count) + "   not touched: " + Num(undecided.Count));
            output.WriteLine("Live Antiphon.PtyHost processes now: " + Num(seams.LivePtyHosts()));
            return new PtyHostReaperResult { ExitCode = failed > 0 ? 1 : 0, Verdicts = verdicts, KillRequests = requests };
        }

        // Every rule, in the script's order; a protected row keeps its other reasons too.
        private static PtyHostVerdict Verdict(PtyHostReaperOptions options, Manifest m, HashSet<string> known, Dictionary<int, PtyHostProcess> processes,
            Dictionary<string, string> runnerStatus, IFileSystem files, DateTime now)
        {
            var reasons = new List<string>();
            var rule = "";
            var isProtected = known.Contains(m.SessionId);
            if (isProtected) reasons.Add("R1 has AgentSessions row (protected)");

            processes.TryGetValue(m.HostPid, out var hostProcess);
            if (hostProcess == null)
            {
                reasons.Add("R2 host pid not alive");
            }
            else if (!string.Equals(hostProcess.Name, "Antiphon.PtyHost.exe", StringComparison.OrdinalIgnoreCase))
            {
                reasons.Add("R2 host pid is " + hostProcess.Name + ", not a pty-host (pid reused)");
            }
            else if (hostProcess.CreationDate == null || Math.Abs((hostProcess.CreationDate.Value - m.HostStartUtc).TotalSeconds) > options.PidReuseToleranceSec)
            {
                reasons.Add("R2 host start " + Time(hostProcess.CreationDate) + " does not match manifest " + Time(m.HostStartUtc) + " (pid reused)");
            }

            PtyHostProcess child = null;
            if (m.ChildPid > 0) processes.TryGetValue(m.ChildPid, out child);
            if (child == null) reasons.Add("R3 child pid not alive");

            if (m.Exited) reasons.Add("R4 manifest records an exit (host is lingering, will leave on its own)");

            var cmd = string.Equals(m.ExeLeaf, "cmd.exe", StringComparison.OrdinalIgnoreCase);
            var checkInterpreter = cmd && string.Equals(m.Cwd, options.CheckInterpreterDir, StringComparison.OrdinalIgnoreCase);
            var diagnose = cmd && string.Equals(m.Cwd, options.DiagnoseDir, StringComparison.OrdinalIgnoreCase);
            var distiller = cmd && string.Equals(m.Cwd, options.OutputDistillerDir, StringComparison.OrdinalIgnoreCase);
            if (checkInterpreter || diagnose || distiller)
            {
                rule = distiller ? "test-raw-output-distiller" : diagnose ? "test-raw-diagnose" : "test-raw-check-interpreter";
                if (child != null && !string.Equals(child.Name, "cmd.exe", StringComparison.OrdinalIgnoreCase)) reasons.Add("R3 child is " + child.Name + ", expected cmd.exe");
                if (string.IsNullOrEmpty(m.AnsiLog) || !files.File.Exists(m.AnsiLog))
                {
                    reasons.Add("R6 ansi log missing");
                }
                else
                {
                    var length = files.FileInfo.New(m.AnsiLog).Length;
                    if (length > options.MaxBannerOnlyAnsiBytes) reasons.Add("R6 ansi log is " + length.ToString(CultureInfo.InvariantCulture) + " bytes - something was typed or printed");
                }
            }
            else if (KindTestDirectory.IsMatch(m.Cwd))
            {
                rule = "kind-test-temp-dir";
                if (child != null && !child.Name.StartsWith("claude", StringComparison.OrdinalIgnoreCase)) reasons.Add("R3 child is " + child.Name + ", expected claude");
            }
            else
            {
                reasons.Add("R5 shape not recognised (" + m.ExeLeaf + " in " + m.Cwd + ")");
            }

            // [int] rounds half to even.
            var ageMin = Convert.ToInt32((now - m.CreatedUtc).TotalMinutes, CultureInfo.InvariantCulture);
            if (ageMin < options.MinAgeMinutes) reasons.Add("R7 only " + Num(ageMin) + " min old (< " + Num(options.MinAgeMinutes) + ")");

            runnerStatus.TryGetValue(m.SessionId, out var status);
            if (!string.Equals(status, "Running", StringComparison.OrdinalIgnoreCase)) reasons.Add("R8 runner status is '" + status + "', not Running");

            return new PtyHostVerdict
            {
                SessionId = m.SessionId,
                Rule = rule,
                AgeMin = ageMin,
                HostPid = m.HostPid,
                ChildPid = m.ChildPid,
                Exe = m.ExeLeaf,
                Cwd = m.Cwd,
                Protected = isProtected,
                Orphan = reasons.Count == 0,
                Reasons = string.Join("; ", reasons),
            };
        }

        private static PtyHostReaperResult Abort() => new PtyHostReaperResult { ExitCode = 2 };

        // Sort-Object AgeMin -Descending: the oldest first (stable).
        private static IEnumerable<PtyHostVerdict> Oldest(IEnumerable<PtyHostVerdict> rows) => rows.OrderByDescending(v => v.AgeMin);

        // Format-Table: a header, a rule and one line per row, each column as wide as its widest cell; nothing for no rows.
        private static void Table(System.IO.TextWriter output, string[] headers, IEnumerable<string[]> cells)
        {
            var rows = cells.ToList();
            if (rows.Count == 0) return;
            var widths = headers.Select((header, i) => Math.Max(header.Length, rows.Max(row => (row[i] ?? "").Length))).ToArray();
            output.WriteLine(string.Join(" ", headers.Select((header, i) => header.PadRight(widths[i]))).TrimEnd());
            output.WriteLine(string.Join(" ", widths.Select(width => new string('-', width))));
            foreach (var row in rows) output.WriteLine(string.Join(" ", row.Select((cell, i) => (cell ?? "").PadRight(widths[i]))).TrimEnd());
            output.WriteLine("");
        }

        // [string]$value: a string's value, another scalar's text, "" for null or a missing key.
        private static string Text(JsonElement element, string name)
        {
            if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var value)) return "";
            return value.ValueKind switch
            {
                JsonValueKind.String => value.GetString() ?? "",
                JsonValueKind.Null => "",
                _ => value.GetRawText(),
            };
        }

        private static string Time(DateTime? value) => value == null ? "(unknown)" : value.Value.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

        private static string Num(int value) => value.ToString(CultureInfo.InvariantCulture);

        // One pty-host manifest (PtyHostManifest's JSON).
        private sealed class Manifest
        {
            public string SessionId = "";
            public int HostPid;
            public DateTime HostStartUtc;
            public int ChildPid;
            public string ExeLeaf = "";
            public string Cwd = "";
            public DateTime CreatedUtc;
            public bool Exited;
            public string AnsiLog = "";

            // Throws FormatException naming the field when a required one is missing or malformed.
            public static Manifest Read(IFileSystem files, string path)
            {
                using var document = JsonDocument.Parse(files.File.ReadAllText(path));
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object) throw new FormatException("not a JSON object");
                var session = Required(root, "sessionId", JsonValueKind.String).GetString();
                if (!Guid.TryParseExact(session, "D", out _)) throw new FormatException("sessionId is not a GUID");
                var exe = Optional(root, "exe");
                return new Manifest
                {
                    SessionId = session,
                    HostPid = Required(root, "hostPid", JsonValueKind.Number).GetInt32(),
                    HostStartUtc = Date(root, "hostStartTimeUtc"),
                    ChildPid = root.TryGetProperty("childPid", out var child) && child.ValueKind == JsonValueKind.Number ? child.GetInt32() : 0,
                    ExeLeaf = Leaf(exe),
                    Cwd = Optional(root, "cwd"),
                    CreatedUtc = Date(root, "createdAtUtc"),
                    Exited = root.TryGetProperty("exitCode", out var exit) && exit.ValueKind != JsonValueKind.Null,
                    AnsiLog = Optional(root, "ansiLogPath"),
                };
            }

            private static JsonElement Required(JsonElement root, string name, JsonValueKind kind)
            {
                if (!root.TryGetProperty(name, out var value) || value.ValueKind != kind) throw new FormatException(name + " is missing or not a " + kind.ToString().ToLowerInvariant());
                return value;
            }

            private static string Optional(JsonElement root, string name) =>
                root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";

            private static DateTime Date(JsonElement root, string name)
            {
                var text = Required(root, name, JsonValueKind.String).GetString();
                if (!DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out var value)) throw new FormatException(name + " is not a date");
                return value;
            }

            // The executable's file name, after the last \ or /.
            private static string Leaf(string path)
            {
                if (string.IsNullOrEmpty(path)) return "";
                var slash = Math.Max(path.LastIndexOf('\\'), path.LastIndexOf('/'));
                return slash < 0 ? path : path.Substring(slash + 1);
            }
        }

        // The production machine (Windows): HTTP on one client (each request under its own deadline), docker on the host's launcher
        // (its output read), the Win32 process table (ZombieAgentReaper's reader) and the host's process table for liveness.
        private sealed class PtyHostReaperOs : IPtyHostReaperSeams
        {
            private static readonly HttpClient Client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
            private readonly NightlyHost _host;

            public PtyHostReaperOs(NightlyHost host) => _host = host;

            public NightlyReportHttpResponse Http(string method, string uri, TimeSpan timeout)
            {
                using var deadline = new CancellationTokenSource(timeout);
                using var request = new HttpRequestMessage(new HttpMethod(method), uri);
                using var response = Client.SendAsync(request, deadline.Token).GetAwaiter().GetResult();
                var body = response.Content.ReadAsStringAsync(deadline.Token).GetAwaiter().GetResult();
                return new NightlyReportHttpResponse((int)response.StatusCode, body ?? "");
            }

            public ZombieDatabaseAnswer Query(string container, string user, string database, string sql)
            {
                var target = DesktopPostgres.Resolve(_host, container, user, database);
                return new ZombieDatabaseAnswer(0, target.Query(sql));
            }

            public IReadOnlyList<PtyHostProcess> Processes()
            {
                if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("the census reads the Windows process table.");
                return ZombieAgentReaper.Win32Processes.Read().Select(row => new PtyHostProcess(row.Process.ProcessId, row.Process.Name, row.Process.CreationDate)).ToArray();
            }

            public bool IsAlive(int pid) => _host.Processes.IsAlive(pid);

            public int LivePtyHosts()
            {
                try
                {
                    var hosts = Process.GetProcessesByName("Antiphon.PtyHost");
                    foreach (var process in hosts) process.Dispose();
                    return hosts.Length;
                }
                catch (InvalidOperationException)
                {
                    return 0;
                }
            }
        }
    }
}
