// CARD-0254: the universal agent context's byte budget. It replaced scripts/check-agent-context.ps1 (unit 25 of
// docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md); the CLI runs it as `antiphon-scripts check-agent-context`
// (scripts/antiphon on sh, scripts\antiphon.cmd on Windows), and scripts/bootstrap-check.ps1 runs it as its agent context
// check.
//
//   check-agent-context [--repo <checkout>]
//
// Reads <checkout>/AGENTS.md (the CLI's default checkout: the one it was built in). Raw bytes are authoritative: character
// and word counts can hide UTF-8 growth. It prints the byte count, the 24,576-byte delivery target and the 30,720-byte hard
// ceiling, then the bytes of each level-two section (from its "## " heading to the next). Exit 0 within the target, after
// "PASS: AGENTS.md is within the 24576-byte delivery target."; 1 with one stderr line when the file is missing, is not
// valid UTF-8, has no level-two section, or is over the ceiling or the target. Plain C#: no PowerShell types. ASCII only.
#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO.Abstractions;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Antiphon.Nightly
{
    /// <summary>The check-agent-context command's options.</summary>
    public sealed class AgentContextCheckOptions
    {
        /// <summary>The CLI's usage line.</summary>
        public const string Usage = "check-agent-context [--repo <checkout>]";

        /// <summary>The checkout whose AGENTS.md is read.</summary>
        public string Repo { get; init; } = "";

        /// <summary>The arguments after the command name; defaultRepo is the CLI's checkout (null: none, and --repo is then required).</summary>
        public static AgentContextCheckOptions Parse(IReadOnlyList<string> arguments, string defaultRepo)
        {
            var flags = NightlyArgs.Parse(arguments, new[] { "--repo" }, Array.Empty<string>());
            var repo = flags.Value("--repo", "");
            if (repo.Length == 0) repo = defaultRepo ?? "";
            if (repo.Length == 0) throw new NightlyUsageException("missing --repo (no checkout found to default to)");
            return new AgentContextCheckOptions { Repo = repo };
        }
    }

    /// <summary>The AGENTS.md byte budget (check-agent-context.ps1).</summary>
    public static class AgentContextCheck
    {
        /// <summary>The delivery target in raw UTF-8 bytes.</summary>
        public const int TargetBytes = 24576;

        /// <summary>The hard ceiling in raw UTF-8 bytes.</summary>
        public const int HardCeilingBytes = 30720;

        private static readonly Regex Section = new Regex("(?m)^## .+$", RegexOptions.CultureInvariant);

        /// <summary>Checks the budget; 0 after the PASS line on host.Out, or 1 after one host.Error line.</summary>
        public static int Run(AgentContextCheckOptions options, NightlyHost host, IFileSystem files)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (host == null) throw new ArgumentNullException(nameof(host));
            if (files == null) throw new ArgumentNullException(nameof(files));
            var path = files.Path.Combine(options.Repo, "AGENTS.md");
            if (!files.File.Exists(path)) return Refuse(host, "AGENTS.md was not found at " + path + ".");
            var bytes = files.File.ReadAllBytes(path);
            var size = bytes.Length;
            var strict = new UTF8Encoding(false, true);
            string text;
            try
            {
                text = strict.GetString(bytes);
            }
            catch (DecoderFallbackException e)
            {
                return Refuse(host, "AGENTS.md is not valid UTF-8: " + e.Message);
            }

            host.Out.WriteLine("AGENTS.md raw UTF-8 bytes: " + Number(size));
            host.Out.WriteLine("Delivery target: <= " + Number(TargetBytes) + " bytes");
            host.Out.WriteLine("Hard ceiling:    <= " + Number(HardCeilingBytes) + " bytes");

            var sections = Section.Matches(text);
            if (sections.Count == 0) return Refuse(host, "AGENTS.md has no level-two sections to report.");
            host.Out.WriteLine("");
            host.Out.WriteLine("Section sizes (raw UTF-8 bytes):");
            foreach (var line in SectionTable(text, sections, strict)) host.Out.WriteLine(line);

            if (size > HardCeilingBytes)
                return Refuse(host, "AGENTS.md is " + Number(size) + " raw UTF-8 bytes: above the " + Number(HardCeilingBytes) + "-byte hard ceiling by "
                    + Number(size - HardCeilingBytes) + " bytes.");
            if (size > TargetBytes)
                return Refuse(host, "AGENTS.md is " + Number(size) + " raw UTF-8 bytes: above the " + Number(TargetBytes) + "-byte delivery target by "
                    + Number(size - TargetBytes) + " bytes (still below the " + Number(HardCeilingBytes) + "-byte hard ceiling).");
            host.Out.WriteLine("PASS: AGENTS.md is within the " + Number(TargetBytes) + "-byte delivery target.");
            return 0;
        }

        // The sections as a two-column table, Section left-aligned and Bytes right-aligned, after a blank line (Format-Table -AutoSize).
        private static IEnumerable<string> SectionTable(string text, MatchCollection sections, Encoding encoding)
        {
            var rows = new List<(string Name, string Bytes)>();
            for (var index = 0; index < sections.Count; index++)
            {
                var start = sections[index].Index;
                var end = index + 1 < sections.Count ? sections[index + 1].Index : text.Length;
                rows.Add((sections[index].Value.Substring(3).Trim(), Number(encoding.GetByteCount(text.Substring(start, end - start)))));
            }
            var nameWidth = Math.Max("Section".Length, rows.Max(row => row.Name.Length));
            var bytesWidth = Math.Max("Bytes".Length, rows.Max(row => row.Bytes.Length));
            yield return "";
            yield return "Section".PadRight(nameWidth) + " " + "Bytes".PadLeft(bytesWidth);
            yield return "-------".PadRight(nameWidth) + " " + "-----".PadLeft(bytesWidth);
            foreach (var row in rows) yield return row.Name.PadRight(nameWidth) + " " + row.Bytes.PadLeft(bytesWidth);
            yield return "";
        }

        private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

        private static int Refuse(NightlyHost host, string message)
        {
            host.Error.WriteLine(message);
            return 1;
        }
    }
}
