using System.Text;
using System.Text.Json.Nodes;
using Antiphon.TestSupport;
using Antiphon.Tests.Application;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

[NativeLinuxOnly(NativeLinuxReason)]
[Timeout(600_000)]
[Category("Unit")]
public sealed class VolumeRetirementContentTests
{
    internal const string NativeLinuxReason = "CARD-1609: content retirement proofs require native Linux (bash, flock, real Git and POSIX mounts); Windows skips explicitly.";

    [Test]
    [Arguments("ignored", "Ready")]
    [Arguments("outside", "RecycleGitAuditUnknown")]
    [Arguments("remote", "RecycleGitAuditUnknown")]
    [Arguments("not-ignored", "RecycleGitAuditUnknown")]
    [Arguments("dirty", "RecycleWorktreeDirty")]
    [Arguments("unpublished", "RecycleUnpublishedWork")]
    [ParallelLimiter<ProcessSpawnLimit>]
    public async Task Selector_fixture_requires_ignored_scratch_no_remote_and_safe_parent(string vector, string expected)
    {
        NativeLinuxGate.Require(NativeLinuxReason);
        using var fixture = new C1008HostFixture(main: false);
        var (_, outer) = await VolumeRetirementFixture.SeedPublishedOuter(fixture);
        var nested = Path.Combine(outer, vector == "outside" ? "other-scratch" : ".antiphon/selector-evidence/x/fixtures/y");
        var setup = await VolumeRetirementFixture.Bash($$"""
            set -euo pipefail
            git init -q -b main {{VolumeRetirementFixture.Quote(nested)}}
            git -C {{VolumeRetirementFixture.Quote(nested)}} -c user.name=Fixture -c user.email=fixture@example.invalid -c commit.gpgsign=false commit --allow-empty -qm fixture
            """);
        setup.Exit.ShouldBe(0, setup.Output);
        if (vector == "remote")
        {
            var remote = await VolumeRetirementFixture.Bash("git -C " + VolumeRetirementFixture.Quote(nested) +
                " remote add backup " + VolumeRetirementFixture.Quote(Path.Combine(fixture.Root, "absent-origin.git")));
            remote.Exit.ShouldBe(0, remote.Output);
        }
        if (vector == "dirty") await File.AppendAllTextAsync(Path.Combine(outer, "readme.txt"), "dirty\n");
        if (vector == "unpublished" || vector == "not-ignored")
        {
            if (vector == "not-ignored") await File.WriteAllTextAsync(Path.Combine(outer, ".gitignore"), "");
            var change = await VolumeRetirementFixture.Bash("git -C " + VolumeRetirementFixture.Quote(outer) +
                " -c commit.gpgsign=false commit -qam change --allow-empty" +
                (vector == "not-ignored" ? " && git -C " + VolumeRetirementFixture.Quote(outer) + " push -q origin HEAD" : ""));
            change.Exit.ShouldBe(0, change.Output);
        }
        var preview = await VolumeRetirementFixture.Preview(fixture);
        var text = preview.Output + await VolumeRetirementFixture.AuditText(fixture);
        fixture.Removed.ShouldBeEmpty();
        if (expected == "Ready")
        {
            preview.Exit.ShouldBe(0, text);
            text.ShouldContain(VolumeRetirementFixture.ReadyMarker);
            text.ShouldContain("disposable-fixture classifier=selector-evidence-v1");
        }
        else
        {
            preview.Exit.ShouldBe(2, text);
            text.ShouldContain(expected);
            text.ShouldNotContain(VolumeRetirementFixture.ReadyMarker);
        }
    }

    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public async Task Current_tooling_and_missing_history_do_not_block()
    {
        NativeLinuxGate.Require(NativeLinuxReason);
        var probe = "\n" + VolumeRetirementFixture.ShellProbe;
        using var missingHistory = new C1008HostFixture(main: false);
        var unpinned = await missingHistory.Run("retire-temp-runner", probe, dryRun: true);
        unpinned.Exit.ShouldBe(0, "c1609-c1: unpinned preview reaches the current entry; " + unpinned.Output);
        unpinned.Output.ShouldContain("C1609_TOOLING protocol=2 policy=1");
        unpinned.Output.ShouldContain("auditPending=false");
        unpinned.Output.ShouldContain(VolumeRetirementFixture.ReadyMarker);
        missingHistory.Removed.ShouldBeEmpty();

        using var missingStack = new C1008HostFixture(main: false);
        await File.WriteAllTextAsync(Path.Combine(missingStack.Root, "temp.env"), "");
        var stack = await missingStack.Run("retire-temp-runner", probe, dryRun: true);
        stack.Exit.ShouldBe(0, "c1609-c1: an empty temp.env is not the content gate; " + stack.Output);
        stack.Output.ShouldNotContain("TempStackMissing");
        stack.Output.ShouldContain(VolumeRetirementFixture.ReadyMarker);
        missingStack.Removed.ShouldBeEmpty();

        using var conflicting = new C1008HostFixture(main: false);
        await VolumeRetirementFixture.RetainPreviewImage(conflicting);
        var seedPath = Path.Combine(conflicting.Root, "server", "temp-container-retirement",
            VolumeRetirementFixture.AppliedCleanup + ".json");
        var seed = JsonNode.Parse(await File.ReadAllTextAsync(seedPath))!.AsObject();
        ReplaceReceipt(conflicting, seed, "c99400000000000000000000000000000011",
            "f84228f2bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", "sha256:" + new string('4', 64));
        ReplaceReceipt(conflicting, seed, "c99400000000000000000000000000000012",
            "dddddddddddddddddddddddddddddddddddddddd", "sha256:" + new string('e', 64), append: true);
        var conflict = await conflicting.Run("retire-temp-runner", probe, dryRun: true);
        conflict.Exit.ShouldBe(0, "c1609-c1: conflicting old images warn and still prove content; " + conflict.Output);
        conflict.Output.ShouldContain("C1609_TOOLING protocol=2 policy=1");
        conflict.Output.ShouldContain(VolumeRetirementFixture.ReadyMarker);
        conflicting.Removed.ShouldBeEmpty();

        using var missingRuntime = new C1008HostFixture(main: false);
        missingRuntime.Docker["fault"] = "image-inspect-error";
        var runtime = await missingRuntime.Run("retire-temp-runner");
        runtime.Exit.ShouldBe(2, "c1609-c1: a missing current image refuses; " + runtime.Output);
        missingRuntime.Removed.ShouldBeEmpty();
        runtime.Output.ShouldNotContain(VolumeRetirementFixture.ReadyMarker);

        // A current compatible runtime can differ from every creating build.
        // The helper must use that descriptor, never the old receipt or -Sha.
        using var current = new C1008HostFixture(main: false);
        await VolumeRetirementFixture.RetainPreviewImage(current);
        var currentImage = "sha256:" + new string('e', 64);
        var descriptor = Path.Combine(current.Root, "server", "audit-current.json");
        await File.WriteAllTextAsync(descriptor,
            "{\"image\":\"" + currentImage + "\",\"policyVersion\":1,\"protocol\":2,\"schema\":1}");
        var selected = await current.Run("retire-temp-runner", probe, dryRun: true);
        selected.Exit.ShouldBe(0, "c1609 current compatible tooling; " + selected.Output);
        selected.Output.ShouldContain(VolumeRetirementFixture.ReadyMarker);
        var helper = current.Trace.Where(a => a.Length > 0 && a[0] == "run").Last();
        helper[Array.IndexOf(helper, "-c") - 1].ShouldBe(currentImage,
            "current descriptor, independent of historical cleanup image and caller SHA");
        current.Removed.ShouldBeEmpty();

        foreach (var fault in new[] { "missing", "duplicate", "policy", "writable", "symlink" })
        {
            using var invalid = new C1008HostFixture(main: false);
            var path = Path.Combine(invalid.Root, "server", "audit-current.json");
            if (fault == "missing") File.Delete(path);
            if (fault == "duplicate") await File.WriteAllTextAsync(path,
                "{\"image\":\"" + currentImage + "\",\"policyVersion\":0,\"policyVersion\":1,\"protocol\":2,\"schema\":1}");
            if (fault == "policy") await File.WriteAllTextAsync(path,
                "{\"image\":\"" + currentImage + "\",\"policyVersion\":99,\"protocol\":2,\"schema\":1}");
            if (fault == "writable") File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.OtherWrite);
            if (fault == "symlink")
            {
                File.Move(path, path + ".real");
                File.CreateSymbolicLink(path, path + ".real");
            }
            var refusedRuntime = await invalid.Run("retire-temp-runner", probe, dryRun: true);
            refusedRuntime.Exit.ShouldBe(2, "c1609 current runtime must refuse " + fault + "; " + refusedRuntime.Output);
            refusedRuntime.Output.ShouldNotContain(VolumeRetirementFixture.ReadyMarker);
            invalid.Removed.ShouldBeEmpty();
            invalid.Trace.Any(a => a.Length > 0 && a[0] == "run").ShouldBeFalse("invalid current runtime starts no helper");
        }

        var admitted =
            unpinned.Output.Contains(VolumeRetirementFixture.ReadyMarker, StringComparison.Ordinal) &&
            stack.Output.Contains(VolumeRetirementFixture.ReadyMarker, StringComparison.Ordinal) &&
            conflict.Output.Contains(VolumeRetirementFixture.ReadyMarker, StringComparison.Ordinal);
        admitted.ShouldBeTrue(
            "missing or conflicting history must admit a current Ready proof; base kept the old binding. " +
            "unpinned=" + VolumeRetirementFixture.Truncate(unpinned.Output) +
            " stack=" + VolumeRetirementFixture.Truncate(stack.Output) +
            " conflict=" + VolumeRetirementFixture.Truncate(conflict.Output));
    }

    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public async Task State_credentials_refuse_before_removal()
    {
        NativeLinuxGate.Require(NativeLinuxReason);
        using var fixture = new C1008HostFixture(main: false);
        var credential = Path.Combine(VolumeRetirementFixture.StateMount(fixture), "auth.json");
        await File.WriteAllTextAsync(credential, "{\"kind\":\"process-marker\"}\n");
        var run = await fixture.Run("retire-temp-runner");
        var state = "antiphon-runner-temp_runner-state";
        var removed = fixture.Removed.Contains(state);
        var refused = run.Exit != 0 && !removed &&
            run.Output.Contains("reason=state-credential", StringComparison.Ordinal);
        refused.ShouldBeTrue(
            "state credentials must refuse with zero removes; the work-only audit permitted removal. " +
            "exit=" + run.Exit + " removed=[" + string.Join(",", fixture.Removed) + "] out=" +
            VolumeRetirementFixture.Truncate(run.Output));

        using var unreadable = new C1008HostFixture(main: false);
        var mount = VolumeRetirementFixture.StateMount(unreadable);
        var previous = File.GetUnixFileMode(mount);
        try
        {
            File.SetUnixFileMode(mount, UnixFileMode.None);
            var blocked = await unreadable.Run("retire-temp-runner");
            blocked.Exit.ShouldBe(2, "an unreadable state mount refuses; " + VolumeRetirementFixture.Truncate(blocked.Output));
            blocked.Output.ShouldContain("reason=state-unreadable");
            blocked.Output.ShouldNotContain("reason=state-credential");
            unreadable.Removed.ShouldBeEmpty();
        }
        finally
        {
            File.SetUnixFileMode(mount, previous);
        }

        using var elevation = new C1008HostFixture(main: false);
        // Evidence setup still uses sudo -n install. Refuse only the mount probe.
        var denied = await elevation.Run("retire-temp-runner", """
            sudo() {
                if [ "$1" = -n ] && [ "$2" = true ]; then return 1; fi
                [ "$1" = -n ] && shift
                if [ "$1" = install ]; then mkdir -p "${@: -1}"
                elif [ "$1" = df ]; then printf 'Filesystem 1024-blocks Used Available Capacity Mounted on\nfixture 99999999 1 25000000 1%% /fixture\n'
                else "$@"; fi
            }
            """);
        denied.Exit.ShouldBe(2, "a sudo refusal is fail-closed; " + VolumeRetirementFixture.Truncate(denied.Output));
        denied.Output.ShouldContain("reason=state-elevation-unavailable");
        denied.Output.ShouldNotContain("reason=state-credential");
        elevation.Removed.ShouldBeEmpty();
    }

    // Claude Code's per-process UDS messaging key: claude/sessions/<pid>.<sha256 of the
    // socket path>.key, a 0600 JSON peer token that dies with its pid. Only that exact
    // shape in the state volume is disposable; every other credential name refuses.
    private static readonly string SessionHash = string.Concat(Enumerable.Repeat("0123456789abcdef", 4));

    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public async Task Claude_session_keys_are_disposable_state_bound_into_the_inventory()
    {
        NativeLinuxGate.Require(NativeLinuxReason);
        using var fixture = new C1008HostFixture(main: false);
        var state = VolumeRetirementFixture.StateMount(fixture);
        var sessions = Path.Combine(state, "claude", "sessions");
        var key = Path.Combine(sessions, "4151." + SessionHash + ".key");
        var planted = await VolumeRetirementFixture.Bash($$"""
            set -euo pipefail
            mkdir -p -- {{VolumeRetirementFixture.Quote(sessions)}}
            head -c 111 /dev/zero | tr '\0' x > {{VolumeRetirementFixture.Quote(key)}}
            head -c 256 /dev/zero | tr '\0' y > {{VolumeRetirementFixture.Quote(Path.Combine(sessions, "1." + SessionHash + ".key"))}}
            head -c 7 /dev/zero | tr '\0' z > {{VolumeRetirementFixture.Quote(Path.Combine(sessions, "1234567890." + SessionHash + ".key"))}}
            """);
        planted.Exit.ShouldBe(0, "c1609-r4: plant session keys; " + planted.Output);
        var ok = await VolumeRetirementFixture.Preview(fixture);
        var okText = ok.Output + await VolumeRetirementFixture.AuditText(fixture);
        ok.Exit.ShouldBe(0, "c1609-r4: exact Claude session keys are disposable state; " + VolumeRetirementFixture.Truncate(okText));
        okText.ShouldContain(VolumeRetirementFixture.ReadyMarker);
        okText.ShouldContain("disposable-fixture classifier=claude-session-key-v1 path=claude/sessions/4151." + SessionHash + ".key size=111");
        okText.ShouldContain("disposable-fixture classifier=claude-session-key-v1 path=claude/sessions/1." + SessionHash + ".key size=256");
        okText.ShouldContain("disposable-fixture classifier=claude-session-key-v1 path=claude/sessions/1234567890." + SessionHash + ".key size=7");
        okText.ShouldNotContain("reason=state-credential");
        fixture.Removed.ShouldBeEmpty();

        var upper = string.Concat(Enumerable.Repeat("0123456789ABCDEF", 4));
        var variants = new (string Kind, string Path)[]
        {
            ("size", Path.Combine("claude", "sessions", "2." + SessionHash + ".key")),
            ("file", Path.Combine("claude", "sessions", "2." + upper + ".key")),
            ("file", Path.Combine("claude", "sessions", "2." + SessionHash[..63] + ".key")),
            ("file", Path.Combine("claude", "sessions", "2." + SessionHash + "0.key")),
            ("file", Path.Combine("claude", "sessions", "2." + SessionHash + ".key\n.key")),
            ("file", Path.Combine("claude", "sessions", "\n2." + SessionHash + ".key")),
            ("file", Path.Combine("claude", "sessions", "0." + SessionHash + ".key")),
            ("file", Path.Combine("claude", "sessions", "12345678901." + SessionHash + ".key")),
            ("file", Path.Combine("claude", "sessions", "x." + SessionHash + ".key")),
            ("file", Path.Combine("claude", "sessions", "nested", "2." + SessionHash + ".key")),
            ("file", Path.Combine("claude-1654", "sessions", "2." + SessionHash + ".key")),
            ("file", Path.Combine("sessions", "2." + SessionHash + ".key")),
            ("file", Path.Combine("other", "claude", "sessions", "2." + SessionHash + ".key")),
            ("file", Path.Combine("claude", "2." + SessionHash + ".key")),
            ("file", Path.Combine("claude", "tls.key")),
            ("file", Path.Combine("claude", "sessions", "2." + SessionHash + ".pem")),
            ("file", Path.Combine("claude", "sessions", "auth.json")),
            ("file", Path.Combine("claude", "auth.json")),
            ("file", Path.Combine("claude", ".credentials.json")),
            ("symlink", Path.Combine("claude", "sessions", "2." + SessionHash + ".key")),
            ("dir", Path.Combine("claude", "sessions", "2." + SessionHash + ".key")),
            ("hardlink", Path.Combine("claude", "sessions", "2." + SessionHash + ".key")),
        };
        foreach (var (kind, relative) in variants)
        {
            using var variant = new C1008HostFixture(main: false);
            var root = VolumeRetirementFixture.StateMount(variant);
            var path = Path.Combine(root, relative);
            var target = Path.Combine(root, "variant", "target");
            Directory.CreateDirectory(Path.Combine(root, "claude", "sessions"));
            await File.WriteAllTextAsync(Path.Combine(root, "claude", "sessions", "4151." + SessionHash + ".key"), new string('x', 111));
            var shaped = await VolumeRetirementFixture.Bash($$"""
                set -euo pipefail
                mkdir -p -- {{VolumeRetirementFixture.Quote(Path.GetDirectoryName(path)!)}} {{VolumeRetirementFixture.Quote(Path.GetDirectoryName(target)!)}}
                case {{kind}} in
                    file) printf x > {{VolumeRetirementFixture.Quote(path)}} ;;
                    size) head -c 257 /dev/zero | tr '\0' x > {{VolumeRetirementFixture.Quote(path)}} ;;
                    symlink) printf x > {{VolumeRetirementFixture.Quote(target)}}; ln -s ../../variant/target {{VolumeRetirementFixture.Quote(path)}} ;;
                    dir) mkdir -p -- {{VolumeRetirementFixture.Quote(path)}} ;;
                    hardlink) printf x > {{VolumeRetirementFixture.Quote(target)}}; ln {{VolumeRetirementFixture.Quote(target)}} {{VolumeRetirementFixture.Quote(path)}} ;;
                esac
                """);
            shaped.Exit.ShouldBe(0, "c1609-r4: plant " + kind + " at " + relative + "; " + shaped.Output);
            var refused = await VolumeRetirementFixture.Preview(variant);
            var text = refused.Output + await VolumeRetirementFixture.AuditText(variant);
            refused.Exit.ShouldBe(2, "c1609-r4: " + kind + " at " + relative.Replace("\n", "\\n") + " refuses; " + VolumeRetirementFixture.Truncate(text));
            text.ShouldContain("reason=state-credential");
            text.ShouldNotContain(VolumeRetirementFixture.ReadyMarker);
            variant.Removed.ShouldBeEmpty();
        }

        // The accepted shape is bound into the preview inventory: the same set applies,
        // a session key added after the Ready preview invalidates it with zero removes.
        using var applied = new C1008HostFixture(main: false);
        var appliedKey = Path.Combine(VolumeRetirementFixture.StateMount(applied), "claude", "sessions", "4151." + SessionHash + ".key");
        Directory.CreateDirectory(Path.GetDirectoryName(appliedKey)!);
        await File.WriteAllTextAsync(appliedKey, new string('x', 111));
        await VolumeRetirementFixture.ReadyPreview(applied);
        var deleted = await applied.Run("retire-temp-runner");
        deleted.Exit.ShouldBe(0, "c1609-r4: the previewed session-key set applies; " + VolumeRetirementFixture.Truncate(deleted.Output));
        applied.Removed.ShouldContain("antiphon-runner-temp_runner-state");

        using var drifted = new C1008HostFixture(main: false);
        var driftSessions = Path.Combine(VolumeRetirementFixture.StateMount(drifted), "claude", "sessions");
        Directory.CreateDirectory(driftSessions);
        await File.WriteAllTextAsync(Path.Combine(driftSessions, "4151." + SessionHash + ".key"), new string('x', 111));
        await VolumeRetirementFixture.ReadyPreview(drifted);
        await File.WriteAllTextAsync(Path.Combine(driftSessions, "4152." + SessionHash + ".key"), new string('x', 111));
        var stale = await drifted.Run("retire-temp-runner");
        stale.Exit.ShouldBe(2, "c1609-r4: a changed session-key set invalidates the Ready preview; " + VolumeRetirementFixture.Truncate(stale.Output));
        stale.Output.ShouldContain("reason=preview-required");
        drifted.Removed.ShouldBeEmpty();
    }

    private static readonly string InnerVolume = string.Concat(Enumerable.Repeat("ded26e61", 8));

    // An inner dockerd's own data-root bookkeeping. Accepted only with no inner user
    // data: containers/ empty, volumes/ holding docker's files and empty anonymous
    // volumes, engine-id a 36-byte file. Everything else stays dind-unknown.
    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public async Task Pristine_inner_docker_layout_is_disposable_and_bound_into_the_inventory()
    {
        NativeLinuxGate.Require(NativeLinuxReason);
        using var fixture = new C1008HostFixture(main: false);
        var dind = VolumeRetirementFixture.DindMount(fixture);
        await PlantPristineDind(dind);
        var ok = await VolumeRetirementFixture.Preview(fixture);
        var okText = ok.Output + await VolumeRetirementFixture.AuditText(fixture);
        ok.Exit.ShouldBe(0, "c1609-r4: a pristine inner docker layout is disposable; " + VolumeRetirementFixture.Truncate(okText));
        okText.ShouldContain(VolumeRetirementFixture.ReadyMarker);
        okText.ShouldContain("disposable-fixture classifier=dind-engine-id-v1 path=engine-id size=36");
        okText.ShouldContain("disposable-fixture classifier=dind-containers-empty-v1 path=containers");
        okText.ShouldContain("disposable-fixture classifier=dind-volumes-v1 path=volumes/metadata.db type=f");
        okText.ShouldContain("disposable-fixture classifier=dind-volumes-v1 path=volumes/backingFsBlockDev type=f");
        okText.ShouldContain("disposable-fixture classifier=dind-volumes-v1 path=volumes/" + InnerVolume + " type=d");
        okText.ShouldContain("disposable-fixture classifier=dind-volumes-v1 path=volumes/" + InnerVolume + "/_data type=d");
        okText.ShouldNotContain("reason=dind-unknown");
        fixture.Removed.ShouldBeEmpty();

        var upper = InnerVolume.ToUpperInvariant();
        var variants = new (string Name, string Script)[]
        {
            ("engine-id-35", "printf '%035d' 0 > engine-id"),
            ("engine-id-37", "printf '%037d' 0 > engine-id"),
            ("engine-id-symlink", "mv engine-id tmp/real-id && ln -s tmp/real-id engine-id"),
            ("engine-id-dir", "rm -f engine-id && mkdir engine-id"),
            ("containers-entry", "mkdir containers/" + InnerVolume),
            ("containers-file", "printf x > containers/config.v2.json"),
            ("containers-symlink", "rmdir containers && ln -s tmp containers"),
            ("containers-file-type", "rmdir containers && printf x > containers"),
            ("volume-data", "printf 'row\\n' > volumes/" + InnerVolume + "/_data/PG_VERSION"),
            ("volume-data-dir", "mkdir volumes/" + InnerVolume + "/_data/base"),
            ("volume-opts", "printf '{}' > volumes/" + InnerVolume + "/opts.json"),
            ("volume-data-symlink", "rmdir volumes/" + InnerVolume + "/_data && ln -s /tmp volumes/" + InnerVolume + "/_data"),
            ("volume-data-file", "rmdir volumes/" + InnerVolume + "/_data && printf x > volumes/" + InnerVolume + "/_data"),
            ("volume-upper", "mkdir -p volumes/" + upper + "/_data"),
            ("volume-short", "mkdir -p volumes/" + InnerVolume[..63] + "/_data"),
            ("volume-named", "mkdir -p volumes/pgdata/_data"),
            ("volume-newline", "mkdir -p \"volumes/" + InnerVolume + "\n\"/_data"),
            ("volume-symlink", "ln -s " + InnerVolume + " volumes/" + InnerVolume[..63] + "f"),
            ("volume-file", "printf x > volumes/" + InnerVolume[..63] + "f"),
            ("volume-other-file", "printf x > volumes/notes.txt"),
            ("backing-data", "printf x > volumes/backingFsBlockDev"),
            ("backing-symlink", "rm -f volumes/backingFsBlockDev && ln -s metadata.db volumes/backingFsBlockDev"),
            ("metadata-symlink", "mv volumes/metadata.db tmp/real.db && ln -s ../tmp/real.db volumes/metadata.db"),
            ("metadata-dir", "rm -f volumes/metadata.db && mkdir volumes/metadata.db"),
            ("volumes-symlink", "mv volumes tmp/real-volumes && ln -s tmp/real-volumes volumes"),
        };
        foreach (var (name, script) in variants)
        {
            using var variant = new C1008HostFixture(main: false);
            var root = VolumeRetirementFixture.DindMount(variant);
            await PlantPristineDind(root);
            var changed = await VolumeRetirementFixture.Bash("set -euo pipefail\ncd " + VolumeRetirementFixture.Quote(root) + "\n" + script);
            changed.Exit.ShouldBe(0, "c1609-r4: plant " + name + "; " + changed.Output);
            var refused = await VolumeRetirementFixture.Preview(variant);
            var text = refused.Output + await VolumeRetirementFixture.AuditText(variant);
            refused.Exit.ShouldBe(2, "c1609-r4: " + name + " refuses; " + VolumeRetirementFixture.Truncate(text));
            text.ShouldContain("reason=dind-unknown");
            text.ShouldNotContain(VolumeRetirementFixture.ReadyMarker);
            variant.Removed.ShouldBeEmpty();
        }

        // A second anonymous volume after the Ready preview changes the bound inventory.
        using var drifted = new C1008HostFixture(main: false);
        var driftRoot = VolumeRetirementFixture.DindMount(drifted);
        await PlantPristineDind(driftRoot);
        await VolumeRetirementFixture.ReadyPreview(drifted);
        Directory.CreateDirectory(Path.Combine(driftRoot, "volumes", string.Concat(Enumerable.Repeat("abcdef01", 8)), "_data"));
        var stale = await drifted.Run("retire-temp-runner");
        stale.Exit.ShouldBe(2, "c1609-r4: a changed inner volume set invalidates the Ready preview; " + VolumeRetirementFixture.Truncate(stale.Output));
        stale.Output.ShouldContain("reason=preview-required");
        drifted.Removed.ShouldBeEmpty();
    }

    private static async Task PlantPristineDind(string root)
    {
        var planted = await VolumeRetirementFixture.Bash($$"""
            set -euo pipefail
            cd {{VolumeRetirementFixture.Quote(root)}}
            mkdir -p containers overlay2 image buildkit network plugins runtimes swarm tmp volumes/{{InnerVolume}}/_data
            printf '6f1c2d3e-0000-4000-8000-0123456789ab' > engine-id
            : > volumes/backingFsBlockDev
            head -c 32768 /dev/zero > volumes/metadata.db
            """);
        planted.Exit.ShouldBe(0, "c1609-r4: plant pristine dind; " + planted.Output);
    }

    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public async Task Private_origin_advanced_head_uses_real_host_proof()
    {
        NativeLinuxGate.Require(NativeLinuxReason);
        // A refusal still has to drain its producer. An early diagnostic reader
        // must not turn a large, classified audit failure into SIGPIPE/exit 1.
        foreach (var hostCase in new[] { "retire-temp-runner", "retire-temp-inventory" })
        {
            using var diagnostic = new C1008HostFixture(main: false);
            var refused = await diagnostic.Run(hostCase, """
                c1008_audit_readonly() {
                    printf 'audit check=stream-fault status=2 repo=.\n'
                    printf '%1048576s\n' '' | tr ' ' x
                    return 2
                }
                """, dryRun: hostCase == "retire-temp-runner");
            refused.Exit.ShouldBe(2, "H7 large audit refusal is typed, not a broken pipe; " + refused.Output);
            refused.Output.ShouldContain("RecycleGitAuditUnknown audit check=stream-fault status=2 repo=.");
            refused.Output.ShouldNotContain("UnhandledExit");
            refused.Output.ShouldNotContain(VolumeRetirementFixture.ReadyMarker);
            diagnostic.Removed.ShouldBeEmpty();
        }
        var root = Path.Combine("/var/tmp", "c1008-host-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        using var fixture = new C1008HostFixture(main: false, root);
        var seeded = await VolumeRetirementFixture.SeedAdvancedOrigin(fixture);
        await File.WriteAllTextAsync(Path.Combine(root, "helper-auth-128"), "1\n");
        await VolumeRetirementFixture.RetainPreviewImage(fixture);
        var bound = "C1008_CLEANUP_OPERATION=" + VolumeRetirementFixture.PreviewCleanup;

        var probe = "\n" + VolumeRetirementFixture.ShellProbe;
        var inventory = await fixture.Run("retire-temp-inventory",
            "C1008_CLEANUP_OPERATION=" + VolumeRetirementFixture.AppliedCleanup + probe);
        inventory.Exit.ShouldBe(0, "c1609-h7: inventory lists the private origin; " + inventory.Output);
        inventory.Output.ShouldContain("C1008_PRIVATE_ORIGIN repo=repos/school-revision origin=" + VolumeRetirementFixture.OriginUrl);
        var helperTrace = Path.Combine(root, "helper-git-trace");
        File.Exists(helperTrace).ShouldBeFalse("c1609-h7: offline inventory never invokes a remote from the helper");

        var denyEvidence = Path.Combine(root, "deny-evidence");
        var denied = await VolumeRetirementFixture.InvokeHostProof(
            denyEvidence, Path.Combine(root, "evidence", "retire-temp-inventory"), seeded.Bare,
            Path.Combine(root, "deny-remote"), deny: true);
        denied.Exit.ShouldBe(2, "c1609-h7: a host read of 128 refuses; " + denied.Output);
        var deniedReceipt = Path.Combine(denyEvidence, "c590-result.json");
        var deniedText = denied.Output + (File.Exists(deniedReceipt) ? await File.ReadAllTextAsync(deniedReceipt) : "");
        deniedText.ShouldContain("host-ls-remote", Case.Sensitive,
            "c1609-h7: the real writer names the host 128; " + VolumeRetirementFixture.Truncate(deniedText));
        var deniedPreview = await fixture.Run("retire-temp-runner", bound + probe, dryRun: true);
        deniedPreview.Exit.ShouldBe(2, "c1609-h7: no host proof refuses; " + deniedPreview.Output);
        deniedPreview.Output.ShouldContain("RecycleGitAuditUnknown");
        deniedPreview.Output.ShouldNotContain(VolumeRetirementFixture.ReadyMarker);

        var evidence = Path.Combine(root, "host-evidence");
        var proved = await VolumeRetirementFixture.InvokeHostProof(
            evidence, Path.Combine(root, "evidence", "retire-temp-inventory"), seeded.Bare,
            Path.Combine(root, "remote-root"), deny: false);
        proved.Exit.ShouldBe(0, "c1609-h7: the real host writer reads B; " +
            VolumeRetirementFixture.Truncate(proved.Output));
        proved.Output.ShouldContain("export C1008_HOST_PROOF=");
        var proofText = string.Join('\n', Directory.EnumerateFiles(evidence, "*", SearchOption.AllDirectories).Select(File.ReadAllText));
        proofText.ShouldContain(seeded.B + "\trefs/heads/main");
        (await File.ReadAllTextAsync(Path.Combine(evidence, "host-git-trace"))).ShouldContain("ls-remote");
        (await File.ReadAllTextAsync(Path.Combine(evidence, "host-git-trace"))).ShouldContain("fetch");
        var localProofDir = Directory.EnumerateDirectories(evidence).Single(path => Path.GetFileName(path).StartsWith("c1008-proof-", StringComparison.Ordinal));
        var proofDir = Path.Combine(root,"remote-root","home","mc","antiphon-c590",Path.GetFileName(localProofDir));
        var proofFile = Directory.GetFiles(proofDir).Single(path => System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(path), "^[0-9a-f]{64}$"));
        var headers = File.ReadAllLines(proofFile);
        headers[0].ShouldBe("c1008-host-proof 2", "H7 actual bridge produces current v2 authority");
        var v2Bindings = "\nexport C1609_PROOF_NONCE=" + headers[5]["nonce ".Length..] +
            "\nexport C1609_PROOF_INVENTORY=" + headers[6]["inventory ".Length..] + "\n";

        File.Delete(Path.Combine(root, "helper-auth-128"));
        var child = await VolumeRetirementFixture.Bash(
            "set -euo pipefail\n" +
            "git -C " + VolumeRetirementFixture.Quote(seeded.Repo) + " commit -q --allow-empty -m unpublished\n");
        child.Exit.ShouldBe(0, "c1609-h7: unpublished child; " + child.Output);
        var unpublished = await fixture.Run("retire-temp-runner", bound + "\nexport C1008_HOST_PROOF=" +
            VolumeRetirementFixture.Quote(proofDir) + "\nexport C1008_OPERATION_STARTED=1700000000\n" + v2Bindings + probe, dryRun: true);
        unpublished.Exit.ShouldBe(2, "c1609-h7: an unpublished child refuses; " + unpublished.Output);
        unpublished.Output.ShouldContain("RecycleUnpublishedWork", Case.Sensitive, "H7 explicit host graph must classify the unpublished child; " + unpublished.Output);
        unpublished.Output.ShouldNotContain(VolumeRetirementFixture.ReadyMarker);
        var restore = await VolumeRetirementFixture.Bash(
            "set -euo pipefail\n" +
            "git -C " + VolumeRetirementFixture.Quote(seeded.Repo) + " reset -q --hard " + VolumeRetirementFixture.Quote(seeded.A) + "\n" +
            "if [ -d " + VolumeRetirementFixture.Quote(Path.Combine(seeded.Repo, ".git", "logs")) + " ]; then find " +
            VolumeRetirementFixture.Quote(Path.Combine(seeded.Repo, ".git", "logs")) + " -type f -delete; fi\n" +
            "rm -f -- " + VolumeRetirementFixture.Quote(Path.Combine(seeded.Repo, ".git", "ORIG_HEAD")) + "\n" +
            "git -C " + VolumeRetirementFixture.Quote(seeded.Repo) + " remote set-url origin " + VolumeRetirementFixture.Quote(VolumeRetirementFixture.OriginUrl) + "\n");
        restore.Exit.ShouldBe(0, "c1609-h7: restore A; " + restore.Output);
        await File.WriteAllTextAsync(Path.Combine(root, "helper-auth-128"), "1\n");
        if (File.Exists(helperTrace)) File.Delete(helperTrace);

        var preview = await fixture.Run("retire-temp-runner",
            bound + "\nexport C1008_HOST_PROOF=" + VolumeRetirementFixture.Quote(proofDir) +
            "\nexport C1008_OPERATION_STARTED=1700000000\n" + v2Bindings + probe, dryRun: true);
        File.Exists(helperTrace).ShouldBeFalse("c1609-h7: admission consumes host ancestry without a helper remote lookup");
        fixture.Removed.ShouldBeEmpty();
        var admitted = preview.Exit == 0 &&
            preview.Output.Contains(VolumeRetirementFixture.ReadyMarker, StringComparison.Ordinal);
        admitted.ShouldBeTrue(
            "host ancestry of A from advertised B must admit a Ready preview with zero removes; " +
            "base refused origin-present. exit=" + preview.Exit + " out=" +
            VolumeRetirementFixture.Truncate(preview.Output));
        var unchanged = await VolumeRetirementFixture.Bash(
            "git -C " + VolumeRetirementFixture.Quote(seeded.Repo) + " cat-file -e " +
            VolumeRetirementFixture.Quote(seeded.B + "^{commit}") + " 2>/dev/null");
        unchanged.Exit.ShouldNotBe(0, "H7 host bundle must not populate the target volume with B");
        var originalProof = await File.ReadAllTextAsync(proofFile);
        var originalBundle = await File.ReadAllBytesAsync(proofFile+".bundle");
        var readyFile = Path.Combine(root,"server/retirement",VolumeRetirementFixture.Operation,"receipt.json");
        var readyBytes = await File.ReadAllBytesAsync(readyFile);
        foreach (var vector in new[] {"nonce","inventory","coverage","expired","future","duplicate","bundle"})
        {
            var changed = File.ReadAllLines(proofFile);
            var binding = v2Bindings;
            switch(vector)
            {
                case "nonce": changed[5]="nonce "+new string('0',32); break;
                case "inventory": changed[6]="inventory "+new string('0',64); break;
                case "coverage":
                    changed[6]="inventory "+new string('0',64);
                    binding+="export C1609_PROOF_INVENTORY="+new string('0',64)+"\n";
                    break;
                case "expired":
                    changed[4]="created "+DateTimeOffset.UtcNow.AddSeconds(-1000).ToUnixTimeSeconds();
                    changed[10]="completed "+DateTimeOffset.UtcNow.AddSeconds(-901).ToUnixTimeSeconds(); break;
                case "future":
                    changed[4]="created "+DateTimeOffset.UtcNow.AddSeconds(121).ToUnixTimeSeconds();
                    changed[10]="completed "+DateTimeOffset.UtcNow.AddSeconds(122).ToUnixTimeSeconds(); break;
                case "duplicate": changed=changed.Concat(new[]{changed[11]}).ToArray(); break;
                case "bundle":
                    var damaged=originalBundle.ToArray(); damaged[^1]^=1;
                    await File.WriteAllBytesAsync(proofFile+".bundle",damaged);
                    changed[7]="bundle "+Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(damaged)).ToLowerInvariant();
                    break;
            }
            await File.WriteAllTextAsync(proofFile,string.Join('\n',changed)+"\n");
            var rejected = await fixture.Run("retire-temp-runner",bound+"\nexport C1008_HOST_PROOF="+
                VolumeRetirementFixture.Quote(proofDir)+"\nexport C1008_OPERATION_STARTED=1700000000\n"+binding+probe,dryRun:true);
            rejected.Exit.ShouldBe(2,"H7 v2 "+vector+" must refuse: "+rejected.Output);
            rejected.Output.ShouldContain("RecycleGitAuditUnknown");
            rejected.Output.ShouldNotContain(VolumeRetirementFixture.ReadyMarker);
            (await File.ReadAllBytesAsync(readyFile)).ShouldBe(readyBytes,"H7 refused proof cannot replace or consume Ready");
            fixture.Removed.ShouldBeEmpty();
            await File.WriteAllTextAsync(proofFile,originalProof);
            await File.WriteAllBytesAsync(proofFile+".bundle",originalBundle);
        }
    }

    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public async Task Every_ref_recovery_tip_and_tag_object_requires_publication()
    {
        NativeLinuxGate.Require(NativeLinuxReason);
        using var fixture = new C1008HostFixture(main: false);
        var (_, outer) = await VolumeRetirementFixture.SeedPublishedOuter(fixture);
        var planted = await VolumeRetirementFixture.Bash(
            "set -euo pipefail\n" +
            "git -C " + VolumeRetirementFixture.Quote(outer) + " checkout -q -b private\n" +
            "printf 'secret\\n' > " + VolumeRetirementFixture.Quote(outer) + "/secret.txt\n" +
            "git -C " + VolumeRetirementFixture.Quote(outer) + " add secret.txt\n" +
            "git -C " + VolumeRetirementFixture.Quote(outer) + " -c commit.gpgsign=false commit -qm private\n" +
            "git -C " + VolumeRetirementFixture.Quote(outer) + " checkout -q main\n");
        planted.Exit.ShouldBe(0, "c1609-c3: private branch; " + planted.Output);
        var refused = await VolumeRetirementFixture.Preview(fixture);
        var refusedText = refused.Output + await VolumeRetirementFixture.AuditText(fixture);
        refused.Exit.ShouldBe(2, "c1609-c3: private branch; " + VolumeRetirementFixture.Truncate(refusedText));
        refusedText.ShouldContain("RecycleUnpublishedWork");
        refusedText.ShouldNotContain(VolumeRetirementFixture.ReadyMarker);
        fixture.Removed.ShouldBeEmpty();

        var restore = await VolumeRetirementFixture.Bash(
            "git -C " + VolumeRetirementFixture.Quote(outer) + " branch -D private");
        restore.Exit.ShouldBe(0, "c1609-c3: drop private; " + restore.Output);
        var tagged = await VolumeRetirementFixture.Bash(
            "set -euo pipefail\n" +
            "git -C " + VolumeRetirementFixture.Quote(outer) +
            " -c user.name=Fixture -c user.email=fixture@example.invalid tag -a public-peeled -m 'tag body stays local'\n");
        tagged.Exit.ShouldBe(0, "c1609-c3: annotated tag; " + tagged.Output);
        var tagRun = await VolumeRetirementFixture.Preview(fixture);
        var tagText = tagRun.Output + await VolumeRetirementFixture.AuditText(fixture);
        tagRun.Exit.ShouldBe(2, "c1609-c3: unadvertised tag object; " + VolumeRetirementFixture.Truncate(tagText));
        tagText.ShouldContain("RecycleUnpublishedWork");
        tagText.ShouldNotContain(VolumeRetirementFixture.ReadyMarker);
        fixture.Removed.ShouldBeEmpty();

        var dropTag = await VolumeRetirementFixture.Bash(
            "git -C " + VolumeRetirementFixture.Quote(outer) + " tag -d public-peeled");
        dropTag.Exit.ShouldBe(0, "c1609-c3: drop tag; " + dropTag.Output);
        var reflogOnly = await VolumeRetirementFixture.Preview(fixture);
        var reflogText = reflogOnly.Output + await VolumeRetirementFixture.AuditText(fixture);
        reflogOnly.Exit.ShouldBe(2, "c1609-c3: HEAD reflog still names the private commit; " + VolumeRetirementFixture.Truncate(reflogText));
        reflogText.ShouldContain("RecycleUnpublishedWork");
        reflogText.ShouldNotContain(VolumeRetirementFixture.ReadyMarker);
        fixture.Removed.ShouldBeEmpty();
        var expire = await VolumeRetirementFixture.Bash(
            "git -C " + VolumeRetirementFixture.Quote(outer) + " reflog expire --expire=now --all");
        expire.Exit.ShouldBe(0, "c1609-c3: expire reflog; " + expire.Output);
        var admitted = await VolumeRetirementFixture.Preview(fixture);
        var admittedText = admitted.Output + await VolumeRetirementFixture.AuditText(fixture);
        admitted.Exit.ShouldBe(0, "c1609-c3: published main; " + VolumeRetirementFixture.Truncate(admittedText));
        admittedText.ShouldContain(VolumeRetirementFixture.ReadyMarker);
        fixture.Removed.ShouldBeEmpty();
    }

    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public async Task Worktree_content_and_admin_closure_are_complete()
    {
        NativeLinuxGate.Require(NativeLinuxReason);
        using var fixture = new C1008HostFixture(main: false);
        var (_, outer) = await VolumeRetirementFixture.SeedPublishedOuter(fixture);
        await File.WriteAllTextAsync(Path.Combine(outer, "dirty.txt"), "dirty\n");
        var dirty = await VolumeRetirementFixture.Preview(fixture);
        var dirtyText = dirty.Output + await VolumeRetirementFixture.AuditText(fixture);
        dirty.Exit.ShouldBe(2, "c1609-c4: untracked; " + VolumeRetirementFixture.Truncate(dirtyText));
        dirtyText.ShouldContain("RecycleWorktreeDirty");
        dirtyText.ShouldNotContain(VolumeRetirementFixture.ReadyMarker);
        fixture.Removed.ShouldBeEmpty();
        File.Delete(Path.Combine(outer, "dirty.txt"));

        var staged = await VolumeRetirementFixture.Bash(
            "set -euo pipefail\n" +
            "printf 'staged\\n' > " + VolumeRetirementFixture.Quote(outer) + "/staged.txt\n" +
            "git -C " + VolumeRetirementFixture.Quote(outer) + " add staged.txt\n");
        staged.Exit.ShouldBe(0, "c1609-c4: stage; " + staged.Output);
        var stagedRun = await VolumeRetirementFixture.Preview(fixture);
        var stagedText = stagedRun.Output + await VolumeRetirementFixture.AuditText(fixture);
        stagedRun.Exit.ShouldBe(2, "c1609-c4: staged; " + VolumeRetirementFixture.Truncate(stagedText));
        stagedText.ShouldContain("RecycleWorktreeDirty");
        fixture.Removed.ShouldBeEmpty();
        var unstage = await VolumeRetirementFixture.Bash(
            "git -C " + VolumeRetirementFixture.Quote(outer) + " reset -q && rm -f -- " +
            VolumeRetirementFixture.Quote(Path.Combine(outer, "staged.txt")));
        unstage.Exit.ShouldBe(0, "c1609-c4: unstage; " + unstage.Output);

        var clean = await VolumeRetirementFixture.Preview(fixture);
        (clean.Output + await VolumeRetirementFixture.AuditText(fixture)).ShouldContain(VolumeRetirementFixture.ReadyMarker);
        clean.Exit.ShouldBe(0, "c1609-c4: clean control; " + clean.Output);
        fixture.Removed.ShouldBeEmpty();
    }

    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public async Task Evidence_names_and_filesystem_shapes_fail_closed()
    {
        NativeLinuxGate.Require(NativeLinuxReason);
        using var fixture = new C1008HostFixture(main: false);
        var (_, outer) = await VolumeRetirementFixture.SeedPublishedOuter(fixture);
        var evidence = Path.Combine(VolumeRetirementFixture.WorkMount(fixture), "test-evidence", "nested");
        Directory.CreateDirectory(evidence);
        await File.WriteAllTextAsync(Path.Combine(evidence, "HEAD"), "ref: refs/heads/main\n");
        var refused = await VolumeRetirementFixture.Preview(fixture);
        var text = refused.Output + await VolumeRetirementFixture.AuditText(fixture);
        refused.Exit.ShouldBe(2, "c1609-c5: evidence HEAD; " + VolumeRetirementFixture.Truncate(text));
        text.ShouldContain("reason=evidence-name");
        text.ShouldNotContain(VolumeRetirementFixture.ReadyMarker);
        fixture.Removed.ShouldBeEmpty();

        File.Delete(Path.Combine(evidence, "HEAD"));
        await File.WriteAllTextAsync(Path.Combine(evidence, "notes.txt"), "harmless\n");
        var admitted = await VolumeRetirementFixture.Preview(fixture);
        var admittedText = admitted.Output + await VolumeRetirementFixture.AuditText(fixture);
        admitted.Exit.ShouldBe(0, "c1609-c5: harmless evidence; " + VolumeRetirementFixture.Truncate(admittedText));
        admittedText.ShouldContain(VolumeRetirementFixture.ReadyMarker);
        fixture.Removed.ShouldBeEmpty();
        _ = outer;
    }

    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public async Task All_target_roles_are_classified()
    {
        NativeLinuxGate.Require(NativeLinuxReason);
        using var clean = new C1008HostFixture(main: false);
        await VolumeRetirementFixture.SeedPublishedOuter(clean);
        var ok = await VolumeRetirementFixture.Preview(clean);
        var okText = ok.Output + await VolumeRetirementFixture.AuditText(clean);
        ok.Exit.ShouldBe(0, "c1609-c6: recognized layout; " + VolumeRetirementFixture.Truncate(okText));
        okText.ShouldContain(VolumeRetirementFixture.ReadyMarker);
        clean.Removed.ShouldBeEmpty();

        using var dind = new C1008HostFixture(main: false);
        await VolumeRetirementFixture.SeedPublishedOuter(dind);
        var store = Path.Combine(VolumeRetirementFixture.DindMount(dind), "custom-store");
        Directory.CreateDirectory(store);
        await File.WriteAllTextAsync(Path.Combine(store, "data"), "persistent\n");
        var bad = await VolumeRetirementFixture.Preview(dind);
        var badText = bad.Output + await VolumeRetirementFixture.AuditText(dind);
        bad.Exit.ShouldBe(2, "c1609-c6: unknown dind store; " + VolumeRetirementFixture.Truncate(badText));
        badText.ShouldContain("reason=dind-unknown");
        badText.ShouldNotContain(VolumeRetirementFixture.ReadyMarker);
        dind.Removed.ShouldBeEmpty();
    }

    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public async Task Read_failures_and_incomplete_inventory_refuse()
    {
        NativeLinuxGate.Require(NativeLinuxReason);
        using var fixture = new C1008HostFixture(main: false);
        var (_, outer) = await VolumeRetirementFixture.SeedPublishedOuter(fixture);
        await File.WriteAllTextAsync(Path.Combine(outer, ".git", "packed-refs"), "not a packed-refs file\n");
        var refused = await VolumeRetirementFixture.Preview(fixture);
        var text = refused.Output + await VolumeRetirementFixture.AuditText(fixture);
        refused.Exit.ShouldBe(2, "c1609-c7: truncated packed-refs; " + VolumeRetirementFixture.Truncate(text));
        text.ShouldContain("RecycleGitAuditUnknown");
        text.ShouldNotContain(VolumeRetirementFixture.ReadyMarker);
        fixture.Removed.ShouldBeEmpty();
    }

    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public async Task Audit_identity_and_namespace_are_stable()
    {
        NativeLinuxGate.Require(NativeLinuxReason);
        using var fixture = new C1008HostFixture(main: false);
        await VolumeRetirementFixture.SeedPublishedOuter(fixture);
        var run = await VolumeRetirementFixture.Preview(fixture);
        run.Exit.ShouldBe(0, "c1609-c8: preview; " + run.Output);
        var trace = string.Join('\n', fixture.Trace.Select(row => string.Join(' ', row)));
        trace.ShouldContain("--user");
        trace.ShouldContain("1654:1654");
        trace.ShouldContain("readonly");
        trace.ShouldContain("target=/work");
        trace.ShouldContain("--cap-drop");
        trace.ShouldContain("--network none");
        trace.ShouldContain("--read-only");
        trace.ShouldContain("target=/work,readonly,volume-nocopy");
        trace.ShouldContain("target=/tmp,readonly,volume-nocopy");
        trace.ShouldContain("TMPDIR=/audit-scratch");
        run.Output.ShouldContain(VolumeRetirementFixture.ReadyMarker);
        fixture.Removed.ShouldBeEmpty();
    }

    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public async Task No_content_or_credentials_are_executed_or_exported()
    {
        NativeLinuxGate.Require(NativeLinuxReason);
        using var fixture = new C1008HostFixture(main: false);
        var (_, outer) = await VolumeRetirementFixture.SeedPublishedOuter(fixture);
        var hook = Path.Combine(outer, ".git", "hooks", "post-checkout");
        await File.WriteAllTextAsync(hook, "#!/bin/sh\nprintf 'HOOK-SENTINEL-1609\\n' > " +
            VolumeRetirementFixture.Quote(Path.Combine(fixture.Root, "hook-ran")) + "\n");
        var mode = await VolumeRetirementFixture.Bash("chmod 0755 " + VolumeRetirementFixture.Quote(hook));
        mode.Exit.ShouldBe(0, "c1609-c9: hook mode; " + mode.Output);
        var run = await VolumeRetirementFixture.Preview(fixture);
        var text = run.Output + await VolumeRetirementFixture.AuditText(fixture);
        File.Exists(Path.Combine(fixture.Root, "hook-ran")).ShouldBeFalse("c1609-c9: a repository hook must not run");
        text.ShouldNotContain("HOOK-SENTINEL-1609");
        text.ShouldContain("reason=hook");
        text.ShouldNotContain(VolumeRetirementFixture.ReadyMarker);
        fixture.Removed.ShouldBeEmpty();
    }

    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public async Task Absent_work_does_not_skip_other_volumes()
    {
        NativeLinuxGate.Require(NativeLinuxReason);
        using var fixture = new C1008HostFixture(main: false);
        fixture.Docker["volumes"]!.AsObject().Remove("antiphon-runner-temp_work");
        await File.WriteAllTextAsync(Path.Combine(VolumeRetirementFixture.StateMount(fixture), "auth.json"), "{}\n");
        var refused = await VolumeRetirementFixture.Preview(fixture);
        var text = refused.Output + await VolumeRetirementFixture.AuditText(fixture);
        refused.Exit.ShouldBe(2, "c1609-c10: absent work still scans state; " + VolumeRetirementFixture.Truncate(text));
        text.ShouldContain("reason=state-credential");
        text.ShouldNotContain(VolumeRetirementFixture.ReadyMarker);
        fixture.Removed.ShouldBeEmpty();
        fixture.Docker["volumes"]!.AsObject().ContainsKey("antiphon-runner-temp_work").ShouldBeFalse(
            "c1609-c10: inspecting an absent work volume must not create it");

        using var empty = new C1008HostFixture(main: false);
        foreach (var role in new[] { "work", "runner-tmp", "dind-data", "runner-state" })
            empty.Docker["volumes"]!.AsObject().Remove("antiphon-runner-temp_" + role);
        var noop = await VolumeRetirementFixture.Preview(empty);
        noop.Exit.ShouldBe(0, "c1609-c10: all absent is a no-op; " + noop.Output);
        noop.Output.ShouldContain(VolumeRetirementFixture.ReadyMarker);
        empty.Removed.ShouldBeEmpty();
        foreach (var role in new[] { "work", "runner-tmp", "dind-data", "runner-state" })
            empty.Docker["volumes"]!.AsObject().ContainsKey("antiphon-runner-temp_" + role).ShouldBeFalse();
    }

    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public async Task Preview_lists_every_offender_without_effects()
    {
        NativeLinuxGate.Require(NativeLinuxReason);
        using var fixture = new C1008HostFixture(main: false);
        var (_, outer) = await VolumeRetirementFixture.SeedPublishedOuter(fixture, "work-repo");
        var tmpRepo = Path.Combine(VolumeRetirementFixture.TmpMount(fixture), "scratch-repo");
        var planted = await VolumeRetirementFixture.Bash($$"""
            set -euo pipefail
            git -C {{VolumeRetirementFixture.Quote(outer)}} checkout -q -b retained
            printf 'unpublished-work\n' > {{VolumeRetirementFixture.Quote(outer)}}/kept.txt
            git -C {{VolumeRetirementFixture.Quote(outer)}} add kept.txt
            git -C {{VolumeRetirementFixture.Quote(outer)}} -c commit.gpgsign=false commit -qm unpublished-work
            work_oid=$(git -C {{VolumeRetirementFixture.Quote(outer)}} rev-parse HEAD)
            git -C {{VolumeRetirementFixture.Quote(outer)}} checkout -q main
            printf 'dirty-work\n' > {{VolumeRetirementFixture.Quote(outer)}}/dirty.txt
            git init -q -b main {{VolumeRetirementFixture.Quote(tmpRepo)}}
            git -C {{VolumeRetirementFixture.Quote(tmpRepo)}} config user.name Fixture
            git -C {{VolumeRetirementFixture.Quote(tmpRepo)}} config user.email fixture@example.invalid
            git -C {{VolumeRetirementFixture.Quote(tmpRepo)}} config commit.gpgsign false
            printf 'tmp\n' > {{VolumeRetirementFixture.Quote(tmpRepo)}}/note.txt
            git -C {{VolumeRetirementFixture.Quote(tmpRepo)}} add note.txt
            git -C {{VolumeRetirementFixture.Quote(tmpRepo)}} -c commit.gpgsign=false commit -qm tmp-base
            git -C {{VolumeRetirementFixture.Quote(tmpRepo)}} remote add origin {{VolumeRetirementFixture.Quote(Path.Combine(fixture.Root, "work-repo.git"))}}
            printf 'fetch-private\n' > {{VolumeRetirementFixture.Quote(tmpRepo)}}/private.txt
            git -C {{VolumeRetirementFixture.Quote(tmpRepo)}} add private.txt
            git -C {{VolumeRetirementFixture.Quote(tmpRepo)}} -c commit.gpgsign=false commit -qm fetch-private
            fetch_oid=$(git -C {{VolumeRetirementFixture.Quote(tmpRepo)}} rev-parse HEAD)
            git -C {{VolumeRetirementFixture.Quote(tmpRepo)}} reset -q --hard HEAD~1
            printf '%s\t\tfetch of private\n' "$fetch_oid" > {{VolumeRetirementFixture.Quote(tmpRepo)}}/.git/FETCH_HEAD
            printf 'work=%s\nfetch=%s\n' "$work_oid" "$fetch_oid"
            """);
        planted.Exit.ShouldBe(0, "c1609-c11: plant offenders; " + planted.Output);
        var workOid = planted.Output.Split('\n').Single(line => line.StartsWith("work=", StringComparison.Ordinal))[5..];
        var fetchOid = planted.Output.Split('\n').Single(line => line.StartsWith("fetch=", StringComparison.Ordinal))[6..];
        var evidence = Path.Combine(VolumeRetirementFixture.WorkMount(fixture), "test-evidence");
        Directory.CreateDirectory(evidence);
        await File.WriteAllTextAsync(Path.Combine(evidence, "HEAD"), "ref: refs/heads/main\n");
        await File.WriteAllTextAsync(Path.Combine(VolumeRetirementFixture.StateMount(fixture), "auth.json"), "{}\n");
        var broken = Path.Combine(VolumeRetirementFixture.WorkMount(fixture), "repos", "broken");
        Directory.CreateDirectory(Path.Combine(broken, ".git"));
        await File.WriteAllTextAsync(Path.Combine(broken, ".git", "HEAD"), "not-a-ref\n");

        var run = await VolumeRetirementFixture.Preview(fixture);
        var text = run.Output + await VolumeRetirementFixture.AuditText(fixture);
        run.Exit.ShouldNotBe(0, "c1609-c11: findings refuse; " + VolumeRetirementFixture.Truncate(text));
        text.ShouldContain(workOid);
        text.ShouldContain(fetchOid);
        text.ShouldContain("\"kind\":\"reflog\"");
        text.ShouldContain("\"kind\":\"fetch-head\"");
        text.ShouldContain("\"kind\":\"dirty\"");
        text.ShouldContain("\"kind\":\"evidence\"");
        text.ShouldContain("\"kind\":\"state\"");
        text.ShouldContain("\"complete\":false");
        text.ShouldContain("repos/broken");
        text.ShouldNotContain(VolumeRetirementFixture.ReadyMarker);
        fixture.Removed.ShouldBeEmpty();
    }

    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public async Task Fetch_head_requires_proven_objects_not_fresh_names()
    {
        NativeLinuxGate.Require(NativeLinuxReason);
        using var fixture = new C1008HostFixture(main: false);
        var (bare, outer) = await VolumeRetirementFixture.SeedPublishedOuter(fixture);
        var published = await VolumeRetirementFixture.Bash(
            "set -euo pipefail\n" +
            "oid=$(git -C " + VolumeRetirementFixture.Quote(outer) + " rev-parse HEAD)\n" +
            "blob=$(git -C " + VolumeRetirementFixture.Quote(outer) + " rev-parse HEAD:readme.txt)\n" +
            "printf '%s\\t\\tbranch main of published\\n%s\\tnot-for-merge\\tblob readme\\n' \"$oid\" \"$blob\" > " +
            VolumeRetirementFixture.Quote(Path.Combine(outer, ".git", "FETCH_HEAD")) + "\n" +
            "printf 'oid=%s\nblob=%s\n' \"$oid\" \"$blob\"\n");
        published.Exit.ShouldBe(0, "c1609-c12: published fetch head; " + published.Output);
        var oid = published.Output.Split('\n').Single(line => line.StartsWith("oid=", StringComparison.Ordinal))[4..];
        var blob = published.Output.Split('\n').Single(line => line.StartsWith("blob=", StringComparison.Ordinal))[5..];
        var ok = await VolumeRetirementFixture.Preview(fixture);
        var okText = ok.Output + await VolumeRetirementFixture.AuditText(fixture);
        ok.Exit.ShouldBe(0, "c1609-c12: published entries; " + VolumeRetirementFixture.Truncate(okText));
        okText.ShouldContain("published-fetch-head");
        okText.ShouldContain(oid);
        okText.ShouldContain(blob);
        okText.ShouldContain(VolumeRetirementFixture.ReadyMarker);
        fixture.Removed.ShouldBeEmpty();

        var mixed = await VolumeRetirementFixture.Bash($$"""
            set -euo pipefail
            git -C {{VolumeRetirementFixture.Quote(outer)}} checkout -q -b hidden
            printf 'hidden\n' > {{VolumeRetirementFixture.Quote(outer)}}/hidden.txt
            git -C {{VolumeRetirementFixture.Quote(outer)}} add hidden.txt
            git -C {{VolumeRetirementFixture.Quote(outer)}} -c commit.gpgsign=false commit -qm hidden
            hidden=$(git -C {{VolumeRetirementFixture.Quote(outer)}} rev-parse HEAD)
            git -C {{VolumeRetirementFixture.Quote(outer)}} checkout -q main
            git -C {{VolumeRetirementFixture.Quote(outer)}} branch -D hidden
            printf '%s\t\tbranch main of published\n%s\tnot-for-merge\thidden commit\n' {{VolumeRetirementFixture.Quote(oid)}} "$hidden" > {{VolumeRetirementFixture.Quote(Path.Combine(outer, ".git", "FETCH_HEAD"))}}
            printf 'hidden=%s\n' "$hidden"
            """);
        mixed.Exit.ShouldBe(0, "c1609-c12: mixed fetch head; " + mixed.Output);
        var hidden = mixed.Output.Split('\n').Single(line => line.StartsWith("hidden=", StringComparison.Ordinal))[7..];
        var mixedRun = await VolumeRetirementFixture.Preview(fixture);
        var mixedText = mixedRun.Output + await VolumeRetirementFixture.AuditText(fixture);
        mixedRun.Exit.ShouldBe(2, "c1609-c12: mixed entries; " + VolumeRetirementFixture.Truncate(mixedText));
        mixedText.ShouldContain(oid);
        mixedText.ShouldContain(hidden);
        mixedText.ShouldContain("published-fetch-head");
        mixedText.ShouldContain("unpublished");
        mixedText.ShouldNotContain(VolumeRetirementFixture.ReadyMarker);
        fixture.Removed.ShouldBeEmpty();

        var reset = await VolumeRetirementFixture.Bash(
            "set -euo pipefail\n" +
            "git -C " + VolumeRetirementFixture.Quote(outer) + " reset -q --hard origin/main\n" +
            "rm -rf -- " + VolumeRetirementFixture.Quote(Path.Combine(outer, ".git", "logs")) + "\n" +
            "git -C " + VolumeRetirementFixture.Quote(outer) + " checkout -q main\n");
        reset.Exit.ShouldBe(0, "c1609-c12: reset; " + reset.Output);
        await File.WriteAllTextAsync(Path.Combine(outer, ".git", "FETCH_HEAD"), "not-an-object\t\tbad\n");
        var bad = await VolumeRetirementFixture.Preview(fixture);
        var badText = bad.Output + await VolumeRetirementFixture.AuditText(fixture);
        bad.Exit.ShouldBe(2, "c1609-c12: malformed; " + VolumeRetirementFixture.Truncate(badText));
        badText.ShouldContain("reason=fetch-head-malformed");
        badText.ShouldNotContain(VolumeRetirementFixture.ReadyMarker);
        fixture.Removed.ShouldBeEmpty();
        _ = bare;
    }

    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public async Task Only_exact_known_seed_fixtures_are_disposable()
    {
        NativeLinuxGate.Require(NativeLinuxReason);
        using var fixture = new C1008HostFixture(main: false);
        var (_, outer) = await VolumeRetirementFixture.SeedPublishedOuter(fixture);
        var template = Path.Combine(outer, ".antiphon", "test-output", "card-1605", "template");
        var exact = await SeedTemplate(template, "exact");
        exact.Exit.ShouldBe(0, "c1609-c13: exact seed; " + exact.Output);
        var ok = await VolumeRetirementFixture.Preview(fixture);
        var okText = ok.Output + await VolumeRetirementFixture.AuditText(fixture);
        ok.Exit.ShouldBe(0, "c1609-c13: exact seed is disposable; " + VolumeRetirementFixture.Truncate(okText));
        okText.ShouldContain("disposable-fixture classifier=v1");
        okText.ShouldContain(VolumeRetirementFixture.ReadyMarker);
        fixture.Removed.ShouldBeEmpty();

        foreach (var kind in new[] { "gpgsig", "encoding", "custom" })
        {
            var replaced = await SeedTemplate(template, kind);
            replaced.Exit.ShouldBe(0, "c1609-c13: " + kind + "; " + replaced.Output);
            var refused = await VolumeRetirementFixture.Preview(fixture);
            var text = refused.Output + await VolumeRetirementFixture.AuditText(fixture);
            refused.Exit.ShouldBe(2, "c1609-c13: " + kind + " header refuses; " + VolumeRetirementFixture.Truncate(text));
            text.ShouldContain("reason=seed-bytes");
            text.ShouldNotContain(VolumeRetirementFixture.ReadyMarker);
            text.ShouldNotContain("HEADER-SENTINEL-1609");
            fixture.Removed.ShouldBeEmpty();
        }

        var wrongPath = Path.Combine(outer, "cases", "other", "main");
        var moved = await VolumeRetirementFixture.Bash(
            "rm -rf -- " + VolumeRetirementFixture.Quote(template) + "\n");
        moved.Exit.ShouldBe(0, "c1609-c13: remove template; " + moved.Output);
        var offPath = await SeedTemplate(wrongPath, "exact");
        offPath.Exit.ShouldBe(0, "c1609-c13: off-path seed; " + offPath.Output);
        var off = await VolumeRetirementFixture.Preview(fixture);
        var offText = off.Output + await VolumeRetirementFixture.AuditText(fixture);
        off.Exit.ShouldBe(2, "c1609-c13: path is not disposable; " + VolumeRetirementFixture.Truncate(offText));
        offText.ShouldContain("reason=no-remote-unclassified");
        offText.ShouldNotContain(VolumeRetirementFixture.ReadyMarker);
        fixture.Removed.ShouldBeEmpty();
    }

    // Claude Code keeps a bash-edit-diff snapshot Git directory per Bash call under
    // /tmp/claude-<uid>/bash-edit-diff/<n>-<n>-<hex16>: one synthetic parentless
    // "snapshot" commit on refs/heads/main, objects borrowed through alternates, no
    // logs, hooks or other refs. Only that exact shape in the tmp volume is
    // disposable tool scratch; every variant keeps the evidence-name refusal.
    // The dirty/reflog tmp scratch repo and the no-index seed-shaped work clone are
    // listed but do not block (the retire-temp r3 preview's remaining classes).
    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public async Task Claude_bash_edit_diff_snapshots_are_disposable_tool_scratch()
    {
        NativeLinuxGate.Require(NativeLinuxReason);
        using var fixture = new C1008HostFixture(main: false);
        var (_, outer) = await VolumeRetirementFixture.SeedPublishedOuter(fixture);
        var (_, seeded) = await VolumeRetirementFixture.SeedPublishedOuter(fixture, "seeded");
        var tmp = VolumeRetirementFixture.TmpMount(fixture);
        var snapshots = Path.Combine(tmp, "claude-1654", "bash-edit-diff");
        var scratch = Path.Combine(tmp, "claude-1654", "-work-task", "scratchpad", "repository with spaces");
        var planted = await VolumeRetirementFixture.Bash($$"""
            set -euo pipefail
            {{SnapshotFunction}}
            snapshot {{VolumeRetirementFixture.Quote(Path.Combine(snapshots, "10132844646867334109-4770532842197759895-8745e6346e434d17"))}} exact
            snapshot {{VolumeRetirementFixture.Quote(Path.Combine(snapshots, "1041258303595891311-12499127780866753949-ef910dbe4c97b2e6"))}} exact
            mkdir -p {{VolumeRetirementFixture.Quote(Path.Combine(snapshots, "10132844646867334109-9543953992008861987-7382766fd60483a6"))}}
            git init -q -b main {{VolumeRetirementFixture.Quote(scratch)}}
            git -C {{VolumeRetirementFixture.Quote(scratch)}} config user.name Fixture
            git -C {{VolumeRetirementFixture.Quote(scratch)}} config user.email fixture@example.invalid
            printf 'tmp\n' > {{VolumeRetirementFixture.Quote(scratch)}}/note.txt
            git -C {{VolumeRetirementFixture.Quote(scratch)}} add note.txt
            git -C {{VolumeRetirementFixture.Quote(scratch)}} -c commit.gpgsign=false commit -qm scratch
            printf 'edited\n' > {{VolumeRetirementFixture.Quote(scratch)}}/note.txt
            rm -f -- {{VolumeRetirementFixture.Quote(Path.Combine(seeded, ".git", "index"))}} {{VolumeRetirementFixture.Quote(Path.Combine(seeded, ".gitignore"))}} {{VolumeRetirementFixture.Quote(Path.Combine(seeded, "readme.txt"))}}
            """);
        planted.Exit.ShouldBe(0, "c1609-r3: plant snapshots; " + planted.Output);
        var ok = await VolumeRetirementFixture.Preview(fixture);
        var okText = ok.Output + await VolumeRetirementFixture.AuditText(fixture);
        ok.Exit.ShouldBe(0, "c1609-r3: exact snapshots are tool scratch; " + VolumeRetirementFixture.Truncate(okText));
        okText.ShouldContain(VolumeRetirementFixture.ReadyMarker);
        okText.ShouldContain("disposable-fixture classifier=bash-edit-diff-v1 path=/tmp/claude-1654/bash-edit-diff/10132844646867334109-4770532842197759895-8745e6346e434d17 commit=");
        okText.ShouldContain("disposable-fixture classifier=bash-edit-diff-v1 path=/tmp/claude-1654/bash-edit-diff/1041258303595891311-12499127780866753949-ef910dbe4c97b2e6 commit=");
        okText.ShouldNotContain("reason=evidence-name");
        okText.ShouldContain("\"kind\":\"dirty\"");
        okText.ShouldContain("\"kind\":\"reflog\"");
        okText.ShouldContain("repos/seeded");
        fixture.Removed.ShouldBeEmpty();

        var variants = new (string Kind, string Path)[]
        {
            ("extra-ref", Path.Combine(snapshots, "1-2-0123456789abcdef")),
            ("stash", Path.Combine(snapshots, "1-2-0123456789abcdef")),
            ("foreign-commit", Path.Combine(snapshots, "1-2-0123456789abcdef")),
            ("parent", Path.Combine(snapshots, "1-2-0123456789abcdef")),
            ("logs", Path.Combine(snapshots, "1-2-0123456789abcdef")),
            ("packed-refs", Path.Combine(snapshots, "1-2-0123456789abcdef")),
            ("hooks", Path.Combine(snapshots, "1-2-0123456789abcdef")),
            ("remote", Path.Combine(snapshots, "1-2-0123456789abcdef")),
            ("detached", Path.Combine(snapshots, "1-2-0123456789abcdef")),
            ("exact", Path.Combine(snapshots, "1-2-0123456789abcdef", "nested")),
            ("exact", Path.Combine(snapshots, "1-2-0123456789ABCDEF")),
            ("exact", Path.Combine(tmp, "claude-1654", "other-tool", "1-2-0123456789abcdef")),
            ("exact", Path.Combine(tmp, "bash-edit-diff", "1-2-0123456789abcdef")),
        };
        foreach (var (kind, path) in variants)
        {
            var variant = await VolumeRetirementFixture.Bash($$"""
                set -euo pipefail
                rm -rf -- {{VolumeRetirementFixture.Quote(Path.Combine(snapshots, "1-2-0123456789abcdef"))}} {{VolumeRetirementFixture.Quote(Path.Combine(snapshots, "1-2-0123456789ABCDEF"))}} {{VolumeRetirementFixture.Quote(Path.Combine(tmp, "claude-1654", "other-tool"))}} {{VolumeRetirementFixture.Quote(Path.Combine(tmp, "bash-edit-diff"))}}
                {{SnapshotFunction}}
                snapshot {{VolumeRetirementFixture.Quote(path)}} {{kind}}
                """);
            variant.Exit.ShouldBe(0, "c1609-r3: plant " + kind + " at " + path + "; " + variant.Output);
            var refused = await VolumeRetirementFixture.Preview(fixture);
            var text = refused.Output + await VolumeRetirementFixture.AuditText(fixture);
            refused.Exit.ShouldBe(2, "c1609-r3: " + kind + " at " + path + " refuses; " + VolumeRetirementFixture.Truncate(text));
            text.ShouldContain("reason=evidence-name");
            text.ShouldNotContain(VolumeRetirementFixture.ReadyMarker);
            fixture.Removed.ShouldBeEmpty();
        }

        // The same exact shape in the work volume is never tool scratch.
        var work = Path.Combine(VolumeRetirementFixture.WorkMount(fixture), "claude-1654", "bash-edit-diff", "1-2-0123456789abcdef");
        var moved = await VolumeRetirementFixture.Bash($$"""
            set -euo pipefail
            rm -rf -- {{VolumeRetirementFixture.Quote(Path.Combine(tmp, "bash-edit-diff"))}}
            {{SnapshotFunction}}
            snapshot {{VolumeRetirementFixture.Quote(work)}} exact
            """);
        moved.Exit.ShouldBe(0, "c1609-r3: plant work snapshot; " + moved.Output);
        var workRun = await VolumeRetirementFixture.Preview(fixture);
        var workText = workRun.Output + await VolumeRetirementFixture.AuditText(fixture);
        workRun.Exit.ShouldBe(2, "c1609-r3: work-volume snapshot refuses; " + VolumeRetirementFixture.Truncate(workText));
        workText.ShouldNotContain(VolumeRetirementFixture.ReadyMarker);
        workText.ShouldNotContain("disposable-fixture classifier=bash-edit-diff-v1 path=claude-1654");
        fixture.Removed.ShouldBeEmpty();
        _ = outer;
    }

    // snapshot <dir> <kind>: the Claude Code bash-edit-diff layout, optionally varied.
    private const string SnapshotFunction = """
        snapshot() {
            local dir="$1" kind="$2" tree oid author msg parent=''
            mkdir -p -- "$dir/objects/info" "$dir/refs/heads" "$dir/refs/tags" "$dir/info"
            printf 'ref: refs/heads/main\n' > "$dir/HEAD"
            git config --file "$dir/config" core.repositoryformatversion 0
            git config --file "$dir/config" core.bare false
            git config --file "$dir/config" core.logallrefupdates false
            git config --file "$dir/config" core.untrackedcache true
            : > "$dir/config.global"
            printf '# exclude\n' > "$dir/info/exclude"
            printf '* -filter -working-tree-encoding -text -eol -ident !diff\n' > "$dir/info/attributes"
            printf '/work/repos/antiphon/.git/objects\n' > "$dir/objects/info/alternates"
            printf 'snapshot bytes\n' > "$dir/objects/blob.txt"
            tree="$(printf '100644 blob %s\tfile.txt\n' "$(git --git-dir="$dir" hash-object -w "$dir/objects/blob.txt")" | git --git-dir="$dir" mktree)"
            rm -f -- "$dir/objects/blob.txt"
            author='bash-edit-diff <bash-edit-diff@localhost> 1000000000 +0000'
            msg='snapshot'
            [ "$kind" != foreign-commit ] || msg='real work'
            if [ "$kind" = parent ]; then
                parent="$(printf 'tree %s\nauthor %s\ncommitter %s\n\nsnapshot\n' "$tree" "$author" "$author" | git --git-dir="$dir" hash-object -w -t commit --stdin)"
                parent="parent $parent"$'\n'
            fi
            oid="$(printf 'tree %s\n%sauthor %s\ncommitter %s\n\n%s\n' "$tree" "$parent" "$author" "$author" "$msg" | git --git-dir="$dir" hash-object -w -t commit --stdin)"
            printf '%s\n' "$oid" > "$dir/refs/heads/main"
            GIT_INDEX_FILE="$dir/index" git --git-dir="$dir" read-tree "$oid"
            case "$kind" in
                extra-ref) printf '%s\n' "$oid" > "$dir/refs/heads/other" ;;
                stash) printf '%s\n' "$oid" > "$dir/refs/stash" ;;
                logs) mkdir -p -- "$dir/logs/refs/heads"; printf '%s %s x <x@x> 1 +0000\tx\n' 0000000000000000000000000000000000000000 "$oid" > "$dir/logs/refs/heads/main" ;;
                packed-refs) printf '%s refs/heads/kept\n' "$oid" > "$dir/packed-refs" ;;
                hooks) mkdir -p -- "$dir/hooks"; printf '#!/bin/sh\n' > "$dir/hooks/post-checkout"; chmod +x "$dir/hooks/post-checkout" ;;
                remote) git config --file "$dir/config" remote.origin.url https://fixture.invalid/x.git ;;
                detached) printf '%s\n' "$oid" > "$dir/HEAD" ;;
            esac
        }
        """;

    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public async Task Gone_tracking_retains_original_commit_protection()
    {
        NativeLinuxGate.Require(NativeLinuxReason);
        using var fixture = new C1008HostFixture(main: false);
        var (bare, outer) = await VolumeRetirementFixture.SeedPublishedOuter(fixture);
        var reachable = await VolumeRetirementFixture.Bash($$"""
            set -euo pipefail
            git -C {{VolumeRetirementFixture.Quote(outer)}} checkout -q -b feature
            printf 'feature\n' > {{VolumeRetirementFixture.Quote(outer)}}/feature.txt
            git -C {{VolumeRetirementFixture.Quote(outer)}} add feature.txt
            git -C {{VolumeRetirementFixture.Quote(outer)}} -c commit.gpgsign=false commit -qm feature
            git -C {{VolumeRetirementFixture.Quote(outer)}} push -q origin HEAD:refs/heads/feature
            git -C {{VolumeRetirementFixture.Quote(outer)}} checkout -q main
            git -C {{VolumeRetirementFixture.Quote(outer)}} merge -q --no-ff feature -m merge-feature
            git -C {{VolumeRetirementFixture.Quote(outer)}} push -q origin HEAD:refs/heads/main
            git -C {{VolumeRetirementFixture.Quote(bare)}} update-ref -d refs/heads/feature
            git -C {{VolumeRetirementFixture.Quote(outer)}} fetch -q origin
            git -C {{VolumeRetirementFixture.Quote(outer)}} remote prune origin
            """);
        reachable.Exit.ShouldBe(0, "c1609-c14: reachable original; " + reachable.Output);
        var passed = await VolumeRetirementFixture.Preview(fixture);
        var passedText = passed.Output + await VolumeRetirementFixture.AuditText(fixture);
        passed.Exit.ShouldBe(0, "c1609-c14: original still reachable; " + VolumeRetirementFixture.Truncate(passedText));
        passedText.ShouldContain(VolumeRetirementFixture.ReadyMarker);
        fixture.Removed.ShouldBeEmpty();

        var rebased = await VolumeRetirementFixture.Bash($$"""
            set -euo pipefail
            git -C {{VolumeRetirementFixture.Quote(outer)}} checkout -q -b replay
            printf 'replay\n' > {{VolumeRetirementFixture.Quote(outer)}}/replay.txt
            git -C {{VolumeRetirementFixture.Quote(outer)}} add replay.txt
            git -C {{VolumeRetirementFixture.Quote(outer)}} -c commit.gpgsign=false commit -qm replay
            tree=$(git -C {{VolumeRetirementFixture.Quote(outer)}} rev-parse HEAD^{tree})
            parent=$(git -C {{VolumeRetirementFixture.Quote(outer)}} rev-parse HEAD^)
            alt=$(git -C {{VolumeRetirementFixture.Quote(outer)}} commit-tree "$tree" -p "$parent" -m replay)
            git -C {{VolumeRetirementFixture.Quote(outer)}} update-ref refs/heads/replay "$alt"
            git -C {{VolumeRetirementFixture.Quote(outer)}} checkout -q main
            printf 'alt=%s\n' "$alt"
            """);
        rebased.Exit.ShouldBe(0, "c1609-c14: rebased twin; " + rebased.Output);
        var alt = rebased.Output.Split('\n').Single(line => line.StartsWith("alt=", StringComparison.Ordinal))[4..];
        var refused = await VolumeRetirementFixture.Preview(fixture);
        var refusedText = refused.Output + await VolumeRetirementFixture.AuditText(fixture);
        refused.Exit.ShouldBe(2, "c1609-c14: twin is not the original; " + VolumeRetirementFixture.Truncate(refusedText));
        refusedText.ShouldContain(alt);
        refusedText.ShouldContain("recovery=publish-the-original-commit-to-an-origin-archive-branch-then-preview-again");
        refusedText.ShouldNotContain(VolumeRetirementFixture.ReadyMarker);
        fixture.Removed.ShouldBeEmpty();

        var archive = await VolumeRetirementFixture.Bash(
            "set -euo pipefail\n" +
            "git -C " + VolumeRetirementFixture.Quote(outer) + " push -q origin " +
            VolumeRetirementFixture.Quote(alt) + ":refs/heads/archive\n" +
            "git -C " + VolumeRetirementFixture.Quote(outer) + " fetch -q origin\n");
        archive.Exit.ShouldBe(0, "c1609-c14: archive branch; " + archive.Output);
        var again = await VolumeRetirementFixture.Preview(fixture);
        var againText = again.Output + await VolumeRetirementFixture.AuditText(fixture);
        again.Exit.ShouldBe(0, "c1609-c14: archive head publishes the original; " + VolumeRetirementFixture.Truncate(againText));
        againText.ShouldContain(VolumeRetirementFixture.ReadyMarker);
        fixture.Removed.ShouldBeEmpty();
    }

    private static async Task<(int Exit, string Output)> SeedTemplate(string path, string kind)
    {
        var header = kind switch
        {
            "gpgsig" => "printf 'gpgsig -----BEGIN PGP SIGNATURE-----\\n HEADER-SENTINEL-1609\\n -----END PGP SIGNATURE-----\\n'\n",
            "encoding" => "printf 'encoding HEADER-SENTINEL-1609\\n'\n",
            "custom" => "printf 'x-card-1609 HEADER-SENTINEL-1609\\n'\n",
            _ => ""
        };
        return await VolumeRetirementFixture.Bash($$"""
            set -euo pipefail
            rm -rf -- {{VolumeRetirementFixture.Quote(path)}}
            git init -q -b main {{VolumeRetirementFixture.Quote(path)}}
            git -C {{VolumeRetirementFixture.Quote(path)}} config user.name Fixture
            git -C {{VolumeRetirementFixture.Quote(path)}} config user.email fixture@example.invalid
            git -C {{VolumeRetirementFixture.Quote(path)}} config commit.gpgsign false
            printf '.antiphon/\n' > {{VolumeRetirementFixture.Quote(path)}}/.gitignore
            printf 'benign fixture' > {{VolumeRetirementFixture.Quote(path)}}/seed.txt
            git -C {{VolumeRetirementFixture.Quote(path)}} add .gitignore seed.txt
            tree=$(git -C {{VolumeRetirementFixture.Quote(path)}} write-tree)
            {
              printf 'tree %s\n' "$tree"
              {{header}}
              printf 'author Fixture <fixture@example.invalid> 1000000000 +0000\n'
              printf 'committer Fixture <fixture@example.invalid> 1000000001 +0000\n'
              printf '\nseed\n'
            } > {{VolumeRetirementFixture.Quote(path)}}/.git/COMMIT_EDITMSG.c1609
            oid=$(git -C {{VolumeRetirementFixture.Quote(path)}} hash-object --literally -t commit -w --stdin < {{VolumeRetirementFixture.Quote(path)}}/.git/COMMIT_EDITMSG.c1609)
            rm -rf -- {{VolumeRetirementFixture.Quote(path)}}/.git/logs
            git -C {{VolumeRetirementFixture.Quote(path)}} update-ref refs/heads/main "$oid"
            git -C {{VolumeRetirementFixture.Quote(path)}} symbolic-ref HEAD refs/heads/main
            git -C {{VolumeRetirementFixture.Quote(path)}} read-tree HEAD
            printf 'oid=%s\n' "$oid"
            """);
    }

    private static void ReplaceReceipt(C1008HostFixture fixture, JsonObject seed, string operation, string sourceSha, string image, bool append = false)
    {
        var copy = JsonNode.Parse(seed.ToJsonString())!.AsObject();
        copy["operationId"] = operation;
        copy["sourceSha"] = sourceSha;
        copy["image"] = image;
        copy["dryRun"] = false;
        copy["outcome"] = "completed";
        copy.Remove("originalCleanupOperationId");
        var dir = Path.Combine(fixture.Root, "server", "temp-container-retirement");
        if (!append)
        {
            foreach (var file in Directory.GetFiles(dir, "c994*.json")) File.Delete(file);
        }
        File.WriteAllText(Path.Combine(dir, operation + ".json"), copy.ToJsonString());
    }

    // CARD-1665. C1008HostFixture runs a caller's setup once, at the script's top-level
    // EXIT trap. The temp content audit arms and restores its own EXIT traps mid-run;
    // re-running the setup there would wrap this renamed write_result around its own
    // wrapper, so the final result would recurse instead of being written once.
    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public async Task Fixture_setup_runs_once_across_audit_traps()
    {
        NativeLinuxGate.Require(NativeLinuxReason);
        using var fixture = new C1008HostFixture(main: false);
        const string wrap = """
            FUNCNEST=100
            printf 'setup\n' >> "$C1008_FIXTURE_ROOT/c1665-setup"
            c1665_body="$(declare -f write_result)"; eval "${c1665_body/write_result/c1665_original_write_result}"
            write_result() { printf 'wrapped\n' >> "$C1008_FIXTURE_ROOT/c1665-wrapped"; c1665_original_write_result "$@"; }
            """;
        var preview = await fixture.Run("retire-temp-runner", wrap, dryRun: true);
        var wrapped = Path.Combine(fixture.Root, "c1665-wrapped");
        (File.Exists(wrapped) ? File.ReadAllLines(wrapped).Length : 0).ShouldBe(1,
            "c1665: the renamed write_result wraps the product once and the result is written once; " + preview.Output);
        File.ReadAllLines(Path.Combine(fixture.Root, "c1665-setup")).Length.ShouldBe(1,
            "c1665: the setup ran once although the audit armed its own EXIT traps; " + preview.Output);
        preview.Exit.ShouldBe(0, "c1665: the wrapped preview completes; " + preview.Output);
        preview.Output.ShouldContain(VolumeRetirementFixture.ReadyMarker);
        File.Exists(Path.Combine(fixture.Root, "evidence", "retire-temp-runner", "preview-audit.out"))
            .ShouldBeTrue("c1665: the temp content audit, which arms the inner EXIT trap, ran");
    }

    // CARD-1665 (review 88cb4849 A-3). The temp apply's post-removal content audit is
    // conditional on removed containers, but a temp recycle never removes one: binding the
    // generation refuses any present owned temp container (that cleanup is the separate
    // exact-ID retire-temp-containers phase) before the lock, any stop or any audit.
    // C1364 pins the reachable case (no container, one scan); this pins the other branch shut.
    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public async Task Temp_recycle_refuses_a_present_owned_container_before_any_audit()
    {
        NativeLinuxGate.Require(NativeLinuxReason);
        using var fixture = new C1008HostFixture(main: false);
        await VolumeRetirementFixture.ReadyPreview(fixture);
        fixture.ReloadDocker();
        fixture.Statuses["server2-temp"]!["runnerSessions"] = 0;
        fixture.Docker["containers"]!.AsArray().Add(
            fixture.Container('7', "antiphon-runner-temp", "session-runner", running: false));
        var traceBefore = fixture.Trace.Length;
        var run = await fixture.Run("retire-temp-runner");
        run.Exit.ShouldBe(2, "c1665-a3: a present owned temp container refuses; " + run.Output);
        run.Output.ShouldContain("DIAGNOSIS=RecycleContainerStateUnknown");
        var observed = fixture.Trace.Skip(traceBefore).ToArray();
        observed.Count(a => a[0] == "run").ShouldBe(0, "c1665-a3: no content audit before the refusal; " + run.Output);
        observed.Count(a => a[0] is "stop" or "rm" or "create" or "start").ShouldBe(0, "c1665-a3: no container effect");
        fixture.Removed.ShouldBeEmpty("c1665-a3: no volume removed");
        File.Exists(Path.Combine(fixture.Root, "server", "recycle", "c100800000000000000000000000000000001.json"))
            .ShouldBeFalse("c1665-a3: the refusal precedes the recycle journal");
    }

    // CARD-1673 (M65-B). The generation binding refuses a present owned temp container on its own:
    // it answers before the topology layer (c1008_owned_mounts) is ever called, so that layer cannot
    // mask its loss. The wrapper only records that the topology layer was reached.
    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public async Task Temp_recycle_generation_binding_refuses_a_present_owned_container_alone()
    {
        NativeLinuxGate.Require(NativeLinuxReason);
        using var fixture = new C1008HostFixture(main: false);
        await VolumeRetirementFixture.ReadyPreview(fixture);
        fixture.ReloadDocker();
        fixture.Statuses["server2-temp"]!["runnerSessions"] = 0;
        fixture.Docker["containers"]!.AsArray().Add(
            fixture.Container('7', "antiphon-runner-temp", "session-runner", running: false));
        var reached = Path.Combine(fixture.Root, "c1673-owned-mounts-reached");
        var run = await fixture.Run("retire-temp-runner", extra: $$"""
            eval "$(declare -f c1008_owned_mounts | sed '1s/^c1008_owned_mounts/c1673_real_owned_mounts/')"
            c1008_owned_mounts() { : > '{{reached}}'; c1673_real_owned_mounts "$@"; }
            """);
        run.Exit.ShouldBe(2, "c1673-b: a present owned temp container refuses; " + run.Output);
        run.Output.ShouldContain("DIAGNOSIS=RecycleContainerStateUnknown");
        File.Exists(reached).ShouldBeFalse("c1673-b: the generation binding refused before the topology layer; " + run.Output);
        File.Exists(Path.Combine(fixture.Root, "server", "recycle", "c100800000000000000000000000000000001.json"))
            .ShouldBeFalse("c1673-b: the refusal precedes the recycle journal");
    }

    // CARD-1673 (M65-T). The topology layer refuses a present owned temp container on its own: the
    // generation binding is shown a census without the project's containers (so it passes), and the
    // owned-container topology at the recycle (the temp model declares no services) still refuses
    // with the same diagnosis before any run, stop or rm and before the journal.
    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public async Task Temp_recycle_topology_layer_refuses_a_present_owned_container_alone()
    {
        NativeLinuxGate.Require(NativeLinuxReason);
        using var fixture = new C1008HostFixture(main: false);
        await VolumeRetirementFixture.ReadyPreview(fixture);
        fixture.ReloadDocker();
        fixture.Statuses["server2-temp"]!["runnerSessions"] = 0;
        fixture.Docker["containers"]!.AsArray().Add(
            fixture.Container('7', "antiphon-runner-temp", "session-runner", running: false));
        var bound = Path.Combine(fixture.Root, "c1673-generation-bound");
        var traceBefore = fixture.Trace.Length;
        var run = await fixture.Run("retire-temp-runner", extra: $$"""
            eval "$(declare -f c1008_bind_generation | sed '1s/^c1008_bind_generation/c1673_real_bind_generation/')"
            c1008_bind_generation() {
                local c1673_census="$C1008_CENSUS"
                C1008_CENSUS="$(printf '%s' "$c1673_census" | jq -c --arg p "$C1008_PROJECT" '[.[]|select(.Config.Labels["com.docker.compose.project"]!=$p)]')"
                c1673_real_bind_generation "$@"
                C1008_CENSUS="$c1673_census"
                : > '{{bound}}'
            }
            """);
        File.Exists(bound).ShouldBeTrue("c1673-t: the generation binding passed with the container hidden; " + run.Output);
        run.Exit.ShouldBe(2, "c1673-t: the topology layer refuses the present owned temp container; " + run.Output);
        run.Output.ShouldContain("DIAGNOSIS=RecycleContainerStateUnknown");
        var observed = fixture.Trace.Skip(traceBefore).ToArray();
        observed.Count(a => a[0] == "run").ShouldBe(0, "c1673-t: no content audit before the refusal; " + run.Output);
        observed.Count(a => a[0] is "stop" or "rm" or "create" or "start").ShouldBe(0, "c1673-t: no container effect");
        fixture.Removed.ShouldBeEmpty("c1673-t: no volume removed");
        File.Exists(Path.Combine(fixture.Root, "server", "recycle", "c100800000000000000000000000000000001.json"))
            .ShouldBeFalse("c1673-t: the refusal precedes the recycle journal");
    }

    // Task 5b7656a9: the real census read (no C727 stub) streams the packet to ssh stdin.
    // Its awaited write must not leak a VoidTaskResult into the function output, or the
    // manifest check sees two items and every real deploy-temp stops TempRetirementJournalUnknown.
    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public async Task Real_census_read_returns_only_the_census_text()
    {
        NativeLinuxGate.Require(NativeLinuxReason);
        var rows = await VolumeRetirementFixture.RunOutputWitness("Census");
        rows.ShouldBe(new[]
        {
            "OUTPUT census count=1 types=System.String streamed=True",
            "OUTPUT proof count=1 types=System.String",
            "OUTPUT clearable ok",
        }, string.Join("\n", rows));
    }

    // Task bb0cd0ec: deploy-temp lifts its own deployment hold after deploy-temp-runner has
    // created the four temp volumes. Only the reactivation clear of a retired placeholder
    // runs the volume/journal check; the hold lift must reach its POST, and the reactivation
    // clear must still refuse while the volumes are present.
    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public async Task Hold_lift_clear_after_deploy_is_not_refused_for_its_own_volumes()
    {
        NativeLinuxGate.Require(NativeLinuxReason);
        var rows = await VolumeRetirementFixture.RunOutputWitness("HoldLift");
        rows.ShouldBe(new[]
        {
            "OUTPUT reactivate ok posts=1",
            "OUTPUT holdlift ok posts=2",
            "OUTPUT reactivate-retained threw:TempVolumesRetained antiphon-runner-temp_work posts=2",
        }, string.Join("\n", rows));
    }

    // Task bb0cd0ec: the hold lift skips the volume check only for the runner this run
    // deployed. A retired runner or another build refuses under the lock before the POST.
    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public async Task Hold_lift_clear_refuses_a_retired_or_other_build_runner()
    {
        NativeLinuxGate.Require(NativeLinuxReason);
        var rows = await VolumeRetirementFixture.RunOutputWitness("HoldLiftRefused");
        rows.ShouldBe(new[]
        {
            "OUTPUT holdlift-retired threw:TempRunnerRetiredDuringHold posts=0",
            "OUTPUT holdlift-other-build threw:TempHoldLiftBuildMismatch posts=0",
        }, string.Join("\n", rows));
    }

    // CARD-1689: the temp-retirement exchange refused three different causes as the
    // same bare RecycleGitAuditUnknown, and the coordinator console shows only that
    // diagnosis. Each cause now names itself and the host file that holds its detail.
    private const string ExchangeProject = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1";
    private const string ExchangeNonce = "0123456789abcdef0123456789abcdef";
    private const string ExchangeOrigin = "https://fixture.invalid/card-1689/private.git";
    private static readonly string ExchangeRequest = "C1008_PRIVATE_ORIGIN repo=repos/private origin=" + ExchangeOrigin +
        " originHash=" + new string('1', 64) + " proofName=" + new string('2', 64);

    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public async Task Exchange_inventory_refusal_names_its_check_and_detail_file()
    {
        NativeLinuxGate.Require(NativeLinuxReason);
        var root = Directory.CreateTempSubdirectory("c1689-exchange-").FullName;
        try
        {
            var refused = await PrepareExchange(root, 2,
                "audit check=git-dir-shape status=0 repo=repos/private\nRecycleGitAuditUnknown\n", registry: "", stdin: "/dev/null");
            refused.Exit.ShouldBe(2, refused.Output);
            Diagnosis(refused.Output).ShouldBe("RecycleGitAuditUnknown audit check=git-dir-shape status=0 repo=repos/private reason=inventory detail=" +
                ExchangeOperationRoot(root) + "/inventory-" + ExchangeNonce + ".out", refused.Output);
            var empty = await PrepareExchange(root, 2, "", registry: "", stdin: "/dev/null", fresh: true);
            Diagnosis(empty.Output).ShouldBe("RecycleGitAuditUnknown audit check=unclassified status= repo=. reason=inventory detail=" +
                ExchangeOperationRoot(root) + "/inventory-" + ExchangeNonce + ".out", empty.Output);

            // Publication and dirty-tree refusals keep their established diagnoses.
            var unpublished = await PrepareExchange(root, 3, "", registry: "", stdin: "/dev/null", fresh: true);
            Diagnosis(unpublished.Output).ShouldBe("RecycleUnpublishedWork", unpublished.Output);
            var dirty = await PrepareExchange(root, 4, "", registry: "", stdin: "/dev/null", fresh: true);
            Diagnosis(dirty.Output).ShouldBe("RecycleWorktreeDirty", dirty.Output);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public async Task Exchange_registry_refusals_name_the_registry_cause()
    {
        NativeLinuxGate.Require(NativeLinuxReason);
        var root = Directory.CreateTempSubdirectory("c1689-exchange-").FullName;
        try
        {
            var registry = Path.Combine(root, "server", "retirement-origins.json");
            var requests = ExchangeOperationRoot(root) + "/inventory-" + ExchangeNonce + "/private-origins.txt";
            var write = "umask 077; jq -ncS --arg p " + VolumeRetirementFixture.Quote(ExchangeProject) + " --arg o " +
                VolumeRetirementFixture.Quote(ExchangeOrigin) + " ";
            var vectors = new (string Name, string Registry, string Expected)[]
            {
                ("missing", "", "RecycleGitAuditUnknown reason=registry-missing detail=" + registry),
                ("non-canonical", "umask 077; jq -nS --arg p " + VolumeRetirementFixture.Quote(ExchangeProject) +
                    " '{schema:1,projects:{($p):{origins:[]}}}' > \"$registry\"",
                    "RecycleGitAuditUnknown reason=registry-invalid detail=" + registry),
                ("writable", write + "'{schema:1,projects:{($p):{origins:[$o]}}}' > \"$registry\"; chmod 0666 \"$registry\"",
                    "RecycleGitAuditUnknown reason=registry-invalid detail=" + registry),
                ("project", write + "'{schema:1,projects:{\"bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbb2\":{origins:[$o]}}}' > \"$registry\"",
                    "RecycleGitAuditUnknown reason=registry-project detail=" + registry),
                ("unapproved", write + "'{schema:1,projects:{($p):{origins:[($o|rtrimstr(\".git\"))]}}}' > \"$registry\"",
                    "RecycleGitAuditUnknown reason=unapproved-origin detail=" + requests),
            };
            var seen = new List<string>();
            foreach (var vector in vectors)
            {
                var run = await PrepareExchange(root, 0, ExchangeRequest + "\n", vector.Registry, stdin: "/dev/null", fresh: seen.Count > 0);
                run.Exit.ShouldBe(2, vector.Name + "; " + run.Output);
                Diagnosis(run.Output).ShouldBe(vector.Expected, vector.Name + "; " + run.Output);
                run.Output.ShouldNotContain("C1609_PROOF_REQUEST", Case.Sensitive, vector.Name + ": the registry refuses before the exchange");
                seen.Add(vector.Expected);
            }
            seen.Distinct().Count().ShouldBe(4, "missing, invalid, project and unapproved are distinct causes");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public async Task Exchange_proof_failure_names_its_stage()
    {
        NativeLinuxGate.Require(NativeLinuxReason);
        var root = Directory.CreateTempSubdirectory("c1689-exchange-").FullName;
        try
        {
            var registry = "umask 077; jq -ncS --arg p " + VolumeRetirementFixture.Quote(ExchangeProject) + " --arg o " +
                VolumeRetirementFixture.Quote(ExchangeOrigin) + " '{schema:1,projects:{($p):{origins:[$o]}}}' > \"$registry\"";
            var silent = await PrepareExchange(root, 0, ExchangeRequest + "\n", registry, stdin: "/dev/null");
            silent.Exit.ShouldBe(2, silent.Output);
            silent.Output.ShouldContain("C1609_PROOF_REQUEST ", Case.Sensitive, "a registered origin reaches the desktop exchange");
            Diagnosis(silent.Output).ShouldBe("RecycleGitAuditUnknown reason=proof-exchange stage=response", silent.Output);

            var reply = Path.Combine(root, "reply");
            await File.WriteAllTextAsync(reply, "C1609_PROOF_RESPONSE " + new string('0', 32) + "\n");
            var mismatched = await PrepareExchange(root, 0, ExchangeRequest + "\n", registry, stdin: reply, fresh: true);
            Diagnosis(mismatched.Output).ShouldBe("RecycleGitAuditUnknown reason=proof-exchange stage=binding", mismatched.Output);

            // No private origin: no registry read and no exchange, as before.
            var none = await PrepareExchange(root, 0, "", registry: "", stdin: "/dev/null", fresh: true);
            none.Exit.ShouldBe(0, none.Output);
            none.Output.ShouldContain("PREPARE_EXIT=0", Case.Sensitive);
            none.Output.ShouldNotContain("DIAGNOSIS=");
            File.Exists(ExchangeOperationRoot(root) + "/inventory-" + ExchangeNonce + "/c590-result.json").ShouldBeTrue(none.Output);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    // CARD-1689 Mutation X-3a/X-3b: a canonical, private registry that names this
    // project and lists the origin is still refused when its shape is wrong.
    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public async Task Exchange_registry_with_a_wrong_shape_is_invalid_before_the_exchange()
    {
        NativeLinuxGate.Require(NativeLinuxReason);
        var root = Directory.CreateTempSubdirectory("c1689-exchange-").FullName;
        try
        {
            var registry = Path.Combine(root, "server", "retirement-origins.json");
            var trusted = ExchangeOperationRoot(root) + "/inventory-" + ExchangeNonce + "/trusted-origins.json";
            var write = "umask 077; jq -ncS --arg p " + VolumeRetirementFixture.Quote(ExchangeProject) + " --arg o " +
                VolumeRetirementFixture.Quote(ExchangeOrigin) + " ";
            var control = await PrepareExchange(root, 0, ExchangeRequest + "\n",
                write + "'{schema:1,projects:{($p):{origins:[$o]}}}' > \"$registry\"", stdin: "/dev/null");
            control.Output.ShouldContain("C1609_PROOF_REQUEST ", Case.Sensitive, "control: the well-shaped registry reaches the exchange");
            var vectors = new (string Name, string Program)[]
            {
                ("schema-2", "{schema:2,projects:{($p):{origins:[$o]}}}"),
                ("extra-key", "{schema:1,extra:1,projects:{($p):{origins:[$o]}}}"),
                ("duplicate-origin", "{schema:1,projects:{($p):{origins:[$o,$o]}}}"),
                ("non-guid-project", "{schema:1,projects:{($p):{origins:[$o]},\"NOT-A-GUID\":{origins:[$o]}}}"),
                ("extra-entry-key", "{schema:1,projects:{($p):{origins:[$o],note:1}}}"),
                ("empty-origin", "{schema:1,projects:{($p):{origins:[$o,\"\"]}}}"),
                ("non-string-origin", "{schema:1,projects:{($p):{origins:[$o,1]}}}"),
            };
            foreach (var vector in vectors)
            {
                var run = await PrepareExchange(root, 0, ExchangeRequest + "\n", write + "'" + vector.Program + "' > \"$registry\"",
                    stdin: "/dev/null", fresh: true);
                run.Exit.ShouldBe(2, vector.Name + "; " + run.Output);
                Diagnosis(run.Output).ShouldBe("RecycleGitAuditUnknown reason=registry-invalid detail=" + registry, vector.Name + "; " + run.Output);
                run.Output.ShouldNotContain("C1609_PROOF_REQUEST", Case.Sensitive, vector.Name + ": the registry refuses before the exchange");
                File.Exists(trusted).ShouldBeFalse(vector.Name + ": nothing is exported from an invalid registry");
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    // CARD-1689 Mutation X-7: the desktop approves origins from trusted-origins.json,
    // so the export carries this project's registered origins and no other project's.
    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public async Task Exchange_exports_only_this_projects_registered_origins()
    {
        NativeLinuxGate.Require(NativeLinuxReason);
        var root = Directory.CreateTempSubdirectory("c1689-exchange-").FullName;
        try
        {
            const string second = "https://fixture.invalid/card-1689/second.git";
            const string other = "https://fixture.invalid/card-1689/other-project.git";
            var registry = "umask 077; jq -ncS --arg p " + VolumeRetirementFixture.Quote(ExchangeProject) + " --arg o " +
                VolumeRetirementFixture.Quote(ExchangeOrigin) + " --arg s " + VolumeRetirementFixture.Quote(second) + " --arg x " +
                VolumeRetirementFixture.Quote(other) +
                " '{schema:1,projects:{($p):{origins:[$o,$s]},\"bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbb2\":{origins:[$x]}}}' > \"$registry\"";
            var run = await PrepareExchange(root, 0, ExchangeRequest + "\n", registry, stdin: "/dev/null");
            Diagnosis(run.Output).ShouldBe("RecycleGitAuditUnknown reason=proof-exchange stage=response", run.Output);
            var exported = await File.ReadAllBytesAsync(ExchangeOperationRoot(root) + "/inventory-" + ExchangeNonce + "/trusted-origins.json");
            var expected = "{\"origins\":[\"" + ExchangeOrigin + "\",\"" + second + "\"],\"projectId\":\"" + ExchangeProject + "\",\"schema\":1}\n";
            Encoding.UTF8.GetString(exported).ShouldBe(expected);
            exported.SequenceEqual(Encoding.UTF8.GetBytes(expected)).ShouldBeTrue("trusted-origins.json bytes");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    // CARD-1689 Mutation X-8: git error text ahead of the audit line never reaches the
    // console diagnosis; only the first `audit check=` line does.
    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public async Task Exchange_inventory_diagnosis_skips_git_error_lines()
    {
        NativeLinuxGate.Require(NativeLinuxReason);
        var root = Directory.CreateTempSubdirectory("c1689-exchange-").FullName;
        try
        {
            var refused = await PrepareExchange(root, 2,
                "fatal: unable to access 'https://user@fixture.invalid/card-1689/private.git/': Could not resolve host\n" +
                "audit check=ls-remote status=128 repo=repos/private\n" +
                "audit check=git-dir-shape status=0 repo=repos/other\n", registry: "", stdin: "/dev/null");
            refused.Exit.ShouldBe(2, refused.Output);
            var diagnosis = Diagnosis(refused.Output);
            diagnosis.ShouldBe("RecycleGitAuditUnknown audit check=ls-remote status=128 repo=repos/private reason=inventory detail=" +
                ExchangeOperationRoot(root) + "/inventory-" + ExchangeNonce + ".out", refused.Output);
            diagnosis.ShouldNotContain("://");
            diagnosis.ShouldNotContain("@");
            diagnosis.ShouldNotContain("fatal:");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    // CARD-1689 Mutation X-6: a request line that does not have the exact private-origin
    // shape is never approved. jq's all() treats an unmatched capture as vacuously true.
    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public async Task Exchange_malformed_private_origin_lines_are_never_approved()
    {
        NativeLinuxGate.Require(NativeLinuxReason);
        var root = Directory.CreateTempSubdirectory("c1689-exchange-").FullName;
        try
        {
            const string spaced = "https://fixture.invalid/card-1689/private copy.git";
            var requests = ExchangeOperationRoot(root) + "/inventory-" + ExchangeNonce + "/private-origins.txt";
            var registry = "umask 077; jq -ncS --arg p " + VolumeRetirementFixture.Quote(ExchangeProject) + " --arg o " +
                VolumeRetirementFixture.Quote(ExchangeOrigin) + " --arg s " + VolumeRetirementFixture.Quote(spaced) +
                " '{schema:1,projects:{($p):{origins:[$o,$s]}}}' > \"$registry\"";
            var suffix = " originHash=" + new string('1', 64) + " proofName=" + new string('2', 64);
            var vectors = new (string Name, string Line)[]
            {
                ("repeated-suffix", ExchangeRequest + suffix),
                ("spaced-origin", "C1008_PRIVATE_ORIGIN repo=repos/private origin=" + spaced + suffix),
                ("upper-hash", "C1008_PRIVATE_ORIGIN repo=repos/private origin=" + ExchangeOrigin +
                    " originHash=" + new string('A', 64) + " proofName=" + new string('2', 64)),
            };
            var fresh = false;
            foreach (var vector in vectors)
            {
                var run = await PrepareExchange(root, 0, vector.Line + "\n", registry, stdin: "/dev/null", fresh: fresh);
                fresh = true;
                run.Exit.ShouldBe(2, vector.Name + "; " + run.Output);
                Diagnosis(run.Output).ShouldBe("RecycleGitAuditUnknown reason=unapproved-origin detail=" + requests, vector.Name + "; " + run.Output);
                run.Output.ShouldNotContain("C1609_PROOF_REQUEST", Case.Sensitive, vector.Name + ": no exchange for an unmatched request line");
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string ExchangeOperationRoot(string root) =>
        Path.Combine(root, "server", "retirement", VolumeRetirementFixture.Operation);

    private static string Diagnosis(string output) =>
        output.Split('\n').Where(line => line.StartsWith("DIAGNOSIS=", StringComparison.Ordinal))
            .Select(line => line["DIAGNOSIS=".Length..]).SingleOrDefault() ?? "<none>";

    // Real c1609_prepare_exchange with only the host inventory helper and the
    // c590-remote refusal writer stubbed. stdin is the desktop coordinator's reply.
    private static async Task<(int Exit, string Output)> PrepareExchange(string root, int inventoryExit, string inventory,
        string registry, string stdin, bool fresh = false)
    {
        var inventoryFile = Path.Combine(root, "inventory-lines");
        await File.WriteAllTextAsync(inventoryFile, inventory);
        var run = await VolumeRetirementFixture.Bash($$"""
            set -euo pipefail
            SERVER2_ROOT={{VolumeRetirementFixture.Quote(Path.Combine(root, "server"))}}
            registry="$SERVER2_ROOT/retirement-origins.json"
            {{(fresh ? "rm -rf -- \"$SERVER2_ROOT\"" : ":")}}
            C1008_OPERATION={{VolumeRetirementFixture.Operation}}
            C1008_PROJECT_ID={{ExchangeProject}}
            C1609_EXCHANGE=1
            C1609_PHASE_NONCE={{ExchangeNonce}}
            C1609_EXCHANGE_PROOF_ROOT={{VolumeRetirementFixture.Quote(Path.Combine(root, "control"))}}
            c1008_refuse() { printf 'DIAGNOSIS=%s\n' "$1"; exit 2; }
            c1008_audit_readonly() {
                [ "$1" = inventory ] || return 9
                cat -- {{VolumeRetirementFixture.Quote(inventoryFile)}}
                return {{inventoryExit}}
            }
            source {{VolumeRetirementFixture.Quote(Path.Combine(DelegateScriptRunner.RepoRoot, "scripts/c1008-retirement.sh"))}}
            (umask 077; mkdir -p -- "$(c1609_retirement_root)")
            {{(registry.Length == 0 ? ":" : registry)}}
            code=0
            c1609_prepare_exchange < {{VolumeRetirementFixture.Quote(stdin)}} || code=$?
            printf 'PREPARE_EXIT=%s\n' "$code"
            exit "$code"
            """);
        return run;
    }
}
