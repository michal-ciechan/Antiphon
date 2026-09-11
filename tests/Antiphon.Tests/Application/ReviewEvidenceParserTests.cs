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
