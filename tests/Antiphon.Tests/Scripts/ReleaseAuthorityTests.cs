using System.Collections.Specialized;
using System.Text;
using Antiphon.Nightly;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

// The release authority's pure rules (Antiphon.Nightly.ReleaseAuthority), called in-process: the pinned policy reader,
// required UIDs, evidence containment, the card correlation line, instants and the canonical digest text. The
// git-backed freeze, pinned reload and ledger run through test-release-gate.ps1 until the publisher is compiled.
[Category("Unit")]
public sealed class ReleaseAuthorityTests
{
    [Test]
    public void Shipped_policy_reads_as_the_rc_authority()
    {
        var bytes = File.ReadAllBytes(Path.Combine(C1514Support.RepoRoot(), "tests", "test-execution-policy.json"));
        var policy = ReleaseAuthority.PolicyFromBytes(bytes);
        policy.RequiredSuites.ShouldContain("antiphon");
        policy.RequiredSuites.ShouldContain("markdown-pdf", "schema 3 adds markdown-pdf");
        policy.Exclusions.ShouldNotBeEmpty("rc declares windows exclusions");
        policy.PolicyHash.ShouldBe(policy.Object.PolicyHash, "stated hash");
        ReleaseAuthority.PolicyFromBytes(Encoding.UTF8.GetBytes("﻿" + Encoding.UTF8.GetString(bytes))).PolicyHash.ShouldBe(policy.PolicyHash, "a BOM is not content");
    }

    [Test]
    public void Policy_refusals_are_stable_tokens()
    {
        Refusal(() => ReleaseAuthority.PolicyFromBytes(Encoding.UTF8.GetBytes("{oops"))).ShouldBe("policy-unparseable");
        Refusal(() => ReleaseAuthority.PolicyFromBytes(Encoding.UTF8.GetBytes("[1]"))).ShouldBe("policy-unparseable");
        Refusal(() => ReleaseAuthority.PolicyFromBytes(Encoding.UTF8.GetBytes("{\"schemaVersion\":9}"))).ShouldBe("policy-schema-unsupported");
        Refusal(() => ReleaseAuthority.PolicyFromBytes(Encoding.UTF8.GetBytes("{\"schemaVersion\":\"2\"}"))).ShouldBe("policy-schema-unsupported");
        Refusal(() => ReleaseAuthority.PolicyFromBytes(Encoding.UTF8.GetBytes("{\"schemaVersion\":2,\"policyHash\":\"0\"}"))).ShouldBe("policy-hash-stale");
    }

    [Test]
    public void Required_uids_leave_out_pinned_class_and_method_rows()
    {
        var nodes = new object[]
        {
            new OrderedDictionary { { "uid", "a1" }, { "class", "Ns.AlphaTests" }, { "method", "One" } },
            new OrderedDictionary { { "uid", "a2" }, { "class", "Ns.AlphaTests" }, { "method", "Two" } },
            new OrderedDictionary { { "uid", "b1" }, { "class", "Ns.BetaTests" }, { "method", "One" } },
            new OrderedDictionary { { "uid", "c1" }, { "class", "Ns.GammaTests" }, { "method", "One" } },
        };
        var exclusions = new object[]
        {
            new AuthorityExclusion { Suite = "antiphon", Class = "AlphaTests", Methods = new[] { "two" } },
            new AuthorityExclusion { Suite = "ANTIPHON", Class = "Ns.BetaTests" },
            new AuthorityExclusion { Suite = "other", Class = "GammaTests" },
        };
        string.Join(",", ReleaseAuthority.RequiredUids(nodes, exclusions, "antiphon")).ShouldBe("a1,c1", "method row, whole class, other suite");
    }

    [Test]
    public void Evidence_paths_stay_under_the_candidate_root()
    {
        var root = Path.Combine(Path.GetTempPath(), "antiphon-authority-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "evidence"));
        try
        {
            ReleaseAuthority.ResolveEvidencePath(root, "evidence/a.trx").ShouldBe(Path.GetFullPath(Path.Combine(root, "evidence", "a.trx")));
            ReleaseAuthority.ResolveEvidencePath(root, "../outside.trx").ShouldBe("", "escape");
            ReleaseAuthority.ResolveEvidencePath(root, Path.Combine(root, "evidence", "a.trx")).ShouldBe("", "rooted");
            ReleaseAuthority.ResolveEvidencePath(root, " ").ShouldBe("", "blank");
        }
        finally { Directory.Delete(root, true); }
    }

    [Test]
    public void Card_correlation_names_exactly_the_run()
    {
        var expected = new OrderedDictionary { { "intentId", "i" }, { "candidateId", "c" }, { "sha", "s" }, { "runId", "r" } };
        ReleaseAuthority.CardCorrelation("body\nrelease-correlation: intentId=i candidateId=c sha=s runId=r\r\nmore", expected).ShouldBeTrue();
        ReleaseAuthority.CardCorrelation("release-correlation: intentId=i candidateId=c sha=s", expected).ShouldBeFalse("a key missing");
        ReleaseAuthority.CardCorrelation("release-correlation: intentId=i candidateId=c sha=s runId=r runId=r", expected).ShouldBeFalse("a key repeated");
        ReleaseAuthority.CardCorrelation("release-correlation: IntentId=i candidateId=c sha=s runId=r", expected).ShouldBeFalse("keys are case-sensitive");
        ReleaseAuthority.CardCorrelation("release-correlation: intentId=i candidateId=c sha=s runId=r\nrelease-correlation: x=y", expected).ShouldBeFalse("a second line");
        ReleaseAuthority.CardCorrelation(42L, expected).ShouldBeFalse("not a string");
    }

    [Test]
    public void Instants_and_canonical_text()
    {
        ReleaseAuthority.Instant("2026-09-23T08:30:30Z").ShouldBe(new DateTime(2026, 9, 23, 8, 30, 30, DateTimeKind.Utc));
        ReleaseAuthority.Instant("2026-09-23T08:30:30").ShouldBe(new DateTime(2026, 9, 23, 8, 30, 30, DateTimeKind.Utc), "no zone is UTC");
        ReleaseAuthority.Instant(new object[] { "2026-09-23T08:30:30Z" }).ShouldBeNull("an array is never an instant");
        ReleaseAuthority.Instant(5L).ShouldBeNull("a number is never an instant");
        PlainJson.Canonical(new OrderedDictionary { { "b", 1L }, { "a", new object[] { "x\ty", true } }, { "", "dropped" }, { "c", null } })
            .ShouldBe("{\"a\":[\"x\\ty\",true],\"b\":1,\"c\":null}");
        ReleaseAuthority.GitBlobId(Encoding.ASCII.GetBytes("hello\n")).ShouldBe("ce013625030ba8dba906f756967f9e9ca394464a", "git hash-object");
    }

    private static string Refusal(Action action) => Should.Throw<NightlyRefusal>(action).Message;
    private static string Refusal<T>(Func<T> action) => Should.Throw<NightlyRefusal>(() => { action(); }).Message;
}
