using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Agents.Tui;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;

namespace Antiphon.Server.Api.Endpoints;

public static class AgentTuiEndpoints
{
    public static void MapAgentTuiEndpoints(this WebApplication app)
    {
        var routes = app.MapGroup("/api/agent-tui")
            .WithTags("Agent TUI");

        routes.MapGet("/runner-types", (AgentTuiRunnerCatalog catalogue) =>
            Results.Ok(catalogue.List()));

        routes.MapGet("/profiles", async (
            HttpContext context,
            AgentTuiProfileService service) =>
            Results.Ok(await service.ListAsync(context.RequestAborted)));

        routes.MapPost("/profiles", async (
            HttpContext context,
            AgentTuiProfileWriteRequest request,
            AgentTuiProfileService service) =>
        {
            var profile = await service.CreateAsync(request, context.RequestAborted);
            return Results.Created($"/api/agent-tui/profiles/{profile.Id}", profile);
        });

        routes.MapGet("/profiles/{profileId:guid}", async (
            HttpContext context,
            Guid profileId,
            AgentTuiProfileService service) =>
            Results.Ok(await service.GetAsync(profileId, context.RequestAborted)));

        routes.MapPatch("/profiles/{profileId:guid}", async (
            HttpContext context,
            Guid profileId,
            AgentTuiProfileWriteRequest request,
            AgentTuiProfileService service,
            AgentTuiMetrics metrics) =>
        {
            try
            {
                return Results.Ok(await service.UpdateAsync(
                    profileId,
                    request,
                    context.RequestAborted));
            }
            catch (HttpException exception) when (exception.Code == "profile_revision_conflict")
            {
                metrics.RecordRevisionConflict(AgentTuiRevisionMetricOperation.ProfileUpdate);
                throw;
            }
        });

        routes.MapPost("/profiles/{profileId:guid}/duplicate", async (
            HttpContext context,
            Guid profileId,
            DuplicateAgentTuiProfileRequest request,
            AgentTuiProfileService service) =>
        {
            var duplicate = await service.DuplicateAsync(
                profileId,
                request,
                context.RequestAborted);
            return Results.Created($"/api/agent-tui/profiles/{duplicate.Id}", duplicate);
        });

        routes.MapDelete("/profiles/{profileId:guid}", async (
            HttpContext context,
            Guid profileId,
            AgentTuiProfileService service) =>
        {
            await service.DeleteAsync(profileId, context.RequestAborted);
            return Results.NoContent();
        });

        routes.MapPut("/profiles/{profileId:guid}/secrets/{environmentName}", async (
            HttpContext context,
            Guid profileId,
            string environmentName,
            AgentTuiSecretPutApiRequest request,
            AgentTuiProfileService service,
            AgentTuiMetrics metrics) =>
        {
            const AgentTuiSecretMetricOperation operation = AgentTuiSecretMetricOperation.Write;
            try
            {
                ValidateSecretPutRequest(context, request);
                var result = await service.PutSecretAsync(
                    profileId,
                    environmentName,
                    new AgentTuiSecretWriteRequest(
                        request.Value!,
                        request.ExpectedRevision,
                        ServerCorrelationIdentity()),
                    context.RequestAborted);
                metrics.RecordSecret(operation, AgentTuiMetricOutcome.Succeeded);
                return Results.Ok(result);
            }
            catch (HttpException exception)
            {
                RecordSecretFailure(metrics, operation, exception);
                if (exception.Code == "profile_revision_conflict")
                    metrics.RecordRevisionConflict(AgentTuiRevisionMetricOperation.SecretWrite);
                throw;
            }
        });

        routes.MapDelete("/profiles/{profileId:guid}/secrets/{environmentName}", async (
            HttpContext context,
            Guid profileId,
            string environmentName,
            [FromBody] AgentTuiSecretDeleteApiRequest request,
            AgentTuiProfileService service,
            AgentTuiMetrics metrics) =>
        {
            const AgentTuiSecretMetricOperation operation = AgentTuiSecretMetricOperation.Clear;
            try
            {
                ValidateExactProperties(request.AdditionalProperties, "expectedRevision");
                var result = await service.ClearSecretAsync(
                    profileId,
                    environmentName,
                    new AgentTuiSecretClearRequest(
                        request.ExpectedRevision,
                        ServerCorrelationIdentity()),
                    context.RequestAborted);
                metrics.RecordSecret(operation, AgentTuiMetricOutcome.Succeeded);
                return Results.Ok(result);
            }
            catch (HttpException exception)
            {
                RecordSecretFailure(metrics, operation, exception);
                if (exception.Code == "profile_revision_conflict")
                    metrics.RecordRevisionConflict(AgentTuiRevisionMetricOperation.SecretClear);
                throw;
            }
        });

        routes.MapGet("/profiles/{profileId:guid}/models", async (
            HttpContext context,
            Guid profileId,
            AgentTuiProfileService service) =>
            Results.Ok(await service.GetModelsAsync(profileId, context.RequestAborted)));

        routes.MapPost("/profiles/{profileId:guid}/models/refresh", async (
            HttpContext context,
            Guid profileId,
            AgentTuiProfileService service,
            AgentTuiMetrics metrics) =>
        {
            var profile = await service.GetAsync(profileId, context.RequestAborted);
            var startedAt = Stopwatch.GetTimestamp();
            try
            {
                var result = await service.RefreshModelsWithOutcomeAsync(
                    profileId,
                    context.RequestAborted);
                metrics.RecordDiscovery(
                    result.Run.Id,
                    profile.Kind,
                    result.Run.Status,
                    result.Run.Stages.Any(stage =>
                        stage.Name == "discovery"
                        && stage.Status == AgentTuiValidationStageStatus.Passed),
                    Stopwatch.GetElapsedTime(startedAt));
                return Results.Ok(result);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                metrics.RecordDiscovery(
                    null,
                    profile.Kind,
                    AgentTuiValidationStatus.Failed,
                    catalogueRefreshed: false,
                    Stopwatch.GetElapsedTime(startedAt));
                throw;
            }
        });

        routes.MapGet("/profiles/{profileId:guid}/capabilities", async (
            HttpContext context,
            Guid profileId,
            AgentTuiProfileService service) =>
            Results.Ok(await service.GetCapabilitiesAsync(profileId, context.RequestAborted)));

        routes.MapPost("/profiles/{profileId:guid}/validate", async (
            HttpContext context,
            Guid profileId,
            AgentTuiProfileService service,
            AgentTuiMetrics metrics) =>
        {
            var profile = await service.GetAsync(profileId, context.RequestAborted);
            var startedAt = Stopwatch.GetTimestamp();
            var run = await service.ValidateAsync(profileId, context.RequestAborted);
            metrics.RecordValidation(
                profile.Kind,
                run,
                Stopwatch.GetElapsedTime(startedAt));
            return Results.Ok(run);
        });

        routes.MapGet("/validation-runs/{runId:guid}", async (
            HttpContext context,
            Guid runId,
            AgentTuiProfileService service) =>
            Results.Ok(await service.GetValidationRunAsync(runId, context.RequestAborted)));

        app.MapGet("/metrics/agent-tui", async (
            HttpContext context,
            AppDbContext db,
            AgentTuiMetrics metrics,
            AgentTuiKeyProtectionReadiness keyReadiness,
            TimeProvider timeProvider) =>
        {
            var text = await metrics.RenderAsync(
                db,
                keyReadiness,
                timeProvider,
                context.RequestAborted);
            return Results.Text(
                text,
                "text/plain; version=0.0.4",
                Encoding.UTF8);
        }).WithTags("Agent TUI");
    }

    private static void ValidateSecretPutRequest(
        HttpContext context,
        AgentTuiSecretPutApiRequest request)
    {
        ValidateExactProperties(request.AdditionalProperties, "value", "expectedRevision");
        if (context.Request.Query.ContainsKey("value"))
        {
            throw new ValidationException(
                "request",
                "Secret values are accepted only in the JSON request body.",
                "invalid_request");
        }
        if (string.IsNullOrEmpty(request.Value))
        {
            throw new ValidationException(
                "value",
                "A non-empty secret value is required.",
                "invalid_request");
        }
    }

    private static void ValidateExactProperties(
        IReadOnlyDictionary<string, JsonElement>? additionalProperties,
        params string[] acceptedProperties)
    {
        if (additionalProperties is not { Count: > 0 })
            return;
        throw new ValidationException(
            "request",
            $"Only {string.Join(" and ", acceptedProperties)} are accepted.",
            "invalid_request");
    }

    private static string ServerCorrelationIdentity() =>
        Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString("N");

    private static void RecordSecretFailure(
        AgentTuiMetrics metrics,
        AgentTuiSecretMetricOperation operation,
        HttpException exception)
    {
        var outcome = exception.Code switch
        {
            "profile_revision_conflict" => AgentTuiMetricOutcome.Conflict,
            "invalid_environment_name" or "invalid_request" => AgentTuiMetricOutcome.Invalid,
            _ => AgentTuiMetricOutcome.Failed
        };
        metrics.RecordSecret(operation, outcome);
    }
}

public sealed record AgentTuiSecretPutApiRequest(
    string? Value,
    int ExpectedRevision)
{
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalProperties { get; init; }
}

public sealed record AgentTuiSecretDeleteApiRequest(int ExpectedRevision)
{
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalProperties { get; init; }
}
