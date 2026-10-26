namespace Antiphon.Tests.Checkpoints;

public abstract partial class CheckpointTestBase
{
    partial void CaptureDiagnostics(IReadOnlyList<string> roots) =>
        CheckpointNativeDiagnostics.Capture(GetType().Name, roots);
}
