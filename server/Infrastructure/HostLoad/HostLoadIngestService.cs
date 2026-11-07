using System.Linq.Expressions;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Infrastructure.HostLoad;

public sealed record HostLoadIngestBatch(string RunnerId, string? After, HostLoadHistoryPage Page);
public sealed record HostLoadIngestReceipt(int Inserted, int Duplicates, string? CommittedCursor);

/// <summary>One bounded transaction commits K, loss ranges and the cursor. Derived progress is separate.</summary>
public sealed class HostLoadIngestService(AppDbContext db, IOptions<HostLoadHistorySettings> options,
    HostLoadCounterCache counters, TimeProvider time)
{
    public async Task<HostLoadIngestReceipt> IngestAsync(HostLoadIngestBatch batch, CancellationToken ct)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(3), time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, deadline.Token);
        var token = linked.Token;
        Validate(batch);
        var runnerId = HostLoadCounterCache.Canonical(batch.RunnerId);
        var unique = new Dictionary<(Guid Store, Guid Boot, long Seq), (HostLoadSample Sample, string Payload, string Hash)>();
        foreach (var sample in batch.Page.Samples)
        {
            var payload = CanonicalPayload(sample); var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
            var key = (sample.Source.RunnerStoreId, sample.Source.BootId, sample.Source.Sequence);
            if (unique.TryGetValue(key, out var seen) && seen.Hash != hash) throw Conflict();
            unique[key] = (sample, payload, hash);
        }
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtext({"host-load:" + runnerId}))", token);
        var cursor = await db.HostLoadIngestCursors.SingleOrDefaultAsync(c => c.RunnerId == runnerId, token);
        if (cursor is not null && cursor.RunnerStoreId != batch.Page.Source.RunnerStoreId) throw Conflict();
        var predicate = IdentityPredicate(unique.Keys);
        var existing = await db.HostLoadSampleIdentities.Where(predicate).ToListAsync(token);
        var byKey = existing.ToDictionary(i => (i.RunnerStoreId, i.BootId, i.Sequence));
        var inserted = 0; var duplicates = 0;
        foreach (var (key, value) in unique)
        {
            if (byKey.TryGetValue(key, out var previous))
            { if (previous.PayloadHash != value.Hash) throw Conflict(); duplicates++; continue; }
            // A stale optimistic page cannot advance a recipient that has committed another frontier.
            if (cursor?.Cursor != batch.After) throw new ConflictException("History page is no longer at the committed frontier.", "host_load_frontier_changed");
            var binding = Binding(runnerId, value.Sample);
            db.HostLoadSampleIdentities.Add(new() { RunnerStoreId = key.Store, BootId = key.Boot, Sequence = key.Seq,
                PayloadHash = value.Hash, IngestedAt = time.GetUtcNow() });
            db.HostLoadSamples.Add(new() { RunnerStoreId = key.Store, BootId = key.Boot, Sequence = key.Seq,
                RunnerId = runnerId, SampledAt = value.Sample.SampledAt, IngestedAt = time.GetUtcNow(),
                PhysicalHostKey = binding?.PhysicalHostKey, SourceEpoch = binding?.SourceEpoch,
                IsPrimary = binding is { Primary: true } && IsSolePrimary(binding, value.Sample.SampledAt),
                PayloadJson = value.Payload, CountersJson = JsonSerializer.Serialize(counters.Nearest(runnerId, value.Sample.SampledAt), PhoneHomeFraming.Json) });
            inserted++;
        }
        if (cursor is null)
        {
            cursor = new() { RunnerId = runnerId, RunnerStoreId = batch.Page.Source.RunnerStoreId };
            db.HostLoadIngestCursors.Add(cursor);
        }
        if (cursor.Cursor == batch.After)
        {
            cursor.Cursor = batch.Page.NextCursor ?? cursor.Cursor;
            cursor.GapsJson = JsonSerializer.Serialize(batch.Page.Gaps, PhoneHomeFraming.Json);
            cursor.Complete = batch.Page.Complete; cursor.CommittedAt = time.GetUtcNow();
        }
        await db.SaveChangesAsync(token);
        await transaction.CommitAsync(token);
        return new(inserted, duplicates, cursor.Cursor);
    }

    private HostLoadBinding? Binding(string runnerId, HostLoadSample sample)
    {
        var matches = options.Value.Bindings.Where(b => HostLoadCounterCache.Canonical(b.RunnerId) == runnerId
            && sample.SampledAt >= b.From && (b.To is null || sample.SampledAt < b.To)
            && b.PhysicalHostKey == sample.PhysicalHostKey && b.Revision == sample.BindingRevision
            && b.SourceEpoch == sample.SourceEpoch).ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }
    private bool IsSolePrimary(HostLoadBinding binding, DateTimeOffset at) => options.Value.Bindings.Count(b => b.Primary
        && b.PhysicalHostKey == binding.PhysicalHostKey && b.SourceEpoch == binding.SourceEpoch && at >= b.From && (b.To is null || at < b.To)) == 1;

    public static void Validate(HostLoadIngestBatch batch)
    {
        var page = batch.Page;
        if (page.SchemaVersion != 1 || page.Source.RunnerStoreId == Guid.Empty || page.Samples.Count > 120
            || JsonSerializer.SerializeToUtf8Bytes(page, PhoneHomeFraming.Json).Length > 1048576)
            throw new ValidationException("history", "Invalid history schema or batch bounds.");
        foreach (var sample in page.Samples)
        {
            if (sample.SchemaVersion != 1 || sample.Source.RunnerStoreId != page.Source.RunnerStoreId || sample.Source.BootId == Guid.Empty
                || sample.Source.Sequence <= 0 || sample.IntervalSeconds != 5 || sample.SampledAt.Offset != TimeSpan.Zero
                || HostLoadCounterCache.Canonical(sample.Source.RunnerId) != HostLoadCounterCache.Canonical(batch.RunnerId)
                || sample.CpuPercent is < 0 or > 100 || sample.Processes.Any(p => p.Name is not ("runner" or "session" or "pty-host")))
                throw new ValidationException("history", "Invalid host sample.");
        }
        if (page.Gaps.Any(g => g.RunnerStoreId != page.Source.RunnerStoreId || g.FromSequence <= 0 || g.ToSequence < g.FromSequence))
            throw new ValidationException("history", "Invalid loss range.");
    }
    private static Expression<Func<HostLoadSampleIdentity, bool>> IdentityPredicate(IEnumerable<(Guid Store, Guid Boot, long Seq)> keys)
    {
        var parameter = Expression.Parameter(typeof(HostLoadSampleIdentity), "identity");
        Expression expression = Expression.Constant(false);
        foreach (var key in keys)
        {
            var part = Expression.AndAlso(Expression.Equal(Expression.Property(parameter, nameof(HostLoadSampleIdentity.RunnerStoreId)), Expression.Constant(key.Store)),
                Expression.AndAlso(Expression.Equal(Expression.Property(parameter, nameof(HostLoadSampleIdentity.BootId)), Expression.Constant(key.Boot)),
                    Expression.Equal(Expression.Property(parameter, nameof(HostLoadSampleIdentity.Sequence)), Expression.Constant(key.Seq))));
            expression = Expression.OrElse(expression, part);
        }
        return Expression.Lambda<Func<HostLoadSampleIdentity, bool>>(expression, parameter);
    }
    private static ConflictException Conflict() => new("Host load identity has conflicting content.", "host_load_identity_conflict");
    private static string CanonicalPayload(HostLoadSample sample)
    {
        var element = JsonSerializer.SerializeToElement(sample, PhoneHomeFraming.Json);
        using var buffer = new MemoryStream(); using (var writer = new Utf8JsonWriter(buffer)) WriteCanonical(writer, element);
        return Encoding.UTF8.GetString(buffer.ToArray());
    }
    private static void WriteCanonical(Utf8JsonWriter writer, JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        { writer.WriteStartObject(); foreach (var property in element.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal))
            { writer.WritePropertyName(property.Name); WriteCanonical(writer, property.Value); } writer.WriteEndObject(); }
        else if (element.ValueKind == JsonValueKind.Array)
        { writer.WriteStartArray(); foreach (var item in element.EnumerateArray()) WriteCanonical(writer, item); writer.WriteEndArray(); }
        else element.WriteTo(writer);
    }
}
