using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Antiphon.Messaging.Service;

/// <summary>Lets <c>dotnet ef migrations</c> build the context offline (no live database needed).</summary>
public sealed class MessagingDbContextFactory : IDesignTimeDbContextFactory<MessagingDbContext>
{
    public MessagingDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<MessagingDbContext>()
            .UseNpgsql("Host=localhost;Database=antiphon_messaging;Username=antiphon;Password=antiphon")
            .Options;
        return new MessagingDbContext(options);
    }
}
