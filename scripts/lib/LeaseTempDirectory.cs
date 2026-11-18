#nullable disable
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;

namespace Antiphon.Build
{
    // CARD-1981. A Linux build lease gets a private TMPDIR so the SDK's empty
    // six-character directory is created there and removed with the lease.
    // Windows keeps its own TEMP. The leak was observed on Linux only.
    public static class LeaseTempDirectory
    {
        public static readonly AsyncLocal<string> Ambient = new AsyncLocal<string>();
        private static readonly Regex OwnedPath = new Regex("^/tmp/aslot-[0-9a-f]{8}\\z", RegexOptions.CultureInvariant);

        public static LeaseTempHandle TryCreate()
        {
            if (!OperatingSystem.IsLinux())
                return null;
            try
            {
                for (var attempt = 0; attempt < 8; attempt++)
                {
                    var path = "/tmp/aslot-" + Guid.NewGuid().ToString("N").Substring(0, 8);
                    if (!OwnedPath.IsMatch(path) || Directory.Exists(path) || File.Exists(path))
                        continue;
                    Directory.CreateDirectory(path);
                    try
                    {
                        File.SetUnixFileMode(path, (UnixFileMode)0x1C0);
                        var token = Guid.NewGuid().ToString("N");
                        var marker = path + "/.lease";
                        File.WriteAllText(marker, token);
                        File.SetUnixFileMode(marker, (UnixFileMode)0x180);
                        LinuxStat id;
                        if (!TryStat(path, out id) || id.IsSymlink || !id.IsDirectory)
                        {
                            Abandon(path);
                            continue;
                        }
                        return new LeaseTempHandle(path, token, id);
                    }
                    catch (Exception)
                    {
                        Abandon(path);
                    }
                }
            }
            catch (Exception)
            {
            }
            return null;
        }

        // Removes a directory this process just created and has not published.
        // A path statx cannot identify is left in place. A symlink is left in place,
        // including its target.
        private static void Abandon(string path)
        {
            try
            {
                if (path == null || !OwnedPath.IsMatch(path))
                    return;
                LinuxStat now;
                if (!TryStat(path, out now) || now.IsSymlink || !now.IsDirectory)
                    return;
                var markerPath = path + "/.lease";
                LinuxStat marker;
                if (TryStat(markerPath, out marker))
                {
                    if (marker.IsSymlink || marker.IsDirectory)
                        return;
                    File.Delete(markerPath);
                }
                Directory.Delete(path, false);
            }
            catch (Exception)
            {
            }
        }

        internal static void BestEffortRemove(string path, string token, LinuxStat created)
        {
            try
            {
                if (path == null || token == null || !OwnedPath.IsMatch(path))
                    return;
                LinuxStat now;
                if (!TryStat(path, out now) || now.IsSymlink || !now.IsDirectory)
                    return;
                if (now.Ino != created.Ino || now.DevMajor != created.DevMajor || now.DevMinor != created.DevMinor)
                    return;
                var markerPath = path + "/.lease";
                LinuxStat marker;
                if (!TryStat(markerPath, out marker) || marker.IsSymlink || marker.IsDirectory)
                    return;
                if (File.ReadAllText(markerPath) != token)
                    return;
                RemoveChildren(path);
                Directory.Delete(path, false);
            }
            catch (Exception)
            {
            }
        }

        private static void RemoveChildren(string path)
        {
            foreach (var child in Directory.EnumerateFileSystemEntries(path))
            {
                LinuxStat stat;
                if (!TryStat(child, out stat))
                    continue;
                if (stat.IsSymlink)
                {
                    try { File.Delete(child); } catch (Exception) { }
                    continue;
                }
                if (stat.IsDirectory)
                {
                    RemoveChildren(child);
                    try { Directory.Delete(child, false); } catch (Exception) { }
                    continue;
                }
                try { File.Delete(child); } catch (Exception) { }
            }
        }

        internal static bool TryStat(string path, out LinuxStat stat)
        {
            stat = new LinuxStat();
            if (!OperatingSystem.IsLinux())
                return false;
            LinuxStatx raw;
            if (Statx(-100, path, 0x100, 0x103, out raw) != 0 || (raw.Mask & 0x101) != 0x101)
                return false;
            var type = raw.Mode & 0xF000;
            stat = new LinuxStat
            {
                Ino = raw.Ino,
                DevMajor = raw.DevMajor,
                DevMinor = raw.DevMinor,
                IsDirectory = type == 0x4000,
                IsSymlink = type == 0xA000,
            };
            return true;
        }

        [StructLayout(LayoutKind.Explicit, Size = 256)]
        private struct LinuxStatx
        {
            [FieldOffset(0)] public uint Mask;
            [FieldOffset(28)] public ushort Mode;
            [FieldOffset(32)] public ulong Ino;
            [FieldOffset(136)] public uint DevMajor;
            [FieldOffset(140)] public uint DevMinor;
        }

        [DllImport("libc", EntryPoint = "statx", SetLastError = true)]
        private static extern int Statx(int fd, string path, int flags, uint mask, out LinuxStatx stat);

        internal struct LinuxStat
        {
            public ulong Ino;
            public uint DevMajor;
            public uint DevMinor;
            public bool IsDirectory;
            public bool IsSymlink;
        }
    }

    public sealed class LeaseTempHandle : IDisposable
    {
        private readonly string _token;
        private readonly LeaseTempDirectory.LinuxStat _created;
        private int _disposed;

        internal LeaseTempHandle(string path, string token, LeaseTempDirectory.LinuxStat created)
        {
            DirectoryPath = path;
            _token = token;
            _created = created;
        }

        public string DirectoryPath { get; private set; }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;
            LeaseTempDirectory.BestEffortRemove(DirectoryPath, _token, _created);
        }
    }
}
