// lstat/open/fstat for test-phase-evidence.ps1 on Linux. Also compiled on its own by Add-Type: keep it to the
// C# 5 surface.
using System;
using System.Runtime.InteropServices;
namespace Antiphon.TestPhase {
    [StructLayout(LayoutKind.Sequential)]
    public struct LinuxStat {
        public ulong Dev;
        public ulong Ino;
        public ulong Nlink;
        public uint Mode;
        public uint Uid;
        public uint Gid;
        public int Pad0;
        public ulong Rdev;
        public long Size;
        public ulong A; public ulong B; public ulong C; public ulong D;
        public ulong E; public ulong F; public ulong G; public ulong H;
        public ulong I; public ulong J; public ulong K; public ulong L;
    }
    public static class FileIdentity {
        [DllImport("libc", EntryPoint="lstat", SetLastError=true)]
        public static extern int LStat(string path, out LinuxStat stat);
        [DllImport("libc", EntryPoint="open", SetLastError=true)]
        public static extern int Open(string path, int flags);
        [DllImport("libc", EntryPoint="fstat", SetLastError=true)]
        public static extern int FStat(int fd, out LinuxStat stat);
        [DllImport("libc", EntryPoint="close", SetLastError=true)]
        public static extern int Close(int fd);
    }
}
