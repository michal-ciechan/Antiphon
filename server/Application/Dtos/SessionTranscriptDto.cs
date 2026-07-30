namespace Antiphon.Server.Application.Dtos;

/// <summary>Structured transcript for a session — the lossless counterpart to the raw terminal buffer.</summary>
public sealed record SessionTranscriptDto(
    Guid SessionId,
    IReadOnlyList<TranscriptEntryDto> Entries,
    long LastSequence);

public sealed record TranscriptEntryDto(
    long Sequence,
    string Kind,
    string? Uuid,
    string? ParentUuid,
    DateTime? Timestamp,
    string? Role,
    string? Text,
    string? ToolName,
    string? ToolInput,
    string? ToolUseId,
    bool? ToolIsError,
    string? StopReason,
    // API-call attribution: entries of one API call share ApiCallId and repeat identical usage —
    // group by ApiCallId and count usage once per call.
    string? ApiCallId = null,
    int? InputTokens = null,
    int? OutputTokens = null,
    int? CacheReadTokens = null,
    int? CacheCreationTokens = null);
