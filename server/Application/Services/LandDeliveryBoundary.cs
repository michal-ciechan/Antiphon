namespace Antiphon.Server.Application.Services;

/// <summary>Instance-scoped observation seam for durable I/O handoffs. Production has no barriers or dropped wakeups.</summary>
public class LandDeliveryBoundary
{
    public virtual Task ReachedAsync(string boundary, Guid taskId, Guid identity, CancellationToken ct) => Task.CompletedTask;
    public virtual bool DropWakeup(string boundary, Guid identity) => false;
}
