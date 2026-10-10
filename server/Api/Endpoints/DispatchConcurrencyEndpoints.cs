using System.Globalization;
using System.Text.Json;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Services;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Api.Endpoints;

public static class DispatchConcurrencyEndpoints
{
    public static void MapDispatchConcurrencyEndpoints(this WebApplication app)
    {
        var global = app.MapGroup("/api/dispatch-concurrency").WithTags("DispatchConcurrency");
        global.MapGet("/", async (DispatchConcurrencySettingsService settings, CancellationToken ct) =>
            Results.Ok(await settings.GetGlobalAsync(ct)));
        global.MapPut("/", PutAsync);
        global.MapGet("/revisions", (HttpContext http, DispatchConcurrencySettingsService settings, CancellationToken ct) =>
            HistoryAsync(http, settings, projectId: null, ct));

        var project = app.MapGroup("/api/projects/{projectId:guid}/dispatch-concurrency")
            .WithTags("DispatchConcurrency");
        project.MapGet("/", async (Guid projectId, DispatchConcurrencySettingsService settings, CancellationToken ct) =>
            Results.Ok(await settings.GetProjectAsync(projectId, ct)));
        project.MapPut("/", PutProjectAsync);
        project.MapGet("/revisions", (Guid projectId, HttpContext http, DispatchConcurrencySettingsService settings, CancellationToken ct) =>
            HistoryAsync(http, settings, projectId, ct));
    }

    private static async Task<IResult> PutAsync(
        HttpContext http,
        DispatchConcurrencySettingsService settings,
        AgentTaskService tasks,
        IOptions<JsonOptions> json,
        CancellationToken ct)
    {
        var (request, caller) = await ReadPutAsync(http, tasks, json.Value.SerializerOptions, project: false, ct);
        return Results.Ok(await settings.PutGlobalAsync(request, caller, ct));
    }

    private static async Task<IResult> PutProjectAsync(
        Guid projectId,
        HttpContext http,
        DispatchConcurrencySettingsService settings,
        AgentTaskService tasks,
        IOptions<JsonOptions> json,
        CancellationToken ct)
    {
        var (request, caller) = await ReadPutAsync(http, tasks, json.Value.SerializerOptions, project: true, ct);
        return Results.Ok(await settings.PutProjectAsync(projectId, request, caller, ct));
    }

    private static async Task<IResult> HistoryAsync(
        HttpContext http,
        DispatchConcurrencySettingsService settings,
        Guid? projectId,
        CancellationToken ct)
    {
        var limit = 50;
        long? before = null;
        if (http.Request.Query.TryGetValue("limit", out var limitText) && limitText.Count > 0)
        {
            if (!int.TryParse(limitText.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out limit))
                throw new ValidationException("limit", "limit must be from 1 to 100.");
        }

        if (http.Request.Query.TryGetValue("beforeRevision", out var beforeText) && beforeText.Count > 0)
        {
            if (!long.TryParse(beforeText.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
                || parsed < 0)
                throw new ValidationException("beforeRevision", "beforeRevision must be a non-negative revision.");
            before = parsed;
        }

        return Results.Ok(await settings.RevisionsAsync(projectId, before, limit, ct));
    }

    private static async Task<(PutDispatchConcurrencyRequest Request, Guid? CallerTaskId)> ReadPutAsync(
        HttpContext http,
        AgentTaskService tasks,
        JsonSerializerOptions options,
        bool project,
        CancellationToken ct)
    {
        JsonDocument document;
        try
        {
            document = await JsonDocument.ParseAsync(
                http.Request.Body,
                new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Disallow, AllowTrailingCommas = false },
                ct);
        }
        catch (JsonException ex)
        {
            throw new ValidationException("body", ex.Message);
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw new ValidationException("body", "A dispatch-concurrency object is required.");

            var seen = new HashSet<string>(StringComparer.Ordinal);
            var errors = new Dictionary<string, string[]>();
            foreach (var property in root.EnumerateObject())
            {
                if (!seen.Add(property.Name))
                    errors[property.Name] = [$"Duplicate field '{property.Name}'."];
            }

            Require(root, seen, errors, "expectedRevision");
            Require(root, seen, errors, "overrides");
            Require(root, seen, errors, "reason");
            Require(root, seen, errors, "provenance");
            if (project)
                Require(root, seen, errors, "expectedGlobalRevision");

            foreach (var name in seen)
            {
                if (name is "expectedRevision" or "expectedGlobalRevision" or "overrides" or "reason" or "provenance")
                    continue;
                errors[name] = [$"Unknown field '{name}'."];
            }

            if (errors.Count > 0)
                throw new ValidationException(errors);

            try
            {
                var expected = ReadInt64(root, "expectedRevision", errors);
                long? expectedGlobal = null;
                if (root.TryGetProperty("expectedGlobalRevision", out _))
                    expectedGlobal = ReadInt64(root, "expectedGlobalRevision", errors);
                var overrides = root.GetProperty("overrides");
                if (overrides.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
                    errors["overrides"] = ["overrides is required."];
                else if (overrides.ValueKind != JsonValueKind.Object)
                    errors["overrides"] = ["overrides must be a JSON object."];
                var reason = ReadString(root, "reason", errors);
                var provenance = ReadString(root, "provenance", errors);
                if (errors.Count > 0)
                    throw new ValidationException(errors);

                _ = options;
                var caller = await AgentTaskEndpoints.ResolvePollingCallerAsync(http, tasks, ct);
                return (new PutDispatchConcurrencyRequest(
                    expected, expectedGlobal, overrides.Clone(), reason ?? "", provenance ?? ""), caller?.Task?.Id);
            }
            catch (ValidationException)
            {
                throw;
            }
            catch (JsonException ex)
            {
                throw new ValidationException("body", ex.Message);
            }
        }
    }

    private static void Require(JsonElement root, HashSet<string> seen, Dictionary<string, string[]> errors, string name)
    {
        if (!seen.Contains(name) || !root.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
            errors[name] = [$"{name} is required."];
    }

    private static long ReadInt64(JsonElement root, string name, Dictionary<string, string[]> errors)
    {
        var value = root.GetProperty(name);
        if (value.ValueKind != JsonValueKind.Number
            || !value.TryGetInt64(out var number)
            || !long.TryParse(value.GetRawText(), NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
        {
            errors[name] = [$"{name} must be an integer."];
            return 0;
        }

        return number;
    }

    private static string? ReadString(JsonElement root, string name, Dictionary<string, string[]> errors)
    {
        var value = root.GetProperty(name);
        if (value.ValueKind != JsonValueKind.String)
        {
            errors[name] = [$"{name} must be a string."];
            return null;
        }

        return value.GetString();
    }
}
