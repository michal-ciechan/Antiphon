namespace Antiphon.HostCleanup.Diagnostics;

// S1 red scaffold: compile-capable, deliberately cannot identify a real holder yet.
public sealed class FileUseCapture
{
    public Task<FileUseSnapshot> TryCaptureAsync(FileUseRequest request, CancellationToken cancellationToken = default) =>
        Task.FromResult(new FileUseSnapshot(Guid.NewGuid().ToString("N"), FileUseStatus.Unavailable,
            FileUseCoverage.None, request.Path, request.Error.GetType().Name, request.Error.HResult,
            "diagnostic unavailable", request.FailureUtc, DateTimeOffset.UtcNow, [], [FileUseReason.UnsupportedPlatform]));
}
