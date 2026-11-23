using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Review;

/// <summary>
/// The scoped <see cref="IReviewAgentRunner"/>: hands each call to the transport
/// <c>Review:Agents:Transport</c> names. It is deliberately not disposable. A scoped factory that
/// returned the singleton <see cref="CliReviewAgentRunner"/> directly made every scope dispose it
/// when the scope ended: the executor's pass ended, and its <c>Dispose</c> cancelled the reviewer
/// that the pass had just started. The singleton is now disposed only with the container, at
/// shutdown.
/// </summary>
public sealed class SelectedReviewAgentRunner(IServiceProvider services, IOptions<ReviewAgentSettings> settings) : IReviewAgentRunner
{
    public Task<ReviewAgentResult> RunAsync(ReviewAgentRequest request, CancellationToken ct) =>
        Selected().RunAsync(request, ct);

    private IReviewAgentRunner Selected() =>
        ReviewAgentTransports.IsCli(settings.Value.Transport)
            ? services.GetRequiredService<CliReviewAgentRunner>()
            : services.GetRequiredService<DelegationReviewAgentRunner>();
}
