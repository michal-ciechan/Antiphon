using A2A;

namespace Antiphon.SessionRunner.Agent2Agent;

/// <summary>
/// The SDK server, with CancelTask stopping the running Codex turn first. The SDK cancels only detached runs itself and
/// then waits for the task's execution lock, which a blocking SendMessage holds for the whole turn; without this a
/// cancel would wait for the turn to finish and then find the task already completed.
/// </summary>
public sealed class CodexA2ARequestHandler(
    CodexA2AAgent agent, ITaskStore taskStore, ChannelEventNotifier notifier, ILogger<A2AServer> logger,
    A2AServerOptions options, AgentCard agentCard, IPushNotificationStore pushStore,
    IPushNotificationUrlValidator pushUrlValidator, IPushNotificationSender pushSender, TimeProvider clock)
    : A2AServer(agent, taskStore, notifier, logger, options, agentCard, pushStore, pushUrlValidator, pushSender)
{
    private static readonly TimeSpan PersistWait = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan PersistPoll = TimeSpan.FromMilliseconds(25);

    public override async Task<AgentTask> CancelTaskAsync(CancelTaskRequest request, A2ARequestContext? requestContext,
        CancellationToken cancellationToken = default)
    {
        if (await agent.StopTurnAsync(request.Id, cancellationToken))
        {
            // The turn recorded Canceled on its way out; the SDK persists it as it drains the run's events.
            var deadline = clock.GetUtcNow() + PersistWait;
            while (clock.GetUtcNow() < deadline)
            {
                if (await taskStore.GetTaskAsync(request.Id, cancellationToken) is { } task && task.Status.State.IsTerminal())
                    return task;
                await Task.Delay(PersistPoll, clock, cancellationToken);
            }
        }
        return await base.CancelTaskAsync(request, requestContext, cancellationToken);
    }
}
