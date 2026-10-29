namespace Antiphon.NativeLinuxPolicy.Probe;

internal static class ProbeLog
{
    internal static int Touch(string token)
    {
        if (!OperatingSystem.IsLinux())
            throw new InvalidOperationException("marked node ran off Linux: " + token);
        var path = Environment.GetEnvironmentVariable("NATIVE_LINUX_PROBE_SENTINEL");
        if (string.IsNullOrEmpty(path))
            throw new InvalidOperationException("NATIVE_LINUX_PROBE_SENTINEL is unset");
        lock (Gate)
            File.AppendAllText(path, token + "\n");
        return 1;
    }

    internal static void Portable(string token)
    {
        var path = Environment.GetEnvironmentVariable("NATIVE_LINUX_PROBE_SENTINEL");
        if (string.IsNullOrEmpty(path))
            throw new InvalidOperationException("NATIVE_LINUX_PROBE_SENTINEL is unset");
        lock (Gate)
            File.AppendAllText(path, token + "\n");
    }

    private static readonly object Gate = new();
}
