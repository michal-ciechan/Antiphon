using System.Diagnostics;
using System.Globalization;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;

var cacheProbe = Environment.GetEnvironmentVariable(Antiphon.Hosting.PwshPrivateCache.ProbeVariable);
if (!string.IsNullOrWhiteSpace(cacheProbe))
    return Antiphon.Hosting.PwshPrivateCache.CompleteProbe(cacheProbe);

if (args.Length >= 1 && args[0] == "cli-echo")
    return CliEcho(args);
// W2-nightly S17: a named command echoed as cli-echo is (CLI_ECHO_COMMAND, e.g. nightly-tests), so a launcher test sees the argv a production hop hands the CLI.
if (args.Length >= 1 && !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CLI_ECHO_COMMAND")) && args[0] == Environment.GetEnvironmentVariable("CLI_ECHO_COMMAND"))
    return CliEcho(args);
if (args.Length >= 5 && args[0] == "post-land-slot")
    return PostLandSlot(args);
if (args.Length == 9 && args[0] == "post-land-lock")
    return PostLandLock(args);
if (args.Length >= 3 && args[0] == "fixture-child")
    return await FixtureChild(args[1], args[2]);
if (args.Length >= 3 && args[0] == "fixture-grandchild")
    return await FixtureGrandchild(args[1], args[2]);
if (args.Length == 4 && args[0] == "c1375-owned-tree")
    return C1375OwnedTree(args[1], args[2], args[3]);
if (args.Length == 4 && args[0] == "checkpoint-pool-witness")
    return CheckpointPoolWitness(args[1], args[2], args[3]);
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
    // SIGKILL of this process group skips ProcessExit, so release the private cache first.
    Antiphon.Hosting.PwshPrivateCache.Release();
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

// CARD-1375 owned tree for NightlyProbeSupervisorTests: c1375-owned-tree <root|child|grandchild> <directory> <nonce>.
// root starts child and child starts grandchild, each inheriting both standard streams. Each writes its stdout and
// stderr markers, then publishes <directory>/<role>.json (role, nonce, pid, parentPid, /proc start, start ticks), then
// waits for ever. There is no self-exit deadline: the supervisor must terminate it.
static int C1375OwnedTree(string role, string directory, string nonce)
{
    if (!OperatingSystem.IsLinux() || role is not ("root" or "child" or "grandchild")) return 2;
    Process? next = null;
    if (role != "grandchild")
    {
        var psi = new ProcessStartInfo(Environment.ProcessPath ?? "dotnet") { UseShellExecute = false };
        foreach (var arg in new[] { typeof(Program).Assembly.Location, "c1375-owned-tree", role == "root" ? "child" : "grandchild", directory, nonce })
            psi.ArgumentList.Add(arg);
        // Start the next generation before publishing our record: its startup can overlap
        // ours within the same readiness bound. The child is retained for normal OS reaping.
        next = Process.Start(psi) ?? throw new InvalidOperationException("Fixture next generation did not start.");
    }
    var stat = File.ReadAllText($"/proc/{Environment.ProcessId}/stat");
    var fields = stat[(stat.LastIndexOf(')') + 2)..].Split(' ');
    var identity = System.Text.Json.JsonSerializer.Serialize(new
    {
        role, nonce, pid = Environment.ProcessId, parentPid = int.Parse(fields[1], CultureInfo.InvariantCulture),
        start = fields[19], startTimeUtcTicks = Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks,
    });
    var path = Path.Combine(directory, role + ".json");
    File.WriteAllText(path + ".tmp", identity);
    Console.Out.WriteLine($"C1375 {role} stdout {nonce}");
    Console.Error.WriteLine($"C1375 {role} stderr {nonce}");
    // Publication certifies both markers are already written, even if this process
    // is descheduled immediately after the harness observes its readiness record.
    File.Move(path + ".tmp", path);
    next?.WaitForExit();
    while (true) Thread.Sleep(1000);
}

// Two-process owner publication for PostLandEarlyWarningTests:
// post-land-lock <lockPath> <runId> <readyPath> <goPath> <ownArmed> <peerArmed> <resultPath> <releasePath>
// Writes ready, spins until go exists, arms, spins until the peer is armed, then calls
// production EnterLock. Publishes true or false, then stays alive until release exists so the
// winner's pid is still live when the loser inspects the owner record. Does not call ExitLock.
static int PostLandLock(string[] args)
{
    var lockPath = args[1];
    var runId = args[2];
    var readyPath = args[3];
    var goPath = args[4];
    var ownArmed = args[5];
    var peerArmed = args[6];
    var resultPath = args[7];
    var releasePath = args[8];
    File.WriteAllText(readyPath, "ready\n");
    if (!SpinUntil(goPath, 20_000))
        return 3;
    File.WriteAllText(ownArmed, "armed\n");
    if (!SpinUntil(peerArmed, 5_000))
        return 4;
    var won = Antiphon.Nightly.PostLandEarlyWarning.EnterLock(lockPath, runId, 0);
    var temp = resultPath + ".tmp";
    File.WriteAllText(temp, won ? "true\n" : "false\n");
    File.Move(temp, resultPath);
    Console.WriteLine(won ? "acquired=true" : "acquired=false");
    if (!SpinUntil(releasePath, 20_000))
        return 5;
    return 0;
}

static bool SpinUntil(string path, int budgetMs)
{
    var deadline = Environment.TickCount64 + budgetMs;
    while (!File.Exists(path))
    {
        if (Environment.TickCount64 > deadline)
            return false;
        Thread.SpinWait(64);
    }
    return true;
}

// A fake build slot for PostLandEarlyWarningTests: post-land-slot <marker> <trx> <build-exit> <shard-exit> -Label <label>
// -- <command...>. It prints the slot's lines, appends its arguments to the marker, copies the TRX to a --no-build
// command's --results-directory/--report-trx-filename, and exits with the shard or the build exit.
static int PostLandSlot(string[] args)
{
    var marker = args[1];
    var trxSource = args[2];
    var rest = args.Skip(5).ToArray();
    Console.WriteLine("BUILD SLOT granted");
    Console.WriteLine("Build succeeded.");
    File.AppendAllText(marker, string.Join("\n", rest) + "\n");
    string results = "", fileName = "";
    for (var i = 0; i + 1 < rest.Length; i++)
    {
        if (rest[i] == "--results-directory") results = rest[i + 1];
        if (rest[i] == "--report-trx-filename") fileName = rest[i + 1];
    }
    if (!rest.Contains("--no-build")) return int.Parse(args[3], CultureInfo.InvariantCulture);
    if (results.Length > 0 && fileName.Length > 0)
    {
        Directory.CreateDirectory(results);
        var destination = Path.Combine(results, fileName);
        File.Copy(trxSource, destination, true);
        File.SetLastWriteTimeUtc(destination, DateTime.UtcNow);
    }
    return int.Parse(args[4], CultureInfo.InvariantCulture);
}

// The argv and environment observer of AntiphonCliLauncherTests and NightlyHostTests: cli-echo [arguments...]. It
// prints one JSON line: the arguments after "cli-echo", the working and base directories, the pid, and the values of
// the variables CLI_ECHO_ENV names (comma separated; null when unset). With "--result-file <path>" among the
// arguments it also writes that line to the path (resultFileExisted: a file was already there). CLI_ECHO_HOLD then
// waits, at most 60 s, until that file exists. CLI_ECHO_EXIT is the exit code (0 when unset).
static int CliEcho(string[] args)
{
    var arguments = args.Skip(1).ToArray();
    string? resultFile = null;
    for (var i = 0; i + 1 < arguments.Length; i++)
        if (arguments[i] == "--result-file") resultFile = arguments[i + 1];
    var environment = new SortedDictionary<string, string?>(StringComparer.Ordinal);
    foreach (var name in (Environment.GetEnvironmentVariable("CLI_ECHO_ENV") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        environment[name] = Environment.GetEnvironmentVariable(name);
    var line = System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, object?>
    {
        ["arguments"] = arguments,
        ["workingDirectory"] = Directory.GetCurrentDirectory(),
        ["baseDirectory"] = AppContext.BaseDirectory,
        ["pid"] = Environment.ProcessId,
        ["environment"] = environment,
        ["resultFileExisted"] = resultFile != null && File.Exists(resultFile),
    });
    if (resultFile != null) File.WriteAllText(resultFile, line);
    Console.WriteLine(line);
    Console.Out.Flush();
    var hold = Environment.GetEnvironmentVariable("CLI_ECHO_HOLD");
    if (!string.IsNullOrEmpty(hold))
    {
        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (!File.Exists(hold) && DateTime.UtcNow < deadline) Thread.Sleep(20);
    }
    return int.TryParse(Environment.GetEnvironmentVariable("CLI_ECHO_EXIT"), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var code) ? code : 0;
}

// CARD-1601's pool witness for CheckpointReceiptStallBudgetTests: checkpoint-pool-witness <assembly> <type> <root>.
// The reader type (the library CheckpointFileReader, or the ConfinedFileReader the test compiled from
// scripts/lib/checkpoint-file-reader.ps1) is loaded from its file. This process caps its own thread pool at four
// workers and occupies all four with healthy receipt reads, which must still complete: the reader waits on its
// dedicated I/O thread, never on a pool worker. Its own process, because the cap is process-wide.
static int CheckpointPoolWitness(string assemblyPath, string typeName, string root)
{
    var reader = System.Reflection.Assembly.LoadFrom(assemblyPath).GetType(typeName, throwOnError: true)!;
    Directory.CreateDirectory(root);
    var path = Path.Combine(root, "receipt.json");
    var expected = Enumerable.Repeat((byte)42, 85).ToArray();
    File.WriteAllBytes(path, expected);
    var read = reader.GetMethods().Single(m => m.Name == "ReadBytes" && m.GetParameters().Length == 5 && m.GetParameters()[0].ParameterType == typeof(string));
    ThreadPool.GetMinThreads(out var min, out var minIo);
    ThreadPool.GetMaxThreads(out var max, out var maxIo);
    using var entered = new CountdownEvent(4);
    using var release = new ManualResetEventSlim();
    using var finished = new CountdownEvent(4);
    Exception? failure = null;
    var completed = false;
    try
    {
        if (!ThreadPool.SetMinThreads(4, minIo) || !ThreadPool.SetMaxThreads(4, maxIo)) throw new InvalidOperationException("could not cap isolated pool");
        for (var i = 0; i < 4; i++)
            Task.Run(() =>
            {
                entered.Signal();
                release.Wait();
                try
                {
                    var bytes = (byte[])read.Invoke(null, [root, path, 1024, null, CancellationToken.None])!;
                    if (!bytes.SequenceEqual(expected)) throw new InvalidDataException("wrong receipt bytes");
                }
                catch (Exception ex) { Interlocked.CompareExchange(ref failure, ex, null); }
                finally { finished.Signal(); }
            });
        if (!entered.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("pool callers did not enter");
        release.Set();
        completed = finished.Wait(TimeSpan.FromSeconds(10));
    }
    finally
    {
        ThreadPool.SetMaxThreads(max, maxIo);
        ThreadPool.SetMinThreads(min, minIo);
        release.Set();
        if (!finished.Wait(TimeSpan.FromSeconds(20))) throw new TimeoutException("isolated pool did not drain after restoration");
    }
    if (!completed) throw new TimeoutException("healthy receipts did not complete with all pool workers occupied");
    if (failure is not null) throw new InvalidOperationException("healthy receipt refused", failure);
    Console.WriteLine("PASS pool-saturation readers=4");
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
        if (fields.Length != 4 || fields[0] != nonce) throw new InvalidDataException("Handle probe nonce mismatch.");
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
        var signaled = false;
        if (unrelatedEvent != IntPtr.Zero)
        {
            // Handle values are process-local. Pin the transported value's current
            // object BEFORE opening the named fixture event, otherwise that open
            // could itself reuse the value and manufacture a false inheritance hit.
            var current = GetCurrentProcess();
            if (DuplicateHandle(current, unrelatedEvent, current, out var snapshot, 0, false, 2))
            {
                using var observed = new Microsoft.Win32.SafeHandles.SafeFileHandle(snapshot, true);
                using var expected = OpenEventW(0x100000, false, fields[3]); // SYNCHRONIZE
                if (expected.IsInvalid) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Open named fixture event");
                if (CompareObjectHandles(observed, expected))
                {
                    signaled = SetEvent(snapshot);
                    if (!signaled) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Signal inherited fixture event");
                }
            }
            else if (Marshal.GetLastWin32Error() != 6) // invalid transported handle is the expected non-inheritance arm
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Retain transported event observation");
        }
        var receipt = $"{nonce} {query} {retained} {signaled} {GetStdHandle(-10).ToInt64()} {GetStdHandle(-11).ToInt64()} {GetStdHandle(-12).ToInt64()}";
        File.WriteAllText(Path.Combine(directory, "probe-receipt.tmp"), receipt);
        File.Move(Path.Combine(directory, "probe-receipt.tmp"), Path.Combine(directory, "probe-receipt"));
    }

    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool QueryInformationJobObject(IntPtr job, int kind, IntPtr buffer, uint size, out uint returned);
    [DllImport("kernel32.dll")] private static extern IntPtr GetCurrentProcess();
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool DuplicateHandle(IntPtr sourceProcess, IntPtr source, IntPtr targetProcess, out IntPtr copy, uint access, bool inherit, uint options);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool SetEvent(IntPtr handle);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern Microsoft.Win32.SafeHandles.SafeFileHandle OpenEventW(uint access, bool inherit, string name);
    [DllImport("kernelbase.dll", SetLastError = true)] private static extern bool CompareObjectHandles(Microsoft.Win32.SafeHandles.SafeFileHandle first, Microsoft.Win32.SafeHandles.SafeFileHandle second);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr GetStdHandle(int standard);
}
