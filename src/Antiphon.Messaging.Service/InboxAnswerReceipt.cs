namespace Antiphon.Messaging.Service;

public sealed class InboxAnswerReceipt
{
    public string Channel { get; set; } = "";
    public string ConversationId { get; set; } = "";
    public string ChannelMessageId { get; set; } = "";
    public DateTimeOffset AnsweredAt { get; set; }
    public string? OutboundMessageId { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
}

internal sealed class InboxAnswerReconciler(IServiceScopeFactory scopes, ILogger<InboxAnswerReconciler> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        do
        {
            try
            {
                using var scope = scopes.CreateScope();
                await EfInboxReceiptStore.ApplyAnswersAsync(scope.ServiceProvider.GetRequiredService<MessagingDbContext>(),
                    null, null, null, stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Reconciling accepted gateway answers failed");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
