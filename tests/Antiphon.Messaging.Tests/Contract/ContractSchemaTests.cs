using System.Text.Json.Nodes;
using Antiphon.Messaging.SchemaGen;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Messaging.Tests.Contract;

public sealed class ContractSchemaTests
{
    [Test]
    public void Committed_schema_matches_generated()
    {
        var dir = ContractDirectory();
        File.ReadAllText(Path.Combine(dir, ContractSchema.ChannelMessageFileName))
            .ShouldBe(ContractSchema.ChannelMessageJson);
        File.ReadAllText(Path.Combine(dir, ContractSchema.ChannelReplyFileName))
            .ShouldBe(ContractSchema.ChannelReplyJson);
    }

    [Test]
    public void Byte_array_content_is_annotated_base64()
    {
        AssertHasBase64(ContractSchema.ChannelMessageJson);
        AssertHasBase64(ContractSchema.ChannelReplyJson);
    }

    private static void AssertHasBase64(string schemaJson)
    {
        var node = JsonNode.Parse(schemaJson) ?? throw new InvalidOperationException("schema did not parse");
        FindContentEncoding(node).ShouldBeTrue(
            "byte[] attachment content must be annotated contentEncoding: base64");
    }

    private static bool FindContentEncoding(JsonNode? node) => node switch
    {
        JsonObject obj when obj["contentEncoding"]?.GetValue<string>() == "base64" => true,
        JsonObject obj => obj.Any(kv => FindContentEncoding(kv.Value)),
        JsonArray arr => arr.Any(FindContentEncoding),
        _ => false,
    };

    private static string ContractDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "docs", "messaging", "contract", "v1");
            if (Directory.Exists(candidate) && File.Exists(Path.Combine(candidate, ContractSchema.ChannelMessageFileName)))
                return candidate;
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not find docs/messaging/contract/v1 above " + AppContext.BaseDirectory);
    }
}
