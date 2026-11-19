// Compiled file checks of test-phase-evidence.ps1: the path identity taken before every evidence read and
// write, and the bounded, deadline-limited read used for sidecars, TRX and JSONL files. PathIdentity and
// BoundedRead return what their PowerShell functions returned on Windows; Kind is the same identity on both
// platforms (lstat on Linux) for the compiled readers (QualificationTiming).
using System;
using System.Diagnostics;
using System.IO;
using System.Management.Automation;
using System.Runtime.InteropServices;
using Antiphon.TestPhase;

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
            string kind = Kind(path, out long size);
            return Identity(kind, size);
        }

        /// <summary>
        /// PathIdentity's kind and size without the wrapper object. On Linux: lstat, 'regular' for a regular
        /// file, 'unsupported' for anything else (a link, FIFO, directory), 'missing' for ENOENT.
        /// </summary>
        internal static string Kind(string path, out long size)
        {
            size = 0;
            if (string.IsNullOrWhiteSpace(path)) return "missing";
            long queued = EvidenceFiles.PendingLength(path);
            if (queued >= 0) { size = queued; return "regular"; }
            if (!OperatingSystem.IsWindows()) return LinuxKind(path, out size);
            return WindowsKind(path, out size);
        }

        private static string LinuxKind(string path, out long size)
        {
            size = 0;
            if (FileIdentity.LStat(path, out LinuxStat stat) != 0) return Marshal.GetLastWin32Error() == 2 ? "missing" : "unreadable";
            size = stat.Size;
            return (stat.Mode & 0xF000) == 0x8000 ? "regular" : "unsupported";
        }

        private static string WindowsKind(string path, out long size)
        {
            size = 0;
            // A file (the usual answer): existence, attributes and length from one look at it. Anything else
            // (a directory, nothing there, a path File.Exists refuses such as one ending in a separator, an
            // error) is answered by the calls below, as before.
            try
            {
                var info = new FileInfo(path);
                string full = info.FullName;
                char last = full.Length > 0 ? full[full.Length - 1] : (char)0;
                if (last != Path.DirectorySeparatorChar && last != Path.AltDirectorySeparatorChar && info.Exists)
                {
                    FileAttributes seen = info.Attributes;
                    if ((seen & FileAttributes.Directory) == 0)
                    {
                        if ((seen & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint) return "unsupported";
                        if ((seen & (FileAttributes.Hidden | FileAttributes.System)) == 0) size = info.Length;
                        return "regular";
                    }
                }
            }
            catch { }
            if (!File.Exists(path) && !Directory.Exists(path)) return "missing";
            FileAttributes attrs;
            try { attrs = File.GetAttributes(path); }
            catch { return "unreadable"; }
            if ((attrs & FileAttributes.Directory) == FileAttributes.Directory) return "unsupported";
            if ((attrs & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint) return "unsupported";
            if ((attrs & (FileAttributes.Hidden | FileAttributes.System)) == 0)
            {
                try { size = new FileInfo(path).Length; } catch { size = 0; }
            }
            return "regular";
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
        /// is false when bytes were left or the deadline passed. Refusals carry the identity kind, 'budget',
        /// 'unreadable', or 'deferred-write-failed'. A queued evidence write is waited on first. A failed write
        /// refuses and leaves the failure stored, so the read is not the previous bytes reported as complete.
        /// </summary>
        public static PSObject BoundedRead(string path, int maxBytes, int deadlineMilliseconds, Stopwatch watch)
        {
            if (maxBytes < 1) maxBytes = 1;
            if (deadlineMilliseconds < 1) deadlineMilliseconds = 1;
            if (watch == null) watch = Stopwatch.StartNew();
            string kind = Kind(path, out _);
            if (kind != "regular") return Read(null, false, true, kind);
            if (watch.ElapsedMilliseconds >= deadlineMilliseconds) return Read(null, false, true, "budget");
            EvidenceFiles.Wait(path);
            if (EvidenceFiles.PathFailed(path)) return Read(null, false, true, "deferred-write-failed");
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
