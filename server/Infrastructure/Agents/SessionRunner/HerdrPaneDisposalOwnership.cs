using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Server.Infrastructure.Agents.SessionRunner;

/// <summary>Shares Start's ordered session locks; no transaction spans runner I/O.</summary>
public sealed class HerdrPaneDisposalOwnership(AppDbContext db, SessionMessageQueueService queue,
    ILaunchOwnership launches) : IHerdrPaneDisposalOwnership
{
    public async Task<IAsyncDisposable> AcquireAsync(HerdrPaneDisposalPreview preview, CancellationToken cancellationToken)
    {
        var ids = preview.Claims.Select(c => c.SessionId)
            .Concat(new[] { preview.ExpectedSessionId, preview.ExpectedNativeSessionId }.OfType<Guid>())
            .Distinct().Order().ToArray();
        var held = new List<SemaphoreSlim>();
        try
        {
            foreach (var id in ids)
            {
                var gate = queue.GetLock(id);
                await gate.WaitAsync(cancellationToken);
                held.Add(gate);
            }
            var pointers = ids.Select(id => id.ToString("D")).ToArray();
            var owners = await db.Agents.AsNoTracking().Where(a => pointers.Contains(a.PersistentSessionId!))
                .Select(a => a.Id).ToArrayAsync(cancellationToken);
            var stopped = await db.AgentSupervisionStates.AsNoTracking()
                .Where(s => owners.Contains(s.AgentId) && s.Suspended).Select(s => s.AgentId).ToArrayAsync(cancellationToken);
            if (owners.Except(stopped).Any() || ids.Any(launches.Owns)
                || await db.AgentSessions.AsNoTracking().AnyAsync(s => ids.Contains(s.Id)
                    && (s.Status == SessionStatus.Starting || s.Status == SessionStatus.Running || s.Status == SessionStatus.Stopping), cancellationToken))
                throw new ConflictException("Stop the current owner and wait for its launch to release before inspecting again.", HerdrProblemTypes.PaneBound);
            return new Lease(held);
        }
        catch { foreach (var gate in held.AsEnumerable().Reverse()) gate.Release(); throw; }
    }

    private sealed class Lease(List<SemaphoreSlim> held) : IAsyncDisposable
    {
        private int _released;
        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
                foreach (var gate in held.AsEnumerable().Reverse()) gate.Release();
            return ValueTask.CompletedTask;
        }
    }
}
