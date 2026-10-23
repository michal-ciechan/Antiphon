namespace Antiphon.Checkpoints;

public static class RowTimeout
{
    public static int DeriveRowMinutes(int? estimatedMinutes, int? explicitTimeout, int manifestDefault)
    {
        if (explicitTimeout is > 0)
            return explicitTimeout.Value;
        if (estimatedMinutes is > 0)
            return Math.Max(15, 3 * estimatedMinutes.Value);
        return Math.Max(15, manifestDefault);
    }

    public static int DeriveTotalMinutes(IEnumerable<int> estimatedMinutes, int? explicitTotal)
    {
        if (explicitTotal is > 0)
            return explicitTotal.Value;
        try { return checked((int)Math.Max(30L, checked(2L * estimatedMinutes.Sum(value => (long)value) + 10L))); }
        catch (OverflowException) { throw new ManifestValidationException("total-timeout", "derived execution budget overflow"); }
    }

    public static Task<DriverResult> RunWithDeadlineAsync(
        IDriver driver,
        DriverRequest request,
        TimeSpan deadline,
        CancellationToken cancellationToken) =>
        RunWithDeadlineAsync(driver, request, deadline, TimeProvider.System, cancellationToken);

    internal static async Task<DriverResult> RunWithDeadlineAsync(
        IDriver driver,
        DriverRequest request,
        TimeSpan deadline,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        using var deadlineCancellation = deadline > TimeSpan.Zero
            ? new CancellationTokenSource(deadline, timeProvider)
            : new CancellationTokenSource();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadlineCancellation.Token);
        try
        {
            var result = await driver.RunAsync(request, timeout.Token).ConfigureAwait(false);
            if (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                if (KeepsConfinement(result))
                    return result;
                return result with { Killed = true, TimedOut = true, ExitCode = ExitCodes.Timeout };
            }

            return result;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new DriverResult(ExitCodes.Timeout, "", "", Killed: true, TimedOut: true);
        }
    }

    private static bool KeepsConfinement(DriverResult result)
    {
        if (result.ExitCode != ExitCodes.Invalid && result.ExitCode != ExitCodes.RestoreFailed)
            return false;
        var text = result.Stdout + "\n" + result.Stderr;
        return text.Contains("refused: tampered", StringComparison.Ordinal)
            || text.Contains("refused: restore-failed", StringComparison.Ordinal);
    }
}
