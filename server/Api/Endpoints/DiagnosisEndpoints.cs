using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Api.Endpoints;

public static class DiagnosisEndpoints
{
    /// <summary>
    /// Read-only Diagnoses ledger (CARD-0352 D7). One query burst each; no client work.
    /// </summary>
    public static void MapDiagnosisEndpoints(this WebApplication app)
    {
        var diagnoses = app.MapGroup("/api/diagnoses").WithTags("Diagnoses");

        diagnoses.MapGet("/", async (
            Guid? cardId,
            Guid? taskId,
            DateTime? since,
            DiagnosisOutcome? outcome,
            DiagnosisKind? kind,
            int? limit,
            DiagnoseService diagnose,
            CancellationToken ct) =>
            Results.Ok(await diagnose.ListAsync(cardId, taskId, since, outcome, kind, limit, ct)));

        diagnoses.MapGet("/stats", async (
            DateTime? since,
            DiagnoseService diagnose,
            CancellationToken ct) =>
            Results.Ok(await diagnose.StatsAsync(since, ct)));
    }
}
