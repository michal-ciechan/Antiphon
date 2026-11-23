// CARD-0599 D-8/D-15, CARD-1311 and CARD-1339: publish one CalVer tag and GitHub Release for the exact tested SHA,
// `antiphon-scripts publish-release --candidate-id <id> [...]`. It replaced scripts/publish-release.ps1 (deleted with unit
// 14 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md, which also deleted promote-release.ps1); the release
// cut (ReleaseCut, the release-cut command) runs it in process. The cases run in process (ReleasePublishFixture and the classes that use it).
//
// It runs only on an RC complete-green. It validates the immutable candidate journal, the remote candidate SHA and - D-15 -
// the pinned authority before it writes anything: the policy blob re-read through git at the pinned SHA, the exact rc suite
// set, the frozen chunk plan and the full execution ledger (every chunk, every required expanded UID, every declared
// client/script roster entry, one intent/candidate/SHA/RunId), and the release card read back through the Antiphon API
// (GET /api/cards/{id} only) at exactly the receipt's revision with this run's correlation line in its body. Diagnostic,
// NoReport and seam-driven runs refuse. A summary or report is an output of that validation, never a source of required
// suites or success; a legacy candidate with no authority cannot publish and must be recut.
//
// The publication sequence, each transition journalled before the next begins:
//   1. reserve the tag (long-lived vYYYY.MM.DD.N-rc.K, stamp vYYYY.MM.DD.N)
//   2. create the local annotated tag at that SHA
//   3. push only that tag, never with --force
//   4. verify the peeled remote target
//   5. gh release create --verify-tag --draft --notes-file <file> (a pre-release tag also passes --prerelease)
//   6. upload release-manifest.json and the sanitized verification summary
//   7. read back tag and asset digests
//   8. publish the draft and persist the release id and URL read back from gh release view
// A retry resumes the same journal. Same tag/SHA/manifest is success or recovery; any mismatch is a hard refusal. A tag
// with no published release stays pending, not released; a readback that still reports a draft refuses (still-draft).
// Tags are never deleted or recreated. Publishing never restarts AppHost or the runner and never checks out a tag in the
// main worktree. Exit 0 published (or -WhatIf), 1 a remote write failed, 3 refused. Plain C#. ASCII only.
#nullable disable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Antiphon.Nightly
{
    /// <summary>The publish-release command's options (publish-release.ps1's parameters).</summary>
    public sealed class ReleasePublishOptions
    {
        /// <summary>The CLI's usage line.</summary>
        public const string Usage = "publish-release --candidate-id <id> [--release-root <dir>] [--checkout-root <dir>] [--repository <owner/name>]"
            + " [--expected-policy-hash <hash>] [--required-suites <a,b,...>] [--antiphon-api-url <url>] [--what-if]";

        /// <summary>The producer's release root.</summary>
        public const string DefaultReleaseRoot = @"C:\Antiphon\releases";

        /// <summary>The publication destination.</summary>
        public const string DefaultRepository = "michal-ciechan/Antiphon";

        /// <summary>The Antiphon API the release card is read back from when neither the flag nor ANTIPHON_API names one.</summary>
        public const string DefaultAntiphonApiUrl = "http://localhost:17202";

        private static readonly string[] Valued =
        {
            "--candidate-id", "--release-root", "--checkout-root", "--repository", "--expected-policy-hash", "--required-suites", "--antiphon-api-url",
        };

        private static readonly string[] Switches = { "--what-if" };

        /// <summary>Holds publications.json and candidates\&lt;id&gt;.</summary>
        public string ReleaseRoot { get; set; } = DefaultReleaseRoot;

        /// <summary>The candidate to publish; blank refuses missing-candidate.</summary>
        public string CandidateId { get; set; } = "";

        /// <summary>The owned release clone; blank is &lt;ReleaseRoot&gt;\checkout.</summary>
        public string CheckoutRoot { get; set; } = "";

        /// <summary>The publication destination (owner/name): it must equal the frozen authority's and the checkout's origin.</summary>
        public string Repository { get; set; } = DefaultRepository;

        /// <summary>Legacy: when given, the pinned policy hash must equal it.</summary>
        public string ExpectedPolicyHash { get; set; } = "";

        /// <summary>Legacy: when given, the pinned required suite set must equal it.</summary>
        public string[] RequiredSuites { get; set; } = Array.Empty<string>();

        /// <summary>The Antiphon API the release card is read back from; blank is DefaultAntiphonApiUrl.</summary>
        public string AntiphonApiUrl { get; set; } = "";

        /// <summary>Validate and report the proposed tag; write nothing (no reservation, no publication authority, nothing remote).</summary>
        public bool WhatIf { get; set; }

        /// <summary>The scripts directory whose pinned scripts and source trees the authority binds (ReleaseAuthority.ScriptHashes).</summary>
        public string ScriptsRoot { get; set; } = "";

        /// <summary>Explicit test mode (ANTIPHON_RELEASE_GATE_TEST_MODE exactly '1', read by the caller).</summary>
        public bool TestMode { get; set; }

        /// <summary>A PowerShell caller's seams file, for the admission only (no C# caller loads one).</summary>
        public string SeamsPath { get; set; } = "";

        /// <summary>
        /// The arguments after the command name. scriptsRoot is the checkout's scripts directory; variables reads
        /// ANTIPHON_API (the API when --antiphon-api-url is not given) and ANTIPHON_RELEASE_GATE_TEST_MODE for the caller.
        /// NightlyUsageException for a malformed line.
        /// </summary>
        public static ReleasePublishOptions Parse(IReadOnlyList<string> arguments, string scriptsRoot, Func<string, string> variables)
        {
            variables ??= _ => null;
            var flags = NightlyArgs.Parse(arguments, Valued, Switches);
            return new ReleasePublishOptions
            {
                ReleaseRoot = flags.Value("--release-root", DefaultReleaseRoot),
                CandidateId = flags.Value("--candidate-id", ""),
                CheckoutRoot = flags.Value("--checkout-root", ""),
                Repository = flags.Value("--repository", DefaultRepository),
                ExpectedPolicyHash = flags.Value("--expected-policy-hash", ""),
                RequiredSuites = flags.List("--required-suites").ToArray(),
                AntiphonApiUrl = flags.Value("--antiphon-api-url", variables("ANTIPHON_API") ?? ""),
                WhatIf = flags.Switch("--what-if"),
                ScriptsRoot = scriptsRoot ?? "",
                TestMode = ReleaseGateRemote.TestMode(variables(ReleaseGateRemote.TestModeVariable)),
            };
        }
    }

    /// <summary>One publication's answer (publish-release.ps1's -PassThru record).</summary>
    public sealed class ReleasePublishResult
    {
        private static readonly JsonSerializerOptions Relaxed = new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

        /// <summary>0 published or -WhatIf, 1 a remote write failed, 3 refused.</summary>
        public int ExitCode { get; set; } = 1;

        /// <summary>The refusal ('' when published): a reason token, or the gate's reasons joined with commas.</summary>
        public string Refusal { get; set; } = "";

        /// <summary>The candidate.</summary>
        public string CandidateId { get; set; } = "";

        /// <summary>The reserved (or proposed) tag.</summary>
        public string Tag { get; set; } = "";

        /// <summary>The tested SHA.</summary>
        public string Sha { get; set; } = "";

        /// <summary>The release is published.</summary>
        public bool Published { get; set; }

        /// <summary>The published release's id, as gh release view read it back.</summary>
        public string ReleaseId { get; set; } = "";

        /// <summary>The published release's URL, as gh release view read it back.</summary>
        public string ReleaseUrl { get; set; } = "";

        /// <summary>The candidate's existing reservation was reused.</summary>
        public bool Resumed { get; set; }

        /// <summary>The release manifest's digest.</summary>
        public string ManifestDigest { get; set; } = "";

        /// <summary>The command's line: candidateId, tag, sha, published, releaseId, releaseUrl, refusal, exitCode.</summary>
        public string ToJson() => new JsonObject
        {
            ["candidateId"] = CandidateId, ["tag"] = Tag, ["sha"] = Sha, ["published"] = Published, ["releaseId"] = ReleaseId,
            ["releaseUrl"] = ReleaseUrl, ["refusal"] = Refusal, ["exitCode"] = ExitCode,
        }.ToJsonString(Relaxed);
    }

    /// <summary>CARD-0599 D-8's publisher.</summary>
    public static class ReleasePublish
    {
        private const string Found = ReleaseGateRef.Found;
        private const string Absent = ReleaseGateRef.Absent;

        /// <summary>
        /// Publishes (or, with WhatIf, validates and proposes) the candidate's tag and release. The [publish-release] lines go
        /// to the host's output; the result is the command's record. A refusal raised inside (a reservation conflict, an
        /// unreadable journal, a missing seams file) refuses with exit 3 and its message.
        /// </summary>
        public static ReleasePublishResult Run(ReleasePublishOptions options, ReleaseGateHost host)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (host == null) throw new ArgumentNullException(nameof(host));
            var result = new ReleasePublishResult { CandidateId = options.CandidateId ?? "" };
            try
            {
                return Publish(options, host, result);
            }
            catch (NightlyRefusal refusal)
            {
                Line(host, "REFUSED: " + refusal.Message + ".");
                result.Published = false;
                result.Refusal = refusal.Message;
                result.ExitCode = 3;
                return result;
            }
        }

        private static void Line(ReleaseGateHost host, string message) => host.Out.WriteLine("[publish-release] " + message);

        private static bool Blank(string text) => string.IsNullOrWhiteSpace(text);

        private static bool Same(string left, string right) => string.Equals(left ?? "", right ?? "", StringComparison.OrdinalIgnoreCase);

        private static Hashtable Bag(params (string Name, object Value)[] entries)
        {
            var bag = new Hashtable(StringComparer.OrdinalIgnoreCase);
            foreach (var (name, value) in entries) bag[name] = value;
            return bag;
        }

        private static ReleasePublishResult Publish(ReleasePublishOptions options, ReleaseGateHost host, ReleasePublishResult result)
        {
            ReleasePublishResult Refuse(string reason, int exitCode, string line)
            {
                if (line != null) Line(host, line);
                result.Refusal = reason;
                result.ExitCode = exitCode;
                return result;
            }

            // Review bf928242: seams (fake remote, injected clock) only in explicit test mode, and never alongside a real
            // remote. Refused before anything is read.
            var admission = host.Admission(options.TestMode, options.SeamsPath);
            if (admission.Length > 0) return Refuse(admission, 3, "REFUSED: seam admission (" + admission + ").");
            var remote = host.Remote(options.TestMode);
            var journals = host.Journals();
            var apiUrl = Blank(options.AntiphonApiUrl) ? ReleasePublishOptions.DefaultAntiphonApiUrl : options.AntiphonApiUrl;
            var candidateId = options.CandidateId ?? "";
            if (Blank(candidateId)) return Refuse("missing-candidate", 3, "REFUSED: --candidate-id is required.");
            var releaseRoot = Blank(options.ReleaseRoot) ? ReleasePublishOptions.DefaultReleaseRoot : options.ReleaseRoot;
            var checkout = Blank(options.CheckoutRoot) ? Path.Combine(releaseRoot, "checkout") : options.CheckoutRoot;
            var repository = options.Repository ?? "";

            var paths = ReleaseGateCredit.CandidatePaths(releaseRoot, candidateId);
            foreach (var needed in new[] { paths.GreenPath, paths.JournalPath })
                if (!File.Exists(needed)) return Refuse("missing-evidence", 3, "REFUSED: missing " + needed + ".");
            var green = ReleaseGateHost.ReadDocument(paths.GreenPath);
            var candidate = ReleaseGateHost.ReadDocument(paths.JournalPath);
            var sha = ReleaseGateHost.Text(green, "sha");
            result.Sha = sha;

            // Resolve the remote candidate before any gate decision: a moved ref must fail, and an unresolved remote is not
            // treated as agreement.
            var remoteSha = remote.Head(checkout, ReleaseGateHost.Text(candidate, "ref")).Sha;

            // D-15: the authority is re-verified on every attempt, recovery included. It never reads summary.json; the pinned
            // blob and the ledger are the only inputs. Repository is the publication destination (it must equal the frozen
            // value and the checkout's origin); evidence may not post-date the publisher's clock; the receipt must match the
            // release card as the Antiphon API serves it now.
            var attemptUtc = remote.UtcNow();
            var authority = ReleaseAuthority.Check(paths.Root, checkout, green, candidate, repository, attemptUtc, apiUrl, options.ScriptsRoot, host.Reader);
            var gate = ReleaseGatePublication.Gate(green, candidate, remoteSha, authority, options.ExpectedPolicyHash, options.RequiredSuites);
            if (!gate.Ok)
            {
                var reasons = string.Join(",", gate.Reasons);
                return Refuse(reasons, 3, "REFUSED: publication gate (" + reasons + ").");
            }
            var inventory = remote.TagInventory(checkout);
            if (!inventory.Ok) return Refuse("remote-tag-inventory-failed", 3, "REFUSED: remote tag inventory could not be read.");
            var pinned = authority.Pinned.Authority;
            var ledger = PlainJson.Read(Path.Combine(ReleaseAuthority.Dir(paths.Root), "ledger.json"));
            var suites = authority.Suites.Cast<object>().ToArray();
            var exclusions = pinned.Exclusions.Select(e => (object)new OrderedDictionary
            {
                { "suite", e.Suite ?? "" }, { "class", e.Class ?? "" }, { "reason", e.Reason ?? "" }, { "owner", e.Owner ?? "" },
            }).ToArray();
            var summaryDigest = ReleaseAuthority.Sha256Text(NightlyDocuments.CanonicalJson(new OrderedDictionary
            {
                { "profile", "rc" }, { "policyHash", pinned.PolicyHash ?? "" }, { "suites", suites }, { "exclusions", exclusions },
            }));
            var publicationAuthority = ReleaseAuthority.Publication(paths.Root, authority.Pinned, summaryDigest, options.WhatIf);
            if (!publicationAuthority.Ok) return Refuse(publicationAuthority.Reason, 3, "REFUSED: " + publicationAuthority.Reason + ".");

            var publicationJournal = Path.Combine(releaseRoot, "publications.json");
            // A candidate without a cut time is cut now (the wall clock); a long-lived candidate's tag is dated by the
            // publisher's clock.
            var cutUtc = ReleaseGateCredit.Utc(candidate != null && candidate.TryGetValue("cutUtc", out var cut) ? cut : null) ?? host.UtcNow;
            if (ReleaseGateTags.LongLivedCandidateId(candidateId)) cutUtc = attemptUtc;
            var remoteTags = inventory.Tags.ToArray();
            // Read-only: -WhatIf reports the proposed tag and never reserves it, so publications.json is untouched.
            var reservation = options.WhatIf
                ? journals.Proposal(publicationJournal, candidateId, sha, cutUtc, remoteTags)
                : journals.Reserve(publicationJournal, candidateId, sha, cutUtc, remoteTags);
            var tag = reservation.Tag;
            result.Tag = tag;
            result.Resumed = reservation.Reused;

            if (!options.WhatIf)
            {
                // A reservation's identity never changes after an external race. Refuse before creating even a local tag,
                // and leave the reservation for recovery.
                var selected = remote.Tag(checkout, tag);
                if (selected.Status != Absent && (selected.Status != Found || !string.Equals(selected.Sha, sha, StringComparison.Ordinal)))
                    return Refuse("tag-conflict", 3, null);
                var parsedTag = ReleaseGateTags.Tag(tag);
                if (parsedTag.Prerelease)
                {
                    var final = remote.Tag(checkout, ReleaseGateTags.NewTag(parsedTag.Date, parsedTag.Sequence));
                    if (final.Status != Absent && (final.Status != Found || !string.Equals(final.Sha, sha, StringComparison.Ordinal)))
                        return Refuse("final-tag-conflict", 3, null);
                }
            }

            var capabilities = green != null && green.TryGetValue("capabilities", out var listed) && PsValue.IsTrue(listed) ? PsValue.AsArray(listed) : Array.Empty<object>();
            var manifest = ReleaseGatePublication.NewManifest(new OrderedDictionary(StringComparer.OrdinalIgnoreCase)
            {
                { "repository", repository }, { "tag", tag }, { "candidateRef", ReleaseGateHost.Text(candidate, "ref") }, { "candidateId", candidateId },
                { "sha", sha }, { "nativeRunId", ReleaseGateHost.Text(green, "runId") }, { "windmillJobId", ReleaseGateHost.Text(green, "windmillJobId") },
                { "scheduleSlot", ReleaseGateHost.Text(candidate, "scheduleSlot") }, { "startedAt", ReleaseGateHost.Text(green, "startedAt") },
                { "completedAt", ReleaseGateHost.Text(green, "completedAt") }, { "policyHash", pinned.PolicyHash ?? "" }, { "profile", "rc" },
                { "scriptHashes", pinned.ScriptHashes }, { "buildHash", AuthorityValue.Str(AuthorityValue.Get(ledger, "buildHash")) },
                { "bundleHash", AuthorityValue.Str(AuthorityValue.Get(ledger, "bundleHash")) }, { "suites", suites }, { "exclusions", exclusions },
                { "summaryDigest", summaryDigest }, { "capabilities", capabilities }, { "publicationAuthorityDigest", publicationAuthority.Digest },
            });
            var allow = ReleaseGatePublication.ManifestAllowlistCheck(manifest);
            if (!allow.Ok) return Refuse("manifest-allowlist", 3, "REFUSED: manifest carries non-allowlisted fields (" + string.Join(",", allow.Extra) + ").");
            var manifestDigest = ReleaseGatePublication.ManifestDigest(manifest);
            result.ManifestDigest = manifestDigest;

            var publishJournal = paths.PublishJournalPath;
            if (File.Exists(publishJournal))
            {
                var prior = ReleaseGateHost.ReadDocument(publishJournal);
                foreach (var (field, value) in new[] { ("tag", tag), ("sha", sha), ("manifestDigest", manifestDigest), ("publicationAuthorityDigest", publicationAuthority.Digest) })
                {
                    var have = ReleaseGateHost.Text(prior, field);
                    if (!Blank(have) && !Same(have, value))
                        return Refuse("journal-mismatch:" + field, 3, "REFUSED: " + field + " changed since the journal (" + have + " -> " + value + ").");
                }
            }

            if (options.WhatIf)
            {
                Line(host, "WhatIf: would publish " + tag + " at " + sha);
                result.ExitCode = 0;
                return result;
            }

            journals.WriteStep(publishJournal, "intent", new OrderedDictionary
            {
                { "candidateId", candidateId }, { "tag", tag }, { "sha", sha }, { "manifestDigest", manifestDigest }, { "repository", repository },
                { "publicationAuthorityDigest", publicationAuthority.Digest },
            });
            PlainJson.WriteAtomic(paths.ManifestPath, manifest);

            // 2/3. The local annotated tag, then a create-only push of that tag alone.
            if (!ReleaseGateJournal.HasStep(publishJournal, "tag-local"))
            {
                var made = remote.Git(checkout, "tag", "-a", tag, sha, "-m", "Antiphon " + tag);
                if (made.ExitCode != 0)
                {
                    var shown = remote.Git(checkout, "rev-list", "-n", "1", tag);
                    if (shown.ExitCode != 0 || !Same((shown.Output ?? "").Trim(), sha))
                        return Refuse("tag-local-failed", 1, "REFUSED: local tag creation failed and no matching tag exists.");
                }
                journals.WriteStep(publishJournal, "tag-local", null);
            }
            if (!ReleaseGateJournal.HasStep(publishJournal, "tag-pushed"))
            {
                var push = remote.PushExactTag(checkout, tag);
                if (push.ExitCode != 0)
                {
                    // Re-read remote state after an ambiguous response before any further write.
                    var peek = remote.Tag(checkout, tag);
                    if (peek.Status != Found || !Same(peek.Sha, sha))
                        return Refuse("tag-push-failed", 1, "REFUSED: tag push failed and the remote tag does not hold the tested sha.");
                }
                journals.WriteStep(publishJournal, "tag-pushed", null);
            }

            // 4. The peeled remote target agrees with the tested sha.
            var verify = remote.Tag(checkout, tag);
            if (verify.Status != Found || !Same(verify.Sha, sha))
                return Refuse("tag-peel-mismatch", 3, "REFUSED: remote tag peels to " + verify.Sha + ", expected " + sha + ".");

            // 5. The draft release.
            var notesPath = Path.Combine(paths.Root, "release-notes.md");
            var previousTag = ReleaseGateJournal.PreviousPublishedTag(publicationJournal, candidateId);
            var commits = new List<string>();
            var log = remote.Git(checkout, "log", "--no-merges", "--format=%h %s", Blank(previousTag) ? sha : previousTag + ".." + sha);
            if (log.ExitCode == 0)
                foreach (var line in (log.Output ?? "").Split('\n'))
                    if (!Blank(line)) commits.Add(line.Trim());
            ReleaseGateJournal.WriteAtomic(notesPath, NotesBody(manifest, previousTag, commits));
            if (!ReleaseGateJournal.HasStep(publishJournal, "draft-created"))
            {
                var create = Bag(("tag", tag), ("notesFile", notesPath));
                var parsed = ReleaseGateTags.Tag(tag);
                if (parsed.Ok && parsed.Prerelease) create["prerelease"] = true;
                if (remote.GitHub("release-create", create, checkout).ExitCode != 0 && remote.GitHub("release-view", Bag(("tag", tag)), checkout).ExitCode != 0)
                    return Refuse("draft-create-failed", 1, "REFUSED: draft creation failed and no release exists for the tag.");
                journals.WriteStep(publishJournal, "draft-created", null);
            }

            // 6/7. The assets, each journalled and read back by digest.
            var summaryAssetPath = Path.Combine(paths.Root, "verification-summary.json");
            PlainJson.WriteAtomic(summaryAssetPath, new OrderedDictionary
            {
                { "schemaVersion", 1 }, { "tag", tag }, { "sha", sha }, { "profile", "rc" }, { "policyHash", pinned.PolicyHash ?? "" },
                { "suites", manifest["suites"] }, { "exclusions", manifest["exclusions"] },
            });
            foreach (var asset in new[] { paths.ManifestPath, summaryAssetPath })
            {
                var name = Path.GetFileName(asset);
                var step = "asset:" + name;
                if (ReleaseGateJournal.HasStep(publishJournal, step)) continue;
                if (remote.GitHub("release-upload", Bag(("tag", tag), ("asset", asset)), checkout).ExitCode != 0)
                    return Refuse("asset-upload-failed:" + name, 1, "REFUSED: asset upload failed (" + name + "); release stays a draft.");
                journals.WriteStep(publishJournal, step, new OrderedDictionary { { step + ":digest", ReleaseAuthority.FileSha256(asset) } });
            }

            // 8. Publish the draft, then persist the final identity.
            if (remote.GitHub("release-publish", Bag(("tag", tag)), checkout).ExitCode != 0)
            {
                var view = remote.GitHub("release-view", Bag(("tag", tag)), checkout);
                var isDraft = true;
                if (view.ExitCode == 0 && view.Body != null) isDraft = PsValue.IsTrue(PsValue.Prop(view.Body, "isDraft"));
                if (isDraft) return Refuse("publish-failed", 1, "REFUSED: publish failed; the draft remains pending, not released.");
            }
            var readback = remote.GitHub("release-view", Bag(("tag", tag)), checkout);
            var releaseId = "";
            var releaseUrl = "";
            var stillDraft = false;
            if (readback.ExitCode == 0 && readback.Body != null)
            {
                releaseId = PsValue.Str(PsValue.Prop(readback.Body, "id"));
                releaseUrl = PsValue.Str(PsValue.Prop(readback.Body, "url"));
                stillDraft = PsValue.IsTrue(PsValue.Prop(readback.Body, "isDraft"));
            }
            if (stillDraft) return Refuse("still-draft", 1, "REFUSED: readback still reports a draft.");
            journals.WriteStep(publishJournal, "published", new OrderedDictionary
            {
                { "releaseId", releaseId }, { "releaseUrl", releaseUrl }, { "publishedAt", host.UtcNow.ToString("o", CultureInfo.InvariantCulture) },
            });
            journals.MarkPublished(publicationJournal, candidateId, releaseId, releaseUrl);

            result.Published = true;
            result.ReleaseId = releaseId;
            result.ReleaseUrl = releaseUrl;
            result.ExitCode = 0;
            Line(host, "published " + tag + " for " + sha);
            return result;
        }

        /// <summary>
        /// Get-ReleaseGateNotesBody: the release notes name the commits since the prior published tag, test counts, profile
        /// and exclusions, evidence identities and the previous release; the first release says there is no earlier
        /// release. Raw logs, credentials, hostnames and transcripts never appear here.
        /// </summary>
        public static string NotesBody(IDictionary manifest, string previousTag, IEnumerable<string> commits)
        {
            string M(string name) => PsValue.Str(manifest?[name]);
            var sb = new StringBuilder();
            sb.AppendLine("Release " + M("tag"));
            sb.AppendLine("");
            sb.AppendLine("Tested SHA: " + M("sha"));
            sb.AppendLine("Candidate: " + M("candidateId") + " (" + M("candidateRef") + ")");
            sb.AppendLine("Profile: " + M("profile") + "   policy hash: " + M("policyHash"));
            sb.AppendLine("Native run: " + M("nativeRunId"));
            sb.AppendLine("");
            sb.AppendLine(Blank(previousTag) ? "Previous release: none - there is no earlier release." : "Previous release: " + previousTag);
            sb.AppendLine("");
            sb.AppendLine("Suites");
            foreach (var row in PsValue.AsArray(manifest?["suites"]))
            {
                if (row == null) continue;
                string R(string name) => PsValue.Str(PsValue.Prop(row, name));
                sb.AppendLine("- " + R("id") + ": discovered " + R("discovered") + ", required " + R("required") + ", executed " + R("executed") + ", passed "
                    + R("passed") + ", failed " + R("failed") + ", skipped " + R("skipped") + ", excluded " + R("excluded"));
            }
            sb.AppendLine("");
            sb.AppendLine("Exclusions");
            var anyExclusion = false;
            foreach (var row in PsValue.AsArray(manifest?["exclusions"]))
            {
                if (row == null) continue;
                anyExclusion = true;
                sb.AppendLine("- " + PsValue.Str(PsValue.Prop(row, "class")) + ": " + PsValue.Str(PsValue.Prop(row, "reason")) + " (owner " + PsValue.Str(PsValue.Prop(row, "owner")) + ")");
            }
            if (!anyExclusion) sb.AppendLine("- none");
            sb.AppendLine("");
            sb.AppendLine("Commits");
            var anyCommit = false;
            foreach (var commit in commits ?? Array.Empty<string>())
            {
                if (Blank(commit)) continue;
                anyCommit = true;
                sb.AppendLine("- " + commit);
            }
            if (!anyCommit) sb.AppendLine("- none recorded");
            return sb.ToString();
        }
    }
}
