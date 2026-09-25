namespace Antiphon.Server.Application.Dtos;

/// <summary>CARD-0716 D-2. Optional body of POST /api/operator/shutdown.</summary>
public sealed record OperatorShutdownRequest(string? Reason);

/// <summary>CARD-0716 D-2. 202 body. The process id is the server the caller waits on.</summary>
public sealed record OperatorShutdownDto(bool Accepted, int Pid, int DrainSeconds, int StartingLaunches);
