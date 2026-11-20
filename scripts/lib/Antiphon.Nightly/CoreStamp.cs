// The loader's source fingerprint (nightly-core.ps1): names, lengths and write times of the files the core's build
// hash covers. A process that already accepted a core compares it before every use, so a checkout that changes
// under a long-lived session is re-checked (and a mismatch refused). Computed here because the PowerShell walk cost
// about 9 ms a call, and the check runs before every ported call.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Enumeration;
using System.Security.Cryptography;
using System.Text;

namespace Antiphon.Nightly
{
    public static class NightlyCoreStamp
    {
        // The path below the project root with forward slashes, as the build identity names it.
        private static string Relative(string full, int root) => full.Substring(root).Replace('\\', '/').TrimStart('/');

        /// <summary>
        /// SHA-256 (upper-case hex) of the ordinally sorted "Project/relative:length:ticks" lines, one per .cs or .csproj
        /// file under each project (case-insensitive), bin/ and obj/ at the project root excluded; "" when a project
        /// directory is missing.
        /// </summary>
        public static string Compute(string[] projects)
        {
            var parts = new List<string>();
            foreach (var project in projects ?? new string[0])
            {
                if (string.IsNullOrEmpty(project) || !Directory.Exists(project)) return "";
                var name = Path.GetFileName(project);
                var root = project.Length;
                // One enumeration: each entry already carries its length and write time, and bin/ and obj/ at the
                // project root are never entered.
                var files = new FileSystemEnumerable<string>(project,
                    (ref FileSystemEntry entry) => name + "/" + Relative(entry.ToFullPath(), root) + ":"
                        + entry.Length.ToString(CultureInfo.InvariantCulture) + ":" + entry.LastWriteTimeUtc.UtcTicks.ToString(CultureInfo.InvariantCulture),
                    new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = 0, IgnoreInaccessible = false, MatchType = MatchType.Win32 })
                {
                    ShouldIncludePredicate = (ref FileSystemEntry entry) => !entry.IsDirectory
                        && (entry.FileName.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) || entry.FileName.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)),
                    ShouldRecursePredicate = (ref FileSystemEntry entry) =>
                    {
                        var rel = Relative(entry.ToFullPath(), root);
                        return !rel.Equals("bin", StringComparison.OrdinalIgnoreCase) && !rel.Equals("obj", StringComparison.OrdinalIgnoreCase);
                    },
                };
                parts.AddRange(files);
            }
            parts.Sort(StringComparer.Ordinal);
            using (var sha = SHA256.Create())
            {
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(string.Join("\n", parts)))).Replace("-", "");
            }
        }
    }
}
