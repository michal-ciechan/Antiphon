// CARD-1842: the expected-duration roster, tests/Shared/expected-duration-roster.json. It is a projection of the
// class-cost file (scripts/lib/nightly-class-costs.json), not a second store: each class copies the cost row's seconds
// and basis, and hostId, os and measuredRunId (null when the row has none). Run is `antiphon-scripts
// export-expected-duration-roster`; it replaced scripts/export-expected-duration-roster.ps1 (deleted 2026-11-23, unit 5
// of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md). Load and ProjectCostFile are the one reader of both
// files: ExpectedDurationSyncTests checks the committed roster with them. The [ExpectedDuration] attribute scan needs
// Roslyn and stays test-side (tests/Shared/ExpectedDurationRoster.cs), so the CLI carries no test code. The roster is
// not an embedded resource anywhere: a refresh must not change the test assembly. Plain C#. ASCII only.
#nullable enable
using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Antiphon.Nightly
{
    /// <summary>One roster class: the class-cost row's seconds and basis, and the measurement identity when the row has one.</summary>
    public sealed record ExpectedDurationEntry(double Seconds, string Basis, string? HostId, string? Os, string? MeasuredRunId);

    /// <summary>The roster file, or the projection of the class-cost file it must equal.</summary>
    public sealed record ExpectedDurationRosterDocument(string Platform, IReadOnlyDictionary<string, ExpectedDurationEntry> Classes);

    /// <summary>The export-expected-duration-roster command's options: the class-cost file read and the roster written.</summary>
    public sealed class ExportExpectedDurationRosterOptions
    {
        /// <summary>The CLI's usage line.</summary>
        public const string Usage = "export-expected-duration-roster [--cost-path <json>] [--output-path <json>]";

        private static readonly string[] Valued = { "--cost-path", "--output-path" };

        /// <summary>The checkout: the defaults are its files, and the roster names a cost file inside it relative to it.</summary>
        public string Repository { get; init; } = "";

        /// <summary>The class-cost file (full path).</summary>
        public string CostPath { get; init; } = "";

        /// <summary>The roster written (full path).</summary>
        public string OutputPath { get; init; } = "";

        /// <summary>
        /// The arguments after the command name. A relative path is taken from currentDirectory; an absent or blank flag is
        /// the repository's own file. NightlyUsageException for a malformed line.
        /// </summary>
        public static ExportExpectedDurationRosterOptions Parse(IReadOnlyList<string> arguments, string currentDirectory, string repository)
        {
            var flags = NightlyArgs.Parse(arguments, Valued, Array.Empty<string>());
            string PathOf(string flag, string relative)
            {
                var value = flags.Value(flag, "");
                return string.IsNullOrWhiteSpace(value)
                    ? Path.GetFullPath(Path.Combine(new[] { repository }.Concat(relative.Split('/')).ToArray()))
                    : Path.GetFullPath(value, currentDirectory);
            }
            return new ExportExpectedDurationRosterOptions
            {
                Repository = Path.GetFullPath(repository),
                CostPath = PathOf("--cost-path", ExpectedDurationRosterFile.CostFileRelativePath),
                OutputPath = PathOf("--output-path", ExpectedDurationRosterFile.RelativePath),
            };
        }
    }

    /// <summary>The roster file: its reader, the class-cost projection and the export.</summary>
    public static class ExpectedDurationRosterFile
    {
        /// <summary>The roster's format.</summary>
        public const string Format = "antiphon-expected-duration-roster-v1";

        /// <summary>The class-cost file's format (the launch planner's).</summary>
        public const string CostFormat = NightlyTestsPlan.ClassCostFormat;

        /// <summary>The roster, relative to the checkout.</summary>
        public const string RelativePath = "tests/Shared/expected-duration-roster.json";

        /// <summary>The class-cost file, relative to the checkout.</summary>
        public const string CostFileRelativePath = "scripts/lib/nightly-class-costs.json";

        /// <summary>The command the roster names as its writer.</summary>
        public const string ExportCommand = "scripts/antiphon export-expected-duration-roster";

        private static readonly JsonWriterOptions Indented = new JsonWriterOptions
        {
            Indented = true, NewLine = "\n", Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        /// <summary>The roster file. InvalidDataException (expected-duration-roster-*) for a file that is not one.</summary>
        public static ExpectedDurationRosterDocument Load(string path) => Read(path, Format, "expected-duration-roster");

        /// <summary>The roster the class-cost file projects to. InvalidDataException (class-cost-*) for a file that is not one.</summary>
        public static ExpectedDurationRosterDocument ProjectCostFile(string path) => Read(path, CostFormat, "class-cost");

        /// <summary>
        /// The roster's text: format, platform, costFile, export and the classes in ordinal order, indented, LF line ends and
        /// a final LF.
        /// </summary>
        public static string Text(ExpectedDurationRosterDocument roster, string costFile)
        {
            if (roster == null) throw new ArgumentNullException(nameof(roster));
            var buffer = new ArrayBufferWriter<byte>();
            using (var writer = new Utf8JsonWriter(buffer, Indented))
            {
                writer.WriteStartObject();
                writer.WriteString("format", Format);
                writer.WriteString("platform", roster.Platform);
                writer.WriteString("costFile", costFile);
                writer.WriteString("export", ExportCommand);
                writer.WriteStartObject("classes");
                foreach (var (name, entry) in roster.Classes.OrderBy(pair => pair.Key, StringComparer.Ordinal))
                {
                    writer.WriteStartObject(name);
                    writer.WriteNumber("seconds", entry.Seconds);
                    writer.WriteString("basis", entry.Basis);
                    Optional(writer, "hostId", entry.HostId);
                    Optional(writer, "os", entry.Os);
                    Optional(writer, "measuredRunId", entry.MeasuredRunId);
                    writer.WriteEndObject();
                }
                writer.WriteEndObject();
                writer.WriteEndObject();
            }
            return Encoding.UTF8.GetString(buffer.WrittenSpan) + "\n";
        }

        /// <summary>
        /// The export: writes the projection of options.CostPath to options.OutputPath (UTF-8, no BOM) and prints
        /// "exported &lt;n&gt; classes -&gt; &lt;path&gt;". The roster names the cost file relative to the checkout when it is
        /// inside it, with '/' separators. Exit 0; 1 the cost file is not one or a file cannot be read or written (one line on
        /// host.Error).
        /// </summary>
        public static int Run(ExportExpectedDurationRosterOptions options, NightlyHost host)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (host == null) throw new ArgumentNullException(nameof(host));
            try
            {
                var roster = ProjectCostFile(options.CostPath);
                File.WriteAllText(options.OutputPath, Text(roster, CheckoutRelative(options.CostPath, options.Repository)), new UTF8Encoding(false));
                host.Out.WriteLine("exported " + roster.Classes.Count + " classes -> " + options.OutputPath);
                return 0;
            }
            catch (Exception e) when (e is InvalidDataException || e is IOException || e is UnauthorizedAccessException || e is JsonException)
            {
                host.Error.WriteLine("export-expected-duration-roster: " + e.Message.Replace("\r", " ").Replace("\n", " "));
                return 1;
            }
        }

        // The path relative to the checkout when it is inside it, else the full path; '/' separators either way.
        private static string CheckoutRelative(string path, string repository)
        {
            var full = Path.GetFullPath(path);
            var prefix = Path.GetFullPath(repository).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
            if (full.StartsWith(prefix, StringComparison.Ordinal)) full = full.Substring(prefix.Length);
            return full.Replace('\\', '/');
        }

        private static void Optional(Utf8JsonWriter writer, string name, string? value)
        {
            if (value == null) writer.WriteNull(name);
            else writer.WriteString(name, value);
        }

        private static ExpectedDurationRosterDocument Read(string path, string format, string label)
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("format", out var formatValue)
                || formatValue.ValueKind != JsonValueKind.String
                || formatValue.GetString() != format)
                throw new InvalidDataException(label + "-format: " + path);
            if (!root.TryGetProperty("platform", out var platform) || platform.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(platform.GetString()))
                throw new InvalidDataException(label + "-platform: " + path);
            if (!root.TryGetProperty("classes", out var classes) || classes.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException(label + "-classes: " + path);

            var entries = new Dictionary<string, ExpectedDurationEntry>(StringComparer.Ordinal);
            foreach (var row in classes.EnumerateObject())
            {
                if (row.Value.ValueKind != JsonValueKind.Object
                    || !row.Value.TryGetProperty("seconds", out var seconds) || seconds.ValueKind != JsonValueKind.Number
                    || !row.Value.TryGetProperty("basis", out var basis) || basis.ValueKind != JsonValueKind.String
                    || string.IsNullOrWhiteSpace(basis.GetString()))
                    throw new InvalidDataException(label + "-row: " + row.Name);
                var entry = new ExpectedDurationEntry(
                    seconds.GetDouble(),
                    basis.GetString()!,
                    OptionalString(row.Value, "hostId", label, row.Name),
                    OptionalString(row.Value, "os", label, row.Name),
                    OptionalString(row.Value, "measuredRunId", label, row.Name));
                if (!entries.TryAdd(row.Name, entry)) throw new InvalidDataException(label + "-duplicate: " + row.Name);
            }

            return new ExpectedDurationRosterDocument(platform.GetString()!, entries);
        }

        private static string? OptionalString(JsonElement row, string name, string label, string className)
        {
            if (!row.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
                return null;
            if (value.ValueKind != JsonValueKind.String)
                throw new InvalidDataException(label + "-" + name + ": " + className);
            return value.GetString();
        }
    }
}
