// Bounded evidence for the failed tests in explicitly named TRX files (the rc qualification skill's step 3). It replaced
// scripts/orchestration/trx-failures.ps1 (unit 25 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md); the CLI
// runs it as `antiphon-scripts trx-failures <a.trx> [<b.trx> ...]` (scripts/antiphon on sh, scripts\antiphon.cmd on Windows).
//
// For each file, each Failed, Error, Timeout or Aborted result (the failure set of TrxReport and WatchQual) prints, in order:
//   FILE <path as given>
//   TEST <testName>
//   DURATION <duration>
//   MESSAGE <the first 1500 characters of the message>
//   STACK (first 3 frames)
//   <up to three "at ..." lines of the stack, as written; the first three non-blank lines when none starts with "at">
//   STDOUT <the first 2500 characters of the result's stdout>
// A file that cannot be read or is not a TeamTest 2010 TestRun prints "TRX-READ-ERROR <path>: <reason>" and the next file is
// read; no path at all prints "TRX-READ-ERROR no input paths supplied.". "no failures" is printed only when every file was
// read and none had a failed result. A diagnostic helper: it grants no test or release credit and always answers exit 0.
// Each prefix is taken of the same string it is measured on. Plain C#: no PowerShell types. ASCII only.
#nullable disable
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.XPath;

namespace Antiphon.Nightly
{
    /// <summary>The trx-failures command (trx-failures.ps1).</summary>
    public static class TrxFailures
    {
        /// <summary>The CLI's usage line.</summary>
        public const string Usage = "trx-failures <a.trx> [<b.trx> ...]";

        /// <summary>The message characters shown.</summary>
        public const int MessageLimit = 1500;

        /// <summary>The stdout characters shown.</summary>
        public const int StdoutLimit = 2500;

        private static readonly Regex Frame = new Regex(@"^at\s+", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        private static readonly Regex LineBreak = new Regex(@"\r?\n", RegexOptions.CultureInvariant);

        /// <summary>
        /// The paths after the command name: every argument is a path. A "--" flag is a NightlyUsageException (the script
        /// took its paths positionally).
        /// </summary>
        public static IReadOnlyList<string> Parse(IReadOnlyList<string> arguments)
        {
            var paths = (arguments ?? Array.Empty<string>()).Select(argument => argument ?? "").ToArray();
            foreach (var path in paths)
                if (path.StartsWith("--", StringComparison.Ordinal)) throw new NightlyUsageException("trx-failures takes TRX paths only, not " + path);
            return paths;
        }

        /// <summary>Prints the failures of each file on host.Out; always 0.</summary>
        public static int Run(IReadOnlyList<string> paths, NightlyHost host, IFileSystem files)
        {
            if (host == null) throw new ArgumentNullException(nameof(host));
            if (files == null) throw new ArgumentNullException(nameof(files));
            var count = 0;
            var readErrors = 0;
            var list = paths ?? Array.Empty<string>();
            if (list.Count == 0)
            {
                host.Out.WriteLine("TRX-READ-ERROR no input paths supplied.");
                readErrors++;
            }
            foreach (var path in list)
            {
                List<string> lines;
                try
                {
                    lines = Failures(path, files.File.ReadAllText(path), out var failed);
                    count += failed;
                }
                catch (Exception e) when (e is XmlException || e is XPathException || e is InvalidDataException || e is IOException
                    || e is UnauthorizedAccessException || e is ArgumentException || e is NotSupportedException)
                {
                    readErrors++;
                    host.Out.WriteLine("TRX-READ-ERROR " + path + ": " + e.Message);
                    continue;
                }
                foreach (var line in lines) host.Out.WriteLine(line);
            }
            if (count == 0 && readErrors == 0) host.Out.WriteLine("no failures");
            return 0;
        }

        /// <summary>
        /// The evidence lines of one TRX text, the file shown as path; failed is the number of failed results. Throws
        /// XmlException for a document that is not well formed and InvalidDataException for one that is not a TeamTest 2010
        /// TestRun.
        /// </summary>
        public static List<string> Failures(string path, string text, out int failed)
        {
            var document = new XmlDocument { XmlResolver = null };
            using (var reader = XmlReader.Create(new StringReader(text ?? ""), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null }))
                document.Load(reader);
            var ns = new XmlNamespaceManager(document.NameTable);
            ns.AddNamespace("t", WatchQual.TrxNamespace);
            var root = document.DocumentElement;
            if (root == null || root.LocalName != "TestRun" || root.NamespaceURI != WatchQual.TrxNamespace) throw new InvalidDataException("Not a supported TRX TestRun.");
            var lines = new List<string>();
            failed = 0;
            foreach (XmlElement result in document.SelectNodes(WatchQual.FailedResults, ns))
            {
                failed++;
                var message = result.SelectSingleNode("t:Output/t:ErrorInfo/t:Message", ns);
                var stack = result.SelectSingleNode("t:Output/t:ErrorInfo/t:StackTrace", ns);
                var stdout = result.SelectSingleNode("t:Output/t:StdOut", ns);
                lines.Add("FILE " + path);
                lines.Add("TEST " + result.GetAttribute("testName"));
                lines.Add("DURATION " + result.GetAttribute("duration"));
                lines.Add("MESSAGE " + Prefix(message?.InnerText ?? "", MessageLimit));
                lines.Add("STACK (first 3 frames)");
                if (stack != null) lines.AddRange(Frames(stack.InnerText));
                lines.Add("STDOUT " + Prefix(stdout?.InnerText ?? "", StdoutLimit));
            }
            return lines;
        }

        /// <summary>The first three ".NET" frames (lines starting "at" once trimmed), else the first three non-blank lines; as written.</summary>
        public static IEnumerable<string> Frames(string stack)
        {
            var all = LineBreak.Split(stack ?? "");
            var frames = all.Where(line => Frame.IsMatch(line.Trim())).ToArray();
            // Keep evidence of a stack in another format rather than discarding it.
            if (frames.Length == 0) frames = all.Where(line => line.Trim().Length > 0).ToArray();
            return frames.Take(3);
        }

        /// <summary>The first limit characters of text, measured on text itself.</summary>
        public static string Prefix(string text, int limit) => text.Substring(0, Math.Min(limit, text.Length));
    }
}
