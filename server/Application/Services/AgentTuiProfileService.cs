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
using Npgsql;

namespace Antiphon.Server.Application.Services;

public sealed class AgentTuiProfileService
{
    private const string SecretProtectionVersion = "v1";
    private const int MaximumArgumentLength = 2000;
    private const int MaximumEnvironmentValueLength = 4000;
    private const int MaximumGuidanceLength = 4000;

    private readonly AppDbContext _db;
    private readonly IAgentTuiSecretProtector _secretProtector;
    private readonly AuditService _auditService;
    private readonly AgentTuiRunnerCatalog _runnerCatalog;
    private readonly TimeProvider _timeProvider;
    private readonly ICurrentUser _currentUser;
    private readonly IEqualityComparer<string> _environmentNameComparer;

    public AgentTuiProfileService(
        AppDbContext db,
        IAgentTuiSecretProtector secretProtector,
        AuditService auditService,
        AgentTuiRunnerCatalog runnerCatalog,
        TimeProvider timeProvider,
        ICurrentUser currentUser)
        : this(
            db,
            secretProtector,
            auditService,
            runnerCatalog,
            timeProvider,
            currentUser,
            AgentEnvironmentVariableNames.ForCurrentPlatform())
    {
    }

    internal AgentTuiProfileService(
        AppDbContext db,
        IAgentTuiSecretProtector secretProtector,
        AuditService auditService,
        AgentTuiRunnerCatalog runnerCatalog,
        TimeProvider timeProvider,
        ICurrentUser currentUser,
        IEqualityComparer<string> environmentNameComparer)
    {
        _db = db;
        _secretProtector = secretProtector;
        _auditService = auditService;
        _runnerCatalog = runnerCatalog;
        _timeProvider = timeProvider;
        _currentUser = currentUser;
        _environmentNameComparer = environmentNameComparer;
    }

    public async Task<IReadOnlyList<AgentTuiProfileDto>> ListAsync(CancellationToken cancellationToken)
    {
        var profiles = await ProfileReadQuery()
            .OrderBy(profile => profile.DisplayName)
            .ToListAsync(cancellationToken);
        return profiles.Select(MapProfile).ToArray();
    }

    public async Task<AgentTuiProfileDto> GetAsync(Guid profileId, CancellationToken cancellationToken)
    {
        var profile = await ProfileReadQuery()
            .SingleOrDefaultAsync(candidate => candidate.Id == profileId, cancellationToken)
            ?? throw new NotFoundException(nameof(AgentTuiProfile), profileId);
        return MapProfile(profile);
    }

    public async Task<IReadOnlyList<AgentTuiModelDto>> GetModelsAsync(
        Guid profileId,
        CancellationToken cancellationToken)
    {
        var profile = await _db.AgentTuiProfiles
            .AsNoTracking()
            .Include(candidate => candidate.Models)
            .SingleOrDefaultAsync(candidate => candidate.Id == profileId, cancellationToken)
            ?? throw new NotFoundException(nameof(AgentTuiProfile), profileId);
        return MergeModels(profile.Kind, profile.Models);
    }

    public async Task<AgentTuiProfileDto> CreateAsync(
        AgentTuiProfileWriteRequest request,
        CancellationToken cancellationToken)
    {
        ValidateProfileRequest(request, requireExpectedRevision: false);
        var now = UtcNow();
        await using var transaction = await BeginTransactionAsync(cancellationToken);

        try
        {
            if (await _db.AgentTuiProfiles.AnyAsync(
                    profile => profile.DisplayName == request.DisplayName,
                    cancellationToken))
            {
                throw new ConflictException($"An agent TUI profile named '{request.DisplayName}' already exists.");
            }

            var hasDefault = await _db.AgentTuiProfiles.AnyAsync(
                profile => profile.IsDefault,
                cancellationToken);
            var makeDefault = request.IsDefault || !hasDefault;
            if (makeDefault)
                await ClearOtherDefaultsAsync(exceptProfileId: null, cancellationToken);

            var profile = new AgentTuiProfile
            {
                Id = Guid.NewGuid(),
                DisplayName = request.DisplayName.Trim(),
                Kind = request.Kind,
                IsEnabled = request.IsEnabled || makeDefault,
                IsDefault = makeDefault,
                Source = AgentTuiProfileSource.Operator,
                CreatedAt = now,
                UpdatedAt = now
            };
            _db.AgentTuiProfiles.Add(profile);
            await _db.SaveChangesAsync(cancellationToken);

            var revision = NewRevision(profile.Id, 1, request, now);
            _db.AgentTuiProfileRevisions.Add(revision);
            AddOperatorModels(profile, request.Models, now);
            await _db.SaveChangesAsync(cancellationToken);

            profile.ActiveRevisionId = revision.Id;
            profile.ActiveRevision = revision;
            await _db.SaveChangesAsync(cancellationToken);
            await CommitAsync(transaction, cancellationToken);
            return await GetAsync(profile.Id, cancellationToken);
        }
        catch (DbUpdateException exception)
        {
            await RollbackAsync(transaction, cancellationToken);
            throw new ConflictException("The agent TUI profile could not be created because its state changed.", exception);
        }
        catch (Exception exception) when (IsTransactionConcurrencyFailure(exception))
        {
            await RollbackAsync(transaction, cancellationToken);
            throw new ConflictException("The agent TUI profile could not be created because its state changed.", exception);
        }
        catch
        {
            await RollbackAsync(transaction, cancellationToken);
            throw;
        }
    }

    public async Task<AgentTuiProfileDto> UpdateAsync(
        Guid profileId,
        AgentTuiProfileWriteRequest request,
        CancellationToken cancellationToken)
    {
        ValidateProfileRequest(request, requireExpectedRevision: true);
        var now = UtcNow();
        await using var transaction = await BeginTransactionAsync(cancellationToken);

        try
        {
            var profile = await _db.AgentTuiProfiles
                .Include(candidate => candidate.ActiveRevision)
                .Include(candidate => candidate.Models)
                .Include(candidate => candidate.Secrets)
                .SingleOrDefaultAsync(candidate => candidate.Id == profileId, cancellationToken)
                ?? throw new NotFoundException(nameof(AgentTuiProfile), profileId);
            var activeRevision = RequireActiveRevision(profile);
            EnsureExpectedRevision(activeRevision, request.ExpectedRevision!.Value);
            EnsureConfiguredSecretsRetained(profile.Secrets, request);

            if (await _db.AgentTuiProfiles.AnyAsync(
                    candidate => candidate.Id != profileId && candidate.DisplayName == request.DisplayName,
                    cancellationToken))
            {
                throw new ConflictException($"An agent TUI profile named '{request.DisplayName}' already exists.");
            }

            if (request.IsDefault)
                await ClearOtherDefaultsAsync(profileId, cancellationToken);

            var revision = NewRevision(
                profile.Id,
                activeRevision.RevisionNumber + 1,
                request,
                now);
            _db.AgentTuiProfileRevisions.Add(revision);

            var oldOperatorModels = profile.Models
                .Where(model => model.Source == AgentTuiModelSource.Operator)
                .ToArray();
            _db.AgentTuiModels.RemoveRange(oldOperatorModels);
            AddOperatorModels(profile, request.Models, now);

            profile.DisplayName = request.DisplayName.Trim();
            profile.Kind = request.Kind;
            profile.IsDefault = request.IsDefault || profile.IsDefault;
            profile.IsEnabled = request.IsEnabled || profile.IsDefault;
            profile.ActiveRevisionId = revision.Id;
            profile.ActiveRevision = revision;
            profile.UpdatedAt = now;

            await _db.SaveChangesAsync(cancellationToken);
            await CommitAsync(transaction, cancellationToken);
            return await GetAsync(profile.Id, cancellationToken);
        }
        catch (DbUpdateException exception)
        {
            await RollbackAsync(transaction, cancellationToken);
            throw new ConflictException("The profile revision conflicts with another update.", exception);
        }
        catch (Exception exception) when (IsTransactionConcurrencyFailure(exception))
        {
            await RollbackAsync(transaction, cancellationToken);
            throw new ConflictException("The profile revision conflicts with another update.", exception);
        }
        catch
        {
            await RollbackAsync(transaction, cancellationToken);
            throw;
        }
    }

    public async Task<AgentTuiProfileDto> DuplicateAsync(
        Guid profileId,
        DuplicateAgentTuiProfileRequest request,
        CancellationToken cancellationToken)
    {
        ValidateDisplayName(request.DisplayName);
        var now = UtcNow();
        await using var transaction = await BeginTransactionAsync(cancellationToken);

        try
        {
            var source = await _db.AgentTuiProfiles
                .Include(profile => profile.ActiveRevision)
                .Include(profile => profile.Models)
                .SingleOrDefaultAsync(profile => profile.Id == profileId, cancellationToken)
                ?? throw new NotFoundException(nameof(AgentTuiProfile), profileId);
            var sourceRevision = RequireActiveRevision(source);
            if (await _db.AgentTuiProfiles.AnyAsync(
                    profile => profile.DisplayName == request.DisplayName,
                    cancellationToken))
            {
                throw new ConflictException($"An agent TUI profile named '{request.DisplayName}' already exists.");
            }

            var duplicate = new AgentTuiProfile
            {
                Id = Guid.NewGuid(),
                DisplayName = request.DisplayName.Trim(),
                Kind = source.Kind,
                IsEnabled = false,
                IsDefault = false,
                Source = AgentTuiProfileSource.Operator,
                CreatedAt = now,
                UpdatedAt = now
            };
            _db.AgentTuiProfiles.Add(duplicate);
            await _db.SaveChangesAsync(cancellationToken);

            var revision = CopyRevision(sourceRevision, duplicate.Id, revisionNumber: 1, now);
            _db.AgentTuiProfileRevisions.Add(revision);
            foreach (var model in source.Models.Where(model => model.Source == AgentTuiModelSource.Operator))
            {
                _db.AgentTuiModels.Add(new AgentTuiModel
                {
                    Id = Guid.NewGuid(),
                    ProfileId = duplicate.Id,
                    Identifier = model.Identifier,
                    DisplayName = model.DisplayName,
                    Family = model.Family,
                    Source = AgentTuiModelSource.Operator,
                    Availability = model.Availability,
                    IsSuggestedDefault = model.IsSuggestedDefault,
                    CreatedAt = now,
                    UpdatedAt = now,
                    Profile = duplicate
                });
            }

            await _db.SaveChangesAsync(cancellationToken);
            duplicate.ActiveRevisionId = revision.Id;
            duplicate.ActiveRevision = revision;
            await _db.SaveChangesAsync(cancellationToken);
            await CommitAsync(transaction, cancellationToken);
            return await GetAsync(duplicate.Id, cancellationToken);
        }
        catch (DbUpdateException exception)
        {
            await RollbackAsync(transaction, cancellationToken);
            throw new ConflictException("The profile could not be duplicated because its state changed.", exception);
        }
        catch (Exception exception) when (IsTransactionConcurrencyFailure(exception))
        {
            await RollbackAsync(transaction, cancellationToken);
            throw new ConflictException("The profile could not be duplicated because its state changed.", exception);
        }
        catch
        {
            await RollbackAsync(transaction, cancellationToken);
            throw;
        }
    }

    public async Task DeleteAsync(Guid profileId, CancellationToken cancellationToken)
    {
        await using var transaction = await BeginTransactionAsync(cancellationToken);
        try
        {
            var profile = await _db.AgentTuiProfiles
                .Include(candidate => candidate.Revisions)
                .SingleOrDefaultAsync(candidate => candidate.Id == profileId, cancellationToken)
                ?? throw new NotFoundException(nameof(AgentTuiProfile), profileId);

            if (profile.IsDefault)
                throw new ConflictException("The installation default profile cannot be deleted.");
            if (await _db.Agents.AnyAsync(agent => agent.TuiProfileId == profileId, cancellationToken))
                throw new ConflictException("The profile is assigned to one or more agents.");

            var revisionIds = profile.Revisions.Select(revision => revision.Id).ToArray();
            if (await _db.AgentSessions.AnyAsync(
                    session => session.TuiProfileRevisionId != null
                               && revisionIds.Contains(session.TuiProfileRevisionId.Value),
                    cancellationToken))
            {
                throw new ConflictException("The profile has revisions referenced by agent sessions.");
            }

            profile.ActiveRevisionId = null;
            await _db.SaveChangesAsync(cancellationToken);
            _db.AgentTuiProfiles.Remove(profile);
            await _db.SaveChangesAsync(cancellationToken);
            await CommitAsync(transaction, cancellationToken);
        }
        catch (DbUpdateException exception)
        {
            await RollbackAsync(transaction, cancellationToken);
            throw new ConflictException("The profile is still in use and cannot be deleted.", exception);
        }
        catch (Exception exception) when (IsTransactionConcurrencyFailure(exception))
        {
            await RollbackAsync(transaction, cancellationToken);
            throw new ConflictException("The profile is still in use and cannot be deleted.", exception);
        }
        catch
        {
            await RollbackAsync(transaction, cancellationToken);
            throw;
        }
    }

    public async Task<AgentTuiSecretMutationDto> PutSecretAsync(
        Guid profileId,
        string environmentName,
        AgentTuiSecretWriteRequest request,
        CancellationToken cancellationToken)
    {
        ValidateSecretMutation(environmentName, request.ExpectedRevision, request.CorrelationId);
        if (string.IsNullOrEmpty(request.Value))
            throw new ValidationException(nameof(request.Value), "A non-empty secret value is required.");

        var preflightProfile = await _db.AgentTuiProfiles
            .AsNoTracking()
            .Include(candidate => candidate.ActiveRevision)
            .SingleOrDefaultAsync(candidate => candidate.Id == profileId, cancellationToken)
            ?? throw new NotFoundException(nameof(AgentTuiProfile), profileId);
        var preflightRevision = RequireActiveRevision(preflightProfile);
        EnsureExpectedRevision(preflightRevision, request.ExpectedRevision);
        var preflightDeclaredName = RequireDeclaredManagedSecret(preflightRevision, environmentName);
        var protectedValue = _secretProtector.Protect(
            profileId,
            preflightDeclaredName,
            request.Value);

        var now = UtcNow();
        await using var transaction = await BeginTransactionAsync(cancellationToken);
        _db.ChangeTracker.Clear();

        try
        {
            var profile = await _db.AgentTuiProfiles
                .Include(candidate => candidate.ActiveRevision)
                .Include(candidate => candidate.Secrets)
                .SingleOrDefaultAsync(candidate => candidate.Id == profileId, cancellationToken)
                ?? throw new NotFoundException(nameof(AgentTuiProfile), profileId);
            var revision = RequireActiveRevision(profile);
            EnsureExpectedRevision(revision, request.ExpectedRevision);
            var declaredName = RequireDeclaredManagedSecret(revision, environmentName);
            if (!string.Equals(declaredName, preflightDeclaredName, StringComparison.Ordinal))
            {
                throw new ConflictException(
                    "The managed secret declaration changed while the secret was being protected.");
            }

            var matchingSecrets = profile.Secrets
                .Where(secret => _environmentNameComparer.Equals(secret.Name, declaredName))
                .ToArray();
            if (matchingSecrets.Length > 1)
                throw new ConflictException("The profile contains ambiguous host-equivalent managed secrets.");
            var secret = matchingSecrets.SingleOrDefault();
            var operation = secret is null ? "set" : "replace";
            if (secret is null)
            {
                secret = new AgentTuiSecret
                {
                    Id = Guid.NewGuid(),
                    ProfileId = profileId,
                    Name = declaredName,
                    CreatedAt = now
                };
                _db.AgentTuiSecrets.Add(secret);
            }

            secret.Name = declaredName;
            secret.Ciphertext = protectedValue;
            secret.ProtectionVersion = SecretProtectionVersion;
            secret.UpdatedAt = now;
            profile.UpdatedAt = now;

            await RecordSecretAuditAsync(
                profileId,
                declaredName,
                operation,
                request.CorrelationId,
                now,
                cancellationToken);
            await CommitAsync(transaction, cancellationToken);
            return new AgentTuiSecretMutationDto(
                declaredName,
                true,
                now,
                revision.RevisionNumber);
        }
        catch (DbUpdateException exception)
        {
            await RollbackAsync(transaction, cancellationToken);
            throw new ConflictException("The secret could not be saved because the profile state changed.", exception);
        }
        catch (Exception exception) when (IsTransactionConcurrencyFailure(exception))
        {
            await RollbackAsync(transaction, cancellationToken);
            throw new ConflictException("The secret could not be saved because the profile state changed.", exception);
        }
        catch
        {
            await RollbackAsync(transaction, cancellationToken);
            throw;
        }
    }

    public async Task<AgentTuiSecretMutationDto> ClearSecretAsync(
        Guid profileId,
        string environmentName,
        AgentTuiSecretClearRequest request,
        CancellationToken cancellationToken)
    {
        ValidateSecretMutation(environmentName, request.ExpectedRevision, request.CorrelationId);
        var now = UtcNow();
        await using var transaction = await BeginTransactionAsync(cancellationToken);

        try
        {
            var profile = await _db.AgentTuiProfiles
                .Include(candidate => candidate.ActiveRevision)
                .Include(candidate => candidate.Secrets)
                .SingleOrDefaultAsync(candidate => candidate.Id == profileId, cancellationToken)
                ?? throw new NotFoundException(nameof(AgentTuiProfile), profileId);
            var revision = RequireActiveRevision(profile);
            EnsureExpectedRevision(revision, request.ExpectedRevision);

            var matchingSecrets = profile.Secrets
                .Where(secret => _environmentNameComparer.Equals(secret.Name, environmentName))
                .ToArray();
            if (matchingSecrets.Length > 1)
                throw new ConflictException("The profile contains ambiguous host-equivalent managed secrets.");
            var secret = matchingSecrets.SingleOrDefault();
            var declaredName = FindDeclaredSecret(revision, environmentName);
            var canonicalName = declaredName ?? secret?.Name
                ?? throw new ValidationException(
                    nameof(environmentName),
                    "The profile does not declare or store this managed secret.");
            if (secret is not null)
                _db.AgentTuiSecrets.Remove(secret);
            profile.UpdatedAt = now;

            await RecordSecretAuditAsync(
                profileId,
                canonicalName,
                "clear",
                request.CorrelationId,
                now,
                cancellationToken);
            await CommitAsync(transaction, cancellationToken);
            return new AgentTuiSecretMutationDto(
                canonicalName,
                false,
                now,
                revision.RevisionNumber);
        }
        catch (DbUpdateException exception)
        {
            await RollbackAsync(transaction, cancellationToken);
            throw new ConflictException("The secret could not be cleared because the profile state changed.", exception);
        }
        catch (Exception exception) when (IsTransactionConcurrencyFailure(exception))
        {
            await RollbackAsync(transaction, cancellationToken);
            throw new ConflictException("The secret could not be cleared because the profile state changed.", exception);
        }
        catch
        {
            await RollbackAsync(transaction, cancellationToken);
            throw;
        }
    }

    private IQueryable<AgentTuiProfile> ProfileReadQuery() => _db.AgentTuiProfiles
        .AsNoTracking()
        .Include(profile => profile.ActiveRevision)
        .Include(profile => profile.Secrets)
        .Include(profile => profile.Models)
        .AsSplitQuery();

    private AgentTuiProfileDto MapProfile(AgentTuiProfile profile)
    {
        var revision = RequireActiveRevision(profile);
        var arguments = DeserializeArray(revision.ArgumentsJson);
        var configuredSecrets = profile.Secrets.ToDictionary(
            secret => secret.Name,
            _environmentNameComparer);
        var secretNames = DeserializeArray(revision.SecretEnvironmentNamesJson)
            .Concat(configuredSecrets.Keys)
            .Distinct(_environmentNameComparer)
            .OrderBy(name => name, StringComparer.Ordinal)
            .Select(name => configuredSecrets.TryGetValue(name, out var secret)
                ? new AgentTuiSecretMetadataDto(name, true, secret.UpdatedAt)
                : new AgentTuiSecretMetadataDto(name, false, null))
            .ToArray();
        var runner = _runnerCatalog.Get(profile.Kind, arguments);

        return new AgentTuiProfileDto(
            profile.Id,
            profile.DisplayName,
            profile.Kind,
            profile.IsEnabled,
            profile.IsDefault,
            profile.Source,
            profile.SourceDefinitionName,
            revision.Id,
            revision.RevisionNumber,
            new AgentTuiProfileRevisionDto(
                revision.Id,
                revision.RevisionNumber,
                revision.Executable,
                arguments,
                DeserializeArray(revision.DiscoveryArgumentsJson),
                DeserializeArray(revision.VersionArgumentsJson),
                revision.WorkingDirectory,
                revision.AuthenticationMode,
                DeserializeDictionary(revision.NonSecretEnvironmentJson),
                DeserializeArray(revision.SecretEnvironmentNamesJson),
                revision.ModelArgumentName,
                revision.Guidance,
                revision.CreatedAt),
            secretNames,
            MergeModels(profile.Kind, profile.Models),
            runner.Capabilities,
            profile.CreatedAt,
            profile.UpdatedAt);
    }

    private IReadOnlyList<AgentTuiModelDto> MergeModels(
        AgentKind kind,
        IEnumerable<AgentTuiModel> persistedModels)
    {
        var merged = _runnerCatalog.Get(kind).CuratedModels
            .ToDictionary(model => model.Identifier, StringComparer.Ordinal);
        foreach (var model in persistedModels.OrderBy(model => model.Identifier, StringComparer.Ordinal))
        {
            merged[model.Identifier] = new AgentTuiModelDto(
                model.Identifier,
                model.DisplayName,
                model.Family,
                model.Source,
                model.Availability,
                model.DiscoveredAt,
                model.RunnerVersion,
                model.IsSuggestedDefault);
        }

        return merged.Values
            .OrderBy(model => model.Source)
            .ThenBy(model => model.Identifier, StringComparer.Ordinal)
            .ToArray();
    }

    private async Task RecordSecretAuditAsync(
        Guid profileId,
        string environmentName,
        string operation,
        string correlationId,
        DateTime occurredAt,
        CancellationToken cancellationToken)
    {
        var summary =
            $"Agent TUI secret: profileId={profileId}; environmentName={environmentName}; "
            + $"operation={operation}; result=succeeded; occurredAt={occurredAt:O}; correlationId={correlationId}";
        await _auditService.RecordEventAsync(
            AuditEventType.ToolInvocation,
            workflowId: null,
            stageId: null,
            stageExecutionId: null,
            summary,
            clientIp: _currentUser.IpAddress,
            userId: _currentUser.UserId,
            gitTagName: null,
            fullContentJson: null,
            cancellationToken);
    }

    private AgentTuiProfileRevision NewRevision(
        Guid profileId,
        int revisionNumber,
        AgentTuiProfileWriteRequest request,
        DateTime now) => new()
        {
            Id = Guid.NewGuid(),
            ProfileId = profileId,
            RevisionNumber = revisionNumber,
            Executable = request.Executable.Trim(),
            ArgumentsJson = JsonSerializer.Serialize(request.Arguments),
            DiscoveryArgumentsJson = JsonSerializer.Serialize(request.DiscoveryArguments),
            VersionArgumentsJson = JsonSerializer.Serialize(request.VersionArguments),
            WorkingDirectory = NullIfWhiteSpace(request.WorkingDirectory),
            AuthenticationMode = request.AuthenticationMode,
            NonSecretEnvironmentJson = JsonSerializer.Serialize(request.NonSecretEnvironment),
            SecretEnvironmentNamesJson = JsonSerializer.Serialize(
            request.SecretEnvironmentNames.Distinct(_environmentNameComparer).ToArray()),
            ModelArgumentName = NullIfWhiteSpace(request.ModelArgumentName),
            Guidance = request.Guidance ?? string.Empty,
            CreatedAt = now
        };

    private static AgentTuiProfileRevision CopyRevision(
        AgentTuiProfileRevision source,
        Guid profileId,
        int revisionNumber,
        DateTime now) => new()
        {
            Id = Guid.NewGuid(),
            ProfileId = profileId,
            RevisionNumber = revisionNumber,
            Executable = source.Executable,
            ArgumentsJson = source.ArgumentsJson,
            DiscoveryArgumentsJson = source.DiscoveryArgumentsJson,
            VersionArgumentsJson = source.VersionArgumentsJson,
            WorkingDirectory = source.WorkingDirectory,
            AuthenticationMode = source.AuthenticationMode,
            NonSecretEnvironmentJson = source.NonSecretEnvironmentJson,
            SecretEnvironmentNamesJson = source.SecretEnvironmentNamesJson,
            ModelArgumentName = source.ModelArgumentName,
            Guidance = source.Guidance,
            CreatedAt = now
        };

    private void AddOperatorModels(
        AgentTuiProfile profile,
        IReadOnlyList<AgentTuiModelWriteDto> models,
        DateTime now)
    {
        foreach (var model in models
                     .GroupBy(candidate => candidate.Identifier, StringComparer.Ordinal)
                     .Select(group => group.Last()))
        {
            _db.AgentTuiModels.Add(new AgentTuiModel
            {
                Id = Guid.NewGuid(),
                ProfileId = profile.Id,
                Identifier = model.Identifier,
                DisplayName = model.DisplayName,
                Family = NullIfWhiteSpace(model.Family),
                Source = AgentTuiModelSource.Operator,
                Availability = AgentTuiModelAvailability.Unverified,
                IsSuggestedDefault = model.IsSuggestedDefault,
                CreatedAt = now,
                UpdatedAt = now,
                Profile = profile
            });
        }
    }

    private async Task ClearOtherDefaultsAsync(
        Guid? exceptProfileId,
        CancellationToken cancellationToken)
    {
        var defaults = await _db.AgentTuiProfiles
            .Where(profile => profile.IsDefault && profile.Id != exceptProfileId)
            .ToListAsync(cancellationToken);
        foreach (var profile in defaults)
        {
            profile.IsDefault = false;
            profile.UpdatedAt = UtcNow();
        }
    }

    private void ValidateProfileRequest(
        AgentTuiProfileWriteRequest request,
        bool requireExpectedRevision)
    {
        ValidateDisplayName(request.DisplayName);
        if (!Enum.IsDefined(request.Kind))
            throw new ValidationException(nameof(request.Kind), "The runner kind is invalid.");
        if (!Enum.IsDefined(request.AuthenticationMode))
            throw new ValidationException(nameof(request.AuthenticationMode), "The authentication mode is invalid.");
        if (string.IsNullOrWhiteSpace(request.Executable))
            throw new ValidationException(nameof(request.Executable), "An executable is required.");
        if (request.Executable.Length > 2000)
            throw new ValidationException(nameof(request.Executable), "The executable is too long.");
        if (request.WorkingDirectory?.Length > 1000)
            throw new ValidationException(nameof(request.WorkingDirectory), "The working directory is too long.");
        if (request.ModelArgumentName?.Length > 100)
            throw new ValidationException(nameof(request.ModelArgumentName), "The model argument name is too long.");
        if (request.Guidance is null || request.Guidance.Length > MaximumGuidanceLength)
            throw new ValidationException(nameof(request.Guidance), "The profile guidance is too long.");
        if (requireExpectedRevision && request.ExpectedRevision is null)
            throw new ValidationException(nameof(request.ExpectedRevision), "Expected revision is required.");
        if (request.ExpectedRevision is <= 0)
            throw new ValidationException(nameof(request.ExpectedRevision), "Expected revision must be positive.");

        if (request.Arguments is null
            || request.DiscoveryArguments is null
            || request.VersionArguments is null)
        {
            throw new ValidationException(nameof(request.Arguments), "Argument collections are required.");
        }
        foreach (var argument in request.Arguments
                     .Concat(request.DiscoveryArguments)
                     .Concat(request.VersionArguments))
        {
            if (argument is null || argument.Length > MaximumArgumentLength)
                throw new ValidationException(nameof(request.Arguments), "Every argument must be bounded.");
        }

        if (request.NonSecretEnvironment is null || request.SecretEnvironmentNames is null)
            throw new ValidationException(nameof(request.NonSecretEnvironment), "Environment collections are required.");
        foreach (var name in request.NonSecretEnvironment.Keys.Concat(request.SecretEnvironmentNames))
            ValidateEnvironmentName(name);
        if (request.SecretEnvironmentNames.Count
            != request.SecretEnvironmentNames.Distinct(_environmentNameComparer).Count())
            throw new ValidationException(nameof(request.SecretEnvironmentNames), "Secret environment names must be unique.");
        if (request.NonSecretEnvironment.Keys.Count()
            != request.NonSecretEnvironment.Keys.Distinct(_environmentNameComparer).Count())
        {
            throw new ValidationException(
                nameof(request.NonSecretEnvironment),
                "Ordinary environment names must be unique for the host platform.");
        }
        if (request.NonSecretEnvironment.Keys.Intersect(
                request.SecretEnvironmentNames,
                _environmentNameComparer).Any())
            throw new ValidationException(nameof(request.NonSecretEnvironment), "An environment name cannot be both secret and ordinary.");
        if (request.NonSecretEnvironment.Values.Any(value =>
                value is null || value.Length > MaximumEnvironmentValueLength))
        {
            throw new ValidationException(
                nameof(request.NonSecretEnvironment),
                "Every ordinary environment value must be bounded.");
        }
        if (request.AuthenticationMode == AgentTuiAuthenticationMode.WrapperManaged
            && request.SecretEnvironmentNames.Count > 0)
        {
            throw new ValidationException(
                nameof(request.SecretEnvironmentNames),
                "Wrapper-managed profiles cannot declare managed secrets.");
        }

        if (request.Models is null)
            throw new ValidationException(nameof(request.Models), "The model collection is required.");
        foreach (var model in request.Models)
        {
            if (string.IsNullOrWhiteSpace(model.Identifier) || model.Identifier.Length > 500)
                throw new ValidationException(nameof(request.Models), "Every model requires a bounded identifier.");
            if (string.IsNullOrWhiteSpace(model.DisplayName) || model.DisplayName.Length > 200)
                throw new ValidationException(nameof(request.Models), "Every model requires a bounded display name.");
            if (model.Family?.Length > 200)
                throw new ValidationException(nameof(request.Models), "Every model family must be bounded.");
        }
    }

    private static void ValidateDisplayName(string displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName) || displayName.Trim().Length > 200)
            throw new ValidationException(nameof(displayName), "A bounded display name is required.");
    }

    private static void ValidateSecretMutation(
        string environmentName,
        int expectedRevision,
        string correlationId)
    {
        ValidateEnvironmentName(environmentName);
        if (expectedRevision <= 0)
            throw new ValidationException(nameof(expectedRevision), "Expected revision must be positive.");
        if (string.IsNullOrWhiteSpace(correlationId)
            || correlationId.Length > 200
            || correlationId.Any(character =>
                !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_' and not '.' and not ':'))
        {
            throw new ValidationException(nameof(correlationId), "A bounded correlation identity is required.");
        }
    }

    private static void ValidateEnvironmentName(string environmentName)
    {
        if (!AgentEnvironmentVariableNames.IsValid(environmentName))
        {
            throw new ValidationException(nameof(environmentName), "The environment-variable name is invalid.");
        }
    }

    private string RequireDeclaredManagedSecret(
        AgentTuiProfileRevision revision,
        string environmentName)
    {
        if (revision.AuthenticationMode != AgentTuiAuthenticationMode.ManagedEnvironment)
        {
            throw new ValidationException(
                nameof(environmentName),
                "Wrapper-managed profiles cannot store managed secrets.");
        }

        return FindDeclaredSecret(revision, environmentName)
            ?? throw new ValidationException(
                nameof(environmentName),
                "The profile does not declare this managed secret.");
    }

    private string? FindDeclaredSecret(
        AgentTuiProfileRevision revision,
        string environmentName)
    {
        var matches = DeserializeArray(revision.SecretEnvironmentNamesJson)
            .Where(name => _environmentNameComparer.Equals(name, environmentName))
            .ToArray();
        if (matches.Length > 1)
            throw new ConflictException("The profile contains ambiguous host-equivalent secret declarations.");
        return matches.SingleOrDefault();
    }

    private void EnsureConfiguredSecretsRetained(
        IEnumerable<AgentTuiSecret> configuredSecrets,
        AgentTuiProfileWriteRequest request)
    {
        var removed = configuredSecrets
            .Where(secret => !request.SecretEnvironmentNames.Contains(secret.Name, _environmentNameComparer))
            .Select(secret => secret.Name)
            .ToArray();
        if (removed.Length > 0)
        {
            throw new ConflictException(
                $"Configured managed secret '{removed[0]}' must be cleared before it is removed or wrapper-managed authentication is selected.");
        }
    }

    private static AgentTuiProfileRevision RequireActiveRevision(AgentTuiProfile profile)
    {
        if (profile.ActiveRevisionId is null || profile.ActiveRevision is null)
            throw new ConflictException("The profile has no usable active revision.");
        if (string.IsNullOrWhiteSpace(profile.ActiveRevision.Executable))
            throw new ConflictException("The profile active revision has no executable.");
        return profile.ActiveRevision;
    }

    private static void EnsureExpectedRevision(AgentTuiProfileRevision revision, int expectedRevision)
    {
        if (revision.RevisionNumber != expectedRevision)
        {
            throw new ConflictException(
                $"Profile revision conflict: expected {expectedRevision}, current {revision.RevisionNumber}.");
        }
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
            // PostgreSQL completes an aborted serializable transaction before surfacing 40001/40P01.
            // Preserve that authoritative failure instead of masking it with a second rollback error.
        }
    }

    private static bool IsTransactionConcurrencyFailure(Exception exception)
    {
        var current = exception;
        while (current is not null)
        {
            if (current is PostgresException postgres)
            {
                return postgres.SqlState is PostgresErrorCodes.SerializationFailure
                    or PostgresErrorCodes.DeadlockDetected;
            }

            current = current.InnerException;
        }

        return false;
    }

    private DateTime UtcNow() => _timeProvider.GetUtcNow().UtcDateTime;

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string[] DeserializeArray(string json) =>
        JsonSerializer.Deserialize<string[]>(json) ?? [];

    private static IReadOnlyDictionary<string, string> DeserializeDictionary(string json) =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(json)
        ?? new Dictionary<string, string>();
}
