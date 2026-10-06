using System.Text.Json;
using System.Text.Json.Serialization;
using Antiphon.Server.Application.Dtos;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Unit")]
public class AttentionKindWireTests
{
    private static readonly JsonSerializerOptions ServerJson = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false) },
    };

    [Test]
    public void Every_attention_kind_has_a_distinct_wire_value()
    {
        var values = Enum.GetNames<AttentionKind>()
            .Select(name => (Name: name, Value: (int)Enum.Parse<AttentionKind>(name)))
            .ToList();
        var duplicate = values.GroupBy(entry => entry.Value)
            .FirstOrDefault(group => group.Count() > 1);

        duplicate.ShouldBeNull($"duplicate-attention-kind-value: {string.Join(", ", duplicate?.Select(entry => entry.Name) ?? [])}");
    }

    [Test]
    public void Runner_and_task_input_kinds_keep_their_server_json_names()
    {
        JsonSerializer.Serialize(AttentionKind.RunnerUnavailable, ServerJson)
            .ShouldBe("\"RunnerUnavailable\"", "runner-unavailable-wire-name");
        JsonSerializer.Deserialize<AttentionKind>("\"RunnerUnavailable\"", ServerJson)
            .ShouldBe(AttentionKind.RunnerUnavailable, "runner-unavailable-round-trip");
        JsonSerializer.Serialize(AttentionKind.TaskInputUnreadable, ServerJson)
            .ShouldBe("\"TaskInputUnreadable\"", "task-input-unreadable-wire-name");
        JsonSerializer.Deserialize<AttentionKind>("\"TaskInputUnreadable\"", ServerJson)
            .ShouldBe(AttentionKind.TaskInputUnreadable, "task-input-unreadable-round-trip");
    }
    [Test]
    public void Host_cleanup_kinds_have_distinct_appended_values_and_json_names()
    {
        var kinds = new[]
        {
            AttentionKind.HostCleanupSummary,
            AttentionKind.HostCleanupDiskPressure,
            AttentionKind.HostCleanupHoldExpired,
            AttentionKind.WorktreeCleanupBacklog,
        };
        kinds.Select(kind => (int)kind).ShouldBe(new[] { 53, 54, 55, 56 }, "cleanup-appended-wire-values");
        foreach (var kind in kinds)
        {
            Enum.GetNames<AttentionKind>().Count(name => Enum.Parse<AttentionKind>(name) == kind)
                .ShouldBe(1, "cleanup-distinct-wire-value");
            var json = JsonSerializer.Serialize(kind, ServerJson);
            json.ShouldBe($"\"{kind}\"", "cleanup-json-name");
            JsonSerializer.Deserialize<AttentionKind>(json, ServerJson).ShouldBe(kind, "cleanup-json-round-trip");
        }
    }

    [Test]
    public void Seat_occupancy_kinds_keep_appended_values_57_through_59()
    {
        // S3 review: the cleanup pin stops at 56, so a renumber of the seat kinds would be silent.
        var kinds = new[]
        {
            AttentionKind.SeatIdle,
            AttentionKind.OccupancyDivergence,
            AttentionKind.SlotOrphan,
        };
        kinds.Select(kind => (int)kind).ShouldBe(new[] { 57, 58, 59 }, "seat-appended-wire-values");
        foreach (var kind in kinds)
        {
            var json = JsonSerializer.Serialize(kind, ServerJson);
            json.ShouldBe($"\"{kind}\"", "seat-json-name");
            JsonSerializer.Deserialize<AttentionKind>(json, ServerJson).ShouldBe(kind, "seat-json-round-trip");
        }
    }

}
