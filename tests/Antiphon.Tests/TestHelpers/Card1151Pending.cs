using TUnit.Core.Exceptions;

namespace Antiphon.Tests.TestHelpers;

/// <summary>
/// CARD-1151 TestDesign skeleton marker. Every skeleton body throws TUnit's skip exception with
/// its slice tag, so the method is discovered, counted as skipped, never green, and cannot be
/// mistaken for proof. Code replaces each body in the slice that lands the behaviour.
/// </summary>
internal static class Card1151Pending
{
    public static Task Skip(string slice, string method) =>
        Task.FromException(new SkipTestException(
            $"CARD-1151 {slice} pending: {method} is a TestDesign skeleton; Code writes the body."));
}
