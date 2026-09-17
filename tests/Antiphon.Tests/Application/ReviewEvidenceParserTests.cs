using Antiphon.Server.Application.Services;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Unit")]
public class ReviewEvidenceParserTests
{
    private static readonly Guid Subject = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
    private const string Sha40 = "0123456789abcdef0123456789abcdef01234567";
    private const string Sha64 = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    [Test]
    [Arguments(Sha40)]
    [Arguments(Sha64)]
    [Arguments("0123456789ABCDEF0123456789ABCDEF01234567")]
    public void C488_ReviewBlockGrammar(string sha)
    {
        var parsed = ReviewEvidence.TryParse(Report(Block(Subject, sha)));
        parsed.Found.ShouldBeTrue();
        parsed.Usable.ShouldBeTrue();
        parsed.SubjectTaskId.ShouldBe(Subject);
        parsed.ReviewedSourceSha.ShouldBe(sha.ToLowerInvariant());
    }

    [Test]
    [Arguments("")]
    [Arguments("abc")]
    [Arguments("deadbee")]
    [Arguments("HEAD")]
    [Arguments("refs/heads/master")]
    [Arguments("0123456789abcdef0123456789abcdef0123456\n7")]
    public void C488_ReviewBlockGrammar_rejects_invalid_sha(string sha)
    {
        var parsed = ReviewEvidence.TryParse(Report(Block(Subject, sha)));
        parsed.Found.ShouldBeTrue();
        parsed.Usable.ShouldBeFalse();
    }

    [Test]
    public void C488_DuplicateBlocksRefuse()
    {
        var block = Block(Subject, Sha40);
        var parsed = ReviewEvidence.TryParse(Report(block + "\n" + block));
        parsed.Found.ShouldBeTrue();
        parsed.Usable.ShouldBeFalse();
        parsed.Warning.ShouldBe("review_evidence_duplicate");
    }

    [Test]
    public void C488_QuotedAndFencedBlocksAreNotEvidence()
    {
        var quoted = "> --- review evidence ---\n> subjectTaskId: " + Subject + "\n> reviewedSourceSha: " + Sha40;
        ReviewEvidence.TryParse(Report(quoted)).Found.ShouldBeFalse();
        var fenced = "```\n--- review evidence ---\nsubjectTaskId: " + Subject + "\nreviewedSourceSha: " + Sha40 + "\n```";
        ReviewEvidence.TryParse(Report(fenced)).Found.ShouldBeFalse();
    }

    [Test]
    public void C488_MissingBlockIsNotFound()
    {
        ReviewEvidence.TryParse(Report("no evidence here")).Found.ShouldBeFalse();
        ReviewEvidence.TryParse(null).Found.ShouldBeFalse();
    }

    // CARD-0544 V-5 / G-44: one well-formed declaration parses; anything else is Unknown, never Full.
    [Test]
    public void C544_ScopeGrammar()
    {
        var rows = new (string Row, string? ScopeLine, Antiphon.Server.Domain.Enums.VerificationScope Expected)[]
        {
            ("full", "ordinaryScopeCompleted: Full", Antiphon.Server.Domain.Enums.VerificationScope.Full),
            ("interim", "ordinaryScopeCompleted: Interim", Antiphon.Server.Domain.Enums.VerificationScope.Interim),
            ("none", "ordinaryScopeCompleted: None", Antiphon.Server.Domain.Enums.VerificationScope.None),
            ("case-insensitive", "OrdinaryScopeCompleted: full", Antiphon.Server.Domain.Enums.VerificationScope.Full),
            ("missing", null, Antiphon.Server.Domain.Enums.VerificationScope.Unknown),
            ("empty", "ordinaryScopeCompleted:", Antiphon.Server.Domain.Enums.VerificationScope.Unknown),
            ("misspelled", "ordinaryScopeCompleted: Fulll", Antiphon.Server.Domain.Enums.VerificationScope.Unknown),
            ("numeric", "ordinaryScopeCompleted: 3", Antiphon.Server.Domain.Enums.VerificationScope.Unknown),
            ("sentence", "ordinaryScopeCompleted: Full except client", Antiphon.Server.Domain.Enums.VerificationScope.Unknown),
            ("indented", "  ordinaryScopeCompleted: Full", Antiphon.Server.Domain.Enums.VerificationScope.Unknown),
        };
        foreach (var (row, scopeLine, expected) in rows)
        {
            var block = Block(Subject, Sha40) + (scopeLine is null ? "" : "\n" + scopeLine);
            var parsed = ReviewEvidence.TryParse(Report(block));
            parsed.Found.ShouldBeTrue(row);
            parsed.Usable.ShouldBeTrue(row);
            parsed.Scope.ShouldBe(expected, row);
        }

        // Existing fenced/quoted grammar: a scope inside a non-evidence block never counts.
        var fenced = "```\n" + Block(Subject, Sha40) + "\nordinaryScopeCompleted: Full\n```";
        ReviewEvidence.TryParse(Report(fenced)).Scope.ShouldBe(Antiphon.Server.Domain.Enums.VerificationScope.Unknown, "fenced");
        ReviewEvidence.CapToRound(Antiphon.Server.Domain.Enums.VerificationScope.Full, Antiphon.Server.Domain.Enums.VerificationRound.Interim)
            .ShouldBe(Antiphon.Server.Domain.Enums.VerificationScope.Interim, "cap");
    }

    // CARD-0544 V-5 / G-45: a declaration repeated, equal or conflicting, is unusable Unknown.
    [Test]
    public void C544_DuplicateScope()
    {
        var rows = new (string Row, string Lines)[]
        {
            ("equal", "ordinaryScopeCompleted: Full\nordinaryScopeCompleted: Full"),
            ("conflicting-full-last", "ordinaryScopeCompleted: Interim\nordinaryScopeCompleted: Full"),
            ("conflicting-full-first", "ordinaryScopeCompleted: Full\nordinaryScopeCompleted: None"),
        };
        foreach (var (row, lines) in rows)
        {
            var parsed = ReviewEvidence.TryParse(Report(Block(Subject, Sha40) + "\n" + lines));
            parsed.Found.ShouldBeTrue(row);
            parsed.Scope.ShouldBe(Antiphon.Server.Domain.Enums.VerificationScope.Unknown, row);
        }

        var twoBlocks = Block(Subject, Sha40) + "\nordinaryScopeCompleted: Full\n" + Block(Subject, Sha40) + "\nordinaryScopeCompleted: Full";
        var duplicated = ReviewEvidence.TryParse(Report(twoBlocks));
        duplicated.Usable.ShouldBeFalse("duplicate-blocks");
        duplicated.Scope.ShouldBe(Antiphon.Server.Domain.Enums.VerificationScope.Unknown, "duplicate-blocks");
    }

    private static string Block(Guid subject, string sha) =>
        $"--- review evidence ---\nsubjectTaskId: {subject:D}\nreviewedSourceSha: {sha}";

    private static string Report(string evidence) =>
        $"""
        review body
        {evidence}
        --- next stage ---
        next: land
        handoff: bind SHA
        [antiphon-report:aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee done]
        """;
}
