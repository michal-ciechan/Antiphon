using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Channels;
using A2A;
using Microsoft.Extensions.Options;

namespace Antiphon.SessionRunner.Agent2Agent;

/// <summary>
/// A2A handler over headless Codex. One A2A context is one Codex thread; each message is one turn. Turns in a context run
/// one at a time, and at most <see cref="RunnerA2ASettings.MaxConcurrentTurns"/> run at once.
/// </summary>
public sealed class CodexA2AAgent : IAgentHandler
{
    public const string ThreadIdKey = "antiphon.codex.threadId";
    public const string CwdKey = "antiphon.cwd";
    public const string UsageKey = "antiphon.codex.usage";

    private readonly RunnerA2ASettings _settings;
    private readonly CodexTurnRunner _runner;
    private readonly TimeProvider _clock;
    private readonly ILogger<CodexA2AAgent> _log;
    private static readonly TimeSpan StopWait = TimeSpan.FromSeconds(10);
    private readonly ConcurrentDictionary<string, ContextState> _contexts = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, RunningTurn> _running = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _capacity;
    private readonly ITaskStore _store;

    public CodexA2AAgent(IOptions<RunnerA2ASettings> options, ICodexTurnLauncher launcher, ITaskStore store,
        TimeProvider clock, ILogger<CodexA2AAgent> log)
    {
        _settings = options.Value;
        _store = store;
        _clock = clock;
        _log = log;
        _runner = new CodexTurnRunner(_settings, launcher, clock);
        _capacity = new SemaphoreSlim(_settings.MaxConcurrentTurns, _settings.MaxConcurrentTurns);
    }

    internal int ContextCount => _contexts.Count;

    /// <summary>
    /// Stops the task's running turn and waits until the turn has recorded Canceled. False when no turn is running.
    /// The SDK runs the cancel handler only after the turn releases the task's execution lock, so
    /// <see cref="CodexA2ARequestHandler"/> calls this before the SDK's own cancel.
    /// </summary>
    internal async Task<bool> StopTurnAsync(string taskId, CancellationToken cancellationToken)
    {
        if (!_running.TryGetValue(taskId, out var turn)) return false;
        try { await turn.Cancellation.CancelAsync(); }
        catch (ObjectDisposedException) { } // The turn finished between the lookup and the cancel.
        try { await turn.Finished.Task.WaitAsync(StopWait, _clock, cancellationToken); }
        catch (TimeoutException) { _log.LogWarning("A2A task {TaskId}: the Codex turn did not stop within {Seconds}s", taskId, StopWait.TotalSeconds); }
        return true;
    }

    public async Task ExecuteAsync(RequestContext context, AgentEventQueue eventQueue, CancellationToken cancellationToken)
    {
        var updater = new TaskUpdater(eventQueue, context.TaskId, context.ContextId);
        await updater.SubmitAsync(cancellationToken: cancellationToken);

        var prompt = string.Join("\n\n", context.Message.Parts.Where(part => part.Text is not null).Select(part => part.Text));
        if (string.IsNullOrWhiteSpace(prompt))
        {
            await updater.RejectAsync(AgentText("The message has no text part."), cancellationToken: cancellationToken);
            return;
        }

        var state = _contexts.GetOrAdd(context.ContextId, _ => new ContextState());
        state.LastUsed = _clock.GetUtcNow();
        if (!state.Gate.Wait(0))
        {
            await updater.RejectAsync(AgentText("A turn is already running in this context. Wait for it or cancel it."),
                cancellationToken: cancellationToken);
            return;
        }
        try
        {
            if (!_capacity.Wait(0))
            {
                await updater.RejectAsync(AgentText($"All {_settings.MaxConcurrentTurns} turn slots are busy. Try again later."),
                    cancellationToken: cancellationToken);
                return;
            }
            try
            {
                if (state.Cwd is null && Bind(state, context.Message.Metadata) is { } refusal)
                {
                    await updater.RejectAsync(AgentText(refusal), cancellationToken: cancellationToken);
                    return;
                }
                await RunTurnAsync(context, updater, state, prompt, cancellationToken);
            }
            finally { _capacity.Release(); }
        }
        finally
        {
            state.LastUsed = _clock.GetUtcNow();
            state.Gate.Release();
            Evict();
        }
    }

    public async Task CancelAsync(RequestContext context, AgentEventQueue eventQueue, CancellationToken cancellationToken)
    {
        await StopTurnAsync(context.TaskId, cancellationToken);
        await new TaskUpdater(eventQueue, context.TaskId, context.ContextId).CancelAsync(cancellationToken: cancellationToken);
    }

    private async Task RunTurnAsync(RequestContext context, TaskUpdater updater, ContextState state, string prompt,
        CancellationToken cancellationToken)
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var running = new RunningTurn(cancellation);
        _running[context.TaskId] = running;
        try
        {
            await updater.StartWorkAsync(AgentText(state.ThreadId is null ? "Starting a Codex thread." : "Resuming the Codex thread."),
                cancellationToken: cancellationToken);
            // An agent message is reported as progress only once a later item shows it was not the final answer.
            string? pending = null;
            async ValueTask OnEvent(CodexExecEvent item)
            {
                switch (item)
                {
                    case CodexExecEvent.ThreadStarted started:
                        state.ThreadId = started.ThreadId;
                        break;
                    case CodexExecEvent.ItemCompleted { ItemType: "agent_message" } message:
                        if (pending is not null) await updater.StartWorkAsync(AgentText(pending), cancellationToken: cancellation.Token);
                        pending = message.Text;
                        break;
                    case CodexExecEvent.ItemCompleted completed when CodexExecEventParser.Summarize(completed) is { } summary:
                        if (pending is not null) await updater.StartWorkAsync(AgentText(pending), cancellationToken: cancellation.Token);
                        pending = null;
                        await updater.StartWorkAsync(AgentText(summary), cancellationToken: cancellation.Token);
                        break;
                }
            }

            CodexTurnResult result;
            try
            {
                result = await _runner.RunAsync(new CodexTurnRequest(prompt, state.Cwd!, state.ThreadId), OnEvent, cancellation.Token);
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
                // A CancelTask (StopTurnAsync), or the SDK cancelling the run: client gone, host stopping, or its own
                // cancel of a detached run. The SDK records no terminal state for a cancelled run, so record it here.
                await RecordCanceledAsync(updater, context.TaskId);
                return;
            }
            if (result.ThreadId is not null) state.ThreadId = result.ThreadId;

            var metadata = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            if (state.ThreadId is not null) metadata[ThreadIdKey] = JsonSerializer.SerializeToElement(state.ThreadId);
            if (result.Usage is { } usage) metadata[UsageKey] = usage;
            // The stored task keeps artifacts and the status message, not status-update metadata, so the thread id and
            // usage ride on the artifact (or the failure message) as well as on the final update.
            if (result.Succeeded)
            {
                await updater.AddArtifactAsync([Part.FromText(result.FinalMessage ?? "")], name: "response",
                    metadata: metadata, cancellationToken: cancellationToken);
                await updater.CompleteAsync(metadata: metadata, cancellationToken: cancellationToken);
            }
            else
            {
                _log.LogWarning("A2A task {TaskId}: Codex turn failed: {Error} {Stderr}", context.TaskId, result.Error, result.Stderr);
                if (result.FinalMessage is { } last) await updater.StartWorkAsync(AgentText(last), cancellationToken: cancellationToken);
                await updater.FailAsync(AgentText(result.Error!, metadata), metadata, cancellationToken);
            }
        }
        finally
        {
            _running.TryRemove(new KeyValuePair<string, RunningTurn>(context.TaskId, running));
            running.Finished.TrySetResult();
        }
    }

    /// <summary>
    /// Records Canceled through the event queue, or straight into the task store when the SDK has already closed the
    /// queue (a blocking client that disconnected).
    /// </summary>
    private async Task RecordCanceledAsync(TaskUpdater updater, string taskId)
    {
        try
        {
            await updater.CancelAsync(cancellationToken: CancellationToken.None);
            return;
        }
        catch (Exception e) when (e is ChannelClosedException or InvalidOperationException or ObjectDisposedException) { }
        if (await _store.GetTaskAsync(taskId, CancellationToken.None) is not { } task || task.Status.State.IsTerminal()) return;
        task.Status = new A2A.TaskStatus { State = TaskState.Canceled, Timestamp = _clock.GetUtcNow() };
        await _store.SaveTaskAsync(taskId, task, CancellationToken.None);
        _log.LogInformation("A2A task {TaskId}: recorded Canceled after the client's event stream closed", taskId);
    }

    /// <summary>Fixes a new context's working directory and, when allowed, the existing Codex thread it continues.</summary>
    private string? Bind(ContextState state, Dictionary<string, JsonElement>? metadata)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(_settings.WorkspaceRoot!));
        var cwd = root;
        if (metadata?.TryGetValue(CwdKey, out var requested) == true)
        {
            if (requested.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(requested.GetString()))
                return $"{CwdKey} must be a directory path.";
            cwd = Path.TrimEndingDirectorySeparator(Path.GetFullPath(requested.GetString()!, root));
            var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            if (!string.Equals(cwd, root, comparison) && !cwd.StartsWith(root + Path.DirectorySeparatorChar, comparison))
                return $"{CwdKey} must be under the runner's workspace root.";
            if (!Directory.Exists(cwd)) return $"{CwdKey} '{cwd}' does not exist.";
            // A junction or symlink below the root could point anywhere; refuse any link between the root and cwd.
            for (var dir = new DirectoryInfo(cwd); !string.Equals(Path.TrimEndingDirectorySeparator(dir.FullName), root, comparison);
                 dir = dir.Parent!)
                if (dir.Attributes.HasFlag(FileAttributes.ReparsePoint))
                    return $"{CwdKey} must not pass through a link ('{dir.FullName}').";
        }
        if (metadata?.TryGetValue(ThreadIdKey, out var thread) == true)
        {
            if (!_settings.AllowThreadAttach)
                return $"{ThreadIdKey} is refused: {RunnerA2ASettings.SectionName}:AllowThreadAttach is off.";
            if (thread.ValueKind != JsonValueKind.String || !Guid.TryParse(thread.GetString(), out _))
                return $"{ThreadIdKey} must be a Codex thread id.";
            state.ThreadId = thread.GetString();
        }
        state.Cwd = cwd;
        return null;
    }

    /// <summary>Keeps at most MaxContexts contexts by dropping the least recently used idle ones.</summary>
    private void Evict()
    {
        var excess = _contexts.Count - _settings.MaxContexts;
        if (excess <= 0) return;
        foreach (var (id, state) in _contexts.Where(pair => pair.Value.Gate.CurrentCount == 1)
                     .OrderBy(pair => pair.Value.LastUsed).Take(excess).ToArray())
            _contexts.TryRemove(new KeyValuePair<string, ContextState>(id, state));
    }

    private static Message AgentText(string text, Dictionary<string, JsonElement>? metadata = null) => new()
    {
        Role = Role.Agent,
        MessageId = Guid.NewGuid().ToString("N"),
        Parts = [Part.FromText(text)],
        Metadata = metadata,
    };

    private sealed record RunningTurn(CancellationTokenSource Cancellation)
    {
        public TaskCompletionSource Finished { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class ContextState
    {
        public SemaphoreSlim Gate { get; } = new(1, 1);
        public string? Cwd { get; set; }
        public string? ThreadId { get; set; }
        public DateTimeOffset LastUsed { get; set; }
    }
}
