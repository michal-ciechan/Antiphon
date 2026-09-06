using Antiphon.SessionRunner.Contracts;

namespace Antiphon.SessionRunner;

/// <summary>
/// CARD-0382: RFC 9457 mapping for <see cref="GrokRulesLaunchException"/>. Shared by
/// <c>POST /sessions</c> and V-3k so the test drives the same symbol the catch clause calls.
/// </summary>
internal static class GrokRulesProblemMapper
{
    public static IResult Map(GrokRulesLaunchException ex) =>
        Results.Problem(
            title: "Grok rules argv unsafe",
            detail: ex.Message,
            statusCode: StatusCodes.Status409Conflict,
            type: GrokRulesArgvPolicy.ProblemCode);
}
