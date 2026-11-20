// A value that writes its own JSON: the plain C# records of the core (no PowerShell types) implement it, and the
// core's writers (NightlyJson.WriteFile) write it in place, so a record can sit inside a document a script builds.
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Antiphon.Nightly
{
    public interface IJsonWritable
    {
        void WriteJson(Utf8JsonWriter writer);
    }

    /// <summary>Compact JSON for the core's records: non-ASCII text unescaped, as the scripts' writers wrote it.</summary>
    public static class CompactJson
    {
        public static readonly JsonWriterOptions Options = new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, Indented = false };

        public static string Text(IJsonWritable value)
        {
            using var buffer = new System.IO.MemoryStream();
            using (var writer = new Utf8JsonWriter(buffer, Options)) value.WriteJson(writer);
            return System.Text.Encoding.UTF8.GetString(buffer.ToArray());
        }

        public static void Number(Utf8JsonWriter writer, string name, double? value)
        {
            if (value == null) writer.WriteNull(name);
            else writer.WriteNumber(name, value.Value);
        }
    }
}
