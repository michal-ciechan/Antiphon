using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace Antiphon.LandGraphAudit;

/// <summary>
/// CARD-1483 S5 (with CARD-1528, 1540, 1753 and 1780): author-time approval of the land build graph. This is
/// the body of scripts/audit-land-verification-graph.ps1, ported to C# with the same steps in the same order,
/// the same refusal messages and the same exit codes; the script is now a wrapper that resolves the built
/// tool (rebuilding it when stale) and runs its <c>audit</c> command, which calls <see cref="Run"/>.
/// <list type="bullet">
/// <item>-Update requires the emulation pre-check (<see cref="LandGraphPrecheck"/>, in process) AND the real
/// MSBuild evaluation (the tool's evaluator, a child process). -Check verifies the pre-check, the manifest and
/// the digest/pins record, and always evaluates. The recorded SDK must be a stable release selectable by the
/// committed latestMinor policy (global.json at HEAD).</item>
/// <item>-LandRefresh is only for scripts/land-graph-land-refresh.ps1, and only together with -Update. It lets
/// that script stage pins beside the manifest for one land-graph commit. Without -Update it refuses. A bare
/// -Update still refuses with "commit source and pins before auditing".</item>
/// <item>The evaluator's own clocks bound each solution pass and the canary copy. This wait is the evaluator's
/// ceiling (<see cref="EvaluationBudget.ToolCeilingSeconds"/>: phase total plus
/// <see cref="EvaluationBudget.StartupGraceSeconds"/>). An explicit budget overrides that wait and is exact: it
/// never skips evaluation and never approves by itself.</item>
/// <item>A killed evaluator's canary is removed from the recorded path by <see cref="LandGraphCanaryRemoval"/> on
/// the kill, tool-watchdog, timeout and exception paths. Exit 0 means both evaluation passes, the canary copy
/// and the comparison approved. Any other evaluator exit, including the api-probe code 3, refuses. A canary
/// left behind when this process dies is not swept on the next run.</item>
/// </list>
/// The evaluation stays a child process (not MSBuildLocator in this process): its kill, timeout, watchdog and
/// canary semantics are those of a separate process tree, and Antiphon.Tests (net9.0) runs this class in
/// process. PowerShell semantics are those of the script under pwsh without strict mode: a .NET method failure
/// reads <c>Exception calling "M" with "N" argument(s): "..."</c>, string comparisons are
/// <see cref="Ps"/>'s, and global.json goes through <see cref="PsJson"/> (ConvertFrom-Json).
/// </summary>
public static class LandGraphAuditGate
{
    public const int Approved = 0;
    public const int Refused = 1;

    internal const string ManifestRelative = "server/Application/Services/LandVerificationBuildGraph.manifest.txt";
    internal const string EvaluationRelative = "server/Application/Services/LandVerificationBuildGraph.evaluation.txt";
    internal const string PinsRelative = "server/Application/Services/LandVerificationBuildGraph.pins.txt";
    internal const string StableSdkPattern = @"\A[1-9][0-9]*\.(?:0|[1-9][0-9]*)\.[1-9][0-9]{2,}\z";
    private const int ChildTimeoutMilliseconds = 300000;

    /// <param name="RepoRoot">The script's <c>$RepoRoot</c> (Split-Path -Parent $PSScriptRoot), used as given.</param>
    /// <param name="ToolPath">The resolved tool DLL (Resolve-LandAuditTool's Path): the evaluator this run starts.</param>
    /// <param name="EvaluationBudgetSeconds">The script's -EvaluationBudgetSeconds; 0 waits for the evaluator ceiling.</param>
    /// <param name="Head">
    /// Set only by the wrapper after it has itself read HEAD and run the source check (its path when the tool had
    /// to be rebuilt first, so those run before the rebuild exactly as before); the run then starts at the
    /// pre-check. The closing source checks still run against it.
    /// </param>
    public sealed record Options(string RepoRoot, bool Check, bool Update, bool LandRefresh, string ToolPath,
        int EvaluationBudgetSeconds = 0, string? Head = null);

    /// <summary>The script body: 0 approved (both passes, the canary copy and the comparison), 1 refused.</summary>
    /// <param name="stdout">The script's output stream (Write-Output).</param>
    /// <param name="stderr">The script's [Console]::Error.</param>
    public static int Run(Options options, TextWriter stdout, TextWriter stderr) =>
        new Gate(options, stdout, stderr).Execute();

    /// <summary>
    /// Assert-SelectableSdk: the recorded SDK must be a stable release that the committed global.json policy
    /// (read only after the record's own checks, as the script did) can select. Throws the refusal.
    /// </summary>
    internal static void AssertSelectableSdk(string version, Func<string> readGlobalJson)
    {
        if (!Ps.CMatch(version, StableSdkPattern) || !Version.TryParse(version, out var candidate))
            throw new AuditRefusal("evaluation record has invalid SDK provenance");
        // Admit stable SDK releases under the committed policy only. A new policy or
        // a prerelease pin needs explicit qualification rather than a permissive fallback.
        var policy = PsJson.Get(PsJson.ConvertFrom(readGlobalJson()), "sdk");
        var policyVersion = PsJson.Get(policy, "version");
        Version? minimum = null;
        if (PsJson.IsTrue(PsJson.CNotMatchOp(policyVersion, StableSdkPattern))
            || !Version.TryParse(PsJson.ArgumentString(policyVersion), out minimum)
            || PsJson.IsTrue(PsJson.CNeOp(PsJson.Get(policy, "rollForward"), "latestMinor")))
        {
            throw new AuditRefusal("SDK provenance requires a stable pin with rollForward latestMinor");
        }

        // latestMinor can advance minor, feature band and patch, but never major or
        // select a version below the pin (including an earlier patch in its band).
        if (candidate.Major != minimum.Major || candidate < minimum)
            throw new AuditRefusal("evaluation record SDK provenance is outside committed roll-forward policy");
    }

    private sealed class Gate(Options options, TextWriter stdout, TextWriter stderr)
    {
        private readonly string _repoRoot = options.RepoRoot;
        private string _auditCanaryRecord = "";
        private string _auditCanaryRepo = "";

        private string RecordDirectory => PsPath.Join(_repoRoot, "server/Application/Services");
        private string ManifestPath => PsPath.Join(_repoRoot, ManifestRelative);
        private string EvaluationPath => PsPath.Join(_repoRoot, EvaluationRelative);
        private string PinsPath => PsPath.Join(_repoRoot, PinsRelative);

        public int Execute()
        {
            string? temporaryManifest = null;
            string? temporaryEvaluation = null;
            var code = Approved;
            try
            {
                try
                {
                    if (options.Check && options.Update) throw new AuditRefusal("Specify -Check or -Update, not both.");
                    if (options.LandRefresh && !options.Update) throw new AuditRefusal("-LandRefresh requires -Update.");
                    var allowPendingPins = options.Update && options.LandRefresh;
                    string head;
                    if (options.Head is null)
                    {
                        head = GetGitValue("rev-parse", "HEAD");
                        AssertHeadSource(head, allowPendingPins);
                    }
                    else head = options.Head;

                    // The pre-check is the same build's LandGraphPrecheck, called directly (no child process);
                    // scripts/audit-land-verification-precheck.ps1 is the same code for standalone use.
                    var toolPath = options.ToolPath;
                    // The script built the pre-check child's arguments here; GetFullPath is the only call that can fail.
                    PsNet.Call("GetFullPath", 1, () => Path.GetFullPath(toolPath));
                    if (options.Update)
                    {
                        PsNet.Call("CreateDirectory", 1, () => Directory.CreateDirectory(RecordDirectory));
                        temporaryManifest = PsPath.Join(RecordDirectory, ".land-manifest-" + Guid.NewGuid().ToString("N"));
                    }

                    var pre = RunPrecheck(temporaryManifest);
                    if (pre.Text.Length > 0) stdout.WriteLine(pre.Text.TrimEnd());
                    if (pre.Code != 0) throw new AuditRefusal("land graph pre-check refused");

                    var pins = allowPendingPins
                        ? GetGitValue("hash-object", PinsPath)
                        : GetGitValue("rev-parse", "HEAD:" + PinsRelative);
                    if (!Ps.CMatch(pins, "^(?:[0-9a-f]{40}|[0-9a-f]{64})$")) throw new AuditRefusal("pins blob SHA is invalid");
                    var sdkRun = InvokeChild("dotnet", ["--version"]);
                    if (sdkRun.Code != 0 || !Ps.CMatch(sdkRun.Text.Trim(), StableSdkPattern))
                        throw new AuditRefusal("SDK resolution failed: " + sdkRun.Text.Trim());
                    var sdk = sdkRun.Text.Trim();
                    var selectedManifest = options.Update ? temporaryManifest! : ManifestPath;
                    var manifest = PsNet.Call("ReadAllLines", 1, () => File.ReadAllLines(selectedManifest));
                    // $manifest[0] of an empty file is $null.
                    string?[] expected = [manifest.Length > 0 ? manifest[0] : null, sdk, pins];

                    if (options.Update)
                    {
                        AssertEvaluatedGraph(toolPath);
                        AssertHeadSource(head, allowPendingPins);
                        temporaryEvaluation = PsPath.Join(RecordDirectory, ".land-evaluation-" + Guid.NewGuid().ToString("N"));
                        var text = string.Join("\n", expected.Select(static line => line ?? "")) + "\n";
                        PsNet.Call("WriteAllText", 3, () => File.WriteAllText(temporaryEvaluation, text, Encoding.ASCII));
                        // Publish the manifest last: an interrupted write cannot approve a new graph early.
                        PublishRecord(temporaryEvaluation, EvaluationPath);
                        PublishRecord(temporaryManifest!, ManifestPath);
                    }
                    else
                    {
                        if (!File.Exists(EvaluationPath)) throw new AuditRefusal("evaluation record missing; run -Update");
                        var record = PsNet.Call("ReadAllLines", 1, () => File.ReadAllLines(EvaluationPath));
                        if (record.Length != 3) throw new AuditRefusal("evaluation record must be three lines");
                        string[] names = ["digest", "SDK", "pins SHA"];
                        foreach (var i in new[] { 0, 2 })
                        {
                            if (!Ps.CEq(record[i], expected[i])) throw new AuditRefusal("evaluation record mismatch: " + names[i]);
                        }

                        // global.json is part of the exact graph digest and owns SDK selection/roll-forward.
                        // SDK provenance never replaces evaluation: even a host-equal record must
                        // not approve imports, items or globs without checking the actual graph.
                        AssertSelectableSdk(record[1]);
                        AssertEvaluatedGraph(toolPath);
                        AssertHeadSource(head, false);
                    }
                }
                catch (Exception refusal)
                {
                    ClearAuditCanary();
                    stderr.WriteLine("audit refused: " + refusal.Message);
                    code = Refused;
                }
            }
            catch (Exception uncaught)
            {
                // An error thrown while handling the refusal (canary removal) ended the script with exit 1.
                stderr.WriteLine(uncaught.Message);
                code = Refused;
            }
            finally
            {
                foreach (var path in new[] { temporaryManifest, temporaryEvaluation })
                {
                    if (!string.IsNullOrEmpty(path) && File.Exists(path))
                    {
                        try
                        {
                            PsNet.Call("Delete", 1, () => File.Delete(path));
                        }
                        catch (AuditRefusal failed)
                        {
                            stderr.WriteLine(failed.Message);
                            code = Refused;
                        }
                    }
                }
            }

            return code;
        }

        private void ClearAuditCanary()
        {
            if (string.IsNullOrEmpty(_auditCanaryRecord)) return;
            LandGraphCanaryRemoval.Remove(_auditCanaryRecord, _auditCanaryRepo, stderr);
        }

        private ChildRun RunPrecheck(string? temporaryManifest)
        {
            // In process: the text the tool's 'precheck' command printed (UTF-8) as this process would have read
            // it back from that child, stdout then stderr; and the child's 300 s wait.
            var work = Task.Run(() =>
            {
                using var output = new StringWriter(CultureInfo.InvariantCulture);
                using var error = new StringWriter(CultureInfo.InvariantCulture);
                var exit = LandGraphPrecheck.Run(_repoRoot, !options.Update, options.Update,
                    options.Update ? temporaryManifest : null, output, error);
                return new ChildRun(exit, ChildText.AsRead(output.ToString()) + ChildText.AsRead(error.ToString()));
            });
            if (!work.Wait(ChildTimeoutMilliseconds))
                throw new AuditRefusal("subprocess exceeded " + (ChildTimeoutMilliseconds / 1000).ToString(CultureInfo.InvariantCulture) + " s: dotnet");
            return work.Result;
        }

        private string GetGitValue(params string[] arguments)
        {
            var run = InvokeChild("git", arguments);
            if (run.Code != 0) throw new AuditRefusal("git failed: " + run.Text.Trim());
            return run.Text.Trim();
        }

        private void AssertSelectableSdk(string version) =>
            LandGraphAuditGate.AssertSelectableSdk(version, () => GetGitValue("show", "HEAD:global.json"));

        private void AssertHeadSource(string head, bool allowPendingPins)
        {
            if (!Ps.CEq(GetGitValue("rev-parse", "HEAD"), head)) throw new AuditRefusal("HEAD changed during audit");
            // Evaluation reads disk, while the pre-check hashes HEAD. Bind the two.
            // Generated records are the only tracked paths permitted to differ.
            // Pending pins are allowed only for scripts/land-graph-land-refresh.ps1.
            var arguments = new List<string> { "diff", "--quiet", "HEAD", "--", ".", ":(exclude)" + ManifestRelative, ":(exclude)" + EvaluationRelative };
            if (allowPendingPins) arguments.Add(":(exclude)" + PinsRelative);
            var run = InvokeChild("git", arguments);
            if (run.Code != 0) throw new AuditRefusal("source differs from HEAD; commit source and pins before auditing");
        }

        private int GetEvaluatorBudgetSeconds()
        {
            if (options.EvaluationBudgetSeconds < 0) throw new AuditRefusal("evaluation budget must be positive");
            if (options.EvaluationBudgetSeconds > 0) return options.EvaluationBudgetSeconds;
            // The evaluator's ceiling: this build is the evaluator the run starts (the script asked that DLL's
            // --print-budget, which prints this same sum as ceiling=).
            return EvaluationBudget.ToolCeilingSeconds;
        }

        private void AssertEvaluatedGraph(string? evaluatorPath)
        {
            if (string.IsNullOrEmpty(evaluatorPath))
                evaluatorPath = PsPath.Join(_repoRoot, "tools/Antiphon.LandGraphAudit/bin-land-audit/Release/net10.0/Antiphon.LandGraphAudit.dll");
            if (!File.Exists(evaluatorPath)) throw new AuditRefusal("built evaluator missing: " + evaluatorPath);
            var budget = GetEvaluatorBudgetSeconds();
            // The ceiling already includes the evaluator's one startup grace. An explicit budget is exact.
            var timeoutMs = (long)budget * 1000;
            var record = PsPath.Join(Path.GetTempPath(), "antiphon-landgraph-canary-record-" + Guid.NewGuid().ToString("N") + ".txt");
            _auditCanaryRecord = record;
            _auditCanaryRepo = _repoRoot;
            var full = PsNet.Call("GetFullPath", 1, () => Path.GetFullPath(evaluatorPath));
            string[] toolArguments = [full, "--repo", _repoRoot, "--solution", "Antiphon.sln", "--canary-record", record];
            try
            {
                // [int]$TimeoutMs: the script's parameter binding refused a wait past Int32.
                if (timeoutMs > int.MaxValue)
                {
                    throw new AuditRefusal("Cannot process argument transformation on parameter 'TimeoutMs'. Cannot convert value \""
                        + timeoutMs.ToString(CultureInfo.InvariantCulture)
                        + "\" to type \"System.Int32\". Error: \"Value was either too large or too small for an Int32.\"");
                }

                var evaluation = InvokeChild("dotnet", toolArguments, (int)timeoutMs, evaluationTimeout: true);
                // The tool watchdog exits the child on its own ceiling. Drop the recorded canary here too.
                if (Ps.IndexOf(evaluation.Text, "the audit exceeded") >= 0) ClearAuditCanary();
                if (evaluation.Text.Length > 0) stdout.WriteLine(evaluation.Text.TrimEnd());
                if (evaluation.Code != 0)
                    throw new AuditRefusal("land graph evaluation refused (exit " + evaluation.Code.ToString(CultureInfo.InvariantCulture) + ")");
            }
            finally
            {
                ClearAuditCanary();
                if (!string.IsNullOrEmpty(record) && File.Exists(record)) PsNet.Call("Delete", 1, () => File.Delete(record));
                _auditCanaryRecord = "";
            }
        }

        private static void PublishRecord(string source, string destination)
        {
            if (File.Exists(destination)) PsNet.Call("Replace", 3, () => File.Replace(source, destination, null));
            else PsNet.Call("Move", 2, () => File.Move(source, destination));
        }

        private ChildRun InvokeChild(string executable, IReadOnlyList<string> arguments, int timeoutMs = ChildTimeoutMilliseconds,
            bool evaluationTimeout = false)
        {
            var start = new ProcessStartInfo
            {
                FileName = executable,
                WorkingDirectory = _repoRoot,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            foreach (var argument in arguments) start.ArgumentList.Add(argument);
            // dotnet run/test parents must not steer SDK discovery in the author-time evaluator.
            foreach (var key in start.Environment.Keys.ToArray())
            {
                if (key.StartsWith("MSBuild", StringComparison.OrdinalIgnoreCase)) start.Environment.Remove(key);
            }

            var process = new Process { StartInfo = start };
            var clock = Stopwatch.StartNew();
            var started = false;
            try
            {
                started = PsNet.Call("Start", 0, process.Start);
                if (!started) throw new AuditRefusal("could not start " + executable);
                var stdoutText = process.StandardOutput.ReadToEndAsync();
                var stderrText = process.StandardError.ReadToEndAsync();
                if (!process.WaitForExit(timeoutMs))
                {
                    if (evaluationTimeout)
                    {
                        var seconds = (int)Math.Round(timeoutMs / 1000.0, MidpointRounding.ToEven);
                        var note = "evaluation-failed: evaluating Antiphon.sln exceeded " + seconds.ToString(CultureInfo.InvariantCulture) + " s";
                        try { process.Kill(true); }
                        catch { try { process.Kill(); } catch { } }
                        try { process.WaitForExit(10000); } catch { }
                        ClearAuditCanary();
                        var partial = "";
                        try { if (stdoutText.Wait(5000)) partial = stdoutText.Result; } catch { }
                        try { if (stderrText.Wait(5000)) partial += stderrText.Result; } catch { }
                        return new ChildRun(2, partial + "\n" + note);
                    }

                    throw new AuditRefusal("subprocess exceeded " + WholeSeconds(timeoutMs) + " s: " + executable);
                }

                var remaining = (int)Math.Max(1, timeoutMs - clock.ElapsedMilliseconds);
                if (!PsNet.Call("WaitAll", 2, () => Task.WaitAll([stdoutText, stderrText], remaining)))
                    throw new AuditRefusal("subprocess output exceeded " + WholeSeconds(timeoutMs) + " s: " + executable);
                return new ChildRun(process.ExitCode, stdoutText.Result + stderrText.Result);
            }
            finally
            {
                try
                {
                    // A process that never started has no HasExited; the script read that as $null (not exited)
                    // and its Kill() fallback then threw, which is the error the script reported.
                    var exited = started && process.HasExited;
                    if (!exited)
                    {
                        try { process.Kill(true); }
                        catch { PsNet.Call("Kill", 0, () => process.Kill()); }
                        process.WaitForExit(10000);
                        ClearAuditCanary();
                    }
                }
                finally
                {
                    process.Dispose();
                }
            }
        }

        private static string WholeSeconds(int milliseconds) =>
            ((int)Math.Round(milliseconds / 1000.0, MidpointRounding.ToEven)).ToString(CultureInfo.InvariantCulture);
    }

    private readonly record struct ChildRun(int Code, string Text);
}

/// <summary>A refusal the script raised with <c>throw</c> (or a .NET failure it reported the same way).</summary>
internal sealed class AuditRefusal(string message) : Exception(message);

/// <summary>PowerShell's view of .NET calls and paths the graph script made.</summary>
internal static class PsNet
{
    /// <summary>A .NET method call: a failure reads <c>Exception calling "M" with "N" argument(s): "inner"</c>.</summary>
    public static T Call<T>(string method, int arguments, Func<T> call)
    {
        try
        {
            return call();
        }
        catch (AuditRefusal)
        {
            throw;
        }
        catch (Exception error)
        {
            throw new AuditRefusal(Describe(method, arguments, error));
        }
    }

    public static void Call(string method, int arguments, Action call) =>
        Call(method, arguments, () => { call(); return true; });

    public static string Describe(string method, int arguments, Exception error) =>
        "Exception calling \"" + method + "\" with \"" + arguments.ToString(CultureInfo.InvariantCulture) + "\" argument(s): \""
        + error.Message + "\"";
}

internal static class PsPath
{
    /// <summary><c>Join-Path</c> on the FileSystem provider: one separator between, slashes normalised.</summary>
    public static string Join(string parent, string child)
    {
        var separator = Path.DirectorySeparatorChar;
        if (separator == '\\')
        {
            parent = parent.Replace('/', '\\');
            child = child.Replace('/', '\\');
        }

        return parent.TrimEnd(separator) + separator + child.TrimStart(separator);
    }
}

/// <summary>Text a child process printed, as the script read it back.</summary>
internal static class ChildText
{
    /// <summary>
    /// The pre-check command wrote UTF-8; the script decoded the child's bytes with the console encoding (the
    /// default for a redirected child). The same round trip keeps any non-ASCII text byte-identical.
    /// </summary>
    public static string AsRead(string written)
    {
        if (written.All(static c => c < 128)) return written;
        var console = ConsoleDecoding();
        if (console is null || console.CodePage == Encoding.UTF8.CodePage) return written;
        return console.GetString(new UTF8Encoding(false).GetBytes(written));
    }

    private static Encoding? ConsoleDecoding()
    {
        try
        {
            return Console.OutputEncoding;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
