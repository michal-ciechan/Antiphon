using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-1767. The RefOnly owner SELECT in <see cref="WorkspaceReservationJournal"/> is dead
/// when the normalized reservation key has no ref: that fence requires a nonempty ref, so the
/// overlap is <c>Same()</c> only. A ref-bearing key still reads owners and still fences RefOnly.
/// </summary>
[Category("Integration")]
[Timeout(120_000)]
public sealed class C1767ReflessOverlapTests
{
    [Test]
    public async Task Refless_key_skips_the_owner_select_and_keeps_same_path_overlap()
    {
        var root = Path.Combine(Path.GetTempPath(), "c1767-" + Guid.NewGuid().ToString("N"));
        var repo = Path.Combine(root, "repo");
        var same = WorkspaceReservationKey.For(Path.Combine(root, "same"), "refs/heads/feat/c1767", repo);
        var other = WorkspaceReservationKey.For(Path.Combine(root, "other"), "refs/heads/feat/c1767", repo);
        var refOnly = Guid.NewGuid();
        var checkout = Guid.NewGuid();
        var sameRow = Guid.NewGuid();
        var otherRefOnlyRow = Guid.NewGuid();
        var otherCheckoutRow = Guid.NewGuid();

        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using (var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString)))
        {
            db.AgentTasks.Add(TaskRow(refOnly, DesktopWorkspaceKind.RefOnly, same.CanonicalPath));
            db.AgentTasks.Add(TaskRow(checkout, DesktopWorkspaceKind.Checkout, other.CanonicalPath));
            db.WorkspaceUseReservations.Add(Reservation(sameRow, same, refOnly));
            db.WorkspaceUseReservations.Add(Reservation(otherRefOnlyRow, other, refOnly));
            db.WorkspaceUseReservations.Add(Reservation(otherCheckoutRow, other, checkout));
            await db.SaveChangesAsync();
        }

        await using var host = Host.Open(schema.ConnectionString);
        foreach (var rawRef in new[] { "", "   ", "\t" })
        {
            var key = new WorkspaceReservationKey(same.CanonicalPath, rawRef, same.CommonDirectory);
            key.Normalized().SourceFullRef.Length.ShouldBe(0, "normalized ref is empty");

            host.Counter.Reset();
            var active = await host.Journal.ReadActiveAsync(key, CancellationToken.None);
            Ids(active).ShouldBe(new[] { sameRow }, "same-path overlap only");
            OwnerReads(host).ShouldBe(0, "read-active owner select: " + host.Counter.Roster());

            host.Counter.Reset();
            var live = await host.Journal.ReadActiveAsync(key, liveOwnersOnly: true, CancellationToken.None);
            Ids(live).ShouldBe(new[] { sameRow }, "live read keeps the same row");
            OwnerReads(host).ShouldBe(0, "read-active-live owner select: " + host.Counter.Roster());

            host.Counter.Reset();
            var admit = await host.Journal.TryAdmitConsumerAsync(
                new WorkspaceReservationCommand(key, WorkspaceReservationKind.Launch, Guid.NewGuid()),
                CancellationToken.None);
            admit.Accepted.ShouldBeFalse();
            admit.Reason.ShouldBe("workspace_reserved");
            OwnerReads(host).ShouldBe(0, "admit owner select: " + host.Counter.Roster());

            host.Counter.Reset();
            var claim = await host.Journal.TryClaimRetirementAsync(
                new WorkspaceReservationCommand(
                    key, WorkspaceReservationKind.Retirement, refOnly, RetirementId: Guid.NewGuid()),
                CancellationToken.None);
            claim.Accepted.ShouldBeFalse();
            claim.Reason.ShouldBe("workspace_in_use");
            OwnerReads(host).ShouldBe(0, "claim owner select: " + host.Counter.Roster());
        }

        // No reservation row, but the admitting task is still in the owner-id set.
        // That is the initial-refused-dispatch shape: Shared, no branch, empty ref.
        var emptyPath = WorkspaceReservationKey.NormalizePath(Path.Combine(root, "empty"));
        var emptyKey = new WorkspaceReservationKey(emptyPath, "", emptyPath);
        host.Counter.Reset();
        var admitted = await host.Journal.TryAdmitConsumerAsync(
            new WorkspaceReservationCommand(emptyKey, WorkspaceReservationKind.Launch, refOnly),
            CancellationToken.None);
        admitted.Accepted.ShouldBeTrue(host.Counter.Roster());
        admitted.Reason.ShouldBeNull();
        OwnerReads(host).ShouldBe(0, "empty-row admit owner select: " + host.Counter.Roster());
    }

    [Test]
    public async Task Ref_bearing_key_still_reads_owners_and_fences_refonly()
    {
        var root = Path.Combine(Path.GetTempPath(), "c1767-ref-" + Guid.NewGuid().ToString("N"));
        var repo = Path.Combine(root, "repo");
        const string sharedRef = "refs/heads/feat/c1767-shared";
        const string checkoutRef = "refs/heads/feat/c1767-checkout";
        var refOnlyPath = WorkspaceReservationKey.For(Path.Combine(root, "ref-only"), sharedRef, repo);
        var checkoutSharedPath = WorkspaceReservationKey.For(Path.Combine(root, "checkout-shared"), sharedRef, repo);
        var checkoutOnlyPath = WorkspaceReservationKey.For(Path.Combine(root, "checkout-only"), checkoutRef, repo);
        var refOnly = Guid.NewGuid();
        var checkout = Guid.NewGuid();
        var refOnlyRow = Guid.NewGuid();
        var checkoutSharedRow = Guid.NewGuid();
        var checkoutOnlyRow = Guid.NewGuid();

        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using (var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString)))
        {
            db.AgentTasks.Add(TaskRow(refOnly, DesktopWorkspaceKind.RefOnly, refOnlyPath.CanonicalPath));
            db.AgentTasks.Add(TaskRow(checkout, DesktopWorkspaceKind.Checkout, checkoutSharedPath.CanonicalPath));
            db.WorkspaceUseReservations.Add(Reservation(refOnlyRow, refOnlyPath, refOnly));
            db.WorkspaceUseReservations.Add(Reservation(checkoutSharedRow, checkoutSharedPath, checkout));
            db.WorkspaceUseReservations.Add(Reservation(checkoutOnlyRow, checkoutOnlyPath, checkout));
            await db.SaveChangesAsync();
        }

        await using var host = Host.Open(schema.ConnectionString);
        var crossPath = WorkspaceReservationKey.For(Path.Combine(root, "third"), "feat/c1767-shared", repo);
        host.Counter.Reset();
        var active = await host.Journal.ReadActiveAsync(crossPath, CancellationToken.None);
        Ids(active).ShouldBe(new[] { refOnlyRow }, "only the RefOnly owner is fenced across paths");
        OwnerReads(host).ShouldBe(1, "ref-bearing read still selects owners: " + host.Counter.Roster());

        var checkoutFence = WorkspaceReservationKey.For(Path.Combine(root, "fourth"), "feat/c1767-checkout", repo);
        host.Counter.Reset();
        var refused = await host.Journal.TryClaimRetirementAsync(
            new WorkspaceReservationCommand(
                checkoutFence, WorkspaceReservationKind.Retirement, refOnly, RetirementId: Guid.NewGuid()),
            CancellationToken.None);
        refused.Accepted.ShouldBeFalse();
        refused.Reason.ShouldBe("workspace_in_use", "a RefOnly requester fences a Checkout row on the same ref");
        OwnerReads(host).ShouldBe(1, "ref-bearing claim still selects owners: " + host.Counter.Roster());

        var checkoutRequester = WorkspaceReservationKey.For(Path.Combine(root, "fifth"), "feat/c1767-checkout", repo);
        host.Counter.Reset();
        var accepted = await host.Journal.TryClaimRetirementAsync(
            new WorkspaceReservationCommand(
                checkoutRequester, WorkspaceReservationKind.Retirement, checkout, RetirementId: Guid.NewGuid()),
            CancellationToken.None);
        accepted.Accepted.ShouldBeTrue(host.Counter.Roster());
        accepted.Reason.ShouldBeNull("a Checkout requester does not fence another Checkout row on a different path");
        OwnerReads(host).ShouldBe(1, "checkout claim still selects owners: " + host.Counter.Roster());
    }

    private static int OwnerReads(Host host) =>
        host.Counter.Commands.Count(sql => sql.Contains("DesktopWorkspaceKind", StringComparison.Ordinal));

    private static Guid[] Ids(IEnumerable<WorkspaceReservationSnapshot> rows) =>
        rows.Select(row => row.Id).OrderBy(id => id).ToArray();

    private static AgentTask TaskRow(Guid id, DesktopWorkspaceKind kind, string directory) => new()
    {
        Id = id,
        RootTaskId = id,
        Title = "CARD-1767 overlap",
        Goal = "Count the owner read.",
        WorkingDirectory = directory,
        DesktopWorkspaceKind = kind,
        Status = AgentTaskStatus.Queued,
        CreatedAt = DateTime.UtcNow,
    };

    private static WorkspaceUseReservation Reservation(Guid id, WorkspaceReservationKey key, Guid taskId) => new()
    {
        Id = id,
        Generation = 1,
        CanonicalPath = key.CanonicalPath,
        SourceFullRef = key.SourceFullRef,
        CommonDirectory = key.CommonDirectory,
        TaskId = taskId,
        Kind = WorkspaceReservationKind.Retirement,
        CreatedAt = DateTime.UtcNow,
        Active = true,
    };

    private sealed class Host : IAsyncDisposable
    {
        private readonly ServiceProvider _provider;
        public FullCommandCounter Counter { get; }
        public WorkspaceReservationJournal Journal { get; }

        private Host(ServiceProvider provider, FullCommandCounter counter)
        {
            _provider = provider;
            Counter = counter;
            Journal = new WorkspaceReservationJournal(
                provider.GetRequiredService<IServiceScopeFactory>(), TimeProvider.System);
        }

        public static Host Open(string connection)
        {
            var counter = new FullCommandCounter();
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddScoped(_ => new AppDbContext(TestDbFixture.CreateDbContextOptions(connection, counter)));
            return new Host(services.BuildServiceProvider(), counter);
        }

        public async ValueTask DisposeAsync() => await _provider.DisposeAsync();
    }
}
