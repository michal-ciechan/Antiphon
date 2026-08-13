using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;

namespace Antiphon.Server.Application.Services;

public sealed partial class AgentTuiSecretIdempotencyCache
{
    private const int MaximumKeyLength = 200;
    private static readonly TimeSpan EntryLifetime = TimeSpan.FromMinutes(30);
    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    public async Task<IdempotentSecretResult> ExecuteAsync(
        string? idempotencyKey,
        Guid profileId,
        string environmentName,
        Func<Task<AgentTuiSecretMutationDto>> action,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
            return new IdempotentSecretResult(await action(), IsOriginalExecution: true);

        var key = idempotencyKey.Trim();
        ValidateKey(key);
        var cacheKey = $"{profileId:D}\u001f{environmentName}\u001f{key}";
        var created = false;
        var entry = _entries.GetOrAdd(cacheKey, _ =>
        {
            created = true;
            return new Entry();
        });

        if (!created)
        {
            return new IdempotentSecretResult(
                await entry.Completion.Task.WaitAsync(cancellationToken),
                IsOriginalExecution: false);
        }

        try
        {
            var result = await action();
            entry.Completion.TrySetResult(result);
            ScheduleCleanup(cacheKey);
            return new IdempotentSecretResult(result, IsOriginalExecution: true);
        }
        catch (Exception exception)
        {
            _entries.TryRemove(cacheKey, out _);
            entry.Completion.TrySetException(exception);
            throw;
        }
    }

    public readonly record struct IdempotentSecretResult(
        AgentTuiSecretMutationDto Result,
        bool IsOriginalExecution);

    private void ScheduleCleanup(string cacheKey)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(EntryLifetime);
            }
            catch
            {
                // Best-effort cleanup only.
            }

            _entries.TryRemove(cacheKey, out _);
        });
    }

    private static void ValidateKey(string key)
    {
        if (key.Length is 0 or > MaximumKeyLength || !IdempotencyKeyRegex().IsMatch(key))
        {
            throw new ValidationException(
                "Idempotency-Key",
                "Idempotency-Key must be 1-200 characters of letters, digits, '.', '_', ':', or '-'.",
                "invalid_idempotency_key");
        }
    }

    [GeneratedRegex(@"^[A-Za-z0-9._:-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex IdempotencyKeyRegex();

    private sealed class Entry
    {
        public TaskCompletionSource<AgentTuiSecretMutationDto> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
