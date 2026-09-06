using System.Text.Json;
using Antiphon.Server.Application.Exceptions;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Api.Endpoints;

internal static class CardFileRequestReader
{
    internal static async Task<T> ReadAsync<T>(HttpContext http, CancellationToken ct)
    {
        try
        {
            using var document = await JsonDocument.ParseAsync(http.Request.Body, cancellationToken: ct);
            if (document.RootElement.ValueKind != JsonValueKind.Object) throw new JsonException();
            var options = http.RequestServices.GetRequiredService<IOptions<JsonOptions>>().Value.SerializerOptions;
            return document.RootElement.Deserialize<T>(options) ?? throw new JsonException();
        }
        catch (JsonException)
        {
            // Do not retain parser messages/inner exceptions: they can contain submitted private text.
            throw new BadRequestException("Invalid request JSON or field shape.");
        }
    }
}
