// The CARD-1405 snapshot's modes (unit 53; see PostgresSnapshot.cs): Invoke-PostgresSnapshot and the age publish, the age drill,
// the plain publish (desktop and server2), the plain server2 drill and their shared scratch guard, in the script's order. ASCII only.
#nullable disable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Antiphon.Nightly
{
    public static partial class PostgresSnapshot
    {
        /// <summary>
        /// The command (Invoke-PostgresSnapshot): the result line's code is the exit code (1 when an unexpected failure ends the
        /// run before one is written: one stderr line). seams null: production.
        /// </summary>
        public static int Run(PostgresSnapshotOptions options, NightlyHost host, PostgresSnapshotSeams seams = null)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (host == null) throw new ArgumentNullException(nameof(host));
            var session = new PostgresSnapshotSession(host, seams, options.StaleRunMinutes);
            try
            {
                Invoke(options, session);
                return session.Exit;
            }
            catch (Exception e)
            {
                host.Error.WriteLine("postgres-snapshot: " + e.Message.Replace("\r", " ").Replace("\n", " "));
                return 1;
            }
        }

        private static void Invoke(PostgresSnapshotOptions o, PostgresSnapshotSession s)
        {
            var planOnly = !o.Execute;
            if (o.WhatIf && o.Execute) { s.Result(true, "ambiguous-mode", ""); return; }
            if (o.WhatIf) planOnly = true;
            if (o.Target == "server2" && !o.Plain)
            {
                // The server2 source is plain-only; the age path keeps the desktop.
                s.Result(planOnly, "ambiguous-mode", "");
                return;
            }
            if (o.Plain && o.RestoreDrill && o.Target == "server2") { PlainDrillMode(o, s, planOnly); return; }
            if (o.Plain && o.RestoreDrill)
            {
                s.Line(PlainBanner);
                s.Result(planOnly, "plain-drill-unsupported", "");
                return;
            }
            if (o.Plain) { PlainMode(o, s, planOnly); return; }
            var sync = string.IsNullOrWhiteSpace(o.SyncRoot) ? DefaultSyncRoot : o.SyncRoot;
            var dest = string.IsNullOrWhiteSpace(o.Destination) ? Path.Combine(sync, "antiphon-postgres") : o.Destination;
            var stage = string.IsNullOrWhiteSpace(o.StagingPath) ? s.DefaultStaging() : o.StagingPath;
            var container = BareName(o.Container);
            var database = BareName(o.Database);
            var scratchContainer = BareName(o.ScratchContainer);
            var scratchDatabase = BareName(o.ScratchDatabase);
            var scratchVolume = BareName(o.ScratchVolume);
            var sourceVolume = BareName(o.SourceVolume);

            var namesOk = IsToken(container) && IsToken(database) && IsToken(scratchContainer) && IsToken(scratchDatabase) && IsToken(scratchVolume)
                && IsToken(sourceVolume) && IsImage(o.ScratchImage);
            if (!namesOk || o.Retain < 1 || o.MinimumDumpBytes < 1) { s.Result(planOnly, "bad-name", ""); return; }
            var drillGuard = DrillRefused(container, database, scratchContainer, scratchDatabase, scratchVolume, sourceVolume);
            if (!string.IsNullOrWhiteSpace(drillGuard)) { s.Result(planOnly, "drill-refused", "guard=" + drillGuard); return; }
            if (!Directory.Exists(sync)) { s.Result(planOnly, "missing-sync-root", ""); return; }
            if (PostgresSnapshotSession.ContainsLink(stage)) { s.Result(planOnly, "staging-link", ""); return; }
            if (PathInside(sync, stage)) { s.Result(planOnly, "staging-inside-sync", ""); return; }
            if (PathInside(dest, stage)) { s.Result(planOnly, "staging-inside-destination", ""); return; }

            if (o.RestoreDrill)
            {
                var identity = ReferencePath(s, o.IdentityFile, o.IdentityRef);
                if (string.IsNullOrWhiteSpace(identity)) { s.Result(planOnly, "missing-identity", ""); return; }
                var identityState = PostgresSnapshotSession.OutsideFile(identity, sync, dest, "identity-inside-sync", "identity-link");
                if (identityState == "missing") { s.Result(planOnly, "missing-identity", ""); return; }
                if (identityState != "ok") { s.Result(planOnly, identityState, ""); return; }
                if (planOnly)
                {
                    s.Line("step drill: decrypt the newest snapshot with age -d -i <identity-file>");
                    s.Line("step drill: pg_restore --list must include TABLE DATA and the plaintext must be at least " + Num(o.MinimumDumpBytes) + " bytes");
                    s.Line("step drill: pg_restore into scratch container " + scratchContainer + " database " + scratchDatabase + " volume " + scratchVolume);
                    s.Line("step drill: leave the production database and volume untouched");
                    s.Line("step drill: remove the labelled scratch container and volume unless -KeepScratch");
                    s.Result(true, "ok", "drill=1 scratch=" + scratchContainer);
                    return;
                }
                var age = s.Resolve("age");
                var docker = s.Resolve("docker");
                if (age == null) { s.Result(false, "missing-age", ""); return; }
                if (docker == null) { s.Result(false, "missing-docker", ""); return; }
                var rootState = s.StagingRoot(stage);
                if (rootState != "") { s.Result(false, rootState, ""); return; }
                var daemon = s.DockerDaemon(docker, o.AllowRemoteDocker);
                if (daemon != "") { s.Result(false, daemon, ""); return; }
                s.DaemonWarning();
                if (!s.InitializeStaging(stage)) { s.Result(false, "staging-mode", ""); return; }
                var run = s.RunStaging;
                if (string.IsNullOrWhiteSpace(run)) { s.Result(false, "staging-mode", ""); return; }
                try
                {
                    var drilled = Drill(s, run, dest, identity, scratchContainer, scratchDatabase, scratchVolume, o.ScratchImage, docker, age, container, database,
                        sourceVolume, o.MinimumDumpBytes, o.KeepScratch);
                    s.Result(false, drilled.Reason, drilled.Detail);
                }
                finally
                {
                    s.RemoveStaging(run);
                }
                return;
            }

            var recipient = ReferencePath(s, o.RecipientFile, o.RecipientRef);
            if (string.IsNullOrWhiteSpace(recipient)) { s.Result(planOnly, "missing-recipient", ""); return; }
            var recipientState = PostgresSnapshotSession.OutsideFile(recipient, sync, dest, "recipient-inside-sync", "recipient-link");
            if (recipientState == "missing") { s.Result(planOnly, "missing-recipient", ""); return; }
            if (recipientState != "ok") { s.Result(planOnly, recipientState, ""); return; }

            if (planOnly)
            {
                s.Line("step dump: docker exec " + container + " pg_dump -Fc --no-owner --no-acl -U antiphon -d " + database + " -f /tmp/antiphon-snapshot.dump");
                s.Line("step verify: pg_restore --list must include TABLE DATA and the dump must be at least " + Num(o.MinimumDumpBytes) + " bytes");
                s.Line("step encrypt: age -R <recipient-file> -o <staging>.dump.age");
                s.Line("step copy: move the ciphertext into the snapshot directory as a new antiphon-<utc>.dump.age file");
                s.Line("step retention: keep " + Num(o.Retain) + " dump.age files and prune only after the new file is verified");
                s.Result(true, "ok", "dump=1 encrypt=1 copy=1 retention=" + Num(o.Retain));
                return;
            }

            var ageCommand = s.Resolve("age");
            var dockerCommand = s.Resolve("docker");
            if (ageCommand == null) { s.Result(false, "missing-age", ""); return; }
            if (dockerCommand == null) { s.Result(false, "missing-docker", ""); return; }
            var root = s.StagingRoot(stage);
            if (root != "") { s.Result(false, root, ""); return; }
            var daemonState = s.DockerDaemon(dockerCommand, o.AllowRemoteDocker);
            if (daemonState != "") { s.Result(false, daemonState, ""); return; }
            s.DaemonWarning();
            if (!s.InitializeStaging(stage)) { s.Result(false, "staging-mode", ""); return; }
            var runDir = s.RunStaging;
            if (string.IsNullOrWhiteSpace(runDir)) { s.Result(false, "staging-mode", ""); return; }
            try
            {
                var published = Publish(s, container, database, runDir, dest, recipient, o.Retain, dockerCommand, ageCommand, o.MinimumDumpBytes);
                s.Result(false, published.Reason, published.Detail);
            }
            finally
            {
                s.RemoveStaging(runDir);
            }
        }

        // Resolve-SnapshotReferencePath: the file, else the path the environment variable names, else null.
        private static string ReferencePath(PostgresSnapshotSession s, string file, string reference)
        {
            if (!string.IsNullOrWhiteSpace(file)) return file;
            if (string.IsNullOrWhiteSpace(reference)) return null;
            var value = s.Variable(reference);
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }

        /// <summary>Invoke-SnapshotPublish: the age path's dump, verify, encrypt, move and retention.</summary>
        public static SnapshotStep Publish(PostgresSnapshotSession s, string sourceContainer, string sourceDatabase, string stage, string destDir, string recipientPath,
            int keep, string docker, string age, long minimumBytes)
        {
            var stamp = s.Stamp();
            var containerDump = "/tmp/antiphon-snapshot-" + stamp + ".dump";
            var hostDump = Path.Combine(stage, "antiphon-" + stamp + ".dump");
            var hostCipher = Path.Combine(stage, "antiphon-" + stamp + ".dump.age");
            var destName = "antiphon-" + stamp + ".dump.age";
            var destFile = Path.Combine(destDir, destName);
            var published = false;
            try
            {
                var dump = s.Native(docker, new[] { "exec", sourceContainer, "pg_dump", "-Fc", "--no-owner", "--no-acl", "-U", "antiphon", "-d", sourceDatabase, "-f", containerDump });
                if (dump.Exit != 0) return new SnapshotStep(4, "dump-failed");
                var list = s.Native(docker, new[] { "exec", sourceContainer, "pg_restore", "--list", containerDump });
                var listed = list.Lines.Where(line => !string.IsNullOrWhiteSpace(line)).ToArray();
                if (list.Exit != 0 || !HasTableData(listed)) return new SnapshotStep(5, "verify-failed");
                var sizeText = s.Native(docker, new[] { "exec", sourceContainer, "wc", "-c", containerDump });
                var containerBytes = FirstNumber(sizeText);
                if (sizeText.Exit != 0 || containerBytes < minimumBytes) return new SnapshotStep(5, "verify-failed");
                var copied = s.Native(docker, new[] { "cp", sourceContainer + ":" + containerDump, hostDump });
                if (copied.Exit != 0 || !PostgresSnapshotSession.Exists(hostDump)) return new SnapshotStep(4, "dump-failed");
                var hostBytes = new FileInfo(hostDump).Length;
                if (hostBytes != containerBytes) return new SnapshotStep(5, "verify-failed");
                var encrypted = s.Native(age, new[] { "-R", recipientPath, "-o", hostCipher, hostDump });
                if (encrypted.Exit != 0 || !PostgresSnapshotSession.Exists(hostCipher)) return new SnapshotStep(6, "encrypt-failed");
                var cipherBytes = new FileInfo(hostCipher).Length;
                if (cipherBytes <= 0) return new SnapshotStep(6, "encrypt-failed");
                if (PostgresSnapshotSession.Exists(destFile)) return new SnapshotStep(7, "copy-failed");
                if (!PostgresSnapshotSession.Exists(destDir)) Directory.CreateDirectory(destDir);
                File.Move(hostCipher, destFile);
                published = true;
                var destBytes = new FileInfo(destFile).Length;
                if (destBytes != cipherBytes)
                {
                    PostgresSnapshotSession.RemovePartial(destFile);
                    published = false;
                    return new SnapshotStep(7, "copy-failed");
                }
                var prune = s.UpdateRetention(destDir, keep, destName);
                if (!prune.Ok)
                {
                    PostgresSnapshotSession.WriteStatus(destDir, stamp, 8, destBytes);
                    return new SnapshotStep(8, "prune-failed");
                }
                PostgresSnapshotSession.WriteStatus(destDir, stamp, 0, destBytes);
                return new SnapshotStep(0, "ok", "bytes=" + Num(destBytes) + " kept=" + Num(prune.Kept));
            }
            finally
            {
                s.Native(docker, new[] { "exec", sourceContainer, "rm", "-f", containerDump });
                PostgresSnapshotSession.RemovePartial(hostDump);
                if (!published) PostgresSnapshotSession.RemovePartial(hostCipher);
            }
        }

        // The first run of digits on wc -c's first line; -1 without one.
        private static long FirstNumber(SnapshotNative sizeText)
        {
            if (sizeText.Lines.Count < 1) return -1;
            var match = System.Text.RegularExpressions.Regex.Match(sizeText.Lines[0] ?? "", @"(\d+)");
            return match.Success && long.TryParse(match.Groups[1].Value, out var value) ? value : -1;
        }

        // ---- the drills ----

        private sealed class DrillState
        {
            public bool Owned;
            public bool VolumeCreated;
            public string SourceId = "";
            public string TargetId = "";
            public IReadOnlyList<string> ProtectedMounts = Array.Empty<string>();
        }

        // Invoke-SnapshotDrillScratch: the guard prefix both drills share. Null when the scratch container is ready, else the refusal.
        private static SnapshotStep DrillScratch(PostgresSnapshotSession s, DrillState state, string docker, string drillContainer, string drillDatabase,
            string drillVolume, string image, string sourceContainer, string sourceDatabase, string sourceVolume)
        {
            if (NameMatch(drillDatabase, new[] { sourceDatabase, "antiphon" })) return new SnapshotStep(9, "drill-refused", "guard=database");
            var source = s.ContainerFact(docker, sourceContainer);
            if (!source.Found || !source.Parse || string.IsNullOrWhiteSpace(source.Id)) return new SnapshotStep(9, "drill-refused", "guard=source-unknown");
            state.SourceId = source.Id;
            state.ProtectedMounts = s.ProtectedMounts(docker, source, sourceContainer).ToArray();
            var productionIds = s.ProductionIds(docker, sourceContainer, source).ToArray();
            if (IdPrefixHit(drillContainer, productionIds)) return new SnapshotStep(9, "drill-refused", "guard=id-prefix");
            var existing = s.ContainerFact(docker, drillContainer);
            if (existing.Found && !existing.Parse) return new SnapshotStep(9, "drill-refused", "guard=inspect");
            if (existing.Found)
            {
                if (IdPrefixHit(drillContainer, new[] { existing.Id }.Concat(productionIds))) return new SnapshotStep(9, "drill-refused", "guard=id-prefix");
                var guard = ReuseGuard(existing, state.SourceId, source.Name, sourceVolume, productionIds, state.ProtectedMounts);
                if (guard != "ok") return new SnapshotStep(9, "drill-refused", "guard=" + guard);
                var gate = s.VolumeGate(docker, drillVolume, sourceVolume, state.ProtectedMounts, existing.Id, false);
                if (gate != "ok") return new SnapshotStep(9, "drill-refused", "guard=" + gate);
                if (!existing.Mounts.Any(mount => NameMatch(drillVolume, new[] { mount }))) return new SnapshotStep(9, "drill-refused", "guard=volume-mismatch");
                state.TargetId = existing.Id;
            }
            else
            {
                var gate = s.VolumeGate(docker, drillVolume, sourceVolume, state.ProtectedMounts, "", true);
                if (gate == "absent")
                {
                    var made = s.Native(docker, new[] { "volume", "create", "--label", DrillLabel + "=1", drillVolume });
                    if (made.Exit != 0) return new SnapshotStep(9, "drill-failed", "");
                    var check = s.VolumeFact(docker, drillVolume);
                    if (!check.Found || !check.Parse || check.Label != "1") return new SnapshotStep(9, "drill-failed", "");
                    state.VolumeCreated = true;
                }
                else if (gate != "ok")
                {
                    return new SnapshotStep(9, "drill-refused", "guard=" + gate);
                }
                var started = s.Native(docker, new[]
                {
                    "run", "-d", "--name", drillContainer, "--label", DrillLabel + "=1", "--network", "none", "-v", drillVolume + ":/var/lib/postgresql/data",
                    "-e", "POSTGRES_HOST_AUTH_METHOD=trust", "-e", "POSTGRES_USER=antiphon", image,
                });
                if (started.Exit != 0) return new SnapshotStep(9, "drill-failed", "");
                var created = s.ContainerFact(docker, drillContainer);
                var createdGuard = ReuseGuard(created, state.SourceId, source.Name, sourceVolume, productionIds, state.ProtectedMounts);
                if (createdGuard != "ok")
                {
                    if (created.Found && created.Parse && created.Label == "1" && !SameId(created.Id, state.SourceId))
                    {
                        state.Owned = true;
                        state.TargetId = created.Id;
                    }
                    return new SnapshotStep(9, "drill-refused", "guard=" + createdGuard);
                }
                state.TargetId = created.Id;
            }
            if (string.IsNullOrWhiteSpace(state.TargetId)) return new SnapshotStep(9, "drill-refused", "guard=inspect");
            state.Owned = true;
            return null;
        }

        // Complete-SnapshotDrillScratch. The script's drill functions returned their result and these cleanup warnings on one output
        // stream, and the caller kept only the result's Reason and Detail, so the warnings were never printed; they are not here.
        private static void CompleteDrillScratch(PostgresSnapshotSession s, DrillState state, string docker, string drillVolume, string sourceVolume, bool keep)
        {
            if (state.Owned && !keep) RemoveDrillContainer(s, docker, drillVolume, state.SourceId, sourceVolume, state.TargetId, state.ProtectedMounts);
            else if (state.VolumeCreated && !state.Owned) RemoveCreatedVolume(s, docker, drillVolume, sourceVolume, state.ProtectedMounts);
        }

        // Remove-SnapshotCreatedVolume: only an unused labelled volume the drill created goes.
        private static void RemoveCreatedVolume(PostgresSnapshotSession s, string docker, string volume, string sourceVolume, IReadOnlyList<string> sourceMounts)
        {
            var denied = ProductionVolumes.Concat(new[] { sourceVolume }).Concat(sourceMounts).ToArray();
            if (NameMatch(volume, denied)) return;
            var fact = s.VolumeFact(docker, volume);
            if (!fact.Found || !fact.Parse || fact.Label != "1") return;
            var users = s.VolumeUsers(docker, volume);
            if (!users.Ok || users.Ids.Count > 0) return;
            s.Native(docker, new[] { "volume", "rm", volume });
        }

        // Remove-SnapshotDrillContainer: the labelled scratch container by its id, then its labelled volume.
        private static void RemoveDrillContainer(PostgresSnapshotSession s, string docker, string drillVolume, string sourceId, string sourceVolume, string expectedId,
            IReadOnlyList<string> sourceMounts)
        {
            if (string.IsNullOrWhiteSpace(expectedId)) return;
            var fact = s.ContainerFact(docker, expectedId);
            var same = fact.Found && fact.Parse && SameId(fact.Id, expectedId) && fact.Label == "1";
            var safeName = !NameMatch(fact.Name, ProductionContainers);
            var notSource = !SameId(fact.Id, sourceId);
            if (!same || !safeName || !notSource) return;
            var mounts = fact.Mounts.ToArray();
            s.Native(docker, new[] { "rm", "-f", fact.Id });
            var mounted = mounts.Any(mount => NameMatch(drillVolume, new[] { mount }));
            var volume = s.VolumeFact(docker, drillVolume);
            var denied = ProductionVolumes.Concat(new[] { sourceVolume }).Concat(sourceMounts).ToArray();
            if (!mounted || !volume.Found || !volume.Parse || volume.Label != "1" || NameMatch(drillVolume, denied)) return;
            s.Native(docker, new[] { "volume", "rm", drillVolume });
        }

        /// <summary>Invoke-SnapshotDrill: the age drill (decrypt the newest exact-name snapshot, list, restore into the scratch).</summary>
        private static SnapshotStep Drill(PostgresSnapshotSession s, string stage, string destDir, string identityPath, string drillContainer, string drillDatabase,
            string drillVolume, string image, string docker, string age, string sourceContainer, string sourceDatabase, string sourceVolume, long minimumBytes, bool keep)
        {
            var state = new DrillState();
            var plain = Path.Combine(stage, "antiphon-drill-plain.dump");
            try
            {
                var refused = DrillScratch(s, state, docker, drillContainer, drillDatabase, drillVolume, image, sourceContainer, sourceDatabase, sourceVolume);
                if (refused != null) return refused;
                var targetId = state.TargetId;
                var newest = NewestDump(destDir, DumpPattern);
                if (newest == null) return new SnapshotStep(9, "missing-snapshot", "");
                var decoded = s.Native(age, new[] { "-d", "-i", identityPath, "-o", plain, newest });
                if (decoded.Exit != 0 || !PostgresSnapshotSession.Exists(plain)) return new SnapshotStep(9, "drill-failed", "");
                var plainBytes = new FileInfo(plain).Length;
                if (plainBytes < minimumBytes) return new SnapshotStep(5, "verify-failed", "");
                var listed = s.Native(docker, new[] { "run", "--rm", "--network", "none", "-v", stage + ":/work:ro", "--entrypoint", "pg_restore", image, "--list",
                    "/work/antiphon-drill-plain.dump" });
                var toc = listed.Lines.Where(line => !string.IsNullOrWhiteSpace(line)).ToArray();
                if (listed.Exit != 0 || !HasTableData(toc)) return new SnapshotStep(5, "verify-failed", "");
                if (!Ready(s, docker, targetId, 12)) return new SnapshotStep(9, "drill-failed", "");
                if (NameMatch(drillDatabase, new[] { "antiphon", sourceDatabase })) return new SnapshotStep(9, "drill-refused", "guard=database");
                if (s.Native(docker, new[] { "exec", targetId, "dropdb", "--if-exists", "-U", "antiphon", drillDatabase }).Exit != 0) return new SnapshotStep(9, "drill-failed", "");
                if (s.Native(docker, new[] { "exec", targetId, "createdb", "-U", "antiphon", drillDatabase }).Exit != 0) return new SnapshotStep(9, "drill-failed", "");
                const string remote = "/tmp/antiphon-drill.dump";
                if (s.Native(docker, new[] { "cp", plain, targetId + ":" + remote }).Exit != 0) return new SnapshotStep(9, "drill-failed", "");
                var restored = s.Native(docker, new[] { "exec", targetId, "pg_restore", "--no-owner", "--no-acl", "--exit-on-error", "-U", "antiphon", "-d", drillDatabase, remote });
                s.Native(docker, new[] { "exec", targetId, "rm", "-f", remote });
                if (restored.Exit != 0) return new SnapshotStep(9, "drill-failed", "");
                return new SnapshotStep(0, "ok", "drill=1 bytes=" + Num(plainBytes));
            }
            finally
            {
                PostgresSnapshotSession.RemovePartial(plain);
                CompleteDrillScratch(s, state, docker, drillVolume, sourceVolume, keep);
            }
        }

        // pg_isready, once a second, up to tries times.
        private static bool Ready(PostgresSnapshotSession s, string docker, string targetId, int tries)
        {
            for (var attempt = 0; attempt < tries; attempt++)
            {
                if (s.Native(docker, new[] { "exec", targetId, "pg_isready", "-U", "antiphon" }).Exit == 0) return true;
                s.SleepOneSecond();
            }
            return false;
        }

        // Get-ChildItem -Filter ... -File | the exact pattern | Sort-Object Name -Descending | the first.
        private static string NewestDump(string directory, string pattern)
        {
            if (!Directory.Exists(directory)) return null;
            FileInfo[] files;
            try { files = new DirectoryInfo(directory).GetFiles(); } catch (Exception) { return null; }
            return files.Where(file => (file.Attributes & FileAttributes.Hidden) == 0 && Matches(file.Name, pattern))
                .OrderByDescending(file => file.Name, StringComparer.OrdinalIgnoreCase).Select(file => file.FullName).FirstOrDefault();
        }

        /// <summary>
        /// Invoke-SnapshotPlainDrill (plan D-6/D-7): the newest plain dump copied into the labelled scratch container on server2 (no
        /// bind mount), listed and restored there, the public tables counted in the scratch database only.
        /// </summary>
        private static SnapshotStep PlainDrill(PostgresSnapshotSession s, string dumpPath, string drillContainer, string drillDatabase, string drillVolume, string image,
            string docker, string sourceContainer, string sourceDatabase, string sourceVolume, long minimumBytes, bool keep)
        {
            var watch = s.StartWatch();
            var state = new DrillState();
            const string remote = "/tmp/antiphon-drill.dump";
            try
            {
                var dumpBytes = new FileInfo(dumpPath).Length;
                if (dumpBytes < minimumBytes) return new SnapshotStep(5, "verify-failed", "");
                var refused = DrillScratch(s, state, docker, drillContainer, drillDatabase, drillVolume, image, sourceContainer, sourceDatabase, sourceVolume);
                if (refused != null) return refused;
                var targetId = state.TargetId;
                if (!Ready(s, docker, targetId, 30)) return new SnapshotStep(9, "drill-failed", "");
                if (NameMatch(drillDatabase, new[] { "antiphon", sourceDatabase })) return new SnapshotStep(9, "drill-refused", "guard=database");
                if (s.Native(docker, new[] { "exec", targetId, "dropdb", "--if-exists", "-U", "antiphon", drillDatabase }).Exit != 0) return new SnapshotStep(9, "drill-failed", "");
                if (s.Native(docker, new[] { "exec", targetId, "createdb", "-U", "antiphon", drillDatabase }).Exit != 0) return new SnapshotStep(9, "drill-failed", "");
                if (s.Native(docker, new[] { "cp", dumpPath, targetId + ":" + remote }).Exit != 0) return new SnapshotStep(9, "drill-failed", "");
                var listed = s.Native(docker, new[] { "exec", targetId, "pg_restore", "--list", remote });
                var toc = listed.Lines.Where(line => !string.IsNullOrWhiteSpace(line)).ToArray();
                if (listed.Exit != 0 || !HasTableData(toc)) return new SnapshotStep(5, "verify-failed", "");
                var restored = s.Native(docker, new[] { "exec", targetId, "pg_restore", "--no-owner", "--no-acl", "--exit-on-error", "-U", "antiphon", "-d", drillDatabase, remote });
                if (restored.Exit != 0) return new SnapshotStep(9, "drill-failed", "");
                var counted = s.Native(docker, new[] { "exec", targetId, "psql", "-U", "antiphon", "-d", drillDatabase, "-tAc",
                    "select count(*) from information_schema.tables where table_schema = 'public' and table_type = 'BASE TABLE'" });
                var countLines = counted.Lines.Where(line => !string.IsNullOrWhiteSpace(line)).ToArray();
                if (counted.Exit != 0 || countLines.Length != 1 || !Matches(countLines[0].Trim(), "^[0-9]{1,6}$")) return new SnapshotStep(9, "drill-failed", "");
                var tables = int.Parse(countLines[0].Trim(), System.Globalization.CultureInfo.InvariantCulture);
                var seconds = Seconds(watch());
                return new SnapshotStep(0, "ok", "drill=1 bytes=" + Num(dumpBytes) + " tables=" + Num(tables) + " seconds=" + Num(seconds));
            }
            finally
            {
                if (!string.IsNullOrWhiteSpace(state.TargetId) && state.Owned) s.Native(docker, new[] { "exec", state.TargetId, "rm", "-f", remote });
                CompleteDrillScratch(s, state, docker, drillVolume, sourceVolume, keep);
            }
        }

        // Invoke-SnapshotPlainDrillMode: --plain --restore-drill --target server2.
        private static void PlainDrillMode(PostgresSnapshotOptions o, PostgresSnapshotSession s, bool planOnly)
        {
            s.Line(PlainBanner);
            if (o.ContainerGiven) { s.Result(planOnly, "ambiguous-mode", ""); return; }
            var source = Server2Container;
            var sourceVolume = Server2Volume;
            s.DockerContext = Server2Context;
            var dest = string.IsNullOrWhiteSpace(o.Destination) ? PlainDestination : o.Destination;
            var database = BareName(o.Database);
            var scratch = BareName(o.ScratchContainer);
            var scratchDb = BareName(o.ScratchDatabase);
            var scratchVolume = BareName(o.ScratchVolume);
            var namesOk = IsToken(database) && IsToken(scratch) && IsToken(scratchDb) && IsToken(scratchVolume);
            if (!namesOk || o.MinimumDumpBytes < 1) { s.Result(planOnly, "bad-name", ""); return; }
            var drillGuard = DrillRefused(source, database, scratch, scratchDb, scratchVolume, sourceVolume);
            if (!string.IsNullOrWhiteSpace(drillGuard)) { s.Result(planOnly, "drill-refused", "guard=" + drillGuard); return; }
            s.Line("step source: target=server2 container=" + source + " context=" + Server2Context);
            s.Line("step drill: copy the newest antiphon-<utc>.dump from " + dest + " into scratch container " + scratch + " with docker cp (no bind mount)");
            s.Line("step drill: the scratch image is the source container image id");
            s.Line("step drill: pg_restore --list must include TABLE DATA, then pg_restore --exit-on-error into database " + scratchDb + " volume " + scratchVolume
                + " network none");
            s.Line("step drill: count public tables in " + scratchDb + " only; leave the production database and volume untouched");
            s.Line("step drill: remove the labelled scratch container and volume unless -KeepScratch");
            if (planOnly) { s.Result(true, "ok", "drill=1 scratch=" + scratch); return; }

            var newest = NewestDump(dest, PlainDumpPattern);
            if (newest == null) { s.Result(false, "missing-snapshot", ""); return; }
            var docker = s.Resolve("docker");
            if (docker == null) { s.Result(false, "missing-docker", ""); return; }
            var daemon = s.DockerDaemon(docker, false);
            if (daemon != "") { s.Result(false, daemon, ""); return; }
            var sourceFact = s.ContainerFact(docker, source);
            if (!IsServer2Source(sourceFact)) { s.Result(false, "source-mismatch", ""); return; }
            var image = sourceFact.ImageId;
            if (!Matches(image, "^sha256:[0-9a-f]{64}$", caseSensitive: true)) { s.Result(false, "drill-refused", "guard=image"); return; }
            var drilled = PlainDrill(s, newest, scratch, scratchDb, scratchVolume, image, docker, source, database, sourceVolume, o.MinimumDumpBytes, o.KeepScratch);
            s.Result(false, drilled.Reason, drilled.Detail);
        }

        /// <summary>
        /// Invoke-SnapshotPlainPublish: dump, list and size inside the source, copy out, publish through .partial, copy back and list
        /// again, then retention, the optional OneDrive copy and the status (each failure writes its status first).
        /// </summary>
        public static SnapshotStep PlainPublish(PostgresSnapshotSession s, string sourceContainer, string sourceDatabase, string stage, string destDir, int keep,
            string docker, long minimumBytes, bool copyToOneDrive = false, string syncRoot = "", int oneDriveKeep = 7, string targetName = "desktop",
            string serverVersion = "")
        {
            var watch = s.StartWatch();
            var stamp = s.Stamp();
            var containerDump = "/tmp/antiphon-snapshot-" + stamp + ".dump";
            var verifyRemote = "/tmp/antiphon-snapshot-verify-" + stamp + ".dump";
            var hostDump = Path.Combine(stage, "antiphon-" + stamp + ".dump");
            var destName = "antiphon-" + stamp + ".dump";
            var destPartial = Path.Combine(destDir, destName + ".partial");
            var destFile = Path.Combine(destDir, destName);
            var published = false;
            SnapshotStep Failed(int code, string reason)
            {
                PostgresSnapshotSession.WritePlainStatus(destDir, stamp, code, 0, "", Seconds(watch()), 0, target: targetName, serverVersion: serverVersion);
                return new SnapshotStep(code, reason);
            }
            try
            {
                var dump = s.Native(docker, new[] { "exec", sourceContainer, "pg_dump", "-Fc", "--no-owner", "--no-acl", "-U", "antiphon", "-d", sourceDatabase, "-f", containerDump });
                if (dump.Exit != 0) return Failed(4, "dump-failed");
                var list = s.Native(docker, new[] { "exec", sourceContainer, "pg_restore", "--list", containerDump });
                var listed = list.Lines.Where(line => !string.IsNullOrWhiteSpace(line)).ToArray();
                var sizeText = s.Native(docker, new[] { "exec", sourceContainer, "wc", "-c", containerDump });
                var containerBytes = FirstNumber(sizeText);
                var ready = list.Exit == 0 && sizeText.Exit == 0 && PlainVerified(listed, containerBytes, minimumBytes);
                if (!ready) return Failed(5, "verify-failed");
                var copied = s.Native(docker, new[] { "cp", sourceContainer + ":" + containerDump, hostDump });
                if (copied.Exit != 0 || !PostgresSnapshotSession.Exists(hostDump)) return Failed(4, "dump-failed");
                var hostBytes = new FileInfo(hostDump).Length;
                if (hostBytes != containerBytes) return Failed(5, "verify-failed");
                if (PostgresSnapshotSession.Exists(destFile) || PostgresSnapshotSession.Exists(destPartial)) return Failed(7, "copy-failed");
                File.Copy(hostDump, destPartial, true);
                var partialBytes = new FileInfo(destPartial).Length;
                if (partialBytes != hostBytes) return Failed(7, "copy-failed");
                File.Move(destPartial, destFile);
                published = true;
                var sent = s.Native(docker, new[] { "cp", destFile, sourceContainer + ":" + verifyRemote });
                if (sent.Exit != 0)
                {
                    PostgresSnapshotSession.RemovePartial(destFile);
                    published = false;
                    return Failed(5, "verify-failed");
                }
                var again = s.Native(docker, new[] { "exec", sourceContainer, "pg_restore", "--list", verifyRemote });
                s.Native(docker, new[] { "exec", sourceContainer, "rm", "-f", verifyRemote });
                var againLines = again.Lines.Where(line => !string.IsNullOrWhiteSpace(line)).ToArray();
                var destBytes = new FileInfo(destFile).Length;
                if (again.Exit != 0 || !PlainVerified(againLines, destBytes, minimumBytes))
                {
                    PostgresSnapshotSession.RemovePartial(destFile);
                    published = false;
                    return Failed(5, "verify-failed");
                }
                var tables = PlainTableCount(againLines);
                var prefix = PostgresSnapshotSession.Sha256Prefix(destFile);
                var prune = s.UpdatePlainRetention(destDir, keep, destName);
                var duration = Seconds(watch());
                var cloudState = "";
                long cloudBytes = 0;
                var cloudFolder = "";
                var cloudLoud = "";
                if (copyToOneDrive)
                {
                    var cloud = s.OneDriveCopy(destFile, syncRoot, oneDriveKeep);
                    if (cloud != null)
                    {
                        cloudState = cloud.State;
                        cloudBytes = cloud.Bytes;
                        cloudFolder = cloud.Folder;
                        cloudLoud = cloud.Loud;
                    }
                    else
                    {
                        cloudState = "failed";
                        cloudLoud = "ONEDRIVE COPY oneDrive=failed";
                    }
                }
                var exitCode = prune.Ok ? 0 : 8;
                var exitReason = prune.Ok ? "ok" : "prune-failed";
                PostgresSnapshotSession.WritePlainStatus(destDir, stamp, exitCode, destBytes, prefix, duration, tables, cloudState, cloudBytes, cloudFolder, targetName,
                    serverVersion);
                if (exitCode != 0) return new SnapshotStep(exitCode, exitReason, null, cloudLoud);
                return new SnapshotStep(0, "ok", "bytes=" + Num(destBytes) + " kept=" + Num(prune.Kept) + " utc=" + stamp, cloudLoud);
            }
            finally
            {
                s.Native(docker, new[] { "exec", sourceContainer, "rm", "-f", containerDump, verifyRemote });
                PostgresSnapshotSession.RemovePartial(hostDump);
                if (!published) PostgresSnapshotSession.RemovePartial(destPartial);
            }
        }

        // Invoke-SnapshotPlainMode: --plain (desktop or server2).
        private static void PlainMode(PostgresSnapshotOptions o, PostgresSnapshotSession s, bool planOnly)
        {
            var sync = string.IsNullOrWhiteSpace(o.SyncRoot) ? DefaultSyncRoot : o.SyncRoot;
            var dest = string.IsNullOrWhiteSpace(o.Destination) ? PlainDestination : o.Destination;
            var stage = string.IsNullOrWhiteSpace(o.StagingPath) ? s.DefaultStaging() : o.StagingPath;
            var container = o.Container;
            s.Line(PlainBanner);
            var targetName = o.Target;
            if (targetName == "server2")
            {
                // One word selects the container, project, volume and context together.
                if (o.ContainerGiven) { s.Result(planOnly, "ambiguous-mode", ""); return; }
                container = Server2Container;
                s.DockerContext = Server2Context;
            }
            if (PostgresSnapshotSession.ContainsLink(dest)) { s.Result(planOnly, "destination-link", ""); return; }
            if (PlainInsideOneDrive(dest, sync))
            {
                s.Line("step destination: " + dest + " outside-onedrive=no");
                s.Result(planOnly, "destination-inside-onedrive", "");
                return;
            }
            s.Line("step destination: " + dest + " outside-onedrive=yes");
            if (PostgresSnapshotSession.ContainsLink(stage)) { s.Result(planOnly, "staging-link", ""); return; }
            if (Directory.Exists(sync) && PathInside(sync, stage)) { s.Result(planOnly, "staging-inside-sync", ""); return; }
            if (PathInside(dest, stage)) { s.Result(planOnly, "staging-inside-destination", ""); return; }

            var aclOpen = false;
            if (PostgresSnapshotSession.Exists(dest))
            {
                if (!Directory.Exists(dest)) { s.Result(planOnly, "destination-acl", ""); return; }
                if (!s.OwnedDirectory(dest))
                {
                    aclOpen = true;
                    s.Line("step acl: user-only acl=open");
                    if (planOnly) { s.Result(true, "destination-acl", ""); return; }
                }
                else
                {
                    s.Line("step acl: user-only acl=ok");
                }
            }
            else
            {
                s.Line("step acl: user-only directory=absent");
            }

            if (targetName == "server2") s.Line("step source: target=server2 container=" + container + " context=" + Server2Context);
            s.Line("step dump: docker exec " + container + " pg_dump -Fc --no-owner --no-acl -U antiphon -d " + o.Database + " -f /tmp/antiphon-snapshot.dump");
            s.Line("step verify: pg_restore --list on the new file must include TABLE DATA before prune; minimum " + Num(o.MinimumDumpBytes) + " bytes");
            s.Line("step copy: write the dump to antiphon-<utc>.dump.partial then rename to antiphon-<utc>.dump");
            s.Line("step retention: keep " + Num(o.Retain) + " plain dump files and prune only after the new file is verified");
            s.Line("step status: snapshot-status.json partial-write then rename (utc, exit, bytes, sha256 prefix, duration, tables, mode=plain)");
            if (o.OneDriveCopy)
            {
                s.Line("step onedrive: copy the verified unencrypted dump into " + OneDriveFolder(sync) + " as .partial then rename when sha256 matches; keep "
                    + Num(o.OneDriveRetain));
            }
            if (planOnly) { s.Result(true, "ok", "plain=1 encryption=none retention=" + Num(o.Retain)); return; }

            if (aclOpen)
            {
                if (!s.RestrictDirectory(dest)) { s.Result(false, "destination-acl", ""); return; }
                s.Line("step acl: tightened to user-only");
            }
            else if (!PostgresSnapshotSession.Exists(dest))
            {
                var parent = Path.GetDirectoryName(Path.GetFullPath(dest));
                if (!string.IsNullOrWhiteSpace(parent) && !PostgresSnapshotSession.Exists(parent)) { s.Result(false, "destination-acl", ""); return; }
                if (!s.NewPrivateDirectory(dest)) { s.Result(false, "destination-acl", ""); return; }
                s.Line("step acl: user-only created");
            }

            var docker = s.Resolve("docker");
            if (docker == null) { s.Result(false, "missing-docker", ""); return; }
            var rootState = s.StagingRoot(stage);
            if (rootState != "") { s.Result(false, rootState, ""); return; }
            var daemon = s.DockerDaemon(docker, o.AllowRemoteDocker);
            if (daemon != "") { s.Result(false, daemon, ""); return; }
            s.DaemonWarning();
            if (targetName == "server2")
            {
                var sourceFact = s.ContainerFact(docker, container);
                if (!IsServer2Source(sourceFact)) { s.Result(false, "source-mismatch", ""); return; }
            }
            var serverVersion = s.ServerVersion(docker, container);
            if (!s.InitializeStaging(stage)) { s.Result(false, "staging-mode", ""); return; }
            var run = s.RunStaging;
            if (string.IsNullOrWhiteSpace(run)) { s.Result(false, "staging-mode", ""); return; }
            try
            {
                var published = PlainPublish(s, container, o.Database, run, dest, o.Retain, docker, o.MinimumDumpBytes, o.OneDriveCopy, sync, o.OneDriveRetain, targetName,
                    serverVersion);
                if (!string.IsNullOrWhiteSpace(published.Loud)) s.Line(published.Loud);
                s.Result(false, published.Reason, published.Detail);
            }
            finally
            {
                s.RemoveStaging(run);
            }
        }
    }
}
