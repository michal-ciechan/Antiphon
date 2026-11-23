// CARD-1311 and CARD-1339: promote one pre-release tag vYYYY.MM.DD.N-rc.K to the final CalVer tag vYYYY.MM.DD.N at the same
// peeled SHA, `antiphon-scripts promote-release --tag <pre-release tag> --candidate-id <id> [...]`. It replaced
// scripts/promote-release.ps1 (deleted with unit 14 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md); the
// cases run in process.
//
// The pre-release tag must already exist and must peel to a full SHA that the candidate's RC complete-green credits. The
// live candidate ref tip must be that same SHA: a different tip refuses remote-candidate-moved, and an unresolved tip
// refuses remote-candidate-unresolved, before any tag or GitHub write. The final tag is created at the peeled SHA. Every
// attempt, read-only previews and same-final recovery included, requalifies the pinned authority, the Full ledger, the
// script and source-tree digests and the release-card readback (ReleaseAuthority.Check, as the publisher), and consumes
// exactly one published reservation (ReleaseGateJournal.PromotionReservation). An existing final tag at the same SHA
// succeeds only after that, and after checked journal recording; an existing final tag at another SHA refuses. The
// pre-release tag and its GitHub pre-release are left in place. A post-push recording failure refuses
// promotion-journal-write-failed and reports the verified remote tag without claiming completed promotion. Seams and test
// mode are admitted as the publisher admits them (ReleaseGateHost). Exit 0 promoted, resumed or -WhatIf, 1 a remote write
// or the journal record failed, 3 refused. Plain C#. ASCII only.
#nullable disable
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Antiphon.Nightly
{
    /// <summary>The promote-release command's options (promote-release.ps1's parameters).</summary>
    public sealed class ReleasePromoteOptions
    {
        /// <summary>The CLI's usage line.</summary>
        public const string Usage = "promote-release --tag <vYYYY.MM.DD.N-rc.K> --candidate-id <id> [--release-root <dir>] [--checkout-root <dir>]"
            + " [--repository <owner/name>] [--antiphon-api-url <url>] [--what-if]";

        private static readonly string[] Valued = { "--tag", "--candidate-id", "--release-root", "--checkout-root", "--repository", "--antiphon-api-url" };

        private static readonly string[] Switches = { "--what-if" };

        /// <summary>Holds publications.json and candidates\&lt;id&gt;.</summary>
        public string ReleaseRoot { get; set; } = ReleasePublishOptions.DefaultReleaseRoot;

        /// <summary>The pre-release tag to promote.</summary>
        public string Tag { get; set; } = "";

        /// <summary>The candidate the pre-release was published for; blank refuses missing-candidate.</summary>
        public string CandidateId { get; set; } = "";

        /// <summary>The owned release clone; blank is &lt;ReleaseRoot&gt;\checkout.</summary>
        public string CheckoutRoot { get; set; } = "";

        /// <summary>The publication destination (owner/name), as the publisher's.</summary>
        public string Repository { get; set; } = ReleasePublishOptions.DefaultRepository;

        /// <summary>The Antiphon API the release card is read back from; blank is ReleasePublishOptions.DefaultAntiphonApiUrl.</summary>
        public string AntiphonApiUrl { get; set; } = "";

        /// <summary>Validate everything and report the final tag; write nothing.</summary>
        public bool WhatIf { get; set; }

        /// <summary>The scripts directory whose pinned scripts and source trees the authority binds.</summary>
        public string ScriptsRoot { get; set; } = "";

        /// <summary>Explicit test mode (ANTIPHON_RELEASE_GATE_TEST_MODE exactly '1', read by the caller).</summary>
        public bool TestMode { get; set; }

        /// <summary>A PowerShell caller's seams file, for the admission only.</summary>
        public string SeamsPath { get; set; } = "";

        /// <summary>The arguments after the command name, as ReleasePublishOptions.Parse reads them.</summary>
        public static ReleasePromoteOptions Parse(IReadOnlyList<string> arguments, string scriptsRoot, Func<string, string> variables)
        {
            variables ??= _ => null;
            var flags = NightlyArgs.Parse(arguments, Valued, Switches);
            return new ReleasePromoteOptions
            {
                ReleaseRoot = flags.Value("--release-root", ReleasePublishOptions.DefaultReleaseRoot),
                Tag = flags.Value("--tag", ""),
                CandidateId = flags.Value("--candidate-id", ""),
                CheckoutRoot = flags.Value("--checkout-root", ""),
                Repository = flags.Value("--repository", ReleasePublishOptions.DefaultRepository),
                AntiphonApiUrl = flags.Value("--antiphon-api-url", variables("ANTIPHON_API") ?? ""),
                WhatIf = flags.Switch("--what-if"),
                ScriptsRoot = scriptsRoot ?? "",
                TestMode = ReleaseGateRemote.TestMode(variables(ReleaseGateRemote.TestModeVariable)),
            };
        }
    }

    /// <summary>One promotion's answer (promote-release.ps1's -PassThru record).</summary>
    public sealed class ReleasePromoteResult
    {
        private static readonly JsonSerializerOptions Relaxed = new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

        /// <summary>0 promoted, resumed or -WhatIf, 1 a remote write or the journal record failed, 3 refused.</summary>
        public int ExitCode { get; set; } = 1;

        /// <summary>The refusal ('' when promoted).</summary>
        public string Refusal { get; set; } = "";

        /// <summary>The candidate.</summary>
        public string CandidateId { get; set; } = "";

        /// <summary>The final tag vYYYY.MM.DD.N.</summary>
        public string Tag { get; set; } = "";

        /// <summary>The pre-release tag asked for.</summary>
        public string PreReleaseTag { get; set; } = "";

        /// <summary>The peeled SHA.</summary>
        public string Sha { get; set; } = "";

        /// <summary>This attempt created and pushed the final tag.</summary>
        public bool Promoted { get; set; }

        /// <summary>The final tag already held the peeled SHA; this attempt recorded it.</summary>
        public bool Resumed { get; set; }

        /// <summary>The command's line: candidateId, tag, preReleaseTag, sha, promoted, resumed, refusal, exitCode.</summary>
        public string ToJson() => new JsonObject
        {
            ["candidateId"] = CandidateId, ["tag"] = Tag, ["preReleaseTag"] = PreReleaseTag, ["sha"] = Sha, ["promoted"] = Promoted,
            ["resumed"] = Resumed, ["refusal"] = Refusal, ["exitCode"] = ExitCode,
        }.ToJsonString(Relaxed);
    }

    /// <summary>CARD-1311's promoter.</summary>
    public static class ReleasePromote
    {
        private const string Found = ReleaseGateRef.Found;
        private const string Absent = ReleaseGateRef.Absent;
        private const string Failed = ReleaseGateRef.Failed;

        /// <summary>
        /// Promotes (or, with WhatIf, validates) the pre-release tag. The [promote-release] lines go to the host's output; the
        /// result is the command's record. A refusal raised inside refuses with exit 3 and its message.
        /// </summary>
        public static ReleasePromoteResult Run(ReleasePromoteOptions options, ReleaseGateHost host)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (host == null) throw new ArgumentNullException(nameof(host));
            var result = new ReleasePromoteResult { CandidateId = options.CandidateId ?? "", PreReleaseTag = options.Tag ?? "" };
            try
            {
                return Promote(options, host, result);
            }
            catch (NightlyRefusal refusal)
            {
                Line(host, "REFUSED: " + refusal.Message + ".");
                result.Promoted = false;
                result.Resumed = false;
                result.Refusal = refusal.Message;
                result.ExitCode = 3;
                return result;
            }
        }

        private static void Line(ReleaseGateHost host, string message) => host.Out.WriteLine("[promote-release] " + message);

        private static bool Blank(string text) => string.IsNullOrWhiteSpace(text);

        private static bool Same(string left, string right) => string.Equals(left ?? "", right ?? "", StringComparison.OrdinalIgnoreCase);

        private static ReleasePromoteResult Promote(ReleasePromoteOptions options, ReleaseGateHost host, ReleasePromoteResult result)
        {
            ReleasePromoteResult Refuse(string reason, int exitCode, string line)
            {
                Line(host, line);
                result.Refusal = reason;
                result.ExitCode = exitCode;
                return result;
            }

            // The publisher's seam admission: seams only in explicit test mode, never beside a real remote.
            var admission = host.Admission(options.TestMode, options.SeamsPath);
            if (admission.Length > 0) return Refuse(admission, 3, "REFUSED: seam admission (" + admission + ").");
            var remote = host.Remote(options.TestMode);
            var journals = host.Journals();
            var candidateId = options.CandidateId ?? "";
            if (Blank(candidateId)) return Refuse("missing-candidate", 3, "REFUSED: --candidate-id is required.");
            var releaseRoot = Blank(options.ReleaseRoot) ? ReleasePublishOptions.DefaultReleaseRoot : options.ReleaseRoot;
            var checkout = Blank(options.CheckoutRoot) ? Path.Combine(releaseRoot, "checkout") : options.CheckoutRoot;
            var apiUrl = Blank(options.AntiphonApiUrl) ? ReleasePublishOptions.DefaultAntiphonApiUrl : options.AntiphonApiUrl;
            var preTag = options.Tag ?? "";
            var parsed = ReleaseGateTags.Tag(preTag);
            if (!parsed.Ok || !parsed.Prerelease)
            {
                var reason = !parsed.Ok && !Blank(parsed.Reason) && parsed.Reason != "tag-shape" ? parsed.Reason : "tag-shape";
                return Refuse(reason, 3, "REFUSED: " + preTag + " is not a pre-release tag (" + reason + ").");
            }
            var finalName = ReleaseGateTags.NewTag(parsed.Date, parsed.Sequence);
            result.Tag = finalName;

            var pre = remote.Tag(checkout, preTag);
            if (pre.Status == Absent) return Refuse("prerelease-missing", 3, "REFUSED: pre-release tag " + preTag + " is not on the remote.");
            var peeled = pre.Sha;
            if (pre.Status != Found) return Refuse("tag-peel", 3, "REFUSED: pre-release tag " + preTag + " peeled to " + peeled + ".");
            result.Sha = peeled;

            var paths = ReleaseGateCredit.CandidatePaths(releaseRoot, candidateId);
            foreach (var needed in new[] { paths.GreenPath, paths.JournalPath })
                if (!File.Exists(needed)) return Refuse("missing-evidence", 3, "REFUSED: missing " + needed + ".");
            var green = ReleaseGateHost.ReadDocument(paths.GreenPath);
            var candidate = ReleaseGateHost.ReadDocument(paths.JournalPath);
            var verdict = ReleaseGateCredit.Verdict(green);
            if (!string.Equals(verdict.CreditKind, "rc-release", StringComparison.OrdinalIgnoreCase) || !verdict.Ok)
                return Refuse("not-rc-complete-green", 3, "REFUSED: credit is not rc-release (" + string.Join(",", verdict.Reasons) + ").");
            if (!Same(ReleaseGateHost.Text(green, "candidateId"), candidateId) || !Same(ReleaseGateHost.Text(candidate, "candidateId"), candidateId))
                return Refuse("not-rc-complete-green", 3, "REFUSED: credited candidate id does not match --candidate-id.");
            if (!Same(ReleaseGateHost.Text(green, "sha"), peeled) || !Same(ReleaseGateHost.Text(candidate, "sha"), peeled))
                return Refuse("credited-sha-mismatch", 3, "REFUSED: credited sha " + ReleaseGateHost.Text(green, "sha") + " does not match peeled sha " + peeled + ".");

            // The tested SHA is what is promoted. A live tip that has moved is a different candidate. Refuse before any tag or
            // GitHub write.
            var reference = ReleaseGateHost.Text(candidate, "ref");
            var live = remote.Head(checkout, reference);
            if (live.Status != Found) return Refuse("remote-candidate-unresolved", 3, "REFUSED: live ref " + reference + " did not resolve.");
            if (!Same(live.Sha, peeled)) return Refuse("remote-candidate-moved", 3, "REFUSED: live " + reference + " is " + live.Sha + "; pre-release peels to " + peeled + ".");

            var final = remote.Tag(checkout, finalName);
            if (final.Status == Failed) return Refuse("tag-peel", 3, "REFUSED: final tag " + finalName + " could not be verified.");
            if (final.Status == Found && !Same(final.Sha, peeled))
                return Refuse("final-tag-conflict", 3, "REFUSED: final tag " + finalName + " already points at " + final.Sha + ".");

            // Requalify every attempt, including read-only previews and same-final recovery.
            var authority = ReleaseAuthority.Check(paths.Root, checkout, green, candidate, options.Repository ?? "", remote.UtcNow(), apiUrl, options.ScriptsRoot, host.Reader);
            var gate = ReleaseGatePublication.Gate(green, candidate, live.Sha, authority, "", Array.Empty<string>());
            if (!gate.Ok)
            {
                var reasons = string.Join(",", gate.Reasons);
                return Refuse(reasons, 3, "REFUSED: publication gate (" + reasons + ").");
            }
            var publications = Path.Combine(releaseRoot, "publications.json");
            var reservation = ReleaseGateJournal.PromotionReservation(publications, candidateId, peeled, preTag, finalName);
            if (!reservation.Ok) return Refuse(reservation.Reason, 3, "REFUSED: " + reservation.Reason + ".");
            var resume = final.Status == Found;
            if (options.WhatIf)
            {
                Line(host, "WhatIf: would promote " + preTag + " to " + finalName + " at " + peeled);
                result.Resumed = resume;
                result.ExitCode = 0;
                return result;
            }

            if (!resume)
            {
                var made = remote.Git(checkout, "tag", "-a", finalName, peeled, "-m", "Antiphon " + finalName);
                if (made.ExitCode != 0)
                {
                    var shown = remote.Git(checkout, "rev-list", "-n", "1", finalName);
                    if (!Same(shown.ExitCode == 0 ? (shown.Output ?? "").Trim() : "", peeled))
                        return Refuse("tag-local-failed", 1, "REFUSED: local tag creation failed and no matching tag exists.");
                }
                if (remote.PushExactTag(checkout, finalName).ExitCode != 0)
                {
                    var again = remote.Tag(checkout, finalName);
                    if (again.Status != Found || !Same(again.Sha, peeled))
                        return Refuse("tag-push-failed", 1, "REFUSED: tag push failed and the remote tag does not hold the peeled sha.");
                }
                var verified = remote.Tag(checkout, finalName);
                if (verified.Status != Found || !Same(verified.Sha, peeled))
                    return Refuse("tag-peel", 3, "REFUSED: final tag peels to " + verified.Sha + ", expected " + peeled + ".");
            }

            // Fresh and resumed finals share the same checked persistence boundary.
            bool recorded;
            try
            {
                recorded = journals.RecordFinalTag(publications, candidateId, peeled, preTag, finalName);
            }
            catch (Exception e)
            {
                Line(host, "journal recording failed: " + e.Message);
                recorded = false;
            }
            if (!recorded)
                return Refuse("promotion-journal-write-failed", 1, "REFUSED: final tag " + finalName + " is verified at " + peeled + ", but its journal record failed.");
            result.Promoted = !resume;
            result.Resumed = resume;
            result.ExitCode = 0;
            Line(host, "promoted " + preTag + " to " + finalName + " at " + peeled);
            return result;
        }
    }
}
