namespace Antiphon.Server.Application.Interfaces;

public interface IFileSystemWatcher : IDisposable
{
    event EventHandler<WorkflowFileChangedEventArgs>? Changed;

    void Watch(Guid boardId, string directoryPath, string fileName);

    void Unwatch(Guid boardId);
}

public sealed class WorkflowFileChangedEventArgs : EventArgs
{
    public WorkflowFileChangedEventArgs(Guid boardId, string path)
    {
        BoardId = boardId;
        Path = path;
    }

    public Guid BoardId { get; }

    public string Path { get; }
}
