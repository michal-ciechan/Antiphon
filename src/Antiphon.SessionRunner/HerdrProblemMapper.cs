using Antiphon.SessionRunner.Contracts;

namespace Antiphon.SessionRunner;

/// <summary>
/// CARD-0213: RFC 9457 problem-details for herdr launch/attach/inspect faults. Shared by
/// <c>POST /sessions</c> (so <c>pane_occupied</c> is a 409 with its code, not a 500) and the
/// inspect/attach routes.
/// </summary>
internal static class HerdrProblemMapper
{
    public static IResult MapLaunch(HerdrLaunchException ex)
    {
        var status = string.Equals(ex.Code, HerdrProblemTypes.PaneNotFound, StringComparison.OrdinalIgnoreCase)
            ? StatusCodes.Status404NotFound
            : StatusCodes.Status409Conflict;
        return Results.Problem(
            title: TitleFor(ex.Code),
            detail: ex.Message,
            statusCode: status,
            type: ex.Code ?? "herdr_launch_failed");
    }

    public static IResult MapUnavailable(HerdrBackendUnavailableException ex) =>
        Results.Problem(
            title: "Herdr unreachable",
            detail: ex.Message,
            statusCode: StatusCodes.Status503ServiceUnavailable,
            type: HerdrProblemTypes.Unreachable);

    private static string TitleFor(string? code) => code switch
    {
        HerdrProblemTypes.PaneNotFound => "Herdr pane not found",
        HerdrProblemTypes.PaneUnoccupied => "Herdr pane unoccupied",
        HerdrProblemTypes.KindMismatch => "Herdr kind mismatch",
        HerdrProblemTypes.PaneForeign => "Herdr pane foreign",
        HerdrProblemTypes.PaneBound => "Herdr pane bound",
        HerdrProblemTypes.NativeIdUnknown => "Herdr native session id unknown",
        HerdrProblemTypes.TranscriptNotFound => "Herdr transcript not found",
        HerdrProblemTypes.PaneChanged => "Herdr pane changed",
        HerdrProblemTypes.PaneOccupied => "Herdr pane occupied",
        HerdrProblemTypes.GkpEnvMissing => "Herdr gkp launch cannot route",
        "pane_shell" => "Herdr pane shell",
        _ => "Herdr launch refused",
    };
}
