using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Services;

/// <summary>CARD-0514 D-1: map a child probe to Armed / Unarmed / Unknown. Unknown never authorizes a write.</summary>
public static class RemoteControlBridgeClassifier
{
    public static RemoteControlBridgeState Classify(RcProbeResult? probe, int? pid, bool probeFailed)
    {
        if (probeFailed || pid is null || probe is null || !probe.StateFileFound)
            return RemoteControlBridgeState.Unknown;
        return probe.Armed ? RemoteControlBridgeState.Armed : RemoteControlBridgeState.Unarmed;
    }
}
