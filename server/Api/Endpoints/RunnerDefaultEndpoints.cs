using System.Text.Json;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Services;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Api.Endpoints;

public static class RunnerDefaultEndpoints
{
    public static void MapRunnerDefaultEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/runner-defaults").WithTags("RunnerDefaults");

        group.MapGet("/", async (RunnerDefaultSettingsService defaults, CancellationToken ct) =>
            Results.Ok(await defaults.GetAsync(ct)));

        group.MapPut("/", async (
            HttpContext http,
            RunnerDefaultSettingsService defaults,
            AgentTaskService tasks,
            IOptions<JsonOptions> json,
            CancellationToken ct) =>
        {
            var request = await RunnerDefaultsPut.ReadAsync(http.Request.Body, json.Value.SerializerOptions, ct);
            var caller = await AgentTaskEndpoints.ResolvePollingCallerAsync(http, tasks, ct);
            return Results.Ok(await defaults.PutAsync(request, caller?.Task?.Id, ct));
        });

        group.MapGet("/revisions", async (
            long? beforeRevision,
            int? limit,
            RunnerDefaultSettingsService defaults,
            CancellationToken ct) =>
            Results.Ok(await defaults.RevisionsAsync(beforeRevision, limit ?? 50, ct)));
    }
}

/// <summary>
/// CARD-0710 D-9. <c>globalRunnerId: null</c> clears the global default. A missing property is not
/// that clear: required fields must be present or the PUT is 422 and the saved revision stays.
/// </summary>
public static class RunnerDefaultsPut
{
    public static readonly string[] RequiredFields =
        ["expectedRevision", "globalRunnerId", "kindDefaults", "reason", "provenance"];

    public static async Task<PutRunnerDefaultsRequest> ReadAsync(
        Stream body, JsonSerializerOptions options, CancellationToken ct)
    {
        JsonDocument document;
        try
        {
            document = await JsonDocument.ParseAsync(body, cancellationToken: ct);
        }
        catch (JsonException ex)
        {
            throw new ValidationException("body", ex.Message);
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw new ValidationException("body", "A runner-defaults object is required.");
            var errors = new Dictionary<string, string[]>();
            foreach (var name in RequiredFields)
            {
                if (!Has(root, name))
                    errors[name] = [$"{name} is required."];
            }

            if (errors.Count > 0)
                throw new ValidationException(errors);
            try
            {
                return root.Deserialize<PutRunnerDefaultsRequest>(options)
                    ?? throw new ValidationException("body", "A runner-defaults object is required.");
            }
            catch (JsonException ex)
            {
                throw new ValidationException("body", ex.Message);
            }
        }
    }

    private static bool Has(JsonElement body, string name)
    {
        foreach (var property in body.EnumerateObject())
        {
            if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
