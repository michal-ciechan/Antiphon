using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LibGit2Sharp;

namespace Antiphon.Tests.TestHelpers;

/// <summary>
/// A frozen tree of git repositories (any mix of repositories, bare remotes, linked worktrees and clones under
/// one root) that is cloned for each test instead of being rebuilt with git processes.
/// <para>
/// A template comes from one of three places:
/// <list type="bullet">
/// <item><see cref="BuildOnceAsync"/>: built by a callback the first time a key is asked for, then shared by
/// the whole test process;</item>
/// <item><see cref="FromDirectory"/>: an existing folder that holds a template (see <see cref="SaveTo"/>);</item>
/// <item><see cref="FromArchive"/>: a zip written by <see cref="SaveArchive"/>, extracted once per process.</item>
/// </list>
/// </para>
/// <para>
/// <see cref="CloneTo"/> produces a tree that is equivalent to building it again with different values for the
/// template's tokens, at a different root:
/// <list type="bullet">
/// <item><b>Tokens</b> (for example a task id) are replaced everywhere: in file and directory names (so a branch
/// named <c>feat/card-task-&lt;id&gt;</c> is renamed), in every git metadata file, in working-tree files, and in
/// git objects. A blob, tree or commit whose content holds a token is rewritten (same author, committer,
/// times and message, token replaced), so object ids change exactly as they would have on a fresh build;
/// the old-to-new id map is applied to refs, reflogs and the binary index (raw ids, then its checksum).</item>
/// <item><b>The root path</b> is replaced in git metadata (configs, <c>.git</c> gitfiles,
/// <c>worktrees/*/gitdir</c>, logs) in all its spellings: backslash, forward slash and config-escaped.</item>
/// <item><b>Timestamps</b> (creation and last write) are kept, so on Windows git trusts the copied index stat
/// data and a first <c>git status</c> does not rewrite it. Elsewhere (inode numbers change) each work tree's
/// index is refreshed once.</item>
/// </list>
/// Objects are copied, never hard-linked, so a test that damages its clone's objects cannot reach the
/// template. Not supported, and refused: a token inside an index path name, and committed files that hold an
/// absolute path under the root (they cannot change without changing history).
/// </para>
/// </summary>
internal sealed class GitTreeTemplate
{
    public const string EnvVar = "ANTIPHON_TEST_GIT_TEMPLATE";
    private const string ManifestName = ".antiphon-git-template.json";

    /// <summary>Templates are on unless <c>ANTIPHON_TEST_GIT_TEMPLATE=off</c>.</summary>
    public static bool Enabled { get; } =
        !string.Equals(Environment.GetEnvironmentVariable(EnvVar)?.Trim(), "off", StringComparison.OrdinalIgnoreCase);

    private static readonly ConcurrentDictionary<string, Lazy<Task<GitTreeTemplate>>> Built = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, Lazy<GitTreeTemplate>> Extracted = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Where the template's files are. Read-only by convention: clones never write here.</summary>
    public string Directory { get; }
    /// <summary>The root path as it is spelled inside the template's files (where it was built).</summary>
    public string WrittenRoot { get; }
    /// <summary>Token name to the value the template was built with (each value at least 8 characters).</summary>
    public IReadOnlyDictionary<string, string> Tokens { get; }
    /// <summary>Free-form values recorded at build time (for example a seed commit id).</summary>
    public IReadOnlyDictionary<string, string> Values { get; }

    private readonly Dictionary<string, (long Created, long Written)> _times;

    private GitTreeTemplate(string directory, Manifest manifest)
    {
        Directory = directory;
        WrittenRoot = manifest.WrittenRoot;
        Tokens = manifest.Tokens;
        Values = manifest.Values;
        _times = manifest.Times ?? [];
    }

    public sealed record Manifest(string WrittenRoot, Dictionary<string, string> Tokens, Dictionary<string, string> Values,
        Dictionary<string, (long Created, long Written)>? Times = null);

    /// <summary>The result of a clone: its root and the object ids that changed because a token did.</summary>
    public sealed record Clone(string Root, IReadOnlyDictionary<string, string> ObjectMap)
    {
        /// <summary>The clone's id for a template object id (unchanged when no token was in it).</summary>
        public string Map(string templateId) => ObjectMap.TryGetValue(templateId, out var id) ? id : templateId;
    }

    // ---- sources -------------------------------------------------------------------------------------------

    /// <summary>
    /// The template for <paramref name="key"/>, built by <paramref name="build"/> the first time in this process.
    /// <paramref name="build"/> receives an empty root and the token values to build with, and returns values to
    /// record. The template is deleted when the process exits.
    /// </summary>
    public static async Task<GitTreeTemplate> BuildOnceAsync(string key, IReadOnlyCollection<string> tokenNames,
        Func<string, IReadOnlyDictionary<string, string>, Task<Dictionary<string, string>>> build)
    {
        var lazy = Built.GetOrAdd(key, _ => new Lazy<Task<GitTreeTemplate>>(async () =>
        {
            using var span = TestTrace.Span("git-template build");
            SweepStale();
            var root = Path.Combine(Path.GetTempPath(), "antiphon-gittpl-" + Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(root);
            AppDomain.CurrentDomain.ProcessExit += (_, _) => DeleteTree(root);
            var tokens = tokenNames.ToDictionary(n => n, _ => Guid.NewGuid().ToString("N"), StringComparer.Ordinal);
            var values = await build(root, tokens).ConfigureAwait(false);
            return SaveTo(root, root, tokens, values);
        }));
        try
        {
            return await lazy.Value.ConfigureAwait(false);
        }
        catch
        {
            // A failed build is not cached: the next caller tries again (or the caller falls back).
            Built.TryRemove(new KeyValuePair<string, Lazy<Task<GitTreeTemplate>>>(key, lazy));
            throw;
        }
    }

    /// <summary>Freezes the tree at <paramref name="directory"/> as a template: records its manifest (root as
    /// spelled in its files, tokens, values and file times) and returns it.</summary>
    public static GitTreeTemplate SaveTo(string directory, string writtenRoot, IReadOnlyDictionary<string, string> tokens,
        IReadOnlyDictionary<string, string> values)
    {
        foreach (var (name, value) in tokens)
            if (value.Length < 8) throw new ArgumentException($"token {name} must be at least 8 characters");
        var full = Path.GetFullPath(directory);
        if (HasReparsePoint(full)) throw new InvalidOperationException("a git template cannot hold links");
        var times = new Dictionary<string, (long, long)>(StringComparer.Ordinal);
        foreach (var path in System.IO.Directory.EnumerateFileSystemEntries(full, "*", SearchOption.AllDirectories))
        {
            var relative = Relative(full, path);
            if (relative == ManifestName) continue;
            times[relative] = (File.GetCreationTimeUtc(path).Ticks, File.GetLastWriteTimeUtc(path).Ticks);
        }
        var manifest = new Manifest(Path.GetFullPath(writtenRoot), new(tokens, StringComparer.Ordinal),
            new(values, StringComparer.Ordinal), times);
        File.WriteAllText(Path.Combine(full, ManifestName), JsonSerializer.Serialize(manifest, JsonOptions));
        return new GitTreeTemplate(full, manifest);
    }

    /// <summary>A template already on disk (a folder holding a manifest written by <see cref="SaveTo"/>).</summary>
    public static GitTreeTemplate FromDirectory(string directory)
    {
        var full = Path.GetFullPath(directory);
        var manifest = JsonSerializer.Deserialize<Manifest>(File.ReadAllText(Path.Combine(full, ManifestName)), JsonOptions)
            ?? throw new InvalidDataException("empty git template manifest");
        return new GitTreeTemplate(full, manifest);
    }

    /// <summary>A template saved with <see cref="SaveArchive"/>; extracted once per process (per archive path and
    /// content), with the recorded file times restored.</summary>
    public static GitTreeTemplate FromArchive(string archive)
    {
        var full = Path.GetFullPath(archive);
        var digest = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(full)))[..16];
        return Extracted.GetOrAdd(full + "|" + digest, _ => new Lazy<GitTreeTemplate>(() =>
        {
            var directory = Path.Combine(Path.GetTempPath(), "antiphon-gittpl-" + Guid.NewGuid().ToString("N"));
            AppDomain.CurrentDomain.ProcessExit += (_, _) => DeleteTree(directory);
            ZipFile.ExtractToDirectory(full, directory);
            var template = FromDirectory(directory);
            template.RestoreTimes(directory);
            return template;
        })).Value;
    }

    /// <summary>Writes this template, manifest included, to a zip that <see cref="FromArchive"/> reads.</summary>
    public void SaveArchive(string archive)
    {
        if (File.Exists(archive)) File.Delete(archive);
        ZipFile.CreateFromDirectory(Directory, archive, CompressionLevel.Optimal, includeBaseDirectory: false);
    }

    // ---- clone ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Clones the template into <paramref name="root"/> (which may exist, holding only empty directories), with
    /// each token replaced by the value in <paramref name="tokens"/> (a token left out keeps its value).
    /// </summary>
    public Clone CloneTo(string root, IReadOnlyDictionary<string, string>? tokens = null)
    {
        using var span = TestTrace.Span("git-template clone");
        var target = Path.GetFullPath(root);
        if (System.IO.Directory.Exists(target)
            && System.IO.Directory.EnumerateFiles(target, "*", SearchOption.AllDirectories).Any())
            throw new InvalidOperationException("git template clone target must be empty");
        var swaps = new List<(string Old, string New)>();
        foreach (var (name, value) in Tokens)
        {
            var replacement = tokens is not null && tokens.TryGetValue(name, out var v) ? v : value;
            if (replacement.Length != value.Length)
                throw new ArgumentException($"token {name} must keep its length ({value.Length})");
            if (replacement != value) swaps.Add((value, replacement));
        }
        var unknown = tokens?.Keys.Except(Tokens.Keys).ToList();
        if (unknown is { Count: > 0 }) throw new ArgumentException("unknown template tokens: " + string.Join(", ", unknown));

        var plan = GetPlan();
        var paths = PathSpellings(WrittenRoot, target);
        string To(string relative) => Path.Combine(target, SwapText(relative, swaps));

        // 1. Objects that hold a token, rewritten byte for byte (loose objects) before any file is written, so
        //    the id map can go into refs, logs and indexes in the same pass as everything else.
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        if (swaps.Count > 0 && plan.Affected is not null)
            using (TestTrace.Span("git-template objects"))
                foreach (var (id, bytes) in RewriteLoose(plan.Affected, swaps, map))
                    foreach (var home in plan.ObjectHomes[id])
                    {
                        var path = Path.Combine(To(home), "objects", map[id][..2], map[id][2..]);
                        System.IO.Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                        if (!File.Exists(path)) File.WriteAllBytes(path, bytes);
                    }

        // 2. Files: plain copies unless something in them changes.
        using (TestTrace.Span("git-template files"))
        {
            foreach (var dir in plan.Directories) System.IO.Directory.CreateDirectory(To(dir));
            System.IO.Directory.CreateDirectory(target);
            var hex = map.Select(kv => (kv.Key, kv.Value)).ToList();
            var raw = map.Select(kv => (Convert.FromHexString(kv.Key), Convert.FromHexString(kv.Value))).ToList();
            // Per-file latency (antivirus scanning of each new file) dominates, not bytes: copy in parallel.
            Parallel.ForEach(plan.Files, new ParallelOptions { MaxDegreeOfParallelism = 8 }, file =>
            {
                var from = Path.Combine(Directory, file.Relative);
                var to = To(file.Relative);
                switch (file.Kind)
                {
                    case FileKind.SampleHook:
                        return;
                    case FileKind.Object:
                        if (!File.Exists(to)) File.Copy(from, to);
                        return;
                    case FileKind.Index:
                        File.Copy(from, to);
                        if (swaps.Count > 0) RefuseTokenInIndexPaths(to, swaps);
                        if (raw.Count > 0) PatchIndex(to, raw);
                        return;
                    case FileKind.Metadata when file.HasOldText:
                    {
                        var bytes = SwapBytes(SwapBytes(SwapBytes(File.ReadAllBytes(from), swaps), paths), hex);
                        File.WriteAllBytes(to, bytes);
                        File.SetLastWriteTimeUtc(to, File.GetLastWriteTimeUtc(from));
                        return;
                    }
                    case FileKind.WorkTree when file.HasOldText:
                        File.WriteAllBytes(to, SwapBytes(File.ReadAllBytes(from), swaps));
                        RestoreTime(file.Relative, to);
                        return;
                    case FileKind.WorkTree:
                        // Creation time too: git for Windows compares it (as ctime) with the index stat data.
                        File.Copy(from, to);
                        RestoreTime(file.Relative, to);
                        return;
                    default:
                        File.Copy(from, to);
                        return;
                }
            });
        }

        // 3. Packed objects: rewritten with libgit2 in each repository of the clone, then the map applied.
        if (swaps.Count > 0 && plan.Affected is null)
            using (TestTrace.Span("git-template packed"))
            {
                foreach (var gitDir in plan.GitDirs)
                    foreach (var (from, to) in RewriteObjects(To(gitDir), swaps))
                    {
                        if (map.TryGetValue(from, out var known) && known != to)
                            throw new InvalidOperationException($"object {from} rewrote differently across repositories");
                        map[from] = to;
                    }
                var hex = map.Select(kv => (kv.Key, kv.Value)).ToList();
                var raw = map.Select(kv => (Convert.FromHexString(kv.Key), Convert.FromHexString(kv.Value))).ToList();
                foreach (var file in plan.Files.Where(f => f.Kind is FileKind.Metadata or FileKind.Index))
                {
                    var path = To(file.Relative);
                    if (file.Kind == FileKind.Index) { PatchIndex(path, raw); continue; }
                    var bytes = File.ReadAllBytes(path);
                    var replaced = SwapBytes(bytes, hex);
                    if (!ReferenceEquals(replaced, bytes)) WritePreservingTimes(path, replaced);
                }
            }

        if (!OperatingSystem.IsWindows()) RefreshIndexes(target);
        span?.SetTag("git.template.rewritten", map.Count);
        return new Clone(target, map);
    }

    // ---- plan: what the template holds, worked out once ---------------------------------------------------

    private enum FileKind { Object, Index, Metadata, WorkTree, SampleHook }
    private sealed record PlanFile(string Relative, FileKind Kind, bool HasOldText);
    private sealed record RawObject(string Id, string Type, byte[] Content);
    private sealed record Plan(List<string> GitDirs, List<string> Directories, List<PlanFile> Files,
        List<RawObject>? Affected, Dictionary<string, List<string>> ObjectHomes);

    private Plan? _plan;
    private readonly object _planGate = new();

    private Plan GetPlan()
    {
        lock (_planGate) return _plan ??= BuildPlan();
    }

    /// <summary>
    /// Classifies every file once (object, index, git metadata, work-tree file, sample hook; and whether it holds
    /// an old token, the written root or an id that changes), and finds the loose objects that hold a token,
    /// directly or through a tree or commit that refers to one, in dependency order. When any repository has a
    /// pack, <see cref="Plan.Affected"/> is null and clones rewrite objects with libgit2 instead.
    /// </summary>
    private Plan BuildPlan()
    {
        var gitDirs = GitDirectories(Directory);
        var relativeGitDirs = gitDirs.Select(d => Relative(Directory, d)).ToList();
        var oldTokens = Tokens.Values.Select(v => (v, v)).ToList();
        var oldRoot = PathSpellings(WrittenRoot, "\0").Select(p => (p.Old, p.Old)).ToList();

        List<RawObject>? affected = null;
        var homes = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var packed = gitDirs.Any(d => System.IO.Directory.Exists(Path.Combine(d, "objects", "pack"))
            && System.IO.Directory.EnumerateFiles(Path.Combine(d, "objects", "pack"), "*.pack").Any());
        if (!packed)
        {
            var objects = new Dictionary<string, RawObject>(StringComparer.Ordinal);
            for (var i = 0; i < gitDirs.Count; i++)
                foreach (var file in System.IO.Directory.EnumerateFiles(Path.Combine(gitDirs[i], "objects"), "*", SearchOption.AllDirectories))
                {
                    var dir = Path.GetFileName(Path.GetDirectoryName(file)!);
                    var name = Path.GetFileName(file);
                    if (dir.Length != 2 || name.Length != 38 || !(dir + name).All(char.IsAsciiHexDigit)) continue;
                    var id = (dir + name).ToLowerInvariant();
                    if (!homes.TryGetValue(id, out var list)) homes[id] = list = [];
                    list.Add(relativeGitDirs[i]);
                    if (!objects.ContainsKey(id)) objects[id] = ReadLoose(id, file);
                }
            affected = [];
            var state = new Dictionary<string, bool>(StringComparer.Ordinal);
            bool Visit(string id)
            {
                if (state.TryGetValue(id, out var known)) return known;
                state[id] = false; // cycles are impossible in git; this only guards a damaged store
                if (!objects.TryGetValue(id, out var obj)) return false; // not in any loose store: unchanged
                var hit = !ReferenceEquals(SwapBytes(obj.Content, oldTokens), obj.Content);
                if (obj.Type == "tree")
                    foreach (var (_, _, child) in ParseTree(obj.Content)) hit |= Visit(Convert.ToHexString(child).ToLowerInvariant());
                else if (obj.Type == "commit")
                    foreach (var reference in CommitReferences(obj.Content)) hit |= Visit(reference);
                else if (obj.Type == "tag") hit |= false;
                state[id] = hit;
                if (hit) affected.Add(obj);
                return hit;
            }
            foreach (var id in objects.Keys.Order(StringComparer.Ordinal)) Visit(id);
        }

        var oldIds = affected?.Select(o => (o.Id, o.Id)).ToList() ?? [];
        var directories = System.IO.Directory.EnumerateDirectories(Directory, "*", SearchOption.AllDirectories)
            .Select(d => Relative(Directory, d)).ToList();
        var files = new List<PlanFile>();
        foreach (var file in System.IO.Directory.EnumerateFiles(Directory, "*", SearchOption.AllDirectories))
        {
            var relative = Relative(Directory, file);
            if (relative == ManifestName) continue;
            var inGit = InGitDirectory(file, gitDirs);
            var name = Path.GetFileName(file);
            FileKind kind;
            if (inGit && IsObjectFile(file, gitDirs)) kind = FileKind.Object;
            else if (inGit && name == "index") kind = FileKind.Index;
            else if (inGit && name.EndsWith(".sample", StringComparison.Ordinal)
                     && Path.GetFileName(Path.GetDirectoryName(file)) == "hooks") kind = FileKind.SampleHook;
            else if (inGit || name == ".git") kind = FileKind.Metadata;
            else kind = FileKind.WorkTree;
            var hasOld = false;
            if (kind is FileKind.Metadata or FileKind.WorkTree)
            {
                var bytes = File.ReadAllBytes(file);
                hasOld = !ReferenceEquals(SwapBytes(bytes, oldTokens), bytes)
                    || kind == FileKind.Metadata && (!ReferenceEquals(SwapBytes(bytes, oldRoot), bytes)
                        || !ReferenceEquals(SwapBytes(bytes, oldIds), bytes));
            }
            files.Add(new PlanFile(relative, kind, hasOld));
        }
        return new Plan(relativeGitDirs, directories, files, affected, homes);
    }

    private static RawObject ReadLoose(string id, string file)
    {
        using var input = File.OpenRead(file);
        using var zlib = new ZLibStream(input, CompressionMode.Decompress);
        using var buffer = new MemoryStream();
        zlib.CopyTo(buffer);
        var all = buffer.ToArray();
        var nul = Array.IndexOf(all, (byte)0);
        var header = Encoding.ASCII.GetString(all, 0, nul);
        return new RawObject(id, header[..header.IndexOf(' ')], all[(nul + 1)..]);
    }

    /// <summary>Each affected object with its tokens replaced and its references mapped, in dependency order;
    /// returns the compressed loose bytes of every object whose id changed, and fills <paramref name="map"/>.</summary>
    private static List<(string OldId, byte[] Loose)> RewriteLoose(List<RawObject> affected,
        List<(string Old, string New)> swaps, Dictionary<string, string> map)
    {
        var written = new List<(string, byte[])>();
        foreach (var obj in affected)
        {
            var content = obj.Type switch
            {
                "tree" => RewriteTree(obj.Content, swaps, map),
                "commit" => RewriteCommit(obj.Content, swaps, map),
                _ => (byte[])SwapBytes(obj.Content, swaps).Clone(),
            };
            var header = Encoding.ASCII.GetBytes($"{obj.Type} {content.Length}\0");
            var full = new byte[header.Length + content.Length];
            header.CopyTo(full, 0);
            content.CopyTo(full, header.Length);
            var id = Convert.ToHexString(SHA1.HashData(full)).ToLowerInvariant();
            if (id == obj.Id) continue;
            map[obj.Id] = id;
            using var compressed = new MemoryStream();
            using (var zlib = new ZLibStream(compressed, CompressionLevel.Fastest, leaveOpen: true)) zlib.Write(full);
            written.Add((obj.Id, compressed.ToArray()));
        }
        return written;
    }

    private static byte[] RewriteTree(byte[] content, List<(string Old, string New)> swaps, Dictionary<string, string> map)
    {
        var entries = ParseTree(content).Select(e =>
        {
            var name = SwapBytes(e.Name, swaps);
            var hex = Convert.ToHexString(e.Id).ToLowerInvariant();
            var id = map.TryGetValue(hex, out var mapped) ? Convert.FromHexString(mapped) : e.Id;
            return (e.Mode, Name: name, Id: id);
        }).ToList();
        // git orders entries by name, a tree's name compared as if it ended in '/'.
        static byte[] Key((byte[] Mode, byte[] Name, byte[] Id) e) =>
            Encoding.ASCII.GetString(e.Mode) == "40000" ? [.. e.Name, (byte)'/'] : e.Name;
        entries.Sort((a, b) => Key(a).AsSpan().SequenceCompareTo(Key(b)));
        using var output = new MemoryStream(content.Length);
        foreach (var (mode, name, id) in entries)
        {
            output.Write(mode); output.WriteByte((byte)' '); output.Write(name); output.WriteByte(0); output.Write(id);
        }
        return output.ToArray();
    }

    private static byte[] RewriteCommit(byte[] content, List<(string Old, string New)> swaps, Dictionary<string, string> map)
    {
        var result = (byte[])SwapBytes(content, swaps).Clone();
        // Only the header's tree/parent lines carry ids; the message is left to the token swap above.
        var end = IndexOf(result, "\n\n"u8.ToArray(), 0);
        var header = Encoding.ASCII.GetString(result, 0, end < 0 ? result.Length : end);
        var offset = 0;
        foreach (var line in header.Split('\n'))
        {
            if ((line.StartsWith("tree ", StringComparison.Ordinal) || line.StartsWith("parent ", StringComparison.Ordinal))
                && map.TryGetValue(line[(line.IndexOf(' ') + 1)..], out var mapped))
                Encoding.ASCII.GetBytes(mapped).CopyTo(result, offset + line.IndexOf(' ') + 1);
            offset += line.Length + 1;
        }
        return result;
    }

    private static IEnumerable<(byte[] Mode, byte[] Name, byte[] Id)> ParseTree(byte[] content)
    {
        for (var at = 0; at < content.Length;)
        {
            var space = Array.IndexOf(content, (byte)' ', at);
            var nul = Array.IndexOf(content, (byte)0, space);
            yield return (content[at..space], content[(space + 1)..nul], content[(nul + 1)..(nul + 21)]);
            at = nul + 21;
        }
    }

    private static IEnumerable<string> CommitReferences(byte[] content)
    {
        var end = IndexOf(content, "\n\n"u8.ToArray(), 0);
        foreach (var line in Encoding.ASCII.GetString(content, 0, end < 0 ? content.Length : end).Split('\n'))
            if (line.StartsWith("tree ", StringComparison.Ordinal) || line.StartsWith("parent ", StringComparison.Ordinal))
                yield return line[(line.IndexOf(' ') + 1)..];
    }

    // ---- objects -------------------------------------------------------------------------------------------

    /// <summary>Rewrites, in the repository at <paramref name="gitDir"/>, every object reachable from a ref
    /// whose content (blob bytes, tree entry names, commit message or signatures) holds an old token.</summary>
    private static Dictionary<string, string> RewriteObjects(string gitDir, List<(string Old, string New)> swaps)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        using var repo = new Repository(gitDir);
        var odb = repo.ObjectDatabase;
        var trees = new Dictionary<ObjectId, Tree>();
        var blobs = new Dictionary<ObjectId, Blob>();
        var commits = new Dictionary<ObjectId, Commit>();

        Blob RewriteBlob(Blob blob)
        {
            if (blobs.TryGetValue(blob.Id, out var done)) return done;
            using var stream = blob.GetContentStream();
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            var bytes = buffer.ToArray();
            var replaced = SwapBytes(bytes, swaps);
            var result = ReferenceEquals(replaced, bytes) ? blob : odb.CreateBlob(new MemoryStream(replaced));
            return blobs[blob.Id] = result;
        }

        Tree RewriteTree(Tree tree)
        {
            if (trees.TryGetValue(tree.Id, out var done)) return done;
            var changed = false;
            var definition = new TreeDefinition();
            foreach (var entry in tree)
            {
                var name = SwapText(entry.Name, swaps);
                changed |= name != entry.Name;
                switch (entry.Target)
                {
                    case Blob b:
                        var nb = RewriteBlob(b);
                        changed |= nb.Id != b.Id;
                        definition.Add(name, nb, entry.Mode);
                        break;
                    case Tree t:
                        var nt = RewriteTree(t);
                        changed |= nt.Id != t.Id;
                        definition.Add(name, nt);
                        break;
                    default:
                        definition.Add(name, entry.Target.Id, entry.Mode); // gitlinks: kept as they are
                        break;
                }
            }
            var result = changed ? odb.CreateTree(definition) : tree;
            return trees[tree.Id] = result;
        }

        Signature Swap(Signature s) => new(SwapText(s.Name, swaps), SwapText(s.Email, swaps), s.When);

        Commit RewriteCommit(Commit commit)
        {
            if (commits.TryGetValue(commit.Id, out var done)) return done;
            var parents = commit.Parents.Select(RewriteCommit).ToList();
            var tree = RewriteTree(commit.Tree);
            var message = SwapText(commit.Message, swaps);
            var author = Swap(commit.Author);
            var committer = Swap(commit.Committer);
            var same = tree.Id == commit.Tree.Id && message == commit.Message
                && parents.Select(p => p.Id).SequenceEqual(commit.Parents.Select(p => p.Id))
                && author.Name == commit.Author.Name && author.Email == commit.Author.Email
                && committer.Name == commit.Committer.Name && committer.Email == commit.Committer.Email;
            var result = same ? commit : odb.CreateCommit(author, committer, message, tree, parents, prettifyMessage: false);
            return commits[commit.Id] = result;
        }

        foreach (var reference in repo.Refs)
        {
            var target = reference.ResolveToDirectReference()?.Target;
            if (target is Commit c) RewriteCommit(c);
            else if (target is TagAnnotation) throw new NotSupportedException("annotated tags in a git template");
        }
        foreach (var (from, to) in commits.Where(kv => kv.Key != kv.Value.Id)) map[from.Sha] = to.Sha;
        foreach (var (from, to) in trees.Where(kv => kv.Key != kv.Value.Id)) map[from.Sha] = to.Sha;
        foreach (var (from, to) in blobs.Where(kv => kv.Key != kv.Value.Id)) map[from.Sha] = to.Sha;
        return map;
    }

    /// <summary>Replaces raw object ids in a binary index (entries and the cache-tree extension) and rewrites
    /// its trailing SHA-1 checksum; the file's times are kept.</summary>
    private static void PatchIndex(string path, List<(byte[] Old, byte[] New)> ids)
    {
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length < 32 || Encoding.ASCII.GetString(bytes, 0, 4) != "DIRC" || ids.Count == 0) return;
        if (ids[0].Old.Length != 20) throw new NotSupportedException("only SHA-1 indexes are supported");
        var body = bytes.AsSpan(0, bytes.Length - 20).ToArray();
        var changed = false;
        foreach (var (old, @new) in ids)
            for (var at = IndexOf(body, old, 0); at >= 0; at = IndexOf(body, old, at + old.Length))
            {
                @new.CopyTo(body, at);
                changed = true;
            }
        if (!changed) return;
        var result = new byte[bytes.Length];
        body.CopyTo(result, 0);
        SHA1.HashData(body).CopyTo(result, body.Length);
        WritePreservingTimes(path, result);
    }

    private static void RefuseTokenInIndexPaths(string index, List<(string Old, string New)> swaps)
    {
        var bytes = File.ReadAllBytes(index);
        foreach (var (old, _) in swaps)
            if (IndexOf(bytes, Encoding.ASCII.GetBytes(old), 0) >= 0
                || IndexOf(bytes, Encoding.ASCII.GetBytes(old.ToUpperInvariant()), 0) >= 0)
                throw new NotSupportedException("a template token in an index path cannot be cloned (entry order)");
    }

    private static void RefreshIndexes(string root)
    {
        foreach (var gitFile in System.IO.Directory.EnumerateFileSystemEntries(root, ".git", SearchOption.AllDirectories))
        {
            var workTree = Path.GetDirectoryName(gitFile)!;
            var start = new ProcessStartInfo("git") { WorkingDirectory = workTree, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var a in new[] { "update-index", "-q", "--refresh" }) start.ArgumentList.Add(a);
            using var process = Process.Start(start);
            process?.WaitForExit();
        }
    }

    // ---- files ---------------------------------------------------------------------------------------------

    /// <summary>Repositories in the tree: a directory holding HEAD, objects/ and refs/ (a .git directory or a
    /// bare repository). Linked worktree admin directories share their repository's objects.</summary>
    private static List<string> GitDirectories(string root) =>
        System.IO.Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories).Prepend(root)
            .Where(d => File.Exists(Path.Combine(d, "HEAD")) && System.IO.Directory.Exists(Path.Combine(d, "objects"))
                && System.IO.Directory.Exists(Path.Combine(d, "refs")))
            .Select(Path.GetFullPath).ToList();

    private static bool InGitDirectory(string file, List<string> gitDirs) =>
        gitDirs.Any(d => file.StartsWith(d + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));

    private static bool IsObjectFile(string file, List<string> gitDirs) =>
        gitDirs.Any(d => file.StartsWith(Path.Combine(d, "objects") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));

    /// <summary>Every spelling git uses for a path: as given, forward slashes, and doubled backslashes (a config
    /// value), longest first so a doubled spelling is not half-replaced by the plain one.</summary>
    private static List<(string Old, string New)> PathSpellings(string from, string to)
    {
        var a = Path.TrimEndingDirectorySeparator(from);
        var b = Path.TrimEndingDirectorySeparator(to);
        var list = new List<(string, string)>
        {
            (a.Replace("\\", "\\\\"), b.Replace("\\", "\\\\")),
            (a, b),
            (a.Replace('\\', '/'), b.Replace('\\', '/')),
        };
        return list.Where(p => p.Item1 != p.Item2).Distinct().OrderByDescending(p => p.Item1.Length).ToList();
    }

    private static string SwapText(string text, List<(string Old, string New)> swaps)
    {
        foreach (var (old, @new) in swaps)
        {
            text = text.Replace(old, @new, StringComparison.Ordinal);
            if (old.Any(char.IsLetter)) text = text.Replace(old.ToUpperInvariant(), @new.ToUpperInvariant(), StringComparison.Ordinal);
        }
        return text;
    }

    /// <summary>Byte-level replacement (ASCII/UTF-8). Returns the same array when nothing matched. Path
    /// spellings match ignoring ASCII case on Windows, where git may spell a drive or folder either way.</summary>
    private static byte[] SwapBytes(byte[] bytes, List<(string Old, string New)> swaps)
    {
        var result = bytes;
        foreach (var (old, @new) in swaps)
        {
            var from = Encoding.UTF8.GetBytes(old);
            var to = Encoding.UTF8.GetBytes(@new);
            var ignoreCase = OperatingSystem.IsWindows() || old.All(c => char.IsAsciiHexDigit(c));
            var at = IndexOf(result, from, 0, ignoreCase);
            if (at < 0) continue;
            using var output = new MemoryStream(result.Length + Math.Max(0, to.Length - from.Length) * 4);
            var start = 0;
            while (at >= 0)
            {
                output.Write(result, start, at - start);
                // Keep the matched spelling's case for hex tokens (git writes either case of an id).
                var upper = from.Length > 0 && result[at..(at + from.Length)].Any(c => c is >= (byte)'A' and <= (byte)'F')
                    && old.All(char.IsAsciiHexDigit);
                output.Write(upper ? Encoding.UTF8.GetBytes(@new.ToUpperInvariant()) : to);
                start = at + from.Length;
                at = IndexOf(result, from, start, ignoreCase);
            }
            output.Write(result, start, result.Length - start);
            result = output.ToArray();
        }
        return result;
    }

    private static int IndexOf(byte[] haystack, byte[] needle, int start, bool ignoreCase = false)
    {
        if (needle.Length == 0) return -1;
        for (var i = start; i + needle.Length <= haystack.Length; i++)
        {
            var match = true;
            for (var j = 0; j < needle.Length && match; j++)
            {
                var a = haystack[i + j];
                var b = needle[j];
                match = a == b || ignoreCase && a < 128 && b < 128 && char.ToLowerInvariant((char)a) == char.ToLowerInvariant((char)b);
            }
            if (match) return i;
        }
        return -1;
    }

    private void RestoreTimes(string directory)
    {
        // Files first, then directories deepest first (writing a file touches its directory).
        foreach (var (relative, times) in _times.Where(t => File.Exists(Path.Combine(directory, t.Key))))
            SetTimes(Path.Combine(directory, relative), times, file: true);
        foreach (var (relative, times) in _times.Where(t => System.IO.Directory.Exists(Path.Combine(directory, t.Key)))
                     .OrderByDescending(t => t.Key.Length))
            SetTimes(Path.Combine(directory, relative), times, file: false);
    }

    private void RestoreTime(string templateRelative, string target)
    {
        if (_times.TryGetValue(templateRelative, out var times)) SetTimes(target, times, file: true);
        else
        {
            var source = Path.Combine(Directory, templateRelative);
            File.SetCreationTimeUtc(target, File.GetCreationTimeUtc(source));
            File.SetLastWriteTimeUtc(target, File.GetLastWriteTimeUtc(source));
        }
    }

    private static void SetTimes(string path, (long Created, long Written) times, bool file)
    {
        var created = new DateTime(times.Created, DateTimeKind.Utc);
        var written = new DateTime(times.Written, DateTimeKind.Utc);
        if (file) { File.SetCreationTimeUtc(path, created); File.SetLastWriteTimeUtc(path, written); }
        else { System.IO.Directory.SetCreationTimeUtc(path, created); System.IO.Directory.SetLastWriteTimeUtc(path, written); }
    }

    private static void WritePreservingTimes(string path, byte[] bytes)
    {
        var created = File.GetCreationTimeUtc(path);
        var written = File.GetLastWriteTimeUtc(path);
        var attributes = File.GetAttributes(path);
        File.SetAttributes(path, FileAttributes.Normal);
        File.WriteAllBytes(path, bytes);
        File.SetCreationTimeUtc(path, created);
        File.SetLastWriteTimeUtc(path, written);
        File.SetAttributes(path, attributes);
    }

    private static string Relative(string root, string path) => Path.GetRelativePath(root, path);

    private static bool HasReparsePoint(string root) =>
        new DirectoryInfo(root).EnumerateFileSystemInfos("*", SearchOption.AllDirectories)
            .Any(i => (i.Attributes & FileAttributes.ReparsePoint) != 0);

    /// <summary>A killed test process cannot delete its templates at exit; one older than a day is stale (a live
    /// process builds its own and never outlives that).</summary>
    private static void SweepStale()
    {
        try
        {
            foreach (var dir in System.IO.Directory.EnumerateDirectories(Path.GetTempPath(), "antiphon-gittpl-*"))
                if (System.IO.Directory.GetCreationTimeUtc(dir) < DateTime.UtcNow.AddDays(-1)) DeleteTree(dir);
        }
        catch (Exception) { /* best effort */ }
    }

    internal static void DeleteTree(string root)
    {
        try
        {
            if (!System.IO.Directory.Exists(root)) return;
            foreach (var file in System.IO.Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);
            System.IO.Directory.Delete(root, true);
        }
        catch (Exception) { /* best effort at process exit */ }
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { IncludeFields = true, WriteIndented = false };
}
