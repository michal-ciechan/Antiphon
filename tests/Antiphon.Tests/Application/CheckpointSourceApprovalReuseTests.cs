using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class CheckpointSourceApprovalReuseTests
{
    [Test]
    public async Task Family_reset_restores_database_and_service_state()
    {
        await using var family = await CheckpointSourceApprovalFamily.CreateAsync();
        object services, queue, fault, verifier;
        await using (var first = await family.OpenProtocolAsync())
        {
            await CheckpointSourceApprovalTests.OrdinaryCaseAsync(first, false, LandPhase.Prepared, false);
            services = first.Services; queue = first.Queue; fault = first.Fault; verifier = first.Verifier;
            await using var db = first.CreateContext();
            var dispatch = await db.AgentTaskEvents.FirstAsync();
            db.AgentTaskDispatchWarningIntents.Add(new AgentTaskDispatchWarningIntent
            {
                Id = Guid.NewGuid(), DispatchEventId = dispatch.Id, TaskId = first.Git.TaskId,
                WarningKey = "family-poison", NotificationId = Guid.NewGuid(), Detail = "poison",
                Body = "poison", ContentDigest = new string('f', 64), CreatedAt = first.Clock.GetUtcNow().UtcDateTime,
                NextAttemptAt = first.Clock.GetUtcNow().UtcDateTime,
            });
            db.WorkspaceUseReservations.Add(new WorkspaceUseReservation
            {
                Id = Guid.NewGuid(), TaskId = first.Git.TaskId, CanonicalPath = first.Git.Source,
                CommonDirectory = first.Git.Repository, SourceFullRef = first.Git.SourceRef,
                CreatedAt = first.Clock.GetUtcNow().UtcDateTime,
            });
            await db.SaveChangesAsync();
            first.Fault.Phase = LandPhase.Prepared;
            first.Fault.AfterCommit = true;
            first.Verifier.Passed = false;
            first.Verifier.Barrier = () => throw new InvalidOperationException("poison verifier");
            first.Queue.TryEnqueue(first.Git.TaskId, "poison").ShouldBeTrue();
        }
        await family.ResetAsync();
        var restored = await family.ReadRowsAsync();
        foreach (var (table, rows) in family.Baseline)
            restored[table].ShouldBe(rows, "family-db-baseline " + table);
        await using (var next = await family.OpenProtocolAsync())
        {
            next.Fault.Phase.ShouldBeNull("family-fault-unarmed");
            next.Fault.Triggered.ShouldBeFalse("family-fault-unarmed");
            next.Fault.AfterCommit.ShouldBeFalse("family-fault-unarmed");
            next.Fault.AfterSave.ShouldBeFalse("family-fault-unarmed");
            next.Fault.AwaitingCommit.ShouldBeFalse("family-fault-unarmed");
            next.Fault.Matches.ShouldBeNull("family-fault-unarmed");
            next.Fault.TerminalCut.ShouldBeNull("family-fault-unarmed");
            next.Fault.AfterAcknowledged.ShouldBeNull("family-fault-unarmed");
            next.Fault.AfterSaveAcknowledged.ShouldBeNull("family-fault-unarmed");
            next.Fault.OnTransactionStarted.ShouldBeNull("family-fault-unarmed");
            next.Verifier.Invocations.ShouldBeEmpty("family-case-services-fresh");
            next.Verifier.Passed.ShouldBeTrue("family-case-services-fresh");
            next.Verifier.Barrier.ShouldBeNull("family-case-services-fresh");
            next.Verifier.Calls.ShouldBe(0, "family-case-services-fresh");
            next.Queue.TryDequeue(out _).ShouldBeFalse("family-case-services-fresh");
            ReferenceEquals(services, next.Services).ShouldBeFalse("family-case-services-fresh");
            ReferenceEquals(queue, next.Queue).ShouldBeFalse("family-case-services-fresh");
            ReferenceEquals(fault, next.Fault).ShouldBeFalse("family-case-services-fresh");
            ReferenceEquals(verifier, next.Verifier).ShouldBeFalse("family-case-services-fresh");
        }
        family.StoreAllocations.ShouldBe(1, "family-store-created-once");
    }

    [Test]
    public async Task Family_reset_restores_native_git_image()
    {
        await using var family = await CheckpointSourceApprovalFamily.CreateAsync(native: true);
        var fixture = family.Fixture!;
        var expected = await ObserveGitAsync(fixture);
        var keep = File.ReadAllBytes(Path.Combine(fixture.Source, "keep.txt"));
        var attributes = File.GetAttributes(Path.Combine(fixture.Source, "keep.txt"));
        var mode = OperatingSystem.IsWindows() ? (UnixFileMode?)null : File.GetUnixFileMode(Path.Combine(fixture.Source, "keep.txt"));
        var oldRecorder = fixture.Git;
        await using (var first = await family.OpenNativeAsync())
            await CheckpointSourceApprovalTests.PublishedCleanupCaseAsync(first, "ordinary", false);
        Directory.Exists(fixture.Source).ShouldBeFalse("cleanup must really delete source before restoration");
        await fixture.RequiredAsync(fixture.Repository, "update-ref", "refs/antiphon/poison", "HEAD");
        await fixture.RequiredAsync(fixture.Repository, "worktree", "add", "--detach", Path.Combine(fixture.Root, "extra"), "HEAD");
        await File.WriteAllTextAsync(Path.Combine(fixture.Repository, "keep.txt"), "poison index");
        await fixture.RequiredAsync(fixture.Repository, "add", "keep.txt");
        Directory.CreateDirectory(Path.Combine(fixture.Repository, ".antiphon"));
        await File.WriteAllTextAsync(Path.Combine(fixture.Repository, ".antiphon", "poison"), "ignored");
        oldRecorder.HooksPathOverride = "poison";
        await family.ResetAsync();
        Directory.Exists(fixture.Source).ShouldBeTrue("family-native-image-restored");
        var actual = await ObserveGitAsync(fixture);
        actual.ShouldBe(expected, "family-native-image-restored");
        File.ReadAllBytes(Path.Combine(fixture.Source, "keep.txt")).ShouldBe(keep, "family-native-image-restored");
        File.GetAttributes(Path.Combine(fixture.Source, "keep.txt")).ShouldBe(attributes, "family-native-image-restored");
        if (mode is not null) File.GetUnixFileMode(Path.Combine(fixture.Source, "keep.txt")).ShouldBe(mode.Value, "family-native-image-restored");
        ReferenceEquals(oldRecorder, fixture.Git).ShouldBeFalse("family-recorder-fresh");
        fixture.Git.HooksPathOverride.ShouldBeNull("family-recorder-fresh");
        fixture.Git.Trace.ShouldBeEmpty("family-recorder-fresh");
        family.NativeInitializations.ShouldBe(1, "family-native-init-once");
    }

    private static async Task<string[]> ObserveGitAsync(LandingGitFixture fixture)
    {
        var reader = new LandingGitFixture.FixtureGit(Path.Combine(fixture.Root, "home"), fixture.TaskId);
        var values = new List<string>();
        foreach (var (directory, args) in new (string, string[])[]
        {
            (fixture.Repository, ["show-ref"]), (fixture.Remote, ["show-ref"]),
            (fixture.Repository, ["worktree", "list", "--porcelain"]),
            (fixture.Source, ["rev-parse", "HEAD"]),
            (fixture.Source, ["status", "--porcelain", "--ignored"]),
            (fixture.Source, ["ls-files", "--stage"]),
        })
        {
            var result = await reader.RunAsync(directory, args, CancellationToken.None);
            result.ExitCode.ShouldBe(0, "family-native-image-restored " + args[0]);
            values.Add(result.Output);
        }
        return values.ToArray();
    }

    [Test]
    public async Task Family_cases_are_order_independent()
    {
        foreach (var kind in new[] { "ordinary", "self", "adoption", "cleanup-ordinary", "cleanup-self", "cleanup-adoption" })
        {
            var ownerId = Guid.NewGuid();
            bool?[] sequence = kind.StartsWith("cleanup-", StringComparison.Ordinal)
                ? [false, null, null, false] : [true, false, null, null, false, true];
            var oracle = new Dictionary<string, string>();
            foreach (var assertion in sequence.Distinct())
            {
                await using var fresh = await CheckpointSourceApprovalFamily.CreateAsync(kind != "ordinary", ownerId);
                oracle[assertion?.ToString() ?? "null"] = await ObserveCaseAsync(fresh, kind, assertion);
            }
            await using var reused = await CheckpointSourceApprovalFamily.CreateAsync(kind != "ordinary");
            foreach (var assertion in sequence)
            {
                await reused.ResetAsync();
                var baseline = await reused.ReadRowsAsync();
                foreach (var (table, rows) in reused.Baseline)
                    baseline[table].ShouldBe(rows, "family-order-independent " + kind + " " + table);
                (await ObserveCaseAsync(reused, kind, assertion)).ShouldBe(oracle[assertion?.ToString() ?? "null"],
                    "family-order-independent " + kind + " " + assertion);
            }
        }
    }

    private static async Task<string> ObserveCaseAsync(CheckpointSourceApprovalFamily family, string kind, bool? assertion)
    {
        if (kind == "ordinary")
        {
            await using var h = await family.OpenProtocolAsync();
            await CheckpointSourceApprovalTests.OrdinaryCaseAsync(h, false, LandPhase.Prepared, assertion);
            await using var db = h.CreateContext();
            var request = await db.AgentTaskLandRequests.SingleAsync();
            var op = await h.OperationAsync();
            return $"{request.IsPending}|{request.ExpectedSourceSha == h.Git.SourceHead}|{op?.Phase}|{op?.Cleanup}|{op?.LastReason}|{h.Verifier.Calls}|" +
                string.Join(',', h.Git.Trace.Select(a => a[0]));
        }
        await using var native = await family.OpenNativeAsync();
        if (kind.StartsWith("cleanup-", StringComparison.Ordinal))
            await CheckpointSourceApprovalTests.PublishedCleanupCaseAsync(native, kind[8..], assertion);
        else await CheckpointSourceApprovalTests.RecoveryCaseAsync(native, kind, "resume", assertion);
        await using var freshDb = native.CreateContext();
        var requests = await freshDb.AgentTaskLandRequests.OrderBy(r => r.RequestedAt).ToListAsync();
        var operation = await native.OperationAsync();
        var remote = await native.Fixture.RequiredAsync(native.Fixture.Remote, "rev-parse", native.Fixture.TargetRef);
        var source = await native.Fixture.RequiredAsync(native.Fixture.Remote, "rev-parse", native.Fixture.SourceRef);
        foreach (var request in requests.Where(r => r.ReviewEvidenceId != null))
        {
            var evidence = await freshDb.StageOutcomes.SingleAsync(o => o.Id == request.ReviewEvidenceId);
            request.ExpectedSourceSha.ShouldBe(evidence.ReviewedSourceSha, "family-order-independent approval coordinates");
        }
        return $"{string.Join(',', requests.Select(r => r.IsPending))}|{operation?.Phase}|{operation?.Cleanup}|{operation?.LastReason}|{native.Verifier.Calls}|{remote.Trim()}|{source.Trim()}|" +
            string.Join(',', native.Fixture.Git.Trace.Select(a => a[0]));
    }

    [Test]
    public async Task Borrowed_family_lifetime_preserves_default_ownership()
    {
        var family = await CheckpointSourceApprovalFamily.CreateAsync(native: true);
        var root = family.Fixture!.Root;
        var connection = family.Schema.ConnectionString;
        await using (var borrowed = await family.OpenNativeAsync())
        {
            await using var context = borrowed.CreateContext();
            (await context.AgentTasks.CountAsync()).ShouldBe(1);
        }
        (await DatabaseExistsAsync(connection)).ShouldBeTrue("family-borrowed-owner-intact");
        Directory.Exists(root).ShouldBeTrue("family-borrowed-owner-intact");
        var destructiveCalls = 0;
        var other = new NpgsqlConnectionStringBuilder(connection) { Database = "postgres" }.ConnectionString;
        Should.Throw<InvalidOperationException>(() => family.ValidateDatabaseTarget(family.Schema, other, () => destructiveCalls++), "family-foreign-target-untouched");
        var counterfeit = new IsolatedTestSchema(new NpgsqlConnectionStringBuilder(connection).Database!, connection);
        Should.Throw<InvalidOperationException>(() => family.ValidateDatabaseTarget(counterfeit, connection, () => destructiveCalls++), "family-foreign-target-untouched");
        var image = CheckpointSourceFixtureImage.Capture(root);
        var foreign = Path.Combine(Path.GetTempPath(), "c886-foreign-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(foreign);
        try
        {
            Should.Throw<InvalidOperationException>(() => image.ValidateTarget(foreign, () => destructiveCalls++), "family-foreign-target-untouched");
        }
        finally { Directory.Delete(foreign); }
        destructiveCalls.ShouldBe(0, "family-foreign-target-untouched");
        await family.DisposeAsync();
        await family.DisposeAsync();
        family.StoreDrops.ShouldBe(1, "family-default-owner-disposed");
        (await DatabaseExistsAsync(connection)).ShouldBeFalse("family-default-owner-disposed");
        Directory.Exists(root).ShouldBeFalse("family-default-owner-disposed");
        var protocol = new LandingProtocolHarness();
        await protocol.InitializeAsync();
        var protocolConnection = protocol.Schema.ConnectionString;
        var protocolRoot = protocol.Git.Root;
        await protocol.DisposeAsync();
        (await DatabaseExistsAsync(protocolConnection)).ShouldBeFalse("family-default-owner-disposed");
        Directory.Exists(protocolRoot).ShouldBeFalse("family-default-owner-disposed");
        var native = new LandingSafetyHarness();
        await native.InitializeAsync();
        var nativeConnection = native.Schema.ConnectionString;
        var nativeRoot = native.Fixture.Root;
        await native.DisposeAsync();
        (await DatabaseExistsAsync(nativeConnection)).ShouldBeFalse("family-default-owner-disposed");
        Directory.Exists(nativeRoot).ShouldBeFalse("family-default-owner-disposed");
    }

    private static async Task<bool> DatabaseExistsAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(TestDbFixture.MaintenanceConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT EXISTS(SELECT 1 FROM pg_database WHERE datname = @name)", connection);
        command.Parameters.AddWithValue("name", new NpgsqlConnectionStringBuilder(connectionString).Database!);
        return (bool)(await command.ExecuteScalarAsync())!;
    }
}
