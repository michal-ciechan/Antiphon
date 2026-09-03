using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// Facade over <see cref="StandingSpecialistProvisioner"/> for the standing check interpreter
/// (CARD-0047 slice 4 amendment §1.2, extracted CARD-0352 S1). Constructor, <see cref="EnsureAsync"/>,
/// <see cref="Slug"/>, and <see cref="ResolveWorkingDirectory"/> stay so the ten provisioner tests,
/// DI registration, and <c>AgentTaskCheckHostedService</c> do not move.
/// </summary>
public sealed class CheckInterpreterProvisioner
{
    public const string DefaultSlug = "antiphon-check-interpreter";
    public const string DefaultDirectoryName = "check-interpreter";

    public const string Details =
        "Antiphon furniture, not a stray: the standing specialist that reads delegate "
        + "check-in bundles and says what they look like (CARD-0047). Supervised, haiku "
        + "tier, no tools. Its instructions are reconciled from code on every startup.";

    private readonly StandingSpecialistProvisioner _inner;
    private readonly DelegationSettings _settings;

    public CheckInterpreterProvisioner(
        AppDbContext db,
        IOptions<DelegationSettings> settings,
        TimeProvider timeProvider,
        ILogger<CheckInterpreterProvisioner> logger,
        AgentControlService? control = null,
        AgentWorkspaceProvisioner? workspace = null)
    {
        _settings = settings.Value;
        _inner = new StandingSpecialistProvisioner(db, timeProvider, logger, control, workspace);
    }

    /// <summary>
    /// The specialist, guaranteed to exist and to be carrying the current contract. Null when the
    /// feature is switched off — the caller degrades, it does not fail.
    /// </summary>
    public async Task<Agent?> EnsureAsync(CancellationToken ct)
    {
        if (!_settings.CheckInterpreterEnabled)
            return null;

        return await _inner.EnsureAsync(Spec(_settings), ct);
    }

    public static SpecialistSpec Spec(DelegationSettings settings) => new(
        Role: AgentTaskRole.Check,
        Slug: Slug(settings),
        WorkingDirectory: ResolveWorkingDirectory(settings),
        Details: Details,
        BundleKey: InstructionBundles.CheckInterpreter,
        ContractVersion: CheckInterpretation.ContractVersion,
        DenyHookStderr: CheckInterpretation.DenyHookStderr,
        UnavailableIncidentKind: AgentIncidentKind.CheckInterpreterUnavailable,
        DisplayName: "check interpreter");

    /// <summary>The configured slug, floored to the default so a blank setting cannot un-name the agent.</summary>
    public static string Slug(DelegationSettings settings) =>
        string.IsNullOrWhiteSpace(settings.CheckInterpreterAgentSlug)
            ? DefaultSlug
            : settings.CheckInterpreterAgentSlug.Trim();

    /// <summary>
    /// Where the specialist runs. Explicit config wins; otherwise the first allowed root's
    /// <c>.antiphon\check-interpreter</c>. With no allowed roots configured (this deployment's
    /// default) it falls back under the system temp path — anywhere is fine as long as it is not a
    /// directory another agent or the operator also runs Claude in, which is the whole requirement.
    /// </summary>
    public static string ResolveWorkingDirectory(DelegationSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.CheckInterpreterWorkingDirectory))
            return settings.CheckInterpreterWorkingDirectory.Trim();

        var root = settings.AllowedRoots.FirstOrDefault(r => !string.IsNullOrWhiteSpace(r));
        return root is null
            ? Path.Combine(Path.GetTempPath(), "antiphon", DefaultDirectoryName)
            : Path.Combine(root.Trim(), ".antiphon", DefaultDirectoryName);
    }
}
