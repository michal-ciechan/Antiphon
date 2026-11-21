using System.Diagnostics;
using Antiphon.Tests.Application;
using Antiphon.Tests.TestHelpers;
using Antiphon.TestSupport;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

// Close must skip the same receipt-less never-Ready previews the clearable
// check already skips, and must still refuse a preview that published Ready,
// still has a live owner, or already has a close appendix.
[Timeout(120_000)]
[Category("Unit")]
[NativeLinuxOnly("Close exemption runs the Linux retirement shell, /proc start ticks, and a fake ssh")]
public sealed class VolumeRetirementCloseExemptionTests
{
    private const string Shell = """
        set -euo pipefail
        umask 077
        root="$(mktemp -d)"
        root="$(readlink -e -- "$root")"
        trap 'rm -rf -- "$root"' EXIT
        export SERVER2_ROOT="$root/server"
        export C1008_OPERATION=c1008aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa
        export C1008_PROJECT=antiphon-runner-temp
        export C1609_CLOSE_TOOLING_DIGEST=0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef
        # shellcheck disable=SC1090
        source "$RETIREMENT"
        c1008_volume() { printf 'null\n'; }
        docker() {
            case "$1" in
                info) printf 'fixture-daemon\n' ;;
                ps) ;;
                *) return 1 ;;
            esac
        }
        targets='["antiphon-runner-temp_dind-data","antiphon-runner-temp_runner-state","antiphon-runner-temp_runner-tmp","antiphon-runner-temp_work"]'
        op="$SERVER2_ROOT/retirement/$C1008_OPERATION"
        mkdir -p -- "$op"
        base="$(jq -cn --arg op "$C1008_OPERATION" --argjson targets "$targets" '{
          schema:2,policyVersion:1,operationId:$op,project:"antiphon-runner-temp",mode:"scripted",
          daemon:"fixture-daemon",image:("sha256:"+("a"*64)),inventoryDigest:("b"*64),
          phaseNonce:("a"*32),readyAt:"2026-10-31T00:00:00Z",retiredAt:"2026-10-30T00:00:00Z",
          targets:$targets,fingerprints:($targets|map({key:.,value:null})|from_entries)}')"
        jq -ncS --argjson base "$base" '$base+{state:"Ready",consumed:false}' \
            > "$op/preview-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa.json"
        jq -ncS --argjson base "$base" --argjson targets "$targets" '
          $base+{state:"PartialRefused",consumed:true,owner:null,
            intents:($targets[0:3]|map({intent:"remove",name:.,result:"removed"})),
            effects:($targets[0:3]|map({name:.,result:"removed"}))}' > "$op/receipt.json"
        jq -ncS --arg op "$C1008_OPERATION" '{
          schema:1,active:false,children:[],complete:true,daemon:"fixture-daemon",
          nonce:("c"*32),operationId:$op,owner:{pid:99999999,startTicks:"0"},readyPublished:true}' \
            > "$op/lifecycle.json"
        receipt_before="$(sha256sum "$op/receipt.json" | cut -d" " -f1)"
        write_sibling() {
            local id="$1" published="$2" live="$3" pending="$4"
            local dir="$SERVER2_ROOT/retirement/$id" pid=99999999 ticks=0
            mkdir -p -- "$dir"
            if [ "$live" = 1 ]; then
                pid=$$
                ticks="$(awk '{ sub(/^.*\) /, ""); print $20 }' "/proc/$$/stat")"
            fi
            jq -ncS --arg op "$id" --argjson pid "$pid" --arg ticks "$ticks" --argjson published "$published" '{
              schema:1,active:false,children:[],complete:true,daemon:"fixture-daemon",
              nonce:("d"*32),operationId:$op,owner:{pid:$pid,startTicks:$ticks},readyPublished:$published}' \
                > "$dir/lifecycle.json"
            if [ "$pending" = 1 ]; then printf '{}\n' > "$dir/close-pending.json"; fi
            sha256sum "$dir/lifecycle.json" | cut -d" " -f1 > "$dir/lifecycle.sha"
        }
        write_sibling c1008bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb false 0 0
        case "$MODE" in
            exempt) write_sibling c1008cccccccccccccccccccccccccccccccc false 0 0 ;;
            ready) write_sibling c1008cccccccccccccccccccccccccccccccc true 0 0 ;;
            live) write_sibling c1008cccccccccccccccccccccccccccccccc false 1 0 ;;
            pending) write_sibling c1008cccccccccccccccccccccccccccccccc false 0 1 ;;
            *) printf 'bad mode\n' >&2; exit 9 ;;
        esac
        printf 'SETUP_OK=1\n'
        expected="$(sha256sum "$op/receipt.json" | cut -d" " -f1)"
        reason64="$(printf '%s' 'volumes already absent' | base64 -w0)"
        set +e
        c1609_close_host "$expected" operator operator-token "$reason64"
        code=$?
        set -e
        printf 'CLOSE_EXIT=%s\n' "$code"
        if [ -f "$op/closure.json" ]; then
            after="$(sha256sum "$op/receipt.json" | cut -d' ' -f1)"
            jq -er --arg before "$receipt_before" --arg after "$after" '
              ([.reconciliations[]|select(.result=="removed-observed")]|length) as $removed |
              ([.reconciliations[]|select(.result=="unattributed-absence")]|length) as $absent |
              select($before==$after) | "REMOVED_OBSERVED=\($removed)\nUNATTRIBUTED=\($absent)"' "$op/closure.json"
        else
            printf 'CLOSURE=absent\n'
        fi
        stable=1
        for id in c1008bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb c1008cccccccccccccccccccccccccccccccc; do
            dir="$SERVER2_ROOT/retirement/$id"
            [ "$(sha256sum "$dir/lifecycle.json" | cut -d" " -f1)" = "$(cat "$dir/lifecycle.sha")" ] || stable=0
            [ ! -e "$dir/receipt.json" ] && [ ! -L "$dir/receipt.json" ] || stable=0
            [ ! -e "$dir/closure.json" ] && [ ! -L "$dir/closure.json" ] || stable=0
            if [ "$MODE" = pending ] && [ "$id" = c1008cccccccccccccccccccccccccccccccc ]; then
                [ -f "$dir/close-pending.json" ] && [ ! -L "$dir/close-pending.json" ] || stable=0
            else
                [ ! -e "$dir/close-pending.json" ] && [ ! -L "$dir/close-pending.json" ] || stable=0
            fi
        done
        printf 'SIBLINGS_UNTOUCHED=%s\n' "$stable"
        """;

    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public async Task Close_exempts_receiptless_never_ready_previews()
    {
        var run = await Run(Shell, "exempt");
        run.Exit.ShouldBe(0, run.Output);
        var output = run.Output;
        output.ShouldContain("SETUP_OK=1");
        output.ShouldContain("CLOSE_EXIT=0");
        output.ShouldContain("REMOVED_OBSERVED=3");
        output.ShouldContain("UNATTRIBUTED=1");
        output.ShouldContain("SIBLINGS_UNTOUCHED=1");
        output.ShouldNotContain("CLOSURE=absent");
    }

    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public async Task Close_refuses_receiptless_preview_that_published_ready()
    {
        var run = await Run(Shell, "ready");
        run.Exit.ShouldBe(0, run.Output);
        var output = run.Output;
        output.ShouldContain("SETUP_OK=1");
        output.ShouldContain("CLOSE_EXIT=2");
        output.ShouldContain("CLOSURE=absent");
        output.ShouldContain("SIBLINGS_UNTOUCHED=1");
    }

    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public async Task Close_refuses_receiptless_preview_with_live_owner()
    {
        var run = await Run(Shell, "live");
        run.Exit.ShouldBe(0, run.Output);
        var output = run.Output;
        output.ShouldContain("SETUP_OK=1");
        output.ShouldContain("CLOSE_EXIT=2");
        output.ShouldContain("CLOSURE=absent");
        output.ShouldContain("SIBLINGS_UNTOUCHED=1");
    }

    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public async Task Close_refuses_receiptless_preview_with_close_pending()
    {
        var run = await Run(Shell, "pending");
        run.Exit.ShouldBe(0, run.Output);
        var output = run.Output;
        output.ShouldContain("SETUP_OK=1");
        output.ShouldContain("CLOSE_EXIT=2");
        output.ShouldContain("CLOSURE=absent");
        output.ShouldContain("SIBLINGS_UNTOUCHED=1");
    }

    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public async Task Close_wrapper_surfaces_remote_refusal_token()
    {
        var daemon = await Transport("printf 'TempRetirementCloseRefused: Daemon\\n' >&2; exit 2");
        daemon.Exit.ShouldBe(2, daemon.Output);
        daemon.Output.ShouldContain("TempRetirementCloseRefused: Daemon");
        daemon.Output.ShouldNotContain("JournalUnknown");

        var retained = await Transport(
            "printf 'TempRetirementCloseRefused: TempVolumesRetained antiphon-runner-temp_work\\n' >&2; exit 2");
        retained.Exit.ShouldBe(2, retained.Output);
        retained.Output.ShouldContain("TempRetirementCloseRefused: TempVolumesRetained antiphon-runner-temp_work");

        var empty = await Transport("exit 2");
        empty.Exit.ShouldBe(2, empty.Output);
        empty.Output.ShouldContain("TempRetirementCloseRefused: JournalUnknown");

        var secret = await Transport("printf 'TempRetirementCloseRefused: password=hunter2\\n' >&2; exit 2");
        secret.Exit.ShouldBe(2, secret.Output);
        secret.Output.ShouldContain("TempRetirementCloseRefused: JournalUnknown");
        secret.Output.ShouldNotContain("hunter2");

        var closed = await Transport("printf 'TempRetirementClosed operation=c1008aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa idempotent=false\\n'; exit 0");
        closed.Exit.ShouldBe(0, closed.Output);
        closed.Output.ShouldContain("TempRetirementClosed operation=c1008aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa idempotent=false");
    }

    private static async Task<(int Exit, string Output)> Run(string script, string mode)
    {
        var psi = new ProcessStartInfo("bash")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add(script);
        psi.Environment["RETIREMENT"] = Path.Combine(DelegateScriptRunner.RepoRoot, "scripts/c1008-retirement.sh");
        psi.Environment["MODE"] = mode;
        return await Wait(psi, TimeSpan.FromSeconds(60));
    }

    private static async Task<(int Exit, string Output)> Transport(string sshBody)
    {
        var root = Directory.CreateTempSubdirectory("c1609-close-ssh-").FullName;
        try
        {
            var ssh = Path.Combine(root, "ssh");
            await File.WriteAllTextAsync(ssh, "#!/bin/bash\ncat >/dev/null\n" + sshBody + "\n");
            File.SetUnixFileMode(ssh, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            var script = Path.Combine(DelegateScriptRunner.RepoRoot, "scripts/retire-runner-volumes.ps1");
            var psi = new ProcessStartInfo("pwsh")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            psi.ArgumentList.Add("-NoProfile");
            psi.ArgumentList.Add("-NonInteractive");
            psi.ArgumentList.Add("-Command");
            psi.ArgumentList.Add(". " + VolumeRetirementFixture.Quote(script) + "; Invoke-Server2Bash -Script 'printf hi' -TimeoutMs 15000");
            psi.Environment["PATH"] = root + ":" + Environment.GetEnvironmentVariable("PATH");
            return await Wait(psi, TimeSpan.FromSeconds(30));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static async Task<(int Exit, string Output)> Wait(ProcessStartInfo psi, TimeSpan budget)
    {
        using var proc = Process.Start(psi) ?? throw new InvalidOperationException("child did not start");
        var stdout = proc.StandardOutput.ReadToEndAsync();
        var stderr = proc.StandardError.ReadToEndAsync();
        using var deadline = new CancellationTokenSource(budget);
        try
        {
            await proc.WaitForExitAsync(deadline.Token);
        }
        catch
        {
            if (!proc.HasExited)
            {
                proc.Kill(true);
                await proc.WaitForExitAsync();
            }
            throw;
        }
        return (proc.ExitCode, await stdout + await stderr);
    }
}
