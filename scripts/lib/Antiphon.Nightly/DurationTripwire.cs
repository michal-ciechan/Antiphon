// CARD-0110 S6 / CARD-0475 S6: report the Antiphon.Tests TRX rows of 5 s or more whose class is not on the slow-test
// allowlist. It replaced scripts/test-duration-tripwire.ps1, deleted 2026-11-24 (unit 33 of
// docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md); the CLI runs it as
//
//   antiphon-scripts test-duration-tripwire --trx <run.trx> [--allowlist <file>]
//
// (scripts/antiphon test-duration-tripwire on sh, scripts\antiphon.cmd test-duration-tripwire on Windows). The allowlist
// defaults to the checkout's tests/Antiphon.Tests/slow-tests-allowlist.txt: one class per line, blank lines and lines that
// start with '#' ignored, each matching a row's simple or fully-qualified class name exactly (ignoring case). A row's class
// is the one its UnitTestResult testId joins to (TestDefinitions/UnitTest/TestMethod@className), never the display name;
// test ids match ignoring case and a later definition of the same id wins, as the script's hashtable did. A row whose
// definition, class or method is missing is unresolved and cannot be exempted.
//
// Output (host.Out): "SLOW-TEST TRIPWIRE: expanded rows=<n>", one "CLASS <class>: expanded=<n> slow-rows=<n>
// body-seconds=<s.sss> (summed TRX body time, not wall-clock duration)" line per class (classes merged ignoring case, the
// unresolved rows under "<unresolved>", ordered ignoring case), the unresolved rows, then either "SLOW-TEST TRIPWIRE: 0
// unlisted tests >= 5s" or the count and one "<seconds>s  <identity>  <display>" line per unlisted slow row. Numbers are
// invariant culture. Exit 0 clean; 1 an unlisted slow row or an unresolved identity; 2 invalid input ("SLOW-TEST TRIPWIRE:
// invalid input: ..." on host.Out: a missing TRX or allowlist, malformed XML, no results, a missing, unparsable or negative
// duration). Usage errors are NightlyUsageException (the CLI's exit 2). Plain C#: no PowerShell types. ASCII only.
#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Xml;

namespace Antiphon.Nightly
{
    /// <summary>The test-duration-tripwire command's options (test-duration-tripwire.ps1 -Trx -Allowlist).</summary>
    public sealed class DurationTripwireOptions
    {
        /// <summary>The CLI's usage line.</summary>
        public const string Usage = "test-duration-tripwire --trx <run.trx> [--allowlist <file>]";

        /// <summary>The TRX to read.</summary>
        public string Trx { get; init; } = "";

        /// <summary>The allowlist to read.</summary>
        public string Allowlist { get; init; } = "";

        /// <summary>The checkout's allowlist (tests/Antiphon.Tests/slow-tests-allowlist.txt).</summary>
        public static string DefaultAllowlist(string repoRoot) =>
            Path.Combine(repoRoot ?? "", "tests", "Antiphon.Tests", "slow-tests-allowlist.txt");

        /// <summary>The flags after the command name; --trx is required, --allowlist defaults to the checkout's.</summary>
        public static DurationTripwireOptions Parse(IReadOnlyList<string> arguments, string repoRoot)
        {
            var flags = NightlyArgs.Parse(arguments, new[] { "--trx", "--allowlist" }, Array.Empty<string>());
            return new DurationTripwireOptions
            {
                Trx = flags.Required("--trx"),
                Allowlist = flags.Value("--allowlist", "") is { Length: > 0 } given ? given : DefaultAllowlist(repoRoot),
            };
        }
    }

    /// <summary>The slow-test tripwire (test-duration-tripwire.ps1).</summary>
    public static class DurationTripwire
    {
        /// <summary>A row at or over this many seconds is slow.</summary>
        public const double SlowSeconds = 5;

        private const string Prefix = "SLOW-TEST TRIPWIRE: ";

        private sealed class ClassStats
        {
            public int Expanded;
            public int Slow;
            public double BodySeconds;
        }

        private sealed class Definition
        {
            public string ClassName;
            public string MethodName;
        }

        /// <summary>Reads the TRX and the allowlist and prints the report; see the header for the exit codes.</summary>
        public static int Run(DurationTripwireOptions options, NightlyHost host, IFileSystem files)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (host == null) throw new ArgumentNullException(nameof(host));
            if (files == null) throw new ArgumentNullException(nameof(files));
            var output = host.Out;
            int Invalid(string message)
            {
                output.WriteLine(Prefix + "invalid input: " + message);
                return 2;
            }
            if (!Exists(files, options.Trx)) return Invalid("missing path: " + options.Trx);
            if (!Exists(files, options.Allowlist)) return Invalid("missing allowlist: " + options.Allowlist);

            var entries = files.File.ReadAllLines(options.Allowlist).Select(line => line.Trim())
                .Where(line => line.Length > 0 && !line.StartsWith("#", StringComparison.Ordinal)).ToArray();

            var doc = new XmlDocument { XmlResolver = null };
            try
            {
                using var reader = XmlReader.Create(new StringReader(files.File.ReadAllText(options.Trx)),
                    new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
                doc.Load(reader);
            }
            catch (XmlException e)
            {
                return Invalid("malformed XML: " + e.Message);
            }
            if (doc.DocumentElement == null) return Invalid("malformed XML: no root element");

            var ns = new XmlNamespaceManager(doc.NameTable);
            var uri = doc.DocumentElement.NamespaceURI;
            if (uri.Length > 0) ns.AddNamespace("t", uri);
            XmlNodeList Select(string xpath) => uri.Length > 0 ? doc.SelectNodes(xpath, ns) : doc.SelectNodes(xpath.Replace("t:", ""));

            var definitions = new Dictionary<string, Definition>(StringComparer.OrdinalIgnoreCase);
            foreach (XmlElement unit in Select("//t:TestDefinitions/t:UnitTest").OfType<XmlElement>())
            {
                var id = unit.GetAttribute("id");
                if (id.Length == 0) continue;
                var method = unit.ChildNodes.OfType<XmlElement>().FirstOrDefault(child => child.LocalName == "TestMethod");
                definitions[id] = new Definition { ClassName = method?.GetAttribute("className"), MethodName = method?.GetAttribute("name") };
            }

            var results = Select("//t:Results/t:UnitTestResult").OfType<XmlElement>().ToList();
            if (results.Count == 0) results = Select("//t:UnitTestResult").OfType<XmlElement>().ToList();
            if (results.Count == 0) return Invalid("empty results");

            var hits = new List<string>();
            var unresolved = new List<string>();
            var stats = new Dictionary<string, ClassStats>(StringComparer.OrdinalIgnoreCase);
            foreach (var result in results)
            {
                var testId = result.GetAttribute("testId");
                var display = result.GetAttribute("testName");
                var durationText = result.GetAttribute("duration");
                if (string.IsNullOrWhiteSpace(durationText))
                    return Invalid("missing duration for testId=" + testId + " testName=" + display);
                if (!TimeSpan.TryParse(durationText, CultureInfo.InvariantCulture, out var duration))
                    return Invalid("unparsable duration '" + durationText + "' for testId=" + testId + " testName=" + display);
                if (duration.TotalSeconds < 0)
                    return Invalid("negative duration '" + durationText + "' for testId=" + testId + " testName=" + display);

                Definition definition = null;
                if (testId.Length > 0) definitions.TryGetValue(testId, out definition);
                var full = definition == null ? "" : definition.ClassName ?? "";
                var method = definition == null ? "" : definition.MethodName ?? "";
                var simple = string.IsNullOrWhiteSpace(full) ? full : full.Split('.')[^1];
                var identity = full.Length > 0 ? full + "." + method : display;

                var key = full.Length > 0 ? full : "<unresolved>";
                if (!stats.TryGetValue(key, out var classStats)) stats[key] = classStats = new ClassStats();
                classStats.Expanded++;
                classStats.BodySeconds += duration.TotalSeconds;

                var resolved = definition != null && !string.IsNullOrWhiteSpace(full) && !string.IsNullOrWhiteSpace(method);
                if (!resolved) unresolved.Add("unresolved testId=" + testId + " testName=" + display);
                if (duration.TotalSeconds < SlowSeconds) continue;

                classStats.Slow++;
                var allowed = resolved && entries.Any(entry => string.Equals(full, entry, StringComparison.OrdinalIgnoreCase)
                    || (simple.Length > 0 && string.Equals(simple, entry, StringComparison.OrdinalIgnoreCase)));
                if (!allowed) hits.Add(duration.TotalSeconds.ToString("n3", CultureInfo.InvariantCulture) + "s  " + identity + "  " + display);
            }

            output.WriteLine(Prefix + "expanded rows=" + results.Count.ToString(CultureInfo.InvariantCulture));
            foreach (var pair in stats.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase))
                output.WriteLine("CLASS " + pair.Key + ": expanded=" + pair.Value.Expanded.ToString(CultureInfo.InvariantCulture)
                    + " slow-rows=" + pair.Value.Slow.ToString(CultureInfo.InvariantCulture)
                    + " body-seconds=" + pair.Value.BodySeconds.ToString("0.000", CultureInfo.InvariantCulture)
                    + " (summed TRX body time, not wall-clock duration)");
            if (unresolved.Count > 0)
            {
                output.WriteLine(Prefix + unresolved.Count.ToString(CultureInfo.InvariantCulture) + " unresolved identit" + (unresolved.Count == 1 ? "y" : "ies"));
                foreach (var line in unresolved) output.WriteLine(line);
            }
            if (hits.Count == 0 && unresolved.Count == 0)
            {
                output.WriteLine(Prefix + "0 unlisted tests >= 5s");
                return 0;
            }
            if (hits.Count > 0)
            {
                output.WriteLine(Prefix + hits.Count.ToString(CultureInfo.InvariantCulture) + " unlisted tests >= 5s");
                foreach (var line in hits) output.WriteLine(line);
            }
            return 1;
        }

        private static bool Exists(IFileSystem files, string path) =>
            !string.IsNullOrEmpty(path) && files.File.Exists(path);
    }
}
