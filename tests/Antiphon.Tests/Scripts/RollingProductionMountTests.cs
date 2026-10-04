using System.Text.Json.Nodes;
using Antiphon.Tests.Application;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

[Category("Unit")]
public sealed class RollingProductionMountTests
{
    private static JsonObject Observation(C1008HostFixture f, string project) => new()
    {
        ["model"] = f.Docker["models"]![project]!.DeepClone(),
        ["owned"] = new JsonArray(f.Container('1', project, "session-runner", false), f.Container('2', project, "state-init", false)),
        ["volumes"] = f.Docker["volumes"]!.DeepClone(), ["project"] = project
    };

    // One Bash child per method; every case invokes the real production function.
    private static async Task Prove(string label, Action<C1008HostFixture, JsonObject, Action<string, bool, JsonObject>> cases)
    {
        using var f = new C1008HostFixture();
        var vectors = new JsonArray(); var expected = new Dictionary<string, bool>(StringComparer.Ordinal);
        void Add(string key, bool accepted, JsonObject input) {
            expected.Add(key, accepted);
            vectors.Add(new JsonObject { ["key"] = key, ["input"] = input.DeepClone() });
        }
        foreach (var project in new[] { "antiphon-runner", "antiphon-runner-temp" }) {
            var good = Observation(f, project); Add(project + ":valid", true, good);
            cases(f, good, (key, success, input) => Add(project + ":" + key, success, input));
        }
        File.WriteAllText(Path.Combine(f.Root, "mount-vectors.json"), vectors.ToJsonString());
        var command = """
            LANE=host
            while IFS= read -r vector; do
                key="$(printf '%s' "$vector" | jq -r .key)"
                (
                    input="$(printf '%s' "$vector" | jq -c .input)"
                    C1008_PROJECT="$(printf '%s' "$input" | jq -r .project)"
                    C994_FIXTURE_COMPOSE_JSON="$(printf '%s' "$input" | jq -c .model)"
                    owned="$(printf '%s' "$input" | jq -c .owned)"
                    volumes="$(printf '%s' "$input" | jq -c .volumes)"
                    override="$(printf '%s' "$input" | jq -r '.codexOverride // empty')"
                    [ -z "$override" ] || CODEX_HOME_PATH="$override"
                    override="$(printf '%s' "$input" | jq -r '.gitOverride // empty')"
                    [ -z "$override" ] || GIT_IDENTITY_PATH="$override"
                    metadataFault="$(printf '%s' "$input"|jq -r '.metadataFault // empty')"
                    if [ -n "$metadataFault" ];then sudo(){ [ "$1" = -n ] && shift;[ "$1" != "$metadataFault" ] || return 2;command "$@"; };fi
                    compose_host() { printf '%s' "$C994_FIXTURE_COMPOSE_JSON"; }
                    compose_temp() { printf '%s' "$C994_FIXTURE_COMPOSE_JSON"; }
                    c1008_refuse() { exit 2; }
                    model="$(c1008_compose_model)" || exit 2
                    for role in work runner-state runner-tmp dind-data; do
                        name="${C1008_PROJECT}_$role"
                        c1008_private_identity "$(printf '%s' "$volumes" | jq -c --arg n "$name" '.[$n]')" "$name" "$C1008_PROJECT" "$role" || exit 2
                    done
                    c1008_owned_mounts "$owned" "$model" "$volumes"
                    printf '%s' "$C1008_OWNED" | jq -e 'all(.[]; .Topology.version==1)' >/dev/null
                ) >/dev/null 2>&1 && code=0 || code=$?
                printf 'MOUNT_CASE %s %s\n' "$key" "$code"
            done < <(jq -c '.[]' "$C1008_FIXTURE_ROOT/mount-vectors.json")
            write_result true '' 0
            """;
        var run = await f.Run(extra: command);
        run.Exit.ShouldBe(0, label + ": child; " + run.Output);
        var actual = run.Output.Split('\n').Where(x => x.StartsWith("MOUNT_CASE ", StringComparison.Ordinal))
            .Select(x => x.Split(' ')).ToDictionary(x => x[1], x => x[2] == "0", StringComparer.Ordinal);
        actual.Keys.Order().ShouldBe(expected.Keys.Order(), label + ": exact case roster");
        foreach (var (key, value) in expected) actual[key].ShouldBe(value, label + ": " + key);
    }

    [Test, ParallelLimiter<ProcessSpawnLimit>]
    public Task C994_Compose_model_preserves_service_specific_mounts() => Prove("c994-model-targets", (_, good, add) => {
        foreach (var service in new[] { "state-init", "session-runner" }) {
            var bad = good.DeepClone().AsObject();
            var state = bad["model"]!["services"]![service]!["volumes"]!.AsArray().Single(x => x!["source"]!.GetValue<string>() == "runner-state")!;
            state["target"] = service == "state-init" ? "/state" : "/runner-state"; add(service + ":swap", false, bad);
        }
        var variants = new[] { "external", "nocopy", "kind", "options", "missing", "extra", "duplicate", "read-only-type", "unused-volume", "unused-secret" };
        foreach (var variant in variants) {
            var bad = good.DeepClone().AsObject(); var mounts = bad["model"]!["services"]!["session-runner"]!["volumes"]!.AsArray();
            switch (variant) {
                case "unused-volume": bad["model"]!["volumes"]!["foreign"] = new JsonObject{["name"]="foreign"}; break;
                case "unused-secret": bad["model"]!["secrets"]!["foreign"] = new JsonObject{["file"]="/foreign"}; break;
                case "external": bad["model"]!["volumes"]!["work"]!["external"] = true; break;
                case "nocopy": mounts.Single(x => x!["source"]!.GetValue<string>() == "runner-nuget-packages")!["volume"]!["nocopy"] = false; break;
                case "kind": mounts[0]!["type"] = "tmpfs"; break;
                case "options": mounts[0]!["volume"]!["subpath"] = "foreign"; break;
                case "missing": mounts.RemoveAt(0); break;
                case "extra": mounts.Add(new JsonObject { ["type"]="bind", ["source"]="/foreign", ["target"]="/foreign" }); break;
                case "duplicate": mounts[1]!["target"] = mounts[0]!["target"]!.DeepClone(); break;
                case "read-only-type": mounts[0]!["read_only"] = "false"; break;
            }
            add(variant, false, bad);
        }
    });

    [Test, ParallelLimiter<ProcessSpawnLimit>]
    public Task C994_Named_volume_identity_and_topology_are_exact() => Prove("c994-volume-mount c994-volume-owner", (_, good, add) => {
        foreach (var field in new[] { "Name", "Source", "Destination", "Type", "RW", "missing-RW", "string-RW", "duplicate", "owner", "role", "generation", "anonymous", "mountpoint" }) {
            var bad = good.DeepClone().AsObject(); var mounts = bad["owned"]![0]!["Mounts"]!.AsArray(); var mount = mounts[0]!.AsObject();
            if (field == "missing-RW") mount.Remove("RW");
            else if (field == "string-RW") mount["RW"] = "true";
            else if(field=="generation")bad["volumes"]![mount["Name"]!.GetValue<string>()]!["CreatedAt"]=null;
            else if(field=="anonymous")mount["Name"]="";
            else if(field=="mountpoint")bad["volumes"]![mount["Name"]!.GetValue<string>()]!["Mountpoint"]="/foreign";
            else if (field == "RW") mount["RW"] = false;
            else if (field == "duplicate") mounts.Add(mount.DeepClone());
            else if (field is "owner" or "role") bad["volumes"]![mount["Name"]!.GetValue<string>()]!["Labels"]!["com.docker.compose." + (field == "owner" ? "project" : "volume")] = "foreign";
            else mount[field] = "foreign";
            add(field, false, bad);
        }
        var reordered = good.DeepClone().AsObject();
        foreach (var container in reordered["owned"]!.AsArray()) {
            var mounts = container!["Mounts"]!.AsArray(); var copy = mounts.Reverse().Select(x => x!.DeepClone()).ToArray();
            mounts.Clear(); foreach (var m in copy) mounts.Add(m);
        }
        add("unordered", true, reordered);
    });

    [Test, ParallelLimiter<ProcessSpawnLimit>]
    public Task C994_Bind_sources_are_canonical_and_typed() => Prove("c994-bind-proof", (f, good, add) => {
        foreach (var variant in new[] { "source", "target", "rw", "missing", "extra", "directory-for-file", "symlink", "ancestor", "file-for-directory", "readlink", "stat", "provider-missing" }) {
            var bad = good.DeepClone().AsObject(); var mounts = bad["owned"]![0]!["Mounts"]!.AsArray();
            var bind = mounts.Single(x => x!["Destination"]!.GetValue<string>() == "/run/antiphon/gitconfig")!;
            switch (variant) {
                case "source": bind["Source"] = f.Root + "/missing"; break;
                case "target": bind["Destination"] = "/unexpected"; break;
                case "rw": bind["RW"] = true; break;
                case "missing": mounts.Remove(bind); break;
                case "extra": mounts.Add(new JsonObject { ["Type"]="bind", ["Source"]=f.Root, ["Destination"]="/foreign", ["RW"]=true }); break;
                case "directory-for-file": File.Delete(f.Root + "/gitconfig"); Directory.CreateDirectory(f.Root + "/gitconfig"); break;
                case "readlink": case "stat": bad["metadataFault"]=variant;break;
                case "provider-missing": mounts.Remove(mounts.Single(x=>x!["Destination"]!.GetValue<string>()=="/state/codex"));break;
                case "file-for-directory":
                    var file=f.Root+"/not-directory";File.WriteAllText(file,"inert");bad["codexOverride"]=file;
                    foreach(var service in new[]{"session-runner","state-init"}){
                        bad["model"]!["services"]![service]!["volumes"]!.AsArray().Single(x=>x!["source"]!.GetValue<string>()==f.Root+"/codex")!["source"]=file;
                        bad["owned"]![service=="session-runner"?0:1]!["Mounts"]!.AsArray().Single(x=>x!["Source"]!.GetValue<string>()==f.Root+"/codex")!["Source"]=file;
                    }break;
                case "ancestor":
                    var parent=f.Root+"/parent-link";if(!Directory.Exists(parent))Directory.CreateSymbolicLink(parent,f.Root);
                    var child=parent+"/codex";bad["codexOverride"]=child;
                    foreach(var service in new[]{"session-runner","state-init"}){
                        bad["model"]!["services"]![service]!["volumes"]!.AsArray().Single(x=>x!["source"]!.GetValue<string>()==f.Root+"/codex")!["source"]=child;
                        bad["owned"]![service=="session-runner"?0:1]!["Mounts"]!.AsArray().Single(x=>x!["Source"]!.GetValue<string>()==f.Root+"/codex")!["Source"]=child;
                    }break;
                case "symlink":
                    var link = f.Root + "/codex-link";
                    if (!Directory.Exists(link)) Directory.CreateSymbolicLink(link, f.Root + "/codex");
                    bad["codexOverride"] = link;
                    foreach (var service in new[] { "session-runner", "state-init" }) {
                        bad["model"]!["services"]![service]!["volumes"]!.AsArray().Single(x => x!["source"]!.GetValue<string>() == f.Root + "/codex")!["source"] = link;
                        bad["owned"]![service == "session-runner" ? 0 : 1]!["Mounts"]!.AsArray().Single(x => x!["Source"]!.GetValue<string>() == f.Root + "/codex")!["Source"] = link;
                    }
                    break;
            }
            // File-kind faults are represented by a separately owned source so
            // valid controls still see the original metadata at execution time.
            if (variant == "directory-for-file") {
                Directory.Delete(f.Root + "/gitconfig"); File.WriteAllText(f.Root + "/gitconfig", "inert");
                bad["model"]!["services"]!["session-runner"]!["volumes"]!.AsArray().Single(x => x!["target"]!.GetValue<string>() == "/run/antiphon/gitconfig")!["source"] = f.Root;
                bad["gitOverride"] = f.Root; bind["Source"] = f.Root;
            }
            add(variant, false, bad);
        }
    });

    [Test, ParallelLimiter<ProcessSpawnLimit>]
    public Task C994_Secrets_match_read_only_files() => Prove("c994-secret-proof", (_, good, add) => {
        foreach (var variant in new[] { "source", "target", "external", "rw", "missing-rw", "string-rw", "missing", "duplicate" }) {
            var bad = good.DeepClone().AsObject(); var model = bad["model"]!; var mounts = bad["owned"]![0]!["Mounts"]!.AsArray();
            var secret = mounts.Single(x => x!["Destination"]!.GetValue<string>() == "/run/secrets/phone-home")!.AsObject();
            switch (variant) {
                case "source": model["secrets"]!["phone-home"]!["file"] = "/foreign"; break;
                case "target": model["services"]!["session-runner"]!["secrets"]![0]!["target"] = "/foreign"; break;
                case "external": model["secrets"]!["phone-home"]!["external"] = true; break;
                case "rw": secret["RW"] = true; break;
                case "missing-rw": secret.Remove("RW"); break;
                case "string-rw": secret["RW"] = "false"; break;
                case "missing": mounts.Remove(secret); break;
                case "duplicate": mounts.Add(secret.DeepClone()); break;
            }
            add(variant, false, bad);
        }
        var shortTarget = good.DeepClone().AsObject();
        foreach (var secret in shortTarget["model"]!["services"]!["session-runner"]!["secrets"]!.AsArray()) secret!["target"] = secret["source"]!.DeepClone();
        add("short-target", true, shortTarget);
    });

    [Test, ParallelLimiter<ProcessSpawnLimit>]
    public Task C994_Tmpfs_configuration_survives_exit() => Prove("c994-tmpfs-proof", (_, good, add) => {
        foreach (var variant in new[] { "running", "inspected", "mount-encoding", "missing", "contradiction", "extra", "readonly", "options", "impostor", "state-init-tmpfs", "duplicate-config", "malformed-options" }) {
            var bad = good.DeepClone().AsObject(); var container = bad["owned"]![0]!;
            switch (variant) {
                case "running": container["State"]!["Running"] = true; container["State"]!["Status"] = "running"; break;
                case "inspected": container["Mounts"]!.AsArray().Add(new JsonObject { ["Type"]="tmpfs", ["Destination"]="/run/antiphon", ["RW"]=true, ["Source"]="" }); break;
                case "mount-encoding": container["HostConfig"]!["Tmpfs"] = new JsonObject(); container["HostConfig"]!["Mounts"] = new JsonArray(new JsonObject { ["Type"]="tmpfs", ["Target"]="/run/antiphon", ["ReadOnly"]=false, ["TmpfsOptions"]=new JsonObject() }); break;
                case "missing": container["HostConfig"] = new JsonObject(); break;
                case "contradiction": container["HostConfig"]!["Mounts"] = new JsonArray(new JsonObject { ["Type"]="tmpfs", ["Target"]="/run/antiphon", ["ReadOnly"]=true }); break;
                case "extra": container["HostConfig"]!["Tmpfs"]!["/foreign"] = ""; break;
                case "readonly": container["HostConfig"]!["Tmpfs"]!["/run/antiphon"] = "ro"; break;
                case "options": container["HostConfig"]!["Tmpfs"]!["/run/antiphon"] = "size=123"; break;
                case "state-init-tmpfs":bad["owned"]![1]!["HostConfig"]!["Tmpfs"]!["/foreign"]="";break;
                case "duplicate-config":container["HostConfig"]!["Mounts"]=new JsonArray(new JsonObject{["Type"]="tmpfs",["Target"]="/run/antiphon"},new JsonObject{["Type"]="tmpfs",["Target"]="/run/antiphon"});break;
                case "malformed-options":container["HostConfig"]!["Tmpfs"]!["/run/antiphon"]=1;break;
                case "impostor": container["Mounts"]!.AsArray().Add(new JsonObject { ["Type"]="bind", ["Source"]="/foreign", ["Destination"]="/run/antiphon", ["RW"]=true }); break;
            }
            add(variant, variant is "running" or "inspected" or "mount-encoding", bad);
        }
    });
}
