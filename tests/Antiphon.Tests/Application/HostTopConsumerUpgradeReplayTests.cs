using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Infrastructure.HostLoad;
using Antiphon.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>CARD-1884. A pre-upgrade sample keeps its stored identity through the ingest queue and spool recovery.</summary>
[Category("Integration"), NotInParallel("C1514HostLoadServer"), ParallelLimiter<ProcessSpawnLimit>]
[ClassDataSource<C1514HostLoadFixture>(Shared = SharedType.PerTestSession)]
public class HostTopConsumerUpgradeReplayTests(C1514HostLoadFixture fixture)
{
    [Before(Class)]
    public static Task WarmDatabase() => TestDbFixture.Lifecycle.EnsureReadyAsync();

    [Test, Timeout(30_000)]
    public async Task Pre_upgrade_replay_keeps_persisted_identity()
    {
        await using var world = await fixture.WorldAsync();
        var produced = await world.ProduceAsync(C1514HostLoadWorld.T0);
        var hash = Sha(PreUpgradeCanonical(produced));
        await using (var db = world.Db())
        {
            db.HostLoadSampleIdentities.Add(new HostLoadSampleIdentity
            {
                RunnerStoreId = produced.Source.RunnerStoreId,
                BootId = produced.Source.BootId,
                Sequence = produced.Source.Sequence,
                PayloadHash = hash,
                IngestedAt = C1514HostLoadWorld.T0,
            });
            await db.SaveChangesAsync();
        }

        var page = world.History.ReadHistory(null, 120);
        world.Pump.TryEnqueue(new HostLoadIngestBatch("grok-linux", null, page)).ShouldBeTrue();
        var replay = await world.Pump.ProcessOneAsync(CancellationToken.None);
        replay.Inserted.ShouldBe(0);
        replay.Duplicates.ShouldBe(page.Samples.Count);

        RewriteChunksWithoutTopConsumers(world.Runner.HistoryPath!);
        var recovered = new HostLoadSpool(world.Runner.HistoryPath!, world.Runner, world.Clock, world.Spool.BootId);
        var recoveredPage = recovered.ReadHistory(null, 120);
        var recoveredSample = recoveredPage.Samples.Single(s => s.Source.Sequence == produced.Source.Sequence);
        recoveredSample.TopConsumers.ShouldBeEmpty();
        world.Pump.TryEnqueue(new HostLoadIngestBatch("grok-linux", replay.CommittedCursor, recoveredPage)).ShouldBeTrue();
        var second = await world.Pump.ProcessOneAsync(CancellationToken.None);
        second.Inserted.ShouldBe(0);
        second.Duplicates.ShouldBeGreaterThan(0);
    }

    [Test, Timeout(30_000)]
    public async Task Different_top_consumers_still_conflict()
    {
        await using var world = await fixture.WorldAsync();
        var produced = await world.ProduceAsync(C1514HostLoadWorld.T0);
        var hash = Sha(PreUpgradeCanonical(produced));
        await using (var db = world.Db())
        {
            db.HostLoadSampleIdentities.Add(new HostLoadSampleIdentity
            {
                RunnerStoreId = produced.Source.RunnerStoreId,
                BootId = produced.Source.BootId,
                Sequence = produced.Source.Sequence,
                PayloadHash = hash,
                IngestedAt = C1514HostLoadWorld.T0,
            });
            await db.SaveChangesAsync();
        }

        var changed = produced with
        {
            TopConsumers = [new HostLoadTopConsumer(4242, 10, "VBCSCompiler", "compiler", 5.3, 409600)],
        };
        var page = new HostLoadHistoryPage(1, changed.Source, [changed], [], null, false, true);
        world.Pump.TryEnqueue(new HostLoadIngestBatch("grok-linux", null, page)).ShouldBeTrue();
        await Should.ThrowAsync<ConflictException>(() => world.Pump.ProcessOneAsync(CancellationToken.None));
        await using var fresh = world.Db(false);
        (await fresh.HostLoadSamples.CountAsync(s => s.Sequence == produced.Source.Sequence)).ShouldBe(0);
    }

    [Test, Timeout(30_000)]
    public async Task Eight_top_consumers_round_trip_through_spool_recovery()
    {
        await using var world = await fixture.WorldAsync();
        var rows = new List<HostTopConsumerSample>(9);
        for (var i = 0; i < 9; i++)
            rows.Add(new HostTopConsumerSample(i + 1, 100, "pwsh", HostConsumerRules.Other, 9 - i, 4096L * (i + 1)));
        var host = new HostSample(C1514HostLoadWorld.T0, 40, 24, null, null, null, null, null, null, null, [], null, [])
        {
            TopConsumers = rows,
        };
        var projected = HostLoadProjection.Create(host, world.Spool.NextSource(), world.Runner, HostAdmissionSnapshot.Unknown());
        projected.TopConsumers.Count.ShouldBe(HostConsumerRules.MaxCount);
        projected.TopConsumers.Select(row => row.Pid).ShouldBe(Enumerable.Range(1, 8).ToArray());
        world.Spool.Append(projected);

        var recovered = new HostLoadSpool(world.Runner.HistoryPath!, world.Runner, world.Clock, world.Spool.BootId);
        var round = recovered.ReadHistory(null, 120).Samples.Single(s => s.Source.Sequence == projected.Source.Sequence);
        round.TopConsumers.Count.ShouldBe(8);
        round.TopConsumers.Select(row => row.Pid).ShouldBe(projected.TopConsumers.Select(row => row.Pid));
        round.TopConsumers.Select(row => row.CpuCores).ShouldBe(projected.TopConsumers.Select(row => row.CpuCores));
        round.TopConsumers.Select(row => row.RssBytes).ShouldBe(projected.TopConsumers.Select(row => row.RssBytes));

        var page = recovered.ReadHistory(null, 120);
        world.Pump.TryEnqueue(new HostLoadIngestBatch("grok-linux", null, page)).ShouldBeTrue();
        var receipt = await world.Pump.ProcessOneAsync(CancellationToken.None);
        receipt.Inserted.ShouldBe(1);
        await using var db = world.Db(false);
        var stored = JsonSerializer.Deserialize<HostLoadSample>(
            (await db.HostLoadSamples.SingleAsync(s => s.Sequence == projected.Source.Sequence)).PayloadJson,
            PhoneHomeFraming.Json);
        stored.ShouldNotBeNull();
        stored.TopConsumers.Count.ShouldBe(8);
        stored.TopConsumers.Select(row => row.Pid).ShouldBe(projected.TopConsumers.Select(row => row.Pid));
        stored.TopConsumers.Any(row => row.Pid == 9).ShouldBeFalse();
    }

    private static string PreUpgradeCanonical(HostLoadSample sample)
    {
        var server = ServerCanonical(sample);
        Canonicalize(JsonSerializer.SerializeToElement(sample, PhoneHomeFraming.Json)).ShouldBe(server);
        var absent = JsonNode.Parse(server)!.AsObject();
        absent.Remove("topConsumers");
        return Canonicalize(JsonSerializer.SerializeToElement(absent, PhoneHomeFraming.Json));
    }

    private static string ServerCanonical(HostLoadSample sample)
    {
        var method = typeof(HostLoadIngestService).GetMethod("CanonicalPayload", BindingFlags.NonPublic | BindingFlags.Static);
        method.ShouldNotBeNull();
        return (string)method.Invoke(null, [sample])!;
    }

    private static void RewriteChunksWithoutTopConsumers(string root)
    {
        foreach (var file in Directory.GetFiles(root, "chunk-*.jsonl"))
        {
            var lines = File.ReadAllLines(file);
            var rewritten = new string[lines.Length];
            for (var i = 0; i < lines.Length; i++)
            {
                if (lines[i].Length == 0)
                {
                    rewritten[i] = lines[i];
                    continue;
                }

                var entry = JsonNode.Parse(lines[i])!.AsObject();
                if (entry["sample"] is JsonObject sample)
                    sample.Remove("topConsumers");
                rewritten[i] = entry.ToJsonString(PhoneHomeFraming.Json);
            }

            File.WriteAllText(file, string.Join('\n', rewritten) + "\n");
        }
    }

    private static string Sha(string payload) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));

    private static string Canonicalize(JsonElement element)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
            WriteCanonical(writer, element);
        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static void WriteCanonical(Utf8JsonWriter writer, JsonElement element, bool omitEmptyTopConsumers = true)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            writer.WriteStartObject();
            foreach (var property in element.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal))
            {
                if (omitEmptyTopConsumers && property.NameEquals("topConsumers"u8)
                    && (property.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined
                        || (property.Value.ValueKind == JsonValueKind.Array && property.Value.GetArrayLength() == 0)))
                    continue;
                writer.WritePropertyName(property.Name);
                WriteCanonical(writer, property.Value, omitEmptyTopConsumers: false);
            }

            writer.WriteEndObject();
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            writer.WriteStartArray();
            foreach (var item in element.EnumerateArray())
                WriteCanonical(writer, item, omitEmptyTopConsumers: false);
            writer.WriteEndArray();
        }
        else
        {
            element.WriteTo(writer);
        }
    }
}
