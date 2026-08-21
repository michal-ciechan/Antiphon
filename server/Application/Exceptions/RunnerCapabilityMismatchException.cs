namespace Antiphon.Server.Application.Exceptions;

/// <summary>Refuses a launch that the connected runner explicitly says it cannot observe.</summary>
public sealed class RunnerCapabilityMismatchException(string message) : Exception(message);
