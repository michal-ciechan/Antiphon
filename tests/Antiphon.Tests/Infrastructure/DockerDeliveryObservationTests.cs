using System.Text;
using Antiphon.DockerStack.Fixture;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Infrastructure;

[Category("Unit")]
public sealed class DockerDeliveryObservationTests
{
    [Test]
    public void Foreign_observer_owner_is_refused()
    {
        var called = false;
        var identity = new DeliveryCaseIdentity { Project = "parent", Resource = "db-1" };
        Should.Throw<DeliveryOwnerMismatchException>(() => identity.EnsureOwner("other", "db-1"));
        called.ShouldBeFalse();
    }

    [Test]
    public void Observer_arguments_are_closed()
    {
        var parsed = Antiphon.DockerStack.Fixture.Program.TryParseObserve(["observe", "--connection-string", "Host=db"], out _, out var error);
        parsed.ShouldBeFalse(error);
        error.ShouldContain("rejected");
    }

    [Test]
    public void Observer_grants_are_select_only()
    {
        var sql = File.ReadAllText(Path.Combine(DockerStackDocuments.RepoRoot, "tests", "Antiphon.DockerStack.Fixture", "provision-observer.sql"));
        var grants = sql.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => line.StartsWith("GRANT ", StringComparison.OrdinalIgnoreCase))
            .ToList();
        grants.Count.ShouldBe(3);
        grants.ShouldAllBe(line => line.StartsWith("GRANT SELECT ", StringComparison.OrdinalIgnoreCase));
        grants.ShouldContain(line => line.Contains("\"AgentSessions\"", StringComparison.Ordinal));
        grants.ShouldContain(line => line.Contains("\"SessionQueuedMessages\"", StringComparison.Ordinal));
        grants.ShouldContain(line => line.Contains("\"TranscriptEntries\"", StringComparison.Ordinal));
    }

    [Test]
    public void Query_is_bound_to_session()
    {
        var reader = new QueueObservationReader();
        reader.Sql.ShouldContain("@session");
        var foreign = reader.Apply([Row(session: Guid.NewGuid())], Guid.NewGuid(), 0, "body");
        foreign.ShouldBeEmpty();
    }

    [Test]
    public void Query_excludes_preexisting_rows()
    {
        var session = Guid.NewGuid();
        var reader = new QueueObservationReader();
        var rows = reader.Apply([Row(session, sequence: 1, body: "body")], session, 5, "body");
        rows.ShouldBeEmpty();
    }

    [Test]
    public void Query_requires_exact_body()
    {
        var session = Guid.NewGuid();
        var reader = new QueueObservationReader();
        reader.Sql.ShouldContain("\"Body\" = @body");
        reader.Apply([Row(session, sequence: 2, body: "body-prefix")], session, 0, "body").ShouldBeEmpty();
        reader.Apply([Row(session, sequence: 2, body: "body")], session, 0, "body").Count.ShouldBe(1);
    }

    [Test]
    public void Query_is_status_independent()
    {
        var session = Guid.NewGuid();
        var reader = new QueueObservationReader();
        reader.Sql.ShouldNotContain("AND \"Status\"");
        reader.Apply([Row(session, sequence: 2, body: "body", status: "Sent")], session, 0, "body").Count.ShouldBe(1);
    }

    [Test]
    public void Non_ui_or_bound_row_is_refused()
    {
        DeliveryEvidenceValidator.ValidateBindings(Row(origin: "System")).Code.ShouldBe("NonOrdinaryRow");
        DeliveryEvidenceValidator.ValidateBindings(Row(sourceTask: Guid.NewGuid())).Code.ShouldBe("NonOrdinaryRow");
        DeliveryEvidenceValidator.ValidateBindings(Row(notification: Guid.NewGuid())).Code.ShouldBe("NonOrdinaryRow");
        DeliveryEvidenceValidator.ValidateBindings(Row(schedule: Guid.NewGuid())).Code.ShouldBe("NonOrdinaryRow");
        DeliveryEvidenceValidator.ValidateBindings(Row(maintenance: "AutomaticArm")).Code.ShouldBe("NonOrdinaryRow");
        DeliveryEvidenceValidator.ValidateBindings(Row()).Accepted.ShouldBeTrue();
    }

    [Test]
    public void Duplicate_queue_rows_are_refused()
    {
        var decision = DeliveryEvidenceValidator.SelectSingle([Row(), Row()], null);
        decision.Code.ShouldBe("AmbiguousQueueRows");
        decision.Accepted.ShouldBeFalse();
    }

    [Test]
    public void Observed_row_cannot_be_replaced()
    {
        var original = Guid.NewGuid();
        var decision = DeliveryEvidenceValidator.SelectSingle([Row(id: Guid.NewGuid())], original);
        decision.Code.ShouldBe("RowIdentityChanged");
    }

    [Test]
    public void Pending_without_attempt_keeps_nulls()
    {
        var snapshot = DeliveryEvidenceValidator.ExportAttempt(Row(attempts: 0));
        snapshot.HasFile.ShouldBeFalse();
        snapshot.Floor.ShouldBeNull();
        snapshot.Generation.ShouldBeNull();
    }

    [Test]
    public void Attempt_requires_committed_floor() =>
        Should.Throw<InvalidOperationException>(() => DeliveryEvidenceValidator.ExportAttempt(Row(attempts: 1, floor: null)))
            .Message.ShouldBe("MissingAttemptFloor");

    [Test]
    public void Retype_appends_attempt_instead_of_overwrite()
    {
        var attempts = new List<AttemptTuple>();
        DeliveryEvidenceValidator.RecordAttempt(attempts, new AttemptTuple(1, 4, DateTime.UnixEpoch, DateTime.UnixEpoch));
        DeliveryEvidenceValidator.RecordAttempt(attempts, new AttemptTuple(2, 9, DateTime.UnixEpoch.AddMinutes(1), DateTime.UnixEpoch));
        attempts.Count.ShouldBe(2);
        attempts[0].Floor.ShouldBe(4);
    }

    [Test]
    public void Native_prompt_must_be_complete()
    {
        var expectation = Expect(Marked("HEAD\nTAIL"));
        var prefix = DeliveryEvidenceValidator.ValidateReceipt(expectation, [Prompt("1", "HEAD")], [], 1, expectation.AcceptedGeneration, expectation.AcceptedGeneration);
        prefix.Code.ShouldBe("IncompletePrompt");
        var missingTail = DeliveryEvidenceValidator.ValidateReceipt(expectation, [Prompt("1", expectation.Body[..^4])], [], 1, expectation.AcceptedGeneration, expectation.AcceptedGeneration);
        missingTail.Code.ShouldBe("IncompletePrompt");
    }

    [Test]
    public void Expected_body_digest_must_match()
    {
        var expectation = Expect("body") with { BodySha256 = "00" };
        DeliveryEvidenceValidator.ValidateReceipt(expectation, [Prompt("1", "body")], [Prompt("1", "body", 6)], 1, expectation.AcceptedGeneration, expectation.AcceptedGeneration)
            .Code.ShouldBe("BodyDigestMismatch");
    }

    [Test]
    public void Summary_identity_must_match()
    {
        var expectation = Expect("no markers");
        DeliveryEvidenceValidator.ValidateReceipt(expectation, [Prompt("1", expectation.Body)], [Prompt("1", expectation.Body, 6)], 1, expectation.AcceptedGeneration, expectation.AcceptedGeneration)
            .Code.ShouldBe("SummaryIdentityMismatch");
    }

    [Test]
    public void Stored_receipt_must_join_native_uuid()
    {
        var expectation = Expect(Marked("body"));
        var decision = DeliveryEvidenceValidator.ValidateReceipt(
            expectation, [Prompt("native", expectation.Body)], [Prompt("stored", expectation.Body, 6)], 1, expectation.AcceptedGeneration, expectation.AcceptedGeneration);
        decision.Code.ShouldBe("ReceiptJoinMismatch");
    }

    [Test]
    public void Stored_receipt_must_exceed_attempt_floor()
    {
        var expectation = Expect(Marked("body"));
        var decision = DeliveryEvidenceValidator.ValidateReceipt(
            expectation, [Prompt("1", expectation.Body, 5)], [Prompt("1", expectation.Body, 5)], 5, expectation.AcceptedGeneration, expectation.AcceptedGeneration);
        decision.Code.ShouldBe("FloorNotExceeded");
    }

    [Test]
    public void Native_sequence_cannot_satisfy_server_floor()
    {
        var expectation = Expect(Marked("body"));
        var decision = DeliveryEvidenceValidator.ValidateReceipt(
            expectation, [Prompt("1", expectation.Body, 100)], [Prompt("1", expectation.Body, 4)], 5, expectation.AcceptedGeneration, expectation.AcceptedGeneration);
        decision.Accepted.ShouldBeFalse();
    }

    [Test]
    public void Duplicate_native_prompts_are_refused()
    {
        var expectation = Expect(Marked("body"));
        var decision = DeliveryEvidenceValidator.ValidateReceipt(
            expectation, [Prompt("1", expectation.Body), Prompt("2", expectation.Body)], [Prompt("1", expectation.Body, 6)], 1, expectation.AcceptedGeneration, expectation.AcceptedGeneration);
        decision.Code.ShouldBe("DuplicateNativePrompt");
    }

    [Test]
    public void Duplicate_stored_receipts_are_refused()
    {
        var expectation = Expect(Marked("body"));
        var decision = DeliveryEvidenceValidator.ValidateReceipt(
            expectation,
            [Prompt("1", expectation.Body)],
            [Prompt("1", expectation.Body, 6), Prompt("1", expectation.Body, 7)],
            1, expectation.AcceptedGeneration, expectation.AcceptedGeneration);
        decision.Code.ShouldBe("DuplicateStoredReceipt");
    }

    [Test]
    public void Native_only_is_not_delivery_success()
    {
        var expectation = Expect(Marked("body"));
        var decision = DeliveryEvidenceValidator.ValidateReceipt(
            expectation, [Prompt("1", expectation.Body)], [], 1, expectation.AcceptedGeneration, expectation.AcceptedGeneration);
        decision.Accepted.ShouldBeFalse();
        decision.State.ShouldBe("native-received/server-pending");
    }

    [Test]
    public void Initial_generation_mismatch_is_refused()
    {
        var expectation = Expect(Marked("body"));
        var decision = DeliveryEvidenceValidator.ValidateReceipt(
            expectation, [Prompt("1", expectation.Body)], [Prompt("1", expectation.Body, 6)], 1, DateTime.UnixEpoch, expectation.AcceptedGeneration);
        decision.Code.ShouldBe("GenerationMismatch");
    }

    [Test]
    public void Generation_change_during_read_is_refused()
    {
        var expectation = Expect(Marked("body"));
        var decision = DeliveryEvidenceValidator.ValidateReceipt(
            expectation, [Prompt("1", expectation.Body)], [Prompt("1", expectation.Body, 6)], 1, expectation.AcceptedGeneration, DateTime.UnixEpoch.AddMinutes(5));
        decision.Code.ShouldBe("GenerationChanged");
    }

    [Test]
    public void Missing_generation_is_refused()
    {
        var decision = DeliveryEvidenceValidator.CompareGeneration(DateTime.UnixEpoch, null, DateTime.UnixEpoch);
        decision.Code.ShouldBe("GenerationUnavailable");
    }

    [Test]
    public void Generation_uses_postgres_precision()
    {
        var left = new DateTime(2026, 9, 21, 0, 0, 0, DateTimeKind.Utc).AddTicks(10);
        var right = left.AddTicks(10);
        DeliveryEvidenceValidator.NormalizeGeneration(left).ShouldNotBe(DeliveryEvidenceValidator.NormalizeGeneration(right));
        DeliveryEvidenceValidator.NormalizeGeneration(left).ShouldBe(DeliveryEvidenceValidator.NormalizeGeneration(left.AddTicks(3)));
    }

    [Test]
    public void Observation_transaction_is_read_only_repeatable()
    {
        var reader = new QueueObservationReader();
        reader.Isolation.ShouldBe("RepeatableRead");
        reader.ReadOnly.ShouldBeTrue();
    }

    [Test]
    public void Receipt_kind_must_be_user_prompt()
    {
        var expectation = Expect(Marked("body"));
        var decision = DeliveryEvidenceValidator.ValidateReceipt(
            expectation, [Prompt("1", expectation.Body, kind: "AssistantText")], [Prompt("1", expectation.Body, 6)], 1, expectation.AcceptedGeneration, expectation.AcceptedGeneration);
        decision.Code.ShouldBe("ReceiptKindMismatch");
    }

    [Test]
    public void Stored_body_must_be_complete()
    {
        var expectation = Expect(Marked("body"));
        var decision = DeliveryEvidenceValidator.ValidateReceipt(
            expectation, [Prompt("1", expectation.Body)], [Prompt("1", "truncated", 6)], 1, expectation.AcceptedGeneration, expectation.AcceptedGeneration);
        decision.Code.ShouldBe("StoredBodyIncomplete");
    }

    [Test]
    public void Oversized_summary_is_refused()
    {
        var identity = new DeliveryCaseIdentity();
        var legal = new string('a', DeliveryCaseIdentity.InlineCeilingBytes);
        identity.EnsureInline(legal);
        Should.Throw<OversizedSummaryException>(() => identity.EnsureInline(legal + "b"));
    }

    [Test]
    public void Frozen_row_field_change_is_refused()
    {
        var id = Guid.NewGuid();
        var expectation = Expect("body");
        var changed = Row(id: id, body: "other", sequence: 2);
        DeliveryEvidenceValidator.ValidateFrozen(changed, expectation with { Body = "body", QueueHighWater = 0 }, id)
            .Code.ShouldBe("FrozenFieldChanged");
    }

    private static QueueCandidate Row(
        Guid? session = null,
        Guid? id = null,
        long sequence = 1,
        string body = "body",
        string status = "Pending",
        string origin = "Ui",
        Guid? sourceTask = null,
        Guid? notification = null,
        Guid? schedule = null,
        string maintenance = "None",
        int attempts = 0,
        long? floor = null) =>
        new(id ?? Guid.NewGuid(), session ?? Guid.NewGuid(), sequence, status, body, origin, sourceTask, notification, schedule, maintenance, attempts, floor, attempts == 0 ? null : DateTime.UnixEpoch, null, null);

    private static PromptRecord Prompt(string uuid, string body, long sequence = 1, string kind = "UserPrompt") =>
        new(uuid, kind, body, sequence);

    private static string Marked(string body) => "run-1 case-1 sha-1 digest-1 " + body;

    private static DeliveryExpectation Expect(string body) =>
        new(body, DeliveryCaseIdentity.Sha256(body), Guid.NewGuid(), 0, new DateTime(2026, 9, 21, 0, 0, 0, DateTimeKind.Utc), "run-1", "case-1", "sha-1", "digest-1");
}
