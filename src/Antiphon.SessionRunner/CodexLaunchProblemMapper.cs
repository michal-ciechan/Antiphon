using Antiphon.SessionRunner.Contracts;

namespace Antiphon.SessionRunner;

/// <summary>
/// CARD-0497: RFC 9457 mapping for <see cref="CodexLaunchException"/>. Shared by
/// <c>POST /sessions</c> and the mapper unit test so both drive the same symbol.
/// </summary>
internal static class CodexLaunchProblemMapper
{
    public static IResult Map(CodexLaunchException ex) =>
        Results.Problem(
            title: "Codex launch refused",
            detail: ex.Message,
            statusCode: StatusCodes.Status409Conflict,
            type: ex.Code);
}
