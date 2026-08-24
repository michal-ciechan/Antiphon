using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// CARD-0162: sweep herdr sessions for sustained status disagreement with
/// <see cref="SessionMessageQueueService.IsWorkingAsync"/>. Detection only — Warning incident;
/// never kills, retypes, or changes session/queue state. Dependencies are deliberately limited
/// so the never-act pin can assert by reflection that delivery/control services are not wired.
/// </summary>
public sealed class HerdrStatusCorroborationService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly AgentSessionRuntime _runtime;
    private readonly ISessionRunnerClient _runner;
    private readonly HerdrCorroborationSettings _settings;
    private readonly ILogger<HerdrStatusCorroborationService> _logger;

    public HerdrStatusCorroborationService(
        IServiceScopeFactory scopeFactory,
        AgentSessionRuntime runtime,
        ISessionRunnerClient runner,
        IOptions<SupervisionSettings> settings,
        ILogger<HerdrStatusCorroborationService> logger)
    {
        _scopeFactory = scopeFactory;
        _runtime = runtime;
        _runner = runner;
        _settings = settings.Value.HerdrCorroboration;
        _logger = logger;
    }

    /// <summary>
    /// Evaluate every Running herdr session. Returns how many NEW disagreement incidents this
    /// pass raised (not a global count — shared-DB rules apply).
    /// </summary>
    public async Task<int> SweepAsync(CancellationToken ct)
    {
        if (!_settings.Enabled)
            return 0;

        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var supervisor = scope.ServiceProvider.GetRequiredService<AgentSupervisorService>();

        var sessions = await db.AgentSessions.AsNoTracking()
            .Where(s => s.Status == SessionStatus.Running && s.SessionBackend == SessionBackend.Herdr)
            .Select(s => s.Id)
            .ToListAsync(ct);

        var raised = 0;
        foreach (var sessionId in sessions)
        {
            ct.ThrowIfCancellationRequested();
            var sessionIdText = sessionId.ToString("D");
            var agentId = await db.Agents.AsNoTracking()
                .Where(a => a.PersistentSessionId == sessionIdText)
                .Select(a => (Guid?)a.Id)
                .FirstOrDefaultAsync(ct);
            if (agentId is not Guid owner)
            {
                _logger.LogDebug(
                    "Skipping herdr corroboration for unclaimed session {SessionId}", sessionId);
                continue;
            }

            try
            {
                if (await TryRaiseAsync(db, supervisor, sessionId, owner, ct))
                    raised++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                _logger.LogWarning(ex,
                    "Herdr corroboration failed for session {SessionId}; sweep continues", sessionId);
            }
        }

        return raised;
    }

    private async Task<bool> TryRaiseAsync(
        AppDbContext db,
        AgentSupervisorService supervisor,
        Guid sessionId,
        Guid agentId,
        CancellationToken ct)
    {
        try
        {
            SessionRunnerSessionDto mapped;
            try
            {
                mapped = await _runner.GetAsync(sessionId, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogDebug(ex, "Runner GET failed for herdr corroboration of {SessionId}", sessionId);
                return false;
            }

            var herdrStatus = mapped.AgentStatus;
            var since = mapped.AgentStatusSinceUtc;
            if (string.IsNullOrWhiteSpace(herdrStatus)
                || string.Equals(herdrStatus, "unknown", StringComparison.Ordinal))
                return false;

            if (since is null)
                return false;

            var sustained = DateTime.UtcNow - since.Value;
            if (sustained < TimeSpan.FromMinutes(Math.Max(1, _settings.MinSustainedMinutes)))
                return false;

            var working = await SessionMessageQueueService.IsWorkingAsync(db, sessionId, ct);
            if (!IsDisagreement(herdrStatus, working))
                return false;

            // Pull-before-raise (CARD-0055 / stall precedent).
            await _runtime.CatchUpTranscriptAsync(sessionId, ct);
            working = await SessionMessageQueueService.IsWorkingAsync(db, sessionId, ct);
            if (!IsDisagreement(herdrStatus, working))
            {
                _logger.LogInformation(
                    "Herdr disagreement withheld for session {SessionId}: pull flipped IsWorkingAsync",
                    sessionId);
                return false;
            }

            var latest = await db.AgentIncidents.AsNoTracking()
                .Where(i => i.SessionId == sessionId && i.Kind == AgentIncidentKind.HerdrStatusDisagreement)
                .OrderByDescending(i => i.CreatedAt)
                .Select(i => new { i.CreatedAt })
                .FirstOrDefaultAsync(ct);

            if (latest is not null && latest.CreatedAt >= since.Value)
                return false; // same episode already on record

            var channelBound = await IsChannelBoundAsync(db, sessionId, ct);
            var message =
                $"Herdr agent_status '{herdrStatus}' (since {since:o}, pane session {sessionId:N}) "
                + $"disagrees with IsWorkingAsync={working}. "
                + "Corroboration only — no automatic action was or will be taken.";

            await supervisor.RecordIncidentAsync(
                agentId,
                sessionId,
                AgentIncidentKind.HerdrStatusDisagreement,
                AlertSeverity.Warning,
                message,
                failureReason: $"herdr-status:{herdrStatus}",
                raiseAlert: channelBound,
                ct: ct);
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(ex, "Herdr corroboration evaluation failed for {SessionId}", sessionId);
            return false;
        }
    }

    /// <summary>
    /// Plan §5 matrix: (working|blocked)×!IsWorking → RAISE A; (idle|done)×IsWorking → RAISE B.
    /// </summary>
    internal static bool IsDisagreement(string herdrStatus, bool isWorking) =>
        herdrStatus switch
        {
            "working" or "blocked" => !isWorking,
            "idle" or "done" => isWorking,
            _ => false,
        };

    private static async Task<bool> IsChannelBoundAsync(AppDbContext db, Guid sessionId, CancellationToken ct)
    {
        var sessionIdText = sessionId.ToString("D");
        var agentId = await db.Agents
            .Where(a => a.PersistentSessionId == sessionIdText)
            .Select(a => (Guid?)a.Id)
            .FirstOrDefaultAsync(ct);
        if (agentId is not Guid id)
            return false;

        return await db.ChatChannels.AsNoTracking().AnyAsync(c => c.AgentId == id, ct);
    }
}
