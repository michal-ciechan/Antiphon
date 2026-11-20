using System.Text;
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
    private static readonly string CutA = new('a', 40);
    private static readonly string CutB = new('b', 40);
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-11-19T00:00:00Z");
    private const string AnchorInstant = "2026-11-18T00:00:00Z";
    private const string NowInstant = "2026-11-19T00:00:00Z";
    private const string FullInstant = "2026-10-20T00:00:00Z";

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

    [Test]
    public void EscapedLoneSurrogateInAValueIsChainInvalid()
    {
        var json = YoungChain(trusted: 2, anchorAge: TimeSpan.FromDays(1))
            .Replace("\"head\"", "\"\\uD800\"", StringComparison.Ordinal);
        var answer = RcTagBacking.Test(json);
        answer.Answer.ShouldBe("not-backed(chain-invalid)");
        answer.GateCredit.ShouldBeFalse();
    }

    [Test]
    public void EscapedLoneSurrogateInANameIsChainInvalid()
    {
        var json = YoungChain(trusted: 2, anchorAge: TimeSpan.FromDays(1))
            .Replace("\"runId\"", "\"\\uD800\"", StringComparison.Ordinal);
        var answer = RcTagBacking.Test(json);
        answer.Answer.ShouldBe("not-backed(chain-invalid)");
        answer.GateCredit.ShouldBeFalse();
    }

    [Test]
    public void RawLoneSurrogateIsChainInvalid()
    {
        var json = YoungChain(trusted: 2, anchorAge: TimeSpan.FromDays(1))
            .Replace("\"head\"", "\"he\uD800ad\"", StringComparison.Ordinal);
        var answer = RcTagBacking.Test(json);
        answer.Answer.ShouldBe("not-backed(chain-invalid)");
        answer.GateCredit.ShouldBeFalse();
    }

    [Test]
    public void NullInputIsChainInvalid()
    {
        var answer = RcTagBacking.Test(null);
        answer.Answer.ShouldBe("not-backed(chain-invalid)");
        answer.GateCredit.ShouldBeFalse();
    }

    [Test]
    public void EmptyInputIsChainInvalid()
    {
        var answer = RcTagBacking.Test("");
        answer.Answer.ShouldBe("not-backed(chain-invalid)");
        answer.GateCredit.ShouldBeFalse();
    }

    [Test]
    public void DeepNestingIsChainInvalid()
    {
        var json = new StringBuilder();
        for (var i = 0; i < 32; i++)
            json.Append("{\"a\":");
        json.Append('1');
        for (var i = 0; i < 32; i++)
            json.Append('}');
        var answer = RcTagBacking.Test(json.ToString());
        answer.Answer.ShouldBe("not-backed(chain-invalid)");
        answer.GateCredit.ShouldBeFalse();
    }

    [Test]
    public void InvalidUtf8BytesAreChainInvalid()
    {
        var bytes = Encoding.UTF8.GetBytes(YoungChain(trusted: 2, anchorAge: TimeSpan.FromDays(1)));
        bytes[0] = 0xFF;
        var answer = RcTagBacking.Test(Encoding.UTF8.GetString(bytes));
        answer.Answer.ShouldBe("not-backed(chain-invalid)");
        answer.GateCredit.ShouldBeFalse();
    }

    [Test]
    public void DateLessAnchorTimeIsChainInvalid()
    {
        var json = Swap(YoungChain(trusted: 2, anchorAge: TimeSpan.FromDays(1)), AnchorInstant, "00:00Z");
        json = Swap(json, NowInstant, "00:00Z");
        var answer = RcTagBacking.Test(json);
        answer.Answer.ShouldBe("not-backed(chain-invalid)");
        answer.GateCredit.ShouldBeFalse();
    }

    [Test]
    public void DateLessUtcNowIsChainInvalid()
    {
        var answer = RcTagBacking.Test(Swap(YoungChain(trusted: 2, anchorAge: TimeSpan.FromDays(1)), NowInstant, "00:00Z"));
        answer.Answer.ShouldBe("not-backed(chain-invalid)");
        answer.GateCredit.ShouldBeFalse();
    }

    [Test]
    public void MonthNameTimestampIsChainInvalid()
    {
        var answer = RcTagBacking.Test(Swap(YoungChain(trusted: 2, anchorAge: TimeSpan.FromDays(1)), AnchorInstant, "18 Nov 2026 00:00:00Z"));
        answer.Answer.ShouldBe("not-backed(chain-invalid)");
        answer.GateCredit.ShouldBeFalse();
    }

    [Test]
    public void DateOnlyTimestampIsChainInvalid()
    {
        var answer = RcTagBacking.Test(Swap(YoungChain(trusted: 2, anchorAge: TimeSpan.FromDays(1)), AnchorInstant, "2026-11-18Z"));
        answer.Answer.ShouldBe("not-backed(chain-invalid)");
        answer.GateCredit.ShouldBeFalse();
    }

    [Test]
    public void OffsetTimestampIsChainInvalid()
    {
        var answer = RcTagBacking.Test(Swap(YoungChain(trusted: 2, anchorAge: TimeSpan.FromDays(1)), AnchorInstant, "2026-11-18T00:00:00+00:00"));
        answer.Answer.ShouldBe("not-backed(chain-invalid)");
        answer.GateCredit.ShouldBeFalse();
    }

    [Test]
    public void LocalTimestampIsChainInvalid()
    {
        var answer = RcTagBacking.Test(Swap(YoungChain(trusted: 2, anchorAge: TimeSpan.FromDays(1)), AnchorInstant, "2026-11-18T00:00:00"));
        answer.Answer.ShouldBe("not-backed(chain-invalid)");
        answer.GateCredit.ShouldBeFalse();
    }

    [Test]
    public void SpaceSeparatedTimestampIsChainInvalid()
    {
        var answer = RcTagBacking.Test(Swap(YoungChain(trusted: 2, anchorAge: TimeSpan.FromDays(1)), AnchorInstant, "2026-11-18 00:00:00Z"));
        answer.Answer.ShouldBe("not-backed(chain-invalid)");
        answer.GateCredit.ShouldBeFalse();
    }

    [Test]
    public void LowercaseTTimestampIsChainInvalid()
    {
        var answer = RcTagBacking.Test(Swap(YoungChain(trusted: 2, anchorAge: TimeSpan.FromDays(1)), AnchorInstant, "2026-11-18t00:00:00Z"));
        answer.Answer.ShouldBe("not-backed(chain-invalid)");
        answer.GateCredit.ShouldBeFalse();
    }

    [Test]
    public void DateLessTimeOnExactFullIsChainInvalid()
    {
        var answer = RcTagBacking.Test(Swap(ExactFull(), FullInstant, "00:00Z"));
        answer.Answer.ShouldBe("not-backed(chain-invalid)");
        answer.GateCredit.ShouldBeFalse();
    }

    [Test]
    public void OffsetTimeOnExactFullIsChainInvalid()
    {
        var answer = RcTagBacking.Test(Swap(ExactFull(), FullInstant, "2026-10-20T00:00:00+00:00"));
        answer.Answer.ShouldBe("not-backed(chain-invalid)");
        answer.GateCredit.ShouldBeFalse();
    }

    [Test]
    public void LocalTimeOnExactFullIsChainInvalid()
    {
        var answer = RcTagBacking.Test(Swap(ExactFull(), FullInstant, "2026-10-20T00:00:00"));
        answer.Answer.ShouldBe("not-backed(chain-invalid)");
        answer.GateCredit.ShouldBeFalse();
    }

    [Test]
    public void EightFractionalDigitsAreChainInvalid()
    {
        var answer = RcTagBacking.Test(Swap(ExactFull(), FullInstant, "2026-10-20T00:00:00.12345678Z"));
        answer.Answer.ShouldBe("not-backed(chain-invalid)");
        answer.GateCredit.ShouldBeFalse();
    }

    [Test]
    public void FractionalUtcSecondsOnExactFullStayFull()
    {
        var one = RcTagBacking.Test(Swap(ExactFull(), FullInstant, "2026-10-20T00:00:00.1Z"));
        var seven = RcTagBacking.Test(Swap(ExactFull(), FullInstant, "2026-10-20T00:00:00.1234567Z"));
        one.Answer.ShouldBe("full");
        seven.Answer.ShouldBe("full");
        one.GateCredit.ShouldBeFalse();
        seven.GateCredit.ShouldBeFalse();
    }

    [Test]
    public void DuplicateTrustedCutCountsOnce()
    {
        var cut = Cut("cut-a", CutA);
        var json = WithCuts(YoungChain(trusted: 99, anchorAge: TimeSpan.FromDays(1)), "[" + cut + "," + cut + "]");
        var answer = RcTagBacking.Test(json);
        answer.Answer.ShouldBe("not-backed(shadow-trust-absent)");
        answer.GateCredit.ShouldBeFalse();
    }

    [Test]
    public void TwoDistinctTrustedCutsDoNotUnlockTheChain()
    {
        var json = WithCuts(
            YoungChain(trusted: 0, anchorAge: TimeSpan.FromDays(1)),
            "[" + Cut("cut-a", CutA) + "," + Cut("cut-b", CutB) + "]");
        var answer = RcTagBacking.Test(json);
        answer.Answer.ShouldBe("not-backed(chain-form-disabled-until-trusted-cuts)");
        answer.GateCredit.ShouldBeFalse();
    }

    [Test]
    public void NotWindowsIsNotBacked()
    {
        var answer = RcTagBacking.Test(Swap(ExactFull(), "\"platform\":\"windows\"", "\"platform\":\"linux\""));
        answer.Answer.ShouldBe("not-backed(not-windows)");
        answer.GateCredit.ShouldBeFalse();
    }

    [Test]
    public void AnchorHasCarriesIsNotBacked()
    {
        var full = Link("full-run", Tagged, "executed-full", origin: null, At(TimeSpan.FromDays(30)),
            allExecuted: true, carriedCount: 3);
        var answer = RcTagBacking.Test(Document(0, new[] { full }, new[] { new TagChainCommit(Tagged, Array.Empty<string>()) }));
        answer.Answer.ShouldBe("not-backed(anchor-has-carries)");
        answer.GateCredit.ShouldBeFalse();
    }

    [Test]
    public void FutureDatedAnchorIsNotBacked()
    {
        var answer = RcTagBacking.Test(YoungChain(trusted: 2, anchorAge: TimeSpan.FromMinutes(-5)));
        answer.Answer.ShouldBe("not-backed(full-anchor-future)");
        answer.GateCredit.ShouldBeFalse();
    }

    [Test]
    public void ChainCycleIsNotBacked()
    {
        var a = Link("run-a", Tagged, "carried", "run-b", completed: null);
        var b = Link("run-b", Mid, "carried", "run-a", completed: null);
        var answer = RcTagBacking.Test(Document(2, new[] { a, b }, Lineage(Anchor)));
        answer.Answer.ShouldBe("not-backed(chain-cycle-or-depth)");
        answer.GateCredit.ShouldBeFalse();
    }

    [Test]
    public void DuplicateJsonNamesAreChainInvalid()
    {
        const string root = "{\"format\":\"antiphon-rc-tag-chain-v1\"";
        var json = YoungChain(trusted: 2, anchorAge: TimeSpan.FromDays(1));
        json.StartsWith(root, StringComparison.Ordinal).ShouldBeTrue();
        json = "{\"format\":\"antiphon-rc-tag-chain-v1\",\"format\":\"antiphon-rc-tag-chain-v1\"" + json.Substring(root.Length);
        var answer = RcTagBacking.Test(json);
        answer.Answer.ShouldBe("not-backed(chain-invalid)");
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

    private static string ExactFull()
    {
        var full = Link("full-run", Tagged, "executed-full", origin: null, At(TimeSpan.FromDays(30)),
            allExecuted: true);
        return Document(0, new[] { full }, new[] { new TagChainCommit(Tagged, Array.Empty<string>()) });
    }

    private static string Swap(string json, string from, string to)
    {
        var index = json.IndexOf(from, StringComparison.Ordinal);
        if (index < 0)
            throw new InvalidOperationException("fixture marker missing: " + from);
        return json.Remove(index, from.Length).Insert(index, to);
    }

    private static string WithCuts(string json, string cutsArray)
    {
        var end = json.LastIndexOf('}');
        if (end < 0)
            throw new InvalidOperationException("fixture json");
        return json.Insert(end, ",\"trustedCuts\":" + cutsArray);
    }

    private static string Cut(string runId, string sha) =>
        "{\"runId\":\"" + runId + "\",\"cutSha\":\"" + sha + "\",\"admitted\":true,\"executedSamples\":1}";

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
