using System.Text.Json;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.SessionRunner.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;
using Rig = Antiphon.Tests.Application.RemoteWorkspacePreparerTests.Rig;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-1677 S2 CP-9. The server half of the /tmp sweep against the scripted phone-home peer (shared Postgres,
/// isolated schema): <c>TempSweep:Enabled=false</c> sends nothing, an enabled sweep defaults to DryRun and its
/// reply becomes Lane RunnerTemp rows, and <c>claude-scratch</c> is sent off by default (V-51, V-51b, V-51c).
/// The settings start from the shipped type defaults; only the retention runner list is set.
/// </summary>
[Category("Integration")]
[Timeout(45_000)]
public sealed class C1677TempSweepServerTests
{
    private sealed class World : IAsyncDisposable
    {
        public Rig Rig { get; private init; } = null!;
        public RunnerWorktreeRetentionSettings Settings { get; private init; } = null!;
        public PhoneHomeTempSweepResponse Reply { get; set; } = new(TempSweepModes.DryRun, [], []);

        public static async Task<World> StartAsync()
        {
            var settings = new RunnerWorktreeRetentionSettings { PauseBetweenSeconds = 0 };
            var rig = await Rig.StartAsync(
                capabilities: new("InboxConhost", "inbox", "test", false,
                    Features: [RunnerCapabilityFeatures.WorkspaceRetentionV1, RunnerCapabilityFeatures.TempSweepV1],
                    VerificationCustodyBackend: null),
                configure: services =>
                {
                    services.AddSingleton<IOptionsMonitor<RunnerWorktreeRetentionSettings>>(new Monitor(settings));
                    services.AddScoped<RunnerWorktreeRetentionService>();
                });
            settings.Runners.Add(rig.RunnerId);
            var world = new World { Rig = rig, Settings = settings };
            rig.Peer.Reply = world.Answer;
            return world;
        }

        private PhoneHomeFrame? Answer(PhoneHomeFrame frame)
        {
            if (frame.Kind != PhoneHomeFrameKind.Request)
                return null;
            return frame.Operation switch
            {
                PhoneHomeOperation.WorkspaceRetentionInventory => Result(frame, new PhoneHomeWorkspaceRetentionInventoryResponse([], true)),
                PhoneHomeOperation.TempSweep => Result(frame, Reply),
                _ => null,
            };
        }

        private static PhoneHomeFrame Result(PhoneHomeFrame request, object payload) =>
            new(PhoneHomeFrameKind.Result, request.Epoch, request.RequestId, request.Operation,
                JsonSerializer.SerializeToElement(payload, PhoneHomeFraming.Json));

        public async Task TickAsync()
        {
            using var scope = Rig.Provider.CreateScope();
            (await scope.ServiceProvider.GetRequiredService<RunnerWorktreeRetentionService>().RunTickAsync(default))
                .ShouldHaveSingleItem().RunnerId.ShouldBe(Rig.RunnerId);
        }

        public List<PhoneHomeTempSweepRequest> Sweeps() =>
            Rig.Peer.Incoming.ToArray()
                .Where(f => f.Kind == PhoneHomeFrameKind.Request && f.Operation == PhoneHomeOperation.TempSweep)
                .Select(f => f.Payload!.Value.Deserialize<PhoneHomeTempSweepRequest>(PhoneHomeFraming.Json)!)
                .ToList();

        public int Inventories() =>
            Rig.Peer.Incoming.ToArray().Count(f => f.Kind == PhoneHomeFrameKind.Request
                && f.Operation == PhoneHomeOperation.WorkspaceRetentionInventory);

        public async Task<List<WorktreeResidueRunCandidate>> TempRowsAsync()
        {
            await using var db = Rig.NewDb();
            return await db.WorktreeResidueRunCandidates.AsNoTracking()
                .Where(r => r.Lane == RunnerWorktreeRetentionService.LaneTemp)
                .OrderBy(r => r.Path).ToListAsync();
        }

        public ValueTask DisposeAsync() => Rig.DisposeAsync();
    }

    private sealed class Monitor(RunnerWorktreeRetentionSettings value) : IOptionsMonitor<RunnerWorktreeRetentionSettings>
    {
        public RunnerWorktreeRetentionSettings CurrentValue => value;
        public RunnerWorktreeRetentionSettings Get(string? name) => value;
        public IDisposable? OnChange(Action<RunnerWorktreeRetentionSettings, string?> listener) => null;
    }

    [Test]
    public async Task Disabled_sweep_sends_nothing()
    {
        // V-51 (G-111). The retention tick itself runs (the inventory is read), but no TempSweep frame is sent.
        await using var w = await World.StartAsync();
        w.Settings.TempSweep.Enabled.ShouldBeFalse("the type default is off");
        await w.TickAsync();
        w.Inventories().ShouldBe(1, "control: the tick reached the runner");
        w.Sweeps().ShouldBeEmpty();
        (await w.TempRowsAsync()).ShouldBeEmpty();
    }

    [Test]
    public async Task Enabled_sweep_defaults_to_dry_run_and_records_rows()
    {
        // V-51b (G-112). Execute stays at its default (false), so the frame is DryRun; the reply becomes rows.
        await using var w = await World.StartAsync();
        w.Settings.TempSweep.Enabled = true;
        w.Reply = new PhoneHomeTempSweepResponse(TempSweepModes.DryRun,
            [
                new TempSweepCandidate("c487-probe-" + new string('a', 32), "c487-probe", TempSweepCodes.WouldRemove, Bytes: 4096),
                new TempSweepCandidate("c1514-" + new string('b', 32), "c1514", TempSweepCodes.Kept, TempSweepCodes.TooYoung, Bytes: 10),
            ],
            [new TempSweepUnregistered("g104-<hex>", 3, 300, true)],
            Root: "/tmp");
        await w.TickAsync();
        var sweep = w.Sweeps().ShouldHaveSingleItem();
        sweep.Mode.ShouldBe(TempSweepModes.DryRun);
        sweep.MinAgeHours.ShouldBe(48);
        sweep.MaxEntries.ShouldBe(200);
        sweep.MaxBytes.ShouldBe(21_474_836_480);
        var rows = await w.TempRowsAsync();
        rows.Select(r => (r.Path, r.Outcome, r.ReasonCode, r.Branch)).ShouldBe(
        [
            ("/tmp/c1514-" + new string('b', 32), "Held", TempSweepCodes.TooYoung, "temp:c1514;bytes=10"),
            ("/tmp/c487-probe-" + new string('a', 32), "Preview", TempSweepCodes.WouldRemove, "temp:c487-probe;bytes=4096"),
            ("/tmp/g104-<hex>", "Held", "unregistered", "temp:unregistered;count=3;bytes=300"),
        ]);
        rows.ShouldAllBe(r => r.RunnerId == w.Rig.RunnerId && r.OperationId == sweep.RequestId && r.DirectoryRemoved == null);
    }

    [Test]
    public async Task Claude_scratch_is_sent_off_by_default()
    {
        // V-51c (G-113).
        await using var w = await World.StartAsync();
        w.Settings.TempSweep.Enabled = true;
        await w.TickAsync();
        var families = w.Sweeps().ShouldHaveSingleItem().Families.ShouldNotBeNull();
        families[RunnerTempSweepSettings.ClaudeScratch].ShouldBeFalse();
    }
}
