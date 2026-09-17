using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;

namespace Antiphon.NightlyWatchdog;

/// <summary>
/// CARD-0545 D-4: the watchdog's durable ledger, a SQLite file in its own state directory (off the
/// Windows host, outside Windmill's database). Intent (outage + notification + attempt 1) is one
/// transaction committed before any network call; receipts are unique on <c>(nid, messageId)</c>.
/// A ledger is bound to exactly one namespace.
/// </summary>
public sealed class Ledger : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly string _connectionString;
    private readonly TimeProvider _clock;
    private readonly Func<string> _nidFactory;

    public string Path { get; }
    public string Namespace { get; }
    public string InstanceId { get; }

    public Ledger(string path, string @namespace, TimeProvider clock, Func<string>? nidFactory = null)
    {
        Path = path;
        Namespace = @namespace;
        _clock = clock;
        _nidFactory = nidFactory ?? (() => Ulid.New(_clock.GetUtcNow()));
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path))!);
        _connectionString = new SqliteConnectionStringBuilder { DataSource = path, Pooling = true }.ToString();
        _connection = new SqliteConnection(_connectionString);
        _connection.Open();
        Exec("PRAGMA journal_mode=WAL;");
        Exec("PRAGMA busy_timeout=5000;");
        Exec("""
            CREATE TABLE IF NOT EXISTS heartbeat (
              id INTEGER PRIMARY KEY CHECK (id = 1),
              namespace TEXT NOT NULL,
              instanceId TEXT NOT NULL,
              heartbeatAt TEXT NULL,
              destinationHash TEXT NULL,
              lastDueDay TEXT NULL,
              stateJson TEXT NULL);
            CREATE TABLE IF NOT EXISTS counters (key TEXT PRIMARY KEY, value INTEGER NOT NULL);
            CREATE TABLE IF NOT EXISTS probes (id INTEGER PRIMARY KEY AUTOINCREMENT, at TEXT NOT NULL, json TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS outages (
              outageId TEXT PRIMARY KEY,
              kind TEXT NOT NULL,
              dueDay TEXT NOT NULL,
              epoch INTEGER NOT NULL,
              openedAt TEXT NOT NULL,
              closedAt TEXT NULL,
              jobId TEXT NULL,
              runId TEXT NULL,
              failureNid TEXT NULL,
              recoveryNid TEXT NULL,
              evidenceJson TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS notifications (
              nid TEXT PRIMARY KEY,
              kind TEXT NOT NULL,
              outageId TEXT NULL,
              linkedNid TEXT NULL,
              runId TEXT NULL,
              state TEXT NOT NULL,
              createdAt TEXT NOT NULL,
              receivedAt TEXT NULL,
              contentJson TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS attempts (
              nid TEXT NOT NULL,
              attempt INTEGER NOT NULL,
              startedAt TEXT NOT NULL,
              accepted INTEGER NOT NULL DEFAULT 0,
              acceptedAt TEXT NULL,
              messageId TEXT NULL,
              errorClass TEXT NULL,
              error TEXT NULL,
              retryAfterUtc TEXT NULL,
              body TEXT NOT NULL,
              bodySha256 TEXT NOT NULL,
              PRIMARY KEY (nid, attempt));
            CREATE TABLE IF NOT EXISTS receipts (
              nid TEXT NOT NULL,
              attempt INTEGER NOT NULL,
              messageId TEXT NOT NULL,
              dateUtc TEXT NOT NULL,
              peerHash TEXT NOT NULL,
              textSha256 TEXT NOT NULL,
              readObservedAt TEXT NULL,
              importedAt TEXT NOT NULL);
            CREATE UNIQUE INDEX IF NOT EXISTS receipts_nid_message ON receipts (nid, messageId);
            """);

        using var read = Command("SELECT namespace, instanceId FROM heartbeat WHERE id = 1");
        using var reader = read.ExecuteReader();
        if (reader.Read())
        {
            var stored = reader.GetString(0);
            if (!string.Equals(stored, @namespace, StringComparison.Ordinal))
            {
                reader.Close();
                read.Dispose();
                _connection.Dispose();
                SqliteConnection.ClearPool(new SqliteConnection(_connectionString));
                throw new LedgerNamespaceMismatchException(stored, @namespace);
            }
            InstanceId = reader.GetString(1);
        }
        else
        {
            reader.Close();
            InstanceId = "wd-" + Ulid.New(_clock.GetUtcNow()).ToLowerInvariant();
            using var insert = Command("INSERT INTO heartbeat (id, namespace, instanceId) VALUES (1, $ns, $id)");
            insert.Parameters.AddWithValue("$ns", @namespace);
            insert.Parameters.AddWithValue("$id", InstanceId);
            insert.ExecuteNonQuery();
        }
    }

    // ---------------- writes ----------------

    /// <summary>D-4: outage row, notification (pending) and attempt 1 in one transaction.</summary>
    public (OutageRow Outage, NotificationRow Notification, AttemptRow Attempt) OpenOutageWithIntent(
        string kind, string dueDay, string? jobId, string? runId, JsonObject evidence, Func<string, NotificationContent> content)
    {
        var now = Now();
        using var tx = _connection.BeginTransaction();
        int epoch;
        using (var max = Command("SELECT COALESCE(MAX(epoch), 0) FROM outages WHERE kind = $k AND dueDay = $d", tx))
        {
            max.Parameters.AddWithValue("$k", kind);
            max.Parameters.AddWithValue("$d", dueDay);
            epoch = Convert.ToInt32(max.ExecuteScalar(), CultureInfo.InvariantCulture) + 1;
        }
        var outageId = $"nw:{Namespace}:{dueDay}:{kind}:{epoch}";
        using (var insert = Command("""
            INSERT INTO outages (outageId, kind, dueDay, epoch, openedAt, jobId, runId, evidenceJson)
            VALUES ($id, $k, $d, $e, $at, $job, $run, $ev)
            """, tx))
        {
            insert.Parameters.AddWithValue("$id", outageId);
            insert.Parameters.AddWithValue("$k", kind);
            insert.Parameters.AddWithValue("$d", dueDay);
            insert.Parameters.AddWithValue("$e", epoch);
            insert.Parameters.AddWithValue("$at", Iso(now));
            insert.Parameters.AddWithValue("$job", (object?)jobId ?? DBNull.Value);
            insert.Parameters.AddWithValue("$run", (object?)runId ?? DBNull.Value);
            insert.Parameters.AddWithValue("$ev", evidence.ToJsonString());
            insert.ExecuteNonQuery();
        }
        var nid = _nidFactory();
        var notification = InsertNotification(tx, nid, "failure", outageId, null, runId, content(outageId), now);
        var attempt = InsertAttempt(tx, nid, 1, content(outageId), now);
        using (var link = Command("UPDATE outages SET failureNid = $nid WHERE outageId = $id", tx))
        {
            link.Parameters.AddWithValue("$nid", nid);
            link.Parameters.AddWithValue("$id", outageId);
            link.ExecuteNonQuery();
        }
        tx.Commit();
        return (Outage(outageId)!, notification, attempt);
    }

    /// <summary>D-8: closure and the recovery notification (own nid, linkedNid, attempt 1) in one transaction.</summary>
    public NotificationRow CloseOutageWithRecovery(string outageId, JsonObject closeEvidence,
        Func<OutageRow, bool, NotificationContent> content)
    {
        var now = Now();
        var outage = Outage(outageId) ?? throw new InvalidOperationException($"unknown outage {outageId}");
        var failure = outage.FailureNid is null ? null : Notification(outage.FailureNid);
        var failureReceived = failure?.State == "received";
        using var tx = _connection.BeginTransaction();
        var evidence = JsonNode.Parse(outage.EvidenceJson) as JsonObject ?? new JsonObject();
        foreach (var (key, value) in closeEvidence)
            evidence[key] = value?.DeepClone();
        using (var close = Command("UPDATE outages SET closedAt = $at, evidenceJson = $ev WHERE outageId = $id AND closedAt IS NULL", tx))
        {
            close.Parameters.AddWithValue("$at", Iso(now));
            close.Parameters.AddWithValue("$ev", evidence.ToJsonString());
            close.Parameters.AddWithValue("$id", outageId);
            close.ExecuteNonQuery();
        }
        var nid = _nidFactory();
        var body = content(outage, failureReceived);
        var recovery = InsertNotification(tx, nid, "recovery", outageId, outage.FailureNid, outage.RunId, body, now);
        InsertAttempt(tx, nid, 1, body, now);
        using (var link = Command("UPDATE outages SET recoveryNid = $nid WHERE outageId = $id", tx))
        {
            link.Parameters.AddWithValue("$nid", nid);
            link.Parameters.AddWithValue("$id", outageId);
            link.ExecuteNonQuery();
        }
        tx.Commit();
        return recovery;
    }

    public void AmendOutage(string outageId, Action<JsonObject> mutate)
    {
        var outage = Outage(outageId) ?? throw new InvalidOperationException($"unknown outage {outageId}");
        var evidence = JsonNode.Parse(outage.EvidenceJson) as JsonObject ?? new JsonObject();
        mutate(evidence);
        using var update = Command("UPDATE outages SET evidenceJson = $ev WHERE outageId = $id");
        update.Parameters.AddWithValue("$ev", evidence.ToJsonString());
        update.Parameters.AddWithValue("$id", outageId);
        update.ExecuteNonQuery();
    }

    /// <summary>DL-545-C: a ledger-backed notice with no outage row (the qualification notice).</summary>
    public NotificationRow OpenNoticeWithIntent(NotificationContent content)
    {
        var now = Now();
        using var tx = _connection.BeginTransaction();
        var nid = _nidFactory();
        var row = InsertNotification(tx, nid, content.Kind, null, null, null, content, now);
        InsertAttempt(tx, nid, 1, content, now);
        tx.Commit();
        return row;
    }

    /// <summary>A retry attempt's intent, committed before its send.</summary>
    public AttemptRow CreateAttempt(string nid, int attempt)
    {
        var content = Content(nid);
        using var tx = _connection.BeginTransaction();
        var row = InsertAttempt(tx, nid, attempt, content, Now());
        tx.Commit();
        return row;
    }

    public void RecordAttempt(string nid, int attempt, TransportResult result)
    {
        var now = Now();
        using var tx = _connection.BeginTransaction();
        using (var update = Command("""
            UPDATE attempts SET accepted = $acc, acceptedAt = $accAt, messageId = $msg, errorClass = $cls, error = $err, retryAfterUtc = $retry
            WHERE nid = $nid AND attempt = $attempt
            """, tx))
        {
            update.Parameters.AddWithValue("$acc", result.Accepted ? 1 : 0);
            update.Parameters.AddWithValue("$accAt", result.Accepted ? Iso(now) : DBNull.Value);
            update.Parameters.AddWithValue("$msg", (object?)result.MessageId ?? DBNull.Value);
            update.Parameters.AddWithValue("$cls", result.Accepted ? DBNull.Value : (object?)(result.ErrorClass ?? "transport") ?? DBNull.Value);
            update.Parameters.AddWithValue("$err", (object?)result.Error ?? DBNull.Value);
            update.Parameters.AddWithValue("$retry", result.RetryAfterSeconds is { } s ? Iso(now.AddSeconds(s)) : DBNull.Value);
            update.Parameters.AddWithValue("$nid", nid);
            update.Parameters.AddWithValue("$attempt", attempt);
            update.ExecuteNonQuery();
        }
        if (result.Accepted)
        {
            // Tier 1: Telegram accepted the message. Never 'received' (D-5, PC-81).
            using var state = Command("UPDATE notifications SET state = 'sent' WHERE nid = $nid AND state = 'pending'", tx);
            state.Parameters.AddWithValue("$nid", nid);
            state.ExecuteNonQuery();
        }
        tx.Commit();
    }

    /// <summary>D-6 tier 2. Idempotent on (nid, messageId). Returns <c>imported</c> or <c>duplicate</c>.</summary>
    public string ImportReceipt(string nid, int attempt, string messageId, DateTime dateUtc, string peerHash, string textSha256,
        bool readByRecipient)
    {
        var now = Now();
        using var tx = _connection.BeginTransaction();
        int inserted;
        using (var insert = Command("""
            INSERT INTO receipts (nid, attempt, messageId, dateUtc, peerHash, textSha256, readObservedAt, importedAt)
            VALUES ($nid, $attempt, $msg, $date, $peer, $sha, $read, $at)
            ON CONFLICT (nid, messageId) DO NOTHING
            """, tx))
        {
            insert.Parameters.AddWithValue("$nid", nid);
            insert.Parameters.AddWithValue("$attempt", attempt);
            insert.Parameters.AddWithValue("$msg", messageId);
            insert.Parameters.AddWithValue("$date", Iso(dateUtc));
            insert.Parameters.AddWithValue("$peer", peerHash);
            insert.Parameters.AddWithValue("$sha", textSha256);
            insert.Parameters.AddWithValue("$read", readByRecipient ? Iso(now) : DBNull.Value);
            insert.Parameters.AddWithValue("$at", Iso(now));
            inserted = insert.ExecuteNonQuery();
        }
        if (inserted == 0 && readByRecipient)
        {
            using var read = Command("UPDATE receipts SET readObservedAt = $at WHERE nid = $nid AND messageId = $msg AND readObservedAt IS NULL", tx);
            read.Parameters.AddWithValue("$at", Iso(now));
            read.Parameters.AddWithValue("$nid", nid);
            read.Parameters.AddWithValue("$msg", messageId);
            read.ExecuteNonQuery();
        }
        using (var state = Command("UPDATE notifications SET state = 'received', receivedAt = $at WHERE nid = $nid AND state <> 'received'", tx))
        {
            state.Parameters.AddWithValue("$at", Iso(now));
            state.Parameters.AddWithValue("$nid", nid);
            state.ExecuteNonQuery();
        }
        tx.Commit();
        return inserted == 0 ? "duplicate" : "imported";
    }

    public void SetDestinationHash(string hash)
    {
        using var update = Command("UPDATE heartbeat SET destinationHash = $h WHERE id = 1");
        update.Parameters.AddWithValue("$h", hash);
        update.ExecuteNonQuery();
    }

    public void UpdateHeartbeat(JsonObject state, LastDueDay? lastDueDay)
    {
        using var update = Command("""
            UPDATE heartbeat SET heartbeatAt = $at, stateJson = $state, lastDueDay = COALESCE($due, lastDueDay) WHERE id = 1
            """);
        update.Parameters.AddWithValue("$at", Iso(Now()));
        update.Parameters.AddWithValue("$state", state.ToJsonString());
        update.Parameters.AddWithValue("$due", lastDueDay is null ? DBNull.Value : JsonSerializer.Serialize(lastDueDay));
        update.ExecuteNonQuery();
    }

    public void SetLastDueDay(LastDueDay lastDueDay)
    {
        using var update = Command("UPDATE heartbeat SET lastDueDay = $due WHERE id = 1");
        update.Parameters.AddWithValue("$due", JsonSerializer.Serialize(lastDueDay));
        update.ExecuteNonQuery();
    }

    public IReadOnlyDictionary<string, int> Counters()
    {
        using var read = Command("SELECT key, value FROM counters");
        return ReadAll(read, r => (r.GetString(0), (int)r.GetInt64(1))).ToDictionary(x => x.Item1, x => x.Item2, StringComparer.Ordinal);
    }

    public int Counter(string key)
    {
        using var read = Command("SELECT value FROM counters WHERE key = $k");
        read.Parameters.AddWithValue("$k", key);
        return read.ExecuteScalar() is long v ? (int)v : 0;
    }

    public void SetCounter(string key, int value)
    {
        using var write = Command("INSERT INTO counters (key, value) VALUES ($k, $v) ON CONFLICT (key) DO UPDATE SET value = $v");
        write.Parameters.AddWithValue("$k", key);
        write.Parameters.AddWithValue("$v", value);
        write.ExecuteNonQuery();
    }

    public void SaveProbe(JsonObject probe)
    {
        using var write = Command("INSERT INTO probes (at, json) VALUES ($at, $json)");
        write.Parameters.AddWithValue("$at", Iso(Now()));
        write.Parameters.AddWithValue("$json", probe.ToJsonString());
        write.ExecuteNonQuery();
        using var prune = Command("DELETE FROM probes WHERE id <= (SELECT MAX(id) FROM probes) - 1000");
        prune.ExecuteNonQuery();
    }

    // ---------------- reads ----------------

    public JsonObject? LatestProbe()
    {
        using var read = Command("SELECT json FROM probes ORDER BY id DESC LIMIT 1");
        return read.ExecuteScalar() is string json ? JsonNode.Parse(json) as JsonObject : null;
    }

    public IReadOnlyList<OutageRow> Outages()
    {
        using var read = Command("SELECT outageId, kind, dueDay, epoch, openedAt, closedAt, jobId, runId, failureNid, recoveryNid, evidenceJson FROM outages ORDER BY openedAt, outageId");
        return ReadAll(read, ReadOutage);
    }

    public OutageRow? Outage(string outageId)
    {
        using var read = Command("SELECT outageId, kind, dueDay, epoch, openedAt, closedAt, jobId, runId, failureNid, recoveryNid, evidenceJson FROM outages WHERE outageId = $id");
        read.Parameters.AddWithValue("$id", outageId);
        return ReadAll(read, ReadOutage).FirstOrDefault();
    }

    public IReadOnlyList<NotificationRow> Notifications()
    {
        using var read = Command("SELECT nid, kind, outageId, linkedNid, runId, state, createdAt, receivedAt FROM notifications ORDER BY createdAt, rowid");
        return ReadAll(read, ReadNotification);
    }

    public NotificationRow? Notification(string nid)
    {
        using var read = Command("SELECT nid, kind, outageId, linkedNid, runId, state, createdAt, receivedAt FROM notifications WHERE nid = $nid");
        read.Parameters.AddWithValue("$nid", nid);
        return ReadAll(read, ReadNotification).FirstOrDefault();
    }

    public IReadOnlyList<NotificationRow> PendingNotifications() =>
        Notifications().Where(n => n.State != "received").ToList();

    public NotificationContent Content(string nid)
    {
        using var read = Command("SELECT contentJson FROM notifications WHERE nid = $nid");
        read.Parameters.AddWithValue("$nid", nid);
        return NotificationContent.FromJson((string)(read.ExecuteScalar() ?? throw new InvalidOperationException($"unknown notification {nid}")));
    }

    public IReadOnlyList<AttemptRow> Attempts(string nid)
    {
        using var read = Command("SELECT nid, attempt, startedAt, accepted, acceptedAt, messageId, errorClass, retryAfterUtc, body, bodySha256 FROM attempts WHERE nid = $nid ORDER BY attempt");
        read.Parameters.AddWithValue("$nid", nid);
        return ReadAll(read, ReadAttempt);
    }

    public IReadOnlyList<AttemptRow> Attempts()
    {
        using var read = Command("SELECT nid, attempt, startedAt, accepted, acceptedAt, messageId, errorClass, retryAfterUtc, body, bodySha256 FROM attempts ORDER BY startedAt, nid, attempt");
        return ReadAll(read, ReadAttempt);
    }

    public IReadOnlyList<ReceiptRow> Receipts()
    {
        using var read = Command("SELECT nid, attempt, messageId, dateUtc, peerHash, textSha256, readObservedAt, importedAt FROM receipts ORDER BY importedAt, rowid");
        return ReadAll(read, r => new ReceiptRow(r.GetString(0), r.GetInt32(1), r.GetString(2), Date(r, 3)!.Value, r.GetString(4),
            r.GetString(5), Date(r, 6), Date(r, 7)!.Value));
    }

    public HeartbeatRow Heartbeat()
    {
        using var read = Command("SELECT namespace, instanceId, heartbeatAt, destinationHash, lastDueDay, stateJson FROM heartbeat WHERE id = 1");
        return ReadAll(read, r => new HeartbeatRow(r.GetString(0), r.GetString(1), Date(r, 2), Text(r, 3),
            Text(r, 4) is { } due ? JsonSerializer.Deserialize<LastDueDay>(due) : null, Text(r, 5))).Single();
    }

    public void Dispose()
    {
        try { Exec("PRAGMA wal_checkpoint(TRUNCATE);"); } catch (SqliteException) { }
        _connection.Close();
        _connection.Dispose();
        SqliteConnection.ClearPool(new SqliteConnection(_connectionString));
    }

    // ---------------- helpers ----------------

    private NotificationRow InsertNotification(SqliteTransaction tx, string nid, string kind, string? outageId, string? linkedNid,
        string? runId, NotificationContent content, DateTime now)
    {
        using var insert = Command("""
            INSERT INTO notifications (nid, kind, outageId, linkedNid, runId, state, createdAt, contentJson)
            VALUES ($nid, $kind, $oid, $link, $run, 'pending', $at, $content)
            """, tx);
        insert.Parameters.AddWithValue("$nid", nid);
        insert.Parameters.AddWithValue("$kind", kind);
        insert.Parameters.AddWithValue("$oid", (object?)outageId ?? DBNull.Value);
        insert.Parameters.AddWithValue("$link", (object?)linkedNid ?? DBNull.Value);
        insert.Parameters.AddWithValue("$run", (object?)runId ?? DBNull.Value);
        insert.Parameters.AddWithValue("$at", Iso(now));
        insert.Parameters.AddWithValue("$content", content.ToJson());
        insert.ExecuteNonQuery();
        return new NotificationRow(nid, kind, outageId, linkedNid, runId, "pending", now, null);
    }

    private AttemptRow InsertAttempt(SqliteTransaction tx, string nid, int attempt, NotificationContent content, DateTime now)
    {
        var body = NotificationBody.Render(content, nid, attempt);
        var sha = NotificationBody.Sha256Hex(body);
        using var insert = Command("""
            INSERT INTO attempts (nid, attempt, startedAt, body, bodySha256) VALUES ($nid, $attempt, $at, $body, $sha)
            """, tx);
        insert.Parameters.AddWithValue("$nid", nid);
        insert.Parameters.AddWithValue("$attempt", attempt);
        insert.Parameters.AddWithValue("$at", Iso(now));
        insert.Parameters.AddWithValue("$body", body);
        insert.Parameters.AddWithValue("$sha", sha);
        insert.ExecuteNonQuery();
        return new AttemptRow(nid, attempt, now, false, null, null, null, null, body, sha);
    }

    private static OutageRow ReadOutage(SqliteDataReader r) => new(r.GetString(0), r.GetString(1), r.GetString(2), r.GetInt32(3),
        Date(r, 4)!.Value, Date(r, 5), Text(r, 6), Text(r, 7), Text(r, 8), Text(r, 9), r.GetString(10));

    private static NotificationRow ReadNotification(SqliteDataReader r) => new(r.GetString(0), r.GetString(1), Text(r, 2), Text(r, 3),
        Text(r, 4), r.GetString(5), Date(r, 6)!.Value, Date(r, 7));

    private static AttemptRow ReadAttempt(SqliteDataReader r) => new(r.GetString(0), r.GetInt32(1), Date(r, 2)!.Value, r.GetInt32(3) == 1,
        Date(r, 4), Text(r, 5), Text(r, 6), Date(r, 7), r.GetString(8), r.GetString(9));

    private static List<T> ReadAll<T>(SqliteCommand command, Func<SqliteDataReader, T> map)
    {
        using var reader = command.ExecuteReader();
        var rows = new List<T>();
        while (reader.Read()) rows.Add(map(reader));
        return rows;
    }

    private static string? Text(SqliteDataReader r, int i) => r.IsDBNull(i) ? null : r.GetString(i);

    private static DateTime? Date(SqliteDataReader r, int i) => r.IsDBNull(i)
        ? null
        : DateTime.Parse(r.GetString(i), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).ToUniversalTime();

    internal static string Iso(DateTime value) => LondonClock.AsUtc(value).ToString("O", CultureInfo.InvariantCulture);

    private DateTime Now() => _clock.GetUtcNow().UtcDateTime;

    private SqliteCommand Command(string sql, SqliteTransaction? tx = null)
    {
        var command = _connection.CreateCommand();
        command.CommandText = sql;
        command.Transaction = tx;
        return command;
    }

    private void Exec(string sql)
    {
        using var command = Command(sql);
        command.ExecuteNonQuery();
    }
}
