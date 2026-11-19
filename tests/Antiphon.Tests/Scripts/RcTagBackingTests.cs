using System.Text.Json;
using Antiphon.Nightly;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-1820 S5. A human tag is backed by an all-executed Windows Full of the tagged SHA,
/// or, once two trusted shadow cuts exist, by a carry chain to a Full younger than 7 days.
/// </summary>
[Category("Unit")]
[Timeout(60_000)]
public sealed class RcTagBackingTests
{
    private static readonly string Tagged = new('1', 40);
    private static readonly string Mid = new('2', 40);
    private static readonly string Anchor = new('3', 40);
    private static readonly string Stranger = new('4', 40);
    private static readonly string OffPath = new('5', 40);
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-11-19T00:00:00Z");

    [Test]
    public void ChainOlderThanSevenDaysIsNotBacked()
    {
        var answer = RcTagBacking.Test(YoungChain(trusted: 2, anchorAge: TimeSpan.FromDays(8)));
        answer.Answer.ShouldBe("not-backed(chain-older-than-seven-days)");
        answer.GateCredit.ShouldBeFalse();
    }

    [Test]
    public void ShadowOnlyChainIsNotBacked()
    {
        var answer = RcTagBacking.Test(YoungChain(trusted: 1, anchorAge: TimeSpan.FromDays(1), headDisposition: "shadow"));
        answer.Answer.ShouldBe("not-backed(shadow-trust-absent)");
        answer.Kind.ShouldBe("not-backed");
        answer.GateCredit.ShouldBeFalse();
    }

    [Test]
    public void CarryWithoutAnchorIsNotBacked()
    {
        var head = Link("head", Tagged, "carried", origin: null, completed: null);
        var answer = RcTagBacking.Test(Document(2, new[] { head }, Lineage(Anchor)));
        answer.Answer.ShouldBe("not-backed(carry-without-anchor)");
        answer.GateCredit.ShouldBeFalse();
    }

    [Test]
    public void ExactlySevenDaysIsNotBacked()
    {
        var answer = RcTagBacking.Test(YoungChain(trusted: 2, anchorAge: TimeSpan.FromDays(7)));
        answer.Answer.ShouldBe("not-backed(chain-older-than-seven-days)");
        answer.GateCredit.ShouldBeFalse();
    }

    [Test]
    public void MissingLinkIsNotBacked()
    {
        var head = Link("head", Tagged, "carried", origin: "gone", completed: null);
        var answer = RcTagBacking.Test(Document(2, new[] { head }, Lineage(Anchor)));
        answer.Answer.ShouldBe("not-backed(missing-link)");
        answer.GateCredit.ShouldBeFalse();
    }

    [Test]
    public void SeedAnchorIsNotBacked()
    {
        var head = Link("head", Tagged, "carried", "seed-run", completed: null);
        var seed = Link("seed-run", Anchor, "executed-full", origin: null, At(TimeSpan.FromDays(1)),
            seed: true, allExecuted: true);
        var answer = RcTagBacking.Test(Document(2, new[] { head, seed }, Lineage(Anchor)));
        answer.Answer.ShouldBe("not-backed(seed-is-not-credit)");
        answer.GateCredit.ShouldBeFalse();
    }

    [Test]
    public void AnchorNotAncestorIsNotBacked()
    {
        var head = Link("head", Tagged, "carried", "anchor-run", completed: null);
        var anchor = Link("anchor-run", Stranger, "executed-full", origin: null, At(TimeSpan.FromDays(1)),
            allExecuted: true);
        var commits = new[]
        {
            new TagChainCommit(Tagged, new[] { Mid }),
            new TagChainCommit(Mid, new[] { OffPath }),
            new TagChainCommit(Stranger, Array.Empty<string>())
        };
        var answer = RcTagBacking.Test(Document(2, new[] { head, anchor }, commits));
        answer.Answer.ShouldBe("not-backed(anchor-not-ancestor)");
        answer.GateCredit.ShouldBeFalse();
    }

    [Test]
    public void ExactFullOfTaggedShaIsFull()
    {
        var full = Link("full-run", Tagged, "executed-full", origin: null, At(TimeSpan.FromDays(30)),
            allExecuted: true);
        var answer = RcTagBacking.Test(Document(0, new[] { full }, new[] { new TagChainCommit(Tagged, Array.Empty<string>()) }));
        answer.Answer.ShouldBe("full");
        answer.Kind.ShouldBe("full");
        answer.GateCredit.ShouldBeFalse();
    }

    [Test]
    public void ChainWithinSevenDaysWithTwoTrustedCutsIsBacked()
    {
        var age = TimeSpan.FromDays(7) - TimeSpan.FromSeconds(1);
        var answer = RcTagBacking.Test(YoungChain(trusted: 2, anchorAge: age));
        answer.Kind.ShouldBe("chain-to-full");
        answer.AnchorSha.ShouldBe(Anchor);
        answer.Age.ShouldBe(age);
        answer.Answer.ShouldBe("chain-to-full(" + Anchor + "," + RcTagBackingResult.FormatAge(age) + ")");
        answer.GateCredit.ShouldBeFalse();
    }

    [Test]
    public void OpenFailureIsNotBacked()
    {
        var answer = RcTagBacking.Test(YoungChain(trusted: 2, anchorAge: TimeSpan.FromDays(1), headOpenFailure: true));
        answer.Answer.ShouldBe("not-backed(open-failure)");
    }

    [Test]
    public void ShadowDiscrepancyIsNotBacked()
    {
        var answer = RcTagBacking.Test(YoungChain(trusted: 2, anchorAge: TimeSpan.FromDays(1), anchorDiscrepancy: true));
        answer.Answer.ShouldBe("not-backed(shadow-discrepancy)");
    }

    [Test]
    public void UnadmittedLinkIsNotBacked()
    {
        var answer = RcTagBacking.Test(YoungChain(trusted: 2, anchorAge: TimeSpan.FromDays(1), anchorAdmitted: false));
        answer.Answer.ShouldBe("not-backed(link-not-admitted)");
    }

    [Test]
    public void GateCreditClaimIsNotBacked()
    {
        var full = Link("full-run", Tagged, "executed-full", origin: null, At(TimeSpan.FromDays(1)),
            allExecuted: true, gateCredit: true);
        var answer = RcTagBacking.Test(Document(2, new[] { full }, new[] { new TagChainCommit(Tagged, Array.Empty<string>()) }));
        answer.Answer.ShouldBe("not-backed(gate-credit-claimed)");
        answer.GateCredit.ShouldBeFalse();
    }

    private static string YoungChain(int trusted, TimeSpan anchorAge, string headDisposition = "carried",
        bool headOpenFailure = false, bool anchorDiscrepancy = false, bool anchorAdmitted = true)
    {
        var head = Link("head", Tagged, headDisposition, "anchor-run", completed: null, openFailure: headOpenFailure);
        var anchor = Link("anchor-run", Anchor, "executed-full", origin: null, At(anchorAge),
            allExecuted: true, shadowDiscrepancy: anchorDiscrepancy, admitted: anchorAdmitted);
        return Document(trusted, new[] { head, anchor }, Lineage(Anchor));
    }

    private static string Document(int trusted, TagChainEnvelope[] envelopes, TagChainCommit[] commits) =>
        JsonSerializer.Serialize(new
        {
            format = "antiphon-rc-tag-chain-v1",
            taggedSha = Tagged,
            utcNow = Now.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture),
            trustedShadowCuts = trusted,
            envelopes,
            commits
        });

    private static TagChainCommit[] Lineage(string anchorSha) => new[]
    {
        new TagChainCommit(Tagged, new[] { anchorSha }),
        new TagChainCommit(anchorSha, Array.Empty<string>())
    };

    private static string At(TimeSpan age) =>
        Now.Subtract(age).UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss'Z'");

    private static TagChainEnvelope Link(string runId, string source, string disposition, string? origin, string? completed,
        bool seed = false, bool admitted = true, bool openFailure = false, bool shadowDiscrepancy = false,
        bool gateCredit = false, bool allExecuted = false, int carriedCount = 0) =>
        new(runId, source, disposition, origin, completed, seed, admitted, openFailure, shadowDiscrepancy,
            gateCredit, allExecuted, carriedCount);

    private sealed record TagChainEnvelope(
        string runId,
        string sourceSha,
        string disposition,
        string? originRunId,
        string? anchorCompletedAtUtc,
        bool seed,
        bool admitted,
        bool openFailure,
        bool shadowDiscrepancy,
        bool gateCredit,
        bool allExecuted,
        int carriedCount)
    {
        public string format { get; } = "antiphon-rc-tag-envelope-v1";
        public string platform { get; } = "windows";
    }

    private sealed record TagChainCommit(string sha, string[] parents);
}
