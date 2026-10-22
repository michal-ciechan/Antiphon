using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// CARD-0167 S3: the provider-endpoint merge ranks (plan D-4) and the one rule that decides which
/// endpoint a launch projects. Both resolvers call <see cref="Select"/> once per launch and apply
/// <see cref="Selection.Project"/> at rank 3b (after the project default env, before the inherited
/// caller env) and <see cref="Selection.Agent"/> at rank 5b (after the agent's own LaunchEnvJson,
/// before the launch-time override); Codex's <c>-c</c> pairs go after the caller's ExtraArgs. One
/// class, so the profile path and the legacy registry path cannot drift apart.
///
/// <para>The mode governs. <c>Inherit</c> projects the project's entry for the resolved kind, if
/// any. <c>Endpoint</c> projects the agent's own endpoint INSTEAD of the project's: the agent said
/// "my proxy", so the project's key placeholder must not follow a URL-only agent endpoint to another
/// host, and Codex must not receive two provider tables. <c>Direct</c> projects nothing and removes
/// nothing: a hand-typed value in a generic env box stays, because the generic layers are not this
/// setting's to unset.</para>
///
/// <para>Custody: this class moves URLs and key NAMES. The placeholder it emits is resolved last by
/// <c>ApiKeyEnvResolver</c>; nothing here logs, and the one resolver log line is host-only.</para>
/// </summary>
public static class ProviderEndpointLayers
{
    /// <summary>Endpoint mode on a row with no endpoint of its own (the API refuses the pair; a hand edit can store it).</summary>
    public const string EndpointRequiredCode = "provider_endpoint_required";

    /// <summary>What one launch projects. At most one of <see cref="Project"/> and <see cref="Agent"/> is set; <see cref="Source"/> is the stored record it came from.</summary>
    public sealed record Selection(
        ProviderEndpointProjection.Projected? Project,
        ProviderEndpointProjection.Projected? Agent,
        ProviderEndpoint? Source)
    {
        public static readonly Selection None = new(null, null, null);

        public ProviderEndpointProjection.Projected? Effective => Agent ?? Project;

        /// <summary>The argv tail (Codex's five <c>-c</c> pairs); empty for Claude, Grok and no setting.</summary>
        public IReadOnlyList<string> Args => Effective?.Args ?? Array.Empty<string>();
    }

    public static Selection Select(
        AgentKind kind,
        IReadOnlyDictionary<AgentKind, ProviderEndpoint>? projectEndpoints,
        ProviderEndpointMode mode,
        ProviderEndpoint? agentEndpoint)
    {
        switch (mode)
        {
            case ProviderEndpointMode.Direct:
                return Selection.None;

            case ProviderEndpointMode.Endpoint:
                if (agentEndpoint is null)
                {
                    // Loud, not a silent fall-through to the project endpoint or to no proxy at all.
                    throw new ConflictException(
                        "The agent is in Endpoint mode but has no provider endpoint of its own; set "
                        + "providerEndpoint or change providerEndpointMode.",
                        EndpointRequiredCode);
                }

                return new Selection(null, ProviderEndpointProjection.For(kind, agentEndpoint), agentEndpoint);

            case ProviderEndpointMode.Inherit:
                return projectEndpoints is not null && projectEndpoints.TryGetValue(kind, out var fromProject)
                    ? new Selection(ProviderEndpointProjection.For(kind, fromProject), null, fromProject)
                    : Selection.None;

            default:
                throw new ConflictException(
                    $"Unknown provider endpoint mode '{mode}'.", "provider_endpoint_mode_invalid");
        }
    }

    /// <summary>
    /// The host of the endpoint's URL, for the one resolver log line. Never the path (a path could
    /// carry a tenant token), never the port, never a value.
    /// </summary>
    public static string HostOf(ProviderEndpoint endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        return Uri.TryCreate(endpoint.BaseUrl?.Trim(), UriKind.Absolute, out var uri) && uri.Host.Length > 0
            ? uri.Host
            : "(unparsed)";
    }

    /// <summary>
    /// The inherit-list view of one process's provider routing for the warm-pool predicate
    /// (CARD-0260): rank 3b, then the stored launch env, then rank 5b, on the names a launch would
    /// use. A refusal (unsupported kind, missing key name, bad stored URL, half-set row) projects
    /// nothing here; the launch is where it is raised.
    /// </summary>
    public static IReadOnlyDictionary<string, string> PoolView(
        AgentKind kind,
        IReadOnlyDictionary<string, string> launchEnv,
        IReadOnlyDictionary<AgentKind, ProviderEndpoint>? projectEndpoints,
        ProviderEndpointMode mode,
        ProviderEndpoint? agentEndpoint)
    {
        ArgumentNullException.ThrowIfNull(launchEnv);

        Selection selection;
        try
        {
            selection = Select(kind, projectEndpoints, mode, agentEndpoint);
        }
        catch (Exception exception) when (exception is ConflictException or ValidationException)
        {
            selection = Selection.None;
        }

        var env = new Dictionary<string, string>(StringComparer.Ordinal);
        if (selection.Project is { } project)
        {
            foreach (var (key, value) in project.Env)
                env[key] = value;
        }

        foreach (var (key, value) in launchEnv)
            env[key] = value;

        if (selection.Agent is { } agent)
        {
            foreach (var (key, value) in agent.Env)
                env[key] = value;
        }

        return env;
    }
}
