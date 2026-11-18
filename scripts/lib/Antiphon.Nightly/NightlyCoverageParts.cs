// Per-chunk pieces of the native suite verdict (nightly-coverage.ps1), compiled. Each returns what the named
// PowerShell code computed, or null where it cannot vouch for the same answer; the caller then runs its
// PowerShell body, which stays as the fallback.
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
        /// Get-NightlyLatestDiagnosticLog for an existing directory: the newest .diag file, else the newest file
        /// (by LastWriteTimeUtc). "" when the directory holds no files. Null wherever Get-ChildItem | Sort-Object
        /// could answer differently (a hidden or system file, two candidates with the same time, an unreadable
        /// directory): the caller then runs its PowerShell body.
        /// </summary>
        public static string LatestDiagnosticLog(string directory)
        {
            try
            {
                var files = new DirectoryInfo(directory).GetFiles();
                if (files.Length == 0) return "";
                FileInfo newestDiag = null, newestAny = null;
                bool diagTie = false, anyTie = false;
                foreach (var file in files)
                {
                    if ((file.Attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0) return null;
                    Newest(file, ref newestAny, ref anyTie);
                    if (string.Equals(file.Extension, ".diag", StringComparison.OrdinalIgnoreCase)) Newest(file, ref newestDiag, ref diagTie);
                }
                if (newestDiag != null) return diagTie ? null : newestDiag.FullName;
                return anyTie ? null : newestAny.FullName;
            }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
        }

        private static void Newest(FileInfo file, ref FileInfo newest, ref bool tie)
        {
            if (newest == null || file.LastWriteTimeUtc > newest.LastWriteTimeUtc) { newest = file; tie = false; }
            else if (file.LastWriteTimeUtc == newest.LastWriteTimeUtc) tie = true;
        }
    }
}
