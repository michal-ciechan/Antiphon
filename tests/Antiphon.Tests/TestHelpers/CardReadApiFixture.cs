using System.Data.Common;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Antiphon.Tests.TestHelpers;

public sealed class CardReadApiFixture : AntiphonWebAppFactory
{
    private Guid _projectId;
    private int _sequence;
    public CardSnapshotProbe Probe { get; } = new();

    protected override void ApplyTestOverrides(IServiceCollection services)
    {
        services.AddDbContext<AppDbContext>(options => options.AddInterceptors(Probe));
        services.Configure<CardsSettings>(settings =>
        {
            settings.MaxListResults = 500;
            settings.SummaryPreviewChars = 200;
        });
    }

    public override async Task ResetAsync()
    {
        Probe.Disarm();
        await base.ResetAsync();
        _sequence = 0;
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTime.UtcNow;
        var project = new Project
        {
            Id = Guid.NewGuid(), Name = $"Card read {_sequence++} {Guid.NewGuid():N}",
            GitRepositoryUrl = "https://example.test/cards.git",
            LocalRepositoryPath = Path.Combine(Path.GetTempPath(), $"card-read-{Guid.NewGuid():N}"),
            BaseBranch = "main", CreatedAt = now, UpdatedAt = now
        };
        db.Projects.Add(project);
        await db.SaveChangesAsync();
        _projectId = project.Id;
    }

    public sealed class CardSnapshotProbe : DbCommandInterceptor
    {
        private Guid? _board;
        private string? _readerContext;
        private DbDataReader? _metadataReader;
        private DbTransaction? _metadataTransaction;
        private TaskCompletionSource<bool>? _gate;
        private TaskCompletionSource<bool>? _release;
        public bool MetadataClosedAtGate { get; private set; }
        public bool SameTransaction { get; private set; }
        public int GateHits { get; private set; }

        public void Arm(Guid board)
        {
            Disarm();
            _board = board;
            _gate = NewSignal();
            _release = NewSignal();
            MetadataClosedAtGate = false;
            SameTransaction = false;
            GateHits = 0;
        }

        public Task WaitForPageAsync() => (_gate ?? throw new InvalidOperationException("Probe not armed"))
            .Task.WaitAsync(TimeSpan.FromSeconds(15));

        public void Release() => _release?.TrySetResult(true);

        public void Disarm()
        {
            Release();
            _board = null;
            _readerContext = null;
            _metadataReader = null;
            _metadataTransaction = null;
            _gate = null;
            _release = null;
        }

        public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command,
            CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (Scoped(command) && result.FieldCount == 2 &&
                result.GetName(0) == "Id" && result.GetName(1) == "UpdatedAt")
            {
                _readerContext = eventData.Context?.ContextId.ToString();
                _metadataReader = result;
                _metadataTransaction = command.Transaction;
            }
            return new(result);
        }

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (_gate is not null && _readerContext is not null && Scoped(command) &&
                _readerContext == eventData.Context?.ContextId.ToString() &&
                command.CommandText.Contains("LIMIT", StringComparison.OrdinalIgnoreCase))
            {
                GateHits++;
                MetadataClosedAtGate = _metadataReader?.IsClosed == true;
                SameTransaction = _metadataTransaction is not null &&
                    ReferenceEquals(_metadataTransaction, command.Transaction);
                _gate.TrySetResult(true);
                await (_release ?? throw new InvalidOperationException("Probe release missing"))
                    .Task.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken);
            }
            return result;
        }

        private bool Scoped(DbCommand command) => _board is Guid board &&
            command.CommandText.Contains("Cards", StringComparison.Ordinal) &&
            command.Parameters.Cast<DbParameter>().Any(p => p.Value is Guid id && id == board);

        private static TaskCompletionSource<bool> NewSignal() =>
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    public async Task<BoardDetailDto> BoardAsync()
    {
        using var scope = Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<BoardService>().CreateAsync(
            new CreateBoardRequest(_projectId, $"Card read board {Guid.NewGuid():N}"), CancellationToken.None);
    }

    public async Task<IReadOnlyList<Card>> CardsAsync(Guid boardId, int count, string? marker = null,
        bool archived = false, DateTime? firstAt = null)
    {
        await using var db = Writer();
        var column = await db.BoardColumns.Where(c => c.BoardId == boardId && c.StateKey == "backlog")
            .Select(c => c.Id).SingleAsync();
        var start = firstAt ?? DateTime.UtcNow.AddDays(-2);
        var cards = Enumerable.Range(0, count).Select(i => new Card
        {
            Id = Guid.NewGuid(), BoardId = boardId, BoardColumnId = column,
            Identifier = $"CR-{_sequence++:D6}", Title = marker is null ? $"Card {i}" : $"{marker} {i}",
            Description = "body", CreatedAt = start.AddMinutes(-1), UpdatedAt = start.AddSeconds(-i),
            ArchivedAt = archived ? start.AddDays(1) : null
        }).ToList();
        db.Cards.AddRange(cards);
        await db.SaveChangesAsync();
        return cards;
    }

    public AppDbContext Writer() => new(TestDbFixture.CreateDbContextOptions(ConnectionString));

    public async Task<IReadOnlyList<Guid>> OracleAsync(Guid boardId, bool includeArchived = false)
    {
        await using var db = Writer();
        var query = db.Cards.Where(c => c.BoardId == boardId);
        if (!includeArchived) query = query.Where(c => c.ArchivedAt == null);
        return await query.OrderByDescending(c => c.UpdatedAt).ThenBy(c => c.Id)
            .Select(c => c.Id).ToListAsync();
    }

    public async Task CleanupAsync()
    {
        if (_projectId == Guid.Empty) return;
        await using var db = Writer();
        var boardIds = await db.Boards.Where(b => b.ProjectId == _projectId).Select(b => b.Id).ToListAsync();
        var cardIds = await db.Cards.Where(c => boardIds.Contains(c.BoardId)).Select(c => c.Id).ToListAsync();
        await db.AgentSessions.Where(s => s.CardId != null && cardIds.Contains(s.CardId.Value)).ExecuteDeleteAsync();
        await db.CardComments.Where(c => cardIds.Contains(c.CardId)).ExecuteDeleteAsync();
        await db.CardRevisions.Where(r => cardIds.Contains(r.CardId)).ExecuteDeleteAsync();
        await db.Cards.Where(c => cardIds.Contains(c.Id)).ExecuteDeleteAsync();
        await db.BoardWorkflowDefinitions.Where(d => boardIds.Contains(d.BoardId)).ExecuteDeleteAsync();
        await db.BoardColumns.Where(c => boardIds.Contains(c.BoardId)).ExecuteDeleteAsync();
        await db.Boards.Where(b => boardIds.Contains(b.Id)).ExecuteDeleteAsync();
        await db.Projects.Where(p => p.Id == _projectId).ExecuteDeleteAsync();
        _projectId = Guid.Empty;
    }
}
