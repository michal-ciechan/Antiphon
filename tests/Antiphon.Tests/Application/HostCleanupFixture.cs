using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.Infrastructure;
using Antiphon.Tests.TestHelpers;
using Microsoft.Extensions.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Antiphon.Tests.Application;

internal sealed class HostCleanupServerFixture(IsolatedTestSchema schema) : IAsyncDisposable
{
    public const long GiB = 1024L * 1024 * 1024;
    public Guid BoardId { get; } = Guid.NewGuid();
    public HostCleanupClock Clock { get; } = new();
    public HostCleanupSettings Settings { get; } = new();
    public HostCleanupRecordingEvents Events { get; } = new();
    public AppDbContext Db() => new(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
    public AppDbContext Db(IInterceptor interceptor) => new(new DbContextOptionsBuilder<AppDbContext>(
        TestDbFixture.CreateDbContextOptions(schema.ConnectionString)).AddInterceptors(interceptor).Options);
    public static async Task<HostCleanupServerFixture> CreateAsync() =>
        new(await TestDbFixture.CreateIsolatedSchemaAsync());

    public HostCleanupReceiptDto Receipt(int day = 1, long worktreeBytes = 20 * GiB,
        bool complete = true, bool daily = true)
    {
        var planned = new DateTime(2026, 10, day, 10, 0, 0, DateTimeKind.Utc);
        return new(Guid.NewGuid(), BoardId, "host-a", "storage-a", "store-a", "boot-a",
            new string('a', 40), new string('b', 64), new string('c', 64),
            DateOnly.FromDateTime(planned), daily, false, complete, planned, planned.AddMinutes(1),
            10, 10 * GiB, 0, 0,
            [Candidate(worktreeBytes)], planned.AddMinutes(1), true, 10 * GiB, 1000 * GiB,
            200 * GiB, 200 * GiB);
    }

    public HostCleanupReportedCandidate Candidate(long bytes = 20 * GiB, string path = "/virtual/task-a") =>
        new(path, "storage-a", "file-a", "generation-a", "worktree", true, "would-remove",
            "eligible_existing_owner_only", "Disposable", "CARD-0692", "owner_unavailable",
            Clock.UtcNow.UtcDateTime.AddDays(-2), true, bytes, bytes, null, "inventory_only", 0);

    public async Task<Guid?> IngestAsync(HostCleanupReceiptDto receipt)
    {
        await using var db = Db();
        return await new HostCleanupService(db, Events, Clock).IngestAsync(receipt, default);
    }

    public async Task<IReadOnlyList<AttentionItemDto>> AttentionAsync(Guid? boardId = null)
    {
        await using var db = Db();
        return await new HostCleanupAttentionService(db, Options.Create(Settings), Clock)
            .ReadAsync(boardId ?? BoardId, default);
    }

    public ValueTask DisposeAsync() => schema.DisposeAsync();
}

internal sealed class HostCleanupRecordingEvents : IEventBus
{
    public bool Fail { get; set; }
    public List<string> Names { get; } = [];
    public Task PublishToGroupAsync(string group, string eventName, object payload, CancellationToken ct = default) =>
        PublishToAllAsync(eventName, payload, ct);
    public Task PublishToAllAsync(string eventName, object payload, CancellationToken ct = default)
    {
        if (Fail) throw new IOException("virtual event unavailable");
        Names.Add(eventName);
        return Task.CompletedTask;
    }
}
