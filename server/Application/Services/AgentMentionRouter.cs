using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Antiphon.Server.Application.Services;

public sealed class AgentMentionRouter : IDisposable
{
    private const int MaxLineChars = 4096;
    private const int PendingMessageMinChars = 8;
    private static readonly TimeSpan PendingMentionDebounce = TimeSpan.FromMilliseconds(300);
    private readonly Channel<MentionRouteCommand> _commands = Channel.CreateBounded<MentionRouteCommand>(
        new BoundedChannelOptions(1024)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });
    private readonly ConcurrentDictionary<Guid, string> _pendingText = new();
    private readonly ConcurrentDictionary<Guid, PendingMentionState> _pendingStates = new();
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly MentionScanner _scanner;
    private readonly ILogger<AgentMentionRouter> _logger;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _worker;

    public AgentMentionRouter(
        IServiceScopeFactory scopeFactory,
        MentionScanner scanner,
        ILogger<AgentMentionRouter> logger)
    {
        _scopeFactory = scopeFactory;
        _scanner = scanner;
        _logger = logger;
        _worker = Task.Run(ProcessAsync);
    }

    public void ObserveDelta(Guid sourceSessionId, string text)
    {
        if (string.IsNullOrEmpty(text))
            return;

        var stripped = MentionScanner.StripAnsi(text);
        var combined = _pendingText.AddOrUpdate(
            sourceSessionId,
            stripped,
            (_, existing) => existing + stripped);
        var lines = combined
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n');
        var completedLineCount = combined.EndsWith('\n') ? lines.Length : Math.Max(0, lines.Length - 1);
        for (var i = 0; i < completedLineCount; i++)
        {
            EnqueueMentions(sourceSessionId, lines[i], skipPendingDuplicate: true);
            _pendingStates.TryRemove(sourceSessionId, out _);
        }

        var remainder = completedLineCount < lines.Length ? lines[^1] : string.Empty;
        if (remainder.Length > MaxLineChars)
            remainder = remainder[^MaxLineChars..];
        _pendingText[sourceSessionId] = remainder;
        SchedulePendingMentions(sourceSessionId, remainder);
    }

    public void Dispose()
    {
        _cts.Cancel();
        _commands.Writer.TryComplete();
        try
        {
            _worker.Wait(TimeSpan.FromSeconds(2));
        }
        catch
        {
            // Best-effort shutdown for background mention routing.
        }
        _cts.Dispose();
    }

    private void EnqueueMentions(Guid sourceSessionId, string line, bool skipPendingDuplicate = false)
    {
        foreach (var mention in _scanner.Extract(line))
        {
            if (skipPendingDuplicate && PendingRouteExists(sourceSessionId, mention))
                continue;

            if (!_commands.Writer.TryWrite(new MentionRouteCommand(sourceSessionId, mention)))
                _logger.LogWarning("Dropped mention route command for source session {SessionId}", sourceSessionId);
        }
    }

    private void SchedulePendingMentions(Guid sourceSessionId, string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            _pendingStates.TryRemove(sourceSessionId, out _);
            return;
        }

        var state = _pendingStates.GetOrAdd(sourceSessionId, _ => new PendingMentionState());
        long version;
        lock (state.Gate)
        {
            state.Text = line;
            version = ++state.Version;
        }

        _ = EnqueuePendingMentionsAfterDebounceAsync(sourceSessionId, version);
    }

    private async Task EnqueuePendingMentionsAfterDebounceAsync(Guid sourceSessionId, long version)
    {
        try
        {
            await Task.Delay(PendingMentionDebounce, _cts.Token);
        }
        catch (OperationCanceledException) when (_cts.IsCancellationRequested)
        {
            return;
        }

        if (!_pendingStates.TryGetValue(sourceSessionId, out var state))
            return;

        string line;
        lock (state.Gate)
        {
            if (state.Version != version)
                return;

            line = state.Text;
        }

        var mentions = _scanner.Extract(line);
        if (mentions.Count == 0)
            return;

        lock (state.Gate)
        {
            if (state.Version != version)
                return;

            foreach (var mention in mentions)
            {
                if (!IsPendingMentionReady(mention))
                    continue;

                var key = PendingRouteKey(mention);
                if (!state.RoutedKeys.Add(key))
                    continue;

                if (!_commands.Writer.TryWrite(new MentionRouteCommand(sourceSessionId, mention)))
                    _logger.LogWarning("Dropped pending mention route command for source session {SessionId}", sourceSessionId);
            }
        }
    }

    private bool PendingRouteExists(Guid sourceSessionId, AgentMention mention)
    {
        if (!_pendingStates.TryGetValue(sourceSessionId, out var state))
            return false;

        lock (state.Gate)
        {
            return state.RoutedKeys.Contains(PendingRouteKey(mention));
        }
    }

    private async Task ProcessAsync()
    {
        try
        {
            await foreach (var command in _commands.Reader.ReadAllAsync(_cts.Token))
            {
                try
                {
                    await using var scope = _scopeFactory.CreateAsyncScope();
                    var service = scope.ServiceProvider.GetRequiredService<AgentChannelService>();
                    await service.RouteMentionAsync(command.SourceSessionId, command.Mention, _cts.Token);
                }
                catch (OperationCanceledException) when (_cts.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to route mention from session {SessionId}", command.SourceSessionId);
                }
            }
        }
        catch (OperationCanceledException) when (_cts.IsCancellationRequested)
        {
        }
    }

    private sealed record MentionRouteCommand(Guid SourceSessionId, AgentMention Mention);

    private sealed class PendingMentionState
    {
        public object Gate { get; } = new();
        public string Text { get; set; } = string.Empty;
        public long Version { get; set; }
        public HashSet<string> RoutedKeys { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsPendingMentionReady(AgentMention mention) =>
        string.IsNullOrWhiteSpace(mention.Message) || mention.Message.Length >= PendingMessageMinChars;

    private static string PendingRouteKey(AgentMention mention) => mention.Target;
}
