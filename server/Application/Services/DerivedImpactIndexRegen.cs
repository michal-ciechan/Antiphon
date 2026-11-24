using System.ComponentModel;
using System.Diagnostics;
using Antiphon.Server.Application.Interfaces;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// One land-worktree regeneration of <c>tests/derived-impact-index.json</c>.
/// The production path builds the repo's own driver under one build-slot hold, then runs it
/// (<c>dotnet run --project &lt;driver&gt; --no-build</c>) until it prints unchanged. No PowerShell.
/// </summary>
internal interface IDerivedImpactIndexRegenerator
{
    Task<DerivedImpactRegenResult> RegenerateAsync(string landWorktree, CancellationToken ct);
}

/// <summary><paramref name="Reason"/> is a short unprefixed reason.</summary>
internal sealed record DerivedImpactRegenResult(bool Succeeded, bool Changed, string? Reason,
    long Milliseconds, bool ToolchainUnavailable = false);

internal static class DerivedImpactIndexRegen
{
    internal const string RelativePath = "tests/derived-impact-index.json";
    internal const string CommitMessage = "chore(land): regenerate the derived impact index";
    internal const string FailurePrefix = "index-regen-failed:";
    internal const string UnavailablePrefix = "index-regen-unavailable:";
    internal const string ProjectRelative = "scripts/regenerate-derived-impact-index/Antiphon.DerivedImpactIndex.csproj";
    internal const int BudgetSeconds = 180;

    /// <summary>The driver build, in the land worktree: the checkout's SDK, no apphost, the slot's CPU grant when it has one.</summary>
    internal static IReadOnlyList<string> BuildArguments(int maxCpuCount)
    {
        var args = new List<string> { "build", ProjectRelative, "-p:UseAppHost=false", "--nologo" };
        if (maxCpuCount > 0)
            args.Add("-maxcpucount:" + maxCpuCount);
        return args;
    }

    /// <summary>
    /// One driver run over the land worktree, as <c>scripts/antiphon regenerate-derived-impact-index --no-build</c> runs it:
    /// <c>dotnet run</c> rather than <c>dotnet exec</c> of a fixed dll path, so the worktree's own project decides the output
    /// path (its target framework and configuration can differ from the ones this server was built with).
    /// </summary>
    internal static IReadOnlyList<string> RunArguments(string landWorktree) =>
        ["run", "--project", ProjectRelative, "--no-build", "-p:UseAppHost=false", "--", Path.GetFullPath(landWorktree)];

    /// <summary>Exactly one conflict, and it is the derived-impact index. Any other shape stays a rebase conflict.</summary>
    internal static bool IsOnlyIndex(IReadOnlyList<string> paths)
    {
        if (paths.Count != 1) return false;
        var path = paths[0].Replace('\\', '/');
        return string.Equals(path, RelativePath, StringComparison.Ordinal);
    }
}

/// <summary>What one child did: whether it exited 0, and its standard output.</summary>
internal readonly record struct DerivedImpactRegenProcess(bool Ok, string StdOut);

/// <summary>
/// Runs the checkout's generator in the land worktree. A missing slot refuses; this path never builds unleased.
/// One hold covers the driver build and at most two <c>--no-build</c> driver runs. No second solution build.
/// <paramref name="run"/> starts one child (null: a real process); a test records the argv instead.
/// </summary>
internal sealed class DerivedImpactIndexRegenerator(IBuildSlotGate? slots, DerivedImpactIndexRegenerator.ChildRunner? run = null)
    : IDerivedImpactIndexRegenerator
{
    /// <summary>Starts <paramref name="file"/> with <paramref name="args"/> in <paramref name="cwd"/> and waits for it.</summary>
    internal delegate Task<DerivedImpactRegenProcess> ChildRunner(string cwd, string file, IReadOnlyList<string> args, CancellationToken ct);

    private readonly ChildRunner _run = run ?? RunAsync;

    public async Task<DerivedImpactRegenResult> RegenerateAsync(string landWorktree, CancellationToken ct)
    {
        var started = Environment.TickCount64;
        long Elapsed() => Environment.TickCount64 - started;

        var project = Path.Combine(landWorktree, DerivedImpactIndexRegen.ProjectRelative);
        if (!File.Exists(project))
            return new(false, false, "missing-generator", Elapsed());

        // Use the checkout's SDK selection. The dotnet host can be present in a runtime-only image.
        using (var probeBudget = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            probeBudget.CancelAfter(TimeSpan.FromSeconds(10));
            try
            {
                if (!await ToolAvailableAsync(landWorktree, "dotnet", "--version", probeBudget.Token))
                    return new(false, false, "dotnet-sdk", Elapsed(), ToolchainUnavailable: true);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                return new(false, false, "toolchain-check-timeout", Elapsed(), ToolchainUnavailable: true);
            }
        }
        var probeMilliseconds = Elapsed();
        if (slots is null)
            return new(false, false, "slot-missing", Elapsed());

        var name = Path.GetFileName(landWorktree.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.IsNullOrEmpty(name)) name = "land";
        BuildSlotHold? slot = null;
        try
        {
            slot = await slots.AcquireAsync("land-index-regen " + name, static _ => { }, ct);
            if (slot.Outcome == BuildSlotHoldOutcome.Timeout)
                return new(false, false, "slot-timeout", Elapsed());
            if (slot.Outcome == BuildSlotHoldOutcome.Unleased)
                return new(false, false, "slot-unleased", Elapsed());
            if (slot.Outcome is not (BuildSlotHoldOutcome.Granted or BuildSlotHoldOutcome.Unlimited))
                return new(false, false, "slot-missing", Elapsed());

            using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
            budget.CancelAfter(TimeSpan.FromMilliseconds(Math.Max(0,
                DerivedImpactIndexRegen.BudgetSeconds * 1000 - probeMilliseconds)));
            try
            {
                var build = await _run(landWorktree, "dotnet", DerivedImpactIndexRegen.BuildArguments(slot.MaxCpuCount), budget.Token);
                if (budget.IsCancellationRequested && !ct.IsCancellationRequested)
                    return new(false, false, "timeout", Elapsed());
                if (!build.Ok)
                    return new(false, false, "build-failed", Elapsed());

                var changed = false;
                var driverArgs = DerivedImpactIndexRegen.RunArguments(landWorktree);
                for (var attempt = 1; attempt <= 2; attempt++)
                {
                    var result = await _run(landWorktree, "dotnet", driverArgs, budget.Token);
                    if (budget.IsCancellationRequested && !ct.IsCancellationRequested)
                        return new(false, false, "timeout", Elapsed());
                    if (!result.Ok)
                        return new(false, false, "nonzero", Elapsed());
                    if (result.StdOut.Contains("derived-impact-index unchanged", StringComparison.Ordinal))
                        return new(true, changed, null, Elapsed());
                    if (!result.StdOut.Contains("derived-impact-index changed", StringComparison.Ordinal))
                        return new(false, false, "unexpected-output", Elapsed());
                    changed = true;
                }
                return new(false, false, "still-changed", Elapsed());
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                return new(false, false, "timeout", Elapsed());
            }
            catch (Exception)
            {
                return new(false, false, "nonzero", Elapsed());
            }
        }
        finally
        {
            if (slot is not null)
                await slots.ReleaseAsync(slot, static _ => { }, CancellationToken.None);
        }
    }

    private async Task<bool> ToolAvailableAsync(string cwd, string file, string arg, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        try
        {
            var outcome = await _run(cwd, file, [arg], ct);
            ct.ThrowIfCancellationRequested();
            return outcome.Ok;
        }
        catch (Win32Exception)
        {
            ct.ThrowIfCancellationRequested();
            return false;
        }
    }

    private static async Task<DerivedImpactRegenProcess> RunAsync(string cwd, string file, IReadOnlyList<string> args, CancellationToken ct)
    {
        var start = new ProcessStartInfo
        {
            FileName = file,
            WorkingDirectory = cwd,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var arg in args)
            start.ArgumentList.Add(arg);
        // Policy 0 before Apply, so the shared Linux compiler path does not print LINUX COMPILER.
        start.Environment["ANTIPHON_LINUX_COMPILER_POLICY"] = "0";
        new Antiphon.Build.BuildDriverEnvironment().Apply(start);
        start.Environment["ANTIPHON_LINUX_COMPILER_POLICY"] = "0";
        using var process = Process.Start(start) ?? throw new InvalidOperationException("runner-start");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync(ct);
        }
        catch
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None);
            try { await Task.WhenAll(stdout, stderr); } catch (Exception) { }
            throw;
        }
        await Task.WhenAll(stdout, stderr);
        return new DerivedImpactRegenProcess(process.ExitCode == 0, await stdout);
    }
}
