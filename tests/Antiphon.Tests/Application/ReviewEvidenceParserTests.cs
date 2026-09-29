using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;
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
        AssertIgnored(ReviewEvidence.TryParse(Report(quoted)), "quoted");
        var fenced = "```\n--- review evidence ---\nsubjectTaskId: " + Subject + "\nreviewedSourceSha: " + Sha40 + "\n```";
        AssertIgnored(ReviewEvidence.TryParse(Report(fenced)), "fenced");
    }

    [Test]
    public void C807_IgnoredHeadingMatrix()
    {
        var block = Block(Subject, Sha40) + "\nordinaryScopeCompleted: Full";
        var rows = new (string Name, string Text)[]
        {
            ("fence", "```\n" + block + "\n```"),
            ("language", "```markdown\n" + block + "\n```"),
            ("four", "````\n" + block + "\n````"),
            ("indented-fence", "   ```\n" + block + "\n   ```"),
            ("unclosed", "```\n" + block),
            ("quote", "> " + block.Replace("\n", "\n> ")),
            ("quote-tight", ">" + block.Replace("\n", "\n>")),
            ("nested-tight", ">>" + block.Replace("\n", "\n>>")),
            ("nested-spaced", "> > " + block.Replace("\n", "\n> > ")),
            ("nested-indented", "  >  > " + block.Replace("\n", "\n  >  > ")),
            ("one-space", " " + block.Replace("\n", "\n ")),
            ("four-spaces", "    " + block.Replace("\n", "\n    ")),
            ("tab", "\t" + block.Replace("\n", "\n\t")),
        };
        foreach (var (name, content) in rows)
            foreach (var newline in new[] { "\n", "\r\n" })
                AssertIgnored(ReviewEvidence.TryParse(Report(content).Replace("\n", newline)), name + newline.Length);
    }

    [Test]
    public void C807_MixedExamplesPreserveStandalone()
    {
        var other = Guid.Parse("11111111-2222-3333-4444-555555555555");
        var ignored = "```\n" + Block(other, new string('b', 40)) + "\nordinaryScopeCompleted: None\n```";
        var actual = Block(Subject, Sha40) + "\nordinaryScopeCompleted: Full";
        foreach (var newline in new[] { "\n", "\r\n" })
        {
            var parsed = ReviewEvidence.TryParse(Report(ignored + "\n" + actual + "\n--- separator ---\n" + ignored)
                .Replace("\n", newline));
            parsed.Found.ShouldBeTrue();
            parsed.Usable.ShouldBeTrue();
            parsed.SubjectTaskId.ShouldBe(Subject);
            parsed.ReviewedSourceSha.ShouldBe(Sha40);
            parsed.Scope.ShouldBe(VerificationScope.Full);
            parsed.Warning.ShouldBeNull();
        }
    }

    [Test]
    public void C807_StandaloneErrorPrecedence()
    {
        var ignored = "```\n" + Block(Subject, Sha40) + "\n```\n";
        var rows = new (string Name, string Text, string Warning)[]
        {
            ("duplicate", Block(Subject, Sha40) + "\n" + Block(Subject, Sha40), "review_evidence_duplicate"),
            ("subject", Block(Guid.Empty, Sha40), "review_evidence_subject_invalid"),
            ("sha", Block(Subject, "bad"), "review_evidence_sha_invalid"),
            ("placement", "--- next stage ---\nnext: land\n" + Block(Subject, Sha40), "review_evidence_after_next_stage"),
        };
        foreach (var (name, content, warning) in rows)
        {
            var parsed = ReviewEvidence.TryParse(ignored + content);
            parsed.Found.ShouldBeTrue(name);
            parsed.Usable.ShouldBeFalse(name);
            parsed.Warning.ShouldBe(warning, name);
        }
    }

    [Test]
    public void C807_ReportTokenCutsOffEvidence()
    {
        var token = "[antiphon-report:aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee done]";
        var block = Block(Subject, Sha40);
        var after = ReviewEvidence.TryParse("body\n" + token + "\n" + block);
        after.Found.ShouldBeFalse();
        after.Warning.ShouldBeNull();
        AssertIgnored(ReviewEvidence.TryParse("```\n" + block + "\n```\n" + token + "\n" + block), "cutoff");
    }

    [Test]
    public void C807_HeadingPrecision()
    {
        foreach (var text in new string?[] { null, "", "   ", "review evidence", "`--- review evidence ---`",
                     "Prose: --- review evidence ---", "--- review evidence --- extra", "> This quotes --- review evidence ---" })
        {
            var parsed = ReviewEvidence.TryParse(text);
            parsed.Found.ShouldBeFalse(text);
            parsed.Warning.ShouldBeNull();
        }
        var actual = ReviewEvidence.TryParse("--- REVIEW EVIDENCE --- \t\nsubjectTaskId: " + Subject
            + "\nreviewedSourceSha: " + Sha40 + "\nordinaryScopeCompleted: Full");
        actual.Usable.ShouldBeTrue();
        actual.Scope.ShouldBe(VerificationScope.Full);
    }

    private static void AssertIgnored(ReviewEvidence.Result parsed, string row)
    {
        parsed.Found.ShouldBeTrue(row);
        parsed.Usable.ShouldBeFalse(row);
        parsed.SubjectTaskId.ShouldBeNull(row);
        parsed.ReviewedSourceSha.ShouldBeNull(row);
        parsed.Scope.ShouldBe(VerificationScope.Unknown, row);
        parsed.Warning.ShouldBe(ReviewEvidence.NotStandaloneWarning, row);
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
