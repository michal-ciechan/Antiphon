using Porta.Pty;

namespace Antiphon.Agents.Pty;

/// <summary>Fail-closed signal seam for the pinned Linux Porta wait observation.</summary>
internal sealed class PortaExitSignalReader(
    string fieldName = "exitSignal",
    string assemblyVersion = "1.0.7.0",
    Action<string>? diagnostic = null)
{
    internal int? ReadCompleted(IPtyConnection connection)
    {
        // Compile-compatible unknown evidence until the pinned accessor is implemented.
        _ = fieldName;
        _ = assemblyVersion;
        diagnostic?.Invoke("signal unknown: Porta exit signal is unavailable");
        return null;
    }
}
