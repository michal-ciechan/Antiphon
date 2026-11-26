// The live lane's portable checkpoint commissions (CARD-1545 S1c), ported from scripts/test-live-lane-isolation.ps1 and the
// RunPortableRows case of scripts/test-live-lane-evidence.ps1 (W2 slice S22; both scripts are deleted) as the CLI commands
// live-lane-isolation-rows and live-lane-evidence-rows. Each creates a fresh owned context, imports its plan with the
// checkpoint tool, rewrites the manifest for this run (its own build outputs, smaller budgets, the context in the rows'
// environment), and runs the listed groups to terminal completion with `dotnet <tool> start` and `wait` (exit 75: still
// running). They own no build slot (the checkpoint runner owns the one slot layer) and run no provider: the rows use fake
// dedicated profiles (S1c-profile) and synthetic evidence (S1c-sanitization). Children, the clock and the console come
// through ILiveLaneCommissionHost (the CLI binds the real machine, a test a fake checkpoint tool). The budgets are
// constants here, which CheckpointCallerBudgetTests lints against the plans' estimates. Plain C#, framework only. ASCII only.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Antiphon.LiveLane
{
    /// <summary>One child of a commission.</summary>
    public sealed class LiveLaneProcessStart
    {
        public string FileName { get; set; }
        public IReadOnlyList<string> Arguments { get; set; } = Array.Empty<string>();
        public string WorkingDirectory { get; set; }
        /// <summary>Variables set (a null value removes one) on top of this process's environment.</summary>
        public IReadOnlyDictionary<string, string> Environment { get; set; } = new Dictionary<string, string>();
        /// <summary>True: stdout is captured into the result. False: the child writes to this process's console.</summary>
        public bool Capture { get; set; }
        public TimeSpan Timeout { get; set; } = TimeSpan.FromHours(2);
    }

    /// <summary>The machine a commission runs on: children, a pause between polls and the console.</summary>
    public interface ILiveLaneCommissionHost
    {
        /// <summary>Runs the child to its exit (Exit null when it could not start or outran its timeout, then it is killed).</summary>
        LiveLaneProcessResult Run(LiveLaneProcessStart start);
        void Sleep(TimeSpan delay);
        TextWriter Out { get; }
        TextWriter Error { get; }
    }

    /// <summary>A commission failed (the scripts threw): the message is printed on stderr, exit 1.</summary>
    public sealed class LiveLaneCommissionException : Exception
    {
        public LiveLaneCommissionException(string message) : base(message) { }
    }

    /// <summary>What the commissions share: the checkpoint tool's import, start and wait.</summary>
    public abstract class LiveLaneCommissionBase
    {
        protected LiveLaneCommissionBase(string repository, string tool, ILiveLaneCommissionHost host)
        {
            Repository = repository;
            Tool = tool;
            Host = host;
        }

        protected string Repository { get; }
        protected string Tool { get; }
        protected ILiveLaneCommissionHost Host { get; }
        protected Dictionary<string, string> ChildEnvironment { get; } = new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>git rev-parse HEAD in the repository: the expected source of every row.</summary>
        protected string Source()
        {
            var result = Host.Run(new LiveLaneProcessStart { FileName = "git", Arguments = new[] { "-C", Repository, "rev-parse", "HEAD" }, Capture = true, Timeout = TimeSpan.FromSeconds(30) });
            var source = result.Stdout.Trim();
            if (result.Exit != 0 || source.Length != 40) throw new LiveLaneCommissionException("Cannot read source SHA");
            return source;
        }

        /// <summary>dotnet &lt;tool&gt; import --plan &lt;plan&gt; --out &lt;manifest&gt;: the exit code (its output reaches the console).</summary>
        protected int Import(string plan, string manifest) =>
            Dotnet(new[] { Tool, "import", "--plan", Path.Combine(Repository, plan), "--out", manifest }, false).Exit ?? 1;

        protected LiveLaneProcessResult Dotnet(IEnumerable<string> arguments, bool capture) =>
            Host.Run(new LiveLaneProcessStart
            {
                FileName = "dotnet", Arguments = arguments.ToArray(), WorkingDirectory = Repository, Environment = new Dictionary<string, string>(ChildEnvironment, StringComparer.Ordinal), Capture = capture,
                Timeout = TimeSpan.FromHours(2)
            });

        protected static string NewKey(int length) => Guid.NewGuid().ToString("N").Substring(0, length);

        /// <summary>Get-FileHash: the SHA-256 of a file, upper-case hex.</summary>
        protected static string FileHash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

        protected static string Json(object value) => JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>
    /// live-lane-isolation-rows (test-live-lane-isolation.ps1): the S1c-profile producer row CP-18 (PTY callers), then the
    /// consumers CP-24 and its exact-filter CP-24-PARITY row (C680), under one owned portable context
    /// (ANTIPHON_LIVE_PORTABLE_CONTEXT) that binds the source, the run, and after the producer its receipt and build. With
    /// --controls the PC-V7..V17 control rows run too (V-7..V-12 with the producer, V-13..V-17 with the consumers).
    /// </summary>
    public sealed class LiveLaneIsolationRows : LiveLaneCommissionBase
    {
        public const string Name = "live-lane-isolation-rows";
        public const string Usage = Name + " --checkpoint-tool <path> [--controls]";
        public const string Plan = "docs/superpowers/plans/2026-10-31-card-1545-s1c-test-design.md";
        public const int TotalMinutes = 26;
        public const int HardWallMinutes = 29;
        public const string TotalOverrideReason = "Bounded live-lane diagnostics retain the 26m execution and 29m wall caps, including controls";
        public static readonly IReadOnlyList<string> ProducerRows = new[] { "CP-18" };
        public static readonly IReadOnlyList<string> ConsumerRows = new[] { "CP-24", "CP-24-PARITY" };
        /// <summary>The last control variant that runs with the producer (PTY); the later ones run with the consumers (C680).</summary>
        public const int LastProducerControl = 12;
        public const int ControlEstimateMinutes = 1;
        public static readonly IReadOnlyList<(int Variant, string Method)> Controls = new[]
        {
            (7, "DedicatedConfigOwnsTrustAndEvidence"), (8, "CwdInstructionsCannotLeak"), (9, "SettingsProjectionDetectsDrift"), (10, "UnknownConfigDisablesReuse"),
            (11, "LaunchArgumentsKeepProductionSources"), (12, "ModelSelectionComesFromContext"),
            (13, "DedicatedConfigOwnsTrustAndEvidence"), (14, "CwdInstructionsCannotLeak"), (15, "SettingsProjectionDetectsDrift"), (16, "UnknownConfigDisablesReuse"),
            (17, "StartupArgvMatchesProductionApplyOff")
        };

        private static readonly Regex RunStarted = new Regex("RUN ([^ ]+) started", RegexOptions.CultureInvariant);
        private readonly bool _controls;
        private readonly string _scratchParent;

        public LiveLaneIsolationRows(string repository, string tool, bool controls, ILiveLaneCommissionHost host, string scratchParent = null)
            : base(repository, tool, host)
        {
            _controls = controls;
            _scratchParent = scratchParent;
        }

        /// <summary>The manifest text for this run: the imported plan rewritten as the script rewrote it, and the extra rows.</summary>
        public static string Rewrite(string manifest, string runKey, string contextPath, bool controls, out List<string> producerRows, out List<string> consumerRows)
        {
            manifest = manifest.Replace("properties: {}", "properties:\n    UseAppHost: false").Replace("slotWaitMinutes: 45", "slotWaitMinutes: 25").Replace("rowMinutes: 15", "rowMinutes: 16");
            manifest = manifest.Replace("bin-live-pty-isolation", "bin-" + runKey + "-pty").Replace("bin-live-isolation", "bin-" + runKey + "-c680");
            manifest = manifest.Replace("timeoutMinutes: 18", "timeoutMinutes: 2").Replace("timeoutMinutes: 30", "timeoutMinutes: 4");
            manifest = manifest.Replace("environment: {}", "environment:\n    ANTIPHON_LIVE_PORTABLE_CONTEXT: '" + contextPath + "'");
            // TUnit omits Explicit under the class wildcard, while the checkpoint filter grammar refuses an exact-name OR.
            // Materialize CP-24's fifth method as one exact row on its build.
            manifest = manifest.Replace("minExecuted: 5\n", "minExecuted: 4\n");
            manifest += "\n\n- id: CP-24-PARITY\n  repeat: 1\n  after: [S1c-profile]\n  build: bin-" + runKey + "-c680\n  group: portable-live-c680-isolation\n"
                + "  filter: /*/Antiphon.Tests.Scripts/LiveLaneIsolationTests/StartupArgvMatchesProductionApplyOff\n  expect: [StartupArgvMatchesProductionApplyOff]\n"
                + "  minExecuted: 1\n  estimatedMinutes: 1\n  timeoutMinutes: 4\n  serial: true\n  environment:\n    ANTIPHON_LIVE_PORTABLE_CONTEXT: '" + contextPath + "'";
            producerRows = ProducerRows.ToList();
            consumerRows = ConsumerRows.ToList();
            if (!controls) return manifest;
            foreach (var (variant, method) in Controls)
            {
                var pty = variant <= LastProducerControl;
                var build = pty ? "bin-" + runKey + "-pty" : "bin-" + runKey + "-c680";
                var prefix = pty ? "/*/Antiphon.Agents.Pty.Tests/LiveClaudeIsolationWiringTests/" : "/*/Antiphon.Tests.Scripts/LiveLaneIsolationTests/";
                var minutes = pty ? 2 : 4;
                var id = "PC-V" + variant.ToString(CultureInfo.InvariantCulture);
                manifest += "\n\n- id: " + id + "\n  repeat: 1\n  after: [S1c-profile]\n  build: " + build + "\n  group: portable-profile-controls\n  filter: " + prefix + method
                    + "\n  expect: [" + method + "]\n  minExecuted: 1\n  estimatedMinutes: " + ControlEstimateMinutes.ToString(CultureInfo.InvariantCulture)
                    + "\n  timeoutMinutes: " + minutes.ToString(CultureInfo.InvariantCulture) + "\n  serial: true\n  environment:\n    ANTIPHON_LIVE_PORTABLE_CONTEXT: '"
                    + contextPath + "'\n    C1545_PROFILE_CONTROLS: V-" + variant.ToString(CultureInfo.InvariantCulture);
                (pty ? producerRows : consumerRows).Add(id);
            }
            return manifest;
        }

        /// <summary>Runs the producer group, binds its receipt and build into the context, then the consumer group. Exit 0, else the import's exit or 1.</summary>
        public int Run()
        {
            try
            {
                var source = Source();
                var runKey = "f727-" + NewKey(8);
                var scratch = string.IsNullOrEmpty(_scratchParent) ? Directory.CreateTempSubdirectory("c1545-portable-").FullName
                    : Directory.CreateDirectory(Path.Combine(_scratchParent, "c1545-portable-" + NewKey(8))).FullName;
                File.WriteAllText(Path.Combine(scratch, "inert-executable"), "inert attested portable executable");
                var contextPath = Path.Combine(scratch, "context.json");
                var context = new Dictionary<string, object>
                {
                    ["root"] = scratch, ["source"] = source, ["run"] = runKey, ["producerBuild"] = null, ["producerReceipt"] = null, ["producerReceiptHash"] = null
                };
                File.WriteAllText(contextPath, Json(context));
                var manifestPath = Path.Combine(Repository, ".antiphon", runKey + ".yaml");
                Directory.CreateDirectory(Path.GetDirectoryName(manifestPath));
                var imported = Import(Plan, manifestPath);
                if (imported != 0) return imported;
                var manifest = Rewrite(File.ReadAllText(manifestPath).Replace("\r\n", "\n"), runKey, contextPath, _controls, out var producerRows, out var consumerRows);
                File.WriteAllText(manifestPath, manifest + "\n");
                ChildEnvironment["TEMP"] = "/tmp";
                var producer = Group(manifestPath, producerRows, source, contextPath);
                var receipt = Path.Combine(Repository, ".antiphon", "checkpoints", producer, "report.json");
                context["producerReceipt"] = receipt;
                context["producerReceiptHash"] = FileHash(receipt);
                context["producerBuild"] = FileHash(Path.Combine(Repository, "tests", "Antiphon.Agents.Pty.Tests", "bin-" + runKey + "-pty", "Antiphon.Agents.Pty.Tests.dll"));
                File.WriteAllText(contextPath, Json(context));
                var consumer = Group(manifestPath, consumerRows, source, contextPath);
                Host.Out.WriteLine("PROFILE COMPLETE context=" + contextPath + " producer=" + receipt + " consumer=" + consumer);
                return 0;
            }
            catch (Exception e) when (e is LiveLaneCommissionException || e is IOException || e is UnauthorizedAccessException)
            {
                Host.Error.WriteLine(Name + ": " + e.Message);
                return 1;
            }
        }

        // Invoke-Group: start the rows (its output printed), then wait until the run is terminal.
        private string Group(string manifestPath, IReadOnlyList<string> rows, string source, string contextPath)
        {
            var start = Dotnet(new[]
            {
                Tool, "start", manifestPath, "--rows", string.Join(",", rows), "--expected-source-sha", source, "--serial",
                "--total-timeout", TotalMinutes.ToString(CultureInfo.InvariantCulture) + "m", "--total-timeout-override-reason", TotalOverrideReason,
                "--hard-wall-timeout", HardWallMinutes.ToString(CultureInfo.InvariantCulture) + "m", "--keep-outputs"
            }, true);
            Host.Out.Write(start.Stdout);
            if (start.Exit != 0) throw new LiveLaneCommissionException("Checkpoint start refused (" + (start.Exit?.ToString(CultureInfo.InvariantCulture) ?? "no exit") + ")");
            var match = RunStarted.Match(start.Stdout);
            if (!match.Success) throw new LiveLaneCommissionException("Missing owned run identity");
            var run = match.Groups[1].Value;
            int? code;
            do
            {
                code = Dotnet(new[] { Tool, "wait", "--run", run, "--max-wait", "50s", "--expected-source-sha", source }, false).Exit;
            } while (code == 75);
            if (code != 0) throw new LiveLaneCommissionException("Checkpoint group failed (" + (code?.ToString(CultureInfo.InvariantCulture) ?? "no exit") + "), run=" + run + ", context=" + contextPath);
            return run;
        }
    }

    /// <summary>
    /// live-lane-evidence-rows (test-live-lane-evidence.ps1 -Case RunPortableRows): the S1c-sanitization rows CP-20 (PTY
    /// callers) and CP-26 (C680), then the scan row CP-29 as a second run started only after both are green (a command row is
    /// admitted before test builds, and CP-29's deadline cannot cover them), then the outer publication. The owned context
    /// (ANTIPHON_LIVE_EVIDENCE_CONTEXT) binds the source, the tests' checkpoint run and the marker-case roster.
    /// </summary>
    public sealed class LiveLaneEvidenceRows : LiveLaneCommissionBase
    {
        public const string Name = "live-lane-evidence-rows";
        public const string Usage = Name + " --checkpoint-tool <path>";
        public const string Plan = "docs/superpowers/plans/2026-10-31-card-1545-s1c-sanitization-checkpoints.md";
        public const string ContextVariable = "ANTIPHON_LIVE_EVIDENCE_CONTEXT";
        /// <summary>The two groups and their total and hard-wall minutes (each start carries its own total).</summary>
        public static readonly IReadOnlyList<(string Rows, int TotalMinutes, int HardMinutes)> Groups = new[] { ("CP-20,CP-26", 70, 90), ("CP-29", 20, 25) };
        /// <summary>CP-29's command in the plan; on Windows the run rewrites it to the cmd launcher (a command row runs in cmd.exe there).</summary>
        public const string ScanCommand = "scripts/antiphon live-lane-evidence CompletedRows";
        public const string WindowsScanCommand = "scripts\\antiphon.cmd live-lane-evidence CompletedRows";

        private readonly Func<IReadOnlyList<string>> _roster;
        private readonly Func<string, int> _publish;
        private readonly bool _windows;

        /// <param name="roster">The marker-case roster ("case test cp" lines; LiveLaneEvidence.PortableRoster in the CLI).</param>
        /// <param name="publish">The outer publication of a context (live-lane-evidence OuterPublish): its exit code.</param>
        public LiveLaneEvidenceRows(string repository, string tool, ILiveLaneCommissionHost host, Func<IReadOnlyList<string>> roster, Func<string, int> publish, bool windows)
            : base(repository, tool, host)
        {
            _roster = roster;
            _publish = publish;
            _windows = windows;
        }

        /// <summary>The manifest text for this run: the imported plan rewritten as the script rewrote it (and CP-29's launcher on Windows).</summary>
        public static string Rewrite(string manifest, string runKey, bool windows)
        {
            manifest = manifest.Replace("properties: {}", "properties:\n    UseAppHost: false").Replace("slotWaitMinutes: 45", "slotWaitMinutes: 25");
            manifest = manifest.Replace("bin-live-pty-evidence", "bin-" + runKey + "-pty").Replace("bin-live-evidence", "bin-" + runKey + "-c680");
            return windows ? manifest.Replace(ScanCommand, WindowsScanCommand) : manifest;
        }

        /// <summary>Exit 0 published; 2 context-absent (no tool); otherwise the failing step's exit, or 1 for a failed step without one.</summary>
        public int Run()
        {
            if (string.IsNullOrWhiteSpace(Tool) || !File.Exists(Tool))
            {
                Host.Out.WriteLine("INVALID/evidence-sanitization reason=context-absent");
                return 2;
            }
            try
            {
                var source = Source();
                var runKey = "s1c-ev-g" + NewKey(6);
                var owned = Path.Combine(Repository, ".antiphon", runKey);
                Directory.CreateDirectory(owned);
                var export = Path.Combine(owned, "cp20-export.txt");
                File.WriteAllText(export, "");
                var cases = new List<Dictionary<string, string>>();
                foreach (var line in _roster())
                {
                    var parts = (line ?? "").Split(' ');
                    if (parts.Length != 3) throw new LiveLaneCommissionException("roster line is not case test cp");
                    cases.Add(new Dictionary<string, string> { ["case"] = parts[0], ["test"] = parts[1], ["cp"] = parts[2] });
                }
                var context = new Dictionary<string, object>
                {
                    ["source"] = source, ["repo"] = Repository, ["runToken"] = "portable", ["checkpointRun"] = "",
                    ["resultsRoot"] = Path.Combine(Repository, ".antiphon", "checkpoints"), ["exportPath"] = export, ["receipt"] = Path.Combine(owned, "receipt.txt"),
                    ["summary"] = Path.Combine(owned, "summary.txt"), ["publicSummary"] = Path.Combine(owned, "public-summary.txt"), ["requirePublic"] = true,
                    ["cases"] = cases,
                    ["outputs"] = new[]
                    {
                        new Dictionary<string, string> { ["id"] = "CP-20", ["path"] = "tests/Antiphon.Agents.Pty.Tests/bin-" + runKey + "-pty" },
                        new Dictionary<string, string> { ["id"] = "CP-26", ["path"] = "tests/Antiphon.Tests/bin-" + runKey + "-c680" }
                    }
                };
                var contextPath = Path.Combine(owned, "context.json");
                File.WriteAllText(contextPath, Json(context));
                var manifestPath = Path.Combine(owned, "manifest.yaml");
                var imported = Import(Plan, manifestPath);
                if (imported != 0) return imported;
                File.WriteAllText(manifestPath, Rewrite(File.ReadAllText(manifestPath), runKey, _windows));
                ChildEnvironment["TEMP"] = "/tmp";
                ChildEnvironment["TMPDIR"] = "/tmp";
                ChildEnvironment[ContextVariable] = contextPath;
                // Start returns before the executor finishes; its output is not captured (the detached executor would hold
                // the pipe for the whole run).
                var tests = Start(manifestPath, Groups[0], source, out var exit);
                if (tests == null) return exit;
                Host.Out.WriteLine("tests-run=" + tests);
                context["checkpointRun"] = tests;
                File.WriteAllText(contextPath, Json(context));
                var code = Wait(owned, tests, source);
                if (code != 0) return code;
                var scan = Start(manifestPath, Groups[1], source, out exit);
                if (scan == null) return exit;
                Host.Out.WriteLine("scan-run=" + scan);
                code = Wait(owned, scan, source);
                if (code != 0) return code;
                return _publish(contextPath);
            }
            catch (Exception e) when (e is LiveLaneCommissionException || e is IOException || e is UnauthorizedAccessException || e is JsonException)
            {
                Host.Error.WriteLine(Name + ": " + e.Message);
                return 1;
            }
        }

        // Invoke-EvidenceCheckpoint: the start (3 minutes at most), then the one new run directory and its state's run id.
        private string Start(string manifestPath, (string Rows, int TotalMinutes, int HardMinutes) group, string source, out int exit)
        {
            var root = Path.Combine(Repository, ".antiphon", "checkpoints");
            Directory.CreateDirectory(root);
            var before = new HashSet<string>(Directory.GetDirectories(root), StringComparer.Ordinal);
            var started = Host.Run(new LiveLaneProcessStart
            {
                FileName = "dotnet", WorkingDirectory = Repository, Environment = new Dictionary<string, string>(ChildEnvironment, StringComparer.Ordinal), Capture = false, Timeout = TimeSpan.FromMinutes(3),
                Arguments = new[]
                {
                    Tool, "start", manifestPath, "--rows", group.Rows, "--expected-source-sha", source, "--serial",
                    "--total-timeout", group.TotalMinutes.ToString(CultureInfo.InvariantCulture) + "m",
                    "--hard-wall-timeout", group.HardMinutes.ToString(CultureInfo.InvariantCulture) + "m", "--keep-outputs"
                }
            });
            if (started.Exit == null) throw new LiveLaneCommissionException("checkpoint start did not return");
            Host.Out.WriteLine("start-exit=" + started.Exit.Value.ToString(CultureInfo.InvariantCulture) + " rows=" + group.Rows);
            exit = started.Exit.Value;
            if (exit != 0) return null;
            string run = null;
            for (var attempt = 0; attempt < 25 && run == null; attempt++)
            {
                var created = Directory.GetDirectories(root).Where(directory => !before.Contains(directory)).ToList();
                if (created.Count > 1) throw new LiveLaneCommissionException("checkpoint run identity count=" + created.Count.ToString(CultureInfo.InvariantCulture));
                if (created.Count == 1) run = Path.GetFileName(created[0].TrimEnd('\\', '/'));
                else Host.Sleep(TimeSpan.FromMilliseconds(200));
            }
            if (string.IsNullOrWhiteSpace(run)) throw new LiveLaneCommissionException("Missing checkpoint run identity");
            var statePath = Path.Combine(root, run, "state.json");
            var state = "";
            for (var attempt = 0; attempt < 25; attempt++)
            {
                if (File.Exists(statePath))
                {
                    state = File.ReadAllText(statePath);
                    if (state.Contains("\"runId\"")) break;
                }
                Host.Sleep(TimeSpan.FromMilliseconds(200));
            }
            using var document = JsonDocument.Parse(state);
            if (!document.RootElement.TryGetProperty("runId", out var runId) || runId.ValueKind != JsonValueKind.String || runId.GetString() != run)
                throw new LiveLaneCommissionException("checkpoint run id mismatch");
            return run;
        }

        // Wait-EvidenceCheckpoint: wait in 50 s slices (75: still running), each slice's output kept in a log beside the
        // context and printed.
        private int Wait(string owned, string run, string source)
        {
            int code;
            do
            {
                var result = Dotnet(new[] { Tool, "wait", "--run", run, "--max-wait", "50s", "--expected-source-sha", source }, true);
                var log = Path.Combine(owned, "wait-" + run + "-" + NewKey(6) + ".log");
                File.WriteAllText(log, result.Stdout);
                Host.Out.WriteLine(result.Stdout);
                code = result.Exit ?? 1;
                Host.Out.WriteLine("wait-exit=" + code.ToString(CultureInfo.InvariantCulture) + " run=" + run);
            } while (code == 75);
            return code;
        }
    }
}