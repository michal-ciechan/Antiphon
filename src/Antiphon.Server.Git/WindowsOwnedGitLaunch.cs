using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Antiphon.Server.Infrastructure.Git;

/// <summary>
/// S3a pre-creation transport. No child is created here and no caller selects this path yet.
/// S3b must keep this owner alive through CreateProcessW, then release the child ends promptly.
/// </summary>
internal sealed class WindowsOwnedGitLaunch : IDisposable
{
    private readonly List<IDisposable> _resources = [];
    private bool _disposed;
    private WindowsOwnedGitLaunch() { }
    internal string Executable { get; private set; } = "";
    internal string CommandLine { get; private set; } = "";
    internal string WorkingDirectory { get; private set; } = "";
    internal string EnvironmentBlock { get; private set; } = "";
    internal SafeHandle Job { get; private set; } = null!;
    internal SafeHandle Attributes { get; private set; } = null!;
    internal WindowsOwnedGitPipe Input { get; private set; } = null!;
    internal WindowsOwnedGitPipe Output { get; private set; } = null!;
    internal WindowsOwnedGitPipe Error { get; private set; } = null!;
    internal WindowsOwnedGitStreams Streams { get; private set; } = null!;

    internal static WindowsOwnedGitLaunch Prepare(ProcessStartInfo start, IWindowsOwnedGitLaunchNative native)
    {
        Validate(start);
        var launch = new WindowsOwnedGitLaunch();
        try
        {
            // Validate and encode all user input before allocating native resources.
            launch.Executable = native.ResolveExecutable(start.FileName);
            launch.CommandLine = BuildCommandLine(launch.Executable, start);
            launch.WorkingDirectory = Path.GetFullPath(string.IsNullOrEmpty(start.WorkingDirectory)
                ? Environment.CurrentDirectory : start.WorkingDirectory);
            launch.EnvironmentBlock = BuildEnvironment(start.Environment);
            launch.Job = launch.Own(native.CreateJob());
            launch.Input = launch.Own(native.CreatePipe(parentWrites: true));
            launch.Output = launch.Own(native.CreatePipe(parentWrites: false));
            launch.Error = launch.Own(native.CreatePipe(parentWrites: false));
            launch.Attributes = launch.Own(native.CreateAttributes(launch.Job,
                [launch.Input.Child, launch.Output.Child, launch.Error.Child]));
            launch.Streams = launch.Own(new WindowsOwnedGitStreams(launch.Input.Parent,
                launch.Output.Parent, launch.Error.Parent, start));
            return launch;
        }
        catch
        {
            launch.Dispose();
            // Do not disclose file names, environment or native exception payloads.
            throw new InvalidOperationException("owned_git_windows_prepare_failed");
        }
    }

    private T Own<T>(T value) where T : IDisposable { _resources.Add(value); return value; }

    internal void ReleaseChildHandles()
    {
        // Only call after creation returns, while all attribute buffers are still alive.
        Input.Child.Dispose();
        Output.Child.Dispose();
        Error.Child.Dispose();
        Attributes.Dispose();
    }

    internal static string QuoteArgument(string value)
    {
        RejectNul(value);
        var text = new StringBuilder("\"");
        var slashes = 0;
        foreach (var ch in value)
        {
            if (ch == '\\') { slashes++; continue; }
            text.Append('\\', ch == '"' ? slashes * 2 + 1 : slashes);
            text.Append(ch);
            slashes = 0;
        }
        text.Append('\\', slashes * 2);
        return text.Append('"').ToString();
    }

    internal static string BuildCommandLine(string executable, ProcessStartInfo start)
    {
        RejectNul(executable);
        if (executable.Contains('"')) throw new ArgumentException("owned_git_executable_invalid");
        if (start.ArgumentList.Count != 0 && start.Arguments.Length != 0)
            throw new ArgumentException("owned_git_arguments_conflict");
        var text = new StringBuilder(QuoteArgument(executable));
        foreach (var arg in start.ArgumentList) text.Append(' ').Append(QuoteArgument(arg));
        if (start.Arguments.Length != 0) { RejectNul(start.Arguments); text.Append(' ').Append(start.Arguments); }
        if (text.Length >= 32767) throw new ArgumentException("owned_git_command_too_long");
        return text.ToString();
    }

    internal static string BuildEnvironment(IEnumerable<KeyValuePair<string, string?>> environment)
    {
        var sorted = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in environment)
        {
            RejectNul(key);
            if (key.Length == 0 || key.Contains('=')) throw new ArgumentException("owned_git_environment_key_invalid");
            if (value is null) continue; // Removal, distinct from an empty value.
            RejectNul(value);
            if (!sorted.TryAdd(key, value)) throw new ArgumentException("owned_git_environment_duplicate");
        }
        var block = new StringBuilder();
        foreach (var (key, value) in sorted) block.Append(key).Append('=').Append(value).Append('\0');
        if (block.Length == 0) block.Append('\0');
        return block.Append('\0').ToString();
    }

    private static void Validate(ProcessStartInfo start)
    {
        if (start.UseShellExecute || start.Verb.Length != 0 || start.UserName.Length != 0
            || !start.RedirectStandardInput || !start.RedirectStandardOutput || !start.RedirectStandardError
            || !start.CreateNoWindow)
            throw new ArgumentException("owned_git_windows_options_unsupported");
        if (string.IsNullOrWhiteSpace(start.FileName)) throw new ArgumentException("owned_git_executable_invalid");
        RejectNul(start.FileName);
        RejectNul(start.WorkingDirectory);
        if (OperatingSystem.IsWindows() && (start.Password is not null || start.PasswordInClearText is not null
            || start.Domain.Length != 0 || start.LoadUserProfile))
            throw new ArgumentException("owned_git_windows_credentials_unsupported");
    }

    private static void RejectNul(string value)
    {
        if (value.Contains('\0')) throw new ArgumentException("owned_git_nul_refused");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        // Reverse acquisition order; one failed close cannot suppress the remaining closes.
        for (var i = _resources.Count - 1; i >= 0; i--)
        {
            try { _resources[i].Dispose(); }
            catch (Exception) { }
        }
    }
}

/// <summary>External OS boundary only. Each operation owns and unwinds its partial allocations.</summary>
internal interface IWindowsOwnedGitLaunchNative
{
    string ResolveExecutable(string fileName);
    SafeHandle CreateJob();
    WindowsOwnedGitPipe CreatePipe(bool parentWrites);
    SafeHandle CreateAttributes(SafeHandle job, IReadOnlyList<SafeHandle> inheritedHandles);
}

internal sealed class WindowsOwnedGitPipe(Stream parent, SafeHandle child) : IDisposable
{
    internal Stream Parent { get; } = parent;
    internal SafeHandle Child { get; } = child;
    public void Dispose() { try { Parent.Dispose(); } finally { Child.Dispose(); } }
}

/// <summary>Unselected native preparation; JOB_LIST and HANDLE_LIST buffers survive until disposal.</summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
internal sealed class WindowsOwnedGitLaunchNative : IWindowsOwnedGitLaunchNative
{
    public string ResolveExecutable(string fileName)
    {
        var buffer = new StringBuilder(32768);
        var length = Native.SearchPath(null, fileName, ".exe", buffer.Capacity, buffer, IntPtr.Zero);
        if (length == 0 || length >= buffer.Capacity)
            throw new InvalidOperationException("owned_git_executable_not_found");
        return buffer.ToString();
    }

    public SafeHandle CreateJob()
    {
        // No breakaway, no KILL_ON_JOB_CLOSE; explicit termination belongs to S3b.
        var job = Native.CreateJobObject(IntPtr.Zero, null);
        if (!job.IsInvalid) return job;
        job.Dispose();
        throw new InvalidOperationException("owned_git_job_create_failed");
    }

    public WindowsOwnedGitPipe CreatePipe(bool parentWrites)
    {
        var security = new WindowsGitSecurityAttributes
        {
            Length = Marshal.SizeOf<WindowsGitSecurityAttributes>(), InheritHandle = 1
        };
        // CreatePipe failure leaves its out values indeterminate: do not close those values.
        if (!Native.CreatePipe(out var read, out var write, ref security, 0))
            throw new InvalidOperationException("owned_git_pipe_create_failed");
        var readOwner = new Microsoft.Win32.SafeHandles.SafeFileHandle(read, true);
        var writeOwner = new Microsoft.Win32.SafeHandles.SafeFileHandle(write, true);
        try
        {
            var parent = parentWrites ? writeOwner : readOwner;
            var child = parentWrites ? readOwner : writeOwner;
            if (!Native.SetHandleInformation(parent, 1, 0))
                throw new InvalidOperationException("owned_git_pipe_inheritance_failed");
            // Anonymous CreatePipe handles are synchronous. Async callers use FileStream's
            // synchronous-handle adapter, rather than falsely marking the handle overlapped.
            var stream = new FileStream(parent, parentWrites ? FileAccess.Write : FileAccess.Read,
                4096, isAsync: false);
            return new(stream, child);
        }
        catch { readOwner.Dispose(); writeOwner.Dispose(); throw; }
    }

    public SafeHandle CreateAttributes(SafeHandle job, IReadOnlyList<SafeHandle> inheritedHandles) =>
        WindowsGitAttributeList.Create(job, inheritedHandles);

    [StructLayout(LayoutKind.Sequential)]
    internal struct WindowsGitSecurityAttributes
    {
        internal int Length;
        internal IntPtr SecurityDescriptor;
        internal int InheritHandle;
    }

    internal static class Native
    {
        [DllImport("kernel32.dll", EntryPoint = "SearchPathW", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern uint SearchPath(string? path, string fileName, string extension,
            int bufferLength, StringBuilder buffer, IntPtr filePart);
        [DllImport("kernel32.dll", EntryPoint = "CreateJobObjectW", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern WindowsGitKernelHandle CreateJobObject(IntPtr attributes, string? name);
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool CloseHandle(IntPtr handle);
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool CreatePipe(out IntPtr read, out IntPtr write,
            ref WindowsGitSecurityAttributes attributes, int size);
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetHandleInformation(SafeHandle handle, int mask, int flags);
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool InitializeProcThreadAttributeList(IntPtr list, int count, int flags, ref nuint size);
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool UpdateProcThreadAttribute(IntPtr list, uint flags, nuint attribute,
            IntPtr value, nuint size, IntPtr previous, IntPtr returnedSize);
        [DllImport("kernel32.dll")]
        internal static extern void DeleteProcThreadAttributeList(IntPtr list);
    }
}

[System.Runtime.Versioning.SupportedOSPlatform("windows")]
internal sealed class WindowsGitKernelHandle : Microsoft.Win32.SafeHandles.SafeHandleZeroOrMinusOneIsInvalid
{
    public WindowsGitKernelHandle() : base(true) { }
    protected override bool ReleaseHandle() => WindowsOwnedGitLaunchNative.Native.CloseHandle(handle);
}

[System.Runtime.Versioning.SupportedOSPlatform("windows")]
internal sealed class WindowsGitAttributeList : Microsoft.Win32.SafeHandles.SafeHandleZeroOrMinusOneIsInvalid
{
    private IntPtr _jobs, _inherited;
    private bool _initialized;
    private readonly List<SafeHandle> _pinned = [];
    private WindowsGitAttributeList() : base(true) { }

    internal static WindowsGitAttributeList Create(SafeHandle job, IReadOnlyList<SafeHandle> inherited)
    {
        var owner = new WindowsGitAttributeList();
        try
        {
            nuint size = 0;
            WindowsOwnedGitLaunchNative.Native.InitializeProcThreadAttributeList(IntPtr.Zero, 2, 0, ref size);
            if (size == 0 || size > 1024 * 1024) throw new InvalidOperationException("owned_git_attribute_size_failed");
            owner.SetHandle(Marshal.AllocHGlobal(checked((int)size)));
            if (!WindowsOwnedGitLaunchNative.Native.InitializeProcThreadAttributeList(owner.handle, 2, 0, ref size))
                throw new InvalidOperationException("owned_git_attribute_init_failed");
            owner._initialized = true;
            // Retain native handle lifetimes even if the caller closes its own reference.
            // Acquire after SetHandle: even initialization failure now has a cleanup owner.
            foreach (var item in new[] { job }.Concat(inherited))
            {
                var added = false;
                item.DangerousAddRef(ref added);
                if (added) owner._pinned.Add(item);
            }
            owner._jobs = Marshal.AllocHGlobal(IntPtr.Size);
            Marshal.WriteIntPtr(owner._jobs, job.DangerousGetHandle());
            owner.Update(0x0002000D, owner._jobs, IntPtr.Size); // PROC_THREAD_ATTRIBUTE_JOB_LIST
            owner._inherited = Marshal.AllocHGlobal(checked(inherited.Count * IntPtr.Size));
            for (var i = 0; i < inherited.Count; i++)
                Marshal.WriteIntPtr(owner._inherited, i * IntPtr.Size, inherited[i].DangerousGetHandle());
            owner.Update(0x00020002, owner._inherited, inherited.Count * IntPtr.Size); // HANDLE_LIST
            return owner;
        }
        catch { owner.Dispose(); throw; }
    }

    private void Update(nuint attribute, IntPtr values, int bytes)
    {
        if (!WindowsOwnedGitLaunchNative.Native.UpdateProcThreadAttribute(handle, 0, attribute,
            values, (nuint)bytes, IntPtr.Zero, IntPtr.Zero))
            throw new InvalidOperationException("owned_git_attribute_update_failed");
    }

    protected override bool ReleaseHandle()
    {
        if (_initialized) WindowsOwnedGitLaunchNative.Native.DeleteProcThreadAttributeList(handle);
        if (_jobs != IntPtr.Zero) Marshal.FreeHGlobal(_jobs);
        if (_inherited != IntPtr.Zero) Marshal.FreeHGlobal(_inherited);
        Marshal.FreeHGlobal(handle);
        foreach (var item in _pinned) item.DangerousRelease();
        return true;
    }
}

// S3b fills these structures using original safe process/thread handles. No Process reflection.
[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct WindowsGitStartupInfo
{
    internal int Size;
    internal IntPtr Reserved, Desktop, Title;
    internal uint X, Y, XSize, YSize, XCountChars, YCountChars, FillAttribute, Flags;
    internal ushort ShowWindow, ReservedSize;
    internal IntPtr ReservedBytes, StandardInput, StandardOutput, StandardError;
}

[StructLayout(LayoutKind.Sequential)]
internal struct WindowsGitStartupInfoEx
{
    internal WindowsGitStartupInfo StartupInfo;
    internal IntPtr AttributeList;
}
