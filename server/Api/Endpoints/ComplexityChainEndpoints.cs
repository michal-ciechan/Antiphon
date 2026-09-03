using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Api.Endpoints;

public static class ComplexityChainEndpoints
{
    /// <summary>
    /// Per-(role, complexity) fallback chains (CARD-0090, CARD-0332). Own group, not hung off
    /// routing-pins: that script is card+stage grain and this is neither. Two-segment
    /// PUT/DELETE /{complexity} is the any-role alias of /any/{complexity}.
    /// </summary>
    public static void MapComplexityChainEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/complexity-chains").WithTags("ComplexityChains");

        group.MapGet("/", async (
            string? role,
            ComplexityChainService chains,
            CancellationToken ct) =>
            Results.Ok(await chains.ListAsync(ParseRole(role, required: false), ct)));

        group.MapPut("/{role}/{complexity}", async (
            string role,
            string complexity,
            PutComplexityChainRequest request,
            HttpContext http,
            ComplexityChainService chains,
            AgentTaskService tasks,
            CancellationToken ct) =>
        {
            var caller = await AgentTaskEndpoints.ResolvePollingCallerAsync(http, tasks, ct);
            return Results.Ok(await chains.UpsertAsync(
                ParseRole(role, required: true), ParseComplexity(complexity), request, caller?.Task?.Id, ct));
        });

        group.MapDelete("/{role}/{complexity}", async (
            string role,
            string complexity,
            ComplexityChainService chains,
            CancellationToken ct) =>
        {
            await chains.ClearAsync(ParseRole(role, required: true), ParseComplexity(complexity), ct);
            return Results.NoContent();
        });

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
                role: null, ParseComplexity(complexity), request, caller?.Task?.Id, ct));
        });

        group.MapDelete("/{complexity}", async (
            string complexity,
            ComplexityChainService chains,
            CancellationToken ct) =>
        {
            await chains.ClearAsync(role: null, ParseComplexity(complexity), ct);
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

    /// <summary>
    /// <c>any</c> (case-insensitive) or omitted → null. Check/Distill/Diagnose → 422.
    /// </summary>
    internal static AgentTaskRole? ParseRole(string? value, bool required)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            if (required)
            {
                throw new ValidationException(
                    "role",
                    "'' is not a task role.");
            }

            return null;
        }

        if (string.Equals(value, "any", StringComparison.OrdinalIgnoreCase))
            return null;

        if (!Enum.TryParse<AgentTaskRole>(value, ignoreCase: true, out var role) || !Enum.IsDefined(role))
        {
            throw new ValidationException(
                "role",
                $"'{value}' is not a task role.");
        }

        ComplexityChainService.ValidateCellRole(role);
        return role;
    }
}
