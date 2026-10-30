using Antiphon.SessionRunner.Contracts;

namespace Antiphon.Server.Application.Dtos;

/// <summary>Actual process evidence, separate from the legacy integer adapter completion.</summary>
public sealed record AgentExitObservation(int? ExitCode, int? ExitSignal = null, ProcessStopIntentDto? ExitStopIntent = null);
