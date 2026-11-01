using System.Text.Json;
using Antiphon.TestSupport.LiveClaude;

namespace Antiphon.Agents.Pty.Tests;

internal sealed record StartupExperiment(string Run, string Epoch, string Binary, string Profile, string Settings);
internal sealed record StartupOwner(int Pid, string Started, string Session, string Cwd);
internal sealed record StartupBridge(StartupOwner Owner, string? Id, bool Readable = true);
internal sealed record StartupMarker(string Source, string Session, string Retained);
internal sealed record StartupSettings(IReadOnlyList<StartupMarker> Markers, IReadOnlyList<string> Records);
internal sealed record StartupControl(StartupExperiment Experiment, StartupOwner Owner, StartupBridge? Bridge,
    TimeSpan Latency, int ModelPrompts, StartupSettings Settings, bool Completed = true);
internal sealed record StartupObservation(StartupOwner Owner, StartupBridge? Bridge, TimeSpan Elapsed, StartupSettings Settings);

/// <summary>Diagnostic-only startup decisions over independently observed process/hook facts.</summary>
internal static class LiveClaudeStartupEvidence
{
    public static bool OwnedBridge(StartupOwner owner, StartupBridge? bridge)
        => bridge is { Readable: true } && bridge.Owner == owner && owner.Pid > 0
           && !string.IsNullOrWhiteSpace(owner.Started) && !string.IsNullOrWhiteSpace(owner.Session)
           && !string.IsNullOrWhiteSpace(bridge.Id);

    public static void RequireBridge(StartupOwner owner, StartupBridge? bridge)
    {
        if (!OwnedBridge(owner, bridge)) throw new LiveClaudeRefusal("bridge-unestablished");
    }

    public static async Task<StartupBridge?> EstablishBridgeAsync(StartupOwner owner,
        Func<Task<StartupBridge?>> read, Func<Task<StartupBridge?>> arm)
    {
        var bridge = await read();
        if (!OwnedBridge(owner, bridge)) bridge = await arm();
        RequireBridge(owner, bridge);
        return bridge;
    }

    public static void RequireControl(StartupExperiment experiment, StartupControl? control)
    {
        if (control is null || !control.Completed) throw new LiveClaudeRefusal("premise-absent");
        if (control.Experiment.Run != experiment.Run || control.Experiment.Epoch != experiment.Epoch)
            throw new LiveClaudeRefusal("premise-absent");
        if (control.Experiment.Binary != experiment.Binary) throw new LiveClaudeRefusal("premise-absent");
        if (control.Experiment.Profile != experiment.Profile) throw new LiveClaudeRefusal("premise-absent");
        if (control.Experiment.Settings != experiment.Settings) throw new LiveClaudeRefusal("premise-absent");
        if (control.ModelPrompts != 0) throw new LiveClaudeRefusal("premise-absent");
        if (control.Latency < TimeSpan.Zero || !OwnedBridge(control.Owner, control.Bridge))
            throw new LiveClaudeRefusal("premise-absent");
    }

    public static TimeSpan OffWindow(StartupControl control)
        => control.Latency > TimeSpan.FromSeconds(15) ? control.Latency : TimeSpan.FromSeconds(15);

    // The read occurs before the inclusive deadline test. An event at the deadline is evidence.
    public static async Task<StartupObservation> ObserveOffAsync(StartupOwner owner, StartupControl control,
        Func<TimeSpan> elapsed, Func<Task<StartupBridge?>> read, Func<TimeSpan, Task> advance,
        Func<StartupSettings> settings)
    {
        var window = OffWindow(control);
        while (true)
        {
            var bridge = await read();
            var now = elapsed();
            if (OwnedBridge(owner, bridge) || bridge is { Readable: false } || now >= window)
                return new(owner, bridge, now, settings());
            await advance(window - now < TimeSpan.FromMilliseconds(200) ? window - now : TimeSpan.FromMilliseconds(200));
        }
    }

    public static void RequireOff(StartupExperiment experiment, StartupControl? control, StartupObservation observation)
    {
        RequireControl(experiment, control);
        if (OwnedBridge(observation.Owner, observation.Bridge))
            throw new InvalidOperationException("C1545 startup/off bridge appeared with settings off");
        if (observation.Bridge is { Readable: false }) throw new LiveClaudeRefusal("bridge-unobserved");
        if (observation.Elapsed < OffWindow(control!))
            throw new InvalidOperationException("C1545 startup/window incomplete observation");
        RequireSettings(control!, observation.Owner, observation.Settings);
    }

    public static void RequireSlash(StartupExperiment experiment, StartupControl control,
        StartupObservation before, StartupBridge? after)
    {
        RequireOff(experiment, control, before);
        if (!OwnedBridge(before.Owner, after))
            throw new InvalidOperationException("C1545 startup/slash bridge did not arm");
    }

    public static void RequireSettings(StartupControl control, StartupOwner owner, StartupSettings observed)
    {
        foreach (var source in new[] { "user", "project", "local" })
        {
            var expected = control.Settings.Markers.Where(m => m.Source == source && m.Session == control.Owner.Session).ToArray();
            var actual = observed.Markers.Where(m => m.Source == source && m.Session == owner.Session).ToArray();
            if (expected.Length != 1) throw new LiveClaudeRefusal("settings-unobserved");
            if (actual.Length != 1) throw new InvalidOperationException("C1545 startup/marker observed side lost a control marker");
            if (actual[0].Retained != expected[0].Retained)
                throw new InvalidOperationException("C1545 startup/retained setting changed");
        }
        foreach (var kind in new[] { "permission-mode", "atis-latch" })
            if (control.Settings.Records.Contains(kind) && !observed.Records.Contains(kind))
                throw new InvalidOperationException("C1545 startup/records startup record lost");
    }

    // Read exactly the owned process sidecar, never a same-cwd search or screen badge.
    public static StartupBridge? ReadBridge(string configDir, StartupOwner owner)
    {
        var file = Path.Combine(configDir, "sessions", owner.Pid + ".json");
        try
        {
            if (!File.Exists(file)) return null;
            for (var part = file; part is not null; part = Path.GetDirectoryName(part))
                if ((File.GetAttributes(part) & FileAttributes.ReparsePoint) != 0) throw new IOException();
            if (DateTimeOffset.TryParse(owner.Started, out var started) && File.GetLastWriteTimeUtc(file) < started.UtcDateTime)
                return new(owner, null, false);
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var doc = JsonDocument.Parse(stream);
            var root = doc.RootElement;
            if (root.GetProperty("sessionId").GetString() != owner.Session
                || Path.GetFullPath(root.GetProperty("cwd").GetString()!) != Path.GetFullPath(owner.Cwd)) return new(owner, null, false);
            // PID/start ownership is captured at the native boundary; the sidecar cannot invent it.
            return new(owner, root.TryGetProperty("bridgeSessionId", out var id) ? id.GetString() : null);
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException or KeyNotFoundException or InvalidOperationException or ArgumentException)
        { return new(owner, null, false); }
    }

    public static StartupSettings ReadSettings(string markerFile, IReadOnlyList<string> records)
    {
        try
        {
            var markers = File.ReadAllLines(markerFile).Where(s => !string.IsNullOrWhiteSpace(s)).Select(s =>
            {
                using var doc = JsonDocument.Parse(s); var r = doc.RootElement;
                return new StartupMarker(r.GetProperty("source").GetString()!, r.GetProperty("session").GetString()!, r.GetProperty("retained").GetString()!);
            }).ToArray();
            return new(markers, records);
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException or KeyNotFoundException or InvalidOperationException)
        { throw new LiveClaudeRefusal("settings-unobserved"); }
    }
}

/// <summary>One coordinator per test session, injected into both canary classes.</summary>
public sealed class LiveClaudeStartupFixture
{
    private readonly object _sync = new();
    private Task<StartupControl>? _control;
    private StartupExperiment? _experiment;

    internal Task<StartupControl> ControlAsync(StartupExperiment experiment, Func<Task<StartupControl>> start)
    {
        lock (_sync)
        {
            if (_experiment is not null && _experiment != experiment) throw new LiveClaudeRefusal("premise-absent");
            _experiment = experiment;
            return _control ??= CaptureStartAsync(start);
        }
    }

    private static async Task<StartupControl> CaptureStartAsync(Func<Task<StartupControl>> start) => await start();

    internal async Task<StartupControl> BeforeDependentAsync(StartupExperiment experiment,
        Func<Task<StartupControl>> start, Action dependent)
    {
        var control = await ControlAsync(experiment, start);
        LiveClaudeStartupEvidence.RequireControl(experiment, control);
        dependent();
        return control;
    }
}
