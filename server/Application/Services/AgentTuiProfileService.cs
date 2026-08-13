using System.Data;
using System.Text.Json;
using System.Text.RegularExpressions;
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

public sealed partial class AgentTuiProfileService
{
    private const string SecretProtectionVersion = "v1";
    private const int MaximumArgumentLength = 2000;
    private const int MaximumEnvironmentValueLength = 4000;
    private const int MaximumGuidanceLength = 4000;
    private const int MaximumPersistedOperationJsonLength = 16_000;
    private const int MaximumPersistedSummaryLength = 4000;
    private const int MaximumRunnerVersionLength = 200;

    private readonly AppDbContext _db;
    private readonly IAgentTuiSecretProtector _secretProtector;
    private readonly AuditService _auditService;
    private readonly AgentTuiRunnerCatalog _runnerCatalog;
    private readonly TimeProvider _timeProvider;
    private readonly ICurrentUser _currentUser;
    private readonly IEqualityComparer<string> _environmentNameComparer;
    private readonly AgentTuiOperationCoordinator? _operationCoordinator;
    private readonly IRunnerProcessProbe? _processProbe;

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
            AgentEnvironmentVariableNames.ForCurrentPlatform(),
            operationCoordinator: null,
            processProbe: null)
    {
    }

    public AgentTuiProfileService(
        AppDbContext db,
        IAgentTuiSecretProtector secretProtector,
        AuditService auditService,
        AgentTuiRunnerCatalog runnerCatalog,
        TimeProvider timeProvider,
        ICurrentUser currentUser,
        AgentTuiOperationCoordinator operationCoordinator,
        IRunnerProcessProbe processProbe)
        : this(
            db,
            secretProtector,
            auditService,
            runnerCatalog,
            timeProvider,
            currentUser,
            AgentEnvironmentVariableNames.ForCurrentPlatform(),
            operationCoordinator,
            processProbe)
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
        : this(
            db,
            secretProtector,
            auditService,
            runnerCatalog,
            timeProvider,
            currentUser,
            environmentNameComparer,
            operationCoordinator: null,
            processProbe: null)
    {
    }

    internal AgentTuiProfileService(
        AppDbContext db,
        IAgentTuiSecretProtector secretProtector,
        AuditService auditService,
        AgentTuiRunnerCatalog runnerCatalog,
        TimeProvider timeProvider,
        ICurrentUser currentUser,
        IEqualityComparer<string> environmentNameComparer,
        AgentTuiOperationCoordinator? operationCoordinator,
        IRunnerProcessProbe? processProbe)
    {
        _db = db;
        _secretProtector = secretProtector;
        _auditService = auditService;
        _runnerCatalog = runnerCatalog;
        _timeProvider = timeProvider;
        _currentUser = currentUser;
        _environmentNameComparer = environmentNameComparer;
        _operationCoordinator = operationCoordinator;
        _processProbe = processProbe;
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

    public Task<IReadOnlyList<AgentTuiModelDto>> RefreshModelsAsync(
        Guid profileId,
        CancellationToken cancellationToken) =>
        RequireOperationCoordinator().RefreshModelsAsync(profileId, cancellationToken);

    public Task<AgentTuiValidationRunDto> ValidateAsync(
        Guid profileId,
        CancellationToken cancellationToken) =>
        RequireOperationCoordinator().ValidateAsync(profileId, cancellationToken);

    public async Task<IReadOnlyList<AgentTuiCapabilityDto>> GetCapabilitiesAsync(
        Guid profileId,
        CancellationToken cancellationToken)
    {
        var profile = await _db.AgentTuiProfiles
            .AsNoTracking()
            .Include(candidate => candidate.ActiveRevision)
            .SingleOrDefaultAsync(candidate => candidate.Id == profileId, cancellationToken)
            ?? throw new NotFoundException(nameof(AgentTuiProfile), profileId);
        var revision = RequireActiveRevision(profile);
        var cached = await _db.AgentTuiValidationRuns
            .AsNoTracking()
            .Where(run => run.ProfileId == profileId
                          && run.ProfileRevisionId == revision.Id
                          && run.Operation == "validation"
                          && run.Status != AgentTuiValidationStatus.Running)
            .OrderByDescending(run => run.CreatedAt)
            .Select(run => run.CapabilitiesJson)
            .FirstOrDefaultAsync(cancellationToken);
        var cachedCapabilities = DeserializeCapabilities(cached);
        return cachedCapabilities is { Count: > 0 }
            ? cachedCapabilities
            : _runnerCatalog.Get(profile.Kind, DeserializeArray(revision.ArgumentsJson)).Capabilities;
    }

    public async Task<AgentTuiValidationRunDto> GetValidationRunAsync(
        Guid runId,
        CancellationToken cancellationToken)
    {
        var run = await _db.AgentTuiValidationRuns
            .SingleOrDefaultAsync(candidate => candidate.Id == runId, cancellationToken)
            ?? throw new NotFoundException(nameof(AgentTuiValidationRun), runId);
        if (run.Status == AgentTuiValidationStatus.Running
            && !IsOperationRunActive(run))
        {
            await FinalizeIncompleteOperationCoreAsync(
                run.Id,
                AgentTuiValidationStatus.TimedOut,
                "The bounded operation could not confirm completion and was terminalized safely.",
                cancellationToken);
        }
        return MapValidationRun(run);
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

            foreach (var model in profile.Models.Where(IsRevisionBoundModel))
            {
                model.Availability = AgentTuiModelAvailability.Stale;
                model.UpdatedAt = now;
            }
            ReconcileOperatorModels(profile, request.Models, now);

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

    internal async Task<IReadOnlyList<AgentTuiModelDto>> RefreshModelsCoreAsync(
        Guid profileId,
        CancellationToken cancellationToken)
    {
        var snapshot = await LoadOperationSnapshotAsync(profileId, cancellationToken);
        await ReconcileInactiveOperationRunsCoreAsync(
            profileId,
            "discovery",
            cancellationToken);
        if (snapshot.Kind != AgentKind.OpenCode)
            return MergeModels(snapshot.Kind, snapshot.Models);

        var run = await CreateOperationRunAsync(snapshot, "discovery", cancellationToken);
        var auth = BuildAuthenticationEnvironment(snapshot);
        if (!auth.Ready)
        {
            await MarkDiscoveredModelsStaleAsync(snapshot, cancellationToken);
            await CompleteOperationRunAsync(
                run,
                AgentTuiValidationStatus.Failed,
                [Stage("discovery", AgentTuiValidationStageStatus.Failed, auth.Message)],
                [],
                runnerVersion: null,
                "Model discovery could not start because authentication is not ready.",
                new AgentTuiSuitabilityDto(false, false, false, false),
                cancellationToken);
            return await GetModelsAsync(profileId, cancellationToken);
        }

        var result = await RequireProcessProbe().RunAsync(
            BuildProcessRequest(snapshot, snapshot.DiscoveryArguments, auth),
            cancellationToken);

        var parsed = ParseDiscoveredModels(result);
        var runnerVersion = await LatestRunnerVersionAsync(snapshot, cancellationToken);
        if (parsed.IsComplete)
        {
            await ReplaceDiscoveredModelsAsync(
                snapshot,
                parsed.Identifiers,
                runnerVersion,
                cancellationToken);
            await CompleteOperationRunAsync(
                run,
                AgentTuiValidationStatus.Succeeded,
                [Stage("discovery", AgentTuiValidationStageStatus.Passed,
                    $"Discovered {parsed.Identifiers.Count} model identifiers.")],
                [],
                runnerVersion,
                "Model discovery completed successfully.",
                new AgentTuiSuitabilityDto(false, false, false, false),
                cancellationToken);
        }
        else
        {
            await MarkDiscoveredModelsStaleAsync(snapshot, cancellationToken);
            var status = result.TimedOut || result.Cancelled
                ? AgentTuiValidationStatus.TimedOut
                : AgentTuiValidationStatus.Failed;
            await CompleteOperationRunAsync(
                run,
                status,
                [Stage("discovery", AgentTuiValidationStageStatus.Failed, parsed.Message)],
                [],
                runnerVersion,
                parsed.Message,
                new AgentTuiSuitabilityDto(false, false, false, false),
                cancellationToken);
        }

        return await GetModelsAsync(profileId, cancellationToken);
    }

    internal async Task<AgentTuiValidationRunDto> ValidateCoreAsync(
        Guid profileId,
        CancellationToken cancellationToken)
    {
        var snapshot = await LoadOperationSnapshotAsync(profileId, cancellationToken);
        await ReconcileInactiveOperationRunsCoreAsync(
            profileId,
            "validation",
            cancellationToken);
        var run = await CreateOperationRunAsync(snapshot, "validation", cancellationToken);
        var probe = RequireProcessProbe();
        var stages = new List<AgentTuiValidationStageDto>();
        var capabilities = _runnerCatalog.Get(snapshot.Kind, snapshot.Arguments).Capabilities;
        var timedOut = false;

        var executable = await probe.CheckExecutableAsync(
            snapshot.Executable,
            cancellationToken);
        stages.Add(Stage(
            "executable",
            executable.IsAvailable
                ? AgentTuiValidationStageStatus.Passed
                : AgentTuiValidationStageStatus.Failed,
            executable.Message));

        var arguments = await CheckOrderedArgumentsAsync(
            snapshot,
            probe,
            cancellationToken);
        stages.Add(Stage(
            "arguments",
            arguments.IsAvailable
                ? AgentTuiValidationStageStatus.Passed
                : AgentTuiValidationStageStatus.Failed,
            arguments.Message));

        var workingDirectory = snapshot.WorkingDirectory is null
            ? new RunnerPathCheck(true, "The process default working directory will be used.")
            : await probe.CheckDirectoryAsync(snapshot.WorkingDirectory, cancellationToken);
        stages.Add(Stage(
            "workingDirectory",
            workingDirectory.IsAvailable
                ? AgentTuiValidationStageStatus.Passed
                : AgentTuiValidationStageStatus.Failed,
            workingDirectory.Message));

        var auth = BuildAuthenticationEnvironment(snapshot);
        stages.Add(Stage(
            "authentication",
            auth.Ready
                ? AgentTuiValidationStageStatus.Passed
                : AgentTuiValidationStageStatus.Failed,
            auth.Message));

        string? runnerVersion = null;
        if (stages.Any(stage => stage.Status == AgentTuiValidationStageStatus.Failed))
        {
            AddSkippedProbeStages(stages);
            var failedSuitability = new AgentTuiSuitabilityDto(false, false, false, false);
            stages.Add(SuitabilityStage(failedSuitability));
            await CompleteOperationRunAsync(
                run,
                AgentTuiValidationStatus.Failed,
                stages,
                capabilities,
                runnerVersion,
                "Profile validation failed before runner probing.",
                failedSuitability,
                cancellationToken);
            return MapValidationRun(run);
        }

        if (snapshot.VersionArguments.Count == 0)
        {
            stages.Add(Stage(
                "versionCapabilities",
                AgentTuiValidationStageStatus.Skipped,
                "No version arguments are configured; static capabilities remain authoritative."));
        }
        else
        {
            var versionResult = await probe.RunAsync(
                BuildProcessRequest(snapshot, snapshot.VersionArguments, auth),
                cancellationToken);
            timedOut |= versionResult.TimedOut || versionResult.Cancelled;
            runnerVersion = ParseRunnerVersion(snapshot.Kind, versionResult);
            if (runnerVersion is null)
            {
                stages.Add(Stage(
                    "versionCapabilities",
                    AgentTuiValidationStageStatus.Failed,
                    ProbeFailureMessage(versionResult, "Runner version and capabilities could not be verified.")));
            }
            else
            {
                stages.Add(Stage(
                    "versionCapabilities",
                    AgentTuiValidationStageStatus.Passed,
                    "Runner version and declared capabilities were verified."));
            }
        }

        if (snapshot.Kind == AgentKind.OpenCode)
        {
            var discoveryResult = await probe.RunAsync(
                BuildProcessRequest(snapshot, snapshot.DiscoveryArguments, auth),
                cancellationToken);
            timedOut |= discoveryResult.TimedOut || discoveryResult.Cancelled;
            var discovery = ParseDiscoveredModels(discoveryResult);
            stages.Add(Stage(
                "discovery",
                discovery.IsComplete
                    ? AgentTuiValidationStageStatus.Passed
                    : AgentTuiValidationStageStatus.Degraded,
                discovery.Message));
            if (discovery.IsComplete)
            {
                await ReplaceDiscoveredModelsAsync(
                    snapshot,
                    discovery.Identifiers,
                    runnerVersion,
                    cancellationToken);
            }
            else
            {
                await MarkDiscoveredModelsStaleAsync(snapshot, cancellationToken);
            }
        }
        else
        {
            stages.Add(Stage(
                "discovery",
                AgentTuiValidationStageStatus.Skipped,
                "This runner uses its cached curated and operator catalogue."));
        }

        var startupResult = await probe.RunAsync(
            BuildProcessRequest(
                snapshot,
                snapshot.Arguments,
                auth,
                stopAfter: TimeSpan.FromSeconds(1)),
            cancellationToken);
        timedOut |= startupResult.TimedOut || startupResult.Cancelled;
        var startupPassed = startupResult.Started
                            && !startupResult.TimedOut
                            && !startupResult.Cancelled
                            && !startupResult.OutputTruncated
                            && startupResult.CleanupConfirmed
                            && !startupResult.SensitiveOutputDetected
                            && startupResult.ExitCode is null or 0;
        stages.Add(Stage(
            "startup",
            startupPassed
                ? AgentTuiValidationStageStatus.Passed
                : AgentTuiValidationStageStatus.Failed,
            startupPassed
                ? "The bounded startup probe started successfully."
                : ProbeFailureMessage(startupResult, "The bounded startup probe failed.")));
        stages.Add(Stage(
            "cleanStop",
            startupResult.CleanlyStopped && startupResult.CleanupConfirmed
                ? AgentTuiValidationStageStatus.Passed
                : AgentTuiValidationStageStatus.Failed,
            startupResult.CleanlyStopped && startupResult.CleanupConfirmed
                ? "The startup probe stopped without leaving a child process."
                : !startupResult.CleanupConfirmed
                    ? "The startup probe could not confirm process cleanup."
                    : "The startup probe required forced cleanup."));

        var suitability = CalculateSuitability(stages, capabilities);
        stages.Add(SuitabilityStage(suitability));
        var status = timedOut
            ? AgentTuiValidationStatus.TimedOut
            : stages.Any(stage => stage.Status == AgentTuiValidationStageStatus.Failed)
                ? AgentTuiValidationStatus.Failed
                : stages.Any(stage => stage.Status == AgentTuiValidationStageStatus.Degraded)
                  || !suitability.Queued
                  || !suitability.Delegated
                  || !suitability.Resumable
                    ? AgentTuiValidationStatus.Partial
                    : AgentTuiValidationStatus.Succeeded;
        var summary = status switch
        {
            AgentTuiValidationStatus.Succeeded => "Profile validation completed successfully.",
            AgentTuiValidationStatus.Partial => "Profile validation completed with declared limitations.",
            AgentTuiValidationStatus.TimedOut => "Profile validation reached its bounded timeout.",
            _ => "Profile validation failed."
        };
        await CompleteOperationRunAsync(
            run,
            status,
            stages,
            capabilities,
            runnerVersion,
            summary,
            suitability,
            cancellationToken);
        return MapValidationRun(run);
    }

    internal async Task<AgentTuiValidationRunDto?> FinalizeIncompleteOperationCoreAsync(
        Guid runId,
        AgentTuiValidationStatus status,
        string summary,
        CancellationToken cancellationToken)
    {
        var run = await _db.AgentTuiValidationRuns
            .SingleOrDefaultAsync(candidate => candidate.Id == runId
                                               && candidate.Status == AgentTuiValidationStatus.Running,
                cancellationToken);
        if (run is null)
            return null;
        if (run.Operation == "discovery")
        {
            await MarkDiscoveredModelsStaleAsync(
                run.ProfileId,
                run.ProfileRevisionId,
                cancellationToken);
        }
        await CompleteOperationRunAsync(
            run,
            status,
            [Stage(
                run.Operation,
                AgentTuiValidationStageStatus.Failed,
                status == AgentTuiValidationStatus.TimedOut
                    ? "The bounded operation reached its deadline; retained data remains available."
                    : "The bounded operation failed safely; retained data remains available.")],
            [],
            runnerVersion: null,
            summary,
            new AgentTuiSuitabilityDto(false, false, false, false),
            cancellationToken);
        return MapValidationRun(run);
    }

    private async Task ReconcileInactiveOperationRunsCoreAsync(
        Guid profileId,
        string operation,
        CancellationToken cancellationToken)
    {
        var runIds = await _db.AgentTuiValidationRuns
            .AsNoTracking()
            .Where(run => run.ProfileId == profileId
                          && run.Operation == operation
                          && run.Status == AgentTuiValidationStatus.Running)
            .OrderBy(run => run.CreatedAt)
            .Select(run => run.Id)
            .ToListAsync(cancellationToken);
        foreach (var runId in runIds)
        {
            if (_operationCoordinator?.IsRunActive(profileId, operation, runId) == true)
                continue;
            await FinalizeIncompleteOperationCoreAsync(
                runId,
                AgentTuiValidationStatus.TimedOut,
                "The bounded operation could not confirm completion and was terminalized safely.",
                cancellationToken);
        }
    }

    private async Task<AgentTuiOperationSnapshot> LoadOperationSnapshotAsync(
        Guid profileId,
        CancellationToken cancellationToken)
    {
        var profile = await _db.AgentTuiProfiles
            .AsNoTracking()
            .Include(candidate => candidate.ActiveRevision)
            .Include(candidate => candidate.Models)
            .Include(candidate => candidate.Secrets)
            .AsSplitQuery()
            .SingleOrDefaultAsync(candidate => candidate.Id == profileId, cancellationToken)
            ?? throw new NotFoundException(nameof(AgentTuiProfile), profileId);
        var revision = RequireActiveRevision(profile);
        return new AgentTuiOperationSnapshot(
            profile.Id,
            profile.Kind,
            revision.Id,
            revision.RevisionNumber,
            revision.Executable,
            DeserializeArray(revision.ArgumentsJson),
            DeserializeArray(revision.DiscoveryArgumentsJson),
            DeserializeArray(revision.VersionArgumentsJson),
            revision.WorkingDirectory,
            revision.AuthenticationMode,
            DeserializeDictionary(revision.NonSecretEnvironmentJson),
            DeserializeArray(revision.SecretEnvironmentNamesJson),
            profile.Secrets.Select(secret => new SnapshotSecret(
                secret.Name,
                secret.Ciphertext)).ToArray(),
            profile.Models.Select(CloneModel).ToArray());
    }

    private AuthenticationEnvironment BuildAuthenticationEnvironment(
        AgentTuiOperationSnapshot snapshot)
    {
        var environment = new Dictionary<string, string>(
            snapshot.NonSecretEnvironment,
            _environmentNameComparer);
        if (snapshot.AuthenticationMode == AgentTuiAuthenticationMode.WrapperManaged)
        {
            return new AuthenticationEnvironment(
                true,
                environment,
                [],
                "Authentication is owned by the configured wrapper; managed keys were not accessed.");
        }

        var plaintextValues = new List<string>();
        foreach (var declaredName in snapshot.SecretEnvironmentNames)
        {
            var matches = snapshot.Secrets
                .Where(secret => _environmentNameComparer.Equals(secret.Name, declaredName))
                .ToArray();
            if (matches.Length != 1)
            {
                return new AuthenticationEnvironment(
                    false,
                    new Dictionary<string, string>(),
                    [],
                    "One or more declared managed credentials are missing or ambiguous.");
            }

            try
            {
                var plaintext = _secretProtector.Unprotect(
                    snapshot.ProfileId,
                    matches[0].Name,
                    matches[0].Ciphertext);
                if (string.IsNullOrEmpty(plaintext) || plaintext.Length > MaximumEnvironmentValueLength)
                {
                    return new AuthenticationEnvironment(
                        false,
                        new Dictionary<string, string>(),
                        [],
                        "One or more managed credentials could not be read safely.");
                }
                environment[matches[0].Name] = plaintext;
                plaintextValues.Add(plaintext);
            }
            catch
            {
                return new AuthenticationEnvironment(
                    false,
                    new Dictionary<string, string>(),
                    [],
                    "Managed credential protection is not ready for this profile.");
            }
        }

        return new AuthenticationEnvironment(
            true,
            environment,
            plaintextValues,
            "All declared managed credentials are configured and decryptable.");
    }

    private static async Task<RunnerPathCheck> CheckOrderedArgumentsAsync(
        AgentTuiOperationSnapshot snapshot,
        IRunnerProcessProbe probe,
        CancellationToken cancellationToken)
    {
        if (snapshot.Arguments.Count > 256
            || snapshot.DiscoveryArguments.Count > 256
            || snapshot.VersionArguments.Count > 256
            || snapshot.Arguments.Concat(snapshot.DiscoveryArguments).Concat(snapshot.VersionArguments)
                .Any(argument => argument is null || argument.Length > MaximumArgumentLength))
        {
            return new RunnerPathCheck(false, "The ordered argument collection is invalid or too large.");
        }

        var wrapperPaths = new List<string>();
        foreach (var argumentSet in new[]
                 {
                     snapshot.Arguments,
                     snapshot.DiscoveryArguments,
                     snapshot.VersionArguments
                 })
        {
            for (var index = 0; index < argumentSet.Count; index++)
            {
                if (!string.Equals(argumentSet[index], "-File", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (index + 1 >= argumentSet.Count || string.IsNullOrWhiteSpace(argumentSet[index + 1]))
                {
                    return new RunnerPathCheck(
                        false,
                        "A wrapper file argument is missing its separate path value.");
                }
                wrapperPaths.Add(argumentSet[index + 1]);
            }
        }
        var distinctWrapperPaths = wrapperPaths.Distinct(StringComparer.Ordinal).ToArray();
        if (distinctWrapperPaths.Length == 0)
            return new RunnerPathCheck(true, "Ordered arguments require no shell interpolation.");
        foreach (var path in distinctWrapperPaths)
        {
            var check = await probe.CheckFileAsync(path, cancellationToken);
            if (!check.IsAvailable)
                return check;
        }
        return new RunnerPathCheck(true, "Ordered arguments and wrapper files are available.");
    }

    private RunnerProcessRequest BuildProcessRequest(
        AgentTuiOperationSnapshot snapshot,
        IReadOnlyList<string> arguments,
        AuthenticationEnvironment authentication,
        TimeSpan? stopAfter = null) => new(
            snapshot.Executable,
            arguments.ToArray(),
            snapshot.WorkingDirectory,
            new Dictionary<string, string>(authentication.Environment, _environmentNameComparer),
            authentication.SecretValues.ToArray(),
            stopAfter);

    private static DiscoveryParseResult ParseDiscoveredModels(RunnerProcessResult result)
    {
        if (!result.Started
            || result.ExitCode != 0
            || result.TimedOut
            || result.Cancelled
            || result.OutputTruncated
            || !result.CleanupConfirmed
            || result.SensitiveOutputDetected)
        {
            return new DiscoveryParseResult(
                false,
                [],
                ProbeFailureMessage(result, "Model discovery failed; the previous catalogue was retained."));
        }

        var identifiers = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        using var reader = new StringReader(result.StandardOutput);
        while (reader.ReadLine() is { } identifier)
        {
            if (identifier.Length == 0
                || identifier.Length > 500
                || !ModelIdentifierRegex().IsMatch(identifier))
            {
                return new DiscoveryParseResult(
                    false,
                    [],
                    "Model discovery returned malformed output; the previous catalogue was retained.");
            }
            if (seen.Add(identifier))
                identifiers.Add(identifier);
        }
        if (identifiers.Count == 0)
        {
            return new DiscoveryParseResult(
                false,
                [],
                "Model discovery returned no usable identifiers; the previous catalogue was retained.");
        }
        return new DiscoveryParseResult(
            true,
            identifiers,
            $"Discovered {identifiers.Count} model identifiers.");
    }

    private async Task ReplaceDiscoveredModelsAsync(
        AgentTuiOperationSnapshot snapshot,
        IReadOnlyList<string> identifiers,
        string? runnerVersion,
        CancellationToken cancellationToken)
    {
        await using var transaction = await BeginTransactionAsync(cancellationToken);
        try
        {
            var activeRevisionId = await _db.AgentTuiProfiles
                .AsNoTracking()
                .Where(profile => profile.Id == snapshot.ProfileId)
                .Select(profile => profile.ActiveRevisionId)
                .SingleOrDefaultAsync(cancellationToken);
            if (activeRevisionId != snapshot.RevisionId)
            {
                await CommitAsync(transaction, cancellationToken);
                return;
            }

            await ReplaceDiscoveredModelsForActiveRevisionAsync(
                snapshot,
                identifiers,
                runnerVersion,
                cancellationToken);
            await CommitAsync(transaction, cancellationToken);
        }
        catch
        {
            await RollbackAsync(transaction, cancellationToken);
            throw;
        }
    }

    private async Task ReplaceDiscoveredModelsForActiveRevisionAsync(
        AgentTuiOperationSnapshot snapshot,
        IReadOnlyList<string> identifiers,
        string? runnerVersion,
        CancellationToken cancellationToken)
    {
        var now = UtcNow();
        var persisted = await _db.AgentTuiModels
            .Where(model => model.ProfileId == snapshot.ProfileId)
            .ToListAsync(cancellationToken);
        var accepted = identifiers.ToHashSet(StringComparer.Ordinal);
        var curated = _runnerCatalog.Get(snapshot.Kind).CuratedModels
            .ToDictionary(model => model.Identifier, StringComparer.Ordinal);
        _db.AgentTuiModels.RemoveRange(persisted.Where(model =>
            !accepted.Contains(model.Identifier)
            && model.Source is AgentTuiModelSource.Discovered or AgentTuiModelSource.Curated));

        for (var index = 0; index < identifiers.Count; index++)
        {
            var identifier = identifiers[index];
            var existing = persisted.SingleOrDefault(model =>
                string.Equals(model.Identifier, identifier, StringComparison.Ordinal));
            if (existing is not null)
            {
                if (curated.TryGetValue(identifier, out var curatedModel)
                    && existing.Source != AgentTuiModelSource.Operator)
                {
                    existing.DisplayName = curatedModel.DisplayName;
                    existing.Family = curatedModel.Family;
                    existing.Source = AgentTuiModelSource.Curated;
                    existing.IsSuggestedDefault = curatedModel.IsSuggestedDefault;
                }
                existing.Availability = AgentTuiModelAvailability.Verified;
                existing.DiscoveredAt = now;
                existing.RunnerVersion = runnerVersion;
                existing.UpdatedAt = now.AddTicks(index);
                continue;
            }

            var isCurated = curated.TryGetValue(identifier, out var suggestion);

            _db.AgentTuiModels.Add(new AgentTuiModel
            {
                Id = Guid.NewGuid(),
                ProfileId = snapshot.ProfileId,
                Identifier = identifier,
                DisplayName = isCurated ? suggestion!.DisplayName : identifier,
                Family = isCurated ? suggestion!.Family : null,
                Source = isCurated
                    ? AgentTuiModelSource.Curated
                    : AgentTuiModelSource.Discovered,
                Availability = AgentTuiModelAvailability.Verified,
                DiscoveredAt = now,
                RunnerVersion = runnerVersion,
                IsSuggestedDefault = isCurated && suggestion!.IsSuggestedDefault,
                CreatedAt = now.AddTicks(index),
                UpdatedAt = now.AddTicks(index)
            });
        }

        foreach (var operatorModel in persisted.Where(model =>
                     model.Source == AgentTuiModelSource.Operator
                     && !accepted.Contains(model.Identifier)))
        {
            operatorModel.Availability = AgentTuiModelAvailability.Unverified;
            operatorModel.DiscoveredAt = null;
            operatorModel.RunnerVersion = null;
            operatorModel.UpdatedAt = now.AddTicks(identifiers.Count);
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task MarkDiscoveredModelsStaleAsync(
        AgentTuiOperationSnapshot snapshot,
        CancellationToken cancellationToken) =>
        await MarkDiscoveredModelsStaleAsync(
            snapshot.ProfileId,
            snapshot.RevisionId,
            cancellationToken);

    private async Task MarkDiscoveredModelsStaleAsync(
        Guid profileId,
        Guid expectedRevisionId,
        CancellationToken cancellationToken)
    {
        await using var transaction = await BeginTransactionAsync(cancellationToken);
        try
        {
            var activeRevisionId = await _db.AgentTuiProfiles
                .AsNoTracking()
                .Where(profile => profile.Id == profileId)
                .Select(profile => profile.ActiveRevisionId)
                .SingleOrDefaultAsync(cancellationToken);
            if (activeRevisionId != expectedRevisionId)
            {
                await CommitAsync(transaction, cancellationToken);
                return;
            }

            await MarkDiscoveredModelsStaleForActiveRevisionAsync(profileId, cancellationToken);
            await CommitAsync(transaction, cancellationToken);
        }
        catch
        {
            await RollbackAsync(transaction, cancellationToken);
            throw;
        }
    }

    private async Task MarkDiscoveredModelsStaleForActiveRevisionAsync(
        Guid profileId,
        CancellationToken cancellationToken)
    {
        var models = await _db.AgentTuiModels
            .Where(model => model.ProfileId == profileId
                            && (model.Source == AgentTuiModelSource.Discovered
                                || model.Availability == AgentTuiModelAvailability.Verified))
            .ToListAsync(cancellationToken);
        foreach (var model in models)
        {
            model.Availability = AgentTuiModelAvailability.Stale;
            model.UpdatedAt = UtcNow();
        }
        if (models.Count > 0)
            await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task<string?> LatestRunnerVersionAsync(
        AgentTuiOperationSnapshot snapshot,
        CancellationToken cancellationToken) =>
        await _db.AgentTuiValidationRuns
            .AsNoTracking()
            .Where(run => run.ProfileId == snapshot.ProfileId
                          && run.ProfileRevisionId == snapshot.RevisionId
                          && run.Operation == "validation"
                          && run.RunnerVersion != null)
            .OrderByDescending(run => run.CreatedAt)
            .Select(run => run.RunnerVersion)
            .FirstOrDefaultAsync(cancellationToken);

    private async Task<AgentTuiValidationRun> CreateOperationRunAsync(
        AgentTuiOperationSnapshot snapshot,
        string operation,
        CancellationToken cancellationToken)
    {
        var now = UtcNow();
        var run = new AgentTuiValidationRun
        {
            Id = Guid.NewGuid(),
            ProfileId = snapshot.ProfileId,
            ProfileRevisionId = snapshot.RevisionId,
            Operation = operation,
            Status = AgentTuiValidationStatus.Running,
            ResultsJson = "{}",
            CapabilitiesJson = "[]",
            Summary = "Operation is running.",
            CreatedAt = now,
            StartedAt = now
        };
        RequireOperationCoordinator().RegisterRun(
            snapshot.ProfileId,
            operation,
            run.Id);
        _db.AgentTuiValidationRuns.Add(run);
        await _db.SaveChangesAsync(cancellationToken);
        return run;
    }

    private async Task CompleteOperationRunAsync(
        AgentTuiValidationRun run,
        AgentTuiValidationStatus status,
        IReadOnlyList<AgentTuiValidationStageDto> stages,
        IReadOnlyList<AgentTuiCapabilityDto> capabilities,
        string? runnerVersion,
        string summary,
        AgentTuiSuitabilityDto suitability,
        CancellationToken cancellationToken)
    {
        run.Status = status;
        run.ResultsJson = BoundedJson(new PersistedValidationResults(stages, suitability));
        run.CapabilitiesJson = BoundedJson(capabilities);
        run.RunnerVersion = BoundText(runnerVersion, MaximumRunnerVersionLength);
        run.Summary = BoundText(summary, MaximumPersistedSummaryLength);
        run.CompletedAt = UtcNow();
        await _db.SaveChangesAsync(cancellationToken);
    }

    private static AgentTuiValidationRunDto MapValidationRun(AgentTuiValidationRun run)
    {
        PersistedValidationResults results;
        try
        {
            results = JsonSerializer.Deserialize<PersistedValidationResults>(run.ResultsJson)
                      ?? EmptyValidationResults;
        }
        catch (JsonException)
        {
            results = EmptyValidationResults;
        }
        var capabilities = DeserializeCapabilities(run.CapabilitiesJson) ?? [];
        return new AgentTuiValidationRunDto(
            run.Id,
            run.ProfileId,
            run.ProfileRevisionId,
            run.Operation,
            run.Status,
            results.Stages,
            capabilities,
            run.RunnerVersion,
            run.Summary ?? string.Empty,
            results.Suitability,
            run.CreatedAt,
            run.StartedAt,
            run.CompletedAt);
    }

    private static IReadOnlyList<AgentTuiCapabilityDto>? DeserializeCapabilities(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;
        try
        {
            return JsonSerializer.Deserialize<AgentTuiCapabilityDto[]>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static AgentTuiSuitabilityDto CalculateSuitability(
        IReadOnlyList<AgentTuiValidationStageDto> stages,
        IReadOnlyList<AgentTuiCapabilityDto> capabilities)
    {
        var mandatoryStagesPassed = stages
            .Where(stage => stage.Name is "executable" or "arguments" or "workingDirectory"
                or "authentication" or "versionCapabilities" or "startup" or "cleanStop")
            .All(stage => stage.Status is AgentTuiValidationStageStatus.Passed
                or AgentTuiValidationStageStatus.Skipped);
        var structured = CapabilityIsSupported(capabilities, "structuredActivity");
        var resumable = CapabilityIsSupported(capabilities, "sessionResume");
        return new AgentTuiSuitabilityDto(
            mandatoryStagesPassed,
            mandatoryStagesPassed && structured,
            mandatoryStagesPassed && structured,
            mandatoryStagesPassed && resumable);
    }

    private static bool CapabilityIsSupported(
        IReadOnlyList<AgentTuiCapabilityDto> capabilities,
        string name) =>
        capabilities.Single(capability => capability.Name == name).State
        == AgentTuiCapabilityState.Supported;

    private static AgentTuiValidationStageDto SuitabilityStage(AgentTuiSuitabilityDto suitability) =>
        Stage(
            "suitability",
            suitability.Interactive
            && suitability.Queued
            && suitability.Delegated
            && suitability.Resumable
                ? AgentTuiValidationStageStatus.Passed
                : suitability.Interactive
                    ? AgentTuiValidationStageStatus.Degraded
                    : AgentTuiValidationStageStatus.Failed,
            suitability.Interactive
                ? "Interactive use is available; unattended and resume suitability follow declared capabilities."
                : "The profile is not suitable for runner use until mandatory validation stages pass.");

    private static void AddSkippedProbeStages(
        ICollection<AgentTuiValidationStageDto> stages,
        bool includeVersion = true)
    {
        if (includeVersion)
        {
            stages.Add(Stage(
                "versionCapabilities",
                AgentTuiValidationStageStatus.Skipped,
                "Version and capability probing was skipped after an earlier mandatory failure."));
        }
        stages.Add(Stage(
            "discovery",
            AgentTuiValidationStageStatus.Skipped,
            "Model discovery was skipped after an earlier mandatory failure."));
        stages.Add(Stage(
            "startup",
            AgentTuiValidationStageStatus.Skipped,
            "Startup probing was skipped after an earlier mandatory failure."));
        stages.Add(Stage(
            "cleanStop",
            AgentTuiValidationStageStatus.Skipped,
            "Clean-stop verification was skipped after an earlier mandatory failure."));
    }

    private static string? ParseRunnerVersion(
        AgentKind kind,
        RunnerProcessResult result)
    {
        if (!result.Started
            || result.ExitCode != 0
            || result.TimedOut
            || result.Cancelled
            || result.OutputTruncated
            || !result.CleanupConfirmed
            || result.SensitiveOutputDetected)
        {
            return null;
        }
        var versions = result.StandardOutput.ReplaceLineEndings("\n")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .Select(line => NormalizeRunnerVersion(kind, line))
            .Where(version => version is not null)
            .ToArray();
        return versions is [{ Length: <= MaximumRunnerVersionLength } version]
            ? version
            : null;
    }

    private static string? NormalizeRunnerVersion(AgentKind kind, string line)
    {
        var (displayName, match) = kind switch
        {
            AgentKind.ClaudeCode => ("Claude Code", ClaudeRunnerVersionRegex().Match(line)),
            AgentKind.Codex => ("Codex", CodexRunnerVersionRegex().Match(line)),
            AgentKind.OpenCode => ("OpenCode", OpenCodeRunnerVersionRegex().Match(line)),
            _ => (string.Empty, Match.Empty)
        };
        if (!match.Success)
            return null;

        var version = match.Groups["version"].Success
            ? match.Groups["version"].Value
            : match.Groups["suffixVersion"].Value;
        return $"{displayName} {version}";
    }

    private static string ProbeFailureMessage(RunnerProcessResult result, string fallback)
    {
        if (result.TimedOut || result.Cancelled)
            return "The bounded runner probe did not complete before its deadline.";
        if (result.OutputTruncated)
            return "The runner probe exceeded the bounded output limit.";
        if (!result.CleanupConfirmed)
            return "The runner probe could not confirm process cleanup.";
        if (result.SensitiveOutputDetected)
            return "The runner probe returned credential-like diagnostics that were discarded.";
        return fallback;
    }

    private static AgentTuiValidationStageDto Stage(
        string name,
        AgentTuiValidationStageStatus status,
        string message) => new(
            name,
            status,
            BoundText(message, 500) ?? "No safe stage detail is available.");

    private static string BoundedJson<T>(T value)
    {
        var json = JsonSerializer.Serialize(value);
        return json.Length <= MaximumPersistedOperationJsonLength ? json : "{}";
    }

    private static string? BoundText(string? value, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var normalized = value.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return normalized.Length <= maximumLength
            ? normalized
            : normalized[..maximumLength];
    }

    private static AgentTuiModel CloneModel(AgentTuiModel model) => new()
    {
        Id = model.Id,
        ProfileId = model.ProfileId,
        Identifier = model.Identifier,
        DisplayName = model.DisplayName,
        Family = model.Family,
        Source = model.Source,
        Availability = model.Availability,
        DiscoveredAt = model.DiscoveredAt,
        RunnerVersion = model.RunnerVersion,
        IsSuggestedDefault = model.IsSuggestedDefault,
        CreatedAt = model.CreatedAt,
        UpdatedAt = model.UpdatedAt
    };

    private AgentTuiOperationCoordinator RequireOperationCoordinator() =>
        _operationCoordinator
        ?? throw new InvalidOperationException("Agent TUI operation coordination is not configured.");

    private bool IsOperationRunActive(AgentTuiValidationRun run) =>
        _operationCoordinator?.IsRunActive(run.ProfileId, run.Operation, run.Id) == true;

    private IRunnerProcessProbe RequireProcessProbe() =>
        _processProbe
        ?? throw new InvalidOperationException("Agent TUI runner probing is not configured.");

    private static readonly PersistedValidationResults EmptyValidationResults = new(
        [],
        new AgentTuiSuitabilityDto(false, false, false, false));

    private sealed record AgentTuiOperationSnapshot(
        Guid ProfileId,
        AgentKind Kind,
        Guid RevisionId,
        int RevisionNumber,
        string Executable,
        IReadOnlyList<string> Arguments,
        IReadOnlyList<string> DiscoveryArguments,
        IReadOnlyList<string> VersionArguments,
        string? WorkingDirectory,
        AgentTuiAuthenticationMode AuthenticationMode,
        IReadOnlyDictionary<string, string> NonSecretEnvironment,
        IReadOnlyList<string> SecretEnvironmentNames,
        IReadOnlyList<SnapshotSecret> Secrets,
        IReadOnlyList<AgentTuiModel> Models);

    private sealed record SnapshotSecret(string Name, string Ciphertext);

    private sealed record AuthenticationEnvironment(
        bool Ready,
        IReadOnlyDictionary<string, string> Environment,
        IReadOnlyList<string> SecretValues,
        string Message);

    private sealed record DiscoveryParseResult(
        bool IsComplete,
        IReadOnlyList<string> Identifiers,
        string Message);

    private sealed record PersistedValidationResults(
        IReadOnlyList<AgentTuiValidationStageDto> Stages,
        AgentTuiSuitabilityDto Suitability);

    [GeneratedRegex(
        @"^[A-Za-z0-9][A-Za-z0-9._-]*/[A-Za-z0-9][A-Za-z0-9._:/-]*$",
        RegexOptions.CultureInvariant)]
    private static partial Regex ModelIdentifierRegex();

    [GeneratedRegex(
        @"^\s*(?:claude(?:[\s-]+code)?(?:\s+version)?\s+v?(?<version>[0-9]+(?:\.[0-9]+){1,3}(?:[-+][0-9A-Za-z.-]+)?)|v?(?<suffixVersion>[0-9]+(?:\.[0-9]+){1,3}(?:[-+][0-9A-Za-z.-]+)?)\s+\(claude code\))\s*$",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex ClaudeRunnerVersionRegex();

    [GeneratedRegex(
        @"^\s*codex(?:-cli)?(?:\s+version)?\s+v?(?<version>[0-9]+(?:\.[0-9]+){1,3}(?:[-+][0-9A-Za-z.-]+)?)\s*$",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex CodexRunnerVersionRegex();

    [GeneratedRegex(
        @"^\s*(?:opencode(?:\s+version)?\s+v?)?(?<version>[0-9]+(?:\.[0-9]+){1,3}(?:[-+][0-9A-Za-z.-]+)?)\s*$",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex OpenCodeRunnerVersionRegex();

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
        foreach (var model in persistedModels
                     .OrderBy(model => model.Source)
                     .ThenBy(model => model.CreatedAt)
                     .ThenBy(model => model.Identifier, StringComparer.Ordinal))
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

        return merged.Values.ToArray();
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

    private IReadOnlyList<AgentTuiModel> AddOperatorModels(
        AgentTuiProfile profile,
        IReadOnlyList<AgentTuiModelWriteDto> models,
        DateTime now)
    {
        var added = new List<AgentTuiModel>();
        foreach (var model in models
                     .GroupBy(candidate => candidate.Identifier, StringComparer.Ordinal)
                     .Select(group => group.Last()))
        {
            var entity = new AgentTuiModel
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
            };
            _db.AgentTuiModels.Add(entity);
            added.Add(entity);
        }
        return added;
    }

    private void ReconcileOperatorModels(
        AgentTuiProfile profile,
        IReadOnlyList<AgentTuiModelWriteDto> models,
        DateTime now)
    {
        var requested = models
            .GroupBy(candidate => candidate.Identifier, StringComparer.Ordinal)
            .Select(group => group.Last())
            .ToArray();
        var requestedIdentifiers = requested
            .Select(model => model.Identifier)
            .ToHashSet(StringComparer.Ordinal);
        _db.AgentTuiModels.RemoveRange(profile.Models.Where(model =>
            model.Source == AgentTuiModelSource.Operator
            && !requestedIdentifiers.Contains(model.Identifier)));

        foreach (var model in requested)
        {
            var existing = profile.Models.SingleOrDefault(candidate =>
                string.Equals(candidate.Identifier, model.Identifier, StringComparison.Ordinal));
            if (existing is null)
            {
                AddOperatorModels(profile, [model], now);
                continue;
            }

            var hasDiscoveryEvidence = HasDiscoveryEvidence(existing);
            existing.DisplayName = model.DisplayName;
            existing.Family = NullIfWhiteSpace(model.Family);
            existing.Source = AgentTuiModelSource.Operator;
            existing.Availability = hasDiscoveryEvidence
                ? AgentTuiModelAvailability.Stale
                : AgentTuiModelAvailability.Unverified;
            existing.IsSuggestedDefault = model.IsSuggestedDefault;
            existing.UpdatedAt = now;
        }
    }

    private static bool IsRevisionBoundModel(AgentTuiModel model) =>
        model.Source == AgentTuiModelSource.Discovered || HasDiscoveryEvidence(model);

    private static bool HasDiscoveryEvidence(AgentTuiModel model) =>
        model.DiscoveredAt.HasValue
        || !string.IsNullOrEmpty(model.RunnerVersion)
        || model.Availability is AgentTuiModelAvailability.Verified or AgentTuiModelAvailability.Stale;

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
