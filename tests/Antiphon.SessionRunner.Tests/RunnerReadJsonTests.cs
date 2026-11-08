using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Antiphon.SessionRunner.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Shouldly;
using TUnit.Core;

namespace Antiphon.SessionRunner.Tests;

/// <summary>
/// CARD-1762: source-generated buffer, snapshot, and transcript JSON stays byte-identical
/// to today's reflection serializers, including an old peer's body.
/// </summary>
[Category("Unit")]
public class RunnerReadJsonTests
{
    private static readonly JsonSerializerOptions ReflectionPhone = new(JsonSerializerDefaults.Web);
    private static readonly JsonSerializerOptions ReflectionHttp = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static readonly DateTime Started = new(2026, 11, 7, 12, 16, 35, 123, DateTimeKind.Utc);
    private static readonly DateTimeOffset Stamp = new(2026, 11, 7, 7, 16, 35, TimeSpan.FromHours(-5));
    private static readonly Guid SessionId = Guid.Parse("4142c507-241f-4234-aa07-c03ebcc7f7d2");
    private const string Unicode = "café\n日本語 \"quote\" \\slash 😀";
    private static readonly string Large = new string('A', 256 * 1024) + "ü";

    [Test]
    public void Golden_read_bodies_match_reflection_for_null_empty_unicode_and_large()
    {
        RunnerReadJson.PhoneBuffer.OriginatingResolver.ShouldBeSameAs(RunnerReadJson.Phone);
        RunnerReadJson.HttpBuffer.OriginatingResolver.ShouldBeSameAs(RunnerReadJson.Http);
        RunnerReadJson.PhoneSnapshot.OriginatingResolver.ShouldBeSameAs(RunnerReadJson.Phone);
        RunnerReadJson.HttpTranscript.OriginatingResolver.ShouldBeSameAs(RunnerReadJson.Http);

        JsonSerializer.Deserialize("null", RunnerReadJson.PhoneBuffer).ShouldBeNull();
        JsonSerializer.Deserialize("null", RunnerReadJson.HttpSnapshot).ShouldBeNull();
        JsonSerializer.Deserialize("null", RunnerReadJson.PhoneTranscript).ShouldBeNull();

        foreach (var shape in Shapes())
        {
            Same(shape.Buffer, RunnerReadJson.PhoneBuffer, ReflectionPhone, "phone buffer " + shape.Name);
            Same(shape.Buffer, RunnerReadJson.HttpBuffer, ReflectionHttp, "http buffer " + shape.Name);
            Same(shape.Snapshot, RunnerReadJson.PhoneSnapshot, ReflectionPhone, "phone snapshot " + shape.Name);
            Same(shape.Snapshot, RunnerReadJson.HttpSnapshot, ReflectionHttp, "http snapshot " + shape.Name);
            Same(shape.Transcript, RunnerReadJson.PhoneTranscript, ReflectionPhone, "phone transcript " + shape.Name);
            Same(shape.Transcript, RunnerReadJson.HttpTranscript, ReflectionHttp, "http transcript " + shape.Name);
            OldPeerRoundTrip(shape);
        }

        var phone = JsonSerializer.SerializeToUtf8Bytes(UnicodeBuffer(), RunnerReadJson.PhoneBuffer);
        var http = JsonSerializer.SerializeToUtf8Bytes(UnicodeBuffer(), RunnerReadJson.HttpBuffer);
        var phoneText = Encoding.UTF8.GetString(phone);
        var httpText = Encoding.UTF8.GetString(http);
        phoneText.Contains('é', StringComparison.Ordinal).ShouldBeFalse();
        phoneText.Contains("\\u00E9", StringComparison.Ordinal).ShouldBeTrue();
        httpText.Contains('é', StringComparison.Ordinal).ShouldBeTrue();
        httpText.Contains("\\u00E9", StringComparison.Ordinal).ShouldBeFalse();
    }

    [Test]
    public void Source_generated_contract_rejects_a_reflection_type_info()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
        };
        var reflection = (JsonTypeInfo<RunnerBufferDto>)options.GetTypeInfo(typeof(RunnerBufferDto));
        var ex = Should.Throw<InvalidOperationException>(() => RunnerReadJson.Use(reflection));
        ex.Message.ShouldContain("source-generated");
    }

    [Test]
    public async Task Http_and_phone_home_writers_admit_the_source_generated_contract()
    {
        var shape = Shapes().Single(s => s.Name == "unicode");
        using (var watch = RunnerReadJson.Watch())
        {
            var buffer = await Write<RunnerBufferDto>(SessionReadLaunchRoutes.Buffer(shape.Buffer));
            var snapshot = await Write<RunnerSnapshotDto>(SessionReadLaunchRoutes.Snapshot(shape.Snapshot));
            var transcript = await Write<RunnerTranscriptDto>(SessionReadLaunchRoutes.Transcript(shape.Transcript));
            SameBytes(buffer, JsonSerializer.SerializeToUtf8Bytes(shape.Buffer, ReflectionHttp), "http buffer route");
            SameBytes(snapshot, JsonSerializer.SerializeToUtf8Bytes(shape.Snapshot, ReflectionHttp), "http snapshot route");
            SameBytes(transcript, JsonSerializer.SerializeToUtf8Bytes(shape.Transcript, ReflectionHttp), "http transcript route");
            watch.Count.ShouldBe(3);
        }

        var runtime = new FixedReads(shape.Buffer, shape.Snapshot, shape.Transcript);
        var dispatcher = new PhoneHomeCommandDispatcher(runtime, new PhoneHomeSettings { Enabled = true, AllowedCwd = "/work" });
        using (var watch = RunnerReadJson.Watch())
        {
            var buffer = await dispatcher.DispatchAsync(Request(PhoneHomeOperation.Buffer), CancellationToken.None);
            var snapshot = await dispatcher.DispatchAsync(Request(PhoneHomeOperation.Snapshot), CancellationToken.None);
            var transcript = await dispatcher.DispatchAsync(Request(PhoneHomeOperation.Transcript), CancellationToken.None);
            buffer.Kind.ShouldBe(PhoneHomeFrameKind.Result);
            snapshot.Kind.ShouldBe(PhoneHomeFrameKind.Result);
            transcript.Kind.ShouldBe(PhoneHomeFrameKind.Result);
            buffer.Payload!.Value.GetRawText().ShouldBe(JsonSerializer.Serialize(shape.Buffer, ReflectionPhone));
            snapshot.Payload!.Value.GetRawText().ShouldBe(JsonSerializer.Serialize(shape.Snapshot, ReflectionPhone));
            transcript.Payload!.Value.GetRawText().ShouldBe(JsonSerializer.Serialize(shape.Transcript, ReflectionPhone));
            watch.Count.ShouldBe(3);
        }
    }

    private static void Same<T>(T value, JsonTypeInfo<T> contract, JsonSerializerOptions reflection, string label)
    {
        var generated = JsonSerializer.SerializeToUtf8Bytes(value, contract);
        var old = JsonSerializer.SerializeToUtf8Bytes(value, reflection);
        SameBytes(generated, old, label);
        var fromOld = JsonSerializer.Deserialize(old, contract);
        JsonSerializer.SerializeToUtf8Bytes(fromOld!, contract).AsSpan().SequenceEqual(generated).ShouldBeTrue(label + " old-to-new");
        var fromNew = JsonSerializer.Deserialize<T>(generated, reflection);
        JsonSerializer.SerializeToUtf8Bytes(fromNew, reflection).AsSpan().SequenceEqual(old).ShouldBeTrue(label + " new-to-old");
    }

    private static void OldPeerRoundTrip(Shape shape)
    {
        var bufferBytes = JsonSerializer.SerializeToUtf8Bytes(shape.Buffer, ReflectionPhone);
        JsonSerializer.Deserialize(bufferBytes, RunnerReadJson.PhoneBuffer)!.Buffer.ShouldBe(shape.Buffer.Buffer);
        var snapshotBytes = JsonSerializer.SerializeToUtf8Bytes(shape.Snapshot, ReflectionHttp);
        var snapshot = JsonSerializer.Deserialize(snapshotBytes, RunnerReadJson.HttpSnapshot)!;
        snapshot.RawOutput.ShouldBe(shape.Snapshot.RawOutput);
        ProviderDialogState? dialogState = snapshot.Dialog?.State;
        dialogState.ShouldBe(shape.Snapshot.Dialog?.State);
        var transcriptBytes = JsonSerializer.SerializeToUtf8Bytes(shape.Transcript, ReflectionPhone);
        var transcript = JsonSerializer.Deserialize(transcriptBytes, RunnerReadJson.PhoneTranscript)!;
        transcript.Entries.Count.ShouldBe(shape.Transcript.Entries.Count);
        if (transcript.Entries.Count > 0)
            transcript.Entries[0].Text.ShouldBe(shape.Transcript.Entries[0].Text);
    }

    private static async Task<byte[]> Write<T>(IResult result)
    {
        result.ShouldBeOfType<JsonHttpResult<T>>();
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        await result.ExecuteAsync(context);
        context.Response.StatusCode.ShouldBe(StatusCodes.Status200OK);
        return ((MemoryStream)context.Response.Body).ToArray();
    }

    private static void SameBytes(byte[] actual, byte[] expected, string label)
    {
        if (actual.AsSpan().SequenceEqual(expected))
            return;
        var n = Math.Min(actual.Length, expected.Length);
        var at = 0;
        while (at < n && actual[at] == expected[at])
            at++;
        var start = Math.Max(0, at - 24);
        var take = Math.Min(actual.Length - start, 80);
        var near = take > 0 ? Encoding.UTF8.GetString(actual, start, take) : "";
        actual.Length.ShouldBe(expected.Length, $"{label} first difference at {at} near {near}");
        actual[at].ShouldBe(expected[at], $"{label} at {at} near {near}");
    }

    private static RunnerBufferDto UnicodeBuffer() => new(SessionId, Unicode, 7);

    private static PhoneHomeFrame Request(PhoneHomeOperation operation) =>
        new(PhoneHomeFrameKind.Request, 1, Guid.NewGuid(), operation,
            JsonSerializer.SerializeToElement(new { sessionId = SessionId }, PhoneHomeFraming.Json));

    private static IEnumerable<Shape> Shapes()
    {
        yield return new Shape("null", new(SessionId, "", 0),
            new(SessionId, "", "", 0, Started),
            new(SessionId, [], 0));
        yield return new Shape("empty", new(SessionId, "", 1),
            new(SessionId, "", "", 1, Started, null, null),
            new(SessionId, [], 1, false, null, null, null));
        yield return new Shape("unicode", UnicodeBuffer(), UnicodeSnapshot(Unicode), UnicodeTranscript(Unicode));
        yield return new Shape("large", new(SessionId, Large, 9), UnicodeSnapshot(Large), UnicodeTranscript(Large));
    }

    private static RunnerSnapshotDto UnicodeSnapshot(string text) =>
        new(SessionId, text, "screen " + Unicode, 4, Started, Started,
            new ProviderDialogObservation(SessionId, Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"),
                Started, 4, 2, ProviderDialogState.Blocked, "modal", true, true,
                new SubmittedInputProof(Guid.Parse("bbbbbbbb-bbbb-cccc-dddd-eeeeeeeeeeee"),
                    "bind-" + Unicode, 1, 2, "prompt-" + Unicode, 3),
                false, true, ProviderDialogState.Clear));

    private static RunnerTranscriptDto UnicodeTranscript(string text)
    {
        RunnerTranscriptEvent entry = new(SessionId, 3, "UserPrompt", null, null, Stamp, "user", text,
            "tool", "{\"a\":\"b\"}", "use-1", false, "end_turn", "api-1", 1, 2, 3, 4, false, null, null, "model-x", 5, "UTC");
        return new(SessionId, [entry], 8, true, Started,
            new RunnerTranscriptReadiness("reading", "bind", 8, 9, "waiting"), Stamp);
    }

    private sealed record Shape(string Name, RunnerBufferDto Buffer, RunnerSnapshotDto Snapshot, RunnerTranscriptDto Transcript);

    private sealed class FixedReads(RunnerBufferDto buffer, RunnerSnapshotDto snapshot, RunnerTranscriptDto transcript) : IPhoneHomeRuntimeSurface
    {
        public RunnerCapabilitiesDto Capabilities() => new("InboxConhost", "inbox", "test", false, Features: []);
        public string Health() => "Healthy";
        public IReadOnlyList<RunnerSessionDto> List() => [];
        public Task<RunnerSessionDto> GetAsync(Guid sessionId, CancellationToken ct) => throw new KeyNotFoundException();
        public Task<RunnerSessionDto> StartAsync(RunnerLaunchRequest request, CancellationToken ct) => throw new InvalidOperationException();
        public RunnerBufferDto GetBuffer(Guid sessionId) => buffer;
        public Task<RunnerSnapshotDto> GetSnapshotAsync(Guid sessionId, CancellationToken ct) => Task.FromResult(snapshot);
        public RunnerTranscriptDto GetTranscript(Guid sessionId) => transcript;
        public Task SendInputAsync(Guid sessionId, string input, CancellationToken ct) => Task.CompletedTask;
        public Task<RunnerConditionalInputResult> SendConditionalInputAsync(Guid sessionId, RunnerConditionalInputRequest request, CancellationToken ct) =>
            Task.FromResult(new RunnerConditionalInputResult(sessionId, ConditionalInputOutcomes.Unsupported));
        public Task ClearLiveBufferAsync(Guid sessionId, CancellationToken ct) => Task.CompletedTask;
        public Task ResizeAsync(Guid sessionId, int cols, int rows, CancellationToken ct) => Task.CompletedTask;
        public Task<RunnerKillGenerationResult> KillGenerationAsync(Guid sessionId, DateTime expectedAcceptedStartedAt, CancellationToken ct) =>
            Task.FromResult(new RunnerKillGenerationResult(sessionId, false, KillGenerationOutcomes.Missing, null));
        public int OwnedSessionCount => 0;
    }
}
