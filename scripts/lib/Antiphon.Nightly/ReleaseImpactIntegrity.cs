#nullable enable
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

[assembly: InternalsVisibleTo("Antiphon.Tests")]

namespace Antiphon.Nightly;

/// <summary>The strict byte projection of an antiphon-p2-integrity-v1 diagnostic proposal.</summary>
public static partial class ReleaseImpactIntegrity
{
    public const string Format = "antiphon-p2-integrity-v1";
    public const int EnvelopeLimit = 8 * 1024 * 1024;
    public const int ProofLimit = 16 * 1024 * 1024;
    public const int AggregateProofLimit = 128 * 1024 * 1024;
    public const int CollectionLimit = 65_536;
    public const int StringLimit = 65_536;
    public const int DepthLimit = 64;

    private static readonly UTF8Encoding Utf8 = new(false, true);
    private static readonly Dictionary<string, Shape> Roles = CreateRoles();
    private static readonly string[] ContextNames =
    [
        "format", "repositoryId", "line", "baselineSha", "baselineTree", "candidateSha", "candidateTree",
        "runId", "attempt", "revision", "targetDigest", "algorithmSha",
    ];
    private static readonly string[] RootNames =
    [
        .. ContextNames, "algorithmDigest", "baselineManifestDigest", "candidateManifestDigest",
        "compatibilityDigest", "baselineIndexDigest", "candidateIndexDigest", "baselineGraphDigest",
        "candidateGraphDigest", "diffDigest", "universeDigest", "rosterDigest", "inventoryDigest",
        "receiptsDigest", "obligationsDigest", "trustDigest", "selectionDigest",
    ];
    private static readonly string[] RootWireNames = [.. RootNames, "rootDigest"];
    private static readonly string[] EnvelopeNames = [.. RootWireNames, "projections"];

    public static IReadOnlyList<string> StructuredRoles { get; } = Array.AsReadOnly(Roles.Keys.ToArray());

    /// <summary>Canonical root preimage. A supplied seal is checked for shape and excluded from the bytes.</summary>
    public static byte[] RootBytes(JsonElement root)
    {
        var fields = ReadObject(root, RootWireNames, "root");
        RequireText(fields["rootDigest"], "rootDigest", RcChunkReceipt.IsDigest);
        return EncodeRoot(fields);
    }

    private static byte[] EncodeRoot(IReadOnlyDictionary<string, JsonElement> root)
    {
        RequireText(root["format"], "format", value => value == Format);
        using var writer = new CanonicalWriter(false);
        foreach (var field in RootNames)
        {
            var value = root[field];
            switch (field)
            {
                case "attempt":
                case "revision":
                    UInt32(writer, value, field);
                    break;
                case "baselineSha":
                case "baselineTree":
                case "candidateSha":
                case "candidateTree":
                case "algorithmSha":
                    GitId(writer, value, field);
                    break;
                default:
                    if (field.EndsWith("Digest", StringComparison.Ordinal)) Digest(writer, value, field);
                    else Text(writer, value, field);
                    break;
            }
        }
        return writer.Bytes();
    }

    public static string RawSha256(ReadOnlySpan<byte> bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    public static byte[] ProjectionBytes(string role, JsonElement projection, bool foldedPaths)
    {
        if (!Roles.TryGetValue(role, out var shape)) throw Invalid("projection-role");
        using var writer = new CanonicalWriter(foldedPaths);
        writer.String(Format + "/" + role);
        shape(writer, projection, role);
        return writer.Bytes();
    }

    /// <summary>Writes a diagnostic envelope from supplied projections. This does not grant admission.</summary>
    public static byte[] Write(JsonElement context, IReadOnlyDictionary<string, JsonElement> projections, bool foldedPaths)
    {
        var root = ReadObject(context, ContextNames.Where(name => name != "targetDigest").ToArray(), "context");
        if (projections.Count != Roles.Count) throw Invalid("projections");
        foreach (var role in Roles.Keys)
        {
            if (!projections.TryGetValue(role, out var value)) throw Invalid(role);
            root.Add(role + "Digest", JsonSerializer.SerializeToElement(RawSha256(ProjectionBytes(role, value, foldedPaths))));
        }
        root.Add("rootDigest", JsonSerializer.SerializeToElement(RawSha256(EncodeRoot(root))));
        root.Add("projections", JsonSerializer.SerializeToElement(projections));
        var bytes = JsonSerializer.SerializeToUtf8Bytes(root);
        if (bytes.Length > EnvelopeLimit) throw Invalid("envelope-size");
        return bytes;
    }

    public sealed record Decoded(
        IReadOnlyDictionary<string, JsonElement> Root,
        IReadOnlyDictionary<string, JsonElement> Projections,
        string RootDigest);

    /// <summary>Validates the full wire shape and all proposal seals. Semantic admission requires independent evidence.</summary>
    public static Decoded Decode(Stream input, bool foldedPaths, Func<string, Stream>? openProof = null,
        int envelopeLimit = EnvelopeLimit)
    {
        if (envelopeLimit is <= 0 or > EnvelopeLimit) throw new ArgumentOutOfRangeException(nameof(envelopeLimit));
        using var document = Parse(ReadBounded(input, envelopeLimit, "envelope-size"));
        var fields = ReadObject(document.RootElement, EnvelopeNames, "envelope");
        var rootBytes = EncodeRoot(fields);
        var rootDigest = RequireText(fields["rootDigest"], "rootDigest", RcChunkReceipt.IsDigest);
        if (rootDigest != RawSha256(rootBytes)) throw Invalid("rootDigest");

        var projections = ReadObject(fields["projections"], Roles.Keys.ToArray(), "projections");
        var objects = new ProofObjects(openProof);
        foreach (var role in Roles.Keys)
        {
            var value = projections[role];
            if (value.ValueKind == JsonValueKind.Object && value.TryGetProperty("address", out _))
            {
                var address = ReadObject(value, ["address", "byteCount"], role);
                var sha = RequireText(address["address"], role, RcChunkReceipt.IsDigest);
                if (address["byteCount"].ValueKind != JsonValueKind.Number || !address["byteCount"].TryGetUInt64(out var length))
                    throw Invalid(role);
                try { projections[role] = value = objects.Get(role, sha, length); }
                catch (WireException) { throw new WireException(role, isAddress: true); }
            }
            var canonical = ProjectionBytes(role, value, foldedPaths);
            if (RawSha256(canonical) != fields[role + "Digest"].GetString()) throw Invalid(role);
        }

        var root = RootWireNames.ToDictionary(name => name, name => fields[name].Clone(), StringComparer.Ordinal);
        return new Decoded(new System.Collections.ObjectModel.ReadOnlyDictionary<string, JsonElement>(root),
            new System.Collections.ObjectModel.ReadOnlyDictionary<string, JsonElement>(
                projections.ToDictionary(pair => pair.Key, pair => pair.Value.Clone(), StringComparer.Ordinal)), rootDigest);
    }

    private sealed class ProofObjects(Func<string, Stream>? open)
    {
        private readonly Dictionary<string, (ulong Length, JsonElement Value)> _objects = new(StringComparer.Ordinal);
        private ulong _total;

        internal JsonElement Get(string role, string sha, ulong byteCount)
        {
            if (_objects.TryGetValue(sha, out var known))
            {
                if (known.Length != byteCount) throw Invalid(role);
                return known.Value;
            }
            if (open is null || byteCount > ProofLimit || byteCount > AggregateProofLimit - _total) throw Invalid(role);
            byte[] bytes;
            try
            {
                using var stream = open(sha);
                bytes = ReadBounded(stream, (int)byteCount, role);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                throw Invalid(role);
            }
            if ((ulong)bytes.Length != byteCount || RawSha256(bytes) != sha) throw Invalid(role);
            using var document = Parse(bytes);
            var value = document.RootElement.Clone();
            _objects.Add(sha, (byteCount, value));
            _total += byteCount;
            return value;
        }
    }

    internal static byte[] ReadBounded(Stream stream, int cap, string role)
    {
        if (stream.CanSeek && stream.Length - stream.Position > cap) throw Invalid(role);
        using var result = new MemoryStream();
        Span<byte> buffer = stackalloc byte[8192];
        while (result.Length < cap)
        {
            var read = stream.Read(buffer[..Math.Min(buffer.Length, cap - (int)result.Length)]);
            if (read == 0) return result.ToArray();
            result.Write(buffer[..read]);
        }
        if (stream.ReadByte() != -1) throw Invalid(role);
        return result.ToArray();
    }

    public static void CheckCollectionCount(ulong count)
    {
        if (count > CollectionLimit) throw Invalid("collection-size");
    }

    internal static JsonDocument Parse(ReadOnlyMemory<byte> bytes)
    {
        try
        {
            // JsonDocument defers decoding text. Validate UTF-8 even in fields later rejected by the schema.
            Utf8.GetCharCount(bytes.Span);
            var scan = new Utf8JsonReader(bytes.Span, new JsonReaderOptions { MaxDepth = DepthLimit });
            Span<int> sizes = stackalloc int[DepthLimit];
            while (scan.Read())
            {
                var depth = scan.CurrentDepth;
                switch (scan.TokenType)
                {
                    case JsonTokenType.StartArray:
                    case JsonTokenType.StartObject:
                        sizes[depth] = 0;
                        break;
                    case JsonTokenType.EndArray:
                    case JsonTokenType.EndObject:
                        break;
                    default:
                        if (scan.TokenType is JsonTokenType.String or JsonTokenType.PropertyName)
                        {
                            if (scan.ValueSpan.Length > 6 * StringLimit) throw Invalid("string-size");
                            if (scan.ValueIsEscaped)
                            {
                                var length = Math.Min(scan.ValueSpan.Length, StringLimit);
                                var decoded = System.Buffers.ArrayPool<byte>.Shared.Rent(length);
                                try { scan.CopyString(decoded.AsSpan(0, length)); }
                                catch (ArgumentException) { throw Invalid("string-size"); }
                                finally { System.Buffers.ArrayPool<byte>.Shared.Return(decoded); }
                            }
                            else if (scan.ValueSpan.Length > StringLimit) throw Invalid("string-size");
                        }
                        break;
                }
                if (depth > 0 &&
                    scan.TokenType is not (JsonTokenType.PropertyName or JsonTokenType.EndArray or JsonTokenType.EndObject))
                    CheckCollectionCount((ulong)++sizes[depth - 1]);
            }
            return JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = DepthLimit });
        }
        catch (Exception error) when (error is JsonException or DecoderFallbackException or InvalidOperationException)
        {
            throw Invalid("json");
        }
    }

    private static Dictionary<string, JsonElement> ReadObject(JsonElement value, IReadOnlyList<string> fields, string label)
    {
        if (value.ValueKind != JsonValueKind.Object) throw Invalid(label);
        var members = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var member in value.EnumerateObject())
        {
            if (!fields.Contains(member.Name, StringComparer.Ordinal) || !members.TryAdd(member.Name, member.Value))
                throw Invalid(label);
        }
        if (members.Count != fields.Count) throw Invalid(label);
        return members;
    }

    private static string ReadString(string? value, string label)
    {
        try
        {
            if (value is null || Utf8.GetByteCount(value) > StringLimit) throw Invalid(label);
            return value;
        }
        catch (EncoderFallbackException)
        {
            throw Invalid(label);
        }
    }

    private static string RequireText(JsonElement value, string label, Func<string, bool>? accepts = null)
    {
        if (value.ValueKind != JsonValueKind.String) throw Invalid(label);
        string text;
        try
        {
            text = ReadString(value.GetString(), label);
        }
        catch (InvalidOperationException)
        {
            throw Invalid(label);
        }
        if (accepts is not null && !accepts(text)) throw Invalid(label);
        return text;
    }

    public sealed class WireException : FormatException
    {
        internal WireException(string component, bool isAddress = false) : base("Invalid P2 evidence: " + component)
        {
            Component = component;
            IsAddress = isAddress;
        }

        public string Component { get; }
        public bool IsAddress { get; }
    }

    private static WireException Invalid(string component) => new(component);

    private delegate void Shape(CanonicalWriter writer, JsonElement value, string label);

    private static void Text(CanonicalWriter writer, JsonElement value, string label) => writer.String(RequireText(value, label));
    private static void GitId(CanonicalWriter writer, JsonElement value, string label) => writer.String(RequireText(value, label, RcChunkReceipt.IsSha));
    private static void Digest(CanonicalWriter writer, JsonElement value, string label) => writer.String(RequireText(value, label, RcChunkReceipt.IsDigest));
    private static void Path(CanonicalWriter writer, JsonElement value, string label) => writer.Path(RequireText(value, label), label);
    private static void UInt32(CanonicalWriter writer, JsonElement value, string label)
    {
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetUInt32(out var number)) throw Invalid(label);
        writer.Number(number);
    }

    private static void UInt64(CanonicalWriter writer, JsonElement value, string label)
    {
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetUInt64(out var number)) throw Invalid(label);
        writer.WideNumber(number);
    }

    private static void Boolean(CanonicalWriter writer, JsonElement value, string label)
    {
        if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) throw Invalid(label);
        writer.Flag(value.GetBoolean());
    }

    private static Shape Enum(params string[] tags) =>
        (writer, value, label) => writer.String(RequireText(value, label, tag => tags.Contains(tag, StringComparer.Ordinal)));

    private static Shape Optional(Shape inner) => (writer, value, label) =>
    {
        writer.Optional(value.ValueKind == JsonValueKind.Null ? null : () => inner(writer, value, label));
    };

    private static Shape Record(params (string Name, Shape Value)[] fields)
    {
        var names = fields.Select(field => field.Name).ToArray();
        return (writer, value, label) =>
        {
            var members = ReadObject(value, names, label);
            foreach (var (name, encode) in fields) encode(writer, members[name], label + "." + name);
        };
    }

    private static Shape Sequence(Shape item) => (writer, value, label) =>
    {
        writer.Number((uint)ArrayCount(value, label));
        foreach (var element in value.EnumerateArray()) item(writer, element, label);
    };

    private static int ArrayCount(JsonElement value, string label)
    {
        if (value.ValueKind != JsonValueKind.Array) throw Invalid(label);
        var count = value.GetArrayLength();
        CheckCollectionCount((ulong)count);
        return count;
    }

    private static Shape Set(Shape item, params string[] keyFields) => (writer, value, label) =>
    {
        var count = ArrayCount(value, label);
        var ordered = value.EnumerateArray().Select(element => (Key: Key(element, keyFields, label), Value: element))
            .OrderBy(row => row.Key, StringComparer.Ordinal).ToArray();
        writer.Number((uint)count);
        string? previous = null;
        foreach (var row in ordered)
        {
            if (row.Key == previous) throw Invalid(label + ".duplicate-key");
            item(writer, row.Value, label);
            previous = row.Key;
        }
    };

    private static string Key(JsonElement row, string[] fields, string label)
    {
        string Part(JsonElement value)
        {
            if (value.ValueKind == JsonValueKind.Null) return "\0";
            if (value.ValueKind == JsonValueKind.Number && value.TryGetUInt64(out var number))
                return number.ToString("D20", CultureInfo.InvariantCulture);
            return RequireText(value, label).Replace("\0", "\0\u0001", StringComparison.Ordinal);
        }

        if (fields.Length == 0) return Part(row);
        if (row.ValueKind != JsonValueKind.Object) throw Invalid(label);
        var parts = new string[fields.Length];
        for (var i = 0; i < fields.Length; i++)
        {
            if (!row.TryGetProperty(fields[i], out var value)) throw Invalid(label);
            parts[i] = Part(value);
        }
        return string.Join("\0\0", parts);
    }

    internal sealed class CanonicalWriter(bool foldedPaths) : IDisposable
    {
        private readonly MemoryStream _buffer = new();
        private readonly Dictionary<string, string> _aliases = new(StringComparer.OrdinalIgnoreCase);
        public void String(string value) => RcBoundary.AddString(_buffer, value, Utf8);
        public void Number(uint value) => RcBoundary.AddU32(_buffer, value);
        public void WideNumber(ulong value)
        {
            Span<byte> bytes = stackalloc byte[8];
            BinaryPrimitives.WriteUInt64BigEndian(bytes, value);
            _buffer.Write(bytes);
        }

        public void Flag(bool value) => _buffer.WriteByte(value ? (byte)1 : (byte)0);

        public void Optional(Action? writeValue)
        {
            Flag(writeValue is not null);
            writeValue?.Invoke();
        }

        public void Path(string value, string label)
        {
            var parts = value.Split('/');
            var prefix = "";
            foreach (var part in parts)
            {
                if (part is "" or "." or ".." || part.Any(c => char.IsControl(c) || c is '\\' or ':'))
                    throw Invalid(label);
                if (!foldedPaths) continue;
                if (part.EndsWith('.') || part.EndsWith(' ')) throw Invalid(label);
                prefix += "/" + part;
                var folded = prefix.Normalize(NormalizationForm.FormC);
                if (_aliases.TryGetValue(folded, out var prior) && prior != prefix) throw Invalid(label + ".target-alias");
                _aliases[folded] = prefix;
            }
            String(value);
        }

        public byte[] Bytes() => _buffer.ToArray();
        public void Dispose() => _buffer.Dispose();
    }
}
