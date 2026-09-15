using System.Text;
using System.Text.Json;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Domain.Entities;

namespace Antiphon.Server.Application.Services;

public sealed class WorktreeCleanupPresentation
{
    public const int CaptureByteLimit = 32768;
    public const int SummaryLimit = 600;
    public const int DetailLimit = 950;

    public string Serialize(WorktreeCleanupCapture capture)
    {
        var json = JsonSerializer.Serialize(capture);
        while (Encoding.UTF8.GetByteCount(json) > CaptureByteLimit)
        {
            if (capture.Handles.Owners.Count > 1)
                capture = capture with { Handles = capture.Handles with {
                    Owners = capture.Handles.Owners.SkipLast(1).ToArray(),
                    OmittedOwners = capture.Handles.OmittedOwners + 1, Status = WorktreeLockStatus.Partial },
                    Omitted = capture.Omitted + 1 };
            else if (capture.Native.Observations.Count > 1)
            {
                // Preserve the first qualifying observation even when it is the last candidate.
                var observations = capture.Native.Observations.ToList();
                var qualifying = observations.FindIndex(WorktreeNativeSnapshot.IsSharingConflict);
                observations.RemoveAt(qualifying == observations.Count - 1 ? observations.Count - 2 : observations.Count - 1);
                capture = capture with { Native = capture.Native with {
                    Observations = observations, Status = WorktreeLockStatus.Partial },
                    Omitted = capture.Omitted + 1 };
            }
            else throw new ArgumentException("cleanup_capture_too_large");
            json = JsonSerializer.Serialize(capture);
        }
        return json;
    }

    public string Summary(WorktreeCleanupCapture capture)
    {
        var owner = capture.Handles.Owners.FirstOrDefault();
        var minimum = $"capture={capture.Id:N} at {capture.At:O}; "
            + (owner is null ? $"Handle {capture.Handles.Status}/{capture.Handles.Reason}"
                : $"{Clip(owner.Name, 80)} PID={owner.ProcessId}");
        var sharing = capture.Native.Observations.FirstOrDefault(WorktreeNativeSnapshot.IsSharingConflict);
        var facts = $"; Git {capture.GitFailure.GeneratedCode} exit={capture.GitFailure.ExitCode?.ToString() ?? "unknown"}"
            + $"; DeleteAccessOpen={(sharing is null ? capture.Native.Reason : sharing.NativeErrorCode.ToString())}"
            + $"; owners={capture.Handles.Owners.Count} omitted={capture.Omitted + capture.Handles.OmittedOwners}";
        return Clip(minimum + facts + (owner is null ? "" : $"; path={Clip(owner.RelativePath, 512)}"), SummaryLimit);
    }

    public string Detail(string reason, WorktreeCleanupReference? capture)
    {
        if (capture is null) return Clip(reason, DetailLimit);
        var diagnostic = capture.Summary ?? $"capture={capture.AttemptId:N} {capture.State}";
        var provenance = capture.PriorAttempt ? $"prior attempt request={capture.RequestId:N}; " : "";
        return Clip(Clip(reason, 250) + "; " + provenance + Clip(diagnostic, SummaryLimit), DetailLimit);
    }

    public WorktreeCleanupReference Reference(WorktreeCleanupAttempt attempt, bool prior = false) =>
        new(attempt.Id, attempt.RequestId, attempt.OperationId, attempt.CaptureAt,
            attempt.CaptureState, attempt.Summary, attempt.CaptureJson, prior);

    internal (string Required, string Optional) NotificationParts(WorktreeCleanupReference reference,
        string reason, int requiredByteLimit)
    {
        // Build from the committed fields, not the display summary (which may already have
        // clipped a path or a diagnostic). Reserve every fixed fact before bounding the name.
        WorktreeCleanupCapture? capture = null;
        try
        {
            if (reference.CaptureJson is not null)
                capture = JsonSerializer.Deserialize<WorktreeCleanupCapture>(reference.CaptureJson);
        }
        catch (JsonException) { /* Retain the reference and report unavailable evidence. */ }
        var identity = $"capture={reference.AttemptId:N} at {reference.At:O}; "
            + (reference.PriorAttempt ? $"prior attempt request={reference.RequestId:N}; " : "");
        if (capture?.Handles?.Owners is null || capture.Native?.Observations is null || capture.GitFailure is null
            || capture.Id != reference.AttemptId || capture.RequestId != reference.RequestId
            || capture.OperationId != reference.OperationId)
            return (identity + $"{reference.State}; diagnostic evidence unavailable",
                $"{reason}; {reference.Summary}");

        var sharing = capture.Native.Observations.FirstOrDefault(WorktreeNativeSnapshot.IsSharingConflict);
        var facts = $"; Git {ClipUtf8(capture.GitFailure.GeneratedCode, 40)} exit={capture.GitFailure.ExitCode?.ToString() ?? "unknown"}"
            + $"; DeleteAccessOpen={ClipUtf8(sharing is null ? capture.Native.Reason : sharing.NativeErrorCode.ToString()!, 40)}";
        var owner = capture.Handles.Owners.FirstOrDefault();
        var pid = owner is null ? "" : $" PID={owner.ProcessId}";
        var nameBudget = requiredByteLimit - Encoding.UTF8.GetByteCount(identity + pid + facts);
        var name = owner is null ? $"Handle {capture.Handles.Status}/{capture.Handles.Reason}" : owner.Name;
        var required = identity + ClipUtf8(name, Math.Min(owner is null ? 120 : 240, nameBudget)) + pid + facts;
        var optional = $"{reason}; owners={capture.Handles.Owners.Count} omitted={capture.Omitted + capture.Handles.OmittedOwners}"
            + (owner is null ? "" : $"; path={owner.RelativePath}");
        return (required, optional);
    }

    public static string ClipUtf8(string text, int byteLimit)
    {
        if (Encoding.UTF8.GetByteCount(text) <= byteLimit) return text;
        var result = new StringBuilder();
        var bytes = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            if (bytes + rune.Utf8SequenceLength > byteLimit - 1) break;
            result.Append(rune.ToString());
            bytes += rune.Utf8SequenceLength;
        }
        return result.Append('~').ToString();
    }

    public static string Clip(string text, int limit) => text.Length <= limit ? text : text[..(limit - 1)] + "~";
}
