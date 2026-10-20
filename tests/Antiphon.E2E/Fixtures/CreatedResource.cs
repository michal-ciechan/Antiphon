namespace Antiphon.E2E.Fixtures;

internal static class CreatedResource
{
    internal static async Task UseAsync<T>(
        Task<T> createTask, Func<T, Task> useAsync, CancellationToken cancellationToken)
        where T : IDisposable
    {
        using var resource = await createTask.WaitAsync(cancellationToken);
        await useAsync(resource);
    }
}
