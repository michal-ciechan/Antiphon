// Per-chunk pieces of the native suite verdict (nightly-coverage.ps1), compiled. Each returns what the named
// PowerShell code computed; the PowerShell copies were removed (the core is required, 2026-11-13).
using System;
using System.Collections.Generic;
using System.IO;

namespace Antiphon.Nightly
{
    public static class NightlyCoverageParts
    {
        /// <summary>
        /// Get-NightlyExpectedSkipRosterDigest: SHA-256 of the ordinally sorted "platform\ntest\nreasonClass\nreasonPrefix"
        /// lines, one per non-null entry. Null when an entry's reasonClass is invalid: the PowerShell body then
        /// raises its own error.
        /// </summary>
        public static string RosterDigest(object entries)
        {
            var lines = new List<string>();
            if (entries != null)
            {
                foreach (var entry in PsValue.AsArray(entries))
                {
                    if (entry == null) continue;
                    var reasonClass = ReasonClass(entry);
                    if (reasonClass == null) return null;
                    lines.Add(PsValue.Str(PsValue.Prop(entry, "platform")) + "\n" + PsValue.Str(PsValue.Prop(entry, "test")) + "\n"
                        + reasonClass + "\n" + PsValue.Str(PsValue.Prop(entry, "reasonPrefix")));
                }
            }
            var sorted = lines.ToArray();
            Array.Sort(sorted, StringComparer.Ordinal);
            return TestPhaseRecords.Sha256Text(sorted.Length > 0 ? string.Join("\n", sorted) : "");
        }

        // Get-NightlyExpectedSkipReasonClass, or null where it throws.
        private static string ReasonClass(object entry)
        {
            var value = "";
            var property = PsValue.Property(entry, "reasonClass", out var found);
            if (found) value = PsValue.Str(property);
            if (string.IsNullOrWhiteSpace(value)) return "platform";
            if (value != "platform" && value != "live-context") return null;
            return value;
        }

        /// <summary>@($rows | Where-Object { $null -ne $_ }).Count.</summary>
        public static int PresentCount(object[] rows)
        {
            if (rows == null) return 0;
            var count = 0;
            foreach (var row in rows) if (row != null) count++;
            return count;
        }

        /// <summary>
        /// Get-NightlyLatestDiagnosticLog for an existing directory: the newest .diag file, else the newest file, by
        /// LastWriteTimeUtc. Equal timestamps take the ordinally first full path. The removed body
        /// (95d43ee27da2 nightly-coverage.ps1) listed with Get-ChildItem, which on NTFS is name order and omits
        /// Hidden and System, then Sort-Object LastWriteTimeUtc -Descending, which keeps that order for equal
        /// times, so a.diag wins a tie with b.diag. Directory enumeration order is not that listing on Linux.
        /// "" when there is no such file or the directory cannot be listed (Get-ChildItem -ErrorAction
        /// SilentlyContinue listed nothing).
        /// </summary>
        public static string LatestDiagnosticLog(string directory)
        {
            try
            {
                FileInfo newestDiag = null, newestAny = null;
                foreach (var file in new DirectoryInfo(directory).GetFiles())
                {
                    if ((file.Attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0) continue;
                    if (PreferLatest(file, newestAny)) newestAny = file;
                    if (string.Equals(file.Extension, ".diag", StringComparison.OrdinalIgnoreCase)
                        && PreferLatest(file, newestDiag)) newestDiag = file;
                }
                if (newestDiag != null) return newestDiag.FullName;
                return newestAny == null ? "" : newestAny.FullName;
            }
            catch (IOException) { return ""; }
            catch (UnauthorizedAccessException) { return ""; }
        }

        // Equal LastWriteTimeUtc: the ordinally smaller full path. Same directory prefix, so this is name order.
        private static bool PreferLatest(FileInfo candidate, FileInfo current)
        {
            if (current == null) return true;
            var byTime = candidate.LastWriteTimeUtc.CompareTo(current.LastWriteTimeUtc);
            if (byTime != 0) return byTime > 0;
            return string.Compare(candidate.FullName, current.FullName, StringComparison.Ordinal) < 0;
        }
    }
}
