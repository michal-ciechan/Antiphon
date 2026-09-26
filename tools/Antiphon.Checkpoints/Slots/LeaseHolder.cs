namespace Antiphon.Checkpoints;

/// <summary>
/// One live process whose pid the build-slot broker treats as the lease holder.
/// A pid-idempotent broker returns the same lease for the same pid, so each
/// in-flight driver needs its own holder.
/// </summary>
public interface ILeaseHolder : IAsyncDisposable
{
    int Pid { get; }

    string? ProcessStartUtc { get; }
}

public interface ILeaseHolderSource
{
    ILeaseHolder Open();
}

public sealed class ProcessLeaseHolderSource : ILeaseHolderSource
{
    public ILeaseHolder Open() => ProcessLeaseHolder.Start(Environment.ProcessId);
}

public sealed class ProcessLeaseHolder : ILeaseHolder
{
    private readonly Process _process;

    private ProcessLeaseHolder(Process process) => _process = process;

    public int Pid => _process.Id;

    public string? ProcessStartUtc => null;

    public static ProcessLeaseHolder Start(int parentPid)
    {
        var dll = Path.Combine(AppContext.BaseDirectory, "Antiphon.Checkpoints.dll");
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        psi.ArgumentList.Add(dll);
        psi.ArgumentList.Add("hold");
        psi.ArgumentList.Add("--parent");
        psi.ArgumentList.Add(parentPid.ToString(System.Globalization.CultureInfo.InvariantCulture));
        var process = Process.Start(psi) ?? throw new InvalidOperationException("slot holder did not start");
        return new ProcessLeaseHolder(process);
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (!_process.HasExited)
                _process.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
        }

        try
        {
            using var wait = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await _process.WaitForExitAsync(wait.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is OperationCanceledException or InvalidOperationException)
        {
        }

        _process.Dispose();
    }
}
