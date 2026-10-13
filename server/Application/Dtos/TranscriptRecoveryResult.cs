namespace Antiphon.Server.Application.Dtos;

public enum TranscriptRecoveryOutcome { Unknown, CheckedWorking, RecoveredIdle }

/// <summary>Checked evidence outcome; S4 consumes it after the session commit lease is released.</summary>
public sealed record TranscriptRecoveryResult(
    TranscriptRecoveryOutcome Outcome,
    string Reason,
    DateTime? AcceptedGeneration,
    long PreStoredSequence,
    long PostStoredSequence,
    long AddedCount,
    TimeSpan Duration);
