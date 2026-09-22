using System.Collections.Concurrent;
using Antiphon.SessionRunner.Contracts;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// CARD-0604 G-21. Carries a REMOTE session's spilled body from the point it is spilled to the
/// Input frame that types its pointer, so the body travels in the Input payload and the RUNNER
/// writes the file inside the session's own cwd.
///
/// The desktop must never write it: a remote session's <c>Cwd</c> is a Windows path it cannot see,
/// so a desktop write leaves a file no one reads behind a prompt pointing at nothing — the agent
/// is told "read it in full before you do anything else" about a path that does not exist.
///
/// The staged body is one-shot and per session: taking it clears it, so a retyped pointer after a
/// composer-evidence retry does not rewrite the file, and a body left staged by a delivery that
/// never happened is replaced by the next spill rather than accumulating.
/// </summary>
public sealed class RemoteSpillCourier
{
    private readonly ConcurrentDictionary<Guid, StagedSpill> _staged = new();

    public void Stage(Guid sessionId, string runnerCwd, PhoneHomeInputSpill spill) =>
        _staged[sessionId] = new StagedSpill(runnerCwd, spill);

    public bool TryTake(Guid sessionId, out StagedSpill staged) =>
        _staged.TryRemove(sessionId, out staged!);

    /// <summary>
    /// CARD-0604 D-3. Look at the staged body WITHOUT clearing it. Taking it before the Input
    /// frame is acknowledged loses it outright when the phone-home socket drops mid-request:
    /// the body is gone from memory, the runner never wrote the file, and the retry types a
    /// pointer at a path that does not exist. Pair this with <see cref="Ack"/>.
    /// </summary>
    public bool TryPeek(Guid sessionId, out StagedSpill staged) =>
        _staged.TryGetValue(sessionId, out staged!);

    /// <summary>
    /// CARD-0604 D-3. Clear the staged body now that the runner has acknowledged writing it.
    /// Only the body that was actually delivered is removed: a newer spill staged while the
    /// frame was in flight stays, so it is not silently dropped by a late acknowledgement.
    /// </summary>
    public bool Ack(Guid sessionId, StagedSpill delivered) =>
        _staged.TryRemove(new KeyValuePair<Guid, StagedSpill>(sessionId, delivered));

    public bool IsStaged(Guid sessionId) => _staged.ContainsKey(sessionId);

    public void Clear(Guid sessionId) => _staged.TryRemove(sessionId, out _);

    public sealed record StagedSpill(string RunnerCwd, PhoneHomeInputSpill Spill);
}
