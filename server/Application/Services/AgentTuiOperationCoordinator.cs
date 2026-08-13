using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Services;

public sealed class AgentTuiOperationCoordinator
{
    private const int OperationReserveMilliseconds = 850;
    private const int MinimumOperationMilliseconds = 150;
    private static readonly TimeSpan RecoveryTimeout = TimeSpan.FromMilliseconds(300);
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeSpan _operationTimeout;
    private readonly ConcurrentDictionary<OperationKey, Lazy<Task<object>>> _operations = new();
    private readonly ConcurrentDictionary<OperationKey, Guid> _activeRuns = new();

    public AgentTuiOperationCoordinator(
        IServiceScopeFactory scopeFactory,
        IOptions<AgentTuiSettings> settings)
    {
        _scopeFactory = scopeFactory;
        var timeoutSeconds = settings.Value.ProbeTimeoutSeconds;
        if (timeoutSeconds is <= 0 or > AgentTuiSettings.MaximumProbeTimeoutSeconds)
        {
            throw new OptionsValidationException(
                nameof(AgentTuiSettings),
                typeof(AgentTuiSettings),
                [$"ProbeTimeoutSeconds must be between 1 and {AgentTuiSettings.MaximumProbeTimeoutSeconds}."]);
        }

        _operationTimeout = TimeSpan.FromMilliseconds(Math.Max(
            MinimumOperationMilliseconds,
            (timeoutSeconds * 1000) - OperationReserveMilliseconds));
    }

    public async Task<IReadOnlyList<AgentTuiModelDto>> RefreshModelsAsync(
        Guid profileId,
        CancellationToken cancellationToken)
    {
        var result = await JoinAsync(
            new OperationKey(profileId, AgentTuiOperation.Discovery),
            cancellationToken);
        return (IReadOnlyList<AgentTuiModelDto>)result;
    }

    public async Task<AgentTuiValidationRunDto> ValidateAsync(
        Guid profileId,
        CancellationToken cancellationToken)
    {
        var result = await JoinAsync(
            new OperationKey(profileId, AgentTuiOperation.Validation),
            cancellationToken);
        return (AgentTuiValidationRunDto)result;
    }

    private async Task<object> JoinAsync(
        OperationKey key,
        CancellationToken cancellationToken)
    {
        var operation = _operations.GetOrAdd(
            key,
            operationKey => new Lazy<Task<object>>(
                () => RunAsync(operationKey),
                LazyThreadSafetyMode.ExecutionAndPublication));
        return await operation.Value.WaitAsync(cancellationToken);
    }

    private async Task<object> RunAsync(OperationKey key)
    {
        using var operationLifetime = new CancellationTokenSource(_operationTimeout);
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var service = scope.ServiceProvider.GetRequiredService<AgentTuiProfileService>();
            return key.Operation switch
            {
                AgentTuiOperation.Discovery =>
                    await service.RefreshModelsCoreAsync(key.ProfileId, operationLifetime.Token),
                AgentTuiOperation.Validation =>
                    await service.ValidateCoreAsync(key.ProfileId, operationLifetime.Token),
                _ => throw new ArgumentOutOfRangeException(nameof(key.Operation))
            };
        }
        catch (OperationCanceledException exception) when (operationLifetime.IsCancellationRequested)
        {
            return await RecoverOrRethrowAsync(
                key,
                AgentTuiValidationStatus.TimedOut,
                "The bounded operation reached its deadline.",
                exception);
        }
        catch (Exception exception)
        {
            return await RecoverOrRethrowAsync(
                key,
                AgentTuiValidationStatus.Failed,
                "The bounded operation failed safely.",
                exception);
        }
        finally
        {
            _activeRuns.TryRemove(key, out _);
            _operations.TryRemove(key, out _);
        }
    }

    internal void RegisterRun(Guid profileId, string operation, Guid runId)
    {
        var key = new OperationKey(profileId, ParseOperation(operation));
        if (!_activeRuns.TryAdd(key, runId))
            throw new InvalidOperationException("An active Agent TUI run is already registered for this operation.");
    }

    internal bool IsRunActive(Guid profileId, string operation, Guid runId)
    {
        if (!TryParseOperation(operation, out var parsed))
            return false;
        return _activeRuns.TryGetValue(new OperationKey(profileId, parsed), out var activeRunId)
               && activeRunId == runId;
    }

    private async Task<object?> RecoverAsync(
        OperationKey key,
        AgentTuiValidationStatus status,
        string summary)
    {
        if (!_activeRuns.TryGetValue(key, out var runId))
            return null;

        const int maximumAttempts = 3;
        var startedAt = Stopwatch.GetTimestamp();
        Exception? lastFailure = null;
        for (var attempt = 0; attempt < maximumAttempts; attempt++)
        {
            var elapsed = Stopwatch.GetElapsedTime(startedAt);
            var remaining = RecoveryTimeout - elapsed;
            if (remaining <= TimeSpan.Zero)
                break;
            var attemptsRemaining = maximumAttempts - attempt;
            var attemptTimeout = remaining / attemptsRemaining;
            using var finalization = new CancellationTokenSource(attemptTimeout);
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var service = scope.ServiceProvider.GetRequiredService<AgentTuiProfileService>();
                var run = await service.FinalizeIncompleteOperationCoreAsync(
                    runId,
                    status,
                    summary,
                    finalization.Token);
                if (run is null)
                    return null;
                return key.Operation == AgentTuiOperation.Discovery
                    ? await service.GetModelsAsync(key.ProfileId, finalization.Token)
                    : run;
            }
            catch (Exception exception)
            {
                lastFailure = exception;
            }
        }

        if (lastFailure is not null)
            ExceptionDispatchInfo.Capture(lastFailure).Throw();
        return null;
    }

    private async Task<object> RecoverOrRethrowAsync(
        OperationKey key,
        AgentTuiValidationStatus status,
        string summary,
        Exception originalException)
    {
        try
        {
            var recovered = await RecoverAsync(key, status, summary);
            if (recovered is not null)
                return recovered;
        }
        catch
        {
            // Preserve the authoritative operation failure when bounded recovery is unavailable.
        }

        ExceptionDispatchInfo.Capture(originalException).Throw();
        throw new System.Diagnostics.UnreachableException();
    }

    private readonly record struct OperationKey(Guid ProfileId, AgentTuiOperation Operation);

    private static AgentTuiOperation ParseOperation(string operation) =>
        TryParseOperation(operation, out var parsed)
            ? parsed
            : throw new ArgumentOutOfRangeException(nameof(operation));

    private static bool TryParseOperation(string operation, out AgentTuiOperation parsed)
    {
        switch (operation)
        {
            case "discovery":
                parsed = AgentTuiOperation.Discovery;
                return true;
            case "validation":
                parsed = AgentTuiOperation.Validation;
                return true;
            default:
                parsed = default;
                return false;
        }
    }

    private enum AgentTuiOperation
    {
        Discovery,
        Validation
    }
}
