using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using Antiphon.SessionRunner.Contracts;

namespace Antiphon.SessionRunner;

/// <summary>
/// CARD-0679 R5 repair 2 (review 18f52a40): the newest launch generation this runner accepted for
/// each session id, written before the process starts. The session table forgets a session on
/// <c>ReleaseSlot</c> and on a runner restart; this watermark does not, so a pre-ack re-send that
/// arrives after either is refused instead of starting the same generation twice.
/// <para>With a directory, one file per session id survives a restart; without one (tests, a runner
/// that is not configured for it) the watermark survives a release only.</para>
/// </summary>
public sealed class PhoneHomeLaunchGenerationStore
{
    /// <summary>
    /// An entry older than this cannot fence anything: a desktop re-sends a lost Launch within its
    /// transport retry budget (minutes), and every later launch of the same id carries a newer generation.
    /// </summary>
    public static readonly TimeSpan Retention = TimeSpan.FromDays(7);

    private const string Extension = ".generation";
    private readonly string? _directory;
    private readonly ConcurrentDictionary<Guid, DateTime> _accepted = new();

    public PhoneHomeLaunchGenerationStore(string? directory)
    {
        _directory = string.IsNullOrWhiteSpace(directory) ? null : directory;
        if (_directory is null)
            return;
        Directory.CreateDirectory(_directory);
        Prune(DateTime.UtcNow - Retention);
    }

    /// <summary>The accepted watermark for <paramref name="sessionId"/>, or null when none was recorded.</summary>
    public DateTime? Read(Guid sessionId)
    {
        if (_accepted.TryGetValue(sessionId, out var cached))
            return cached;
        if (_directory is null)
            return null;
        var path = PathFor(sessionId);
        if (!File.Exists(path))
            return null;
        var text = File.ReadAllText(path).Trim();
        if (!DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var stored))
            throw new InvalidDataException($"Launch generation watermark '{path}' is unreadable.");
        return _accepted.GetOrAdd(sessionId, SessionGeneration.Normalize(stored));
    }

    /// <summary>
    /// Raises the watermark to <paramref name="generation"/> and makes it durable before returning.
    /// A write that fails throws, so the caller never starts a process the fence does not know about.
    /// </summary>
    public void Record(Guid sessionId, DateTime generation)
    {
        var normalized = SessionGeneration.Normalize(generation);
        if (Read(sessionId) is { } current && SessionGeneration.Compare(current, normalized) >= 0)
            return;
        if (_directory is not null)
        {
            var path = PathFor(sessionId);
            var tmp = path + ".tmp";
            using (var stream = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                stream.Write(Encoding.UTF8.GetBytes(normalized.ToString("O", CultureInfo.InvariantCulture)));
                stream.Flush(flushToDisk: true);
            }

            File.Move(tmp, path, overwrite: true);
        }

        _accepted[sessionId] = normalized;
    }

    private string PathFor(Guid sessionId) => Path.Combine(_directory!, sessionId.ToString("N") + Extension);

    private void Prune(DateTime cutoffUtc)
    {
        foreach (var file in Directory.EnumerateFiles(_directory!, "*" + Extension))
        {
            try
            {
                if (File.GetLastWriteTimeUtc(file) < cutoffUtc)
                    File.Delete(file);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A stale watermark that cannot be deleted only fences generations nobody re-sends.
            }
        }
    }
}
