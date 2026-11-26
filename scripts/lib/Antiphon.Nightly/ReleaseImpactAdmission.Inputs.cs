#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Antiphon.Nightly;

public static partial class ReleaseImpactAdmission
{
    public sealed record Target(string Os, string Architecture, string Profile, string Tfm, string Rid,
        string Configuration, bool FoldedPaths, IReadOnlyDictionary<string, string> BuildProperties, IReadOnlyList<string> Argv);

    public sealed record ObservedInput(string Side, string? Chunk, string Role, string Digest, string ProvenanceDigest, string SourceSha);
    public sealed record ToolFile(string Path, string Mode, string BlobId, ulong Size, string Digest);
    public sealed record Installation(string SourceRepository, string SourceSha, string EvaluatorVersion, string Extractor,
        IReadOnlyList<ToolFile> ReviewedFiles, string LoadedAssemblyDigest, Func<string, Stream> OpenInstalledFile);
    public sealed record Approval(Request ApprovedRequest, DateTimeOffset ObservedAt, DateTimeOffset ValidUntil, string Watermark, bool Withdrawn);
    public sealed record History(uint Ordinal, string Sha, string RunId, uint Attempt, uint Revision, string Chunk, string Status, string Reason);

    public sealed record Artifact(string Id, string Digest, ulong ByteCount, Func<Stream> Open);
    public sealed record InventoryPin(string RunId, uint Attempt, uint Revision, string Sha, Artifact Artifact);
    public sealed record ExecutionPin(string Chunk, string Sha, string RunId, uint Attempt, string InventorySeal, string InputDigest,
        Artifact Evidence);
    public sealed record TrustContract(string Project, string Side, string Sha, string Extractor, string ContractHash, string EvidenceDigest);
    public sealed record OwnExecution(ExecutionPin Pin, InventoryPin Inventory);

    // Evaluated source inputs are supplied by a reviewed, separately installed evaluator, never by the proposal.
    // The file reader serves immutable tree objects and excludes the index that the evaluator must regenerate.
    public sealed record Evaluation(string BuilderDigest, IReadOnlyList<string> BuilderInputs, byte[] RegeneratedIndex,
        RcInputSnapshot Inputs, IReadOnlyList<RcReaderCensusEntry> Readers, IReadOnlyDictionary<string, string> Compatibility);
    public sealed record Endpoint(string Sha, string Tree, IReadOnlyList<ToolFile> Files, Func<string, byte[]> ReadSourceBlob);

    public sealed record Authority(string RepositoryRoot, Target ObservedTarget, Installation Installation,
        Approval Approval, IReadOnlyList<Chunk> BaselineChunks, IReadOnlyList<History> History, IReadOnlyList<ObservedInput> ObservedInputs,
        Func<Endpoint, Evaluation?> Evaluate, InventoryPin Inventory, IReadOnlyList<ExecutionPin> Origins,
        IReadOnlyList<OwnExecution>? CurrentExecutions, IReadOnlyList<TrustContract> Contracts,
        Func<string, Stream>? OpenProjection = null);

    public sealed record Preparation(EvidenceSnapshot? Evidence, string Reason);

    private static readonly JsonSerializerOptions P2Json = new(JsonSerializerDefaults.Web);
    private static JsonElement Json<T>(T value) => JsonSerializer.SerializeToElement(value, P2Json);

    private sealed class Refusal(string reason) : Exception(reason);
    private static void Require(bool condition, string reason)
    {
        if (!condition) throw new Refusal(reason);
    }
}
