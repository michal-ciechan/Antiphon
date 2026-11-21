using Antiphon.Nightly;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-1836 S6a: the shadow dirty launch order, in-process (moved from test-rc-dirty-order.ps1). Open failures
/// launch first, then interrupted, not-run, changed and sample rows. A pass on the failing tip, a fixProven flag or a
/// digest change never clears a failure; a later green clears it only when its commit contains a fix (ancestry, or
/// one exact cherry-pick trailer). Carries stay omitted and refuse an open failure. The off switch holds: Full mode,
/// no gate credit, and no caller. Inputs are plain dictionaries; only git is started.
/// </summary>
[Category("Integration")]
[ParallelLimiter<GitProcessSpawnLimit>]
[Timeout(60_000)]
public sealed class RcDirtyLaunchOrderTests
{
    private const string NotInHistory = "candidate-not-in-history";

    private readonly List<string> errors = [];

    private void Note(bool ok, string label)
    {
        if (!ok) errors.Add(label);
    }

    private void Contract(string name) =>
        errors.ShouldBeEmpty("DO " + name + ".contract: " + string.Join("; ", errors));

    private static Dictionary<string, object?> Row(string key, string disposition) => new(StringComparer.Ordinal)
    {
        ["key"] = key, ["disposition"] = disposition, ["reasons"] = new object[] { "digest-mismatch" },
    };

    private static Dictionary<string, object?> Attempt(int sequence, string sha, string key, string outcome, bool fixProven = false, string digest = "same") =>
        new(StringComparer.Ordinal)
        {
            ["sequence"] = sequence, ["sourceSha"] = sha, ["fixProven"] = fixProven,
            ["results"] = new object[]
            {
                new Dictionary<string, object?>(StringComparer.Ordinal) { ["key"] = key, ["outcome"] = outcome, ["inputDigest"] = digest },
            },
        };

    private static Dictionary<string, object?> Fix(string key, string fixSha) => new(StringComparer.Ordinal) { ["key"] = key, ["fixSha"] = fixSha };

    private static RcDirtyLaunchOrder Launch(object[] obligations, object[] attempts, object[] fixes, string repositoryRoot, string candidateSha) =>
        RcDirtyOrder.Launch(obligations, attempts, fixes, repositoryRoot, candidateSha);

    private static bool HasKey(RcDirtyLaunchOrder order, string key) => order.Launch.Any(row => string.Equals(row.Key, key, StringComparison.Ordinal));

    private static string BandOf(RcDirtyLaunchOrder order, string key) =>
        order.Launch.FirstOrDefault(row => string.Equals(row.Key, key, StringComparison.Ordinal))?.Band ?? "";

    private static string LaunchText(RcDirtyLaunchOrder order) =>
        string.Join(",", order.Launch.Select(item => item.Key + ":" + item.Band + ":" + item.Disposition));

    private static bool Omitted(RcDirtyLaunchOrder order, string key) => order.Omitted.Any(item => string.Equals(item, key, StringComparison.Ordinal));

    private void Refuses(Action body, string reason)
    {
        try
        {
            body();
            Note(false, "accepted:" + reason);
        }
        catch (Exception ex)
        {
            Note(ex.Message.Contains(reason, StringComparison.Ordinal), "wrong-refusal:" + reason + ":" + ex.Message);
        }
    }

    /// <summary>A repository with base on main, fix on fixline and other on a branch from base.</summary>
    private sealed class DoRepo : IDisposable
    {
        public DoRepo()
        {
            try
            {
                Repo.Init();
                Base = Commit("base");
                Repo.Git("checkout", "-q", "-b", "fixline");
                Fix = Commit("fixed", "fix");
                Repo.Git("checkout", "-q", "-b", "other", Base);
                Other = Commit("other");
            }
            catch
            {
                Repo.Dispose();
                throw;
            }
        }

        public RcInputFixtureRepository Repo { get; } = new("rc-dirty-");
        public string Base { get; }
        public string Fix { get; }
        public string Other { get; }
        public string Root => Repo.Root;

        public string Commit(string note, string? message = null, params string[] extra)
        {
            Repo.Put("note", note + "\n");
            Repo.Git("add", "note");
            Repo.Git(["commit", "-qm", message ?? note, .. extra]);
            return Repo.Head();
        }

        public void Dispose() => Repo.Dispose();
    }

    private void AssertOffSwitch(RcDirtyLaunchOrder order)
    {
        Note(!order.GateCredit, "gate-credit-stays-false");
        Note(order.EffectiveMode == "Full", "result-effective-mode-full");
        Note(!order.TrustTransition, "no-trust-transition");
        Note(order.Activation == "off" && order.Shadow, "activation-off");
        foreach (var name in new[] { "release-cut.ps1", "nightly-run.ps1", "nightly-tests.ps1" })
        {
            var text = RcInputFixtureRepository.ReadSource("scripts/" + name);
            Note(text.Contains("ValidateSet('Full', 'Shadow', 'ChangeAware')", StringComparison.Ordinal), name + "-modes-unchanged");
            Note(!HasDirtyOrderCaller(text), name + "-has-no-dirty-order-caller");
            Note(!text.Contains("Incremental", StringComparison.Ordinal), name + "-has-no-incremental-mode");
        }
        var selection = RcInputFixtureRepository.ReadSource("scripts/lib/release-selection.ps1");
        Note(selection.Contains("effectiveMode='Full'", StringComparison.Ordinal), "release-selection-effective-mode-full");
        var compiled = RcInputFixtureRepository.ReadSource("scripts/lib/Antiphon.Scripts/ReleaseSelection.cs");
        Note(compiled.Contains("(\"effectiveMode\", \"Full\")", StringComparison.Ordinal), "compiled-selector-effective-mode-full");
        Note(!compiled.Contains("Incremental", StringComparison.Ordinal), "compiled-selector-has-no-incremental");
        // The compiled credit verdict refuses an rc green whose explicit coverage scope is not Full.
        var scoped = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["profile"] = "rc", ["trigger"] = "rc", ["ref"] = "rc/2026-10-18", ["coverageComplete"] = true, ["testsPassed"] = true,
            ["reportDelivered"] = true, ["exitCode"] = 0, ["runId"] = "r", ["policyHash"] = "p", ["sha"] = new string('a', 40),
            ["expectedSha"] = new string('a', 40), ["candidateId"] = "rc-2026-10-18-aaaaaaaaaaaa", ["coverageScope"] = "Shadow",
        };
        var shadow = ReleaseGateCredit.Verdict(scoped);
        scoped["coverageScope"] = "Full";
        var full = ReleaseGateCredit.Verdict(scoped);
        Note(!shadow.Ok && shadow.Reasons.Contains("full-credit-scope-required") && full.Ok, "gate-credit-refuses-non-full-scope");
        foreach (var name in new[] { "publish-release.ps1", "promote-release.ps1" })
        {
            var path = Path.Combine(RcInputFixtureRepository.SourceRoot, "scripts", name);
            if (File.Exists(path)) Note(!HasDirtyOrderCaller(File.ReadAllText(path)), name + "-has-no-dirty-order-caller");
        }
        // The implementation (the PowerShell wrapper is gone) neither publishes, packs, replaces the full seed order,
        // nor honors a caller's fixProven flag.
        var body = RcInputFixtureRepository.ReadSource("scripts/lib/Antiphon.Nightly/RcDirtyOrder.cs");
        Note(!body.Contains("publish-release", StringComparison.Ordinal), "library-does-not-publish");
        Note(!body.Contains("Resolve-NightlyStoppedSeedOrder", StringComparison.Ordinal) && !body.Contains("StoppedSeedOrder", StringComparison.Ordinal),
            "full-seed-function-not-replaced");
        Note(!body.Contains("nightly-tests-impl", StringComparison.Ordinal), "library-does-not-pack");
        Note(!body.Contains("FixProven", StringComparison.OrdinalIgnoreCase), "library-does-not-honor-fix-proven");
        // The full stopped-seed order is compiled (DispatchSchedule.StoppedSeedOrder in the nightly core).
        var seed = RcInputFixtureRepository.ReadSource("scripts/lib/Antiphon.Nightly/DispatchSchedule.cs");
        Note(seed.Contains("public static PSObject StoppedSeedOrder(", StringComparison.Ordinal), "full-seed-function-remains");
    }

    // A caller names the deleted library or the compiled type.
    private static bool HasDirtyOrderCaller(string text) =>
        text.Contains("rc-dirty-order", StringComparison.Ordinal) || text.Contains("RcDirtyOrder", StringComparison.Ordinal);

    [Test]
    public void OnlyDirtyAndSampleLaunch()
    {
        object[] rows = [Row("dirty", "Dirty"), Row("sample", "Sample"), Row("sample-failed", "Sample"), Row("carry", "Carry")];
        object[] attempts = [Attempt(1, "old", "sample", "passed"), Attempt(2, "old", "sample-failed", "failed"), Attempt(3, "old", "carry", "passed")];
        var order = Launch(rows, attempts, [], "", NotInHistory);
        var present = HasKey(order, "dirty") && HasKey(order, "sample") && HasKey(order, "sample-failed") && !HasKey(order, "carry");
        Note(present, "only-dirty-and-sample-launch");
        var keys = order.Launch.Select(item => item.Key).ToList();
        Note(keys.Distinct(StringComparer.Ordinal).Count() == keys.Count, "launch-keys-unique");
        Note(HasKey(order, "sample") && HasKey(order, "sample-failed") && BandOf(order, "sample-failed") == "failed", "sample-membership-preserved");
        Refuses(() => Launch([rows[0], rows[0]], [], [], "", ""), "duplicate-obligation");
        AssertOffSwitch(order);
        Contract(nameof(OnlyDirtyAndSampleLaunch));
    }

    [Test]
    public void OpenFailurePrecedesChanged()
    {
        object[] rows = [Row("sample", "Sample"), Row("changed", "Dirty"), Row("not-run", "Dirty"), Row("interrupted", "Dirty"), Row("failed", "Dirty")];
        object[] attempts =
        [
            Attempt(1, "old-sha", "sample", "passed"), Attempt(2, "old-sha", "changed", "passed"), Attempt(3, "old-sha", "interrupted", "interrupted"),
            Attempt(4, "old-sha", "failed", "failed"),
        ];
        var order = Launch(rows, attempts, [], "", NotInHistory);
        var index = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < order.Launch.Length; i++) index[order.Launch[i].Key] = i;
        int At(string key) => index.TryGetValue(key, out var at) ? at : -1;
        var ordered = At("failed") < At("interrupted") && At("interrupted") < At("not-run") && At("not-run") < At("changed") && At("changed") < At("sample");
        Note(ordered, "open-failure-precedes-changed");
        Note(BandOf(order, "failed") == "failed" && BandOf(order, "interrupted") == "interrupted" && BandOf(order, "not-run") == "not-run"
            && BandOf(order, "changed") == "changed" && BandOf(order, "sample") == "sample", "band-names");
        AssertOffSwitch(order);
        Contract(nameof(OpenFailurePrecedesChanged));
    }

    [Test]
    public void SameTipPassCannotClearFailure()
    {
        using var repo = new DoRepo();
        repo.Repo.Git("checkout", "-q", "-b", "picked", repo.Base);
        var pick = repo.Commit("ported", "port", "-m", "(cherry picked from commit " + repo.Fix + ")");
        repo.Repo.Git("checkout", "-q", "-b", "child", repo.Fix);
        repo.Repo.Git("commit", "--allow-empty", "-qm", "child");
        var child = repo.Repo.Head();
        string b = repo.Base, fix = repo.Fix, other = repo.Other;
        object[] rows =
        [
            Row("same", "Dirty"), Row("proven", "Dirty"), Row("digest", "Dirty"), Row("later-no-fix", "Dirty"), Row("later-fix", "Dirty"),
            Row("present", "Dirty"), Row("cherry", "Dirty"),
        ];
        object[] attempts =
        [
            Attempt(1, b, "same", "failed"),
            Attempt(2, b, "same", "passed"),
            Attempt(3, b, "proven", "failed", true),
            Attempt(4, b, "digest", "failed"),
            Attempt(5, b, "digest", "passed", false, "other-digest"),
            Attempt(6, b, "later-no-fix", "failed"),
            Attempt(7, other, "later-no-fix", "passed"),
            Attempt(8, b, "later-fix", "failed"),
            Attempt(9, fix, "later-fix", "passed"),
            Attempt(10, fix, "present", "failed"),
            Attempt(11, child, "present", "passed"),
            Attempt(12, b, "cherry", "failed"),
            Attempt(13, pick, "cherry", "passed"),
        ];
        object[] fixes = [Fix("later-no-fix", fix), Fix("later-fix", fix), Fix("present", fix), Fix("cherry", fix)];
        var order = Launch(rows, attempts, fixes, repo.Root, b);
        Note(HasKey(order, "same") && BandOf(order, "same") == "failed", "same-tip-pass-stays-failed");
        Note(HasKey(order, "proven") && BandOf(order, "proven") == "failed", "fix-proven-stays-failed");
        Note(HasKey(order, "digest") && BandOf(order, "digest") == "failed", "digest-only-stays-failed");
        Note(HasKey(order, "later-no-fix") && BandOf(order, "later-no-fix") == "failed", "later-unrelated-green-stays-failed");
        Note(HasKey(order, "later-fix") && BandOf(order, "later-fix") == "changed", "later-contained-fix-leaves-failed");
        Note(HasKey(order, "present") && BandOf(order, "present") == "failed", "fix-present-at-failure-stays-failed");
        Note(HasKey(order, "cherry") && BandOf(order, "cherry") == "changed", "cherry-pick-fix-leaves-failed");
        Note(!order.GateCredit && !order.TrustTransition && order.EffectiveMode == "Full", "discharged-still-has-no-gate-credit");
        AssertOffSwitch(order);
        Contract(nameof(SameTipPassCannotClearFailure));
    }

    [Test]
    public void CaseDistinctKeysStaySeparate()
    {
        const string upper = "Suite/FooTests";
        const string lower = "Suite/fooTests";
        object[] rows = [Row(upper, "Dirty"), Row(lower, "Sample")];
        object[] attempts = [Attempt(1, "old-sha", upper, "failed"), Attempt(2, "old-sha", lower, "passed")];
        var order = Launch(rows, attempts, [], "", NotInHistory);
        var text = LaunchText(order);
        Note(text == upper + ":failed:Dirty," + lower + ":sample:Sample", "case-distinct-launch:" + text);
        using var built = new DoRepo();
        object[] fixRows = [Row(upper, "Dirty"), Row(lower, "Dirty")];
        object[] fixAttempts =
        [
            Attempt(1, built.Base, upper, "failed"), Attempt(2, built.Base, lower, "failed"), Attempt(3, built.Other, upper, "passed"),
            Attempt(4, built.Fix, lower, "passed"),
        ];
        var fixedOrder = Launch(fixRows, fixAttempts, [Fix(lower, built.Fix)], built.Root, built.Base);
        var fixedText = LaunchText(fixedOrder);
        Note(fixedText == upper + ":failed:Dirty," + lower + ":changed:Dirty", "case-distinct-fix:" + fixedText);
        AssertOffSwitch(order);
        Contract(nameof(CaseDistinctKeysStaySeparate));
    }

    [Test]
    public void CarryWithOpenFailureRefuses()
    {
        object[] rows = [Row("k", "Carry"), Row("d", "Dirty")];
        object[] same = [Attempt(1, "sha-a", "k", "failed"), Attempt(2, "sha-a", "k", "passed", true)];
        Refuses(() => Launch(rows, same, [], "", NotInHistory), "carry-with-open-failure");
        object[] interrupted = [Attempt(1, "sha-a", "k", "interrupted")];
        Refuses(() => Launch(rows, interrupted, [], "", ""), "carry-with-open-failure");
        object[] later = [Attempt(1, "sha-a", "k", "failed"), Attempt(2, "sha-b", "k", "passed", true)];
        Refuses(() => Launch(rows, later, [], "", ""), "carry-with-open-failure");
        object[] cleanAttempts = [Attempt(1, "old", "k", "passed"), Attempt(2, "old", "d", "passed")];
        var clean = Launch(rows, cleanAttempts, [], "", NotInHistory);
        Note(!HasKey(clean, "k") && Omitted(clean, "k") && HasKey(clean, "d") && BandOf(clean, "d") == "changed", "clean-carry-stays-omitted");
        using var built = new DoRepo();
        object[] dischargedAttempts =
        [
            Attempt(1, built.Base, "k", "failed"), Attempt(2, built.Fix, "k", "passed", true), Attempt(3, built.Base, "d", "passed"),
        ];
        var discharged = Launch(rows, dischargedAttempts, [Fix("k", built.Fix)], built.Root, built.Base);
        var dischargedText = LaunchText(discharged);
        Note(!HasKey(discharged, "k") && Omitted(discharged, "k") && dischargedText == "d:changed:Dirty", "discharged-carry-stays-omitted:" + dischargedText);
        AssertOffSwitch(clean);
        Contract(nameof(CarryWithOpenFailureRefuses));
    }

    [Test]
    public void UnknownShaRefuses()
    {
        using var built = new DoRepo();
        const string unknown = "deadbeefdeadbeefdeadbeefdeadbeefdeadbeef";
        Note(unknown != built.Base && unknown != built.Fix && unknown != built.Other, "unknown-sha-is-not-a-fixture-commit");
        object[] fixRows = [Row("fix-unknown", "Dirty")];
        object[] fixAttempts = [Attempt(1, built.Base, "fix-unknown", "failed"), Attempt(2, built.Other, "fix-unknown", "passed")];
        Refuses(() => Launch(fixRows, fixAttempts, [Fix("fix-unknown", unknown)], built.Root, built.Base), "repo-sha-unresolved");
        object[] greenRows = [Row("green-unknown", "Dirty")];
        object[] greenAttempts = [Attempt(1, built.Base, "green-unknown", "failed"), Attempt(2, unknown, "green-unknown", "passed")];
        Refuses(() => Launch(greenRows, greenAttempts, [Fix("green-unknown", built.Fix)], built.Root, built.Base), "repo-sha-unresolved");
        object[] heldRows = [Row("held", "Dirty")];
        object[] heldAttempts = [Attempt(1, built.Base, "held", "failed"), Attempt(2, built.Other, "held", "passed")];
        var held = Launch(heldRows, heldAttempts, [Fix("held", built.Fix)], built.Root, built.Base);
        Note(HasKey(held, "held") && BandOf(held, "held") == "failed", "known-uncontained-fix-stays-failed");
        AssertOffSwitch(held);
        Contract(nameof(UnknownShaRefuses));
    }
}
