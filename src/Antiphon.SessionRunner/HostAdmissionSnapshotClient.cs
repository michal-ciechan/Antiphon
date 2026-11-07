using System.Text.Json;
using Antiphon.SessionRunner.Contracts;

namespace Antiphon.SessionRunner;

/// <summary>External I/O seam for observing the actual admission authority, with no admission writes.</summary>
public interface IHostAdmissionSnapshotReader
{
    Task<HostAdmissionSnapshot> ReadAsync(CancellationToken ct);
}

public sealed class HostAdmissionSnapshotClient(HttpClient http, HostStatsSettings settings, TimeProvider time,
    Func<BuildSlotListing?> local) : IHostAdmissionSnapshotReader, IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private HostAdmissionSnapshot? _cached;
    private long _cachedAt;

    public async Task<HostAdmissionSnapshot> ReadAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (_cached is not null && time.GetElapsedTime(_cachedAt) < TimeSpan.FromSeconds(5)) return _cached;
            HostAdmissionSnapshot answer;
            if (settings.AuthorityEndpoint is null)
            {
                var listing = local();
                answer = listing is null || settings.PhysicalHostKey is null
                    ? HostAdmissionSnapshot.Unknown(settings.PhysicalHostKey)
                    : new(settings.AuthorityId ?? "in-process", settings.PhysicalHostKey, settings.BindingRevision ?? 1,
                        time.GetUtcNow(), "known", "Lease", listing.Occupied, listing.Budget, listing.Waiters.Count);
            }
            else
            {
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(3), time);
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, deadline.Token);
                try
                {
                    using var response = await http.GetAsync(settings.AuthorityEndpoint, HttpCompletionOption.ResponseHeadersRead, linked.Token);
                    response.EnsureSuccessStatusCode();
                    if (response.Content.Headers.ContentLength > 65536) throw new InvalidDataException();
                    await using var stream = await response.Content.ReadAsStreamAsync(linked.Token);
                    using var body = new MemoryStream();
                    var buffer = new byte[4096];
                    int read;
                    while ((read = await stream.ReadAsync(buffer, linked.Token)) != 0)
                    {
                        if (body.Length + read > 65536) throw new InvalidDataException();
                        body.Write(buffer, 0, read);
                    }
                    answer = JsonSerializer.Deserialize<HostAdmissionSnapshot>(body.ToArray(), PhoneHomeFraming.Json)
                        ?? HostAdmissionSnapshot.Unknown();
                }
                catch (Exception ex) when (ex is HttpRequestException or IOException or JsonException
                    or OperationCanceledException or NotSupportedException)
                {
                    ct.ThrowIfCancellationRequested();
                    answer = HostAdmissionSnapshot.Unknown(settings.PhysicalHostKey);
                }
            }
            var age = answer.ObservedAt is { } at ? time.GetUtcNow() - at : (TimeSpan?)null;
            if (settings.PhysicalHostKey is null || answer.PhysicalHostKey != settings.PhysicalHostKey
                || string.IsNullOrWhiteSpace(answer.Id) || (settings.AuthorityId is not null && answer.Id != settings.AuthorityId)
                || answer.Revision is null or < 1 || answer.Occupied is null or < 0 || answer.Budget is null or < 1
                || answer.Waiters is null or < 0 || answer.Mode is not ("Lease" or "Load")
                || answer.State != "known" || age is null || age < TimeSpan.Zero)
                answer = HostAdmissionSnapshot.Unknown(settings.PhysicalHostKey);
            else if (age > TimeSpan.FromSeconds(15))
                answer = HostAdmissionSnapshot.Unknown(settings.PhysicalHostKey) with { State = "stale", ObservedAt = answer.ObservedAt };
            _cached = answer;
            _cachedAt = time.GetTimestamp();
            return answer;
        }
        finally { _gate.Release(); }
    }
    public void Dispose() => _gate.Dispose();
}
