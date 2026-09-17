namespace Antiphon.NightlyWatchdog;

/// <summary>
/// CARD-0545 D-6. Turns reader observations into tier-2 receipts. An observation imports only when, in
/// order: (1) it came from the authorized peer; (2) its marker names a ledger notification and one of its
/// attempts; (3) the marker's oid/due/kind (and the run for run-bound notifications) equal the ledger;
/// (4) SHA-256 of the whole observed text equals that attempt's body hash; (5) it is not older than the
/// attempt start minus 120 s. The first failing rule is the reason. <c>readByRecipient</c> never grants
/// <c>received</c>.
/// </summary>
public sealed class ReceiptImporter(Ledger ledger, WatchdogOptions options)
{
    public static readonly TimeSpan AttemptFloorSkew = TimeSpan.FromSeconds(120);

    public IReadOnlyList<ImportOutcome> Import(IReadOnlyList<RecipientObservation> observations, Action? beforeCommit = null)
    {
        var outcomes = new List<ImportOutcome>();
        foreach (var observation in observations)
            outcomes.Add(ImportOne(observation, beforeCommit));
        return outcomes;
    }

    private ImportOutcome ImportOne(RecipientObservation observation, Action? beforeCommit)
    {
        // Rule 1: the authorized destination's own view only.
        if (string.IsNullOrWhiteSpace(options.ReaderPeer)
            || !string.Equals(observation.PeerId, options.ReaderPeer, StringComparison.Ordinal))
            return new ImportOutcome(null, null, "peer-unauthorized", false);

        var marker = NotificationBody.ParseMarker(observation.Text);
        if (!marker.Present)
            return new ImportOutcome(marker.Nid, marker.Attempt, "marker-missing", false);

        // Rule 2: the notification and the named attempt exist in this ledger.
        var notification = ledger.Notification(marker.Nid!);
        if (notification is null)
            return new ImportOutcome(marker.Nid, marker.Attempt, "unknown-notification", false);
        var attempt = ledger.Attempts(notification.Nid).FirstOrDefault(a => a.Attempt == marker.Attempt);
        if (attempt is null)
            return new ImportOutcome(marker.Nid, marker.Attempt, "unknown-attempt", false);

        // Rule 3: outage, due day and kind identity; the run for run-bound notifications.
        var content = ledger.Content(notification.Nid);
        if (!string.Equals(marker.Oid, notification.OutageId ?? "none", StringComparison.Ordinal)
            || !string.Equals(marker.Due, content.Due, StringComparison.Ordinal)
            || !string.Equals(marker.Kind, notification.Kind, StringComparison.Ordinal))
            return new ImportOutcome(marker.Nid, marker.Attempt, "identity-mismatch", false);
        if (notification.RunId is not null && !string.Equals(marker.Run, notification.RunId, StringComparison.Ordinal))
            return new ImportOutcome(marker.Nid, marker.Attempt, "run-mismatch", false);

        // Rule 4: the whole produced payload, not the marker.
        if (!string.Equals(NotificationBody.Sha256Hex(observation.Text), attempt.BodySha256, StringComparison.Ordinal))
            return new ImportOutcome(marker.Nid, marker.Attempt, "body-mismatch", false);

        // Rule 5: evidence predating the attempt cannot confirm it.
        if (LondonClock.AsUtc(observation.DateUtc) < attempt.StartedAt - AttemptFloorSkew)
            return new ImportOutcome(marker.Nid, marker.Attempt, "predates-attempt", false);

        beforeCommit?.Invoke();
        var result = ledger.ImportReceipt(notification.Nid, attempt.Attempt, observation.MessageId, LondonClock.AsUtc(observation.DateUtc),
            WatchdogOptions.Sha256Hex(observation.PeerId)[..16], NotificationBody.Sha256Hex(observation.Text), observation.ReadByRecipient);
        return new ImportOutcome(notification.Nid, attempt.Attempt, result, result == "imported");
    }
}
