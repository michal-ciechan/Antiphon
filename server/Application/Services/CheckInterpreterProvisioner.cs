using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// Find-or-create the standing check interpreter (CARD-0047 slice 4 amendment §1.2). Idempotent,
/// called once at <c>AgentTaskCheckHostedService</c> startup and again by any check that finds the
/// agent missing — so deleting the agent is a recoverable act, not a permanent loss of the feature:
/// the check that finds it gone degrades, and the next one is warm.
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
/// <para>The standing instructions are a versioned constant (<see cref="CheckInterpretation.Contract"/>)
/// and the row is reconciled against it on every call: the code is the source of truth, the agent row
/// is its projection. A running session is never touched — the contract is re-rendered into
/// <c>--append-system-prompt</c> on the next launch, which for an AlwaysOn agent is guaranteed to
/// come.</para>
/// </summary>
public sealed class CheckInterpreterProvisioner
{
    private readonly AppDbContext _db;
    private readonly DelegationSettings _settings;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<CheckInterpreterProvisioner> _logger;
    private readonly AgentControlService? _control;
    private readonly AgentWorkspaceProvisioner? _workspace;

    public CheckInterpreterProvisioner(
        AppDbContext db,
        IOptions<DelegationSettings> settings,
        TimeProvider timeProvider,
        ILogger<CheckInterpreterProvisioner> logger,
        // Optional so a harness that wires no control service still provisions the row; without it
        // the first check simply waits for a supervision tick to bring the session up.
        AgentControlService? control = null,
        // Optional for the same reason. The floor also lands on every StartAsync, so a harness
        // without one is not a specialist without a CLAUDE.md — only one that gets it later.
        AgentWorkspaceProvisioner? workspace = null)
    {
        _db = db;
        _settings = settings.Value;
        _timeProvider = timeProvider;
        _logger = logger;
        _control = control;
        _workspace = workspace;
    }

    /// <summary>
    /// The specialist, guaranteed to exist and to be carrying the current contract. Null when the
    /// feature is switched off — the caller degrades, it does not fail.
    /// </summary>
    public async Task<Agent?> EnsureAsync(CancellationToken ct)
    {
        if (!_settings.CheckInterpreterEnabled)
            return null;

        var slug = Slug(_settings);
        var existing = await _db.Agents.FirstOrDefaultAsync(a => a.Slug == slug, ct);
        if (existing is not null)
        {
            await ReconcileAsync(existing, ct);
            return existing;
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var directory = ResolveWorkingDirectory(_settings);
        PrepareWorkspace(directory);

        var agent = new Agent
        {
            Id = Guid.NewGuid(),
            Name = slug,
            Slug = slug,
            WorkingDirectory = directory,
            Details =
                "Antiphon furniture, not a stray: the standing specialist that reads delegate "
                + "check-in bundles and says what they look like (CARD-0047). Supervised, haiku "
                + "tier, no tools. Its instructions are reconciled from code on every startup.",
            Status = AgentStatus.Idle,
            // Haiku. The work is reading a few KB of facts and picking one of five words; paying a
            // frontier tier per check would cost more than the checks are worth.
            ModelLevel = AgentModelLevel.Low,
            AlwaysOn = true,
            RemoteControlEnabled = false,
            IsPoolDelegate = false,
            SystemPromptAppend = CheckInterpretation.Contract,
            CreatedAt = now,
            UpdatedAt = now,
        };
        _db.Agents.Add(agent);
        await _db.SaveChangesAsync(ct);

        // The CLAUDE.md floor (CARD-0059), after PrepareWorkspace has armed the hook so the generated
        // text knows this agent has no tools. The specialist is the agent the floor was designed
        // against: its directory is a bare scratch path with nothing in it to read.
        _workspace?.Provision(agent);

        _logger.LogInformation(
            "Provisioned the check interpreter '{Slug}' ({AgentId}) in {Directory}",
            slug, agent.Id, directory);

        // Start it NOW rather than waiting for a supervision tick, so the first check after a
        // deployment has a warm session to talk to instead of degrading. Best-effort: a failure
        // here costs one cold check, and the supervisor picks the agent up regardless.
        if (_control is not null)
        {
            try
            {
                // IgnoreSubscriptionQuota: deployment warm-up is not a human choosing a provider.
                await _control.StartAsync(
                    agent.Id, new StartAgentRequest(IgnoreSubscriptionQuota: true), ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(
                    ex, "Could not start the newly provisioned check interpreter '{Slug}'; "
                    + "supervision will bring it up", slug);
            }
        }

        return agent;
    }

    /// <summary>
    /// The row is a projection of the code. Only the standing contract is reconciled: the operator
    /// still owns the agent's own knobs, and overwriting those would make the UI lie about what the
    /// operator had set. A running session is deliberately left alone — the contract rides
    /// <c>--append-system-prompt</c> on every launch, so the next one carries it.
    /// </summary>
    private async Task ReconcileAsync(Agent agent, CancellationToken ct)
    {
        // The scratch directory and its deny-all hook are re-checked every time and cost nothing
        // when they are already right — this is how a specialist whose workspace got cleaned up
        // (a temp sweep, a manual delete) heals instead of quietly regaining tool access.
        PrepareWorkspace(agent.WorkingDirectory);
        // Same self-healing reasoning as the hook, one file over: a swept scratch directory gets its
        // floor back, and the hand-written stopgap that predates this service is adopted under the
        // marker the first time this runs against it.
        _workspace?.Provision(agent);

        if (string.Equals(agent.SystemPromptAppend, CheckInterpretation.Contract, StringComparison.Ordinal))
            return;

        _logger.LogInformation(
            "Reconciling the check interpreter '{Slug}' onto contract v{Version}",
            agent.Slug, CheckInterpretation.ContractVersion);
        agent.SystemPromptAppend = CheckInterpretation.Contract;
        agent.UpdatedAt = _timeProvider.GetUtcNow().UtcDateTime;
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>Create the scratch directory and (re)write the deny-all PreToolUse hook into it.</summary>
    private void PrepareWorkspace(string directory)
    {
        try
        {
            Directory.CreateDirectory(directory);

            var hookPath = Path.Combine(
                directory,
                CheckInterpretation.DenyHookRelativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(hookPath)!);

            // Compare before writing: the file is versioned with the constant, and a no-op ensure
            // must stay a no-op (nothing downstream watches this path, but a churning mtime is the
            // kind of noise that makes a real change invisible).
            var current = File.Exists(hookPath) ? File.ReadAllText(hookPath) : null;
            if (!string.Equals(current, CheckInterpretation.DenyAllToolsSettingsJson, StringComparison.Ordinal))
                File.WriteAllText(hookPath, CheckInterpretation.DenyAllToolsSettingsJson);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Not fatal, and deliberately loud: the contract still tells the specialist to use no
            // tools, so this degrades the hard guarantee to a soft one rather than losing the agent.
            _logger.LogWarning(
                ex, "Could not prepare the check interpreter's workspace at {Directory}; "
                + "the deny-all tool hook may not be armed", directory);
        }
    }

    /// <summary>The configured slug, floored to the default so a blank setting cannot un-name the agent.</summary>
    public static string Slug(DelegationSettings settings) =>
        string.IsNullOrWhiteSpace(settings.CheckInterpreterAgentSlug)
            ? "antiphon-check-interpreter"
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
            ? Path.Combine(Path.GetTempPath(), "antiphon", "check-interpreter")
            : Path.Combine(root.Trim(), ".antiphon", "check-interpreter");
    }
}
