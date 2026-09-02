using Antiphon.Server.Application.Dtos;

namespace Antiphon.Server.Application.Exceptions;

/// <summary>
/// CARD-0057: a card schedule with <c>Start</c> of Release or Spawn was created without
/// <c>acceptSpend: true</c>. HTTP 422, <c>code: spend_unacknowledged</c>, with the preview
/// embedded so the refusal itself says what would have been started.
/// </summary>
public sealed class SpendUnacknowledgedException : HttpException
{
    public const string ErrorCode = "spend_unacknowledged";

    public SpendUnacknowledgedException(SchedulePreviewDto preview)
        : base(
            422,
            "This schedule would start a session. Re-submit with acceptSpend: true after reading the preview.",
            ErrorCode,
            new Dictionary<string, object?> { ["preview"] = preview })
    {
        Preview = preview;
    }

    public SchedulePreviewDto Preview { get; }
}
