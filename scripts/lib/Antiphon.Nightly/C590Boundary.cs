// The CARD-0590 command boundary of the ported entries (unit 47 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md,
// W8 slice B): the C# twin of Invoke-C590 and Write-C590Result (scripts/c590-command.ps1) for the commands verify-docker-stack,
// test-docker and test-docker-container. Since unit 66 it is the live bridge's too (C590Bridge, scripts/c590-real.ps1 before), and
// c590-command.ps1 is gone.
//
// Invoke records {"exe":...,"args":[...]} as one compact JSON line in the sink file ANTIPHON_C590_SINK names (when it is set),
// then, with ANTIPHON_C590_STUB set (any non-empty value), starts nothing; without it, it starts the executable with those
// arguments on the host's launcher, its output captured and ignored, as Invoke-C590 did. Every caller here discards the answer,
// so the stub file's command table is not read (Invoke-C590 parsed it only to answer). Result writes
// <evidenceRoot>/c590-result.json ({"accepted":...,"diagnosis":...,"exit":...} and the extras, compact, ASCII, one line), prints
// "DIAGNOSIS=<diagnosis>" and returns the exit code. The manifest helpers read a value as the scripts did after ConvertFrom-Json
// ([string], [bool], [int] and [double] casts, @() of a value), with each key matched exactly.
// Plain C#: no PowerShell types. ASCII only.
#nullable disable
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Antiphon.Nightly
{
    /// <summary>A failure the PowerShell entries ended with exit 1 (a thrown error): the command prints one stderr line and exits 1.</summary>
    public sealed class C590Failure : Exception
    {
        /// <summary>A failure with its reason.</summary>
        public C590Failure(string message) : base(message) { }
    }

    /// <summary>Invoke-C590 and Write-C590Result of the ported CARD-0590 entries.</summary>
    public sealed class C590Boundary
    {
        /// <summary>Set (non-empty): every Invoke is recorded only, nothing starts.</summary>
        public const string StubVariable = "ANTIPHON_C590_STUB";

        /// <summary>The file every Invoke is recorded in, one JSON line each (unset: not recorded).</summary>
        public const string SinkVariable = "ANTIPHON_C590_SINK";

        /// <summary>The result file under the evidence root.</summary>
        public const string ResultFileName = "c590-result.json";

        private static readonly JsonSerializerOptions Compact = new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

        private readonly NightlyHost _host;
        private readonly IFileSystem _files;

        /// <summary>The boundary of one run: its host (launcher, output, variables) and file system.</summary>
        public C590Boundary(NightlyHost host, IFileSystem files)
        {
            _host = host ?? throw new ArgumentNullException(nameof(host));
            _files = files ?? throw new ArgumentNullException(nameof(files));
        }

        /// <summary>ANTIPHON_C590_STUB is set: commands are recorded, never started.</summary>
        public bool Stubbed => !string.IsNullOrEmpty(_host.Variables(StubVariable));

        /// <summary>Invoke-C590: records the command in the sink, then starts it unless stubbed (its output is captured and ignored).</summary>
        public void Invoke(string exe, params string[] arguments)
        {
            var args = arguments ?? Array.Empty<string>();
            var record = new JsonObject
            {
                ["exe"] = exe ?? "",
                ["args"] = new JsonArray(args.Select(argument => (JsonNode)JsonValue.Create(argument ?? "")).ToArray()),
            };
            var sink = _host.Variables(SinkVariable);
            if (!string.IsNullOrEmpty(sink)) _files.File.AppendAllText(sink, record.ToJsonString(Compact) + Environment.NewLine, Encoding.ASCII);
            if (Stubbed) return;
            try
            {
                _host.Launch(new NightlyLaunch(exe, args, "", CaptureOutput: true));
            }
            catch (Exception e) when (e is Win32Exception || e is InvalidOperationException || e is IOException || e is UnauthorizedAccessException)
            {
                throw new C590Failure("cannot start " + exe + ": " + e.Message);
            }
        }

        /// <summary>
        /// Write-C590Result: writes the result file, prints DIAGNOSIS= (unless print is false: a caller that captured the function's
        /// output, as the live bridge's proof exports did, never showed it), and returns exitCode (the command's exit).
        /// </summary>
        public int Result(string evidenceRoot, bool accepted, string diagnosis, int exitCode, JsonObject extra = null, bool print = true)
        {
            if (string.IsNullOrEmpty(evidenceRoot)) throw new C590Failure("the manifest has no evidenceRoot");
            _files.Directory.CreateDirectory(evidenceRoot);
            var record = new JsonObject { ["accepted"] = accepted, ["diagnosis"] = diagnosis ?? "", ["exit"] = exitCode };
            if (extra != null)
                foreach (var pair in extra) record[pair.Key] = pair.Value?.DeepClone();
            _files.File.WriteAllText(_files.Path.Combine(evidenceRoot, ResultFileName), record.ToJsonString(Compact) + Environment.NewLine, Encoding.ASCII);
            if (print) _host.Out.WriteLine("DIAGNOSIS=" + (diagnosis ?? ""));
            return exitCode;
        }
    }

    /// <summary>A CARD-0590 manifest read as the scripts read it after ConvertFrom-Json.</summary>
    public static class C590Manifest
    {
        /// <summary>The JSON document at path; C590Failure when it cannot be read or is not JSON (a duplicate key included).</summary>
        public static JsonNode Read(IFileSystem files, string path)
        {
            string text;
            try
            {
                text = files.File.ReadAllText(path);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException || e is NotSupportedException)
            {
                throw new C590Failure("cannot read " + path + ": " + e.Message);
            }
            try
            {
                var node = JsonNode.Parse(text);
                Touch(node);
                return node;
            }
            catch (Exception e) when (e is JsonException || e is ArgumentException || e is InvalidOperationException)
            {
                throw new C590Failure(path + " is not valid JSON: " + e.Message);
            }
        }

        // Every object's properties once: a duplicate key throws here (ConvertFrom-Json refused one too), not at a later read.
        private static void Touch(JsonNode node)
        {
            if (node is JsonObject obj)
                foreach (var pair in obj) Touch(pair.Value);
            else if (node is JsonArray array)
                foreach (var item in array) Touch(item);
        }

        /// <summary>$node.name: the property's value (exact key), or null when the node is not an object or has no such property.</summary>
        public static JsonNode Field(JsonNode node, string name) =>
            node is JsonObject obj && obj.TryGetPropertyValue(name, out var value) ? value : null;

        /// <summary>The property exists (its value may be null).</summary>
        public static bool Has(JsonNode node, string name) => node is JsonObject obj && obj.ContainsKey(name);

        /// <summary>[string]$node.name.</summary>
        public static string Str(JsonNode node, string name) => HealthJson.Str(Field(node, name));

        /// <summary>[bool]$node.name.</summary>
        public static bool IsTrue(JsonNode node, string name) => HealthJson.IsTrue(Field(node, name));

        /// <summary>[int]$node.name; C590Failure when the cast fails.</summary>
        public static int Int(JsonNode node, string name) =>
            HealthJson.CastInt(Field(node, name)) ?? throw new C590Failure("cannot convert " + name + " to an integer");

        /// <summary>[double]$node.name; C590Failure when the cast fails.</summary>
        public static double Double(JsonNode node, string name)
        {
            var value = Field(node, name);
            switch (HealthJson.Kind(value))
            {
                case JsonValueKind.Null: return 0;
                case JsonValueKind.True: return 1;
                case JsonValueKind.False: return 0;
                case JsonValueKind.Number:
                case JsonValueKind.String:
                    var text = HealthJson.Str(value);
                    if (string.IsNullOrWhiteSpace(text)) return 0;
                    if (double.TryParse(text, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var number)) return number;
                    break;
            }
            throw new C590Failure("cannot convert " + name + " to a number");
        }

        /// <summary>@($value): an array's items, a single value as one item, and null as one null item.</summary>
        public static IReadOnlyList<JsonNode> Items(JsonNode value) =>
            value is JsonArray array ? array.ToArray() : new[] { value };

        /// <summary>first -eq second for text (PowerShell: case-insensitive).</summary>
        public static bool Eq(string first, string second) => HealthJson.Eq(first, second);

        /// <summary>
        /// Test-Boundary (verify-docker-stack.ps1) and Test-InsideCheckout (test-docker.ps1): the canonical path when given, else
        /// the full path of path, is root or under it (case-insensitive, either separator after the root). A path that has no
        /// full path is a C590Failure.
        /// </summary>
        public static bool Inside(IFileSystem files, string path, string root, string canonical)
        {
            try
            {
                var candidate = !string.IsNullOrEmpty(canonical) ? canonical : files.Path.GetFullPath(path ?? "");
                var bound = files.Path.GetFullPath(root ?? "").TrimEnd('\\', '/');
                var normalized = candidate.TrimEnd('\\', '/');
                if (normalized.Length < bound.Length || !normalized.Substring(0, bound.Length).Equals(bound, StringComparison.OrdinalIgnoreCase)) return false;
                if (normalized.Length == bound.Length) return true;
                var next = normalized[bound.Length];
                return next == '\\' || next == '/';
            }
            catch (Exception e) when (e is ArgumentException || e is NotSupportedException || e is PathTooLongException)
            {
                throw new C590Failure("not a path: " + e.Message);
            }
        }
    }
}
