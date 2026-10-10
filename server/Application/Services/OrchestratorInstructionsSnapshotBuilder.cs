using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// CARD-0822. Reads the covered stores into a plain snapshot. Occupancy is carried and not rendered.
/// <see cref="ReadPipelinePolicy"/> reads the same stores as the CARD-0881 effective-settings read (GET /api/agent-tasks/pipeline?projectId=); occupancy stays out of the file.
/// </summary>
public sealed class OrchestratorInstructionsSnapshotBuilder(
    AppDbContext db,
    IOptions<DelegationSettings> settings,
    HostBudgetService budgets,
    DispatchConcurrencySettingsService concurrency,
    RunnerDefaultSettingsService runnerDefaults,
    TimeProvider clock,
    IServiceProvider services)
{
    private readonly DelegationSettings _settings = settings.Value;

    public async Task<OrchestratorInstructionsSnapshot> BuildAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        return new OrchestratorInstructionsSnapshot(
            await ReadPipelinePolicy(ct),
            await ReadRunnersAsync(ct),
            await ReadDefaultsAsync(ct),
            await ReadHoldsAsync(now, ct),
            await ReadPinsAsync(now, ct),
            Levels(),
            _settings.OrchestratorInstructions.StandingInstructions.ToArray());
    }

    /// <summary>
    /// Effective global role policy from <see cref="DispatchConcurrencySettingsService"/> when that
    /// read works. <see cref="DelegationSettings.RolePolicy"/> is the fallback, and it still supplies
    /// level, escalate-to, and kind. The local budget is always <see cref="HostBudgetService"/>.
    /// </summary>
    public async Task<PipelineCapsSection> ReadPipelinePolicy(CancellationToken ct)
    {
        EffectivePolicy? policy = null;
        try
        {
            policy = await concurrency.ReadEffectiveAsync(null, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            policy = null;
        }

        HostLimit? local = null;
        try
        {
            local = await budgets.EffectiveAsync("local", ct);
        }
        catch (NotFoundException)
        {
            local = null;
        }

        var maxConcurrent = local?.Effective ?? _settings.MaxConcurrentTasks;
        var maxConcurrentSource = local?.Source ?? "config";
        var maxOpen = policy?.MaxParallel ?? _settings.MaxOpenTasks;
        var roles = policy is null ? RolesFromSettings() : RolesFromPolicy(policy);
        return new PipelineCapsSection(
            maxConcurrent,
            maxConcurrentSource,
            maxOpen,
            _settings.DefaultWorkerWorkspace.ToString(),
            _settings.MinOrchestratorLevel.ToString(),
            roles);
    }

    private IReadOnlyList<RoleInstructionRow> RolesFromPolicy(EffectivePolicy policy)
    {
        var rows = new List<RoleInstructionRow>(policy.Roles.Count);
        foreach (var role in policy.Roles.OrderBy(role => role.Role.ToString(), StringComparer.Ordinal))
        {
            _settings.RolePolicy.TryGetValue(role.Role.ToString(), out var entry);
            rows.Add(new RoleInstructionRow(
                role.Role.ToString(),
                role.MaxParallel,
                (entry?.Level ?? _settings.DefaultLevel).ToString(),
                entry?.EscalateTo?.ToString(),
                entry?.Kind?.ToString()));
        }

        return rows;
    }

    private IReadOnlyList<RoleInstructionRow> RolesFromSettings() =>
        _settings.RolePolicy
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => new RoleInstructionRow(
                pair.Key,
                pair.Value.RecommendedInFlight,
                pair.Value.Level.ToString(),
                pair.Value.EscalateTo?.ToString(),
                pair.Value.Kind?.ToString()))
            .ToArray();

    private async Task<IReadOnlyList<RunnerInstructionRow>> ReadRunnersAsync(CancellationToken ct)
    {
        var directory = services.GetService<PhoneHomeRunnerDirectory>();
        if (directory is null)
            return [];

        IReadOnlyList<SessionRunnerCatalogueEntryDto> entries;
        try
        {
            entries = await SessionRunnerCatalogue.ListAsync(
                directory,
                db,
                _settings,
                services.GetService<RemoteWorkspacePreparer>(),
                ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return [];
        }

        var rows = new List<RunnerInstructionRow>(entries.Count);
        foreach (var entry in entries)
            rows.Add(await ToRunnerAsync(entry, ct));
        return rows;
    }

    private async Task<RunnerInstructionRow> ToRunnerAsync(SessionRunnerCatalogueEntryDto entry, CancellationToken ct)
    {
        var hostId = string.Equals(entry.RunnerId, RunnerPlatformWire.DesktopId, StringComparison.Ordinal)
            ? "local"
            : entry.RunnerId;
        int? effective = entry.Capacity;
        var source = "declared";
        try
        {
            var limit = await budgets.EffectiveAsync(hostId, ct);
            effective = limit.Effective;
            source = limit.Source;
        }
        catch (NotFoundException)
        {
            effective = entry.Capacity;
            source = "runner";
        }

        var retired = entry.DispatchEligible && !entry.Draining && !entry.AcceptingNewWork;
        return new RunnerInstructionRow(
            entry.RunnerId,
            entry.Platform,
            entry.DispatchEligible,
            entry.Capacity,
            effective,
            source,
            entry.Draining,
            retired,
            entry.Features.ToArray(),
            entry.Occupied,
            entry.CapacityObservedAt?.ToString("o"));
    }

    private async Task<RunnerDefaultsSection> ReadDefaultsAsync(CancellationToken ct)
    {
        var snapshot = await runnerDefaults.EnsureInitializedAsync(ct);
        var dto = await runnerDefaults.GetAsync(ct);
        var kinds = snapshot.KindDefaults
            .OrderBy(pair => pair.Key.ToString(), StringComparer.Ordinal)
            .Select(pair => new KindDefaultInstruction(pair.Key.ToString(), pair.Value))
            .ToArray();
        return new RunnerDefaultsSection(
            dto.Revision,
            dto.LastProvenance,
            dto.GlobalRunnerId,
            kinds,
            dto.LastReason);
    }

    private async Task<IReadOnlyList<HoldInstructionRow>> ReadHoldsAsync(DateTime now, CancellationToken ct)
    {
        var rows = await db.ModelAvailabilityHolds.AsNoTracking()
            .Where(hold => hold.ClearedAt == null && (hold.DisabledUntil == null || hold.DisabledUntil > now))
            .OrderByDescending(hold => hold.HitAt)
            .ThenBy(hold => hold.ModelAlias)
            .ToListAsync(ct);
        return rows.Select(hold => new HoldInstructionRow(
            hold.Kind.ToString(),
            hold.ModelAlias,
            hold.Source.ToString(),
            FormatInstant(hold.DisabledUntil),
            hold.Reason,
            new DateTimeOffset(DateTime.SpecifyKind(hold.HitAt, DateTimeKind.Utc)))).ToArray();
    }

    private async Task<IReadOnlyList<PinInstructionRow>> ReadPinsAsync(DateTime now, CancellationToken ct)
    {
        var pins = await db.RoutingPins.AsNoTracking()
            .Where(pin => pin.ClearedAt == null && (pin.NotAfter == null || pin.NotAfter > now))
            .ToListAsync(ct);
        var cardIds = pins.Where(pin => pin.CardId is not null).Select(pin => pin.CardId!.Value).Distinct().ToArray();
        var cards = cardIds.Length == 0
            ? []
            : await (
                from card in db.Cards.AsNoTracking()
                join board in db.Boards.AsNoTracking() on card.BoardId equals board.Id
                where cardIds.Contains(card.Id)
                select new { card.Id, card.Identifier, Board = board.Name }).ToListAsync(ct);
        var byCard = cards.ToDictionary(card => card.Id);

        return pins.Select(pin =>
        {
            string? board = null;
            string? identifier = null;
            if (pin.CardId is Guid cardId && byCard.TryGetValue(cardId, out var card))
            {
                board = card.Board;
                identifier = card.Identifier;
            }

            var forbidden = string.IsNullOrWhiteSpace(pin.ForbiddenAliases)
                ? []
                : pin.ForbiddenAliases.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return new PinInstructionRow(
                pin.CardId is null,
                board,
                identifier,
                pin.Role.ToString(),
                pin.Candidates.Select(candidate => candidate.Describe()).ToArray(),
                forbidden,
                pin.Strength.ToString(),
                pin.Provenance.ToString(),
                FormatInstant(pin.NotBefore),
                FormatInstant(pin.NotAfter),
                pin.Reason,
                new DateTimeOffset(DateTime.SpecifyKind(pin.CreatedAt, DateTimeKind.Utc)));
        }).ToArray();
    }

    private static IReadOnlyList<LevelInstructionRow> Levels()
    {
        AgentKind[] kinds = [AgentKind.ClaudeCode, AgentKind.Codex, AgentKind.Grok];
        AgentModelLevel[] levels =
        [
            AgentModelLevel.Frontier,
            AgentModelLevel.High,
            AgentModelLevel.Medium,
            AgentModelLevel.Low,
        ];
        var rows = new List<LevelInstructionRow>(kinds.Length * levels.Length);
        foreach (var kind in kinds)
        {
            foreach (var level in levels)
                rows.Add(new LevelInstructionRow(kind.ToString(), level.ToString(), ModelLevelAliases.For(kind, level)));
        }

        return rows;
    }

    private static string? FormatInstant(DateTime? value) =>
        value is DateTime instant
            ? DateTime.SpecifyKind(instant, DateTimeKind.Utc).ToString("yyyy-MM-ddTHH:mm:ssZ")
            : null;
}
