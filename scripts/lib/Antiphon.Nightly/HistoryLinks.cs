// The failure history's evidence link check, compiled: a node's identity and the link check (PriorFailureHistory and
// the scripts call HistoryLinkCache.AssertNoLink), the stopped-run seed export's walk and its summary-prefix
// cut (StoppedSeedFiles.cs). These were an Add-Type block compiled in every process that read history, with
// the check itself in PowerShell: about 3 ms a path, 15 s of a seed export over a 5,000-entry run directory.
//
// A node's identity (HistoryNodeIdentity.Read):
//   Windows: one handle query on the node itself (CreateFile with FILE_FLAG_OPEN_REPARSE_POINT, so never a link's
//     target, then GetFileInformationByHandle). Missing on ERROR_FILE_NOT_FOUND or ERROR_PATH_NOT_FOUND, unreadable
//     on any other error. A reparse point is a link, and so is a regular file with more than one name (a hard link:
//     the NumberOfLinks test Get-Item's LinkType makes). Link checks keep the volume serial plus the 64-bit file
//     index. The hash cache (CacheFileIdentity) prefers FileIdInfo's 128-bit id and falls back to that index.
//     Unknown is never an identity.
//   Elsewhere: lstat. S_IFLNK is a link, and so is a regular file whose nlink is above 1; dev plus ino is the identity.
//   forceWindows on a non-Windows host (the WindowsAncestorSwapRefused case): the Windows attribute test from
//     File.GetAttributes, with the identity and the link tests (which Get-Item's LinkType made there) from lstat.
//
// The check (HistoryLinkCache.Check), for each node from the root down to the path: an ancestor above the parent is
// read once and, when it is a directory, its identity cached; the path and its parent are read on every check. A
// missing node passes; an unreadable one refuses with evidence-unreadable; a link, or a cached directory whose
// identity has changed, refuses with evidence-link. Only directories are cached: replacing one changes its identity.
//
// The walk (HistoryLinkCache.Walk): breadth first from the roots, in enumeration order, each entry checked as above.
// The reads for one directory's entries (each entry's parent, then the entry) run in parallel; the checks then run
// in entry order on those reads, so the refusal returned is the one a one-by-one walk meets first.
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Antiphon.TestPhase;
using Microsoft.Win32.SafeHandles;

namespace Antiphon.Nightly
{
    public enum CacheIdentityKind
    {
        Unknown = 0,
        FileIndex64 = 1,
        FileId128 = 2,
    }

    /// <summary>
    /// Identity the hash cache may store. FileId128 is FileIdInfo's 16-byte id plus its volume.
    /// FileIndex64 is the 64-bit file index (or Linux dev/ino) fallback. Unknown is never equal.
    /// </summary>
    public sealed class CacheIdentity
    {
        public CacheIdentityKind Kind { get; private set; }
        public ulong Volume { get; private set; }
        public ulong Index { get; private set; }
        public byte[] FileId { get; private set; }
        public bool Known => Kind != CacheIdentityKind.Unknown;

        public static CacheIdentity Unknown() => new CacheIdentity { Kind = CacheIdentityKind.Unknown };

        public static CacheIdentity FromFileIndex(ulong volume, ulong index) =>
            new CacheIdentity { Kind = CacheIdentityKind.FileIndex64, Volume = volume, Index = index };

        public static CacheIdentity FromFileId(ulong volume, byte[] fileId)
        {
            if (fileId == null || fileId.Length != CacheFileIdentity.FileIdBytes) return Unknown();
            var copy = new byte[CacheFileIdentity.FileIdBytes];
            Buffer.BlockCopy(fileId, 0, copy, 0, CacheFileIdentity.FileIdBytes);
            return new CacheIdentity { Kind = CacheIdentityKind.FileId128, Volume = volume, FileId = copy };
        }

        public static bool Same(CacheIdentity left, CacheIdentity right)
        {
            if (left == null || right == null) return false;
            if (left.Kind == CacheIdentityKind.Unknown || right.Kind == CacheIdentityKind.Unknown) return false;
            if (left.Kind != right.Kind || left.Volume != right.Volume) return false;
            if (left.Kind == CacheIdentityKind.FileId128) return SameBytes(left.FileId, right.FileId);
            return left.Index == right.Index;
        }

        private static bool SameBytes(byte[] left, byte[] right)
        {
            if (left == null || right == null || left.Length != CacheFileIdentity.FileIdBytes || right.Length != left.Length) return false;
            for (int i = 0; i < left.Length; i++) if (left[i] != right[i]) return false;
            return true;
        }
    }

    /// <summary>
    /// Windows cache identity. A successful FileIdInfo query wins. Otherwise the 64-bit index from
    /// GetFileInformationByHandle. Neither query, or a file id that is not 16 bytes, is unknown when
    /// the 64-bit query also failed. TestOverride is the Linux witness seam; production leaves it null.
    /// </summary>
    public static class CacheFileIdentity
    {
        public const int FileIdBytes = 16;

        public static CacheIdentity TestOverride { get; set; }

        public static CacheIdentity Decide(bool fileIdInfoOk, ulong volumeSerial, byte[] fileId,
            bool byHandleOk, uint volumeSerial32, ulong fileIndex)
        {
            if (fileIdInfoOk)
            {
                var id = CacheIdentity.FromFileId(volumeSerial, fileId);
                if (id.Known) return id;
            }
            if (byHandleOk) return CacheIdentity.FromFileIndex(volumeSerial32, fileIndex);
            return CacheIdentity.Unknown();
        }

        public static CacheIdentity Read(string path)
        {
            var forced = TestOverride;
            if (forced != null) return forced;
            var id = HistoryNodeIdentity.Read(path, false);
            if (id == null || id.Missing || id.Unreadable || id.Linked || id.Directory) return CacheIdentity.Unknown();
            return id.CacheId ?? CacheIdentity.Unknown();
        }
    }

    /// <summary>A node's identity: missing, unreadable, a link, a directory, and its device and file ids.</summary>
    public sealed class HistoryNodeId
    {
        public bool Missing { get; internal set; }
        public bool Linked { get; internal set; }
        public bool Directory { get; internal set; }
        public ulong Dev { get; internal set; }
        public ulong Ino { get; internal set; }
        public bool Unreadable { get; internal set; }
        public CacheIdentity CacheId { get; internal set; }

        internal static HistoryNodeId MissingNode() => new HistoryNodeId { Missing = true };

        internal static HistoryNodeId UnreadableNode() => new HistoryNodeId { Unreadable = true };
    }

    public static class HistoryNodeIdentity
    {
        /// <summary>
        /// Tests: read nodes with the Windows attribute test on a non-Windows host (the WindowsAncestorSwapRefused case).
        /// Every check and walk in the process uses it.
        /// </summary>
        public static bool ForceWindows { get; set; }

        private const uint ReadAttributes = 0x80;
        private const uint ShareAll = 7;
        private const uint OpenExisting = 3;
        private const uint BackupSemantics = 0x02000000;
        private const uint OpenReparsePoint = 0x00200000;
        private const uint KindMask = 0xF000;
        private const uint KindLink = 0xA000;
        private const uint KindDirectory = 0x4000;

        // BY_HANDLE_FILE_INFORMATION. Its times are FILETIMEs (two DWORDs, 4-byte aligned): a long there would be
        // 8-byte aligned and shift every later field by 4 bytes.
        [StructLayout(LayoutKind.Sequential)]
        private struct ByHandleInfo
        {
            public uint FileAttributes;
            public System.Runtime.InteropServices.ComTypes.FILETIME CreationTime;
            public System.Runtime.InteropServices.ComTypes.FILETIME LastAccessTime;
            public System.Runtime.InteropServices.ComTypes.FILETIME LastWriteTime;
            public uint VolumeSerialNumber;
            public uint FileSizeHigh;
            public uint FileSizeLow;
            public uint NumberOfLinks;
            public uint FileIndexHigh;
            public uint FileIndexLow;
        }

        [DllImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out ByHandleInfo info);

        // FILE_INFO_BY_HANDLE_CLASS.FileIdInfo. The 128-bit id is the cache identity when this query works.
        private const int FileIdInfoClass = 18;

        [StructLayout(LayoutKind.Sequential)]
        private struct FileIdInfoData
        {
            public ulong VolumeSerialNumber;
            public ulong FileIdLow;
            public ulong FileIdHigh;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetFileInformationByHandleEx(SafeFileHandle handle, int infoClass, out FileIdInfoData info, uint size);

        /// <summary>The node's identity, read on the node itself (never a link's target).</summary>
        public static HistoryNodeId Read(string path, bool forceWindows)
        {
            if (OperatingSystem.IsWindows()) return ReadWindows(path);
            return forceWindows ? ReadForcedWindows(path) : ReadLstat(path);
        }

        private static HistoryNodeId ReadWindows(string path)
        {
            using (var handle = CreateFile(path, ReadAttributes, ShareAll, IntPtr.Zero, OpenExisting, BackupSemantics | OpenReparsePoint, IntPtr.Zero))
            {
                if (handle.IsInvalid) return Failed(Marshal.GetLastWin32Error());
                if (!GetFileInformationByHandle(handle, out var info)) return Failed(Marshal.GetLastWin32Error());
                var attributes = (FileAttributes)info.FileAttributes;
                var id = new HistoryNodeId
                {
                    Linked = (attributes & FileAttributes.ReparsePoint) != 0,
                    Directory = (attributes & FileAttributes.Directory) != 0,
                    Dev = info.VolumeSerialNumber,
                    Ino = ((ulong)info.FileIndexHigh << 32) | info.FileIndexLow,
                };
                if (!id.Linked && !id.Directory && info.NumberOfLinks > 1) id.Linked = true;
                if (!id.Linked && !id.Directory) id.CacheId = WindowsCacheId(handle, info, id.Ino);
                return id;
            }
        }

        // FileIdInfo when the query is available, otherwise the 64-bit index. A failed query is not zero-equal.
        private static CacheIdentity WindowsCacheId(SafeFileHandle handle, ByHandleInfo info, ulong fileIndex)
        {
            bool fileIdOk = false;
            ulong volume = 0;
            byte[] fileId = null;
            try
            {
                if (GetFileInformationByHandleEx(handle, FileIdInfoClass, out var fileIdInfo, (uint)Marshal.SizeOf<FileIdInfoData>()))
                {
                    fileIdOk = true;
                    volume = fileIdInfo.VolumeSerialNumber;
                    fileId = new byte[16];
                    WriteUInt64Le(fileId, 0, fileIdInfo.FileIdLow);
                    WriteUInt64Le(fileId, 8, fileIdInfo.FileIdHigh);
                }
            }
            catch (EntryPointNotFoundException) { }
            catch (DllNotFoundException) { }
            return CacheFileIdentity.Decide(fileIdOk, volume, fileId, true, info.VolumeSerialNumber, fileIndex);
        }

        private static void WriteUInt64Le(byte[] bytes, int at, ulong value)
        {
            for (int i = 0; i < 8; i++) bytes[at + i] = (byte)(value >> (8 * i));
        }

        private static HistoryNodeId Failed(int error) =>
            error == 2 || error == 3 ? HistoryNodeId.MissingNode() : HistoryNodeId.UnreadableNode();

        private static HistoryNodeId ReadForcedWindows(string path)
        {
            FileAttributes attributes;
            try { attributes = File.GetAttributes(path); }
            catch (FileNotFoundException) { return HistoryNodeId.MissingNode(); }
            catch (DirectoryNotFoundException) { return HistoryNodeId.MissingNode(); }
            catch (Exception) { return HistoryNodeId.UnreadableNode(); }
            bool linked = (attributes & FileAttributes.ReparsePoint) != 0;
            bool directory = (attributes & FileAttributes.Directory) != 0;
            if (FileIdentity.LStat(path, out var stat) != 0) return HistoryNodeId.UnreadableNode();
            uint kind = stat.Mode & KindMask;
            if (kind == KindLink) linked = true;
            if (kind == KindDirectory) directory = true;
            if (!linked && !directory && stat.Nlink > 1) linked = true;
            return NodeFromStat(linked, directory, stat.Dev, stat.Ino);
        }

        private static HistoryNodeId ReadLstat(string path)
        {
            if (FileIdentity.LStat(path, out var stat) != 0)
            {
                return Marshal.GetLastWin32Error() == 2 ? HistoryNodeId.MissingNode() : HistoryNodeId.UnreadableNode();
            }
            uint kind = stat.Mode & KindMask;
            var id = NodeFromStat(kind == KindLink, kind == KindDirectory, stat.Dev, stat.Ino);
            // A directory's nlink counts its subdirectories. A file's above 1 is a hard link.
            if (!id.Linked && !id.Directory && stat.Nlink > 1) id.Linked = true;
            if (id.Linked) id.CacheId = null;
            return id;
        }

        private static HistoryNodeId NodeFromStat(bool linked, bool directory, ulong dev, ulong ino)
        {
            var id = new HistoryNodeId { Linked = linked, Directory = directory, Dev = dev, Ino = ino };
            if (!linked && !directory) id.CacheId = CacheIdentity.FromFileIndex(dev, ino);
            return id;
        }
    }

    /// <summary>One check: the refusal (null when none) and the path's own identity from that check's read.</summary>
    public sealed class HistoryLinkCheck
    {
        public string Refusal { get; internal set; }
        public HistoryNodeId Leaf { get; internal set; }
    }

    /// <summary>An entry the walk does not descend into, named as the PowerShell walk named it.</summary>
    public sealed class HistoryWalkFile
    {
        internal HistoryWalkFile(string full)
        {
            FullName = full;
            Name = Path.GetFileName(full);
            DirectoryName = Path.GetDirectoryName(full);
            Extension = Path.GetExtension(full);
        }

        public string FullName { get; }
        public string Name { get; }
        public string DirectoryName { get; }
        public string Extension { get; }
    }

    /// <summary>A walk: the refusal (null when none) and the entries met before it, in walk order.</summary>
    public sealed class HistoryWalk
    {
        public string Refusal { get; internal set; }
        public HistoryWalkFile[] Files { get; internal set; }
    }

    /// <summary>
    /// The link cache of one export or history read ($script:NightlyHistoryLinkCache): the identities of the
    /// directories already checked, by full path.
    /// </summary>
    public sealed class HistoryLinkCache
    {
        // Below this many entries a directory's reads stay on the calling thread.
        private const int ParallelThreshold = 32;

        private static readonly ParallelOptions ReadOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount, 1, 8),
        };

        private readonly Dictionary<string, HistoryNodeId> _directories = new Dictionary<string, HistoryNodeId>(StringComparer.Ordinal);

        /// <summary>The process's cache: every history read and the seed export check through it.</summary>
        public static HistoryLinkCache Current { get; private set; } = new HistoryLinkCache();

        /// <summary>A fresh process cache (each seed export starts with one).</summary>
        public static void Reset() => Current = new HistoryLinkCache();

        /// <summary>The link check through the process cache: the path's refusal, or null.</summary>
        public static string Refusal(string path) => Current.Check(path, HistoryNodeIdentity.ForceWindows).Refusal;

        /// <summary>The link check through the process cache: throws the refusal (evidence-link, evidence-unreadable).</summary>
        public static void AssertNoLink(string path)
        {
            var refusal = Refusal(path);
            if (refusal != null) throw new NightlyRefusal(refusal);
        }

        /// <summary>The directories cached so far.</summary>
        public int Count => _directories.Count;

        /// <summary>The full path, without a trailing separator unless it is a root.</summary>
        public static string FullPath(string path)
        {
            var full = Path.GetFullPath(path);
            var root = Path.GetPathRoot(full) ?? string.Empty;
            if (full.Length > root.Length) full = full.TrimEnd('\\', '/');
            return full;
        }

        /// <summary>The link check of one path: from the root down, ancestors cached, the path and its parent read.</summary>
        public HistoryLinkCheck Check(string path, bool forceWindows)
        {
            var chain = Chain(FullPath(path));
            var refusal = Evaluate(chain, null, null, forceWindows, out var leaf);
            return new HistoryLinkCheck { Refusal = refusal, Leaf = leaf };
        }

        /// <summary>
        /// The seed export's walk from <paramref name="roots"/> (empty names skipped): every entry checked, directories
        /// that are not links walked in turn, every other entry returned. Stops at the first refusal.
        /// </summary>
        public HistoryWalk Walk(string[] roots, bool forceWindows)
        {
            var files = new List<HistoryWalkFile>();
            var pending = new Queue<string>();
            foreach (var root in roots ?? Array.Empty<string>())
            {
                if (!string.IsNullOrEmpty(root)) pending.Enqueue(root);
            }
            while (pending.Count > 0)
            {
                var entries = new List<string>(System.IO.Directory.EnumerateFileSystemEntries(pending.Dequeue()));
                int count = entries.Count;
                var chains = new List<string>[count];
                var parents = new HistoryNodeId[count];
                var leaves = new HistoryNodeId[count];
                var failures = new Exception[count];
                void ReadEntry(int index)
                {
                    try
                    {
                        var chain = Chain(FullPath(entries[index]));
                        chains[index] = chain;
                        // The parent before the entry, as the root-down check reads them.
                        if (chain.Count > 1) parents[index] = HistoryNodeIdentity.Read(chain[1], forceWindows);
                        leaves[index] = HistoryNodeIdentity.Read(chain[0], forceWindows);
                    }
                    catch (Exception ex) { failures[index] = ex; }
                }
                if (count >= ParallelThreshold) Parallel.For(0, count, ReadOptions, ReadEntry);
                else for (int index = 0; index < count; index++) ReadEntry(index);
                for (int index = 0; index < count; index++)
                {
                    if (failures[index] != null) ExceptionDispatchInfo.Capture(failures[index]).Throw();
                    var refusal = Evaluate(chains[index], parents[index], leaves[index], forceWindows, out var leaf);
                    if (refusal != null) return new HistoryWalk { Refusal = refusal, Files = files.ToArray() };
                    var full = chains[index][0];
                    if (leaf.Directory && !leaf.Linked) pending.Enqueue(full);
                    else files.Add(new HistoryWalkFile(full));
                }
            }
            return new HistoryWalk { Files = files.ToArray() };
        }

        // The path, then each parent up to the root.
        private static List<string> Chain(string full)
        {
            var chain = new List<string>();
            var part = full;
            while (!string.IsNullOrEmpty(part))
            {
                chain.Add(part);
                var parent = Path.GetDirectoryName(part);
                if (string.IsNullOrEmpty(parent) || string.Equals(parent, part, StringComparison.Ordinal)) break;
                part = parent;
            }
            return chain;
        }

        // The root-down check of one chain. parentRead and leafRead, when given, are that check's reads of chain[1]
        // and chain[0] (made in parallel by the walk); any other node is read here.
        private string Evaluate(List<string> chain, HistoryNodeId parentRead, HistoryNodeId leafRead, bool forceWindows, out HistoryNodeId leaf)
        {
            leaf = null;
            for (int i = chain.Count - 1; i >= 0; i--)
            {
                var node = chain[i];
                if (i > 1 && _directories.ContainsKey(node)) continue;
                var id = i == 0 && leafRead != null ? leafRead
                    : i == 1 && parentRead != null ? parentRead
                    : HistoryNodeIdentity.Read(node, forceWindows);
                if (i == 0) leaf = id;
                if (id.Missing) continue;
                if (id.Unreadable) return "evidence-unreadable";
                if (id.Linked) return "evidence-link";
                if (_directories.TryGetValue(node, out var prior))
                {
                    if (prior.Directory || id.Directory)
                    {
                        if (prior.Linked || prior.Directory != id.Directory || prior.Dev != id.Dev || prior.Ino != id.Ino) return "evidence-link";
                    }
                }
                else if (id.Directory)
                {
                    _directories[node] = id;
                }
            }
            return null;
        }
    }

    /// <summary>The summary prefix's cut (StoppedSeedFiles.SummaryPrefix): where the summary's bulk rows begin.</summary>
    public static class HistorySummaryPrefix
    {
        /// <summary>
        /// The offset of the first top-level "timingChunks", "suiteTimeline" or "suites" key's opening quote in
        /// <paramref name="data"/>, or -1 when none is there yet.
        /// </summary>
        public static int CutBeforeBulk(byte[] data) => new Scanner().Feed(data, data.Length, 0);

        /// <summary>
        /// CutBeforeBulk over a file read in blocks: Feed each block in order (with its offset in the file) and stop at
        /// the first non-negative result, without scanning the earlier blocks again.
        /// </summary>
        public sealed class Scanner
        {
            private int _depth;
            private bool _inString, _escape, _wantKey, _readingKey;
            private int _keyStart = -1;
            private readonly List<byte> _key = new List<byte>();

            public int Feed(byte[] block, int count, int offset)
            {
                for (int i = 0; i < count; i++)
                {
                    byte b = block[i];
                    if (_inString)
                    {
                        if (_escape) { _escape = false; if (_readingKey) _key.Add(b); continue; }
                        if (b == (byte)'\\') { _escape = true; continue; }
                        if (b == (byte)'"')
                        {
                            _inString = false;
                            if (_readingKey)
                            {
                                _readingKey = false;
                                string text = Encoding.UTF8.GetString(_key.ToArray());
                                if (_depth == 1 && (text == "timingChunks" || text == "suiteTimeline" || text == "suites")) return _keyStart;
                                _key.Clear();
                            }
                            continue;
                        }
                        if (_readingKey) _key.Add(b);
                        continue;
                    }
                    if (b == (byte)'"')
                    {
                        _inString = true;
                        if (_depth == 1 && _wantKey) { _readingKey = true; _keyStart = offset + i; _key.Clear(); _wantKey = false; }
                        continue;
                    }
                    if (b == (byte)'{' || b == (byte)'[')
                    {
                        _depth++;
                        if (b == (byte)'{' && _depth == 1) _wantKey = true;
                        continue;
                    }
                    if (b == (byte)'}' || b == (byte)']') { _depth--; continue; }
                    if (b == (byte)',' && _depth == 1) { _wantKey = true; }
                }
                return -1;
            }
        }
    }
}
