using System.Text;

namespace Antiphon.Tests.TestHelpers;

/// <summary>
/// The one list of owned-child worker modes. A child launched with a worker marker runs that
/// worker against a parent-owned connection and exits before the shared-store warm-up, so it
/// never starts its own container regardless of assembly-hook ordering (CARD-0646).
/// </summary>
internal static class TestWorkerModes
{
    internal sealed record Mode(string Marker, Func<string, Task> RunAsync);

    internal static IReadOnlyList<Mode> All { get; } =
    [
        new(CodexStartupDeliveryWorker.Marker, CodexStartupDeliveryWorker.RunAsync),
        new(PostLandMutationDeliveryWorker.Marker, PostLandMutationDeliveryWorker.RunAsync),
        new(LandQueueRaceWorker.Marker, LandQueueRaceWorker.RunAsync),
        new(CheckCompactionCrashWorker.Marker, CheckCompactionCrashWorker.RunAsync),
        new(LandingRemovalCrashWorker.Marker, LandingRemovalCrashWorker.RunAsync),
    ];

    internal static (Mode Mode, string Payload)? Requested()
    {
        foreach (var mode in All)
        {
            if (Environment.GetEnvironmentVariable(mode.Marker) is { } payload)
                return (mode, payload);
        }

        return null;
    }

    /// <summary>Runs the requested worker and exits: 0 on success, 1 on failure.</summary>
    internal static async Task RunAndExitAsync(Mode mode, string payload)
    {
        try
        {
            await mode.RunAsync(payload);
            Environment.Exit(0);
        }
        catch (Exception ex)
        {
            var text = mode.Marker + " failed (dbLifecycle=" + TestDbFixture.Lifecycle.State + "): "
                + ex.GetType().Name + ": " + ex.Message + Environment.NewLine + ex.StackTrace + Environment.NewLine;
            Console.Error.Write(text);
            Console.Error.Flush();
            try
            {
                var stderr = Console.OpenStandardError();
                var bytes = Encoding.UTF8.GetBytes(text);
                stderr.Write(bytes, 0, bytes.Length);
                stderr.Flush();
            }
            catch
            {
                // Best-effort: Environment.Exit still reports failure.
            }

            Environment.Exit(1);
        }
    }
}
