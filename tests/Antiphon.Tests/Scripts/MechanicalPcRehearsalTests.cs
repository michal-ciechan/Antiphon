using System.Text;
using System.Text.RegularExpressions;
using Antiphon.Nightly;
using Antiphon.Tests.Scripts.Nightly;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-0479 V-9..V-12 and R-9: the mechanical-pc command (Antiphon.Nightly.MechanicalPc, the checks of
/// docs/mechanical-pc-contract.md) rehearsed on disposable Git fixtures, in process. Canned CHECKPOINT/EXECUTED/FAILED text
/// stands in for dotnet. Until unit 33 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md the checks were
/// fenced PowerShell snippets and scripts/test-mechanical-pc-contract.ps1 extracted and ran them; the rows keep its names.
/// </summary>
[Category("Integration")]
[ParallelLimiter<GitProcessSpawnLimit>]
public sealed class MechanicalPcRehearsalTests
{
    private static readonly UTF8Encoding Utf8 = new(false);

    private static readonly (string Path, string Text)[] StandardFiles =
    [
        ("src/a.txt", "alpha BEFORE omega\n"),
        ("src/b.txt", "bravo\n"),
        ("fixtures/config.txt", "config-v1\n"),
        ("backend/identity.txt", "backend-v1\n"),
    ];

    private static readonly string[] Members = ["src/a.txt", "src/b.txt", "fixtures/config.txt", "backend/identity.txt"];
    private static readonly string[] ExactExecuted = ["Example.Negative", "Example.CompanionOwner", "Example.CompanionPrereq"];
    private static readonly string[] ExactFailed = ["Example.Negative"];

    [Test]
    public void C479_V09a_BindingSource() => Case("binding-source", 5, w =>
    {
        var snap = w.NewSnapshot();
        var pack = w.NewPack("preflight-ok");
        w.WriteIndex(pack, snap.Root, Members);
        var seal = w.Seal(snap.Root, Members);
        var ok = w.Run("preflight", "--snapshot-root", snap.Root, "--pack-root", pack, "--l", snap.Head);
        w.Check(Accepted(ok) && w.SealHolds(seal, snap.Root), "binding-source:head-equals-l-accepts", ok.Text);

        var badHead = w.Run("preflight", "--snapshot-root", snap.Root, "--pack-root", pack, "--l", new string('0', 40));
        w.Check(Refused(badHead, "source-mismatch") && w.SealHolds(seal, snap.Root), "binding-source:head-mismatch-refuses", badHead.Text);

        var remote = w.NewSnapshot();
        Git(remote.Root, "checkout", "-q", "-b", "remote-tip");
        Git(remote.Root, "commit", "-q", "--allow-empty", "-m", "R");
        var tip = Git(remote.Root, "rev-parse", "HEAD");
        Git(remote.Root, "checkout", "-q", "master");
        Git(remote.Root, "update-ref", "refs/remotes/origin/master", tip);
        var remotePack = w.NewPack("preflight-remote");
        w.WriteIndex(remotePack, remote.Root, Members);
        var remoteSeal = w.Seal(remote.Root, Members);
        var advanced = w.Run("preflight", "--snapshot-root", remote.Root, "--pack-root", remotePack, "--l", remote.Head);
        var stillL = Git(remote.Root, "rev-parse", "HEAD") == remote.Head;
        w.Check(Accepted(advanced) && stillL && w.SealHolds(remoteSeal, remote.Root) && tip != remote.Head, "binding-source:remote-advanced-accepts", advanced.Text);

        var missing = w.NewSnapshot();
        var missingPack = w.NewPack("preflight-missing");
        w.WriteIndex(missingPack, missing.Root, Members);
        File.Delete(Path.Combine(missingPack, "index.sha256"));
        var missingSeal = w.Seal(missing.Root, Members);
        var noDigest = w.Run("preflight", "--snapshot-root", missing.Root, "--pack-root", missingPack, "--l", missing.Head);
        w.Check(Refused(noDigest, "binding-missing") && w.SealHolds(missingSeal, missing.Root), "binding-source:missing-digest-refuses", noDigest.Text);

        var drift = w.NewSnapshot();
        var driftPack = w.NewPack("preflight-member");
        w.WriteIndex(driftPack, drift.Root, Members);
        WriteUtf8(Full(drift.Root, "src/a.txt"), "member drift\n");
        var after = Sha(Full(drift.Root, "src/a.txt"));
        var member = w.Run("preflight", "--snapshot-root", drift.Root, "--pack-root", driftPack, "--l", drift.Head);
        w.Check(Refused(member, "artifact-mismatch") && Sha(Full(drift.Root, "src/a.txt")) == after, "binding-source:member-hash-mismatch-refuses", member.Text);
    });

    [Test]
    public void C479_V09b_BindingPreimage() => Case("binding-preimage", 3, w =>
    {
        var crlf = w.NewSnapshot();
        var live = Full(crlf.Root, "src/a.txt");
        var lfHash = Sha(live);
        WriteUtf8(live, "alpha BEFORE omega\r\n");
        var crlfHash = Sha(live);
        var crlfPack = w.NewPack("crlf");
        w.WriteApply(crlfPack, ["src/a.txt"], [Closure(crlf.Root, "fixtures/config.txt")],
            [new Edit("src/a.txt", lfHash, w.ReplacedHash(live, "BEFORE", "AFTER"), "BEFORE", "AFTER", 1)]);
        var crlfResult = w.Run("apply", "--snapshot-root", crlf.Root, "--pack-root", crlfPack);
        w.Check(Refused(crlfResult, "patch-mismatch") && Sha(live) == crlfHash, "binding-preimage:crlf-refuses", crlfResult.Text);

        var candidate = w.NewSnapshot();
        var candidateLive = Full(candidate.Root, "src/a.txt");
        var candidateSeal = Sha(candidateLive);
        var cBytes = Path.Combine(w.NewWork("c-bytes"), "a.txt");
        WriteUtf8(cBytes, "candidate BEFORE omega\n");
        var candidatePack = w.NewPack("candidate");
        w.WriteApply(candidatePack, ["src/a.txt"], [Closure(candidate.Root, "fixtures/config.txt")],
            [new Edit("src/a.txt", Sha(cBytes), w.ReplacedHash(candidateLive, "BEFORE", "AFTER"), "BEFORE", "AFTER", 1)]);
        var candidateResult = w.Run("apply", "--snapshot-root", candidate.Root, "--pack-root", candidatePack);
        w.Check(Refused(candidateResult, "patch-mismatch") && Sha(candidateLive) == candidateSeal, "binding-preimage:candidate-at-c-refuses", candidateResult.Text);

        var backend = w.NewSnapshot();
        var backendSeal = w.Seal(backend.Root, ["src/a.txt", "backend/identity.txt"]);
        var backendPack = w.NewPack("backend");
        var backendA = Full(backend.Root, "src/a.txt");
        w.WriteApply(backendPack, ["src/a.txt"], [new ClosureItem("backend/identity.txt", new string('0', 64))],
            [new Edit("src/a.txt", Sha(backendA), w.ReplacedHash(backendA, "BEFORE", "AFTER"), "BEFORE", "AFTER", 1)]);
        var backendResult = w.Run("apply", "--snapshot-root", backend.Root, "--pack-root", backendPack);
        w.Check(Refused(backendResult, "artifact-mismatch") && w.SealHolds(backendSeal, backend.Root), "binding-preimage:backend-drift-refuses", backendResult.Text);
    });

    [Test]
    public void C479_V10a_ApplyExact() => Case("apply-exact", 3, w =>
    {
        var snap = w.NewSnapshot();
        var full = Full(snap.Root, "src/a.txt");
        var before = Sha(full);
        var mutant = w.ReplacedHash(full, "BEFORE", "AFTER");
        var others = w.Seal(snap.Root, ["src/b.txt", "fixtures/config.txt", "backend/identity.txt"]);
        var pack = w.NewPack("exact");
        w.WriteApply(pack, ["src/a.txt"], [Closure(snap.Root, "fixtures/config.txt")], [new Edit("src/a.txt", before, mutant, "BEFORE", "AFTER", 1)]);
        var first = w.Run("apply", "--snapshot-root", snap.Root, "--pack-root", pack);
        var second = w.Run("apply", "--snapshot-root", snap.Root, "--pack-root", pack);
        w.Check(Accepted(first) && Refused(second, "patch-mismatch") && Sha(full) == mutant, "apply-exact:applies-once-accepts", first.Text + " / " + second.Text);
        w.Check(Sha(full) == mutant, "apply-exact:mutant-sha256-matches", Sha(full));
        w.Check(w.SealHolds(others, snap.Root) && Sha(full) != before, "apply-exact:changed-file-set-matches", first.Text);
    });

    [Test]
    public void C479_V10b_ApplyContext() => Case("apply-context", 2, w =>
    {
        var dup = w.NewSnapshot();
        var dupFull = Full(dup.Root, "src/a.txt");
        WriteUtf8(dupFull, "BEFORE and BEFORE\n");
        var dupBefore = Sha(dupFull);
        var dupPack = w.NewPack("dup");
        w.WriteApply(dupPack, ["src/a.txt"], [Closure(dup.Root, "fixtures/config.txt")],
            [new Edit("src/a.txt", dupBefore, w.ReplacedHash(dupFull, "BEFORE", "AFTER"), "BEFORE", "AFTER", 1)]);
        var dupResult = w.Run("apply", "--snapshot-root", dup.Root, "--pack-root", dupPack);
        w.Check(Refused(dupResult, "patch-mismatch") && Sha(dupFull) == dupBefore, "apply-context:duplicate-refuses-no-write", dupResult.Text);

        var absent = w.NewSnapshot();
        var absentFull = Full(absent.Root, "src/a.txt");
        WriteUtf8(absentFull, "nothing here\n");
        var absentBefore = Sha(absentFull);
        var absentPack = w.NewPack("absent");
        w.WriteApply(absentPack, ["src/a.txt"], [Closure(absent.Root, "fixtures/config.txt")],
            [new Edit("src/a.txt", absentBefore, w.ReplacedHash(absentFull, "BEFORE", "AFTER"), "BEFORE", "AFTER", 1)]);
        var absentResult = w.Run("apply", "--snapshot-root", absent.Root, "--pack-root", absentPack);
        w.Check(Refused(absentResult, "patch-mismatch") && Sha(absentFull) == absentBefore, "apply-context:absent-refuses-no-write", absentResult.Text);
    });

    [Test]
    public void C479_V10c_ApplyClosure() => Case("apply-closure", 3, w =>
    {
        var extra = w.NewSnapshot();
        var a = Full(extra.Root, "src/a.txt");
        var b = Full(extra.Root, "src/b.txt");
        var aBefore = Sha(a);
        var bBefore = Sha(b);
        var extraPack = w.NewPack("extra");
        w.WriteApply(extraPack, ["src/a.txt"], [Closure(extra.Root, "fixtures/config.txt")],
        [
            new Edit("src/a.txt", aBefore, w.ReplacedHash(a, "BEFORE", "AFTER"), "BEFORE", "AFTER", 1),
            new Edit("src/b.txt", bBefore, w.ReplacedHash(b, "bravo", "BRAVO"), "bravo", "BRAVO", 1),
        ]);
        var extraResult = w.Run("apply", "--snapshot-root", extra.Root, "--pack-root", extraPack);
        w.Check(Refused(extraResult, "patch-mismatch") && Sha(a) == aBefore && Sha(b) == bBefore, "apply-closure:extra-file-restores-and-refuses", extraResult.Text);

        var partial = w.NewSnapshot();
        var pa = Full(partial.Root, "src/a.txt");
        var pb = Full(partial.Root, "src/b.txt");
        var paBefore = Sha(pa);
        var pbBefore = Sha(pb);
        var partialPack = w.NewPack("partial");
        w.WriteApply(partialPack, ["src/a.txt", "src/b.txt"], [Closure(partial.Root, "fixtures/config.txt")],
        [
            new Edit("src/a.txt", paBefore, w.ReplacedHash(pa, "BEFORE", "AFTER"), "BEFORE", "AFTER", 1),
            new Edit("src/b.txt", new string('0', 64), w.ReplacedHash(pb, "bravo", "BRAVO"), "bravo", "BRAVO", 1),
        ]);
        var partialResult = w.Run("apply", "--snapshot-root", partial.Root, "--pack-root", partialPack);
        w.Check(Refused(partialResult, "patch-mismatch") && Sha(pa) == paBefore && Sha(pb) == pbBefore, "apply-closure:partial-second-preimage-restores-first", partialResult.Text);

        var fixture = w.NewSnapshot();
        var fixtureSeal = w.Seal(fixture.Root, ["src/a.txt", "fixtures/config.txt"]);
        var fixturePack = w.NewPack("fixture");
        var fixtureA = Full(fixture.Root, "src/a.txt");
        w.WriteApply(fixturePack, ["src/a.txt"], [new ClosureItem("fixtures/config.txt", new string('1', 64))],
            [new Edit("src/a.txt", Sha(fixtureA), w.ReplacedHash(fixtureA, "BEFORE", "AFTER"), "BEFORE", "AFTER", 1)]);
        var fixtureResult = w.Run("apply", "--snapshot-root", fixture.Root, "--pack-root", fixturePack);
        w.Check(Refused(fixtureResult, "artifact-mismatch") && w.SealHolds(fixtureSeal, fixture.Root), "apply-closure:fixture-hash-refuses-before-write", fixtureResult.Text);
    });

    [Test]
    public void C479_V11a_OracleRows() => Case("oracle-rows", 9, w =>
    {
        var exact = w.Oracle(ExactDriver(), ExactExecuted, ExactFailed);
        w.Check(Accepted(exact), "oracle-rows:exact-negative-and-companions-accept", exact.Text);

        var extra = w.Oracle(ExactDriver(failed: ["Example.Negative", "Example.CompanionOwner"]), ExactExecuted, ExactFailed);
        w.Check(Refused(extra, "unexpected-red"), "oracle-rows:extra-failure-rejects", extra.Text);

        var zero = w.Oracle(ExactDriver(exitCode: 0, executed: [], failed: []), ExactExecuted, []);
        w.Check(Refused(zero, "selector-drift"), "oracle-rows:zero-rows-reject", zero.Text);

        var missing = w.Oracle(ExactDriver(executed: ["Example.Negative", "Example.CompanionOwner"], failed: ExactFailed), ExactExecuted, ExactFailed);
        w.Check(Refused(missing, "selector-drift"), "oracle-rows:missing-row-rejects", missing.Text);

        var extraRow = w.Oracle(ExactDriver(executed: ["Example.Negative", "Example.CompanionOwner", "Example.CompanionPrereq", "Example.Extra"]), ExactExecuted, ExactFailed);
        w.Check(Refused(extraRow, "selector-drift"), "oracle-rows:extra-row-rejects", extraRow.Text);

        var dup = w.Oracle(ExactDriver(executed: ["Example.Negative", "Example.Negative", "Example.CompanionOwner"]), ["Example.Negative", "Example.CompanionOwner"], ExactFailed);
        w.Check(Refused(dup, "selector-drift"), "oracle-rows:duplicate-row-rejects", dup.Text);

        var skipped = w.Oracle(ExactDriver(skipped: 1), ExactExecuted, ExactFailed);
        w.Check(Refused(skipped, "selector-drift"), "oracle-rows:skipped-rejects", skipped.Text);

        var exit2 = w.Oracle(ExactDriver(exitCode: 2), ExactExecuted, ExactFailed);
        w.Check(Refused(exit2, "prerequisite-missing"), "oracle-rows:driver-exit-2-rejects", exit2.Text);

        var exit3 = w.Oracle(ExactDriver(exitCode: 3), ExactExecuted, ExactFailed);
        w.Check(Refused(exit3, "selector-unresolved"), "oracle-rows:driver-exit-3-rejects", exit3.Text);
    });

    [Test]
    public void C479_V11b_OracleProvenance() => Case("oracle-provenance", 3, w =>
    {
        var pre = w.Oracle(ExactDriver(), ExactExecuted, ExactFailed, preexisted: true);
        w.Check(Refused(pre, "stale-output"), "oracle-provenance:preexisting-results-reject", pre.Text);

        var unknown = w.Oracle(ExactDriver(buildSource: "unknown"), ExactExecuted, ExactFailed);
        w.Check(Refused(unknown, "stale-output"), "oracle-provenance:unknown-build-source-rejects", unknown.Text);

        var same = w.Oracle(ExactDriver(), ExactExecuted, ExactFailed, sameOutput: true);
        w.Check(Refused(same, "stale-output"), "oracle-provenance:same-output-identity-rejects", same.Text);
    });

    [Test]
    public void C479_V12a_RestoreInterrupt() => Case("restore-interrupt", 3, w =>
    {
        var afterApply = w.RestoreCase();
        w.Check(Accepted(afterApply.Result) && Sha(Full(afterApply.Root, "src/a.txt")) == afterApply.PreimageA,
            "restore-interrupt:after-apply-restores-preimage", afterApply.Result.Text);

        var afterRed = w.RestoreCase(redNote: true);
        w.Check(Accepted(afterRed.Result) && Sha(Full(afterRed.Root, "src/a.txt")) == afterRed.PreimageA,
            "restore-interrupt:after-red-restores-preimage", afterRed.Result.Text);

        var noMarker = w.RestoreCase(phase: "restored");
        var withMarker = w.RestoreCase(phase: "restored", marker: true);
        w.Check(Refused(noMarker.Result, "stale-output") && Accepted(withMarker.Result) && Sha(Full(withMarker.Root, "src/a.txt")) == withMarker.PreimageA,
            "restore-interrupt:restored-requires-forced-rebuild", noMarker.Result.Text + " / " + withMarker.Result.Text);
    });

    [Test]
    public void C479_V12b_RestoreForeignEdit() => Case("restore-foreign-edit", 2, w =>
    {
        var snap = w.NewSnapshot();
        var evidence = w.NewEvidence();
        var a = Full(snap.Root, "src/a.txt");
        var b = Full(snap.Root, "src/b.txt");
        var originalA = Path.Combine(w.NewWork("pre"), "a.txt");
        var originalB = Path.Combine(w.NewWork("pre"), "b.txt");
        File.Copy(a, originalA, true);
        File.Copy(b, originalB, true);
        var stagedA = Path.Combine(w.NewWork("mut"), "a.txt");
        File.WriteAllText(stagedA, File.ReadAllText(a).Replace("BEFORE", "AFTER", StringComparison.Ordinal));
        var recordedMutantA = Sha(stagedA);
        WriteUtf8(a, "FOREIGN EDIT\n");
        var foreign = Sha(a);
        var mutantB = w.MutantFile(b, "bravo", "BRAVO");
        CopyBackup(evidence, "src/a.txt", originalA);
        CopyBackup(evidence, "src/b.txt", originalB);
        var pack = w.NewPack("foreign");
        WriteRestore(pack, [new RestoreFile("src/a.txt", Sha(originalA), recordedMutantA), new RestoreFile("src/b.txt", Sha(originalB), mutantB)]);
        var result = w.Run("restore", "--snapshot-root", snap.Root, "--evidence-root", evidence, "--pack-root", pack, "--phase", "restore");
        w.Check(Refused(result, "restore-mismatch") && result.Text.Contains("path=src/a.txt", StringComparison.Ordinal) && Sha(a) == foreign,
            "restore-foreign-edit:path-retained-and-reported", result.Text);
        w.Check(Sha(b) == mutantB, "restore-foreign-edit:sibling-retained", Sha(b));
    });

    [Test]
    public void C479_V12c_InstantiateTokens() => Case("instantiate-tokens", 5, w =>
    {
        var snap = w.NewSnapshot();
        var unknownEvidence = w.NewEvidence();
        var unknown = w.Instantiate(w.NewTemplate("root={snapshotRoot}\nextra={notAToken}\n"), unknownEvidence, snap.Root);
        w.Check(Refused(unknown, "unknown-token") && !Directory.Exists(Path.Combine(unknownEvidence, "PC1")), "instantiate-tokens:unknown-token-refuses", unknown.Text);

        var missing = w.Run("instantiate", "--snapshot-root", snap.Root, "--evidence-root", w.NewEvidence(), "--template-path", w.NewTemplate("task={taskId}\nroot={snapshotRoot}\n"),
            "--run-id", "run-a", "--pc-id", "PC1", "--phase", "baseline", "--results-relative", "out");
        w.Check(Refused(missing, "missing-value"), "instantiate-tokens:missing-value-refuses", missing.Text);

        var emptyEvidence = w.NewEvidence();
        var empty = w.Instantiate(w.NewTemplate("root={snapshotRoot}\n"), emptyEvidence, snap.Root, resultsRelative: "");
        w.Check(Refused(empty, "empty-results") && !Directory.Exists(Path.Combine(emptyEvidence, "PC1")), "instantiate-tokens:empty-results-refuses", empty.Text);

        var pathEvidence = w.NewEvidence();
        var escaped = w.Instantiate(w.NewTemplate("root={snapshotRoot}\n"), pathEvidence, snap.Root, resultsRelative: "../out");
        w.Check(Refused(escaped, "path-not-contained") && !Directory.Exists(Path.Combine(pathEvidence, "PC1")), "instantiate-tokens:path-not-contained-refuses", escaped.Text);

        var okTemplate = w.NewTemplate("root={snapshotRoot}\nevidence={evidenceRoot}\ntask={taskId}\nrun={runId}\npc={pcId}\nphase={phase}\n");
        var okEvidence = w.NewEvidence();
        var first = w.Instantiate(okTemplate, okEvidence, snap.Root, runId: "run-a");
        var attemptA = Path.Combine(okEvidence, "PC1", "baseline", "run-a");
        var manifestA = Path.Combine(attemptA, "resolved-manifest.txt");
        var manifestText = Accepted(first) && File.Exists(manifestA) ? File.ReadAllText(manifestA) : "";
        var resolved = manifestText.Contains(snap.Root, StringComparison.Ordinal) && !manifestText.Contains("{snapshotRoot}", StringComparison.Ordinal);
        var manifestHash = resolved ? Sha(manifestA) : "";
        var second = w.Instantiate(okTemplate, okEvidence, snap.Root, runId: "run-b");
        var attemptB = Path.Combine(okEvidence, "PC1", "baseline", "run-b");
        var secondOk = Accepted(second) && Directory.Exists(attemptB);
        var third = w.Instantiate(okTemplate, okEvidence, snap.Root, runId: "run-a");
        var kept = resolved && Sha(manifestA) == manifestHash;
        w.Check(resolved && secondOk && Refused(third, "existing-attempt") && kept, "instantiate-tokens:existing-attempt-refuses",
            first.Text + " / " + second.Text + " / " + third.Text);
    });

    // A usage error is the CLI's exit 2 with no verdict line; an unreadable pack file is exit 1, not a verdict either.
    [Test]
    public void Usage_errors_and_unreadable_inputs_are_not_verdicts()
    {
        Should.Throw<NightlyUsageException>(() => MechanicalPcOptions.Parse([]));
        Should.Throw<NightlyUsageException>(() => MechanicalPcOptions.Parse(["bless"]));
        Should.Throw<NightlyUsageException>(() => MechanicalPcOptions.Parse(["preflight", "--snapshot-root", "a", "--pack-root", "b"]));
        Should.Throw<NightlyUsageException>(() => MechanicalPcOptions.Parse(["oracle", "--pack-root", "b", "--snapshot-root", "a"]));
        MechanicalPcOptions.Parse(["restore", "--snapshot-root", "a", "--evidence-root", "e", "--pack-root", "p"]).Phase.ShouldBe("restore");
        Case("unreadable", 1, w =>
        {
            var result = w.Run("oracle", "--pack-root", w.NewPack("empty"));
            w.Check(result.Exit == MechanicalPc.Failed && !result.Text.Contains("C479 ", StringComparison.Ordinal) && result.Text.Contains("oracle.json", StringComparison.Ordinal),
                "unreadable:missing-oracle-json-fails", result.Text);
        });
    }

    // ---- the rehearsal world (the driver's helpers) ----------------------------------------------------------------------

    private sealed record Snapshot(string Root, string Head);
    private sealed record ClosureItem(string Path, string Sha);
    private sealed record Edit(string Path, string Preimage, string Mutant, string Context, string Replacement, int Expected);
    private sealed record RestoreFile(string Path, string Preimage, string Mutant);
    private sealed record Result(int Exit, string Text);
    private sealed record RestoreRun(Result Result, string Root, string PreimageA);

    private static void Case(string name, int expectedRows, Action<World> body)
    {
        var root = Path.Combine(Path.GetTempPath(), "c479-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var world = new World(root, name);
            body(world);
            world.Rows.ShouldAllPass(expectedRows);
        }
        finally
        {
            ReadOnlyDirectory.Delete(root);
        }
    }

    private static bool Accepted(Result r) => r.Exit == 0 && r.Text.Contains("C479 ACCEPT", StringComparison.Ordinal) && !r.Text.Contains("C479 REFUSE", StringComparison.Ordinal);

    private static bool Refused(Result r, string reason) => r.Exit == 2 && r.Text.Contains("C479 REFUSE " + reason, StringComparison.Ordinal);

    private static string Full(string root, string relative) => Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));

    private static string Sha(string path) => MechanicalPc.Sha256(new System.IO.Abstractions.FileSystem(), path) ?? "missing";

    private static string BytesSha(byte[] bytes) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();

    private static void WriteUtf8(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text, Utf8);
    }

    private static string Git(string repo, params string[] arguments)
    {
        var result = NightlyNativeGit.Run(repo, arguments);
        result.ExitCode.ShouldBe(0, "git " + string.Join(" ", arguments) + " -> " + result.Output);
        return result.Output.Trim();
    }

    private static string Json(string value) => "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    private static string JsonArray(IEnumerable<string> items) => "[" + string.Join(",", items.Select(Json)) + "]";

    private static ClosureItem Closure(string root, string relative) => new(relative, Sha(Full(root, relative)));

    private static void CopyBackup(string evidence, string relative, string source)
    {
        var dest = Full(Path.Combine(evidence, "backup"), relative);
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        File.Copy(source, dest, true);
    }

    private static void WriteRestore(string pack, IEnumerable<RestoreFile> files) =>
        WriteUtf8(Path.Combine(pack, "restore.json"), "{\"files\":[" + string.Join(",", files.Select(f =>
            "{\"path\":" + Json(f.Path) + ",\"preimageSha256\":" + Json(f.Preimage) + ",\"mutantSha256\":" + Json(f.Mutant) + "}")) + "]}");

    // The canned checkpoint driver text: a CHECKPOINT summary, the EXIT CODE line, then EXECUTED and FAILED rows.
    private static string ExactDriver(int exitCode = 1, int skipped = 0, string buildSource = "verified", string[]? executed = null, string[]? failed = null)
    {
        executed ??= ExactExecuted;
        failed ??= ExactFailed;
        var lines = new List<string>
        {
            "CHECKPOINT CP-x commit=abc build=ok filter=/*/*/Example/* executed=" + executed.Length + " passed=0 failed=" + failed.Length + " skipped=" + skipped
                + " trx=/tmp/c479.trx slot=granted waited=0s dirty=0 source=abc sourceState=clean buildSource=" + buildSource,
            "CHECKPOINT CP-x EXIT CODE: " + exitCode,
        };
        lines.AddRange(executed.Select(name => "EXECUTED " + name));
        lines.AddRange(failed.Select(name => "FAILED " + name));
        return string.Join("\n", lines) + "\n";
    }

    private sealed class World(string root, string caseName)
    {
        public NightlyCaseRows Rows { get; } = new();

        public void Check(bool ok, string row, string detail) => Rows.Check(ok, "C479 " + row, detail);

        public string NewWork(string name)
        {
            var dir = Path.Combine(root, name + "-" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(dir);
            return dir;
        }

        public string NewPack(string name) => Directory.CreateDirectory(Path.Combine(NewWork(name), "pack")).FullName;

        public string NewEvidence() => Directory.CreateDirectory(Path.Combine(NewWork("evidence"), "root")).FullName;

        public Result Run(params string[] arguments)
        {
            var output = new StringWriter();
            var exit = MechanicalPc.Run(MechanicalPcOptions.Parse(arguments), NightlyHost.Production(output, output), new System.IO.Abstractions.FileSystem());
            return new Result(exit, output.ToString().Trim());
        }

        public Snapshot NewSnapshot()
        {
            var snapshot = Path.Combine(NewWork("repo"), "snapshot");
            Directory.CreateDirectory(snapshot);
            foreach (var (relative, text) in StandardFiles) WriteUtf8(Full(snapshot, relative), text);
            Git(snapshot, "init", "-q", "-b", "master");
            Git(snapshot, "config", "user.email", "c479@example.invalid");
            Git(snapshot, "config", "user.name", "c479");
            Git(snapshot, "config", "commit.gpgsign", "false");
            Git(snapshot, "config", "core.autocrlf", "false");
            Git(snapshot, "add", "--", "src", "fixtures", "backend");
            Git(snapshot, "commit", "-q", "-m", "L");
            return new Snapshot(snapshot, Git(snapshot, "rev-parse", "HEAD"));
        }

        public Dictionary<string, string> Seal(string snapshot, IEnumerable<string> paths) => paths.ToDictionary(p => p, p => Sha(Full(snapshot, p)));

        public bool SealHolds(Dictionary<string, string> seal, string snapshot) => seal.All(pair => Sha(Full(snapshot, pair.Key)) == pair.Value);

        // The bytes the apply writes (File.WriteAllText: UTF-8 without a byte-order mark), hashed from a scratch copy.
        public string ReplacedHash(string full, string context, string replacement)
        {
            var tmp = Path.Combine(NewWork("mut"), "bytes.txt");
            File.WriteAllText(tmp, File.ReadAllText(full).Replace(context, replacement, StringComparison.Ordinal));
            return Sha(tmp);
        }

        public string MutantFile(string full, string context, string replacement)
        {
            var tmp = Path.Combine(NewWork("stage"), "staged.txt");
            File.WriteAllText(tmp, File.ReadAllText(full).Replace(context, replacement, StringComparison.Ordinal));
            File.Copy(tmp, full, true);
            return Sha(full);
        }

        public void WriteIndex(string pack, string snapshot, IEnumerable<string> members)
        {
            var json = "{\"members\":[" + string.Join(",", members.Select(rel => "{\"path\":" + Json(rel) + ",\"sha256\":" + Json(Sha(Full(snapshot, rel))) + "}")) + "]}";
            var path = Path.Combine(pack, "index.json");
            WriteUtf8(path, json);
            WriteUtf8(Path.Combine(pack, "index.sha256"), BytesSha(File.ReadAllBytes(path)));
        }

        public void WriteApply(string pack, string[] allowed, ClosureItem[] closure, Edit[] edits)
        {
            var json = "{\"allowedFiles\":" + JsonArray(allowed)
                + ",\"closure\":[" + string.Join(",", closure.Select(c => "{\"path\":" + Json(c.Path) + ",\"sha256\":" + Json(c.Sha) + "}")) + "]"
                + ",\"edits\":[" + string.Join(",", edits.Select(e => "{\"path\":" + Json(e.Path) + ",\"preimageSha256\":" + Json(e.Preimage) + ",\"mutantSha256\":" + Json(e.Mutant)
                    + ",\"context\":" + Json(e.Context) + ",\"replacement\":" + Json(e.Replacement) + ",\"expectedMatches\":" + e.Expected + "}")) + "]}";
            WriteUtf8(Path.Combine(pack, "apply.json"), json);
        }

        // Read-Driver over the canned text, then oracle.json with fixed mutant and restored output identities.
        public Result Oracle(string driverText, string[] expectedExecuted, string[] expectedFailed, bool preexisted = false, bool sameOutput = false)
        {
            var executed = new List<string>();
            var failed = new List<string>();
            var skipped = 0;
            var buildSource = "verified";
            var exitCode = 0;
            foreach (var line in driverText.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0))
            {
                if (line.StartsWith("EXECUTED ", StringComparison.Ordinal)) executed.Add(line[9..].Trim());
                else if (line.StartsWith("FAILED ", StringComparison.Ordinal)) failed.Add(line[7..].Trim());
                else if (Regex.Match(line, @"EXIT CODE:\s*(\d+)") is { Success: true } exit) exitCode = int.Parse(exit.Groups[1].Value);
                else if (line.StartsWith("CHECKPOINT ", StringComparison.Ordinal))
                {
                    if (Regex.Match(line, @"skipped=(\d+)") is { Success: true } s) skipped = int.Parse(s.Groups[1].Value);
                    if (Regex.Match(line, @"buildSource=([A-Za-z0-9_-]+)") is { Success: true } b) buildSource = b.Groups[1].Value;
                }
            }
            var mutant = new string('a', 64);
            var restored = sameOutput ? mutant : new string('b', 64);
            var pack = NewPack("oracle");
            WriteUtf8(Path.Combine(pack, "oracle.json"), "{\"driverExit\":" + exitCode + ",\"buildSource\":" + Json(buildSource) + ",\"mutantOutputHash\":" + Json(mutant)
                + ",\"restoredOutputHash\":" + Json(restored) + ",\"skipped\":" + skipped + ",\"resultsPreexisted\":" + (preexisted ? "true" : "false")
                + ",\"executed\":" + JsonArray(executed) + ",\"failed\":" + JsonArray(failed) + ",\"expectedExecuted\":" + JsonArray(expectedExecuted)
                + ",\"expectedFailed\":" + JsonArray(expectedFailed) + "}");
            return Run("oracle", "--pack-root", pack);
        }

        // A mutated src/a.txt with its backup taken, an optional red note and forced-rebuild marker, then restore.
        public RestoreRun RestoreCase(string phase = "restore", bool marker = false, bool redNote = false)
        {
            var snap = NewSnapshot();
            var evidence = NewEvidence();
            var a = Full(snap.Root, "src/a.txt");
            var originalA = Path.Combine(NewWork("pre"), "a.txt");
            File.Copy(a, originalA, true);
            var preimageA = Sha(a);
            MutantFile(a, "BEFORE", "AFTER");
            CopyBackup(evidence, "src/a.txt", originalA);
            if (redNote) WriteUtf8(Path.Combine(evidence, "red-phase.txt"), "exit=1");
            if (marker) WriteUtf8(Path.Combine(evidence, "forced-rebuild.marker"), "rebuilt");
            var pack = NewPack("restore");
            WriteRestore(pack, [new RestoreFile("src/a.txt", preimageA, Sha(a))]);
            var result = Run("restore", "--snapshot-root", snap.Root, "--evidence-root", evidence, "--pack-root", pack, "--phase", phase);
            return new RestoreRun(result, snap.Root, preimageA);
        }

        public string NewTemplate(string body)
        {
            var path = Path.Combine(NewWork("template"), "manifest.txt");
            WriteUtf8(path, body);
            return path;
        }

        public Result Instantiate(string template, string evidence, string snapshot, string runId = "run-a", string resultsRelative = "out") =>
            Run("instantiate", "--snapshot-root", snapshot, "--evidence-root", evidence, "--template-path", template, "--task-id", "task-1", "--run-id", runId,
                "--pc-id", "PC1", "--phase", "baseline", "--results-relative", resultsRelative);

        public override string ToString() => caseName;
    }
}
