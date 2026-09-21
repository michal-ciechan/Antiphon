namespace Antiphon.DockerStack.Fixture;

public sealed record DeliveryDecision(bool Accepted, string? Code, string? State);

public sealed record AttemptTuple(int Attempt, long? Floor, DateTime? Generation, DateTime? StartedAt);

public sealed record AttemptSnapshot(bool HasFile, long? Floor, DateTime? Generation);

public sealed record PromptRecord(string Uuid, string Kind, string Body, long Sequence);

public sealed record DeliveryExpectation(
    string Body,
    string BodySha256,
    Guid RecipientSessionId,
    long QueueHighWater,
    DateTime? AcceptedGeneration,
    string RunId,
    string CaseName,
    string SourceSha,
    string EvidenceDigest);

public static class DeliveryEvidenceValidator
{
    public static string NormalizePrompt(string? text)
    {
        if (text is null)
            return "";
        return text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Trim();
    }

    public static bool PromptEquals(string? expected, string? actual) =>
        string.Equals(NormalizePrompt(expected), NormalizePrompt(actual), StringComparison.Ordinal);

    public static DateTime NormalizeGeneration(DateTime value)
    {
        var ticks = value.Ticks - (value.Ticks % 10);
        return new DateTime(ticks, DateTimeKind.Utc);
    }

    public static DeliveryDecision ValidateBindings(QueueCandidate row)
    {
        if (!string.Equals(row.Origin, "Ui", StringComparison.Ordinal)
            || row.SourceTaskId is not null
            || row.SourceLandNotificationId is not null
            || row.SourceScheduleId is not null
            || !string.Equals(row.MaintenanceKind, "None", StringComparison.Ordinal))
            return new DeliveryDecision(false, "NonOrdinaryRow", null);
        return new DeliveryDecision(true, null, null);
    }

    public static DeliveryDecision ValidateFrozen(QueueCandidate row, DeliveryExpectation expectation, Guid frozenId)
    {
        if (row.Id != frozenId)
            return new DeliveryDecision(false, "RowIdentityChanged", null);
        if (!string.Equals(row.Body, expectation.Body, StringComparison.Ordinal)
            || row.Sequence <= expectation.QueueHighWater
            || row.AgentSessionId != expectation.RecipientSessionId)
            return new DeliveryDecision(false, "FrozenFieldChanged", null);
        return new DeliveryDecision(true, null, null);
    }

    public static DeliveryDecision SelectSingle(IReadOnlyList<QueueCandidate> matches, Guid? frozenId)
    {
        if (matches.Count > 1)
            return new DeliveryDecision(false, "AmbiguousQueueRows", null);
        if (matches.Count == 0)
            return new DeliveryDecision(false, "NotYetObserved", null);
        if (frozenId is Guid id && matches[0].Id != id)
            return new DeliveryDecision(false, "RowIdentityChanged", null);
        return new DeliveryDecision(true, null, null);
    }

    public static void RecordAttempt(IList<AttemptTuple> attempts, AttemptTuple tuple) =>
        attempts.Add(tuple);

    public static AttemptSnapshot ExportAttempt(QueueCandidate row)
    {
        if (row.DeliveryAttempts == 0)
            return new AttemptSnapshot(false, null, null);
        if (row.Floor is null || row.Generation is null)
            throw new InvalidOperationException("MissingAttemptFloor");
        return new AttemptSnapshot(true, row.Floor, row.Generation);
    }

    public static DeliveryDecision CompareGeneration(DateTime? expected, DateTime? runnerGeneration, DateTime? databaseGeneration)
    {
        var actual = runnerGeneration;
        if (actual is null || expected is null)
            return new DeliveryDecision(false, "GenerationUnavailable", null);
        if (NormalizeGeneration(expected.Value) != NormalizeGeneration(actual.Value))
            return new DeliveryDecision(false, "GenerationMismatch", null);
        _ = databaseGeneration;
        return new DeliveryDecision(true, null, null);
    }

    public static DeliveryDecision ValidateReceipt(
        DeliveryExpectation expectation,
        IReadOnlyList<PromptRecord> nativePrompts,
        IReadOnlyList<PromptRecord> storedPrompts,
        long? attemptFloor,
        DateTime? attemptGeneration,
        DateTime? generationAfterRead)
    {
        if (!string.Equals(DeliveryCaseIdentity.Sha256(expectation.Body), expectation.BodySha256, StringComparison.Ordinal))
            return new DeliveryDecision(false, "BodyDigestMismatch", null);
        if (!expectation.Body.Contains(expectation.RunId, StringComparison.Ordinal)
            || !expectation.Body.Contains(expectation.CaseName, StringComparison.Ordinal)
            || !expectation.Body.Contains(expectation.SourceSha, StringComparison.Ordinal)
            || !expectation.Body.Contains(expectation.EvidenceDigest, StringComparison.Ordinal))
            return new DeliveryDecision(false, "SummaryIdentityMismatch", null);

        var initial = CompareGeneration(expectation.AcceptedGeneration, attemptGeneration, databaseGeneration: null);
        if (!initial.Accepted)
            return initial;
        var finalGeneration = CompareGeneration(expectation.AcceptedGeneration, generationAfterRead, databaseGeneration: null);
        if (!finalGeneration.Accepted && finalGeneration.Code == "GenerationMismatch")
            return new DeliveryDecision(false, "GenerationChanged", null);
        if (!finalGeneration.Accepted)
            return finalGeneration;

        var native = nativePrompts.Where(prompt =>
            string.Equals(prompt.Kind, "UserPrompt", StringComparison.Ordinal)
            && PromptEquals(expectation.Body, prompt.Body)).ToList();
        if (native.Count == 0)
        {
            if (nativePrompts.Any(prompt => PromptEquals(expectation.Body, prompt.Body)))
                return new DeliveryDecision(false, "ReceiptKindMismatch", null);
            if (nativePrompts.Any(prompt => NormalizePrompt(prompt.Body).Length > 0))
                return new DeliveryDecision(false, "IncompletePrompt", null);
            return new DeliveryDecision(false, "NativePromptMissing", null);
        }

        if (native.Count > 1)
            return new DeliveryDecision(false, "DuplicateNativePrompt", null);

        var storedMatches = storedPrompts.Where(prompt =>
            string.Equals(prompt.Uuid, native[0].Uuid, StringComparison.Ordinal)).ToList();
        if (storedMatches.Count == 0)
        {
            if (storedPrompts.Any(prompt => PromptEquals(expectation.Body, prompt.Body)))
                return new DeliveryDecision(false, "ReceiptJoinMismatch", null);
            return new DeliveryDecision(false, null, "native-received/server-pending");
        }
        if (storedMatches.Count > 1)
            return new DeliveryDecision(false, "DuplicateStoredReceipt", null);
        if (!string.Equals(storedMatches[0].Kind, "UserPrompt", StringComparison.Ordinal))
            return new DeliveryDecision(false, "ReceiptKindMismatch", null);
        if (!PromptEquals(expectation.Body, storedMatches[0].Body))
            return new DeliveryDecision(false, "StoredBodyIncomplete", null);
        if (attemptFloor is null || storedMatches[0].Sequence <= attemptFloor.Value)
            return new DeliveryDecision(false, "FloorNotExceeded", null);
        return new DeliveryDecision(true, null, "delivered");
    }
}
