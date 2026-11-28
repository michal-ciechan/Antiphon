using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace Antiphon.Nightly
{
    public static class TestTraceBreakdown
    {
        public const string Usage = "test-trace-summary <trace file or dir>";

        private static readonly string[] Phases =
        {
            "Startup", "Discovery", "One-time setup", "One-time teardown", "Per-test setup",
            "Per-test body", "Per-test teardown", "Limiter wait", "Process exit/teardown",
        };

        public static int Run(string path, TextWriter output, TextWriter error)
        {
            try
            {
                var files = Directory.Exists(path)
                    ? Directory.GetFiles(path, "test-trace-*.jsonl", SearchOption.AllDirectories)
                    : new[] { path };
                if (files.Length == 0)
                {
                    error.WriteLine("No test-trace JSONL files in " + path + ".");
                    return 1;
                }
                foreach (var file in files.OrderBy(file => file, StringComparer.Ordinal))
                    output.Write(Text(TestTraceSummary.Read(file), file));
                return 0;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                error.WriteLine(ex.Message);
                return 1;
            }
        }

        public static string Text(IReadOnlyList<TestTraceSpan> spans, string path)
        {
            using var output = new StringWriter(CultureInfo.InvariantCulture);
            output.WriteLine(path);
            var timed = spans.Where(s => s.Start != default && double.IsFinite(s.DurationMs) && s.DurationMs >= 0).ToArray();
            if (timed.Length == 0)
            {
                output.WriteLine("  No completed spans.");
                return output.ToString();
            }

            var lifetime = timed.FirstOrDefault(s => IsProcess(s, "process lifetime"));
            var start = lifetime?.Start ?? timed.Min(s => s.Start);
            var end = lifetime is null ? timed.Max(End) : End(lifetime);
            var byId = timed.ToLookup(s => (s.TraceId, s.SpanId));

            var children = timed.Where(s => s.ParentId != null).ToLookup(s => (s.TraceId, s.ParentId));
            // TUnit test cases have independent trace IDs; suite membership is carried by tags.
            var firstBodyBySuite = timed.Where(s => Framework(s) && s.Name == "test case")
                .SelectMany(test => children[(test.TraceId, test.SpanId)]
                    .Where(child => Framework(child) && child.Name == "test body")
                    .Select(body => (Suite: Suite(test), body.Start)))
                .GroupBy(item => item.Suite)
                .ToDictionary(group => group.Key, group => group.Min(item => item.Start));
            var phases = Phases.ToDictionary(phase => phase,
                _ => new List<(DateTime Start, DateTime End, string Owner)>());
            var classes = new Dictionary<string, double>();
            var lastTest = start;
            var lastWork = start;
            foreach (var span in timed)
            {
                var parent = byId[(span.TraceId, span.ParentId)].FirstOrDefault();
                var owner = Owner(span, byId);
                var phase = Phase(span, parent, children, firstBodyBySuite, owner);
                if (phase is not null)
                    Add(phase, span.Start, End(span), phase.StartsWith("Per-test ", StringComparison.Ordinal) ? owner : "");
                if (Framework(span) && span.Name == "test case")
                {
                    var name = Tag(span, "tunit.test.class") ?? Tag(span, "test.suite.name") ?? "(unknown)";
                    classes[name] = classes.GetValueOrDefault(name) + span.DurationMs;
                    lastTest = Max(lastTest, End(span));
                    var body = children[(span.TraceId, span.SpanId)]
                        .Where(s => Framework(s) && s.Name == "test body")
                        .OrderBy(s => s.Start)
                        .FirstOrDefault();
                    if (body is not null)
                        Add("Per-test setup", span.Start, body.Start, owner);
                }
                if (span != lifetime && !IsProcess(span, "process startup")
                    && !(Framework(span) && span.Name is "test session" or "test assembly" or "test suite"))
                    lastWork = Max(lastWork, End(span));
            }

            if (lifetime is not null && lastTest > start)
                Add("Process exit/teardown", Max(lastTest, lastWork), end);

            var wall = (end - start).TotalMilliseconds;
            var accounted = UnionMs(phases.Values.SelectMany(intervals => intervals)
                .Select(i => (i.Start, i.End)));
            output.WriteLine($"  Wall{(lifetime is null ? " (observed lower bound)" : "")} {wall:F3} ms");
            output.WriteLine("  Phase totals (ms, concurrent tests can overlap)");
            foreach (var (phase, intervals) in phases)
            {
                if (intervals.Count == 0)
                    output.WriteLine($"    {phase,-22} -");
                else
                    output.WriteLine($"    {phase,-22} {intervals.GroupBy(i => i.Owner)
                        .Sum(group => UnionMs(group.Select(i => (i.Start, i.End)))):F3}");
            }
            output.WriteLine($"    {"Idle/unaccounted",-22} {Math.Max(0, wall - accounted):F3}");
            output.WriteLine("    Per-test setup includes time inside the test case before its first body span.");
            output.WriteLine("    - = no observed timing (TUnit 1.44 does not expose ParallelLimiter wait)");
            output.WriteLine("  Top 20 spans by inclusive time (ms)");
            foreach (var span in timed.Where(s => s != lifetime).OrderByDescending(s => s.DurationMs).Take(20))
                output.WriteLine($"    {span.DurationMs,12:F3}  {span.Name}{TestLabel(span)}");
            output.WriteLine("  Top 10 classes by summed test-case time (ms)");
            foreach (var (name, duration) in classes.OrderByDescending(pair => pair.Value).ThenBy(pair => pair.Key, StringComparer.Ordinal).Take(10))
                output.WriteLine($"    {duration,12:F3}  {name}");
            return output.ToString();

            void Add(string phase, DateTime from, DateTime to, string owner = "")
            {
                from = Max(from, start);
                to = to < end ? to : end;
                if (to >= from)
                    phases[phase].Add((from, to, owner));
            }
        }

        private static string Phase(TestTraceSpan span, TestTraceSpan parent,
            ILookup<(string, string), TestTraceSpan> children,
            IReadOnlyDictionary<(string Namespace, string Name), DateTime> firstBodyBySuite, string owner)
        {
            if (IsProcess(span, "process startup"))
                return "Startup";
            var fixture = Tag(span, "antiphon.fixture");
            if (fixture is not null && (owner.Length == 0 ||
                fixture is "SharedApp" or "db-assembly" or "db-runtime" or "harness-runtime" or
                    "c0824-producer-seed" or "c0824-producer-transport"))
            {
                var teardown = span.Name.EndsWith(" dispose", StringComparison.Ordinal) ||
                    Tag(span, "antiphon.phase") is "host-shutdown" or "fixture-teardown" or "db-cleanup";
                return teardown ? "One-time teardown" : "One-time setup";
            }
            if (!Framework(span))
                return null;
            if (span.Name == "test discovery" || parent?.Name == "test discovery")
                return "Discovery";
            if (span.Name == "test body")
                return "Per-test body";
            if (span.Name == "parallel limiter wait")
                return "Limiter wait";

            var scope = Tag(span, "tunit.trace.scope");
            var perTest = scope == "test" || (scope is null && parent?.Name == "test case");
            if (span.Name.StartsWith("initialize ", StringComparison.Ordinal) ||
                span.Name.StartsWith("construct ", StringComparison.Ordinal))
                return perTest ? "Per-test setup" : "One-time setup";
            if (span.Name.StartsWith("dispose ", StringComparison.Ordinal))
                return perTest ? "Per-test teardown" : "One-time teardown";
            if (!span.Name.StartsWith("hook: ", StringComparison.Ordinal) || parent is null)
                return null;

            // TUnit 1.44 names its hooks "hook: <method>" without the Before/After kind.
            // A hook runs before the work enclosed by its parent, or after that work finishes.
            var firstWork = children[(parent.TraceId, parent.SpanId)]
                .Where(s => Framework(s) && (perTest ? s.Name == "test body" :
                    s.Name is "test assembly" or "test suite" or "test case"))
                .Select(s => (DateTime?)s.Start).Min();
            if (firstWork is null && parent.Name == "test suite" &&
                firstBodyBySuite.TryGetValue(Suite(parent), out var firstBody))
                firstWork = firstBody;
            if (firstWork is null)
                return null;
            var before = span.Start < firstWork;
            return perTest ? before ? "Per-test setup" : "Per-test teardown"
                : before ? "One-time setup" : "One-time teardown";
        }

        private static string Owner(TestTraceSpan span, ILookup<(string, string), TestTraceSpan> byId)
        {
            // Terminate even if a malformed input file contains a cycle.
            for (var depth = 0; depth < 32 && span is not null; depth++)
            {
                if (Tag(span, "tunit.test.id") is { } id)
                    return id;
                if (Framework(span) && span.Name == "test case")
                    return span.TraceId + "/" + span.SpanId;
                span = byId[(span.TraceId, span.ParentId)].FirstOrDefault();
            }
            return "";
        }

        private static double UnionMs(IEnumerable<(DateTime Start, DateTime End)> intervals)
        {
            var end = DateTime.MinValue;
            var total = 0.0;
            foreach (var interval in intervals.OrderBy(i => i.Start))
            {
                if (interval.End <= end)
                    continue;
                total += (interval.End - Max(end, interval.Start)).TotalMilliseconds;
                end = interval.End;
            }
            return total;
        }

        private static (string Namespace, string Name) Suite(TestTraceSpan span) =>
            (Tag(span, "tunit.class.namespace"), Tag(span, "test.suite.name"));

        private static string TestLabel(TestTraceSpan span) => Tag(span, "test.case.name") is { } name ? " " + name : "";
        private static string Tag(TestTraceSpan span, string key) => span.Tags.FirstOrDefault(pair => pair.Key == key).Value;
        private static bool Framework(TestTraceSpan span) => span.Source is "TUnit" or "TUnit.Lifecycle";
        private static bool IsProcess(TestTraceSpan span, string name) => span.Source == "Antiphon.Tests" && span.Name == name;
        private static DateTime End(TestTraceSpan span) => span.Start.AddMilliseconds(span.DurationMs);
        private static DateTime Max(DateTime x, DateTime y) => x > y ? x : y;
    }
}
