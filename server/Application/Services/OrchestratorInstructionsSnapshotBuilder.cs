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
    KeepSixService keepSix,
    TimeProvider clock,
    IServiceProvider services)
{
    private readonly DelegationSettings _settings = settings.Value;

    public async Task<OrchestratorInstructionsSnapshot> BuildAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var failures = new Dictionary<string, string>(StringComparer.Ordinal);
        async Task<T> ReadAsync<T>(string section, string part, Func<Task<T>> read, T unavailable)
        {
            try { return await read(); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Placeholder data is not an observation: renderer and differ read this state
                // before looking at the section. Exception details do not create settings revisions.
                failures[section] = $"{part} read failed";
                return unavailable;
            }
        }

        return new OrchestratorInstructionsSnapshot(
            await ReadAsync("caps", "policy", () => ReadPipelinePolicy(ct), new PipelineCapsSection(0, "", 0, "", "", [])),
            await ReadAsync<IReadOnlyList<RunnerInstructionRow>>("runners", "runners", () => ReadRunnersAsync(ct), []),
            await ReadAsync("defaults", "defaults", () => ReadDefaultsAsync(ct), new RunnerDefaultsSection(0, null, null, [], null)),
            await ReadAsync<IReadOnlyList<HoldInstructionRow>>("holds", "holds", () => ReadHoldsAsync(now, ct), []),
            await ReadAsync<IReadOnlyList<PinInstructionRow>>("pins", "pins", () => ReadPinsAsync(now, ct), []),
            Levels(),
            _settings.OrchestratorInstructions.StandingInstructions.ToArray(),
            await ReadAsync("matrix", "matrix", () => ReadMatrixAsync(ct), new MatrixSection(_settings.DefaultComplexity.GetValueOrDefault().ToString(), [])),
            failures,
            await keepSix.ReadInstructionRowsAsync(ct));
    }

    /// <summary>
    /// Effective global role policy and local host budget. Configuration supplies level,
    /// escalate-to and kind. A failed read is recorded by BuildAsync as an unknown section.
    /// </summary>
    public async Task<PipelineCapsSection> ReadPipelinePolicy(CancellationToken ct)
    {
        var policy = await concurrency.ReadEffectiveAsync(null, ct);

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
        var maxOpen = policy.MaxParallel;
        var roles = RolesFromPolicy(policy);
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

    private async Task<IReadOnlyList<RunnerInstructionRow>> ReadRunnersAsync(CancellationToken ct)
    {
        var directory = services.GetService<PhoneHomeRunnerDirectory>();
        if (directory is null)
            return [];

        var entries = await SessionRunnerCatalogue.ListAsync(
            directory, db, _settings, services.GetService<RemoteWorkspacePreparer>(), ct);
        if (entries.Any(entry => entry.UnavailableReason == "catalogue_read_failed"))
            throw new InvalidOperationException("Runner catalogue read failed.");

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

    private async Task<MatrixSection> ReadMatrixAsync(CancellationToken ct)
    {
        var chains = services.GetRequiredService<ComplexityChainService>();
        var defaultTier = _settings.DefaultComplexity.GetValueOrDefault();
        var rows = new List<MatrixInstructionRow>();
        foreach (var role in ComplexityRoutingService.RoutableRoles)
        {
            // ListAsync(role) uses GetEffectiveAsync for the three tiers, including the pin overlay.
            var list = await chains.ListAsync(role, ct);
            var cells = list.Chains.ToDictionary(cell => cell.Complexity);
            var effective = cells[defaultTier].Effective!;
            rows.Add(new MatrixInstructionRow(
                role.ToString(),
                MatrixCell(cells[TaskComplexity.Hard]),
                MatrixCell(cells[TaskComplexity.Medium]),
                MatrixCell(cells[TaskComplexity.Easy]),
                UnknownRead(effective, "stage pin", "chain", "chain metadata", "standing agent", "availability")
                    ?? (effective.WithheldReason is { } reason ? $"withheld: {reason}" : MatrixCandidates(effective.Candidates)),
                UnknownRead(effective, "stage pin", "chain", "standing agent") ?? (effective.Source switch
                {
                    "pin" => "stage pin",
                    "pin+chain" => "stage pin + chain",
                    "agent" => "stage pin (standing agent)",
                    _ => effective.Source,
                })));
        }
        return new MatrixSection(defaultTier.ToString(), rows);
    }

    private static string MatrixCell(ComplexityChainDto cell) =>
        UnknownRead(cell.Effective, "chain") ?? MatrixCandidates(cell.Candidates);

    private static string? UnknownRead(ComplexityEffectiveDto? effective, params string[] parts)
    {
        // The effective read contract appends semicolon-separated "<part> read failed (<type>)"
        // diagnostics. Match complete parts, so metadata faults do not erase observed cells.
        var reasons = effective?.WithheldReason?.Split(';', StringSplitOptions.TrimEntries) ?? [];
        var failed = parts.Where(part => reasons.Any(reason =>
            reason.StartsWith(part + " read failed (", StringComparison.Ordinal) && reason.EndsWith(')'))).ToArray();
        return failed.Length == 0 ? null : $"unknown ({string.Join(", ", failed)} read failed)";
    }

    private static string MatrixCandidates(IReadOnlyList<ComplexityCandidateDto> candidates) =>
        candidates.Count == 0 ? "unset" : string.Join(", ", candidates.Select(candidate =>
            $"{candidate.AgentKind}/{candidate.ModelLevel} ({candidate.Alias})"));

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
