using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Exceptions;

/// <summary>
/// CARD-0022: a create/start that would launch a held model. HTTP 409,
/// <c>code: model_disabled</c>, with a <c>modelAvailability</c> problem-details extension.
/// </summary>
public sealed class ModelDisabledException : HttpException
{
    public const string ErrorCode = "model_disabled";

    public ModelDisabledException(ModelAvailabilityHold hold, IReadOnlyList<string> available)
        : base(409, FormatRefusal(hold, available), ErrorCode, BuildExtensions(hold, available))
    {
        Hold = hold;
        Available = available;
    }

    private ModelDisabledException(
        ModelAvailabilityHold hold, IReadOnlyList<string> available, string message)
        : base(409, message, ErrorCode, BuildExtensions(hold, available))
    {
        Hold = hold;
        Available = available;
    }

    public ModelAvailabilityHold Hold { get; }

    public IReadOnlyList<string> Available { get; }

    /// <summary>
    /// CARD-0305: the same refusal, with a sentence appended saying the available list does not
    /// satisfy a Required routing pin. Same code and same <c>modelAvailability</c> extension — the
    /// pin does not turn a hold into a different failure, it explains why picking from the list is
    /// not the operator's decision to make silently.
    /// </summary>
    public ModelDisabledException WithCoda(string coda) =>
        new(Hold, Available, $"{Message} — {coda}");

    public static string FormatRefusal(ModelAvailabilityHold hold, IReadOnlyList<string> available)
    {
        var availableList = available.Count == 0 ? "(none)" : string.Join(", ", available);
        var name = hold.ModelAlias == ModelAlias.KindWide
            ? hold.Kind.ToString()
            : hold.ModelAlias;
        var sourceClause = SourceClause(hold);
        if (hold.DisabledUntil is { } until)
        {
            return $"{name} is disabled until {until:yyyy-MM-ddTHH:mm:ssZ} ({sourceClause}); available: {availableList}";
        }

        return $"{name} is disabled ({sourceClause}); available: {availableList}";
    }

    private static string SourceClause(ModelAvailabilityHold hold) => hold.Source switch
    {
        ModelAvailabilitySource.Manual when hold.DisabledUntil is null => "manual, no re-enable time",
        ModelAvailabilitySource.Manual => "manual",
        _ when hold.DisabledUntil is null => "per-model cap, no reset stated",
        _ => "session-limit",
    };

    private static IReadOnlyDictionary<string, object?> BuildExtensions(
        ModelAvailabilityHold hold, IReadOnlyList<string> available) =>
        new Dictionary<string, object?>
        {
            ["modelAvailability"] = new ModelAvailabilityProblemDto(
                hold.Kind.ToString(),
                hold.ModelAlias,
                hold.DisabledUntil,
                hold.Source.ToString(),
                available),
        };
}

/// <summary>The <c>modelAvailability</c> problem-details extension. Property names camelCase on the wire.</summary>
public sealed record ModelAvailabilityProblemDto(
    string Kind,
    string ModelAlias,
    DateTime? DisabledUntil,
    string Source,
    IReadOnlyList<string> Available);
