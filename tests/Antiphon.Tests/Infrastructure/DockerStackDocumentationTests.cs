using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Infrastructure;

// CARD-0604 S6, R-6. The docs name the new shape AND the old sentences are gone. A superseded
// paragraph left standing is worse than a missing one: the next reader follows it.
[Category("Unit")]
public sealed class DockerStackDocumentationTests
{
    [Test]
    public void Recycle_census_preflight_is_documented()
    {
        var text = Read("docs/docker-stack.md");
        var preflight = text.IndexOf("-Phase check-census", StringComparison.Ordinal);
        preflight.ShouldBeGreaterThanOrEqualTo(0);
        preflight.ShouldBeLessThan(text.IndexOf("-Phase check-host-jq", StringComparison.Ordinal));
        foreach (var pin in new[] { "RECYCLE_PROJECT", "RECYCLE_CENSUS", "RecycleProjectUnresolved",
                     "landPending", "Queued/Dispatched/Working/Blocked", "cause=NoMatch", "-ProjectId", "cause=Http status=400",
                     "four attempts", "backoffs of 2s, 5s and 10s", "Drift and Unstable", "PartialRefused",
                     "Main keeps the v1 collector: three attempts" })
            text.ShouldContain(pin);
    }

    // CARD-1353 D-8/D-10. Lands are desktop work; the docs drop the rollout land freeze, state the
    // runner-bound census rule with its bounded wait, keep the AppHost pull wait, and the CARD-1008
    // plan rows that pinned the old rule carry an in-place superseded note.
    [Test]
    public void Lands_proceed_during_rollout_is_documented()
    {
        var text = Read("docs/docker-stack.md");
        foreach (var pin in new[] { "boundLandPending", "bound to the runner being recycled", "-LandWaitMinutes",
                     "C1008_LAND_WAIT_SECONDS", "C1008_LAND_POLL_SECONDS", "RECYCLE_LAND_WAIT", "C1008_LAND_WAIT", "CARD-1353" })
            text.ShouldContain(pin, Case.Sensitive, "c1353-docs: " + pin);
        var census = text.IndexOf("The wrapper and host census each walk two server-filtered closures", StringComparison.Ordinal);
        census.ShouldBeGreaterThanOrEqualTo(0, "c1353-docs: census paragraph");
        var end = text.IndexOf("Always retire `server2-temp`", census, StringComparison.Ordinal);
        end.ShouldBeGreaterThan(census, "c1353-docs: census paragraph end");
        var paragraph = text[census..end];
        paragraph.ShouldContain("cannot end", Case.Sensitive, "c1353-docs: off-runner lands are outside the compared snapshot");
        paragraph.ShouldContain("cause=Unstable", Case.Sensitive, "c1353-docs: the Unstable cause stays named");
        paragraph.ShouldContain("RECYCLE_LAND_WAIT", Case.Sensitive, "c1353-docs: the wait line is named in the census paragraph");
        foreach (var gone in new[] { "verify no land is pending", "Keep the no-new-land interval",
                     "host census refuse `RecycleLandInFlight` on the row itself" })
            text.ShouldNotContain(gone, Case.Sensitive, "c1353-docs: the freeze sentence must be gone: " + gone);

        var loop = Read("docs/orchestration-loop.md");
        loop.ShouldContain("need no land freeze", Case.Sensitive, "c1353-docs: the rolling phases need no land freeze");
        loop.ShouldContain("never pull while a land runs", Case.Sensitive, "c1353-docs: the AppHost pull wait stays");
        loop.ShouldContain("retained temp volume blocks deploy-temp", Case.Sensitive, "c1609-docs: retained volumes block deploy-temp");
        loop.ShouldContain("retire-runner-volumes.ps1 -Close", Case.Sensitive, "c1609-docs: Close is the audited journal command");
        loop.ShouldContain("tempPrivateVolumes=0", Case.Sensitive, "c1609-docs: Retired still requires zero temp-private volumes");

        var plan = Read("docs/superpowers/plans/2026-10-03-card-1008-rolling-volume-recycle-and-retire-temp-plan.md");
        System.Text.RegularExpressions.Regex.Matches(plan, "Superseded 2026-10-19 by CARD-1353 D-1").Count
            .ShouldBe(3, "c1353-docs: PC-9, PC-49 and PC-51 carry the in-place note");
    }

    [Test]
    public void Retired_temp_cleanup_and_retirement_gates_match()
    {
        var text = Read("docs/docker-stack.md");
        foreach (var expected in new[] { "temp container absence", "including already-absent cleanup", "one deadline",
            "TempContainerExitTimeout", "exited retired temp", "normal shutdown", "pending confirmed absence",
            "never starts new container cleanup", "Docker prune, other volumes, markers and donor archives remain human-gated",
            "Assert-TempRetirementClearable", "before `/drain/clear`", "TempVolumesRetained", "tempPrivateVolumes=0",
            "-Close -OperationId", "journal transition, not a delete" })
            text.ShouldContain(expected, Case.Sensitive, "c994-runbook: " + expected);
        text.ShouldNotContain("Current retire-temp guard caveat", Case.Sensitive, "c994-runbook: obsolete null guard caveat");
        text.ShouldNotContain("Until CARD-0994/CARD-1008", Case.Sensitive, "c994-runbook: obsolete implementation caveat");
    }

    [Test]
    public void Main_volume_recycling_is_scripted_only()
    {
        var text = Read("docs/docker-stack.md");
        const string heading = "### Volume recycling and disk reclaim (CARD-1008)";
        var start = text.IndexOf(heading, StringComparison.Ordinal);
        start.ShouldBeGreaterThanOrEqualTo(0);
        var end = text.IndexOf(" ### ", start + heading.Length, StringComparison.Ordinal);
        var section = end < 0 ? text[start..] : text[start..end];

        section.ShouldContain("Assert-ZeroCounters", Case.Sensitive, "the stopped-main refusal must be explained");
        section.ShouldContain("RunnerCounterUnknown", Case.Sensitive, "a stopped main makes redeploy-old refuse");
        section.ShouldContain("scripted only");
        section.ShouldContain("GIT_NO_LAZY_FETCH=1");
        // CARD-1105: the linked worktree's own reflog and the pseudorefs are tips; the old
        // sentence claimed every worktree reflog was read while only the common logs were.
        section.ShouldContain("in each linked worktree's own logs");
        // CARD-1105 repair 4: AUTO_MERGE (a tree) left the tip list; it is ignored after the
        // operation and refuses while one is in progress.
        section.ShouldContain("ORIG_HEAD, FETCH_HEAD, REBASE_HEAD, BISECT_HEAD and MERGE_AUTOSTASH pseudorefs");
        section.ShouldContain("AUTO_MERGE, the tree Git leaves after a conflicted merge or rebase, is ignored when no merge");
        section.ShouldContain("an entry outside it refuses `RecycleGitAuditUnknown`");
        // CARD-1105 repair 4: present-origin comparison, admin-directory scan, commit-graph
        // off, product/agent state, the ignored-files decision and the overall budget.
        section.ShouldContain("`core.commitGraph=false`");
        // CARD-1105 repair 6: origin's real heads are fetched into a scratch proof repository; the
        // present-only comparison is the fallback, and runner-tmp checkouts are read in a read-only mount.
        section.ShouldNotContain("against the origin heads that `ls-remote` advertises and that are present in the clone", Case.Sensitive,
            "the present-only comparison is now the fallback");
        section.ShouldNotContain("An advertised head the clone has not fetched is skipped", Case.Sensitive, "fetched heads are compared");
        section.ShouldContain("fetches origin's heads without blobs (`git fetch --filter=blob:none`) into a scratch bare repository");
        section.ShouldContain("so the audited volume is never written");
        section.ShouldContain("only the advertised heads present in the clone are compared, which can only refuse more");
        section.ShouldContain("which the audit helper mounts read-only beside `/work`");
        section.ShouldContain("counts `commits`, `tips`, `gone-tracking`");
        section.ShouldNotContain("must prove publication against current origin heads", Case.Sensitive,
            "the exact-advertisement comparison is gone");
        section.ShouldContain("audited whether or not its checkout still exists");
        section.ShouldContain("a non-empty `antiphon/children/` journal means a repository mutation may still be running and refuses unknown");
        section.ShouldContain("`antiphon/` (the never-unlinked `landing.lock` lease file, reviews and verification evidence) is product state, not repository state");
        // CARD-1594: antiphon-evidence/ and review-evidence/ are both walked for Git state.
        // Plain files and directories stay allowed. File content is never read.
        section.ShouldContain("`antiphon-evidence/` is allowed only in the common directory");
        section.ShouldContain("`antiphon/` is safe because it is product state");
        section.ShouldContain("`review-evidence/` is agent evidence rather than repository state");
        section.ShouldContain("It is walked for Git state with the same rules as `antiphon-evidence/`");
        section.ShouldContain("The audit never reads file content, so a patch or notes there are deleted with the volume");
        section.ShouldContain("A symlink, any other file type, a regular file with a second hard link, or an entry the audit cannot read refuses `RecycleGitAuditUnknown` (`gitdir-entry`)");
        section.ShouldContain("`.git` (any type), `HEAD`, `ORIG_HEAD`, `FETCH_HEAD`, `MERGE_HEAD`, `CHERRY_PICK_HEAD`, `REVERT_HEAD`, `REBASE_HEAD`, `BISECT_HEAD`, `AUTO_MERGE`, `MERGE_AUTOSTASH`, `commondir`, `gitdir`, `objects`, `refs`, `packed-refs`, `reftable`, `shallow`, and any name containing a control character");
        section.ShouldContain("appends `entry=`");
        section.ShouldNotContain("a dot-name (including a nested `.git`)", Case.Sensitive, "ordinary dot-names under evidence trees stay allowed");
        section.ShouldContain("There is no depth, file-count or size cap");
        section.ShouldContain("an executable bit on a regular file is allowed");
        section.ShouldContain("A directory named `logs` is allowed");
        // CARD-1335: the linked-admin mirror-owner stamp is on the closed list, and a
        // gitdir-entry refusal names that admin.
        section.ShouldContain("antiphon-mirror-owner.json");
        section.ShouldContain("at most 4096 bytes");
        section.ShouldContain("A `gitdir-entry` refusal names that admin directory");
        section.ShouldContain("a file hidden by an ignore rule is accepted by design");
        // CARD-1105 repair 7, raised by CARD-1316: the default budget is 10800 s
        // (a clean standing volume measured 5225 s). The proof fetches share a 600 s
        // allowance, tmp paths are translated before resolving, nested Git metadata in a
        // tmp checkout is classified as under /work.
        section.ShouldContain("the whole audit has a 3-hour (10800 s) budget");
        section.ShouldNotContain("30-minute budget", Case.Sensitive, "the budget is 10800 s");
        section.ShouldNotContain("50-minute (3000 s) budget", Case.Sensitive, "CARD-1316 raised the standing-volume cap");
        section.ShouldContain("once all proof fetches of the audit together have used their 600 s allowance");
        section.ShouldContain("is translated to the tmp mount before anything resolves it");
        section.ShouldContain("a `..` in the recorded path refuses");
        // CARD-1105 repair 8: a link target or recorded path is never normalized (a trailing newline).
        section.ShouldContain("Link targets and recorded paths are read byte for byte; one holding a control character (a newline, CR or NUL included) cannot be resolved exactly and refuses unknown.");
        section.ShouldContain("Inside a registered tmp checkout every `.git` entry and every Git-shaped `HEAD` directory is classified as under `/work`");
        section.ShouldNotContain("resolving outside the tmp mount through a symlink refuses unknown", Case.Sensitive, "the tmp path is proven, not resolved first");
        section.ShouldNotContain("each worktree's private refs and HEAD (also bare HEAD), and both sides of every reflog entry must prove publication",
            Case.Sensitive, "the false every-reflog sentence is gone");
        // CARD-1105 repair 5: an existing orphaned checkout is inspected, non-commit tips must be
        // the advertised object, symlinks and HEAD-named files follow checkout ownership.
        section.ShouldContain("its files are inspected even if the checkout's `.git` pointer is missing or names another directory");
        section.ShouldContain("must be the very object origin advertises (`ls-remote` heads and tags)");
        section.ShouldContain("Symlinks are never followed: a tracked symlink compares by its link text");
        section.ShouldContain("A file named `HEAD` counts as a repository only in a directory shaped like a Git directory");
        section.ShouldContain("ignored content and a moving origin leave the before/after comparison equal");
        section.ShouldNotContain("Noncommit tips, unsupported index modes and grafts refuse unknown", Case.Sensitive,
            "non-commit tips are proven as objects, not refused unknown");
        section.ShouldNotContain("Its checkout's files are inspected only when its `gitdir` file and the checkout's `.git` file name each other",
            Case.Sensitive, "an existing checkout with a lost pointer is inspected");
        section.ShouldContain("git clone --filter=blob:none --no-checkout");
        section.ShouldContain("RecycleGitAuditUnknown");
        section.ShouldContain("GithubTokenAbsent");
        // CARD-1607: a private origin is proved on the desktop. The container still
        // receives no credential. With no proof, the preview environment is unchanged.
        section.ShouldContain("When no host proof is supplied, the preview passes that same environment and nothing further.", Case.Sensitive);
        section.ShouldContain("No GitHub token, SSH agent, or git credential helper is passed.", Case.Sensitive);
        section.ShouldContain("passes only ref names and object ids through a read-only bind", Case.Sensitive);
        section.ShouldContain("No credential enters the container.", Case.Sensitive);
        section.ShouldContain("Any other ls-remote failure stays a refusal.", Case.Sensitive);
        section.ShouldContain("The proof is bound to operation id, operation start, repository path, and origin URL hash", Case.Sensitive);
        section.ShouldContain("a missing, empty, unreadable, stale, or mismatched proof refuses `RecycleGitAuditUnknown`", Case.Sensitive);
        section.ShouldContain("Apply asks the desktop again rather than reusing the preview proof.", Case.Sensitive);
        section.ShouldContain("asks the desktop again for fresh proofs", Case.Sensitive);
        foreach (var volume in new[] { "work", "runner-tmp", "dind-data" })
            section.ShouldNotContain($"docker volume rm antiphon-runner_{volume}", Case.Sensitive,
                "manual main-volume removal cannot precede redeploy-old");
        // CARD-1609: temp deletion uses the v2 census. Main stays on the v1 collector.
        section.ShouldContain("four attempts", Case.Sensitive, "c1609-docs: temp census attempts");
        section.ShouldContain("backoffs of 2s, 5s and 10s", Case.Sensitive, "c1609-docs: temp census backoff");
        section.ShouldContain("Drift and Unstable", Case.Sensitive, "c1609-docs: only those causes retry");
        section.ShouldContain("PartialRefused", Case.Sensitive, "c1609-docs: a failed down -v stays partial");
        section.ShouldContain("Main keeps the v1 collector: three attempts", Case.Sensitive, "c1609-docs: main census is unchanged");
        section.ShouldContain("tempPrivateVolumes=0", Case.Sensitive, "c1609-docs: Retired zero is still required");
    }

    [Test]
    public void Docker_stack_doc_names_the_nested_daemon()
    {
        var text = Read("docs/docker-stack.md");
        text.ShouldContain("nested");
        text.ShouldContain("docker-compose.server2-runner.yml");
        text.ShouldContain("privileged");
        text.ShouldContain("SiblingDaemonRefused");
        text.ShouldContain("dind-entrypoint.sh");

        // The retired sibling lane and its variable must not still be offered as instructions.
        text.Contains("Pass `DOCKER_SOCKET_GID` from the socket's group", StringComparison.Ordinal)
            .ShouldBeFalse("the socket-gid instruction is gone with the sibling lane");
        text.Contains("is the only application override that mounts the socket", StringComparison.Ordinal)
            .ShouldBeFalse("no compose file mounts a socket any more");
        text.Contains("SourceLanding Mutation stays on the Windows local path (CARD-0598)", StringComparison.Ordinal)
            .ShouldBeFalse("that sentence is re-aimed at the two backends, not left as-is");
        text.ShouldContain("windows-job-v1");
        text.ShouldContain("linux-cgroup-v1");
        text.ShouldContain("not a custody receipt");
        // CARD-0628 G-17c.
        text.ShouldContain("Claude Code 2.1.280");
        text.ShouldContain("1e08503d");
        // CARD-0628 Round D.
        text.ShouldContain("CLAUDE_OAUTH_TOKEN_FILE");
        text.ShouldContain("/run/antiphon/claude-oauth-token");
    }

    // CARD-0604 S12, R-6 (Cut B). The docs name the second custody backend and what it actually
    // is, and the sentence that said the server2 runner is not a supported producer is gone --
    // a superseded refusal left standing sends the next reader to the wrong lane.
    [Test]
    public void Docs_name_linux_cgroup_backend()
    {
        var stack = Read("docs/docker-stack.md");
        stack.ShouldContain("linux-cgroup-v1");
        stack.ShouldContain("antiphon-custody-enter");
        stack.ShouldContain("antiphon-custody-kill");
        stack.ShouldContain("no_new_privs");
        stack.ShouldContain("sudoers.d/antiphon-custody");
        // The operator's confirmed boundary: container-internal, not server2's host sudoers.
        stack.ShouldContain("container-internal");
        stack.ShouldContain("does not touch server2's host sudoers");
        stack.Contains("not** a supported producer until that cut lands", StringComparison.Ordinal)
            .ShouldBeFalse("the Cut A refusal sentence is superseded");

        var testing = Read("docs/testing-and-build.md");
        testing.ShouldContain("linux-cgroup-v1");
        testing.ShouldContain("delegate.ps1 -Role Mutation -Runner server2");
        testing.ShouldContain("provider_sign_in_required");
        testing.ShouldContain("restoration.json");

        var credentials = Read("docs/agent-credentials.md");
        credentials.ShouldContain("runner-state");
        credentials.ShouldContain("grok login");
        credentials.ShouldContain("verification-custody");
    }

    [Test]
    public void Orchestration_loop_names_server2_producer()
    {
        var text = Read("docs/orchestration-loop.md");
        text.ShouldContain("two supported Mutation producers");
        text.ShouldContain("-Runner server2");
        text.ShouldContain("linux-cgroup-v1");
        // The fence that survives the new lane: the nested daemon is not an executor.
        text.ShouldContain("nested Docker daemon inside that runner is still never an executor");
    }

    [Test]
    public void Credentials_doc_names_the_deploy_key_custody()
    {
        var text = Read("docs/agent-credentials.md");
        text.ShouldContain("antiphon-server2-runner");
        text.ShouldContain("/run/antiphon/deploy-key");
        text.ShouldContain("tmpfs");
        text.ShouldContain("/run/secrets/phone-home");
        text.ShouldContain("DeployKeyMissing");
        text.ShouldContain("PhoneHomeSecretMissing");
        text.ShouldContain("ssh.github.com:443");

        // Names and locations only. A real key or secret in a tracked doc is the failure this
        // paragraph exists to prevent.
        text.Contains("BEGIN OPENSSH PRIVATE KEY", StringComparison.Ordinal).ShouldBeFalse();
        System.Text.RegularExpressions.Regex.IsMatch(text, @"\b[0-9a-f]{64}\b")
            .ShouldBeFalse("a 64-hex literal in the credentials doc looks like the phone-home secret");
    }

    [Test]
    public void Card_0590_D6_is_annotated_superseded()
    {
        var text = Read("docs/superpowers/plans/2026-09-21-card-0590-self-contained-docker-stack-plan.md");
        text.ShouldContain("Superseded 2026-09-22 by CARD-0604");
        // Both the re-aimed decisions carry the annotation IN PLACE, and D-14 is cited as standing.
        text.ShouldContain("CARD-0604 D-1/D-5/D-6");
        text.ShouldContain("CARD-0604 D-12/D-17");
        text.ShouldContain("D-14 below is untouched and still binding");
        // Cut B's own annotation, in place on the paragraph it supersedes.
        text.ShouldContain("Superseded 2026-09-22 by CARD-0604 D-13/D-17/D-19 (Cut B)");
        text.ShouldContain("no guard was removed to make the Linux lane pass");
    }

    [Test]
    public void Ops_http_names_runner_status_and_runner_id()
    {
        var text = Read("docs/ops-http.md");
        text.ShouldContain("/api/session-runners/{runnerId}/status");
        text.ShouldContain("dispatchEligible");
        text.ShouldContain("`runnerId`");
        // The distinction that costs a wasted dispatch if it is missed.
        text.ShouldContain("`available` alone is not enough to dispatch");
    }

    [Test]
    public void Testing_doc_names_the_production_enablement_steps()
    {
        var text = Read("docs/testing-and-build.md");
        text.ShouldContain("PhoneHomeRunner:Runners");
        text.ShouldContain("Runners:server2");
        text.ShouldContain("Runners:server2-temp");
        text.ShouldContain("AllowDelegatedTasks=true");
        text.ShouldContain("restart-apphost.ps1");
        text.ShouldContain("delegate.ps1 -Runner server2");
        // The rule that caused CARD-0358: enablement runs from the main checkout, not a worktree.
        text.ShouldContain("never a worktree");
        text.ShouldContain("--ff-only");
    }

    [Test]
    public void Agent_kinds_doc_names_the_raw_allow_list()
    {
        var text = Read("docs/agent-kinds.md");
        text.ShouldContain("runner-bound");
        text.ShouldContain("/usr/local/bin/pwsh");
        // CARD-0628 G-17a: the sentence changes with the image, and the old one is gone.
        text.ShouldContain("Grok and Claude Code are the agents the image carries");
        text.ShouldContain("Codex is not installed there and stays refused");
        text.ShouldContain("are Grok or Claude Code (CARD-0628)");
        text.Contains("Grok is the only agent the image carries", StringComparison.Ordinal)
            .ShouldBeFalse("the superseded sentence is not left standing");
        text.ShouldContain("phone_home_env_refused");
        text.ShouldContain("phone_home_remote_control_refused");
    }

    // CARD-0628 G-17: the operator's auth decision in names and locations only -- the setup-token
    // is primary, the interactive login the fallback, an API key never -- and no secret-shaped text.
    [Test]
    public void Credentials_doc_names_the_claude_login()
    {
        var text = Read("docs/agent-credentials.md");
        text.ShouldContain("CLAUDE_CODE_OAUTH_TOKEN");
        text.ShouldContain("claude setup-token");
        text.ShouldContain("/state/claude");
        text.ShouldContain("claude auth login");
        text.ShouldContain("provider_sign_in_required");
        text.ShouldContain("phone_home_env_refused");
        text.ShouldContain("`ANTHROPIC_API_KEY` is never a fallback");
        text.IndexOf("**Primary:** `CLAUDE_CODE_OAUTH_TOKEN`", StringComparison.Ordinal)
            .ShouldBeInRange(0, text.IndexOf("**Fallback:** interactive login store", StringComparison.Ordinal));
        // Round D: the file-mount custody, and the superseded environment pass-through is gone.
        text.ShouldContain("antiphon/server2/claude-oauth-token");
        text.ShouldContain("/home/mc/antiphon-server2/secrets/claude_oauth_token");
        text.ShouldContain("/run/antiphon/claude-oauth-token");
        text.Contains("passes `CLAUDE_CODE_OAUTH_TOKEN` through from that environment", StringComparison.Ordinal)
            .ShouldBeFalse("the compose environment pass-through is superseded");
        text.Contains("sk-ant-", StringComparison.Ordinal).ShouldBeFalse("no Anthropic key or token fragment");
        System.Text.RegularExpressions.Regex.IsMatch(text, @"\b[0-9a-f]{64}\b")
            .ShouldBeFalse("the Claude digest lives in the Dockerfile and docker-stack.md, never here");
    }

    // CARD-1105 R-1. Whitespace is collapsed by Read, so each pin is a phrase the prose must still
    // contain after wrapping. A wrong sentence fails the collected list by name.
    [Test]
    public void Mount_generation_protocol_is_documented()
    {
        var text = Read("docs/docker-stack.md");
        var pins = new[]
        {
            "### Mount generations and the rolling proofs (CARD-1105)",
            "RecycleGenerationUnknown",
            "RecycleGenerationMismatch",
            "RecycleComposeMismatch",
            "SOURCE_REVISION",
            "C1008_GENERATION",
            "add it to the target roster in c1008_compose_model and to the bind-kind table in the same commit as the Compose change",
            "keep its bind-kind entry and keep exporting its host-path variable for at least one generation",
            "accepts the running container's own generation and requires the new one after recreate",
            "A state-init-only temp binds by SOURCE_REVISION alone",
            "Host Compose v2.18.1",
            "scripts/fixtures/c1105-compose-v218-config.json",
            "For the host project in apply mode",
            "C1008_PROJECT == HOST_PROJECT",
            "C1008_DRY_RUN == 0",
            "C1105_Compose_cmp_refuses_before_removal",
            "same-count replacement with unique destinations passes that count guard",
            "error(\"mount count\")",
            "error(\"destination\")",
            "error(\"mount identity\")",
            "RecycleContainerStateUnknown",
            "c1008_compose_model",
            "error(\"mount set\")",
            "same expected roster does not take that refusal",
        };
        var missing = new List<string>();
        foreach (var pin in pins)
            if (!text.Contains(pin, StringComparison.Ordinal)) missing.Add(pin);
        missing.ShouldBeEmpty("mount-generation pins missing: " + string.Join(" | ", missing));
        // CARD-1142: the mount-set refusal is the rendered roster versus the expected block.
        text.Contains("on-disk Compose file is still the new generation", StringComparison.Ordinal)
            .ShouldBeFalse("generation alone is not the mount-set refusal");
        // CARD-1142 repair 2: same-count unique destinations pass the count guard.
        text.Contains("whose mount set differs from the older roster", StringComparison.Ordinal)
            .ShouldBeFalse("a differing mount set is not refused on count");
        text.Contains("refuses on mount count", StringComparison.Ordinal)
            .ShouldBeFalse("mount count is one guard, not the refusal for a differing set");
    }

    // CARD-0628 G-17b.
    [Test]
    public void Ops_http_names_provider_auth()
    {
        var text = Read("docs/ops-http.md");
        text.ShouldContain("/api/session-runners/{runnerId}/provider-auth/{provider}");
        text.ShouldContain("phone_home_unavailable");
        text.ShouldContain("phone_home_unsupported_operation");
        text.ShouldContain("Worktree + Grok or Claude Code");
        text.Contains("Worktree + Grok only", StringComparison.Ordinal).ShouldBeFalse();
    }

    // CARD-0849. Slot acquisition is outside the 30s arrival window. Exit 4 is its own diagnosis.
    [Test]
    public void Fixture_gate_slot_wait_is_outside_the_arrival_window()
    {
        var text = Read("docs/docker-stack.md");
        foreach (var pin in new[]
        {
            "slot-held",
            "FixtureBuildSlotTimeout",
            "FixtureOfflineRestoreFailed",
            "FixtureRestoreBarrierFailed",
            "30-second",
            "45 minutes",
            "A pid-only SIGINT can be dropped",
            "process-group SIGINT",
            "exits 130 when the trap finishes, or 2 when the barrier records FixtureRestoreBarrierFailed",
            "TERM exits 143",
            "does not release that slot to retry"
        })
            text.ShouldContain(pin, Case.Sensitive, pin);
    }

    /// <summary>
    /// Prose wraps. Asserting a phrase against the raw bytes makes the guard fail the moment a
    /// paragraph is rewrapped, which says nothing about whether the doc still means what it must,
    /// so every assertion here reads the text with runs of whitespace collapsed to one space.
    /// </summary>
    private static string Read(string relative) =>
        System.Text.RegularExpressions.Regex.Replace(DockerStackDocuments.Read(relative), @"\s+", " ");
}
