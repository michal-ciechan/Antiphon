using System.ComponentModel;
using System.Diagnostics;
using Antiphon.Server.Application.Interfaces;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// One land-worktree regeneration of <c>tests/derived-impact-index.json</c>.
/// The production path builds the repo's own driver under one build-slot hold, then runs
/// <c>scripts/regenerate-derived-impact-index.ps1 -NoBuild</c> until it prints unchanged.
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
    internal const string ScriptRelative = "scripts/regenerate-derived-impact-index.ps1";
    internal const string ProjectRelative = "scripts/regenerate-derived-impact-index/Antiphon.DerivedImpactIndex.csproj";
    internal const int BudgetSeconds = 180;

    /// <summary>Exactly one conflict, and it is the derived-impact index. Any other shape stays a rebase conflict.</summary>
    internal static bool IsOnlyIndex(IReadOnlyList<string> paths)
    {
        if (paths.Count != 1) return false;
        var path = paths[0].Replace('\\', '/');
        return string.Equals(path, RelativePath, StringComparison.Ordinal);
    }
}

/// <summary>
/// Runs the checkout's generator in the land worktree. A missing slot refuses; this path never builds unleased.
/// One hold covers the driver build and at most two <c>-NoBuild</c> runs. No second solution build.
/// </summary>
internal sealed class DerivedImpactIndexRegenerator(IBuildSlotGate? slots) : IDerivedImpactIndexRegenerator
{
    public async Task<DerivedImpactRegenResult> RegenerateAsync(string landWorktree, CancellationToken ct)
    {
        var started = Environment.TickCount64;
        long Elapsed() => Environment.TickCount64 - started;

        var script = Path.Combine(landWorktree, DerivedImpactIndexRegen.ScriptRelative);
        var project = Path.Combine(landWorktree, DerivedImpactIndexRegen.ProjectRelative);
        if (!File.Exists(script) || !File.Exists(project))
            return new(false, false, "missing-generator", Elapsed());

        // Use the checkout's SDK selection. The dotnet host can be present in a runtime-only image.
        using (var probeBudget = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            probeBudget.CancelAfter(TimeSpan.FromSeconds(10));
            try
            {
                if (!await ToolAvailableAsync(landWorktree, probeBudget.Token, "dotnet", "--version"))
                    return new(false, false, "dotnet-sdk", Elapsed(), ToolchainUnavailable: true);
                if (!await ToolAvailableAsync(landWorktree, probeBudget.Token, "pwsh", "-Version"))
                    return new(false, false, "pwsh", Elapsed(), ToolchainUnavailable: true);
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
                var buildArgs = new List<string>
                {
                    "build", DerivedImpactIndexRegen.ProjectRelative, "-p:UseAppHost=false", "--nologo",
                };
                if (slot.MaxCpuCount > 0)
                    buildArgs.Add("-maxcpucount:" + slot.MaxCpuCount);
                var build = await RunAsync(landWorktree, budget.Token, "dotnet", buildArgs);
                if (budget.IsCancellationRequested && !ct.IsCancellationRequested)
                    return new(false, false, "timeout", Elapsed());
                if (!build.Ok)
                    return new(false, false, "build-failed", Elapsed());

                var changed = false;
                for (var run = 1; run <= 2; run++)
                {
                    var result = await RunAsync(landWorktree, budget.Token, "pwsh",
                        ["-NoProfile", "-File", DerivedImpactIndexRegen.ScriptRelative, "-NoBuild"]);
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

    private static async Task<bool> ToolAvailableAsync(string cwd, CancellationToken ct, string file, string arg)
    {
        ct.ThrowIfCancellationRequested();
        try
        {
            var outcome = await RunAsync(cwd, ct, file, [arg]);
            ct.ThrowIfCancellationRequested();
            return outcome.Ok;
        }
        catch (Win32Exception)
        {
            ct.ThrowIfCancellationRequested();
            return false;
        }
    }

    private static async Task<ProcessOutcome> RunAsync(string cwd, CancellationToken ct, string file, IReadOnlyList<string> args)
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
        return new ProcessOutcome(process.ExitCode == 0, await stdout);
    }

    private readonly record struct ProcessOutcome(bool Ok, string StdOut);
}
