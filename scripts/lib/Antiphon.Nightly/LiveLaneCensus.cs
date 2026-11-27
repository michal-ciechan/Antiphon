// The live-lane census (CARD-1545 S1a CP-30, CARD-1645 D-1 to D-4) of live-lane-manifest.ps1, compiled in W2-nightly
// S25 with the census cases of scripts/test-live-lane-manifest.ps1 (both files are gone): the commission-scoped check
// that the candidate's discovery (the CP-25 and CP-1 producer exports) has the same membership, categories,
// expansions and nightly and rc dispositions as the pre-S1 fixture at the census seed, apart from the six LiveClaude
// additions and the declared additive identities, under an unchanged policy and unchanged policy readers.
//
// - LiveLaneCensus is one census session: its owned scratch (LiveLaneScratch) and caches. Run is
//   Invoke-LiveLaneCensus, FullCensus is Invoke-LiveLaneFullCensus (the real census, then every refusal it must make);
//   Clear is Clear-LiveLaneOwnedScratch.
// - FullCensusUnchanged is the old FullCensusUnchanged case (the CP-30 command row, "antiphon-scripts live-lane-census"):
//   the export, receipt (with its census check) and scratch witnesses, then the full census over the context that
//   ANTIPHON_LIVE_PORTABLE_CONTEXT names. It is not a check to re-run on later trees (docs/testing-and-build.md).
// - AdditiveIdentityWitness and TargetScopeWitness are the CensusAdditiveIdentity and CensusTargetScope cases
//   (LiveLaneInventoryTests). EmitPolicyHash is the EmitPolicyHash case ("live-lane-emit-policy-hash"); RunGroup is the
//   Group case (the S1a commission, "live-lane-group").
//
// Each method decides what the PowerShell it replaces decided, on the same inputs: -eq, -ne and -contains on text
// ignore case (the invariant culture), Sort-Object -Unique sorts with the current culture ignoring case, git output is
// stdout trimmed with stderr dropped (Get-LiveLaneGit). Deliberate differences: the seed's policy is read as the typed
// ExecutionPolicy (the PowerShell passed ConvertFrom-Json's object to NightlyPolicy.ProfileExclusions, which no longer
// takes one, so a census that reached the seed's exclusions threw); both policy hashes are computed in-process (the
// PowerShell hashed the seed copy in a pwsh child that dot-sourced the copy's own scripts/lib/nightly-core.ps1, which
// the copy never has, so the unmutated census always added policy-hash-changed; the readers are protected paths, so
// a reader edit is still reader-input-changed); a policy mutant is written with System.Text.Json; the engine gets the
// exclusion and identity rows as objects, not as JSON text. No provider call, no live Claude. Plain C#. ASCII-only.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Antiphon.Nightly
{
    /// <summary>A census answer: accepted when it raised no diagnostic.</summary>
    public sealed record LiveLaneCensusResult(bool Accepted, IReadOnlyList<string> Diagnostics)
    {
        /// <summary>Test-LiveLaneCensusHas: refused, with every one of need among the diagnostics.</summary>
        public bool Has(params string[] need)
        {
            if (Accepted) return false;
            foreach (var item in need)
                if (!Contains(Diagnostics, item)) return false;
            return true;
        }

        private static bool Contains(IReadOnlyList<string> have, string item)
        {
            foreach (var value in have)
                if (LiveLaneJson.Same(value, item)) return true;
            return false;
        }
    }

    /// <summary>The full census's output lines and exit code (Invoke-LiveLaneFullCensus).</summary>
    public sealed record LiveLaneCensusRun(IReadOnlyList<string> Lines, int ExitCode);

    /// <summary>
    /// The census's mutants ($Mutant): each field that is set replaces one input (null: the context's). Policy hashes
    /// are always computed in-process now, so the old fastHash key has no field.
    /// </summary>
    public sealed class LiveLaneCensusMutant
    {
        public string ReceiptRoot { get; init; }
        public string Seed { get; init; }
        public string TargetRef { get; init; }
        public string BoundSha { get; init; }
        public string MissingPath { get; init; }
        public string FixtureBlob { get; init; }
        public IReadOnlyList<LiveLaneProvenance> Provenance { get; init; }
        public string ExtraPath { get; init; }
        public string DriftPath { get; init; }
        public string Cp25 { get; init; }
        public string Cp1 { get; init; }
        public string ExportSource { get; init; }
        public string ExportRun { get; init; }
        public string ExportHash { get; init; }
        public bool? Terminal { get; init; }
        public string AfterRoot { get; init; }
        public string PolicyPath { get; init; }

        /// <summary>drop-first, remove-integration, tag-seventh, add-optin, extra-expansion, unknown-node or drop-live.</summary>
        public string Patch { get; init; }

        internal static readonly LiveLaneCensusMutant None = new LiveLaneCensusMutant();

        internal bool EditsExport => ExportSource != null || ExportRun != null || ExportHash != null || Terminal != null;
    }

    public sealed class LiveLaneCensus
    {
        /// <summary>$script:LiveLanePreS1BaseSha: the pre-S1 base the census proves no drift against.</summary>
        public const string PreS1BaseSha = "bf0b874e695a642118e2dd55913228e2f635d009";

        /// <summary>$script:LiveLanePreS1PinRel: the committed pin of the pre-S1 base.</summary>
        public const string PreS1PinRel = "tests/fixtures/live-lanes/pre-s1-base.sha";

        /// <summary>$script:LiveLaneLandingTargetRef: the landing target the pre-S1 base must be on (CARD-1645).</summary>
        public const string LandingTargetRef = "refs/remotes/origin/master";

        /// <summary>The compiled census itself: the source that declares PreS1BaseSha.</summary>
        public const string EngineSource = "scripts/lib/Antiphon.Nightly/LiveLaneCensus.cs";

        /// <summary>
        /// $script:LiveLaneAllowedSeedPaths: the paths a seed commit (pre..seed) may touch; any other is seed-range. The
        /// six PowerShell-era entries stay (the historical seed 49b77d40b touched them). Keep the old path of the Pty
        /// inventory for that range and allow its current invariant path. A re-seed edits PreS1BaseSha in EngineSource.
        /// </summary>
        public static IReadOnlyList<string> AllowedSeedPaths { get; } = new[]
        {
            "tests/live-lanes.json",
            "scripts/lib/live-lane-manifest.ps1",
            "scripts/test-live-lane-manifest.ps1",
            "tests/Antiphon.Tests/Scripts/LiveLaneInventoryTests.cs",
            "tests/Antiphon.Agents.Pty.Tests/LiveClaudeInventoryTests.cs",
            "tests/Antiphon.Agents.Pty.Tests/Invariants/LiveClaudeInventoryTests.cs",
            "tests/fixtures/live-lanes/full-discovery-before.json",
            EngineSource,
        };

        /// <summary>
        /// $script:LiveLaneImmutableSeedPaths: the pin is outside the seed allow-list. A seed commit that touches it is
        /// seed-engine even if a later edit adds the path to the allow-list above.
        /// </summary>
        public static IReadOnlyList<string> ImmutableSeedPaths { get; } = new[]
        {
            PreS1PinRel,
        };

        /// <summary>
        /// $script:LiveLaneProtectedPaths: the policy and its readers. Each must exist at the seed (seed-object-missing),
        /// be the pre-S1 base's blob (baseline-drift) and hash as the candidate's file (reader-input-changed).
        /// W2-nightly S27 deleted scripts/lib/nightly-coverage.ps1 (its readers are the compiled ones listed here), so it
        /// is no longer a protected path: a candidate without it could never hash as the seed's file.
        /// </summary>
        public static IReadOnlyList<string> ProtectedPaths { get; } = new[]
        {
            "tests/test-execution-policy.json",
            "scripts/lib/nightly-common.ps1",
            "scripts/lib/Antiphon.Nightly/NightlyPolicy.cs",
            "scripts/lib/Antiphon.Nightly/ExecutionPolicy.cs",
            "scripts/lib/Antiphon.Nightly/DiscoveryIndex.cs",
            "scripts/lib/Antiphon.Nightly/DiagnosticLog.cs",
            "scripts/lib/Antiphon.Nightly/DiagScan.cs",
        };

        /// <summary>
        /// The census engine's sources: a seed commit that touches one and declares another pre-S1 base is seed-engine.
        /// The two PowerShell files held it until S25 (their declaration is LiveLanePreS1BaseSha = '...'); EngineSource
        /// holds it now (PreS1BaseSha = "...").
        /// </summary>
        private static readonly (string Path, Regex Declares)[] Engines =
        {
            ("scripts/lib/live-lane-manifest.ps1", new Regex(@"LiveLanePreS1BaseSha\s*=\s*'([0-9a-fA-F]{40})'", RegexOptions.CultureInvariant)),
            ("scripts/test-live-lane-manifest.ps1", null),
            (EngineSource, new Regex(@"PreS1BaseSha\s*=\s*""([0-9a-fA-F]{40})""", RegexOptions.CultureInvariant)),
        };

        private static readonly Regex Sha = new Regex("^[0-9a-f]{40}$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private readonly LiveLaneScratch _scratch = new LiveLaneScratch();
        private readonly Dictionary<string, SeedMaterial> _seeds = new Dictionary<string, SeedMaterial>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string[]> _declares = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, LiveLaneExport> _exports = new Dictionary<string, LiveLaneExport>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, ExclusionRow[]> _exclusions = new Dictionary<string, ExclusionRow[]>(StringComparer.OrdinalIgnoreCase);
        private readonly CensusEngine _engine = new CensusEngine();
        private string _identityKey;
        private (CensusEngine.Id[] Additive, CensusEngine.Id[] Contract) _identities;

        /// <summary>The session's owned scratch: every directory and dangling commit the census made.</summary>
        public LiveLaneScratch Scratch => _scratch;

        /// <summary>Clear-LiveLaneOwnedScratch: removes the session's scratch and forgets its seed copies.</summary>
        public void Clear()
        {
            _scratch.Clear();
            _seeds.Clear();
        }

        /// <summary>The platform of the exclusion rows: linux on Linux, else windows ($IsLinux).</summary>
        public static string Platform => OperatingSystem.IsLinux() ? "linux" : "windows";

        // ---- the census -----------------------------------------------------------------------------------------

        /// <summary>
        /// Invoke-LiveLaneCensus: every diagnostic of the context's commission (mutant: the inputs one refusal case
        /// replaces), each once, in the order first raised. A null context is context-required.
        /// </summary>
        public LiveLaneCensusResult Run(LiveLaneContext context, LiveLaneCensusMutant mutant = null)
        {
            var diagnostics = new List<string>();
            if (context == null) return new LiveLaneCensusResult(false, new[] { "context-required" });
            mutant ??= LiveLaneCensusMutant.None;
            var receipt = LiveLaneReceipt.Check(context.SourceSha, mutant.ReceiptRoot ?? context.CheckpointResults);
            if (!receipt.Accepted) diagnostics.Add(receipt.Reason);
            var repo = context.CandidateRoot;
            if (string.IsNullOrWhiteSpace(repo)) repo = RepoRoot(Directory.GetCurrentDirectory());
            var seed = mutant.Seed ?? context.CensusBaseSha ?? "";
            if (string.IsNullOrWhiteSpace(seed))
            {
                diagnostics.Add("seed-object-missing");
                return new LiveLaneCensusResult(false, diagnostics);
            }
            var head = Git(repo, "rev-parse", "HEAD").Text;
            if (LiveLaneJson.Same(seed, head)) diagnostics.Add("self-seed");
            if (Git(repo, "merge-base", "--is-ancestor", seed, head).Exit != 0) diagnostics.Add("seed-not-ancestor");
            var pre = context.PreS1BaseSha ?? "";
            var pin = PinnedPreS1(repo);
            if (string.IsNullOrWhiteSpace(pin) || !LiveLaneJson.Same(pin, PreS1BaseSha)) diagnostics.Add("baseline-drift");
            if (!LiveLaneJson.Same(pre, PreS1BaseSha) || !LiveLaneJson.Same(pre, pin)) diagnostics.Add("baseline-drift");
            if (LiveLaneJson.Same(pre, seed) || LiveLaneJson.Same(pre, head)) diagnostics.Add("self-seed");
            if (Git(repo, "merge-base", "--is-ancestor", pre, seed).Exit != 0) diagnostics.Add("baseline-drift");
            var targetRef = context.LandingTarget;
            if (string.IsNullOrWhiteSpace(targetRef)) targetRef = LandingTargetRef;
            if (mutant.TargetRef != null) targetRef = mutant.TargetRef;
            diagnostics.AddRange(TargetDiagnostics(repo, pre, seed, head, targetRef));
            if (mutant.BoundSha != null)
            {
                var bound = mutant.BoundSha.ToLowerInvariant();
                if (!LiveLaneJson.Same(bound, PreS1BaseSha)) diagnostics.Add("seed-engine");
                if (LiveLaneJson.Same(bound, seed) || LiveLaneJson.Same(bound, head)) diagnostics.Add("self-seed");
            }
            var fixtureRel = context.FixturePath;
            if (string.IsNullOrWhiteSpace(fixtureRel)) fixtureRel = LiveLaneManifestFiles.FixtureRel;
            var required = new List<string>(ProtectedPaths) { fixtureRel };
            if (mutant.MissingPath != null) required.Add(mutant.MissingPath);
            foreach (var rel in required)
                if (Git(repo, "cat-file", "-e", seed + ":" + rel).Exit != 0) diagnostics.Add("seed-object-missing");
            var blob = Git(repo, "rev-parse", seed + ":" + fixtureRel).Text;
            var expectedBlob = mutant.FixtureBlob ?? context.FixtureBlob ?? "";
            if (string.IsNullOrWhiteSpace(blob) || !LiveLaneJson.Same(blob, expectedBlob)) diagnostics.Add("fixture-binding");
            SeedMaterial material = null;
            if (!string.IsNullOrWhiteSpace(blob)) material = Material(repo, seed, fixtureRel, pre);
            if (material == null)
            {
                diagnostics.Add("seed-object-missing");
                return new LiveLaneCensusResult(false, Unique(diagnostics));
            }
            var parent = Git(repo, "rev-parse", seed + "^").Text;
            var recorded = material.Recorded;
            if (!LiveLaneJson.Same(recorded, parent) || LiveLaneJson.Same(recorded, seed)) diagnostics.Add("producer-provenance");
            if (LiveLaneJson.Same(recorded, head) || LiveLaneJson.Same(recorded, seed)) diagnostics.Add("self-seed");
            var provenance = mutant.Provenance ?? context.ProducerProvenance;
            foreach (var (project, wantHash) in material.Projects)
            {
                var got = LiveLaneContext.FindProvenance(provenance, project);
                if (!LiveLaneJson.Same(got?.AssemblyHash ?? "", wantHash) || !LiveLaneJson.Same(got?.SourceSha ?? "", recorded)) diagnostics.Add("producer-provenance");
            }
            var touched = SortedUnique(Lines(Git(repo, "log", "--name-only", "--pretty=format:", pre + ".." + seed).Text));
            if (mutant.ExtraPath != null) touched.Add(mutant.ExtraPath);
            foreach (var name in touched)
            {
                if (Listed(ImmutableSeedPaths, name)) diagnostics.Add("seed-engine");
                if (!Listed(AllowedSeedPaths, name)) diagnostics.Add("seed-range");
            }
            diagnostics.AddRange(SeedDeclaresBase(repo, pre, seed, head));
            foreach (var rel in ProtectedPaths)
            {
                var drift = mutant.DriftPath != null && LiveLaneJson.Same(mutant.DriftPath, rel);
                var equal = !material.ProtectedEqual.TryGetValue(rel, out var same) || same;
                if (drift || !equal) diagnostics.Add("baseline-drift");
            }
            var cp25Path = mutant.Cp25 ?? context.Cp25Export ?? "";
            var cp1Path = mutant.Cp1 ?? context.Cp1Export ?? "";
            var cp25 = ReadExport(cp25Path);
            var cp1 = ReadExport(cp1Path);
            if (cp25 == null || cp1 == null) diagnostics.Add("producer-incomplete");
            else
            {
                foreach (var loaded in new[] { cp25, cp1 })
                {
                    var export = loaded;
                    var expectedHash = export.AssemblyHash;
                    if (mutant.EditsExport) export = Shell(export, mutant);
                    var check = LiveLaneExports.Check(export, context.SourceSha, context.RunId, expectedHash, export.Project, false, export.AssemblyPath);
                    if (!check.Accepted && !diagnostics.Contains(check.Reason)) diagnostics.Add(check.Reason);
                }
            }
            var afterRoot = mutant.AfterRoot ?? repo;
            try
            {
                var beforeText = File.ReadAllText(Path.Combine(material.BeforeDir, "tests", "test-execution-policy.json"));
                var afterPolicyFile = Path.Combine(afterRoot, "tests", "test-execution-policy.json");
                if (!string.IsNullOrWhiteSpace(mutant.PolicyPath)) afterPolicyFile = mutant.PolicyPath;
                if (!LiveLaneJson.Same(NightlyPolicy.HashText(beforeText), NightlyPolicy.HashText(File.ReadAllText(afterPolicyFile)))) diagnostics.Add("policy-hash-changed");
            }
            catch (Exception)
            {
                diagnostics.Add("policy-hash-changed");
            }
            foreach (var rel in ProtectedPaths)
            {
                var afterFile = Path.Combine(afterRoot, rel);
                material.FileHash.TryGetValue(rel, out var h1);
                if (string.IsNullOrWhiteSpace(h1) || !File.Exists(afterFile))
                {
                    diagnostics.Add("reader-input-changed");
                    continue;
                }
                if (!LiveLaneJson.Same(h1, NightlyClassNames.FileSha256(afterFile))) diagnostics.Add("reader-input-changed");
            }
            var afterPolicyPath = mutant.PolicyPath ?? Path.Combine(afterRoot, "tests", "test-execution-policy.json");
            var beforeEx = material.BeforeEx;
            var afterEx = CachedExclusions(afterPolicyPath, Platform);
            var (additive, contract) = Identities(repo);
            diagnostics.AddRange(_engine.Compare(material.FixtureCopy, cp25 != null ? cp25Path : "", cp1 != null ? cp1Path : "", beforeEx, afterEx, additive, contract,
                mutant.Patch ?? ""));
            var reasonChanged = false;
            var ownerChanged = false;
            var beforeBy = new Dictionary<string, ExclusionRow>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in beforeEx) beforeBy[row.Class ?? ""] = row;
            foreach (var row in afterEx)
            {
                if (!beforeBy.TryGetValue(row.Class ?? "", out var was)) continue;
                if (!LiveLaneJson.Same(was.Reason, row.Reason)) reasonChanged = true;
                if (!LiveLaneJson.Same(was.Owner, row.Owner)) ownerChanged = true;
            }
            if (reasonChanged) diagnostics.Add("disposition-reason-changed");
            if (ownerChanged) diagnostics.Add("disposition-owner-changed");
            var unique = Unique(diagnostics);
            return new LiveLaneCensusResult(unique.Count == 0, unique);
        }

        /// <summary>
        /// Invoke-LiveLaneFullCensus: the scope line, then the real census of the context; when it is accepted, every
        /// refusal case (a missing context, a non-ancestor or self seed, a missing object, a foreign fixture blob, bad
        /// provenance, pre-S1 drift, an unlisted seed edit, a foreign or pending producer export, red, running, missing
        /// and foreign receipts, a removed uid, category edits, a seventh or dropped LiveClaude tag, a seed-engine edit,
        /// an extra expansion, an undeclared uid, a nightly disposition change, a reader edit, the C680 exclusion and
        /// exclusion reason and owner edits) must refuse with its reason. Exit 2 with the diagnostics when the context
        /// is missing or refused, 1 with an "expected-refusal-missing &lt;case&gt;" line per case that did not refuse,
        /// else 0 and "accepted". repoRoot holds the scratch; the census reads the context's candidate root. The
        /// session's scratch is cleared afterwards.
        /// </summary>
        public LiveLaneCensusRun FullCensus(string repoRoot, LiveLaneContext context)
        {
            var lines = new List<string>();
            try
            {
                if (context == null)
                {
                    lines.Add("context-required");
                    return new LiveLaneCensusRun(lines, 2);
                }
                var scopeTarget = string.IsNullOrWhiteSpace(context.LandingTarget) ? LandingTargetRef : context.LandingTarget;
                var scopeRepo = string.IsNullOrWhiteSpace(context.CandidateRoot) ? repoRoot : context.CandidateRoot;
                var scopeSha = Git(scopeRepo, "rev-parse", "--verify", "--quiet", scopeTarget + "^{commit}").Text;
                lines.Add(ScopeLine(context, scopeSha.Trim()));
                var real = Run(context);
                if (!real.Accepted)
                {
                    lines.AddRange(real.Diagnostics);
                    return new LiveLaneCensusRun(lines, 2);
                }
                var missing = new List<string>();
                void Expect(string name, LiveLaneCensusResult result, params string[] need)
                {
                    if (!result.Has(need)) missing.Add(name);
                }
                LiveLaneCensusResult With(LiveLaneCensusMutant m) => Run(context, m);

                Expect("context-absent", Run(null), "context-required");
                var dangling = _scratch.DanglingSeed(repoRoot);
                Expect("seed-nonancestor", With(new LiveLaneCensusMutant { Seed = dangling }), "seed-not-ancestor");
                Expect("object-missing", With(new LiveLaneCensusMutant { MissingPath = "tests/fixtures/live-lanes/does-not-exist" }), "seed-object-missing");
                Expect("fixture-blob", With(new LiveLaneCensusMutant { FixtureBlob = new string('a', 40) }), "fixture-binding");
                var badProvenance = new[]
                {
                    new LiveLaneProvenance("Antiphon.Tests", new string('0', 40), new string('0', 64)),
                    new LiveLaneProvenance("Antiphon.Agents.Pty.Tests", new string('0', 40), new string('0', 64)),
                };
                Expect("producer-provenance", With(new LiveLaneCensusMutant { Provenance = badProvenance }), "producer-provenance");
                Expect("prebase-drift", With(new LiveLaneCensusMutant { DriftPath = "tests/test-execution-policy.json" }), "baseline-drift");
                Expect("seed-unlisted-edit", With(new LiveLaneCensusMutant { ExtraPath = "docs/not-a-live-lane.md" }), "seed-range");
                Expect("candidate-source", With(new LiveLaneCensusMutant { ExportSource = new string('b', 40) }), "source-mismatch");
                Expect("candidate-build", With(new LiveLaneCensusMutant { ExportHash = new string('c', 64) }), "build-mismatch");
                Expect("candidate-run", With(new LiveLaneCensusMutant { ExportRun = "other-run" }), "run-mismatch");
                Expect("pending-producer", With(new LiveLaneCensusMutant { Terminal = false }), "producer-incomplete");

                var redRoot = _scratch.NewOwned(repoRoot, "receiptred" + Guid.NewGuid().ToString("N"));
                LiveLaneReceipt.WriteFixture(redRoot, context.SourceSha, "red");
                Expect("producer-row-red", With(new LiveLaneCensusMutant { ReceiptRoot = redRoot }), "producer-row-not-green");
                var timedRoot = _scratch.NewOwned(repoRoot, "receipttime" + Guid.NewGuid().ToString("N"));
                LiveLaneReceipt.WriteFixture(timedRoot, context.SourceSha, "running");
                Expect("producer-row-timeout", With(new LiveLaneCensusMutant { ReceiptRoot = timedRoot }), "producer-row-not-green");
                var absentRoot = _scratch.NewOwned(repoRoot, "receiptmiss" + Guid.NewGuid().ToString("N"));
                Expect("producer-receipt-missing", With(new LiveLaneCensusMutant { ReceiptRoot = absentRoot }), "producer-receipt-missing");
                var foreignRoot = _scratch.NewOwned(repoRoot, "receiptforeign" + Guid.NewGuid().ToString("N"));
                LiveLaneReceipt.WriteFixture(foreignRoot, new string('b', 40), "green", "unknown", new string('b', 40));
                Expect("producer-receipt-foreign", With(new LiveLaneCensusMutant { ReceiptRoot = foreignRoot }), "producer-receipt-foreign");

                Expect("remove-old-uid", With(new LiveLaneCensusMutant { Patch = "drop-first" }), "old-membership-changed");
                Expect("remove-Integration", With(new LiveLaneCensusMutant { Patch = "remove-integration" }), "category-delta");
                Expect("tag-seventh", With(new LiveLaneCensusMutant { Patch = "tag-seventh" }), "category-delta", "liveclaude-count");
                Expect("drop-liveclaude", With(new LiveLaneCensusMutant { Patch = "drop-live" }), "liveclaude-count");
                var headNow = Git(repoRoot, "rev-parse", "HEAD").Text.Trim();
                Expect("self-seed", With(new LiveLaneCensusMutant { Seed = headNow }), "self-seed");
                Expect("seed-engine", With(new LiveLaneCensusMutant { ExtraPath = PreS1PinRel }), "seed-engine");
                Expect("seed-declares-base", With(new LiveLaneCensusMutant { BoundSha = headNow }), "seed-engine", "self-seed");
                Expect("extra-expansion", With(new LiveLaneCensusMutant { Patch = "extra-expansion" }), "extra-expansion");
                Expect("unknown-new-uid", With(new LiveLaneCensusMutant { Patch = "unknown-node" }), "undeclared-new-uid");
                Expect("default-disposition", With(new LiveLaneCensusMutant { Patch = "add-optin" }), "nightly-disposition-changed");

                var readerRoot = _scratch.NewOwned(repoRoot, "reader" + Guid.NewGuid().ToString("N"));
                CopyProtected(repoRoot, readerRoot);
                File.AppendAllText(Path.Combine(readerRoot, "scripts", "lib", "Antiphon.Nightly", "NightlyPolicy.cs"), "\n// c1545 scratch\n" + Environment.NewLine, Encoding.ASCII);
                Expect("reader-edit", With(new LiveLaneCensusMutant { AfterRoot = readerRoot }), "reader-input-changed");

                var c680 = PolicyMutant(repoRoot, policy =>
                {
                    var rc = (JsonObject)policy["profiles"]["rc"];
                    var list = new JsonArray();
                    var existing = rc["exclusions"];
                    if (existing is JsonArray rows)
                        foreach (var row in rows) list.Add(row?.DeepClone());
                    else if (existing != null) list.Add(existing.DeepClone());
                    list.Add(new JsonObject
                    {
                        ["suite"] = "antiphon",
                        ["class"] = "ClaudeDialogWatchdogCanaryTests",
                        ["reason"] = "card-1545 scratch exclusion",
                        ["owner"] = "CARD-1545",
                    });
                    rc["exclusions"] = list;
                });
                Expect("c680-exclusion", With(new LiveLaneCensusMutant { AfterRoot = c680.Root, PolicyPath = c680.PolicyPath }), "policy-hash-changed", "C680-required-to-excluded");
                var reason = PolicyMutant(repoRoot, policy =>
                {
                    var first = policy["profiles"]["rc"]["exclusions"][0];
                    first["reason"] = PolicyText(first["reason"]) + " scratch";
                });
                Expect("reason-edit", With(new LiveLaneCensusMutant { AfterRoot = reason.Root, PolicyPath = reason.PolicyPath }), "disposition-reason-changed");
                var owner = PolicyMutant(repoRoot, policy => policy["profiles"]["rc"]["exclusions"][0]["owner"] = "CARD-1545-SCRATCH");
                Expect("owner-edit", With(new LiveLaneCensusMutant { AfterRoot = owner.Root, PolicyPath = owner.PolicyPath }), "disposition-owner-changed");

                if (missing.Count > 0)
                {
                    foreach (var name in missing) lines.Add("expected-refusal-missing " + name);
                    return new LiveLaneCensusRun(lines, 1);
                }
                lines.Add("accepted");
                return new LiveLaneCensusRun(lines, 0);
            }
            finally
            {
                Clear();
            }
        }

        /// <summary>
        /// The FullCensusUnchanged case (the CP-30 command row, "antiphon-scripts live-lane-census"): the export, receipt
        /// (its census check included: census-skipped-receipt) and scratch witnesses must pass (else their problems and
        /// exit 1), then FullCensus over readContext() (the context ANTIPHON_LIVE_PORTABLE_CONTEXT names), its lines
        /// written; a new antiphon-live-lane-* temp entry is tmp-residue and exit 1. Returns the exit code.
        /// </summary>
        public static int FullCensusUnchanged(string repoRoot, Func<LiveLaneContext> readContext, TextWriter output)
        {
            var witness = new List<string>(LiveLaneExports.ExportPendingWitness(repoRoot));
            if (witness.Count == 0) witness.AddRange(LiveLaneReceipt.ProducerReceiptWitness(repoRoot, RefusesRedReceipt));
            if (witness.Count == 0) witness.AddRange(LiveLaneScratch.ScratchResidueWitness(repoRoot, LiveLaneManifestFiles.FixtureRel, ProtectedPaths));
            if (witness.Count > 0)
            {
                foreach (var item in witness) output.WriteLine(item);
                return 1;
            }
            var beforeResidue = LiveLaneScratch.TempResidue();
            var run = new LiveLaneCensus().FullCensus(repoRoot, readContext());
            foreach (var line in run.Lines) output.WriteLine(line);
            foreach (var name in LiveLaneScratch.TempResidue())
            {
                var seen = false;
                foreach (var old in beforeResidue)
                    if (LiveLaneJson.Same(old, name)) seen = true;
                if (!seen)
                {
                    output.WriteLine("tmp-residue");
                    return 1;
                }
            }
            return run.ExitCode;
        }

        /// <summary>
        /// The receipt witness's census check (census-skipped-receipt): the census of a context without a seed over the
        /// receipt root red refuses with producer-row-not-green.
        /// </summary>
        public static bool RefusesRedReceipt(string repoRoot, string sourceSha, string red)
        {
            var census = new LiveLaneCensus();
            try
            {
                var wired = census.Run(new LiveLaneContext { CandidateRoot = repoRoot, SourceSha = sourceSha, CheckpointResults = red, CensusBaseSha = "" });
                return wired.Has("producer-row-not-green");
            }
            finally
            {
                census.Clear();
            }
        }

        // ---- the inputs ----------------------------------------------------------------------------------------

        /// <summary>Get-LiveLanePinnedPreS1: the pinned pre-S1 base, lower case; blank when the pin is missing or not a sha.</summary>
        public static string PinnedPreS1(string repoRoot)
        {
            var path = Path.Combine(repoRoot, PreS1PinRel);
            if (!File.Exists(path)) return "";
            var text = File.ReadAllText(path).Trim().ToLowerInvariant();
            return Sha.IsMatch(text) ? text : "";
        }

        /// <summary>
        /// The pre-S1 base an engine source declares (Get-LiveLaneDeclaredPreS1 for the PowerShell engine, the
        /// PreS1BaseSha constant for EngineSource), lower case; blank when it declares none.
        /// </summary>
        public static string DeclaredPreS1(string enginePath, string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "";
            foreach (var (path, declares) in Engines)
            {
                if (declares == null || !string.Equals(path, enginePath, StringComparison.Ordinal)) continue;
                var match = declares.Match(text);
                return match.Success ? match.Groups[1].Value.ToLowerInvariant() : "";
            }
            return "";
        }

        /// <summary>
        /// Test-LiveLaneSeedDeclaresBase: for each commit of pre..seed, seed-engine when it touches the pin or touches an
        /// engine source that declares another pre-S1 base, self-seed when the base an engine source declares there is
        /// the commit, the seed or the head. Each once; cached per pre, seed and head.
        /// </summary>
        public IReadOnlyList<string> SeedDeclaresBase(string repoRoot, string pre, string seed, string head)
        {
            var key = pre + "|" + seed + "|" + head + "|" + PreS1BaseSha;
            if (_declares.TryGetValue(key, out var cached)) return cached;
            var found = new List<string>();
            var listed = Git(repoRoot, "rev-list", pre + ".." + seed);
            if (listed.Exit == 0)
            {
                foreach (var line in listed.Text.Split('\n'))
                {
                    var commit = line.Trim().ToLowerInvariant();
                    if (!Sha.IsMatch(commit)) continue;
                    var names = Lines(Git(repoRoot, "diff-tree", "--no-commit-id", "--name-only", "-r", commit).Text);
                    var engineTouched = false;
                    foreach (var name in names)
                    {
                        if (Listed(ImmutableSeedPaths, name)) found.Add("seed-engine");
                        foreach (var (path, _) in Engines)
                            if (LiveLaneJson.Same(name, path)) engineTouched = true;
                    }
                    foreach (var (path, declares) in Engines)
                    {
                        if (declares == null) continue;
                        var shown = Git(repoRoot, "show", commit + ":" + path);
                        var bound = shown.Exit == 0 ? DeclaredPreS1(path, shown.Text) : "";
                        if (engineTouched && !string.IsNullOrWhiteSpace(bound) && !LiveLaneJson.Same(bound, PreS1BaseSha)) found.Add("seed-engine");
                        if (LiveLaneJson.Same(bound, commit) || LiveLaneJson.Same(bound, seed) || LiveLaneJson.Same(bound, head)) found.Add("self-seed");
                    }
                }
            }
            var unique = Unique(found).ToArray();
            _declares[key] = unique;
            return unique;
        }

        /// <summary>
        /// Get-LiveLaneTargetDiagnostics (CARD-1645 D-1/D-4): target-unresolved when the landing target does not resolve,
        /// pre-not-on-target when the pre-S1 base is not on it, later-tree when an unlanded head's merge-base with the
        /// target is past the seed, or the head carries a target mainline commit past the seed that is not on its own
        /// first-parent line. A head already on the target is not diagnosed.
        /// </summary>
        public static IReadOnlyList<string> TargetDiagnostics(string repoRoot, string pre, string seed, string head, string targetRef)
        {
            var found = new List<string>();
            var resolved = Git(repoRoot, "rev-parse", "--verify", "--quiet", targetRef + "^{commit}");
            var target = resolved.Text.Trim().ToLowerInvariant();
            if (resolved.Exit != 0 || !Sha.IsMatch(target))
            {
                found.Add("target-unresolved");
                return found;
            }
            if (Git(repoRoot, "merge-base", "--is-ancestor", pre, target).Exit != 0) found.Add("pre-not-on-target");
            if (Git(repoRoot, "merge-base", "--is-ancestor", head, target).Exit != 0)
            {
                var b = Git(repoRoot, "merge-base", head, target);
                var baseSha = b.Text.Trim();
                var covered = b.Exit == 0 && Sha.IsMatch(baseSha) && Git(repoRoot, "merge-base", "--is-ancestor", baseSha, seed).Exit == 0;
                if (!covered) found.Add("later-tree");
            }
            var carried = Git(repoRoot, "rev-list", head, "^" + seed);
            var mainline = Git(repoRoot, "rev-list", "--first-parent", target, "^" + seed);
            var own = Git(repoRoot, "rev-list", "--first-parent", head, "^" + seed);
            if (carried.Exit != 0 || mainline.Exit != 0 || own.Exit != 0) found.Add("later-tree");
            else
            {
                var onMainline = new HashSet<string>(Lines(mainline.Text), StringComparer.Ordinal);
                var ownLine = new HashSet<string>(Lines(own.Text), StringComparer.Ordinal);
                foreach (var commit in Lines(carried.Text))
                {
                    if (onMainline.Contains(commit) && !ownLine.Contains(commit))
                    {
                        found.Add("later-tree");
                        break;
                    }
                }
            }
            return Unique(found);
        }

        /// <summary>
        /// Get-LiveLaneCensusScopeLine: the binding a reviewer compares with an independent CP-27/CP-28 recapture (the
        /// fixture is a committed blob the seed author wrote; nothing here re-derives it).
        /// </summary>
        public static string ScopeLine(LiveLaneContext context, string targetSha)
        {
            var target = string.IsNullOrWhiteSpace(context.LandingTarget) ? LandingTargetRef : context.LandingTarget;
            return "census scope=commission not-run-on-later-trees seed=" + context.CensusBaseSha
                + " pre=" + context.PreS1BaseSha
                + " head=" + context.SourceSha
                + " target=" + target + "@" + targetSha
                + " fixtureBlob=" + context.FixtureBlob
                + " fixture=unauthenticated (compare the blob with an independent CP-27/CP-28 recapture)";
        }

        /// <summary>
        /// Get-LiveLaneExclusionsFromObject: the rc profile's exclusion rows for platform, for each suite of defaultSuites
        /// and the rc requiredSuites (sorted, each once, ignoring case; blanks skipped), in that order.
        /// </summary>
        public static ExclusionRow[] Exclusions(ExecutionPolicy policy, string platform)
        {
            var ids = new List<string>();
            if (policy.DefaultSuites != null) ids.AddRange(policy.DefaultSuites);
            if (policy.Profiles != null && policy.Profiles.TryGetValue("rc", out var rc) && rc != null) ids.AddRange(rc.RequiredSuites ?? new string[0]);
            var rows = new List<ExclusionRow>();
            foreach (var id in SortedUnique(ids))
            {
                if (string.IsNullOrWhiteSpace(id)) continue;
                rows.AddRange(NightlyPolicy.ProfileExclusions(policy, "rc", id, platform));
            }
            return rows.ToArray();
        }

        /// <summary>The EmitPolicyHash case: the hash of policyPath, or of the policy root's tests/test-execution-policy.json.</summary>
        public static string EmitPolicyHash(string policyRoot, string policyPath = "")
        {
            if (string.IsNullOrWhiteSpace(policyRoot)) throw new ArgumentException("policy-root-required");
            var path = string.IsNullOrWhiteSpace(policyPath) ? Path.Combine(policyRoot, "tests", "test-execution-policy.json") : policyPath;
            return NightlyPolicy.HashText(File.ReadAllText(path));
        }

        // Get-LiveLaneCensusSeedMaterial: the fixture and protected inputs at seed in owned scratch, the fixture's producing
        // source and projects, each protected input's equality with pre and hash, the seed policy's exclusions. Null when a
        // copy failed. Cached per fixture blob, pre and protected blobs until Clear.
        private SeedMaterial Material(string repoRoot, string seed, string fixtureRel, string pre)
        {
            var blob = Git(repoRoot, "rev-parse", seed + ":" + fixtureRel).Text;
            if (string.IsNullOrWhiteSpace(blob)) return null;
            var protectedKey = new StringBuilder();
            foreach (var rel in ProtectedPaths) protectedKey.Append('|').Append(Git(repoRoot, "rev-parse", seed + ":" + rel).Text);
            var key = blob + "|" + fixtureRel + "|" + pre + protectedKey;
            if (_seeds.TryGetValue(key, out var cached)) return cached;
            var before = _scratch.SeedCopy(repoRoot, seed, fixtureRel, ProtectedPaths);
            if (before == null) return null;
            var material = new SeedMaterial { BeforeDir = before, FixtureCopy = Path.Combine(before, fixtureRel) };
            using (var fixture = LiveLaneJson.ReadFile(material.FixtureCopy))
            {
                var producing = LiveLaneJson.Prop(fixture.RootElement, "producing");
                material.Recorded = LiveLaneJson.Field(producing, "sourceSha");
                foreach (var project in LiveLaneJson.Rows(producing, "projects"))
                    material.Projects.Add((LiveLaneJson.Field(project, "project"), LiveLaneJson.Field(project, "assemblyHash")));
            }
            foreach (var rel in ProtectedPaths)
                material.ProtectedEqual[rel] = LiveLaneJson.Same(Git(repoRoot, "rev-parse", seed + ":" + rel).Text, Git(repoRoot, "rev-parse", pre + ":" + rel).Text);
            foreach (var rel in ProtectedPaths) material.FileHash[rel] = NightlyClassNames.FileSha256(Path.Combine(before, rel));
            var policyPath = Path.Combine(before, "tests", "test-execution-policy.json");
            material.BeforeEx = Exclusions(ExecutionPolicy.Parse(File.ReadAllText(policyPath), policyPath), Platform);
            _seeds[key] = material;
            return material;
        }

        private sealed class SeedMaterial
        {
            public string BeforeDir;
            public string FixtureCopy;
            public string Recorded = "";
            public readonly List<(string Project, string AssemblyHash)> Projects = new List<(string, string)>();
            public readonly Dictionary<string, bool> ProtectedEqual = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            public readonly Dictionary<string, string> FileHash = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            public ExclusionRow[] BeforeEx = new ExclusionRow[0];
        }

        // Read-LiveLaneExportFile: the export at path (cached per file, length and write time); null when blank or missing.
        private LiveLaneExport ReadExport(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
            var info = new FileInfo(path);
            var key = info.FullName + "|" + info.Length + "|" + info.LastWriteTimeUtc.Ticks;
            if (_exports.TryGetValue(key, out var cached)) return cached;
            var export = LiveLaneExports.Read(path);
            _exports[key] = export;
            return export;
        }

        // Copy-LiveLaneExportShell with the mutant's source, run, hash and terminal flag.
        private static LiveLaneExport Shell(LiveLaneExport export, LiveLaneCensusMutant mutant) => new LiveLaneExport
        {
            Format = export.Format,
            Project = export.Project,
            Cp = export.Cp,
            SourceSha = mutant.ExportSource ?? export.SourceSha,
            RunId = mutant.ExportRun ?? export.RunId,
            AssemblyHash = mutant.ExportHash ?? export.AssemblyHash,
            AssemblyPath = export.AssemblyPath,
            Terminal = mutant.Terminal ?? export.Terminal ?? false,
            Nodes = export.Nodes,
        };

        // Get-LiveLaneCachedExclusions: the exclusions of the policy at path (cached per file, length, write time and
        // platform); policy-missing when there is none.
        private ExclusionRow[] CachedExclusions(string policyPath, string platform)
        {
            if (string.IsNullOrWhiteSpace(policyPath) || !File.Exists(policyPath)) throw new InvalidOperationException("policy-missing");
            var info = new FileInfo(policyPath);
            var key = info.FullName + "|" + info.Length + "|" + info.LastWriteTimeUtc.Ticks + "|" + platform;
            if (_exclusions.TryGetValue(key, out var cached)) return cached;
            var rows = Exclusions(ExecutionPolicy.Parse(File.ReadAllText(policyPath), policyPath), platform);
            _exclusions[key] = rows;
            return rows;
        }

        // Get-LiveLaneCensusIdentityPayload: tests/live-lanes.json's additiveContract rows and the six core identities.
        private (CensusEngine.Id[] Additive, CensusEngine.Id[] Contract) Identities(string repoRoot)
        {
            var path = Path.Combine(repoRoot, "tests", "live-lanes.json");
            if (!File.Exists(path)) throw new InvalidOperationException("manifest-missing");
            var info = new FileInfo(path);
            var key = info.FullName + "|" + info.Length + "|" + info.LastWriteTimeUtc.Ticks;
            if (_identityKey == key) return _identities;
            var additive = new List<CensusEngine.Id>();
            using (var manifest = LiveLaneJson.ReadFile(path))
                foreach (var row in LiveLaneJson.Rows(manifest.RootElement, "additiveContract")) additive.Add(CensusEngine.Id.FromJson(row));
            _identities = (additive.ToArray(), CensusEngine.Id.Contract());
            _identityKey = key;
            return _identities;
        }

        // New-LiveLanePolicyMutant: the protected inputs copied into owned scratch, the policy edited and re-hashed.
        private (string Root, string PolicyPath) PolicyMutant(string repoRoot, Action<JsonNode> edit)
        {
            var scratch = _scratch.NewOwned(repoRoot, "policy" + Guid.NewGuid().ToString("N"));
            CopyProtected(repoRoot, scratch);
            var policyPath = Path.Combine(scratch, "tests", "test-execution-policy.json");
            var policy = JsonNode.Parse(File.ReadAllText(policyPath), new JsonNodeOptions { PropertyNameCaseInsensitive = true });
            edit(policy);
            policy["policyHash"] = NightlyPolicy.HashText(policy.ToJsonString());
            var options = new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
            File.WriteAllText(policyPath, policy.ToJsonString(options) + Environment.NewLine, new UTF8Encoding(false));
            return (scratch, policyPath);
        }

        // [string] of a policy value.
        private static string PolicyText(JsonNode value) => value == null ? "" : value.GetValueKind() == JsonValueKind.String ? value.GetValue<string>() : value.ToJsonString();

        private static void CopyProtected(string repoRoot, string root)
        {
            foreach (var rel in ProtectedPaths)
            {
                var dest = Path.Combine(root, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(dest));
                File.Copy(Path.Combine(repoRoot, rel), dest, true);
            }
        }

        // ---- the witnesses -------------------------------------------------------------------------------------

        /// <summary>
        /// The CensusAdditiveIdentity case (CARD-1645 item 2): a declared additive identity present in the after set is
        /// neither missing nor undeclared; one removed is declared-additive-missing; a class and method twin under another
        /// namespace or project is undeclared-new-uid and leaves the declared one missing. None: accepted.
        /// </summary>
        public static IReadOnlyList<string> AdditiveIdentityWitness(string repoRoot)
        {
            var problems = new List<string>();
            var scratch = new LiveLaneScratch();
            var dir = scratch.NewOwned(repoRoot, "additive" + Guid.NewGuid().ToString("N"));
            try
            {
                var old = CensusNode("Antiphon.Tests", "Antiphon.Tests.Scripts", "C1645OldTests", "Kept");
                var declared = new[] { new CensusEngine.Id("Antiphon.Tests", "Antiphon.Tests.Scripts", "C1645NewTests", "Added", "") };
                var shapes = new (string Name, LiveLaneJsonObject[] Nodes)[]
                {
                    ("present", new[] { old, CensusNode("Antiphon.Tests", "Antiphon.Tests.Scripts", "C1645NewTests", "Added") }),
                    ("removed", new[] { old }),
                    ("otherNamespace", new[] { old, CensusNode("Antiphon.Tests", "Antiphon.Tests.Elsewhere", "C1645NewTests", "Added") }),
                    ("otherProject", new[] { old, CensusNode("Antiphon.Agents.Pty.Tests", "Antiphon.Tests.Scripts", "C1645NewTests", "Added") }),
                };
                var beforePath = Path.Combine(dir, "before.json");
                WriteCensusNodes(beforePath, new[] { old });
                var engine = new CensusEngine();
                var contract = CensusEngine.Id.Contract();
                var seen = new Dictionary<string, List<string>>(StringComparer.Ordinal);
                foreach (var (name, nodes) in shapes)
                {
                    var path = Path.Combine(dir, name + ".json");
                    WriteCensusNodes(path, nodes);
                    seen[name] = engine.Compare(beforePath, path, "", new ExclusionRow[0], new ExclusionRow[0], declared, contract, "");
                }
                var present = seen["present"];
                if (present.Contains("declared-additive-missing") || present.Contains("undeclared-new-uid"))
                    problems.Add("C1645 D-2/present declared addition diagnosed: " + string.Join(",", present));
                if (!seen["removed"].Contains("declared-additive-missing"))
                    problems.Add("C1645 D-2/removed declared addition not diagnosed: " + string.Join(",", seen["removed"]));
                foreach (var name in new[] { "otherNamespace", "otherProject" })
                {
                    if (!seen[name].Contains("undeclared-new-uid")) problems.Add("C1645 D-2/" + name + " class+method match counted as declared: " + string.Join(",", seen[name]));
                    if (!seen[name].Contains("declared-additive-missing")) problems.Add("C1645 D-2/" + name + " declared identity counted present: " + string.Join(",", seen[name]));
                }
            }
            finally
            {
                scratch.Clear();
            }
            return problems;
        }

        /// <summary>
        /// The CensusTargetScope case (CARD-1645 items 1 and 4) in a scratch repository: an honest subject, a pre-S1 base
        /// off the target, a subject merged with later master, the subject landed by a no-ff merge and by a fast-forward,
        /// a candidate cut from later master, the later tree landed, and a missing target each give the target
        /// diagnostics they must; the scope line names its binding. None: accepted.
        /// </summary>
        public static IReadOnlyList<string> TargetScopeWitness(string repoRoot)
        {
            var problems = new List<string>();
            var scratch = new LiveLaneScratch();
            var dir = scratch.NewOwned(repoRoot, "target" + Guid.NewGuid().ToString("N"));
            try
            {
                ScratchGit(dir, "init", "-q", "--template=", "-b", "master");
                ScratchCommit(dir, "a.txt", "base");
                var pre = ScratchCommit(dir, "a.txt", "pre");
                ScratchGit(dir, "checkout", "-q", "-b", "subject");
                var seed = ScratchCommit(dir, "b.txt", "seed");
                var head = ScratchCommit(dir, "c.txt", "subject");
                ScratchGit(dir, "checkout", "-q", "-b", "offtarget", pre);
                var offPre = ScratchCommit(dir, "d.txt", "caller-chosen pre");
                var offSeed = ScratchCommit(dir, "e.txt", "caller-chosen seed");
                var offHead = ScratchCommit(dir, "f.txt", "caller-chosen head");
                ScratchGit(dir, "checkout", "-q", "master");
                var later = ScratchCommit(dir, "g.txt", "later master");
                ScratchGit(dir, "update-ref", "refs/remotes/origin/master", later);
                ScratchGit(dir, "checkout", "-q", "subject");
                ScratchGit(dir, "merge", "-q", "--no-edit", "--no-ff", "master");
                var merged = ScratchGit(dir, "rev-parse", "HEAD");
                // The reviewed subject after it lands is not a later tree: a no-ff merge keeps its commits off the target
                // first-parent line, a fast-forward keeps them on its own.
                ScratchGit(dir, "checkout", "-q", "-b", "landedmerge", later);
                ScratchGit(dir, "merge", "-q", "--no-edit", "--no-ff", head);
                ScratchGit(dir, "update-ref", "refs/remotes/origin/landedmerge", "HEAD");
                ScratchGit(dir, "update-ref", "refs/remotes/origin/landedff", head);
                // The D-4 tree (subject merged with later master) stays a later tree after a no-ff land.
                ScratchGit(dir, "checkout", "-q", "-b", "landedlater", later);
                ScratchGit(dir, "merge", "-q", "--no-edit", "--no-ff", merged);
                ScratchGit(dir, "update-ref", "refs/remotes/origin/landedlater", "HEAD");
                // A candidate cut from the target after the seed carries the seed only through a merge.
                ScratchGit(dir, "checkout", "-q", "-b", "forkedlater", later);
                ScratchGit(dir, "merge", "-q", "--no-edit", "--no-ff", seed);
                var forked = ScratchCommit(dir, "h.txt", "forked candidate");
                const string target = LandingTargetRef;
                var none = new string[0];
                var cases = new (string Name, string Pre, string Seed, string Head, string Target, string[] Want, string[] Forbid)[]
                {
                    ("honest", pre, seed, head, target, none, new[] { "pre-not-on-target", "later-tree", "target-unresolved" }),
                    ("offTarget", offPre, offSeed, offHead, target, new[] { "pre-not-on-target" }, none),
                    ("laterTree", pre, seed, merged, target, new[] { "later-tree" }, new[] { "pre-not-on-target" }),
                    ("landedMerge", pre, seed, head, "refs/remotes/origin/landedmerge", none, new[] { "later-tree", "pre-not-on-target" }),
                    ("landedFastForward", pre, seed, head, "refs/remotes/origin/landedff", none, new[] { "later-tree", "pre-not-on-target" }),
                    ("forkedLater", pre, seed, forked, target, new[] { "later-tree" }, new[] { "pre-not-on-target" }),
                    ("landedLaterMerge", pre, seed, merged, "refs/remotes/origin/landedlater", new[] { "later-tree" }, new[] { "pre-not-on-target" }),
                    ("noTarget", pre, seed, head, "refs/remotes/origin/c1645-absent", new[] { "target-unresolved" }, none),
                };
                foreach (var c in cases)
                {
                    var got = TargetDiagnostics(dir, c.Pre, c.Seed, c.Head, c.Target);
                    foreach (var want in c.Want)
                        if (!Listed(got, want)) problems.Add("C1645 " + c.Name + " missing " + want + ": " + string.Join(",", got));
                    foreach (var bad in c.Forbid)
                        if (Listed(got, bad)) problems.Add("C1645 " + c.Name + " unexpected " + bad + ": " + string.Join(",", got));
                }
                var blob = string.Concat(System.Linq.Enumerable.Repeat("ab", 20));
                var context = new LiveLaneContext { CensusBaseSha = seed, PreS1BaseSha = pre, SourceSha = head, FixtureBlob = blob, LandingTarget = target };
                var scope = ScopeLine(context, later);
                foreach (var needle in new[] { "scope=commission", "not-run-on-later-trees", "seed=" + seed, "pre=" + pre, "head=" + head, "fixtureBlob=" + blob, "fixture=unauthenticated", "target=" + target + "@" + later })
                    if (scope.IndexOf(needle, StringComparison.Ordinal) < 0) problems.Add("C1645 D-1/D-4 scope line lacks " + needle + ": " + scope);
            }
            finally
            {
                scratch.Clear();
            }
            return problems;
        }

        // New-LiveLaneCensusNode: a Unit node whose uid is namespace.type.method.project.
        private static LiveLaneJsonObject CensusNode(string project, string ns, string type, string method) => new LiveLaneJsonObject
        {
            { "project", project },
            { "uid", ns + "." + type + "." + method + "." + project },
            { "namespace", ns },
            { "type", type },
            { "method", method },
            { "arguments", new string[0] },
            { "categories", new[] { "Unit" } },
        };

        private static void WriteCensusNodes(string path, IEnumerable<LiveLaneJsonObject> nodes) =>
            LiveLaneJson.WriteFile(path, new LiveLaneJsonObject { { "nodes", new List<LiveLaneJsonObject>(nodes) } }, finalNewLine: false);

        // Invoke-LiveLaneScratchGit: git -C root with a fixed identity, unsigned; refuses "scratch-git <args>: <output>".
        private static string ScratchGit(string root, params string[] arguments)
        {
            var start = new ProcessStartInfo("git")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = new UTF8Encoding(false),
                StandardErrorEncoding = new UTF8Encoding(false),
            };
            foreach (var argument in new[] { "-C", root, "-c", "user.name=c1645", "-c", "user.email=c1645@example.invalid", "-c", "commit.gpgsign=false" })
                start.ArgumentList.Add(argument);
            foreach (var argument in arguments) start.ArgumentList.Add(argument);
            using var process = Process.Start(start) ?? throw new InvalidOperationException("git did not start");
            var stderr = process.StandardError.ReadToEndAsync();
            var stdout = process.StandardOutput.ReadToEnd();
            var errors = stderr.GetAwaiter().GetResult();
            process.WaitForExit();
            var text = (stdout + errors).Trim();
            if (process.ExitCode != 0) throw new InvalidOperationException("scratch-git " + string.Join(" ", arguments) + ": " + text);
            return text;
        }

        // New-LiveLaneScratchCommit: file := message, committed; returns the commit.
        private static string ScratchCommit(string root, string file, string message)
        {
            File.WriteAllText(Path.Combine(root, file), message + "\n");
            ScratchGit(root, "add", "--", file);
            ScratchGit(root, "commit", "-q", "-m", message);
            return ScratchGit(root, "rev-parse", "HEAD");
        }

        // ---- the S1a commission --------------------------------------------------------------------------------

        /// <summary>
        /// Write-LiveLaneGroupContext: &lt;repo&gt;/.antiphon/live-lane-capture/context.json for the group: S1a-baseline
        /// (no producer) or S1a (candidate mode, export required, bound to the census seed's fixture blob and its
        /// producers' provenance; census-base-required without a seed, fixture-binding without its fixture). Any other
        /// group refuses (unknown-group). Returns the context's full path.
        /// </summary>
        public static string WriteGroupContext(string repoRoot, string group, string censusBaseSha, string head, string checkpointResults)
        {
            var captureRoot = Path.Combine(repoRoot, ".antiphon", "live-lane-capture");
            Directory.CreateDirectory(captureRoot);
            var runId = Guid.NewGuid().ToString("N");
            var mode = "baseline";
            var exportRequired = false;
            var reason = "baseline-no-producer";
            var census = "";
            var fixtureBlob = "";
            LiveLaneJsonObject provenance = null;
            if (group == "S1a")
            {
                if (!Sha.IsMatch(censusBaseSha ?? "")) throw new InvalidOperationException("census-base-required");
                mode = "candidate";
                exportRequired = true;
                reason = "candidate";
                census = censusBaseSha.ToLowerInvariant();
                var spec = census + ":" + LiveLaneManifestFiles.FixtureRel;
                var blob = Git(repoRoot, "rev-parse", spec);
                if (blob.Exit != 0 || string.IsNullOrWhiteSpace(blob.Text)) throw new InvalidOperationException("fixture-binding");
                fixtureBlob = blob.Text.Trim();
                var scratch = new LiveLaneScratch();
                var fixtureScratch = scratch.NewOwned(repoRoot, "fixture" + runId);
                string recorded;
                var projects = new List<(string Project, string AssemblyHash)>();
                try
                {
                    var copy = Path.Combine(fixtureScratch, "fixture.json");
                    LiveLaneScratch.CopyGitBlob(repoRoot, spec, copy);
                    using var fixture = LiveLaneJson.ReadFile(copy);
                    var producing = LiveLaneJson.Prop(fixture.RootElement, "producing");
                    recorded = LiveLaneJson.Field(producing, "sourceSha");
                    foreach (var row in LiveLaneJson.Rows(producing, "projects")) projects.Add((LiveLaneJson.Field(row, "project"), LiveLaneJson.Field(row, "assemblyHash")));
                }
                finally
                {
                    scratch.Clear();
                }
                provenance = new LiveLaneJsonObject();
                foreach (var (project, assemblyHash) in projects)
                    provenance.Add(project, new LiveLaneJsonObject { { "sourceSha", recorded }, { "assemblyHash", assemblyHash } });
            }
            else if (group != "S1a-baseline")
            {
                throw new InvalidOperationException("unknown-group");
            }
            var contextPath = Path.Combine(captureRoot, "context.json");
            var doc = new LiveLaneJsonObject
            {
                { "mode", mode },
                { "exportRequired", exportRequired },
                { "reason", reason },
                { "censusBaseSha", census },
                { "sourceSha", head },
                { "preS1BaseSha", PreS1BaseSha },
                { "runId", runId },
                { "candidateRoot", repoRoot },
                { "cp25Export", Path.Combine(captureRoot, "cp25-export.json") },
                { "cp1Export", Path.Combine(captureRoot, "cp1-export.json") },
                { "fixturePath", LiveLaneManifestFiles.FixtureRel },
                { "fixtureBlob", fixtureBlob },
                { "landingTarget", LandingTargetRef },
                { "producerProvenance", provenance },
                { "protectedPaths", ProtectedPaths },
                { "allowedSeedPaths", AllowedSeedPaths },
                { "checkpointResults", checkpointResults },
            };
            LiveLaneJson.WriteFile(contextPath, doc, finalNewLine: false);
            return Path.GetFullPath(contextPath);
        }

        /// <summary>
        /// The Group case (the S1a commission): writes the group's context (S1a fetches origin master first: the census
        /// checks the pre-S1 base against the fetched landing target), then runs the checkpoint tool over the manifest
        /// (default .antiphon/s1a-sanitization-checkpoints.yaml) at the expected source (default HEAD) with the context in
        /// ANTIPHON_LIVE_PORTABLE_CONTEXT: S1a runs its rows CP-25, CP-1 and CP-30 one at a time (a command row is ready
        /// before its builds, so one --after run would start CP-30 first and the census would refuse producer-incomplete),
        /// any other group runs --after the group. Each run: --serial, 90m total and 100m hard wall (the admission budget
        /// on a saturated host). Returns the first failing run's exit code, else 0. tool-dll-missing without the tool.
        /// </summary>
        public static int RunGroup(string repoRoot, string group, string toolDll, string checkpointManifest, string expectedSourceSha, string censusBaseSha)
        {
            if (string.IsNullOrWhiteSpace(toolDll) || !(File.Exists(toolDll) || Directory.Exists(toolDll))) throw new InvalidOperationException("tool-dll-missing");
            var manifestPath = string.IsNullOrWhiteSpace(checkpointManifest) ? Path.Combine(repoRoot, ".antiphon", "s1a-sanitization-checkpoints.yaml") : checkpointManifest;
            var head = Git(repoRoot, "rev-parse", "HEAD").Text.Trim();
            var expected = string.IsNullOrWhiteSpace(expectedSourceSha) ? head : expectedSourceSha;
            if (group == "S1a") Inherit("git", null, "-C", repoRoot, "fetch", "-q", "origin", "master");
            var results = Path.Combine(repoRoot, ".antiphon", "live-lane-runs", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(results);
            var contextPath = WriteGroupContext(repoRoot, group, censusBaseSha, head, results);
            var environment = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["ANTIPHON_LIVE_PORTABLE_CONTEXT"] = contextPath,
                ["TEMP"] = "/tmp",
                ["TMP"] = "/tmp",
                ["MSBUILDDISABLENODEREUSE"] = "1",
                ["DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER"] = "1",
            };
            string[] Tool(string selector, string value) => new[]
            {
                "exec", toolDll, "run", manifestPath, selector, value, "--expected-source-sha", expected, "--serial", "--total-timeout", "90m",
                "--hard-wall-timeout", "100m", "--results-root", results,
            };
            if (group == "S1a")
            {
                foreach (var row in new[] { "CP-25", "CP-1", "CP-30" })
                {
                    var code = Inherit("dotnet", environment, Tool("--rows", row));
                    if (code != 0) return code;
                }
                return 0;
            }
            return Inherit("dotnet", environment, Tool("--after", group));
        }

        // A child on this console (output not redirected) with the extra environment; its exit code.
        private static int Inherit(string file, IReadOnlyDictionary<string, string> environment, params string[] arguments)
        {
            var start = new ProcessStartInfo(file) { UseShellExecute = false };
            foreach (var argument in arguments) start.ArgumentList.Add(argument);
            if (environment != null)
                foreach (var pair in environment) start.Environment[pair.Key] = pair.Value;
            using var process = Process.Start(start) ?? throw new InvalidOperationException(file + " did not start");
            process.WaitForExit();
            return process.ExitCode;
        }

        // ---- helpers -------------------------------------------------------------------------------------------

        private static (int Exit, string Text) Git(string repoRoot, params string[] arguments) => LiveLaneScratch.Git(repoRoot, arguments);

        // Get-LiveLaneRepoRoot: the nearest directory at or above start with Antiphon.sln.
        private static string RepoRoot(string start)
        {
            var dir = Directory.Exists(start) ? new DirectoryInfo(start) : File.Exists(start) ? new FileInfo(start).Directory : null;
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "Antiphon.sln"))) return dir.FullName;
                dir = dir.Parent;
            }
            throw new InvalidOperationException("repo-root-missing");
        }

        // Git output lines, trimmed, blanks dropped.
        private static List<string> Lines(string text)
        {
            var lines = new List<string>();
            foreach (var line in (text ?? "").Split('\n'))
            {
                var trimmed = line.Trim();
                if (trimmed.Length > 0) lines.Add(trimmed);
            }
            return lines;
        }

        // Sort-Object -Unique: the current culture, ignoring case; neighbours that compare equal are one.
        private static List<string> SortedUnique(IEnumerable<string> values)
        {
            var comparer = StringComparer.Create(CultureInfo.CurrentCulture, true);
            var sorted = new List<string>(values);
            sorted.Sort(comparer);
            var unique = new List<string>();
            foreach (var value in sorted)
                if (unique.Count == 0 || comparer.Compare(unique[unique.Count - 1], value) != 0) unique.Add(value);
            return unique;
        }

        // Select-Object -Unique: each value once (ordinal), in the order first seen.
        private static List<string> Unique(IEnumerable<string> values)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var unique = new List<string>();
            foreach (var value in values)
                if (seen.Add(value)) unique.Add(value);
            return unique;
        }

        // -contains: the list holds the value, ignoring case.
        private static bool Listed(IEnumerable<string> list, string value)
        {
            foreach (var item in list)
                if (LiveLaneJson.Same(item, value)) return true;
            return false;
        }
    }

    /// <summary>
    /// The census engine (LiveLaneCensusEngine, Add-Type'd by live-lane-manifest.ps1 until S25): BEFORE is the fixture's
    /// nodes, AFTER the producer exports' nodes (optionally patched by a mutant); compares membership by uid, category
    /// deltas (only the six contract LiveClaude additions allowed), expansions (a GUID in an argument is one slot),
    /// declared additive identities, and the nightly and rc dispositions (the rc one with the profile exclusions).
    /// </summary>
    internal sealed class CensusEngine
    {
        private sealed class Node
        {
            public string Project;
            public string Uid;
            public string Ns;
            public string Type;
            public string Method;
            public string ArgKey;
            public string[] Cats;
        }

        /// <summary>A declared identity: project, namespace, class, method and the argument key (values joined by LF).</summary>
        internal sealed class Id
        {
            public Id(string project, string ns, string @class, string method, string argKey)
            {
                Project = project ?? "";
                Ns = ns ?? "";
                Class = @class ?? "";
                Method = method ?? "";
                ArgKey = argKey ?? "";
            }

            public string Project { get; }
            public string Ns { get; }
            public string Class { get; }
            public string Method { get; }
            public string ArgKey { get; }

            // ConvertTo-LiveLaneIdentityJson of one manifest row (class, else type), as the engine parsed it.
            internal static Id FromJson(JsonElement row)
            {
                var @class = LiveLaneJson.Field(row, "class");
                if (string.IsNullOrWhiteSpace(@class)) @class = LiveLaneJson.Field(row, "type");
                return new Id(LiveLaneJson.Field(row, "project"), LiveLaneJson.Field(row, "namespace"), @class, LiveLaneJson.Field(row, "method"),
                    string.Join("\n", LiveLaneJson.Strings(row, "arguments")));
            }

            internal static Id[] Contract()
            {
                var ids = new List<Id>();
                foreach (var row in LiveLaneContract.Core) ids.Add(new Id(row.Project, row.Namespace, row.Class, row.Method, string.Join("\n", row.Arguments)));
                return ids.ToArray();
            }
        }

        private sealed class ExRow
        {
            public string Class;
            public string Reason;
            public string Owner;
            public string[] Methods;
        }

        private readonly Dictionary<string, Node[]> _files = new Dictionary<string, Node[]>(StringComparer.Ordinal);

        /// <summary>The diagnostics of BEFORE (beforeFile) against AFTER (after1 then after2, patched), each once.</summary>
        public List<string> Compare(string beforeFile, string after1, string after2, IReadOnlyList<ExclusionRow> beforeEx, IReadOnlyList<ExclusionRow> afterEx,
            Id[] additive, Id[] contract, string patch)
        {
            var before = Load(beforeFile);
            var after = Concat(Load(after1), Load(after2));
            after = Apply(after, patch ?? "", contract);
            return Run(before, after, Rows(beforeEx), Rows(afterEx), additive, contract);
        }

        private static ExRow[] Rows(IReadOnlyList<ExclusionRow> rows)
        {
            var list = new List<ExRow>();
            foreach (var row in rows ?? new ExclusionRow[0])
            {
                var methods = new List<string>();
                foreach (var m in row.Methods ?? new string[0])
                    if (!string.IsNullOrWhiteSpace(m)) methods.Add(m);
                list.Add(new ExRow { Class = row.Class ?? "", Reason = row.Reason ?? "", Owner = row.Owner ?? "", Methods = methods.ToArray() });
            }
            return list.ToArray();
        }

        private static string Stamp(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return "missing";
            var info = new FileInfo(path);
            return info.Length.ToString(CultureInfo.InvariantCulture) + ":" + info.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture);
        }

        private Node[] Load(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return new Node[0];
            var stamp = path + "\n" + Stamp(path);
            if (_files.TryGetValue(stamp, out var hit)) return hit;
            var list = new List<Node>();
            using (var doc = JsonDocument.Parse(File.ReadAllBytes(path)))
            {
                if (!doc.RootElement.TryGetProperty("nodes", out var nodes) || nodes.ValueKind != JsonValueKind.Array)
                {
                    _files[stamp] = new Node[0];
                    return _files[stamp];
                }
                foreach (var el in nodes.EnumerateArray()) list.Add(ReadNode(el));
            }
            var arr = list.ToArray();
            _files[stamp] = arr;
            return arr;
        }

        private static Node ReadNode(JsonElement el)
        {
            var n = new Node
            {
                Project = Str(el, "project"),
                Uid = Str(el, "uid"),
                Ns = Str(el, "namespace"),
                Type = Str(el, "type"),
                Method = Str(el, "method"),
                ArgKey = ArgKey(el),
                Cats = CatList(el),
            };
            if (string.IsNullOrEmpty(n.Type)) n.Type = Str(el, "class");
            return n;
        }

        private static string Str(JsonElement el, string name)
        {
            if (!el.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null) return "";
            if (value.ValueKind == JsonValueKind.String) return value.GetString() ?? "";
            return value.ToString();
        }

        private static string ArgKey(JsonElement el)
        {
            if (!el.TryGetProperty("arguments", out var value) || value.ValueKind == JsonValueKind.Null) return "";
            if (value.ValueKind == JsonValueKind.String) return value.GetString() ?? "";
            if (value.ValueKind != JsonValueKind.Array) return "";
            var parts = new List<string>();
            foreach (var item in value.EnumerateArray()) parts.Add(item.ValueKind == JsonValueKind.String ? (item.GetString() ?? "") : item.ToString());
            return string.Join("\n", parts);
        }

        private static string[] CatList(JsonElement el)
        {
            if (!el.TryGetProperty("categories", out var value)) return new string[0];
            if (value.ValueKind == JsonValueKind.String) return new[] { value.GetString() ?? "" };
            if (value.ValueKind != JsonValueKind.Array) return new string[0];
            var parts = new List<string>();
            foreach (var item in value.EnumerateArray())
            {
                var text = item.ValueKind == JsonValueKind.String ? (item.GetString() ?? "") : item.ToString();
                if (text.Length > 0) parts.Add(text);
            }
            return parts.ToArray();
        }

        private static Node[] Concat(Node[] left, Node[] right)
        {
            var all = new Node[left.Length + right.Length];
            Array.Copy(left, 0, all, 0, left.Length);
            Array.Copy(right, 0, all, left.Length, right.Length);
            return all;
        }

        private static Node Clone(Node n) => new Node
        {
            Project = n.Project,
            Uid = n.Uid,
            Ns = n.Ns,
            Type = n.Type,
            Method = n.Method,
            ArgKey = n.ArgKey,
            Cats = (string[])n.Cats.Clone(),
        };

        private static bool HasCat(string[] cats, string name)
        {
            foreach (var cat in cats)
                if (string.Equals(cat, name, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static string[] WithoutCat(string[] cats, string name)
        {
            var list = new List<string>();
            foreach (var cat in cats)
                if (!string.Equals(cat, name, StringComparison.OrdinalIgnoreCase)) list.Add(cat);
            return list.ToArray();
        }

        private static string[] WithCat(string[] cats, string name)
        {
            var list = new List<string>(cats) { name };
            return list.ToArray();
        }

        private static bool IsContract(Node n, Id[] contract)
        {
            foreach (var id in contract)
            {
                if (!string.Equals(id.Project, n.Project, StringComparison.OrdinalIgnoreCase)) continue;
                if (!string.Equals(id.Ns, n.Ns, StringComparison.OrdinalIgnoreCase)) continue;
                if (!string.Equals(id.Class, n.Type, StringComparison.OrdinalIgnoreCase)) continue;
                if (!string.Equals(id.Method, n.Method, StringComparison.OrdinalIgnoreCase)) continue;
                if (!string.Equals(id.ArgKey, n.ArgKey, StringComparison.OrdinalIgnoreCase)) continue;
                return true;
            }
            return false;
        }

        // A mutant's edit of AFTER: one node dropped, retagged or added (the FullCensus refusal cases).
        private static Node[] Apply(Node[] after, string patch, Id[] contract)
        {
            if (string.IsNullOrEmpty(patch)) return after;
            if (patch == "drop-first")
            {
                if (after.Length == 0) return after;
                var dropped = new Node[after.Length - 1];
                Array.Copy(after, 1, dropped, 0, dropped.Length);
                return dropped;
            }
            var copy = (Node[])after.Clone();
            if (patch == "remove-integration")
            {
                for (var i = 0; i < copy.Length; i++)
                {
                    if (!HasCat(copy[i].Cats, "Integration")) continue;
                    var n = Clone(copy[i]);
                    n.Cats = WithoutCat(n.Cats, "Integration");
                    copy[i] = n;
                    break;
                }
                return copy;
            }
            if (patch == "tag-seventh")
            {
                for (var i = 0; i < copy.Length; i++)
                {
                    if (IsContract(copy[i], contract)) continue;
                    if (HasCat(copy[i].Cats, "LiveClaude")) continue;
                    var n = Clone(copy[i]);
                    n.Cats = WithCat(n.Cats, "LiveClaude");
                    copy[i] = n;
                    break;
                }
                return copy;
            }
            if (patch == "add-optin")
            {
                for (var i = 0; i < copy.Length; i++)
                {
                    if (HasCat(copy[i].Cats, "OptIn") || HasCat(copy[i].Cats, "Explicit")) continue;
                    if (IsContract(copy[i], contract)) continue;
                    var n = Clone(copy[i]);
                    n.Cats = WithCat(n.Cats, "OptIn");
                    copy[i] = n;
                    break;
                }
                return copy;
            }
            if (patch == "extra-expansion")
            {
                if (copy.Length == 0) return copy;
                var n = Clone(copy[0]);
                n.Uid = (n.Uid ?? "") + "-extra";
                n.ArgKey = "extra-arg";
                var grown = new Node[copy.Length + 1];
                Array.Copy(copy, grown, copy.Length);
                grown[copy.Length] = n;
                return grown;
            }
            if (patch == "unknown-node")
            {
                var n = new Node
                {
                    Project = "Antiphon.Tests",
                    Uid = "undeclared-live-lane-uid",
                    Ns = "Antiphon.Tests.Scripts",
                    Type = "NotInContract",
                    Method = "UnknownMethod",
                    ArgKey = "",
                    Cats = new[] { "Unit" },
                };
                var grown = new Node[copy.Length + 1];
                Array.Copy(copy, grown, copy.Length);
                grown[copy.Length] = n;
                return grown;
            }
            if (patch == "drop-live")
            {
                for (var i = 0; i < copy.Length; i++)
                {
                    if (!IsContract(copy[i], contract)) continue;
                    if (!HasCat(copy[i].Cats, "LiveClaude")) continue;
                    var n = Clone(copy[i]);
                    n.Cats = WithoutCat(n.Cats, "LiveClaude");
                    copy[i] = n;
                    break;
                }
                return copy;
            }
            return copy;
        }

        private static List<string> Run(Node[] before, Node[] after, ExRow[] beforeEx, ExRow[] afterEx, Id[] additive, Id[] contract)
        {
            var diag = new List<string>();
            var liveAdds = 0;
            var beforeMap = new Dictionary<string, Node>(StringComparer.OrdinalIgnoreCase);
            foreach (var n in before) beforeMap[n.Uid ?? ""] = n;
            var afterMap = new Dictionary<string, Node>(StringComparer.OrdinalIgnoreCase);
            foreach (var n in after) afterMap[n.Uid ?? ""] = n;
            foreach (var pair in beforeMap)
            {
                if (!afterMap.TryGetValue(pair.Key, out var other))
                {
                    Add(diag, "old-membership-changed");
                    continue;
                }
                CategoryDelta(pair.Value.Cats, other.Cats, out var removed, out var addedOther, out var addedLive);
                if (addedLive && !removed && !addedOther) liveAdds++;
                if (removed || addedOther) Add(diag, "category-delta");
                else if (addedLive && !IsContract(pair.Value, contract)) Add(diag, "category-delta");
                if (!string.Equals(StableArg(pair.Value.ArgKey), StableArg(other.ArgKey), StringComparison.OrdinalIgnoreCase)) Add(diag, "extra-expansion");
            }
            foreach (var pair in afterMap)
            {
                if (beforeMap.ContainsKey(pair.Key)) continue;
                var n = pair.Value;
                var declared = false;
                foreach (var id in additive)
                {
                    if (!SameId(id, n)) continue;
                    declared = true;
                    break;
                }
                if (!declared) Add(diag, "undeclared-new-uid");
                else if (HasCat(n.Cats, "LiveClaude")) Add(diag, "category-delta");
            }
            if (ExpansionDiffers(before, after)) Add(diag, "extra-expansion");
            var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var n in after)
            {
                foreach (var id in additive)
                {
                    if (!SameId(id, n)) continue;
                    allowed.Add(n.Uid ?? "");
                    break;
                }
            }
            // CARD-1645: a declared addition that is no longer in the after set is a removal.
            foreach (var id in additive)
            {
                var present = false;
                foreach (var n in after)
                {
                    if (!SameId(id, n)) continue;
                    present = true;
                    break;
                }
                if (!present) Add(diag, "declared-additive-missing");
            }
            var beforeDefault = Disposition(before, null, false);
            var afterDefault = Disposition(after, null, false);
            var beforeRc = Disposition(before, beforeEx, true);
            var afterRc = Disposition(after, afterEx, true);
            if (!beforeDefault.Required.SetEquals(Filter(afterDefault.Required, allowed)) || !beforeDefault.Excluded.SetEquals(Filter(afterDefault.Excluded, allowed)))
                Add(diag, "nightly-disposition-changed");
            if (!beforeRc.Required.SetEquals(Filter(afterRc.Required, allowed)) || !beforeRc.Excluded.SetEquals(Filter(afterRc.Excluded, allowed)))
            {
                var c680 = "";
                foreach (var n in before)
                {
                    if (!string.Equals(n.Method, "C680_LiveNudgeRefusesPermissionAndQuestionDialogs", StringComparison.OrdinalIgnoreCase)) continue;
                    c680 = n.Uid ?? "";
                    break;
                }
                if (c680.Length > 0 && beforeRc.Required.Contains(c680) && afterRc.Excluded.Contains(c680)) Add(diag, "C680-required-to-excluded");
            }
            if (liveAdds != 6) Add(diag, "liveclaude-count");
            return diag;
        }

        // A declared identity is project, namespace, class and method; a twin elsewhere is not it.
        private static bool SameId(Id id, Node n) =>
            string.Equals(id.Project, n.Project, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(id.Ns, n.Ns, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(id.Class, n.Type, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(id.Method, n.Method, StringComparison.OrdinalIgnoreCase);

        private static void CategoryDelta(string[] before, string[] after, out bool removed, out bool addedOther, out bool addedLive)
        {
            removed = false;
            addedOther = false;
            addedLive = false;
            var b = new HashSet<string>(before ?? new string[0], StringComparer.Ordinal);
            var a = new HashSet<string>(after ?? new string[0], StringComparer.Ordinal);
            foreach (var item in b)
                if (!a.Contains(item)) removed = true;
            foreach (var item in a)
            {
                if (b.Contains(item)) continue;
                if (string.Equals(item, "LiveClaude", StringComparison.Ordinal)) addedLive = true;
                else addedOther = true;
            }
        }

        private static bool IsHex(char c) => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');

        private static int HexRun(string value, int start)
        {
            var run = 0;
            while (start + run < value.Length && IsHex(value[start + run])) run++;
            return run;
        }

        // C672 discovery calls Guid.NewGuid for one hold sentence. The expansion slot is the UID.
        private static string StableArg(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            var sb = new StringBuilder(value.Length);
            var i = 0;
            while (i < value.Length)
            {
                var run = HexRun(value, i);
                if (run == 32)
                {
                    sb.Append("{guid}");
                    i += 32;
                    continue;
                }
                if (run == 8 && i + 36 <= value.Length && value[i + 8] == '-' && HexRun(value, i + 9) == 4 && value[i + 13] == '-' && HexRun(value, i + 14) == 4
                    && value[i + 18] == '-' && HexRun(value, i + 19) == 4 && value[i + 23] == '-' && HexRun(value, i + 24) == 12)
                {
                    sb.Append("{guid}");
                    i += 36;
                    continue;
                }
                if (run > 0)
                {
                    sb.Append(value, i, run);
                    i += run;
                    continue;
                }
                sb.Append(value[i]);
                i++;
            }
            return sb.ToString();
        }

        private static bool ExpansionDiffers(Node[] before, Node[] after)
        {
            var left = Expand(before);
            var right = Expand(after);
            foreach (var pair in right)
            {
                if (!left.TryGetValue(pair.Key, out var other)) continue;
                if (!pair.Value.SetEquals(other)) return true;
            }
            return false;
        }

        private static Dictionary<string, HashSet<string>> Expand(Node[] nodes)
        {
            var map = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var n in nodes)
            {
                var key = (n.Type ?? "") + "|" + (n.Method ?? "");
                if (!map.TryGetValue(key, out var set))
                {
                    set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    map[key] = set;
                }
                set.Add(StableArg(n.ArgKey));
            }
            return map;
        }

        private sealed class Sets
        {
            public HashSet<string> Required;
            public HashSet<string> Excluded;
        }

        private static Sets Disposition(Node[] nodes, ExRow[] exclusions, bool profile)
        {
            var required = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var index = new Dictionary<string, List<ExRow>>(StringComparer.OrdinalIgnoreCase);
            if (profile && exclusions != null)
            {
                foreach (var row in exclusions)
                {
                    if (string.IsNullOrWhiteSpace(row.Class)) continue;
                    if (!index.TryGetValue(row.Class, out var list))
                    {
                        list = new List<ExRow>();
                        index[row.Class] = list;
                    }
                    list.Add(row);
                }
            }
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var n in nodes)
            {
                var uid = n.Uid ?? "";
                if (!seen.Add(uid)) continue;
                var hit = profile ? ProfileHit(n, index) : HasCat(n.Cats, "OptIn") || HasCat(n.Cats, "Explicit");
                if (hit) excluded.Add(uid);
                else required.Add(uid);
            }
            return new Sets { Required = required, Excluded = excluded };
        }

        private static bool ProfileHit(Node n, Dictionary<string, List<ExRow>> index)
        {
            var className = (n.Ns ?? "") + "." + (n.Type ?? "");
            var open = className.IndexOf('(');
            if (open > 0 && className.EndsWith(")", StringComparison.Ordinal)) className = className.Substring(0, open);
            var dot = className.LastIndexOf('.');
            var simple = (dot >= 0 && dot < className.Length - 1) ? className.Substring(dot + 1) : className;
            if (Hit(index, simple, n.Method)) return true;
            if (!string.Equals(simple, className, StringComparison.OrdinalIgnoreCase) && Hit(index, className, n.Method)) return true;
            return false;
        }

        private static bool Hit(Dictionary<string, List<ExRow>> index, string key, string method)
        {
            if (!index.TryGetValue(key, out var rows)) return false;
            foreach (var row in rows)
            {
                if (row.Methods == null || row.Methods.Length == 0) return true;
                foreach (var m in row.Methods)
                    if (string.Equals(m, method, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        private static HashSet<string> Filter(HashSet<string> set, HashSet<string> allowed)
        {
            var copy = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var id in set)
                if (!allowed.Contains(id)) copy.Add(id);
            return copy;
        }

        private static void Add(List<string> diag, string name)
        {
            if (!diag.Contains(name)) diag.Add(name);
        }
    }
}
