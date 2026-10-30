using System.Diagnostics;
using System.Reflection;
using Porta.Pty;

namespace Antiphon.Agents.Pty;

/// <summary>
/// Reads the sole upstream reaper's signal after its completion barrier. This private contract
/// deliberately fails closed on a package upgrade; the loaded-assembly witness must be requalified.
/// No reflection is performed on Windows or on unqualified Unix platforms.
/// </summary>
internal sealed class PortaExitSignalReader
{
    private const string SignalField = "exitSignal";
    private const string AssemblyVersion = "1.0.7.0";
    private const string InformationalVersion = "1.0.7+54684ba55148ed6bcd0c827ad7e8841a3289a466";
    private readonly Lazy<Accessor?> _accessor;
    private readonly Action<string>? _diagnostic;
    private int _reported;

    internal PortaExitSignalReader(string fieldName = SignalField,
        string assemblyVersion = AssemblyVersion, Action<string>? diagnostic = null)
    {
        _diagnostic = diagnostic;
        _accessor = new Lazy<Accessor?>(() => Resolve(fieldName, assemblyVersion));
    }

    /// <summary>Called only for an exit callback or a successful WaitForExit(0) replay.</summary>
    internal int? ReadCompleted(IPtyConnection connection)
    {
        if (OperatingSystem.IsWindows()) return 0;
        if (!OperatingSystem.IsLinux()) return null;
        try
        {
            // Also defend this seam against a premature caller. The field's initial zero is
            // not evidence. WaitForExit only reads Porta's event; it never calls waitpid again.
            if (!connection.WaitForExit(0)) return null;
            var accessor = _accessor.Value;
            if (accessor is null) return null;
            if (connection.GetType() != accessor.LinuxType)
            {
                ReportUnknown();
                return null;
            }
            if (accessor.Signal.GetValue(connection) is int signal && signal >= 0)
                return signal;
        }
        catch (Exception)
        {
            // Reflection or observer failures must never escape the native exit callback.
        }
        ReportUnknown();
        return null;
    }

    private Accessor? Resolve(string fieldName, string assemblyVersion)
    {
        try
        {
            var assembly = typeof(IPtyConnection).Assembly;
            var unix = assembly.GetType("Porta.Pty.Unix.PtyConnection", throwOnError: false);
            var linux = assembly.GetType("Porta.Pty.Linux.PtyConnection", throwOnError: false);
            var field = unix?.GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (assembly.GetName().Name == "Porta.Pty"
                && assembly.GetName().Version?.ToString() == assemblyVersion
                && assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion == InformationalVersion
                && unix is not null && linux?.BaseType == unix
                && field is { IsPrivate: true, IsStatic: false } && field.FieldType == typeof(int))
                return new Accessor(linux, field);
        }
        catch (Exception)
        {
            // Missing metadata/access is unknown, not observed no-signal.
        }
        ReportUnknown();
        return null;
    }

    private void ReportUnknown()
    {
        if (Interlocked.Exchange(ref _reported, 1) != 0) return;
        const string message = "signal unknown: Porta.Pty Linux exitSignal contract unavailable (expected 1.0.7.0)";
        try
        {
            if (_diagnostic is { } diagnostic) diagnostic(message);
            else Trace.TraceWarning(message);
        }
        catch (Exception) { /* diagnostics cannot break exit publication */ }
    }

    private sealed record Accessor(Type LinuxType, FieldInfo Signal);
}
