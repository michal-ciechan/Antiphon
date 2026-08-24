namespace Antiphon.FakeLlmApi;

/// <summary>Codex Responses-API surface: GET /v1/models, streaming POST /v1/responses.</summary>
internal static class CodexStubEndpoints
{
    public static void Map(WebApplication app, StubScript script)
    {
        app.MapGet("/v1/models", async (HttpContext ctx) =>
        {
            var next = script.Next(StubEndpointKeys.CodexModels);
            if (next is ScriptedError err)
            {
                await OpenAiResponsesSse.WriteErrorAsync(ctx.Response, err, ctx.RequestAborted);
                return;
            }

            await OpenAiResponsesSse.WriteModelsListAsync(ctx.Response, ctx.RequestAborted);
        });

        app.MapPost("/v1/responses", async (HttpContext ctx) =>
        {
            var next = script.Next(StubEndpointKeys.CodexResponses);
            switch (next)
            {
                case ScriptedError err:
                    await OpenAiResponsesSse.WriteErrorAsync(ctx.Response, err, ctx.RequestAborted);
                    break;
                case ScriptedTextTurn turn:
                    await OpenAiResponsesSse.WriteTextTurnAsync(ctx.Response, turn.Text, ctx.RequestAborted);
                    break;
                default:
                    await OpenAiResponsesSse.WriteTextTurnAsync(ctx.Response, "ok", ctx.RequestAborted);
                    break;
            }
        });
    }
}
