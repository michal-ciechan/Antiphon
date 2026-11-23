// CARD-0599 (D-3, D-8) and CARD-1339: the release gate's remote boundary - the git-backed remote ref, head and tag reads,
// the exact tag push, the remote tag inventory, the GitHub release calls and the publisher's clock. Moved from
// scripts/lib/release-gate.ps1 (Get-ReleaseGateRemoteRef/Head/Tag, Push-ReleaseGateExactTag,
// Get-ReleaseGateRemoteTagInventory, Invoke-ReleaseGateGit, Invoke-ReleaseGateGitHub, Get-ReleaseGateUtcNow,
// Test-ReleaseGateTestMode and Import-ReleaseGatePublisherSeams), deleted with unit 12 of
// docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md.
//
// Git, GitHub and the clock are seams at the I/O boundary only: a seam answers for the remote or the time, never decides
// whether to publish. Production is NightlyNativeGit.Run, gh on a WatchedLauncher and the system clock. The
// still-PowerShell publisher, promoter and candidate coordinator hand in their test seams ($ReleaseGateSeams of a
// -SeamsPath file: Git, GitHub, UtcNow) through Seamed; the publisher admits them only in explicit test mode
// (ANTIPHON_RELEASE_GATE_TEST_MODE exactly '1', read by the caller and passed in) and refuses test mode whenever a real
// remote would be reached (Review bf928242), so a real publication never runs on an injected clock. Plain C#; the
// PowerShell seam answers are read by name (PsValue). ASCII only.
#nullable disable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using System.IO;
using System.Text.Json;

namespace Antiphon.Nightly
{
    /// <summary>Get-ReleaseGateRemoteTag: the peeled SHA (Sha) and the tag object (ObjectSha); Found only when both records are.</summary>
    public sealed record ReleaseGateRemoteTag(string Status, string Sha, string ObjectSha);

    /// <summary>Get-ReleaseGateRemoteTagInventory: the canonical release tags on the remote, or Ok false when the read is unusable.</summary>
    public sealed record ReleaseGateTagInventory(bool Ok, IReadOnlyList<string> Tags);

    /// <summary>One GitHub call's answer: the exit code, the captured output (release-view only), the verb and, for release-view, the release.</summary>
    public sealed class ReleaseGateGitHubResult
    {
        /// <summary>An answer.</summary>
        public ReleaseGateGitHubResult(int exitCode, string output, string verb, object body)
        {
            ExitCode = exitCode;
            Output = output ?? "";
            Verb = verb ?? "";
            Body = body;
        }

        /// <summary>gh's exit code (0 done).</summary>
        public int ExitCode { get; }

        /// <summary>release-view's JSON text; empty for the other verbs, whose output is this process's own.</summary>
        public string Output { get; }

        /// <summary>The verb answered.</summary>
        public string Verb { get; }

        /// <summary>
        /// release-view's release (id, name, url, isDraft, tagName, read by name), null when there is none. Production: the
        /// JSON object as a case-insensitive OrderedDictionary; a fake's own object otherwise.
        /// </summary>
        public object Body { get; }
    }

    /// <summary>One GitHub operation: the verb (release-view, release-create, release-upload, release-publish) and its argument bag.</summary>
    public delegate ReleaseGateGitHubResult ReleaseGateGitHub(string verb, IDictionary arguments, string workingDirectory);

    /// <summary>The release gate's remote: git, GitHub and the publisher's clock.</summary>
    public sealed class ReleaseGateRemote
    {
        /// <summary>The environment variable whose exact value '1' is explicit test mode. The caller reads it and passes TestMode(value) in.</summary>
        public const string TestModeVariable = "ANTIPHON_RELEASE_GATE_TEST_MODE";

        /// <summary>The empty 40-zero object id update-ref compares a new ref against.</summary>
        private const string ZeroSha = "0000000000000000000000000000000000000000";

        private readonly NightlyGit _git;
        private readonly ReleaseGateGitHub _gitHub;
        private readonly Func<DateTime> _clock;

        /// <summary>A remote over these seams; a null one is the production seam.</summary>
        public ReleaseGateRemote(NightlyGit git, ReleaseGateGitHub gitHub, Func<DateTime> clock)
        {
            _git = git ?? NightlyNativeGit.Run;
            _gitHub = gitHub ?? ProductionGitHub(new WatchedLauncher());
            _clock = clock ?? (() => DateTime.UtcNow);
        }

        /// <summary>The production remote: git on PATH, gh on PATH sharing this console, the system clock.</summary>
        public static ReleaseGateRemote Production() => new ReleaseGateRemote(null, null, null);

        /// <summary>Test-ReleaseGateTestMode: explicit opt-in only, the exact value '1'. Unset or anything else is production.</summary>
        public static bool TestMode(string value) => string.Equals(value, "1", StringComparison.Ordinal);

        /// <summary>
        /// Import-ReleaseGatePublisherSeams' admission, once the caller loaded the seams (it loads them only in test mode).
        /// Outside test mode a seams file is refused before it is loaded ('seams-outside-test-mode'); production is ''.
        /// In test mode a named seams file must exist ('missing seams' refusal) and both the Git and the GitHub adapters must
        /// be fakes: no seams file, or one without either adapter, would reach a real remote ('test-mode-real-remote').
        /// '' admits.
        /// </summary>
        public static string PublisherAdmission(bool testMode, string seamsPath, object git, object gitHub)
        {
            var hasSeams = !string.IsNullOrWhiteSpace(seamsPath);
            if (!testMode) return hasSeams ? "seams-outside-test-mode" : "";
            if (hasSeams && !File.Exists(seamsPath)) throw new NightlyRefusal("missing seams " + seamsPath);
            return git != null && gitHub != null ? "" : "test-mode-real-remote";
        }

        /// <summary>
        /// A remote over the PowerShell test seams of a -SeamsPath file ($ReleaseGateSeams.Git, .GitHub, .UtcNow; each null
        /// is production). git answers (WorkingDirectory, Arguments) and github (Verb, Arguments, WorkingDirectory) with a
        /// record whose ExitCode, Output and Body are read by name (a missing ExitCode reads as 0, no answer as a failure).
        /// utcNow answers the publisher's instant; it is refused outside test mode ('release-gate clock seam refused
        /// outside test mode') and an answer that is no instant is refused too, never replaced by the wall clock.
        /// </summary>
        public static ReleaseGateRemote Seamed(Func<string, string[], object> git, Func<string, IDictionary, string, object> gitHub, Func<object> utcNow,
            bool testMode)
        {
            NightlyGit seamGit = null;
            if (git != null)
                seamGit = (workingDirectory, arguments) =>
                {
                    var answer = Single(git(workingDirectory, ToArray(arguments)));
                    if (answer == null) return new NightlyGitResult(1, "");
                    var code = PsValue.Prop(answer, "ExitCode");
                    return new NightlyGitResult(code == null ? 0 : PsOps.ToInt(code), PsValue.Str(PsValue.Prop(answer, "Output")));
                };
            ReleaseGateGitHub seamGitHub = null;
            if (gitHub != null)
                seamGitHub = (verb, arguments, workingDirectory) =>
                {
                    var answer = Single(gitHub(verb, arguments, workingDirectory));
                    if (answer == null) return new ReleaseGateGitHubResult(1, "", verb, null);
                    var code = PsValue.Prop(answer, "ExitCode");
                    return new ReleaseGateGitHubResult(code == null ? 0 : PsOps.ToInt(code), PsValue.Str(PsValue.Prop(answer, "Output")), verb,
                        PsValue.Prop(answer, "Body"));
                };
            Func<DateTime> clock = null;
            if (utcNow != null)
                clock = () =>
                {
                    if (!testMode) throw new NightlyRefusal("release-gate clock seam refused outside test mode");
                    var value = PsValue.Unwrap(Single(utcNow()));
                    if (value is DateTime instant) return instant.ToUniversalTime();
                    throw new NightlyRefusal("release-gate clock seam returned no instant");
                };
            return new ReleaseGateRemote(seamGit, seamGitHub, clock);
        }

        // A PowerShell answer: one object, or the last of several outputs (the function's return value).
        private static object Single(object answer)
        {
            if (answer is object[] many) return many.Length == 0 ? null : many[many.Length - 1];
            return answer;
        }

        private static string[] ToArray(IReadOnlyList<string> arguments)
        {
            var copy = new string[arguments?.Count ?? 0];
            for (var i = 0; i < copy.Length; i++) copy[i] = arguments[i];
            return copy;
        }

        /// <summary>Invoke-ReleaseGateGit: one git call in the working directory.</summary>
        public NightlyGitResult Git(string workingDirectory, params string[] arguments) =>
            _git(workingDirectory, arguments ?? Array.Empty<string>()) ?? new NightlyGitResult(1, "");

        /// <summary>
        /// Invoke-ReleaseGateGitHub: one call shape for every GitHub operation, the verb plus an argument bag (tag,
        /// notesFile, prerelease, asset). Production shells out to the already-authorized gh CLI; nothing here mints, reads
        /// or prints a credential.
        /// </summary>
        public ReleaseGateGitHubResult GitHub(string verb, IDictionary arguments, string workingDirectory) =>
            _gitHub(verb, arguments ?? new Hashtable(), workingDirectory ?? "");

        /// <summary>Get-ReleaseGateUtcNow (D-15): the instant evidence timestamps are bounded by, in UTC.</summary>
        public DateTime UtcNow() => _clock().ToUniversalTime();

        /// <summary>Get-ReleaseGateRemoteRef: one fully qualified ref (refs/heads/... or refs/tags/...) on origin, matched exactly.</summary>
        public ReleaseGateRef Ref(string workingDirectory, string reference)
        {
            reference ??= "";
            string kind;
            if (reference.StartsWith("refs/heads/", StringComparison.Ordinal)) kind = "--heads";
            else if (reference.StartsWith("refs/tags/", StringComparison.Ordinal)) kind = "--tags";
            else throw new NightlyRefusal("remote ref must be fully qualified");
            return ReleaseGateTags.RemoteRef(Git(workingDirectory, "ls-remote", kind, "origin", reference), reference);
        }

        /// <summary>Get-ReleaseGateRemoteHead: a branch on origin; a bare name is read as refs/heads/&lt;name&gt;.</summary>
        public ReleaseGateRef Head(string workingDirectory, string reference)
        {
            reference ??= "";
            if (!reference.StartsWith("refs/heads/", StringComparison.Ordinal)) reference = "refs/heads/" + reference;
            return Ref(workingDirectory, reference);
        }

        /// <summary>
        /// Get-ReleaseGateRemoteTag: the tag's peeled record (refs/tags/&lt;tag&gt;^{}) and its direct record. Both absent is
        /// Absent; anything but both found is Failed (a lightweight tag has no peeled record); Found carries the peeled SHA.
        /// </summary>
        public ReleaseGateRemoteTag Tag(string workingDirectory, string tag)
        {
            var reference = "refs/tags/" + tag;
            var peel = Ref(workingDirectory, reference + "^{}");
            var direct = Ref(workingDirectory, reference);
            if (peel.Status == ReleaseGateRef.Absent && direct.Status == ReleaseGateRef.Absent) return new ReleaseGateRemoteTag(ReleaseGateRef.Absent, "", "");
            if (peel.Status != ReleaseGateRef.Found || direct.Status != ReleaseGateRef.Found) return new ReleaseGateRemoteTag(ReleaseGateRef.Failed, "", direct.Sha);
            return new ReleaseGateRemoteTag(ReleaseGateRef.Found, peel.Sha, direct.Sha);
        }

        /// <summary>
        /// Push-ReleaseGateExactTag: git applies DWIM even to explicit push destinations, while pattern refspecs map
        /// complete refs literally. One such mapping gets its own private namespace (refs/antiphon-release-push/&lt;guid&gt;/)
        /// holding only our annotated tag object, created and then removed with a compare-and-swap. Never --force. Exit 1
        /// when the local tag is unreadable, the private ref cannot be made or removed; otherwise the push's own answer.
        /// </summary>
        public NightlyGitResult PushExactTag(string workingDirectory, string tag)
        {
            var failed = new NightlyGitResult(1, "");
            var shown = Git(workingDirectory, "show-ref", "--verify", "--hash", "refs/tags/" + tag);
            var sha = (shown.Output ?? "").Trim();
            if (shown.ExitCode != 0 || !ReleaseGateCredit.FullSha(sha)) return failed;
            var prefix = "refs/antiphon-release-push/" + Guid.NewGuid().ToString("N") + "/";
            var privateRef = prefix + tag;
            if (Git(workingDirectory, "update-ref", privateRef, sha, ZeroSha).ExitCode != 0) return failed;
            var push = failed;
            NightlyGitResult cleanup;
            try
            {
                push = Git(workingDirectory, "push", "origin", prefix + "*:refs/tags/*");
            }
            finally
            {
                cleanup = Git(workingDirectory, "update-ref", "-d", privateRef, sha);
            }
            return cleanup.ExitCode != 0 ? failed : push;
        }

        /// <summary>
        /// Get-ReleaseGateRemoteTagInventory (CARD-1339): ls-remote --tags --refs origin, kept to canonical release tag names
        /// (vYYYY.MM.DD.N[-rc.K] spelled as NewTag spells them; anything else under refs/tags is skipped). A failed read, a
        /// record that is not two fields, a canonical tag without a full SHA, or one tag named at two SHAs is Ok false.
        /// The tags are in first-seen order.
        /// </summary>
        public ReleaseGateTagInventory TagInventory(string workingDirectory)
        {
            var failed = new ReleaseGateTagInventory(false, Array.Empty<string>());
            var read = Git(workingDirectory, "ls-remote", "--tags", "--refs", "origin");
            if (read.ExitCode != 0) return failed;
            var order = new List<string>();
            var names = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var line in (read.Output ?? "").Split('\n'))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var fields = line.Trim().Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                if (fields.Length != 2) return failed;
                var reference = fields[1];
                if (!reference.StartsWith("refs/tags/", StringComparison.Ordinal)) continue;
                var tag = reference.Substring("refs/tags/".Length);
                var parsed = ReleaseGateTags.Tag(tag);
                if (!parsed.Ok) continue;
                if (!string.Equals(tag, ReleaseGateTags.Canonical(parsed), StringComparison.Ordinal)) continue;
                if (!ReleaseGateCredit.FullSha(fields[0])) return failed;
                if (names.TryGetValue(tag, out var seen))
                {
                    if (!string.Equals(seen, fields[0], StringComparison.Ordinal)) return failed;
                    continue;
                }
                names[tag] = fields[0];
                order.Add(tag);
            }
            return new ReleaseGateTagInventory(true, order.ToArray());
        }

        /// <summary>
        /// gh's arguments for a verb: release-view (--json id,name,url,isDraft,tagName), release-create (--verify-tag
        /// --draft --title &lt;tag&gt; --notes-file &lt;file&gt;, and --prerelease when the bag says so), release-upload
        /// (--clobber) and release-publish (edit --draft=false). Another verb is refused.
        /// </summary>
        public static IReadOnlyList<string> GitHubArguments(string verb, IDictionary arguments)
        {
            string Arg(string name) => arguments != null && arguments.Contains(name) ? PsValue.Str(arguments[name]) : "";
            switch (verb)
            {
                case "release-view":
                    return new[] { "release", "view", Arg("tag"), "--json", "id,name,url,isDraft,tagName" };
                case "release-create":
                    var create = new List<string> { "release", "create", Arg("tag"), "--verify-tag", "--draft", "--title", Arg("tag"), "--notes-file", Arg("notesFile") };
                    if (arguments != null && arguments.Contains("prerelease") && PsValue.IsTrue(arguments["prerelease"])) create.Add("--prerelease");
                    return create;
                case "release-upload":
                    return new[] { "release", "upload", Arg("tag"), Arg("asset"), "--clobber" };
                case "release-publish":
                    return new[] { "release", "edit", Arg("tag"), "--draft=false" };
                default:
                    throw new NightlyRefusal("unknown github verb " + verb);
            }
        }

        /// <summary>
        /// The production GitHub seam: gh (on PATH) in the working directory on the launcher, Watched custody. The write
        /// verbs share this process's console as Start-Process -NoNewWindow did; release-view's output is captured (never
        /// printed) and its JSON object is the answer's Body (null when gh failed or printed no object).
        /// </summary>
        public static ReleaseGateGitHub ProductionGitHub(INightlyLauncher launcher)
        {
            if (launcher == null) throw new ArgumentNullException(nameof(launcher));
            return (verb, arguments, workingDirectory) =>
            {
                var argv = GitHubArguments(verb, arguments);
                var view = verb == "release-view";
                var ran = launcher.Run(new NightlyLaunch("gh", argv, workingDirectory ?? "", CaptureOutput: view));
                object body = view && ran.ExitCode == 0 ? JsonObjectOrNull(ran.LogText) : null;
                return new ReleaseGateGitHubResult(ran.ExitCode, view ? ran.LogText : "", verb, body);
            };
        }

        /// <summary>A JSON object as a case-insensitive OrderedDictionary of plain values; null when the text is not one.</summary>
        internal static OrderedDictionary JsonObjectOrNull(string text)
        {
            try
            {
                using var document = JsonDocument.Parse(text ?? "");
                return document.RootElement.ValueKind == JsonValueKind.Object ? (OrderedDictionary)Plain(document.RootElement) : null;
            }
            catch (JsonException) { return null; }
        }

        /// <summary>A JSON value as plain values: objects case-insensitive OrderedDictionary, arrays object[], integers long, other numbers double.</summary>
        public static object Plain(JsonElement value)
        {
            switch (value.ValueKind)
            {
                case JsonValueKind.Object:
                    var map = new OrderedDictionary(StringComparer.OrdinalIgnoreCase);
                    foreach (var property in value.EnumerateObject()) map[property.Name] = Plain(property.Value);
                    return map;
                case JsonValueKind.Array:
                    var items = new List<object>();
                    foreach (var item in value.EnumerateArray()) items.Add(Plain(item));
                    return items.ToArray();
                case JsonValueKind.String: return value.GetString();
                case JsonValueKind.Number:
                    var raw = value.GetRawText();
                    if (raw.IndexOfAny(new[] { '.', 'e', 'E' }) < 0 && value.TryGetInt64(out var whole)) return whole;
                    return value.GetDouble();
                case JsonValueKind.True: return true;
                case JsonValueKind.False: return false;
                default: return null;
            }
        }
    }
}
