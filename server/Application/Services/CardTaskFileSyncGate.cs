using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// Per-repository lock for CARD-0004 card-file sync. One <see cref="SemaphoreSlim"/> per
/// full-path-normalised repository path: the tick takes it with <c>WaitAsync(0)</c> and skips a
/// busy repo; the endpoint takes it the same way and answers 409 <c>card_file_sync_running</c>.
/// Boards of one project run sequentially under that one lock. Last skip-reason lives here so a
/// rebase that lasts ten ticks logs Warning once.
/// </summary>
public sealed class CardTaskFileSyncGate
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, string> _lastSkipReasons = new(StringComparer.OrdinalIgnoreCase);
    private readonly ILogger<CardTaskFileSyncGate> _logger;

    public CardTaskFileSyncGate(ILogger<CardTaskFileSyncGate>? logger = null)
    {
        _logger = logger ?? NullLogger<CardTaskFileSyncGate>.Instance;
    }

    /// <summary>
    /// Try to enter the lock for <paramref name="repositoryPath"/> without waiting. Null means
    /// another sync already holds it.
    /// </summary>
    public async ValueTask<IDisposable?> TryEnterAsync(string repositoryPath, CancellationToken ct = default)
    {
        var key = Normalize(repositoryPath);
        var sem = _locks.GetOrAdd(key, static _ => new SemaphoreSlim(1, 1));
        var entered = await sem.WaitAsync(0, ct).ConfigureAwait(false);
        return entered ? new Lease(sem) : null;
    }

    /// <summary>
    /// Remember the last skip reason for this repository. Returns true when the reason
    /// <em>changed</em> (S2 logs at Warning only then).
    /// </summary>
    public bool NoteSkipReason(string repositoryPath, string? reason)
    {
        var key = Normalize(repositoryPath);
        if (reason is null)
        {
            _lastSkipReasons.TryRemove(key, out _);
            return false;
        }

        var changed = true;
        _lastSkipReasons.AddOrUpdate(
            key,
            reason,
            (_, previous) =>
            {
                changed = !string.Equals(previous, reason, StringComparison.Ordinal);
                return reason;
            });

        if (changed)
            _logger.LogWarning("Card file sync skipped for {Repository}: {Reason}", key, reason);
        else
            _logger.LogDebug("Card file sync skipped for {Repository}: {Reason}", key, reason);

        return changed;
    }

    public string? LastSkipReason(string repositoryPath) =>
        _lastSkipReasons.TryGetValue(Normalize(repositoryPath), out var reason) ? reason : null;

    private static string Normalize(string repositoryPath) =>
        Path.GetFullPath(repositoryPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private sealed class Lease(SemaphoreSlim sem) : IDisposable
    {
        private SemaphoreSlim? _sem = sem;

        public void Dispose()
        {
            var s = Interlocked.Exchange(ref _sem, null);
            s?.Release();
        }
    }
}
