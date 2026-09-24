using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Infrastructure.Data;

namespace Antiphon.Server.Application.Services;

public sealed record ExpectationDeliveryResult(Guid NudgeId, ExpectationSendOutcome? Outcome, string Reason);

/// <summary>CARD-0650 S4 direct delivery. Scaffold: records nothing and sends nothing.</summary>
public sealed class ExpectationNudgeDeliveryService
{
    public ExpectationNudgeDeliveryService(
        AppDbContext db,
        IExpectationPromptSender sender,
        TimeProvider time,
        IExpectationCatchUp? catchUp = null)
    {
    }

    public Task<ExpectationDeliveryResult> DeliverAsync(
        ExpectationDirectiveSettings directive, Guid nudgeId, CancellationToken ct) =>
        Task.FromResult(new ExpectationDeliveryResult(nudgeId, null, "not_implemented"));
}
