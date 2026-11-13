using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Infrastructure.HostLoad;
using Antiphon.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>CARD-1884. A pre-upgrade sample keeps its stored identity through the ingest queue and spool recovery.</summary>
[Category("Integration"), NotInParallel("C1514HostLoadServer"), ParallelLimiter<ProcessSpawnLimit>]
[ClassDataSource<C1514HostLoadFixture>(Shared = SharedType.PerTestSession)]
public class HostTopConsumerUpgradeReplayTests(C1514HostLoadFixture fixture)
{
    // Canonical bytes and SHA-256 from commit 3dc5e5c335c9ea0e3d622b042ebf809584ce0117.
    // Produced by that commit's HostLoadSample, PhoneHomeFraming.Json and CanonicalPayload.
    // The \u002B in sampledAt is the serializer's escaped plus, kept literal in this raw string.
    private const string HistoricalCanonical = """{"authority":{"activeCommands":null,"budget":null,"hardCeiling":null,"id":null,"mode":"unknown","observedAt":null,"occupied":null,"physicalHostKey":null,"revision":null,"state":"unknown","waiters":null},"bindingRevision":null,"busyCoreEquivalent":null,"cpuPercent":10,"effectiveCoreLimit":null,"hostLogicalCores":null,"intervalSeconds":5,"ioWaitPercent":null,"load1":null,"load15":null,"load5":null,"loadPerCore":null,"memory":{"availableBytes":null,"totalBytes":null,"usedBytes":null},"metricScope":"unknown","physicalHostKey":null,"processCount":null,"processes":[],"sampledAt":"2026-11-06T12:00:00\u002B00:00","schemaVersion":1,"source":{"bootId":"22222222-2222-2222-2222-222222222222","runnerId":"grok-linux","runnerStoreId":"11111111-1111-1111-1111-111111111111","sequence":7},"sourceEpoch":null,"swap":{"freeBytes":null,"meaning":"swap","totalBytes":null,"usedBytes":null},"unknownReasons":{},"volumes":[]}""";
    private const string HistoricalHash = "6A8AD0083C7D103214E68970D57412E03594BA35108BDE20A10F578772FC77A9";

    [Before(Class)]
    public static Task WarmDatabase() => TestDbFixture.Lifecycle.EnsureReadyAsync();

    [Test, Timeout(30_000)]
    public async Task Pre_upgrade_replay_keeps_persisted_identity()
    {
        var sample = JsonSerializer.Deserialize<HostLoadSample>(HistoricalCanonical, PhoneHomeFraming.Json);
        sample.ShouldNotBeNull();
        sample.TopConsumers.ShouldBeEmpty();
        Sha(HistoricalCanonical).ShouldBe(HistoricalHash);
        ServerCanonical(sample).ShouldBe(HistoricalCanonical);

        await using var world = await fixture.WorldAsync();
        await using (var db = world.Db())
        {
            db.HostLoadSampleIdentities.Add(new HostLoadSampleIdentity
            {
                RunnerStoreId = sample.Source.RunnerStoreId,
                BootId = sample.Source.BootId,
                Sequence = sample.Source.Sequence,
                PayloadHash = HistoricalHash,
                IngestedAt = C1514HostLoadWorld.T0,
            });
            await db.SaveChangesAsync();
        }

        world.Pump.TryEnqueue(Page(sample)).ShouldBeTrue();
        var replay = await world.Pump.ProcessOneAsync(CancellationToken.None);
        replay.Inserted.ShouldBe(0);
        replay.Duplicates.ShouldBe(1);
        await using var fresh = world.Db(false);
        (await fresh.HostLoadSamples.CountAsync(s => s.Sequence == sample.Source.Sequence)).ShouldBe(0);
    }

    [Test, Timeout(30_000)]
    public async Task Different_top_consumers_still_conflict()
    {
        var historical = JsonSerializer.Deserialize<HostLoadSample>(HistoricalCanonical, PhoneHomeFraming.Json);
        historical.ShouldNotBeNull();
        var shared = new HostLoadTopConsumer(11, 100, "csc", HostConsumerRules.Compiler, 1.5, 4096);
        var first = historical with
        {
            TopConsumers = [shared, new HostLoadTopConsumer(22, 100, "pwsh", HostConsumerRules.Other, 0.4, 8192)],
        };
        var second = historical with
        {
            TopConsumers = [shared, new HostLoadTopConsumer(33, 100, "dotnet", HostConsumerRules.Build, 2.0, 16384)],
        };
        ServerCanonical(first).ShouldNotBe(ServerCanonical(second));
        ServerCanonical(first).ShouldNotBe(HistoricalCanonical);

        await using var world = await fixture.WorldAsync();
        world.Pump.TryEnqueue(Page(first)).ShouldBeTrue();
        var inserted = await world.Pump.ProcessOneAsync(CancellationToken.None);
        inserted.Inserted.ShouldBe(1);
        inserted.Duplicates.ShouldBe(0);

        world.Pump.TryEnqueue(Page(first)).ShouldBeTrue();
        var same = await world.Pump.ProcessOneAsync(CancellationToken.None);
        same.Inserted.ShouldBe(0);
        same.Duplicates.ShouldBe(1);

        world.Pump.TryEnqueue(Page(second)).ShouldBeTrue();
        await Should.ThrowAsync<ConflictException>(() => world.Pump.ProcessOneAsync(CancellationToken.None));
        await using var fresh = world.Db(false);
        (await fresh.HostLoadSamples.CountAsync(s => s.Sequence == first.Source.Sequence)).ShouldBe(1);
        var stored = JsonSerializer.Deserialize<HostLoadSample>(
            (await fresh.HostLoadSamples.SingleAsync(s => s.Sequence == first.Source.Sequence)).PayloadJson,
            PhoneHomeFraming.Json);
        stored.ShouldNotBeNull();
        stored.TopConsumers.Select(row => row.Pid).ShouldBe([11, 22]);
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
        var historyPath = Path.Combine(world.Root, "capture-queue");
        var settings = CaptureSettings(world, historyPath);
        using var release = new ManualResetEventSlim(false);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var capture = new HostLoadCaptureService(Options.Create(settings), world.Clock, new C1514Authority(world.Clock),
            NullLogger<HostLoadCaptureService>.Instance, open: () => new HostLoadSpool(settings.HistoryPath!, settings, world.Clock,
                appendObservation: (stage, _) =>
                {
                    if (stage != "before-flush") return;
                    entered.TrySetResult();
                    if (!release.Wait(TimeSpan.FromSeconds(15)))
                        throw new TimeoutException("before-flush hold was not released");
                }));
        try
        {
            await capture.StartAsync(CancellationToken.None);
            await C1514HostLoadWorld.EventuallyAsync(() => SpoolReady(capture));
            capture.TryCapture(host).ShouldBeTrue();
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var busy = Task.Run(() =>
            {
                try
                {
                    capture.ReadHistory(null, 120);
                    return false;
                }
                catch (IOException ex)
                {
                    return ex.Message == "host_load_history_busy";
                }
            });
            await Task.WhenAny(busy, Task.Delay(TimeSpan.FromSeconds(2)));
            busy.IsCompleted.ShouldBeTrue("a history read returns while an append holds the spool gate");
            (await busy).ShouldBeTrue();
        }
        finally
        {
            release.Set();
        }

        await C1514HostLoadWorld.EventuallyAsync(() => capture.LastAppendedAt == C1514HostLoadWorld.T0);
        var live = capture.ReadHistory(null, 120).Samples.Single();
        live.TopConsumers.Count.ShouldBe(HostConsumerRules.MaxCount);
        live.TopConsumers.Select(row => row.Pid).ShouldBe(Enumerable.Range(1, 8).ToArray());
        var page = capture.ReadHistory(null, 120);
        world.Pump.TryEnqueue(new HostLoadIngestBatch("grok-linux", null, page)).ShouldBeTrue();
        world.Pump.TryEnqueue(new HostLoadIngestBatch("grok-linux", null, page)).ShouldBeTrue();
        world.Pump.TryEnqueue(new HostLoadIngestBatch("grok-linux", null, page)).ShouldBeFalse();
        capture.ReadHistory(null, 120).Samples.Single().TopConsumers.Any(row => row.Pid == 9).ShouldBeFalse();

        await capture.StopAsync(CancellationToken.None);
        capture.Dispose();

        var recovered = new HostLoadSpool(historyPath, settings, world.Clock).ReadHistory(null, 120);
        var round = recovered.Samples.Single();
        round.TopConsumers.Count.ShouldBe(8);
        round.TopConsumers.Select(row => row.Pid).ShouldBe(Enumerable.Range(1, 8).ToArray());
        round.TopConsumers.Any(row => row.Pid == 9).ShouldBeFalse();

        var first = await world.Pump.ProcessOneAsync(CancellationToken.None);
        first.Inserted.ShouldBe(1);
        first.Duplicates.ShouldBe(0);
        var second = await world.Pump.ProcessOneAsync(CancellationToken.None);
        second.Inserted.ShouldBe(0);
        second.Duplicates.ShouldBe(1);
        world.Pump.TryEnqueue(new HostLoadIngestBatch("grok-linux", null, recovered)).ShouldBeTrue();
        var replay = await world.Pump.ProcessOneAsync(CancellationToken.None);
        replay.Inserted.ShouldBe(0);
        replay.Duplicates.ShouldBe(1);

        await using var db = world.Db(false);
        var stored = JsonSerializer.Deserialize<HostLoadSample>(
            (await db.HostLoadSamples.SingleAsync(s => s.Sequence == round.Source.Sequence)).PayloadJson,
            PhoneHomeFraming.Json);
        stored.ShouldNotBeNull();
        stored.TopConsumers.Select(row => row.Pid).ShouldBe(Enumerable.Range(1, 8).ToArray());
        stored.TopConsumers.Any(row => row.Pid == 9).ShouldBeFalse();
    }

    private static HostStatsSettings CaptureSettings(C1514HostLoadWorld world, string path) => new()
    {
        HistoryPath = path,
        RunnerId = world.Runner.RunnerId,
        PhysicalHostKey = world.Runner.PhysicalHostKey,
        BindingRevision = world.Runner.BindingRevision,
        SourceEpoch = world.Runner.SourceEpoch,
        ProcessSampling = false,
        IntervalMs = world.Runner.IntervalMs,
    };

    private static bool SpoolReady(HostLoadCaptureService capture)
    {
        try
        {
            capture.ReadHistory(null, 120);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
    }

    private static HostLoadIngestBatch Page(HostLoadSample sample) =>
        new(sample.Source.RunnerId, null, new HostLoadHistoryPage(1, sample.Source, [sample], [], null, false, true));

    private static string ServerCanonical(HostLoadSample sample)
    {
        var method = typeof(HostLoadIngestService).GetMethod("CanonicalPayload", BindingFlags.NonPublic | BindingFlags.Static);
        method.ShouldNotBeNull();
        return (string)method.Invoke(null, [sample])!;
    }

    private static string Sha(string payload) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
}
