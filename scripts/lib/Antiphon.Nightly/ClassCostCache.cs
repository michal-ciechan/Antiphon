// The parsed nightly-class-costs.json, kept once per process.
//
// Get-NightlyClassCostTable (nightly-tests-impl.ps1) and Get-TestPhaseClassBasisMap (test-phase-evidence.ps1)
// both read the same 386 KB file, and a process that loads the libraries more than once or prices several
// suites parsed it again each time. The PowerShell functions still parse it (ConvertFrom-Json) and build their
// tables, so values, key comparers and errors are theirs; this type only remembers the results. An entry is
// served only while the file at that full path still has the digest it had when parsed
// (NightlyClassNames.CachedFileSha256: a stat while the file is settled, a fresh hash otherwise), and is
// stored only when the digest was the same before and after the read. Callers hand out copies of the tables.
using System;
using System.Collections;
using System.Collections.Generic;

namespace Antiphon.Nightly
{
    /// <summary>One parsed class-cost file: the document and the tables built from it so far.</summary>
    public sealed class ClassCostEntry
    {
        /// <summary>Lower-case hex SHA-256 of the file the document was parsed from.</summary>
        public string Digest;
        /// <summary>What ConvertFrom-Json returned for the file's text (may be null).</summary>
        public object Document;
        /// <summary>Get-NightlyClassCostTable's table, or null until it has been built without an error.</summary>
        public Hashtable Costs;
        /// <summary>Get-TestPhaseClassBasisMap's map, or null until it has been built.</summary>
        public Hashtable Basis;
    }

    public static class ClassCostCache
    {
        private static readonly object Gate = new object();
        private static readonly Dictionary<string, ClassCostEntry> Entries = new Dictionary<string, ClassCostEntry>(StringComparer.Ordinal);

        /// <summary>The file's current digest (the value to pass to Store after parsing).</summary>
        public static string Digest(string fullPath)
        {
            return NightlyClassNames.CachedFileSha256(fullPath);
        }

        /// <summary>The entry parsed from this path, when the file still has that digest; otherwise null.</summary>
        public static ClassCostEntry Find(string fullPath)
        {
            ClassCostEntry entry;
            lock (Gate)
            {
                if (!Entries.TryGetValue(fullPath, out entry)) return null;
            }
            if (string.Equals(entry.Digest, Digest(fullPath), StringComparison.Ordinal)) return entry;
            lock (Gate)
            {
                ClassCostEntry current;
                if (Entries.TryGetValue(fullPath, out current) && ReferenceEquals(current, entry)) Entries.Remove(fullPath);
            }
            return null;
        }

        /// <summary>
        /// An entry for a document parsed from the file while it had digestBefore. It is remembered only when
        /// the file still has that digest now (it did not change while it was read); the entry is returned
        /// either way.
        /// </summary>
        public static ClassCostEntry Store(string fullPath, string digestBefore, object document)
        {
            var entry = new ClassCostEntry { Digest = digestBefore, Document = document };
            var unchanged = string.Equals(digestBefore, Digest(fullPath), StringComparison.Ordinal);
            lock (Gate)
            {
                if (unchanged) Entries[fullPath] = entry;
                else Entries.Remove(fullPath);
            }
            return entry;
        }

        /// <summary>Drops every entry (tests).</summary>
        public static void Clear()
        {
            lock (Gate) Entries.Clear();
        }
    }
}
