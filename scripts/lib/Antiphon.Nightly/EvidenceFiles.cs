// The file system the nightly evidence scripts read and write through during a nightly run.
//
// Outside a run the PowerShell callers keep their own file calls. During a run (EnableWriteBehind, from
// nightly-tests.ps1) they call these instead, and these go through one Antiphon.IO.WriteBehind file system:
// writes are queued and land on a background thread (each as a temporary file renamed over the target, so
// another process never sees a torn file), and reads, existence and size answer from the queue, so this
// process always reads what it wrote. Anything else (listing a directory, copying, opening a stream) waits for
// the queued writes first. A crash can lose the writes still queued.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO.Abstractions;
using System.Text;
using Antiphon.IO.WriteBehind;

namespace Antiphon.Nightly
{
    public static class EvidenceFiles
    {
        private static readonly object Gate = new object();
        private static readonly IFileSystem Disk = new FileSystem();
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);
        private static WriteBehindFileSystem buffered;
        private static bool exitHooked;

        /// <summary>True between EnableWriteBehind and DisableWriteBehind.</summary>
        public static bool Buffered
        {
            get { lock (Gate) return buffered != null; }
        }

        private static IFileSystem Fs
        {
            get { lock (Gate) return (IFileSystem)buffered ?? Disk; }
        }

        public static void EnableWriteBehind()
        {
            lock (Gate)
            {
                if (buffered != null) return;
                buffered = new WriteBehindFileSystem(new FileSystem());
                if (!exitHooked)
                {
                    exitHooked = true;
                    AppDomain.CurrentDomain.ProcessExit += (sender, args) => Flush();
                }
            }
        }

        /// <summary>Lands every queued write and returns to direct disk access; returns the writes that failed.</summary>
        public static string[] DisableWriteBehind()
        {
            WriteBehindFileSystem fs;
            lock (Gate) fs = buffered;
            if (fs == null) return new string[0];
            string[] failed = Flush();
            lock (Gate) buffered = null;
            try { fs.Dispose(); } catch (AggregateException) { }
            return failed;
        }

        /// <summary>Waits until every queued write is on disk; returns the writes that failed since the last flush.</summary>
        public static string[] Flush()
        {
            WriteBehindFileSystem fs;
            lock (Gate) fs = buffered;
            if (fs == null) return new string[0];
            try { fs.Flush(); return new string[0]; }
            catch (AggregateException ex) { return Describe(ex); }
        }

        /// <summary>Waits until one path's queued write is on disk; returns the writes that failed.</summary>
        public static string[] Flush(string path)
        {
            WriteBehindFileSystem fs;
            lock (Gate) fs = buffered;
            if (fs == null || string.IsNullOrEmpty(path)) return new string[0];
            try { fs.Flush(System.IO.Path.GetFullPath(path)); return new string[0]; }
            catch (AggregateException ex) { return Describe(ex); }
        }

        private static string[] Describe(AggregateException ex)
        {
            var failed = new List<string>();
            foreach (Exception inner in ex.InnerExceptions)
            {
                var write = inner as WriteBehindException;
                failed.Add(write == null ? inner.Message : write.Path + ": " + write.InnerException.Message);
            }
            return failed.ToArray();
        }

        // ---- Reads and checks: queued content first --------------------------------------------------------

        /// <summary>Test-Path -LiteralPath: a file (queued or on disk) or a directory.</summary>
        public static bool PathExists(string path) =>
            !string.IsNullOrEmpty(path) && (Fs.File.Exists(path) || Fs.Directory.Exists(path));

        /// <summary>Test-Path -LiteralPath -PathType Leaf.</summary>
        public static bool FileExists(string path) => !string.IsNullOrEmpty(path) && Fs.File.Exists(path);

        /// <summary>Test-Path -LiteralPath -PathType Container.</summary>
        public static bool DirectoryExists(string path) => !string.IsNullOrEmpty(path) && Fs.Directory.Exists(path);

        /// <summary>[IO.File]::ReadAllText: UTF-8 unless a byte-order mark says otherwise.</summary>
        public static string ReadAllText(string path) => Fs.File.ReadAllText(path);

        public static string ReadAllText(string path, Encoding encoding) => Fs.File.ReadAllText(path, encoding);

        public static byte[] ReadAllBytes(string path) => Fs.File.ReadAllBytes(path);

        public static string[] ReadAllLines(string path) => Fs.File.ReadAllLines(path);

        /// <summary>Bytes of the path's queued write that has not landed, or -1 when none is queued.</summary>
        public static long PendingLength(string path)
        {
            WriteBehindFileSystem fs;
            lock (Gate) fs = buffered;
            if (fs == null || string.IsNullOrEmpty(path)) return -1;
            long? length = fs.PendingLength(System.IO.Path.GetFullPath(path));
            return length ?? -1;
        }

        /// <summary>Get-NightlyFileSha256 / Get-TestPhaseSha256File: lower-case hex SHA-256 of the file's bytes.</summary>
        public static string Sha256(string path)
        {
            byte[] bytes = Fs.File.ReadAllBytes(path);
            using (var sha = System.Security.Cryptography.SHA256.Create())
            {
                var sb = new StringBuilder(64);
                foreach (byte b in sha.ComputeHash(bytes)) sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
                return sb.ToString();
            }
        }

        // ---- Writes: queued ----------------------------------------------------------------------------------

        /// <summary>[IO.File]::WriteAllText(path, text) / with UTF8Encoding($false): UTF-8 without a byte-order mark.</summary>
        public static void WriteAllText(string path, string text) => Fs.File.WriteAllText(path, text ?? string.Empty);

        /// <summary>[IO.File]::WriteAllLines(path, lines, UTF8Encoding($false)).</summary>
        public static void WriteAllLines(string path, string[] lines) => Fs.File.WriteAllLines(path, lines ?? new string[0], Utf8NoBom);

        /// <summary>
        /// Write-TestPhaseTextAtomic / Write-NightlyAtomicJson: creates the directory, then writes the whole
        /// file so a reader sees the old or the new text. skipLinks: an existing directory or link is left
        /// alone (Write-TestPhaseTextAtomic); otherwise such a path throws.
        /// </summary>
        public static void WriteAtomic(string path, string text, bool skipLinks)
        {
            string full = System.IO.Path.GetFullPath(path);
            if (skipLinks && PendingLength(full) < 0)
            {
                try
                {
                    System.IO.FileAttributes attrs = System.IO.File.GetAttributes(full);
                    if ((attrs & (System.IO.FileAttributes.Directory | System.IO.FileAttributes.ReparsePoint)) != 0) return;
                }
                catch (System.IO.FileNotFoundException) { }
                catch (System.IO.DirectoryNotFoundException) { }
                catch (UnauthorizedAccessException) { return; }
                catch (System.IO.IOException) { return; }
            }
            string dir = System.IO.Path.GetDirectoryName(full);
            if (!string.IsNullOrEmpty(dir) && !System.IO.Directory.Exists(dir)) System.IO.Directory.CreateDirectory(dir);
            if (Buffered)
            {
                Fs.File.WriteAllText(full, text ?? string.Empty);
                return;
            }
            string temp = full + "." + Guid.NewGuid().ToString("N") + ".tmp";
            System.IO.File.WriteAllText(temp, text ?? string.Empty, Utf8NoBom);
            System.IO.File.Move(temp, full, true);
        }
    }
}
