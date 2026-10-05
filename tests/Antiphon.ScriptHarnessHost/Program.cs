using System.Diagnostics;
using System.Globalization;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;

if (args.Length >= 3 && args[0] == "fixture-child")
    return await FixtureChild(args[1], args[2]);
if (args.Length >= 3 && args[0] == "fixture-grandchild")
    return await FixtureGrandchild(args[1], args[2]);
if (args.Length == 1 && args[0] == "windows-sentinel" && OperatingSystem.IsWindows())
{
    await Task.Delay(TimeSpan.FromSeconds(20));
    return 0;
}
if (args.Length < 9 || args[0] != "linux-owner" || !OperatingSystem.IsLinux()) return 2;
var socketPath = args[1];
var nonce = args[2];
var executable = args[3];
var script = args[4];
var caseName = args[5];
var results = args[6];
if (!long.TryParse(args[7], NumberStyles.None, CultureInfo.InvariantCulture, out var failsafeMs) || failsafeMs <= 0)
    return 2;
// The final argument is reserved for future fixture modes and validates the wire shape.
if (args[8] == "fail-setsid")
{
    Console.Error.WriteLine("injected setsid failure before child launch");
    return 3;
}
if (args[8] != "v1") return 2;

var pid = getpid();
if (setsid() != pid || getpgid(0) != pid || getsid(0) != pid) return 3;
using var failsafe = new CancellationTokenSource(TimeSpan.FromMilliseconds(failsafeMs));
using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
try
{
    await socket.ConnectAsync(new UnixDomainSocketEndPoint(socketPath), failsafe.Token);
    using var stream = new NetworkStream(socket, ownsSocket: false);
    var startTicks = ReadStart(pid);
    await WriteFrame(stream, $"HELLO {nonce} {pid} {startTicks} {pid} {pid}", failsafe.Token);
    var authorization = await ReadFrame(stream, failsafe.Token);
    if (authorization != "START " + nonce) return 4;

    var psi = new ProcessStartInfo(executable) { UseShellExecute = false, WorkingDirectory = Directory.GetCurrentDirectory() };
    foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-File", script, "-Case", caseName, "-ResultsDirectory", results })
        psi.ArgumentList.Add(arg);
    foreach (var arg in args.Skip(9)) psi.ArgumentList.Add(arg);
    using var root = Process.Start(psi) ?? throw new InvalidOperationException("pwsh did not start.");
    var rootStart = ReadStart(root.Id);
    await WriteFrame(stream, $"ROOT {nonce} {root.Id} {rootStart}", failsafe.Token);
    // The child inherited both writers. Keeping copies here would make EOF depend
    // on the supervisor's lifetime rather than the logical script tree.
    CloseOwnOutputWriters();

    var rootExit = Task.Run(async () =>
    {
        await root.WaitForExitAsync(failsafe.Token);
        await WriteFrame(stream, $"EXIT {nonce} {root.Id} {rootStart} {root.ExitCode}", failsafe.Token);
    });
    while (true)
    {
        var frame = await ReadFrame(stream, failsafe.Token);
        if (frame == "STOP " + nonce) break;
        if (frame.StartsWith("STOP ", StringComparison.Ordinal)) continue;
        // A malformed command ends custody without executing the script further.
        break;
    }
}
catch (OperationCanceledException) when (failsafe.IsCancellationRequested) { }
catch (EndOfStreamException) { }
catch (IOException) { }
finally
{
    // Only this supervisor signals its own verified group. The parent never sends
    // a delayed signal to a saved group number that could have been recycled.
    if (getpid() == pid && getpgid(0) == pid && getsid(0) == pid && pid > 1)
        kill(-pid, 9);
}
return 5;

static long ReadStart(int pid)
{
    var stat = File.ReadAllText($"/proc/{pid}/stat");
    var end = stat.LastIndexOf(") ", StringComparison.Ordinal);
    var fields = stat[(end + 2)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
    return long.Parse(fields[19], CultureInfo.InvariantCulture);
}

static async Task<string> ReadFrame(Stream stream, CancellationToken token)
{
    using var bytes = new MemoryStream();
    var one = new byte[1];
    while (bytes.Length <= 4096)
    {
        if (await stream.ReadAsync(one, token) == 0) throw new EndOfStreamException();
        if (one[0] == (byte)'\n') return Encoding.UTF8.GetString(bytes.ToArray());
        bytes.WriteByte(one[0]);
    }
    throw new InvalidDataException("Control frame exceeds 4096 bytes.");
}

static Task WriteFrame(Stream stream, string message, CancellationToken token) =>
    stream.WriteAsync(Encoding.UTF8.GetBytes(message + "\n"), token).AsTask();

static void CloseOwnOutputWriters(bool closeStdout = true, bool closeStderr = true)
{
    if (OperatingSystem.IsWindows())
    {
        // Do not initialize Console.Out/Error: cached Console streams can retain
        // another writer. Close just the selected inherited standard handles.
        var writers = new HashSet<IntPtr>();
        if (closeStdout) writers.Add(GetStdHandle(-11));
        if (closeStderr) writers.Add(GetStdHandle(-12));
        foreach (var handle in writers)
        {
            if (handle == IntPtr.Zero || handle == new IntPtr(-1) || !CloseHandle(handle))
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Close inherited standard writer");
        }
        return;
    }
    // dotnet's redirected-process launch can leave duplicate write descriptors
    // (for example 6/7) in the supervisor in addition to fd 1/2. They must all
    // be closed or a completed pwsh never gives the host stream EOF.
    var targets = new HashSet<string>(StringComparer.Ordinal);
    foreach (var fd in new[] { closeStdout ? 1 : -1, closeStderr ? 2 : -1 })
    {
        var target = ReadFdTarget(fd);
        if (target is not null && target.StartsWith("pipe:[", StringComparison.Ordinal)) targets.Add(target);
    }
    var descriptors = Directory.EnumerateFileSystemEntries("/proc/self/fd")
        .Select(Path.GetFileName).Select(value => int.TryParse(value, out var fd) ? fd : -1)
        .Where(fd => fd >= 1).ToArray();
    foreach (var fd in descriptors)
    {
        var target = ReadFdTarget(fd);
        if (target is not null && targets.Contains(target) && close(fd) != 0)
            throw new IOException($"Failed to close inherited output descriptor {fd}.");
    }
}

static string? ReadFdTarget(int fd)
{
    var bytes = new byte[256];
    var count = readlink($"/proc/self/fd/{fd}", bytes, (nuint)bytes.Length);
    return count > 0 ? Encoding.UTF8.GetString(bytes, 0, (int)count) : null;
}

static async Task<int> FixtureChild(string directory, string heldStream)
{
    Directory.CreateDirectory(directory);
    var helper = typeof(Program).Assembly.Location;
    var psi = new ProcessStartInfo("dotnet") { UseShellExecute = false };
    foreach (var arg in new[] { helper, "fixture-grandchild", directory, heldStream }) psi.ArgumentList.Add(arg);
    using var grandchild = Process.Start(psi) ?? throw new InvalidOperationException("Fixture grandchild did not start.");
    CloseOwnOutputWriters(heldStream is "stderr" or "none", heldStream is "stdout" or "none");
    PublishIdentity(directory, "child");
    await Task.Delay(TimeSpan.FromSeconds(20));
    return 0;
}

static async Task<int> FixtureGrandchild(string directory, string heldStream)
{
    CloseOwnOutputWriters(heldStream is "stderr" or "none", heldStream is "stdout" or "none");
    PublishIdentity(directory, "grandchild");
    await Task.Delay(TimeSpan.FromSeconds(20));
    return 0;
}

static void PublishIdentity(string directory, string name)
{
    var path = Path.Combine(directory, name);
    var nonce = OperatingSystem.IsWindows() ? File.ReadAllText(Path.Combine(directory, "nonce")) + " " : "";
    File.WriteAllText(path + ".tmp", $"{nonce}{Environment.ProcessId} {Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks} {(OperatingSystem.IsLinux() ? ReadStart(Environment.ProcessId) : 0)}");
    File.Move(path + ".tmp", path);
}

[DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr GetStdHandle(int standard);
[DllImport("kernel32.dll", SetLastError = true)] static extern bool CloseHandle(IntPtr handle);

[DllImport("libc", SetLastError = true)] static extern int setsid();
[DllImport("libc", SetLastError = true)] static extern int getpid();
[DllImport("libc", SetLastError = true)] static extern int getpgid(int pid);
[DllImport("libc", SetLastError = true)] static extern int getsid(int pid);
[DllImport("libc", SetLastError = true)] static extern int kill(int pid, int signal);
[DllImport("libc", SetLastError = true)] static extern int close(int fd);
[DllImport("libc", SetLastError = true)] static extern nint readlink(string path, byte[] buffer, nuint size);

// Loaded by the real PowerShell root so the probe sees that process's inherited
// handle table, rather than the different table of another helper subprocess.
public static class WindowsScriptHandleProbe
{
    public static void WriteReceipt(string directory, string nonce)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        var fields = File.ReadAllText(Path.Combine(directory, "probe-input")).Split(' ');
        if (fields.Length != 3 || fields[0] != nonce) throw new InvalidDataException("Handle probe nonce mismatch.");
        var job = new IntPtr(long.Parse(fields[1], CultureInfo.InvariantCulture));
        var unrelatedEvent = new IntPtr(long.Parse(fields[2], CultureInfo.InvariantCulture));
        var buffer = Marshal.AllocHGlobal(144);
        bool query, retained = false;
        try
        {
            query = QueryInformationJobObject(job, 9, buffer, 144, out _);
            if (query)
            {
                var current = GetCurrentProcess();
                retained = DuplicateHandle(current, job, current, out var duplicate, 0, false, 2);
                if (retained && !CloseHandle(duplicate)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            }
        }
        finally { Marshal.FreeHGlobal(buffer); }
        var signaled = unrelatedEvent != IntPtr.Zero && SetEvent(unrelatedEvent);
        var receipt = $"{nonce} {query} {retained} {signaled} {GetStdHandle(-10).ToInt64()} {GetStdHandle(-11).ToInt64()} {GetStdHandle(-12).ToInt64()}";
        File.WriteAllText(Path.Combine(directory, "probe-receipt.tmp"), receipt);
        File.Move(Path.Combine(directory, "probe-receipt.tmp"), Path.Combine(directory, "probe-receipt"));
    }

    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool QueryInformationJobObject(IntPtr job, int kind, IntPtr buffer, uint size, out uint returned);
    [DllImport("kernel32.dll")] private static extern IntPtr GetCurrentProcess();
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool DuplicateHandle(IntPtr sourceProcess, IntPtr source, IntPtr targetProcess, out IntPtr copy, uint access, bool inherit, uint options);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool SetEvent(IntPtr handle);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr GetStdHandle(int standard);
}
