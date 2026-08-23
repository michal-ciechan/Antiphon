using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using System.Text.Json.Serialization.Metadata;

namespace Antiphon.Messaging.SchemaGen;

/// <summary>
/// JSON Schema (draft 2020-12) for the Kafka wire types, generated from the same
/// <see cref="MessagingJson"/> options the packages serialize with.
/// </summary>
public static class ContractSchema
{
    public const string ChannelMessageFileName = "channel-message.schema.json";
    public const string ChannelReplyFileName = "channel-reply.schema.json";

    private static readonly JsonSerializerOptions WriteIndented = new() { WriteIndented = true };

    private static readonly JsonSchemaExporterOptions ExporterOptions = new()
    {
        TreatNullObliviousAsNonNullable = true,
        TransformSchemaNode = static (context, schema) =>
        {
            var type = context.TypeInfo.Type;
            if (type == typeof(byte[]) && schema is JsonObject bytes)
            {
                bytes["contentEncoding"] = "base64";
                return bytes;
            }

            if (type.IsEnum)
            {
                var names = new JsonArray();
                foreach (var name in Enum.GetNames(type))
                    names.Add(name);
                return new JsonObject
                {
                    ["type"] = "string",
                    ["enum"] = names,
                };
            }

            return schema;
        },
    };

    public static string ChannelMessageJson => Generate(typeof(ChannelMessage));
    public static string ChannelReplyJson => Generate(typeof(ChannelReply));

    public static string Generate(Type type)
    {
        var options = new JsonSerializerOptions(MessagingJson.Options)
        {
            TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
        };
        var node = options.GetJsonSchemaAsNode(type, ExporterOptions);
        return node.ToJsonString(WriteIndented).Replace("\r\n", "\n") + "\n";
    }

    public static void Write(string directory)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, ChannelMessageFileName), ChannelMessageJson);
        File.WriteAllText(Path.Combine(directory, ChannelReplyFileName), ChannelReplyJson);
    }
}
