using System.Diagnostics;
using System.Text.Json.Nodes;
using Antiphon.Nightly;
using Antiphon.Tests.Scripts.Nightly;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-1339 G-7..G-9: the release candidate's remote reads against a real local bare remote, and the release-candidate
/// command's own exits. Unit 13 of 2026-11-22-remaining-ps1-to-csharp.md moved these rows out of
/// scripts/test-release-gate.ps1 (C1339_ExactRemoteRefs, which ran the deleted release-candidate.ps1 over the
/// fixtures/c1339-release-git.ps1 transport): the candidate (ReleaseCandidate.Run) now runs in process, its git a real git
/// whose "origin" is the bare remote, with that transport's faults (a misleading ls-remote record prepended or a read
/// replaced, by ordinal; a push that fails before or after it lands). A suffix match (refs/heads/archive/&lt;ref&gt;) is
/// never the candidate's identity, and the remote tip is read back by an independent git call.
/// </summary>
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class ReleaseCandidateGitTests
{
    [Test, Timeout(300_000)]
    public void C1339_CandidateRemoteReads(CancellationToken token)
    {
        var rows = new NightlyCaseRows();

        using (var fx = CandidateRemoteFixture.Create(stamp: false, candidateOnRemote: true))
        {
            fx.Read("refs/heads/" + fx.CandidateRef, fx.B + "\trefs/heads/archive/" + fx.CandidateRef);
            var r = fx.Cut();
            var journal = JsonNode.Parse(File.ReadAllText(r.JournalPath))!.AsObject();
            rows.Check(r.ExitCode == 0 && ReleaseGateJournal.Str(journal["sha"]) == fx.Head && ReleaseGateJournal.Str(journal["candidateId"]) == fx.CandidateId && fx.NoWrites(),
                "C1339 G-7 candidate-long-read", r.Refusal);
        }

        using (var fx = CandidateRemoteFixture.Create(stamp: true, candidateOnRemote: true))
        {
            fx.Fixture("push", fx.Bare, fx.B + ":refs/heads/" + fx.CandidateRef);
            fx.Read("refs/heads/" + fx.CandidateRef, fx.Head + "\trefs/heads/archive/" + fx.CandidateRef);
            var r = fx.Cut();
            var tip = fx.Tip();
            var checks = new List<(bool, string)> { (r.Refusal == "remote-candidate-conflict" && tip == fx.B && fx.NoWrites(), r.Refusal) };
            fx.Read("refs/heads/" + fx.CandidateRef, fx.Head + "\trefs/heads/" + fx.CandidateRef, mode: "replace", exit: 1);
            r = fx.Cut();
            checks.Add((r.Refusal == "remote-candidate-unresolved" && fx.NoWrites(), r.Refusal));
            rows.Check(checks.All(c => c.Item1), "C1339 G-8 candidate-stamp-read", string.Join(" | ", checks.Select(c => c.Item2)));
        }

        var recovery = new List<(bool, string)>();
        foreach (var push in new[] { "before", "after" })
        {
            using var fx = CandidateRemoteFixture.Create(stamp: true, candidateOnRemote: false);
            fx.Read("refs/heads/" + fx.CandidateRef, fx.Head + "\trefs/heads/archive/" + fx.CandidateRef, ordinal: 2);
            fx.PushFault = push;
            var r = fx.Cut();
            var tip = fx.Tip();
            var ok = push == "after" ? r.ExitCode == 0 && r.Pushed && tip == fx.Head : r.Refusal == "push-failed" && tip.Length == 0;
            recovery.Add((ok, push + ":" + r.Refusal + " tip=" + tip));
        }
        rows.Check(recovery.All(c => c.Item1), "C1339 G-9 candidate-recovery-read", string.Join(" | ", recovery.Select(c => c.Item2)));
        rows.ShouldAllPass(3, "C1339 G-7 candidate-long-read", "C1339 G-8 candidate-stamp-read", "C1339 G-9 candidate-recovery-read");
    }

    /// <summary>
    /// The staged antiphon-scripts answers release-candidate itself: a refusal before any git call is exit 3 with the
    /// candidate's line and its JSON line last on stdout; a malformed line is usage (exit 2).
    /// </summary>
    [Test, Timeout(180_000)]
    public async Task The_command_prints_its_line_and_json_and_exits_with_the_candidate_code(CancellationToken token)
    {
        using var fx = NightlyFixture.Create("release-candidate-cli");
        var release = fx.PathOf("releases");
        var omitted = await Cli(token, "release-candidate", "--release-root", release, "--checkout-root", fx.PathOf("checkout"));
        omitted.Exit.ShouldBe(3, omitted.Stderr);
        var lines = omitted.Stdout.Replace("\r\n", "\n").Trim().Split('\n');
        lines.ShouldBe(new[]
        {
            "[release-candidate] REFUSED: candidate ref (rc-ref-required).",
            "{\"candidateId\":\"\",\"ref\":\"\",\"sha\":\"\",\"pushed\":false,\"resumed\":false,\"refusal\":\"rc-ref-required\",\"exitCode\":3}",
        });

        var unmarked = fx.PathOf("unmarked");
        Directory.CreateDirectory(Path.Combine(unmarked, ".git"));
        var refused = await Cli(token, "release-candidate", "--ref", "rc/2026-10-15", "--release-root", release, "--checkout-root", unmarked, "--what-if");
        refused.Exit.ShouldBe(3, refused.Stderr);
        refused.Stdout.ShouldContain("\"refusal\":\"unmarked-clone\"");
        Directory.Exists(Path.Combine(release, "candidates")).ShouldBeFalse();

        var usage = await Cli(token, "release-candidate", "--seams-path", fx.PathOf("seams.ps1"));
        usage.Exit.ShouldBe(2);
        usage.Stderr.ShouldContain("usage: antiphon-scripts release-candidate: unknown flag --seams-path");
    }

    private static async Task<(int Exit, string Stdout, string Stderr)> Cli(CancellationToken token, params string[] arguments)
    {
        var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "antiphon-scripts", "Antiphon.Scripts.dll"));
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync(token);
        var stderr = process.StandardError.ReadToEndAsync(token);
        await process.WaitForExitAsync(token);
        return (process.ExitCode, await stdout, await stderr);
    }

    /// <summary>
    /// New-C1339Fixture for the candidate: a real checkout (commits A = Head and B), a real bare remote ("origin" of every
    /// candidate call), the owned marker and the candidate journal the publish fixture left (pinned at Head). Its NightlyGit
    /// is the c1339-release-git.ps1 transport: origin is the bare remote, every call is traced, and the read and push faults
    /// apply. Children inherit no credentials, hooks, signing or network protocols.
    /// </summary>
    private sealed class CandidateRemoteFixture : IDisposable
    {
        private readonly NightlyFixture _fx;
        private readonly List<string> _trace = [];
        private readonly List<(string Ref, string Output, string Mode, int Ordinal, int Exit)> _reads = [];
        private readonly Dictionary<string, int> _ordinals = new(StringComparer.Ordinal);

        private CandidateRemoteFixture(NightlyFixture fx) => _fx = fx;

        public string Checkout => _fx.PathOf("checkout");
        public string Bare => _fx.PathOf("remote.git");
        public string ReleaseRoot => _fx.PathOf("releases");
        public string Head { get; private set; } = "";
        public string B { get; private set; } = "";
        public string CandidateRef { get; private set; } = "";
        public string CandidateId { get; private set; } = "";

        /// <summary>"before": a push fails without landing; "after": it lands, then fails.</summary>
        public string PushFault { get; set; } = "";

        public static CandidateRemoteFixture Create(bool stamp, bool candidateOnRemote)
        {
            var fx = new CandidateRemoteFixture(NightlyFixture.Create("c1339-candidate"));
            try
            {
                Directory.CreateDirectory(fx.Checkout);
                fx.Fixture("init", "-q", "-b", "master", fx.Checkout);
                fx.Head = fx.Commit("policy A");
                fx.B = fx.Commit("distinct B");
                fx.Fixture("init", "--bare", "-q", fx.Bare);
                fx.CandidateRef = stamp ? ReleaseCandidateCases.CandidateRef : "rc/2026-10-15";
                fx.CandidateId = stamp ? ReleaseCandidateCases.CandidateId : "rc-2026-10-15-" + fx.Head[..12];
                if (candidateOnRemote) fx.Fixture("push", fx.Bare, fx.Head + ":refs/heads/" + fx.CandidateRef);
                NightlyFixture.WriteJson(Path.Combine(fx.Checkout, ReleaseCandidate.OwnedMarker), new { kind = "release-clone" });
                // New-C599RcGreen's candidate journal: the tested sha, pushed.
                NightlyFixture.WriteJson(Path.Combine(ReleaseGateCredit.CandidateRoot(fx.ReleaseRoot, fx.CandidateId), "candidate.json"), new
                {
                    schemaVersion = 1, candidateId = fx.CandidateId, @ref = fx.CandidateRef, sha = fx.Head,
                    cutUtc = stamp ? "2026-09-23T08:30:00Z" : "2026-10-15T00:00:00Z", scheduleSlot = "u/lndcobra/antiphon_release_candidates", pushed = true,
                });
                return fx;
            }
            catch
            {
                fx.Dispose();
                throw;
            }
        }

        /// <summary>Set-C1339Read: answers for an ls-remote of ref (prepend the output, or replace the answer), at every read or only the ordinal-th.</summary>
        public void Read(string reference, string output, string mode = "prepend", int ordinal = 0, int exit = 0)
        {
            _reads.Clear();
            _reads.Add((reference, output, mode, ordinal, exit));
        }

        /// <summary>The candidate, in process, over this transport.</summary>
        public ReleaseCandidateResult Cut() => ReleaseCandidate.Run(new ReleaseCandidateOptions { ReleaseRoot = ReleaseRoot, CheckoutRoot = Checkout, Ref = CandidateRef },
            _fx.Host(), new System.IO.Abstractions.FileSystem(), new ReleaseGateRemote(Run, null, null));

        /// <summary>Test-C1339NoWrites: no tag or push reached the transport.</summary>
        public bool NoWrites() => !_trace.Any(line => line.StartsWith("tag ", StringComparison.Ordinal) || line.StartsWith("push ", StringComparison.Ordinal));

        /// <summary>The remote's candidate tip by an independent read; "" when the branch is absent.</summary>
        public string Tip()
        {
            var tip = Git(Bare, "rev-parse", "--verify", "--quiet", "refs/heads/" + CandidateRef);
            return tip.Exit == 0 ? tip.Output : "";
        }

        /// <summary>A fixture git call that must succeed (in the fixture root).</summary>
        public string Fixture(params string[] arguments)
        {
            var directory = arguments[0] == "push" ? Checkout : _fx.Root;
            var run = Git(directory, arguments);
            if (run.Exit != 0) throw new InvalidOperationException("fixture git " + string.Join(" ", arguments) + ": " + run.Error);
            return run.Output;
        }

        private string Commit(string message)
        {
            var run = Git(Checkout, "commit", "--allow-empty", "-q", "-m", message);
            if (run.Exit != 0) throw new InvalidOperationException("fixture commit: " + run.Error);
            return Git(Checkout, "rev-parse", "HEAD").Output;
        }

        // Invoke-C1339FixtureGit: origin is the bare remote (ls-remote, push, fetch), every call is traced, then the faults.
        private NightlyGitResult Run(string workingDirectory, IReadOnlyList<string> arguments)
        {
            if (!string.Equals(Path.GetFullPath(workingDirectory), Path.GetFullPath(Checkout), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("fixture-working-directory " + workingDirectory);
            var forward = arguments.ToArray();
            var sub = forward.Length > 0 ? forward[0] : "";
            switch (sub)
            {
                case "ls-remote":
                    var remoteIndex = forward.Length > 2 && forward[2] == "--refs" ? 3 : 2;
                    if (forward[remoteIndex] != "origin") throw new InvalidOperationException("fixture-transport-refused");
                    forward[remoteIndex] = Bare;
                    break;
                case "push":
                case "fetch":
                    if (forward[1] != "origin") throw new InvalidOperationException("fixture-transport-refused");
                    forward[1] = Bare;
                    break;
                case "rev-parse":
                    break;
                default:
                    throw new InvalidOperationException("fixture-command-refused " + sub);
            }
            _trace.Add(string.Join(" ", arguments));
            if (sub == "push" && PushFault == "before") return new NightlyGitResult(1, "");
            var real = Git(workingDirectory, forward);
            if (sub == "push" && PushFault == "after") return new NightlyGitResult(1, "");
            if (sub == "ls-remote")
            {
                var reference = arguments[^1];
                foreach (var response in _reads)
                {
                    if (!string.Equals(reference, response.Ref, StringComparison.Ordinal)) continue;
                    _ordinals[reference] = _ordinals.TryGetValue(reference, out var seen) ? seen + 1 : 1;
                    if (response.Ordinal != 0 && response.Ordinal != _ordinals[reference]) continue;
                    if (response.Mode == "replace") return new NightlyGitResult(response.Exit, response.Output);
                    return new NightlyGitResult(real.Exit, response.Output + "\n" + real.Output);
                }
            }
            return new NightlyGitResult(real.Exit, real.Output);
        }

        // Invoke-C1339GitProcess: no credentials, hooks, signing, system or global configuration; file transport only; 15 s.
        private (int Exit, string Output, string Error) Git(string workingDirectory, params string[] arguments)
        {
            var start = new ProcessStartInfo("git")
            {
                WorkingDirectory = workingDirectory, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
            };
            foreach (var key in start.Environment.Keys.ToArray())
                if (System.Text.RegularExpressions.Regex.IsMatch(key, "(?i)TOKEN|SECRET|PASSWORD|CREDENTIAL|ANTIPHON|CODEX|CLAUDE|OPENAI|GH_|GITHUB|GIT_|ASKPASS"))
                    start.Environment.Remove(key);
            var emptyConfig = _fx.PathOf("empty-git-config");
            if (!File.Exists(emptyConfig)) File.WriteAllText(emptyConfig, "");
            start.Environment["GIT_ALLOW_PROTOCOL"] = "file";
            start.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
            start.Environment["GIT_CONFIG_GLOBAL"] = emptyConfig;
            start.Environment["GIT_TERMINAL_PROMPT"] = "0";
            foreach (var argument in new[] { "-c", "core.hooksPath=" + _fx.PathOf("no-hooks"), "-c", "commit.gpgSign=false", "-c", "tag.gpgSign=false",
                "-c", "user.name=c1339", "-c", "user.email=c1339@example.invalid" }.Concat(arguments))
                start.ArgumentList.Add(argument);
            using var process = Process.Start(start)!;
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(15_000))
            {
                process.Kill(true);
                throw new InvalidOperationException("fixture-git-timeout " + string.Join(" ", arguments));
            }
            process.WaitForExit();
            return (process.ExitCode, stdout.Result.Trim(), stderr.Result);
        }

        public void Dispose() => _fx.Dispose();
    }
}
