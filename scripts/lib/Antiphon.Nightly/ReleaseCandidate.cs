// CARD-0599 (D-3) and CARD-1311 (D-1, D-2): journal one release candidate. `antiphon-scripts release-candidate [--ref
// <ref>] [--release-root <dir>] [--checkout-root <dir>] [--remote-url <url>] [--source-ref <ref>] [--schedule-slot <slot>]
// [--what-if]`. It replaced scripts/release-candidate.ps1 (Invoke-AntiphonReleaseCandidate), deleted with unit 13 of
// docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md; the RC lane (ReleaseCut, the release-cut command that
// scripts/release-cut.ps1, Windmill's entry, launches since unit 15) calls ReleaseCandidate.Run in process. The cases run in
// process (ReleaseCandidateTests, ReleaseCandidateGitTests, ReleaseCutTests).
//
// An omitted ref refuses rc-ref-required and pushes nothing. A long-lived rc/YYYY-MM-DD must already exist on the remote:
// its tip is journalled under rc-YYYY-MM-DD- plus the first 12 hex digits of the tested SHA; the branch is not created or
// pushed, and a missing ref is rc-ref-missing. A legacy release/rc-YYYYMMDDTHHMMSSZ ref keeps the create-only push: the
// SHA (FETCH_HEAD of origin/<source ref>) is pinned once and journalled before the push; an existing remote stamp at a
// different SHA is remote-candidate-conflict (D-3); the same journal, ref and SHA resumes without a fetch; a lost push
// response is resolved by re-reading the remote, never by pushing again. No force push, no repair commit, no moved tested
// branch. It runs no tests, publishes nothing and restarts nothing.
//
// The producer clone must be owned: not the canonical checkout, not a linked worktree and not an unmarked clone (the
// nightly run's rule). Git is the release gate's remote (ReleaseGateRemote: production git, or a test's seam); the clock
// and the progress lines ("[release-candidate] ...") are the host's; the journal, the marker and the directories go
// through IFileSystem (the reparse resolution of the checkout reads the disk, as the nightly run's does). Exit 0 journalled
// (or WhatIf), 1 a clone, fetch, push or SHA failure, 3 refused. Plain C#. ASCII only.
#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Abstractions;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Antiphon.Nightly
{
    /// <summary>The release-candidate command's options (the script's parameters).</summary>
    public sealed class ReleaseCandidateOptions
    {
        /// <summary>The CLI's usage line.</summary>
        public const string Usage = "release-candidate [--ref <rc/YYYY-MM-DD|release/rc-YYYYMMDDTHHMMSSZ>] [--release-root <dir>] [--checkout-root <dir>]"
            + " [--remote-url <url>] [--source-ref <ref>] [--schedule-slot <slot>] [--what-if]";

        /// <summary>The source ref a legacy stamp candidate pins.</summary>
        public const string DefaultSourceRef = "master";

        private static readonly string[] Valued = { "--ref", "--release-root", "--checkout-root", "--remote-url", "--source-ref", "--schedule-slot" };
        private static readonly string[] Switches = { "--what-if" };

        /// <summary>Producer-owned release state.</summary>
        public string ReleaseRoot { get; set; } = ReleaseGateCredit.DefaultReleaseRoot;

        /// <summary>The producer's own clone; empty is &lt;ReleaseRoot&gt;\checkout.</summary>
        public string CheckoutRoot { get; set; } = "";

        /// <summary>The origin a missing clone is cloned from, and the journal's remoteUrl.</summary>
        public string RemoteUrl { get; set; } = NightlyRunOptions.DefaultRemoteUrl;

        /// <summary>rc/YYYY-MM-DD (journals the existing remote tip) or release/rc-YYYYMMDDTHHMMSSZ (create-only push); empty refuses rc-ref-required.</summary>
        public string Ref { get; set; } = "";

        /// <summary>The branch a legacy stamp candidate pins (fetched once).</summary>
        public string SourceRef { get; set; } = DefaultSourceRef;

        /// <summary>The Windmill schedule slot, journalled as is.</summary>
        public string ScheduleSlot { get; set; } = "";

        /// <summary>Reads, but clones nothing, writes no journal and pushes nothing.</summary>
        public bool WhatIf { get; set; }

        /// <summary>The arguments after the command name; NightlyUsageException for a malformed line.</summary>
        public static ReleaseCandidateOptions Parse(IReadOnlyList<string> arguments)
        {
            var flags = NightlyArgs.Parse(arguments, Valued, Switches);
            return new ReleaseCandidateOptions
            {
                Ref = flags.Value("--ref", ""),
                ReleaseRoot = flags.Value("--release-root", ReleaseGateCredit.DefaultReleaseRoot),
                CheckoutRoot = flags.Value("--checkout-root", ""),
                RemoteUrl = flags.Value("--remote-url", NightlyRunOptions.DefaultRemoteUrl),
                SourceRef = flags.Value("--source-ref", DefaultSourceRef),
                ScheduleSlot = flags.Value("--schedule-slot", ""),
                WhatIf = flags.Switch("--what-if"),
            };
        }
    }

    /// <summary>One candidate journal's outcome (the script's -PassThru record).</summary>
    public sealed class ReleaseCandidateResult
    {
        /// <summary>0 journalled or WhatIf; 1 a clone, fetch, push or SHA failure; 3 refused.</summary>
        public int ExitCode { get; set; } = 1;

        /// <summary>The refusal reason; empty when journalled.</summary>
        public string Refusal { get; set; } = "";

        /// <summary>rc-&lt;stamp&gt;, or rc-YYYY-MM-DD-&lt;sha12&gt; once the long-lived tip is read.</summary>
        public string CandidateId { get; set; } = "";

        /// <summary>The normalized candidate ref.</summary>
        public string Ref { get; set; } = "";

        /// <summary>The pinned full SHA.</summary>
        public string Sha { get; set; } = "";

        /// <summary>The candidate journal (&lt;release root&gt;\candidates\&lt;id&gt;\candidate.json).</summary>
        public string JournalPath { get; set; } = "";

        /// <summary>An existing journal already pinned this candidate.</summary>
        public bool Resumed { get; set; }

        /// <summary>The remote holds the candidate ref at the pinned SHA by this cut (a long-lived ref is never pushed).</summary>
        public bool Pushed { get; set; }

        /// <summary>The command's line: candidateId, ref, sha, pushed, resumed, refusal, exitCode (compact JSON).</summary>
        public string ToJson() => new JsonObject
        {
            ["candidateId"] = CandidateId, ["ref"] = Ref, ["sha"] = Sha, ["pushed"] = Pushed, ["resumed"] = Resumed, ["refusal"] = Refusal,
            ["exitCode"] = ExitCode,
        }.ToJsonString(Compact);

        private static readonly JsonSerializerOptions Compact = new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    }

    /// <summary>CARD-0599 D-3 / CARD-1311: the candidate journal and its create-only push.</summary>
    public static class ReleaseCandidate
    {
        /// <summary>The prefix of every progress line (Write-CandidateLine).</summary>
        public const string LinePrefix = "[release-candidate] ";

        /// <summary>The marker an owned release clone carries at its root.</summary>
        public const string OwnedMarker = ".antiphon-release-owned";

        /// <summary>The candidate journal's file name under the candidate root.</summary>
        public const string JournalName = "candidate.json";

        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        /// <summary>
        /// Journals the candidate options name, writing its progress lines to the host's stdout. The remote answers every git
        /// call (clone, ls-remote, fetch, rev-parse, push); the host's clock stamps createdAt. A journal or marker that cannot
        /// be written throws (IOException, UnauthorizedAccessException): the journal is always written before the push.
        /// </summary>
        public static ReleaseCandidateResult Run(ReleaseCandidateOptions options, NightlyHost host, IFileSystem files, ReleaseGateRemote remote)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (host == null) throw new ArgumentNullException(nameof(host));
            if (files == null) throw new ArgumentNullException(nameof(files));
            if (remote == null) throw new ArgumentNullException(nameof(remote));

            var result = new ReleaseCandidateResult();
            void Say(string message) => host.Out.WriteLine(LinePrefix + message);
            ReleaseCandidateResult Refuse(string reason, int exitCode)
            {
                result.Refusal = reason;
                result.ExitCode = exitCode;
                return result;
            }

            var releaseRoot = options.ReleaseRoot ?? "";
            var checkoutRoot = string.IsNullOrWhiteSpace(options.CheckoutRoot) ? ReleaseGateCredit.Join(releaseRoot, "checkout") : options.CheckoutRoot;
            if (string.IsNullOrWhiteSpace(options.Ref))
            {
                Say("REFUSED: candidate ref (rc-ref-required).");
                return Refuse("rc-ref-required", 3);
            }
            var candidate = ReleaseGateCredit.Candidate(options.Ref, "");
            if (!candidate.Ok)
            {
                Say("REFUSED: candidate ref (" + candidate.Reason + ").");
                return Refuse(candidate.Reason, 3);
            }
            result.CandidateId = candidate.CandidateId;
            result.Ref = candidate.Ref;

            // Ownership: the producer clone is ours, and is not the canonical checkout or a linked worktree. This mirrors
            // the nightly bootstrap's own guard.
            try { checkoutRoot = NightlyRun.ResolvedPath(checkoutRoot); }
            catch (Exception e)
            {
                Say("REFUSED: reparse (" + e.Message + ").");
                return Refuse("reparse", 3);
            }
            if (NightlyPaths.IsSharedTree(checkoutRoot))
            {
                Say("REFUSED: -CheckoutRoot is the shared tree or a worktree.");
                return Refuse("shared-tree", 3);
            }
            var gitPath = ReleaseGateCredit.Join(checkoutRoot, ".git");
            // Test-NightlyGitWorktree: a .git file (a gitdir pointer) is never an independent clone.
            if (files.File.Exists(gitPath))
            {
                Say("REFUSED: linked Git worktree is not an independent clone.");
                return Refuse("linked-worktree", 3);
            }
            if (Exists(files, gitPath) && !Exists(files, ReleaseGateCredit.Join(checkoutRoot, OwnedMarker)))
            {
                Say("REFUSED: unmarked clone is not a proven release checkout.");
                return Refuse("unmarked-clone", 3);
            }

            // A long-lived id includes the tested SHA, which is not known until the remote tip is read. Do not create a root
            // from the empty pre-SHA id.
            string journalPath;
            JsonObject existing = null;
            if (candidate.Kind != "long-lived")
            {
                journalPath = CandidateJournal(files, releaseRoot, candidate.CandidateId);
                result.JournalPath = journalPath;
                if (!TryReadJournal(files, journalPath, out existing, out var unreadable))
                {
                    Say("REFUSED: unreadable candidate journal (" + unreadable + ").");
                    return Refuse("journal-unreadable", 3);
                }
            }

            if (!Exists(files, gitPath))
            {
                if (options.WhatIf) Say("WhatIf: would clone -c core.longpaths=true " + options.RemoteUrl + " into " + checkoutRoot);
                else
                {
                    var parent = files.Path.GetDirectoryName(checkoutRoot) ?? "";
                    files.Directory.CreateDirectory(parent);
                    var clone = remote.Git(parent, "clone", "-c", "core.longpaths=true", options.RemoteUrl, checkoutRoot);
                    if (clone.ExitCode != 0)
                    {
                        Say("REFUSED: clone failed.");
                        return Refuse("clone-failed", 1);
                    }
                    WriteJson(files, ReleaseGateCredit.Join(checkoutRoot, OwnedMarker), new JsonObject { ["origin"] = options.RemoteUrl, ["kind"] = "release-clone" });
                }
            }
            var createdAt = Stamp(host.UtcNow);

            // A long-lived ref is already on the remote. Journal that tip. Do not push.
            if (candidate.Kind == "long-lived")
            {
                var tip = remote.Head(checkoutRoot, candidate.Ref);
                if (tip.Status == ReleaseGateRef.Failed)
                {
                    Say("REFUSED: could not read the long-lived rc ref.");
                    return Refuse("rc-ref-unresolved", 3);
                }
                var remoteSha = tip.Sha ?? "";
                if (string.IsNullOrWhiteSpace(remoteSha))
                {
                    Say("REFUSED: remote " + candidate.Ref + " does not exist.");
                    return Refuse("rc-ref-missing", 3);
                }
                if (!ReleaseGateCredit.FullSha(remoteSha))
                {
                    Say("REFUSED: remote " + candidate.Ref + " did not resolve to a full sha (" + remoteSha + ").");
                    return Refuse("sha-unresolved", 1);
                }
                candidate = ReleaseGateCredit.Candidate(candidate.Ref, remoteSha);
                if (!candidate.Ok || string.IsNullOrWhiteSpace(candidate.CandidateId))
                {
                    Say("REFUSED: long-lived candidate id.");
                    return Refuse("candidate-id", 3);
                }
                result.CandidateId = candidate.CandidateId;
                result.Ref = candidate.Ref;
                result.Sha = remoteSha;
                journalPath = CandidateJournal(files, releaseRoot, candidate.CandidateId);
                result.JournalPath = journalPath;
                if (!TryReadJournal(files, journalPath, out existing, out var unreadable))
                {
                    Say("REFUSED: unreadable candidate journal (" + unreadable + ").");
                    return Refuse("journal-unreadable", 3);
                }
                var pinned = ReleaseGateJournal.Str(existing?["sha"]);
                if (!string.IsNullOrWhiteSpace(pinned) && !string.Equals(pinned, remoteSha, StringComparison.OrdinalIgnoreCase))
                {
                    Say("REFUSED: candidate " + candidate.CandidateId + " already pinned to " + pinned + ".");
                    return Refuse("candidate-repinned", 3);
                }
                if (!string.IsNullOrWhiteSpace(pinned)) result.Resumed = true;
                var tipJournal = Journal(candidate, remoteSha, candidate.Ref, options, createdAt, false);
                if (options.WhatIf)
                {
                    Say("WhatIf: would journal " + candidate.Ref + " at " + remoteSha);
                    result.ExitCode = 0;
                    return result;
                }
                WriteJson(files, journalPath, tipJournal);
                Say("journalled " + candidate.Ref + " at " + remoteSha + "; not pushed");
                result.ExitCode = 0;
                return result;
            }

            // Pin once. A resume never re-pins: it reuses the journal's SHA and only reconciles the remote.
            var resumedSha = ReleaseGateJournal.Str(existing?["sha"]);
            string sha;
            if (!string.IsNullOrWhiteSpace(resumedSha))
            {
                sha = resumedSha;
                result.Resumed = true;
                Say("resuming candidate " + candidate.CandidateId + " at " + sha);
            }
            else
            {
                var fetch = remote.Git(checkoutRoot, "fetch", "origin", options.SourceRef ?? "");
                if (fetch.ExitCode != 0)
                {
                    Say("REFUSED: fetch failed.");
                    return Refuse("fetch-failed", 1);
                }
                sha = (remote.Git(checkoutRoot, "rev-parse", "FETCH_HEAD").Output ?? "").Trim();
                if (!ReleaseGateCredit.FullSha(sha))
                {
                    Say("REFUSED: origin/" + options.SourceRef + " did not resolve to a full sha (" + sha + ").");
                    return Refuse("sha-unresolved", 1);
                }
            }
            result.Sha = sha;
            if (!string.IsNullOrWhiteSpace(resumedSha) && !string.Equals(resumedSha, sha, StringComparison.OrdinalIgnoreCase))
            {
                Say("REFUSED: candidate " + candidate.CandidateId + " already pinned to " + resumedSha + ".");
                return Refuse("candidate-repinned", 3);
            }

            // D-3: an existing remote candidate ref at a different SHA is a hard refusal. Resolve it BEFORE the push so an
            // ambiguous earlier response cannot be papered over with a second write.
            var head = remote.Head(checkoutRoot, candidate.Ref);
            if (head.Status == ReleaseGateRef.Failed)
            {
                Say("REFUSED: remote candidate could not be resolved.");
                return Refuse("remote-candidate-unresolved", 3);
            }
            var present = head.Sha ?? "";
            if (!string.IsNullOrWhiteSpace(present) && !string.Equals(present, sha, StringComparison.OrdinalIgnoreCase))
            {
                Say("REFUSED: remote " + candidate.Ref + " already points at " + present + ".");
                return Refuse("remote-candidate-conflict", 3);
            }

            // Journal before the remote write.
            var journal = Journal(candidate, sha, options.SourceRef ?? "", options, createdAt, existing != null && ReleaseGateJournal.Truthy(existing["pushed"]));
            if (options.WhatIf)
            {
                Say("WhatIf: would cut " + candidate.Ref + " at " + sha);
                result.ExitCode = 0;
                return result;
            }
            WriteJson(files, result.JournalPath, journal);

            ReleaseCandidateResult Pushed()
            {
                journal["pushed"] = true;
                WriteJson(files, result.JournalPath, journal);
                result.Pushed = true;
                result.ExitCode = 0;
                return result;
            }

            if (!string.IsNullOrWhiteSpace(present))
            {
                // Already published at the pinned SHA: idempotent resume, no second write.
                Say("candidate " + candidate.Ref + " already present at " + sha);
                return Pushed();
            }

            // Create-only push. No --force, no lease, no ref move.
            var push = remote.Git(checkoutRoot, "push", "origin", sha + ":refs/heads/" + candidate.Ref);
            if (push.ExitCode != 0)
            {
                // An ambiguous or failed push is resolved by re-reading the remote, never by pushing again under a new identity.
                var recheck = remote.Head(checkoutRoot, candidate.Ref);
                if (recheck.Status == ReleaseGateRef.Found && string.Equals(recheck.Sha, sha, StringComparison.OrdinalIgnoreCase))
                {
                    Say("push response lost but the remote holds the pinned sha.");
                    return Pushed();
                }
                Say("REFUSED: candidate push failed.");
                return Refuse("push-failed", 1);
            }
            Pushed();
            Say("cut " + candidate.Ref + " at " + sha);
            return result;
        }

        // The candidate root is created (New-Item -Force), even under WhatIf; its journal path.
        private static string CandidateJournal(IFileSystem files, string releaseRoot, string candidateId)
        {
            var root = ReleaseGateCredit.CandidateRoot(releaseRoot, candidateId);
            files.Directory.CreateDirectory(root);
            return ReleaseGateCredit.Join(root, JournalName);
        }

        // The journal at path as a JSON object (null when the file is absent or holds another JSON value); false with the
        // reason when it cannot be read or parsed.
        private static bool TryReadJournal(IFileSystem files, string path, out JsonObject journal, out string reason)
        {
            journal = null;
            reason = "";
            if (!files.File.Exists(path)) return true;
            try
            {
                journal = ReleaseGateJournal.Parse(files.File.ReadAllText(path, Encoding.UTF8)) as JsonObject;
                return true;
            }
            catch (Exception e) when (e is JsonException || e is IOException || e is UnauthorizedAccessException)
            {
                reason = e.Message;
                return false;
            }
        }

        // The candidate journal (schema 1), in the script's field order.
        private static JsonObject Journal(ReleaseGateCandidate candidate, string sha, string sourceRef, ReleaseCandidateOptions options, string createdAt, bool pushed) =>
            new JsonObject
            {
                ["schemaVersion"] = 1, ["candidateId"] = candidate.CandidateId, ["ref"] = candidate.Ref, ["sha"] = sha, ["sourceRef"] = sourceRef,
                ["remoteUrl"] = options.RemoteUrl ?? "", ["scheduleSlot"] = options.ScheduleSlot ?? "", ["cutUtc"] = Stamp(candidate.CutUtc), ["createdAt"] = createdAt,
                ["pushed"] = pushed,
            };

        private static string Stamp(DateTime value) => value.ToString("o", CultureInfo.InvariantCulture);

        private static bool Exists(IFileSystem files, string path) => files.File.Exists(path) || files.Directory.Exists(path);

        // Write-NightlyAtomicJson: the directory, then indented UTF-8 without a byte-order mark to a temporary file beside the
        // target, renamed over it.
        private static void WriteJson(IFileSystem files, string path, JsonNode document)
        {
            var directory = files.Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) files.Directory.CreateDirectory(directory);
            var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                files.File.WriteAllText(temp, ReleaseGateJournal.Text(document), Utf8NoBom);
                files.File.Move(temp, path, true);
            }
            finally
            {
                if (files.File.Exists(temp)) files.File.Delete(temp);
            }
        }
    }
}
