using System.Data;
using System.Security.Cryptography;
using System.Text.Json;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// One-way in-process conversion from profile-bound TUI secrets to global API keys. Ciphertexts
/// cannot be copied because the two protectors deliberately have different purpose chains.
/// </summary>
public sealed class AgentTuiSecretMigrator
{
    private readonly AppDbContext _db;
    private readonly IAgentTuiSecretProtector _secretProtector;
    private readonly IApiKeyProtector _apiKeyProtector;
    private readonly ILogger<AgentTuiSecretMigrator> _logger;

    public AgentTuiSecretMigrator(
        AppDbContext db,
        IAgentTuiSecretProtector secretProtector,
        IApiKeyProtector apiKeyProtector,
        ILogger<AgentTuiSecretMigrator> logger)
    {
        _db = db;
        _secretProtector = secretProtector;
        _apiKeyProtector = apiKeyProtector;
        _logger = logger;
    }

    public async Task<AgentTuiSecretMigrationResult> MigrateAsync(CancellationToken cancellationToken)
    {
        var profileIds = await _db.AgentTuiProfiles.AsNoTracking()
            .Where(profile => profile.ActiveRevision != null
                              && profile.ActiveRevision.AuthenticationMode == AgentTuiAuthenticationMode.ManagedEnvironment)
            .Select(profile => profile.Id)
            .ToArrayAsync(cancellationToken);

        var converted = 0;
        var refused = 0;
        foreach (var profileId in profileIds)
        {
            var outcome = await MigrateProfileAsync(profileId, cancellationToken);
            if (outcome.Converted)
                converted++;
            else if (outcome.Refusal is not null)
            {
                refused++;
                _logger.LogWarning("Agent TUI secret migration refused profile {ProfileId}: {Reason}", profileId, outcome.Refusal);
            }
        }

        _logger.LogInformation("Agent TUI secret migration finished: {Converted} profile(s) converted, {Refused} refused.", converted, refused);
        return new AgentTuiSecretMigrationResult(converted, refused);
    }

    private async Task<ProfileMigrationOutcome> MigrateProfileAsync(Guid profileId, CancellationToken cancellationToken)
    {
        await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            var profile = await _db.AgentTuiProfiles
                .Include(candidate => candidate.ActiveRevision)
                .Include(candidate => candidate.Secrets)
                .SingleAsync(candidate => candidate.Id == profileId, cancellationToken);
            var revision = profile.ActiveRevision!;
            if (revision.AuthenticationMode != AgentTuiAuthenticationMode.ManagedEnvironment)
            {
                await transaction.CommitAsync(cancellationToken);
                return new ProfileMigrationOutcome(false, null);
            }

            var environment = DeserializeEnvironment(revision.NonSecretEnvironmentJson, profileId);
            var declaredNames = DeserializeNames(revision.SecretEnvironmentNamesJson, profileId);
            var secretByName = profile.Secrets.ToDictionary(secret => secret.Name, StringComparer.Ordinal);

            foreach (var (environmentName, value) in environment)
            {
                if (ApiKeyPlaceholder.ContainsMarker(value))
                    return await RefuseAsync(transaction, $"environment '{environmentName}' already contains an API key placeholder", cancellationToken);
            }

            var migrationValues = new List<(string Name, string Value)>();
            foreach (var name in declaredNames)
            {
                if (name.Length > ApiKeyNaming.MaxNameLength)
                    return await RefuseAsync(transaction, $"environment '{name}' exceeds the {ApiKeyNaming.MaxNameLength}-character API key name limit", cancellationToken);
                if (!secretByName.TryGetValue(name, out var secret))
                    return await RefuseAsync(transaction, $"environment '{name}' has no stored Agent TUI secret", cancellationToken);
                if (await _db.ApiKeys.AnyAsync(key => key.ProjectId == null && key.Name == name, cancellationToken))
                    return await RefuseAsync(transaction, $"environment '{name}' collides with an existing global API key", cancellationToken);
                try
                {
                    var value = _secretProtector.Unprotect(profile.Id, secret.Name, secret.Ciphertext);
                    if (ApiKeyPlaceholder.ContainsMarker(value))
                        return await RefuseAsync(transaction, $"environment '{name}' contains a value with an API key placeholder marker", cancellationToken);
                    migrationValues.Add((name, value));
                }
                catch (CryptographicException)
                {
                    return await RefuseAsync(transaction, $"environment '{name}' could not be decrypted", cancellationToken);
                }
            }

            var now = DateTime.UtcNow;
            foreach (var (name, value) in migrationValues)
            {
                var key = new ApiKey
                {
                    Id = Guid.NewGuid(),
                    Name = name,
                    ProjectId = null,
                    ProtectionVersion = "v1",
                    CreatedAt = now,
                    UpdatedAt = now,
                };
                try
                {
                    key.Ciphertext = _apiKeyProtector.Protect(key.Id, value);
                }
                catch (CryptographicException)
                {
                    return await RefuseAsync(transaction, $"environment '{name}' could not be protected as an API key", cancellationToken);
                }
                _db.ApiKeys.Add(key);
                environment[name] = $"{{{{key:{name}}}}}";
            }

            var migratedRevision = new AgentTuiProfileRevision
            {
                Id = Guid.NewGuid(),
                ProfileId = profile.Id,
                RevisionNumber = revision.RevisionNumber + 1,
                Executable = revision.Executable,
                ArgumentsJson = revision.ArgumentsJson,
                DiscoveryArgumentsJson = revision.DiscoveryArgumentsJson,
                VersionArgumentsJson = revision.VersionArgumentsJson,
                WorkingDirectory = revision.WorkingDirectory,
                AuthenticationMode = AgentTuiAuthenticationMode.WrapperManaged,
                NonSecretEnvironmentJson = JsonSerializer.Serialize(environment),
                SecretEnvironmentNamesJson = "[]",
                ModelArgumentName = revision.ModelArgumentName,
                Guidance = revision.Guidance,
                CreatedAt = now,
            };
            _db.AgentTuiProfileRevisions.Add(migratedRevision);
            profile.ActiveRevisionId = migratedRevision.Id;
            profile.UpdatedAt = now;
            _db.AgentTuiSecrets.RemoveRange(profile.Secrets);
            await _db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new ProfileMigrationOutcome(true, null);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
        finally
        {
            _db.ChangeTracker.Clear();
        }
    }

    private static Dictionary<string, string> DeserializeEnvironment(string json, Guid profileId)
    {
        try { return JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? []; }
        catch (JsonException) { throw new InvalidOperationException($"Agent TUI secret migration refused profile {profileId:D}: non-secret environment is invalid."); }
    }

    private static string[] DeserializeNames(string json, Guid profileId)
    {
        try { return JsonSerializer.Deserialize<string[]>(json) ?? []; }
        catch (JsonException) { throw new InvalidOperationException($"Agent TUI secret migration refused profile {profileId:D}: declared secret names are invalid."); }
    }

    private static async Task<ProfileMigrationOutcome> RefuseAsync(
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction,
        string reason,
        CancellationToken cancellationToken)
    {
        await transaction.RollbackAsync(cancellationToken);
        return new ProfileMigrationOutcome(false, reason);
    }

    private sealed record ProfileMigrationOutcome(bool Converted, string? Refusal);
}

public sealed record AgentTuiSecretMigrationResult(int ProfilesConverted, int ProfilesRefused);
