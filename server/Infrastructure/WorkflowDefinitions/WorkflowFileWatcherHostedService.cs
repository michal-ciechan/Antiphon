using System.Threading.Channels;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Server.Infrastructure.WorkflowDefinitions;

public sealed class WorkflowFileWatcherHostedService : BackgroundService
{
    private static readonly TimeSpan DebounceDelay = TimeSpan.FromMilliseconds(250);

    private readonly IFileSystemWatcher _watcher;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<WorkflowFileWatcherHostedService> _logger;
    private readonly Channel<Guid> _changes = Channel.CreateUnbounded<Guid>();

    public WorkflowFileWatcherHostedService(
        IFileSystemWatcher watcher,
        IServiceScopeFactory scopeFactory,
        ILogger<WorkflowFileWatcherHostedService> logger)
    {
        _watcher = watcher;
        _scopeFactory = scopeFactory;
        _logger = logger;
        _watcher.Changed += OnWorkflowFileChanged;
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _watcher.Changed -= OnWorkflowFileChanged;
        await base.StopAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RefreshWatchedBoardsAsync(stoppingToken);

        await foreach (var firstBoardId in _changes.Reader.ReadAllAsync(stoppingToken))
        {
            await Task.Delay(DebounceDelay, stoppingToken);
            var boardIds = new HashSet<Guid> { firstBoardId };
            while (_changes.Reader.TryRead(out var boardId))
                boardIds.Add(boardId);

            foreach (var boardId in boardIds)
                await ReloadBoardAsync(boardId, stoppingToken);
        }
    }

    private async Task RefreshWatchedBoardsAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var loader = scope.ServiceProvider.GetRequiredService<WorkflowDefinitionLoader>();
        var boardIds = await db.Boards
            .AsNoTracking()
            .Select(b => b.Id)
            .ToListAsync(ct);

        foreach (var boardId in boardIds)
        {
            try
            {
                await loader.ReloadFromFileAsync(boardId, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Failed to reconcile WORKFLOW.md for board {BoardId}", boardId);
            }
        }
    }

    private async Task ReloadBoardAsync(Guid boardId, CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var loader = scope.ServiceProvider.GetRequiredService<WorkflowDefinitionLoader>();
            await loader.ReloadFromFileAsync(boardId, ct);

            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var fileStore = scope.ServiceProvider.GetRequiredService<IWorkflowFileStore>();
            var board = await db.Boards
                .AsNoTracking()
                .Include(b => b.Project)
                .FirstOrDefaultAsync(b => b.Id == boardId, ct);
            if (board is not null)
                WatchBoard(board, fileStore);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Failed to hot reload WORKFLOW.md for board {BoardId}", boardId);
        }
    }

    private void WatchBoard(Antiphon.Server.Domain.Entities.Board board, IWorkflowFileStore fileStore)
    {
        var path = fileStore.GetWorkflowFilePath(board);
        if (path is null)
            return;

        var directory = Path.GetDirectoryName(path);
        var fileName = Path.GetFileName(path);
        if (string.IsNullOrWhiteSpace(directory) || string.IsNullOrWhiteSpace(fileName))
            return;

        _watcher.Watch(board.Id, directory, fileName);
    }

    private void OnWorkflowFileChanged(object? sender, WorkflowFileChangedEventArgs args)
    {
        _changes.Writer.TryWrite(args.BoardId);
    }
}
