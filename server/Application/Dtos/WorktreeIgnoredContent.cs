using System.Collections.Immutable;

namespace Antiphon.Server.Application.Dtos;

/// <summary>
/// CARD-0665: one reading's git-ignored paths split by the cleanup allowlist. Each array is
/// sorted ordinally so two readings compare with <c>SequenceEqual</c>.
/// </summary>
public sealed record WorktreeIgnoredContent(
    ImmutableArray<string> Disposable,
    ImmutableArray<string> Evidence,
    ImmutableArray<string> Protected);
