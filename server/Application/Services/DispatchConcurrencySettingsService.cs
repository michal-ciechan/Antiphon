using System.Text.Json;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// CARD-0505. Revisioned global and per-project dispatch limits. Startup configuration is copied
/// once into the global seed; later process configuration does not replace a saved policy.
/// </summary>
public sealed class DispatchConcurrencySettingsService
{
    public const string MigrationReason = "Imported Delegation:MaxOpenTasks and Delegation:RolePolicy.";
    public const string CodeRevisionConflict = "dispatch_concurrency_revision_conflict";
    public const string CodeHuman = "dispatch_concurrency_human";

    private readonly AppDbContext _db;
    private readonly DelegationSettings _settings;
    private readonly TimeProvider _clock;
    private readonly IEventBus? _events;
    private readonly IServiceProvider? _services;

    public DispatchConcurrencySettingsService(
        AppDbContext db,
        IOptions<DelegationSettings> settings,
        TimeProvider clock,
        IEventBus? events = null,
        IServiceProvider? services = null)
    {
        _db = db;
        _settings = settings.Value;
        _clock = clock;
        _events = events;
        _services = services;
    }

    public async Task EnsureInitializedAsync(CancellationToken ct)
    {
        if (await GlobalAsync(tracking: false, ct) is not null)
            return;
        // A caller that already holds the parallel key must not take the create key underneath it.
        if (_db.Database.CurrentTransaction is not null)
            throw new InvalidOperationException("Dispatch concurrency settings are not initialized.");

        var owns = _db.Database.CurrentTransaction is null;
        var tx = owns ? await _db.Database.BeginTransactionAsync(ct) : null;
        var inserted = false;
        try
        {
            await TakeLockAsync(DispatchConcurrencyPolicy.CreateAdvisoryKey, ct);
            await TakeLockAsync(DispatchConcurrencyPolicy.ParallelAdvisoryKey, ct);
            if (await GlobalAsync(tracking: false, ct) is not null)
            {
                if (owns)
                    await tx!.CommitAsync(ct);
                return;
            }

            var now = _clock.GetUtcNow().UtcDateTime;
            var seed = DispatchConcurrencyPolicy.ImportSeed(_settings, now);
            var row = new DispatchConcurrencySettings
            {
                Id = Guid.NewGuid(),
                ScopeKey = DispatchConcurrencySettings.GlobalScopeKey,
                ProjectId = null,
                SchemaVersion = DispatchConcurrencySettings.CurrentSchemaVersion,
                OverridesJson = DispatchConcurrencyPolicy.CanonicalOverrides(DispatchConcurrencyPolicy.EmptyDocument()),
                SeedJson = WriteSeed(seed),
                Revision = 1,
                UpdatedAt = now,
                LastReason = MigrationReason,
                LastProvenance = "Migration",
            };
            _db.DispatchConcurrencySettings.Add(row);
            _db.DispatchConcurrencyRevisions.Add(History(row, null, now));
            await _db.SaveChangesAsync(ct);
            if (owns)
                await tx!.CommitAsync(ct);
            inserted = true;
        }
        catch (DbUpdateException)
        {
            if (owns && tx is not null)
                await SafeRollbackAsync(tx, ct);
            DetachMine();
            if (await GlobalAsync(tracking: false, ct) is null)
                throw;
        }
        catch
        {
            if (owns && tx is not null)
                await SafeRollbackAsync(tx, ct);
            DetachMine();
            throw;
        }
        finally
        {
            if (owns && tx is not null)
                await tx.DisposeAsync();
        }

        if (inserted)
            await PublishChangedAsync(DispatchConcurrencySettings.GlobalScopeKey, 1, 1, 0, ct);
    }

    public async Task<EffectivePolicy> ReadEffectiveAsync(Guid? projectId, CancellationToken ct)
    {
        if (await GlobalAsync(tracking: false, ct) is null)
        {
            if (_db.Database.CurrentTransaction is null)
                await EnsureInitializedAsync(ct);
            else
                throw new InvalidOperationException("Dispatch concurrency settings are not initialized.");
        }

        var owns = _db.Database.CurrentTransaction is null;
        var tx = owns
            ? await _db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, ct)
            : null;
        try
        {
            var policy = await ComposeAsync(projectId, ct);
            if (owns)
                await tx!.CommitAsync(ct);
            return policy;
        }
        finally
        {
            if (owns && tx is not null)
                await tx.DisposeAsync();
        }
    }

    /// <summary>
    /// One repeatable-read snapshot of the global policy and each requested project.
    /// A missing global row returns null and does not import a seed: pipeline reads stay read-only.
    /// </summary>
    public async Task<CoherentPolicies?> ReadCoherentAsync(
        IReadOnlyCollection<Guid?> scopes, CancellationToken ct)
    {
        var owns = _db.Database.CurrentTransaction is null;
        var tx = owns
            ? await _db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, ct)
            : null;
        try
        {
            var global = await GlobalAsync(tracking: false, ct);
            if (global is null)
            {
                if (owns)
                    await tx!.CommitAsync(ct);
                return null;
            }

            var seed = ReadSeed(global.SeedJson, global.UpdatedAt);
            var globalOverrides = ReadOverrides(global.OverridesJson);
            var wanted = scopes
                .Where(id => id is Guid)
                .Select(id => id!.Value.ToString("D"))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            var projects = wanted.Length == 0
                ? []
                : await _db.DispatchConcurrencySettings.AsNoTracking()
                    .Where(row => wanted.Contains(row.ScopeKey))
                    .ToListAsync(ct);
            var byKey = projects.ToDictionary(row => row.ScopeKey, StringComparer.Ordinal);
            var resolved = new Dictionary<string, EffectivePolicy>(StringComparer.Ordinal)
            {
                [DispatchConcurrencySettings.GlobalScopeKey] = DispatchConcurrencyPolicy.Resolve(
                    seed, globalOverrides, null, global.Revision, 0),
            };
            foreach (var id in scopes.OfType<Guid>().Distinct())
            {
                byKey.TryGetValue(id.ToString("D"), out var row);
                resolved[id.ToString("D")] = DispatchConcurrencyPolicy.Resolve(
                    seed,
                    globalOverrides,
                    row is null ? null : ReadOverrides(row.OverridesJson),
                    global.Revision,
                    row?.Revision ?? 0);
            }

            if (owns)
                await tx!.CommitAsync(ct);
            return new CoherentPolicies(resolved);
        }
        finally
        {
            if (owns && tx is not null)
                await tx.DisposeAsync();
        }
    }

    public async Task<DispatchConcurrencyGlobalDto> GetGlobalAsync(CancellationToken ct)
    {
        await EnsureInitializedAsync(ct);
        return await ProjectGlobalAsync(ct);
    }

    public async Task<DispatchConcurrencyProjectDto> GetProjectAsync(Guid projectId, CancellationToken ct)
    {
        await EnsureInitializedAsync(ct);
        await RequireProjectAsync(projectId, ct);
        return await ProjectScopedAsync(projectId, ct);
    }

    public async Task<DispatchConcurrencyGlobalDto> PutGlobalAsync(
        PutDispatchConcurrencyRequest request, Guid? callerTaskId, CancellationToken ct) =>
        (DispatchConcurrencyGlobalDto)await PutAsync(projectId: null, request, callerTaskId, ct);

    public async Task<DispatchConcurrencyProjectDto> PutProjectAsync(
        Guid projectId, PutDispatchConcurrencyRequest request, Guid? callerTaskId, CancellationToken ct)
    {
        await RequireProjectAsync(projectId, ct);
        return (DispatchConcurrencyProjectDto)(await PutAsync(projectId, request, callerTaskId, ct))!;
    }

    public async Task<DispatchConcurrencyRevisionPageDto> RevisionsAsync(
        Guid? projectId, long? beforeRevision, int limit, CancellationToken ct)
    {
        if (limit is < 1 or > 100)
            throw new ValidationException("limit", "limit must be from 1 to 100.");
        if (beforeRevision is < 0)
            throw new ValidationException("beforeRevision", "beforeRevision must be a non-negative revision.");
        if (projectId is Guid id)
            await RequireProjectAsync(id, ct);

        var scope = projectId is Guid project ? project.ToString("D") : DispatchConcurrencySettings.GlobalScopeKey;
        var settingsId = await _db.DispatchConcurrencySettings.AsNoTracking()
            .Where(row => row.ScopeKey == scope)
            .Select(row => (Guid?)row.Id)
            .SingleOrDefaultAsync(ct);
        if (settingsId is null)
            return new DispatchConcurrencyRevisionPageDto([], null);

        var query = _db.DispatchConcurrencyRevisions.AsNoTracking().Where(row => row.SettingsId == settingsId);
        if (beforeRevision is long before)
            query = query.Where(row => row.Revision < before);
        var rows = await query.OrderByDescending(row => row.Revision).Take(limit + 1).ToListAsync(ct);
        var page = rows.Take(limit).Select(ToRevision).ToList();
        long? next = rows.Count > limit ? page[^1].Revision : null;
        return new DispatchConcurrencyRevisionPageDto(page, next);
    }

    private async Task<object> PutAsync(
        Guid? projectId, PutDispatchConcurrencyRequest request, Guid? callerTaskId, CancellationToken ct)
    {
        ValidatePut(request, projectId is not null);
        await EnsureInitializedAsync(ct);
        var errors = DispatchConcurrencyPolicy.ValidateOverrides(request.Overrides);
        if (errors.Count > 0)
        {
            throw new ValidationException(errors
                .GroupBy(error => error.Field)
                .ToDictionary(group => group.Key, group => group.Select(error => error.Message).ToArray()));
        }

        if (!DispatchConcurrencyPolicy.TryParseOverrides(request.Overrides, out var incoming))
            throw new ValidationException("overrides", "overrides are not a valid policy.");

        var scope = projectId is Guid id ? id.ToString("D") : DispatchConcurrencySettings.GlobalScopeKey;
        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        var committed = false;
        long globalRevision = 0;
        long projectRevision = 0;
        try
        {
            await TakeLockAsync(DispatchConcurrencyPolicy.CreateAdvisoryKey, ct);
            await TakeLockAsync(DispatchConcurrencyPolicy.ParallelAdvisoryKey, ct);
            var global = await GlobalAsync(tracking: true, ct)
                ?? throw new InvalidOperationException("Dispatch concurrency settings are not initialized.");
            globalRevision = global.Revision;
            var row = projectId is null
                ? global
                : await _db.DispatchConcurrencySettings
                    .SingleOrDefaultAsync(item => item.ScopeKey == scope, ct);
            projectRevision = projectId is null ? 0 : row?.Revision ?? 0;
            if (projectId is not null && request.ExpectedGlobalRevision != globalRevision)
                throw RevisionConflict(globalRevision, projectRevision);
            var currentRevision = row?.Revision ?? 0;
            if (request.ExpectedRevision != currentRevision)
                throw RevisionConflict(globalRevision, projectRevision);

            if (string.Equals(request.Provenance, "Auto", StringComparison.Ordinal))
            {
                if (row is { LastProvenance: "Human" })
                    throw HumanConflict();
                if (row is null && string.Equals(global.LastProvenance, "Human", StringComparison.Ordinal))
                    throw HumanConflict();
            }

            var canonical = DispatchConcurrencyPolicy.CanonicalOverrides(incoming);
            var humanClaim = row is not null
                && !string.Equals(row.LastProvenance, "Human", StringComparison.Ordinal)
                && string.Equals(request.Provenance, "Human", StringComparison.Ordinal);
            if (row is not null && !humanClaim
                && string.Equals(row.LastProvenance, request.Provenance, StringComparison.Ordinal)
                && DispatchConcurrencyPolicy.SameOverrides(row.OverridesJson, request.Overrides))
            {
                await tx.RollbackAsync(ct);
            }
            else
            {
                var now = _clock.GetUtcNow().UtcDateTime;
                var previous = row?.Revision;
                if (row is null)
                {
                    row = new DispatchConcurrencySettings
                    {
                        Id = Guid.NewGuid(),
                        ScopeKey = scope,
                        ProjectId = projectId,
                        SchemaVersion = DispatchConcurrencySettings.CurrentSchemaVersion,
                    };
                    _db.DispatchConcurrencySettings.Add(row);
                }

                row.Revision = currentRevision + 1;
                row.OverridesJson = canonical;
                row.UpdatedAt = now;
                row.LastReason = request.Reason.Trim();
                row.LastProvenance = request.Provenance;
                row.LastCallerTaskId = callerTaskId;
                if (projectId is null)
                    row.SeedJson = global.SeedJson;
                _db.DispatchConcurrencyRevisions.Add(History(row, previous, now));
                await _db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
                committed = true;
                if (projectId is null)
                    globalRevision = row.Revision;
                else
                    projectRevision = row.Revision;
            }
        }
        catch (DbUpdateException ex) when (IsConflict(ex))
        {
            await SafeRollbackAsync(tx, ct);
            DetachMine();
            var current = await CurrentRevisionsAsync(projectId, ct);
            throw new ConflictException(
                "Dispatch concurrency settings were updated by someone else.",
                CodeRevisionConflict,
                new Dictionary<string, object?>
                {
                    ["globalRevision"] = current.Global,
                    ["projectRevision"] = current.Project,
                });
        }
        catch
        {
            if (!committed)
                await SafeRollbackAsync(tx, ct);
            DetachMine();
            throw;
        }

        if (committed)
        {
            var revision = projectId is null ? globalRevision : projectRevision;
            await PublishChangedAsync(scope, revision, globalRevision, projectRevision, ct);
            OrchestratorInstructionsSignal.Fire(_services, $"dispatch-concurrency rev {revision}");
        }

        return projectId is Guid project
            ? await ProjectScopedAsync(project, ct)
            : await ProjectGlobalAsync(ct);
    }

    private async Task<EffectivePolicy> ComposeAsync(Guid? projectId, CancellationToken ct)
    {
        var global = await GlobalAsync(tracking: false, ct)
            ?? throw new InvalidOperationException("Dispatch concurrency settings are not initialized.");
        var seed = ReadSeed(global.SeedJson, global.UpdatedAt);
        var globalOverrides = ReadOverrides(global.OverridesJson);
        DispatchConcurrencyDocument? projectOverrides = null;
        long projectRevision = 0;
        if (projectId is Guid id)
        {
            var project = await _db.DispatchConcurrencySettings.AsNoTracking()
                .SingleOrDefaultAsync(row => row.ScopeKey == id.ToString("D"), ct);
            if (project is not null)
            {
                projectOverrides = ReadOverrides(project.OverridesJson);
                projectRevision = project.Revision;
            }
        }

        return DispatchConcurrencyPolicy.Resolve(seed, globalOverrides, projectOverrides, global.Revision, projectRevision);
    }

    private async Task<DispatchConcurrencyGlobalDto> ProjectGlobalAsync(CancellationToken ct)
    {
        var row = await GlobalAsync(tracking: false, ct)
            ?? throw new InvalidOperationException("Dispatch concurrency settings are not initialized.");
        var seed = ReadSeed(row.SeedJson, row.UpdatedAt);
        var policy = await ComposeAsync(null, ct);
        var occupancy = await OccupancyAsync(null, policy, ct);
        return new DispatchConcurrencyGlobalDto(
            ToSeed(seed),
            DispatchConcurrencyPolicy.ParseElement(row.OverridesJson),
            ToEffective(policy),
            ToEffective(policy),
            Names(),
            Ranges(),
            row.Revision,
            row.UpdatedAt,
            row.LastReason,
            row.LastProvenance,
            row.LastCallerTaskId,
            occupancy);
    }

    private async Task<DispatchConcurrencyProjectDto> ProjectScopedAsync(Guid projectId, CancellationToken ct)
    {
        var global = await GlobalAsync(tracking: false, ct)
            ?? throw new InvalidOperationException("Dispatch concurrency settings are not initialized.");
        var row = await _db.DispatchConcurrencySettings.AsNoTracking()
            .SingleOrDefaultAsync(item => item.ScopeKey == projectId.ToString("D"), ct);
        var inherited = DispatchConcurrencyPolicy.Resolve(
            ReadSeed(global.SeedJson, global.UpdatedAt),
            ReadOverrides(global.OverridesJson),
            null,
            global.Revision,
            row?.Revision ?? 0);
        var effective = await ComposeAsync(projectId, ct);
        var occupancy = await OccupancyAsync(projectId, effective, ct);
        return new DispatchConcurrencyProjectDto(
            projectId,
            DispatchConcurrencyPolicy.ParseElement(row?.OverridesJson ?? DispatchConcurrencyPolicy.CanonicalOverrides(DispatchConcurrencyPolicy.EmptyDocument())),
            ToEffective(inherited),
            ToEffective(effective),
            Names(),
            Ranges(),
            row?.Revision ?? 0,
            global.Revision,
            row?.UpdatedAt,
            row?.LastReason,
            row?.LastProvenance,
            row?.LastCallerTaskId,
            occupancy);
    }

    private async Task<DispatchConcurrencyOccupancyDto> OccupancyAsync(
        Guid? projectId, EffectivePolicy policy, CancellationToken ct)
    {
        var rows = await _db.AgentTasks.AsNoTracking()
            .Where(task => task.ProjectId == projectId)
            .Select(task => new TaskPopulationRow(
                task.Id, task.ProjectId, task.Role, task.Status, task.CreatedAt,
                task.CapacityWaitRetained, task.Title, null))
            .ToListAsync(ct);
        return ToOccupancy(policy, DispatchConcurrencyPolicy.Count(rows, projectId));
    }

    internal static DispatchConcurrencyOccupancyDto ToOccupancy(
        EffectivePolicy policy, PopulationSnapshot population)
    {
        var parallelPopulation = policy.Mode == DispatchConcurrencyMode.LegacyOpen
            ? population.Open
            : population.Parallel;
        var roles = policy.Roles.Select(role =>
        {
            var counts = population.Roles[role.Role];
            var roleParallelCount = policy.Mode == DispatchConcurrencyMode.LegacyOpen
                ? counts.Open
                : counts.Parallel;
            return new DispatchConcurrencyRoleOccupancyDto(
                role.Role.ToString(),
                counts.Open,
                counts.Parallel,
                counts.Queued,
                Remaining(roleParallelCount, role.MaxParallel),
                Remaining(counts.Queued, role.MaxQueued),
                Overage(roleParallelCount, role.MaxParallel),
                Overage(counts.Queued, role.MaxQueued));
        }).ToList();
        var parallelOverage = Overage(parallelPopulation, policy.MaxParallel);
        var queuedOverage = Overage(population.Queued, policy.MaxQueued);
        return new DispatchConcurrencyOccupancyDto(
            population.Open,
            population.Parallel,
            population.Queued,
            Remaining(parallelPopulation, policy.MaxParallel),
            Remaining(population.Queued, policy.MaxQueued),
            parallelOverage,
            queuedOverage,
            parallelOverage > 0 || queuedOverage > 0 || roles.Any(role => role.ParallelOverage > 0 || role.QueuedOverage > 0),
            roles);
    }

    private async Task RequireProjectAsync(Guid projectId, CancellationToken ct)
    {
        if (!await _db.Projects.AsNoTracking().AnyAsync(project => project.Id == projectId, ct))
            throw new NotFoundException("Project", projectId);
    }

    private async Task<DispatchConcurrencySettings?> GlobalAsync(bool tracking, CancellationToken ct)
    {
        var query = tracking
            ? _db.DispatchConcurrencySettings
            : _db.DispatchConcurrencySettings.AsNoTracking();
        return await query.SingleOrDefaultAsync(
            row => row.ScopeKey == DispatchConcurrencySettings.GlobalScopeKey, ct);
    }

    private async Task<(long Global, long Project)> CurrentRevisionsAsync(Guid? projectId, CancellationToken ct)
    {
        var global = await _db.DispatchConcurrencySettings.AsNoTracking()
            .Where(row => row.ScopeKey == DispatchConcurrencySettings.GlobalScopeKey)
            .Select(row => row.Revision)
            .SingleAsync(ct);
        if (projectId is not Guid id)
            return (global, 0);
        var project = await _db.DispatchConcurrencySettings.AsNoTracking()
            .Where(row => row.ScopeKey == id.ToString("D"))
            .Select(row => (long?)row.Revision)
            .SingleOrDefaultAsync(ct);
        return (global, project ?? 0);
    }

    private static void ValidatePut(PutDispatchConcurrencyRequest request, bool project)
    {
        var errors = new Dictionary<string, string[]>();
        if (request.Reason is null || request.Reason.Trim().Length is < 1 or > 400)
            errors["reason"] = ["A reason of 1 to 400 characters is required."];
        if (request.Provenance is not ("Human" or "Auto"))
            errors["provenance"] = ["Provenance must be Human or Auto."];
        if (request.Overrides.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
            errors["overrides"] = ["overrides is required."];
        if (project && request.ExpectedGlobalRevision is null)
            errors["expectedGlobalRevision"] = ["expectedGlobalRevision is required."];
        if (errors.Count > 0)
            throw new ValidationException(errors);
    }

    private static ConflictException RevisionConflict(long globalRevision, long projectRevision) =>
        new(
            $"Dispatch concurrency revision does not match. Global revision is {globalRevision}; project revision is {projectRevision}.",
            CodeRevisionConflict,
            new Dictionary<string, object?>
            {
                ["globalRevision"] = globalRevision,
                ["projectRevision"] = projectRevision,
            });

    private static ConflictException HumanConflict() =>
        new(
            "An automatic edit cannot replace a human dispatch-concurrency choice or shadow one with a new project override.",
            CodeHuman);

    private static DispatchConcurrencyRevision History(DispatchConcurrencySettings row, long? previous, DateTime now) =>
        new()
        {
            Id = Guid.NewGuid(),
            SettingsId = row.Id,
            Revision = row.Revision,
            PreviousRevision = previous,
            SnapshotJson = WriteSnapshot(row),
            CreatedAt = now,
            Reason = row.LastReason,
            Provenance = row.LastProvenance,
            CallerTaskId = row.LastCallerTaskId,
        };

    private static string WriteSnapshot(DispatchConcurrencySettings row)
    {
        using var stream = new MemoryStream();
        using var writer = new Utf8JsonWriter(stream);
        writer.WriteStartObject();
        writer.WriteString("scopeKey", row.ScopeKey);
        if (row.ProjectId is Guid project)
            writer.WriteString("projectId", project.ToString("D"));
        else
            writer.WriteNull("projectId");
        writer.WriteNumber("revision", row.Revision);
        writer.WriteString("provenance", row.LastProvenance);
        writer.WriteString("reason", row.LastReason);
        if (row.LastCallerTaskId is Guid caller)
            writer.WriteString("callerTaskId", caller.ToString("D"));
        else
            writer.WriteNull("callerTaskId");
        writer.WritePropertyName("overrides");
        using (var overrides = JsonDocument.Parse(row.OverridesJson))
            overrides.RootElement.WriteTo(writer);
        if (row.SeedJson is not null)
        {
            writer.WritePropertyName("seed");
            using var seed = JsonDocument.Parse(row.SeedJson);
            seed.RootElement.WriteTo(writer);
        }

        writer.WriteEndObject();
        writer.Flush();
        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }

    private static string WriteSeed(DispatchConcurrencyDocument seed)
    {
        using var body = JsonDocument.Parse(DispatchConcurrencyPolicy.WriteDocument(seed, includeAbsentRoles: true));
        using var stream = new MemoryStream();
        using var writer = new Utf8JsonWriter(stream);
        writer.WriteStartObject();
        writer.WriteString("origin", DispatchConcurrencyPolicy.SeedOrigin);
        writer.WriteString("importedAt", (seed.ImportedAt ?? DateTime.UnixEpoch).ToString("O"));
        foreach (var property in body.RootElement.EnumerateObject())
        {
            writer.WritePropertyName(property.Name);
            property.Value.WriteTo(writer);
        }

        writer.WriteEndObject();
        writer.Flush();
        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }

    private static DispatchConcurrencyDocument ReadSeed(string? json, DateTime fallback)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new InvalidOperationException("Dispatch concurrency seed is missing.");
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (!DispatchConcurrencyPolicy.TryParseOverrides(root, out var parsed))
            throw new InvalidOperationException("Dispatch concurrency seed is unreadable.");
        var imported = root.TryGetProperty("importedAt", out var stamp)
            && DateTime.TryParse(stamp.GetString(), null, System.Globalization.DateTimeStyles.RoundtripKind, out var at)
            ? at
            : fallback;
        return parsed with { ImportedAt = imported };
    }

    private static DispatchConcurrencyDocument ReadOverrides(string json)
    {
        using var document = JsonDocument.Parse(json);
        return DispatchConcurrencyPolicy.TryParseOverrides(document.RootElement, out var parsed)
            ? parsed
            : DispatchConcurrencyPolicy.EmptyDocument();
    }

    private static DispatchConcurrencySeedDto ToSeed(DispatchConcurrencyDocument seed) =>
        new(
            DispatchConcurrencyPolicy.SeedOrigin,
            seed.ImportedAt ?? DateTime.UnixEpoch,
            (seed.Mode ?? DispatchConcurrencyMode.LegacyOpen).ToString(),
            seed.MaxParallel.Value ?? 0,
            seed.MaxQueued.Value,
            seed.Roles
                .OrderBy(pair => pair.Key.ToString(), StringComparer.Ordinal)
                .Select(pair => new DispatchConcurrencySeedRoleDto(
                    pair.Key.ToString(), pair.Value.MaxParallel.Value, pair.Value.MaxQueued.Value))
                .ToList());

    internal static DispatchConcurrencyEffectiveDto ToEffective(EffectivePolicy policy) =>
        new(
            policy.Mode.ToString(),
            policy.ModeSource,
            policy.MaxParallel,
            policy.ParallelSource,
            policy.MaxQueued,
            policy.QueuedSource,
            policy.Roles.Select(role => new DispatchConcurrencyRoleDto(
                role.Role.ToString(),
                role.MaxParallel,
                role.ParallelSource,
                role.MaxQueued,
                role.QueuedSource,
                policy.CombinedParallel(role.Role))).ToList(),
            policy.GlobalRevision,
            policy.ProjectRevision);

    private static DispatchConcurrencyRevisionDto ToRevision(DispatchConcurrencyRevision row) =>
        new(
            row.Revision,
            row.PreviousRevision,
            DispatchConcurrencyPolicy.ParseElement(row.SnapshotJson),
            row.CreatedAt,
            row.Reason,
            row.Provenance,
            row.CallerTaskId);

    private static IReadOnlyList<string> Names() =>
        DispatchConcurrencyPolicy.OrdinaryRoles.Select(role => role.ToString()).ToList();

    private static DispatchConcurrencyRangeDto Ranges() =>
        new(DispatchConcurrencyPolicy.ParallelMin, DispatchConcurrencyPolicy.ParallelMax,
            DispatchConcurrencyPolicy.QueuedMin, DispatchConcurrencyPolicy.QueuedMax);

    private static int? Remaining(int count, int? limit) =>
        limit is int value ? Math.Max(0, value - count) : null;

    private static int Overage(int count, int? limit) =>
        limit is int value ? Math.Max(0, count - value) : 0;

    private async Task PublishChangedAsync(
        string scope, long revision, long globalRevision, long projectRevision, CancellationToken ct)
    {
        if (_events is null)
            return;
        try
        {
            await _events.PublishToAllAsync("DispatchConcurrencyChanged", new
            {
                scope,
                revision,
                globalRevision,
                projectRevision,
            }, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The revision is durable. GET is authoritative when the event is lost.
        }
    }

    private async Task TakeLockAsync(string key, CancellationToken ct) =>
        await _db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtext({key}))", ct);

    private static async Task SafeRollbackAsync(Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction tx, CancellationToken ct)
    {
        try
        {
            await tx.RollbackAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A commit failure can leave the transaction unusable. Keep the original exception.
        }
    }

    private void DetachMine()
    {
        foreach (var entry in _db.ChangeTracker.Entries<DispatchConcurrencySettings>().ToList())
            entry.State = EntityState.Detached;
        foreach (var entry in _db.ChangeTracker.Entries<DispatchConcurrencyRevision>().ToList())
            entry.State = EntityState.Detached;
    }

    private static bool IsConflict(DbUpdateException ex)
    {
        if (ex is DbUpdateConcurrencyException)
            return true;
        for (var current = ex.InnerException; current is not null; current = current.InnerException)
        {
            if (current is PostgresException pg
                && pg.SqlState is PostgresErrorCodes.UniqueViolation or PostgresErrorCodes.SerializationFailure)
                return true;
        }

        return false;
    }
}

/// <summary>Policies read together in one repeatable-read transaction.</summary>
public sealed class CoherentPolicies
{
    private readonly IReadOnlyDictionary<string, EffectivePolicy> _byScope;

    public CoherentPolicies(IReadOnlyDictionary<string, EffectivePolicy> byScope) =>
        _byScope = byScope;

    public EffectivePolicy Global => _byScope[DispatchConcurrencySettings.GlobalScopeKey];

    public EffectivePolicy For(Guid? projectId) =>
        projectId is Guid id && _byScope.TryGetValue(id.ToString("D"), out var policy)
            ? policy
            : Global;
}

public sealed class DispatchConcurrencyStartup : IHostedService
{
    private readonly IServiceScopeFactory _scopes;

    public DispatchConcurrencyStartup(IServiceScopeFactory scopes) => _scopes = scopes;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopes.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<DispatchConcurrencySettingsService>();
        await service.EnsureInitializedAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
