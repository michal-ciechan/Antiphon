// The CARD-0849 desktop front door (unit 67 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md, W10 part 2): what
// scripts/verify-card0849-caches.ps1 did, as the verify-card0849-caches command. The CLI runs it as
//
//   antiphon-scripts verify-card0849-caches --case <Inventory|Fixture|Seed|Reset|Both|Retired|PrunePreview|Prune> [--sha <sha>]
//     [--preview <receipt>] [--saved-donor <server2 path>] [--cold]
//
// Explicit cases only; no case starts a rollout. In the script's order: the reviewed full lowercase SHA (--sha, else
// C849_DEPLOY_SHA) must equal the checkout's HEAD; --preview belongs to Prune only (and Prune needs it), --saved-donor and --cold to
// Seed only and not together; a saved donor is an absolute server2 path; a preview is preview.txt in a runner-cache-prune-preview
// directory under the checkout's .antiphon whose run= and source-sha= lines match. Then one run id (c849 and 16 hex), its evidence
// directory <checkout>/.antiphon/c849-<run>, and for each remote case (Both: verify-runner-caches for server2, then for server2-temp)
// a manifest <evidence>/<i>/manifest.json (compact JSON, ASCII, a line end) handed to the stack verifier IN PROCESS (the
// verify-docker-stack command's DockerStackVerify and its live server2 bridge C590Bridge; the script started
// scripts/verify-docker-stack.ps1 by path). A case that exits non-zero, or whose c590-result.json is not accepted with exit 0,
// stops the front door. The receipts each case copied back are then checked exactly as the script checked them, and the summary
// line and C849_RECEIPT are printed. A refusal is exit 1 and one stderr line, "verify-card0849-caches: <reason>" (the script threw
// it); the reasons are the script's words. Usage is 2.
//
// Values are read as PowerShell 7 read them (RollingPs, PlainValues): -eq, -ne, -match and -notmatch ignore case; -ceq, -cne,
// -cmatch and -cnotmatch do not. The verifier, git and new ids are seams (C849CacheFrontDoorSeams); files are IFileSystem's.
// Plain C#: no PowerShell types. ASCII only.
#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Antiphon.Nightly
{
    /// <summary>The verify-card0849-caches command's options (verify-card0849-caches.ps1's -Case, -Sha, -Preview, -SavedDonor and -Cold).</summary>
    public sealed class C849CacheFrontDoorOptions
    {
        /// <summary>The CLI's usage line.</summary>
        public const string Usage = "verify-card0849-caches --case <Inventory|Fixture|Seed|Reset|Both|Retired|PrunePreview|Prune> [--sha <sha>] [--preview <receipt>] [--saved-donor <path>] [--cold]";

        /// <summary>The cases (the script's ValidateSet; matched ignoring case).</summary>
        public static readonly IReadOnlyList<string> Cases = new[] { "Inventory", "Fixture", "Seed", "Reset", "Both", "Retired", "PrunePreview", "Prune" };

        /// <summary>The case, as given.</summary>
        public string Case { get; init; } = "";

        /// <summary>The reviewed SHA (empty: C849_DEPLOY_SHA).</summary>
        public string Sha { get; init; } = "";

        /// <summary>Prune's preview receipt.</summary>
        public string Preview { get; init; } = "";

        /// <summary>Seed's saved donor archive on server2.</summary>
        public string SavedDonor { get; init; } = "";

        /// <summary>Seed's volumes-only cold mode.</summary>
        public bool Cold { get; init; }

        /// <summary>The checkout (the script's repository: its HEAD, its .antiphon evidence root).</summary>
        public string Checkout { get; init; } = "";

        /// <summary>The arguments after the command name; checkout is the CLI's own.</summary>
        public static C849CacheFrontDoorOptions Parse(IReadOnlyList<string> arguments, string checkout)
        {
            var flags = NightlyArgs.Parse(arguments, new[] { "--case", "--sha", "--preview", "--saved-donor" }, new[] { "--cold" });
            var name = flags.Required("--case");
            if (!Cases.Contains(name, StringComparer.OrdinalIgnoreCase))
                throw new NightlyUsageException("--case must be one of " + string.Join(", ", Cases) + ", not '" + name + "'");
            return new C849CacheFrontDoorOptions
            {
                Case = name,
                Sha = flags.Value("--sha", ""),
                Preview = flags.Value("--preview", ""),
                SavedDonor = flags.Value("--saved-donor", ""),
                Cold = flags.Switch("--cold"),
                Checkout = checkout ?? "",
            };
        }
    }

    /// <summary>What the front door does outside its files.</summary>
    public sealed class C849CacheFrontDoorSeams
    {
        /// <summary>
        /// The stack verifier for one remote case and its manifest; the exit code (its output on the host's streams). Production:
        /// DockerStackVerify.RunInProcess with the live bridge. Required.
        /// </summary>
        public Func<string, string, int> Verify { get; init; }

        /// <summary>&amp; git -C &lt;repo&gt; rev-parse HEAD: its exit code and its output lines (none: an empty list). Required.</summary>
        public Func<string, (int Exit, IReadOnlyList<string> Lines)> GitHead { get; init; }

        /// <summary>The run id's guid. Default Guid.NewGuid.</summary>
        public Func<Guid> NewGuid { get; init; } = Guid.NewGuid;
    }

    /// <summary>The CARD-0849 front door.</summary>
    public static class C849CacheFrontDoor
    {
        /// <summary>The remote case of each single case (Both runs verify-runner-caches twice).</summary>
        public static readonly IReadOnlyDictionary<string, string> RemoteCases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Inventory"] = "runner-cache-inventory",
            ["Fixture"] = "runner-cache-fixture",
            ["Seed"] = "runner-cache-seed",
            ["Reset"] = "runner-cache-reset",
            ["Retired"] = "verify-runner-caches-retired",
            ["PrunePreview"] = "runner-cache-prune-preview",
            ["Prune"] = "runner-cache-prune",
        };

        /// <summary>The message PowerShell threw for a method call on a null value (git printed nothing).</summary>
        public const string NullMethod = "You cannot call a method on a null-valued expression.";

        /// <summary>Runs one case: exit 0 accepted, 1 refused (one stderr line). Usage errors throw NightlyUsageException.</summary>
        public static int Run(C849CacheFrontDoorOptions options, NightlyHost host, IFileSystem files, C849CacheFrontDoorSeams seams)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (host == null) throw new ArgumentNullException(nameof(host));
            if (files == null) throw new ArgumentNullException(nameof(files));
            if (seams?.Verify == null || seams.GitHead == null) throw new ArgumentNullException(nameof(seams));
            try
            {
                new Front(options, host, files, seams).Run();
                return 0;
            }
            catch (Exception e) when (e is C590Failure || e is RollingJsonException || e is IOException || e is UnauthorizedAccessException || e is ArgumentException
                || e is NotSupportedException)
            {
                host.Error.WriteLine("verify-card0849-caches: " + e.Message);
                return 1;
            }
        }

        private sealed class Front
        {
            private readonly C849CacheFrontDoorOptions _o;
            private readonly NightlyHost _host;
            private readonly IFileSystem _files;
            private readonly C849CacheFrontDoorSeams _seams;
            private string _sha;
            private string _evidence;
            private string[] _cases;
            private string[] _runners;

            internal Front(C849CacheFrontDoorOptions options, NightlyHost host, IFileSystem files, C849CacheFrontDoorSeams seams)
            {
                _o = options;
                _host = host;
                _files = files;
                _seams = seams;
            }

            private static C590Failure Refuse(string reason) => new C590Failure(reason);

            private bool Is(string name) => string.Equals(_o.Case, name, StringComparison.OrdinalIgnoreCase);

            private void Line(string text) => _host.Out.WriteLine(text);

            internal void Run()
            {
                var repo = _o.Checkout;
                var sha = _o.Sha ?? "";
                if (sha.Length == 0) sha = _host.Variables("C849_DEPLOY_SHA") ?? "";
                if (!Regex.IsMatch(sha, "^[0-9a-f]{40}$")) throw Refuse("C849_DEPLOY_SHA must be the reviewed full lowercase SHA");
                _sha = sha;
                var head = _seams.GitHead(repo);
                if (head.Lines == null || head.Lines.Count == 0) throw Refuse(NullMethod);
                // (& git ...).Trim() -ne $Sha: an output line that differs (ignoring case) refuses, as does a failed git.
                if (head.Exit != 0 || head.Lines.Any(l => !RollingPs.Eq((l ?? "").Trim(), sha))) throw Refuse("C849 source SHA does not equal checkout HEAD");
                var preview = _o.Preview ?? "";
                var savedDonor = _o.SavedDonor ?? "";
                if (Is("Prune") && preview.Length == 0) throw Refuse("Prune requires -Preview receipt path");
                if (!Is("Prune") && preview.Length > 0) throw Refuse("Preview belongs to Prune only");
                if (savedDonor.Length > 0 && !Is("Seed")) throw Refuse("SavedDonor belongs to Seed only");
                if (_o.Cold && !Is("Seed")) throw Refuse("CacheColdModeInvalid");
                if (_o.Cold && savedDonor.Length > 0) throw Refuse("CacheDonorSourceConflict");
                if (savedDonor.Length > 0 && (!Regex.IsMatch(savedDonor, "^/[A-Za-z0-9._/-]{1,500}$") || savedDonor.Contains("..", StringComparison.Ordinal)
                    || savedDonor.Contains("//", StringComparison.Ordinal)))
                    throw Refuse("CacheSavedDonorPathInvalid");
                if (preview.Length > 0)
                {
                    var candidate = _files.Path.GetFullPath(preview);
                    var root = _files.Path.GetFullPath(_files.Path.Combine(repo, ".antiphon"));
                    if (!candidate.StartsWith(root + _files.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !_files.File.Exists(candidate))
                        throw Refuse("CachePreviewInvalid");
                    preview = candidate;
                }
                var previewRunId = "";
                if (Is("Prune"))
                {
                    var previewText = _files.File.ReadAllText(preview);
                    var run = Regex.Match(previewText, "(?m)^run=(c849[0-9a-f]{16}0)\\r?$");
                    var source = Regex.Match(previewText, "(?m)^source-sha=([0-9a-f]{40})\\r?$");
                    if (!run.Success || !source.Success || !string.Equals(source.Groups[1].Value, sha, StringComparison.Ordinal)
                        || !string.Equals(_files.Path.GetFileName(preview), "preview.txt", StringComparison.Ordinal)
                        || !string.Equals(_files.Path.GetFileName(_files.Path.GetDirectoryName(preview)), "runner-cache-prune-preview", StringComparison.Ordinal))
                        throw Refuse("CachePreviewInvalid");
                    previewRunId = run.Groups[1].Value;
                }
                var runId = "c849" + _seams.NewGuid().ToString("N").Substring(0, 16);
                _evidence = _files.Path.Combine(repo, ".antiphon", "c849-" + runId);
                _files.Directory.CreateDirectory(_evidence);
                _cases = Is("Both") ? new[] { "verify-runner-caches", "verify-runner-caches" } : new[] { RemoteCases[_o.Case] };
                _runners = Is("Both") ? new[] { "server2", "server2-temp" } : new[] { "" };
                for (var i = 0; i < _cases.Length; i++)
                {
                    var remoteCase = _cases[i];
                    var evidenceRoot = _files.Path.Combine(_evidence, I(i));
                    var manifest = RollingPs.Object(("evidenceRoot", evidenceRoot), ("sourceSha", sha), ("runId", runId + I(i)), ("c604Branch", "master"));
                    if (_runners[i].Length > 0) manifest.Add("runnerId", _runners[i]);
                    if (Is("Both") || Is("Retired")) manifest.Add("expectAccepting", true);
                    if (preview.Length > 0) manifest.Add("preview", preview);
                    if (previewRunId.Length > 0) manifest.Add("previewRunId", previewRunId);
                    if (savedDonor.Length > 0) manifest.Add("savedDonor", savedDonor);
                    if (_o.Cold) manifest.Add("coldSeed", true);
                    _files.Directory.CreateDirectory(evidenceRoot);
                    var manifestPath = _files.Path.Combine(evidenceRoot, "manifest.json");
                    // ConvertTo-Json -Compress | Set-Content -Encoding ascii: one line and this platform's line end.
                    _files.File.WriteAllText(manifestPath, RollingPs.Json(manifest) + Environment.NewLine, Encoding.ASCII);
                    if (_seams.Verify(remoteCase, manifestPath) != 0)
                        throw Refuse("C849 case failed: " + remoteCase + " runner=" + _runners[i] + " evidence=" + evidenceRoot);
                    var verdict = Json(Receipt(i, "c590-result.json"));
                    if (!RollingPs.Eq(RollingPs.Prop(verdict, "accepted"), true) || !RollingPs.Eq(RollingPs.Prop(verdict, "exit"), 0))
                        throw Refuse("C849 case receipt refused: " + remoteCase);
                }
                Summary();
                Line("C849_RECEIPT case=" + _o.Case + " source=" + sha + " evidence=" + _evidence);
            }

            private static string I(int i) => i.ToString(CultureInfo.InvariantCulture);

            // $text | ConvertFrom-Json: an empty receipt reads as no value.
            private static object Json(string text) => string.IsNullOrWhiteSpace(text) ? null : RollingPs.ParseEnumerated(text);

            // Read-C849Receipt: <evidence>/<index>/<case>/<name>, which must be a file.
            private string Receipt(int index, string name)
            {
                var path = _files.Path.Combine(_evidence, I(index), _cases[index], name);
                if (!_files.File.Exists(path)) throw Refuse("C849 receipt missing: " + name);
                return _files.File.ReadAllText(path);
            }

            // $value -lt 0 for the counters (numbers; a string compares as text; a boolean never is).
            private static bool Negative(object value)
            {
                if (value is bool) return false;
                if (value is string || PlainValues.IsNumeric(value)) return PlainValues.Compare(value, 0L) < 0;
                throw Refuse("Cannot compare \"" + RollingPs.Str(value) + "\" because it is not IComparable.");
            }

            // Assert-C849Status.
            private object Status(string text, bool accepting, bool requireIdle = false)
            {
                var status = Json(text);
                object P(string name) => RollingPs.Prop(status, name);
                if (P("sessions") == null || P("runnerSessions") == null || P("queuedTasks") == null
                    || Negative(P("sessions")) || Negative(P("runnerSessions")) || Negative(P("queuedTasks")))
                    throw Refuse("C849 unknown runner counters");
                if (requireIdle && (!RollingPs.Eq(P("sessions"), 0) || !RollingPs.Eq(P("runnerSessions"), 0) || !RollingPs.Eq(P("queuedTasks"), 0)))
                    throw Refuse("C849 runner is busy");
                if (accepting && (!RollingPs.Ceq(P("buildVersion"), _sha) || !RollingPs.Eq(P("dispatchEligible"), true)
                    || !RollingPs.Eq(P("acceptingNewWork"), true) || !RollingPs.Eq(P("draining"), false)))
                    throw Refuse("C849 runner acceptance receipt invalid");
                return status;
            }

            private sealed class Contract
            {
                public string Schema;
                public string Kind;
                public string DigestType;
                public string Digest;
                public string Smoke;
            }

            private static readonly Regex ContractShape = new Regex(
                "\\Aschema=(legacy|2|3)\\nkind=(full|cold)\\ndigest-type=(payload-sha256|none|manifest-sha256)\\ndigest=([0-9a-f]{64}|none)\\nsmoke=(passed|not-run)\\n\\z");

            // Read-C849Contract.
            private Contract ReadContract(int index)
            {
                var match = ContractShape.Match(Receipt(index, "seed-contract.txt"));
                if (!match.Success) throw Refuse("C849 seed contract shape invalid");
                var contract = new Contract
                {
                    Schema = match.Groups[1].Value, Kind = match.Groups[2].Value, DigestType = match.Groups[3].Value, Digest = match.Groups[4].Value, Smoke = match.Groups[5].Value,
                };
                var tuple = contract.Schema + "/" + contract.Kind + "/" + contract.DigestType;
                if (!new[] { "legacy/full/payload-sha256", "2/cold/none", "3/full/manifest-sha256" }.Contains(tuple, StringComparer.Ordinal)
                    || (contract.Kind == "cold") != (contract.Digest == "none")
                    || (contract.Kind == "cold" && contract.Smoke != "not-run"))
                    throw Refuse("C849 seed contract tuple invalid");
                return contract;
            }

            // Assert-C849Smoke.
            private void Smoke(int index, Contract contract, string runner, bool required)
            {
                if (required && contract.Kind == "full" && contract.Smoke != "passed") throw Refuse("C849 full smoke required");
                if (contract.Smoke != "passed") return;
                var pattern = "\\AC849_SMOKE runner=" + Regex.Escape(runner) + " uid=1654 restore=0 build=0 run=0 stdout=CARD0849_APPHOST_OK\\n\\z";
                if (!Regex.IsMatch(Receipt(index, "smoke-summary.txt"), pattern)) throw Refuse("C849 smoke receipt invalid");
            }

            // Assert-C849Mounts.
            private void Mounts(int index, string project)
            {
                var mounts = Receipt(index, "runner-mounts.txt").Split('\n');
                foreach (var line in new[]
                {
                    "volume antiphon-runner-cache-nuget-packages /home/app/.nuget/packages true",
                    "volume antiphon-runner-cache-nuget-scratch /var/cache/antiphon/nuget-scratch true",
                    "volume antiphon-runner-cache-npm-content /home/app/.npm/_cacache true",
                    "volume " + project + "_runner-tmp /tmp true",
                })
                    if (mounts.Count(m => string.Equals(m, line, StringComparison.Ordinal)) != 1) throw Refuse("C849 mount receipt invalid");
            }

            private static string[] Lines(string text) => text.Split('\n').Where(l => l.Length > 0).ToArray();

            private static bool M(string text, string pattern) => Regex.IsMatch(text ?? "", pattern, RegexOptions.IgnoreCase);

            private static bool CM(string text, string pattern) => Regex.IsMatch(text ?? "", pattern);

            private static readonly Regex SmokeLine = new Regex("uid=1654 restore=0 build=0 run=0 stdout=CARD0849_APPHOST_OK", RegexOptions.IgnoreCase);

            private void Summary()
            {
                switch (_o.Case.ToLowerInvariant())
                {
                    case "fixture": Fixture(); break;
                    case "seed": Seed(); break;
                    case "reset":
                        if (!string.Equals(Receipt(0, "reset.txt").Trim(), "reset=true volumes=3 marker=absent", StringComparison.Ordinal)) throw Refuse("C849 reset receipt invalid");
                        Line("C849_RESET volumes=3 marker=absent failures=0");
                        break;
                    case "both": Both(); break;
                    case "retired": Retired(); break;
                    case "prunepreview":
                        var previewReceipt = Receipt(0, "preview.txt");
                        if (!CM(previewReceipt, "(?m)^schema=1\\r?$") || !CM(previewReceipt, "(?m)^volume-sha256=[0-9a-f]{64}\\r?$")) throw Refuse("C849 preview receipt invalid");
                        Line("C849_PRUNE_PREVIEW source=" + _sha + " evidence=" + _evidence);
                        break;
                    case "prune":
                        if (!CM(Receipt(0, "prune.txt"), "pruned=[1-3] .*refill=passed smokes=[12] admission=held")) throw Refuse("C849 prune receipt invalid");
                        Line("C849_PRUNE source=" + _sha + " evidence=" + _evidence);
                        break;
                    case "inventory":
                        Receipt(0, "server2-status.json");
                        Receipt(0, "server2-temp-status.json");
                        Line("C849_INVENTORY runners=2 source=" + _sha + " evidence=" + _evidence);
                        break;
                }
            }

            private static int Families(int number) => number switch
            {
                13 or 14 or 29 or 30 => 2,
                15 or 18 or 27 => 3,
                28 => 4,
                31 => 16,
                _ => 1,
            };

            private void Fixture()
            {
                var lines = Lines(Receipt(0, "fixture-summary.txt"));
                foreach (var line in new[] { "source-sha=" + _sha, "inventories=2", "groups=9", "controls=32", "expected-red=32", "variants=60", "expected-red-variants=60", "production-mutations=0" })
                    if (lines.Count(l => string.Equals(l, line, StringComparison.Ordinal)) != 1) throw Refuse("C849 fixture receipt missing " + line);
                if (lines.Length != 9 || lines.Count(l => CM(l, "^image-id=sha256:[0-9a-f]{64}$")) != 1) throw Refuse("C849 fixture image receipt invalid");
                var groups = Lines(Receipt(0, "fixture-groups.txt"));
                var controls = Lines(Receipt(0, "fixture-controls.txt"));
                var variants = Lines(Receipt(0, "fixture-control-variants.txt"));
                if (groups.Length != 9 || controls.Length != 32 || variants.Length != 60) throw Refuse("C849 fixture roster incomplete");
                for (var number = 1; number <= 9; number++)
                    if (groups.Count(g => string.Equals(g, "PASS F-" + I(number), StringComparison.Ordinal)) != 1) throw Refuse("C849 fixture roster incomplete");
                for (var number = 1; number <= 32; number++)
                {
                    var family = "PC-" + number.ToString("d2", CultureInfo.InvariantCulture);
                    if (controls.Count(c => CM(c, "^CONTROL " + family + " expected-red observed=[A-Za-z][A-Za-z0-9:.-]*$")) != 1) throw Refuse("C849 fixture roster incomplete");
                    for (var ordinal = 1; ordinal <= Families(number); ordinal++)
                    {
                        var variant = family + "." + ordinal.ToString("d2", CultureInfo.InvariantCulture);
                        if (variants.Count(v => CM(v, "^" + Regex.Escape(variant) + " expected-red observed=[A-Za-z][A-Za-z0-9:.-]*$")) != 1) throw Refuse("C849 fixture roster incomplete");
                    }
                }
                Line("C849_FIXTURE groups=9 controls=32 expectedRed=32 variants=60 expectedRedVariants=60 inventories=2 failures=0 productionMutations=0");
            }

            private void Seed()
            {
                var contract = ReadContract(0);
                var seed = Receipt(0, "seed.txt");
                if (string.Equals(seed.Trim(), "ready=true kind=cold writable=3", StringComparison.Ordinal))
                {
                    if (contract.Kind != "cold") throw Refuse("C849 cold seed receipt invalid");
                    var cold = Json(Receipt(0, "status.json"));
                    var main = Json(Receipt(0, "main-status.json"));
                    object P(string name) => RollingPs.Prop(cold, name);
                    var runnerSessions = P("runnerSessions");
                    if (P("retiredAt") == null || !RollingPs.Eq(P("available"), false) || !RollingPs.Eq(P("dispatchEligible"), false) || !RollingPs.Eq(P("acceptingNewWork"), false)
                        || !RollingPs.Eq(P("draining"), true) || !RollingPs.Eq(P("retireWhenIdle"), true) || !RollingPs.Ceq(P("redirectTo"), "server2")
                        || !RollingPs.Eq(P("sessions"), 0) || !RollingPs.Eq(P("queuedTasks"), 0) || (runnerSessions != null && !RollingPs.Eq(runnerSessions, 0))
                        || RollingPs.Prop(main, "sessions") == null || RollingPs.Prop(main, "queuedTasks") == null)
                        throw Refuse("C849 cold seed status invalid");
                    Line("C849_SEED kind=cold schema=2 digestType=none ready=true writable=3");
                    return;
                }
                if (_o.Cold) throw Refuse("C849 cold seed receipt invalid");
                if (contract.Kind != "full") throw Refuse("C849 full seed receipt invalid");
                var reused = CM(seed, "\\Aready=true donor=([0-9a-f]{64})? reused=true\\n\\z");
                if (reused && contract.Smoke != "not-run") throw Refuse("C849 reuse smoke invalid");
                Smoke(0, contract, "seed", !reused);
                if (M(seed, "(?m)^ready=true donor=saved(?: |\\r?$)"))
                {
                    var saved = Json(Receipt(0, "status.json"));
                    object P(string name) => RollingPs.Prop(saved, name);
                    var runnerSessions = P("runnerSessions");
                    if (!RollingPs.Eq(P("sessions"), 0) || !RollingPs.Eq(P("queuedTasks"), 0) || (runnerSessions != null && !RollingPs.Eq(runnerSessions, 0))
                        || !RollingPs.Eq(P("draining"), true) || !RollingPs.Eq(P("retireWhenIdle"), true) || !RollingPs.Eq(P("redirectTo"), "server2"))
                        throw Refuse("C849 saved donor status invalid");
                    Line("C849_SEED donor=saved ready=true schema=" + contract.Schema + " digestType=" + contract.DigestType + " smoke=" + contract.Smoke + " recovery=retained");
                    return;
                }
                if (string.Equals(seed, "ready=true donor= reused=true\n", StringComparison.Ordinal))
                {
                    if (contract.Smoke != "not-run") throw Refuse("C849 reuse smoke invalid");
                    Line("C849_SEED donor=none ready=true schema=" + contract.Schema + " digestType=" + contract.DigestType + " smoke=not-run recovery=retained");
                    return;
                }
                var status = Status(Receipt(0, "status.json"), false, true);
                if (!M(seed, "(?m)^ready=true donor=[0-9a-f]{64}(?:\\r)?$") && !CM(seed, "\\Aready=true donor=[0-9a-f]{64} reused=true\\n\\z"))
                    throw Refuse("C849 seed receipt invalid");
                object S(string name) => RollingPs.Prop(status, name);
                if (!RollingPs.Eq(S("draining"), true) || !RollingPs.Eq(S("acceptingNewWork"), false) || !RollingPs.Eq(S("dispatchEligible"), true)
                    || !RollingPs.Eq(S("retireWhenIdle"), true) || !RollingPs.Eq(S("redirectTo"), "server2"))
                    throw Refuse("C849 donor reconnect receipt invalid");
                Line("C849_SEED donor=server2-temp ready=true schema=" + contract.Schema + " digestType=" + contract.DigestType + " smoke=" + contract.Smoke + " recovery=retained");
            }

            private void Both()
            {
                var contracts = new[] { ReadContract(0), ReadContract(1) };
                if (contracts[0].Schema != contracts[1].Schema || contracts[0].Kind != contracts[1].Kind || contracts[0].DigestType != contracts[1].DigestType
                    || contracts[0].Digest != contracts[1].Digest)
                    throw Refuse("C849 mixed seed contracts");
                var hashes = new List<string>();
                var kinds = new[] { Receipt(0, "seed-kind.txt").Trim(), Receipt(1, "seed-kind.txt").Trim() };
                bool KindKnown(string kind) => kind == "full" || kind == "cold";
                if (!KindKnown(kinds[0]) || !KindKnown(kinds[1])) throw Refuse("C849 marker kind invalid");
                if (kinds[0] != kinds[1]) throw Refuse("C849 mixed marker kinds");
                for (var i = 0; i < 2; i++)
                {
                    Status(Receipt(i, "status.json"), true);
                    if (kinds[i] != contracts[i].Kind) throw Refuse("C849 marker kind invalid");
                    Smoke(i, contracts[i], _runners[i], true);
                    Mounts(i, i == 0 ? "antiphon-runner" : "antiphon-runner-temp");
                    var full = string.Equals(kinds[i], "full", StringComparison.OrdinalIgnoreCase);
                    if (full && !SmokeLine.IsMatch(Receipt(i, "smoke-summary.txt"))) throw Refuse("C849 smoke receipt invalid");
                    var mounts = Receipt(i, "runner-mounts.txt");
                    foreach (var volume in new[] { "antiphon-runner-cache-nuget-packages", "antiphon-runner-cache-nuget-scratch", "antiphon-runner-cache-npm-content" })
                        if (!CM(mounts, Regex.Escape(volume))) throw Refuse("C849 shared mount missing: " + volume);
                    var privateTmp = i == 0 ? "antiphon-runner_runner-tmp" : "antiphon-runner-temp_runner-tmp";
                    if (!CM(mounts, Regex.Escape(privateTmp + " /tmp true"))) throw Refuse("C849 private tmp mount invalid");
                    if (full)
                    {
                        var hash = Receipt(i, "seed-hash.txt").Trim();
                        if (hash != contracts[i].Digest) throw Refuse("C849 digest projection invalid");
                        hashes.Add(hash);
                    }
                }
                if (string.Equals(kinds[0], "cold", StringComparison.OrdinalIgnoreCase))
                {
                    Line("C849_BOTH kind=cold schema=2 digestType=none runners=2 writableVolumes=3 sharedVolumes=3 privateTmpVolumes=2 tmpMode=1777 failures=0");
                    return;
                }
                if (!CM(hashes[0], "^[0-9a-f]{64}$") || hashes[0] != hashes[1]) throw Refuse("C849 seed payload differs");
                Line("C849_BOTH runners=2 schema=" + contracts[0].Schema + " digestType=" + contracts[0].DigestType + " smokes=2 sharedVolumes=3 privateTmpVolumes=2 tmpMode=1777 failures=0");
            }

            private void Retired()
            {
                var contract = ReadContract(0);
                Smoke(0, contract, "server2", true);
                Mounts(0, "antiphon-runner");
                Status(Receipt(0, "status.json"), true);
                var kind = Receipt(0, "seed-kind.txt").Trim();
                if (kind != contract.Kind) throw Refuse("C849 marker kind invalid");
                if (kind != "full" && kind != "cold") throw Refuse("C849 marker kind invalid");
                var mounts = Receipt(0, "runner-mounts.txt");
                var rollback = Receipt(0, "rollback.txt");
                if (!CM(mounts, "antiphon-runner_runner-tmp /tmp true") || !CM(rollback, "^rollback-image=sha256:[0-9a-f]{64}\\r?\\n?$")) throw Refuse("C849 retired receipt invalid");
                if (kind == "cold")
                {
                    Line("C849_RETIRED kind=cold schema=2 digestType=none externalVolumes=3 tempPrivateVolumes=0 mainTmpRetained=true rollback=retained failures=0");
                    return;
                }
                var smoke = Receipt(0, "smoke-summary.txt");
                var hash = Receipt(0, "seed-hash.txt").Trim();
                if (!SmokeLine.IsMatch(smoke) || !CM(hash, "^[0-9a-f]{64}$") || hash != contract.Digest) throw Refuse("C849 retired receipt invalid");
                Line("C849_RETIRED schema=" + contract.Schema + " digestType=" + contract.DigestType
                    + " externalVolumes=3 tempPrivateVolumes=0 mainTmpRetained=true smokes=1 rollback=retained failures=0");
            }
        }
    }
}
