using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[Category("Slow")]
public class RuntimeActionClaimQueryTests
{
    [Test]
    public async Task Selection_and_drain_match_legacy_and_independent_destination_oracle()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var now = new DateTime(2026, 11, 13, 12, 0, 0, DateTimeKind.Utc);
        await db.Database.OpenConnectionAsync();
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        var evidence = EvidenceDirectory();
        // One owned clone, no production services/processes; each case has separate session identity.
        foreach (var scenario in new[] { "empty", "transcript", "channel", "mixed", "future-transcript",
                     "different", "carried", "wildcard-earlier", "wildcard-later", "late-carried",
                     "carried-late", "late-late", "multiple-late", "foreign-late", "missing-late",
                     "null-late-key", "same-sequence", "nonchannel-late" })
        {
            var session = Session(now);
            var foreign = Session(now);
            db.AgentSessions.AddRange(session, foreign);
            var messages = new[]
            {
                Message(session.Id, "chat:a", 1, QueuedMessageStatus.Sent, DeliveryVerdict.Delivered),
                Message(session.Id, "chat:b", 2, QueuedMessageStatus.Pending, null),
                Message(session.Id, "chat:c", 3, QueuedMessageStatus.Sent, null),
                Message(session.Id, null, 4, QueuedMessageStatus.Sent, DeliveryVerdict.Delivered),
                Message(foreign.Id, "chat:b", 1, QueuedMessageStatus.Sent, DeliveryVerdict.Delivered)
            };
            db.SessionQueuedMessages.AddRange(messages);
            var earlier = Action(session.Id, now, 1, "channel", "chat:a", due: false);
            var later = Action(session.Id, now, 2, "channel", "chat:b");
            List<SessionRuntimeAction> rows = [earlier, later];
            switch (scenario)
            {
                case "empty": rows = []; break;
                case "transcript":
                    rows = [Action(session.Id, now, 3), Action(session.Id, now, 1), Action(session.Id, now, 2)]; break;
                case "channel": earlier.DueAt = now; break;
                case "mixed":
                    rows.Add(Action(session.Id, now, 10));
                    rows.Add(Action(session.Id, now, 0, "status", phase: 8)); break;
                case "future-transcript": rows = [Action(session.Id, now, 0, due: false), later]; break;
                case "carried": later.ConversationKey = "chat:a"; break;
                case "wildcard-earlier": earlier.ConversationKey = null; earlier.LateConfirmedChannelMessageIds = [messages[1].Id]; break;
                case "wildcard-later": later.ConversationKey = null; later.LateConfirmedChannelMessageIds = [messages[0].Id]; break;
                case "late-carried": earlier.LateConfirmedChannelMessageIds = [messages[1].Id]; break;
                case "carried-late": later.LateConfirmedChannelMessageIds = [messages[0].Id]; break;
                case "late-late":
                    earlier.LateConfirmedChannelMessageIds = [messages[2].Id];
                    later.LateConfirmedChannelMessageIds = [messages[2].Id]; break;
                case "multiple-late": earlier.LateConfirmedChannelMessageIds = [messages[2].Id, messages[1].Id, messages[1].Id]; break;
                case "foreign-late": earlier.LateConfirmedChannelMessageIds = [messages[4].Id]; break;
                case "missing-late": earlier.LateConfirmedChannelMessageIds = [Guid.NewGuid()]; break;
                case "null-late-key": earlier.LateConfirmedChannelMessageIds = [messages[3].Id]; break;
                case "same-sequence":
                    // Unique (session, sequence, kind) permits ties between different kinds.
                    rows = [later, Action(session.Id, now, 2, "catchup", phase: 8),
                        Action(session.Id, now, 2, "status", phase: 8), Action(session.Id, now, 2, "transcript")]; break;
                case "nonchannel-late": earlier.TriggerKind = "transcript"; earlier.LateConfirmedChannelMessageIds = [messages[1].Id]; break;
            }
            db.SessionRuntimeActions.AddRange(rows);
            // Foreign action, including wildcard, must never participate in this session's debt.
            db.SessionRuntimeActions.Add(Action(foreign.Id, now, 0, "channel", null, due: false));
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
            // Explain every semantic fixture as well as the separate large-table matrix.
            var oldPlan = await ExplainAsync(connection, Legacy(db, session.Id, now), generic: false, jit: false);
            var newPlan = await ExplainAsync(connection, SessionRuntimeActionClaimQuery.Query(db, session.Id, now), generic: false, jit: false);
            await File.WriteAllTextAsync(Path.Combine(evidence, scenario + ".json"), JsonSerializer.Serialize(new { oldPlan, newPlan }));
            var expectedOrder = new List<Guid>();
            var actualOrder = new List<Guid>();
            while (true)
            {
                var expected = Oracle(rows, messages, now);
                var legacy = (await Legacy(db, session.Id, now).AsNoTracking().ToListAsync()).SingleOrDefault();
                var actual = (await SessionRuntimeActionClaimQuery.Query(db, session.Id, now).ToListAsync()).SingleOrDefault();
                (legacy?.Id).ShouldBe(expected?.Id, scenario + " legacy oracle");
                (actual?.Id).ShouldBe(expected?.Id, scenario + " production oracle");
                if (actual is null) break;
                actual.LateConfirmedChannelMessageIds.ShouldBe(expected!.LateConfirmedChannelMessageIds);
                actual.UiProjectionDelta.ShouldBe(expected.UiProjectionDelta);
                actual.AcceptedGeneration.ShouldBe(expected.AcceptedGeneration);
                actual.ProducerSequence.ShouldBe(expected.ProducerSequence);
                expectedOrder.Add(expected.Id);
                actualOrder.Add(actual.Id);
                // The later coordinator due-time gate must still see future non-channel rows.
                if (actual.DueAt > now) break;
                rows.Remove(expected);
                db.SessionRuntimeActions.Remove(actual);
                await db.SaveChangesAsync();
            }
            actualOrder.ShouldBe(expectedOrder, scenario + " complete drain order");
        }
    }

    [Test]
    public async Task Small_and_debt_heavy_plans_bound_cost_and_seek_queued_messages()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var now = new DateTime(2026, 11, 13, 12, 0, 0, DateTimeKind.Utc);
        var session = Session(now);
        db.AgentSessions.Add(session);
        db.SessionRuntimeActions.AddRange(Enumerable.Range(1, 3).Select(n => Action(session.Id, now, n)));
        await db.SaveChangesAsync();
        await db.Database.OpenConnectionAsync();
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        await using (var seed = new NpgsqlCommand("""
            INSERT INTO "SessionQueuedMessages" ("Id", "AgentSessionId", "Body", "Status", "Sequence", "Origin", "CreatedAt", "DeliveryAttempts", "ConversationKey")
            SELECT md5('claim-message-' || n)::uuid, @session, 'synthetic', 0, n, 0, @now, 0, 'chat:' || n
            FROM generate_series(1, 12000) n;
            ANALYZE "SessionQueuedMessages";
            ANALYZE "SessionRuntimeActions";
            """, connection))
        {
            seed.Parameters.AddWithValue("session", session.Id);
            seed.Parameters.AddWithValue("now", now);
            await seed.ExecuteNonQueryAsync();
        }
        var evidence = EvidenceDirectory();
        // Keep every plan even if the baseline production query violates the new budget.
        var observations = new List<(string Shape, bool Generic, bool Jit, JsonElement Old, JsonElement New)>();
        foreach (var shape in new[] { "transcript", "debt-heavy" })
        {
            if (shape == "debt-heavy")
            {
                var ids = await db.SessionQueuedMessages.OrderBy(m => m.Sequence).Select(m => m.Id).Take(32).ToArrayAsync();
                db.SessionRuntimeActions.AddRange(Enumerable.Range(4, 32).Select(n =>
                {
                    var a = Action(session.Id, now, n, "channel", "chat:" + (n % 8), due: n % 3 != 0);
                    a.LateConfirmedChannelMessageIds = [ids[n % 32], ids[(n + 1) % 32]];
                    return a;
                }));
                await db.SaveChangesAsync();
                await using var analyze = new NpgsqlCommand("ANALYZE \"SessionRuntimeActions\"", connection);
                await analyze.ExecuteNonQueryAsync();
            }
            foreach (var generic in new[] { false, true })
            foreach (var jit in new[] { false, true })
            {
                var oldPlan = await ExplainAsync(connection, Legacy(db, session.Id, now), generic, jit);
                var newPlan = await ExplainAsync(connection, SessionRuntimeActionClaimQuery.Query(db, session.Id, now), generic, jit);
                observations.Add((shape, generic, jit, oldPlan, newPlan));
                await File.WriteAllTextAsync(Path.Combine(evidence, $"{shape}-{generic}-{jit}.json"),
                    JsonSerializer.Serialize(new { shape, generic, jit, oldPlan, newPlan }));
                Console.WriteLine($"C1886 {shape} generic={generic} jit={jit} oldCost={Cost(oldPlan)} newCost={Cost(newPlan)} oldMs={oldPlan.GetProperty("Execution Time")} newMs={newPlan.GetProperty("Execution Time")}");
            }
        }
        foreach (var row in observations)
        {
            Cost(row.New).ShouldBeLessThan(100000, $"{row.Shape} generic={row.Generic} jit={row.Jit}");
            row.New.TryGetProperty("JIT", out _).ShouldBeFalse("the query must be cheap independently of S1's connection policy");
            var queues = Nodes(row.New.GetProperty("Plan")).Where(n => n.TryGetProperty("Relation Name", out var name)
                && name.GetString() == "SessionQueuedMessages").ToArray();
            queues.ShouldAllBe(n => n.GetProperty("Node Type").GetString() != "Seq Scan", "seek the large queue table");
            if (row.Shape == "transcript")
                queues.ShouldAllBe(n => n.GetProperty("Actual Loops").GetInt64() == 0, "no queued-message work without channel debt");
        }
    }

    private static decimal Cost(JsonElement plan) => plan.GetProperty("Plan").GetProperty("Total Cost").GetDecimal();

    private static string EvidenceDirectory()
    {
        var root = Environment.GetEnvironmentVariable("ANTIPHON_TEST_PHASE_ROOT") ?? Path.GetTempPath();
        var path = Path.Combine(root, "c1886-plans-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        Console.WriteLine("C1886 plans=" + path);
        return path;
    }

    private static IEnumerable<JsonElement> Nodes(JsonElement node)
    {
        yield return node;
        if (node.TryGetProperty("Plans", out var children))
            foreach (var child in children.EnumerateArray())
                foreach (var nested in Nodes(child)) yield return nested;
    }

    private static async Task<JsonElement> ExplainAsync(NpgsqlConnection connection, IQueryable<SessionRuntimeAction> query, bool generic, bool jit)
    {
        await using var source = query.CreateDbCommand();
        var sql = source.CommandText;
        var parameters = source.Parameters.Cast<NpgsqlParameter>().ToArray();
        for (var i = 0; i < parameters.Length; i++)
            sql = Regex.Replace(sql, "@" + Regex.Escape(parameters[i].ParameterName.TrimStart('@')) + @"\b", _ => "$" + (i + 1));
        string Type(object? value) => value switch { Guid => "uuid", DateTime => "timestamptz", int => "integer", string => "text", _ => throw new NotSupportedException() };
        string Literal(object? value) => value switch
        {
            Guid id => "'" + id + "'::uuid",
            DateTime time => "'" + time.ToString("O", CultureInfo.InvariantCulture) + "'::timestamptz",
            int number => number.ToString(CultureInfo.InvariantCulture),
            string text => "'" + text.Replace("'", "''", StringComparison.Ordinal) + "'",
            _ => throw new NotSupportedException()
        };
        await using var command = new NpgsqlCommand("DEALLOCATE ALL; SET jit=" + (jit ? "on" : "off")
            + "; SET plan_cache_mode=" + (generic ? "force_generic_plan" : "force_custom_plan")
            + "; PREPARE c1886 (" + string.Join(',', parameters.Select(p => Type(p.Value))) + ") AS " + sql, connection);
        await command.ExecuteNonQueryAsync();
        command.CommandText = "EXPLAIN (ANALYZE, BUFFERS, SETTINGS, FORMAT JSON, TIMING OFF) EXECUTE c1886 ("
            + string.Join(',', parameters.Select(p => Literal(p.Value))) + ")";
        using var json = JsonDocument.Parse((string)(await command.ExecuteScalarAsync())!);
        var plan = json.RootElement[0].Clone();
        command.CommandText = "SELECT " + (generic ? "generic_plans" : "custom_plans") + " FROM pg_prepared_statements WHERE name='c1886'";
        ((long)(await command.ExecuteScalarAsync())!).ShouldBeGreaterThan(0);
        return plan;
    }

    private static SessionRuntimeAction? Oracle(List<SessionRuntimeAction> actions, SessionQueuedMessage[] messages, DateTime now)
    {
        HashSet<string> Destinations(SessionRuntimeAction a) => messages
            .Where(m => m.AgentSessionId == a.AgentSessionId && a.LateConfirmedChannelMessageIds.Contains(m.Id))
            .Select(m => m.ConversationKey).Append(a.ConversationKey).OfType<string>().ToHashSet(StringComparer.Ordinal);
        var eligible = new List<SessionRuntimeAction>();
        foreach (var a in actions)
        {
            if (a.TriggerKind != "channel") { eligible.Add(a); continue; }
            if (a.DueAt > now) continue;
            var blocked = false;
            foreach (var b in actions.Where(b => b.TriggerKind == "channel" && b.Sequence < a.Sequence))
                if (a.ConversationKey is null || b.ConversationKey is null || Destinations(a).Overlaps(Destinations(b))) blocked = true;
            if (!blocked) eligible.Add(a);
        }
        return eligible.OrderBy(a => a.Phase >= 8).ThenBy(a => a.Sequence).ThenBy(a => a.TriggerKind, StringComparer.Ordinal).FirstOrDefault();
    }

    private static AgentSession Session(DateTime now) => new() { Id = Guid.NewGuid(), DefinitionName = "c1886-synthetic", Cwd = Path.GetTempPath(), CreatedAt = now, StartedAt = now };
    private static SessionQueuedMessage Message(Guid session, string? key, long sequence, QueuedMessageStatus status, DeliveryVerdict? verdict) =>
        new() { Id = Guid.NewGuid(), AgentSessionId = session, ConversationKey = key, Sequence = sequence, Status = status, DeliveryVerdict = verdict };
    private static SessionRuntimeAction Action(Guid session, DateTime now, long sequence, string kind = "transcript", string? key = null, bool due = true, int phase = 1) =>
        new() { Id = Guid.NewGuid(), AgentSessionId = session, Sequence = sequence, ProducerSequence = sequence + 100, TriggerKind = kind, ConversationKey = key,
            DueAt = due ? now : now.AddMinutes(1), CreatedAt = now, AcceptedGeneration = now, Phase = phase, UiProjectionDelta = [1, 2, 3] };

    // Frozen pre-S2 production expression: independent of the replacement SQL and its key-set expansion.
    private static IQueryable<SessionRuntimeAction> Legacy(AppDbContext db, Guid sessionId, DateTime now) =>
        db.SessionRuntimeActions.Where(a => a.AgentSessionId == sessionId
            && (a.TriggerKind != "channel" || (a.DueAt <= now
                && !db.SessionRuntimeActions.Any(b => b.AgentSessionId == sessionId
                    && b.TriggerKind == "channel" && b.Sequence < a.Sequence
                    && (b.ConversationKey == null || a.ConversationKey == null
                        || b.ConversationKey == a.ConversationKey
                        || db.SessionQueuedMessages.Any(m => m.AgentSessionId == sessionId
                            && m.ConversationKey != null
                            && ((a.LateConfirmedChannelMessageIds.Contains(m.Id) && m.ConversationKey == b.ConversationKey)
                                || (b.LateConfirmedChannelMessageIds.Contains(m.Id) && m.ConversationKey == a.ConversationKey)
                                || (a.LateConfirmedChannelMessageIds.Contains(m.Id)
                                    && db.SessionQueuedMessages.Any(n => n.AgentSessionId == sessionId
                                        && b.LateConfirmedChannelMessageIds.Contains(n.Id) && n.ConversationKey == m.ConversationKey)))))))))
            .OrderBy(a => a.Phase >= 8).ThenBy(a => a.Sequence).ThenBy(a => a.TriggerKind).Take(1);
}
