namespace Antiphon.Server.Infrastructure.Agents;

/// <summary>
/// CARD-1320 S3. Recurring reminder tick. This stub enqueues nothing; the tests name that gap.
/// The server reminds only. This job does not dispatch.
/// </summary>
public sealed class KeepSixReminderJob
{
    public const string RecurringJobId = "antiphon:keep-six";

    public Task ExecuteAsync(CancellationToken ct) => Task.CompletedTask;
}
