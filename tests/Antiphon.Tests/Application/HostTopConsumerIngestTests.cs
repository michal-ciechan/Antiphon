using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Infrastructure.HostLoad;
using Antiphon.SessionRunner.Contracts;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>CARD-1884. Ingest stays schema 1 and refuses a top-consumer row that is not publishable.</summary>
[Category("Unit")]
public class HostTopConsumerIngestTests
{
    private static readonly Guid Store = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Boot = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Test]
    public void Valid_compiler_row_is_accepted()
    {
        Should.NotThrow(() => Validate(Sample([new HostLoadTopConsumer(1, 10, "VBCSCompiler", "compiler", 5.3, 409600)])));
    }

    [Test]
    public void Ninth_row_and_unsafe_name_are_rejected()
    {
        var nine = Enumerable.Range(1, 9).Select(pid => new HostLoadTopConsumer(pid, 1, "pwsh", "other", 0.1, 1)).ToArray();
        Should.Throw<ValidationException>(() => Validate(Sample(nine)));
        Should.Throw<ValidationException>(() => Validate(Sample([new HostLoadTopConsumer(1, 1, "secret/TOKEN", "compiler", 1, 1)])));
        Should.Throw<ValidationException>(() => Validate(Sample([new HostLoadTopConsumer(1, 1, "pipename=SUPERSECRET", "other", 1, 1)])));
    }

    [Test]
    public void Unknown_role_is_rejected()
    {
        Should.Throw<ValidationException>(() => Validate(Sample(
            [new HostLoadTopConsumer(1, 1, "VBCSCompiler", "compiler-server", 1, 1)])));
    }

    [Test]
    public void Null_cpu_and_rss_stay_publishable()
    {
        Should.NotThrow(() => Validate(Sample([new HostLoadTopConsumer(1, 1, "VBCSCompiler", "compiler", null, null)])));
    }

    [Test]
    public void Absent_or_empty_top_consumers_keep_the_pre_upgrade_canonical_hash()
    {
        var sample = Sample([]);
        var current = ServerCanonical(sample);
        var local = Canonicalize(JsonSerializer.SerializeToElement(sample, PhoneHomeFraming.Json));
        local.ShouldBe(current, "the witness canonical writer must match HostLoadIngestService");

        var absent = JsonNode.Parse(current)!.AsObject();
        absent.ContainsKey("topConsumers").ShouldBeTrue();
        absent.Remove("topConsumers");
        var preupgrade = Canonicalize(JsonSerializer.SerializeToElement(absent, PhoneHomeFraming.Json));
        preupgrade.ShouldNotContain("topConsumers");

        var replay = JsonSerializer.Deserialize<HostLoadSample>(preupgrade, PhoneHomeFraming.Json);
        replay.ShouldNotBeNull();
        replay.TopConsumers.ShouldBeEmpty();
        ServerCanonical(replay).ShouldBe(preupgrade);

        var explicitEmpty = JsonSerializer.Deserialize<HostLoadSample>(current, PhoneHomeFraming.Json);
        explicitEmpty.ShouldNotBeNull();
        ServerCanonical(explicitEmpty).ShouldBe(preupgrade);

        var changed = sample with { TopConsumers = [new HostLoadTopConsumer(1, 10, "VBCSCompiler", "compiler", 5.3, 409600)] };
        var populated = ServerCanonical(changed);
        populated.ShouldContain("VBCSCompiler");
        populated.ShouldNotBe(preupgrade);
        Sha(populated).ShouldNotBe(Sha(preupgrade));
    }

    [Test]
    public void Schema_stays_one_and_session_names_stay_closed()
    {
        Should.Throw<ValidationException>(() => Validate(Sample([], schema: 2)));
        Should.Throw<ValidationException>(() => Validate(Sample([], processName: "VBCSCompiler")));
        var accepted = Sample([new HostLoadTopConsumer(1, 10, "dotnet", "test", 1.4, 1024)]);
        Should.NotThrow(() => Validate(accepted));
        accepted.SchemaVersion.ShouldBe(1);
    }

    private static HostLoadSample Sample(IReadOnlyList<HostLoadTopConsumer> top, int schema = 1, string? processName = null)
        => new()
        {
            SchemaVersion = schema,
            Source = new HostLoadSource("runner-a", Store, Boot, 1),
            SampledAt = new DateTimeOffset(2026, 11, 13, 0, 0, 0, TimeSpan.Zero),
            IntervalSeconds = 5,
            CpuPercent = 10,
            Processes = processName is null ? [] : [new HostLoadProcess(processName, null, 1, 2)],
            TopConsumers = top,
        };

    private static void Validate(HostLoadSample sample) =>
        HostLoadIngestService.Validate(new HostLoadIngestBatch("runner-a", null,
            new HostLoadHistoryPage(1, sample.Source, [sample], [], null, false, true)));

    private static string ServerCanonical(HostLoadSample sample)
    {
        var method = typeof(HostLoadIngestService).GetMethod("CanonicalPayload", BindingFlags.NonPublic | BindingFlags.Static);
        method.ShouldNotBeNull();
        return (string)method.Invoke(null, [sample])!;
    }

    private static string Sha(string payload) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));

    private static string Canonicalize(JsonElement element)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
            WriteCanonical(writer, element);
        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static void WriteCanonical(Utf8JsonWriter writer, JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            writer.WriteStartObject();
            foreach (var property in element.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal))
            {
                writer.WritePropertyName(property.Name);
                WriteCanonical(writer, property.Value);
            }

            writer.WriteEndObject();
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            writer.WriteStartArray();
            foreach (var item in element.EnumerateArray())
                WriteCanonical(writer, item);
            writer.WriteEndArray();
        }
        else
        {
            element.WriteTo(writer);
        }
    }
}
