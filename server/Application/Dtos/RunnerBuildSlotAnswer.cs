using Antiphon.SessionRunner.Contracts;

namespace Antiphon.Server.Application.Dtos;

/// <summary>
/// CARD-0589: the session runner's answer to <c>POST /build-slots</c> — exactly one of a grant, a
/// busy refusal or a memory-floor refusal. A null answer (not this record) means the runner could
/// not be asked: unreachable, or too old to have the route.
/// </summary>
public sealed record RunnerBuildSlotAnswer(
    BuildSlotGrant? Grant,
    BuildSlotBusy? Busy = null,
    BuildSlotMemoryFloor? MemoryFloor = null);
