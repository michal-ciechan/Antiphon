using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// Facade over <see cref="StandingSpecialistProvisioner"/> for the standing diagnose seat
/// (CARD-0352 S2). Constructor, <see cref="EnsureAsync"/>, <see cref="Slug"/>, and
/// <see cref="ResolveWorkingDirectory"/> mirror <see cref="CheckInterpreterProvisioner"/> so the
/// two seats share find-or-create, reconcile, and the deny-all hook.
/// </summary>
public sealed class DiagnoseProvisioner
{
    public const string DefaultSlug = "antiphon-diagnose";
    public const string DefaultDirectoryName = "diagnose";

    public const string Details =
        "Antiphon furniture, not a stray: the standing specialist that titles untitled tasks "
        + "and labels unlabelled cards (CARD-0352). Supervised, haiku tier, no tools. Its "
        + "instructions are reconciled from code on every startup.";

    private readonly StandingSpecialistProvisioner _inner;
    private readonly DelegationSettings _settings;

    public DiagnoseProvisioner(
        AppDbContext db,
        IOptions<DelegationSettings> settings,
        TimeProvider timeProvider,
        ILogger<DiagnoseProvisioner> logger,
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
        if (!_settings.DiagnoseEnabled)
            return null;

        return await _inner.EnsureAsync(Spec(_settings), ct);
    }

    public static SpecialistSpec Spec(DelegationSettings settings) => new(
        Role: AgentTaskRole.Diagnose,
        Slug: Slug(settings),
        WorkingDirectory: ResolveWorkingDirectory(settings),
        Details: Details,
        BundleKey: InstructionBundles.Diagnose,
        ContractVersion: Diagnosis.ContractVersion,
        DenyHookStderr: Diagnosis.DenyHookStderr,
        UnavailableIncidentKind: AgentIncidentKind.DiagnoseUnavailable,
        DisplayName: "diagnose agent");

    /// <summary>The configured slug, floored to the default so a blank setting cannot un-name the agent.</summary>
    public static string Slug(DelegationSettings settings) =>
        string.IsNullOrWhiteSpace(settings.DiagnoseAgentSlug)
            ? DefaultSlug
            : settings.DiagnoseAgentSlug.Trim();

    /// <summary>
    /// Where the specialist runs. Explicit config wins; otherwise the first allowed root's
    /// <c>.antiphon\diagnose</c>. With no allowed roots configured it falls back under the system
    /// temp path — anywhere is fine as long as it is not a directory another agent or the operator
    /// also runs Claude in, which is the whole requirement.
    /// </summary>
    public static string ResolveWorkingDirectory(DelegationSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.DiagnoseWorkingDirectory))
            return settings.DiagnoseWorkingDirectory.Trim();

        var root = settings.AllowedRoots.FirstOrDefault(r => !string.IsNullOrWhiteSpace(r));
        return root is null
            ? Path.Combine(Path.GetTempPath(), "antiphon", DefaultDirectoryName)
            : Path.Combine(root.Trim(), ".antiphon", DefaultDirectoryName);
    }
}
