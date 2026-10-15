using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Infrastructure.Data;

namespace Antiphon.Server.Application.Services;

// Compile-only contract for the red-first acceptance test. Implemented in the next slice.
public sealed class CardCompletionAcceptanceService(AppDbContext db, TimeProvider clock)
{
    public Task<CardCompletionAcceptance?> WriteAsync(Guid cardId, WriteCardCompletionAcceptanceRequest request,
        string actor, CancellationToken ct) => Task.FromResult<CardCompletionAcceptance?>(null);

    public Task<CardCompletionView> GetAsync(Guid cardId, CancellationToken ct) =>
        Task.FromResult(new CardCompletionView(null, null, null));
}
