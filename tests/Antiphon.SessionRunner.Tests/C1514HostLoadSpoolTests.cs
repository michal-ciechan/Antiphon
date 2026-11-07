using System.Text;
using System.Text.Json;
using Antiphon.SessionRunner.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.SessionRunner.Tests;

[Category("Integration"), Timeout(10_000)]
[ClassDataSource<C1514HostLoadFixture>(Shared = SharedType.PerTestSession)]
public class C1514HostLoadSpoolTests(C1514HostLoadFixture fixture)
{
    [Test]
    public void AppendSurvivesRestart()
    {
        var path = fixture.PrivatePath(); var settings = C1514HostLoadFixture.Config(path);
        var clock = new FakeTimeProvider(C1514HostLoadFixture.T0);
        var spool = new HostLoadSpool(path, settings, clock); var sample = C1514HostLoadFixture.Sample(spool);
        spool.Append(sample);
        var reopened = new HostLoadSpool(path, settings, clock);
        var page = reopened.ReadHistory(null, 120);
        page.Samples.Count.ShouldBe(1);
        page.Samples[0].Source.ShouldBe(sample.Source);
        page.Samples[0].CpuPercent.ShouldBe(40d);
        page.Samples[0].Load1.ShouldBe(24d);
        reopened.Append(sample);
        reopened.ReadHistory(null, 120).Samples.Count.ShouldBe(1);
    }
    [Test]
    public void PartialTailIsQuarantined()
    {
        var path = fixture.PrivatePath(); var settings = C1514HostLoadFixture.Config(path);
        var clock = new FakeTimeProvider(C1514HostLoadFixture.T0);
        var spool = new HostLoadSpool(path, settings, clock); spool.Append(C1514HostLoadFixture.Sample(spool));
        var source = new HostLoadSource("owned-runner", spool.RunnerStoreId, spool.BootId, 102);
        var tail = "{\"ordinal\":2,\"sample\":{\"source\":" + JsonSerializer.Serialize(source, PhoneHomeFraming.Json) + ",\"cpuPercent\":";
        using (var stream = new FileStream(Directory.GetFiles(path, "chunk-*.jsonl").Single(), FileMode.Append))
        { stream.Write(Encoding.UTF8.GetBytes(tail)); stream.WriteByte(0xc3); stream.Flush(true); }
        var page = new HostLoadSpool(path, settings, clock).ReadHistory(null, 120);
        page.Samples.Select(s => s.Source.Sequence).ShouldBe([101L]);
        page.Gaps.ShouldContain(g => g.BootId == source.BootId && g.FromSequence == 102 && g.ToSequence == 102);
        page.Complete.ShouldBeFalse();
        Directory.GetFiles(path, "*.quarantine-*").Length.ShouldBe(1);
    }
    [Test]
    public void CursorCannotSkipUncommitted()
    {
        var settings = C1514HostLoadFixture.Config(fixture.PrivatePath()); var clock = new FakeTimeProvider(C1514HostLoadFixture.T0);
        var spool = new HostLoadSpool(settings.HistoryPath!, settings, clock);
        spool.Append(C1514HostLoadFixture.Sample(spool, 101)); spool.Append(C1514HostLoadFixture.Sample(spool, 102));
        var first = spool.ReadHistory(null, 1); first.Samples.Single().Source.Sequence.ShouldBe(101);
        var parts = first.NextCursor!.Split('.');
        var payload = Encoding.UTF8.GetString(Convert.FromBase64String(parts[0])).Replace("\"through\":1", "\"through\":2", StringComparison.Ordinal);
        var skipped = Convert.ToBase64String(Encoding.UTF8.GetBytes(payload)) + "." + parts[1];
        Should.Throw<ArgumentException>(() => spool.ReadHistory(skipped, 120));
        spool.ReadHistory(null, 120).Samples.Select(s => s.Source.Sequence).ShouldBe([101L, 102L]);
        var otherSettings = C1514HostLoadFixture.Config(fixture.PrivatePath());
        var other = new HostLoadSpool(otherSettings.HistoryPath!, otherSettings, clock);
        other.Append(C1514HostLoadFixture.Sample(other));
        Should.Throw<ArgumentException>(() => spool.ReadHistory(other.ReadHistory(null, 1).NextCursor, 120));
        spool.ReadHistory(first.NextCursor, 120).Samples.Select(s => s.Source.Sequence).ShouldBe([102L]);
    }
    [Test]
    public void ReplayPreservesBootIdentity()
    {
        var settings = C1514HostLoadFixture.Config(fixture.PrivatePath()); var clock = new FakeTimeProvider(C1514HostLoadFixture.T0);
        var spool = new HostLoadSpool(settings.HistoryPath!, settings, clock); var old = C1514HostLoadFixture.Sample(spool);
        spool.Append(old);
        var restarted = new HostLoadSpool(settings.HistoryPath!, settings, clock);
        restarted.BootId.ShouldNotBe(spool.BootId);
        var fresh = C1514HostLoadFixture.Sample(restarted, 1); restarted.Append(fresh);
        var page = restarted.ReadHistory(null, 120);
        page.Samples.Select(s => s.Source).ShouldBe([old.Source, fresh.Source]);
        page.Samples.Select(s => s.CpuPercent).ShouldBe([40d, 40d]);
    }
    [Test]
    public void CapsPersistExplicitGaps()
    {
        var settings = C1514HostLoadFixture.Config(fixture.PrivatePath()); settings.SpoolMaxBytes = 8192;
        var clock = new FakeTimeProvider(C1514HostLoadFixture.T0);
        var spool = new HostLoadSpool(settings.HistoryPath!, settings, clock);
        for (var i = 1; i <= 30; i++) spool.Append(C1514HostLoadFixture.Sample(spool, i));
        spool.Bytes.ShouldBeLessThanOrEqualTo(8192);
        var reopened = new HostLoadSpool(settings.HistoryPath!, settings, clock);
        reopened.ReadHistory(null, 120).Gaps.ShouldNotBeEmpty();
        clock.Advance(TimeSpan.FromHours(24));
        reopened.ReadHistory(null, 120).Samples.Count.ShouldBeGreaterThan(0);
        clock.Advance(TimeSpan.FromMilliseconds(1));
        var expired = new HostLoadSpool(settings.HistoryPath!, settings, clock).ReadHistory(null, 120);
        expired.Samples.ShouldBeEmpty(); expired.Gaps.ShouldNotBeEmpty(); expired.Complete.ShouldBeFalse();
    }
    [Test]
    public async Task DiskFailurePreservesLiveSampling()
    {
        var settings = C1514HostLoadFixture.Config(fixture.PrivatePath()); settings.AppendQueueCapacity = 1;
        var clock = new FakeTimeProvider(C1514HostLoadFixture.T0); var failed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var available = false;
        using var capture = new HostLoadCaptureService(Options.Create(settings), clock, new UnknownAuthority(),
            NullLogger<HostLoadCaptureService>.Instance, open: () =>
            { if (!Volatile.Read(ref available)) { failed.TrySetResult(); throw new IOException(); } return new HostLoadSpool(settings.HistoryPath!, settings, clock); });
        await capture.StartAsync(CancellationToken.None); await failed.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var store = new HostStatsStore(settings, clock);
        using var sampler = new HostStatsSamplerService(new ScriptedHostStatsProbe(), store, new MissingCpu(), () => [], _ => null,
            clock, Options.Create(settings), NullLogger<HostStatsSamplerService>.Instance, capture);
        await sampler.SampleOnceAsync(); clock.Advance(TimeSpan.FromSeconds(5)); await sampler.SampleOnceAsync();
        clock.Advance(TimeSpan.FromSeconds(5)); await sampler.SampleOnceAsync();
        store.Latest().ShouldNotBeNull().At.ShouldBe(clock.GetUtcNow()); capture.DroppedSamples.ShouldBeGreaterThan(0);
        Volatile.Write(ref available, true); await Task.Delay(20); clock.Advance(TimeSpan.FromSeconds(5));
        try
        {
            await C1514HostLoadFixture.EventuallyAsync(() => capture.LastAppendedAt is not null);
            var page = capture.ReadHistory(null, 120); page.Samples.Count.ShouldBe(1); page.Gaps.ShouldNotBeEmpty();
        }
        finally { await capture.StopAsync(CancellationToken.None); }
    }
    private sealed class MissingCpu : IProcessCpuProbe { public TimeSpan? TryGetTotalCpuTime(int pid, DateTime startedAt) => null; }
    private sealed class UnknownAuthority : IHostAdmissionSnapshotReader
    { public Task<HostAdmissionSnapshot> ReadAsync(CancellationToken ct) => Task.FromResult(HostAdmissionSnapshot.Unknown()); }
}
