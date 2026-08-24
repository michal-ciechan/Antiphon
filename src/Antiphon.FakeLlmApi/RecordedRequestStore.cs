using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Antiphon.FakeLlmApi;

/// <summary>
/// Thread-safe in-memory request log. Optional JSONL sidecar truncates bodies and redacts
/// Authorization — memory stays the source of truth for assertions (FakeGateway DeliveryStore shape).
/// </summary>
public sealed class RecordedRequestStore
{
    public const int SidecarBodyTruncateBytes = 16 * 1024;

    private readonly object _gate = new();
    private readonly List<RecordedRequest> _requests = [];
    private readonly ConcurrentDictionary<string, byte> _waiters = new();
    private readonly string? _jsonlPath;
    private long _seq;
    private TaskCompletionSource _signal = NewSignal();

    public RecordedRequestStore(string? jsonlPath = null)
    {
        _jsonlPath = jsonlPath;
        if (jsonlPath is not null)
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(jsonlPath));
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
        }
    }

    public IReadOnlyList<RecordedRequest> All
    {
        get
        {
            lock (_gate)
                return _requests.ToList();
        }
    }

    public RecordedRequest Record(RecordedRequest request)
    {
        lock (_gate)
        {
            // Seq is assigned by the caller via NextSeq, or we stamp here if Seq==0.
            if (request.Seq == 0)
            {
                request = request with { Seq = ++_seq };
            }
            else
            {
                _seq = Math.Max(_seq, request.Seq);
            }

            _requests.Add(request);
            _signal.TrySetResult();
            _signal = NewSignal();
        }

        AppendSidecar(request);
        return request;
    }

    public long NextSeq()
    {
        lock (_gate)
            return ++_seq;
    }

    public IReadOnlyList<RecordedRequest> Query(Func<RecordedRequest, bool> predicate)
    {
        lock (_gate)
            return _requests.Where(predicate).ToList();
    }

    public int Reset()
    {
        lock (_gate)
        {
            var count = _requests.Count;
            _requests.Clear();
            _seq = 0;
            return count;
        }
    }

    /// <summary>
    /// Polls until a recorded request matches <paramref name="predicate"/> or the timeout elapses.
    /// Returns the first match, or null on timeout.
    /// </summary>
    public async Task<RecordedRequest?> WaitForAsync(
        Func<RecordedRequest, bool> predicate,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RecordedRequest? hit;
            Task wait;
            lock (_gate)
            {
                hit = _requests.FirstOrDefault(predicate);
                wait = _signal.Task;
            }

            if (hit is not null)
                return hit;

            var remaining = deadline - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero)
                return null;

            var delay = Task.Delay(remaining, cancellationToken);
            var completed = await Task.WhenAny(wait, delay).ConfigureAwait(false);
            if (completed == delay)
            {
                lock (_gate)
                    return _requests.FirstOrDefault(predicate);
            }
        }
    }

    private void AppendSidecar(RecordedRequest request)
    {
        if (_jsonlPath is null)
            return;

        try
        {
            var bodyBytes = Encoding.UTF8.GetBytes(request.Body ?? "");
            var truncated = bodyBytes.Length > SidecarBodyTruncateBytes;
            var bodyForSidecar = truncated
                ? Encoding.UTF8.GetString(bodyBytes, 0, SidecarBodyTruncateBytes)
                : request.Body;
            var bodySha = Convert.ToHexString(SHA256.HashData(bodyBytes)).ToLowerInvariant();

            var headers = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
            foreach (var (name, values) in request.Headers)
            {
                if (string.Equals(name, "Authorization", StringComparison.OrdinalIgnoreCase))
                {
                    var redacted = values.Select(v =>
                    {
                        var sha = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(v)))
                            .ToLowerInvariant();
                        return $"sha256:{sha}";
                    }).ToArray();
                    headers[name] = redacted;
                }
                else
                {
                    headers[name] = values;
                }
            }

            var row = new
            {
                request.Seq,
                Utc = request.UtcTimestamp,
                request.Method,
                request.Path,
                request.QueryString,
                Headers = headers,
                Body = bodyForSidecar,
                BodyByteLength = request.BodyByteLength,
                BodyTruncated = truncated,
                BodySha256 = bodySha,
                request.ListenPort,
            };

            File.AppendAllText(_jsonlPath, JsonSerializer.Serialize(row) + Environment.NewLine);
        }
        catch
        {
            // Sidecar is convenience only.
        }
    }

    private static TaskCompletionSource NewSignal()
        => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
