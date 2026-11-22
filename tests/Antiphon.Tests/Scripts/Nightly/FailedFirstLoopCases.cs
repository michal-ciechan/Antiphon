using System.Collections.Specialized;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Antiphon.Nightly;
using static Antiphon.Tests.Scripts.Nightly.FailedFirstArmCases;
using H = Antiphon.Tests.Scripts.Nightly.FailedFirstHistoryCases;

namespace Antiphon.Tests.Scripts.Nightly;

/// <summary>
/// W2-nightly S14: the actual-loop fixtures of scripts/test-nightly-failed-first.ps1. Invoke-FFActualLoop runs the nightly
/// tests run (NightlyTests.Run since W2-nightly S17: Invoke-C487NightlyTests of scripts/lib/c487-nightly-tests.ps1; the entry
/// nightly-tests.ps1 is deleted) in a pwsh child on a seams file whose StartProcess stands in for the test hosts (it writes each chunk's TRX and diagnostic from the planned ids and records every launch,
/// discovery, build and active identity as a JSON line); the loop fixtures and the loop history are here. The seams read
/// the active identity and the planned cost through the compiled heartbeat (QualificationHeartbeat, W2-nightly S10).
/// </summary>
internal static class FailedFirstLoopCases
{
    // ---- fixtures -----------------------------------------------------------------------------------------------

    /// <summary>New-FFLoopFixture: the global replay (Full) or the small fixture, with the history of each suite's failing class.</summary>
    public sealed record LoopFixture(object[] Suites, PlainObject History, bool Replay);

    public static LoopFixture Loop(bool full = false)
    {
        if (full)
        {
            var global = FailedFirstDispatchCases.Global();
            return new LoopFixture(global.Suites, global.History, true);
        }
        var small = Small();
        var members = small.Suites.Select(s => (object)new PlainObject(("suite", H.Str(s, "id")), ("className", PsValue.Str(H.Items(H.Items(s, "chunks")[^1], "classes")[0])),
            ("reasons", new object[] { "Failed" }), ("observations", Array.Empty<object>()))).ToArray();
        var history = new PlainObject(("format", "antiphon-nightly-prior-failures-v1"), ("sourceRunId", "old"), ("sourceAttempt", "1"), ("sourceSha", new string('a', 40)),
            ("profile", "rc"), ("platform", "windows"), ("policyHash", new string('b', 64)), ("createdAtUtc", DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture)),
            ("complete", true), ("evidence", Array.Empty<object>()), ("members", members), ("fallbackChunks", Array.Empty<object>()), ("membershipComplete", true),
            ("unresolved", Array.Empty<object>()));
        return new LoopFixture(small.Suites, history, false);
    }

    public sealed record LoopHistory(string Path, string Digest, object History);

    /// <summary>New-FFLoopHistory: a closed run whose TRX fails the first node of each history member's class, exported.</summary>
    public static LoopHistory History(string root, LoopFixture fx)
    {
        var dir = System.IO.Path.Combine(root, "closed-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var rows = new List<object>();
        foreach (var suite in fx.Suites)
        {
            var sid = H.Str(suite, "id");
            var members = H.Items(fx.History, "members").Where(m => H.Str(m, "suite") == sid).ToArray();
            var trx = System.IO.Path.Combine(dir, sid + "-old.trx");
            var nodes = H.Items(suite, "nodes");
            var failures = members.Select(member =>
            {
                var n = nodes.FirstOrDefault(x => NightlyCensus.BareClassIdentity(H.Str(x, "ClassName")) == H.Str(member, "className"));
                return new NightlyTrxRow(H.Str(n, "Uid"), H.Str(n, "ClassName"), H.Str(n, "Method"), "Failed");
            }).ToArray();
            NightlyFixture.WriteTrx(trx, failures);
            rows.Add(new OrderedDictionary { { "id", sid }, { "chunk", "old" }, { "trx", trx }, { "exitCode", 2 }, { "result", "FAIL" }, { "timedOut", false } });
        }
        H.WriteJson(System.IO.Path.Combine(dir, "summary.json"), new OrderedDictionary
        {
            { "runId", "old" }, { "attempt", "1" }, { "sha", new string('a', 40) }, { "profile", "rc" }, { "platform", "windows" }, { "policyHash", new string('b', 64) },
            { "completedAt", DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture) }, { "outcome", "TESTS" }, { "suites", rows.ToArray() },
            { "selectedSuites", fx.Suites.Select(s => (object)H.Str(s, "id")).ToArray() },
        });
        var path = System.IO.Path.Combine(root, "history-" + Guid.NewGuid().ToString("N") + ".json");
        var export = PriorFailureHistory.Export(dir, path);
        return new LoopHistory(path, H.Str(export, "Digest"), PsValue.Prop(export, "History")!);
    }

    // ---- Invoke-FFActualLoop ------------------------------------------------------------------------------------

    public sealed record LoopLaunch(string Suite, string Chunk, string Key, string Host, string Launcher, string[] Nodes, string Filter,
        double? LegacyCost, double? QualifiedCost, double? PlannedCost, bool ProvenBinding, JsonElement? Identity, bool Settled);

    public sealed class LoopTrace
    {
        public List<LoopLaunch> Launches { get; } = new();
        public List<string> Discovery { get; } = new();
        public int Builds { get; set; }
        public int Max { get; set; }
        public bool SettledBeforeReturn => Launches.Any(l => l.Settled);
        public string[] Keys => Launches.Select(l => l.Key).ToArray();
    }

    public sealed class LoopRun
    {
        public required string Root { get; init; }
        public int ExitCode { get; init; }
        public bool TestsPassed { get; init; }
        public bool CoverageComplete { get; init; }
        public JsonElement Summary { get; init; }
        /// <summary>summary.json as the run's readers see it (ConvertFrom-Json values).</summary>
        public object? SummaryValue { get; init; }
        public JsonElement Schedule { get; init; }
        public JsonElement Projection { get; init; }
        public required LoopTrace Trace { get; init; }
        public required object[] Prepared { get; init; }
        public required string Output { get; init; }
    }

    private static readonly string SeamsText = """
        # W2-nightly S14: the actual-loop seams of the failed-first cases (Invoke-FFActualLoop). The fixture and the variant are
        # read from fixture.json beside this file; every launch, discovery, build and active identity is a JSON line in trace.jsonl.
        $global:FFLoopDir = Split-Path -Parent $MyInvocation.MyCommand.Path
        if ($null -eq $global:FFLoop) {
            $fixture = [IO.File]::ReadAllText((Join-Path $global:FFLoopDir 'fixture.json')) | ConvertFrom-Json
            $bySuite = @{}
            foreach ($s in $fixture.suites) {
                $index = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::Ordinal)
                foreach ($n in $s.nodes) { $index.Add([string]$n.Uid, $n) }
                $bySuite[$s.id] = $index
            }
            $global:FFLoop = [pscustomobject]@{ Fixture = $fixture; BySuite = $bySuite; Plans = @{}; PlanGroups = @{}; Active = 0 }
        }
        function global:Get-FFArgument([object[]]$Arguments, [string]$Name) {
            $i = [array]::IndexOf($Arguments, $Name)
            if ($i -ge 0 -and $i + 1 -lt $Arguments.Count) { return [string]$Arguments[$i + 1] }
            return ''
        }
        function global:Write-FFTrace($Row) {
            [IO.File]::AppendAllText((Join-Path $global:FFLoopDir 'trace.jsonl'), ((ConvertTo-Json -InputObject $Row -Depth 12 -Compress) + "`n"))
        }
        function global:Write-FFDiagnostic([string]$Directory, [object[]]$Nodes, [string]$State) {
            [void][IO.Directory]::CreateDirectory($Directory)
            $text = [Text.StringBuilder]::new("Version: '1.44.0.0'`nVersion: 2.2.2`n")
            foreach ($n in $Nodes) {
                [void]$text.Append('TestNodeUpdateMessage { TestNode = TestNode { Uid = TestNodeUid { Value = ').Append([string]$n.Uid).Append(' }, DisplayName = fixture, Properties = [ TestMethodIdentifierProperty { AssemblyFullName = Fixture, Namespace = ').Append([string]$n.Namespace).Append(', TypeName = ').Append([string]$n.Type).Append(', MethodName = ').Append([string]$n.Method).Append(', MethodArity = 0, ParameterTypeFullNames = [], ReturnTypeFullName = System.Void }, ').Append($State).Append("TestNodeStateProperty { Explanation =  } ] } }`n")
            }
            [IO.File]::WriteAllText((Join-Path $Directory 'fixture.diag'), $text.ToString())
        }
        function global:Write-FFNodeTrx([string]$Path, [object[]]$Nodes, [string]$State) {
            $defs = [Text.StringBuilder]::new('<TestDefinitions>'); $results = [Text.StringBuilder]::new('<Results>')
            foreach ($n in $Nodes) {
                $id = [string]$n.Uid; $cls = [string]$n.ClassName; $method = [string]$n.Method
                [void]$defs.Append('<UnitTest id="').Append($id).Append('" name="').Append($cls).Append('.').Append($method).Append('"><TestMethod className="').Append($cls).Append('" name="').Append($method).Append('" /></UnitTest>')
                [void]$results.Append('<UnitTestResult testId="').Append($id).Append('" testName="').Append($cls).Append('.').Append($method).Append('" outcome="').Append($State).Append('" />')
            }
            [void]$defs.Append('</TestDefinitions>'); [void]$results.Append('</Results>')
            [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($Path))
            [IO.File]::WriteAllText($Path, '<?xml version="1.0" encoding="utf-8"?><TestRun>' + $defs.ToString() + $results.ToString() + '</TestRun>')
        }
        $NightlySeams = @{
            HostPlatform = { 'windows' }
            E2EPrerequisites = { return [pscustomobject]@{ Ok = $true; Rows = @() } }
            # The real probes (Get-ToolchainFloorVersionText), run once per process.
            ToolchainVersions = {
                param($tool)
                if ($null -eq $global:FFToolchainText) { $global:FFToolchainText = @{} }
                if (-not $global:FFToolchainText.ContainsKey([string]$tool)) {
                    $text = ''
                    if ($tool -eq 'git') { $text = ((& git --version 2>$null) | Out-String) }
                    elseif ($tool -eq 'dotnet-sdk') { $text = ((& dotnet --list-sdks 2>$null) | Out-String) }
                    $global:FFToolchainText[[string]$tool] = $text
                }
                return $global:FFToolchainText[[string]$tool]
            }
            StartProcess = {
                param($FilePath, $ArgumentList, $WorkingDirectory, $TimeoutMilliseconds, $Environment)
                $state = $global:FFLoop
                $timing = [string]$state.Fixture.timingRoot
                $sid = ''; $cid = ''
                if ($Environment) { $sid = [string]$Environment['ANTIPHON_TEST_PHASE_SUITE']; $cid = [string]$Environment['ANTIPHON_TEST_PHASE_GROUP_ID'] }
                $diag = Get-FFArgument $ArgumentList '--diagnostic-output-directory'
                if ($ArgumentList -contains '--list-tests') {
                    if ($state.Fixture.variant -ceq 'identity-io') { [void][IO.Directory]::CreateDirectory((Join-Path $timing 'active-chunk.json')) }
                    $sid = (Split-Path -Leaf $diag) -replace '-profile-discovery$', '' -replace '-.*-discovery$', ''
                    foreach ($s in $state.Fixture.suites) { if ((Split-Path -Leaf $diag).StartsWith($s.id + '-')) { $sid = $s.id; break } }
                    $s = @($state.Fixture.suites | Where-Object { $_.id -ceq $sid })[0]
                    Write-FFTrace ([ordered]@{ kind = 'discovery'; suite = $sid })
                    Write-FFDiagnostic $diag @($s.nodes) 'Discovered'
                } elseif ($sid) {
                    $active = [Antiphon.Nightly.QualificationHeartbeat]::ReadActiveChunkIdentity($timing)
                    $state.Active++
                    $s = @($state.Fixture.suites | Where-Object { $_.id -ceq $sid })[0]
                    $planPath = Join-Path $timing ('groups-planned-' + $sid + '.json')
                    # One parse per written plan, and one chunk index per parse: the first group of each chunk id.
                    $planInfo = [IO.FileInfo]::new($planPath); $planKey = $planInfo.FullName + '|' + $planInfo.Length + '|' + $planInfo.LastWriteTimeUtc.Ticks
                    if (-not $state.Plans.ContainsKey($planKey)) {
                        $state.Plans[$planKey] = [IO.File]::ReadAllText($planPath) | ConvertFrom-Json
                        $byChunk = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::Ordinal)
                        foreach ($g in $state.Plans[$planKey].groups) { if ($g.chunk -is [string] -and -not $byChunk.ContainsKey($g.chunk)) { $byChunk.Add($g.chunk, $g) } }
                        $state.PlanGroups[$planKey] = $byChunk
                    }
                    $group = $null
                    if (-not $state.PlanGroups[$planKey].TryGetValue($cid, [ref]$group)) { $group = $null }
                    $nodes = @(foreach ($uid in $group.plannedTestIds) { $state.BySuite[$sid][[string]$uid] })
                    if ($nodes.Count -eq 0) { $nodes = @($s.nodes | Where-Object { @($group.classes) -ccontains $_.ClassName }) }
                    $legacyCost = $null; $qualifiedCost = $null
                    if (-not $state.Fixture.replay) {
                        $lookup = Join-Path $timing 'groups-planned.json'
                        $legacyCost = [Antiphon.Nightly.QualificationHeartbeat]::PlannedCost($lookup, $cid).Seconds
                        $qualifiedCost = [Antiphon.Nightly.QualificationHeartbeat]::PlannedCost($lookup, $cid, $sid, 'actual-run', '1').Seconds
                    }
                    $provenBinding = [bool]($active -and $active.sourceDigest -cmatch '^[a-f0-9]{40}$' -and $active.policyDigest -cmatch '^[a-f0-9]{64}$' -and $active.costFileDigest -cmatch '^[a-f0-9]{64}$' -and $active.assemblyDigest -cmatch '^[a-f0-9]{64}$')
                    # With UseAppHost=false the host is dotnet exec <assembly>: retain the assembly as well as its launcher.
                    $testHost = $FilePath
                    if ($ArgumentList.Count -gt 1 -and $ArgumentList[0] -ceq 'exec') { $testHost = [string]$ArgumentList[1] }
                    $special = ($sid -ceq 'antiphon' -and @($group.classes | Where-Object { $_ -clike '*Priority*' }).Count -gt 0)
                    $outcome = 'Passed'; $code = 0; $timeout = $false
                    if ($special -and $state.Fixture.variant -ceq 'failure') { $outcome = 'Failed'; $code = 2 }
                    if ($special -and $state.Fixture.variant -ceq 'timeout') { $timeout = $true; $code = 1 }
                    $trx = Join-Path (Get-FFArgument $ArgumentList '--results-directory') (Get-FFArgument $ArgumentList '--report-trx-filename')
                    Write-FFNodeTrx $trx $nodes $outcome
                    Write-FFDiagnostic $diag $nodes $outcome
                    $settled = [IO.File]::Exists((Join-Path $timing 'groups-executed.json'))
                    # The identity under the active-chunk.json names.
                    $identity = $null
                    if ($active) { $identity = [ordered]@{ format = $active.Format; state = $active.State; generation = $active.Generation; runId = $active.RunId; attempt = $active.Attempt; suite = $active.Suite; chunk = $active.Chunk; planFile = $active.PlanFile; planDigest = $active.PlanDigest; sourceDigest = $active.SourceDigest; policyDigest = $active.PolicyDigest; costFileDigest = $active.CostFileDigest; assemblyDigest = $active.AssemblyDigest; identityDigest = $active.IdentityDigest } }
                    Write-FFTrace ([ordered]@{ kind = 'launch'; suite = $sid; chunk = $cid; key = ($sid + '/' + $cid); host = $testHost; launcher = $FilePath; nodes = @($nodes | ForEach-Object { [string]$_.Uid })
                        filter = (Get-FFArgument $ArgumentList '--treenode-filter'); legacyCost = $legacyCost; qualifiedCost = $qualifiedCost; plannedCost = $group.plannedSeconds
                        provenBinding = $provenBinding; identity = $identity; settled = $settled; active = $state.Active })
                    $state.Active--
                    return [pscustomobject]@{ ExitCode = $code; TimedOut = $timeout; ChildrenExited = $true; CleanupComplete = $true; JobEmptyObserved = $true; LogText = '' }
                } elseif ($ArgumentList -contains 'Antiphon.sln') { Write-FFTrace ([ordered]@{ kind = 'build' }) }
                return [pscustomobject]@{ ExitCode = 0; TimedOut = $false; ChildrenExited = $true; CleanupComplete = $true; JobEmptyObserved = $true; LogText = '' }
            }
        }
        """;

    private static readonly string RunnerText = """
        # W2-nightly S17: the nightly tests run (NightlyTests.Run) on the splat in args.json and its seams file, its result as JSON.
        param([string]$ArgsPath, [string]$ResultPath)
        $ErrorActionPreference = 'Stop'
        $a = [IO.File]::ReadAllText($ArgsPath) | ConvertFrom-Json -AsHashtable
        $lib = Join-Path $a.RepoRoot 'scripts/lib'
        . (Join-Path $lib 'nightly-common.ps1')
        . (Join-Path $lib 'nightly-core.ps1'); $null = Test-NightlyCoreAvailable
        . (Join-Path $lib 'c487-harness.ps1')
        $splat = @{}
        foreach ($k in $a.Keys) { $splat[$k] = $a[$k] }
        $splat.AllowSharedTree = $true
        $result = Invoke-C487NightlyTests -Parameters $splat
        [IO.File]::WriteAllText($ResultPath, (ConvertTo-Json -InputObject ([ordered]@{ ExitCode = $result.ExitCode; testsPassed = [bool]$result.testsPassed; coverageComplete = [bool]$result.coverageComplete }) -Compress))
        """;

    /// <summary>
    /// Invoke-FFActualLoop: the fixture's suites as the policy's rc required suites (their chunks as the declared
    /// chunks; the replay keeps its Windows exclusions, the small fixture none), a Fixture.Never exclusion per suite
    /// unless noExclusions, run as the nightly tests run (NightlyTests.Run) on the loop seams; history, when given, is
    /// its prior-failures file.
    /// </summary>
    public static async Task<LoopRun> Run(string root, LoopFixture fx, string name, string? historyPath, string variant = "pass", bool noExclusions = false,
        CancellationToken cancellationToken = default)
    {
        var dir = System.IO.Path.Combine(root, name);
        Directory.CreateDirectory(dir);
        var timing = System.IO.Path.Combine(dir, "timing");
        var policy = JsonNode.Parse(File.ReadAllText(NightlyPolicy.PolicyPath(H.Repo)))!.AsObject();
        var rc = policy["profiles"]!["rc"]!.AsObject();
        rc["requiredSuites"] = new JsonArray(fx.Suites.Select(s => (JsonNode?)JsonValue.Create(H.Str(s, "id"))).ToArray());
        var chunks = new JsonObject();
        foreach (var suite in fx.Suites)
        {
            var rows = fx.Replay
                ? new JsonArray(new JsonObject { ["id"] = "all", ["classes"] = new JsonArray() })
                : new JsonArray(H.Items(suite, "chunks").Select(c => (JsonNode?)new JsonObject
                {
                    ["id"] = H.Str(c, "id"),
                    ["classes"] = new JsonArray(H.Items(c, "classes").Select(k => (JsonNode?)JsonValue.Create(PsValue.Str(k))).ToArray()),
                    ["seconds"] = JsonValue.Create(PsValue.Double(PsValue.Prop(c, "seconds"))),
                }).ToArray());
            chunks[H.Str(suite, "id")] = rows;
        }
        policy.Remove("chunks");
        policy["chunks"] = chunks;
        // The replay keeps the actual Windows exclusions; the small interleaving witness has none in its first suite.
        if (!fx.Replay) rc["exclusions"] = new JsonArray();
        if (!noExclusions)
            foreach (var suite in fx.Suites)
                rc["exclusions"]!.AsArray().Add(new JsonObject { ["suite"] = H.Str(suite, "id"), ["class"] = "Fixture.Never", ["reason"] = "fixture", ["owner"] = "CARD-1514" });
        policy["policyHash"] = NightlyPolicy.HashText(policy.ToJsonString());
        var policyFile = System.IO.Path.Combine(dir, "policy.json");
        File.WriteAllText(policyFile, policy.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
        H.WriteJson(System.IO.Path.Combine(dir, "fixture.json"), new OrderedDictionary
        {
            { "variant", variant }, { "replay", fx.Replay }, { "timingRoot", timing },
            { "suites", fx.Suites.Select(s => (object)new OrderedDictionary
                {
                    { "id", H.Str(s, "id") },
                    { "nodes", H.Items(s, "nodes").Select(n => (object)new OrderedDictionary
                        {
                            { "Uid", H.Str(n, "Uid") }, { "ClassName", H.Str(n, "ClassName") }, { "Namespace", H.Str(n, "Namespace") }, { "Type", H.Str(n, "Type") }, { "Method", H.Str(n, "Method") },
                        }).ToArray() },
                }).ToArray() },
        });
        var seams = System.IO.Path.Combine(dir, "seams.ps1");
        File.WriteAllText(seams, SeamsText, new UTF8Encoding(false));
        var runner = System.IO.Path.Combine(dir, "run-nightly-tests.ps1");
        File.WriteAllText(runner, RunnerText, new UTF8Encoding(false));
        var args = new OrderedDictionary
        {
            { "RepoRoot", H.Repo }, { "LogRoot", dir }, { "TimingRoot", timing }, { "RunId", "actual-run" }, { "Attempt", 1 }, { "Sha", new string('a', 40) },
            { "GitRef", "origin/rc/2026-10-27" }, { "Profile", "rc" }, { "PolicyPath", policyFile }, { "SeamsPath", seams },
        };
        if (historyPath != null)
        {
            args["PriorFailuresPath"] = historyPath;
            args["PriorFailuresDigest"] = File.Exists(historyPath) ? PriorFailureHistory.Digest(historyPath) : "";
        }
        var argsPath = System.IO.Path.Combine(dir, "args.json");
        H.WriteJson(argsPath, args);
        var resultPath = System.IO.Path.Combine(dir, "result.json");
        var output = await Pwsh(runner, ["-ArgsPath", argsPath, "-ResultPath", resultPath], dir, TimeSpan.FromMinutes(15), cancellationToken);
        if (!File.Exists(resultPath)) throw new InvalidOperationException("the nightly tests run wrote no result (" + name + "):\n" + output);
        var result = ReadJson(resultPath);
        var trace = new LoopTrace();
        var tracePath = System.IO.Path.Combine(dir, "trace.jsonl");
        if (File.Exists(tracePath))
            foreach (var line in File.ReadAllLines(tracePath).Where(l => l.Length > 0))
            {
                using var row = JsonDocument.Parse(line);
                var e = row.RootElement;
                switch (S(e, "kind"))
                {
                    case "discovery": trace.Discovery.Add(S(e, "suite")); break;
                    case "build": trace.Builds++; break;
                    case "launch":
                        static double? Num(JsonElement x, string n) => x.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;
                        JsonElement? identity = e.TryGetProperty("identity", out var id) && id.ValueKind == JsonValueKind.Object ? id.Clone() : null;
                        trace.Launches.Add(new LoopLaunch(S(e, "suite"), S(e, "chunk"), S(e, "key"), S(e, "host"), S(e, "launcher"), A(e, "nodes").Select(n => n.GetString() ?? "").ToArray(),
                            S(e, "filter"), Num(e, "legacyCost"), Num(e, "qualifiedCost"), Num(e, "plannedCost"), B(e, "provenBinding"), identity, B(e, "settled")));
                        trace.Max = Math.Max(trace.Max, (int)N(e, "active"));
                        break;
                }
            }
        var summaryPath = System.IO.Path.Combine(dir, "summary.json");
        var prepared = fx.Suites.Select(s =>
        {
            var plan = ReadJson(System.IO.Path.Combine(timing, "groups-planned-" + H.Str(s, "id") + ".json"));
            return (object)FailedFirstDispatchCases.Suite(H.Str(s, "id"), H.Items(s, "nodes"),
                A(plan, "groups").Select(g => (object)new PlainObject(("id", S(g, "chunk")), ("classes", A(g, "classes").Select(c => (object)(c.GetString() ?? "")).ToArray()))).ToArray(), []);
        }).ToArray();
        return new LoopRun
        {
            Root = dir, ExitCode = (int)N(result, "ExitCode"), TestsPassed = B(result, "testsPassed"), CoverageComplete = B(result, "coverageComplete"),
            Summary = ReadJson(summaryPath), SummaryValue = NightlyJson.ReadFile(summaryPath, false), Schedule = ReadJson(System.IO.Path.Combine(dir, "dispatch-schedule.json")),
            Projection = ReadJson(System.IO.Path.Combine(timing, "groups-executed.json")), Trace = trace, Prepared = prepared, Output = output,
        };
    }

    /// <summary>pwsh -NoProfile -NonInteractive -File script args: the combined output; a timeout kills the tree and throws.</summary>
    public static async Task<string> Pwsh(string script, string[] arguments, string workingDirectory, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo("pwsh") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = workingDirectory };
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-File", script }.Concat(arguments)) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limit.CancelAfter(timeout);
        var stdout = process.StandardOutput.ReadToEndAsync(limit.Token);
        var stderr = process.StandardError.ReadToEndAsync(limit.Token);
        try { await process.WaitForExitAsync(limit.Token); }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            throw new TimeoutException("pwsh " + Path.GetFileName(script) + " exceeded " + timeout);
        }
        return await stdout + await stderr;
    }

    /// <summary>The "suite/chunk" keys of a schedule document's entries.</summary>
    public static string[] Entries(JsonElement schedule) => A(schedule, "entries").Select(e => S(e, "suite") + "/" + S(e, "chunk")).ToArray();

    /// <summary>Copy-Item -Recurse of a directory.</summary>
    public static void CopyTree(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var file in Directory.EnumerateFiles(source)) File.Copy(file, Path.Combine(target, Path.GetFileName(file)));
        foreach (var directory in Directory.EnumerateDirectories(source)) CopyTree(directory, Path.Combine(target, Path.GetFileName(directory)));
    }
}
