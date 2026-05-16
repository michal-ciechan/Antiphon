using Antiphon.Server.Application.Interfaces;

namespace Antiphon.Server.Infrastructure.WorkflowDefinitions;

public sealed class WorkflowFileSystemWatcher : IFileSystemWatcher
{
    private readonly object _gate = new();
    private readonly Dictionary<Guid, System.IO.FileSystemWatcher> _watchers = [];

    public event EventHandler<WorkflowFileChangedEventArgs>? Changed;

    public void Watch(Guid boardId, string directoryPath, string fileName)
    {
        if (string.IsNullOrWhiteSpace(directoryPath) || string.IsNullOrWhiteSpace(fileName))
            return;

        if (!Directory.Exists(directoryPath))
            return;

        lock (_gate)
        {
            if (_watchers.TryGetValue(boardId, out var existing))
            {
                if (PathsEqual(existing.Path, directoryPath) && existing.Filter == fileName)
                    return;

                existing.Dispose();
                _watchers.Remove(boardId);
            }

            var watcher = new System.IO.FileSystemWatcher(directoryPath, fileName)
            {
                IncludeSubdirectories = false,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                EnableRaisingEvents = true
            };
            watcher.Changed += (_, args) => OnChanged(boardId, args.FullPath);
            watcher.Created += (_, args) => OnChanged(boardId, args.FullPath);
            watcher.Renamed += (_, args) => OnChanged(boardId, args.FullPath);
            _watchers[boardId] = watcher;
        }
    }

    public void Unwatch(Guid boardId)
    {
        lock (_gate)
        {
            if (!_watchers.Remove(boardId, out var watcher))
                return;

            watcher.Dispose();
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            foreach (var watcher in _watchers.Values)
                watcher.Dispose();

            _watchers.Clear();
        }
    }

    private void OnChanged(Guid boardId, string path)
    {
        Changed?.Invoke(this, new WorkflowFileChangedEventArgs(boardId, path));
    }

    private static bool PathsEqual(string left, string right)
    {
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return Path.GetFullPath(left).Equals(Path.GetFullPath(right), comparison);
    }
}
