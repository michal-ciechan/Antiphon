namespace Antiphon.Server.Domain.Enums;

/// <summary>Who captured a standing pin. Assigned by the server, never by the caller body.</summary>
public enum PinInstructionSource
{
    Operator = 0,
    Agent = 1
}
