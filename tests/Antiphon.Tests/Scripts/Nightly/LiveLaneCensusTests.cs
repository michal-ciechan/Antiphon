using System.Text.Json;
using Antiphon.Nightly;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts.Nightly;

/// <summary>
/// W2-nightly S25. The live-lane census of the old scripts/lib/live-lane-manifest.ps1, compiled
/// (scripts/lib/Antiphon.Nightly/LiveLaneCensus.cs). The census itself is the commission-scoped CP-30 command row
/// ("antiphon-scripts live-lane-census" over ANTIPHON_LIVE_PORTABLE_CONTEXT) and is not run here: these tests pin its
/// seed lists, its declared pre-S1 base, its policy hash, its refusal without a context (after the export, receipt and
/// scratch witnesses it runs first) and the S1a group context. CensusAdditiveIdentity and CensusTargetScope are
/// LiveLaneInventoryTests. Scratch lives under .antiphon/live-lane-capture and is removed.
/// </summary>
[Category("Unit")]
public sealed class LiveLaneCensusTests
{
    private static string Repo => LiveLaneInventoryTests.LiveLaneProcess.FindRepo();

    /// <summary>The seed lists keep every PowerShell-era entry; the compiled engine source is the one addition.</summary>
    [Test]
    public void SeedListsKeepTheirEntries()
    {
        LiveLaneCensus.ProtectedPaths.ShouldBe(
        [
            "tests/test-execution-policy.json",
            "scripts/lib/nightly-common.ps1",
            "scripts/lib/Antiphon.Nightly/NightlyPolicy.cs",
            "scripts/lib/Antiphon.Nightly/ExecutionPolicy.cs",
            "scripts/lib/Antiphon.Nightly/DiscoveryIndex.cs",
            "scripts/lib/Antiphon.Nightly/DiagnosticLog.cs",
            "scripts/lib/Antiphon.Nightly/DiagScan.cs",
            "scripts/lib/nightly-coverage.ps1",
        ]);
        LiveLaneCensus.AllowedSeedPaths.ShouldBe(
        [
            "tests/live-lanes.json",
            "scripts/lib/live-lane-manifest.ps1",
            "scripts/test-live-lane-manifest.ps1",
            "tests/Antiphon.Tests/Scripts/LiveLaneInventoryTests.cs",
            "tests/Antiphon.Agents.Pty.Tests/LiveClaudeInventoryTests.cs",
            "tests/fixtures/live-lanes/full-discovery-before.json",
            "scripts/lib/Antiphon.Nightly/LiveLaneCensus.cs",
        ]);
        LiveLaneCensus.ImmutableSeedPaths.ShouldBe(["tests/fixtures/live-lanes/pre-s1-base.sha"]);
        File.Exists(Path.Combine(Repo, LiveLaneCensus.EngineSource)).ShouldBeTrue(LiveLaneCensus.EngineSource);
    }

    /// <summary>
    /// The pin and the compiled engine declare the same pre-S1 base, and the engine's declaration is the one
    /// seed-declares-base reads at each seed commit (a re-seed edits it there).
    /// </summary>
    [Test]
    public void TheEngineDeclaresThePinnedBase()
    {
        var repo = Repo;
        LiveLaneCensus.PinnedPreS1(repo).ShouldBe(LiveLaneCensus.PreS1BaseSha);
        LiveLaneCensus.DeclaredPreS1(LiveLaneCensus.EngineSource, File.ReadAllText(Path.Combine(repo, LiveLaneCensus.EngineSource))).ShouldBe(LiveLaneCensus.PreS1BaseSha);
        LiveLaneCensus.DeclaredPreS1("scripts/lib/live-lane-manifest.ps1", "$script:LiveLanePreS1BaseSha = '" + new string('E', 40) + "'").ShouldBe(new string('e', 40));
        LiveLaneCensus.DeclaredPreS1(LiveLaneCensus.EngineSource, "public const string PreS1BaseSha = \"" + new string('E', 40) + "\";").ShouldBe(new string('e', 40));
        LiveLaneCensus.DeclaredPreS1(LiveLaneCensus.EngineSource, "no declaration").ShouldBe("");
        LiveLaneCensus.DeclaredPreS1("scripts/test-live-lane-manifest.ps1", "$script:LiveLanePreS1BaseSha = '" + new string('e', 40) + "'").ShouldBe("");
    }

    /// <summary>The EmitPolicyHash case: the policy's own hash (the policyHash it records), of the root's policy or of a given path.</summary>
    [Test]
    public void EmitPolicyHashIsThePolicysOwnHash()
    {
        var repo = Repo;
        var policy = Path.Combine(repo, "tests", "test-execution-policy.json");
        using var doc = JsonDocument.Parse(File.ReadAllText(policy));
        var recorded = doc.RootElement.GetProperty("policyHash").GetString();
        LiveLaneCensus.EmitPolicyHash(repo).ShouldBe(recorded);
        LiveLaneCensus.EmitPolicyHash(Path.Combine(repo, "docs"), policy).ShouldBe(recorded);
        Should.Throw<ArgumentException>(() => LiveLaneCensus.EmitPolicyHash(""));
    }

    /// <summary>
    /// The FullCensusUnchanged case without a context: the export, receipt (census-skipped-receipt included) and
    /// scratch witnesses pass, then the command refuses context-required with exit 2 and leaves no temp residue.
    /// </summary>
    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public void FullCensusUnchangedRefusesWithoutContext()
    {
        var output = new StringWriter();
        var code = LiveLaneCensus.FullCensusUnchanged(Repo, () => null, output);
        var lines = output.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        code.ShouldBe(2, string.Join('\n', lines));
        lines.ShouldBe(["context-required"]);
    }

    /// <summary>A null context is context-required; a context without a seed is its receipt's reason and seed-object-missing.</summary>
    [Test]
    public void CensusRefusesWithoutContextOrSeed()
    {
        var census = new LiveLaneCensus();
        try
        {
            census.Run(null).Diagnostics.ShouldBe(["context-required"]);
            var receipts = census.Scratch.NewOwned(Repo, "censusnoseed" + Guid.NewGuid().ToString("N"));
            var result = census.Run(new LiveLaneContext { CandidateRoot = Repo, SourceSha = new string('a', 40), CheckpointResults = receipts, CensusBaseSha = "" });
            result.Accepted.ShouldBeFalse();
            result.Diagnostics.ShouldBe(["producer-receipt-missing", "seed-object-missing"]);
            LiveLaneReceipt.WriteFixture(receipts, new string('a', 40), "green");
            census.Run(new LiveLaneContext { CandidateRoot = Repo, SourceSha = new string('a', 40), CheckpointResults = receipts }).Diagnostics.ShouldBe(["seed-object-missing"]);
        }
        finally
        {
            census.Clear();
        }
    }

    /// <summary>
    /// The S1a group's baseline context: no producer, the census's own pre-S1 base, landing target and seed lists, read
    /// back by LiveLaneContext; an unknown group and an S1a group without a census seed refuse.
    /// </summary>
    [Test]
    public void GroupContextNamesTheCensusBinding()
    {
        var scratch = new LiveLaneScratch();
        try
        {
            var root = scratch.NewOwned(Repo, "groupcontext" + Guid.NewGuid().ToString("N"));
            var results = Path.Combine(root, ".antiphon/live-lane-runs/run");
            var path = LiveLaneCensus.WriteGroupContext(root, "S1a-baseline", "", new string('a', 40), results);
            path.ShouldBe(Path.GetFullPath(Path.Combine(root, ".antiphon", "live-lane-capture", "context.json")));
            using (var doc = JsonDocument.Parse(File.ReadAllText(path)))
            {
                var context = doc.RootElement;
                context.GetProperty("mode").GetString().ShouldBe("baseline");
                context.GetProperty("exportRequired").GetBoolean().ShouldBeFalse();
                context.GetProperty("reason").GetString().ShouldBe("baseline-no-producer");
                context.GetProperty("producerProvenance").ValueKind.ShouldBe(JsonValueKind.Null);
                context.GetProperty("protectedPaths").EnumerateArray().Select(p => p.GetString()).ShouldBe(LiveLaneCensus.ProtectedPaths);
                context.GetProperty("allowedSeedPaths").EnumerateArray().Select(p => p.GetString()).ShouldBe(LiveLaneCensus.AllowedSeedPaths);
            }
            var read = LiveLaneContext.Read(path);
            read.Mode.ShouldBe("baseline");
            read.SourceSha.ShouldBe(new string('a', 40));
            read.PreS1BaseSha.ShouldBe(LiveLaneCensus.PreS1BaseSha);
            read.LandingTarget.ShouldBe(LiveLaneCensus.LandingTargetRef);
            read.FixturePath.ShouldBe(LiveLaneManifestFiles.FixtureRel);
            read.CandidateRoot.ShouldBe(root);
            read.CheckpointResults.ShouldBe(results);
            read.ProducerProvenance.ShouldBeNull();
            Should.Throw<InvalidOperationException>(() => LiveLaneCensus.WriteGroupContext(root, "other", "", "", results)).Message.ShouldBe("unknown-group");
            Should.Throw<InvalidOperationException>(() => LiveLaneCensus.WriteGroupContext(root, "S1a", "not-a-sha", "", results)).Message.ShouldBe("census-base-required");
        }
        finally
        {
            scratch.Clear();
        }
    }
}
