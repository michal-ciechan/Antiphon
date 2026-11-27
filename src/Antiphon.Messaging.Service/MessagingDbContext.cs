using Microsoft.EntityFrameworkCore;

namespace Antiphon.Messaging.Service;

public sealed class MessagingDbContext(DbContextOptions<MessagingDbContext> options) : DbContext(options)
{
    public DbSet<InboxMessage> Inbox => Set<InboxMessage>();

    public DbSet<InboxAnswerReceipt> InboxAnswerReceipts => Set<InboxAnswerReceipt>();
    public DbSet<GatewayOutboundOperation> GatewayOutboundOperations => Set<GatewayOutboundOperation>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<InboxAnswerReceipt>().HasKey(a => new { a.Channel, a.ConversationId, a.ChannelMessageId });
        modelBuilder.Entity<InboxAnswerReceipt>().HasIndex(a => a.PublishedAt).HasFilter("\"PublishedAt\" IS NULL");
        var inbox = modelBuilder.Entity<InboxMessage>();
        inbox.ToTable("Inbox");
        inbox.HasKey(x => x.Id);
        inbox.Property(x => x.Status).HasConversion<string>().HasMaxLength(16);
        inbox.HasIndex(x => new { x.Channel, x.Status });
        inbox.HasIndex(x => x.ReceivedAt);
        inbox.HasIndex(x => new { x.Channel, x.ConversationId, x.ChannelMessageId }).IsUnique();
        inbox.Property(x => x.Topic).HasMaxLength(200);
        inbox.HasIndex(x => new { x.Topic, x.Partition, x.Offset });
    }
}
