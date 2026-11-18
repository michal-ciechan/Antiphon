using System.Collections.Immutable;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>CARD-1987 prompt-span allowlist and the oversize counter. No database.</summary>
[Category("Unit")]
public class PromptSpanOutcomeTests
{
    [Test]
    public async Task C1987_prompt_span_prefix_is_allowlisted()
    {
        DbCommandMetrics.LeadingTag("-- prompt-span.read\nSELECT 1").ShouldBe("prompt-span.read");
        DbCommandMetrics.LeadingTag("-- prompt-span.tail\nSELECT 1").ShouldBe("prompt-span.tail");
        DbCommandMetrics.LeadingTag("-- other.prefix\nSELECT 1").ShouldBeNull();
        await Task.CompletedTask;
    }

    [Test]
    public async Task C1987_oversize_store_counts_and_drops()
    {
        // Row overhead is 48 characters, so the ceiling sits between a short row and an 80-character row.
        var cache = new TranscriptPromptSpan.PromptCache(4, 60, 10_000, TimeSpan.FromSeconds(120), () => 0);
        var small = TranscriptPromptSpan.PromptCache.Entry.Derive(
            ImmutableArray.Create(new TranscriptPromptSpan.PromptRow(1, "hi", null, TranscriptKinds.UserPrompt)),
            ImmutableArray.Create(Guid.NewGuid()));
        small.Chars.ShouldBeLessThanOrEqualTo(60);
        cache.Store(new TranscriptPromptSpan.PromptCache.Key("c", Guid.NewGuid()), small);
        cache.ReadOutcomes().TooLargeToStore.ShouldBe(0);
        cache.Snapshot().Entries.ShouldBe(1);

        var big = TranscriptPromptSpan.PromptCache.Entry.Derive(
            ImmutableArray.Create(new TranscriptPromptSpan.PromptRow(1, new string('x', 80), null, TranscriptKinds.UserPrompt)),
            ImmutableArray.Create(Guid.NewGuid()));
        big.Chars.ShouldBeGreaterThan(60);
        cache.Store(new TranscriptPromptSpan.PromptCache.Key("c", Guid.NewGuid()), big);
        cache.Snapshot().Entries.ShouldBe(1);
        cache.ReadOutcomes().TooLargeToStore.ShouldBe(1);
        await Task.CompletedTask;
    }
}
