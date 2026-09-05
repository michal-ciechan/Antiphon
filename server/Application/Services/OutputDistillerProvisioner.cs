using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// Facade over <see cref="StandingSpecialistProvisioner"/> for the standing output-distiller seat
/// (CARD-0330 S2). Constructor, <see cref="EnsureAsync"/>, <see cref="Slug"/>, and
/// <see cref="ResolveWorkingDirectory"/> mirror <see cref="CheckInterpreterProvisioner"/> so the
/// two seats share find-or-create, reconcile, and the deny-all hook.
/// </summary>
public sealed class OutputDistillerProvisioner
{
    public const string DefaultSlug = "antiphon-output-distiller";
    public const string DefaultDirectoryName = "output-distiller";

    public const string Details =
        "Antiphon furniture, not a stray: the standing specialist that distils a finished "
        + "delegate report into the signal the caller needs (CARD-0330). Supervised, haiku "
        + "tier, no tools. Its instructions are reconciled from code on every startup.";

    private readonly StandingSpecialistProvisioner _inner;
    private readonly DelegationSettings _settings;

    public OutputDistillerProvisioner(
        AppDbContext db,
        IOptions<DelegationSettings> settings,
        TimeProvider timeProvider,
        ILogger<OutputDistillerProvisioner> logger,
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
        if (!_settings.OutputDistillerEnabled)
            return null;

        return await _inner.EnsureAsync(Spec(_settings), ct);
    }

    public static SpecialistSpec Spec(DelegationSettings settings) => new(
        Role: AgentTaskRole.Distill,
        Slug: Slug(settings),
        WorkingDirectory: ResolveWorkingDirectory(settings),
        Details: Details,
        BundleKey: InstructionBundles.OutputDistiller,
        ContractVersion: OutputDistillation.ContractVersion,
        DenyHookStderr: OutputDistillation.DenyHookStderr,
        UnavailableIncidentKind: AgentIncidentKind.OutputDistillerUnavailable,
        DisplayName: "output distiller");

    /// <summary>The configured slug, floored to the default so a blank setting cannot un-name the agent.</summary>
    public static string Slug(DelegationSettings settings) =>
        string.IsNullOrWhiteSpace(settings.OutputDistillerAgentSlug)
            ? DefaultSlug
            : settings.OutputDistillerAgentSlug.Trim();

    /// <summary>
    /// Where the specialist runs. Explicit config wins; otherwise the first allowed root's
    /// <c>.antiphon\output-distiller</c>. With no allowed roots configured it falls back under the
    /// system temp path — anywhere is fine as long as it is not a directory another agent or the
    /// operator also runs Claude in, which is the whole requirement.
    /// </summary>
    public static string ResolveWorkingDirectory(DelegationSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.OutputDistillerWorkingDirectory))
            return settings.OutputDistillerWorkingDirectory.Trim();

        var root = settings.AllowedRoots.FirstOrDefault(r => !string.IsNullOrWhiteSpace(r));
        return root is null
            ? Path.Combine(Path.GetTempPath(), "antiphon", DefaultDirectoryName)
            : Path.Combine(root.Trim(), ".antiphon", DefaultDirectoryName);
    }
}
