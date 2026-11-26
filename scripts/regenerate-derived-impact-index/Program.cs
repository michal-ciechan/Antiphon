using Antiphon.Tests.Scripts;
using Antiphon.ReleaseSelection;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

// Additive release-only artifact mode. Ordinary one-argument behavior below is unchanged.
// Inputs come from an immutable commit, never from candidate working bytes or schema 1.
if (args.Length >= 4 && args[0] == "--release-graph")
{
    try
    {
        var (provenSubtraction, compilerManifest, compilerManifestDigest) = ProofOptions(args[4..]);
        var repository = Path.GetFullPath(args[1]);
        var sha = args[2];
        if (!Regex.IsMatch(sha, "\\A[0-9a-f]{40}\\z", RegexOptions.CultureInvariant)
            || Text(Git(repository, ["rev-parse", "--verify", sha + "^{commit}"], pinObjects: provenSubtraction)).Trim() != sha)
            throw new ArgumentException("release graph requires an exact lowercase 40-hex commit SHA");
        var output = Path.GetFullPath(args[3]);
        var relativeOutput = Path.GetRelativePath(repository, output).Replace('\\', '/');
        if (!relativeOutput.StartsWith(".antiphon/selector-replay/", StringComparison.Ordinal)
            || !relativeOutput.EndsWith(".json", StringComparison.Ordinal)
            || File.Exists(output))
            throw new ArgumentException("release graph requires a fresh ignored .antiphon/selector-replay/*.json output");
        Git(repository, ["check-ignore", "--quiet", "--", relativeOutput]);

        var entries = Text(Git(repository, ["ls-tree", "-r", "-z", "--full-tree", sha], pinObjects: provenSubtraction))
            .Split('\0', StringSplitOptions.RemoveEmptyEntries).Select(row =>
            {
                var tab = row.IndexOf('\t');
                var header = row[..tab].Split(' ');
                return new { Path = row[(tab + 1)..], Mode = header[0], Blob = header[2] };
            }).OrderBy(row => row.Path, StringComparer.Ordinal).ToArray();
        var sourceEntries = entries.Where(row => row.Mode is "100644" or "100755"
            && row.Path.EndsWith(".cs", StringComparison.Ordinal)).ToArray();
        byte[] batch = sourceEntries.Length == 0 ? [] : Git(repository, ["cat-file", "--batch"],
            string.Join('\n', sourceEntries.Select(row => row.Blob)) + "\n", pinObjects: provenSubtraction);
        var inputs = entries.Where(row => row.Mode is "100644" or "100755")
            .ToDictionary(row => row.Path, _ => "", StringComparer.Ordinal);
        var position = 0;
        foreach (var entry in sourceEntries)
        {
            var end = Array.IndexOf(batch, (byte)'\n', position);
            if (end < 0) throw new InvalidDataException("release graph blob header missing");
            var header = Encoding.ASCII.GetString(batch, position, end - position).Split(' ');
            if (header.Length != 3 || header[0] != entry.Blob || header[1] != "blob")
                throw new InvalidDataException("release graph blob identity mismatch");
            var size = int.Parse(header[2], CultureInfo.InvariantCulture);
            position = end + 1;
            if (size < 0 || size >= batch.Length - position || batch[position + size] != (byte)'\n')
                throw new InvalidDataException("release graph blob payload missing");
            inputs[entry.Path] = new UTF8Encoding(false, true).GetString(batch, position, size);
            position += size + 1;
        }
        if (position != batch.Length) throw new InvalidDataException("release graph blob payload trailing data");

        var graph = ReleaseImpactGraphBuilder.Build(inputs);
        var unsupported = entries.Where(row => row.Mode is not ("100644" or "100755"))
            .Select(row => "unsupported-git-entry:" + row.Path);
        graph = graph with { IncompleteReasons = graph.IncompleteReasons.Concat(unsupported).Order(StringComparer.Ordinal).ToArray() };
        var inventory = string.Concat(entries.Select(row => row.Path + "\0" + row.Mode + "\0" + row.Blob + "\n"));
        var inventoryDigest = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(inventory)));
        var legacy = new
        {
            schema = "antiphon-release-graph-artifact-v1", sourceSha = sha,
            inputManifestDigest = inventoryDigest,
            inputs = entries, graph,
        };
        var artifact = ReleaseImpactGraphBuilder.Serialize(provenSubtraction ? ProvenArtifact() : legacy);
        object ProvenArtifact()
        {
            var binding = ReleaseCompilerInputs.Bind(sha,
                entries.Where(row => row.Mode is "100644" or "100755").ToDictionary(row => row.Path, row => row.Blob, StringComparer.Ordinal),
                Path.GetFullPath(compilerManifest!), compilerManifestDigest!);
            return new
            {
                schema = "antiphon-release-graph-artifact-v2", sourceSha = sha, inputManifestDigest = inventoryDigest,
                provenSubtraction = true, proofEligible = binding.Eligible, binding.Reason, binding.ManifestDigest,
                inputs = entries, graph,
            };
        }
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        using (var stream = new FileStream(output, FileMode.CreateNew, FileAccess.Write, FileShare.None)) stream.Write(artifact);
        Console.WriteLine($"release-impact graph incomplete/Full classes={graph.Universe.Length} paths={graph.Edges.Length} source={sha}");
        return 0;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine("release-impact graph refused: " + ex.GetType().Name + ": " + ex.Message);
        return 1;
    }
}

if (args.Length != 1 || string.IsNullOrWhiteSpace(args[0]))
{
    Console.Error.WriteLine("usage: Antiphon.DerivedImpactIndex <repo> | --release-graph <repo> <commit-sha> <fresh-ignored-json>"
        + " [--proven-subtraction --compiler-manifest <manifest.json> --expected-compiler-manifest-sha256 <sha256>]");
    return 1;
}

var repo = Path.GetFullPath(args[0]);
var path = Path.Combine(repo, "tests", "derived-impact-index.json");
var before = File.Exists(path) ? File.ReadAllBytes(path) : null;
try
{
    var index = DerivedImpactIndexBuilder.Build(repo);
    DerivedImpactIndexBuilder.Write(repo, index);
}
catch (Exception ex)
{
    Console.Error.WriteLine("derived-impact-index failed: " + ex.GetType().Name + ": " + ex.Message);
    return 1;
}

var after = File.ReadAllBytes(path);
var changed = before is null || !before.AsSpan().SequenceEqual(after);
Console.WriteLine(changed ? "derived-impact-index changed" : "derived-impact-index unchanged");
return 0;

static string Text(byte[] bytes) => Encoding.UTF8.GetString(bytes);

static (bool Enabled, string? Manifest, string? Digest) ProofOptions(string[] options)
{
    var enabled = false;
    var values = new Dictionary<string, string>(StringComparer.Ordinal);
    for (var i = 0; i < options.Length; i++)
    {
        var key = options[i];
        if (key == "--proven-subtraction" && !enabled)
        {
            enabled = true;
            continue;
        }
        if (key is not ("--compiler-manifest" or "--expected-compiler-manifest-sha256") || ++i >= options.Length
            || string.IsNullOrWhiteSpace(options[i]) || !values.TryAdd(key, options[i]))
            throw new ArgumentException("invalid release graph options");
    }
    var manifest = values.GetValueOrDefault("--compiler-manifest");
    var digest = values.GetValueOrDefault("--expected-compiler-manifest-sha256");
    if ((manifest is null) != (digest is null) || enabled && manifest is null)
        throw new ArgumentException("release graph proof requires a compiler manifest and its independently pinned SHA-256");
    return (enabled, manifest, digest);
}

static byte[] Git(string root, string[] arguments, string? input = null, bool pinObjects = false)
{
    var start = new ProcessStartInfo("git")
    {
        WorkingDirectory = root, RedirectStandardOutput = true, RedirectStandardError = true,
        RedirectStandardInput = input is not null, UseShellExecute = false,
    };
    foreach (var key in new[] { "GIT_DIR", "GIT_WORK_TREE", "GIT_INDEX_FILE" }) start.Environment.Remove(key);
    if (pinObjects) start.Environment["GIT_NO_REPLACE_OBJECTS"] = "1";
    foreach (var argument in arguments) start.ArgumentList.Add(argument);
    var watch = Stopwatch.StartNew();
    using var process = Process.Start(start) ?? throw new InvalidOperationException("Git did not start");
    using var output = new MemoryStream();
    var read = process.StandardOutput.BaseStream.CopyToAsync(output);
    var error = process.StandardError.ReadToEndAsync();
    var write = input is null ? Task.CompletedTask : WriteInput();
    try
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        process.WaitForExitAsync(deadline.Token).GetAwaiter().GetResult();
        Task.WhenAll(read, error, write).WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
        if (process.ExitCode != 0) throw new InvalidOperationException("Git input could not be verified");
        return output.ToArray();
    }
    finally
    {
        if (!process.HasExited)
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30)).GetAwaiter().GetResult();
        }
        Console.Error.WriteLine($"release-impact phase=git-{arguments[0]} elapsedMs={watch.ElapsedMilliseconds}");
    }

    async Task WriteInput()
    {
        await process.StandardInput.WriteAsync(input);
        process.StandardInput.Close();
    }
}
