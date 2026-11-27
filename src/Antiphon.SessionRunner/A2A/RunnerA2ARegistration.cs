using System.Net;
using System.Security.Cryptography;
using System.Text;
using A2A;
using A2A.AspNetCore;
using Microsoft.Extensions.Options;

namespace Antiphon.SessionRunner.Agent2Agent;

/// <summary>Registers and maps the optional A2A endpoint. Both calls are no-ops while SessionRunner:A2A:Enabled is false.</summary>
public static class RunnerA2ARegistration
{
    public static IServiceCollection AddRunnerA2A(this IServiceCollection services, IConfiguration configuration)
    {
        var settings = configuration.GetSection(RunnerA2ASettings.SectionName).Get<RunnerA2ASettings>() ?? new();
        if (!settings.Enabled) return services;
        settings.Validate();
        _ = settings.ReadBearerToken();
        services.AddSingleton(Options.Create(settings));
        services.AddSingleton(settings);
        services.AddSingleton<ICodexTurnLauncher, ProcessCodexTurnLauncher>();
        // Registered before AddA2AAgent, whose TryAdd then keeps this handler. The agent is one singleton, reachable both
        // as the SDK's IAgentHandler and by the handler's cancel path.
        services.AddSingleton<CodexA2AAgent>();
        services.AddSingleton<IA2ARequestHandler>(sp => new CodexA2ARequestHandler(
            sp.GetRequiredService<CodexA2AAgent>(), sp.GetRequiredService<ITaskStore>(),
            sp.GetRequiredService<ChannelEventNotifier>(), sp.GetRequiredService<ILogger<A2AServer>>(),
            sp.GetRequiredService<A2AServerOptions>(), sp.GetRequiredService<AgentCard>(),
            sp.GetRequiredService<IPushNotificationStore>(), sp.GetRequiredService<IPushNotificationUrlValidator>(),
            sp.GetRequiredService<IPushNotificationSender>(), sp.GetRequiredService<TimeProvider>()));
        services.AddA2AAgent<CodexA2AAgent>(BuildAgentCard(settings, configuration["urls"]));
        services.AddSingleton<IAgentHandler>(sp => sp.GetRequiredService<CodexA2AAgent>());
        return services;
    }

    public static WebApplication MapRunnerA2A(this WebApplication app)
    {
        if (app.Services.GetService<RunnerA2ASettings>() is not { } settings) return app;
        var token = settings.ReadBearerToken();
        app.MapA2A(settings.Path).AddEndpointFilter((context, next) => Guard(context, next, token, rpc: true));
        app.MapWellKnownAgentCard(app.Services.GetRequiredService<AgentCard>())
            .AddEndpointFilter((context, next) => Guard(context, next, token, rpc: false));
        app.Logger.LogInformation("A2A endpoint {Path} is on (Codex, sandbox {Sandbox}, workspace {Root}, {Access})",
            settings.Path, settings.Sandbox, settings.WorkspaceRoot, token is null ? "loopback callers only" : "bearer token required");
        return app;
    }

    public static AgentCard BuildAgentCard(RunnerA2ASettings settings, string? urls)
    {
        var baseUrl = (settings.PublicUrl ?? FirstUrl(urls)).TrimEnd('/');
        return new AgentCard
        {
            Name = "Antiphon SessionRunner (Codex)",
            Description = "Runs OpenAI Codex turns on this host. One A2A context is one Codex thread; send follow-up "
                + "messages with the same contextId to continue it.",
            Version = typeof(RunnerA2ARegistration).Assembly.GetName().Version?.ToString() ?? "1.0.0",
            SupportedInterfaces =
            [
                new AgentInterface { Url = baseUrl + settings.Path, ProtocolBinding = "JSONRPC", ProtocolVersion = "1.0" },
            ],
            DefaultInputModes = ["text/plain"],
            DefaultOutputModes = ["text/plain"],
            Capabilities = new AgentCapabilities { Streaming = true, PushNotifications = false },
            Skills =
            [
                new AgentSkill
                {
                    Id = "codex",
                    Name = "Codex",
                    // No local paths here: with a token set the card is served to any caller.
                    Description = $"A Codex coding agent working in this runner's workspace with a {settings.Sandbox} sandbox.",
                    Tags = ["codex", "coding"],
                },
            ],
        };
    }

    private static string FirstUrl(string? urls)
    {
        var first = urls?.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
        if (first is null) return "http://localhost:5000";
        return first.Replace("://+:", "://localhost:", StringComparison.Ordinal)
            .Replace("://*:", "://localhost:", StringComparison.Ordinal)
            .Replace("://0.0.0.0:", "://localhost:", StringComparison.Ordinal)
            .Replace("://[::]:", "://localhost:", StringComparison.Ordinal);
    }

    /// <summary>
    /// No token: direct loopback callers only, which a local reverse proxy or a browser page cannot pass for. With a
    /// token: JSON-RPC needs it from every caller; the agent card is open so a remote client can discover the endpoint.
    /// JSON-RPC always needs an application/json body, which a cross-site form or no-preflight fetch cannot send.
    /// </summary>
    internal static ValueTask<object?> Guard(EndpointFilterInvocationContext context, EndpointFilterDelegate next, string? token, bool rpc)
    {
        var http = context.HttpContext;
        if (token is null && LoopbackRefusal(http) is { } refusal)
            return Refuse(StatusCodes.Status403Forbidden, refusal);
        if (rpc && token is not null && !HasToken(http.Request, token))
        {
            http.Response.Headers.WWWAuthenticate = "Bearer";
            return ValueTask.FromResult<object?>(Results.Unauthorized());
        }
        if (rpc && !IsJson(http.Request))
            return Refuse(StatusCodes.Status415UnsupportedMediaType, "A2A JSON-RPC requests must be application/json.");
        return next(context);
    }

    private static string? LoopbackRefusal(HttpContext http)
    {
        if (http.Connection.RemoteIpAddress is not { } remote || !IPAddress.IsLoopback(remote))
            return "The A2A endpoint serves loopback callers only. Set BearerTokenFile to serve others.";
        // A proxy on this host connects from loopback; it gives itself away by forwarding headers or a public Host.
        if (http.Request.Headers.ContainsKey("Forwarded") || http.Request.Headers.Keys.Any(key =>
                key.StartsWith("X-Forwarded-", StringComparison.OrdinalIgnoreCase)))
            return "The A2A endpoint does not serve proxied requests without a bearer token.";
        var host = http.Request.Host.Host;
        if (!(host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
              || IPAddress.TryParse(host.Trim('[', ']'), out var address) && IPAddress.IsLoopback(address)))
            return "The A2A endpoint serves loopback host names only without a bearer token.";
        return null;
    }

    private static bool IsJson(HttpRequest request) =>
        request.ContentType is { } type && type.Split(';')[0].Trim().Equals("application/json", StringComparison.OrdinalIgnoreCase);

    private static ValueTask<object?> Refuse(int status, string detail) =>
        ValueTask.FromResult<object?>(Results.Problem(detail, statusCode: status));

    private static bool HasToken(HttpRequest request, string token)
    {
        var header = request.Headers.Authorization.ToString();
        const string scheme = "Bearer ";
        if (!header.StartsWith(scheme, StringComparison.OrdinalIgnoreCase)) return false;
        var presented = Encoding.UTF8.GetBytes(header[scheme.Length..].Trim());
        return CryptographicOperations.FixedTimeEquals(presented, Encoding.UTF8.GetBytes(token));
    }
}
