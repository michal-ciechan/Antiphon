using System.Text.Json;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public class CardPrivateNotesBoundaryTests
{
    [Test]
    public async Task Failed_policy_probe_after_save_retains_created_card_and_reports_status_unavailable()
    {
        await using var world = new CardFilePrivacyWorld();
        await world.InitializeAsync(false);
        var options = new DbContextOptionsBuilder<Antiphon.Server.Infrastructure.Data.AppDbContext>(TestDbFixture.CreateDbContextOptions())
            .AddInterceptors(new FailBoardProbeAfterInsert()).Options;
        await using var db = new Antiphon.Server.Infrastructure.Data.AppDbContext(options);
        var service = new CardService(db, null!, null!, null!, new MockEventBus(), TimeProvider.System, null!, cardFiles: world.Service(db));
        var created = await service.CreateAsync(world.BoardId, new CreateCardRequest(null, "public", PrivateNotes: "C408_CREATE_PRIVATE"), default);
        created.CardFileStatus!.Reason.ShouldBe("status_unavailable");
        created.HasPrivateNotes.ShouldBeTrue();
        (await db.Cards.CountAsync(c => c.BoardId == world.BoardId)).ShouldBe(1);
    }

    [Test]
    public async Task Content_write_DTO_revision_event_and_log_exclude_private_notes()
    {
        await using var world = new CardFilePrivacyWorld();
        await world.InitializeAsync(false);
        var id = await world.AddCardAsync();
        await using var db = world.Db();
        var card = await db.Cards.SingleAsync(c => c.Id == id);
        var events = new MockEventBus(); var log = new CaptureLogger();
        var service = new CardService(db, null!, null!, null!, events, TimeProvider.System, null!, logger: log, cardFiles: world.Service(db));
        var result = await service.UpdateContentAsync(id, new UpdateCardContentRequest(card.ConcurrencyToken, "public correction", PrivateNotes: "C408_BOUNDARY_PRIVATE"), default);
        JsonSerializer.Serialize(result).ShouldNotContain("C408_BOUNDARY_PRIVATE");
        JsonSerializer.Serialize(await service.GetRevisionsAsync(id, default)).ShouldNotContain("C408_NOTE_CURRENT");
        JsonSerializer.Serialize(events.PublishedEvents).ShouldNotContain("C408_BOUNDARY_PRIVATE");
        string.Join("\n", log.Messages).ShouldNotContain("C408_BOUNDARY_PRIVATE");
        (await service.GetPrivateNotesAsync(id, null, default)).PrivateNotes.ShouldBe("C408_BOUNDARY_PRIVATE");
    }
    private sealed class CaptureLogger : ILogger<CardService>
    {
        public List<string> Messages { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception, Func<TState, Exception?, string> formatter) => Messages.Add(formatter(state, exception));
    }

    private sealed class FailBoardProbeAfterInsert : Microsoft.EntityFrameworkCore.Diagnostics.DbCommandInterceptor
    {
        private bool _saved;
        public override ValueTask<Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<System.Data.Common.DbDataReader>> ReaderExecutingAsync(
            System.Data.Common.DbCommand command, Microsoft.EntityFrameworkCore.Diagnostics.CommandEventData eventData,
            Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<System.Data.Common.DbDataReader> result, CancellationToken ct = default)
        {
            if (command.CommandText.Contains("INSERT INTO \"Cards\"")) _saved = true;
            else if (_saved && command.CommandText.Contains("FROM \"Boards\"")) throw new IOException("C408_SYNTHETIC_PROBE_FAILURE");
            return ValueTask.FromResult(result);
        }
    }
}
