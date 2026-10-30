using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
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
        var preview = await dry.Run("retire-temp", "-DryRun");
        preview.Exit.ShouldBe(0, "c1609-w1: dry-run is the preview entry; " + preview.Output);
        preview.Output.ShouldNotContain("TempStackMissing");
        preview.Trace.Any(row => row["kind"]?.GetValue<string>() == "case" &&
            row["name"]?.GetValue<string>() == "retire-temp-runner").ShouldBeFalse("c1609-w1: dry-run does not apply");

        using var apply = new C1008WrapperFixture();
        var retired = await apply.Run("retire-temp");
        retired.Exit.ShouldBe(0, "c1609-w1: retire-temp apply; " + retired.Output);
        retired.Trace.Any(row => row["kind"]?.GetValue<string>() == "case" &&
            row["name"]?.GetValue<string>() == "retire-temp-runner").ShouldBeTrue("c1609-w1: apply reaches the host case");

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
    [ParallelLimiter<ProcessSpawnLimit>]
    public async Task Manual_parameters_require_operator_attestation()
    {
        var root = Directory.CreateTempSubdirectory("c1609-w2-").FullName;
        try
        {
            var receipt = Receipt(state: "Applying", owner: null, effects: 1);
            var digest = WriteReceipt(root, Operation, receipt);
            WriteCensus(root, "[]");
            var reason = WriteReason(root, "observed absent after content recovery");
            var siblingDir = Path.Combine(root, Sibling);
            Directory.CreateDirectory(siblingDir);
            var siblingText = "{\"schema\":2,\"state\":\"Applying\",\"effects\":[{\"name\":\"antiphon-runner-temp_work\"}]}";
            await File.WriteAllTextAsync(Path.Combine(siblingDir, "receipt.json"), siblingText);
            await File.WriteAllTextAsync(Path.Combine(root, "operator-token"), "operator-secret-token");

            var exclusive = await Close(root, "-Close", "-Apply", "-OperationId", Operation,
                "-ExpectedJournalDigest", digest, "-ReasonFile", reason, "-Sha", Sha);
            exclusive.Exit.ShouldBe(2, exclusive.Output);
            exclusive.Output.ShouldContain("ModeExclusive");
            File.Exists(Path.Combine(root, Operation, "closure.json")).ShouldBeFalse();

            var forced = await Close(root, "-Force", "-Close", "-OperationId", Operation,
                "-ExpectedJournalDigest", digest, "-ReasonFile", reason);
            forced.Exit.ShouldBe(2, forced.Output);
            forced.Output.ShouldContain("ForceRefused");

            var missing = await Close(root, "-Close", "-OperationId", Operation, "-ReasonFile", reason);
            missing.Exit.ShouldBe(2, missing.Output);
            missing.Output.ShouldContain("Digest");

            var wrong = await Close(root, "-Close", "-OperationId", Operation,
                "-ExpectedJournalDigest", new string('0', 64), "-ReasonFile", reason);
            wrong.Exit.ShouldBe(2, wrong.Output);
            wrong.Output.ShouldContain("DigestMismatch");
            File.Exists(Path.Combine(root, Operation, "closure.json")).ShouldBeFalse();
            File.ReadAllBytes(Path.Combine(root, Operation, "receipt.json")).ShouldBe(receipt);

            WriteCensus(root, "[\"antiphon-runner-temp_work\"]");
            var present = await Close(root, "-Close", "-OperationId", Operation,
                "-ExpectedJournalDigest", digest, "-ReasonFile", reason);
            present.Exit.ShouldBe(2, present.Output);
            present.Output.ShouldContain("TempVolumesRetained antiphon-runner-temp_work");
            File.Exists(Path.Combine(root, Operation, "closure.json")).ShouldBeFalse();
            WriteCensus(root, "[]");

            var owned = Receipt(state: "Applying", owner: "helper-1", effects: 1);
            var ownedDigest = WriteReceipt(root, Operation, owned);
            var active = await Close(root, "-Close", "-OperationId", Operation,
                "-ExpectedJournalDigest", ownedDigest, "-ReasonFile", reason);
            active.Exit.ShouldBe(2, active.Output);
            active.Output.ShouldContain("TempRetirementOwnerActive");
            File.Exists(Path.Combine(root, Operation, "closure.json")).ShouldBeFalse();
            digest = WriteReceipt(root, Operation, receipt);

            var closed = await Close(root, "-Close", "-OperationId", Operation,
                "-ExpectedJournalDigest", digest, "-ReasonFile", reason);
            closed.Exit.ShouldBe(0, closed.Output);
            closed.Output.ShouldContain("idempotent=false");
            var closurePath = Path.Combine(root, Operation, "closure.json");
            var closureText = await File.ReadAllTextAsync(closurePath);
            closureText.ShouldNotContain("operator-secret-token");
            var closure = JsonNode.Parse(closureText)!.AsObject();
            closure["schema"]!.GetValue<int>().ShouldBe(2);
            closure["state"]!.GetValue<string>().ShouldBe("Closed");
            closure["operationId"]!.GetValue<string>().ShouldBe(Operation);
            closure["priorDigest"]!.GetValue<string>().ShouldBe(digest);
            closure["actor"]!.GetValue<string>().ShouldBe("operator");
            closure["authority"]!.GetValue<string>().ShouldBe("operator-token");
            closure["volumesAbsent"]!.AsArray().Count.ShouldBe(4);
            File.ReadAllBytes(Path.Combine(root, Operation, "receipt.json")).ShouldBe(receipt);
            (await File.ReadAllTextAsync(Path.Combine(siblingDir, "receipt.json"))).ShouldBe(siblingText);
            var firstBytes = await File.ReadAllBytesAsync(closurePath);

            var again = await Close(root, "-Close", "-OperationId", Operation,
                "-ExpectedJournalDigest", digest, "-ReasonFile", reason);
            again.Exit.ShouldBe(0, again.Output);
            again.Output.ShouldContain("idempotent=true");
            (await File.ReadAllBytesAsync(closurePath)).ShouldBe(firstBytes);

            File.Delete(Path.Combine(root, "operator-token"));
            var siblingDigest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(siblingText))).ToLowerInvariant();
            var unauthenticated = await Close(root, false, null, new[]
            {
                "-Close", "-OperationId", Sibling, "-ExpectedJournalDigest", siblingDigest, "-ReasonFile", reason
            });
            unauthenticated.Exit.ShouldBe(2, unauthenticated.Output);
            unauthenticated.Output.ShouldContain("Authority");

            var authority = Path.Combine(root, "authority.txt");
            await File.WriteAllTextAsync(authority, "delegated for CARD-1609");
            var delegateDigest = WriteReceipt(root, Sibling, Receipt(state: "PartialRefused", owner: null, effects: 1));
            var delegated = await Close(root, false, "task-secret-token", new[]
            {
                "-Close", "-OperationId", Sibling, "-ExpectedJournalDigest", delegateDigest,
                "-ReasonFile", reason, "-AuthorityFile", authority
            });
            delegated.Exit.ShouldBe(0, delegated.Output);
            var delegateClosure = await File.ReadAllTextAsync(Path.Combine(siblingDir, "closure.json"));
            delegateClosure.ShouldNotContain("task-secret-token");
            JsonNode.Parse(delegateClosure)!["authority"]!.GetValue<string>().ShouldBe("task-token");
            (await File.ReadAllTextAsync(Path.Combine(siblingDir, "receipt.json"))).ShouldContain("PartialRefused");
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public async Task Bridge_validates_and_preserves_receipt_fields()
    {
        var script = await File.ReadAllTextAsync(Path.Combine(DelegateScriptRunner.RepoRoot, "scripts/retire-runner-volumes.ps1"));
        script.Contains('\r').ShouldBeFalse("c1609-w3: close script is LF");
        Encoding.UTF8.GetBytes(script).Any(b => b > 127).ShouldBeFalse("c1609-w3: close script is ASCII");
        var deploy = await File.ReadAllTextAsync(Path.Combine(DelegateScriptRunner.RepoRoot, "scripts/deploy-server2.ps1"));
        var call = deploy.IndexOf("\n                Assert-TempRetirementClearable\n", StringComparison.Ordinal);
        var absent = deploy.LastIndexOf("Assert-TempProjectAbsent", call, StringComparison.Ordinal);
        var clear = deploy.IndexOf("/drain/clear", call, StringComparison.Ordinal);
        absent.ShouldBeGreaterThanOrEqualTo(0);
        call.ShouldBeGreaterThan(absent);
        clear.ShouldBeGreaterThan(call);

        var root = Directory.CreateTempSubdirectory("c1609-w3-").FullName;
        try
        {
            var reasonText = "line one\npath C:\\temp\\note \"quoted\"";
            var receipt = Receipt(state: "Ready", owner: null, effects: 0);
            var digest = WriteReceipt(root, Operation, receipt);
            WriteCensus(root, "[]");
            var reason = WriteReason(root, reasonText);
            await File.WriteAllTextAsync(Path.Combine(root, "operator-token"), "operator-secret-token");
            var closed = await Close(root, "-Close", "-OperationId", Operation,
                "-ExpectedJournalDigest", digest, "-ReasonFile", reason);
            closed.Exit.ShouldBe(0, closed.Output);
            var stored = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(root, Operation, "closure.json")))!;
            stored["reason"]!.GetValue<string>().ShouldBe(reasonText.Trim());
            File.ReadAllBytes(Path.Combine(root, Operation, "receipt.json")).ShouldBe(receipt);

            var huge = WriteReason(root, new string('x', 4097));
            File.Delete(Path.Combine(root, Operation, "closure.json"));
            var refused = await Close(root, "-Close", "-OperationId", Operation,
                "-ExpectedJournalDigest", digest, "-ReasonFile", huge);
            refused.Exit.ShouldBe(2, refused.Output);
            refused.Output.ShouldContain("ReasonFile");
            File.Exists(Path.Combine(root, Operation, "closure.json")).ShouldBeFalse();
        }
        finally { Directory.Delete(root, recursive: true); }
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
        var dry = await preview.Run("retire-temp", "-DryRun");
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
        var unbound = await Pwsh("-NoProfile", "-NonInteractive", "-File",
            Path.Combine(DelegateScriptRunner.RepoRoot, "scripts/deploy-server2.ps1"), "-Rolling", "-Phase", "redeploy-old");
        unbound.Exit.ShouldNotBe(0, unbound.Output);
        unbound.Output.ShouldContain("Sha");

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
            c1008_previous_generation "$PROJECT" "$owned" > "$root/out" 2> "$root/err"
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
        tempMismatch.ShouldContain("refused=RecycleGenerationMismatch");
        var tempNoRunner = await Bash(shell, ("SHA", Sha), ("IMAGE", imageA), ("DOCKER_IMAGE", imageB),
            ("PROJECT", "antiphon-runner-temp"), ("SERVICE", "state-init"));
        tempNoRunner.ShouldContain("exit=0");
        tempNoRunner.ShouldContain("statusBuildVersion");
        tempNoRunner.ShouldNotContain("refused=");
    }

    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public async Task Retained_volumes_block_deploy_and_completion()
    {
        var deploy = await File.ReadAllTextAsync(Path.Combine(DelegateScriptRunner.RepoRoot, "scripts/deploy-server2.ps1"));
        var call = deploy.IndexOf("\n                Assert-TempRetirementClearable\n", StringComparison.Ordinal);
        deploy.LastIndexOf("Assert-TempProjectAbsent", call, StringComparison.Ordinal).ShouldBeGreaterThanOrEqualTo(0);
        deploy.IndexOf("/drain/clear", call, StringComparison.Ordinal).ShouldBeGreaterThan(call);
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

        if (!OperatingSystem.IsLinux()) return;
        var function = Extract(remote, "c1609_temp_retirement_clearable");
        var probe = function + "\n" + """
            root="$(mktemp -d)"
            trap 'rm -rf "$root"' EXIT
            SERVER2_ROOT="$root/server"
            docker() { case "${3:-}" in *antiphon-runner-temp_*) [ "$PRESENT" = 1 ] && return 0; return 1 ;; *) return 1 ;; esac; }
            op="$SERVER2_ROOT/retirement/c100800000000000000000000000000000001"
            if [ -n "$STATE" ]; then
                mkdir -p "$op"
                printf '%s\n' "$RECEIPT" > "$op/receipt.json"
                if [ "$CLOSED" = 1 ]; then printf '%s\n' '{"schema":2,"state":"Closed"}' > "$op/closure.json"; fi
            fi
            c1609_clear_reason=
            c1609_temp_retirement_clearable
            printf 'exit=%s reason=%s\n' "$?" "${c1609_clear_reason:-ok}"
            """;
        async Task Shell(string present, string state, int effects, int intents, string closed, string expect)
        {
            var receipt = state.Length == 0 ? "" :
                "{\"schema\":2,\"state\":\"" + state + "\",\"owner\":null,\"effects\":[" +
                string.Join(",", Enumerable.Repeat("{\"name\":\"antiphon-runner-temp_work\"}", effects)) +
                "],\"intents\":[" + string.Join(",", Enumerable.Repeat("{\"name\":\"antiphon-runner-temp_work\"}", intents)) + "]}";
            var output = await Bash(probe,
                ("PRESENT", present), ("STATE", state), ("RECEIPT", receipt), ("CLOSED", closed));
            output.ShouldContain(expect, output);
        }
        await Shell("1", "", 0, 0, "0", "exit=1 reason=TempVolumesRetained");
        await Shell("0", "Refused", 0, 0, "0", "exit=0 reason=ok");
        await Shell("0", "Ready", 1, 0, "0", "exit=1 reason=TempRetirementUnresolved");
        await Shell("0", "Applying", 1, 1, "0", "exit=1 reason=TempRetirementUnresolved");
        await Shell("0", "Applying", 1, 1, "1", "exit=0 reason=ok");
        await Shell("0", "Completed", 1, 1, "0", "exit=0 reason=ok");
        await Shell("1", "Completed", 1, 1, "0", "exit=1 reason=TempVolumesRetained");
        await Shell("0", "Weird", 0, 0, "0", "exit=1 reason=TempRetirementJournalUnknown");
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
        JsonNode.Parse(await File.ReadAllTextAsync(fixture.StatePath))!["statuses"]!["server2-temp"]!["retiredAt"]!
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

    private static Task<(int Exit, string Output)> Close(string root, params string[] args) =>
        Close(root, true, null, args);

    private static async Task<(int Exit, string Output)> Close(string root, bool token, string? taskToken, string[] args)
    {
        var script = Path.Combine(DelegateScriptRunner.RepoRoot, "scripts/retire-runner-volumes.ps1");
        var psi = new ProcessStartInfo("pwsh")
        {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
            WorkingDirectory = DelegateScriptRunner.RepoRoot
        };
        foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-File", script }.Concat(args)) psi.ArgumentList.Add(arg);
        psi.Environment["C1609_RETIREMENT_ROOT"] = root;
        psi.Environment["C1609_VOLUME_CENSUS_FILE"] = Path.Combine(root, "census.json");
        psi.Environment["ANTIPHON_OPERATOR_TOKEN_FILE"] = token ? Path.Combine(root, "operator-token") : "";
        psi.Environment["ANTIPHON_TASK_TOKEN"] = taskToken ?? "";
        return await Wait(psi, TimeSpan.FromSeconds(30));
    }

    private static async Task<(int Exit, string Output)> Pwsh(params string[] args)
    {
        var psi = new ProcessStartInfo("pwsh")
        {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (var arg in args) psi.ArgumentList.Add(arg);
        return await Wait(psi, TimeSpan.FromSeconds(30));
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
