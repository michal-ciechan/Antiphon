// Per-chunk pieces of the native suite verdict (nightly-coverage.ps1), compiled. Each returns what the named
// PowerShell code computed; the PowerShell copies were removed (the core is required, 2026-11-13).
using System;
using System.Collections.Generic;
using System.IO;
using System.Management.Automation;

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
            var property = PSObject.AsPSObject(entry).Properties["reasonClass"];
            if (property != null) value = PsValue.Str(property.Value);
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
        /// LastWriteTimeUtc; on equal times the first in directory order (as Sort-Object -Descending kept the
        /// listing order). Hidden and system files are skipped, as Get-ChildItem without -Force skips them. ""
        /// when there is no such file or the directory cannot be listed (Get-ChildItem -ErrorAction
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
                    if (newestAny == null || file.LastWriteTimeUtc > newestAny.LastWriteTimeUtc) newestAny = file;
                    if (string.Equals(file.Extension, ".diag", StringComparison.OrdinalIgnoreCase)
                        && (newestDiag == null || file.LastWriteTimeUtc > newestDiag.LastWriteTimeUtc)) newestDiag = file;
                }
                if (newestDiag != null) return newestDiag.FullName;
                return newestAny == null ? "" : newestAny.FullName;
            }
            catch (IOException) { return ""; }
            catch (UnauthorizedAccessException) { return ""; }
        }
    }
}
