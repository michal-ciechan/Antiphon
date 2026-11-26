// N0 (unit 57): the content identity of antiphon-scripts' build inputs (CliInputs) and the manifest that records it.
//
// Identity: SHA-256 (64 lower-case hex) over the format line, the operating system, the build recipe
// (CliPublisher.Recipe; unit 59: or another product's CliProduct.Recipe) and, in ordinal name order, each input's name
// and the SHA-256 of its bytes. A published build's folder is named by its first 24 characters and holds the manifest
// (cli-identity): the identity and, per input, the hash, size and last write time the hash was taken at.
//
// Check (Matches): the same format, system and recipe, the same names, and per input the recorded size and time (the fast
// path); where either differs, the bytes are hashed and compared (the content fallback). A time recorded within RacyWindow
// of the hashing (or after it) is stored as 0 and always hashed: a second write in the same clock tick would keep it. ASCII.
#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Antiphon.CliLauncher
{
    /// <summary>One input as a manifest records it.</summary>
    public sealed class CliManifestEntry
    {
        /// <summary>A recorded input.</summary>
        public CliManifestEntry(string hash, long size, long ticks)
        {
            Hash = hash;
            Size = size;
            Ticks = ticks;
        }

        /// <summary>The SHA-256 of the bytes, 64 lower-case hex.</summary>
        public string Hash { get; }

        /// <summary>The size in bytes when hashed.</summary>
        public long Size { get; }

        /// <summary>The last write time when hashed (UTC ticks); 0 when it was too close to the hashing to be trusted.</summary>
        public long Ticks { get; }
    }

    /// <summary>The identity of a set of inputs and what each input was when hashed: a published build's cli-identity, or the build record.</summary>
    public sealed class CliManifest
    {
        /// <summary>The first line of a manifest.</summary>
        public const string Format = "antiphon-cli-identity-v1";

        /// <summary>The identity, 64 lower-case hex.</summary>
        public string Identity { get; init; } = "";

        /// <summary>The operating system the identity was taken on (CliIdentity.Os).</summary>
        public string Os { get; init; } = "";

        /// <summary>The build recipe (CliPublisher.Recipe).</summary>
        public string Recipe { get; init; } = "";

        /// <summary>When the hashes were taken (UTC ticks).</summary>
        public long Hashed { get; init; }

        /// <summary>The build record only: when the build into the shared folder ended (UTC ticks); 0 otherwise.</summary>
        public long BuildEnd { get; init; }

        /// <summary>The inputs by name (ordinal).</summary>
        public IReadOnlyDictionary<string, CliManifestEntry> Files { get; init; } = new SortedDictionary<string, CliManifestEntry>(StringComparer.Ordinal);

        /// <summary>The folder name of the build this identity names: its first 24 characters.</summary>
        public string Name => CliIdentity.Name(Identity);

        /// <summary>A copy with another build end.</summary>
        public CliManifest WithBuildEnd(long ticks) => new CliManifest
        {
            Identity = Identity, Os = Os, Recipe = Recipe, Hashed = Hashed, BuildEnd = ticks, Files = Files,
        };

        /// <summary>The manifest at a path; null when it is missing or not a whole manifest.</summary>
        public static CliManifest Read(string path)
        {
            string[] lines;
            try { lines = File.ReadAllLines(path); }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { return null; }
            if (lines.Length < 7 || lines[0] != Format) return null;
            string Field(int index, string key) =>
                lines[index].StartsWith(key + " ", StringComparison.Ordinal) ? lines[index].Substring(key.Length + 1) : null;
            var identity = Field(1, "identity");
            var os = Field(2, "os");
            var recipe = Field(3, "recipe");
            if (identity == null || identity.Length != 64 || os == null || recipe == null) return null;
            if (!long.TryParse(Field(4, "hashed"), NumberStyles.None, CultureInfo.InvariantCulture, out var hashed)) return null;
            if (!long.TryParse(Field(5, "build-end"), NumberStyles.None, CultureInfo.InvariantCulture, out var buildEnd)) return null;
            if (!int.TryParse(Field(6, "files"), NumberStyles.None, CultureInfo.InvariantCulture, out var count) || lines.Length != 7 + count) return null;
            var files = new SortedDictionary<string, CliManifestEntry>(StringComparer.Ordinal);
            for (var i = 7; i < lines.Length; i++)
            {
                var parts = lines[i].Split(' ', 4);
                if (parts.Length != 4 || parts[0].Length != 64) return null;
                if (!long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var size)) return null;
                if (!long.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)) return null;
                if (files.ContainsKey(parts[3])) return null;
                files[parts[3]] = new CliManifestEntry(parts[0], size, ticks);
            }
            return new CliManifest { Identity = identity, Os = os, Recipe = recipe, Hashed = hashed, BuildEnd = buildEnd, Files = files };
        }

        /// <summary>Writes the manifest (ASCII lines, '\n').</summary>
        public void Write(string path)
        {
            var text = new StringBuilder();
            text.Append(Format).Append('\n');
            text.Append("identity ").Append(Identity).Append('\n');
            text.Append("os ").Append(Os).Append('\n');
            text.Append("recipe ").Append(Recipe).Append('\n');
            text.Append("hashed ").Append(Hashed.ToString(CultureInfo.InvariantCulture)).Append('\n');
            text.Append("build-end ").Append(BuildEnd.ToString(CultureInfo.InvariantCulture)).Append('\n');
            text.Append("files ").Append(Files.Count.ToString(CultureInfo.InvariantCulture)).Append('\n');
            foreach (var pair in Files)
                text.Append(pair.Value.Hash).Append(' ').Append(pair.Value.Size.ToString(CultureInfo.InvariantCulture)).Append(' ')
                    .Append(pair.Value.Ticks.ToString(CultureInfo.InvariantCulture)).Append(' ').Append(pair.Key).Append('\n');
            File.WriteAllText(path, text.ToString(), new UTF8Encoding(false));
        }
    }

    /// <summary>The content identity of the CLI's inputs.</summary>
    public static class CliIdentity
    {
        /// <summary>A last write time this close to the hashing (or later) is not trusted by the fast path.</summary>
        public static readonly TimeSpan RacyWindow = TimeSpan.FromSeconds(3);

        /// <summary>The operating system part of the identity.</summary>
        public static string Os =>
            OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsLinux() ? "linux" : OperatingSystem.IsMacOS() ? "macos" : "other";

        /// <summary>The folder name of an identity: its first 24 characters.</summary>
        public static string Name(string identity) => identity == null || identity.Length < 24 ? "" : identity.Substring(0, 24);

        /// <summary>
        /// The manifest of the inputs as they are now. An input whose size and time match a trusted entry of known keeps that
        /// entry's hash; every other input is hashed. nowUtc is taken before any hashing.
        /// </summary>
        public static CliManifest Compute(IReadOnlyList<CliInput> inputs, CliManifest known, DateTime nowUtc) =>
            Compute(inputs, known, nowUtc, CliPublisher.Recipe);

        /// <summary>Compute for a product's recipe (CliProduct.Recipe).</summary>
        public static CliManifest Compute(IReadOnlyList<CliInput> inputs, CliManifest known, DateTime nowUtc, string recipe)
        {
            if (inputs == null) throw new ArgumentNullException(nameof(inputs));
            if (recipe == null) throw new ArgumentNullException(nameof(recipe));
            var trusted = known != null && known.Os == Os && known.Recipe == recipe ? known.Files : null;
            var files = new SortedDictionary<string, CliManifestEntry>(StringComparer.Ordinal);
            var limit = (nowUtc - RacyWindow).Ticks;
            foreach (var input in inputs)
            {
                string hash = null;
                if (trusted != null && trusted.TryGetValue(input.Name, out var entry) && entry.Ticks != 0 && entry.Ticks == input.Ticks && entry.Size == input.Size)
                    hash = entry.Hash;
                hash ??= HashFile(input.FullPath);
                files[input.Name] = new CliManifestEntry(hash, input.Size, input.Ticks < limit ? input.Ticks : 0);
            }
            return new CliManifest { Identity = Of(files, recipe), Os = Os, Recipe = recipe, Hashed = nowUtc.Ticks, Files = files };
        }

        /// <summary>
        /// Whether the inputs are the ones the stamp records: the same format, system and recipe and the same names, each with
        /// the recorded size and time, or else the recorded bytes.
        /// </summary>
        public static bool Matches(IReadOnlyList<CliInput> inputs, CliManifest stamp) => Matches(inputs, stamp, CliPublisher.Recipe);

        /// <summary>Matches for a product's recipe (CliProduct.Recipe).</summary>
        public static bool Matches(IReadOnlyList<CliInput> inputs, CliManifest stamp, string recipe)
        {
            if (inputs == null) throw new ArgumentNullException(nameof(inputs));
            if (stamp == null || stamp.Os != Os || stamp.Recipe != recipe || stamp.Files.Count != inputs.Count) return false;
            foreach (var input in inputs)
            {
                if (!stamp.Files.TryGetValue(input.Name, out var entry) || entry.Size != input.Size) return false;
                if (entry.Ticks != 0 && entry.Ticks == input.Ticks) continue;
                string hash;
                // An input that went away since the listing (an editor's save) is a change, not a failure.
                try { hash = HashFile(input.FullPath); }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { return false; }
                if (!string.Equals(hash, entry.Hash, StringComparison.Ordinal)) return false;
            }
            return true;
        }

        /// <summary>The identity of name and hash pairs (ordinal name order).</summary>
        public static string Of(IReadOnlyDictionary<string, CliManifestEntry> files) => Of(files, CliPublisher.Recipe);

        /// <summary>Of for a product's recipe (CliProduct.Recipe).</summary>
        public static string Of(IReadOnlyDictionary<string, CliManifestEntry> files, string recipe)
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            hash.AppendData(Encoding.UTF8.GetBytes(CliManifest.Format + "\nos " + Os + "\nrecipe " + recipe + "\n"));
            foreach (var pair in files.OrderBy(p => p.Key, StringComparer.Ordinal))
                hash.AppendData(Encoding.UTF8.GetBytes(pair.Key + "\n" + pair.Value.Hash + "\n"));
            return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
        }

        /// <summary>The SHA-256 of a file's bytes, 64 lower-case hex.</summary>
        public static string HashFile(string path)
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1, FileOptions.SequentialScan);
            return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        }
    }
}
