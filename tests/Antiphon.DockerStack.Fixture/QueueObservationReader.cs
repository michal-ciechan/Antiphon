namespace Antiphon.DockerStack.Fixture;

public sealed record QueueCandidate(
    Guid Id,
    Guid AgentSessionId,
    long Sequence,
    string Status,
    string Body,
    string Origin,
    Guid? SourceTaskId,
    Guid? SourceLandNotificationId,
    Guid? SourceScheduleId,
    string MaintenanceKind,
    int DeliveryAttempts,
    long? Floor,
    DateTime? Generation,
    DateTime? LastDeliveryStartedAt,
    string? Verdict);

public sealed class QueueObservationReader
{
    public string Isolation { get; init; } = "RepeatableRead";
    public bool ReadOnly { get; init; } = true;

    public string Sql
    {
        get
        {
            var clauses = new List<string>
            {
                "SELECT \"Id\", \"Sequence\", \"Status\", \"Body\" FROM \"SessionQueuedMessages\" WHERE 1=1",
            };
            clauses.Add("AND \"AgentSessionId\" = @session");
            clauses.Add("AND \"Sequence\" > @highWater");
            clauses.Add("AND \"Body\" = @body");
            return string.Join(' ', clauses);
        }
    }

    public IReadOnlyList<QueueCandidate> Apply(
        IEnumerable<QueueCandidate> rows, Guid sessionId, long highWater, string body)
    {
        IEnumerable<QueueCandidate> query = rows;
        if (Sql.Contains("\"AgentSessionId\" = @session", StringComparison.Ordinal))
            query = query.Where(row => row.AgentSessionId == sessionId);
        if (Sql.Contains("\"Sequence\" > @highWater", StringComparison.Ordinal))
            query = query.Where(row => row.Sequence > highWater);
        if (Sql.Contains("\"Body\" = @body", StringComparison.Ordinal))
            query = query.Where(row => string.Equals(row.Body, body, StringComparison.Ordinal));
        else if (Sql.Contains("LIKE", StringComparison.OrdinalIgnoreCase))
            query = query.Where(row => row.Body.Contains(body, StringComparison.Ordinal));
        if (Sql.Contains("AND \"Status\"", StringComparison.Ordinal))
            query = query.Where(row => row.Status == "Pending");
        return query.ToList();
    }
}
