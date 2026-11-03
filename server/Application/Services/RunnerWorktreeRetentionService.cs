using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Services;

/// <summary>CARD-1677 S1 RED SHIM: the tick sends nothing and records nothing; the green commit selects and sends.</summary>
public sealed class RunnerWorktreeRetentionService
{
    public const string LaneMirror = "RunnerMirror";
    public const string LaneInventory = "RunnerInventory";
    public const string EventPrefix = "runner_mirror_retained";

    public RunnerWorktreeRetentionService(AppDbContext db, ISessionRunnerDirectory runners,
        IOptionsMonitor<RunnerWorktreeRetentionSettings> settings, TimeProvider clock,
        ILogger<RunnerWorktreeRetentionService> logger)
    {
        _ = db; _ = runners; _ = settings; _ = clock; _ = logger;
    }

    public sealed record TickResult(string RunnerId, int Sent, Guid? RunId);

    public Task<IReadOnlyList<TickResult>> RunTickAsync(CancellationToken ct, string? previewRunner = null) =>
        Task.FromResult<IReadOnlyList<TickResult>>([]);

    internal static string CursorKey(Guid taskId) => "runner-retention:" + taskId.ToString("N");
}
