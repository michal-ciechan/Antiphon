using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Api.Endpoints;

public static class ComplexityChainEndpoints
{
    /// <summary>
    /// Per-complexity fallback chains (CARD-0090). Own group, not hung off routing-pins: that
    /// script is card+stage grain and this is neither.
    /// </summary>
    public static void MapComplexityChainEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/complexity-chains").WithTags("ComplexityChains");

        group.MapGet("/", async (
            ComplexityChainService chains,
            CancellationToken ct) =>
            Results.Ok(await chains.ListAsync(ct)));

        group.MapPut("/{complexity}", async (
            string complexity,
            PutComplexityChainRequest request,
            HttpContext http,
            ComplexityChainService chains,
            AgentTaskService tasks,
            CancellationToken ct) =>
        {
            var caller = await AgentTaskEndpoints.ResolvePollingCallerAsync(http, tasks, ct);
            return Results.Ok(await chains.UpsertAsync(
                ParseComplexity(complexity), request, caller?.Task?.Id, ct));
        });

        group.MapDelete("/{complexity}", async (
            string complexity,
            ComplexityChainService chains,
            CancellationToken ct) =>
        {
            await chains.ClearAsync(ParseComplexity(complexity), ct);
            return Results.NoContent();
        });
    }

    internal static TaskComplexity ParseComplexity(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || !Enum.TryParse<TaskComplexity>(value, ignoreCase: true, out var complexity)
            || !Enum.IsDefined(complexity))
        {
            throw new ValidationException(
                "complexity",
                $"'{value}' is not a complexity tier. Use Hard, Medium, or Easy.");
        }

        return complexity;
    }
}
