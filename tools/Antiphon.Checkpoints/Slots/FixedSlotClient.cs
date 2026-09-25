namespace Antiphon.Checkpoints;

public sealed class FixedSlotClient : IBuildSlotClient
{
    private readonly SlotSession _session;

    public FixedSlotClient(string mode, int maxCpuCount = 4) => _session = new SlotSession(mode, maxCpuCount);

    public Task<SlotSession> ProbeAsync(CancellationToken cancellationToken) => Task.FromResult(_session);

    public Task<SlotLease> AcquireAsync(SlotSession session, string label, CancellationToken cancellationToken) =>
        Task.FromResult(new SlotLease
        {
            State = session.Mode == "off" ? "skipped" : session.Mode,
            MaxCpuCount = session.MaxCpuCount,
        });
}
