// Compiled Windows file checks of test-phase-evidence.ps1: the path identity taken before every evidence
// read and write, and the bounded, deadline-limited read used for sidecars, TRX and JSONL files. Each returns
// what its PowerShell function returned (the PowerShell stays the Linux implementation and the fallback).
using System;
using System.Diagnostics;
using System.IO;
using System.Management.Automation;

namespace Antiphon.Nightly
{
    public static class TestPhaseFiles
    {
        private static PSObject Identity(string kind, long size)
        {
            var o = new PSObject();
            o.Properties.Add(new PSNoteProperty("Kind", kind));
            o.Properties.Add(new PSNoteProperty("Size", size));
            return o;
        }

        /// <summary>
        /// Get-TestPhasePathIdentity on Windows: 'missing', 'unreadable', 'unsupported' (directory or link) or
        /// 'regular' with its length. A queued evidence write is a regular file of its queued length. As
        /// Get-Item without -Force, a hidden or system file reports length 0.
        /// </summary>
        public static PSObject PathIdentity(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return Identity("missing", 0);
            long queued = EvidenceFiles.PendingLength(path);
            if (queued >= 0) return Identity("regular", queued);
            if (!File.Exists(path) && !Directory.Exists(path)) return Identity("missing", 0);
            FileAttributes attrs;
            try { attrs = File.GetAttributes(path); }
            catch { return Identity("unreadable", 0); }
            if ((attrs & FileAttributes.Directory) == FileAttributes.Directory) return Identity("unsupported", 0);
            if ((attrs & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint) return Identity("unsupported", 0);
            long length = 0;
            if ((attrs & (FileAttributes.Hidden | FileAttributes.System)) == 0)
            {
                try { length = new FileInfo(path).Length; } catch { length = 0; }
            }
            return Identity("regular", length);
        }

        private static PSObject Read(byte[] bytes, bool complete, bool refused, string kind)
        {
            var o = new PSObject();
            o.Properties.Add(new PSNoteProperty("Bytes", bytes));
            o.Properties.Add(new PSNoteProperty("Complete", complete));
            o.Properties.Add(new PSNoteProperty("Refused", refused));
            o.Properties.Add(new PSNoteProperty("Kind", kind));
            return o;
        }

        /// <summary>
        /// Read-TestPhaseBoundedBytes on Windows: at most maxBytes, stopping at the deadline on watch; Complete
        /// is false when bytes were left or the deadline passed. Refusals carry the identity kind, 'budget' or
        /// 'unreadable'. A queued evidence write lands first, so the read is of the disk.
        /// </summary>
        public static PSObject BoundedRead(string path, int maxBytes, int deadlineMilliseconds, Stopwatch watch)
        {
            if (maxBytes < 1) maxBytes = 1;
            if (deadlineMilliseconds < 1) deadlineMilliseconds = 1;
            if (watch == null) watch = Stopwatch.StartNew();
            PSObject identity = PathIdentity(path);
            string kind = (string)identity.Properties["Kind"].Value;
            if (kind != "regular") return Read(null, false, true, kind);
            if (watch.ElapsedMilliseconds >= deadlineMilliseconds) return Read(null, false, true, "budget");
            EvidenceFiles.Flush(path);
            FileStream stream;
            try { stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete); }
            catch { return Read(null, false, true, "unreadable"); }
            try
            {
                var buffer = new byte[8192];
                var ms = new MemoryStream();
                bool complete = true;
                while (ms.Length < maxBytes)
                {
                    if (watch.ElapsedMilliseconds >= deadlineMilliseconds) { complete = false; break; }
                    int room = maxBytes - (int)ms.Length;
                    int want = Math.Min(8192, room);
                    int count = stream.Read(buffer, 0, want);
                    if (count <= 0) break;
                    ms.Write(buffer, 0, count);
                }
                if (complete && ms.Length >= maxBytes)
                {
                    if (watch.ElapsedMilliseconds >= deadlineMilliseconds) complete = false;
                    else if (stream.Read(buffer, 0, 1) > 0) complete = false;
                }
                return Read(ms.ToArray(), complete, false, "regular");
            }
            catch { return Read(null, false, true, "unreadable"); }
            finally { stream.Dispose(); }
        }
    }
}
