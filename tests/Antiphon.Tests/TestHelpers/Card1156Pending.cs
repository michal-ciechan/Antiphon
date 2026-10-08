using TUnit.Core.Exceptions;

namespace Antiphon.Tests.TestHelpers;

/// <summary>
/// CARD-1156 TestDesign skeleton marker (the <see cref="Card1151Pending"/> shape). Every skeleton
/// body throws TUnit's skip exception with its slice tag, so the method is discovered, counted as
/// skipped, never green, and cannot be mistaken for proof. Code replaces each body in the slice
/// that lands the behaviour.
/// </summary>
internal static class Card1156Pending
{
    public static Task Skip(string slice, string method) =>
        Task.FromException(new SkipTestException(
            $"CARD-1156 {slice} pending: {method} is a TestDesign skeleton; Code writes the body."));
}
