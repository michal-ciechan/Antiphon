using System.Collections.Concurrent;

namespace Antiphon.Server.Application.Services;

public sealed class WorkflowDefinitionVersionGate
{
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _gates = new();

    public async Task<IDisposable> EnterAsync(Guid boardId, CancellationToken ct)
    {
        var gate = _gates.GetOrAdd(boardId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        return new Releaser(gate);
    }

    private sealed class Releaser(SemaphoreSlim gate) : IDisposable
    {
        public void Dispose()
        {
            gate.Release();
        }
    }
}
