using Antiphon.SessionRunner.Contracts;

namespace Antiphon.Server.Application.Dtos;

/// <summary>Positive evidence that the connected runner cannot tail a requested transcript format.</summary>
public sealed record RunnerCapabilityMismatch(
    string TranscriptFormat,
    RunnerCapabilitiesDto Capabilities,
    string Message);
