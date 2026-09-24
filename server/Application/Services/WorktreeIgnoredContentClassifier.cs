using System.Collections.Immutable;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Settings;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Services;

/// <summary>CARD-0665 D-1: Evidence, then Disposable, else Protected.</summary>
public sealed class WorktreeIgnoredContentClassifier(IOptions<WorktreeCleanupSettings> options)
{
    public WorktreeIgnoredContent Classify(ImmutableArray<string> ignoredPaths) =>
        new([], [], [.. ignoredPaths.Order(StringComparer.Ordinal)]);
}
