using System.Text.Json.Serialization;

namespace Antiphon.SessionRunner.Contracts;

/// <summary>
/// Metadata for the hot runner read bodies. Options on each
/// <see cref="RunnerReadJson"/> instance select the encoder; the names stay Web camelCase.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    NumberHandling = JsonNumberHandling.AllowReadingFromString)]
[JsonSerializable(typeof(RunnerBufferDto))]
[JsonSerializable(typeof(RunnerSnapshotDto))]
[JsonSerializable(typeof(RunnerTranscriptDto))]
[JsonSerializable(typeof(RunnerTranscriptEvent))]
[JsonSerializable(typeof(IReadOnlyList<RunnerTranscriptEvent>))]
[JsonSerializable(typeof(RunnerTranscriptReadiness))]
[JsonSerializable(typeof(ProviderDialogObservation))]
[JsonSerializable(typeof(SubmittedInputProof))]
[JsonSerializable(typeof(ProviderDialogState))]
public partial class RunnerReadJsonContext : JsonSerializerContext
{
}
