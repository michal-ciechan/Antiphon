using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Antiphon.Nightly;
using Antiphon.Tests.Application;
using Antiphon.TestSupport;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

[Timeout(600_000)]
[Category("Unit")]
public sealed class VolumeRetirementWrapperTests
{
    private const string Operation = "c100800000000000000000000000000000001";
    private const string Sibling = "c100800000000000000000000000000000002";
    private static readonly string Sha = new('a', 40);

    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public async Task Retire_temp_uses_current_proof_protocol()
    {
        using var dry = new C1008WrapperFixture();
        var preview = await dry.Run("retire-temp", "--dry-run");
        preview.Exit.ShouldBe(0, "c1609-w1: dry-run is the preview entry; " + preview.Output);
        preview.Output.ShouldNotContain("TempStackMissing");
        var previewCalls = preview.Trace.Where(row => row["kind"]?.GetValue<string>() == "case" &&
            row["name"]?.GetValue<string>() == "retire-temp-runner").ToArray();
        previewCalls.Length.ShouldBe(1, "W1 DryRun reaches the full content Preview");
        previewCalls[0]["recycle"]!["dryRun"]!.GetValue<bool>().ShouldBeTrue("W1 DryRun cannot apply");

        using var apply = new C1008WrapperFixture();
        var retired = await apply.Run("retire-temp");
        retired.Exit.ShouldBe(0, "c1609-w1: retire-temp apply; " + retired.Output);
        var volumeCalls = retired.Trace.Where(row => row["kind"]?.GetValue<string>() == "case" &&
            row["name"]?.GetValue<string>() == "retire-temp-runner").ToArray();
        volumeCalls.Select(row => row["recycle"]!["dryRun"]!.GetValue<bool>()).ShouldBe(new[] { true, false });
        volumeCalls[0]["recycle"]!["operationId"]!.GetValue<string>()
            .ShouldBe(volumeCalls[1]["recycle"]!["operationId"]!.GetValue<string>());
        using var denied = new C1008WrapperFixture();
        denied.State["failRetirementPreview"] = true;
        var stopped = await denied.Run("retire-temp");
        stopped.Exit.ShouldBe(2, stopped.Output);
        var deniedCalls = stopped.Trace.Where(row => row["kind"]?.GetValue<string>() == "case" &&
            row["name"]?.GetValue<string>() == "retire-temp-runner").ToArray();
        deniedCalls.Length.ShouldBe(1, "W1 a refused Preview cannot enter Apply");
        deniedCalls[0]["recycle"]!["dryRun"]!.GetValue<bool>().ShouldBeTrue();

        using var prior = new C1008WrapperFixture();
        var priorArgs = new[] { "--retirement-proof", Operation };
        var consumed = await prior.Run("retire-temp", priorArgs);
        consumed.Exit.ShouldBe(0, consumed.Output);
        var priorCalls = consumed.Trace.Where(row => row["kind"]?.GetValue<string>() == "case" &&
            row["name"]?.GetValue<string>() == "retire-temp-runner").ToArray();
        priorCalls.Length.ShouldBe(1, "W1 an explicit prior Ready proof cannot be silently replaced by a new Preview");
        priorCalls[0]["recycle"]!["dryRun"]!.GetValue<bool>().ShouldBeFalse();
        priorCalls[0]["recycle"]!["operationId"]!.GetValue<string>().ShouldBe(Operation);
        priorCalls[0]["recycle"]!["expectedProofDigest"]!.GetValue<string>().ShouldBe(new string('a',64));
        using var missingPrior = new C1008WrapperFixture();
        missingPrior.State["retirementProofError"] = true;
        var refusedPrior = await missingPrior.Run("retire-temp", "--retirement-proof", Operation);
        refusedPrior.Exit.ShouldBe(2,refusedPrior.Output);
        refusedPrior.Trace.Any(row=>row["kind"]?.GetValue<string>()=="case").ShouldBeFalse(
            "W1 unknown prior proof cannot start cleanup, Preview or Apply");

        using var deploy = new C1008WrapperFixture();
        deploy.State["allowClear"] = true;
        var recreated = await deploy.Run("deploy-temp");
        recreated.Exit.ShouldBe(0, "c1609-w1: empty journal admits deploy; " + recreated.Output);
        recreated.Trace.Any(row => row["kind"]?.GetValue<string>() == "census" &&
            row["name"]?.GetValue<string>() == "temp-retirement-clearable").ShouldBeTrue("c1609-w1: recreation reads the journal");
        recreated.Trace.Any(row => row["kind"]?.GetValue<string>() == "case" &&
            row["name"]?.GetValue<string>() == "runner-cache-seed").ShouldBeTrue();

        using var current = new C1008WrapperFixture();
        var temp = current.State["statuses"]!["server2-temp"]!.AsObject();
        temp["available"] = true;
        temp["dispatchEligible"] = true;
        temp["acceptingNewWork"] = true;
        temp["draining"] = false;
        temp["retireWhenIdle"] = false;
        temp["retiredAt"] = null;
        temp["buildVersion"] = Sha;
        current.State["tempRetirementCensus"] = JsonNode.Parse(
            """{"volumes":["antiphon-runner-temp_work"],"journals":[]}""");
        var reused = await current.Run("deploy-temp");
        reused.Exit.ShouldBe(0, "c1609-w1: an already-current temp does not recreate; " + reused.Output);
        reused.Output.ShouldNotContain("TempVolumesRetained");
        reused.Trace.Any(row => row["kind"]?.GetValue<string>() == "census" &&
            row["name"]?.GetValue<string>() == "temp-retirement-clearable").ShouldBeFalse(
            "c1609-w1: reuse does not clear or census the retirement");

    }

    [Test]
    [NativeLinuxOnly("CARD-1901: the real wrapper renewal witness requires native Linux Bash and POSIX mounts.")]
    [ParallelLimiter<ProcessSpawnLimit>]
    public async Task Retire_temp_real_wrapper_renews_each_effect()
    {
        NativeLinuxGate.Require("CARD-1901: the real wrapper renewal witness requires native Linux Bash and POSIX mounts.");
        using var native = new C1008HostFixture(main: false);
        await VolumeRetirementFixture.ReadyPreview(native);
        var psi = new ProcessStartInfo("node")
        {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
            WorkingDirectory = DelegateScriptRunner.RepoRoot
        };
        psi.ArgumentList.Add(Path.Combine(DelegateScriptRunner.RepoRoot, "scripts/fixtures/c1609-wrapper-v2-cases.mjs"));
        psi.ArgumentList.Add(native.Root);
        var real = await Wait(psi, TimeSpan.FromSeconds(360));
        real.Exit.ShouldBe(0, "W1 real wrapper, private A->B proof, long-scan renewal and locked inventory: " + real.Output);
        real.Output.ShouldContain("C1609_WRAPPER passed");
    }

    // Unit 67: retire-runner-volumes is the command (RetireRunnerVolumes); these ran scripts/retire-runner-volumes.ps1 in pwsh and now
    // run it in process on RetireRunnerVolumesWorld. Get-CloseAuthority's admitted grant is read where it lands: the actor and
    // authority reference of the Close packet's c1609_close_host line.
    [Test]
    public async Task Manual_parameters_require_operator_attestation()
    {
        using var world = new RetireRunnerVolumesWorld();
        var root = world.Root;
        var reason = WriteReason(root, "observed absent after content recovery");
        world.WriteToken("operator-secret-token");
        var valid = new[] { "--close", "--operation-id", Operation, "--expected-journal-digest", new string('a', 64), "--reason-file", reason };
        foreach (var (args, expected) in new[]
        {
            (valid.Concat(new[] { "--apply" }).ToArray(), "ModeExclusive"),
            (valid.Concat(new[] { "--force" }).ToArray(), "ForceRefused"),
            (new[] { "--close", "--operation-id", Operation, "--reason-file", reason }, "Digest"),
            (new[] { "--close", "--operation-id", "foreign", "--expected-journal-digest", new string('a',64), "--reason-file",reason }, "OperationId"),
            (new[] { "--preview" }, "ManualRequired")
        })
        {
            var refused = Close(world, args);
            refused.Exit.ShouldBe(2, refused.Output);
            refused.Output.ShouldContain(expected);
        }
        world.Variables["ANTIPHON_OPERATOR_TOKEN_FILE"] = Path.Combine(root, "missing-operator-token");
        world.Variables["ANTIPHON_TASK_TOKEN"] = "task-secret-token";
        var taskOnly = Close(world, valid);
        taskOnly.Exit.ShouldBe(2, taskOnly.Output);
        taskOnly.Output.ShouldContain("Authority");
        world.Variables["ANTIPHON_OPERATOR_TOKEN_FILE"] = Path.Combine(root, "operator-token");
        var authority = Path.Combine(root, "authority.json");
        await File.WriteAllTextAsync(authority, "delegated for CARD-1609");
        var malformed = Close(world, valid.Concat(new[] { "--authority-file", authority }).ToArray());
        malformed.Exit.ShouldBe(2, malformed.Output);
        malformed.Output.ShouldContain("Authority");
        world.Calls.ShouldBeEmpty("no refusal reaches server2");
        await File.WriteAllTextAsync(authority,
            "{\"schema\":1,\"actor\":\"commissioned-delegate\",\"authorityRef\":\"fixture-task-J8\",\"operationId\":\"" + Operation + "\"}");
        world.Rules.Add(new JsonObject { ["name"] = "ssh", ["stdout"] = new JsonArray("TempRetirementClosed operation=" + Operation + " idempotent=false") });
        // Portable authority/transport logic remains here. Machine Close
        // refusals, append-only history, delegation and the real lock/census
        // now execute through the public command in Linux J8.
        var admitted = Close(world, valid.Concat(new[] { "--authority-file", authority }).ToArray());
        admitted.Exit.ShouldBe(0, admitted.Output);
        world.Inputs().Single().ShouldContain("c1609_close_host '" + new string('a', 64) + "' 'commissioned-delegate' 'fixture-task-J8' ");
        (admitted.Output + string.Join("\n", world.Inputs())).ShouldNotContain("operator-secret-token");
        (admitted.Output + string.Join("\n", world.Inputs())).ShouldNotContain("task-secret-token");
        world.Variables["C1609_RETIREMENT_ROOT"] = root;
        var noLocalOverride = Close(world, ["--close"]);
        noLocalOverride.Exit.ShouldBe(2, noLocalOverride.Output);
        noLocalOverride.Output.ShouldContain("LocalJournalOverrideRefused");
    }

    // Unit 65: deploy-temp reads the temp container census, then the retirement census, before the retired placeholder's clear (the
    // script's source order, now the command's trace on a retired start).
    private static async Task AssertCensusBeforeClear()
    {
        using var f = new C1008WrapperFixture();
        f.State["allowClear"] = true;
        var run = await f.Run("deploy-temp");
        run.Exit.ShouldBe(0, run.Output);
        int At(Func<System.Text.Json.Nodes.JsonObject, bool> match) => Array.FindIndex(run.Trace, row => match(row));
        var absent = At(row => row["kind"]?.GetValue<string>() == "census" && row["name"]?.GetValue<string>() == "temp-project-absent");
        var census = At(row => row["kind"]?.GetValue<string>() == "census" && row["name"]?.GetValue<string>() == "temp-retirement-clearable");
        var clear = At(row => row["method"]?.GetValue<string>() == "POST" && row["suffix"]?.GetValue<string>() == "/drain/clear");
        absent.ShouldBeGreaterThanOrEqualTo(0);
        census.ShouldBeGreaterThan(absent);
        clear.ShouldBeGreaterThan(census);
    }

    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public async Task Bridge_validates_and_preserves_receipt_fields()
    {
        // Unit 67: the Close packet is RetireRunnerVolumes.CloseScript (scripts/retire-runner-volumes.ps1's New-C1609CloseScript): the
        // wire it builds is LF and ASCII, as the script's here-string was.
        var source = await File.ReadAllTextAsync(Path.Combine(DelegateScriptRunner.RepoRoot, "scripts/lib/Antiphon.Nightly/RetireRunnerVolumes.cs"));
        Encoding.UTF8.GetBytes(source).Any(b => b > 127).ShouldBeFalse("c1609-w3: close source is ASCII");
        await AssertCensusBeforeClear();
        using var world = new RetireRunnerVolumesWorld();
        var reason = WriteReason(world.Root, "line one\npath C:\\temp\\note \"quoted\"");
        var text = await File.ReadAllTextAsync(reason);
        var packet = RetireRunnerVolumes.Packet(await File.ReadAllTextAsync(Path.Combine(DelegateScriptRunner.RepoRoot, "scripts/c1008-retirement.sh")),
            await File.ReadAllTextAsync(Path.Combine(DelegateScriptRunner.RepoRoot, "scripts/c590-remote.sh")));
        packet.ShouldNotBeNull("c1609-w3: the four tooling functions are found");
        var wire = RetireRunnerVolumes.CloseScript(Operation, new string('a', 64), text.Trim(), "operator", "operator-token", packet);
        wire.Contains('\r').ShouldBeFalse("c1609-w3: close script is LF");
        Encoding.UTF8.GetBytes(wire).Any(b => b > 127).ShouldBeFalse("c1609-w3: close script is ASCII");
        wire.ShouldContain(Convert.ToBase64String(Encoding.UTF8.GetBytes(text.Trim())), customMessage: "literal reason lost");
        wire.ShouldContain("C1609_CLOSE_TOOLING_DIGEST=", customMessage: "tooling provenance missing");
        wire.ShouldContain("c1008_lock", customMessage: "same-lock call missing");
        var huge = WriteReason(world.Root, new string('x',4097));
        var refused = Close(world, ["--close", "--operation-id", Operation, "--expected-journal-digest", new string('a',64), "--reason-file", huge]);
        refused.Exit.ShouldBe(2, refused.Output);
        refused.Output.ShouldContain("ReasonFile");
    }

    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public async Task Retire_temp_ignores_missing_stack_and_historical_compose()
    {
        var remote = await File.ReadAllTextAsync(Path.Combine(DelegateScriptRunner.RepoRoot, "scripts/c590-remote.sh"));
        remote.ShouldContain("C994_IMAGE_HISTORICAL reason=observed-container-image");
        remote.Contains("[ \"$C994_IMAGE\" = \"$image\" ] || c994_refuse TempContainerReceiptUnavailable", StringComparison.Ordinal)
            .ShouldBeFalse("c1609-w4: container cleanup no longer refuses on historical image equality");

        using var preview = new C1008WrapperFixture();
        var dry = await preview.Run("retire-temp", "--dry-run");
        dry.Exit.ShouldBe(0, "c1609-w4: retire-temp does not require the old stack; " + dry.Output);
        dry.Output.ShouldNotContain("TempStackMissing");
        dry.Output.ShouldNotContain("C994_IMAGE_UNPINNED");
    }

    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public async Task Main_recycle_and_existing_deploy_contracts_are_preserved()
    {
        var remote = await File.ReadAllTextAsync(Path.Combine(DelegateScriptRunner.RepoRoot, "scripts/c590-remote.sh"));
        remote.ShouldContain("c1008_refuse RecycleGenerationMismatch");
        using (var world = new Server2RollingWorld())
        {
            var unbound = world.Run("--rolling", "--phase", "redeploy-old");
            unbound.Exit.ShouldNotBe(0, unbound.Output);
            unbound.Output.ShouldContain("Sha");
        }

        using var same = new C1008WrapperFixture();
        same.State["statuses"]!["server2"] = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(
            DelegateScriptRunner.RepoRoot, "scripts/fixtures/c1008-recycle-cases.json")))!["mainAccepting"]!.DeepClone();
        var kept = await same.Run("redeploy-old");
        kept.Exit.ShouldBe(0, "c1609-w5: matching main SHA does not recycle; " + kept.Output);
        kept.Trace.Any(row => row["kind"]?.GetValue<string>() == "case" &&
            row["name"]?.GetValue<string>() == "deploy-parent").ShouldBeFalse();

        using var drifted = new C1008WrapperFixture();
        drifted.MainDrained();
        drifted.State["allowClear"] = true;
        var entered = await drifted.Run("redeploy-old");
        entered.Trace.Any(row => row["kind"]?.GetValue<string>() == "case" &&
            row["name"]?.GetValue<string>() == "deploy-parent").ShouldBeTrue(
            "c1609-w5: a different main SHA still enters recycle; " + entered.Output);

        if (!OperatingSystem.IsLinux()) return;
        var generation = Extract(remote, "c1008_previous_generation");
        var imageA = "sha256:" + new string('a', 64);
        var imageB = "sha256:" + new string('b', 64);
        var shell = generation + "\n" + """
            c1008_refuse() { printf 'refused=%s\n' "$1"; exit 2; }
            write_result() { printf 'result=%s\n' "$2"; exit "$3"; }
            HOST_PROJECT=antiphon-runner
            TEMP_PROJECT=antiphon-runner-temp
            root="$(mktemp -d)"
            trap 'rm -rf "$root"' EXIT
            printf 'SOURCE_REVISION=%s\n' "$SHA" > "$root/main.env"
            printf 'SOURCE_REVISION=%s\n' "$SHA" > "$root/temp.env"
            SERVER2_ENV="$root/main.env"
            SERVER2_TEMP_ENV="$root/temp.env"
            C1008_STATUS="$(printf '{"buildVersion":"%s"}' "$SHA")"
            service="${SERVICE:-session-runner}"
            owned="$(printf '[{"Config":{"Labels":{"com.docker.compose.service":"%s"}},"Image":"%s"}]' "$service" "$IMAGE")"
            docker() {
                if [ "$1" = image ] && [ "$2" = inspect ]; then printf '%s\n' "$DOCKER_IMAGE"; return 0; fi
                return 1
            }
            set +e
            # c1008_refuse exits the shell. Observe that exit from a subshell so the
            # refusal text survives; the mismatch and image_count=0 checks are unchanged.
            (
                c1008_previous_generation "$PROJECT" "$owned" > "$root/out" 2> "$root/err"
            )
            code=$?
            printf 'exit=%s\n' "$code"
            cat "$root/out" "$root/err"
            """;
        var mainMismatch = await Bash(shell, ("SHA", Sha), ("IMAGE", imageA), ("DOCKER_IMAGE", imageB), ("PROJECT", "antiphon-runner"));
        mainMismatch.ShouldContain("exit=2");
        mainMismatch.ShouldContain("refused=RecycleGenerationMismatch");
        var mainMatch = await Bash(shell, ("SHA", Sha), ("IMAGE", imageA), ("DOCKER_IMAGE", imageA), ("PROJECT", "antiphon-runner"));
        mainMatch.ShouldContain("exit=0");
        mainMatch.ShouldContain("statusBuildVersion");
        var tempMismatch = await Bash(shell, ("SHA", Sha), ("IMAGE", imageA), ("DOCKER_IMAGE", imageB), ("PROJECT", "antiphon-runner-temp"));
        tempMismatch.ShouldContain("exit=0");
        tempMismatch.ShouldNotContain("refused=");
        var tempNoRunner = await Bash(shell, ("SHA", Sha), ("IMAGE", imageA), ("DOCKER_IMAGE", imageB),
            ("PROJECT", "antiphon-runner-temp"), ("SERVICE", "state-init"));
        tempNoRunner.ShouldContain("exit=0");
        tempNoRunner.ShouldContain("statusBuildVersion");
        tempNoRunner.ShouldNotContain("refused=");

        var emptyMain = await Bash(Extract(remote, "c1008_owned_mounts") + "\n" + """
            require_lane() { :; }
            sudo() { [ "$1" = -n ] && shift; command "$@"; }
            c1008_refuse() { printf 'refused=%s\n' "$1"; exit 2; }
            C1008_PROJECT=antiphon-runner
            TEMP_PROJECT=antiphon-runner-temp
            c1008_owned_mounts '[]' '{"c994Topology":{"services":{"session-runner":[{"kind":"bind","source":"/dev/null","file":true}]}}}' '{}'
            printf 'main-empty-admitted\n'
            """);
        emptyMain.ShouldContain("refused=RecycleContainerStateUnknown",
            customMessage: "main retains configured bind metadata validation even when no container remains");
        emptyMain.ShouldNotContain("main-empty-admitted");
    }

    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public async Task Retained_volumes_block_deploy_and_completion()
    {
        await AssertCensusBeforeClear();
        var remote = await File.ReadAllTextAsync(Path.Combine(DelegateScriptRunner.RepoRoot, "scripts/c590-remote.sh"));
        var body = Extract(remote, "case_deploy_temp_runner");
        var guard = body.IndexOf("c849_no_temp_containers", StringComparison.Ordinal);
        var recheck = body.IndexOf("c1609_temp_retirement_clearable", StringComparison.Ordinal);
        var checkout = body.IndexOf("ensure_checkout", StringComparison.Ordinal);
        recheck.ShouldBeGreaterThan(guard);
        checkout.ShouldBeGreaterThan(recheck);

        await AssertDeploy("retained-work", """{"volumes":["antiphon-runner-temp_work"],"journals":[]}""", 2, "TempVolumesRetained");
        await AssertDeploy("retained-all", """{"volumes":["antiphon-runner-temp_work","antiphon-runner-temp_runner-tmp","antiphon-runner-temp_dind-data","antiphon-runner-temp_runner-state"],"journals":[]}""", 2, "TempVolumesRetained");
        await AssertDeploy("inert-refused", Census("Refused", 0, 0, false), 0, null);
        await AssertDeploy("inert-ready", Census("Ready", 0, 0, false), 0, null);
        await AssertDeploy("ready-effect", Census("Ready", 1, 0, false), 2, "TempRetirementUnresolved");
        await AssertDeploy("pending-intent", Census("Pending", 0, 1, false), 2, "TempRetirementUnresolved");
        await AssertDeploy("applying", Census("Applying", 1, 1, false), 2, "TempRetirementUnresolved");
        await AssertDeploy("applying-closed", Census("Applying", 1, 1, true), 0, null);
        await AssertDeploy("partial", Census("PartialRefused", 1, 0, false), 2, "TempRetirementUnresolved");
        await AssertDeploy("completed", Census("Completed", 1, 1, false), 0, null);
        await AssertDeploy("completed-present", """{"volumes":["antiphon-runner-temp_runner-state"],"journals":[{"schema":2,"state":"Completed","owner":null,"effects":1,"intents":1,"closed":false}]}""", 2, "TempVolumesRetained");
        await AssertDeploy("closed", Census("Closed", 0, 0, true), 0, null);
        await AssertDeploy("schema", """{"volumes":[],"journals":[{"schema":1,"state":"Ready","owner":null,"effects":0,"intents":0,"closed":false}]}""", 2, "TempRetirementJournalUnknown");
        await AssertDeploy("owner", """{"volumes":[],"journals":[{"schema":2,"state":"Ready","owner":"helper-1","effects":0,"intents":0,"closed":false}]}""", 2, "TempRetirementOwnerActive");
        await AssertDeploy("unknown", Census("Weird", 0, 0, false), 2, "TempRetirementJournalUnknown");
        using (var broken = new C1008WrapperFixture())
        {
            broken.State["tempRetirementCensusError"] = true;
            var run = await broken.Run("deploy-temp");
            run.Exit.ShouldBe(2, run.Output);
            run.Output.ShouldContain("TempRetirementJournalUnknown");
            run.Trace.Any(row => row["method"]?.GetValue<string>() == "POST").ShouldBeFalse();
        }

        using (var late = new C1008WrapperFixture())
        {
            late.State["allowClear"] = true;
            late.State["tempRetirementCensusAfterRead"] = JsonNode.Parse(
                """{"volumes":["antiphon-runner-temp_work"],"journals":[]}""");
            var changed = await late.Run("deploy-temp");
            changed.Exit.ShouldBe(2, "W6 a supported writer between preflight and POST must be observed under the POST lock; " + changed.Output);
            changed.Output.ShouldContain("TempVolumesRetained");
            changed.Trace.Any(row => row["method"]?.GetValue<string>() == "POST").ShouldBeFalse(
                "W6 the late retained volume cannot clear the retirement epoch");
            changed.Trace.Any(row => row["kind"]?.GetValue<string>() == "case" &&
                row["name"]?.GetValue<string>() is "runner-cache-seed" or "deploy-temp-runner").ShouldBeFalse();
        }

        if (!OperatingSystem.IsLinux()) return;
        var function = Extract(remote, "c1609_temp_retirement_clearable");
        var probe = function + "\n" + """
            root="$(mktemp -d)"
            trap 'rm -rf "$root"' EXIT
            umask 077
            SERVER2_ROOT="$root/server"
            source "$RETIREMENT"
            c1609_load_retirement() { :; }
            c1008_volume() { if [ "$PRESENT" = unknown ]; then return 1; elif [ "$PRESENT" = 1 ]; then printf '{}\n'; else printf 'null\n'; fi; }
            docker() { [ "$1" = info ] && printf 'fixture-daemon\n'; }
            C1008_OPERATION=c100800000000000000000000000000000001
            op="$SERVER2_ROOT/retirement/$C1008_OPERATION"
            if [ -n "$STATE" ]; then
                mkdir -p "$op"
                jq -cnS --arg op "$C1008_OPERATION" '{schema:1,operationId:$op,daemon:"fixture-daemon",nonce:("a"*32),owner:{pid:99999999,startTicks:"0"},active:false,complete:true,readyPublished:true,children:[]}' > "$op/lifecycle.json"
                jq -cnS --arg op "$C1008_OPERATION" --arg state "$STATE" --argjson effects "$EFFECTS" --argjson intents "$INTENTS" '
                  ["antiphon-runner-temp_work","antiphon-runner-temp_runner-tmp","antiphon-runner-temp_dind-data","antiphon-runner-temp_runner-state"] as $targets |
                  {schema:2,policyVersion:1,operationId:$op,project:"antiphon-runner-temp",mode:"scripted",state:$state,consumed:($state!="Ready"),owner:null,
                   daemon:"fixture-daemon",image:("sha256:"+("a"*64)),inventoryDigest:("b"*64),phaseNonce:("a"*32),readyAt:"2026-10-31T00:00:00Z",retiredAt:"2026-10-30T00:00:00Z",
                   targets:$targets,fingerprints:($targets|map({key:.,value:null})|from_entries),
                   intents:($targets[0:$intents]|map({name:.,intent:"remove",result:"removed"})),
                   effects:($targets[0:$effects]|map({name:.,result:(if $intents>0 then "removed" else "alreadyAbsent" end)}))}' > "$op/receipt.json"
                if [ "$CLOSED" = 1 ]; then
                    prior="$(sha256sum "$op/receipt.json" | cut -d' ' -f1)"
                    phase="$(sha256sum "$op/lifecycle.json" | cut -d' ' -f1)"
                    jq -cnS --arg prior "$prior" --arg phase "$phase" --slurpfile r "$op/receipt.json" '
                      $r[0] as $r|{schema:2,state:"Closed",operationId:$r.operationId,priorDigest:$prior,actor:"operator",authority:"fixture",reason:"observed absent",observedAt:"2026-10-31T00:00:00Z",
                      daemon:$r.daemon,targets:$r.targets,volumesAbsent:$r.targets,lifecycleDigest:$phase,censusDigest:("c"*64),toolingDigest:("d"*64),
                      reconciliations:($r.targets|map(. as $n|{name:.,result:(if any($r.intents[]?;.name==$n) then "removed-observed" else "unattributed-absence" end)}))}' > "$op/closure.body"
                    digest="$(sha256sum "$op/closure.body" | cut -d' ' -f1)"
                    jq -cS --arg digest "$digest" '.+{digest:$digest}' "$op/closure.body" > "$op/closure.json"
                fi
            fi
            c1609_clear_reason=
            c1609_temp_retirement_clearable
            printf 'exit=%s reason=%s\n' "$?" "${c1609_clear_reason:-ok}"
            """;
        async Task Shell(string present, string state, int effects, int intents, string closed, string expect)
        {
            var output = await Bash(probe,
                ("PRESENT", present), ("STATE", state), ("EFFECTS", effects.ToString()), ("INTENTS", intents.ToString()),
                ("RETIREMENT", Path.Combine(DelegateScriptRunner.RepoRoot, "scripts/c1008-retirement.sh")), ("CLOSED", closed));
            output.ShouldContain(expect, customMessage: output);
        }
        await Shell("1", "", 0, 0, "0", "exit=1 reason=TempVolumesRetained");
        await Shell("0", "Refused", 0, 0, "0", "exit=0 reason=ok");
        await Shell("0", "Ready", 1, 0, "0", "exit=1 reason=TempRetirementUnresolved");
        await Shell("0", "Applying", 1, 1, "0", "exit=1 reason=TempRetirementUnresolved");
        await Shell("0", "Applying", 1, 1, "1", "exit=0 reason=ok");
        await Shell("0", "Completed", 4, 4, "0", "exit=0 reason=ok");
        await Shell("1", "Completed", 4, 4, "0", "exit=1 reason=TempVolumesRetained");
        await Shell("0", "Weird", 0, 0, "0", "exit=1 reason=TempRetirementJournalUnknown");
        await Shell("unknown", "Refused", 0, 0, "0", "exit=1 reason=TempRetirementJournalUnknown");
    }

    private static string Census(string state, int effects, int intents, bool closed) =>
        "{\"volumes\":[],\"journals\":[{\"schema\":2,\"state\":\"" + state +
        "\",\"owner\":null,\"effects\":" + effects + ",\"intents\":" + intents + ",\"closed\":" +
        (closed ? "true" : "false") + "}]}";

    private static async Task AssertDeploy(string label, string census, int exit, string? reason)
    {
        using var fixture = new C1008WrapperFixture();
        fixture.State["allowClear"] = true;
        fixture.State["tempRetirementCensus"] = JsonNode.Parse(census);
        var before = fixture.State["statuses"]!["server2-temp"]!["retiredAt"]!.GetValue<string>();
        var run = await fixture.Run("deploy-temp");
        run.Exit.ShouldBe(exit, label + ": " + run.Output);
        if (reason is null)
        {
            run.Trace.Any(row => row["kind"]?.GetValue<string>() == "case" &&
                row["name"]?.GetValue<string>() == "runner-cache-seed").ShouldBeTrue(label);
            return;
        }
        run.Output.ShouldContain(reason);
        run.Trace.Any(row => row["method"]?.GetValue<string>() == "POST").ShouldBeFalse(label);
        run.Trace.Any(row => row["kind"]?.GetValue<string>() == "case" &&
            (row["name"]?.GetValue<string>() == "runner-cache-seed" ||
             row["name"]?.GetValue<string>() == "deploy-temp-runner")).ShouldBeFalse(label);
        fixture.State["statuses"]!["server2-temp"]!["retiredAt"]!
            .GetValue<string>().ShouldBe(before, label);
    }

    private static byte[] Receipt(string state, string? owner, int effects)
    {
        var effect = effects == 0 ? "[]" : "[{\"name\":\"antiphon-runner-temp_work\",\"result\":\"intent\"}]";
        var ownerJson = owner is null ? "null" : "\"" + owner + "\"";
        var json = "{\"schema\":2,\"state\":\"" + state + "\",\"operationId\":\"" + Operation +
            "\",\"owner\":" + ownerJson + ",\"consumed\":false,\"effects\":" + effect + ",\"intents\":" + effect +
            ",\"targets\":[\"antiphon-runner-temp_work\"]}";
        return Encoding.UTF8.GetBytes(json);
    }

    private static string WriteReceipt(string root, string operation, byte[] bytes)
    {
        var dir = Path.Combine(root, operation);
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, "receipt.json"), bytes);
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    private static void WriteCensus(string root, string volumes) =>
        File.WriteAllText(Path.Combine(root, "census.json"), "{\"volumes\":" + volumes + "}");

    private static string WriteReason(string root, string text)
    {
        var path = Path.Combine(root, "reason.txt");
        File.WriteAllText(path, text);
        return path;
    }

    // retire-runner-volumes in process on the world (its token file, variables and fakes); stderr then stdout, as the refusals read.
    private static (int Exit, string Output) Close(RetireRunnerVolumesWorld world, string[] args)
    {
        var run = world.Run(RetireRunnerVolumesOptions.Parse(args, world.Checkout));
        return (run.Exit, run.Output + run.Error);
    }

    private static async Task<string> Bash(string script, params (string Key, string Value)[] env)
    {
        var psi = new ProcessStartInfo("bash")
        {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true
        };
        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add(script);
        foreach (var (key, value) in env) psi.Environment[key] = value;
        var run = await Wait(psi, TimeSpan.FromSeconds(30));
        return run.Output;
    }

    private static async Task<(int Exit, string Output)> Wait(ProcessStartInfo psi, TimeSpan budget)
    {
        using var proc = Process.Start(psi) ?? throw new InvalidOperationException("child did not start");
        var stdout = proc.StandardOutput.ReadToEndAsync();
        var stderr = proc.StandardError.ReadToEndAsync();
        using var deadline = new CancellationTokenSource(budget);
        try { await proc.WaitForExitAsync(deadline.Token); }
        catch
        {
            if (!proc.HasExited) { proc.Kill(true); await proc.WaitForExitAsync(); }
            throw;
        }
        return (proc.ExitCode, await stdout + await stderr);
    }

    private static string Extract(string text, string function)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n');
        var start = Array.FindIndex(lines, line => line.StartsWith(function + "() {", StringComparison.Ordinal));
        start.ShouldBeGreaterThanOrEqualTo(0, function);
        var end = Array.FindIndex(lines, start + 1, line => line == "}");
        end.ShouldBeGreaterThan(start, function);
        return string.Join('\n', lines[start..(end + 1)]);
    }
}
