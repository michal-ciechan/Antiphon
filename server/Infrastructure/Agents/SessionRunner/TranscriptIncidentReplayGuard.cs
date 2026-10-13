using Antiphon.Server.Application.Dtos;

namespace Antiphon.Server.Infrastructure.Agents.SessionRunner;

// CARD-1219 tests-first stub: admits everything. Replaced by the real guard in the S2 commit.
internal sealed class TranscriptIncidentReplayGuard(TimeProvider clock, TimeSpan window, int capacity = 512)
{
    public TimeSpan Window => window;
    public int Capacity => capacity;
    public TimeProvider Clock => clock;

    public bool Admit(SessionRunnerTranscriptFaultEvent fault) => true;

    public bool Admit(SessionRunnerTranscriptBoundEvent bound) => true;
}
