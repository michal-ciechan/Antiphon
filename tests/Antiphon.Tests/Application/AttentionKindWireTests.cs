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
}
