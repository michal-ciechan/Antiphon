using System.Text.Json;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Services;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Api.Endpoints;

/// <summary>CARD-1320. Per-orchestrator keep-six toggle. Plural so it does not collide with /api/orchestrator/state.</summary>
public static class KeepSixEndpoints
{
    public static void MapKeepSixEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/orchestrators").WithTags("KeepSix");

        group.MapGet("/keep-six", async (KeepSixService keepSix, CancellationToken ct) =>
            Results.Ok(await keepSix.ListAsync(ct)));

        group.MapPut("/{agentId:guid}/keep-six", async (
            Guid agentId,
            HttpContext http,
            KeepSixService keepSix,
            IOptions<JsonOptions> json,
            CancellationToken ct) =>
        {
            var request = await KeepSixPut.ReadAsync(http.Request.Body, json.Value.SerializerOptions, ct);
            return Results.Ok(await keepSix.PutAsync(agentId, request, ct));
        });

        group.MapPost("/{agentId:guid}/keep-six/hold", async (
            Guid agentId,
            HttpContext http,
            KeepSixService keepSix,
            IOptions<JsonOptions> json,
            CancellationToken ct) =>
        {
            var request = await KeepSixHold.ReadAsync(http.Request.Body, json.Value.SerializerOptions, ct);
            return Results.Ok(await keepSix.HoldAsync(agentId, request, Token(http), ct));
        });

        group.MapDelete("/{agentId:guid}/keep-six/hold", async (
            Guid agentId,
            HttpContext http,
            KeepSixService keepSix,
            CancellationToken ct) =>
            Results.Ok(await keepSix.ClearHoldAsync(agentId, Token(http), ct)));

        group.MapPost("/{agentId:guid}/keep-six/ack", async (
            Guid agentId,
            HttpContext http,
            KeepSixService keepSix,
            CancellationToken ct) =>
            Results.Ok(await keepSix.AckAsync(agentId, Token(http), ct)));
    }

    private static string? Token(HttpContext http) =>
        http.Request.Headers[AgentTaskEndpoints.TokenHeader].FirstOrDefault();
}

/// <summary>The toggle body. Every field must be present; a missing expectedRevision is not a create.</summary>
public static class KeepSixPut
{
    public static readonly string[] RequiredFields =
        ["expectedRevision", "enabled", "target", "reason", "provenance"];

    public static async Task<PutKeepSixRequest> ReadAsync(
        Stream body, JsonSerializerOptions options, CancellationToken ct)
    {
        JsonDocument document;
        try
        {
            document = await JsonDocument.ParseAsync(body, cancellationToken: ct);
        }
        catch (JsonException ex)
        {
            throw new BadRequestException(ex.Message);
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw new BadRequestException("A keep-six object is required.");
            foreach (var name in RequiredFields)
            {
                if (!Has(root, name))
                    throw new BadRequestException($"{name} is required.");
            }

            try
            {
                return root.Deserialize<PutKeepSixRequest>(options)
                    ?? throw new BadRequestException("A keep-six object is required.");
            }
            catch (JsonException ex)
            {
                throw new BadRequestException(ex.Message);
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

/// <summary>The hold body. Both fields must be present.</summary>
public static class KeepSixHold
{
    public static readonly string[] RequiredFields = ["reason", "until"];

    public static async Task<HoldKeepSixRequest> ReadAsync(
        Stream body, JsonSerializerOptions options, CancellationToken ct)
    {
        JsonDocument document;
        try
        {
            document = await JsonDocument.ParseAsync(body, cancellationToken: ct);
        }
        catch (JsonException ex)
        {
            throw new BadRequestException(ex.Message);
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw new BadRequestException("A keep-six hold object is required.");
            foreach (var name in RequiredFields)
            {
                if (!Has(root, name))
                    throw new BadRequestException($"{name} is required.");
            }

            try
            {
                return root.Deserialize<HoldKeepSixRequest>(options)
                    ?? throw new BadRequestException("A keep-six hold object is required.");
            }
            catch (JsonException ex)
            {
                throw new BadRequestException(ex.Message);
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
