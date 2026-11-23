// CARD-1410 / CARD-1842: the class-cost refresh, `antiphon-scripts refresh-nightly-class-costs`. It rewrites
// scripts/lib/nightly-class-costs.json (the launch planner's prices, NightlyTestsPlan.ClassCostTable) from nightly TRX
// files, and replaced scripts/refresh-nightly-class-costs.ps1 (deleted 2026-11-23, unit 5 of
// docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md). The TRX read is NightlyTrx.ClassDurations.
//
// Each measured class is the sum of its TRX result durations (serial time, so it never under-states a parallel launch),
// rounded to 0.1 s and at least 0.1 s, and its result count. --os must be windows or linux:
// - when it is the file's platform, the class's row becomes nodes, seconds, basis "trx", the row's own source (kept),
//   hostId, os and measuredRunId, then the row's other members in their order (otherSample among them);
// - otherwise the measurement is stored as the row's otherSample, and seconds and platform stay; a class with no row
//   gets none.
// Classes absent from the TRX keep their row exactly. The platform changes only with --set-platform, when every row's os
// is --os. Class names match ignoring case, as the planner reads them; a row keeps its spelling. The file is written with
// the classes in ordinal order, indented, LF line ends, UTF-8 without a BOM, and unknown values copied as they were read.
// Plain C#. ASCII only.
#nullable disable
using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml;

namespace Antiphon.Nightly
{
    /// <summary>The refresh-nightly-class-costs command's options.</summary>
    public sealed class RefreshNightlyClassCostsOptions
    {
        /// <summary>The CLI's usage line.</summary>
        public const string Usage = "refresh-nightly-class-costs --trx-path <a.trx,b.trx> --host-id <host> --os <windows|linux> --measured-run-id <run>"
            + " [--output-path <json>] [--source <text>] [--set-platform]";

        private static readonly string[] Valued = { "--trx-path", "--output-path", "--source", "--host-id", "--os", "--measured-run-id" };
        private static readonly string[] Switches = { "--set-platform" };

        /// <summary>The TRX files (full paths), in the order given: a later file's results add to an earlier one's.</summary>
        public IReadOnlyList<string> TrxPaths { get; init; } = Array.Empty<string>();

        /// <summary>The class-cost file read and rewritten (full path); a missing file starts empty.</summary>
        public string OutputPath { get; init; } = "";

        /// <summary>The document's source text; empty: "TRX durations: " and the TRX file names.</summary>
        public string Source { get; init; } = "";

        /// <summary>The measuring host (hostId on each measured row).</summary>
        public string HostId { get; init; } = "";

        /// <summary>windows or linux.</summary>
        public string Os { get; init; } = "";

        /// <summary>The measured run (measuredRunId on each measured row).</summary>
        public string MeasuredRunId { get; init; } = "";

        /// <summary>Move the platform to --os when every row's os is --os.</summary>
        public bool SetPlatform { get; init; }

        /// <summary>
        /// The arguments after the command name; relative paths are taken from currentDirectory, and the output defaults
        /// to the repository's scripts/lib/nightly-class-costs.json. NightlyUsageException for a malformed line or a
        /// missing --trx-path, --host-id, --os or --measured-run-id.
        /// </summary>
        public static RefreshNightlyClassCostsOptions Parse(IReadOnlyList<string> arguments, string currentDirectory, string repository)
        {
            var flags = NightlyArgs.Parse(arguments, Valued, Switches);
            var trx = flags.List("--trx-path");
            if (trx.Count == 0) throw new NightlyUsageException("missing --trx-path");
            var output = flags.Value("--output-path", "");
            return new RefreshNightlyClassCostsOptions
            {
                TrxPaths = trx.Select(path => Path.GetFullPath(path, currentDirectory)).ToArray(),
                OutputPath = string.IsNullOrWhiteSpace(output)
                    ? Path.GetFullPath(Path.Combine(repository, "scripts", "lib", "nightly-class-costs.json"))
                    : Path.GetFullPath(output, currentDirectory),
                Source = flags.Value("--source", ""),
                HostId = flags.Required("--host-id"),
                Os = flags.Required("--os"),
                MeasuredRunId = flags.Required("--measured-run-id"),
                SetPlatform = flags.Switch("--set-platform"),
            };
        }
    }

    /// <summary>The class-cost refresh.</summary>
    public static class ClassCostRefresh
    {
        /// <summary>The command the file names as its refresh.</summary>
        public const string RefreshCommand = "scripts/antiphon refresh-nightly-class-costs --trx-path <a.trx,b.trx> --host-id <host> --os <windows|linux> --measured-run-id <run>";

        private static readonly string[] Measurement = { "nodes", "seconds", "basis", "source", "hostId", "os", "measuredRunId" };

        private static readonly JsonWriterOptions Indented = new JsonWriterOptions
        {
            Indented = true, NewLine = "\n", Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        /// <summary>
        /// The refresh: reads options.OutputPath (when it exists) and the TRX files, writes the refreshed file and prints
        /// "refreshed &lt;n&gt; classes from &lt;m&gt; TRX files -&gt; &lt;path&gt;". Exit 0; 1 refused, nothing written: class-cost-host,
        /// class-cost-run or class-cost-os for a blank host or run or another os, class-cost-format for a file that is not a
        /// class-cost file, a TRX that cannot be read, or a file that cannot be written (one line on host.Error).
        /// </summary>
        public static int Run(RefreshNightlyClassCostsOptions options, NightlyHost host)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (host == null) throw new ArgumentNullException(nameof(host));
            try
            {
                var (text, measured) = Refresh(options);
                File.WriteAllText(options.OutputPath, text, new UTF8Encoding(false));
                host.Out.WriteLine("refreshed " + measured + " classes from " + options.TrxPaths.Count + " TRX files -> " + options.OutputPath);
                return 0;
            }
            catch (Exception e) when (e is InvalidDataException || e is IOException || e is UnauthorizedAccessException || e is JsonException
                || e is XmlException || e is FormatException || e is OverflowException || e is ArgumentException)
            {
                host.Error.WriteLine("refresh-nightly-class-costs: " + e.Message.Replace("\r", " ").Replace("\n", " "));
                return 1;
            }
        }

        /// <summary>The refreshed file's text and the number of classes the TRX files measured; InvalidDataException for a refusal.</summary>
        public static (string Text, int Measured) Refresh(RefreshNightlyClassCostsOptions options)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (string.IsNullOrWhiteSpace(options.HostId)) throw new InvalidDataException("class-cost-host");
            if (string.IsNullOrWhiteSpace(options.MeasuredRunId)) throw new InvalidDataException("class-cost-run");
            if (options.Os != "windows" && options.Os != "linux") throw new InvalidDataException("class-cost-os");

            var rows = new Dictionary<string, JsonObject>(StringComparer.OrdinalIgnoreCase);
            var platform = "";
            if (File.Exists(options.OutputPath))
            {
                if (!(JsonNode.Parse(File.ReadAllText(options.OutputPath)) is JsonObject document)
                    || !(document["format"] is JsonValue format) || !format.TryGetValue<string>(out var formatText)
                    || formatText != NightlyTestsPlan.ClassCostFormat || !(document["classes"] is JsonObject classes))
                    throw new InvalidDataException("class-cost-format");
                foreach (var (name, row) in classes.ToArray())
                {
                    if (!(row is JsonObject rowObject)) throw new InvalidDataException("class-cost-row: " + name);
                    classes.Remove(name);
                    rows[name] = rowObject;
                }
                platform = Plain(document["platform"]);
            }
            if (string.IsNullOrWhiteSpace(platform)) platform = options.Os;
            var sameOs = options.Os == platform;

            var fresh = new Dictionary<string, (int Nodes, double Seconds)>(StringComparer.OrdinalIgnoreCase);
            var order = new List<string>();
            foreach (var path in options.TrxPaths)
            {
                var trx = new XmlDocument();
                trx.LoadXml(File.ReadAllText(path));
                foreach (var (className, seconds) in NightlyTrx.ClassDurations(trx))
                {
                    if (!fresh.TryGetValue(className, out var sum)) { sum = (0, 0.0); order.Add(className); }
                    fresh[className] = (sum.Nodes + 1, sum.Seconds + seconds);
                }
            }
            foreach (var className in order)
            {
                var (nodes, total) = fresh[className];
                var seconds = Math.Max(0.1, Math.Round(total, 1));
                if (sameOs)
                {
                    rows.TryGetValue(className, out var prior);
                    var measuredRow = new JsonObject { ["nodes"] = nodes, ["seconds"] = seconds, ["basis"] = "trx" };
                    if (prior != null && prior["source"] != null) measuredRow["source"] = Plain(prior["source"]);
                    measuredRow["hostId"] = options.HostId;
                    measuredRow["os"] = options.Os;
                    measuredRow["measuredRunId"] = options.MeasuredRunId;
                    if (prior != null)
                    {
                        foreach (var (name, value) in prior.ToArray())
                        {
                            if (Measurement.Contains(name, StringComparer.Ordinal)) continue;
                            prior.Remove(name);
                            measuredRow[name] = value;
                        }
                    }
                    rows[className] = measuredRow;
                }
                else if (rows.TryGetValue(className, out var existing))
                {
                    existing["otherSample"] = new JsonObject
                    {
                        ["nodes"] = nodes, ["seconds"] = seconds, ["basis"] = "trx",
                        ["hostId"] = options.HostId, ["os"] = options.Os, ["measuredRunId"] = options.MeasuredRunId,
                    };
                }
            }
            if (options.SetPlatform && rows.Count > 0 && rows.Values.All(row => Plain(row["os"]) == options.Os)) platform = options.Os;

            var buffer = new ArrayBufferWriter<byte>();
            using (var writer = new Utf8JsonWriter(buffer, Indented))
            {
                writer.WriteStartObject();
                writer.WriteString("format", NightlyTestsPlan.ClassCostFormat);
                writer.WriteString("platform", platform);
                writer.WriteString("source", options.Source.Length > 0
                    ? options.Source
                    : "TRX durations: " + string.Join(", ", options.TrxPaths.Select(Path.GetFileName)));
                writer.WriteString("refresh", RefreshCommand);
                writer.WriteStartObject("classes");
                foreach (var name in rows.Keys.OrderBy(key => key, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(name);
                    rows[name].WriteTo(writer);
                }
                writer.WriteEndObject();
                writer.WriteEndObject();
            }
            return (Encoding.UTF8.GetString(buffer.WrittenSpan) + "\n", fresh.Count);
        }

        // [string]$value: a JSON string as itself, null (or absent) as "", any other value as its JSON text.
        private static string Plain(JsonNode value)
        {
            if (value == null) return "";
            if (value is JsonValue scalar && scalar.TryGetValue<string>(out var text)) return text;
            return value.ToJsonString();
        }
    }
}
