using System.Diagnostics;
using Antiphon.Agents.Pty;
using Antiphon.TestSupport.LiveClaude;

namespace Antiphon.Agents.Pty.Tests;

/// <summary>Owned native boundary shared by CP-5/C514 and the portable reader/verdict witnesses.</summary>
internal sealed class LiveClaudeDeliverySession(PtyAgentRunner runner, LiveClaudeProfile profile, string session)
{
    internal DeliveryBinding Binding => new(session, runner.StartedAt.ToString("O"),
        (runner.Pid ?? throw new LiveClaudeRefusal("process-unobserved")) + ":" + runner.StartedAt.ToString("O"));
    private readonly DateTime started = runner.StartedAt;
    private readonly int? pid = runner.Pid;
    private string? prefix;
    internal DeliveryRequest Capture(string body)
    {
        var bytes = Read();
        _ = LiveClaudeDeliveryEvidence.ReadJsonl(bytes, Binding);
        prefix = bytes;
        return new(Binding, bytes.Count(c => c == '\n'), body);
    }
    private string Read()
    {
        if (runner.Exited.IsCompleted || runner.Pid != pid || runner.StartedAt != started)
            throw new LiveClaudeRefusal("process-changed");
        try
        {
            var path = profile.Paths.FindSessionJsonl(session);
            if (path is null) return "";
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(file);
            var text = reader.ReadToEnd();
            if (prefix is not null && !text.StartsWith(prefix, StringComparison.Ordinal))
                throw new LiveClaudeRefusal("transcript-replaced");
            return text;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        { throw new LiveClaudeRefusal("transcript-unreadable"); }
    }
    internal DeliveryBridge? Bridge()
    {
        if (runner.Exited.IsCompleted || runner.Pid != pid || runner.StartedAt != started)
            return null;
        var owner = new StartupOwner(pid ?? 0, started.ToString("O"), session, profile.Paths.Cwd);
        var bridge = LiveClaudeStartupEvidence.ReadBridge(profile.Paths.ConfigDir, owner);
        return bridge is null ? null : new(bridge.Owner.Pid, bridge.Owner.Started, bridge.Owner.Session, bridge.Id, bridge.Readable);
    }
    internal async Task SendAsync(string body)
    {
        var clock = Stopwatch.StartNew();
        var before = runner.SnapshotScreen();
        DeliveryReadiness State()
        {
            if (runner.Exited.IsCompleted) return DeliveryReadiness.Unknown;
            var screen = runner.SnapshotScreen();
            if (ClaudeBlockingPromptDetector.IsBlocked(screen)) return DeliveryReadiness.Unknown;
            if (ClaudeScreen.IsWorking(screen)) return DeliveryReadiness.Busy;
            return ClaudeScreen.ComposerIsLive(screen) ? DeliveryReadiness.Ready : DeliveryReadiness.Unknown;
        }
        await LiveClaudeDeliveryEvidence.SendOnceAsync(body, State, async () =>
        {
            if (clock.Elapsed >= TimeSpan.FromSeconds(12)) throw new LiveClaudeRefusal("recipient-busy");
            await Task.Delay(100);
        }, text => runner.WriteAsync(text), normalized => runner.WaitForScreenAsync(
            screen => ComposerDeliveryEvidence.IsVisible(before, screen, normalized), TimeSpan.FromSeconds(10)));
    }
    internal async Task<IReadOnlyList<DeliveryRow>> ObserveAsync(DeliveryRequest request, bool completion)
    {
        var clock = Stopwatch.StartNew();
        IReadOnlyList<DeliveryRow> rows = [];
        while (true)
        {
            try
            {
                rows = LiveClaudeDeliveryEvidence.ReadJsonl(Read(), Binding);
                if (completion) LiveClaudeDeliveryEvidence.RequireSubmit(request, rows);
                else _ = LiveClaudeDeliveryEvidence.RequirePrompt(request, rows);
                return rows;
            }
            catch (LiveClaudeRefusal e) when (e.Reason is "prompt-unconfirmed" or "turn-unconfirmed" or "transcript-partial")
            {
                if (clock.Elapsed >= (completion ? TimeSpan.FromMinutes(2) : TimeSpan.FromSeconds(12))) return rows;
                await Task.Delay(100);
            }
        }
    }
}
