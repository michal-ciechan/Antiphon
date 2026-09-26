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
        var sum = estimatedMinutes.Sum();
        return Math.Max(30, 2 * sum + 10);
    }

    public static async Task<DriverResult> RunWithDeadlineAsync(
        IDriver driver,
        DriverRequest request,
        TimeSpan deadline,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (deadline > TimeSpan.Zero)
            timeout.CancelAfter(deadline);
        try
        {
            var result = await driver.RunAsync(request, timeout.Token).ConfigureAwait(false);
            if (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                driver.Kill(entireProcessTree: true);
                return result with { Killed = true, TimedOut = true, ExitCode = ExitCodes.Timeout };
            }

            return result;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            driver.Kill(entireProcessTree: true);
            return new DriverResult(ExitCodes.Timeout, "", "", Killed: true, TimedOut: true);
        }
    }
}
