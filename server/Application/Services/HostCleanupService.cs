using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Infrastructure.Data;

namespace Antiphon.Server.Application.Services;

/// <summary>Protected receipt persistence only. Does not dispatch filesystem work or agents.</summary>
public sealed class HostCleanupService(AppDbContext db, IEventBus events)
{
    public Task<Guid?> IngestAsync(HostCleanupReceiptDto receipt, CancellationToken cancellationToken) =>
        Task.FromResult<Guid?>(null);

    public Task<HostCleanupReportPage?> ReadAsync(Guid boardId, Guid runId, int offset, int take,
        CancellationToken cancellationToken) => Task.FromResult<HostCleanupReportPage?>(null);

    public Task<int> PublishPendingAsync(CancellationToken cancellationToken) => Task.FromResult(0);
}
