using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Services;

public sealed class HostCleanupAttentionService(AppDbContext db, IOptions<HostCleanupSettings> settings,
    TimeProvider clock)
{
    public Task<IReadOnlyList<AttentionItemDto>> ReadAsync(Guid boardId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<AttentionItemDto>>([]);
}
