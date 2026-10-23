using System.Collections.Concurrent;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Antiphon.Server.Application.Services;

internal sealed record TaskProviderWaitBinding(Guid TaskId, int Attempt, Guid SessionId,
    DateTime DispatchedAt, DateTime StartedAt, string RunnerId, Guid? RunnerStoreId)
{
    internal bool Matches(AgentTask task, AgentSession session) => task.Id == TaskId
        && task.Attempt == Attempt && task.AgentSessionId == SessionId && task.DispatchedAt == DispatchedAt
        && task.Status is AgentTaskStatus.Dispatched or AgentTaskStatus.Working
        && session.Id == SessionId && session.Status == SessionStatus.Running
        && SessionGeneration.Equal(session.StartedAt, StartedAt)
        && NormalizeRunner(session.RunnerId) == RunnerId && session.RunnerStoreId == RunnerStoreId;
    internal static string NormalizeRunner(string? id) => string.IsNullOrWhiteSpace(id)
        || string.Equals(id, "desktop", StringComparison.OrdinalIgnoreCase) ? "" : id;
}

internal sealed record TaskProviderWaitFacts(TaskProviderWaitBinding Binding,
    ProviderDialogObservation Observation, DateTime CompletedAt)
{
    internal bool IsWait => Observation.State == ProviderDialogState.Blocked;
    internal string DialogLabel => Observation.Kind switch
    {
        "ToolPermission" => "permission dialog", "Question" => "question dialog", _ => "provider dialog",
    };
    internal bool Fresh(DateTime now) => now >= CompletedAt && now - CompletedAt <= TimeSpan.FromSeconds(90);
    internal string Diagnosis => $"Mid-turn with a current Claude {DialogLabel}. Tool execution is not confirmed.";
}

/// <summary>Read-only diagnostic facts. No cached observation grants authority to send input.</summary>
public sealed class TaskProviderWaitReader(IServiceScopeFactory scopes, AgentSessionRuntime runtime, TimeProvider clock)
{
    private sealed class Slot
    {
        internal readonly SemaphoreSlim Gate = new(1, 1);
        internal TaskProviderWaitFacts? Facts;
        internal TaskProviderWaitBinding? Binding;
        internal DateTime? LastReadAt;
    }
    private readonly ConcurrentDictionary<Guid, Slot> _slots = new();
    internal Func<Guid, CancellationToken, Task<ProviderDialogObservation?>>? ObserveOverride { get; set; }

    internal async Task<TaskProviderWaitBinding?> BindingAsync(Guid taskId, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var task = await db.AgentTasks.AsNoTracking().SingleOrDefaultAsync(t => t.Id == taskId, ct);
        if (task is null || task.AgentKind != AgentKind.ClaudeCode
            || task.Status is not (AgentTaskStatus.Dispatched or AgentTaskStatus.Working)
            || task.AgentSessionId is not Guid id || id == Guid.Empty
            || task.DispatchedAt is not DateTime dispatch || dispatch == default) return null;
        var session = await db.AgentSessions.AsNoTracking().SingleOrDefaultAsync(s => s.Id == id, ct);
        if (session is null || session.AgentKind != AgentKind.ClaudeCode || session.Status != SessionStatus.Running || session.StartedAt == default) return null;
        var runner = TaskProviderWaitBinding.NormalizeRunner(session.RunnerId);
        if (runner.Length != 0 && (session.RunnerStoreId is null || session.RunnerStoreId == Guid.Empty)) return null;
        return new(task.Id, task.Attempt, id, dispatch, session.StartedAt, runner, session.RunnerStoreId);
    }

    internal async Task<TaskProviderWaitFacts?> CachedAsync(Guid taskId, CancellationToken ct)
    {
        var binding = await BindingAsync(taskId, ct);
        if (binding is null || !_slots.TryGetValue(binding.SessionId, out var slot)) return null;
        var facts = Volatile.Read(ref slot.Facts);
        return facts?.Binding == binding && facts.Fresh(clock.GetUtcNow().UtcDateTime) ? facts : null;
    }

    internal async Task<TaskProviderWaitFacts?> ReadAsync(Guid taskId, bool force, CancellationToken ct)
    {
        var before = await BindingAsync(taskId, ct);
        if (before is null) return null;
        var slot = _slots.GetOrAdd(before.SessionId, _ => new());
        await slot.Gate.WaitAsync(ct);
        try
        {
            if (await BindingAsync(taskId, ct) != before) { slot.Facts = null; return null; }
            var now = clock.GetUtcNow().UtcDateTime;
            if (!force && slot.Binding == before && slot.LastReadAt is DateTime last
                && now >= last && now - last < TimeSpan.FromSeconds(60))
                return slot.Facts is { } cached && cached.Fresh(now) ? cached : null;
            // Invalidate before the await: a failed refresh can never leave an old Blocked visible.
            slot.Facts = null;
            slot.Binding = before;
            ProviderDialogObservation? observation;
            try
            {
                observation = await ProviderDialogObservationService.ReadBoundedAsync(
                    token => ObserveOverride is { } read ? read(before.SessionId, token)
                        : runtime.ObserveDialogAsync(before.SessionId, token), ct);
            }
            finally { slot.LastReadAt = clock.GetUtcNow().UtcDateTime; }
            if (await BindingAsync(taskId, ct) != before || !Valid(observation, before)) return null;
            var facts = new TaskProviderWaitFacts(before, observation!, slot.LastReadAt.Value);
            Volatile.Write(ref slot.Facts, facts);
            return facts;
        }
        finally { slot.Gate.Release(); }
    }

    private static bool Valid(ProviderDialogObservation? observation, TaskProviderWaitBinding binding) =>
        observation is { OutputSequence: >= 0, InputRevision: >= 0, AcceptedStartedAt: { } generation }
        && generation != default
        && ProviderDialogObservationService.Refusal(observation, binding.SessionId, binding.StartedAt)
            is null or ConditionalInputOutcomes.ModalBlocked;
}
