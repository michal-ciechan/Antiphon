using System.Text.Json;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[NotInParallel]
public class QuotaEvidenceWireTests
{
    private static RunnerTranscriptEvent Entry(Guid sessionId, string? zone = "Europe/London") =>
        new(sessionId, 1, TranscriptKinds.TurnEnd, Guid.NewGuid().ToString("D"), null,
            DateTimeOffset.Parse("2026-09-25T16:34:07Z"), "assistant", "quota",
            null, null, null, null, "end_turn", IsApiError: true,
            ApiErrorClass: "usage_limit_exceeded", ApiErrorTimeZoneId: zone);

    [Test]
    public void Http_event_mapping_preserves_provider_zone()
    {
        var raw = Entry(Guid.NewGuid());
        var mapped = RunnerContractMapper.MapTranscript(raw);
        mapped.ApiErrorTimeZoneId.ShouldBe("Europe/London");
        mapped.ApiErrorClass.ShouldBe("usage_limit_exceeded");
    }

    [Test]
    public async Task Phone_home_zone_survives_ingestion_and_storage()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var h = await BridgeQueueHarness.CreateAsync(new() { ConnectionString = schema.ConnectionString });
        var raw = Entry(h.SessionId);
        var json = JsonSerializer.Serialize(raw, RunnerContractMapper.Json);
        var phoneHome = RunnerContractMapper.ParseEvent(SessionRunnerEventNames.SessionTranscript, json)!;
        phoneHome.Transcript!.ApiErrorTimeZoneId.ShouldBe("Europe/London");
        await h.Runtime.PersistTranscriptAsync(h.SessionId, [phoneHome.Transcript]);
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        (await db.TranscriptEntries.SingleAsync(t => t.AgentSessionId == h.SessionId))
            .ApiErrorTimeZoneId.ShouldBe("Europe/London");
    }

    [Test]
    public async Task Database_and_transcript_api_roundtrip_preserves_zone()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var h = await BridgeQueueHarness.CreateAsync(new() { ConnectionString = schema.ConnectionString });
        await h.Runtime.PersistTranscriptAsync(h.SessionId, [RunnerContractMapper.MapTranscript(Entry(h.SessionId, "Etc/UTC"))]);
        using var scope = h.Provider.CreateScope();
        var transcript = await scope.ServiceProvider.GetRequiredService<AgentSessionService>()
            .GetTranscriptAsync(h.SessionId, 0, CancellationToken.None);
        transcript.Entries.ShouldHaveSingleItem().ApiErrorTimeZoneId.ShouldBe("Etc/UTC");
    }

    [Test]
    public void Legacy_payload_and_sidecar_without_zone_deserialize_null()
    {
        var raw = Entry(Guid.NewGuid());
        var json = JsonSerializer.Serialize(raw, RunnerContractMapper.Json).Replace(",\"apiErrorTimeZoneId\":\"Europe/London\"", "");
        var parsed = JsonSerializer.Deserialize<RunnerTranscriptEvent>(json, RunnerContractMapper.Json)!;
        parsed.ApiErrorTimeZoneId.ShouldBeNull();
        var sidecar = JsonSerializer.Deserialize<TranscriptSidecar>(
            "{\"sessionId\":\"" + Guid.NewGuid() + "\",\"format\":\"Codex\"}")!;
        sidecar.ApiErrorTimeZoneId.ShouldBeNull();
    }
}
