using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// Find-or-create a standing specialist (CARD-0330 D3 / CARD-0352 D2), parameterised by
/// <see cref="SpecialistSpec"/>. Idempotent. The Check / Distill / Diagnose facades keep their
/// own constructors, DI registrations, and tests.
///
/// <para>Three properties of the row it creates are load-bearing, and none of them is cosmetic:</para>
/// <list type="bullet">
/// <item><b><see cref="Agent.AlwaysOn"/></b> — supervision is not re-implemented here. The existing
/// sweep already ensures every AlwaysOn agent that is not user-suspended has a live session, so a
/// specialist whose session dies is somebody else's already-tested problem.</item>
/// <item><b><see cref="Agent.IsPoolDelegate"/> = false</b> — the pool janitor filters on that flag
/// before retiring or deleting anything, so a standing specialist can never be swept away as an
/// idle warm delegate. It also keeps the settle path's pool handshake off it entirely.</item>
/// <item><b>Its own working directory</b> — Claude's transcript root is per-cwd, so a distinct
/// directory means a distinct transcript project dir. That is the CARD-0006 stranger-transcript
/// hazard removed BY CONSTRUCTION rather than by relying on the C1-C4 binding rules to catch it.</item>
/// </list>
///
/// <para>The standing instructions are a versioned bundle and the row is reconciled against it on
/// every call: the code is the source of truth, the agent row is its projection. A running session
/// is never touched — the contract is re-rendered into <c>--append-system-prompt</c> on the next
/// launch, which for an AlwaysOn agent is guaranteed to come.</para>
/// </summary>
public sealed class StandingSpecialistProvisioner
{
    private readonly AppDbContext _db;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger _logger;
    private readonly AgentControlService? _control;
    private readonly AgentWorkspaceProvisioner? _workspace;

    public StandingSpecialistProvisioner(
        AppDbContext db,
        TimeProvider timeProvider,
        ILogger logger,
        AgentControlService? control = null,
        AgentWorkspaceProvisioner? workspace = null)
    {
        _db = db;
        _timeProvider = timeProvider;
        _logger = logger;
        _control = control;
        _workspace = workspace;
    }

    /// <summary>
    /// The specialist, guaranteed to exist and to be carrying the current contract. The caller
    /// owns the feature switch — this method always provisions.
    /// </summary>
    public async Task<Agent> EnsureAsync(SpecialistSpec spec, CancellationToken ct)
    {
        var existing = await _db.Agents.FirstOrDefaultAsync(a => a.Slug == spec.Slug, ct);
        if (existing is not null)
        {
            await ReconcileAsync(existing, spec, ct);
            return existing;
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        PrepareWorkspace(spec);

        var agent = new Agent
        {
            Id = Guid.NewGuid(),
            Name = spec.Slug,
            Slug = spec.Slug,
            WorkingDirectory = spec.WorkingDirectory,
            Details = spec.Details,
            Status = AgentStatus.Idle,
            ModelLevel = AgentModelLevel.Low,
            AlwaysOn = true,
            RemoteControlEnabled = false,
            IsPoolDelegate = false,
            SystemPromptAppend = spec.Contract,
            CreatedAt = now,
            UpdatedAt = now,
        };
        _db.Agents.Add(agent);
        await _db.SaveChangesAsync(ct);

        _workspace?.Provision(agent);

        _logger.LogInformation(
            "Provisioned the {DisplayName} '{Slug}' ({AgentId}) in {Directory}",
            spec.DisplayName, spec.Slug, agent.Id, spec.WorkingDirectory);

        if (_control is not null)
        {
            try
            {
                await _control.StartAsync(
                    agent.Id, new StartAgentRequest(IgnoreSubscriptionQuota: true), ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(
                    ex, "Could not start the newly provisioned {DisplayName} '{Slug}'; "
                    + "supervision will bring it up", spec.DisplayName, spec.Slug);
            }
        }

        return agent;
    }

    private async Task ReconcileAsync(Agent agent, SpecialistSpec spec, CancellationToken ct)
    {
        PrepareWorkspace(spec with { WorkingDirectory = agent.WorkingDirectory });
        _workspace?.Provision(agent);

        if (string.Equals(agent.SystemPromptAppend, spec.Contract, StringComparison.Ordinal))
            return;

        _logger.LogInformation(
            "Reconciling the {DisplayName} '{Slug}' onto contract v{Version}",
            spec.DisplayName, agent.Slug, spec.ContractVersion);
        agent.SystemPromptAppend = spec.Contract;
        agent.UpdatedAt = _timeProvider.GetUtcNow().UtcDateTime;
        await _db.SaveChangesAsync(ct);
    }

    private void PrepareWorkspace(SpecialistSpec spec)
    {
        try
        {
            Directory.CreateDirectory(spec.WorkingDirectory);

            var hookPath = Path.Combine(
                spec.WorkingDirectory,
                SpecialistSpec.DenyHookRelativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(hookPath)!);

            var current = File.Exists(hookPath) ? File.ReadAllText(hookPath) : null;
            if (!string.Equals(current, spec.DenyAllToolsSettingsJson, StringComparison.Ordinal))
                File.WriteAllText(hookPath, spec.DenyAllToolsSettingsJson);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(
                ex, "Could not prepare the {DisplayName}'s workspace at {Directory}; "
                + "the deny-all tool hook may not be armed", spec.DisplayName, spec.WorkingDirectory);
        }
    }
}
