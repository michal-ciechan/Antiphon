using System.Collections.Concurrent;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// CARD-1470/1467: the land verifier's live build-slot memory state per land request, read by the
/// land status, the LandAged warning, and the attention headline. Process-local like <see cref="AgentTaskLandQueue"/>: the verifier sets it while it
/// waits on the memory floor or builds with the fallback, and clears it when it holds a slot or ends.
/// The durable record is the land operation's <c>build-slot=</c> note.
/// </summary>
public sealed class LandVerificationWaits(TimeProvider clock)
{
    private readonly ConcurrentDictionary<Guid, LandVerificationWaitDto> _waits = new();

    /// <summary>Records the verifier's memory state for <paramref name="requestId"/>; null clears it.</summary>
    public void Set(Guid requestId, BuildSlotMemoryWait? wait)
    {
        if (wait is null)
        {
            _waits.TryRemove(requestId, out _);
            return;
        }
        _waits[requestId] = new LandVerificationWaitDto(
            wait.Fallback ? LandVerificationWaitKind.MemoryFloorFallback : LandVerificationWaitKind.WaitingForMemory,
            wait.AvailableMb, wait.FloorMb, wait.Waited.TotalSeconds, wait.MaxCpuCount, clock.GetUtcNow().UtcDateTime);
    }

    public LandVerificationWaitDto? Find(Guid requestId) => _waits.TryGetValue(requestId, out var wait) ? wait : null;
}
