using Microsoft.EntityFrameworkCore;

namespace Antiphon.Messaging.Service;

public sealed class MessagingDbContext(DbContextOptions<MessagingDbContext> options) : DbContext(options)
{
    public DbSet<InboxMessage> Inbox => Set<InboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var inbox = modelBuilder.Entity<InboxMessage>();
        inbox.ToTable("Inbox");
        inbox.HasKey(x => x.Id);
        inbox.Property(x => x.Status).HasConversion<string>().HasMaxLength(16);
        inbox.HasIndex(x => new { x.Channel, x.Status });
        inbox.HasIndex(x => x.ReceivedAt);
        inbox.HasIndex(x => new { x.Channel, x.ChannelMessageId }).IsUnique();
    }
}
