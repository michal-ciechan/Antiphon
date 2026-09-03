using System.Security.Cryptography;
using System.Text;
using Antiphon.Server.Domain.Entities;

namespace Antiphon.Server.Application.Services;

/// <summary>What one <see cref="AgentWorkspaceProvisioner.Provision"/> call did, for logs and tests.</summary>
public enum WorkspaceFloorOutcome
{
    /// <summary>No file was there; we wrote one.</summary>
    Written,

    /// <summary>Ours already, and out of date. Rewritten.</summary>
    Rewritten,

    /// <summary>Ours already and byte-identical. Nothing written — the mtime does not churn.</summary>
    Unchanged,

    /// <summary>
    /// A file we recognise as our own hand-written stopgap, adopted under the marker so every future
    /// launch maintains it. The ONLY case in which an unmarked file is overwritten.
    /// </summary>
    Adopted,

    /// <summary>
    /// Somebody else's file — a repo's own <c>CLAUDE.md</c>, the operator's. Never touched. This is
    /// the outcome for every delegate running in the repository, and it is the design, not a miss.
    /// </summary>
    LeftAlone,

    /// <summary>The working directory does not exist (or is blank). Nothing to provision into.</summary>
    NoDirectory,

    /// <summary>IO said no. Logged; the caller is not failed over it.</summary>
    Failed,
}

/// <summary>
/// CARD-0059 — the floor: every agent's working directory carries a <c>CLAUDE.md</c> that names the
/// agent's job, so an agent booted into a bare directory knows what it is for without being told
/// again in every message. Claude reads the file from cwd unprompted at every process start, which
/// is what makes this the cheapest channel there is: no pty cost, no launch argument, no
/// re-injection after compaction.
///
/// <para>Generalised from <c>CheckInterpreterProvisioner.PrepareWorkspace</c> (the prototype, which
/// writes the deny-all hook the same idempotent way) and called from the two points where an agent's
/// identity is settled: <c>AgentService.CreateAsync</c> and <c>AgentControlService.StartAsync</c>.
/// Start is the reconcile point — a floor edited in a PR reaches every agent at its next launch,
/// with nothing stored anywhere to drift.</para>
///
/// <para><b>The rule that makes this safe rather than destructive: an unmarked file is NEVER
/// touched.</b> We write only when there is no file at all, or when the file we find carries our own
/// <c>&lt;!-- antiphon:managed &lt;hash8&gt; --&gt;</c> marker on its first line. A repository's own
/// <c>CLAUDE.md</c>, or one the operator wrote, has no marker and survives untouched — which is
/// precisely why this is a no-op for the delegates that run in <c>C:\src\Antiphon</c>, where the
/// repo's file already serves them. Deleting the marker line is how an operator takes permanent
/// ownership of a file we generated.</para>
/// </summary>
public sealed class AgentWorkspaceProvisioner
{
    /// <summary>Claude reads this name from cwd at process start. Not configurable — the name IS the contract.</summary>
    public const string FileName = "CLAUDE.md";

    /// <summary>The first line of a file we own. Anything else on line one means hands off.</summary>
    public const string MarkerPrefix = "<!-- antiphon:managed ";

    private const string MarkerSuffix = " -->";

    /// <summary>
    /// The repository conventions file, looked for in cwd and every ancestor. Named by ABSOLUTE path
    /// in the generated floor: an agent in a scratch directory cannot find it by relative walk, and
    /// telling it "read AGENTS.md" without saying where costs it a search or a hallucination.
    /// </summary>
    public const string ConventionsFileName = "AGENTS.md";

    /// <summary>
    /// SHA-256 of the hand-written stopgap that lived at <c>C:\logs\antiphon\check-interpreter\CLAUDE.md</c>
    /// (written 2026-08-16, 1 477 chars once line endings are normalised to LF and the outer blank
    /// space trimmed) — the worked example this generated floor is modelled on.
    ///
    /// <para>It has no marker, so the never-clobber rule would leave it in place forever and the
    /// generated floor would never reach the one agent it was designed against. Adoption is therefore
    /// EXPLICIT and exact: this one known content, matched byte for byte after normalisation, is
    /// rewritten once under the marker and maintained from then on. A file that merely looks similar
    /// is not adopted — an approximate match here would be the destructive behaviour this whole class
    /// exists to avoid, and the cost of a miss is only that an operator deletes one stale file.</para>
    /// </summary>
    private const string HandWrittenStopgapSha256 =
        "8819712e68ac8c1114900cdfc0963e5681d039f1118f6fd299ce8fbcffc21be4";

    private readonly ILogger<AgentWorkspaceProvisioner> _logger;

    public AgentWorkspaceProvisioner(ILogger<AgentWorkspaceProvisioner> logger) => _logger = logger;

    /// <summary>
    /// Write, rewrite or leave alone the floor in this agent's working directory. Never throws: a
    /// directory that cannot be written is a degraded agent, not a failed create or a failed launch,
    /// and the same reasoning as the deny-hook write it generalises (log loudly, carry on).
    /// </summary>
    public WorkspaceFloorOutcome Provision(
        Agent agent,
        IReadOnlyList<(string Provider, string Title)>? boundChannels = null)
    {
        var outcome = ProvisionCore(agent, boundChannels);
        if (outcome is WorkspaceFloorOutcome.Written or WorkspaceFloorOutcome.Rewritten or WorkspaceFloorOutcome.Adopted)
        {
            _logger.LogInformation(
                "{Outcome} the CLAUDE.md floor for agent '{Agent}' in {Directory}",
                outcome, agent.Name, agent.WorkingDirectory);
        }
        else if (outcome is WorkspaceFloorOutcome.LeftAlone)
        {
            // Debug, not a warning: this is the expected outcome for every agent that runs in a
            // repository, and warning on the normal case trains people to ignore the log.
            _logger.LogDebug(
                "Left the existing unmarked {File} in {Directory} alone for agent '{Agent}'",
                FileName, agent.WorkingDirectory, agent.Name);
        }

        return outcome;
    }

    private WorkspaceFloorOutcome ProvisionCore(
        Agent agent,
        IReadOnlyList<(string Provider, string Title)>? boundChannels)
    {
        if (string.IsNullOrWhiteSpace(agent.WorkingDirectory))
            return WorkspaceFloorOutcome.NoDirectory;

        try
        {
            var directory = Path.GetFullPath(agent.WorkingDirectory.Trim());
            // Deliberately does NOT create the directory. An agent can be created pointing at a path
            // that does not exist yet (CreateWorkingDirectory = false is a real option), and
            // materialising it as a side effect of writing a help file would be a surprise. The
            // launch path already refuses to start in a missing directory; the next start after the
            // operator makes it provisions the floor.
            if (!Directory.Exists(directory))
                return WorkspaceFloorOutcome.NoDirectory;

            var path = Path.Combine(directory, FileName);
            var desired = Render(agent, directory, boundChannels);

            if (!File.Exists(path))
            {
                File.WriteAllText(path, desired);
                return WorkspaceFloorOutcome.Written;
            }

            var existing = File.ReadAllText(path);
            var adopting = !IsManaged(existing) && IsAdoptableStopgap(existing);
            if (!IsManaged(existing) && !adopting)
                return WorkspaceFloorOutcome.LeftAlone;

            // Compare before writing, normalised: a no-op reconcile must stay a no-op, and this runs
            // on every single launch of every agent.
            if (string.Equals(Normalise(existing), Normalise(desired), StringComparison.Ordinal))
                return WorkspaceFloorOutcome.Unchanged;

            File.WriteAllText(path, desired);
            return adopting ? WorkspaceFloorOutcome.Adopted : WorkspaceFloorOutcome.Rewritten;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            _logger.LogWarning(
                ex, "Could not provision the {File} floor for agent '{Agent}' in {Directory}; "
                + "the agent will run without one", FileName, agent.Name, agent.WorkingDirectory);
            return WorkspaceFloorOutcome.Failed;
        }
    }

    /// <summary>True when the first line is our marker — the whole of the never-clobber rule.</summary>
    public static bool IsManaged(string content)
    {
        var newline = content.IndexOf('\n');
        var firstLine = (newline < 0 ? content : content[..newline]).Trim();
        return firstLine.StartsWith(MarkerPrefix, StringComparison.Ordinal)
            && firstLine.EndsWith(MarkerSuffix, StringComparison.Ordinal);
    }

    /// <summary>The one unmarked content we are allowed to overwrite. See <see cref="HandWrittenStopgapSha256"/>.</summary>
    public static bool IsAdoptableStopgap(string content) =>
        string.Equals(Sha256Hex(Normalise(content)), HandWrittenStopgapSha256, StringComparison.Ordinal);

    /// <summary>
    /// The generated floor, marker and all. Pure apart from the two filesystem facts it reports —
    /// whether the deny-all tool hook is armed in this directory, and where the nearest
    /// <see cref="ConventionsFileName"/> is — both of which are properties of the workspace and
    /// therefore belong in a file that describes the workspace.
    /// </summary>
    public static string Render(
        Agent agent,
        string directory,
        IReadOnlyList<(string Provider, string Title)>? boundChannels = null)
    {
        var body = RenderBody(agent, directory, boundChannels);
        return $"{MarkerPrefix}{Sha256Hex(body)[..8]}{MarkerSuffix}\n{body}";
    }

    private static string RenderBody(
        Agent agent,
        string directory,
        IReadOnlyList<(string Provider, string Title)>? boundChannels)
    {
        var text = new StringBuilder();
        text.Append(
            "<!-- Generated by Antiphon (CARD-0059) and rewritten at this agent's next launch.\n"
            + "     To own this file yourself, delete the marker line above: a file without it is never touched. -->\n\n");

        var name = string.IsNullOrWhiteSpace(agent.Name) ? "This agent" : agent.Name.Trim();
        text.Append($"# {name}\n\n");

        text.Append("## Your job\n\n");
        var details = agent.Details?.Trim();
        text.Append(string.IsNullOrEmpty(details)
            // Said plainly rather than papered over: an agent whose job nobody wrote down should
            // follow the work it is handed and not invent a wider remit from the directory it woke up in.
            ? "Nobody has written down a standing job for this agent. Do the work you are handed, and\n"
              + "nothing wider than that. If you need to know what this directory is for, ask rather than\n"
              + "guess.\n\n"
            : $"{details}\n\n");

        if (HasDenyAllToolHook(directory))
        {
            // Lifted from the hand-written stopgap, which is the shape that worked: the constraint is
            // stated as deliberate, because an agent that reads it as a fault spends its turn trying
            // to work around it.
            text.Append(
                "## You have NO TOOLS, deliberately\n\n"
                + "A PreToolUse hook in this directory denies every tool call. This is not a fault and not\n"
                + "something to work around.\n\n"
                + "- Do not try to read files, run commands, search, or fetch anything.\n"
                + "- Do not ask for more information.\n"
                + "- **Answer from what you were handed alone.** If it does not say, say it does not say.\n\n");
        }

        if (FindConventionsFile(directory) is { } conventions)
        {
            text.Append(
                "## The conventions for this work are written down\n\n"
                + $"    {conventions}\n\n"
                + "Read it before you change anything here. It is named by absolute path because a\n"
                + "relative walk will not find it from every directory an agent gets started in.\n\n");
        }

        // Verbatim from the stopgap (plan §5). The launch note orders a session-start ritual on every
        // preamble-configured agent; for an agent whose directory holds no SOUL.md or memory log that
        // is an impossible instruction, and obeying it costs a turn of the agent explaining so.
        text.Append(
            "## There is no session-start ritual\n\n"
            + "You have none; this file is the ritual. A launch note may tell you to follow one — read\n"
            + "SOUL.md, MEMORY.md, today's memory log. There is nothing of the sort here. Do not go\n"
            + "looking, and do not spend a turn reporting that you could not find it: read this file and\n"
            + "start the work you were given.");

        if (boundChannels is { Count: > 0 })
        {
            var label = string.Join(", ", boundChannels.Select(c => $"{c.Provider} \"{c.Title}\""));
            text.Append(
                $"\n\n## You are channel-bound ({label})\n\n"
                + "To send a file to the chat, put `[[attach: <absolute path>]]` on its own line in the turn that\n"
                + "answers the chat — or in a turn triggered by an Antiphon note (`[task … done]`, a check-in).\n"
                + "Your reply to a `[task …]`, `[check …]` or scheduled note is delivered to the chat as a follow-up unless it is exactly `NO_REPLY`; a delegate's own `[[attach:]]` is not — re-emit it or let the `--- deliverable ---` block do it.\n"
                + "Up to 14 MB per turn. Always attach a PDF for documents: Slack renders HTML as a text snippet,\n"
                + "local paths mean nothing to the chat, and a chat user cannot see later file edits.");
        }

        return text.ToString();
    }

    /// <summary>
    /// Exact-match against the deny-all hook <c>CheckInterpreterProvisioner</c> writes, never a
    /// heuristic over any <c>PreToolUse</c> entry. Telling an agent with ordinary tool access that it
    /// has none would be a far worse error than failing to mention a restriction, and an operator's
    /// own settings.json can legitimately hook one tool without disabling the rest.
    /// </summary>
    private static bool HasDenyAllToolHook(string directory)
    {
        var hookPath = Path.Combine(
            directory, CheckInterpretation.DenyHookRelativePath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(hookPath))
            return false;

        try
        {
            return string.Equals(
                Normalise(File.ReadAllText(hookPath)),
                Normalise(CheckInterpretation.DenyAllToolsSettingsJson),
                StringComparison.Ordinal);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// The nearest <see cref="ConventionsFileName"/> at or above <paramref name="directory"/>, by
    /// absolute path, or null. Nearest wins: a worktree's own conventions outrank the checkout's.
    /// </summary>
    public static string? FindConventionsFile(string directory)
    {
        for (var dir = new DirectoryInfo(directory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, ConventionsFileName);
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }

    /// <summary>LF and trimmed, so a CRLF checkout of the same content is the same content.</summary>
    private static string Normalise(string text) => text.ReplaceLineEndings("\n").Trim();

    private static string Sha256Hex(string text) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
}
