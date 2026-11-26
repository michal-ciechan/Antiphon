using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Antiphon.Nightly;
using Antiphon.Tests.Scripts.Nightly;
using Antiphon.Tests.TestHelpers;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-0599 D-15 full V-12 matrix (round B1; ordinary contract is <see cref="ReleaseGateAuthorityContractTests"/>), in
/// process. These cases replaced the Test-C599A_* cases of scripts/test-release-gate.ps1 when unit 14 of
/// docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md moved scripts/publish-release.ps1 to ReleasePublish: each
/// method runs one former case, with the same row names and row count, on <see cref="ReleaseGateFixture"/> (the C# twin of
/// New-C599PublishFx) against the PRODUCTION ReleaseAuthority guards, publication gate (ReleaseGatePublication.Gate) and
/// publisher (ReleasePublish.Run).
///
/// <para>One real-git B1 fixture per method. Every variant restores that valid control byte for byte, changes exactly one
/// field, row or handshake and must be refused with the NAMED reason token, so a removed guard cannot hide behind another
/// refusal. Gate variants run the publisher's own pre-write composition; one publisher variant per guard also proves zero
/// remote writes through the observer.</para>
///
/// <para>The decisive behaviours: the pinned blob - never the summary, private copy or working tree - names the eight
/// suites; every authority field refuses absent, null, mistyped, array-wrapped and foreign; repository binds to the checkout
/// origin and the publication destination and coordinatorVersion to the supported coordinator; every frozen chunk and
/// required expanded UID must pass exactly once in its own chunk; exits, counts, evidence containment, identities, digests,
/// timestamps (never after the injected publisher clock), whole-run markers and credit flags are checked independently; only
/// a typed prepublication receipt from the bound release card, correlated to this run, is accepted (PC-306), and only when
/// the card read back through GET /api/cards/{id} is at exactly the receipt's revision and its body carries this run's
/// correlation (an unavailable readback refuses); the clock and remote seams are admitted only in explicit test mode, which
/// the publisher refuses whenever a real remote would be reached; and WhatIf writes no publication authority and no tag
/// reservation.</para>
///
/// <para>Deliberate deviations from the PowerShell cases (unit 14): (1) the release authority no longer pins
/// publish-release.ps1 or promote-release.ps1 (both deleted; the publisher's C# is pinned by the lib/Antiphon.Nightly
/// source-tree digest), so C599A_ExecutionLedgerJoins G-300 iterates ReleaseAuthority.Scripts and SourceTrees instead of six
/// hard-coded names and its publisher variant mutates lib/Antiphon.Nightly (script-digest-mismatch:lib/Antiphon.Nightly);
/// (2) C599A_PublisherTestMode: there is no seams file and the core reads no environment variable - test mode is
/// ReleasePublishOptions.TestMode, which the CLI computes with ReleaseGateRemote.TestMode - so the 'outside test mode'
/// variants pass each former ANTIPHON_RELEASE_GATE_TEST_MODE value through ReleaseGateRemote.TestMode with the fixture's
/// seams bound, and the 'real remote' variants bind no seams, or seams without the GitHub or without the Git adapter (the
/// checkout origin still points at a missing local path meanwhile); the injected clock's reads are counted
/// (ReleaseGateFixture.ClockReads) instead of a clock-trace file, and "no remote call" is read from the in-memory recipients'
/// traces; (3) dates in the JSON documents stay strings and are never reinterpreted (ConvertFrom-Json turned them into
/// DateTime); (4) the CARD-1731 timing counters ($script:C1731Timing) are dropped, and with them the per-case pwsh child and
/// its 300-second limit.</para>
/// </summary>
[Category("Integration")]
[Category("Slow")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class ReleaseGatePublicationAuthorityTests
{
    private const string LedgerControlRow = "C599 V-12: the valid 13-chunk 19-UID control ledger passes the publication gate";
    private const string FieldControlRow = "C599 V-12: the valid authority control passes before the field matrix";
    private const string ForeignCard = "00000000-0000-4000-8000-00000000c599";
    private const string ForeignCandidate = "rc-20260101T000000Z";
    private const string ForeignCandidateRef = "release/rc-20260101T000000Z";
    private static readonly string Zeros = new('0', 64);

    private static string[] Eight => ReleaseGateFixture.EightSuites;

    // ---- the pinned policy ------------------------------------------------------------------------------------------

    [Test]
    public void C599A_PinnedPolicy()
    {
        // V-12 full matrix for the pinned policy, first child (G-224, G-225, G-285 gate rows). Summary/report and working
        // tree never decide.
        const string controlRow = "C599 V-12: the valid eight-suite control passes the publication gate";
        const string g224 = "C599 G-224: each of the eight pinned suites missing from an agreeing report refuses with zero remote writes";
        const string g225 = "C599 G-225: a wrong, absent, null or mistyped pinned hash blocks before any remote write";
        const string g285 = "C599 G-285: an edited private copy or working-tree policy cannot replace the pinned blob";
        using var fx = ReleaseGateFixture.Create(ReleaseGateFixture.BasePolicyBytes());
        var rows = new NightlyCaseRows();
        var snap = fx.Save();
        var control = fx.Gate();
        rows.Check(control.Ok && control.Suites.Count == 8, controlRow, Reasons(control));

        // G-224: each suite removed from a plan, ledger and summary that all agree still refuses, because the pinned blob
        // names the eight.
        var m = new List<ReleaseGateCheck>();
        foreach (var sid in Eight)
            m.Add(fx.Variant(snap, "drop " + sid, ["required-suite-missing:" + sid], () =>
            {
                fx.UpdateLedger(
                    ledger: l =>
                    {
                        l["chunks"] = Without(l["chunks"], "suite", sid);
                        l["rosters"] = Without(l["rosters"], "suite", sid);
                    },
                    plan: p => p["suites"] = Without(p["suites"], "id", sid));
                ReleaseGateFixture.UpdateJson(fx.SummaryPath, s => s["requiredSuites"] = Strings(Eight.Where(x => x != sid)));
            }));
        m.AddRange(Checks(fx, snap,
            new Change("extra plan suite", ["plan-suite-unexpected:extra"], () => fx.UpdateLedger(plan: p => p["suites"] = ReleaseGateFixture.Plus(p["suites"],
                new JsonObject { ["id"] = "extra", ["kind"] = "roster", ["entries"] = Strings(["x"]) }))),
            new Change("publisher drop client", ["required-suite-missing:client"], () => fx.UpdateLedger(
                ledger: l => l["rosters"] = Without(l["rosters"], "suite", "client"),
                plan: p => p["suites"] = Without(p["suites"], "id", "client")), Publish: true),
            new Change("publisher legacy RequiredSuites seven", ["required-suites-mismatch"], () => { }, Publish: true,
                Edit: o => o.RequiredSuites = Eight.Where(x => x != "e2e").ToArray())));
        ReleaseGateFixture.Matrix(rows, g224, m);

        // G-225: no ExpectedPolicyHash is supplied; the pinned hash is still required.
        m = new List<ReleaseGateCheck>();
        foreach (var v in new[] { V("zeros", Zeros), Absent(), V("null", null), V("number", 7) })
            m.Add(fx.Variant(snap, "authority policyHash " + v.Label, ["policy-hash-mismatch"], () => fx.UpdateAuthority(a => v.Apply(a, "policyHash"))));
        m.AddRange(Checks(fx, snap,
            new Change("green policyHash foreign", ["green-policy-hash-mismatch"], () => fx.UpdateGreen(g => g["policyHash"] = Zeros)),
            new Change("pinned blob states a stale hash", ["policy-hash-stale"], () => fx.SetPinnedPolicy(ReleaseGateFixture.PolicyBytes(staleHash: true))),
            new Change("publisher absent authority hash", ["policy-hash-mismatch"], () => fx.UpdateAuthority(a => a.Remove("policyHash")), Publish: true),
            new Change("publisher legacy ExpectedPolicyHash foreign", ["policy-hash-mismatch"], () => { }, Publish: true,
                Edit: o => o.ExpectedPolicyHash = new string('f', 64))));
        ReleaseGateFixture.Matrix(rows, g225, m);

        // G-285: the blob at the pinned commit decides; the private copy and the working tree do not.
        var sevenBytes = ReleaseGateFixture.PolicyBytes(o => o["profiles"]!["rc"]!["requiredSuites"] = Strings(Eight.Where(x => x != "e2e")));
        ReleaseGateFixture.Matrix(rows, g285, Checks(fx, snap,
            new Change("private copy edited", ["policy-copy-mismatch"], () => File.WriteAllBytes(fx.PrivatePolicyPath, sevenBytes)),
            new Change("seven-suite working tree with seven-suite ledger", ["required-suite-missing:e2e"], () =>
            {
                File.WriteAllBytes(fx.CheckoutPolicyPath, sevenBytes);
                fx.UpdateLedger(ledger: l => l["chunks"] = Without(l["chunks"], "suite", "e2e"), plan: p => p["suites"] = Without(p["suites"], "id", "e2e"));
            }),
            new Change("publisher private copy edited", ["policy-copy-mismatch"], () => File.WriteAllBytes(fx.PrivatePolicyPath, sevenBytes), Publish: true)));
        rows.ShouldAllPass(4, controlRow, g224, g225, g285);
    }

    [Test]
    public void C599A_PinnedPolicySchema()
    {
        // V-12 full matrix for the pinned policy, second child (G-286, WhatIf and the publisher controls): the pinned blob's
        // own shape, and what a publication writes.
        const string repinRow = "C599 G-286: re-pinning the candidate to a new commit with a valid blob is accepted (repin control)";
        const string g286 = "C599 G-286: an unknown schema, missing or empty rc profile and each absent suite in the pinned blob refuse";
        const string whatIfRow = "C599 V-18: -WhatIf passes the gate but writes no publication authority and nothing remote";
        const string whatIfTagRow = "C599 V-18: -WhatIf proposes the next tag without reserving it and leaves publications.json byte-identical";
        const string g285 = "C599 G-285: a changed working tree and moved HEAD cannot alter the frozen suite authority";
        const string digestRow = "C599 V-18: the frozen publication authority digest is journalled and carried in the manifest";
        using var fx = ReleaseGateFixture.Create(ReleaseGateFixture.BasePolicyBytes());
        var rows = new NightlyCaseRows();
        var snap = fx.Save();
        var sevenBytes = ReleaseGateFixture.PolicyBytes(o => o["profiles"]!["rc"]!["requiredSuites"] = Strings(Eight.Where(x => x != "e2e")));

        // G-286: the pinned blob itself must be schema 2 with the full rc profile. Each variant re-pins the whole candidate
        // to a real commit holding that blob.
        fx.Restore(snap);
        fx.SetPinnedPolicy(ReleaseGateFixture.BasePolicyBytes());
        var repin = fx.Gate();
        fx.Restore(snap);
        rows.Check(repin.Ok, repinRow, Reasons(repin));
        var shapes = new List<Shape>
        {
            new("schema 99", "policy-schema-unsupported", o => o["schemaVersion"] = 99),
            new("schema 1", "policy-schema-unsupported", o => o["schemaVersion"] = 1),
            new("schema string", "policy-schema-unsupported", o => o["schemaVersion"] = "2"),
            new("schema absent", "policy-schema-unsupported", o => o.Remove("schemaVersion")),
            new("profiles absent", "policy-rc-profile-missing", o => o.Remove("profiles")),
            new("rc absent", "policy-rc-profile-missing", o => o["profiles"]!.AsObject().Remove("rc")),
            new("rc empty", "policy-rc-profile-missing", o => o["profiles"]!["rc"]!["requiredSuites"] = new JsonArray()),
            new("unknown rc suite", "policy-rc-suite-unknown:extra-suite",
                o => o["profiles"]!["rc"]!["requiredSuites"] = ReleaseGateFixture.Plus(o["profiles"]!["rc"]!["requiredSuites"], JsonValue.Create("extra-suite"))),
            new("undefined suite", "policy-rc-suite-undefined:messaging", o => o["suites"]!.AsObject().Remove("messaging")),
        };
        foreach (var sid in Eight)
            shapes.Add(new Shape("rc without " + sid, "policy-rc-suite-missing:" + sid, RcWithout(sid)));
        var m = new List<ReleaseGateCheck>();
        foreach (var shape in shapes)
        {
            var bytes = ReleaseGateFixture.PolicyBytes(shape.Edit);
            m.Add(fx.Variant(snap, shape.Label, [shape.Token], () => fx.SetPinnedPolicy(bytes)));
        }
        var schema99 = ReleaseGateFixture.PolicyBytes(o => o["schemaVersion"] = 99);
        var noScripts = ReleaseGateFixture.PolicyBytes(RcWithout("scripts"));
        m.AddRange(Checks(fx, snap,
            new Change("unparseable blob", ["policy-unparseable"], () => fx.SetPinnedPolicy(Encoding.UTF8.GetBytes("{ not json"))),
            new Change("publisher schema 99", ["policy-schema-unsupported"], () => fx.SetPinnedPolicy(schema99), Publish: true),
            new Change("publisher rc without scripts", ["policy-rc-suite-missing:scripts"], () => fx.SetPinnedPolicy(noScripts), Publish: true)));
        ReleaseGateFixture.Matrix(rows, g286, m);

        // WhatIf validates the full authority but writes no publication authority and nothing remote.
        fx.Restore(snap);
        var preview = fx.Publish(o => o.WhatIf = true);
        var authorityWritten = File.Exists(fx.PublicationAuthorityPath);
        rows.Check(preview.Result.ExitCode == 0 && !preview.Result.Published && !authorityWritten && fx.NoRemoteWrite(), whatIfRow,
            "exit=" + preview.Result.ExitCode + " refusal=" + preview.Result.Refusal + " file=" + authorityWritten);

        // WhatIf computes the proposed tag read-only: publications.json is never created, and an existing journal (another
        // candidate's same-day tag) stays byte-identical.
        fx.Restore(snap);
        var journal = fx.PublicationsPath;
        var absentBefore = !File.Exists(journal);
        var firstPreview = fx.Publish(o => o.WhatIf = true);
        var absentAfter = !File.Exists(journal);
        ReleaseGateFixture.WriteNode(journal, new JsonObject
        {
            ["schemaVersion"] = 1,
            ["reservations"] = new JsonArray(new JsonObject
            {
                ["candidateId"] = "rc-20260923T020000Z", ["sha"] = ReleaseGateFixture.ShaC, ["tag"] = "v2026.09.23.1", ["sequence"] = 1,
                ["reservedAt"] = "2026-09-23T02:00:00Z", ["published"] = true, ["releaseId"] = "R-0",
                ["releaseUrl"] = "https://example/releases/tag/v2026.09.23.1",
            }),
        });
        var journalBefore = ReleaseAuthority.Sha256(File.ReadAllBytes(journal));
        var secondPreview = fx.Publish(o => o.WhatIf = true);
        var journalAfter = ReleaseAuthority.Sha256(File.ReadAllBytes(journal));
        rows.Check(absentBefore && absentAfter && firstPreview.Result.ExitCode == 0 && firstPreview.Result.Tag == "v2026.09.23.1"
                && secondPreview.Result.ExitCode == 0 && secondPreview.Result.Tag == "v2026.09.23.2" && journalBefore == journalAfter && fx.NoRemoteWrite(),
            whatIfTagRow,
            "absent-after=" + absentAfter + " tags=" + firstPreview.Result.Tag + "/" + secondPreview.Result.Tag + " same=" + (journalBefore == journalAfter)
            + " refusal=" + secondPreview.Result.Refusal);

        // G-285 publisher control: a seven-suite working tree AND a moved HEAD commit do not alter the frozen authority; the
        // pinned eight still publish.
        fx.Restore(snap);
        var pinnedBlob = fx.RealGit("rev-parse", fx.Head + ":tests/test-execution-policy.json");
        File.WriteAllBytes(fx.CheckoutPolicyPath, sevenBytes);
        fx.RealGit("-c", "user.name=c599", "-c", "user.email=c599@example.invalid", "commit", "-q", "-a", "-m", "seven");
        File.WriteAllBytes(fx.CheckoutPolicyPath, Encoding.UTF8.GetBytes("{ dirty"));
        var ok = fx.Publish();
        var manifest = TryRead(fx.ManifestPath);
        var authority = TryRead(fx.AuthorityPath);
        var release = fx.GitHub.Find(ok.Result.Tag);
        rows.Check(ok.Result.Published && release != null && !release.IsDraft && (manifest?["suites"] as JsonArray)?.Count == 8
                && S(authority?["policyBlobId"]) == pinnedBlob,
            g285, ok.Result.Refusal);
        var publication = TryRead(fx.PublicationAuthorityPath);
        var publishJournal = TryRead(fx.PublishJournalPath);
        var digest = S(publication?["digest"]);
        rows.Check(!string.IsNullOrWhiteSpace(digest) && S(manifest?["publicationAuthorityDigest"]) == digest
                && S(publishJournal?["publicationAuthorityDigest"]) == digest && S(publication?["inputs"]?["policyBlobId"]) == pinnedBlob,
            digestRow, digest);
        rows.ShouldAllPass(6, repinRow, g286, whatIfRow, whatIfTagRow, g285, digestRow);
    }

    [Test]
    public void C599A_PinnedPolicyFields() => FieldMatrix(0, 5,
        "C599 V-12: the frozen schemaVersion, kind, repository, profile and coordinatorVersion fields refuse when absent, null, mistyped, array-wrapped or foreign");

    [Test]
    public void C599A_PinnedPolicyIdentityFields() => FieldMatrix(5, 5,
        "C599 V-12: the frozen sha, candidateId, candidateRef, intentId and releaseCardId fields refuse when absent, null, mistyped, array-wrapped or foreign");

    [Test]
    public void C599A_PinnedPolicyBlobFields() => FieldMatrix(10, 4,
        "C599 V-12: the frozen policyPath, policyBlobId, policyRawSha256 and policyHash fields refuse when absent, null, mistyped, array-wrapped or foreign");

    [Test]
    public void C599A_PinnedPolicySuiteFields() => FieldMatrix(14, 4,
        "C599 V-12: the frozen requiredSuites, exclusions, scriptHashes and createdAt fields refuse when absent, null, mistyped, array-wrapped or foreign");

    [Test]
    public void C599A_PinnedPolicyBindings()
    {
        // V-12 full matrix, third child of C599A_PinnedPolicy: strict JSON shapes inside the list/object fields, and the
        // repository/coordinator bindings. The digest chain is recomputable, so these must bind to something outside the files.
        const string controlRow = "C599 V-12: the valid authority control passes before the binding matrix";
        const string shapesRow = "C599 V-12: requiredSuites, exclusions and script digests refuse joined strings, unknown names and array-wrapped scalars";
        const string bindingRow = "C599 V-12: repository binds to the checkout origin and the publication destination, coordinatorVersion to the supported coordinator";
        using var fx = ReleaseGateFixture.Create(ReleaseGateFixture.BasePolicyBytes());
        var rows = new NightlyCaseRows();
        var snap = fx.Save();
        var control = fx.Gate();
        rows.Check(control.Ok, controlRow, Reasons(control));

        var shapes = new[]
        {
            new Shape("requiredSuites comma-joined string", "authority-suites-type", a => a["requiredSuites"] = string.Join(",", Eight)),
            new Shape("requiredSuites unknown suite name", "authority-suites-type", a => a["requiredSuites"] = Strings(Eight.Select(x => x == "e2e" ? "e2e-live" : x))),
            new Shape("requiredSuites duplicated name", "authority-suites-type", a => a["requiredSuites"] = Strings(Eight.Append("antiphon"))),
            new Shape("requiredSuites element array-wrapped", "authority-suites-type",
                a => a["requiredSuites"] = ReleaseGateFixture.Plus(Strings(Eight.Take(7)), ReleaseGateFixture.Wrapped(JsonValue.Create("e2e")))),
            new Shape("requiredSuites element number", "authority-suites-type", a => a["requiredSuites"] = ReleaseGateFixture.Plus(Strings(Eight.Take(7)), JsonValue.Create(8))),
            new Shape("exclusion methods joined string", "authority-exclusions-type", a => a["exclusions"]![0]!["methods"] = "Live"),
            new Shape("exclusion class array-wrapped", "authority-exclusions-type", a => a["exclusions"]![0]!["class"] = WrappedText(a["exclusions"]![0]!["class"])),
            new Shape("exclusion row a string", "authority-exclusions-type", a => a["exclusions"] = new JsonArray(
                new JsonNode?[] { JsonValue.Create("antiphon:ClaudeAdapterIntegrationTests") }.Concat(a["exclusions"]!.AsArray().Skip(1).Select(n => n?.DeepClone())).ToArray())),
            new Shape("script digest array-wrapped", "script-digest-mismatch:lib/Antiphon.Nightly",
                a => a["scriptHashes"]!["lib/Antiphon.Nightly"] = WrappedText(a["scriptHashes"]!["lib/Antiphon.Nightly"])),
            new Shape("createdAt array-wrapped", "ledger-timestamps", a => a["createdAt"] = WrappedText(a["createdAt"])),
        };
        var m = new List<ReleaseGateCheck>();
        foreach (var shape in shapes)
            m.Add(fx.Variant(snap, shape.Label, [shape.Token], () => fx.UpdateAuthority(shape.Edit)));
        m.AddRange(Checks(fx, snap, new Change("publisher requiredSuites comma-joined string", ["authority-suites-type"],
            () => fx.UpdateAuthority(a => a["requiredSuites"] = string.Join(",", Eight)), Publish: true)));
        ReleaseGateFixture.Matrix(rows, shapesRow, m);

        // repository binds to the checkout's origin AND the publication destination; coordinatorVersion binds to the
        // coordinator this publisher supports.
        var originUrl = fx.RealGit("remote", "get-url", "origin");
        Action restoreOrigin = () => fx.RealGit("remote", "set-url", "origin", originUrl);
        m = new List<ReleaseGateCheck>();
        foreach (var url in new[] { "https://github.com/someone-else/Antiphon", "git@github.com:someone-else/Antiphon.git", "https://example.invalid/michal-ciechan/Antiphon",
                     "michal-ciechan/Antiphon" })
            m.Add(fx.Variant(snap, "checkout origin " + url, ["checkout-origin-repository"], () => fx.RealGit("remote", "set-url", "origin", url), cleanup: restoreOrigin));
        m.Add(fx.Variant(snap, "checkout origin removed", ["checkout-origin-repository"], () => fx.RealGit("remote", "remove", "origin"),
            cleanup: () => ReleaseAuthority.Git(fx.Checkout, "remote", "add", "origin", originUrl)));
        m.Add(fx.Variant(snap, "publisher foreign destination", ["authority-repository", "checkout-origin-repository"], () => { }, publish: true,
            edit: o => o.Repository = "someone-else/Antiphon"));
        m.Add(fx.Variant(snap, "publisher foreign checkout origin", ["checkout-origin-repository"],
            () => fx.RealGit("remote", "set-url", "origin", "https://github.com/someone-else/Antiphon"), publish: true, cleanup: restoreOrigin));
        m.AddRange(Checks(fx, snap,
            new Change("publisher foreign authority repository", ["authority-repository"], () => fx.UpdateAuthority(a => a["repository"] = "someone-else/Antiphon"), Publish: true),
            new Change("publisher foreign coordinatorVersion", ["authority-coordinator-version"], () => fx.UpdateAuthority(a => a["coordinatorVersion"] = "c599-b0"), Publish: true),
            new Change("publisher absent coordinatorVersion", ["authority-coordinator-version"], () => fx.UpdateAuthority(a => a.Remove("coordinatorVersion")), Publish: true)));
        ReleaseGateFixture.Matrix(rows, bindingRow, m);
        rows.ShouldAllPass(3, controlRow, shapesRow, bindingRow);
    }

    // ---- the execution ledger ---------------------------------------------------------------------------------------

    [Test]
    public void C599A_ExecutionLedger()
    {
        // V-12 full matrix for the frozen plan and full execution ledger, first child (G-226, G-287..G-289): failed
        // siblings, evidence containment, prerequisites, native exits and zero executions.
        const string g226 = "C599 G-226: a passing sibling chunk never hides a failed chunk in any suite";
        const string g287 = "C599 G-287: outside, absolute and reparse-escaped evidence refuses before publication";
        const string g287Sealed = "C599 G-287: an unreadable evidence directory is refused on a publication line and nothing is published";
        const string g288 = "C599 G-288: each failed, absent, duplicated or mistyped required prerequisite blocks remote writes";
        const string g289 = "C599 G-289: a nonzero or non-numeric child exit or zero executions blocks despite a passing TRX";
        using var fx = ReleaseGateFixture.Create(ReleaseGateFixture.BasePolicyBytes());
        var rows = new NightlyCaseRows();
        var snap = fx.Save();
        var croot = fx.CandidateRoot;
        var evidence = Path.Combine(croot, "evidence", "antiphon-001.trx");
        var chunks = LedgerControl(fx, rows);

        // G-226: a failed chunk is refused whether its passing sibling runs before or after it.
        var m = new List<ReleaseGateCheck>();
        foreach (var suiteId in chunks.Select(c => c.Suite).Distinct())
        {
            var own = chunks.Where(c => c.Suite == suiteId).ToList();
            foreach (var target in new[] { own[0], own[^1] })
                m.Add(fx.Variant(snap, "fail " + target.Id, ["uid-not-passed:" + target.Uids[0]], () => fx.SetTrxOutcome(target.Id, target.Uids[0], "Failed")));
        }
        m.AddRange(Checks(fx, snap, new Change("publisher fail e2e-002", ["uid-not-passed:e2e-u3"], () => fx.SetTrxOutcome("e2e-002", "e2e-u3", "Failed"), Publish: true)));
        ReleaseGateFixture.Matrix(rows, g226, m);

        // G-287: evidence must resolve under the candidate root with no link component.
        var outsideDir = fx.PathOf("outside-evidence");
        var linkPath = Path.Combine(croot, "evidence-link");
        m = Checks(fx, snap,
            new Change("parent-relative escape", ["chunk-evidence-escape:antiphon-001"], () =>
            {
                File.Copy(evidence, Path.Combine(Path.GetDirectoryName(croot)!, "outside.trx"), true);
                fx.UpdateChunkRow("antiphon-001", c => c["trx"] = "../outside.trx");
            }),
            new Change("absolute path", ["chunk-evidence-escape:antiphon-001"], () => fx.UpdateChunkRow("antiphon-001", c => c["trx"] = evidence)));
        foreach (var via in new[] { "gate", "publisher" })
            m.Add(fx.Variant(snap, "linked directory " + via, ["chunk-evidence-escape:antiphon-001"], () =>
            {
                Directory.CreateDirectory(outsideDir);
                File.Copy(evidence, Path.Combine(outsideDir, "antiphon-001.trx"), true);
                ReleaseGateFixture.DirectoryLink(linkPath, outsideDir);
                fx.UpdateChunkRow("antiphon-001", c => c["trx"] = "evidence-link/antiphon-001.trx");
            }, publish: via == "publisher", cleanup: () => RemoveLink(linkPath)));
        ReleaseGateFixture.Matrix(rows, g287, m);

        // CARD-1770: a component under an unreadable directory must be a REFUSED publication line, not an unhandled error.
        var sealedDir = Path.Combine(croot, "sealed-evidence");
        var sealedFile = Path.Combine(sealedDir, "antiphon-001.trx");
        Action? unlock = null;
        var sealedOk = false;
        var sealedDetail = "";
        try
        {
            Directory.CreateDirectory(sealedDir);
            File.Copy(evidence, sealedFile, true);
            fx.UpdateChunkRow("antiphon-001", c => c["trx"] = "sealed-evidence/antiphon-001.trx");
            unlock = LockUnreadableDirectory(sealedDir);
            var probe = AttributeDenial(sealedFile);
            if (!probe.Denied)
            {
                sealedDetail = "attribute read was not denied: " + probe.Detail;
            }
            else
            {
                // An escape continues before that chunk's UIDs are recorded, so the gate also names them missing. The line
                // below is the publisher text for that existing reason list, not a second refusal.
                const string wantLine = "[publish-release] REFUSED: publication gate (chunk-evidence-escape:antiphon-001,uid-missing:antiphon-u1,uid-missing:antiphon-u2).";
                const string wantReason = "chunk-evidence-escape:antiphon-001,uid-missing:antiphon-u1,uid-missing:antiphon-u2";
                var capture = fx.Publish();
                var result = capture.Result;
                var line = capture.Lines.Contains(wantLine);
                var journalAbsent = !File.Exists(fx.PublicationsPath);
                var noRemote = fx.NoRemoteWrite();
                sealedOk = result.ExitCode == 3 && !result.Published && result.Refusal == wantReason && line && noRemote && journalAbsent;
                sealedDetail = "exit=" + result.ExitCode + " published=" + result.Published + " refusal=[" + result.Refusal + "] line=" + line + " remote=" + noRemote
                    + " journalAbsent=" + journalAbsent + " probe=" + probe.Detail + " lines=[" + string.Join(" | ", capture.Lines) + "]";
            }
        }
        catch (Exception e)
        {
            var inner = e;
            while (inner.InnerException != null) inner = inner.InnerException;
            sealedDetail = "threw: " + inner.GetType().FullName + " " + inner.Message;
            sealedOk = false;
        }
        finally
        {
            unlock?.Invoke();
            if (File.Exists(sealedFile)) File.Delete(sealedFile);
            if (Directory.Exists(sealedDir)) Directory.Delete(sealedDir);
            fx.Restore(snap);
        }
        rows.Check(sealedOk, g287Sealed, sealedDetail);

        // G-288: every required build/prerequisite row.
        var prerequisiteShapes = new[]
        {
            new Shape("exit 1", "prerequisite-failed", r => r["exitCode"] = 1),
            new Shape("succeeded false", "prerequisite-failed", r => r["succeeded"] = false),
            new Shape("succeeded string", "prerequisite-failed", r => r["succeeded"] = "true"),
            new Shape("exit string", "prerequisite-failed", r => r["exitCode"] = "0"),
            new Shape("exit absent", "prerequisite-failed", r => r.Remove("exitCode")),
        };
        m = new List<ReleaseGateCheck>();
        foreach (var pre in new[] { "build", "client-bundle", "script-census" })
        {
            foreach (var shape in prerequisiteShapes)
                m.Add(fx.Variant(snap, pre + " " + shape.Label, [shape.Token + ":" + pre], () => fx.UpdateLedger(l => Each(l["prerequisites"], "id", pre, shape.Edit))));
            m.Add(fx.Variant(snap, pre + " absent", ["prerequisite-missing:" + pre], () => fx.UpdateLedger(l => l["prerequisites"] = Without(l["prerequisites"], "id", pre))));
            m.Add(fx.Variant(snap, pre + " duplicated", ["prerequisite-missing:" + pre],
                () => fx.UpdateLedger(l => l["prerequisites"] = ReleaseGateFixture.Plus(l["prerequisites"], Only(l["prerequisites"], "id", pre).ToArray()))));
        }
        m.AddRange(Checks(fx, snap, new Change("publisher script-census exit 1", ["prerequisite-failed:script-census"],
            () => fx.UpdateLedger(l => Each(l["prerequisites"], "id", "script-census", r => r["exitCode"] = 1)), Publish: true)));
        ReleaseGateFixture.Matrix(rows, g288, m);

        // G-289: native exit and actual counts, not an all-passed TRX, decide.
        ReleaseGateFixture.Matrix(rows, g289, Checks(fx, snap,
            new Change("antiphon-001 exit 1", ["chunk-exit:antiphon-001"], () => fx.UpdateChunkRow("antiphon-001", c => c["exitCode"] = 1)),
            new Change("e2e-002 exit -1", ["chunk-exit:e2e-002"], () => fx.UpdateChunkRow("e2e-002", c => c["exitCode"] = -1)),
            new Change("exit absent", ["chunk-exit:pty-host-001"], () => fx.UpdateChunkRow("pty-host-001", c => c.Remove("exitCode"))),
            new Change("exit string", ["chunk-exit:agents-pty-002"], () => fx.UpdateChunkRow("agents-pty-002", c => c["exitCode"] = "0")),
            new Change("zero executions", ["chunk-zero-executed:messaging-002"], () => fx.UpdateTrx("messaging-002", _ => Array.Empty<NightlyTrxRow>())),
            new Change("roster exit 1", ["roster-exit:scripts"], () => fx.UpdateRosterRow("scripts", r => r["exitCode"] = 1)),
            new Change("roster exit absent", ["roster-exit:client"], () => fx.UpdateRosterRow("client", r => r.Remove("exitCode"))),
            new Change("publisher messaging-002 exit 1", ["chunk-exit:messaging-002"], () => fx.UpdateChunkRow("messaging-002", c => c["exitCode"] = 1), Publish: true)));
        rows.ShouldAllPass(6, LedgerControlRow, g226, g287, g287Sealed, g288, g289);
    }

    [Test]
    public void C599A_ExecutionLedgerChunks()
    {
        // V-12 full matrix, second child of C599A_ExecutionLedger (G-290..G-292): the exact frozen chunk set, disjoint UID
        // membership and every required expanded UID.
        const string g290 = "C599 G-290: every deleted, extra or duplicated frozen chunk blocks authority";
        const string g291 = "C599 G-291: a duplicated UID in a sibling chunk, the same chunk or the plan refuses";
        const string g292Uids = "C599 G-292: each required expanded UID removed from its chunk evidence refuses";
        const string g292Shapes = "C599 G-292: skipped, unknown-outcome, unknown-UID, stale and missing evidence refuse";
        using var fx = ReleaseGateFixture.Create(ReleaseGateFixture.BasePolicyBytes());
        var rows = new NightlyCaseRows();
        var snap = fx.Save();
        var chunks = LedgerControl(fx, rows);

        // G-290: exactly the frozen chunk set.
        var m = new List<ReleaseGateCheck>();
        foreach (var chunk in chunks)
            m.Add(fx.Variant(snap, "delete " + chunk.Id, ["chunk-missing:" + chunk.Id], () => fx.UpdateLedger(l => l["chunks"] = Without(l["chunks"], "chunk", chunk.Id))));
        m.AddRange(Checks(fx, snap,
            new Change("extra ledger chunk", ["chunk-unexpected:antiphon-099"], () => fx.UpdateLedger(AddExtraChunk)),
            new Change("duplicate ledger chunk", ["chunk-duplicate:antiphon-001"],
                () => fx.UpdateLedger(l => l["chunks"] = ReleaseGateFixture.Plus(l["chunks"], Only(l["chunks"], "chunk", "antiphon-001").ToArray()))),
            new Change("plan chunk removed", ["plan-uid-missing:antiphon-u3", "chunk-unexpected:antiphon-002"],
                () => fx.UpdateLedger(plan: p => Each(p["suites"], "id", "antiphon", s => s["chunks"] = Without(s["chunks"], "id", "antiphon-002")))),
            new Change("plan chunk added", ["plan-uid-unexpected:antiphon-u9", "chunk-missing:antiphon-004"],
                () => fx.UpdateLedger(plan: p => Each(p["suites"], "id", "antiphon", s => s["chunks"] = ReleaseGateFixture.Plus(s["chunks"],
                    new JsonObject { ["id"] = "antiphon-004", ["classes"] = Strings(["antiphonDeltaTests"]), ["uids"] = Strings(["antiphon-u9"]) })))),
            new Change("publisher extra ledger chunk", ["chunk-unexpected:antiphon-099"], () => fx.UpdateLedger(AddExtraChunk), Publish: true)));
        ReleaseGateFixture.Matrix(rows, g290, m);

        // G-291: disjoint membership; a duplicate UID is never deduplicated into coverage.
        Action dupSibling = () => fx.UpdateTrx("antiphon-002", trx => trx.Append(new NightlyTrxRow("antiphon-u1", "antiphonAlphaTests", "One", "Passed")));
        ReleaseGateFixture.Matrix(rows, g291, Checks(fx, snap,
            new Change("duplicate in sibling chunk", ["uid-wrong-chunk:antiphon-u1"], dupSibling),
            new Change("duplicate in same chunk", ["chunk-trx-unreadable:antiphon-001"], () => fx.UpdateTrx("antiphon-001", trx => trx.Append(trx[0]))),
            new Change("plan overlap", ["plan-uid-overlap:antiphon-u1"], () => fx.UpdateLedger(plan: p => Each(p["suites"], "id", "antiphon",
                s => Each(s["chunks"], "id", "antiphon-002", c => c["uids"] = ReleaseGateFixture.Plus(c["uids"], JsonValue.Create("antiphon-u1")))))),
            new Change("publisher duplicate in sibling chunk", ["uid-wrong-chunk:antiphon-u1"], dupSibling, Publish: true)));

        // G-292: every required expanded UID independently, and every non-pass shape.
        m = new List<ReleaseGateCheck>();
        foreach (var chunk in chunks)
            foreach (var uid in chunk.Uids)
                m.Add(fx.Variant(snap, "remove " + uid, ["uid-missing:" + uid], () => fx.UpdateTrx(chunk.Id, trx => trx.Where(t => t.Id != uid))));
        ReleaseGateFixture.Matrix(rows, g292Uids, m);
        ReleaseGateFixture.Matrix(rows, g292Shapes, Checks(fx, snap,
            new Change("skipped", ["uid-not-passed:messaging-u2"], () => fx.SetTrxOutcome("messaging-001", "messaging-u2", "NotExecuted")),
            new Change("unknown outcome", ["uid-not-passed:session-runner-u3"], () => fx.SetTrxOutcome("session-runner-002", "session-runner-u3", "Inconclusive")),
            new Change("unknown uid", ["uid-unknown:antiphon-zz"],
                () => fx.UpdateTrx("antiphon-001", trx => trx.Append(new NightlyTrxRow("antiphon-zz", "antiphonAlphaTests", "Stray", "Passed")))),
            new Change("stale evidence", ["chunk-evidence-digest:agents-pty-001"],
                () => fx.UpdateTrx("agents-pty-001", trx => trx.Select(t => t with { Outcome = "Failed" }), keepBinding: true)),
            new Change("evidence file missing", ["chunk-evidence-missing:e2e-001"], () => File.Delete(Path.Combine(fx.CandidateRoot, "evidence", "e2e-001.trx"))),
            new Change("publisher unknown outcome", ["uid-not-passed:pty-host-u1"], () => fx.SetTrxOutcome("pty-host-001", "pty-host-u1", "Inconclusive"), Publish: true)));
        rows.ShouldAllPass(5, LedgerControlRow, g290, g291, g292Uids, g292Shapes);
    }

    [Test]
    public void C599A_ExecutionLedgerCounts()
    {
        // V-12 full matrix, third child of C599A_ExecutionLedger (G-293..G-295): empty required sets, counts recomputed from
        // the TRX and pinned exclusions.
        const string g293 = "C599 G-293: a zero-required suite or empty declared roster cannot publish";
        const string g294 = "C599 G-294: inflated or mistyped ledger counts refuse against the recomputed TRX";
        const string g295 = "C599 G-295: a post-result exclusion edit cannot remove a failed UID";
        using var fx = ReleaseGateFixture.Create(ReleaseGateFixture.BasePolicyBytes());
        var rows = new NightlyCaseRows();
        var snap = fx.Save();
        var chunks = LedgerControl(fx, rows);

        // G-293: a consistent zero-required suite or empty declared roster never publishes.
        var m = new List<ReleaseGateCheck>();
        foreach (var suiteId in chunks.Select(c => c.Suite).Distinct())
            m.Add(fx.Variant(snap, "empty " + suiteId, ["required-uids-empty:" + suiteId], () => EmptySuite(fx, suiteId, d => d["suites"]![suiteId] = new JsonArray())));
        m.Add(fx.Variant(snap, "e2e all excluded", ["required-uids-empty:e2e"], () => EmptySuite(fx, "e2e", d => d["suites"]!["e2e"] = Only(d["suites"]!["e2e"], "uid", "e2e-x1"))));
        foreach (var rid in new[] { "client", "scripts" })
            m.Add(fx.Variant(snap, "empty roster " + rid, ["roster-empty:" + rid], () => fx.UpdateLedger(
                ledger: l => Each(l["rosters"], "suite", rid, r => r["entries"] = new JsonArray()),
                plan: p => Each(p["suites"], "id", rid, s => s["entries"] = new JsonArray()))));
        m.Add(fx.Variant(snap, "publisher empty messaging", ["required-uids-empty:messaging"],
            () => EmptySuite(fx, "messaging", d => d["suites"]!["messaging"] = new JsonArray()), publish: true));
        ReleaseGateFixture.Matrix(rows, g293, m);

        // G-294: counts are recomputed from the raw TRX, never trusted from the ledger.
        m = new List<ReleaseGateCheck>();
        foreach (var shape in new[]
                 {
                     new Shape("passed +1", "chunk-count-passed", c => c["passed"] = c["passed"]!.GetValue<int>() + 1),
                     new Shape("executed +1", "chunk-count-executed", c => c["executed"] = c["executed"]!.GetValue<int>() + 1),
                     new Shape("failed string", "chunk-count-failed", c => c["failed"] = "0"),
                     new Shape("skipped absent", "chunk-count-skipped", c => c.Remove("skipped")),
                 })
            m.Add(fx.Variant(snap, shape.Label, [shape.Token + ":antiphon-001"], () => fx.UpdateChunkRow("antiphon-001", shape.Edit)));
        m.AddRange(Checks(fx, snap, new Change("publisher plausible inflated counts", ["chunk-count-passed:session-runner-001", "chunk-count-executed:session-runner-001"],
            () => fx.UpdateChunkRow("session-runner-001", c =>
            {
                c["passed"] = c["passed"]!.GetValue<int>() + 1;
                c["executed"] = c["executed"]!.GetValue<int>() + 1;
            }), Publish: true)));
        ReleaseGateFixture.Matrix(rows, g294, m);

        // G-295: exclusions come from the pinned blob; a post-result edit cannot drop a failed UID.
        static JsonObject Gamma() => new() { ["suite"] = "antiphon", ["class"] = "antiphonGammaTests", ["reason"] = "post-result edit", ["owner"] = "CARD-0599" };
        var excludedBytes = ReleaseGateFixture.PolicyBytes(o => o["profiles"]!["rc"]!["exclusions"] = ReleaseGateFixture.Plus(o["profiles"]!["rc"]!["exclusions"], Gamma()));
        Action failGamma = () => fx.SetTrxOutcome("antiphon-003", "antiphon-u4", "Failed");
        ReleaseGateFixture.Matrix(rows, g295, Checks(fx, snap,
            new Change("authority exclusions edited", ["authority-exclusions-mismatch"], () =>
            {
                failGamma();
                fx.UpdateAuthority(a => a["exclusions"] = ReleaseGateFixture.Plus(a["exclusions"], Gamma()));
            }),
            new Change("working-tree policy edited", ["uid-not-passed:antiphon-u4"], () =>
            {
                failGamma();
                File.WriteAllBytes(fx.CheckoutPolicyPath, excludedBytes);
            }),
            new Change("private copy edited", ["policy-copy-mismatch", "uid-not-passed:antiphon-u4"], () =>
            {
                failGamma();
                File.WriteAllBytes(fx.PrivatePolicyPath, excludedBytes);
            }),
            new Change("summary exclusions edited", ["uid-not-passed:antiphon-u4"], () =>
            {
                failGamma();
                ReleaseGateFixture.UpdateJson(fx.SummaryPath, s =>
                {
                    foreach (var suite in Objects(s["suites"]).ToList()) suite["exclusions"] = new JsonArray(Gamma());
                });
            }),
            new Change("publisher authority exclusions edited", ["authority-exclusions-mismatch"], () =>
            {
                failGamma();
                fx.UpdateAuthority(a => a["exclusions"] = ReleaseGateFixture.Plus(a["exclusions"], Gamma()));
            }, Publish: true)));
        rows.ShouldAllPass(4, LedgerControlRow, g293, g294, g295);
    }

    [Test]
    public void C599A_ExecutionLedgerJoins()
    {
        // V-12 full matrix, second child of C599A_ExecutionLedger: identity joins (G-296..G-299), digests (G-300) and
        // timestamps (G-301).
        const string g296 = "C599 G-296: foreign intent evidence on the plan, ledger, a chunk or a roster refuses";
        const string g297 = "C599 G-297: foreign candidate evidence on the plan, ledger, a chunk or a roster refuses";
        const string g298 = "C599 G-298: foreign full SHA evidence on the plan, ledger, a chunk or a roster refuses";
        const string g299 = "C599 G-299: foreign native RunId evidence on the plan, ledger, a chunk or a roster refuses";
        const string g300 = "C599 G-300: a modified script, authority, plan or discovery digest blocks publication";
        const string g301 = "C599 G-301: out-of-order or stale evidence timestamps refuse";
        const string g301Clock = "C599 G-301: evidence dated after the injected publisher clock refuses";
        using var fx = ReleaseGateFixture.Create(ReleaseGateFixture.BasePolicyBytes());
        var rows = new NightlyCaseRows();
        var snap = fx.Save();
        LedgerControl(fx, rows);

        // G-296..G-299: every record joins the same intent, candidate, full SHA and RunId.
        var abbreviated = fx.Head[..12];
        ReleaseGateFixture.Matrix(rows, g296, Checks(fx, snap,
            new Change("plan", ["plan-intent"], () => fx.UpdateLedger(plan: p => p["intentId"] = "intent-foreign")),
            new Change("ledger", ["ledger-intent"], () => fx.UpdateLedger(l => l["intentId"] = "intent-foreign")),
            new Change("chunk", ["chunk-intent:e2e-001"], () => fx.UpdateChunkRow("e2e-001", c => c["intentId"] = "intent-foreign")),
            new Change("roster", ["roster-identity:scripts"], () => fx.UpdateRosterRow("scripts", r => r["intentId"] = "intent-foreign")),
            new Change("ledger array-wrapped", ["ledger-intent"], () => fx.UpdateLedger(l => l["intentId"] = WrappedText(l["intentId"]))),
            new Change("publisher chunk", ["chunk-intent:agents-pty-002"], () => fx.UpdateChunkRow("agents-pty-002", c => c["intentId"] = "intent-foreign"), Publish: true)));
        ReleaseGateFixture.Matrix(rows, g297, Checks(fx, snap,
            new Change("plan", ["plan-candidate"], () => fx.UpdateLedger(plan: p => p["candidateId"] = ForeignCandidate)),
            new Change("ledger", ["ledger-candidate"], () => fx.UpdateLedger(l => l["candidateId"] = ForeignCandidate)),
            new Change("ledger ref", ["ledger-candidate-ref"], () => fx.UpdateLedger(l => l["candidateRef"] = ForeignCandidateRef)),
            new Change("chunk", ["chunk-candidate:pty-host-002"], () => fx.UpdateChunkRow("pty-host-002", c => c["candidateId"] = ForeignCandidate)),
            new Change("roster", ["roster-identity:client"], () => fx.UpdateRosterRow("client", r => r["candidateId"] = ForeignCandidate)),
            new Change("chunk array-wrapped", ["chunk-candidate:pty-host-001"], () => fx.UpdateChunkRow("pty-host-001", c => c["candidateId"] = WrappedText(c["candidateId"]))),
            new Change("publisher ledger ref", ["ledger-candidate-ref"], () => fx.UpdateLedger(l => l["candidateRef"] = ForeignCandidateRef), Publish: true)));
        ReleaseGateFixture.Matrix(rows, g298, Checks(fx, snap,
            new Change("plan", ["plan-sha"], () => fx.UpdateLedger(plan: p => p["sha"] = ReleaseGateFixture.ShaC)),
            new Change("ledger foreign", ["ledger-sha"], () => fx.UpdateLedger(l => l["sha"] = ReleaseGateFixture.ShaC)),
            new Change("ledger abbreviated", ["ledger-sha"], () => fx.UpdateLedger(l => l["sha"] = abbreviated)),
            new Change("chunk foreign", ["chunk-sha:messaging-001"], () => fx.UpdateChunkRow("messaging-001", c => c["sha"] = ReleaseGateFixture.ShaC)),
            new Change("roster", ["roster-identity:scripts"], () => fx.UpdateRosterRow("scripts", r => r["sha"] = abbreviated)),
            new Change("roster array-wrapped", ["roster-identity:client"], () => fx.UpdateRosterRow("client", r => r["sha"] = WrappedText(r["sha"]))),
            new Change("publisher chunk abbreviated", ["chunk-sha:antiphon-002"], () => fx.UpdateChunkRow("antiphon-002", c => c["sha"] = abbreviated), Publish: true)));
        ReleaseGateFixture.Matrix(rows, g299, Checks(fx, snap,
            new Change("ledger", ["ledger-run"], () => fx.UpdateLedger(l => l["runId"] = "rc-run-foreign")),
            new Change("chunk", ["chunk-run:session-runner-002"], () => fx.UpdateChunkRow("session-runner-002", c => c["runId"] = "rc-run-foreign")),
            new Change("roster", ["roster-identity:client"], () => fx.UpdateRosterRow("client", r => r["runId"] = "rc-run-foreign")),
            new Change("ledger array-wrapped", ["ledger-run"], () => fx.UpdateLedger(l => l["runId"] = WrappedText(l["runId"]))),
            new Change("green", ["ledger-run", "chunk-run:antiphon-001"], () => fx.UpdateGreen(g => g["runId"] = "rc-run-foreign")),
            new Change("publisher chunk", ["chunk-run:e2e-002"], () => fx.UpdateChunkRow("e2e-002", c => c["runId"] = "rc-run-foreign"), Publish: true)));

        // G-300: the authority, plan, discovery and every pinned script and source-tree digest bind. Unit 14: the pinned set
        // is ReleaseAuthority.Scripts and SourceTrees (publish-release.ps1 and promote-release.ps1 are gone; the publisher's
        // C# is the lib/Antiphon.Nightly tree).
        var m = new List<ReleaseGateCheck>();
        foreach (var scriptRel in ReleaseAuthority.Scripts.Concat(ReleaseAuthority.SourceTrees))
            m.Add(fx.Variant(snap, "script " + scriptRel, ["script-digest-mismatch:" + scriptRel], () => fx.UpdateAuthority(a => a["scriptHashes"]![scriptRel] = Zeros)));
        m.AddRange(Checks(fx, snap,
            new Change("authority changed without re-binding", ["plan-authority-digest", "ledger-authority-digest"],
                () => ReleaseGateFixture.UpdateJson(fx.AuthorityPath, a => a["coordinatorVersion"] = "c599-other")),
            new Change("plan digest", ["ledger-plan-digest"], () => fx.UpdateLedger(l => l["executionPlanDigest"] = Zeros)),
            new Change("discovery changed without re-binding", ["plan-discovery-digest"], () => ReleaseGateFixture.UpdateJson(fx.DiscoveryPath,
                d => d["suites"]!["antiphon"] = ReleaseGateFixture.Plus(d["suites"]!["antiphon"],
                    new JsonObject { ["uid"] = "antiphon-x2", ["class"] = "ClaudeAdapterIntegrationTests", ["method"] = "Other" }))),
            new Change("publisher script digest", ["script-digest-mismatch:lib/Antiphon.Nightly"],
                () => fx.UpdateAuthority(a => a["scriptHashes"]!["lib/Antiphon.Nightly"] = Zeros), Publish: true)));
        ReleaseGateFixture.Matrix(rows, g300, m);

        // G-301: authority <= run start <= every chunk/roster window <= run end.
        ReleaseGateFixture.Matrix(rows, g301, Checks(fx, snap,
            new Change("chunk end before start", ["chunk-timestamps:antiphon-002"], () => fx.UpdateChunkRow("antiphon-002", c => c["completedAt"] = "2026-09-23T08:30:59Z")),
            new Change("chunk before run", ["chunk-timestamps:antiphon-001"], () => fx.UpdateChunkRow("antiphon-001", c => c["startedAt"] = "2026-09-23T08:30:40Z")),
            new Change("chunk after run", ["chunk-timestamps:e2e-002"], () => fx.UpdateChunkRow("e2e-002", c => c["completedAt"] = "2026-09-23T09:51:00Z")),
            new Change("chunk unparseable", ["chunk-timestamps:messaging-001"], () => fx.UpdateChunkRow("messaging-001", c => c["startedAt"] = "yesterday")),
            new Change("run before authority", ["ledger-timestamps"], () => fx.UpdateLedger(l => l["startedAt"] = "2026-09-22T08:31:00Z")),
            new Change("run end equals start", ["ledger-timestamps"], () => fx.UpdateLedger(l => l["completedAt"] = l["startedAt"]?.DeepClone())),
            new Change("run start absent", ["ledger-timestamps"], () => fx.UpdateLedger(l => l.Remove("startedAt"))),
            new Change("roster end before start", ["roster-timestamps:client"], () => fx.UpdateRosterRow("client", r => r["completedAt"] = "2026-09-23T09:29:00Z")),
            new Change("publisher chunk end before start", ["chunk-timestamps:pty-host-001"],
                () => fx.UpdateChunkRow("pty-host-001", c => c["completedAt"] = "2026-09-23T08:31:00Z"), Publish: true)));

        // G-301 bound: evidence dated after the publisher's clock refuses. The fixture clock (ReleaseGateFixture.Now) is
        // injected and earlier than the wall clock, so a check against the wall clock would accept every variant below.
        ReleaseGateFixture.Matrix(rows, g301Clock, Checks(fx, snap,
            new Change("whole run shifted one hour ahead", ["evidence-future:ledger", "evidence-future:roster:client", "evidence-future:receipt"],
                () => fx.MoveEvidenceTime(TimeSpan.FromHours(1))),
            new Change("run end after the clock", ["evidence-future:ledger"], () => fx.UpdateLedger(l => l["completedAt"] = "2026-09-23T10:30:00Z")),
            new Change("authority created after the clock", ["evidence-future:authority"], () => fx.UpdateAuthority(a => a["createdAt"] = "2026-09-24T08:30:30Z")),
            new Change("publisher whole run shifted one day ahead", ["evidence-future:ledger"], () => fx.MoveEvidenceTime(TimeSpan.FromDays(1)), Publish: true)));
        rows.ShouldAllPass(8, LedgerControlRow, g296, g297, g298, g299, g300, g301, g301Clock);
    }

    [Test]
    public void C599A_ExecutionLedgerMarkers()
    {
        // V-12 full matrix, fourth child of C599A_ExecutionLedger: whole-run markers (G-302..G-305), credit flags
        // (G-387..G-389), declared rosters and the restored control publication.
        const string g302 = "C599 G-302: a missing, false, string or numeric teardown cannot publish";
        const string g303 = "C599 G-303: a seam-driven ledger or run creates no release";
        const string g304 = "C599 G-304: a NoReport ledger or run creates no release";
        const string g305 = "C599 G-305: subset and diagnostic ledgers or runs create no release";
        const string g387 = "C599 G-387: a string or non-true coverageComplete flag cannot publish";
        const string g388 = "C599 G-388: a string or non-true testsPassed flag cannot publish";
        const string g389 = "C599 G-389: a string or non-true reportDelivered flag cannot publish";
        const string rosterRow = "C599 V-12: client and script evidence validate against their own declared rosters";
        const string restoredRow = "C599 V-12: the restored control ledger publishes with suite counts recomputed from the evidence";
        using var fx = ReleaseGateFixture.Create(ReleaseGateFixture.BasePolicyBytes());
        var rows = new NightlyCaseRows();
        var snap = fx.Save();
        LedgerControl(fx, rows);

        // G-302..G-305 and report acceptance: whole-run markers are real booleans / exact values.
        var markers = new[]
        {
            new Marker(g302, "teardownSucceeded", "teardown-not-succeeded", [Absent(), V("False", false), V("true", "true"), V("1", 1)], false),
            new Marker(g303, "seamed", "ledger-seamed", [Absent(), V("True", true), V("false", "false")], true, "seamed", "seam-driven-run"),
            new Marker(g304, "noReport", "ledger-no-report", [Absent(), V("True", true), V("false", "false")], true, "noReport", "no-report-run"),
            new Marker(g305, "selection", "ledger-selection",
                [Absent(), V("subset", "subset"), V("diagnostic", "diagnostic"), V("Full", "Full"), V("[full]", ReleaseGateFixture.Wrapped(JsonValue.Create("full")))],
                "subset", "diagnostic", "diagnostic-run", "diagnostic", "ledger-diagnostic"),
        };
        var m = new List<ReleaseGateCheck>();
        foreach (var mk in markers)
        {
            m = new List<ReleaseGateCheck>();
            var fields = new List<MarkerField> { new(mk.Field, mk.Token, mk.Values) };
            if (mk.Field2 != null) fields.Add(new MarkerField(mk.Field2, mk.Token2!, [Absent(), V("True", true), V("false", "false")]));
            foreach (var fd in fields)
                foreach (var val in fd.Values)
                    m.Add(fx.Variant(snap, fd.Field + "=" + val.Label, [fd.Token], () => fx.UpdateLedger(l => val.Apply(l, fd.Field))));
            if (mk.Green != null)
                m.Add(fx.Variant(snap, "green " + mk.Green, [mk.GreenToken!], () => fx.UpdateGreen(g => g[mk.Green!] = true)));
            m.Add(fx.Variant(snap, "publisher " + mk.Field, [mk.Token], () => fx.UpdateLedger(l => l[mk.Field] = mk.PublishValue.DeepClone()), publish: true));
            ReleaseGateFixture.Matrix(rows, mk.Row, m);
        }

        // G-387..G-389: complete-green credit flags are real booleans only.
        foreach (var (flagRow, flag) in new[] { (g387, "coverageComplete"), (g388, "testsPassed"), (g389, "reportDelivered") })
        {
            m = new List<ReleaseGateCheck>();
            foreach (var val in new[] { V("true", "true"), V("False", false), Absent(), V("1", 1) })
                m.Add(fx.Variant(snap, flag + "=" + val.Label, ["not-rc-complete-green", "not-true-boolean:" + flag], () => fx.UpdateGreen(g => val.Apply(g, flag))));
            m.Add(fx.Variant(snap, "publisher string " + flag, ["not-rc-complete-green"], () => fx.UpdateGreen(g => g[flag] = "true"), publish: true));
            ReleaseGateFixture.Matrix(rows, flagRow, m);
        }

        // Client and script evidence validate by their own declared rosters.
        ReleaseGateFixture.Matrix(rows, rosterRow, Checks(fx, snap,
            new Change("roster entry missing", ["roster-entry-missing:scripts:test-nightly.ps1"],
                () => fx.UpdateRosterRow("scripts", r => r["entries"] = Without(r["entries"], "id", "test-nightly.ps1"))),
            new Change("roster entry unknown", ["roster-entry-unknown:client:e2e-invented"],
                () => fx.UpdateRosterRow("client", r => r["entries"] = ReleaseGateFixture.Plus(r["entries"], new JsonObject { ["id"] = "e2e-invented", ["outcome"] = "passed" }))),
            new Change("roster entry duplicate", ["roster-duplicate:client:lint"],
                () => fx.UpdateRosterRow("client", r => r["entries"] = ReleaseGateFixture.Plus(r["entries"], r["entries"]![0]))),
            new Change("roster entry skipped", ["roster-entry-not-passed:scripts:test-release-gate.ps1"],
                () => fx.UpdateRosterRow("scripts", r => r["entries"]![0]!["outcome"] = "skipped")),
            new Change("roster row missing", ["roster-missing:scripts"], () => fx.UpdateLedger(l => l["rosters"] = Without(l["rosters"], "suite", "scripts"))),
            new Change("publisher roster entry missing", ["roster-entry-missing:client:vitest"],
                () => fx.UpdateRosterRow("client", r => r["entries"] = Without(r["entries"], "id", "vitest")), Publish: true)));

        // The restored control still publishes, with suite counts recomputed from the ledger.
        fx.Restore(snap);
        var ok = fx.Publish();
        var manifest = TryRead(fx.ManifestPath);
        var antiphonRow = Objects(manifest?["suites"]).FirstOrDefault(row => Is(row["id"], "antiphon"));
        var release = fx.GitHub.Find(ok.Result.Tag);
        rows.Check(ok.Result.Published && release != null && !release.IsDraft && Num(antiphonRow?["chunks"]) == 3 && Num(antiphonRow?["required"]) == 4
                && Num(antiphonRow?["executed"]) == 4 && Num(antiphonRow?["passed"]) == 4,
            restoredRow, ok.Result.Refusal + " " + antiphonRow?.ToJsonString());
        rows.ShouldAllPass(10, LedgerControlRow, g302, g303, g304, g305, g387, g388, g389, rosterRow, restoredRow);
    }

    // ---- the prepublication receipt and the release card readback ---------------------------------------------------

    [Test]
    public void C599A_ExecutionLedgerReceipt()
    {
        // V-12 full matrix, third child of C599A_ExecutionLedger (D-14, G-306): the typed prepublication receipt replaces the
        // old boolean acceptance. Its kind, its recipient (the release card bound to the authority) and its correlation to
        // this intent/candidate/SHA/native run are each checked; the legacy flag confers nothing.
        const string controlRow = "C599 V-12: the valid control ledger with a correlated prepublication receipt passes";
        const string g306 = "C599 G-306: a final or foreign receipt kind where the prepublication receipt is required refuses";
        const string typeRow = "C599 V-12: a missing, boolean, string or array-wrapped receipt cannot publish and the legacy reportAccepted flag confers nothing";
        const string recipientRow = "C599 V-12: a receipt from any recipient but the release card bound to the authority refuses";
        const string correlationRow = "C599 V-12: a receipt not correlated to this intent, candidate, SHA and native run refuses";
        using var fx = ReleaseGateFixture.Create(ReleaseGateFixture.BasePolicyBytes());
        var rows = new NightlyCaseRows();
        var snap = fx.Save();
        var control = fx.Gate();
        rows.Check(control.Ok, controlRow, Reasons(control));

        // G-306 (PC-306): only the prepublication kind can mint publication authority.
        var m = new List<ReleaseGateCheck>();
        foreach (var v in new[]
                 {
                     V("final", "final"), V("publication", "publication"), V("case-changed", "Prepublication"),
                     V("array-wrapped", ReleaseGateFixture.Wrapped(JsonValue.Create("prepublication"))), V("number", 1), V("null", null), Absent(),
                 })
            m.Add(fx.Variant(snap, "receipt kind " + v.Label, ["receipt-kind"], () => fx.UpdateReceipt(r => v.Apply(r, "kind"))));
        m.AddRange(Checks(fx, snap, new Change("publisher final receipt", ["receipt-kind"], () => fx.UpdateReceipt(r => r["kind"] = "final"), Publish: true)));
        ReleaseGateFixture.Matrix(rows, g306, m);

        // Presence and type: a boolean, string or array is not a receipt.
        Action<JsonObject> legacy = l =>
        {
            l["reportAccepted"] = true;
            l.Remove("prepublicationReceipt");
        };
        m = Checks(fx, snap,
            new Change("legacy reportAccepted without receipt", ["receipt-missing"], () => fx.UpdateLedger(legacy)),
            new Change("receipt null", ["receipt-missing"], () => fx.UpdateLedger(l => l["prepublicationReceipt"] = null)));
        foreach (var v in new[] { V("boolean", true), V("string", "prepublication"), V("number", 4) })
            m.Add(fx.Variant(snap, "receipt " + v.Label, ["receipt-type"], () => fx.UpdateLedger(l => v.Apply(l, "prepublicationReceipt"))));
        m.AddRange(Checks(fx, snap,
            new Change("receipt array-wrapped", ["receipt-type"], () => fx.UpdateLedger(l => l["prepublicationReceipt"] = ReleaseGateFixture.Wrapped(l["prepublicationReceipt"]))),
            new Change("publisher legacy reportAccepted without receipt", ["receipt-missing"], () => fx.UpdateLedger(legacy), Publish: true)));
        ReleaseGateFixture.Matrix(rows, typeRow, m);

        // Recipient identity: exactly the release card bound to the authority.
        m = new List<ReleaseGateCheck>();
        foreach (var shape in new[]
                 {
                     new Shape("recipient absent", "receipt-recipient", r => r.Remove("recipient")),
                     new Shape("recipient string", "receipt-recipient", r => r["recipient"] = ReleaseGateFixture.ReleaseCardId),
                     new Shape("recipient master incident", "receipt-recipient", r => r["recipient"]!["kind"] = "master-incident"),
                     new Shape("recipient kind absent", "receipt-recipient", r => r["recipient"]!.AsObject().Remove("kind")),
                     new Shape("foreign card", "receipt-recipient", r => r["recipient"]!["cardId"] = ForeignCard),
                     new Shape("card array-wrapped", "receipt-recipient", r => r["recipient"]!["cardId"] = WrappedText(r["recipient"]!["cardId"])),
                     new Shape("card not a guid", "receipt-recipient", r => r["recipient"]!["cardId"] = "CARD-0599"),
                     new Shape("card absent", "receipt-recipient", r => r["recipient"]!.AsObject().Remove("cardId")),
                     new Shape("revision zero", "receipt-recipient", r => r["recipient"]!["revision"] = 0),
                     new Shape("revision string", "receipt-recipient", r => r["recipient"]!["revision"] = "4"),
                     new Shape("revision absent", "receipt-recipient", r => r["recipient"]!.AsObject().Remove("revision")),
                 })
            m.Add(fx.Variant(snap, shape.Label, [shape.Token], () => fx.UpdateReceipt(shape.Edit)));
        m.AddRange(Checks(fx, snap, new Change("publisher foreign card", ["receipt-recipient"],
            () => fx.UpdateReceipt(r => r["recipient"]!["cardId"] = ForeignCard), Publish: true)));
        ReleaseGateFixture.Matrix(rows, recipientRow, m);

        // Correlation: the same intent, candidate, full SHA and native run, accepted after the run ended and not after the
        // publisher's clock.
        m = new List<ReleaseGateCheck>();
        foreach (var shape in new[]
                 {
                     new Shape("correlation absent", "receipt-correlation", r => r.Remove("correlation")),
                     new Shape("foreign intent", "receipt-correlation", r => r["correlation"]!["intentId"] = "intent-foreign"),
                     new Shape("foreign candidate", "receipt-correlation", r => r["correlation"]!["candidateId"] = ForeignCandidate),
                     new Shape("foreign sha", "receipt-correlation", r => r["correlation"]!["sha"] = ReleaseGateFixture.ShaC),
                     new Shape("abbreviated sha", "receipt-correlation", r => r["correlation"]!["sha"] = S(r["correlation"]!["sha"])[..12]),
                     new Shape("foreign run", "receipt-correlation", r => r["correlation"]!["runId"] = "rc-run-foreign"),
                     new Shape("run array-wrapped", "receipt-correlation", r => r["correlation"]!["runId"] = WrappedText(r["correlation"]!["runId"])),
                     new Shape("run absent", "receipt-correlation", r => r["correlation"]!.AsObject().Remove("runId")),
                     new Shape("accepted before run end", "receipt-timestamps", r => r["acceptedAt"] = "2026-09-23T09:49:00Z"),
                     new Shape("accepted unparseable", "receipt-timestamps", r => r["acceptedAt"] = "soon"),
                     new Shape("accepted absent", "receipt-timestamps", r => r.Remove("acceptedAt")),
                     new Shape("accepted after the clock", "evidence-future:receipt", r => r["acceptedAt"] = "2026-09-23T10:30:00Z"),
                 })
            m.Add(fx.Variant(snap, shape.Label, [shape.Token], () => fx.UpdateReceipt(shape.Edit)));
        m.AddRange(Checks(fx, snap, new Change("publisher foreign run", ["receipt-correlation"],
            () => fx.UpdateReceipt(r => r["correlation"]!["runId"] = "rc-run-foreign"), Publish: true)));
        ReleaseGateFixture.Matrix(rows, correlationRow, m);
        rows.ShouldAllPass(5, controlRow, g306, typeRow, recipientRow, correlationRow);
    }

    [Test]
    public void C599A_ReceiptReadback()
    {
        // V-12 full matrix, D-14 readback (review bf928242 finding 1): publication reads the release card bound to the
        // authority back through the Antiphon API (GET /api/cards/{id}, read-only), requires the receipt's revision to equal
        // the card's CURRENT revision and the card body to carry this run's correlation line. An unavailable readback
        // refuses: publication fails closed. Producer-to-recipient delivery through the report queue is B5/B6 (the report
        // stage writes the receipt).
        const string controlRow = "C599 V-12: the valid control reads the bound release card back once through GET /api/cards/{id}";
        const string unavailableRow = "C599 V-12: an unavailable, failed, unknown or non-object release card readback refuses (fail closed)";
        const string revisionRow = "C599 V-12: a receipt revision other than the release card current revision refuses";
        const string bodyRow = "C599 V-12: a release card body without exactly this run correlation refuses";
        const string identityRow = "C599 V-12: a readback of any card but the bound release card refuses";
        const string restoredRow = "C599 V-12: the restored control publishes after only read-only GETs of the bound release card";
        using var fx = ReleaseGateFixture.Create(ReleaseGateFixture.BasePolicyBytes());
        var rows = new NightlyCaseRows();
        var snap = fx.Save();
        var expectedLine = fx.CardApi.ExpectedLine(ReleaseGateFixture.ReleaseCardId);
        fx.CardApi.ClearRequests();
        var control = fx.Gate();
        var requests = fx.CardApi.Requests;
        rows.Check(control.Ok && requests.Count == 1 && requests[0] == expectedLine, controlRow,
            Reasons(control) + " requests=[" + string.Join(" | ", requests) + "]");

        // Unavailable: any transport failure, non-200 answer or body that is not one JSON object refuses. Nothing falls back
        // to the ledger's own copy of the receipt.
        var apiUrl = fx.CardApi.Url;
        var closed = "http://127.0.0.1:" + ReleaseCardApi.ClosedPort() + "/fx/" + fx.RouteId;
        Action restoreUrl = () => fx.CardApi.Url = apiUrl;
        var m = new List<ReleaseGateCheck>();
        foreach (var (label, mutate) in new (string, Action)[]
                 {
                     ("api answers 500", () => fx.UpdateCard(store: s => s.Status = 500)),
                     ("api answers 404", () => fx.UpdateCard(store: s => s.Status = 404)),
                     ("api answers 401", () => fx.UpdateCard(store: s => s.Status = 401)),
                     ("card unknown to the api", () => fx.UpdateCard(store: s => s.Cards.Remove(ReleaseGateFixture.ReleaseCardId))),
                     ("body not json", () => fx.UpdateCard(store: s => s.Raw = "<html>maintenance</html>")),
                     ("body a json array", () => fx.UpdateCard(store: s => s.Raw = "[]")),
                     ("body a one-card json array", () => fx.UpdateCard(store: s => s.Raw = "[" + s.Cards[ReleaseGateFixture.ReleaseCardId].ToJsonString() + "]")),
                     ("body empty", () => fx.UpdateCard(store: s => s.Raw = "")),
                     ("api unreachable", () => fx.CardApi.Url = closed),
                     ("api url empty", () => fx.CardApi.Url = ""),
                     ("api url not http", () => fx.CardApi.Url = "file:///" + fx.PathOf("card-store.json").Replace('\\', '/')),
                 })
            m.Add(fx.Variant(snap, label, ["release-card-readback-unavailable"], mutate, cleanup: restoreUrl));
        m.AddRange(Checks(fx, snap,
            new Change("publisher api answers 503", ["release-card-readback-unavailable"], () => fx.UpdateCard(store: s => s.Status = 503), Publish: true),
            new Change("publisher api unreachable", ["release-card-readback-unavailable"], () => { }, Publish: true, Edit: o => o.AntiphonApiUrl = closed)));
        ReleaseGateFixture.Matrix(rows, unavailableRow, m);

        // Revision: the receipt must name the card's current revision, exactly.
        ReleaseGateFixture.Matrix(rows, revisionRow, Checks(fx, snap,
            new Change("card moved on after the receipt", ["release-card-readback-revision"], () => fx.UpdateCard(card: c => c["revisionCount"] = 5)),
            new Change("receipt names an earlier revision", ["release-card-readback-revision"], () => fx.UpdateReceipt(r => r["recipient"]!["revision"] = 3)),
            new Change("receipt names a later revision", ["release-card-readback-revision"], () => fx.UpdateReceipt(r => r["recipient"]!["revision"] = 9)),
            new Change("card revision string", ["release-card-readback-revision"], () => fx.UpdateCard(card: c => c["revisionCount"] = "4")),
            new Change("card revision array-wrapped", ["release-card-readback-revision"],
                () => fx.UpdateCard(card: c => c["revisionCount"] = ReleaseGateFixture.Wrapped(JsonValue.Create(4)))),
            new Change("card revision null", ["release-card-readback-revision"], () => fx.UpdateCard(card: c => c["revisionCount"] = null)),
            new Change("card revision absent", ["release-card-readback-revision"], () => fx.UpdateCard(card: c => c.Remove("revisionCount"))),
            new Change("publisher receipt names an earlier revision", ["release-card-readback-revision"],
                () => fx.UpdateReceipt(r => r["recipient"]!["revision"] = 3), Publish: true)));

        // Body: exactly one correlation line naming exactly this intent, candidate, full SHA and run.
        var head = fx.Head;
        var valid = ReleaseGateFixture.CardBody(head);
        var foreignLine = string.Join(" ", ReleaseGateFixture.CardBody(head, runId: "rc-run-foreign").Split('\n')
            .Where(line => line.StartsWith("release-correlation:", StringComparison.OrdinalIgnoreCase)));
        m = new List<ReleaseGateCheck>();
        foreach (var body in new[]
                 {
                     V("no correlation line", "Release candidate " + fx.CandidateId),
                     V("foreign intent", ReleaseGateFixture.CardBody(head, intentId: "intent-foreign")),
                     V("foreign candidate", ReleaseGateFixture.CardBody(head, candidate: ForeignCandidate)),
                     V("foreign sha", ReleaseGateFixture.CardBody(ReleaseGateFixture.ShaC)),
                     V("abbreviated sha", ReleaseGateFixture.CardBody(head[..12])),
                     V("foreign run", ReleaseGateFixture.CardBody(head, runId: "rc-run-foreign")),
                     V("run key missing", Regex.Replace(valid, @" runId=\S+", "", RegexOptions.IgnoreCase)),
                     V("key case changed", valid.Replace("runId=", "RunId=", StringComparison.Ordinal)),
                     V("extra key", Regex.Replace(valid, @"(runId=\S+)", "$1 note=x", RegexOptions.IgnoreCase)),
                     V("second foreign line", valid + "\n" + foreignLine),
                     V("description number", 7),
                     V("description null", null),
                 })
            m.Add(fx.Variant(snap, body.Label, ["release-card-readback-correlation"], () => fx.UpdateCard(card: c => body.Apply(c, "description"))));
        m.AddRange(Checks(fx, snap,
            new Change("description absent", ["release-card-readback-correlation"], () => fx.UpdateCard(card: c => c.Remove("description"))),
            new Change("publisher body names a foreign run", ["release-card-readback-correlation"],
                () => fx.UpdateCard(card: c => c["description"] = ReleaseGateFixture.CardBody(fx.Head, runId: "rc-run-foreign")), Publish: true)));
        ReleaseGateFixture.Matrix(rows, bodyRow, m);

        // Identity: the API must answer with the bound card itself.
        ReleaseGateFixture.Matrix(rows, identityRow, Checks(fx, snap,
            new Change("api serves a different card", ["release-card-readback-identity"], () => fx.UpdateCard(card: c => c["id"] = ForeignCard)),
            new Change("served card id not a guid", ["release-card-readback-identity"], () => fx.UpdateCard(card: c => c["id"] = "CARD-0601")),
            new Change("served card id absent", ["release-card-readback-identity"], () => fx.UpdateCard(card: c => c.Remove("id"))),
            new Change("publisher api serves a different card", ["release-card-readback-identity"], () => fx.UpdateCard(card: c => c["id"] = ForeignCard), Publish: true)));

        // The restored control publishes, and every request the API saw was a GET of the bound card.
        fx.Restore(snap);
        fx.CardApi.ClearRequests();
        var ok = fx.Publish();
        requests = fx.CardApi.Requests;
        var release = fx.GitHub.Find(ok.Result.Tag);
        var foreignRequests = requests.Where(line => line != expectedLine).ToList();
        rows.Check(ok.Result.Published && release != null && !release.IsDraft && requests.Count >= 1 && foreignRequests.Count == 0, restoredRow,
            ok.Result.Refusal + " requests=[" + string.Join(" | ", requests) + "]");
        rows.ShouldAllPass(6, controlRow, unavailableRow, revisionRow, bodyRow, identityRow, restoredRow);
    }

    // ---- seam admission -------------------------------------------------------------------------------------------

    [Test]
    public void C599A_PublisherTestMode()
    {
        // Review bf928242 finding 2: the clock seam (and every other publisher seam) is admitted only in explicit test mode
        // (ReleasePublishOptions.TestMode; the CLI sets it only for ANTIPHON_RELEASE_GATE_TEST_MODE=1 exactly), and the
        // publisher refuses test mode whenever a real remote would be reached (no seams, or seams without the Git or GitHub
        // adapter). A real publication can therefore never run on an injected clock.
        const string testModeRow = "C599 V-18: in test mode the fake remote and the injected clock publish the valid control";
        const string outsideRow = "C599 V-18: outside test mode a seams file is refused before it is loaded, the clock is never read and nothing remote is written";
        const string realRemoteRow = "C599 V-18: test mode refuses a real remote: no seams file, or one without the Git or GitHub adapter";
        using var fx = ReleaseGateFixture.Create(ReleaseGateFixture.BasePolicyBytes());
        var rows = new NightlyCaseRows();
        var snap = fx.Save();

        fx.ResetClockReads();
        var ok = fx.Publish();
        var release = fx.GitHub.Find(ok.Result.Tag);
        rows.Check(ok.Result.Published && release != null && !release.IsDraft && fx.ClockReads > 0, testModeRow, ok.Result.Refusal + " clockReads=" + fx.ClockReads);
        fx.Restore(snap);

        // Outside test mode (unset, or any value but exactly 1) the bound seams are refused before they are used: the clock
        // is never read and no fake or real remote is touched.
        var m = new List<ReleaseGateCheck>();
        foreach (var value in new[] { "", "0", "true", "TRUE", " 1", "1 ", "yes" })
            m.Add(TestModeVariant(fx, snap, "test mode [" + value + "]", ["seams-outside-test-mode"],
                () => fx.Publish(o => o.TestMode = ReleaseGateRemote.TestMode(value))));
        ReleaseGateFixture.Matrix(rows, outsideRow, m);

        // The clock seam's own refusal outside test mode is ReleaseGateLibraryTests.Clock_seam_answers_only_in_test_mode.

        // Test mode with a real remote refuses before any remote read or write. The checkout's origin points at a missing
        // local path for these variants, so even a missing guard could never reach a real remote.
        var originUrl = fx.RealGit("remote", "get-url", "origin");
        var noRemote = fx.PathOf("no-such-remote");
        Action isolate = () => fx.RealGit("remote", "set-url", "origin", noRemote);
        Action restoreOrigin = () => fx.RealGit("remote", "set-url", "origin", originUrl);
        var bound = fx.Seams();
        var gitOnly = new ReleaseGateSeams { Git = bound.Git, UtcNow = bound.UtcNow, JournalWrite = bound.JournalWrite };
        var gitHubOnly = new ReleaseGateSeams { GitHub = bound.GitHub, UtcNow = bound.UtcNow, JournalWrite = bound.JournalWrite };
        m = new List<ReleaseGateCheck>
        {
            TestModeVariant(fx, snap, "no seams file", ["test-mode-real-remote"], () => fx.Publish(noSeams: true), isolate, restoreOrigin),
            TestModeVariant(fx, snap, "seams without the GitHub adapter", ["test-mode-real-remote"], () => fx.Publish(seams: gitOnly), isolate, restoreOrigin),
            TestModeVariant(fx, snap, "seams without the Git adapter", ["test-mode-real-remote"], () => fx.Publish(seams: gitHubOnly), isolate, restoreOrigin),
        };
        ReleaseGateFixture.Matrix(rows, realRemoteRow, m);
        rows.ShouldAllPass(3, testModeRow, outsideRow, realRemoteRow);
    }

    // ---- case helpers -----------------------------------------------------------------------------------------------

    /// <summary>One mutation applied to a JSON object (a policy, authority, receipt, chunk or prerequisite row), with its token.</summary>
    private sealed record Shape(string Label, string Token, Action<JsonObject> Edit);

    /// <summary>One Invoke-C599AVariant call: mutate, the expected tokens, through the publisher (with edit) or the gate.</summary>
    private sealed record Change(string Label, string[] Expect, Action Mutate, bool Publish = false, Action<ReleasePublishOptions>? Edit = null);

    /// <summary>A value set on a field (Set-C599AField), or the field removed when Absent.</summary>
    private sealed record Val(string Label, JsonNode? Value, bool Absent = false)
    {
        public void Apply(JsonObject target, string name)
        {
            if (Absent) target.Remove(name);
            else target[name] = Value?.DeepClone();
        }
    }

    /// <summary>A whole-run marker of C599A_ExecutionLedgerMarkers (and its second field, green flag and publisher value).</summary>
    private sealed record Marker(string Row, string Field, string Token, Val[] Values, JsonNode PublishValue, string? Green = null, string? GreenToken = null,
        string? Field2 = null, string? Token2 = null);

    private sealed record MarkerField(string Field, string Token, Val[] Values);

    /// <summary>One frozen authority field of the field matrix: its tokens, a wrong-typed value and a foreign value (or operation).</summary>
    private sealed record Field(string Name, string[] Tokens, JsonNode WrongType, JsonNode? Foreign, string? ForeignOp = null, string[]? ForeignTokens = null);

    private static Val V(string label, JsonNode? value) => new(label, value);

    private static Val Absent() => new("absent", null, true);

    private static string S(JsonNode? node) => ReleaseGateFixture.Str(node);

    /// <summary>PowerShell's -eq on [string] values: case-insensitive.</summary>
    private static bool Is(JsonNode? node, string text) => string.Equals(S(node), text, StringComparison.OrdinalIgnoreCase);

    private static string Reasons(ReleaseGateAnswer answer) => string.Join(" ", answer.Reasons);

    private static JsonArray Strings(IEnumerable<string> items) => new(items.Select(item => (JsonNode?)JsonValue.Create(item)).ToArray());

    /// <summary>The array's objects (live, not cloned).</summary>
    private static IEnumerable<JsonObject> Objects(JsonNode? array) => (array as JsonArray ?? []).OfType<JsonObject>();

    /// <summary>@($array | Where-Object { [string]$_.key -ne value }).</summary>
    private static JsonArray Without(JsonNode? array, string key, string value) => ReleaseGateFixture.Where(array, n => !Is(n?[key], value));

    /// <summary>@($array | Where-Object { [string]$_.key -eq value }).</summary>
    private static JsonArray Only(JsonNode? array, string key, string value) => ReleaseGateFixture.Where(array, n => Is(n?[key], value));

    /// <summary>foreach ($r in @($array)) { if ([string]$r.key -eq value) { edit $r } }.</summary>
    private static void Each(JsonNode? array, string key, string value, Action<JsonObject> edit)
    {
        foreach (var row in Objects(array).ToList())
            if (Is(row[key], value)) edit(row);
    }

    /// <summary>New-C599AWrapped -Value ([string]$node).</summary>
    private static JsonArray WrappedText(JsonNode? node) => ReleaseGateFixture.Wrapped(JsonValue.Create(S(node)));

    private static JsonObject? TryRead(string path) => File.Exists(path) ? ReleaseGateFixture.ReadObject(path) : null;

    private static long? Num(JsonNode? node) => node is JsonValue value && value.TryGetValue<long>(out var number) ? number : null;

    /// <summary>Each change through Invoke-C599AVariant, in order.</summary>
    private static List<ReleaseGateCheck> Checks(ReleaseGateFixture fx, ReleaseGateSnapshot snap, params Change[] changes) =>
        changes.Select(c => fx.Variant(snap, c.Label, c.Expect, c.Mutate, publish: c.Publish, edit: c.Edit)).ToList();

    /// <summary>The policy edit removing one suite from the rc profile's requiredSuites.</summary>
    private static Action<JsonObject> RcWithout(string suite) =>
        o => o["profiles"]!["rc"]!["requiredSuites"] = ReleaseGateFixture.Where(o["profiles"]!["rc"]!["requiredSuites"], n => !Is(n, suite));

    /// <summary>The ledger control row shared by the four execution-ledger cases; returns the frozen plan's native chunks.</summary>
    private static IReadOnlyList<(string Suite, string Id, string[] Uids)> LedgerControl(ReleaseGateFixture fx, NightlyCaseRows rows)
    {
        var chunks = fx.NativeChunks();
        var uids = chunks.Sum(c => c.Uids.Length);
        var control = fx.Gate();
        rows.Check(control.Ok && chunks.Count == 13 && uids == 19, LedgerControlRow, Reasons(control) + " chunks=" + chunks.Count + " uids=" + uids);
        return chunks;
    }

    /// <summary>A ledger chunk row copied under the unknown id antiphon-099.</summary>
    private static void AddExtraChunk(JsonObject ledger)
    {
        var extra = ledger["chunks"]![0]!.DeepClone();
        extra["chunk"] = "antiphon-099";
        ledger["chunks"] = ReleaseGateFixture.Plus(ledger["chunks"], extra);
    }

    /// <summary>G-293: discovery edited, the suite's plan chunks emptied and its ledger chunk rows removed, every digest re-bound.</summary>
    private static void EmptySuite(ReleaseGateFixture fx, string suite, Action<JsonObject> discovery) =>
        fx.UpdateDiscovery(discovery,
            plan: p => Each(p["suites"], "id", suite, s => s["chunks"] = new JsonArray()),
            ledger: l => l["chunks"] = Without(l["chunks"], "suite", suite));

    // ---- the authority field matrix ---------------------------------------------------------------------------------

    /// <summary>
    /// Invoke-C599AFieldMatrix: the authority field children of C599A_PinnedPolicy. Every frozen authority field absent,
    /// null, mistyped, wrapped in a one-element array (a JSON array is never the scalar it contains) and foreign, each
    /// re-bound so it alone differs; skip/take select the child's fields.
    /// </summary>
    private static void FieldMatrix(int skip, int take, string matrixName)
    {
        using var fx = ReleaseGateFixture.Create(ReleaseGateFixture.BasePolicyBytes());
        var rows = new NightlyCaseRows();
        var snap = fx.Save();
        var control = fx.Gate();
        rows.Check(control.Ok, FieldControlRow, Reasons(control));
        var m = new List<ReleaseGateCheck>();
        foreach (var field in AuthorityFields().Skip(skip).Take(take))
            foreach (var kind in new[] { "absent", "null", "type", "array", "foreign" })
            {
                var expect = kind == "foreign" && field.ForeignTokens != null ? field.ForeignTokens : field.Tokens;
                m.Add(fx.Variant(snap, field.Name + " " + kind, expect, () => fx.UpdateAuthority(a => ApplyField(a, field, kind))));
            }
        ReleaseGateFixture.Matrix(rows, matrixName, m);
        rows.ShouldAllPass(2, FieldControlRow, matrixName);
    }

    private static Field[] AuthorityFields() => new[]
    {
        new Field("schemaVersion", ["authority-schema"], "1", 2),
        new Field("kind", ["authority-kind"], 7, "release-candidate"),
        new Field("repository", ["authority-repository"], 7, "someone-else/Antiphon"),
        new Field("profile", ["authority-profile"], 7, "nightly"),
        new Field("coordinatorVersion", ["authority-coordinator-version"], 1, "c599-other"),
        new Field("sha", ["authority-sha"], 7, ReleaseGateFixture.ShaC),
        new Field("candidateId", ["authority-candidate"], 7, ForeignCandidate),
        new Field("candidateRef", ["authority-candidate-ref"], 7, ForeignCandidateRef),
        new Field("intentId", ["authority-intent"], 7, "intent-foreign", ForeignTokens: ["plan-intent", "ledger-intent"]),
        new Field("releaseCardId", ["authority-release-card"], 7, ForeignCard, ForeignTokens: ["receipt-recipient"]),
        new Field("policyPath", ["authority-policy-path"], 7, "tests/other-policy.json"),
        new Field("policyBlobId", ["policy-blob-id-mismatch"], 7, new string('1', 40)),
        new Field("policyRawSha256", ["policy-raw-digest-mismatch"], 7, Zeros),
        new Field("policyHash", ["policy-hash-mismatch"], 7, Zeros),
        new Field("requiredSuites", ["authority-suites-type", "authority-suites-mismatch"], 8, Strings(Eight.Where(x => x != "e2e")),
            ForeignTokens: ["authority-suites-mismatch"]),
        new Field("exclusions", ["authority-exclusions-type", "authority-exclusions-mismatch"], "none", null, ForeignOp: "drop-first",
            ForeignTokens: ["authority-exclusions-mismatch"]),
        new Field("scriptHashes", ["script-digest-mismatch:lib/Antiphon.Nightly"], "x", null, ForeignOp: "one-changed"),
        new Field("createdAt", ["ledger-timestamps"], true, "2026-09-23T09:00:00Z"),
    };

    private static void ApplyField(JsonObject authority, Field field, string kind)
    {
        switch (kind)
        {
            case "absent":
                authority.Remove(field.Name);
                break;
            case "null":
                authority[field.Name] = null;
                break;
            case "type":
                authority[field.Name] = field.WrongType.DeepClone();
                break;
            case "array":
                authority[field.Name] = ReleaseGateFixture.Wrapped(authority[field.Name]);
                break;
            case "foreign" when field.ForeignOp == "drop-first":
                authority["exclusions"] = new JsonArray(authority["exclusions"]!.AsArray().Skip(1).Select(n => n?.DeepClone()).ToArray());
                break;
            case "foreign" when field.ForeignOp == "one-changed":
                authority["scriptHashes"]!["lib/Antiphon.Nightly"] = Zeros;
                break;
            case "foreign":
                authority[field.Name] = field.Foreign!.DeepClone();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(kind), kind, "unknown field-matrix kind");
        }
    }

    // ---- test mode --------------------------------------------------------------------------------------------------

    /// <summary>
    /// Invoke-C599ATestModeVariant: one publisher run from the restored control under a changed seam admission. Passes only
    /// when it is refused with every expected token, wrote nothing remote, never read the injected clock and never called the
    /// fake git remote (or the fake GitHub).
    /// </summary>
    private static ReleaseGateCheck TestModeVariant(ReleaseGateFixture fx, ReleaseGateSnapshot snap, string label, string[] expect,
        Func<ReleaseGateRun<ReleasePublishResult>> run, Action? before = null, Action? after = null)
    {
        fx.Restore(snap);
        fx.ResetClockReads();
        var gitCalls = fx.Remote.Trace.Count;
        var gitHubCalls = fx.GitHub.Trace.Count;
        string[] got = [];
        var refused = false;
        var touched = false;
        var error = "";
        try
        {
            before?.Invoke();
            var result = run().Result;
            got = result.Refusal.Split(',', StringSplitOptions.RemoveEmptyEntries);
            refused = !result.Published && result.ExitCode != 0 && fx.NoRemoteWrite();
            touched = fx.ClockReads > 0 || fx.Remote.Trace.Count > gitCalls || fx.GitHub.Trace.Count > gitHubCalls;
        }
        catch (Exception e)
        {
            error = "threw: " + e.Message;
        }
        finally
        {
            try { after?.Invoke(); }
            finally { fx.Restore(snap); }
        }
        var missing = expect.Where(token => !got.Contains(token, StringComparer.OrdinalIgnoreCase)).ToArray();
        return new ReleaseGateCheck(refused && !touched && missing.Length == 0 && error.Length == 0,
            label + ": refused=" + refused + " seams-used=" + touched + " missing=[" + string.Join(" ", missing) + "] got=[" + string.Join(" ", got) + "] " + error);
    }

    // ---- evidence links and unreadable directories ----------------------------------------------------------------

    /// <summary>Remove-C599AEvidenceLink: the link itself, never its target.</summary>
    private static void RemoveLink(string link)
    {
        if (Directory.Exists(link) || File.Exists(link)) Directory.Delete(link, false);
    }

    /// <summary>Get-C599AAttributeDenial: true only when the attribute read fails for a reason other than not-found.</summary>
    private static (bool Denied, string Detail) AttributeDenial(string path)
    {
        try
        {
            return (false, File.GetAttributes(path).ToString());
        }
        catch (Exception e)
        {
            var inner = e;
            while (inner.InnerException != null) inner = inner.InnerException;
            return (inner is not (FileNotFoundException or DirectoryNotFoundException), inner.GetType().FullName + " " + inner.Message);
        }
    }

    /// <summary>
    /// Lock-C599AUnreadableDirectory: hide a directory from attribute reads (Windows: a deny ACE for the current user; elsewhere
    /// mode 000). Returns the action that restores the previous access. The caller must see the denial before asserting.
    /// </summary>
    private static Action LockUnreadableDirectory(string directory)
    {
        if (OperatingSystem.IsWindows()) return DenyCurrentUser(directory);
        return ClearUnixMode(directory);
    }

    [SupportedOSPlatform("windows")]
    private static Action DenyCurrentUser(string directory)
    {
        var info = new DirectoryInfo(directory);
        var acl = info.GetAccessControl(AccessControlSections.Access);
        var saved = acl.GetSecurityDescriptorSddlForm(AccessControlSections.Access);
        using var identity = WindowsIdentity.GetCurrent();
        var rights = FileSystemRights.ListDirectory | FileSystemRights.ReadData | FileSystemRights.ReadAttributes | FileSystemRights.ReadExtendedAttributes
            | FileSystemRights.Traverse | FileSystemRights.ReadPermissions;
        acl.AddAccessRule(new FileSystemAccessRule(identity.User!, rights, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None, AccessControlType.Deny));
        info.SetAccessControl(acl);
        return () =>
        {
            var restore = new DirectorySecurity();
            restore.SetSecurityDescriptorSddlForm(saved, AccessControlSections.Access);
            new DirectoryInfo(directory).SetAccessControl(restore);
        };
    }

    [UnsupportedOSPlatform("windows")]
    private static Action ClearUnixMode(string directory)
    {
        var prior = File.GetUnixFileMode(directory);
        File.SetUnixFileMode(directory, UnixFileMode.None);
        return () => File.SetUnixFileMode(directory, prior);
    }
}
