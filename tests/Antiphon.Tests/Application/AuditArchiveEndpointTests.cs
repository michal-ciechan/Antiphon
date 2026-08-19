using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0044 slice 4: <c>DELETE /api/audit/archive</c> with <c>olderThanDays</c> omitted must
/// use <c>AuditSettings.RetentionDays</c>, not a hardcoded 90. A dedicated factory seeds a
/// non-default 14-day window so a 20-day-old record is the discriminator.
/// </summary>
[Category("Integration")]
[NotInParallel]
[ClassDataSource<AuditArchiveWebAppFactory>(Shared = SharedType.PerClass)]
public class AuditArchiveEndpointTests
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private readonly AuditArchiveWebAppFactory _factory;

    public AuditArchiveEndpointTests(AuditArchiveWebAppFactory factory) => _factory = factory;

    [Before(Test)]
    public Task ResetAsync() => _factory.ResetAsync();

    [Test]
    public async Task Omitting_olderThanDays_uses_the_configured_RetentionDays_not_a_hardcoded_90()
    {
        var marker = $"aud-ep-{Guid.NewGuid():N}";
        Guid oldId;
        Guid youngId;
        try
        {
            oldId = await SeedAsync(marker, DateTime.UtcNow.AddDays(-20), """{"prompt":"old"}""", " old");
            youngId = await SeedAsync(marker, DateTime.UtcNow.AddDays(-7), """{"prompt":"young"}""", " young");

            using var client = _factory.CreateClient();
            var response = await client.DeleteAsync("/api/audit/archive");
            response.StatusCode.ShouldBe(HttpStatusCode.OK);

            var result = (await response.Content.ReadFromJsonAsync<ArchiveResultDto>(Json))!;
            result.ArchivedCount.ShouldBeGreaterThanOrEqualTo(1);
            result.OlderThan.ShouldBe(DateTime.UtcNow.AddDays(-14), TimeSpan.FromMinutes(1));

            var old = await LoadAsync(oldId);
            old.ShouldNotBeNull();
            old.FullContent.ShouldBeNull("a 20-day-old record is past the configured 14-day window");
            old.Summary.ShouldBe($"{marker} old");

            var young = await LoadAsync(youngId);
            young.ShouldNotBeNull();
            young.FullContent.ShouldNotBeNull("a 7-day-old record is inside the configured 14-day window");
            young.FullContent.ShouldContain("young");
        }
        finally
        {
            await CleanupAsync(marker);
        }
    }

    private async Task<Guid> SeedAsync(string marker, DateTime createdAt, string fullContent, string suffix)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var id = Guid.NewGuid();
        db.AuditRecords.Add(new AuditRecord
        {
            Id = id,
            EventType = AuditEventType.LlmCall,
            ModelName = "test-model",
            TokensIn = 10,
            TokensOut = 20,
            CostUsd = 0.001m,
            DurationMs = 100,
            Summary = $"{marker}{suffix}",
            FullContent = fullContent,
            CreatedAt = createdAt,
        });
        await db.SaveChangesAsync();
        return id;
    }

    private async Task<AuditRecord?> LoadAsync(Guid id)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.AuditRecords.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id);
    }

    private async Task CleanupAsync(string marker)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.AuditRecords.Where(a => a.Summary.StartsWith(marker)).ExecuteDeleteAsync();
    }
}

public sealed class AuditArchiveWebAppFactory : AntiphonWebAppFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["Audit:RetentionDays"] = "14",
            }));
    }
}
