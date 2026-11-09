using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Antiphon.SessionRunner.Contracts;

/// <summary>
/// Source-generated JSON for buffer, snapshot, and transcript reads.
/// Phone-home keeps the Web default encoder. Runner HTTP keeps the relaxed encoder
/// <c>Results.Ok</c> already writes. There is no cache: each call serializes the body it was given.
/// </summary>
public static class RunnerReadJson
{
    private static readonly JsonSerializerOptions PhoneOptions = new(JsonSerializerDefaults.Web);

    private static readonly JsonSerializerOptions HttpOptions = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static RunnerReadJsonContext Phone { get; } = new(PhoneOptions);

    public static RunnerReadJsonContext Http { get; } = new(HttpOptions);

    public static JsonTypeInfo<RunnerBufferDto> PhoneBuffer => Phone.RunnerBufferDto;

    public static JsonTypeInfo<RunnerSnapshotDto> PhoneSnapshot => Phone.RunnerSnapshotDto;

    public static JsonTypeInfo<RunnerTranscriptDto> PhoneTranscript => Phone.RunnerTranscriptDto;

    public static JsonTypeInfo<RunnerBufferDto> HttpBuffer => Http.RunnerBufferDto;

    public static JsonTypeInfo<RunnerSnapshotDto> HttpSnapshot => Http.RunnerSnapshotDto;

    public static JsonTypeInfo<RunnerTranscriptDto> HttpTranscript => Http.RunnerTranscriptDto;

    public static JsonTypeInfo<T> HttpOf<T>() where T : class
    {
        if (typeof(T) == typeof(RunnerBufferDto))
            return (JsonTypeInfo<T>)(object)HttpBuffer;
        if (typeof(T) == typeof(RunnerSnapshotDto))
            return (JsonTypeInfo<T>)(object)HttpSnapshot;
        if (typeof(T) == typeof(RunnerTranscriptDto))
            return (JsonTypeInfo<T>)(object)HttpTranscript;
        throw new InvalidOperationException(
            "Hot runner HTTP read has no source-generated contract for " + typeof(T).FullName + ".");
    }

    internal static readonly AsyncLocal<AdmissionBox?> Witness = new();

    internal static WitnessScope Watch() => new();

    public static JsonTypeInfo<T> Use<T>(JsonTypeInfo<T> type)
    {
        if (type.OriginatingResolver is not RunnerReadJsonContext)
            throw new InvalidOperationException("Hot runner read JSON requires the source-generated contract.");
        if (Witness.Value is { } box)
            box.Note(type);
        return type;
    }

    public static T? ReadElement<T>(JsonElement? payload, JsonTypeInfo<T> type) where T : class
    {
        var contract = Use(type);
        return payload is { } element ? element.Deserialize(contract) : null;
    }

    internal sealed class AdmissionBox
    {
        public int Count { get; private set; }

        public List<JsonTypeInfo> Admitted { get; } = new(4);

        public void Note(JsonTypeInfo type)
        {
            Count++;
            Admitted.Add(type);
        }
    }

    internal readonly struct WitnessScope : IDisposable
    {
        private readonly AdmissionBox _box;

        public WitnessScope()
        {
            _box = new AdmissionBox();
            Witness.Value = _box;
        }

        public int Count => _box.Count;

        public IReadOnlyList<JsonTypeInfo> Admitted => _box.Admitted;

        public void Dispose()
        {
            if (ReferenceEquals(Witness.Value, _box))
                Witness.Value = null;
        }
    }
}
