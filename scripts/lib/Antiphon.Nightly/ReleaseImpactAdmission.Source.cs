#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Antiphon.Nightly;

public static partial class ReleaseImpactAdmission
{
    private sealed record FileBinding(string Path, string GitMode, string BlobId, ulong RawByteCount, string RawSha256);
    private sealed record SimpleEdge(string Path, string Class);
    private sealed record GraphEdge(string Kind, string Source, string Destination, string Class);
    private sealed record Change(string Path, string Status, string Side);

    private sealed class GitSnapshot(string root) : IDisposable
    {
        private readonly Dictionary<string, byte[]> _blobs = new(StringComparer.Ordinal);
        private int _bytes;
        private Process? _batch;
        private Stream? _batchOutput;

        public string Command(params string[] argv) => TestSelectionGit.Run(root, argv).Stdout;
        public bool Commit(string sha) => RcChunkReceipt.IsSha(sha) &&
            TestSelectionGit.Run(root, ["cat-file", "-t", sha], [0, 1, 128]).Stdout.Trim() == "commit";

        public bool Ancestor(string before, string after) =>
            TestSelectionGit.Run(root, ["merge-base", "--is-ancestor", before, after], [0, 1]).Exit == 0;

        public Tree ReadTree(string sha)
        {
            var listing = Command("ls-tree", "-r", "-z", "--full-tree", sha);
            var files = new Dictionary<string, FileBinding>(StringComparer.Ordinal);
            foreach (var row in listing.Split('\0', StringSplitOptions.RemoveEmptyEntries))
            {
                var tab = row.IndexOf('\t');
                Require(tab > 0, "p2-input:tree");
                var fields = row[..tab].Split(' ');
                Require(fields.Length == 3 && fields[0] is "100644" or "100755" && fields[1] == "blob"
                    && RcChunkReceipt.IsSha(fields[2]), "p2-incomplete:tree-entry");
                var path = row[(tab + 1)..];
                var bytes = Blob(fields[2]);
                Require(files.TryAdd(path, new FileBinding(path, fields[0], fields[2], (ulong)bytes.Length,
                    ReleaseImpactIntegrity.RawSha256(bytes))), "p2-input:tree");
            }
            return new Tree(this, sha, Command("rev-parse", sha + "^{tree}").Trim(), files, listing);
        }

        public Change[] Diff(string before, string after)
        {
            var raw = Command("diff", "--name-status", "--no-renames", "-z", before, after, "--");
            if (raw.Length == 0) return [];
            Require(raw.EndsWith('\0'), "p2-input:diff");
            var fields = raw[..^1].Split('\0');
            Require(fields.Length % 2 == 0, "p2-input:diff");
            var changes = new List<Change>();
            for (var index = 0; index < fields.Length; index += 2)
            {
                var (status, path) = (fields[index], fields[index + 1]);
                Require(status is "A" or "D" or "M" or "T", "p2-incomplete:diff");
                if (status != "A") changes.Add(new Change(path, status, "B"));
                if (status != "D") changes.Add(new Change(path, status, "A"));
            }
            return changes.ToArray();
        }

        public byte[] Blob(string id)
        {
            if (_blobs.TryGetValue(id, out var bytes)) return bytes;
            Require(RcChunkReceipt.IsSha(id), "p2-input:blob-id");
            var batch = _batch ?? StartBatch();
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(4));
            try
            {
                bytes = ReadBlob(batch, id, deadline.Token).GetAwaiter().GetResult();
                Require(ReleaseImpactEvidence.BlobId(bytes, 40) == id, "p2-input:blob-bytes");
                _bytes += bytes.Length;
                _blobs.Add(id, bytes);
                return bytes;
            }
            catch (OperationCanceledException)
            {
                throw new Refusal("p2-incomplete:blob-deadline");
            }
        }

        private Process StartBatch()
        {
            var start = new ProcessStartInfo("git")
            {
                WorkingDirectory = root, UseShellExecute = false, RedirectStandardInput = true,
                RedirectStandardOutput = true, RedirectStandardError = true,
            };
            foreach (var key in new[] { "GIT_DIR", "GIT_WORK_TREE", "GIT_INDEX_FILE" }) start.Environment.Remove(key);
            start.Environment["GIT_NO_REPLACE_OBJECTS"] = "1";
            start.Environment["GIT_NO_LAZY_FETCH"] = "1";
            start.ArgumentList.Add("cat-file");
            start.ArgumentList.Add("--batch");
            _batch = Process.Start(start) ?? throw new Refusal("p2-input:blob-open");
            _batch.BeginErrorReadLine();
            _batchOutput = new BufferedStream(_batch.StandardOutput.BaseStream);
            return _batch;
        }

        private async Task<byte[]> ReadBlob(Process child, string id, CancellationToken cancellation)
        {
            var input = child.StandardInput.BaseStream;
            await input.WriteAsync(Encoding.ASCII.GetBytes(id + "\n"), cancellation).ConfigureAwait(false);
            await input.FlushAsync(cancellation).ConfigureAwait(false);
            var output = _batchOutput!;
            var header = new byte[128];
            var length = 0;
            for (; length < header.Length; length++)
            {
                await output.ReadExactlyAsync(header.AsMemory(length, 1), cancellation).ConfigureAwait(false);
                if (header[length] == '\n') break;
            }
            Require(length < header.Length, "p2-input:blob-header");
            var columns = Encoding.ASCII.GetString(header, 0, length).Split(' ');
            Require(columns.Length == 3 && columns[0] == id && columns[1] == "blob", "p2-input:blob-type");
            if (!int.TryParse(columns[2], System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out var count))
                throw new Refusal("p2-input:blob-size");
            Require(count >= 0 && count <= ReleaseImpactIntegrity.ProofLimit &&
                count <= ReleaseImpactIntegrity.AggregateProofLimit - _bytes, "p2-incomplete:blob-size");
            var result = new byte[count];
            await output.ReadExactlyAsync(result, cancellation).ConfigureAwait(false);
            await output.ReadExactlyAsync(header.AsMemory(0, 1), cancellation).ConfigureAwait(false);
            Require(header[0] == '\n', "p2-input:blob-framing");
            return result;
        }

        public void Dispose()
        {
            if (_batch is null) return;
            try
            {
                _batch.StandardInput.Close();
                if (!_batch.WaitForExit(2_000))
                {
                    _batch.Kill(entireProcessTree: true);
                    _batch.WaitForExit(2_000);
                }
            }
            finally
            {
                _batchOutput?.Dispose();
                _batch.Dispose();
            }
        }
    }

    private sealed record Tree(GitSnapshot Git, string Sha, string Id, Dictionary<string, FileBinding> Files, string Listing)
    {
        public FileBinding File(string path) => Files.TryGetValue(path, out var file) ? file : throw new Refusal("p2-incomplete:source-file");

        public byte[] Blob(string path) => Git.Blob(File(path).BlobId);
        public string Text(string path) => new UTF8Encoding(false, true).GetString(Blob(path)).TrimStart('\uFEFF');
    }

    private sealed record SideSource(Tree Tree, Evaluation Evaluation, RcProjectGraph ProjectGraph, SimpleEdge[] Edges,
        string[] Capped, JsonElement Index, JsonElement Contract);

    private static SideSource ReadSide(Authority authority, Tree tree, string side)
    {
        var pinned = tree.Files.Values.Where(file => file.Path != RcChunkSelection.IndexPath)
            .Select(file => new ToolFile(file.Path, file.GitMode, file.BlobId, file.RawByteCount, file.RawSha256)).ToArray();
        var evaluation = authority.Evaluate(new Endpoint(tree.Sha, tree.Id, Freeze(pinned), path =>
        {
            Require(path != RcChunkSelection.IndexPath, "p2-incomplete:index-builder-input");
            return tree.Blob(path).ToArray();
        })) ?? throw new Refusal("p2-incomplete:evaluator");
        Require(evaluation.Inputs.Sha == tree.Sha && evaluation.Inputs.TreeId == tree.Id, "p2-input:closure:" + side);
        Require((evaluation.Inputs.Unowned ?? []).Length == 0, "p2-incomplete:closure:" + side);
        foreach (var input in evaluation.Inputs.Suites.SelectMany(suite => suite.Inputs))
        {
            if (!input.Present) Require(!tree.Files.ContainsKey(input.Path), "p2-input:closure:" + side);
            else
            {
                var file = tree.File(input.Path);
                Require(input.Mode == file.GitMode && input.Type == "blob" && input.ObjectId == file.BlobId, "p2-input:closure:" + side);
            }
        }

        var index = tree.File(RcChunkSelection.IndexPath);
        var raw = tree.Blob(index.Path);
        Require(evaluation.RegeneratedIndex.AsSpan().SequenceEqual(raw), "p2-incomplete:index-regeneration:" + side);
        using var doc = ReleaseImpactIntegrity.Parse(raw);
        var value = doc.RootElement;
        var version = value.GetProperty("schema").GetUInt32();
        Require(version == 1 && evaluation.BuilderInputs.Count > 0 &&
            !evaluation.BuilderInputs.Contains(RcChunkSelection.IndexPath, StringComparer.Ordinal), "p2-incomplete:index-builder:" + side);
        var edges = value.GetProperty("edges").EnumerateArray()
            .SelectMany(row => row.GetProperty("classes").EnumerateArray().Select(c =>
                new SimpleEdge(row.GetProperty("path").GetString()!, c.GetString()!))).ToArray();
        var capped = value.GetProperty("cappedPaths").EnumerateArray().Select(row =>
            row.ValueKind == JsonValueKind.String ? row.GetString()! : row.GetProperty("path").GetString()!).ToArray();
        var indexWire = Json(new
        {
            side, commit = tree.Sha, index.Path, index.BlobId, index.RawByteCount, index.RawSha256, schema = version,
            evaluation.BuilderDigest, builderInputs = evaluation.BuilderInputs.Select(tree.File).ToArray(),
            metadata = value.EnumerateObject().Where(member => member.Name is not ("edges" or "schema"))
                .Select(member => new { name = member.Name, digest = TestSelectionJson.Digest(member.Value.GetRawText()) }).ToArray(),
            edges, cappedPaths = capped,
        });
        var graph = RcProjectSelection.Read(authority.RepositoryRoot, tree.Sha, listing: tree.Listing);
        Require(graph.FallbackReasons.Length == 0, "p2-incomplete:project-graph:" + side);

        var contract = TestSelection.Contract(authority.RepositoryRoot, tree.Sha, authority.ObservedTarget.Profile);
        var policy = ExecutionPolicy.Parse(tree.Text(TestSelection.PolicyPath));
        Require(policy.SelectionCatalog?.Path == TestSelection.CatalogPath, "p2-input:compatibility:" + side);
        Require(contract["platform"] as string == authority.ObservedTarget.Os, "p2-incomplete:target-contract:" + side);
        return new SideSource(tree, evaluation, graph, edges, capped, indexWire, Json(new
        {
            catalogHash = contract["catalogHash"], policyHash = contract["policyHash"],
            runtimeHash = contract["runtimeHash"], selectorHash = contract["selectorHash"],
        }));
    }
}
