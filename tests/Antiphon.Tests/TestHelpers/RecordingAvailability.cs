using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Domain.Enums;

namespace Antiphon.Tests.TestHelpers;

/// <summary>
/// CARD-0552 M-9. An <see cref="IModelAvailability"/> that answers a fixed verdict and RECORDS the
/// (kind, alias) it was asked about, so a test can assert both that the gate ran and that it asked
/// about the route the create would actually resolve through.
/// </summary>
internal sealed class RecordingAvailability : IModelAvailability
{
    public bool Held { get; set; }

    public List<(AgentKind Kind, string Alias)> Calls { get; } = [];

    public Task<bool> IsHeldAsync(AgentKind kind, string alias, CancellationToken ct)
    {
        Calls.Add((kind, alias));
        return Task.FromResult(Held);
    }
}
