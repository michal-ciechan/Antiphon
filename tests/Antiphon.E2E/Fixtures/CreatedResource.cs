namespace Antiphon.E2E.Fixtures;

internal static class CreatedResource
{
    internal static async Task UseAsync<T>(
        Task<T> createTask, Func<T, Task> useAsync, CancellationToken cancellationToken)
        where T : IDisposable
    {
        try
        {
            var resource = await createTask.WaitAsync(cancellationToken);
            await useAsync(resource);
        }
        finally
        {
            if (createTask.IsCompleted)
            {
                DisposeCompleted(createTask);
            }
            else
            {
                // Cancelling the wait does not cancel creation. Retain ownership of
                // its eventual result without extending the caller's deadline.
                var cleanup = createTask.ContinueWith(DisposeCompleted,
                    CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
                // No caller remains to await a late Dispose failure.
                _ = cleanup.ContinueWith(completed => { _ = completed.Exception; },
                    CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
            }
        }
    }

    private static void DisposeCompleted<T>(Task<T> completed) where T : IDisposable
    {
        if (completed.IsCompletedSuccessfully)
            completed.Result.Dispose();
        else
            _ = completed.Exception; // Observe a creation fault even after its wait was cancelled.
    }
}
