// CARD-0479: the checks a mechanical positive-control worker runs (docs/mechanical-pc-contract.md, contract version 1).
// They were the five fenced PowerShell snippets of that contract, which scripts/test-mechanical-pc-contract.ps1 extracted
// and rehearsed; unit 33 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md moved them here and deleted the
// driver. The CLI runs them as
//
//   antiphon-scripts mechanical-pc preflight --snapshot-root <dir> --pack-root <dir> --l <sha>
//   antiphon-scripts mechanical-pc apply --snapshot-root <dir> --pack-root <dir>
//   antiphon-scripts mechanical-pc oracle --pack-root <dir>
//   antiphon-scripts mechanical-pc restore --snapshot-root <dir> --evidence-root <dir> --pack-root <dir> [--phase restore|restored]
//   antiphon-scripts mechanical-pc instantiate --snapshot-root <dir> --evidence-root <dir> --template-path <file>
//       [--task-id <id>] [--run-id <id>] [--pc-id <id>] [--phase <name>] [--results-relative <path>]
//
// (scripts/antiphon mechanical-pc on sh, scripts\antiphon.cmd mechanical-pc on Windows). Each verb prints "C479 ACCEPT" and
// exits 0, or prints one "C479 REFUSE <reason>" line and exits 2 (restore's unexplained edit: "C479 REFUSE restore-mismatch
// path=<path>", the bytes left in place). A refusal leaves no partial mutant: apply writes the saved preimage bytes back
// first. The pack formats are the contract's (index.json with index.sha256 beside it, apply.json, oracle.json, restore.json;
// JSON names matched ignoring case, as ConvertFrom-Json did). Hashes are lower-case SHA-256 of the bytes, compared ignoring
// case; pack paths use '/' and are joined under the snapshot root. Exit 1 is an input the verb could not read (a missing or
// malformed pack file, template or backup; one host.Error line), which the snippets answered by throwing. Usage errors are
// NightlyUsageException (the CLI's exit 2). Files are IFileSystem's (text written as UTF-8 without a byte-order mark, as
// [IO.File]::WriteAllText did), git is the host's seam, the lines host.Out's. Plain C#: no PowerShell types. ASCII only.
#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Antiphon.Nightly
{
    /// <summary>The mechanical-pc command's verb and options (the snippets' parameters).</summary>
    public sealed class MechanicalPcOptions
    {
        /// <summary>The CLI's usage line.</summary>
        public const string Usage = "mechanical-pc preflight --snapshot-root <dir> --pack-root <dir> --l <sha>"
            + " | mechanical-pc apply --snapshot-root <dir> --pack-root <dir> | mechanical-pc oracle --pack-root <dir>"
            + " | mechanical-pc restore --snapshot-root <dir> --evidence-root <dir> --pack-root <dir> [--phase restore|restored]"
            + " | mechanical-pc instantiate --snapshot-root <dir> --evidence-root <dir> --template-path <file> [--task-id <id>] [--run-id <id>]"
            + " [--pc-id <id>] [--phase <name>] [--results-relative <path>]";

        /// <summary>The verbs, one per contract snippet.</summary>
        public static readonly IReadOnlyList<string> Verbs = new[] { "preflight", "apply", "oracle", "restore", "instantiate" };

        /// <summary>The verb.</summary>
        public string Verb { get; init; } = "";

        /// <summary>The snapshot checkout at L.</summary>
        public string SnapshotRoot { get; init; } = "";

        /// <summary>The bound pack's directory.</summary>
        public string PackRoot { get; init; } = "";

        /// <summary>The verified source SHA L (preflight).</summary>
        public string L { get; init; } = "";

        /// <summary>The task-owned evidence root (restore, instantiate).</summary>
        public string EvidenceRoot { get; init; } = "";

        /// <summary>The phase: restore's restore or restored, instantiate's attempt phase.</summary>
        public string Phase { get; init; } = "";

        /// <summary>The manifest template (instantiate).</summary>
        public string TemplatePath { get; init; } = "";

        /// <summary>The task id token (instantiate).</summary>
        public string TaskId { get; init; } = "";

        /// <summary>The run id token (instantiate).</summary>
        public string RunId { get; init; } = "";

        /// <summary>The PC id token (instantiate).</summary>
        public string PcId { get; init; } = "";

        /// <summary>The results path relative to the attempt (instantiate).</summary>
        public string ResultsRelative { get; init; } = "";

        /// <summary>The verb, then its flags; each verb takes its own snippet's parameters.</summary>
        public static MechanicalPcOptions Parse(IReadOnlyList<string> arguments)
        {
            if (arguments == null || arguments.Count == 0) throw new NightlyUsageException("missing verb (preflight, apply, oracle, restore or instantiate)");
            var verb = Verbs.FirstOrDefault(v => v == arguments[0]) ?? throw new NightlyUsageException("unknown verb '" + arguments[0] + "'");
            var rest = arguments.Skip(1).ToArray();
            switch (verb)
            {
                case "preflight":
                {
                    var flags = NightlyArgs.Parse(rest, new[] { "--snapshot-root", "--pack-root", "--l" }, Array.Empty<string>());
                    return new MechanicalPcOptions { Verb = verb, SnapshotRoot = flags.Required("--snapshot-root"), PackRoot = flags.Required("--pack-root"), L = flags.Required("--l") };
                }
                case "apply":
                {
                    var flags = NightlyArgs.Parse(rest, new[] { "--snapshot-root", "--pack-root" }, Array.Empty<string>());
                    return new MechanicalPcOptions { Verb = verb, SnapshotRoot = flags.Required("--snapshot-root"), PackRoot = flags.Required("--pack-root") };
                }
                case "oracle":
                {
                    var flags = NightlyArgs.Parse(rest, new[] { "--pack-root" }, Array.Empty<string>());
                    return new MechanicalPcOptions { Verb = verb, PackRoot = flags.Required("--pack-root") };
                }
                case "restore":
                {
                    var flags = NightlyArgs.Parse(rest, new[] { "--snapshot-root", "--evidence-root", "--pack-root", "--phase" }, Array.Empty<string>());
                    return new MechanicalPcOptions
                    {
                        Verb = verb, SnapshotRoot = flags.Required("--snapshot-root"), EvidenceRoot = flags.Required("--evidence-root"),
                        PackRoot = flags.Required("--pack-root"), Phase = flags.Value("--phase", "") is { Length: > 0 } phase ? phase : "restore",
                    };
                }
                default:
                {
                    var flags = NightlyArgs.Parse(rest, new[] { "--snapshot-root", "--evidence-root", "--template-path", "--task-id", "--run-id", "--pc-id", "--phase", "--results-relative" },
                        Array.Empty<string>());
                    return new MechanicalPcOptions
                    {
                        Verb = verb, SnapshotRoot = flags.Required("--snapshot-root"), EvidenceRoot = flags.Required("--evidence-root"),
                        TemplatePath = flags.Required("--template-path"), TaskId = flags.Value("--task-id", ""), RunId = flags.Value("--run-id", ""),
                        PcId = flags.Value("--pc-id", ""), Phase = flags.Value("--phase", ""), ResultsRelative = flags.Value("--results-relative", ""),
                    };
                }
            }
        }
    }

    /// <summary>The mechanical positive-control checks (docs/mechanical-pc-contract.md's snippets).</summary>
    public static class MechanicalPc
    {
        /// <summary>The exit of an accepted check.</summary>
        public const int Accepted = 0;

        /// <summary>The exit of an input the verb could not read.</summary>
        public const int Failed = 1;

        /// <summary>The exit of a refusal.</summary>
        public const int Refused = 2;

        /// <summary>The tokens instantiation may substitute, in the order they are replaced.</summary>
        public static readonly IReadOnlyList<string> Tokens = new[] { "snapshotRoot", "evidenceRoot", "taskId", "runId", "pcId", "phase" };

        private static readonly Regex Token = new Regex(@"\{([A-Za-z0-9]+)\}", RegexOptions.CultureInvariant);

        private static readonly JsonNodeOptions IgnoreCase = new JsonNodeOptions { PropertyNameCaseInsensitive = true };

        private sealed class Refusal : Exception
        {
            public Refusal(string line) : base(line) { }
        }

        /// <summary>Runs the verb; see the header for the lines and exit codes.</summary>
        public static int Run(MechanicalPcOptions options, NightlyHost host, IFileSystem files)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (host == null) throw new ArgumentNullException(nameof(host));
            if (files == null) throw new ArgumentNullException(nameof(files));
            try
            {
                switch (options.Verb)
                {
                    case "preflight": Preflight(options, host, files); break;
                    case "apply": Apply(options, files); break;
                    case "oracle": Oracle(options, files); break;
                    case "restore": Restore(options, files); break;
                    case "instantiate": Instantiate(options, files); break;
                    default: throw new ArgumentException("unknown verb " + options.Verb, nameof(options));
                }
                host.Out.WriteLine("C479 ACCEPT");
                return Accepted;
            }
            catch (Refusal refusal)
            {
                host.Out.WriteLine(refusal.Message);
                return Refused;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is JsonException || e is InvalidOperationException)
            {
                host.Error.WriteLine("mechanical-pc " + options.Verb + " failed: " + e.Message);
                return Failed;
            }
        }

        private static Refusal Refuse(string reason) => new Refusal("C479 REFUSE " + reason);

        // Join-Path <root> <relative>: the pack's '/' become this platform's separator.
        private static string Full(string root, string relative)
        {
            var rel = (relative ?? "").Replace('/', Path.DirectorySeparatorChar).TrimStart('\\', '/');
            return root.TrimEnd('\\', '/') + Path.DirectorySeparatorChar + rel;
        }

        /// <summary>The lower-case SHA-256 of a file's bytes; null when there is no such file.</summary>
        public static string Sha256(IFileSystem files, string path)
        {
            if (!files.File.Exists(path)) return null;
            return Convert.ToHexString(SHA256.HashData(files.File.ReadAllBytes(path))).ToLowerInvariant();
        }

        private static bool SameHash(string actual, string expected) =>
            actual != null && string.Equals(actual, (expected ?? "").Trim(), StringComparison.OrdinalIgnoreCase);

        private static JsonObject ReadJson(IFileSystem files, string path)
        {
            if (!files.File.Exists(path)) throw new FileNotFoundException("missing " + path, path);
            return JsonNode.Parse(files.File.ReadAllText(path), IgnoreCase) as JsonObject ?? throw new InvalidOperationException(path + " is not a JSON object");
        }

        private static JsonNode Field(JsonObject o, string name) => o != null && o.TryGetPropertyValue(name, out var value) ? value : null;

        private static string Text(JsonNode node)
        {
            if (node is JsonValue value)
            {
                if (value.TryGetValue<string>(out var s)) return s;
                if (value.TryGetValue<bool>(out var b)) return b ? "True" : "False";
                return value.ToJsonString();
            }
            return node == null ? "" : node.ToJsonString();
        }

        // [int]$x: a number (rounded half to even) or a numeric string; 0 when absent.
        private static int Int(JsonNode node)
        {
            if (node is JsonValue value)
            {
                if (value.TryGetValue<double>(out var d)) return Convert.ToInt32(d, CultureInfo.InvariantCulture);
                if (value.TryGetValue<string>(out var s) && int.TryParse(s.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var n)) return n;
            }
            return 0;
        }

        // [bool]$x: true, a non-empty string or a non-zero number.
        private static bool Bool(JsonNode node)
        {
            if (node is not JsonValue value) return node != null;
            if (value.TryGetValue<bool>(out var b)) return b;
            if (value.TryGetValue<string>(out var s)) return s.Length > 0;
            if (value.TryGetValue<double>(out var d)) return d != 0;
            return false;
        }

        private static IEnumerable<JsonNode> Items(JsonNode node) =>
            node is JsonArray array ? array : node == null ? Enumerable.Empty<JsonNode>() : new[] { node };

        private static IEnumerable<JsonObject> Objects(JsonNode node) => Items(node).OfType<JsonObject>();

        // Compare-Object of two string lists: the same members with the same counts, ignoring case.
        private static bool SameSet(IReadOnlyCollection<string> left, IReadOnlyCollection<string> right) =>
            left.Count == right.Count
            && left.OrderBy(s => s, StringComparer.OrdinalIgnoreCase).SequenceEqual(right.OrderBy(s => s, StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);

        // preflight: HEAD is L, the detached digest is the index's bytes, every member's bytes are the index's.
        private static void Preflight(MechanicalPcOptions o, NightlyHost host, IFileSystem files)
        {
            var git = host.RunGit(o.SnapshotRoot, "rev-parse", "HEAD");
            var head = git.ExitCode == 0
                ? git.Output.Replace("\r", "").Split('\n').Select(line => line.Trim()).LastOrDefault(line => line.Length > 0) ?? ""
                : "";
            if (!string.Equals(head, (o.L ?? "").Trim(), StringComparison.OrdinalIgnoreCase)) throw Refuse("source-mismatch");
            var digestPath = Path.Combine(o.PackRoot, "index.sha256");
            if (!files.File.Exists(digestPath)) throw Refuse("binding-missing");
            var indexPath = Path.Combine(o.PackRoot, "index.json");
            if (!SameHash(Sha256(files, indexPath) ?? throw new FileNotFoundException("missing " + indexPath, indexPath), files.File.ReadAllText(digestPath)))
                throw Refuse("artifact-mismatch");
            var index = ReadJson(files, indexPath);
            foreach (var member in Objects(Field(index, "members")))
                if (!SameHash(Sha256(files, Full(o.SnapshotRoot, Text(Field(member, "path")))), Text(Field(member, "sha256")))) throw Refuse("artifact-mismatch");
        }

        // apply: the closure's bytes, then each edit's preimage, context count and predicted mutant, then the changed-file set.
        // Any refusal after a write puts every saved preimage back first.
        private static void Apply(MechanicalPcOptions o, IFileSystem files)
        {
            var spec = ReadJson(files, Path.Combine(o.PackRoot, "apply.json"));
            foreach (var item in Objects(Field(spec, "closure")))
                if (!SameHash(Sha256(files, Full(o.SnapshotRoot, Text(Field(item, "path")))), Text(Field(item, "sha256")))) throw Refuse("artifact-mismatch");
            var saved = new List<KeyValuePair<string, byte[]>>();
            Refusal Undo(string reason)
            {
                foreach (var pair in saved) files.File.WriteAllBytes(pair.Key, pair.Value);
                return Refuse(reason);
            }
            foreach (var edit in Objects(Field(spec, "edits")))
            {
                var full = Full(o.SnapshotRoot, Text(Field(edit, "path")));
                if (!SameHash(Sha256(files, full), Text(Field(edit, "preimageSha256")))) throw Undo("patch-mismatch");
                var context = Text(Field(edit, "context"));
                if (context.Length == 0) throw Undo("patch-unresolved");
                var text = files.File.ReadAllText(full);
                if (Regex.Matches(text, Regex.Escape(context)).Count != Int(Field(edit, "expectedMatches"))) throw Undo("patch-mismatch");
                // The first preimage of a path is the one restored.
                if (!saved.Any(pair => string.Equals(pair.Key, full, StringComparison.OrdinalIgnoreCase)))
                    saved.Add(new KeyValuePair<string, byte[]>(full, files.File.ReadAllBytes(full)));
                files.File.WriteAllText(full, text.Replace(context, Text(Field(edit, "replacement")), StringComparison.Ordinal));
                if (!SameHash(Sha256(files, full), Text(Field(edit, "mutantSha256")))) throw Undo("patch-mismatch");
            }
            var allowed = Items(Field(spec, "allowedFiles")).Select(node => Text(node).Replace('\\', '/')).ToList();
            var root = o.SnapshotRoot.TrimEnd('\\', '/');
            var changed = saved.Select(pair => pair.Key.Substring(root.Length).TrimStart('\\', '/').Replace('\\', '/')).ToList();
            if (!SameSet(changed, allowed)) throw Undo("patch-mismatch");
        }

        // oracle: fresh, attributable output, then the exact executed roster, then the exact failure set.
        private static void Oracle(MechanicalPcOptions o, IFileSystem files)
        {
            var path = Path.Combine(o.PackRoot, "oracle.json");
            var obs = ReadJson(files, path);
            if (Bool(Field(obs, "resultsPreexisted"))) throw Refuse("stale-output");
            if (string.Equals(Text(Field(obs, "buildSource")), "unknown", StringComparison.OrdinalIgnoreCase)) throw Refuse("stale-output");
            if (string.Equals(Text(Field(obs, "restoredOutputHash")), Text(Field(obs, "mutantOutputHash")), StringComparison.OrdinalIgnoreCase)) throw Refuse("stale-output");
            var driverExit = Int(Field(obs, "driverExit"));
            if (driverExit == 2) throw Refuse("prerequisite-missing");
            if (driverExit == 3) throw Refuse("selector-unresolved");
            if (Int(Field(obs, "skipped")) > 0) throw Refuse("selector-drift");
            var executed = Items(Field(obs, "executed")).Select(Text).ToList();
            if (executed.Count == 0) throw Refuse("selector-drift");
            if (executed.Distinct(StringComparer.Ordinal).Count() != executed.Count) throw Refuse("selector-drift");
            if (!SameSet(executed, Items(Field(obs, "expectedExecuted")).Select(Text).ToList())) throw Refuse("selector-drift");
            if (!SameSet(Items(Field(obs, "failed")).Select(Text).ToList(), Items(Field(obs, "expectedFailed")).Select(Text).ToList())) throw Refuse("unexpected-red");
        }

        // restore: only a path whose bytes are the recorded mutant is replaced from the backup, and must then be the preimage;
        // a restored phase also needs the forced-rebuild marker.
        private static void Restore(MechanicalPcOptions o, IFileSystem files)
        {
            var spec = ReadJson(files, Path.Combine(o.PackRoot, "restore.json"));
            foreach (var file in Objects(Field(spec, "files")))
            {
                var relative = Text(Field(file, "path"));
                var full = Full(o.SnapshotRoot, relative);
                if (!SameHash(Sha256(files, full), Text(Field(file, "mutantSha256")))) throw new Refusal("C479 REFUSE restore-mismatch path=" + relative);
                var backup = Full(Path.Combine(o.EvidenceRoot, "backup"), relative);
                if (!files.File.Exists(backup)) throw new FileNotFoundException("missing backup " + backup, backup);
                files.File.Copy(backup, full, true);
                if (!SameHash(Sha256(files, full), Text(Field(file, "preimageSha256")))) throw Refuse("restore-mismatch");
            }
            if (string.Equals(o.Phase, "restored", StringComparison.OrdinalIgnoreCase) && !files.File.Exists(Path.Combine(o.EvidenceRoot, "forced-rebuild.marker")))
                throw Refuse("stale-output");
        }

        // instantiate: only the six tokens, each with a value, no '..', then a new attempt directory and its resolved manifest.
        private static void Instantiate(MechanicalPcOptions o, IFileSystem files)
        {
            if (string.IsNullOrWhiteSpace(o.ResultsRelative)) throw Refuse("empty-results");
            var values = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["snapshotRoot"] = o.SnapshotRoot, ["evidenceRoot"] = o.EvidenceRoot, ["taskId"] = o.TaskId,
                ["runId"] = o.RunId, ["pcId"] = o.PcId, ["phase"] = o.Phase,
            };
            if (!files.File.Exists(o.TemplatePath)) throw new FileNotFoundException("missing template " + o.TemplatePath, o.TemplatePath);
            var text = files.File.ReadAllText(o.TemplatePath);
            foreach (Match token in Token.Matches(text))
            {
                if (!values.TryGetValue(token.Groups[1].Value, out var value)) throw Refuse("unknown-token");
                if (string.IsNullOrWhiteSpace(value)) throw Refuse("missing-value");
            }
            foreach (var name in Tokens) text = text.Replace("{" + name + "}", values[name] ?? "", StringComparison.Ordinal);
            if (Token.IsMatch(text)) throw Refuse("unknown-token");
            if (text.Contains("..", StringComparison.Ordinal) || o.ResultsRelative.Contains("..", StringComparison.Ordinal)) throw Refuse("path-not-contained");
            // The attempt directory is <evidence>/<pc>/<phase>/<run>: each part needs a value even when the template does not use it.
            if (string.IsNullOrWhiteSpace(o.PcId) || string.IsNullOrWhiteSpace(o.Phase) || string.IsNullOrWhiteSpace(o.RunId)) throw Refuse("missing-value");
            if (new[] { o.PcId, o.Phase, o.RunId }.Any(part => part.Contains("..", StringComparison.Ordinal) || Path.IsPathRooted(part))) throw Refuse("path-not-contained");
            var attempt = Path.Combine(o.EvidenceRoot, o.PcId, o.Phase, o.RunId);
            if (files.Directory.Exists(attempt) || files.File.Exists(attempt)) throw Refuse("existing-attempt");
            files.Directory.CreateDirectory(attempt);
            files.File.WriteAllText(Path.Combine(attempt, "resolved-manifest.txt"), text);
        }
    }
}
