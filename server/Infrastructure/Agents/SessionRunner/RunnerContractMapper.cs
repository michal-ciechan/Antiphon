using System.Text.Json;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Domain.Enums;
using Antiphon.SessionRunner.Contracts;

namespace Antiphon.Server.Infrastructure.Agents.SessionRunner;

public sealed class RunnerContractMapper
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public RunnerLaunchRequest ToLaunchRequest(Guid sessionId, AgentLaunchSpec spec) =>
        new(
            sessionId,
            spec.Exe,
            spec.Args,
            spec.Env,
            spec.Cwd,
            spec.Cols,
            spec.Rows,
            spec.MemoryLimitMb,
            TranscriptEnabled: SessionRunnerHttpClient.TranscriptEnabledFor(spec.Kind),
            TranscriptFormat: SessionRunnerHttpClient.TranscriptFormatFor(spec.Kind),
            Backend: SessionRunnerHttpClient.BackendWire(spec.Backend),
            Herdr: spec.Herdr,
            GrokRulesPayload: spec.GrokRulesPayload,
            CommandLineBudgetChars: spec.CommandLineBudgetChars,
            VerificationBinding: spec.VerificationBinding,
            AcceptedStartedAt: spec.AcceptedStartedAt);

    public SessionRunnerSessionDto Map(RunnerSessionDto dto) =>
        new(
            dto.SessionId,
            dto.Pid,
            dto.StartedAt,
            dto.Status,
            dto.ExitCode,
            MapExitReason(dto.ExitReason),
            dto.LastSequence,
            dto.HostPid,
            dto.Adopted,
            dto.AgentStatus,
            dto.AgentStatusSinceUtc,
            dto.TranscriptBound,
            dto.TranscriptBindHow,
            dto.TranscriptUnboundReason,
            dto.Backend,
            dto.Pending,
            dto.HerdrVerifiedAtUtc,
            dto.HerdrOrigin,
            dto.GrokRulesReceipt,
            dto.AcceptedStartedAt,
            dto.LabelObservation is { Version: 1, Intent.Version: 1 } observation ? observation : null);

    public SessionRunnerTranscriptDto MapTranscript(RunnerTranscriptDto transcript) =>
        new(transcript.SessionId, transcript.Entries.Select(MapTranscript).ToList(), transcript.LastSequence);

    public static SessionRunnerTranscriptEvent MapTranscript(RunnerTranscriptEvent e) =>
        new(
            e.SessionId, e.Sequence, e.Kind, e.Uuid, e.ParentUuid, e.Timestamp, e.Role, e.Text,
            e.ToolName, e.ToolInput, e.ToolUseId, e.ToolIsError, e.StopReason, e.ApiCallId,
            e.InputTokens, e.OutputTokens, e.CacheReadTokens, e.CacheCreationTokens,
            e.IsApiError, e.ApiErrorClass, e.ApiErrorStatus, e.Model, e.ModelCalls);

    public static AgentExitReason MapExitReason(string reason) =>
        Enum.TryParse<AgentExitReason>(reason, ignoreCase: true, out var parsed)
            ? parsed
            : AgentExitReason.Unknown;

    public static SessionRunnerEvent? ParseEvent(string eventName, string json)
    {
        if (eventName == SessionRunnerEventNames.SessionOutput)
        {
            var output = JsonSerializer.Deserialize<RunnerOutputEvent>(json, Json);
            return output is null
                ? null
                : new SessionRunnerEvent(
                    eventName, output.SessionId,
                    Output: new SessionRunnerOutputEvent(output.SessionId, output.Sequence, output.Text));
        }

        if (eventName == SessionRunnerEventNames.SessionExited)
        {
            var exited = JsonSerializer.Deserialize<RunnerSessionExitedEvent>(json, Json);
            return exited is null
                ? null
                : new SessionRunnerEvent(
                    eventName, exited.SessionId,
                    Exited: new SessionRunnerExitedEvent(
                        exited.SessionId, exited.ExitCode, MapExitReason(exited.ExitReason),
                        exited.LastSequence, exited.AcceptedStartedAt));
        }

        if (eventName == SessionRunnerEventNames.SessionAdopted)
        {
            var adopted = JsonSerializer.Deserialize<RunnerSessionAdoptedEvent>(json, Json);
            return adopted is null
                ? null
                : new SessionRunnerEvent(
                    eventName, adopted.SessionId,
                    Adopted: new SessionRunnerAdoptedEvent(
                        adopted.SessionId, adopted.Pid, adopted.LastSequence, adopted.AcceptedStartedAt));
        }

        if (eventName == SessionRunnerEventNames.SessionStarted)
        {
            var started = JsonSerializer.Deserialize<RunnerSessionStartedEvent>(json, Json);
            return started is null ? null : new SessionRunnerEvent(eventName, started.SessionId);
        }

        if (eventName == SessionRunnerEventNames.SessionTranscript)
        {
            var entry = JsonSerializer.Deserialize<RunnerTranscriptEvent>(json, Json);
            return entry is null
                ? null
                : new SessionRunnerEvent(eventName, entry.SessionId, Transcript: MapTranscript(entry));
        }

        return null;
    }
}
