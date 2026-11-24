// Hand a piece of work to another agent. It replaced scripts/delegate.ps1 (deleted 2026-11-24, unit 28 of
// docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md); the CLI runs it as `antiphon-scripts delegate <verb> [...]`
// (scripts/antiphon delegate on sh, scripts\antiphon.cmd delegate on Windows). The antiphon-delegate skill drives it from
// inside a running agent session; identity comes from the ANTIPHON_* environment (the CLI reads it and passes it in), never
// from arguments. This header is the reference the skills and bundles point agents at.
//
// Verbs (scripts/antiphon delegate <verb> ...; every verb also takes --capability <name>):
//   create   --goal <text> | --goal-file <p> [--role R] [--title t] [--orchestrator] [--card CARD-nnnn] [--kind K] [--level L]
//            [--model alias] [--complexity Hard|Medium|Easy] [--refuse-if-exhausted] [--stage S] [--dir d] [--on-agent id]
//            [--agent name] [--worktree | --shared | --read-only | --workspace Worktree|Shared|ReadOnly] [--runner r]
//            [--platform Any|Windows|Linux] [--start-ref ref] [--base-task id | --fresh-worktree] [--cold-workspace]
//            [--source-landing guid] [--repair-source guid] [--verification-round Final|Interim --verification-subject guid
//            --verification-baseline-outcome guid --verification-selection-file p] [--scope areas] [--expect-about M]
//            [--ceiling-minutes M --ceiling-reason r] [--authority text | --authority-file p] [--env-override NAME=v[,NAME=v]]
//            [--no-inherit-env] [--allow-direct-edits] [--no-commit] [--pin] [--ignore-routing-pin]
//            [--ignore-subscription-quota] [--allow-unauthenticated-provider] [--ignore-model-disabled]
//            [--ignore-concurrency-limit]
//   status   <task>
//   land     <task> [--expected-source-sha sha] [--review-evidence-id guid] [--verify filter]
//            [--from-task id | --recover-reviewed-source] [--ignore-known-broken-target]
//   reply    <task> <answer> | --message-file p
//   reply    <task> --ceiling-action Continue|Extend --episode-id guid [--extend-minutes M]
//            [--operator-extend --ceiling-reason r]
//   continue <task>
//   refine   <task> <message> | --message-file p [--interrupt]
//   finding  <task> --stage S (--found "what" | --clean) [--reviewed-source-sha sha] [--reviewed-source-clean true|false]
//   flag     <task> --verdict Lost|Noisy|Good [--note n]
//   reroute  <task> --kind K --level L
//   list-areas [--areas-dir d]
//   worktree-health
//   cleanup-verification <task>
//
// BIND THE CARD (CARD-0040). Pass --card CARD-nnnn, or lead --title with the identifier, and the card moves itself: In
// Progress when this task dispatches, Review when it settles Succeeded with nothing else open, within 60s, recorded on the
// card's history as `card-transitions`. Creation echoes "- bound to CARD-nnnn"; an explicit --card that names no card is
// refused 422 rather than ignored. A move a human makes afterwards is never overridden, and Review -> Done is never automated.
//
// CARD-0147 / CARD-0366 / CARD-0505: a 409 concurrency_limit names the population (open or queued), axis, occupants and cap
// (the server's answer is printed whole). Re-send with --ignore-concurrency-limit only when population is open, canOverride
// is true, and the axis is absolute with no occupant in the stage you are dispatching. A queued population, canOverride false,
// or a same-stage collision never justifies the flag. The flag is one-shot: it does not raise a durable limit, and it does
// not lift a queued population or a SeparateQueues running hold. A same-stage collision still defers. Read the scoped
// pipeline or scripts/antiphon dispatch-concurrency before raising a durable limit.
//
// CARD-1995: a land refused with target_build_broken names its overrides. Re-send with --ignore-known-broken-target (body
// ignoreKnownBrokenTarget: true) to clear the recorded broken targets and build, or set Landing:RefuseKnownBrokenTarget=false.
//
// CARD-0527: settlement commits a Shared task's own footprint unless you pass --no-commit. That is the only opt-out an
// instruction can carry; prose in --goal is an advisory, not a behaviour change. Always/Agent are API-only.
//
// LONG TEXT COMES FROM A FILE. cmd.exe (scripts\antiphon.cmd) cannot carry a line break, and it mangles ^ % & | and inner
// quotes, so a multi-line goal, answer or message goes in --goal-file, --message-file or --authority-file (read as UTF-8 and
// sent as is). The inline flags are for one-liners.
//
// Every request sends the task token (ANTIPHON_TASK_TOKEN) or a named Delegation Capability's token (CARD-0398: --capability
// or ANTIPHON_CAPABILITY names it; the DPAPI store is ANTIPHON_CAPABILITY_STORE, else %LOCALAPPDATA%\Antiphon\capabilities),
// never printed, and the registered caller label delegate.ps1 (AntiphonCaller; the server meters agent-task GET polls by it,
// AgentTaskGetPollMetrics, so the script's label stays). Operator Extend reads the owner-only operator token file
// (ANTIPHON_OPERATOR_TOKEN_FILE, else the server's default path) for that one request. The land version probe's deadline is
// ANTIPHON_VERSION_PROBE_TIMEOUT_SEC (default 5). create forwards the caller's X_LLM_PROJECT and X_LLM_KEY as inheritedLlmEnv
// unless --no-inherit-env. The CLI passes all of these and its working directory in.
//
// Exit 0 done; 1 a refused value, a missing file or a failed request (a land refusal also on stdout); 2 a local refusal the
// script also answered with 2 (ceiling_invalid, workspace_switch_conflict, the --runner, --start-ref and base-override
// shapes, operator_token_unavailable) and usage errors (NightlyUsageException: an unknown flag, a flag the verb does not read,
// a value outside its choices, a missing task). Plain C#: no PowerShell types. ASCII only.
#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Antiphon.Nightly
{
    /// <summary>What the delegate command reads from the environment; the CLI fills it, a test answers its own.</summary>
    public sealed class DelegateCliEnvironment
    {
        /// <summary>ANTIPHON_API; blank: the default.</summary>
        public string Api { get; init; } = "";

        /// <summary>ANTIPHON_TASK_TOKEN; blank: none.</summary>
        public string TaskToken { get; init; } = "";

        /// <summary>ANTIPHON_CAPABILITY: the capability name used when --capability is not given; blank: none.</summary>
        public string Capability { get; init; } = "";

        /// <summary>The capability store directory (ANTIPHON_CAPABILITY_STORE, else %LOCALAPPDATA%\Antiphon\capabilities).</summary>
        public string CapabilityStore { get; init; } = "";

        /// <summary>The operator token file (ANTIPHON_OPERATOR_TOKEN_FILE, else the server's default path).</summary>
        public string OperatorTokenFile { get; init; } = "";

        /// <summary>ANTIPHON_VERSION_PROBE_TIMEOUT_SEC as set; blank or not a positive integer: 5.</summary>
        public string ProbeTimeoutSeconds { get; init; } = "";

        /// <summary>The caller's X_LLM_PROJECT and X_LLM_KEY, each present only when set.</summary>
        public IReadOnlyDictionary<string, string> LlmEnvironment { get; init; } = new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>The directory the checkout is resolved from and relative files are read against (the CLI's working directory).</summary>
        public string WorkingDirectory { get; init; } = "";
    }

    /// <summary>The delegate command's options (delegate.ps1's parameter sets as verbs, its parameters in kebab case).</summary>
    public sealed class DelegateCliOptions
    {
        /// <summary>The CLI's usage line.</summary>
        public const string Usage = "delegate <create|status|land|reply|continue|refine|finding|flag|reroute|list-areas|worktree-health|cleanup-verification>"
            + " [<task>] [<message>] [flags: scripts/lib/Antiphon.Nightly/DelegateCli.cs lists them per verb]";

        /// <summary>The verbs, in their listed spelling.</summary>
        public static readonly IReadOnlyList<string> Verbs = new[]
        {
            "create", "status", "land", "reply", "continue", "refine", "finding", "flag", "reroute", "list-areas", "worktree-health", "cleanup-verification",
        };

        /// <summary>The roles --role takes (the server's AgentRole names a delegate may send).</summary>
        public static readonly IReadOnlyList<string> Roles = new[]
        {
            "Investigate", "Plan", "TestDesign", "Code", "Mutation", "Review", "Debug", "Coverage", "Docs", "Commit", "Test", "Deploy", "Merge", "Custom",
        };

        /// <summary>The tiers --level takes.</summary>
        public static readonly IReadOnlyList<string> Levels = new[] { "Frontier", "High", "Medium", "Low" };

        /// <summary>The agent programs --kind takes (the server's delegatable kinds).</summary>
        public static readonly IReadOnlyList<string> Kinds = new[] { "ClaudeCode", "Grok", "Codex" };

        /// <summary>The rounds --verification-round takes.</summary>
        public static readonly IReadOnlyList<string> VerificationRounds = new[] { "Final", "Interim" };

        /// <summary>The workspaces --workspace takes.</summary>
        public static readonly IReadOnlyList<string> Workspaces = new[] { "Worktree", "Shared", "ReadOnly" };

        /// <summary>The platforms --platform takes.</summary>
        public static readonly IReadOnlyList<string> Platforms = new[] { "Any", "Windows", "Linux" };

        /// <summary>The hardnesses --complexity takes.</summary>
        public static readonly IReadOnlyList<string> Complexities = new[] { "Hard", "Medium", "Easy" };

        /// <summary>The decisions --ceiling-action takes.</summary>
        public static readonly IReadOnlyList<string> CeilingActions = new[] { "Continue", "Extend" };

        /// <summary>The landing-step stages --stage takes.</summary>
        public static readonly IReadOnlyList<string> Stages = new[] { "Rebase", "Verify", "Cleanup", "Review", "FollowUp", "Deploy" };

        /// <summary>The verdicts --verdict takes.</summary>
        public static readonly IReadOnlyList<string> Verdicts = new[] { "Lost", "Noisy", "Good" };

        private static readonly string[] Valued =
        {
            "--capability", "--role", "--goal", "--goal-file", "--title", "--level", "--kind", "--model", "--card", "--source-landing", "--repair-source",
            "--start-ref", "--base-task", "--verification-round", "--verification-subject", "--verification-baseline-outcome",
            "--verification-selection-file", "--dir", "--on-agent", "--agent", "--workspace", "--runner", "--platform", "--scope", "--expect-about",
            "--ceiling-minutes", "--ceiling-reason", "--complexity", "--env-override", "--authority", "--authority-file", "--stage", "--message",
            "--message-file", "--ceiling-action", "--episode-id", "--extend-minutes", "--verify", "--expected-source-sha", "--review-evidence-id",
            "--from-task", "--reviewed-source-sha", "--reviewed-source-clean", "--found", "--verdict", "--note", "--areas-dir",
        };

        private static readonly string[] Switches =
        {
            "--orchestrator", "--fresh-worktree", "--cold-workspace", "--worktree", "--shared", "--read-only", "--allow-direct-edits",
            "--ignore-subscription-quota", "--allow-unauthenticated-provider", "--ignore-model-disabled", "--ignore-concurrency-limit", "--no-commit",
            "--pin", "--ignore-routing-pin", "--refuse-if-exhausted", "--no-inherit-env", "--interrupt", "--operator-extend", "--recover-reviewed-source",
            "--ignore-known-broken-target", "--clean",
        };

        // The flags each verb reads (--capability is every verb's).
        private static readonly Dictionary<string, string[]> Allowed = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["create"] = new[]
            {
                "--role", "--goal", "--goal-file", "--title", "--orchestrator", "--level", "--kind", "--model", "--card", "--source-landing", "--repair-source",
                "--start-ref", "--base-task", "--fresh-worktree", "--cold-workspace", "--verification-round", "--verification-subject",
                "--verification-baseline-outcome", "--verification-selection-file", "--dir", "--on-agent", "--agent", "--worktree", "--workspace", "--shared",
                "--read-only", "--runner", "--platform", "--allow-direct-edits", "--scope", "--expect-about", "--ceiling-minutes", "--ceiling-reason",
                "--ignore-subscription-quota", "--allow-unauthenticated-provider", "--ignore-model-disabled", "--ignore-concurrency-limit", "--no-commit",
                "--pin", "--ignore-routing-pin", "--complexity", "--refuse-if-exhausted", "--env-override", "--no-inherit-env", "--authority",
                "--authority-file", "--stage",
            },
            ["status"] = Array.Empty<string>(),
            ["land"] = new[] { "--verify", "--expected-source-sha", "--review-evidence-id", "--from-task", "--recover-reviewed-source", "--ignore-known-broken-target" },
            ["reply"] = new[] { "--message", "--message-file", "--ceiling-action", "--episode-id", "--extend-minutes", "--operator-extend", "--ceiling-reason" },
            ["continue"] = Array.Empty<string>(),
            ["refine"] = new[] { "--message", "--message-file", "--interrupt" },
            ["finding"] = new[] { "--stage", "--found", "--clean", "--reviewed-source-sha", "--reviewed-source-clean" },
            ["flag"] = new[] { "--verdict", "--note" },
            ["reroute"] = new[] { "--kind", "--level" },
            ["list-areas"] = new[] { "--areas-dir" },
            ["worktree-health"] = Array.Empty<string>(),
            ["cleanup-verification"] = Array.Empty<string>(),
        };

        // The flags whose value is one of a set (delegate.ps1's ValidateSet parameters).
        private static readonly Dictionary<string, IReadOnlyList<string>> Choices = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            ["--role"] = Roles,
            ["--level"] = Levels,
            ["--kind"] = Kinds,
            ["--verification-round"] = VerificationRounds,
            ["--workspace"] = Workspaces,
            ["--platform"] = Platforms,
            ["--complexity"] = Complexities,
            ["--ceiling-action"] = CeilingActions,
            ["--stage"] = Stages,
            ["--verdict"] = Verdicts,
        };

        // The verbs that take the task after the verb, and those that also take a message after it.
        private static readonly string[] TaskVerbs = { "status", "land", "reply", "continue", "refine", "finding", "flag", "reroute", "cleanup-verification" };
        private static readonly string[] MessageVerbs = { "reply", "refine" };

        private Dictionary<string, string> _values = new Dictionary<string, string>(StringComparer.Ordinal);
        private HashSet<string> _switches = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>The verb, in its listed spelling.</summary>
        public string Verb { get; init; } = "create";

        /// <summary>The task the verb acts on (a guid or a short id), as given; empty for create, list-areas and worktree-health.</summary>
        public string Task { get; init; } = "";

        /// <summary>reply/refine: the inline message (the positional after the task, or --message); null when neither was given.</summary>
        public string Message { get; init; }

        /// <summary>The environment the CLI read.</summary>
        public DelegateCliEnvironment Environment { get; init; } = new DelegateCliEnvironment();

        /// <summary>The API base (ANTIPHON_API), without a trailing slash.</summary>
        public string Api => OperatorApi.Base(Environment.Api);

        /// <summary>The version probe's deadline: ANTIPHON_VERSION_PROBE_TIMEOUT_SEC when it is a positive integer, else 5 seconds.</summary>
        public TimeSpan ProbeTimeout =>
            int.TryParse(Environment.ProbeTimeoutSeconds ?? "", NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds) && seconds > 0
                ? TimeSpan.FromSeconds(seconds)
                : TimeSpan.FromSeconds(5);

        /// <summary>True when the flag (valued or switch) was given.</summary>
        public bool Has(string flag) => _values.ContainsKey(flag) || _switches.Contains(flag);

        /// <summary>A valued flag's value exactly as given (possibly empty), or null when it was not given.</summary>
        public string Value(string flag) => _values.TryGetValue(flag, out var value) ? value : null;

        /// <summary>A valued flag's value, or empty when it was not given.</summary>
        public string Text(string flag) => Value(flag) ?? "";

        /// <summary>True when the switch was given.</summary>
        public bool Switch(string flag) => _switches.Contains(flag);

        /// <summary>
        /// The verb, the task (and for reply/refine the message) and the flags; the environment comes from the CLI. An unknown
        /// verb or flag, a flag the verb does not read, a missing task, a value outside its choices, a malformed GUID, integer
        /// or boolean, or reroute/finding/flag without their required flag is a NightlyUsageException. Everything the script
        /// refused by name (with its own exit code) is refused by Run, before any request.
        /// </summary>
        public static DelegateCliOptions Parse(IReadOnlyList<string> arguments, DelegateCliEnvironment environment)
        {
            var verb = BoardCli.Verb(arguments, Verbs, null, out var rest);

            // Positionals may sit anywhere among the flags (PowerShell bound them by position): a valued flag takes the next
            // argument, a switch none, and anything else that is not a flag is positional.
            var valued = new HashSet<string>(Valued, StringComparer.Ordinal);
            var flagArguments = new List<string>();
            var positionals = new List<string>();
            for (var i = 0; i < rest.Count; i++)
            {
                var argument = rest[i];
                if (argument.StartsWith("--", StringComparison.Ordinal))
                {
                    flagArguments.Add(argument);
                    if (valued.Contains(argument) && i + 1 < rest.Count) flagArguments.Add(rest[++i]);
                    continue;
                }
                positionals.Add(argument);
            }
            var flags = NightlyArgs.Parse(flagArguments, Valued, Switches);
            BoardCli.Only(flags, verb, Valued.Concat(Switches), Allowed[verb].Concat(new[] { "--capability" }));

            var task = "";
            string message = null;
            var expected = TaskVerbs.Contains(verb) ? (MessageVerbs.Contains(verb) ? 2 : 1) : 0;
            if (positionals.Count > expected)
                throw new NightlyUsageException(expected == 0
                    ? verb + " takes no positional argument, not '" + positionals[0] + "'"
                    : "unexpected argument '" + positionals[expected] + "' (quote a message that has spaces)");
            if (TaskVerbs.Contains(verb))
            {
                if (positionals.Count == 0 || string.IsNullOrWhiteSpace(positionals[0]))
                    throw new NightlyUsageException("which task? Pass it after the verb: scripts/antiphon delegate " + verb + " <task> ...");
                task = positionals[0];
                if (positionals.Count > 1) message = positionals[1];
            }
            if (MessageVerbs.Contains(verb) && flags.Has("--message"))
            {
                if (message != null) throw new NightlyUsageException("pass the message after the task or as --message, not both");
                message = flags.Value("--message");
            }

            foreach (var flag in new[] { "--source-landing", "--repair-source" })
                if (flags.Has(flag) && !Guid.TryParse(flags.Value(flag), out _)) throw new NightlyUsageException(flag + " must be a GUID, not '" + flags.Value(flag) + "'");
            if (flags.Has("--expect-about"))
            {
                var minutes = flags.Int("--expect-about", 0);
                if (minutes < 1 || minutes > 1440) throw new NightlyUsageException("--expect-about must be from 1 to 1440 minutes, not " + minutes.ToString(CultureInfo.InvariantCulture));
            }
            if (flags.Has("--reviewed-source-clean") && !bool.TryParse(flags.Value("--reviewed-source-clean"), out _))
                throw new NightlyUsageException("--reviewed-source-clean must be true or false, not '" + flags.Value("--reviewed-source-clean") + "'");
            foreach (var entry in flags.List("--env-override"))
            {
                var equals = entry.IndexOf('=');
                if (equals <= 0) throw new NightlyUsageException("--env-override takes NAME=value[,NAME=value...], not '" + entry + "'");
            }
            if (verb == "reroute" && (!flags.Has("--kind") || !flags.Has("--level")))
                throw new NightlyUsageException("reroute requires --kind and --level (the explicit pair a human chose).");
            if (verb == "finding" && !flags.Has("--stage")) throw new NightlyUsageException("finding requires --stage (" + string.Join(", ", Stages) + ")");
            if (verb == "flag" && !flags.Has("--verdict")) throw new NightlyUsageException("flag requires --verdict (" + string.Join(", ", Verdicts) + ")");

            var options = new DelegateCliOptions { Verb = verb, Task = task, Message = message, Environment = environment ?? new DelegateCliEnvironment() };
            foreach (var flag in Valued)
            {
                if (!flags.Has(flag)) continue;
                var value = flags.Value(flag);
                // A ValidateSet value is sent in its listed spelling; an empty one is outside the set too.
                if (Choices.TryGetValue(flag, out var choices))
                    value = BoardCli.Choice(flag, value, choices)
                        ?? throw new NightlyUsageException(flag + " must be one of " + string.Join(", ", choices) + ", not ''");
                options._values[flag] = value;
            }
            foreach (var flag in Switches)
                if (flags.Switch(flag)) options._switches.Add(flag);
            return options;
        }

        /// <summary>--env-override's pairs, in the order given (a name given twice keeps its last value).</summary>
        public IReadOnlyList<KeyValuePair<string, string>> EnvOverride()
        {
            var value = Value("--env-override");
            if (string.IsNullOrEmpty(value)) return Array.Empty<KeyValuePair<string, string>>();
            return value.Split(',').Select(entry => entry.Trim()).Where(entry => entry.Length > 0)
                .Select(entry => new KeyValuePair<string, string>(entry.Substring(0, entry.IndexOf('=')), entry.Substring(entry.IndexOf('=') + 1)))
                .ToList();
        }
    }

    /// <summary>The delegation command (delegate.ps1).</summary>
    public static class DelegateCli
    {
        /// <summary>The registered caller label every request sends (unchanged from the script: the server's metric key).</summary>
        public const string CallerLabel = "delegate.ps1";

        /// <summary>
        /// The status line for Pending runner-sync evidence without a debt row (CARD-1082, CARD-1802, CARD-1805); the docs that
        /// explain Pending evidence say the same (RunnerBranchContractDocumentationTests).
        /// </summary>
        public const string PendingWithoutDebt = "Pending evidence without a debt row: the progress read blocked this task; repair what the handoff names, then reply. Lease contention alone never leaves Pending evidence of a correlated complete report without a debt row (CARD-1802). A bind-refusal recovery has no correlated report, so its handoff asks for one (CARD-1805).";

        /// <summary>Each request's deadline (Invoke-RestMethod had none; a create that outlives this is reported as not completed).</summary>
        public static readonly TimeSpan Deadline = TimeSpan.FromMinutes(10);

        /// <summary>
        /// Runs the verb: the answer on host.Out; exit 0, or 1 or 2 after the refusal or the request's failure on host.Error (a
        /// land refusal also on host.Out). git (the checkout and the bind diagnosis) runs on the host's seam, files are read
        /// through files, every request goes through http (production: NightlyReport.HttpTransport, Deadline) and the land
        /// version probe through probe (production: the same with options.ProbeTimeout). unprotect opens a DPAPI capability
        /// (CapabilityCli.UnprotectCurrentUser on Windows; null elsewhere, where a capability cannot be read).
        /// </summary>
        public static int Run(DelegateCliOptions options, NightlyHost host, IFileSystem files, NightlyReportHttp http, NightlyReportHttp probe,
            Func<byte[], byte[]> unprotect)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (host == null) throw new ArgumentNullException(nameof(host));
            if (files == null) throw new ArgumentNullException(nameof(files));
            if (http == null) throw new ArgumentNullException(nameof(http));
            if (probe == null) throw new ArgumentNullException(nameof(probe));
            try
            {
                return new DelegateRun(options, host, files, http, probe, unprotect).Execute();
            }
            catch (DelegateRefusal refusal)
            {
                if (refusal.AlsoOnOut) host.Out.WriteLine(refusal.Message);
                host.Error.WriteLine(refusal.Message);
                return refusal.Exit;
            }
        }

        // A refused value or a failed request: its line goes to stderr (a land failure to stdout too) and the run exits Exit.
        private sealed class DelegateRefusal : Exception
        {
            public DelegateRefusal(string message, int exit = 1, bool alsoOnOut = false) : base(message)
            {
                Exit = exit;
                AlsoOnOut = alsoOnOut;
            }

            public int Exit { get; }

            public bool AlsoOnOut { get; }
        }

        private sealed class DelegateRun
        {
            private static readonly Regex BindFailure = new Regex(@"could not be converted to [\w.]+\. Path: \$\.(?<field>\w+)", RegexOptions.CultureInvariant);
            private static readonly Regex CardInTitle = new Regex(@"\bCARD-[0-9]+\b", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
            private static readonly Regex FullGuid = new Regex("^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$", RegexOptions.CultureInvariant);
            private static readonly Regex LandSha = new Regex("^(?:[0-9a-fA-F]{40}|[0-9a-fA-F]{64})$", RegexOptions.CultureInvariant);
            private static readonly Regex IsoInstant = new Regex(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}", RegexOptions.CultureInvariant);

            private readonly DelegateCliOptions _o;
            private readonly NightlyHost _host;
            private readonly IFileSystem _files;
            private readonly NightlyReportHttp _http;
            private readonly NightlyReportHttp _probe;
            private readonly Func<byte[], byte[]> _unprotect;
            private readonly Dictionary<string, string> _headers = new Dictionary<string, string>(StringComparer.Ordinal);
            private string _checkout;

            public DelegateRun(DelegateCliOptions options, NightlyHost host, IFileSystem files, NightlyReportHttp http, NightlyReportHttp probe,
                Func<byte[], byte[]> unprotect)
            {
                _o = options;
                _host = host;
                _files = files;
                _http = http;
                _probe = probe;
                _unprotect = unprotect;
            }

            private TextWriter Out => _host.Out;

            public int Execute()
            {
                Credentials();
                _headers[AntiphonCaller.Header] = AntiphonCaller.Label(CallerLabel);
                switch (_o.Verb)
                {
                    case "worktree-health":
                        return WorktreeHealth();
                    case "list-areas":
                        return ListAreas();
                    case "cleanup-verification":
                        Out.WriteLine(OperatorApi.Pretty(Request("POST", "/api/agent-tasks/" + _o.Task + "/cleanup-verification", new JsonObject())));
                        return 0;
                    case "status":
                        return Status();
                    case "land":
                        return Land();
                    case "reroute":
                        return Reroute();
                    case "reply":
                        return Reply();
                    case "continue":
                        Request("POST", "/api/agent-tasks/" + _o.Task + "/continue", new JsonObject { ["origin"] = "Cli" });
                        Out.WriteLine("Continued task " + _o.Task + " with its standing authority. It will resume and report back.");
                        return 0;
                    case "refine":
                        return Refine();
                    case "finding":
                        return Finding();
                    case "flag":
                        return Flag();
                    default:
                        return Create();
                }
            }

            // The task token, or a named capability's token from the DPAPI store (never both, never printed).
            private void Credentials()
            {
                var env = _o.Environment;
                var name = _o.Has("--capability") && !string.IsNullOrWhiteSpace(_o.Text("--capability")) ? _o.Text("--capability") : env.Capability;
                var hasTaskToken = !string.IsNullOrWhiteSpace(env.TaskToken);
                var hasCapability = !string.IsNullOrWhiteSpace(name);
                if (hasTaskToken && hasCapability) throw new DelegateRefusal("Pass --capability / ANTIPHON_CAPABILITY or ANTIPHON_TASK_TOKEN, not both.");
                if (hasCapability)
                {
                    var store = env.CapabilityStore ?? "";
                    var path = _files.Path.Combine(store, name + ".dpapi");
                    if (!_files.File.Exists(path))
                        throw new DelegateRefusal("capability '" + name + "' is not installed under " + store + "; ask the operator to run scripts\\antiphon.cmd capability issue");
                    string token;
                    try
                    {
                        if (_unprotect == null) throw new PlatformNotSupportedException("DPAPI is Windows only.");
                        token = Encoding.UTF8.GetString(_unprotect(_files.File.ReadAllBytes(path)));
                    }
                    catch (Exception)
                    {
                        throw new DelegateRefusal("capability '" + name + "' could not be read from " + store);
                    }
                    _headers[OperatorApi.TaskTokenHeader] = token;
                }
                else if (hasTaskToken)
                {
                    _headers[OperatorApi.TaskTokenHeader] = env.TaskToken;
                }
            }

            // ---- verbs ----

            private int WorktreeHealth()
            {
                var report = Request("POST", "/api/agent-tasks/worktree-health", new JsonObject());
                var count = 0;
                var findingCount = BoardCli.Get(report, "findingCount");
                if (!IsNull(findingCount)) count = Int(findingCount);
                else if (BoardCli.Truthy(BoardCli.Get(report, "findings"))) count = BoardCli.Items(BoardCli.Get(report, "findings")).Count;
                if (count == 0)
                {
                    Out.WriteLine("No stuck delegate worktrees.");
                    return 0;
                }
                Out.WriteLine(Number(count) + " stuck delegate worktree finding(s) (detection only; nothing pruned):");
                foreach (var row in BoardCli.Items(BoardCli.Get(report, "findings")))
                {
                    var severity = Truthy(row, "severity") ? T(row, "severity") : "Error";
                    var shortId = Truthy(row, "shortId") ? T(row, "shortId") : "-";
                    Out.WriteLine("  [" + severity + "] " + T(row, "branch") + "  task " + shortId + "  " + T(row, "detail"));
                }
                return 0;
            }

            private int ListAreas()
            {
                var directory = !string.IsNullOrEmpty(_o.Text("--areas-dir")) ? _o.Text("--areas-dir") : Checkout();
                var map = Request("GET", "/api/agent-tasks/areas?directory=" + Uri.EscapeDataString(directory), null);
                var areas = BoardCli.Items(BoardCli.Get(map, "areas"));
                if (areas.Count == 0)
                {
                    Out.WriteLine("No areas declared for " + T(map, "repoPath") + " - every --scope token is read as a path or a label.");
                    return 0;
                }
                Out.WriteLine("Areas in " + T(map, "sourcePath") + ":");
                foreach (var area in areas)
                {
                    var weight = Same(T(area, "weight"), "allow") ? "  [allow]" : "";
                    Out.WriteLine("  " + T(area, "name") + weight);
                    foreach (var path in BoardCli.Items(BoardCli.Get(area, "paths"))) Out.WriteLine("      " + BoardCli.Text(path));
                }
                Out.WriteLine("");
                Out.WriteLine("An area is added when two tasks collide in it, named for the work, not the folder.");
                return 0;
            }

            private int Status()
            {
                var task = Request("GET", "/api/agent-tasks/" + _o.Task, null);
                var s = BoardCli.Get(task, "summary");
                Out.WriteLine(T(s, "status") + "  " + T(s, "title") + "  " + T(s, "kind") + "/" + T(s, "role") + "  " + T(s, "modelLevel"));
                if (Truthy(s, "requiredPlatform"))
                {
                    var placed = Truthy(s, "runnerId") ? T(s, "runnerId") : "desktop";
                    var observed = Truthy(s, "observedPlatform") ? T(s, "observedPlatform") : "platform unknown";
                    Out.WriteLine("platform: " + T(s, "requiredPlatform") + " on " + placed + "; observed " + observed);
                }
                Out.WriteLine("Delegate: " + T(s, "status"));

                // CARD-1610 S4a D-7: ceiling, hold and seat lines, each only from its server field.
                var taskRef = Truthy(s, "id") ? T(s, "id") : _o.Task;
                var budget = BoardCli.Get(task, "ceilingBudget");
                if (BoardCli.Truthy(budget))
                {
                    if (!IsNull(BoardCli.Get(budget, "resolvedMinutes")))
                        Out.WriteLine("Ceiling: " + T(budget, "resolvedMinutes") + " min (" + T(budget, "resolvedSource") + "); next bound " + T(budget, "nextCeilingMinutes")
                            + " min; cap " + T(budget, "absoluteCapMinutes"));
                    else if (!IsNull(BoardCli.Get(budget, "requestedMinutes")))
                        Out.WriteLine("Ceiling: " + T(budget, "requestedMinutes") + " min requested; resolved at dispatch");
                    else
                        Out.WriteLine("Ceiling: role default; resolved at dispatch");
                    if (Truthy(budget, "requestedReason")) Out.WriteLine("Ceiling reason: " + T(budget, "requestedReason"));
                }
                var hold = BoardCli.Get(task, "ceilingHold");
                if (BoardCli.Truthy(hold))
                {
                    var lastKind = Truthy(hold, "lastTranscriptKind") ? T(hold, "lastTranscriptKind") : "unknown";
                    var lastAge = !IsNull(BoardCli.Get(hold, "lastTranscriptAgeSeconds")) ? Number(Int(BoardCli.Get(hold, "lastTranscriptAgeSeconds"))) + "s" : "age unknown";
                    Out.WriteLine("Ceiling hold: episode " + T(hold, "episodeId") + "; held " + Instant(BoardCli.Get(hold, "heldAt")) + "; bound " + T(hold, "nextCeilingMinutes")
                        + " min; last transcript " + lastKind + " " + lastAge);
                    if (Truthy(hold, "phaseKind"))
                        Out.WriteLine("Phase: " + T(hold, "phaseKind") + " " + T(hold, "phaseLimitMinutes") + "-minute limit, deadline " + Instant(BoardCli.Get(hold, "phaseDeadlineAt")));
                    else
                        Out.WriteLine("Phase: none - no phase deadline covers this tail; the ceiling is its only deadline");
                    if (IsTrue(BoardCli.Get(hold, "capReached")))
                        Out.WriteLine("Final hold at 720 minutes: Continue and Extend refuse. Operator Extend (--operator-extend --extend-minutes 1..240 --ceiling-reason \"...\") or POST /api/agent-tasks/"
                            + taskRef + "/cancel");
                    else
                        Out.WriteLine("Decide: scripts/antiphon delegate reply " + taskRef + " --ceiling-action Continue|Extend --episode-id " + T(hold, "episodeId") + " [--extend-minutes M]");
                }
                var failed = BoardCli.Get(task, "failedSession");
                if (BoardCli.Truthy(failed))
                {
                    var notice = Truthy(failed, "noticeState") ? T(failed, "noticeState") : "none";
                    Out.WriteLine("Seat: " + T(failed, "summary") + "; activity " + T(failed, "activity") + "; session " + T(failed, "sessionId") + "; failure source "
                        + T(failed, "failureSource") + "; notice " + notice);
                    if (Same(T(failed, "summary"), "Failed, session still Running: seat held"))
                        Out.WriteLine("Retry stops this session and spends a new attempt; Cancel does not apply to a Failed task.");
                }
                var evidence = BoardCli.Get(task, "reviewEvidence");
                if (BoardCli.Truthy(evidence))
                    Out.WriteLine("Review evidence: " + T(evidence, "id") + "; subject " + T(evidence, "subjectTaskId") + "; reviewed " + T(evidence, "reviewedSourceSha") + "; clean "
                        + T(evidence, "reviewedSourceClean") + "; ref " + T(evidence, "reviewedSourceRef") + "; outcome " + T(evidence, "outcome"));
                if (Truthy(task, "sourceLandingOperationId"))
                {
                    Out.WriteLine("Verification source: " + T(task, "sourceLandingOperationId") + "; commit " + T(task, "sourceLandingSha"));
                    if (Truthy(task, "verificationCleanupResidue")) Out.WriteLine("Verification residue: " + T(task, "verificationCleanupResidue"));
                    if (Truthy(task, "verificationCleanupSealId"))
                        Out.WriteLine("Verification seal: " + T(task, "verificationCleanupSealId") + "; revision " + T(task, "verificationExecutionRevision") + "; removed dir="
                            + T(task, "verificationDirectoryRemoved") + " registration=" + T(task, "verificationRegistrationRemoved") + " branch=" + T(task, "verificationBranchRemoved"));
                    foreach (var execution in BoardCli.Items(BoardCli.Get(task, "verificationExecutions")))
                    {
                        var receipt = Truthy(execution, "hasReceipt") ? T(execution, "receiptDigest") : "none";
                        Out.WriteLine("Execution " + T(execution, "executionId") + ": session " + T(execution, "sessionId") + "; " + T(execution, "custodyReason") + "; receipt " + receipt);
                    }
                }
                if (Truthy(task, "legacyLandReceipt"))
                    Out.WriteLine("Legacy receipt: " + T(task, "legacyLandReceipt", "state") + "; event " + T(task, "legacyLandReceipt", "eventId"));
                var landing = BoardCli.Get(task, "landing");
                var r = BoardCli.Get(task, "landRequest");
                if (BoardCli.Truthy(r)) LandRequestLines(r, landing);
                else Out.WriteLine("Land: Not requested (legacy receipt evidence, if any, is unverified).");
                if (BoardCli.Truthy(landing))
                {
                    var l = landing;
                    Out.WriteLine("Publication: " + T(l, "publication") + "; operation " + T(l, "operationId") + "; approved " + T(l, "reviewedSha") + "; verified " + T(l, "verifiedSha")
                        + "; remote " + T(l, "remoteSha") + "; confirmed " + T(l, "remoteConfirmedAt") + "; cleanup: " + T(l, "cleanup"));
                    if (Truthy(l, "recoveryMode") && !Same(T(l, "recoveryMode"), "None"))
                    {
                        Out.WriteLine("Recovery receipt: " + T(l, "recoveryMode") + "; source task " + T(l, "recoverySourceTaskId") + "; ref " + T(l, "recoverySourceFullRef") + "; reviewed "
                            + T(l, "reviewedSha") + "; owner remote " + T(l, "recoveryOwnerRemoteBeforeSha") + " -> " + T(l, "recoveryOwnerRemoteAfterSha"));
                        if (IsTrue(BoardCli.Get(l, "recoveryPatchesContained"))) Out.WriteLine("Owner patches contained");
                        if (Truthy(l, "recoveryUncontainedPatches")) Out.WriteLine("Uncontained owner patches: " + T(l, "recoveryUncontainedPatches"));
                    }
                    if (Truthy(l, "reason")) Out.WriteLine("Landing reason: " + T(l, "reason"));
                }
                else Out.WriteLine("Publication: Unconfirmed; cleanup: NotStarted");
                var progress = BoardCli.Get(task, "progressEvidence");
                if (BoardCli.Truthy(progress))
                {
                    var sync = BoardCli.Get(progress, "remoteSync");
                    if (BoardCli.Truthy(sync))
                    {
                        if (Same(T(sync, "state"), "Pending")) Out.WriteLine("Runner sync: Pending " + T(sync, "reason") + "; origin=" + T(sync, "observedSha"));
                        else
                            Out.WriteLine("Runner sync: " + T(sync, "state") + " " + T(sync, "reason") + "; mirror=" + T(sync, "mirrorSha") + " " + T(sync, "mirrorRelation") + " pushed="
                                + T(sync, "mirrorPushed") + " dirty=" + T(sync, "mirrorDirty"));
                    }
                    JsonNode origin = null;
                    if (Truthy(progress, "sources")) origin = BoardCli.Items(BoardCli.Get(progress, "sources")).FirstOrDefault();
                    if (Same(T(progress, "assessment"), "Indeterminate") || Truthy(progress, "reason"))
                        Out.WriteLine("Progress: unavailable; reason " + T(progress, "reason"));
                    else if (BoardCli.Truthy(origin) && (Same(T(origin, "origin"), "RepairSource") || Same(T(origin, "origin"), "RepairSourceRemote")))
                        Out.WriteLine("Progress: repair-source; owner " + T(origin, "ownerTaskId") + "; commit " + T(origin, "commit"));
                    else if (BoardCli.Truthy(origin))
                        Out.WriteLine("Progress: " + T(origin, "origin") + "; commit " + T(origin, "commit"));
                }
                var debt = BoardCli.Get(task, "syncDebt");
                if (BoardCli.Truthy(debt))
                    Out.WriteLine("Desktop sync debt: " + T(debt, "state") + " source=" + T(debt, "sourceSha") + " confirmed=" + T(debt, "confirmedSha") + " reason=" + T(debt, "reasonCode")
                        + " attempts=" + T(debt, "attempts"));
                else if (BoardCli.Truthy(progress) && BoardCli.Truthy(BoardCli.Get(progress, "remoteSync")) && Same(T(progress, "remoteSync", "state"), "Pending"))
                    Out.WriteLine(PendingWithoutDebt);
                var session = BoardCli.Get(task, "session");
                if (BoardCli.Truthy(session))
                {
                    if (Truthy(session, "endedAt")) Out.WriteLine("Session: " + T(session, "sessionId") + " ended " + T(session, "endedAt"));
                    else
                    {
                        var activity = Truthy(session, "working") ? "working" : "idle";
                        var lastTranscript = Truthy(session, "lastTranscriptAt") ? T(session, "lastTranscriptAt") : "none";
                        Out.WriteLine("Session: " + T(session, "sessionId") + " " + T(session, "status") + ", " + activity + "; last transcript " + lastTranscript);
                    }
                }
                if (Truthy(task, "result"))
                {
                    Out.WriteLine("");
                    Out.WriteLine(T(task, "result"));
                }
                else if (Truthy(task, "failureReason"))
                {
                    Out.WriteLine("");
                    Out.WriteLine("failed: " + T(task, "failureReason"));
                }
                return 0;
            }

            private void LandRequestLines(JsonNode r, JsonNode landing)
            {
                var phase = Truthy(r, "statusPhase") ? "; phase " + T(r, "statusPhase") + " since " + Instant(BoardCli.Get(r, "statusPhaseStartedAt")) : "";
                if (Same(T(r, "state"), "Completed"))
                {
                    var outcome = "completed";
                    if (Truthy(r, "terminalFailureCode")) outcome = T(r, "terminalFailureCode");
                    else if (BoardCli.Truthy(landing) && Truthy(landing, "reason")) outcome = T(landing, "reason");
                    var completedAt = Truthy(r, "lastEvaluatedAt") ? T(r, "lastEvaluatedAt") : T(r, "lastProgressAt");
                    Out.WriteLine("Land: Completed; request " + T(r, "id") + "; requested " + T(r, "requestedAt") + "; attempt " + T(r, "attempt") + "; completed at " + completedAt
                        + "; outcome " + outcome + phase);
                }
                else
                {
                    Out.WriteLine("Land: " + T(r, "state") + "; request " + T(r, "id") + "; requested " + T(r, "requestedAt") + "; attempt " + T(r, "attempt") + "; no progress for "
                        + Number(Int(BoardCli.Get(r, "noProgressSeconds"))) + "s" + phase);
                }
                if (Truthy(r, "verificationFailureStep"))
                {
                    Out.WriteLine("Land verification: " + T(r, "verificationFailureStep"));
                    if (Truthy(r, "verificationFailureOutput")) Out.WriteLine(T(r, "verificationFailureOutput"));
                }
                if (Truthy(r, "expectedSourceSha")) Out.WriteLine("Approved original: " + T(r, "expectedSourceSha"));
                else Out.WriteLine("Approved original: (legacy; not bound)");
                if (Truthy(r, "recoveryMode") && !Same(T(r, "recoveryMode"), "None"))
                {
                    Out.WriteLine("Recovery: " + T(r, "recoveryMode") + "; source task " + T(r, "recoverySourceTaskId") + "; ref " + T(r, "recoverySourceFullRef") + "; reviewed "
                        + T(r, "expectedSourceSha") + "; owner status " + T(r, "recoveryOwnerStatus"));
                    if (Truthy(r, "supersedesRequestId")) Out.WriteLine("Superseded land request: " + T(r, "supersedesRequestId"));
                    if (Truthy(r, "recoveryOwnerRemoteAfterSha")) Out.WriteLine("Recovery source now: " + T(r, "recoveryOwnerRemoteAfterSha"));
                    if (IsTrue(BoardCli.Get(r, "recoveryPatchesContained"))) Out.WriteLine("Owner patches contained");
                    if (Truthy(r, "recoveryUncontainedPatches")) Out.WriteLine("Uncontained owner patches: " + T(r, "recoveryUncontainedPatches"));
                }
                if (Truthy(r, "localBeforeSha")) Out.WriteLine("Local before resolution: " + T(r, "localBeforeSha"));
                if (Truthy(r, "remoteSourceSha")) Out.WriteLine("Observed remote source: " + T(r, "remoteSourceSha"));
                if (Truthy(r, "resolvedSourceSha")) Out.WriteLine("Resolved source: " + T(r, "resolvedSourceSha"));
                if (Truthy(r, "candidateSourceSha")) Out.WriteLine("Candidate source: " + T(r, "candidateSourceSha"));
                if (Truthy(r, "sourceRefusalReason")) Out.WriteLine("Source refusal: " + T(r, "sourceRefusalReason"));
                if (Truthy(r, "terminalFailureCode"))
                    Out.WriteLine("Land execution failure: " + T(r, "terminalFailureCode") + "; diagnostic " + T(r, "failureDiagnosticId") + "; exception " + T(r, "failureExceptionType"));
                if (!IsNull(BoardCli.Get(r, "sourceDiagnosticCommand")) || !IsNull(BoardCli.Get(r, "sourceDiagnosticExitCode")) || !IsNull(BoardCli.Get(r, "sourceDiagnosticCode")))
                    Out.WriteLine("Source inspection: " + T(r, "sourceDiagnosticCommand") + "; exit " + T(r, "sourceDiagnosticExitCode") + "; diagnostic " + T(r, "sourceDiagnosticCode"));
                if (Truthy(r, "sourceDiagnosticExceptionType")) Out.WriteLine("Source inspection exception: " + T(r, "sourceDiagnosticExceptionType"));
                if (Truthy(r, "holdReasonCode"))
                    Out.WriteLine("Reason: " + T(r, "holdReasonCode") + "; holder " + T(r, "holdingTaskId") + " (" + T(r, "holdingTaskStatus") + "); " + T(r, "holdDetail"));
                if (Truthy(r, "reconciliationError")) Out.WriteLine("Reconciliation: " + T(r, "reconciliationError"));
                foreach (var n in BoardCli.Items(BoardCli.Get(r, "notifications")))
                    Out.WriteLine("Notification: " + T(n, "kind") + " " + T(n, "state") + "; destination " + T(n, "destinationSessionId") + "; queue " + T(n, "queueMessageId") + "; receipt "
                        + T(n, "confirmedAt") + "; error " + T(n, "lastErrorCode"));
            }

            private int Land()
            {
                var fromTask = _o.Text("--from-task");
                var recover = _o.Switch("--recover-reviewed-source");
                var expected = _o.Text("--expected-source-sha");
                var reviewEvidence = _o.Text("--review-evidence-id");
                if (fromTask.Length > 0 && recover) throw LandFailure("Choose --from-task or --recover-reviewed-source, not both.");
                if (fromTask.Length > 0 || recover)
                {
                    if (!IsLandSha(expected)) throw LandFailure("Reviewed recovery requires --expected-source-sha with a full 40- or 64-character SHA.");
                    if (!Guid.TryParse(reviewEvidence, out var review) || review == Guid.Empty) throw LandFailure("Reviewed recovery requires --review-evidence-id with a full GUID.");
                }
                string resolvedFromTask = null;
                if (fromTask.Length > 0)
                {
                    NightlyReportHttpResponse lookup;
                    try
                    {
                        lookup = _http("GET", _o.Api + "/api/agent-tasks/" + fromTask, _headers, null);
                    }
                    catch (Exception e)
                    {
                        throw LandFailure("The --from-task lookup failed: " + Message(e));
                    }
                    if (!OperatorApi.Succeeded(lookup)) throw LandFailure("The --from-task lookup failed: " + StatusText(lookup.Status));
                    resolvedFromTask = T(Parse(lookup.Body), "summary", "id");
                    if (!Guid.TryParse(resolvedFromTask, out var source) || source == Guid.Empty) throw LandFailure("The --from-task lookup returned no full task GUID.");
                }
                var safeApi = SafeApiBase(_o.Api);
                var version = VersionProbe();
                var observed = "unavailable";
                var hasMarker = false;
                if (version.Ok && !string.IsNullOrWhiteSpace(version.Body))
                {
                    var parsed = Parse(version.Body);
                    if (parsed is JsonObject)
                    {
                        var text = BoardCli.Get(parsed, "version");
                        if (!IsNull(text) && BoardCli.Text(text) != "") observed = BoardCli.Text(text);
                        hasMarker = BoardCli.Items(BoardCli.Get(parsed, "capabilities")).Any(cap => BoardCli.Text(cap) == "land-v2");
                    }
                }
                if (!hasMarker || !IsLandSha(observed))
                    throw LandFailure("Antiphon land refused: " + safeApi + " does not advertise land-v2 (observed SHA " + observed + "). GET " + safeApi
                        + "/api/version and run scripts/antiphon restart-apphost after updating the canonical checkout if needed.");

                var body = new JsonObject();
                if (_o.Text("--verify").Length > 0) body["verify"] = _o.Text("--verify");
                if (expected.Length > 0) body["expectedSourceSha"] = expected;
                if (reviewEvidence.Length > 0) body["reviewEvidenceId"] = reviewEvidence;
                if (!string.IsNullOrEmpty(resolvedFromTask)) body["adoptFromTaskId"] = resolvedFromTask;
                if (recover) body["recoverReviewedSource"] = true;
                if (_o.Switch("--ignore-known-broken-target")) body["ignoreKnownBrokenTarget"] = true;
                var landPath = "/api/agent-tasks/" + _o.Task + "/land/v2";
                NightlyReportHttpResponse answer;
                try
                {
                    answer = _http("POST", _o.Api + landPath, _headers, OperatorApi.Json(body));
                }
                catch (Exception)
                {
                    throw LandFailure("Antiphon land POST to " + safeApi + landPath + " did not complete. The outcome is uncertain: the request may have been accepted; check with"
                        + " scripts/antiphon delegate status " + _o.Task + ".");
                }
                if (!OperatorApi.Succeeded(answer))
                {
                    if (answer.Status == 404 || answer.Status == 405)
                        throw LandFailure("Antiphon land refused: POST " + safeApi + landPath + " returned " + Number(answer.Status) + " (land-v2). Confirm GET " + safeApi
                            + "/api/version, then scripts/antiphon restart-apphost.");
                    throw LandFailure("Antiphon POST " + landPath + " failed: " + FailureDetail(answer));
                }
                // CARD-1657: a reply without a land request id is not proof the land was queued.
                var result = Parse(answer.Body);
                if (!Guid.TryParse(T(result, "requestId"), out var requestId) || requestId == Guid.Empty)
                    throw LandFailure("Antiphon land POST to " + safeApi + landPath + " returned no land request id. The outcome is uncertain: the request may have been accepted; check with"
                        + " scripts/antiphon delegate status " + _o.Task + ".");
                var word = Same(T(result, "status"), "requeued") ? "Requeued land" : "Queued land";
                if (Same(T(result, "notification"), "not-required"))
                    Out.WriteLine(word + " request " + T(result, "requestId") + ". Publication pending; notification=not-required.");
                else
                    Out.WriteLine(word + " request " + T(result, "requestId") + ". Publication pending; status/hold and outcome notifications are tracked.");
                return 0;
            }

            private int Reroute()
            {
                Request("POST", "/api/agent-tasks/" + _o.Task + "/reroute", new JsonObject { ["agentKind"] = _o.Text("--kind"), ["modelLevel"] = _o.Text("--level") });
                Out.WriteLine("rerouted task " + _o.Task + " to " + _o.Text("--kind") + "/" + _o.Text("--level") + " (chain governance ended)");
                return 0;
            }

            private int Reply()
            {
                var message = MessageText();
                if (_o.Has("--ceiling-action"))
                {
                    // CARD-1610 S4a D-6: a typed ceiling decision. The body is exclusive - no origin, message or round - because the
                    // server refuses a typed action beside any of them.
                    var action = _o.Text("--ceiling-action");
                    if (!string.IsNullOrWhiteSpace(message)) throw CeilingInvalid("--ceiling-action is exclusive with an answer message; pass one or the other.");
                    if (!Guid.TryParse(_o.Text("--episode-id"), out var episode) || episode == Guid.Empty)
                        throw CeilingInvalid("--episode-id must be the hold episode id (a GUID) that delegate status prints.");
                    var decision = new JsonObject { ["ceilingAction"] = action, ["episodeId"] = episode.ToString("D") };
                    var operatorExtend = _o.Switch("--operator-extend");
                    if (action == "Continue")
                    {
                        if (_o.Has("--extend-minutes")) throw CeilingInvalid("--ceiling-action Continue takes no --extend-minutes; use Extend.");
                        if (operatorExtend) throw CeilingInvalid("--operator-extend applies only to --ceiling-action Extend.");
                    }
                    else
                    {
                        if (!_o.Has("--extend-minutes")) throw CeilingInvalid("--ceiling-action Extend requires --extend-minutes.");
                        decision["extendMinutes"] = (int)CeilingMinutes("--extend-minutes", _o.Text("--extend-minutes"), 1, operatorExtend ? 240 : 720);
                    }
                    Dictionary<string, string> extra = null;
                    if (operatorExtend)
                    {
                        CeilingReason("--operator-extend", _o.Value("--ceiling-reason"));
                        decision["operatorExtend"] = true;
                        decision["ceilingReason"] = _o.Text("--ceiling-reason");
                        // Owner-only token, for this one request only; the value is never printed.
                        var token = OperatorApi.ReadToken(_files, _o.Environment.OperatorTokenFile, out var refusal);
                        if (token == null) throw new DelegateRefusal("operator_token_unavailable: " + refusal, 2);
                        extra = new Dictionary<string, string>(StringComparer.Ordinal) { [OperatorApi.OperatorTokenHeader] = token };
                    }
                    else if (_o.Has("--ceiling-reason"))
                    {
                        throw CeilingInvalid("--ceiling-reason on a reply applies only to --operator-extend.");
                    }
                    Request("POST", "/api/agent-tasks/" + _o.Task + "/reply", decision, extra);
                    Out.WriteLine("Decision recorded for task " + _o.Task + " (episode " + episode.ToString("D") + "): " + action
                        + ". The session continues; delegate status shows the next bound.");
                    return 0;
                }
                if (_o.Has("--episode-id") || _o.Has("--extend-minutes") || _o.Switch("--operator-extend") || _o.Has("--ceiling-reason"))
                    throw CeilingInvalid("--episode-id, --extend-minutes, --operator-extend and --ceiling-reason need --ceiling-action.");
                if (string.IsNullOrWhiteSpace(message))
                    throw new DelegateRefusal("Pass the answer after the task: scripts/antiphon delegate reply <taskId> \"your answer\" (or --message-file <path>)");
                Request("POST", "/api/agent-tasks/" + _o.Task + "/reply", new JsonObject { ["message"] = message, ["origin"] = "Cli" });
                Out.WriteLine("Answered task " + _o.Task + ". It will resume and report back.");
                return 0;
            }

            private int Refine()
            {
                var message = MessageText();
                if (string.IsNullOrWhiteSpace(message))
                    throw new DelegateRefusal("Pass the message after the task: scripts/antiphon delegate refine <taskId> \"your message\" (or --message-file <path>)");
                var interrupt = _o.Switch("--interrupt");
                var summary = Request("POST", "/api/agent-tasks/" + _o.Task + "/refine", new JsonObject
                {
                    ["message"] = message,
                    ["interruptCurrentTurn"] = interrupt,
                    ["requestId"] = Guid.NewGuid().ToString("D"),
                });
                if (Same(T(summary, "status"), "Queued")) Out.WriteLine("Refined task " + _o.Task + " before dispatch - folded into its brief.");
                else Out.WriteLine("Refined task " + _o.Task + ". The message will land between its turns; its report will note it.");
                if (interrupt)
                {
                    // Write-Output $null printed nothing.
                    if (!IsNull(BoardCli.Get(summary, "interruptWritten"))) Out.WriteLine(T(summary, "interruptWritten"));
                    if (!IsNull(BoardCli.Get(summary, "refinementDelivered"))) Out.WriteLine(T(summary, "refinementDelivered"));
                }
                return 0;
            }

            private int Finding()
            {
                var clean = _o.Switch("--clean");
                var found = _o.Text("--found");
                if (clean && !string.IsNullOrWhiteSpace(found)) throw new DelegateRefusal("Pass --found \"what\" or --clean, not both.");
                if (!clean && string.IsNullOrWhiteSpace(found)) throw new DelegateRefusal("Pass --found \"what you found\" or --clean.");
                var stage = _o.Text("--stage");
                var body = new JsonObject { ["stage"] = stage, ["found"] = !clean };
                if (!clean) body["detail"] = found;
                if (_o.Text("--reviewed-source-sha").Length > 0) body["reviewedSourceSha"] = _o.Text("--reviewed-source-sha");
                if (_o.Has("--reviewed-source-clean")) body["reviewedSourceClean"] = bool.Parse(_o.Text("--reviewed-source-clean"));
                Request("POST", "/api/agent-tasks/" + _o.Task + "/finding", body);
                Out.WriteLine("recorded " + stage + " " + (clean ? "clean" : "found") + " on task " + _o.Task);
                return 0;
            }

            private int Flag()
            {
                var verdict = _o.Text("--verdict");
                var body = new JsonObject { ["verdict"] = verdict };
                if (!string.IsNullOrWhiteSpace(_o.Text("--note"))) body["note"] = _o.Text("--note");
                Request("POST", "/api/agent-tasks/" + _o.Task + "/distillation/feedback", body);
                Out.WriteLine("flagged distillation on task " + _o.Task + " as " + verdict);
                return 0;
            }

            private int Create()
            {
                var goal = ReadText("goal", _o.Value("--goal"), _o.Value("--goal-file"));
                if (string.IsNullOrWhiteSpace(goal)) throw new DelegateRefusal("A --goal (or --goal-file) is required. Write it as an outcome, not a procedure.");

                // Whitespace-only is omitted (do not send title). Measure length after Trim; refuse CR/LF and anything over 80
                // rather than clamping. The server's BuildTitle clamp stays 300.
                string titleText = null;
                var title = _o.Text("--title");
                if (!string.IsNullOrWhiteSpace(title))
                {
                    titleText = title.Trim();
                    if (titleText.IndexOf('\r') >= 0 || titleText.IndexOf('\n') >= 0) throw new DelegateRefusal("--title is a single line");
                    if (titleText.Length > 80)
                        throw new DelegateRefusal("Title is " + Number(titleText.Length) + " characters; the limit is 80. --title is a 2-5 word label for check headers and the board,"
                            + " not a second Goal. Trim it, or put the rest in --goal.");
                }
                else
                {
                    // Same first-line extraction as AgentTaskService.BuildTitle: CR -> LF, skip empty, trim.
                    var firstLine = (goal.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n').FirstOrDefault(line => line != "") ?? "").Trim();
                    if (firstLine.Length > 80)
                        Out.WriteLine("WARNING: no --title; the goal's first line is " + Number(firstLine.Length) + " characters and will become the check-header/board title"
                            + " (server clamp 300) until antiphon-diagnose replaces it, if enabled. Pass --title with 2-5 words (max 80).");
                }

                var card = _o.Text("--card");
                var kind = _o.Text("--kind");
                var level = _o.Text("--level");
                var model = _o.Text("--model");
                var complexity = _o.Text("--complexity");
                var onAgent = _o.Text("--on-agent");
                var agent = _o.Text("--agent");
                var role = _o.Has("--role") ? _o.Text("--role") : "Custom";
                var orchestrator = _o.Switch("--orchestrator");
                var worktree = _o.Switch("--worktree");
                var shared = _o.Switch("--shared");
                var readOnly = _o.Switch("--read-only");
                var workspace = _o.Text("--workspace");
                var pin = _o.Switch("--pin");

                // --pin writes a pin for THIS card+stage. With nothing that could bind a card - no --card, no CARD-nnnn in the
                // title, and no task token whose own card could be inherited - it would write a STAGE-WIDE pin that changes
                // routing for every card. That is a deliberate act, so it goes through scripts/antiphon routing-pin rather than
                // falling out of one dispatch.
                if (pin && card.Length == 0 && !CardInTitle.IsMatch(titleText ?? "") && string.IsNullOrWhiteSpace(_o.Environment.TaskToken))
                    throw new DelegateRefusal("--pin without a card would write a stage-wide pin. Pass --card CARD-nnnn (or lead --title with it), or write the stage pin explicitly"
                        + " with scripts/antiphon routing-pin set.");
                // Same wording as the server's 422 - refused locally so the mistake costs no round trip.
                if (agent.Length > 0 && onAgent.Length > 0)
                    throw new DelegateRefusal("Agent and FollowUpOnTask are two different \"run it on that agent\" idioms - a follow-up already pins to the agent that ran the prior"
                        + " task. Use --agent or --on-agent, not both.");
                if (complexity.Length > 0 && (kind.Length > 0 || level.Length > 0))
                    throw new DelegateRefusal("complexity cannot be combined with agentKind or modelLevel. An explicit pair is a single candidate the caller chose and is never"
                        + " silently rerouted. Pass --complexity without --kind/--level, or pass the pair without --complexity.");
                if (complexity.Length > 0 && model.Length > 0)
                    throw new DelegateRefusal("complexity cannot be combined with model. An explicit alias is a single candidate the caller chose and is never silently rerouted."
                        + " Pass --complexity without --model, or pass --model without --complexity.");

                // CARD-0644 D-1: two workspace switches are ambiguous. Refused here, before any POST, rather than letting the first
                // one silently win.
                if (new[] { worktree, shared, readOnly, workspace.Length > 0 }.Count(given => given) > 1)
                    throw new DelegateRefusal("workspace_switch_conflict: --worktree, --shared, --read-only and --workspace are exclusive; pass at most one.", 2);

                // CARD-1610 S4a D-5: a declared ceiling is checked here, before any request, with the server's rule: 1..720 minutes
                // and a 1..1000-character reason without control characters.
                long? ceiling = null;
                if (_o.Has("--ceiling-minutes"))
                {
                    ceiling = CeilingMinutes("--ceiling-minutes", _o.Text("--ceiling-minutes"), 1, 720);
                    CeilingReason("--ceiling-minutes", _o.Value("--ceiling-reason"));
                }
                else if (_o.Has("--ceiling-reason"))
                {
                    throw CeilingInvalid("--ceiling-reason needs --ceiling-minutes; a reason alone is never stored.");
                }

                var sentRole = orchestrator && role == "Custom" ? "Plan" : role;
                var body = new JsonObject
                {
                    ["goal"] = goal,
                    ["kind"] = orchestrator ? "Orchestrator" : "Worker",
                    ["role"] = sentRole,
                };
                // Workspace is sent only when chosen - omitted, the server applies its fresh default (a Worktree for a fresh worker
                // or sub-orchestrator; the pinned agent's own checkout for --on-agent / --agent). Omission keeps a pin
                // distinguishable from an explicit --worktree.
                if (workspace.Length > 0) body["workspace"] = workspace;
                else if (worktree) body["workspace"] = "Worktree";
                else if (readOnly) body["workspace"] = "ReadOnly";
                else if (shared) body["workspace"] = "Shared";
                if (_o.Switch("--allow-direct-edits")) body["denyDirectEdits"] = false;
                if (onAgent.Length > 0) body["followUpOnTask"] = onAgent;

                // CARD-0604 D-15. Refused locally, before any POST, for the shapes the runner has no design for: Shared/ReadOnly have
                // no canonical desktop record to fast-forward, and --on-agent / --agent continue an EXISTING process that is already
                // somewhere else.
                var runner = _o.Text("--runner");
                var runnerIsDesktop = !string.IsNullOrWhiteSpace(runner) && (Same(runner.Trim(), "local") || Same(runner.Trim(), "desktop"));
                if (!string.IsNullOrWhiteSpace(runner) && !runnerIsDesktop)
                {
                    if (shared || readOnly) throw new DelegateRefusal("--runner requires a Worktree workspace; --shared and --read-only are refused.", 2);
                    if (onAgent.Length > 0 || !string.IsNullOrWhiteSpace(agent))
                        throw new DelegateRefusal("--runner cannot be combined with --on-agent or --agent: those continue an existing process.", 2);
                    // CARD-0604 Cut B: --source-landing on a runner IS supported -- the runner has a real custody backend and creates
                    // the verification snapshot itself. It is still a Mutation and nothing else; the server re-checks this and also
                    // asks that runner whether it can actually perform custody.
                    if (_o.Has("--source-landing") && role != "Mutation") throw new DelegateRefusal("--runner with --source-landing requires --role Mutation (CARD-0604 D-19).", 2);
                    body["runnerId"] = runner;
                    body["workspace"] = "Worktree";
                }
                else if (runnerIsDesktop)
                {
                    body["runnerId"] = "desktop";
                }
                if (_o.Has("--platform")) body["requiredPlatform"] = _o.Text("--platform");
                if (_o.Has("--source-landing")) body["sourceLandingOperationId"] = Guid.Parse(_o.Text("--source-landing")).ToString("D");
                if (_o.Has("--repair-source")) body["repairSourceTaskId"] = Guid.Parse(_o.Text("--repair-source")).ToString("D");

                // CARD-0613. Mirrored locally, before any POST, for the combinations the server can never admit. The selector
                // itself goes over VERBATIM - the server validates and refuses it, and a value this command quietly trimmed would
                // be a base the caller did not ask for.
                var otherWorkspace = workspace.Length > 0 && workspace != "Worktree";
                if (_o.Has("--start-ref"))
                {
                    if (shared || readOnly || otherWorkspace) throw new DelegateRefusal("worktree_start_ref_mode: --start-ref requires a Worktree.", 2);
                    if (onAgent.Length > 0 || !string.IsNullOrWhiteSpace(agent))
                        throw new DelegateRefusal("worktree_start_ref_mode: --start-ref cannot be combined with --on-agent or --agent: those continue an existing checkout.", 2);
                    if (_o.Has("--repair-source") || _o.Has("--source-landing"))
                        throw new DelegateRefusal("worktree_start_ref_mode: --start-ref cannot be combined with --repair-source or --source-landing; those already carry a structured base.", 2);
                    body["worktreeBaseRequestedRef"] = _o.Text("--start-ref");
                    // CARD-0644 D-5: the default fresh Worktree is what a start ref needs; say so on the wire.
                    body["workspace"] = "Worktree";
                }
                if (_o.Switch("--cold-workspace")) body["workspaceColdReset"] = true;
                var fresh = _o.Switch("--fresh-worktree");
                if (_o.Has("--base-task") || fresh)
                {
                    if (_o.Has("--base-task") && string.IsNullOrWhiteSpace(_o.Text("--base-task")))
                        throw new DelegateRefusal("worktree_base_source_invalid: --base-task needs a task id or 8-character short id.", 2);
                    if (_o.Has("--base-task") && fresh) throw new DelegateRefusal("worktree_base_mode: --base-task and --fresh-worktree are mutually exclusive.", 2);
                    if (shared || readOnly || otherWorkspace || onAgent.Length > 0 || agent.Length > 0 || _o.Text("--start-ref").Length > 0 || _o.Has("--repair-source")
                        || _o.Has("--source-landing"))
                        throw new DelegateRefusal("worktree_base_mode: a base override requires a fresh Worktree without --on-agent, --agent, --start-ref, --repair-source or"
                            + " --source-landing.", 2);
                    body["workspace"] = "Worktree";
                    if (_o.Has("--base-task")) body["worktreeBaseTask"] = _o.Text("--base-task");
                    if (fresh) body["freshWorktree"] = true;
                }

                Verification(body, role, orchestrator, shared, readOnly);

                if (agent.Length > 0) body["agent"] = agent;
                if (_o.Text("--stage").Length > 0) body["stage"] = _o.Text("--stage");
                if (!string.IsNullOrEmpty(titleText)) body["title"] = titleText;
                if (level.Length > 0) body["modelLevel"] = level;
                // Sent only when chosen - an omitted --kind leaves the decision to the role policy, which ships unset and therefore
                // resolves to ClaudeCode.
                if (kind.Length > 0) body["agentKind"] = kind;
                // Sent only when chosen. An omitted --model keeps the tier ladder.
                if (model.Length > 0) body["model"] = model;
                if (card.Length > 0) body["card"] = card;
                var directory = _o.Text("--dir");
                if (directory.Length == 0) directory = Checkout();
                if (directory.Length > 0) body["workingDirectory"] = directory;
                if (_o.Text("--scope").Length > 0) body["scope"] = _o.Text("--scope");
                if (_o.Has("--expect-about")) body["expectedMinutes"] = int.Parse(_o.Text("--expect-about"), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
                if (ceiling != null)
                {
                    body["ceilingMinutes"] = ceiling.Value;
                    body["ceilingReason"] = _o.Text("--ceiling-reason");
                }
                if (_o.Switch("--ignore-subscription-quota")) body["ignoreSubscriptionQuota"] = true;
                if (_o.Switch("--allow-unauthenticated-provider")) body["allowUnauthenticatedProvider"] = true;
                if (_o.Switch("--ignore-model-disabled")) body["ignoreModelDisabled"] = true;
                if (_o.Switch("--ignore-concurrency-limit")) body["ignoreConcurrencyLimit"] = true;
                if (_o.Switch("--ignore-routing-pin")) body["ignoreRoutingPin"] = true;
                if (complexity.Length > 0) body["complexity"] = complexity;
                if (_o.Switch("--refuse-if-exhausted")) body["refuseIfExhausted"] = true;
                if (_o.Switch("--no-commit")) body["commitOnSettle"] = "Never";
                var overrides = _o.EnvOverride();
                if (overrides.Count > 0)
                {
                    var launch = new JsonObject();
                    foreach (var pair in overrides) launch[pair.Key] = pair.Value;
                    body["launchEnvOverride"] = launch;
                }
                string authority = null;
                var authorityFile = _o.Text("--authority-file");
                if (!string.IsNullOrWhiteSpace(authorityFile))
                {
                    if (!string.IsNullOrWhiteSpace(_o.Text("--authority"))) throw new DelegateRefusal("Pass --authority or --authority-file, not both.");
                    var resolved = Resolve(authorityFile);
                    if (!_files.File.Exists(resolved)) throw new DelegateRefusal("--authority-file '" + authorityFile + "' does not exist.");
                    authority = _files.File.ReadAllText(resolved, Encoding.UTF8);
                }
                else if (!string.IsNullOrWhiteSpace(_o.Text("--authority")))
                {
                    authority = _o.Text("--authority");
                }
                if (!string.IsNullOrWhiteSpace(authority)) body["authority"] = authority;
                if (!_o.Switch("--no-inherit-env"))
                {
                    var inherited = new JsonObject();
                    foreach (var name in new[] { "X_LLM_PROJECT", "X_LLM_KEY" })
                    {
                        // A hashtable's ContainsKey ignored case.
                        if (overrides.Any(pair => Same(pair.Key, name))) continue;
                        if (_o.Environment.LlmEnvironment != null && _o.Environment.LlmEnvironment.TryGetValue(name, out var value) && value != null) inherited[name] = value;
                    }
                    if (inherited.Count > 0) body["inheritedLlmEnv"] = inherited;
                }

                var created = Request("POST", "/api/agent-tasks", body);
                CreatedLines(created, body, ceiling);
                // --pin records what this dispatch RESOLVED to, not what was typed: a caller who passed no --kind and got Grok from
                // the role policy still means "next time, the same" - and the pin is what makes that survive the next policy
                // change. Human + Required, because a pin written by hand is exactly the decision an Auto write must not overwrite.
                if (pin) Pin(created, sentRole, complexity);
                if (Truthy(created, "followUpMessage")) Out.WriteLine("FOLLOW-UP: " + T(created, "followUpMessage"));
                // And so is this: what the declared --scope costs, right now, against what is already running. 'serialise' means
                // this task waits; 'warn' means it starts and somebody owes a rebase. Said here because the dispatcher's verdict is
                // 5 seconds and one queue away, and by then nobody is reading.
                foreach (var overlap in BoardCli.Items(BoardCli.Get(created, "scopeOverlaps")))
                {
                    if (!BoardCli.Truthy(overlap)) continue;
                    var what = Truthy(overlap, "areas") ? T(overlap, "areas") : "shared checkout";
                    if (Same(T(overlap, "policy"), "serialise"))
                        Out.WriteLine("  will wait behind " + T(overlap, "shortId") + " (" + what + ", " + T(overlap, "workspace") + ")");
                    else
                    {
                        var branch = Truthy(overlap, "branch") ? T(overlap, "branch") : "the shared checkout";
                        Out.WriteLine("  overlaps " + T(overlap, "shortId") + " (" + what + ", " + T(overlap, "workspace") + ") - merge order matters; expect a rebase against " + branch);
                    }
                }
                return 0;
            }

            // CARD-0544: refused locally, before any POST, when the shape can never be admitted.
            private void Verification(JsonObject body, string role, bool orchestrator, bool shared, bool readOnly)
            {
                var fields = new[] { "--verification-subject", "--verification-baseline-outcome", "--verification-selection-file" }.Where(_o.Has).ToList();
                if (!_o.Has("--verification-round") && fields.Count == 0) return;
                var round = _o.Text("--verification-round");
                if (orchestrator || (role != "Code" && role != "Review"))
                    throw new DelegateRefusal("verification_round_role: --verification-round applies only to a Worker --role Code or Review.");
                if (round != "Interim" && fields.Count > 0)
                    throw new DelegateRefusal("verification_round_role: --verification-subject, --verification-baseline-outcome and --verification-selection-file require"
                        + " --verification-round Interim.");
                if (round == "Interim")
                {
                    if ((role == "Code" && (shared || readOnly)) || (role == "Review" && !readOnly))
                        throw new DelegateRefusal("verification_round_role: Interim is supported only for --role Code in its (default) Worktree and --role Review --read-only.");
                    // CARD-0644 D-5: Interim Code runs in the default fresh Worktree; say so on the wire.
                    if (role == "Code") body["workspace"] = "Worktree";
                    foreach (var flag in new[] { "--verification-subject", "--verification-baseline-outcome" })
                        if (!FullGuid.IsMatch(_o.Text(flag)))
                            throw new DelegateRefusal("verification_baseline_invalid: " + flag + " must be a full GUID (xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx), never a short id.");
                    var file = _o.Text("--verification-selection-file");
                    var resolved = Resolve(file);
                    if (string.IsNullOrWhiteSpace(file) || !_files.File.Exists(resolved))
                        throw new DelegateRefusal("verification_selection_invalid: --verification-selection-file must name an existing JSON file.");
                    JsonNode selection;
                    try
                    {
                        selection = JsonNode.Parse(_files.File.ReadAllText(resolved, Encoding.UTF8));
                    }
                    catch (JsonException)
                    {
                        throw new DelegateRefusal("verification_selection_invalid: --verification-selection-file is not valid JSON.");
                    }
                    var allowed = new[] { "artifactPath", "artifactCommitSha", "section" };
                    if (!(selection is JsonObject members)
                        || members.Any(member => !allowed.Contains(member.Key, StringComparer.OrdinalIgnoreCase) || member.Value == null || member.Value.GetValueKind() != JsonValueKind.String))
                        throw new DelegateRefusal("verification_selection_invalid: the selection file must be one object of string artifactPath, artifactCommitSha and section.");
                    body["verificationSubjectTaskId"] = Guid.Parse(_o.Text("--verification-subject")).ToString("D");
                    body["verificationBaselineOutcomeId"] = Guid.Parse(_o.Text("--verification-baseline-outcome")).ToString("D");
                    body["verificationSelection"] = new JsonObject
                    {
                        ["artifactPath"] = Member(members, "artifactPath"),
                        ["artifactCommitSha"] = Member(members, "artifactCommitSha"),
                        ["section"] = Member(members, "section"),
                    };
                }
                if (_o.Has("--verification-round")) body["verificationRound"] = round;
            }

            private void CreatedLines(JsonNode created, JsonObject body, long? ceiling)
            {
                // The RESOLVED kind is echoed, not the requested one - a role policy promoted to Grok in config is exactly the case
                // where the caller asked for nothing and should still see it.
                var kindNote = Truthy(created, "agentKind") && !Same(T(created, "agentKind"), "ClaudeCode") ? " [" + T(created, "agentKind") + "]" : "";
                // Where the report goes is a fact about THIS task, not a constant. A token-less caller has no parent task and no
                // parent session, so nothing is routed anywhere and the result only lands on the board (CARD-0020 S1).
                var routing = Truthy(created, "noReplyRouting")
                    ? " - NO REPLY WILL BE ROUTED: read the result on the board (antiphon task get " + T(created, "shortId") + ")"
                    : " - its report will arrive in your session";
                // Which card this task will move (CARD-0040), said HERE because this is the last moment the caller can still
                // correct a mis-binding - the alternative is discovering it on the board.
                var cardNote = Truthy(created, "cardIdentifier") ? " - bound to " + T(created, "cardIdentifier") : "";
                var exhausted = Same(T(created, "status"), "Blocked") && Truthy(created, "warning")
                    && T(created, "warning").IndexOf("routing exhausted", StringComparison.OrdinalIgnoreCase) >= 0;
                if (exhausted)
                {
                    Out.WriteLine("BLOCKED - " + T(created, "warning"));
                    Out.WriteLine("A human decides: clear a hold (scripts/antiphon model-availability clear), wait for a reset, or scripts/antiphon delegate reroute <id> --kind .."
                        + " --level ..  Do NOT pick a kind yourself.");
                    return;
                }
                Out.WriteLine("queued task " + T(created, "shortId") + " (" + T(body, "kind").ToLowerInvariant() + " " + T(body, "role").ToLowerInvariant() + " on "
                    + T(created, "modelLevel") + kindNote + ")" + routing + cardNote);
                var preview = BoardCli.Get(created, "worktreeBase");
                if (BoardCli.Truthy(preview))
                {
                    var destination = Truthy(preview, "landingTarget") ? T(preview, "landingTarget") : "master";
                    if (Same(T(preview, "decision"), "Continue"))
                    {
                        var source = T(preview, "sourceTaskId");
                        Out.WriteLine("  base preview: continues task " + source.Substring(0, Math.Min(8, source.Length)) + " on " + T(preview, "sourceBranch") + " @ "
                            + T(preview, "sourceSha") + "; new isolated branch; landing target " + destination + " (explicit land)");
                    }
                    else if (Same(T(preview, "decision"), "WaitForLand"))
                        Out.WriteLine("  base preview: waiting for same-card land (" + T(preview, "reason") + "); rechecked before launch");
                    else
                        Out.WriteLine("  base preview: " + T(preview, "decision") + " at " + T(preview, "fallbackRef") + "; landing target " + destination + "; reason " + T(preview, "reason"));
                    foreach (var warning in BoardCli.Items(BoardCli.Get(preview, "warnings")))
                        if (BoardCli.Truthy(warning)) Out.WriteLine("WARNING: " + BoardCli.Text(warning));
                }
                if (Truthy(created, "requiredPlatform"))
                {
                    var placed = Truthy(created, "runnerId") ? T(created, "runnerId") : "desktop";
                    Out.WriteLine("  platform: " + T(created, "requiredPlatform") + " on " + placed);
                }
                if (Truthy(created, "titleDiagnosisQueued"))
                    Out.WriteLine("  title: pending (antiphon-diagnose will set it from the goal; pass --title to set it yourself)");
                if (ceiling != null)
                {
                    // An older server ignores unknown members: say so rather than imply the ceiling holds.
                    var echoed = BoardCli.Get(created, "requestedCeilingMinutes");
                    if (!IsNull(echoed) && Long(echoed) == ceiling.Value)
                        Out.WriteLine("  ceiling: " + Number(ceiling.Value) + " min requested; held Working for a decision at it, never killed");
                    else
                        Out.WriteLine("WARNING: ceiling_unconfirmed: the server did not echo requestedCeilingMinutes=" + Number(ceiling.Value)
                            + "; the role default applies; check GET /api/version");
                }
                var route = BoardCli.Get(created, "routing");
                if (BoardCli.Truthy(route) && Truthy(route, "candidates"))
                {
                    var all = BoardCli.Items(BoardCli.Get(route, "candidates"));
                    var chosenIndex = all.ToList().FindIndex(candidate => Same(T(candidate, "outcome"), "chosen"));
                    if (chosenIndex >= 0)
                    {
                        var chosen = all[chosenIndex];
                        var skipped = all.Where(candidate => Same(T(candidate, "outcome"), "skipped"))
                            .Select(candidate => T(candidate, "alias") + " (" + (Truthy(candidate, "reason") ? T(candidate, "reason") : "skipped") + ")").ToList();
                        var skipText = skipped.Count > 0 ? "; skipped " + string.Join(", ", skipped) : "";
                        var source = T(route, "source");
                        if (IsPinSource(source))
                            Out.WriteLine("routed " + source + " -> " + T(chosen, "alias") + " (candidate " + Number(chosenIndex + 1) + "/" + Number(all.Count) + ")" + skipText);
                        else
                        {
                            var cellRole = T(route, "chainRole");
                            if (!Truthy(route, "chainRole")) cellRole = T(route, "role");
                            if (string.IsNullOrEmpty(cellRole)) cellRole = T(body, "role");
                            var cellLabel = cellRole + "/" + T(created, "complexity");
                            var via = !Truthy(route, "chainRole") && !Same(T(route, "chainSource"), "config") ? "; via any-role " + T(created, "complexity") + " chain" : "";
                            Out.WriteLine("routed " + cellLabel + " -> " + T(chosen, "alias") + " (candidate " + Number(chosenIndex + 1) + "/" + Number(all.Count) + via + ")" + skipText);
                        }
                    }
                }
                // A warning at creation is the caller's one chance to reconsider before the collision.
                if (Truthy(created, "warning")) Out.WriteLine("WARNING: " + T(created, "warning"));
            }

            private void Pin(JsonNode created, string role, string complexity)
            {
                if (!Truthy(created, "cardId"))
                {
                    Out.WriteLine("WARNING: --pin wrote nothing - this task bound no card, and a pin with no card is a stage-wide rule. Use scripts/antiphon routing-pin set if that is"
                        + " what you meant.");
                    return;
                }
                var pinBody = new JsonObject
                {
                    ["card"] = BoardCli.Get(created, "cardId")?.DeepClone(),
                    ["role"] = role,
                    ["provenance"] = "Human",
                    ["strength"] = "Required",
                    ["agentKind"] = BoardCli.Get(created, "agentKind")?.DeepClone(),
                    ["modelLevel"] = BoardCli.Get(created, "modelLevel")?.DeepClone(),
                    ["reason"] = "pinned by scripts/antiphon delegate create --pin from task " + T(created, "shortId"),
                };
                var pinned = Request("PUT", "/api/routing-pins", pinBody);
                var line = "pinned " + T(created, "cardIdentifier") + " " + T(pinned, "role") + " to " + T(pinned, "agentKind") + "/" + T(pinned, "modelLevel") + " (human, required)";
                var route = BoardCli.Get(created, "routing");
                if (BoardCli.Truthy(route) && Truthy(route, "walked") && IsPinSource(T(route, "source")))
                    line += " - this REPLACES the " + Number(BoardCli.Items(BoardCli.Get(route, "candidates")).Count) + "-candidate pin; clear it to restore the list";
                else if (complexity.Length > 0)
                    line += " (this removes " + T(created, "cardIdentifier") + " " + role + " from " + role + "/" + complexity + "-chain fallback; clear the pin to restore it)";
                Out.WriteLine(line);
            }

            // ---- requests ----

            // Invoke-Antiphon: one request on the fatal path. A failure prints the server's own answer whole ("Antiphon <METHOD>
            // <path> failed: <body>"): it explains WHY (worker cannot delegate, directory outside the allowed roots, cost
            // ceiling reached, the concurrency 409's population and occupants) and that is the actionable part. A JSON bind
            // 400 is preceded by the diagnosis line on stdout.
            private JsonNode Request(string method, string path, JsonObject body, IReadOnlyDictionary<string, string> extra = null)
            {
                var headers = new Dictionary<string, string>(_headers, StringComparer.Ordinal);
                if (extra != null)
                    foreach (var header in extra) headers[header.Key] = header.Value;
                NightlyReportHttpResponse answer;
                try
                {
                    answer = _http(method, _o.Api + path, headers, body == null ? null : OperatorApi.Json(body));
                }
                catch (Exception e)
                {
                    throw new DelegateRefusal("Antiphon " + method + " " + path + " failed: " + Message(e));
                }
                if (!OperatorApi.Succeeded(answer))
                {
                    var detail = FailureDetail(answer);
                    var bind = answer.Status == 400 ? BindFailure.Match(detail) : Match.Empty;
                    if (bind.Success)
                    {
                        var field = bind.Groups["field"].Value;
                        var value = "<unknown>";
                        if (body != null && body.TryGetPropertyValue(field, out var raw) && !IsNull(raw) && BoardCli.Text(raw) != "") value = BoardCli.Text(raw);
                        BindDiagnosis(field, value);
                    }
                    throw new DelegateRefusal("Antiphon " + method + " " + path + " failed: " + detail);
                }
                return Parse(answer.Body);
            }

            // CARD-0493 S2: what the served build is, against this checkout, when the server could not bind a value.
            private void BindDiagnosis(string field, string value)
            {
                var versionUri = _o.Api + "/api/version";
                var version = VersionProbe();
                string served = null;
                string served7 = null;
                string head;
                if (!version.Ok)
                {
                    var why = version.Status?.ToString(CultureInfo.InvariantCulture);
                    if (string.IsNullOrWhiteSpace(why)) why = version.Error;
                    if (string.IsNullOrWhiteSpace(why)) why = "error";
                    head = "GET " + versionUri + " failed (" + why + ")";
                }
                else
                {
                    string observed = null;
                    var parsed = string.IsNullOrWhiteSpace(version.Body) ? null : Parse(version.Body);
                    var text = BoardCli.Get(parsed, "version");
                    if (!IsNull(text) && BoardCli.Text(text) != "") observed = BoardCli.Text(text);
                    if (string.IsNullOrWhiteSpace(observed)) head = "GET " + versionUri + " failed (no version)";
                    else
                    {
                        served = observed;
                        served7 = Short(observed);
                        head = "served build " + served7;
                    }
                }
                var parts = new List<string> { "diagnosis: " + head + " rejected '" + value + "' for '" + field + "'" };
                if (served != null)
                {
                    var root = Checkout();
                    var headSha = Git(root, "rev-parse", "HEAD").Text;
                    var head7 = Short(headSha);
                    if (!Git(root, "cat-file", "-e", served + "^{commit}").Ok) parts.Add("served build " + served7 + " is not a commit this checkout knows");
                    else if (!string.IsNullOrWhiteSpace(headSha) && served.Equals(headSha, StringComparison.OrdinalIgnoreCase))
                        parts.Add("HEAD equals the served build; the value is genuinely unknown to this source");
                    else if (Git(root, "merge-base", "--is-ancestor", served, headSha).Ok)
                    {
                        var count = Git(root, "rev-list", "--count", served + ".." + headSha).Text;
                        if (string.IsNullOrWhiteSpace(count)) count = "0";
                        parts.Add("HEAD " + head7 + " is " + count + " commit(s) ahead of the served build");
                    }
                    else parts.Add("served build " + served7 + " is not an ancestor of HEAD " + head7);
                }
                parts.Add("restart the AppHost from the canonical checkout (scripts/antiphon restart-apphost), confirm GET " + versionUri
                    + ", then re-run this exact command; do not re-dispatch under a different --role");
                Out.WriteLine(string.Join("; ", parts));
            }

            private (bool Ok, string Text) Git(string root, params string[] arguments)
            {
                try
                {
                    var result = _host.RunGit(root, arguments);
                    return (result.ExitCode == 0, result.ExitCode == 0 ? (result.Output ?? "").Trim() : "");
                }
                catch (Exception)
                {
                    return (false, "");
                }
            }

            // GET /api/version on the probe transport (its own deadline): ok with the body, or the status (null for a transport
            // failure) and the failure's message.
            private (bool Ok, int? Status, string Body, string Error) VersionProbe()
            {
                try
                {
                    var answer = _probe("GET", _o.Api + "/api/version", _headers, null);
                    if (OperatorApi.Succeeded(answer)) return (true, answer.Status, answer.Body ?? "", null);
                    return (false, answer?.Status, null, StatusText(answer?.Status ?? 0));
                }
                catch (Exception e)
                {
                    return (false, null, null, Message(e));
                }
            }

            // ---- checks ----

            private static DelegateRefusal LandFailure(string message) => new DelegateRefusal(message, 1, alsoOnOut: true);

            // CARD-1610 S4a. A local ceiling refusal: one plain line and exit 2, before any request.
            private static DelegateRefusal CeilingInvalid(string detail) => new DelegateRefusal("ceiling_invalid: " + detail, 2);

            // Whole minutes in minimum..maximum. Parsed here, so 'ten' or a value past a long is a ceiling_invalid refusal.
            private static long CeilingMinutes(string name, string value, long minimum, long maximum)
            {
                if (!long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                    throw CeilingInvalid(name + " must be a whole number of minutes from " + Number(minimum) + " to " + Number(maximum) + ".");
                if (parsed < minimum || parsed > maximum)
                    throw CeilingInvalid(name + " must be from " + Number(minimum) + " to " + Number(maximum) + " minutes (got " + Number(parsed) + ").");
                return parsed;
            }

            // The server's reason rule plus no control characters: the reason is copied into the brief.
            private static void CeilingReason(string name, string reason)
            {
                if (string.IsNullOrWhiteSpace(reason)) throw CeilingInvalid(name + " needs --ceiling-reason (1..1000 characters).");
                if (reason.Length > 1000) throw CeilingInvalid("--ceiling-reason is " + Number(reason.Length) + " characters; the limit is 1000.");
                if (reason.Any(ch => ch < 32 || ch == 127)) throw CeilingInvalid("--ceiling-reason may not contain control characters or line breaks.");
            }

            private static bool IsLandSha(string value) => !string.IsNullOrWhiteSpace(value) && value != "unknown" && LandSha.IsMatch(value);

            private static bool IsPinSource(string source) =>
                source.StartsWith("pin:", StringComparison.OrdinalIgnoreCase) || source.StartsWith("pin+chain:", StringComparison.OrdinalIgnoreCase);

            // ---- texts and files ----

            // reply/refine: the inline message, or --message-file's text (UTF-8, sent as is); both is a refusal.
            private string MessageText() => ReadText("message", _o.Message, _o.Value("--message-file"));

            // The text of --<name> or --<name>-file (both is a refusal); a file is read as UTF-8 and sent as is.
            private string ReadText(string name, string inline, string path)
            {
                if (path == null) return inline;
                if (!string.IsNullOrWhiteSpace(inline)) throw new DelegateRefusal("Pass --" + name + " or --" + name + "-file, not both.");
                var resolved = Resolve(path);
                if (string.IsNullOrWhiteSpace(path) || !_files.File.Exists(resolved)) throw new DelegateRefusal("--" + name + "-file '" + path + "' does not exist.");
                return _files.File.ReadAllText(resolved, Encoding.UTF8);
            }

            // A path the caller gave, relative to the working directory.
            private string Resolve(string path)
            {
                var directory = _o.Environment.WorkingDirectory;
                if (string.IsNullOrEmpty(directory) || string.IsNullOrEmpty(path)) return path;
                return _files.Path.Combine(directory, path);
            }

            // git's top level of the working directory, else the checkout a workspace marker there names (CARD-0251), else the
            // working directory itself (outside a repository, or no git).
            private string Checkout()
            {
                if (_checkout != null) return _checkout;
                var directory = _o.Environment.WorkingDirectory ?? "";
                if (directory.Trim().Length == 0) return _checkout = "";
                try
                {
                    var result = _host.RunGit(directory, "rev-parse", "--show-toplevel");
                    var top = (result.Output ?? "").Split('\n').Select(line => line.Trim()).FirstOrDefault(line => line.Length > 0);
                    if (result.ExitCode == 0 && !string.IsNullOrEmpty(top)) return _checkout = top;
                }
                catch (Exception)
                {
                    // Outside a repository, or git missing: the marker, then the working directory.
                }
                return _checkout = WorkspaceCheckout(directory) ?? directory;
            }

            private string WorkspaceCheckout(string directory)
            {
                try
                {
                    var marker = _files.Path.Combine(directory, OrchestratorWorkspace.MarkerFileName);
                    if (!_files.File.Exists(marker)) return null;
                    var checkout = T(JsonNode.Parse(_files.File.ReadAllText(marker)), "checkout");
                    if (string.IsNullOrWhiteSpace(checkout)) return null;
                    var resolved = _files.Path.GetFullPath(_files.Path.Combine(directory, checkout));
                    return _files.Directory.Exists(resolved) || _files.File.Exists(resolved) ? resolved : null;
                }
                catch (Exception)
                {
                    return null;
                }
            }

            // ---- values ----

            // A failed answer's detail: the body as the server wrote it (ErrorDetails.Message), else the status line
            // Invoke-RestMethod's exception carried.
            private static string FailureDetail(NightlyReportHttpResponse answer)
            {
                var body = answer?.Body ?? "";
                return string.IsNullOrWhiteSpace(body) ? StatusText(answer?.Status ?? 0) : body;
            }

            // "Response status code does not indicate success: 404 (Not Found)."
            private static string StatusText(int status)
            {
                var name = Enum.IsDefined(typeof(HttpStatusCode), status) ? Regex.Replace(((HttpStatusCode)status).ToString(), "(?<=[a-z])(?=[A-Z])", " ") : "";
                return "Response status code does not indicate success: " + Number(status) + (name.Length > 0 ? " (" + name + ")" : "") + ".";
            }

            // A transport failure's message (it names the address or the timeout, never a header).
            private static string Message(Exception e) => e.Message;

            // scheme://host[:port] of the API, without a path or user information.
            private static string SafeApiBase(string api)
            {
                if (!Uri.TryCreate(api, UriKind.Absolute, out var uri)) return api;
                var port = !uri.IsDefaultPort && uri.Port > 0 ? ":" + Number(uri.Port) : "";
                return uri.Scheme + "://" + uri.Host + port;
            }

            // A body as JSON; null for an empty body or one that is not JSON (Invoke-RestMethod answered it as text).
            private static JsonNode Parse(string body)
            {
                if (string.IsNullOrWhiteSpace(body)) return null;
                try
                {
                    return JsonNode.Parse(body);
                }
                catch (JsonException)
                {
                    return null;
                }
            }

            // Format-AntiphonInstant: an ISO time (ConvertFrom-Json made it a DateTime) as UTC yyyy-MM-ddTHH:mm:ssZ; anything else
            // as its text.
            private static string Instant(JsonNode node)
            {
                var text = BoardCli.Text(node);
                if (IsoInstant.IsMatch(text)
                    && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var instant))
                    return instant.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
                return text;
            }

            private static JsonNode Member(JsonObject members, string name) =>
                members.Where(member => Same(member.Key, name)).Select(member => member.Value?.DeepClone()).FirstOrDefault();

            private static string Short(string sha) => string.IsNullOrEmpty(sha) ? sha ?? "" : sha.Substring(0, Math.Min(7, sha.Length));

            private static string T(JsonNode node, params string[] path) => BoardCli.Text(BoardCli.Get(node, path));

            private static bool Truthy(JsonNode node, params string[] path) => BoardCli.Truthy(BoardCli.Get(node, path));

            private static bool IsNull(JsonNode node) => node == null || node.GetValueKind() == JsonValueKind.Null;

            private static bool IsTrue(JsonNode node) => node != null && node.GetValueKind() == JsonValueKind.True;

            private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

            private static string Number(long value) => value.ToString(CultureInfo.InvariantCulture);

            // [int] of a JSON value: a number rounded half to even, a numeric string parsed, anything else 0.
            private static int Int(JsonNode node)
            {
                if (IsNull(node)) return 0;
                if (node.GetValueKind() == JsonValueKind.Number) return (int)Math.Round(node.GetValue<double>(), MidpointRounding.ToEven);
                return double.TryParse(BoardCli.Text(node), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? (int)Math.Round(parsed, MidpointRounding.ToEven) : 0;
            }

            private static long Long(JsonNode node)
            {
                if (IsNull(node)) return 0;
                if (node.GetValueKind() == JsonValueKind.Number) return (long)Math.Round(node.GetValue<double>(), MidpointRounding.ToEven);
                return long.TryParse(BoardCli.Text(node), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;
            }
        }
    }
}
