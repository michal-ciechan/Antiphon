// The test trace summary: `antiphon-scripts summarize-test-trace`, the reader of the span file that
// ANTIPHON_TEST_TRACE_FILE makes tests/Shared/TestTrace.cs write. It replaced scripts/summarize-test-trace.ps1 (deleted
// 2026-11-23, unit 5 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md). TestTrace.cs stays the writer: it
// is compiled into test assemblies that do not reference this core. Each line of the file is one completed span
// (TraceId, SpanId, ParentId, Source, Name, Start, DurationMs, Status, Tags, Pid, as TestTrace.SpanRecord writes them);
// TUnit starts one trace per test, so a trace is a test. It prints:
//   1. tests (root spans) by duration;
//   2. span names ("Source | Name") by self and total time across the run. Self time is the span's duration minus its
//      direct children's durations, floored at zero, so nested work is not counted twice; children that overlap in
//      parallel can make it an underestimate;
//   3. with --test, the span tree of each root whose name contains that text (case-insensitive; * and ? are
//      wildcards), children in start order, children shorter than --min-ms left out.
// A line that is not a span object is skipped. Numbers are printed in the invariant culture. Plain C#. ASCII only.
#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Antiphon.Nightly
{
    /// <summary>The summarize-test-trace command's options.</summary>
    public sealed class SummarizeTestTraceOptions
    {
        /// <summary>The CLI's usage line.</summary>
        public const string Usage = "summarize-test-trace --path <trace.jsonl> [--top 25] [--test <substring>] [--min-ms 1]";

        private static readonly string[] Valued = { "--path", "--top", "--test", "--min-ms" };

        /// <summary>The span file (full path).</summary>
        public string Path { get; init; } = "";

        /// <summary>The rows shown in each ranking.</summary>
        public int Top { get; init; } = 25;

        /// <summary>The root names whose trees are printed; empty prints none.</summary>
        public string Test { get; init; } = "";

        /// <summary>Below the root, a span shorter than this many milliseconds is left out of a tree (with its children).</summary>
        public double MinMs { get; init; } = 1;

        /// <summary>The arguments after the command name; a relative --path is taken from currentDirectory. NightlyUsageException for a malformed line.</summary>
        public static SummarizeTestTraceOptions Parse(IReadOnlyList<string> arguments, string currentDirectory)
        {
            var flags = NightlyArgs.Parse(arguments, Valued, Array.Empty<string>());
            var minMs = 1.0;
            var minText = flags.Value("--min-ms", "");
            if (minText.Length > 0 && !double.TryParse(minText, NumberStyles.Float, CultureInfo.InvariantCulture, out minMs))
                throw new NightlyUsageException("--min-ms expects a number, not '" + minText + "'");
            return new SummarizeTestTraceOptions
            {
                Path = System.IO.Path.GetFullPath(flags.Required("--path"), currentDirectory),
                Top = flags.Int("--top", 25),
                Test = flags.Value("--test", ""),
                MinMs = minMs,
            };
        }
    }

    /// <summary>One span of a trace file.</summary>
    public sealed record TestTraceSpan(string TraceId, string SpanId, string ParentId, string Source, string Name, DateTime Start,
        double DurationMs, IReadOnlyList<KeyValuePair<string, string>> Tags);

    /// <summary>The trace file reader and its summary.</summary>
    public static class TestTraceSummary
    {
        /// <summary>
        /// The spans of a trace file, in file order. A blank line, or one that is not a JSON object with string TraceId and
        /// SpanId and a numeric DurationMs, is skipped.
        /// </summary>
        public static IReadOnlyList<TestTraceSpan> Read(string path)
        {
            var spans = new List<TestTraceSpan>();
            foreach (var line in File.ReadLines(path))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var span = Parse(line);
                if (span != null) spans.Add(span);
            }
            return spans;
        }

        /// <summary>
        /// The summary text of the spans, each line ended by newLine (a tag value keeps its own line breaks).
        /// InvalidDataException (no-spans) when there are none.
        /// </summary>
        public static string Text(IReadOnlyList<TestTraceSpan> spans, string path, int top, string test, double minMs, string newLine = "\n")
        {
            if (spans == null || spans.Count == 0) throw new InvalidDataException("No spans in " + path + ".");
            var byId = new Dictionary<string, TestTraceSpan>(StringComparer.Ordinal);
            foreach (var span in spans) byId[Key(span.TraceId, span.SpanId)] = span;
            var children = new Dictionary<string, List<TestTraceSpan>>(StringComparer.Ordinal);
            foreach (var span in spans)
            {
                if (span.ParentId == null) continue;
                var key = Key(span.TraceId, span.ParentId);
                if (!children.TryGetValue(key, out var list)) children[key] = list = new List<TestTraceSpan>();
                list.Add(span);
            }
            double Self(TestTraceSpan span)
            {
                var sum = 0.0;
                if (children.TryGetValue(Key(span.TraceId, span.SpanId), out var list)) foreach (var child in list) sum += child.DurationMs;
                return Math.Max(0.0, span.DurationMs - sum);
            }

            var roots = spans.Where(span => span.ParentId == null || !byId.ContainsKey(Key(span.TraceId, span.ParentId))).ToList();
            var text = new StringBuilder();
            void Line(string value) => text.Append(value).Append(newLine);
            var traces = spans.Select(span => span.TraceId).Distinct(StringComparer.OrdinalIgnoreCase).Count();
            var sources = spans.Select(span => span.Source).Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(source => source, StringComparer.InvariantCultureIgnoreCase).ToList();
            Line(Format("{0} spans, {1} traces, {2} sources: {3}", spans.Count, traces, sources.Count, string.Join(", ", sources)));

            Line("");
            Line("== Root spans (tests) by duration ==");
            foreach (var root in roots.OrderByDescending(span => span.DurationMs).Take(Math.Max(0, top)))
                Line(Format("{0,10:N1} ms  {1,-12} {2}", root.DurationMs, root.Source, root.Name));

            Line("");
            Line("== Span names by self time (all traces) ==");
            var totals = new Dictionary<string, (int Count, double Total, double Self, double Max)>(StringComparer.Ordinal);
            var order = new List<string>();
            foreach (var span in spans)
            {
                var name = span.Source + " | " + span.Name;
                if (!totals.TryGetValue(name, out var row)) { row = (0, 0.0, 0.0, 0.0); order.Add(name); }
                totals[name] = (row.Count + 1, row.Total + span.DurationMs, row.Self + Self(span), Math.Max(row.Max, span.DurationMs));
            }
            Line(Format("{0,7} {1,12} {2,12} {3,10}  {4}", "count", "self ms", "total ms", "max ms", "source | span"));
            foreach (var name in order.OrderByDescending(n => totals[n].Self).Take(Math.Max(0, top)))
            {
                var row = totals[name];
                Line(Format("{0,7} {1,12:N0} {2,12:N0} {3,10:N0}  {4}", row.Count, row.Self, row.Total, row.Max, name));
            }

            if (!string.IsNullOrEmpty(test))
            {
                var like = Wildcard(test);
                void Tree(TestTraceSpan span, int depth)
                {
                    if (span.DurationMs < minMs && depth > 0) return;
                    var tags = span.Tags == null || span.Tags.Count == 0 ? "" : " {" + string.Join(" ", span.Tags.Select(tag => tag.Key + "=" + tag.Value)) + "}";
                    Line(Format("{0}{1,9:N1} ms (self {2,8:N1})  {3}{4}", new string(' ', 2 * depth), span.DurationMs, Self(span), span.Name, tags));
                    if (children.TryGetValue(Key(span.TraceId, span.SpanId), out var list))
                        foreach (var child in list.OrderBy(c => c.Start)) Tree(child, depth + 1);
                }
                foreach (var root in roots.Where(r => like.IsMatch(r.Name ?? "")))
                {
                    Line("");
                    Line("== Trace: " + root.Name + " ==");
                    Tree(root, 0);
                }
            }
            return text.ToString();
        }

        /// <summary>
        /// The command: reads options.Path and prints the summary on host.Out. Exit 0; 1 the file cannot be read or holds no
        /// span (one line on host.Error).
        /// </summary>
        public static int Run(SummarizeTestTraceOptions options, NightlyHost host)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (host == null) throw new ArgumentNullException(nameof(host));
            try
            {
                host.Out.Write(Text(Read(options.Path), options.Path, options.Top, options.Test, options.MinMs, host.Out.NewLine));
                return 0;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is InvalidDataException)
            {
                host.Error.WriteLine("summarize-test-trace: " + e.Message.Replace("\r", " ").Replace("\n", " "));
                return 1;
            }
        }

        private static string Key(string traceId, string spanId) => traceId + "/" + spanId;

        private static string Format(string format, params object[] values) => string.Format(CultureInfo.InvariantCulture, format, values);

        // The -like "*<test>*" match, ignoring case: * any run, ? one character, [abc] or [a-c] one of a set, a backtick
        // escapes the next character; everything else (and a '[' with no ']') is literal.
        private static Regex Wildcard(string test)
        {
            var pattern = new StringBuilder("^.*");
            for (var i = 0; i < test.Length; i++)
            {
                var c = test[i];
                var close = c == '[' ? test.IndexOf(']', i + 1) : -1;
                if (c == '`' && i + 1 < test.Length) pattern.Append(Regex.Escape(test[++i].ToString()));
                else if (c == '*') pattern.Append(".*");
                else if (c == '?') pattern.Append('.');
                else if (close > i + 1)
                {
                    pattern.Append('[');
                    foreach (var member in test.Substring(i + 1, close - i - 1))
                        pattern.Append(member == '-' ? "-" : member == '\\' || member == '^' || member == '[' ? "\\" + member : member.ToString());
                    pattern.Append(']');
                    i = close;
                }
                else pattern.Append(Regex.Escape(c.ToString()));
            }
            pattern.Append(".*$");
            return new Regex(pattern.ToString(), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline);
        }

        private static TestTraceSpan Parse(string line)
        {
            try
            {
                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object) return null;
                var traceId = Text(root, "TraceId");
                var spanId = Text(root, "SpanId");
                if (traceId == null || spanId == null) return null;
                if (!root.TryGetProperty("DurationMs", out var duration) || duration.ValueKind != JsonValueKind.Number) return null;
                var start = root.TryGetProperty("Start", out var startValue) && startValue.ValueKind == JsonValueKind.String
                    && startValue.TryGetDateTime(out var parsed) ? parsed.ToUniversalTime() : DateTime.MinValue;
                var tags = new List<KeyValuePair<string, string>>();
                if (root.TryGetProperty("Tags", out var tagValues) && tagValues.ValueKind == JsonValueKind.Object)
                    foreach (var tag in tagValues.EnumerateObject())
                        tags.Add(new KeyValuePair<string, string>(tag.Name, tag.Value.ValueKind == JsonValueKind.String ? tag.Value.GetString()
                            : tag.Value.ValueKind == JsonValueKind.Null ? "" : tag.Value.GetRawText()));
                return new TestTraceSpan(traceId, spanId, Text(root, "ParentId"), Text(root, "Source") ?? "", Text(root, "Name") ?? "", start,
                    duration.GetDouble(), tags);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private static string Text(JsonElement root, string name) =>
            root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }
}
