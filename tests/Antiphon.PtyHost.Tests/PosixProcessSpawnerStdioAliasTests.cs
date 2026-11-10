using System.Reflection;
using System.Runtime.InteropServices;
using Antiphon.TestSupport;
using Shouldly;
using TUnit.Core;

namespace Antiphon.PtyHost.Tests;

/// <summary>
/// CARD-0608. CloseStdioAliases must close pipe and socket aliases of the recorded
/// stdio identity and must leave a same-path file descriptor open.
/// </summary>
[Category("Unit")]
[NativeLinuxOnly("CARD-0608 closes pipe and socket aliases through /proc/self/fd. The case remains required on Linux.")]
public sealed class PosixProcessSpawnerStdioAliasTests
{
    [Test]
    public void CloseStdioAliases_closes_pipe_and_socket_aliases_and_leaves_same_path_files_open()
    {
        var owned = new List<int>();
        string? directory = null;
        try
        {
            var pipeAlias = OpenPipeAlias(owned);
            var socketAlias = OpenSocketAlias(owned);
            var unrelated = OpenPipeAlias(owned);
            directory = Directory.CreateTempSubdirectory("card0608-").FullName;
            var fileAlias = OpenFileAlias(directory, "plain", owned);
            var trickyAlias = OpenFileAlias(directory, "pipe:[123]", owned);

            var pipeIdentity = RequireLink(pipeAlias);
            var socketIdentity = RequireLink(socketAlias);
            var unrelatedIdentity = RequireLink(unrelated);
            var fileIdentity = RequireLink(fileAlias);
            var trickyIdentity = RequireLink(trickyAlias);

            pipeIdentity.StartsWith("pipe:[", StringComparison.Ordinal).ShouldBeTrue(pipeIdentity);
            socketIdentity.StartsWith("socket:[", StringComparison.Ordinal).ShouldBeTrue(socketIdentity);
            unrelatedIdentity.StartsWith("pipe:[", StringComparison.Ordinal).ShouldBeTrue(unrelatedIdentity);
            unrelatedIdentity.ShouldNotBe(pipeIdentity);
            fileIdentity.StartsWith("pipe:[", StringComparison.Ordinal).ShouldBeFalse(fileIdentity);
            fileIdentity.StartsWith("socket:[", StringComparison.Ordinal).ShouldBeFalse(fileIdentity);
            trickyIdentity.Contains("pipe:[", StringComparison.Ordinal).ShouldBeTrue(trickyIdentity);
            trickyIdentity.StartsWith("pipe:[", StringComparison.Ordinal).ShouldBeFalse(trickyIdentity);

            var identities = new HashSet<string>(StringComparer.Ordinal);
            identities.Add(pipeIdentity);
            identities.Add(socketIdentity);
            identities.Add(fileIdentity);
            identities.Add(trickyIdentity);

            CloseAliases(identities);

            var failures = new List<string>();
            if (Still(pipeAlias, pipeIdentity))
                failures.Add("pipe alias " + pipeAlias + " (" + pipeIdentity + ") stayed open");
            if (Still(socketAlias, socketIdentity))
                failures.Add("socket alias " + socketAlias + " (" + socketIdentity + ") stayed open");
            if (!Still(unrelated, unrelatedIdentity))
                failures.Add("unrelated pipe " + unrelated + " (" + unrelatedIdentity + ") was closed");
            if (!Still(fileAlias, fileIdentity))
                failures.Add("same-path file " + fileAlias + " (" + fileIdentity + ") was closed");
            if (!Still(trickyAlias, trickyIdentity))
                failures.Add("path containing pipe:[ " + trickyAlias + " (" + trickyIdentity + ") was closed");
            failures.ShouldBeEmpty(string.Join("; ", failures));
        }
        finally
        {
            foreach (var fd in owned)
                _ = CloseFd(fd);
            if (directory is not null)
            {
                try
                {
                    Directory.Delete(directory, recursive: true);
                }
                catch (IOException)
                {
                }
            }
        }
    }

    private static void CloseAliases(HashSet<string> identities)
    {
        var method = typeof(PosixProcessSpawner).GetMethod(
            CloseStdioAliases,
            BindingFlags.Static | BindingFlags.NonPublic,
            null,
            [typeof(HashSet<string>)],
            null);
        if (method is null)
            throw new InvalidOperationException("PosixProcessSpawner.CloseStdioAliases was not found.");
        method.Invoke(null, [identities]);
    }

    private static int OpenPipeAlias(List<int> owned)
    {
        var ends = new int[2];
        if (Pipe(ends) != 0)
            throw new InvalidOperationException("pipe failed: " + Marshal.GetLastPInvokeError());
        owned.Add(ends[0]);
        owned.Add(ends[1]);
        var alias = DupHigh(ends[1]);
        owned.Add(alias);
        RequireClose(ends[0]);
        RequireClose(ends[1]);
        owned.Remove(ends[0]);
        owned.Remove(ends[1]);
        return alias;
    }

    private static int OpenSocketAlias(List<int> owned)
    {
        var sock = Socket(AfUnix, SockStream, 0);
        if (sock < 0)
            throw new InvalidOperationException("socket failed: " + Marshal.GetLastPInvokeError());
        owned.Add(sock);
        var alias = DupHigh(sock);
        owned.Add(alias);
        RequireClose(sock);
        owned.Remove(sock);
        return alias;
    }

    private static int OpenFileAlias(string directory, string name, List<int> owned)
    {
        var path = Path.Combine(directory, name);
        File.WriteAllBytes(path, []);
        var low = Open(path, ORdwr);
        if (low < 0)
            throw new InvalidOperationException("open failed: " + Marshal.GetLastPInvokeError());
        owned.Add(low);
        var alias = DupHigh(low);
        owned.Add(alias);
        RequireClose(low);
        owned.Remove(low);
        return alias;
    }

    private static int DupHigh(int fd)
    {
        var high = Fcntl(fd, FDupFdCloexec, 700);
        if (high < 700)
            throw new InvalidOperationException("dup failed: " + Marshal.GetLastPInvokeError());
        return high;
    }

    private static string RequireLink(int fd)
    {
        var target = ReadLinkOrNull(fd);
        if (target is null)
            throw new InvalidOperationException("readlink failed for fd " + fd);
        return target;
    }

    private static bool Still(int fd, string identity)
    {
        if (FcntlGet(fd, FGetFd) < 0)
            return false;
        return ReadLinkOrNull(fd) == identity;
    }

    private static string? ReadLinkOrNull(int fd)
    {
        var buffer = new byte[4096];
        var written = (int)ReadLink("/proc/self/fd/" + fd, buffer, buffer.Length);
        if (written <= 0 || written >= buffer.Length)
            return null;
        return System.Text.Encoding.UTF8.GetString(buffer, 0, written);
    }

    private static void RequireClose(int fd)
    {
        if (CloseFd(fd) != 0)
            throw new InvalidOperationException("close failed: " + Marshal.GetLastPInvokeError());
    }

    private const string CloseStdioAliases = "CloseStdioAliases";
    private const int ORdwr = 2;
    private const int AfUnix = 1;
    private const int SockStream = 1;
    private const int FGetFd = 1;
    private const int FDupFdCloexec = 1030;

    [DllImport("libc", EntryPoint = "pipe", SetLastError = true)]
    private static extern int Pipe(int[] fds);

    [DllImport("libc", EntryPoint = "socket", SetLastError = true)]
    private static extern int Socket(int domain, int type, int protocol);

    [DllImport("libc", EntryPoint = "open", SetLastError = true)]
    private static extern int Open(string path, int flags);

    [DllImport("libc", EntryPoint = "fcntl", SetLastError = true)]
    private static extern int Fcntl(int fd, int cmd, int arg);

    [DllImport("libc", EntryPoint = "fcntl", SetLastError = true)]
    private static extern int FcntlGet(int fd, int cmd);

    [DllImport("libc", EntryPoint = "close", SetLastError = true)]
    private static extern int CloseFd(int fd);

    [DllImport("libc", EntryPoint = "readlink", SetLastError = true)]
    private static extern nint ReadLink(string path, byte[] buf, nint bufsiz);
}
