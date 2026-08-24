using Shouldly;
using TUnit.Core;

namespace Antiphon.SessionRunner.Tests;

[Category("Unit")]
public class TranscriptClaimRegistryTests
{
    [Test]
    public void Exact_claim_displaces_a_heuristic_claim_and_reports_the_previous_owner()
    {
        var registry = new TranscriptClaimRegistry();
        var victim = Guid.NewGuid();
        var thief = Guid.NewGuid();
        var path = Path.Combine(Path.GetTempPath(), victim.ToString("D") + ".jsonl");

        registry.TryClaim(path, thief).Claimed.ShouldBeTrue();
        registry.OwnerOf(path)!.Value.Strength.ShouldBe(ClaimStrength.Heuristic);

        Guid? displaced = null;
        registry.ClaimDisplaced += (_, prev, next) => displaced = prev;
        var result = registry.TryClaim(path, victim);
        result.Claimed.ShouldBeTrue();
        result.Displaced.ShouldBe(thief);
        displaced.ShouldBe(thief);
        registry.OwnerOf(path)!.Value.ShouldBe((victim, ClaimStrength.Exact));
    }

    [Test]
    public void Heuristic_claim_never_displaces_an_exact_claim()
    {
        var registry = new TranscriptClaimRegistry();
        var owner = Guid.NewGuid();
        var path = Path.Combine(Path.GetTempPath(), owner.ToString("D") + ".jsonl");
        registry.TryClaim(path, owner).Claimed.ShouldBeTrue();
        registry.OwnerOf(path)!.Value.Strength.ShouldBe(ClaimStrength.Exact);

        registry.TryClaim(path, Guid.NewGuid()).Claimed.ShouldBeFalse();
        registry.OwnerOf(path)!.Value.Owner.ShouldBe(owner);
    }

    [Test]
    public void Heuristic_vs_heuristic_stays_first_wins()
    {
        var registry = new TranscriptClaimRegistry();
        var first = Guid.NewGuid();
        var path = Path.Combine(Path.GetTempPath(), "updates.jsonl");
        registry.TryClaim(path, first).Claimed.ShouldBeTrue();
        registry.TryClaim(path, Guid.NewGuid()).Claimed.ShouldBeFalse();
        registry.OwnerOf(path)!.Value.Owner.ShouldBe(first);
        registry.OwnerOf(path)!.Value.Strength.ShouldBe(ClaimStrength.Heuristic);
    }

    [Test]
    public void Same_owner_reclaim_is_idempotent_and_upgrades_to_exact_when_it_is_the_namesake()
    {
        var registry = new TranscriptClaimRegistry();
        var owner = Guid.NewGuid();
        var path = Path.Combine(Path.GetTempPath(), owner.ToString("D") + ".jsonl");
        registry.ForceClaimForTests(path, owner, ClaimStrength.Heuristic);
        registry.TryClaim(path, owner).Claimed.ShouldBeTrue();
        registry.OwnerOf(path)!.Value.Strength.ShouldBe(ClaimStrength.Exact);
        registry.TryClaim(path, owner).Displaced.ShouldBeNull();
    }

    [Test]
    public void Strength_is_derived_from_the_basename_not_asserted()
    {
        var registry = new TranscriptClaimRegistry();
        var owner = Guid.NewGuid();
        var stranger = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("D") + ".jsonl");
        registry.TryClaim(stranger, owner).Claimed.ShouldBeTrue();
        registry.OwnerOf(stranger)!.Value.Strength.ShouldBe(ClaimStrength.Heuristic);
    }

    [Test]
    public void Canonical_path_variants_share_one_claim()
    {
        var registry = new TranscriptClaimRegistry();
        var owner = Guid.NewGuid();
        var dir = Path.Combine(Path.GetTempPath(), $"claim-canon-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var file = Path.Combine(dir, "a.jsonl");
            File.WriteAllText(file, "");
            var dotted = Path.Combine(dir, ".", "a.jsonl");
            registry.TryClaim(file, owner).Claimed.ShouldBeTrue();
            registry.TryClaim(dotted, Guid.NewGuid()).Claimed.ShouldBeFalse();
            registry.OwnerOf(dotted)!.Value.Owner.ShouldBe(owner);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
        }
    }
}
