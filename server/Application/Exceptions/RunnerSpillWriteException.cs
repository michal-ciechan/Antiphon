namespace Antiphon.Server.Application.Exceptions;

/// <summary>The runner positively refused a spill before this Input frame typed any bytes.</summary>
public sealed class RunnerSpillWriteException() : Exception("Runner spill write failed before input.");
