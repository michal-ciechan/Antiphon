using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace Antiphon.Server.Infrastructure.Verification;

/// <summary>
/// The process the server starts to validate one receipt, from <c>CheckpointReceipts:ValidatorScript</c> (N2, unit 59 of
/// docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md). The setting names one of:
/// <list type="bullet">
/// <item>a built <c>Antiphon.Checkpoints.dll</c> (the tests): <c>dotnet exec &lt;dll&gt; validate ...</c>, 30 s;</item>
/// <item>a checkout's CLI entry, <c>&lt;checkout&gt;/scripts/antiphon</c> (or <c>scripts/antiphon.cmd</c>), or the older
/// <c>&lt;checkout&gt;/scripts/validate-checkpoint-receipt.ps1</c>, which names the same checkout: the CLI's
/// <c>checkpoints validate</c> verb, with no PowerShell. Off Windows, <c>/bin/sh scripts/antiphon</c> (argv reaches the
/// CLI as given; the launcher bootstraps itself). On Windows, the published CLI the launchers run
/// (<c>bin-launcher/antiphon-scripts/current</c>) with <c>dotnet exec</c> and the arguments as a list, so no cmd.exe parses
/// the receipt path, and <c>ANTIPHON_CLI_CHECK</c> set to the checkout as the launchers set it (a stale CLI rebuilds
/// itself first); with nothing published yet the server bootstraps it first, as <c>scripts\antiphon.cmd</c> does (the
/// launcher project into a fresh <c>boot-&lt;guid&gt;</c>, then its <c>publish</c>). 5 minutes: a first call may build the
/// CLI and the checkpoint tool (the owner's budget, 2026-11-24).</item>
/// </list>
/// Any other value validates nothing (exit 2, one line): the receipt stays incomplete, as when the validator failed.
/// Exit 9 with one line: the CLI or the validator could not be built or started (the launchers' own code).
/// </summary>
internal static class CheckpointValidatorCommand
{
    /// <summary>A built DLL answers in seconds.</summary>
    internal static readonly TimeSpan DllBudget = TimeSpan.FromSeconds(30);

    /// <summary>The CLI form may first build the CLI and the checkpoint tool (19 s cold on the laptop, 2026-11-24).</summary>
    internal static readonly TimeSpan CliBudget = TimeSpan.FromMinutes(5);

    /// <summary>The setting's file names that name a checkout's CLI (the old launcher and the two launchers).</summary>
    internal static readonly IReadOnlyList<string> CliEntries = ["antiphon", "antiphon.cmd", "validate-checkpoint-receipt.ps1"];

    /// <summary>The exit code when the validator could not be built or started.</summary>
    internal const int Unavailable = 9;

    /// <summary>The exit code when the setting names no validator.</summary>
    internal const int Unconfigured = 2;

    private const int OutputLimit = 65536;
    private static readonly Regex PublishedName = new("^[0-9a-f]{24}$", RegexOptions.CultureInvariant);

    /// <summary>One process: its file, arguments, directory (null: the server's) and the variables it sets (null removes one).</summary>
    internal sealed record Step(string FileName, IReadOnlyList<string> Arguments, string? WorkingDirectory,
        IReadOnlyDictionary<string, string?> Environment);

    /// <summary>Whether the setting names a built DLL.</summary>
    internal static bool IsDll(string validator) => validator.EndsWith(".dll", StringComparison.OrdinalIgnoreCase);

    /// <summary>The budget of one validation.</summary>
    internal static TimeSpan Budget(string validator) => IsDll(validator) ? DllBudget : CliBudget;

    /// <summary>The checkout a CLI-form setting names (the folder above its scripts folder), or null.</summary>
    internal static string? Checkout(string validator)
    {
        if (string.IsNullOrWhiteSpace(validator) || IsDll(validator)) return null;
        string full;
        try { full = Path.GetFullPath(validator.Trim()); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return null; }
        var name = Path.GetFileName(full);
        var scripts = Path.GetDirectoryName(full);
        if (scripts is null || !CliEntries.Contains(name, StringComparer.OrdinalIgnoreCase)
            || !string.Equals(Path.GetFileName(scripts), "scripts", StringComparison.OrdinalIgnoreCase)) return null;
        return Path.GetDirectoryName(scripts);
    }

    /// <summary>The validate verb's arguments for one receipt.</summary>
    internal static string[] VerbArguments(string evidence, string sourceSha, IReadOnlyList<string> rows) =>
        ["validate", "--evidence", evidence, "--expected-source-sha", sourceSha, "--rows", string.Join(',', rows)];

    /// <summary>
    /// The validation process for the setting, or null when the setting names a Windows checkout with no published CLI yet
    /// (bootstrap it first: BootstrapSteps) or names no validator (Checkout is null too).
    /// </summary>
    internal static Step? Command(string validator, IReadOnlyList<string> verb, bool windows)
    {
        if (IsDll(validator)) return new("dotnet", ["exec", validator, .. verb], null, new Dictionary<string, string?>());
        var checkout = Checkout(validator);
        if (checkout is null) return null;
        if (!windows)
            return new("/bin/sh", [Path.Combine(checkout, "scripts", "antiphon"), "checkpoints", .. verb], checkout, new Dictionary<string, string?>());
        var cli = PublishedCli(checkout);
        return cli is null ? null
            : new("dotnet", ["exec", cli, "checkpoints", .. verb], checkout, new Dictionary<string, string?> { ["ANTIPHON_CLI_CHECK"] = checkout });
    }

    /// <summary>The published CLI that bin-launcher/antiphon-scripts/current names in the checkout, or null (as the launchers resolve it).</summary>
    internal static string? PublishedCli(string checkout)
    {
        var cache = Path.Combine(checkout, "bin-launcher", "antiphon-scripts");
        string name;
        try { name = File.ReadAllText(Path.Combine(cache, "current")).Trim(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
        if (!PublishedName.IsMatch(name)) return null;
        var dll = Path.Combine(cache, name, "Antiphon.Scripts.dll");
        return File.Exists(dll) ? dll : null;
    }

    /// <summary>
    /// scripts\antiphon.cmd's first run, as two processes: the launcher project built into boot/out (its own artifacts
    /// path, quiet, no reused nodes or compiler server), then its publish of the checkout's CLI. MSBuild* variables are
    /// left out, as the CLI's own build leaves them out.
    /// </summary>
    internal static Step[] BootstrapSteps(string checkout, string boot, IEnumerable<string> environmentNames)
    {
        var without = environmentNames.Where(name => name.StartsWith("MSBuild", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(name => name, _ => (string?)null, StringComparer.OrdinalIgnoreCase);
        var project = Path.Combine(checkout, "scripts", "lib", "Antiphon.CliLauncher", "Antiphon.CliLauncher.csproj");
        return
        [
            new("dotnet", ["build", project, "-c", "Release", "-o", Path.Combine(boot, "out"), "--artifacts-path", Path.Combine(boot, "artifacts"),
                "-nologo", "-v", "q", "-nodeReuse:false", "-p:UseSharedCompilation=false"], checkout, without),
            new("dotnet", ["exec", Path.Combine(boot, "out", "Antiphon.CliLauncher.dll"), "publish", checkout], checkout, without),
        ];
    }

    /// <summary>
    /// Runs the validation for the setting within its budget: the exit code and the output (stdout, then stderr; at most
    /// 64 KiB each). environment adds variables to every process (tests). The budget's end kills the process tree.
    /// </summary>
    internal static async Task<(int Exit, string Output)> RunAsync(string validator, IReadOnlyList<string> verb,
        CancellationToken ct, IReadOnlyDictionary<string, string?>? environment = null)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(Budget(validator));
        var windows = OperatingSystem.IsWindows();
        var command = Command(validator, verb, windows);
        if (command is null)
        {
            var checkout = Checkout(validator);
            if (checkout is null)
                return (Unconfigured, "validator_unconfigured: CheckpointReceipts:ValidatorScript names neither a built Antiphon.Checkpoints.dll nor a checkout's scripts/antiphon: "
                    + validator);
            var bootstrap = await BootstrapAsync(checkout, environment, budget.Token);
            if (bootstrap is not null) return (Unavailable, bootstrap);
            command = Command(validator, verb, windows);
            if (command is null) return (Unavailable, "antiphon-cli: no published CLI under " + Path.Combine(checkout, "bin-launcher", "antiphon-scripts") + " after the build");
        }
        return await RunStepAsync(With(command, environment), budget.Token);
    }

    // Null when the CLI is published; else the one line that says why not. A failed build keeps its boot folder and log.
    private static async Task<string?> BootstrapAsync(string checkout, IReadOnlyDictionary<string, string?>? environment, CancellationToken ct)
    {
        var cache = Path.Combine(checkout, "bin-launcher", "antiphon-scripts");
        var boot = Path.Combine(cache, "boot-" + Guid.NewGuid().ToString("N"));
        try { Directory.CreateDirectory(boot); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { return "antiphon-cli: cannot create a bootstrap folder under " + cache + ": " + ex.Message; }
        var names = Environment.GetEnvironmentVariables().Keys.Cast<string>().ToArray();
        var steps = BootstrapSteps(checkout, boot, names);
        var log = Path.Combine(boot, "build.log");
        var (built, buildOutput) = await RunStepAsync(With(steps[0], environment), ct, OutputLimit * 4);
        await File.WriteAllTextAsync(log, buildOutput, CancellationToken.None);
        if (built != 0 || !File.Exists(Path.Combine(boot, "out", "Antiphon.CliLauncher.dll")))
            return "antiphon-cli: the launcher did not build (exit " + built + ", log " + log + ")";
        var (published, publishOutput) = await RunStepAsync(With(steps[1], environment), ct);
        try { Directory.Delete(boot, true); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return published == 0 ? null : "antiphon-cli: the CLI was not published (exit " + published + "): " + publishOutput.Trim();
    }

    private static Step With(Step step, IReadOnlyDictionary<string, string?>? environment)
    {
        if (environment is null || environment.Count == 0) return step;
        var merged = new Dictionary<string, string?>(step.Environment, StringComparer.OrdinalIgnoreCase);
        foreach (var pair in environment) merged[pair.Key] = pair.Value;
        return step with { Environment = merged };
    }

    private static async Task<(int Exit, string Output)> RunStepAsync(Step step, CancellationToken ct, int limit = OutputLimit)
    {
        var start = new ProcessStartInfo(step.FileName) { UseShellExecute = false, RedirectStandardOutput = true,
            RedirectStandardError = true, CreateNoWindow = true };
        foreach (var argument in step.Arguments) start.ArgumentList.Add(argument);
        if (step.WorkingDirectory is not null) start.WorkingDirectory = step.WorkingDirectory;
        foreach (var pair in step.Environment)
        {
            if (pair.Value is null) start.Environment.Remove(pair.Key);
            else start.Environment[pair.Key] = pair.Value;
        }
        Process child;
        try { child = Process.Start(start) ?? throw new InvalidOperationException("no process"); }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        { return (Unavailable, "validator_start_failed: " + step.FileName + ": " + ex.Message); }
        using (child)
        {
            var stdout = ReadBoundedAsync(child.StandardOutput, limit, ct);
            var stderr = ReadBoundedAsync(child.StandardError, limit, ct);
            try
            {
                await Task.WhenAll(child.WaitForExitAsync(ct), stdout, stderr).WaitAsync(ct);
                return (child.ExitCode, await stdout + await stderr);
            }
            finally
            {
                if (!child.HasExited) child.Kill(entireProcessTree: true);
                await child.WaitForExitAsync(CancellationToken.None);
            }
        }
    }

    private static async Task<string> ReadBoundedAsync(StreamReader reader, int limit, CancellationToken ct)
    {
        var text = new StringBuilder(); var buffer = new char[4096]; int length;
        while ((length = await reader.ReadAsync(buffer.AsMemory(), ct)) != 0)
        {
            if (text.Length + length > limit) throw new InvalidDataException("validator_output_too_large");
            text.Append(buffer, 0, length);
        }
        return text.ToString();
    }
}
