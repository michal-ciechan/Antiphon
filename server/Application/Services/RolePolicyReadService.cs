using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Settings;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Services;

/// <summary>CARD-0097 S1: the read model behind GET /api/role-policy.</summary>
public sealed class RolePolicyReadService
{
    private readonly DelegationSettings _settings;
    private readonly RoutingPinService _pins;

    public RolePolicyReadService(IOptions<DelegationSettings> settings, RoutingPinService pins)
    {
        _settings = settings.Value;
        _pins = pins;
    }

    public Task<RolePolicyDto> ReadAsync(CancellationToken ct) =>
        throw new NotImplementedException();

    public static RolePolicyDto Compose(DelegationSettings settings, IReadOnlyList<RoutingPinDto> pins) =>
        throw new NotImplementedException();
}
