using System.Data;
using System.Text.Json;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Antiphon.Server.Application.Services;

public sealed class AgentTuiProfileImporter
{
    private const string SecretProtectionVersion = "v1";
    private const int MaximumImportAttempts = 3;
    private readonly AppDbContext _db;
    private readonly IOptions<AgentRegistrySettings> _settings;
    private readonly IAgentTuiSecretProtector _secretProtector;
    private readonly AgentTuiRunnerCatalog _runnerCatalog;
    private readonly TimeProvider _timeProvider;

    public AgentTuiProfileImporter(
        AppDbContext db,
        IOptions<AgentRegistrySettings> settings,
        IAgentTuiSecretProtector secretProtector,
        AgentTuiRunnerCatalog runnerCatalog,
        TimeProvider timeProvider)
    {
        _db = db;
        _settings = settings;
        _secretProtector = secretProtector;
        _runnerCatalog = runnerCatalog;
        _timeProvider = timeProvider;
    }

    public async Task<AgentTuiImportResultDto> ImportAsync(CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= MaximumImportAttempts; attempt++)
        {
            try
            {
                return await ImportAttemptAsync(cancellationToken);
            }
            catch (Exception exception) when (IsImportConcurrencyFailure(exception))
            {
                _db.ChangeTracker.Clear();
                if (attempt == MaximumImportAttempts)
                {
                    throw new ConflictException(
                        "Agent TUI profile import conflicted with another database update.",
                        exception);
                }
            }
            catch (DbUpdateException exception)
            {
                throw new ConflictException(
                    "Agent TUI profile import conflicted with another database update.",
                    exception);
            }
        }

        throw new InvalidOperationException("Agent TUI profile import retry loop exited unexpectedly.");
    }

    private async Task<AgentTuiImportResultDto> ImportAttemptAsync(CancellationToken cancellationToken)
    {
        var profilesExist = await _db.AgentTuiProfiles.AnyAsync(cancellationToken);
        var plans = profilesExist ? [] : BuildImportPlans();
        await using var transaction = await BeginTransactionAsync(cancellationToken);

        try
        {
            var profilesCreated = 0;
            if (!await _db.AgentTuiProfiles.AnyAsync(cancellationToken))
                profilesCreated = await PersistPlansAsync(plans, cancellationToken);

            var installationDefault = await _db.AgentTuiProfiles
                .SingleOrDefaultAsync(profile => profile.IsDefault, cancellationToken);
            var agentsAssigned = installationDefault is null
                ? 0
                : await BackfillAgentsAsync(installationDefault, cancellationToken);

            await CommitAsync(transaction, cancellationToken);
            return new AgentTuiImportResultDto(profilesCreated, agentsAssigned);
        }
        catch
        {
            await RollbackAsync(transaction, cancellationToken);
            throw;
        }
    }

    private ImportPlan[] BuildImportPlans()
    {
        var settings = _settings.Value;
        var orderedDefinitions = settings.Definitions
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .ToArray();
        if (orderedDefinitions.Length == 0)
            return [];

        var defaultName = orderedDefinitions.Any(pair =>
            string.Equals(pair.Key, settings.DefaultDefinition, StringComparison.OrdinalIgnoreCase))
            ? orderedDefinitions.Single(pair =>
                string.Equals(pair.Key, settings.DefaultDefinition, StringComparison.OrdinalIgnoreCase)).Key
            : orderedDefinitions[0].Key;

        return orderedDefinitions.Select((pair, index) =>
        {
            if (!Enum.TryParse<AgentKind>(pair.Value.Kind, ignoreCase: true, out var kind)
                || !Enum.IsDefined(kind))
            {
                throw new ValidationException(
                    nameof(pair.Value.Kind),
                    $"Agent definition '{pair.Key}' has an unsupported runner kind.");
            }
            if (string.IsNullOrWhiteSpace(pair.Value.Exe) || pair.Value.Exe.Length > 2000)
            {
                throw new ValidationException(
                    nameof(pair.Value.Exe),
                    $"Agent definition '{pair.Key}' requires a bounded executable.");
            }
            if (pair.Value.ArgsTemplate is null
                || pair.Value.ArgsTemplate.Any(argument => argument is null || argument.Length > 2000))
            {
                throw new ValidationException(
                    nameof(pair.Value.ArgsTemplate),
                    $"Agent definition '{pair.Key}' requires bounded arguments.");
            }

            var profileId = Guid.NewGuid();
            var classification = AgentEnvironmentVariableNames.Classify(
                pair.Value,
                $"Agents:Definitions:{pair.Key}");
            if (classification.Failures.Count > 0)
            {
                throw new ValidationException(
                    nameof(pair.Value.Env),
                    string.Join(" ", classification.Failures));
            }

            var secretEnvironment = classification.Secrets
                .OrderBy(entry => entry.Key, StringComparer.Ordinal)
                .Select(entry => new ProtectedImportSecret(
                    entry.Key,
                    _secretProtector.Protect(profileId, entry.Key, entry.Value)))
                .ToArray();
            var nonSecretEnvironment = classification.Ordinary
                .OrderBy(entry => entry.Key, StringComparer.Ordinal)
                .ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal);

            return new ImportPlan(
                profileId,
                pair.Key,
                kind,
                string.Equals(pair.Key, defaultName, StringComparison.Ordinal),
                pair.Value.Exe,
                pair.Value.ArgsTemplate.ToArray(),
                nonSecretEnvironment,
                secretEnvironment,
                index);
        }).ToArray();
    }

    private async Task<int> PersistPlansAsync(
        IReadOnlyList<ImportPlan> plans,
        CancellationToken cancellationToken)
    {
        if (plans.Count == 0)
            return 0;

        var baseTime = UtcNow();
        foreach (var plan in plans)
        {
            var now = baseTime.AddTicks(plan.Order);
            _db.AgentTuiProfiles.Add(new AgentTuiProfile
            {
                Id = plan.ProfileId,
                DisplayName = plan.DefinitionName,
                Kind = plan.Kind,
                IsEnabled = true,
                IsDefault = plan.IsDefault,
                Source = AgentTuiProfileSource.ImportedFile,
                SourceDefinitionName = plan.DefinitionName,
                CreatedAt = now,
                UpdatedAt = now
            });
        }
        await _db.SaveChangesAsync(cancellationToken);

        foreach (var plan in plans)
        {
            var now = baseTime.AddTicks(plan.Order);
            var runner = _runnerCatalog.Get(plan.Kind, plan.Arguments);
            var revision = new AgentTuiProfileRevision
            {
                Id = Guid.NewGuid(),
                ProfileId = plan.ProfileId,
                RevisionNumber = 1,
                Executable = plan.Executable,
                ArgumentsJson = JsonSerializer.Serialize(plan.Arguments),
                DiscoveryArgumentsJson = "[]",
                VersionArgumentsJson = "[]",
                AuthenticationMode = plan.Secrets.Count == 0
                    ? AgentTuiAuthenticationMode.WrapperManaged
                    : AgentTuiAuthenticationMode.ManagedEnvironment,
                NonSecretEnvironmentJson = JsonSerializer.Serialize(plan.NonSecretEnvironment),
                SecretEnvironmentNamesJson = JsonSerializer.Serialize(
                    plan.Secrets.Select(secret => secret.Name).ToArray()),
                ModelArgumentName = runner.DefaultModelArgumentName,
                Guidance = runner.Guidance,
                CreatedAt = now
            };
            _db.AgentTuiProfileRevisions.Add(revision);

            foreach (var secret in plan.Secrets)
            {
                _db.AgentTuiSecrets.Add(new AgentTuiSecret
                {
                    Id = Guid.NewGuid(),
                    ProfileId = plan.ProfileId,
                    Name = secret.Name,
                    Ciphertext = secret.Ciphertext,
                    ProtectionVersion = SecretProtectionVersion,
                    CreatedAt = now,
                    UpdatedAt = now
                });
            }

            var profile = _db.AgentTuiProfiles.Local.Single(candidate => candidate.Id == plan.ProfileId);
            profile.ActiveRevisionId = revision.Id;
            profile.ActiveRevision = revision;
        }
        await _db.SaveChangesAsync(cancellationToken);
        return plans.Count;
    }

    private async Task<int> BackfillAgentsAsync(
        AgentTuiProfile installationDefault,
        CancellationToken cancellationToken)
    {
        var agents = await _db.Agents
            .Where(agent => agent.TuiProfileId == null)
            .ToListAsync(cancellationToken);
        foreach (var agent in agents)
        {
            agent.TuiProfileId = installationDefault.Id;
            agent.ModelId ??= _runnerCatalog.MapLegacyModel(
                installationDefault.Kind,
                agent.ModelLevel);
            agent.UpdatedAt = UtcNow();
        }

        if (agents.Count > 0)
            await _db.SaveChangesAsync(cancellationToken);
        return agents.Count;
    }

    private static bool IsImportConcurrencyFailure(Exception exception)
    {
        var current = exception;
        while (current is not null)
        {
            if (current is PostgresException postgres)
            {
                return postgres.SqlState is PostgresErrorCodes.UniqueViolation
                    or PostgresErrorCodes.SerializationFailure
                    or PostgresErrorCodes.DeadlockDetected;
            }

            current = current.InnerException;
        }

        return false;
    }

    private async Task<IDbContextTransaction?> BeginTransactionAsync(CancellationToken cancellationToken)
    {
        if (_db.Database.CurrentTransaction is not null)
            return null;
        return await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
    }

    private static async Task CommitAsync(
        IDbContextTransaction? transaction,
        CancellationToken cancellationToken)
    {
        if (transaction is not null)
            await transaction.CommitAsync(cancellationToken);
    }

    private static async Task RollbackAsync(
        IDbContextTransaction? transaction,
        CancellationToken cancellationToken)
    {
        if (transaction is null)
            return;
        try
        {
            await transaction.RollbackAsync(cancellationToken);
        }
        catch (InvalidOperationException)
        {
            // PostgreSQL may already have completed a transaction rejected for concurrency.
        }
    }

    private DateTime UtcNow() => _timeProvider.GetUtcNow().UtcDateTime;

    private sealed record ImportPlan(
        Guid ProfileId,
        string DefinitionName,
        AgentKind Kind,
        bool IsDefault,
        string Executable,
        IReadOnlyList<string> Arguments,
        IReadOnlyDictionary<string, string> NonSecretEnvironment,
        IReadOnlyList<ProtectedImportSecret> Secrets,
        int Order);

    private sealed record ProtectedImportSecret(string Name, string Ciphertext);
}
