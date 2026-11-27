using System.Collections.Specialized;
using System.Text.Json;
using System.Text.Json.Nodes;
using Antiphon.Nightly;
using Antiphon.Tests.Scripts.Nightly;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-1311: the RC lane (Antiphon.Nightly.ReleaseCut, the release-cut command) in process. Unit 15 of
/// docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md moved the body of scripts/release-cut.ps1 here; the script
/// is Windmill's thin launcher (AntiphonCliLauncherTests' Release_cut_launcher cases). The candidate is the real
/// ReleaseCandidate over a fake remote (FakeCandidateRemote) or a real local bare origin (ReleaseCutWorld.Real), the
/// selection attempt the real ReleaseSelectionAttempt on a real checkout, the rc run a recorded stand-in (the run itself is
/// ChangeAwareReleaseTests.C1313_ParameterHopsInProcess, through NightlyRun on an rc chain) and the publisher the real
/// ReleasePublish in test mode over fake git and GitHub, so no real remote is ever reached.
/// </summary>
[Category("Integration")]
public sealed class ReleaseCutTests
{
    [Test]
    [ParallelLimiter<Antiphon.Tests.TestHelpers.ProcessSpawnLimit>]
    public void C2082_Admitted_cut_binds_the_nightly_tests_process()
    {
        using var world = ReleaseCutWorld.Real("c2082-cut-hop", withNightlyLauncher: true);
        const string invariantClass = "Fixture.Invariants.GuardTests";
        const string ordinaryClass = "Fixture.Product.OrdinaryTests";
        const string owedClass = "Fixture.Product.MessagingTests";
        const string guardA = "guard-arg-1";
        const string guardB = "guard-arg-2";
        const string ordinary = "ordinary-arg";
        const string owed = "owed-arg";
        var root = RcIncrementalCut.IncrementalRoot(world.Release);
        Directory.CreateDirectory(root);
        var approval = Path.Combine(root, "approval.json");
        NightlyFixture.WriteJson(approval, new
        {
            format = RcIncrementalCut.ApprovalFormat, replayVerdictDigest = new string('a', 64),
            selectorHash = RcIncrementalCut.SelectorHash, trustPolicyHash = "absent", shadowRunIds = new[] { "shadow-a", "shadow-b" },
        });
        NightlyFixture.WriteJson(Path.Combine(root, "last-full-sweep.json"), new
        {
            completedUtc = NightlyFixture.DefaultNow.AddHours(-1), sha = world.Sha,
        });
        var inventory = Path.Combine(root, "inventory.json");
        var scope = new RcReceiptScope { Platform = "windows", Architecture = RcReceiptScope.CurrentArchitecture(), Profile = "rc" };
        OrderedDictionary Prior(string suite, string assembly, string chunk, string type, string[] cases)
        {
            var trx = Path.Combine(root, chunk + ".trx");
            NightlyFixture.WriteTrx(trx, cases.Select(id => new NightlyTrxRow(id, type, "Check")));
            var row = new OrderedDictionary
            {
                ["id"] = suite, ["chunk"] = chunk, ["result"] = "pass", ["exitCode"] = 0, ["trx"] = trx, ["runId"] = "prior",
            };
            var content = RcChunkReceipt.ContentDigest(suite, "native",
                cases.Select(id => new RcCaseIdentity(assembly, type, "Check", id)));
            RcChunkReceipt.Stamp(row, world.Sha, content, scope);
            return row;
        }
        var prior = new[]
        {
            Prior("antiphon", "Antiphon.Tests", "prior-guard", invariantClass, [guardA, guardB]),
            Prior("antiphon", "Antiphon.Tests", "prior-ordinary", ordinaryClass, [ordinary]),
            Prior("messaging", "Antiphon.Messaging.Tests", "prior-owed", owedClass, [owed]),
        };
        var digest = RcChunkReceipt.Seal(inventory, world.Sha, 1, prior);
        var laterFailure = new OrderedDictionary
        {
            ["contentDigest"] = prior[2]["contentDigest"], ["result"] = "FAIL", ["disposition"] = "executed",
        };
        var comparison = RcComparisonBaseline.Choose("rc/fixture",
        [
            new RcComparisonInput
            {
                Kind = "rc-attempt", Line = "rc/fixture", Sha = world.Sha, RunId = "prior", Attempt = "1",
                CompletedUtc = NightlyFixture.DefaultNow.AddHours(-1), ReceiptPath = inventory, ReceiptDigest = digest,
                Sealed = true, Ancestor = true, Rows = prior,
            },
            new RcComparisonInput
            {
                Kind = "rc-attempt", Line = "rc/fixture", Sha = world.Sha, RunId = "later", Attempt = "1",
                CompletedUtc = NightlyFixture.DefaultNow.AddMinutes(-30), Ancestor = true, Rows = [laterFailure],
            },
        ]);
        comparison.Owed.ShouldHaveSingleItem().Key.ShouldBe(PsValue.Str(prior[2]["contentDigest"]));
        RcComparisonBaseline.Record(Path.Combine(root, "comparison.json"), comparison, inventory, digest);

        var testsHops = new List<NightlyTestsOptions>();
        var results = new List<NightlyTestsResult>();
        var executed = new List<string>();
        var prefixAtOrdinaryStart = new List<bool>();
        var withholdGuard = true;
        var interruptGuard = false;
        var interruptOrdinary = true;
        var observePrefix = false;
        var blockPrefixWrite = false;
        static bool HasCompletedPrefix(string schedulePath)
        {
            using var schedule = JsonDocument.Parse(File.ReadAllText(schedulePath));
            return schedule.RootElement.TryGetProperty("invariantPrefix", out var prefix)
                && prefix.GetProperty("complete").GetBoolean();
        }

        NightlyHost nightly = null!;
        var launcher = new RecordingLauncher(launch =>
        {
            if (NightlyRunHops.IsSelfHop(launch))
                return NightlyTestsWorld.Result(NightlyRun.Run(NightlyRunHops.SelfHopOptions(launch), nightly, Bindings(world.Checkout)).ExitCode);
            if (NightlyRunHops.IsTestsHop(launch))
            {
                var child = NightlyRunHops.TestsOptions(launch);
                testsHops.Add(child);
                var result = NightlyTests.Run(child, nightly, NightlyTestsWorld.SeamedBindings());
                results.Add(result);
                return NightlyTestsWorld.Result(result.ProcessExitCode);
            }

            var argv = launch.Arguments.ToList();
            string Value(string flag) => argv[argv.IndexOf(flag) + 1];
            if (argv.Contains("--list-tests"))
            {
                object Node(string uid, string type, bool invariant) => new
                {
                    uid, type = type[(type.LastIndexOf('.') + 1)..], @namespace = type[..type.LastIndexOf('.')],
                    method = "Check", state = "Discovered", categories = invariant ? new[] { "RepositoryInvariant", "Unit" } : new[] { "Unit" },
                };
                var diag = Path.Combine(Value("--diagnostic-output-directory"), "list.diag");
                var isMessaging = diag.Contains("messaging-profile", StringComparison.Ordinal);
                NightlyFixture.WriteJson(diag, new
                {
                    format = NightlyDiagnosticLog.DiscoveryFormat, tunitVersion = NightlyDiagnosticLog.SupportedTunit,
                    mtpVersion = NightlyDiagnosticLog.SupportedMtp, assemblyHash = "fixture-assembly",
                    nodes = isMessaging ? new[] { Node(owed, owedClass, false) }
                        : new[] { Node(guardA, invariantClass, true), Node(guardB, invariantClass, true), Node(ordinary, ordinaryClass, false) },
                });
            }
            else if (argv.Contains("--report-trx"))
            {
                var trxName = Value("--report-trx-filename");
                var isMessaging = trxName.StartsWith("messaging-", StringComparison.Ordinal);
                var isInvariant = trxName.StartsWith("antiphon-invariant-", StringComparison.Ordinal);
                var type = isMessaging ? owedClass : isInvariant ? invariantClass : ordinaryClass;
                executed.Add(type);
                if (observePrefix && !isInvariant)
                    prefixAtOrdinaryStart.Add(HasCompletedPrefix(Path.Combine(testsHops.Last().LogRoot, "dispatch-schedule.json")));
                if (isInvariant && interruptGuard) throw new OperationCanceledException("fixture interrupted before the invariant produced a receipt");
                if (isMessaging && interruptOrdinary) throw new OperationCanceledException("fixture interrupted before messaging produced a receipt");
                string[] ids = isMessaging ? [owed] : isInvariant ? withholdGuard ? [guardA] : [guardA, guardB] : [ordinary];
                NightlyFixture.WriteTrx(Path.Combine(Value("--results-directory"), trxName),
                    ids.Select((_, index) => new NightlyTrxRow("adapter-id-" + index, type, "Check")));
                NightlyFixture.WriteExecution(Path.Combine(Value("--diagnostic-output-directory"), "run.diag"),
                    ids.Select(id => new NightlyTestNode(id, type, "Check")));
                if (isInvariant && blockPrefixWrite)
                {
                    var schedulePath = Path.Combine(testsHops.Last().LogRoot, "dispatch-schedule.json");
                    // File.Replace cannot back up the readable schedule over this directory.
                    HasCompletedPrefix(schedulePath).ShouldBeFalse();
                    Directory.CreateDirectory(schedulePath + ".bak");
                    File.WriteAllText(Path.Combine(schedulePath + ".bak", "fixture-owner"), "fixture");
                }
            }
            return RecordingLauncher.Passed();
        });
        var processes = new FakeProcessTable();
        processes.Start(processes.CurrentPid, NightlyRunWorld.ProcessStart);
        var probe = new FakeHostProbe();
        nightly = world.Fixture.Host(git: world.HostGit, launcher: launcher, processes: processes, probe: probe);
        NightlyRunBindings Bindings(string productRoot) => new()
        {
            CliPath = NightlyRunWorld.StagedCli,
            ProductRoot = productRoot,
            RcEvidence = NightlyRcChain.Suppressed(),
        };
        world.Answer = options =>
        {
            options.NoReport = true;
            options.Suites = ["antiphon", "messaging"];
            return NightlyRun.Run(options, nightly, Bindings(NightlyPlanCases.Repo));
        };
        ReleaseCutResult Cut(string id, string mode = "Incremental") => world.Cut(options =>
        {
            options.SelectionMode = mode;
            options.RunId = id;
            options.Attempt = 2;
            options.SkipPublish = true;
        });
        var missing = Cut("c2082-missing");
        missing.CoverageScope.ShouldBe("Incremental", missing.IncrementalReason);
        launcher.Launches.Count(NightlyRunHops.IsSelfHop).ShouldBe(1);
        testsHops.Count.ShouldBe(1, string.Join('\n', world.Lines));
        var child = testsHops.Single();
        child.RequestedSelectionMode.ShouldBe("Incremental");
        child.IncrementalApprovalDigest.ShouldBe(RcChunkReceipt.FileDigest(approval));
        child.IncrementalBaselineSha.ShouldBe(world.Sha);
        child.IncrementalInventoryPath.ShouldBe(inventory);
        child.IncrementalInventoryDigest.ShouldBe(digest);
        child.IncrementalOwedDigest.ShouldBe(RcComparisonBaseline.OwedDigest(comparison.Owed));
        missing.Tested.ShouldBeFalse();
        executed.ShouldBe([invariantClass]);
        results.Single().Reasons.ShouldContain(reason => reason.Contains("repository-invariant-receipt:", StringComparison.Ordinal));
        File.Exists(Path.Combine(child.LogRoot, "rc-chunk-receipts.json")).ShouldBeFalse();

        withholdGuard = false;
        executed.Clear();
        var interrupted = Cut("c2082-resume");
        interrupted.Tested.ShouldBeFalse();
        executed.ShouldBe([invariantClass, owedClass], string.Join('\n', results.Last().Reasons));
        var attempt = testsHops.Last().LogRoot;
        File.ReadLines(Path.Combine(attempt, "incremental-launches.jsonl")).Count().ShouldBe(1);

        interruptOrdinary = false;
        executed.Clear();
        var resumed = Cut("c2082-resume");
        resumed.Tested.ShouldBeTrue(string.Join('\n', results.Last().Reasons));
        resumed.Published.ShouldBeFalse();
        executed.ShouldBe([owedClass]);
        testsHops.Last().LogRoot.ShouldBe(attempt);
        var ledger = RcChunkReceipt.Read(Path.Combine(attempt, "rc-chunk-receipts.json"));
        ledger.Refusal.ShouldBeEmpty();
        ledger.Rows.Length.ShouldBe(3);
        var carried = ledger.Rows.Single(row => PsValue.Str(row["disposition"]) == "carried");
        PsValue.Str(carried["originChunk"]).ShouldBe("prior-ordinary");
        PsValue.Str(carried["originReceiptDigest"]).ShouldBe(digest);
        var fresh = ledger.Rows.Single(row => PsValue.Str(row["chunk"]).StartsWith("antiphon/invariant-", StringComparison.Ordinal));
        PsValue.Str(fresh["runId"]).ShouldBe("c2082-resume");
        PsValue.Str(fresh["attempt"]).ShouldBe("2");
        PsValue.Str(fresh["sha"]).ShouldBe(world.Sha);

        observePrefix = true;
        executed.Clear();
        Cut("c2082-full", "Full").Tested.ShouldBeTrue(string.Join('\n', results.Last().Reasons));
        testsHops.Last().RequestedSelectionMode.ShouldBeEmpty();
        testsHops.Last().IncrementalInventoryDigest.ShouldBeEmpty();
        results.Last().CoverageScope.ShouldBe("Full");
        executed.ShouldBe([invariantClass, ordinaryClass, owedClass], ignoreOrder: true);
        prefixAtOrdinaryStart.ShouldBe([true, true]);

        var opaqueInventory = Path.Combine(root, "opaque-inventory.json");
        var opaque = Prior("client", "client", "prior-opaque", "Client", []);
        var opaqueDigest = RcChunkReceipt.Seal(opaqueInventory, world.Sha, 1, [.. prior, opaque]);
        RcComparisonBaseline.Record(Path.Combine(root, "comparison.json"), comparison, opaqueInventory, opaqueDigest);

        JsonElement RunFallback(string id, bool tested, string[] expectedStarts)
        {
            executed.Clear();
            prefixAtOrdinaryStart.Clear();
            var cut = Cut(id);
            cut.Tested.ShouldBe(tested, string.Join('\n', results.Last().Reasons));
            cut.Published.ShouldBeFalse();
            executed.ShouldBe(expectedStarts);
            var hop = testsHops.Last();
            hop.RequestedSelectionMode.ShouldBe("Incremental");
            hop.IncrementalInventoryDigest.ShouldBe(opaqueDigest);
            File.Exists(Path.Combine(hop.LogRoot, "incremental-plan.json")).ShouldBeFalse();
            using var schedule = JsonDocument.Parse(File.ReadAllText(Path.Combine(hop.LogRoot, "dispatch-schedule.json")));
            schedule.RootElement.TryGetProperty("invariantPrefix", out var prefix).ShouldBeTrue(id + " must persist invariantPrefix");
            using var summary = JsonDocument.Parse(File.ReadAllText(Path.Combine(hop.LogRoot, "summary.json")));
            summary.RootElement.GetProperty("coverageScope").GetString().ShouldBe("Full", id);
            return prefix.Clone();
        }

        void AssertIncompletePrefix(JsonElement prefix, string[] missingUids)
        {
            prefix.GetProperty("complete").GetBoolean().ShouldBeFalse();
            prefix.GetProperty("suite").GetString().ShouldBe("antiphon");
            var chunk = prefix.GetProperty("chunk").GetString();
            chunk.ShouldStartWith("invariant-");
            var outstanding = prefix.GetProperty("outstanding").EnumerateArray().ToArray();
            outstanding.Select(row => row.GetProperty("suite").GetString()).ShouldBe(["antiphon", "antiphon", "messaging"]);
            var missing = outstanding.Single(row => row.GetProperty("chunk").GetString() == chunk);
            missing.GetProperty("missingUids").EnumerateArray().Select(uid => uid.GetString()).ShouldBe(missingUids);
        }

        withholdGuard = true;
        AssertIncompletePrefix(RunFallback("c2082-opaque-partial", false, [invariantClass]), ["Antiphon.Tests/" + guardB]);
        withholdGuard = false;
        interruptGuard = true;
        AssertIncompletePrefix(RunFallback("c2082-opaque-interrupted", false, [invariantClass]),
            ["Antiphon.Tests/" + guardA, "Antiphon.Tests/" + guardB]);
        interruptGuard = false;
        var completed = RunFallback("c2082-opaque-complete", true, [invariantClass, ordinaryClass, owedClass]);
        prefixAtOrdinaryStart.ShouldBe([true, true]);
        completed.GetProperty("complete").GetBoolean().ShouldBeTrue();
        completed.GetProperty("executedGroups").GetInt32().ShouldBe(1);
        completed.TryGetProperty("outstanding", out _).ShouldBeFalse();

        blockPrefixWrite = true;
        foreach (var mode in new[] { "Incremental", "Full" })
        {
            executed.Clear();
            prefixAtOrdinaryStart.Clear();
            var refused = Cut("c2082-prefix-write-failure-" + mode, mode);
            executed.ShouldBe([invariantClass], $"{mode} Tested={refused.Tested}, prefix at ordinary starts: [{string.Join(", ", prefixAtOrdinaryStart)}]");
            prefixAtOrdinaryStart.ShouldBeEmpty();
            refused.Tested.ShouldBeFalse();
            refused.Published.ShouldBeFalse();
            refused.ExitCode.ShouldBe(1);
            refused.Refusal.ShouldBe("repository-invariant-prefix-write-failed");
            var result = results.Last();
            result.Reasons.ShouldContain("repository-invariant-prefix-write-failed");
            result.CoverageScope.ShouldBe("Full");
            result.CoverageComplete.ShouldBeFalse();
            result.TestsPassed.ShouldBeFalse();
            using var summary = JsonDocument.Parse(File.ReadAllText(result.SummaryPath));
            summary.RootElement.GetProperty("succeeded").GetBoolean().ShouldBeFalse();
            summary.RootElement.GetProperty("preflight").GetProperty("unhandledError").GetString()
                .ShouldBe("repository-invariant-prefix-write-failed");
            HasCompletedPrefix(Path.Combine(testsHops.Last().LogRoot, "dispatch-schedule.json")).ShouldBeFalse();
        }

        probe.PlatformName = "linux";
        executed.Clear();
        Cut("c2082-linux").Tested.ShouldBeFalse();
        results.Last().Refusal.ShouldBe("incremental-windows-required");
        executed.ShouldBeEmpty();
    }

    // CARD-1836 S6 (CP-6, V-6, G-20..G-24): an Incremental cut reconciles the owed roster and never reaches the publisher.
    [Test, Timeout(60_000)]
    [ParallelLimiter<Antiphon.Tests.TestHelpers.ProcessSpawnLimit>]
    public void An_incremental_cut_reconciles_the_roster_and_withholds_publication(CancellationToken token)
    {
        var rows = new NightlyCaseRows();
        var roster = Enumerable.Range(1, 5).Select(i => new RcPlannedChunk
        {
            Id = "c" + i, Classes = ["Ns.C" + i], Cases = ["c" + i],
            ContentDigest = RcChunkReceipt.ContentDigest("antiphon", "native", [new RcCaseIdentity("Antiphon.Tests", "Ns.C" + i, "M", "c" + i)])!,
        }).ToArray();
        string[] owed = ["c2", "c3", "c5"];

        // One world per scenario: a prior attempt at the same SHA sealed c1 and c4 clean, c2 FAIL, c3 a skip under a pass label, c5 missing.
        (ReleaseCutWorld World, string Inventory, List<string> Launches, Func<RcIncrementalExecution?> Last) Setup(string prefix, bool approve = true, TimeSpan? sweepAge = null,
            bool withdrawn = false, Action<NightlyRunOptions>? beforeTests = null, string? inventorySha = null, Func<int, string>? platformOf = null, bool laterFail = false,
            Func<int, string>? profileOf = null)
        {
            var world = ReleaseCutWorld.Real(prefix);
            var dir = RcIncrementalCut.IncrementalRoot(world.Release);
            var prior = Path.Combine(world.Release, "prior");
            Directory.CreateDirectory(prior);
            System.Collections.IDictionary Row(int i, string result, int exit, params (string Case, string Outcome)[] cases)
            {
                NightlyFixture.WriteTrx(Path.Combine(prior, "c" + i + ".trx"), cases.Select(c => new NightlyTrxRow(c.Case, "Ns.C" + i, "M", c.Outcome)));
                return new System.Collections.Specialized.OrderedDictionary
                {
                    ["id"] = "antiphon", ["chunk"] = "c" + i, ["result"] = result, ["exitCode"] = exit, ["timedOut"] = false, ["skipped"] = false,
                    ["evidenceReasons"] = Array.Empty<object>(), ["trx"] = "c" + i + ".trx", ["sha"] = world.Sha, ["contentDigest"] = roster[i - 1].ContentDigest,
                    ["disposition"] = "executed", ["runId"] = "prior-run", ["platform"] = platformOf?.Invoke(i) ?? "windows",
                    ["architecture"] = RcReceiptScope.CurrentArchitecture(), ["profile"] = profileOf?.Invoke(i) ?? "rc",
                };
            }
            var inventory = Path.Combine(prior, "rc-chunk-receipts.json");
            System.Collections.IDictionary[] sealedRows =
                [Row(1, "pass", 0, ("c1", "Passed")), Row(2, "FAIL", 1, ("c2", "Failed")), Row(3, "pass", 0, ("c3", "Passed"), ("c3b", "NotExecuted")), Row(4, "pass", 0, ("c4", "Passed"))];
            var digest = RcChunkReceipt.Seal(inventory, inventorySha ?? world.Sha, 1, sealedRows);
            if (approve)
                NightlyFixture.WriteJson(Path.Combine(dir, "approval.json"), new
                {
                    format = "antiphon-rc-incremental-approval-v1", replayVerdictDigest = new string('e', 64), selectorHash = RcIncrementalCut.SelectorHash,
                    trustPolicyHash = "absent", shadowRunIds = new[] { "shadow-1", "shadow-2" }, withdrawn,
                });
            NightlyFixture.WriteJson(Path.Combine(dir, "last-full-sweep.json"), new
            {
                completedUtc = (NightlyFixture.DefaultNow - (sweepAge ?? TimeSpan.FromDays(1))).UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ"), sha = world.Sha,
            });
            // The comparison is recorded from the D-1 choice: its baseline and every later open obligation (Review r1 D-3).
            List<RcComparisonInput> attempts =
            [
                new() { Kind = "rc-attempt", Line = "rc/fixture", Sha = world.Sha, RunId = "prior-run", Attempt = "1", CompletedUtc = NightlyFixture.DefaultNow.AddHours(-2),
                    ReceiptPath = inventory, ReceiptDigest = digest, Sealed = true, Ancestor = true, Rows = sealedRows },
            ];
            if (laterFail)
                attempts.Add(new() { Kind = "rc-attempt", Line = "rc/fixture", Sha = world.Sha, RunId = "later-run", Attempt = "1", CompletedUtc = NightlyFixture.DefaultNow.AddHours(-1),
                    ReceiptPath = "later", ReceiptDigest = "later", Sealed = false, Ancestor = true, Rows =
                    [
                        new System.Collections.Specialized.OrderedDictionary
                        {
                            ["id"] = "antiphon", ["chunk"] = "c4", ["result"] = "FAIL", ["exitCode"] = 1, ["sha"] = world.Sha, ["contentDigest"] = roster[3].ContentDigest,
                            ["disposition"] = "executed", ["skipped"] = false,
                        },
                    ] });
            RcComparisonBaseline.Record(Path.Combine(dir, "comparison.json"), RcComparisonBaseline.Choose("rc/fixture", attempts), inventory, digest);
            var launches = new List<string>();
            RcIncrementalExecution? last = null;
            world.Answer = o =>
            {
                beforeTests?.Invoke(o);
                last = RcIncrementalCut.Execute(o, roster, Path.Combine(world.CandidateRoot, "attempts", o.RunId), c =>
                {
                    launches.Add(c.Id);
                    return new System.Collections.Specialized.OrderedDictionary { ["id"] = "antiphon", ["chunk"] = c.Id, ["result"] = "pass", ["exitCode"] = 0 };
                }, platform: "windows");
                return new NightlyRunResult { ExitCode = last.Refusal.Length == 0 && last.AllGreen ? 0 : 1, Refusal = last.Refusal, RunId = o.RunId };
            };
            return (world, inventory, launches, () => last);
        }
        string[] Sorted(IEnumerable<string> keys) => keys.OrderBy(k => k, StringComparer.Ordinal).ToArray();
        bool NoPublisher(ReleaseCutWorld w) => !w.Lines.Any(l => l.StartsWith("[publish-release]", StringComparison.Ordinal)) && w.PublisherGit.Trace.Count == 0 && w.GitHub.Trace.Count == 0;

        var closed = Setup("c1836-closed", approve: false);
        using (closed.World)
        {
            var cut = closed.World.Cut(o => { o.SelectionMode = "Incremental"; o.RunId = "closed-run"; });
            rows.Check(cut.CoverageScope == "Full" && cut.IncrementalReason == "incremental-not-admitted:approval-absent" && closed.World.Runs.Single().SelectionMode == "Full"
                && closed.Launches.SequenceEqual(roster.Select(c => c.Id)) && cut.Tested && !cut.Published && NoPublisher(closed.World), "s6-gate-closed-full",
                cut.CoverageScope + " " + cut.IncrementalReason + " " + string.Join(",", closed.Launches) + " " + cut.Refusal);
        }

        var admitted = Setup("c1836-admitted");
        using (admitted.World)
        {
            var cut = admitted.World.Cut(o => { o.SelectionMode = "Incremental"; o.RunId = "admitted-run"; });
            var exec = admitted.Last()!;
            var inventoryDigest = RcChunkReceipt.FileDigest(admitted.Inventory);
            rows.Check(cut.CoverageScope == "Incremental" && Sorted(admitted.Launches).SequenceEqual(owed) && admitted.Launches.Count == owed.Length
                && Sorted(exec.Carried.Select(c => PsValue.Str(c["chunk"]))).SequenceEqual(["c1", "c4"])
                && exec.Carried.All(c => PsValue.Str(c["disposition"]) == "carried" && PsValue.Str(c["runId"]) == "prior-run" && PsValue.Str(c["originReceiptDigest"]) == inventoryDigest)
                && exec.Reconciled && exec.AllGreen, "s6-gate-admitted-owed-only", string.Join(",", admitted.Launches) + " " + exec.Refusal + " " + cut.IncrementalReason);
            rows.Check(cut.Tested && !cut.Published && cut.ExitCode == 0 && NoPublisher(admitted.World) && exec.CoverageScope == "Incremental", "s6-no-publication",
                string.Join(" | ", admitted.World.Lines.Where(l => l.Contains("publish", StringComparison.Ordinal))));

            // Crash after the first owed launch, then resume the same attempt: only the unlaunched owed chunks launch, none twice.
            var runOptions = admitted.World.Runs.Single();
            var attempt = Path.Combine(admitted.World.CandidateRoot, "attempts", "resume-run");
            var resumed = new List<string>();
            var crashes = 0;
            Should.Throw<InvalidOperationException>(() => RcIncrementalCut.Execute(runOptions, roster, attempt, c =>
            {
                if (resumed.Count == 1) { crashes++; throw new InvalidOperationException("crash"); }
                resumed.Add(c.Id);
                return new System.Collections.Specialized.OrderedDictionary { ["chunk"] = c.Id, ["result"] = "pass", ["exitCode"] = 0 };
            }, platform: "windows"));
            var second = RcIncrementalCut.Execute(runOptions, roster, attempt, c =>
            {
                resumed.Add(c.Id);
                return new System.Collections.Specialized.OrderedDictionary { ["chunk"] = c.Id, ["result"] = "pass", ["exitCode"] = 0 };
            }, platform: "windows");
            rows.Check(crashes == 1 && Sorted(resumed).SequenceEqual(owed) && resumed.Count == owed.Length && second.Launched.Count == owed.Length - 1 && second.Reconciled,
                "s6-crash-resume", string.Join(",", resumed) + " " + second.Refusal);
        }
        token.ThrowIfCancellationRequested();

        var full = Setup("c1836-full");
        using (full.World)
        {
            var cut = full.World.Cut(o => o.RunId = "full-run");
            rows.Check(cut.CoverageScope == "Full" && cut.RequestedMode == "" && full.Launches.Count == roster.Length
                && full.World.Lines.Count(l => l.StartsWith("[publish-release]", StringComparison.Ordinal)) == 1, "s6-explicit-full-publishes",
                string.Join(" | ", full.World.Lines.Where(l => l.StartsWith("[publish-release]", StringComparison.Ordinal))));
        }

        var scriptsRoot = Path.Combine(NightlyPlanCases.Repo, "scripts");
        var cutScript = File.ReadAllText(Path.Combine(scriptsRoot, "release-cut.ps1"));
        var runScript = File.ReadAllText(Path.Combine(scriptsRoot, "nightly-run.ps1"));
        var parsed = ReleaseCutOptions.Parse(["--ref", ReleaseCutWorld.Ref], scriptsRoot, _ => null);
        var refusedFlag = Should.Throw<NightlyUsageException>(() => ReleaseCutOptions.Parse(["--ref", ReleaseCutWorld.Ref, "--selection-mode", "Incremental"], scriptsRoot, _ => null));
        rows.Check(new ReleaseCutOptions().SelectionMode == "Full" && new NightlyRunOptions().SelectionMode == "Full" && new NightlyRunOptions().RequestedSelectionMode == ""
            && new NightlyTestsOptions().SelectionMode == "Full" && parsed.SelectionMode == "Full" && refusedFlag.Message.Contains("--selection-mode", StringComparison.Ordinal)
            && cutScript.Contains("[ValidateSet('Full', 'Shadow', 'ChangeAware')][string]$SelectionMode = 'Full'", StringComparison.Ordinal)
            && runScript.Contains("[ValidateSet('Full', 'Shadow', 'ChangeAware')][string]$SelectionMode = 'Full'", StringComparison.Ordinal), "s6-default-full");

        var stale = Setup("c1836-stale", beforeTests: o => File.AppendAllText(o.IncrementalInventoryPath, " "));
        using (stale.World)
        {
            var cut = stale.World.Cut(o => { o.SelectionMode = "Incremental"; o.RunId = "stale-run"; });
            rows.Check(stale.Last()!.Refusal == "incremental-plan-stale:inventory-digest-mismatch" && stale.Launches.Count == 0 && !cut.Tested && NoPublisher(stale.World),
                "s6-stale-plan-digest", stale.Last()!.Refusal);
        }

        var mismatch = Setup("c1836-hop", beforeTests: o => o.RequestedSelectionMode = "");
        using (mismatch.World)
        {
            var cut = mismatch.World.Cut(o => { o.SelectionMode = "Incremental"; o.RunId = "hop-run"; });
            var shadowHop = mismatch.World.Runs.Single();
            shadowHop.RequestedSelectionMode = "Incremental";
            shadowHop.SelectionMode = "Shadow";
            var hop2 = new List<string>();
            var second = RcIncrementalCut.Execute(shadowHop, roster, Path.Combine(mismatch.World.CandidateRoot, "attempts", "hop-2"), c =>
            {
                hop2.Add(c.Id);
                return new System.Collections.Specialized.OrderedDictionary { ["chunk"] = c.Id, ["result"] = "pass", ["exitCode"] = 0 };
            });
            rows.Check(mismatch.Last()!.Refusal == "incremental-hop-mismatch" && second.Refusal == "incremental-hop-mismatch" && mismatch.Launches.Count == 0 && hop2.Count == 0
                && !cut.Tested,
                "s6-hop-mismatch", mismatch.Last()!.Refusal + "/" + second.Refusal);
        }

        var aged = Setup("c1836-age", sweepAge: TimeSpan.FromDays(7));
        var young = Setup("c1836-young", sweepAge: TimeSpan.FromDays(7) - TimeSpan.FromSeconds(1));
        using (aged.World)
        using (young.World)
        {
            var old = aged.World.Cut(o => { o.SelectionMode = "Incremental"; o.RunId = "age-run"; });
            var fresh = young.World.Cut(o => { o.SelectionMode = "Incremental"; o.RunId = "young-run"; });
            rows.Check(old.CoverageScope == "Full" && old.IncrementalReason == "incremental-not-admitted:full-sweep-expired" && aged.Launches.Count == roster.Length
                && fresh.CoverageScope == "Incremental", "s6-age-boundary", old.IncrementalReason + " / " + fresh.IncrementalReason);
        }

        var rollback = Setup("c1836-rollback", withdrawn: true);
        using (rollback.World)
        {
            var cut = rollback.World.Cut(o => { o.SelectionMode = "Incremental"; o.RunId = "rollback-run"; });
            rows.Check(cut.CoverageScope == "Full" && cut.IncrementalReason == "incremental-not-admitted:approval-withdrawn" && cut.Sha == rollback.World.Sha
                && rollback.Launches.SequenceEqual(roster.Select(c => c.Id)) && NoPublisher(rollback.World), "s6-rollback", cut.IncrementalReason);
        }

        // Review r1 D-3: carried provenance is bound to the comparison commit, the platform and every later open obligation.
        string[] PlanReasons(ReleaseCutWorld w, string runId, string chunk)
        {
            using var plan = System.Text.Json.JsonDocument.Parse(File.ReadAllBytes(Path.Combine(w.CandidateRoot, "attempts", runId, "incremental-plan.json")));
            return plan.RootElement.GetProperty("entries").EnumerateArray().Single(e => e.GetProperty("chunk").GetString() == chunk)
                .GetProperty("reasons").EnumerateArray().Select(r => r.GetString() ?? "").ToArray();
        }
        string Carried(RcIncrementalExecution? e) => e == null ? "none" : string.Join(",", Sorted(e.Carried.Select(c => PsValue.Str(c["chunk"]))));
        var other = "d" + new string('1', 39);

        var inventoryMoved = Setup("c1836-inv-sha", inventorySha: other);
        using (inventoryMoved.World)
        {
            var cut = inventoryMoved.World.Cut(o => { o.SelectionMode = "Incremental"; o.RunId = "inv-run"; });
            rows.Check(cut.CoverageScope == "Full" && cut.IncrementalReason == "incremental-not-admitted:comparison-inventory-sha"
                && inventoryMoved.Launches.SequenceEqual(roster.Select(c => c.Id)) && NoPublisher(inventoryMoved.World), "s6-inventory-sha-mismatch",
                cut.CoverageScope + " " + cut.IncrementalReason + " " + string.Join(",", inventoryMoved.Launches));
        }

        // Review r2 D-8 (N6): a comparison whose owed set is absent, not an object, or edited after its digest never admits;
        // the cut runs the whole roster Full and publishes nothing. An absent owed set is never read as "no later obligations".
        string Comparison(ReleaseCutWorld w) => Path.Combine(RcIncrementalCut.IncrementalRoot(w.Release), "comparison.json");
        void EditComparison(ReleaseCutWorld w, Action<JsonObject> edit)
        {
            var node = JsonNode.Parse(File.ReadAllText(Comparison(w)))!.AsObject();
            edit(node);
            File.WriteAllText(Comparison(w), node.ToJsonString());
        }
        (string Reason, string Detail) Refused(string prefix, string runId, Action<JsonObject> edit, bool laterFail = false)
        {
            var world = Setup(prefix, laterFail: laterFail);
            using (world.World)
            {
                EditComparison(world.World, edit);
                var cut = world.World.Cut(o => { o.SelectionMode = "Incremental"; o.RunId = runId; });
                var ranFull = cut.CoverageScope == "Full" && world.World.Runs.Single().SelectionMode == "Full" && world.Launches.SequenceEqual(roster.Select(c => c.Id))
                    && world.Last()?.Carried.Count == 0 && cut.Tested && !cut.Published && NoPublisher(world.World);
                return (ranFull ? cut.IncrementalReason : "not-full:" + cut.CoverageScope + ":" + cut.IncrementalReason,
                    runId + "=" + cut.CoverageScope + " " + cut.IncrementalReason + " launched=" + string.Join(",", world.Launches) + " carried=" + Carried(world.Last()));
            }
        }
        var emptyOwedDigest = RcComparisonBaseline.OwedDigest(new Dictionary<string, string>());
        // Both fields dropped (a pre-r2 or torn writer), and the owed set dropped while the empty-set digest is kept.
        var owedGone = Refused("c1836-owed-gone", "owed-gone-run", c => { c.Remove("owed"); c.Remove("owedDigest"); });
        var owedGoneDigestKept = Refused("c1836-owed-empty", "owed-empty-run", c => { c.Remove("owed"); c["owedDigest"] = emptyOwedDigest; });
        var owedNull = Refused("c1836-owed-null", "owed-null-run", c => { c["owed"] = null; c["owedDigest"] = emptyOwedDigest; });
        rows.Check(new[] { owedGone, owedGoneDigestKept, owedNull }.All(r => r.Reason == "incremental-not-admitted:comparison-owed-absent"), "s6-comparison-owed-absent",
            owedGone.Detail + " | " + owedGoneDigestKept.Detail + " | " + owedNull.Detail);

        // Group 136's shape hidden by hand: the later c4 FAIL is deleted from the owed set but the recorded digest kept; and a digest dropped.
        var owedEdited = Refused("c1836-owed-edit", "owed-edit-run", c => c["owed"]!.AsObject().Remove(roster[3].ContentDigest), laterFail: true);
        var digestGone = Refused("c1836-owed-digest", "owed-digest-run", c => c.Remove("owedDigest"));
        rows.Check(owedEdited.Reason == "incremental-not-admitted:comparison-owed-digest" && digestGone.Reason == "incremental-not-admitted:comparison-owed-digest",
            "s6-comparison-owed-digest", owedEdited.Detail + " | " + digestGone.Detail);

        // Group 136's shape: an earlier clean inventory at the same SHA, then a later partial attempt where c4 FAILed.
        var later = Setup("c1836-later", laterFail: true);
        using (later.World)
        {
            var cut = later.World.Cut(o => { o.SelectionMode = "Incremental"; o.RunId = "later-run-a"; });
            var reasons = later.Last() == null ? Array.Empty<string>() : PlanReasons(later.World, "later-run-a", "c4");
            rows.Check(cut.CoverageScope == "Incremental" && Sorted(later.Launches).SequenceEqual(["c2", "c3", "c4", "c5"]) && Carried(later.Last()) == "c1"
                && reasons.Contains("later-open:later-run:FAIL"), "s6-later-fail-same-sha",
                cut.IncrementalReason + " launched=" + string.Join(",", later.Launches) + " carried=" + Carried(later.Last()) + " c4=" + string.Join(",", reasons));
        }

        var linux = Setup("c1836-platform", platformOf: i => i == 1 ? "linux" : "windows");
        using (linux.World)
        {
            var cut = linux.World.Cut(o => { o.SelectionMode = "Incremental"; o.RunId = "platform-run"; });
            var reasons = linux.Last() == null ? Array.Empty<string>() : PlanReasons(linux.World, "platform-run", "c1");
            rows.Check(cut.CoverageScope == "Incremental" && Sorted(linux.Launches).SequenceEqual(["c1", "c2", "c3", "c5"]) && Carried(linux.Last()) == "c4"
                && reasons.Contains("platform:linux"), "s6-foreign-platform-owed",
                "launched=" + string.Join(",", linux.Launches) + " carried=" + Carried(linux.Last()) + " c1=" + string.Join(",", reasons));
        }

        // Review r2 D-7: a same-SHA receipt sealed under another profile (nightly) is owed in an rc hop.
        var nightly = Setup("c1836-profile", profileOf: i => i == 1 ? "nightly" : "rc");
        using (nightly.World)
        {
            var cut = nightly.World.Cut(o => { o.SelectionMode = "Incremental"; o.RunId = "profile-run"; });
            var reasons = nightly.Last() == null ? Array.Empty<string>() : PlanReasons(nightly.World, "profile-run", "c1");
            rows.Check(cut.CoverageScope == "Incremental" && Sorted(nightly.Launches).SequenceEqual(["c1", "c2", "c3", "c5"]) && Carried(nightly.Last()) == "c4"
                && reasons.Contains("profile:nightly"), "s6-foreign-profile-owed",
                "launched=" + string.Join(",", nightly.Launches) + " carried=" + Carried(nightly.Last()) + " c1=" + string.Join(",", reasons));
        }

        // The hop itself: clean receipts from another commit, and a carried row whose origin is not the bound baseline.
        var hop = Setup("c1836-origin");
        using (hop.World)
        {
            var prior = Path.Combine(hop.World.Release, "prior");
            System.Collections.IDictionary Clean(int i, string sha, string disposition = "executed") => new System.Collections.Specialized.OrderedDictionary
            {
                ["id"] = "antiphon", ["chunk"] = "c" + i, ["result"] = "pass", ["exitCode"] = 0, ["timedOut"] = false, ["skipped"] = false,
                ["evidenceReasons"] = Array.Empty<object>(), ["trx"] = "c" + i + ".trx", ["sha"] = sha, ["contentDigest"] = roster[i - 1].ContentDigest,
                ["disposition"] = disposition, ["runId"] = "prior-run", ["platform"] = "windows", ["architecture"] = RcReceiptScope.CurrentArchitecture(), ["profile"] = "rc",
            };
            (List<string> Launched, RcIncrementalExecution Exec) Hop(string name, string baseline, string inventorySha, params System.Collections.IDictionary[] inventoryRows)
            {
                var path = Path.Combine(prior, name + "-receipts.json");
                var digest = RcChunkReceipt.Seal(path, inventorySha, 1, inventoryRows);
                var options = new NightlyRunOptions
                {
                    RunId = name, RequestedSelectionMode = "Incremental", SelectionMode = "Full", ExpectedSha = hop.World.Sha, Profile = "rc",
                    IncrementalApprovalDigest = new string('a', 64), IncrementalBaselineSha = baseline, IncrementalInventoryPath = path, IncrementalInventoryDigest = digest,
                    IncrementalOwedDigest = RcComparisonBaseline.OwedDigest(new Dictionary<string, string>()),
                };
                var launched = new List<string>();
                var exec = RcIncrementalCut.Execute(options, roster, Path.Combine(hop.World.CandidateRoot, "attempts", name), c =>
                {
                    launched.Add(c.Id);
                    return new System.Collections.Specialized.OrderedDictionary { ["chunk"] = c.Id, ["result"] = "pass", ["exitCode"] = 0 };
                }, platform: "windows");
                return (launched, exec);
            }
            var cross = Hop("cross", other, other, Clean(1, other), Clean(4, other));
            rows.Check(cross.Exec.Refusal == "" && Sorted(cross.Launched).SequenceEqual(roster.Select(c => c.Id)) && cross.Exec.Carried.Count == 0 && cross.Exec.Reconciled,
                "s6-cross-source-executes", cross.Exec.Refusal + " launched=" + string.Join(",", cross.Launched) + " carried=" + Carried(cross.Exec));
            var origin = Hop("origin", hop.World.Sha, hop.World.Sha, Clean(1, other, "carried"), Clean(4, hop.World.Sha));
            rows.Check(origin.Exec.Refusal == "" && Sorted(origin.Launched).SequenceEqual(["c1", "c2", "c3", "c5"]) && Carried(origin.Exec) == "c4"
                && PlanReasons(hop.World, "origin", "c1").Contains("origin-sha:" + other), "s6-origin-sha-mismatch",
                origin.Exec.Refusal + " launched=" + string.Join(",", origin.Launched) + " carried=" + Carried(origin.Exec));

            // Full: AllGreen is computed from the launched rows, never a constant.
            // Review r2 A-8 (N9): a launch that returns no row is not green either.
            RcIncrementalExecution FullRun(string name, string red, bool noRow = false) => RcIncrementalCut.Execute(new NightlyRunOptions(), roster,
                Path.Combine(hop.World.CandidateRoot, "attempts", name),
                c => noRow && c.Id == red ? null!
                    : new System.Collections.Specialized.OrderedDictionary { ["chunk"] = c.Id, ["result"] = c.Id == red ? "FAIL" : "pass", ["exitCode"] = c.Id == red ? 1 : 0 },
                platform: "windows");
            var fullRed = FullRun("full-red", "c3");
            var fullGreen = FullRun("full-green", "");
            var fullNoRow = FullRun("full-no-row", "c3", noRow: true);
            rows.Check(fullRed.CoverageScope == "Full" && fullRed.Launched.Count == roster.Length && !fullRed.AllGreen && fullGreen.AllGreen
                && fullNoRow.Launched.Count == roster.Length && !fullNoRow.AllGreen, "s6-full-red-is-red",
                "red=" + fullRed.AllGreen + " green=" + fullGreen.AllGreen + " no-row=" + fullNoRow.AllGreen);
        }

        rows.ShouldAllPass(19, "s6-gate-closed-full", "s6-gate-admitted-owed-only", "s6-no-publication", "s6-explicit-full-publishes", "s6-default-full",
            "s6-stale-plan-digest", "s6-hop-mismatch", "s6-age-boundary", "s6-crash-resume", "s6-rollback", "s6-inventory-sha-mismatch", "s6-later-fail-same-sha",
            "s6-foreign-platform-owed", "s6-cross-source-executes", "s6-origin-sha-mismatch", "s6-full-red-is-red", "s6-comparison-owed-absent",
            "s6-comparison-owed-digest", "s6-foreign-profile-owed");
    }

    [Test]
    public void Refuses_a_ref_that_is_not_a_long_lived_rc_ref_before_anything_is_read()
    {
        foreach (var (reference, refusal) in new[]
                 {
                     ("", "rc-ref-required"), ("   ", "rc-ref-required"), ("release/rc-20260923T083000Z", "rc-ref-required"), ("rc/2026-13-01", "ref-date"),
                     ("release/current", "ref-shape"), ("master", "ref-shape"),
                 })
        {
            using var world = ReleaseCutWorld.Fake("cut-ref");
            var result = world.Cut(o =>
            {
                o.Ref = reference;
                o.ScheduleSlot = "u/lndcobra/antiphon_release_candidates";
                o.JobId = "job-1";
            });
            (result.ExitCode, result.Refusal, result.Tested, result.Published).ShouldBe((3, refusal, false, false), reference);
            world.Lines.ShouldBe(["[release-cut] candidate cut refused (" + refusal + ")."], reference);
            world.Remote.Trace.ShouldBeEmpty(reference);
            world.Runs.ShouldBeEmpty(reference);
            Directory.Exists(Path.Combine(world.Release, "candidates")).ShouldBeFalse(reference);
            result.ToJson().ShouldBe("{\"candidateId\":\"\",\"ref\":\"\",\"sha\":\"\",\"scheduleSlot\":\"u/lndcobra/antiphon_release_candidates\",\"windmillJobId\":\"job-1\","
                + "\"tested\":false,\"published\":false,\"tag\":\"\",\"refusal\":\"" + refusal + "\",\"exitCode\":3}");
        }
    }

    [Test]
    public void A_candidate_refusal_ends_the_cut_with_the_candidate_s_answer()
    {
        using var world = ReleaseCutWorld.Fake("cut-missing", remoteHasRef: false);
        var result = world.Cut();
        (result.ExitCode, result.Refusal, result.Ref, result.Sha).ShouldBe((3, "rc-ref-missing", ReleaseCutWorld.Ref, ""));
        world.Lines.ShouldContain("[release-candidate] REFUSED: remote " + ReleaseCutWorld.Ref + " does not exist.");
        world.Lines[^1].ShouldBe("[release-cut] candidate cut refused (rc-ref-missing).");
        world.Runs.ShouldBeEmpty();
        world.HostGit.Calls.ShouldBeEmpty();
    }

    [Test]
    public void A_failure_while_sealing_the_selection_refuses_before_the_run()
    {
        // The pinned object's fetch fails.
        using (var world = ReleaseCutWorld.Fake("cut-fetch"))
        {
            world.HostGit.Reply("fetch", new NightlyGitResult(1, "fatal: fixture"));
            var result = world.Cut(o => o.RunId = "fetch-run");
            (result.ExitCode, result.Refusal, result.Tested).ShouldBe((3, "selection-candidate-fetch", false));
            world.HostGit.Lines.ShouldContain(line => line.Contains("fetch origin " + world.Sha, StringComparison.Ordinal));
            world.Runs.ShouldBeEmpty();
            File.Exists(Path.Combine(world.CandidateRoot, "candidate.json")).ShouldBeTrue("the candidate was journalled first");
            Directory.Exists(Path.Combine(world.CandidateRoot, "state")).ShouldBeTrue();
        }

        // A sealed attempt for the run with another explicit timing root: refused before the fetch.
        using (var world = ReleaseCutWorld.Fake("cut-conflict"))
        {
            var sealedAttempt = Path.Combine(world.CandidateRoot, "attempts", "sealed-run", "selection-attempt.json");
            NightlyFixture.WriteJson(sealedAttempt, new { timingRoot = world.Fixture.PathOf("sealed timing") });
            var result = world.Cut(o =>
            {
                o.RunId = "sealed-run";
                o.TimingRoot = world.Fixture.PathOf("other timing");
            });
            (result.ExitCode, result.Refusal).ShouldBe((3, "selection-attempt-conflict"));
            world.HostGit.Calls.ShouldBeEmpty();
            world.Runs.ShouldBeEmpty();
        }

        // A checkout that cannot seal an attempt (no commit): the writer's refusal, as is.
        using (var world = ReleaseCutWorld.Fake("cut-unsealable"))
        {
            var result = world.Cut();
            result.ExitCode.ShouldBe(3);
            result.Refusal.ShouldStartWith(ReleaseSelectionAttempt.RefusedPrefix);
            world.Runs.ShouldBeEmpty();
            world.Lines.ShouldNotContain(line => line.StartsWith(ReleaseCut.LinePrefix, StringComparison.Ordinal));
        }
    }

    [Test]
    public void What_if_journals_nothing_seals_nothing_and_publishes_nothing()
    {
        using var world = ReleaseCutWorld.Fake("cut-whatif");
        var result = world.Cut(o =>
        {
            o.WhatIf = true;
            o.RunId = "whatif-run";
        });
        (result.ExitCode, result.Refusal, result.Tested, result.Published, result.CandidateId).ShouldBe((0, "", true, false, world.CandidateId));
        File.Exists(Path.Combine(world.CandidateRoot, "candidate.json")).ShouldBeFalse();
        Directory.Exists(Path.Combine(world.CandidateRoot, "attempts")).ShouldBeFalse();
        world.HostGit.Calls.ShouldBeEmpty("no fetch under WhatIf");
        var run = world.Runs.ShouldHaveSingleItem();
        (run.WhatIf, run.Profile, run.Trigger, run.TimingRoot, run.SelectionAttemptPath, run.RunId).ShouldBe((true, "rc", "rc", "", "", "whatif-run"));
        world.Lines.ShouldContain("[release-candidate] WhatIf: would journal " + ReleaseCutWorld.Ref + " at " + world.Sha);
        world.Lines.ShouldNotContain(line => line.StartsWith("[publish-release]", StringComparison.Ordinal));
        result.ToJson().ShouldNotContain("\"selection\"");
    }

    [Test]
    public void A_red_or_throwing_run_publishes_nothing_and_keeps_the_run_s_answer()
    {
        using (var world = ReleaseCutWorld.Fake("cut-red"))
        {
            world.Answer = o => new NightlyRunResult { ExitCode = 4, Refusal = "", RunId = o.RunId };
            var result = world.Cut(o => o.WhatIf = true);
            (result.ExitCode, result.Refusal, result.Tested, result.Published).ShouldBe((4, "", false, false));
            world.Lines[^1].ShouldBe("[release-cut] rc run did not complete green; publishing nothing (exit 4).");
            world.Lines.ShouldNotContain(line => line.StartsWith("[publish-release]", StringComparison.Ordinal));
        }

        using (var world = ReleaseCutWorld.Fake("cut-refused-run"))
        {
            world.Answer = o => new NightlyRunResult { ExitCode = 3, Refusal = "deferred-busy", RunId = o.RunId };
            var result = world.Cut(o => o.WhatIf = true);
            (result.ExitCode, result.Refusal, result.Tested).ShouldBe((3, "deferred-busy", false));
        }

        using (var world = ReleaseCutWorld.Fake("cut-throw"))
        {
            world.Answer = _ => throw new InvalidOperationException("fixture failure");
            var result = world.Cut(o =>
            {
                o.WhatIf = true;
                o.RunId = "throw-run";
            });
            (result.ExitCode, result.Refusal, result.Tested).ShouldBe((1, "nightly-run-failed", false));
            world.Fixture.Error.ToString().Trim().ShouldBe("nightly-run: InvalidOperationException: fixture failure");
            var record = world.Lines.Single(line => line.StartsWith("{\"nativeRunId\"", StringComparison.Ordinal));
            JsonNode.Parse(record)!["refusal"]!.GetValue<string>().ShouldBe("nightly-run-failed");
            world.Lines[^1].ShouldBe("[release-cut] rc run did not complete green; publishing nothing (exit 1).");
        }
    }

    [Test]
    [ParallelLimiter<Antiphon.Tests.TestHelpers.ProcessSpawnLimit>]
    [Timeout(300_000)]
    public void The_rc_run_gets_the_candidate_its_private_roots_and_the_sealed_selection(CancellationToken token)
    {
        using var world = ReleaseCutWorld.Real("cut-run");
        var result = world.Cut(o =>
        {
            o.RunId = "cut-run";
            o.Repository = "fixture/repository";
            o.SkipPublish = true;
            o.CoordinationRoot = world.Fixture.PathOf("coord");
        });
        (result.ExitCode, result.Refusal, result.Tested, result.Published).ShouldBe((0, "", true, false), string.Join("\n", world.Lines));
        var run = world.Runs.ShouldHaveSingleItem();
        var root = world.CandidateRoot;
        var attempt = Path.Combine(root, "attempts", "cut-run", "selection-attempt.json");
        var selection = Path.Combine(root, "attempts", "cut-run", "selection.json");
        (run.Profile, run.Trigger, run.Ref, run.ExpectedSha, run.CandidateId).ShouldBe(("rc", "rc", ReleaseCutWorld.Ref, world.Sha, world.CandidateId));
        (run.CheckoutRoot, run.StateRoot, run.LogRoot, run.ReleaseRoot, run.CoordinationRoot)
            .ShouldBe((world.Checkout, Path.Combine(root, "state"), Path.Combine(root, "logs"), world.Release, world.Fixture.PathOf("coord")));
        (run.RunId, run.Repository, run.RemoteUrl, run.SelectionMode, run.Attempt, run.BaselineSha).ShouldBe(("cut-run", "fixture/repository", world.Origin, "Full", 1, ""));
        (run.SelectionAttemptPath, run.SelectionAttemptDigest, run.SelectionPath, run.SelectionDigest)
            .ShouldBe((attempt, NightlyPaths.FileSha256(attempt), selection, NightlyPaths.FileSha256(selection)));
        run.TimingRoot.ShouldBe(NightlyPaths.TimingRoot("", Path.Combine(root, "logs"), "cut-run"));
        (run.NoSync, run.NoReport, run.WhatIf, run.AcceptCrossCandidateSeed, run.Suites).ShouldBe((false, false, false, false, (string[]?)null));
        // Every value the run gets is a flag of the nightly-run command (the launcher's former hop): it parses back unchanged.
        JsonSerializer.Serialize(NightlyRunOptions.Parse(run.ToArguments())).ShouldBe(JsonSerializer.Serialize(run));

        Directory.Exists(run.StateRoot).ShouldBeTrue();
        var journal = ReleaseGateFixture.ReadObject(Path.Combine(root, "candidate.json"));
        (ReleaseGateFixture.Str(journal["sha"]), ReleaseGateFixture.Str(journal["ref"]), journal["pushed"]!.GetValue<bool>()).ShouldBe((world.Sha, ReleaseCutWorld.Ref, false));
        world.HostGit.Lines.ShouldContain(line => line.Contains("fetch origin " + world.Sha, StringComparison.Ordinal));
        var record = JsonNode.Parse(result.ToJson())!.AsObject();
        record.Select(p => p.Key).ShouldBe(["candidateId", "ref", "sha", "scheduleSlot", "windmillJobId", "tested", "published", "tag", "refusal", "exitCode", "selection"]);
        (ReleaseGateFixture.Str(record["selection"]!["runId"]), ReleaseGateFixture.Str(record["selection"]!["effectiveMode"])).ShouldBe(("cut-run", "Full"));
        world.Lines.ShouldNotContain(line => line.StartsWith("[publish-release]", StringComparison.Ordinal));
    }

    [Test]
    [ParallelLimiter<Antiphon.Tests.TestHelpers.ProcessSpawnLimit>]
    [Timeout(300_000)]
    public void Only_a_green_run_reaches_the_publisher_and_it_is_asked_once_for_the_candidate(CancellationToken token)
    {
        using var world = ReleaseCutWorld.Real("cut-publish");
        world.Answer = o => new NightlyRunResult { ExitCode = 1, Refusal = "", RunId = o.RunId };
        var red = world.Cut(o => o.RunId = "red-run");
        (red.ExitCode, red.Tested, red.Published).ShouldBe((1, false, false));
        world.Lines.ShouldNotContain(line => line.StartsWith("[publish-release]", StringComparison.Ordinal));

        // Green: the publisher (test mode, fake git and GitHub) is handed this release root and candidate. Nothing here
        // wrote complete-green.json, so it refuses before reading any remote.
        world.Answer = o => new NightlyRunResult { ExitCode = 0, RunId = o.RunId };
        world.ClearLines();
        var green = world.Cut(o => o.RunId = "green-run");
        (green.ExitCode, green.Refusal, green.Tested, green.Published, green.Tag).ShouldBe((3, "missing-evidence", true, false, ""));
        world.Lines.Where(line => line.StartsWith("[publish-release]", StringComparison.Ordinal))
            .ShouldBe(["[publish-release] REFUSED: missing " + Path.Combine(world.CandidateRoot, "complete-green.json") + "."]);
        world.PublisherGit.Trace.ShouldBeEmpty();
        world.GitHub.Trace.ShouldBeEmpty();
    }

    [Test]
    public void Options_take_every_flag_and_the_record_keeps_the_script_s_keys()
    {
        string[] every =
        [
            "--release-root", @"C:\r", "--coordination-root", @"C:\c", "--remote-url", "https://example.invalid/r.git", "--repository", "o/r", "--run-id", "run-1",
            "--schedule-slot", "slot", "--job-id", "job", "--ref", "rc/2026-11-30", "--selection-mode", "shadow", "--baseline-sha", new string('b', 40),
            "--baseline-receipt-path", @"C:\b.json", "--qualification-path", @"C:\q.json", "--timing-root", @"C:\t", "--train-manifest-path", @"C:\m.json",
            "--release-intent-path", @"C:\i.json", "--selection-attempt-path", @"C:\a.json", "--selection-attempt-digest", new string('c', 64),
            "--selection-path", @"C:\s.json", "--selection-digest", new string('d', 64), "--attempt", "3", "--skip-publish", "--what-if", "--result-file", @"C:\out.json",
        ];
        var variables = new Dictionary<string, string> { ["ANTIPHON_API"] = "http://127.0.0.1:1", [ReleaseGateRemote.TestModeVariable] = "1" };
        var o = ReleaseCutOptions.Parse(every, @"C:\scripts", name => variables.TryGetValue(name, out var value) ? value : null);
        (o.ReleaseRoot, o.CoordinationRoot, o.RemoteUrl, o.Repository, o.RunId, o.ScheduleSlot, o.JobId, o.Ref)
            .ShouldBe((@"C:\r", @"C:\c", "https://example.invalid/r.git", "o/r", "run-1", "slot", "job", "rc/2026-11-30"));
        (o.SelectionMode, o.BaselineSha, o.BaselineReceiptPath, o.QualificationPath, o.TimingRoot, o.TrainManifestPath, o.ReleaseIntentPath)
            .ShouldBe(("Shadow", new string('b', 40), @"C:\b.json", @"C:\q.json", @"C:\t", @"C:\m.json", @"C:\i.json"));
        (o.SelectionAttemptPath, o.SelectionAttemptDigest, o.SelectionPath, o.SelectionDigest, o.Attempt, o.SkipPublish, o.WhatIf, o.ResultFile)
            .ShouldBe((@"C:\a.json", new string('c', 64), @"C:\s.json", new string('d', 64), 3, true, true, @"C:\out.json"));
        (o.AntiphonApiUrl, o.TestMode, o.ScriptsRoot).ShouldBe(("http://127.0.0.1:1", true, @"C:\scripts"));

        // The defaults are release-cut.ps1's.
        var d = ReleaseCutOptions.Parse([], "", _ => null);
        (d.ReleaseRoot, d.CoordinationRoot, d.RemoteUrl, d.Repository, d.SelectionMode, d.Attempt, d.Ref, d.TestMode)
            .ShouldBe((@"C:\Antiphon\releases", @"C:\Antiphon\verification", "https://github.com/michal-ciechan/Antiphon", "michal-ciechan/Antiphon", "Full", 1, "", false));
        Should.Throw<NightlyUsageException>(() => ReleaseCutOptions.Parse(["--selection-mode", "Incremental"], "", _ => null));
        Should.Throw<NightlyUsageException>(() => ReleaseCutOptions.Parse(["--seams-path", "seams.ps1"], "", _ => null));
        ReleaseGateRemote.TestMode("true").ShouldBeFalse("test mode is exactly '1'");

        var record = new ReleaseCutResult
        {
            CandidateId = "rc-2026-11-30-aaaaaaaaaaaa", Ref = "rc/2026-11-30", Sha = new string('a', 40), ScheduleSlot = "manual", WindmillJobId = "j", Tested = true,
            Published = true, Tag = "v2026.11.30.1-rc.1", ExitCode = 0, Selection = new System.Collections.Specialized.OrderedDictionary { ["runId"] = "r", ["contract"] = new Dictionary<string, object> { ["deep"] = new[] { 1, 2 } } },
        };
        record.ToJson().ShouldBe("{\"candidateId\":\"rc-2026-11-30-aaaaaaaaaaaa\",\"ref\":\"rc/2026-11-30\",\"sha\":\"" + new string('a', 40) + "\",\"scheduleSlot\":\"manual\","
            + "\"windmillJobId\":\"j\",\"tested\":true,\"published\":true,\"tag\":\"v2026.11.30.1-rc.1\",\"refusal\":\"\",\"exitCode\":0,"
            + "\"selection\":{\"runId\":\"r\",\"contract\":{\"deep\":[1,2]}}}");
    }
}

/// <summary>
/// One release root for the release cut: the candidate checkout at &lt;release&gt;\checkout, the candidate's remote, the host
/// (real or fake git for the pinned object's fetch), a recorded stand-in for the rc run and a test-mode publisher over fake
/// git and GitHub (its card readback refuses: no HTTP). Fake: an owned clone without commits and an in-memory remote whose
/// rc/2026-10-27 is ShaB. Real: a git checkout on rc/2026-10-27 holding the committed selection inputs
/// (NightlyRcChain.SelectionInputs), its bare origin and real git for the candidate's reads and the fetch.
/// </summary>
internal sealed class ReleaseCutWorld : IDisposable
{
    public const string Ref = "rc/2026-10-27";

    private readonly List<NightlyRunOptions> _runs = [];

    private ReleaseCutWorld(NightlyFixture fixture, ReleaseGateRemote candidateRemote, FakeCandidateRemote remote, FakeGit hostGit, string sha)
    {
        Fixture = fixture;
        CandidateRemote = candidateRemote;
        Remote = remote;
        HostGit = hostGit;
        Sha = sha;
        PublisherGit = new FakeCandidateRemote(sha);
        PublisherGit.Refs[Ref] = sha;
    }

    public NightlyFixture Fixture { get; }
    public string Release => Fixture.PathOf("release");
    public string Checkout => Path.Combine(Release, "checkout");
    public string Origin => Fixture.PathOf("origin.git");
    public string Sha { get; }
    public string CandidateId => ReleaseGateCredit.Candidate(Ref, Sha).CandidateId;
    public string CandidateRoot => ReleaseGateCredit.CandidateRoot(Release, CandidateId);

    /// <summary>The candidate's remote as the cut uses it.</summary>
    public ReleaseGateRemote CandidateRemote { get; }

    /// <summary>The fake candidate remote (a Real world's is unused).</summary>
    public FakeCandidateRemote Remote { get; }

    /// <summary>The host's git: the fetch of the pinned object (a Real world forwards every unmatched call to real git).</summary>
    public FakeGit HostGit { get; }

    /// <summary>The publisher's git and GitHub seams.</summary>
    public FakeCandidateRemote PublisherGit { get; }
    public FakeReleaseGitHub GitHub { get; } = new();

    /// <summary>The rc run's answer; the default is green.</summary>
    public Func<NightlyRunOptions, NightlyRunResult> Answer { get; set; } = o => new NightlyRunResult { ExitCode = 0, RunId = o.RunId };

    /// <summary>The options of every run, in order.</summary>
    public IReadOnlyList<NightlyRunOptions> Runs => _runs;

    /// <summary>The output lines so far.</summary>
    public string[] Lines => Fixture.Out.ToString().Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0).ToArray();

    public void ClearLines() => Fixture.Out.GetStringBuilder().Clear();

    public static ReleaseCutWorld Fake(string prefix, bool remoteHasRef = true)
    {
        var fixture = NightlyFixture.Create(prefix);
        ReleaseCandidateCases.OwnedClone(fixture, Path.Combine("release", "checkout"));
        var sha = new string('b', 40);
        var remote = new FakeCandidateRemote(sha);
        if (remoteHasRef) remote.Refs[Ref] = sha;
        return new ReleaseCutWorld(fixture, new ReleaseGateRemote(remote.Run, null, null), remote, new FakeGit(""), sha);
    }

    public static ReleaseCutWorld Real(string prefix, bool withNightlyLauncher = false)
    {
        var fixture = NightlyFixture.Create(prefix);
        try
        {
            var checkout = fixture.PathOf("release", "checkout");
            Directory.CreateDirectory(checkout);
            NightlyRcChain.Git(checkout, "init", "-q", "-b", Ref);
            NightlyRcChain.Git(checkout, "config", "core.autocrlf", "false");
            foreach (var file in NightlyRcChain.SelectionInputs)
            {
                var target = Path.Combine(checkout, file.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(Path.Combine(NightlyPlanCases.Repo, file.Replace('/', Path.DirectorySeparatorChar)), target);
            }
            if (withNightlyLauncher)
            {
                NightlyTestsWorld.CopyPolicyProjects(checkout);
                File.WriteAllText(Path.Combine(checkout, "scripts", "nightly-run.ps1"), "# fixture nightly-run process boundary");
            }
            NightlyRcChain.Git(checkout, "add", "--all");
            NightlyRcChain.Git(checkout, "-c", "user.name=release-cut", "-c", "user.email=release-cut@example.invalid", "-c", "commit.gpgsign=false",
                "-c", "core.hooksPath=.git/no-hooks", "commit", "-qm", "fixture");
            var sha = NightlyRcChain.Git(checkout, "rev-parse", "HEAD").Trim();
            File.WriteAllText(Path.Combine(checkout, ReleaseCandidate.OwnedMarker), "{\"kind\":\"release-clone\"}");
            if (withNightlyLauncher)
                NightlyFixture.WriteJson(Path.Combine(checkout, ".antiphon-nightly-owned"), new { origin = fixture.PathOf("origin.git") });
            NightlyRcChain.Git(fixture.Root, "clone", "-q", "--bare", checkout, fixture.PathOf("origin.git"));
            NightlyRcChain.Git(checkout, "remote", "add", "origin", fixture.PathOf("origin.git"));
            var hostGit = new FakeGit("");
            hostGit.Reply(_ => true, call => NightlyNativeGit.Run(call.WorkingDirectory, call.Arguments));
            return new ReleaseCutWorld(fixture, ReleaseGateRemote.Production(), new FakeCandidateRemote(sha), hostGit, sha);
        }
        catch
        {
            fixture.Dispose();
            throw;
        }
    }

    /// <summary>The cut options for this world (the release root, its origin, the ref, test mode); edit changes them last.</summary>
    public ReleaseCutOptions Options(Action<ReleaseCutOptions>? edit = null)
    {
        var options = new ReleaseCutOptions
        {
            ReleaseRoot = Release, RemoteUrl = Origin, Ref = Ref, CoordinationRoot = Fixture.PathOf("coord"), ScriptsRoot = ReleaseGateFixture.ScriptsRoot,
            AntiphonApiUrl = "http://127.0.0.1:9/never", TestMode = true,
        };
        edit?.Invoke(options);
        return options;
    }

    /// <summary>ReleaseCut.Run of this world's options on its host.</summary>
    public ReleaseCutResult Cut(Action<ReleaseCutOptions>? edit = null) => ReleaseCut.Run(Options(edit), Host());

    public ReleaseCutHost Host() => new()
    {
        Nightly = Fixture.Host(git: HostGit),
        Files = new System.IO.Abstractions.FileSystem(),
        CandidateRemote = CandidateRemote,
        Run = options =>
        {
            _runs.Add(options);
            return Answer(options);
        },
        Publisher = new ReleaseGateHost
        {
            Out = Fixture.Out, Clock = TimeProvider.System,
            Seams = new ReleaseGateSeams { Git = PublisherGit.Run, GitHub = GitHub.Run },
            CardReader = (_, _) => throw new InvalidOperationException("no release card readback in a release-cut test"),
        },
    };

    public void Dispose() => Fixture.Dispose();
}
