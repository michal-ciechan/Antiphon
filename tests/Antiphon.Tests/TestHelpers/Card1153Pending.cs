using TUnit.Core.Exceptions;

namespace Antiphon.Tests.TestHelpers;

/// <summary>
/// CARD-1153 TestDesign skeletons. Every pending body returns a task faulted with
/// <see cref="SkipTestException"/>, so the method is discovered and counted as skipped,
/// never green. Code replaces the body in the slice named by the tag; a checkpoint row
/// requires 0 skipped, so a leftover skeleton fails that row on purpose.
/// </summary>
internal static class Card1153Pending
{
    public static Task Skip(string slice, string method) =>
        Task.FromException(new SkipTestException(
            $"CARD-1153 {slice} pending: {method} is a TestDesign skeleton; Code writes the body."));
}
