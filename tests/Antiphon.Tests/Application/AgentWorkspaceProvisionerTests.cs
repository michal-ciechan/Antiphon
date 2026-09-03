using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0059 — the CLAUDE.md floor.
///
/// <para>The one property that matters more than any other here is the NEGATIVE one: an unmarked
/// file is never touched. Everything else this class does is a convenience; that rule is the
/// difference between a service that helps agents and one that eats a repository's own instructions
/// the first time an agent is started in it. It gets three tests, and they are the ones to read
/// first if this ever needs changing.</para>
/// </summary>
public class AgentWorkspaceProvisionerTests
{
    // ---- writing, rewriting, and not writing ----------------------------------------------------

    [Test]
    public void an_empty_directory_gets_a_floor_naming_the_agents_job()
    {
        using var scratch = new TempWorkspace();
        var agent = AgentIn(scratch.Path, details: "Watch the Kafka bridge and answer questions about it.");

        Provision(agent).ShouldBe(WorkspaceFloorOutcome.Written);

        var content = scratch.ReadFloor();
        content.ShouldStartWith(AgentWorkspaceProvisioner.MarkerPrefix);
        content.ShouldContain("# Kafka Watcher");
        content.ShouldContain("Watch the Kafka bridge and answer questions about it.");
        content.ShouldContain(
            "this file is the ritual",
            customMessage: "the launch note orders a session-start ritual; the floor is what makes it obeyable");
    }

    [Test]
    public void a_second_call_that_would_write_the_same_thing_writes_nothing()
    {
        // This runs on EVERY launch of every agent. A churning mtime is how a real change becomes
        // invisible, and it is the same compare-before-write rule the deny-all hook already follows.
        using var scratch = new TempWorkspace();
        var agent = AgentIn(scratch.Path);
        Provision(agent).ShouldBe(WorkspaceFloorOutcome.Written);
        var writtenAt = File.GetLastWriteTimeUtc(scratch.FloorPath);

        Provision(agent).ShouldBe(WorkspaceFloorOutcome.Unchanged);

        File.GetLastWriteTimeUtc(scratch.FloorPath).ShouldBe(writtenAt);
    }

    [Test]
    public void our_own_stale_file_is_rewritten_when_the_agents_job_changes()
    {
        // The reconcile point is the launch, and there is no stored copy of this text anywhere: the
        // floor is recomputed from the row every time, so an edited job description reaches the agent
        // at its next start with nothing to migrate.
        using var scratch = new TempWorkspace();
        var agent = AgentIn(scratch.Path, details: "Old job.");
        Provision(agent);
        var stale = scratch.ReadFloor();

        agent.Details = "New job, written after the file was.";
        Provision(agent).ShouldBe(WorkspaceFloorOutcome.Rewritten);

        var fresh = scratch.ReadFloor();
        fresh.ShouldContain("New job, written after the file was.");
        fresh.ShouldNotContain("Old job.");
        MarkerOf(fresh).ShouldNotBe(MarkerOf(stale), "the marker carries the content hash, so it moves too");
    }

    // ---- the rule the whole design rests on -----------------------------------------------------

    [Test]
    public void an_unmarked_file_is_never_touched()
    {
        // A repository's own CLAUDE.md. This is what makes the service a no-op for every delegate that
        // runs in the checkout, which is not an accident to be fixed later — it is the design.
        using var scratch = new TempWorkspace();
        const string theirs = "# Claude Code Configuration\n\nSee AGENTS.md for all project conventions.\n";
        File.WriteAllText(scratch.FloorPath, theirs);
        var agent = AgentIn(scratch.Path);

        Provision(agent).ShouldBe(WorkspaceFloorOutcome.LeftAlone);

        scratch.ReadFloor().ShouldBe(theirs);
    }

    [Test]
    public void a_file_whose_marker_line_was_deleted_is_ownership_taken_back()
    {
        // The documented way for an operator to keep an edit: delete the marker. It must work on a
        // file we generated, or the instruction in the file itself is a lie.
        using var scratch = new TempWorkspace();
        var agent = AgentIn(scratch.Path);
        Provision(agent);
        var edited = string.Join('\n', scratch.ReadFloor().Split('\n').Skip(1))
            + "\n\nAnd one rule I added by hand.\n";
        File.WriteAllText(scratch.FloorPath, edited);

        Provision(agent).ShouldBe(WorkspaceFloorOutcome.LeftAlone);

        scratch.ReadFloor().ShouldBe(edited);
    }

    [Test]
    public void a_marker_that_is_not_ours_is_not_ours()
    {
        // Cheap, but the recogniser is a string prefix on one line and a false positive here deletes
        // somebody's file. Anything that is not exactly our comment is somebody else's.
        AgentWorkspaceProvisioner.IsManaged("<!-- antiphon:managed abc12345 -->\n# x").ShouldBeTrue();
        AgentWorkspaceProvisioner.IsManaged("<!-- managed by antiphon -->\n# x").ShouldBeFalse();
        AgentWorkspaceProvisioner.IsManaged("# antiphon:managed\n").ShouldBeFalse();
        AgentWorkspaceProvisioner.IsManaged("").ShouldBeFalse();
        AgentWorkspaceProvisioner.IsManaged("<!-- antiphon:managed abc12345").ShouldBeFalse();
    }

    // ---- adopting the one hand-written file that predates this service --------------------------

    [Test]
    public void the_hand_written_stopgap_is_adopted_once_and_maintained_after_that()
    {
        // C:\logs\antiphon\check-interpreter\CLAUDE.md, written by hand on 2026-08-16 and the worked
        // example this generator is modelled on. It carries no marker, so without an explicit
        // adoption the never-clobber rule would strand the one agent the floor was designed for.
        using var scratch = new TempWorkspace();
        File.WriteAllText(scratch.FloorPath, HandWrittenStopgap);
        var agent = AgentIn(scratch.Path);

        Provision(agent).ShouldBe(WorkspaceFloorOutcome.Adopted);

        scratch.ReadFloor().ShouldStartWith(AgentWorkspaceProvisioner.MarkerPrefix);
        Provision(agent).ShouldBe(WorkspaceFloorOutcome.Unchanged, "adoption happens once, not every launch");
    }

    [Test]
    public void a_file_that_merely_resembles_the_stopgap_is_not_adopted()
    {
        // Exact content or nothing. An approximate match is how a service like this eats a file
        // somebody wrote, and the cost of a miss is only a stale file an operator can delete.
        using var scratch = new TempWorkspace();
        var nearly = HandWrittenStopgap.Replace("Lead with the answer.", "Lead with the conclusion.");
        nearly.ShouldNotBe(HandWrittenStopgap, "the near-miss has to actually differ for this to test anything");
        File.WriteAllText(scratch.FloorPath, nearly);

        Provision(AgentIn(scratch.Path)).ShouldBe(WorkspaceFloorOutcome.LeftAlone);

        scratch.ReadFloor().ShouldBe(nearly);
    }

    [Test]
    public void the_stopgap_is_recognised_through_a_crlf_checkout()
    {
        AgentWorkspaceProvisioner.IsAdoptableStopgap(HandWrittenStopgap).ShouldBeTrue();
        AgentWorkspaceProvisioner.IsAdoptableStopgap(HandWrittenStopgap.ReplaceLineEndings("\r\n")).ShouldBeTrue();
        AgentWorkspaceProvisioner.IsAdoptableStopgap($"\n{HandWrittenStopgap}\n\n").ShouldBeTrue();
    }

    // ---- what the generated text says about the workspace ---------------------------------------

    [Test]
    public void the_deny_all_hook_in_the_directory_makes_the_floor_say_so()
    {
        using var scratch = new TempWorkspace();
        scratch.WriteDenyAllHook();

        Provision(AgentIn(scratch.Path));

        var content = scratch.ReadFloor();
        content.ShouldContain("You have NO TOOLS, deliberately");
        content.ShouldContain("Do not ask for more information.");
    }

    [Test]
    public void an_ordinary_settings_file_never_makes_the_floor_claim_the_agent_has_no_tools()
    {
        // Telling an agent with full tool access that it has none would waste every turn it takes.
        // The recogniser is an exact match against the hook we write, never "has a PreToolUse entry".
        using var scratch = new TempWorkspace();
        Directory.CreateDirectory(Path.Combine(scratch.Path, ".claude"));
        File.WriteAllText(
            Path.Combine(scratch.Path, ".claude", "settings.json"),
            """{ "hooks": { "PreToolUse": [ { "matcher": "Bash", "hooks": [] } ] } }""");

        Provision(AgentIn(scratch.Path));

        scratch.ReadFloor().ShouldNotContain("NO TOOLS");
    }

    [Test]
    public void no_hook_no_claim()
    {
        using var scratch = new TempWorkspace();

        Provision(AgentIn(scratch.Path));

        scratch.ReadFloor().ShouldNotContain("NO TOOLS");
    }

    [Test]
    public void a_conventions_file_in_the_directory_is_named_by_absolute_path()
    {
        using var scratch = new TempWorkspace();
        var conventions = Path.Combine(scratch.Path, "AGENTS.md");
        File.WriteAllText(conventions, "# Conventions");

        Provision(AgentIn(scratch.Path));

        scratch.ReadFloor().ShouldContain(conventions);
    }

    [Test]
    public void a_conventions_file_in_an_ancestor_is_found_and_named_by_absolute_path()
    {
        // The case that motivates the absolute path: an agent two directories down cannot find
        // AGENTS.md by a relative reference, and "read AGENTS.md" without a location costs it a search.
        using var scratch = new TempWorkspace();
        var conventions = Path.Combine(scratch.Path, "AGENTS.md");
        File.WriteAllText(conventions, "# Conventions");
        var nested = Directory.CreateDirectory(Path.Combine(scratch.Path, "a", "b")).FullName;

        Provision(AgentIn(nested));

        File.ReadAllText(Path.Combine(nested, AgentWorkspaceProvisioner.FileName)).ShouldContain(conventions);
    }

    [Test]
    public void the_nearest_conventions_file_wins()
    {
        using var scratch = new TempWorkspace();
        File.WriteAllText(Path.Combine(scratch.Path, "AGENTS.md"), "# Outer");
        var nested = Directory.CreateDirectory(Path.Combine(scratch.Path, "inner")).FullName;
        var inner = Path.Combine(nested, "AGENTS.md");
        File.WriteAllText(inner, "# Inner");

        AgentWorkspaceProvisioner.FindConventionsFile(nested).ShouldBe(inner);
    }

    [Test]
    public void no_conventions_file_anywhere_leaves_the_section_out_rather_than_pointing_at_nothing()
    {
        using var scratch = new TempWorkspace();

        Provision(AgentIn(scratch.Path));

        scratch.ReadFloor().ShouldNotContain("The conventions for this work are written down");
    }

    [Test]
    public void a_bound_agent_gets_a_channel_section_naming_the_attach_follow_up_rule()
    {
        using var scratch = new TempWorkspace();
        var agent = AgentIn(scratch.Path);

        Provision(agent, [("slack", "PredictionMarkets"), ("telegram", "Family")])
            .ShouldBe(WorkspaceFloorOutcome.Written);

        var content = scratch.ReadFloor();
        content.ShouldContain("## You are channel-bound (slack \"PredictionMarkets\", telegram \"Family\")");
        content.ShouldContain("[[attach: <absolute path>]]");
        content.ShouldContain("Your reply to a `[task …]`, `[check …]` or scheduled note is delivered to the chat as a follow-up unless it is exactly `NO_REPLY`");
        content.ShouldContain("A delegate's");
        content.ShouldContain("--- deliverable ---");
        content.ShouldContain("Slack renders HTML as a text snippet");
    }

    [Test]
    public void an_unbound_agent_does_not_get_the_channel_section()
    {
        using var scratch = new TempWorkspace();

        Provision(AgentIn(scratch.Path));

        scratch.ReadFloor().ShouldNotContain("You are channel-bound");
    }

    [Test]
    public void an_unmarked_file_stays_left_alone_even_when_the_agent_is_channel_bound()
    {
        using var scratch = new TempWorkspace();
        const string theirs = "# Claude Code Configuration\n\nSee AGENTS.md for all project conventions.\n";
        File.WriteAllText(scratch.FloorPath, theirs);

        Provision(AgentIn(scratch.Path), [("slack", "PredictionMarkets")])
            .ShouldBe(WorkspaceFloorOutcome.LeftAlone);

        scratch.ReadFloor().ShouldBe(theirs);
    }

    [Test]
    public void an_agent_with_no_written_job_gets_a_floor_that_says_so_instead_of_inventing_one()
    {
        using var scratch = new TempWorkspace();

        Provision(AgentIn(scratch.Path, details: "   "));

        var content = scratch.ReadFloor();
        content.ShouldContain("Nobody has written down a standing job");
        content.ShouldContain("this file is the ritual");
    }

    // ---- degrading -------------------------------------------------------------------------------

    [Test]
    public void a_working_directory_that_does_not_exist_is_not_created_and_not_an_error()
    {
        // CreateWorkingDirectory=false is a real option, so an agent can legitimately point at a path
        // that is not there yet. Materialising it as a side effect of writing a help file would be a
        // surprise, and failing the create over it would be worse.
        using var scratch = new TempWorkspace();
        var missing = Path.Combine(scratch.Path, "not-yet");

        Provision(AgentIn(missing)).ShouldBe(WorkspaceFloorOutcome.NoDirectory);

        Directory.Exists(missing).ShouldBeFalse();
    }

    [Test]
    public void an_agent_with_no_working_directory_at_all_degrades_quietly()
    {
        Provision(AgentIn("")).ShouldBe(WorkspaceFloorOutcome.NoDirectory);
        Provision(AgentIn("   ")).ShouldBe(WorkspaceFloorOutcome.NoDirectory);
    }

    [Test]
    public void a_directory_it_cannot_write_is_logged_and_survived()
    {
        // Mirrors the deny-hook catch: a workspace that cannot be prepared is a degraded agent, never
        // a failed create or a failed launch. Provoked with a path no filesystem accepts, so the test
        // needs no permissions of its own.
        Provision(AgentIn("C:\\\0invalid")).ShouldBeOneOf(
            WorkspaceFloorOutcome.Failed, WorkspaceFloorOutcome.NoDirectory);
    }

    // ---- the specialist this was designed against ------------------------------------------------

    [Test]
    [Category("Integration")]
    public async Task the_check_interpreter_gets_its_floor_when_it_is_provisioned()
    {
        // The agent the hand-written stopgap was written for: a bare scratch directory, a deny-all
        // hook, and a launch note ordering a session-start ritual it cannot perform.
        using var scratch = new TempWorkspace();
        var settings = new DelegationSettings
        {
            CheckInterpreterAgentSlug = $"check-floor-{Guid.NewGuid():N}"[..24],
            CheckInterpreterWorkingDirectory = scratch.Path,
        };

        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions());
        var provisioner = new CheckInterpreterProvisioner(
            db, Options.Create(settings), TimeProvider.System,
            NullLogger<CheckInterpreterProvisioner>.Instance,
            control: null,
            workspace: NewProvisioner());
        var agent = await provisioner.EnsureAsync(CancellationToken.None);

        agent.ShouldNotBeNull();
        var content = scratch.ReadFloor();
        content.ShouldStartWith(AgentWorkspaceProvisioner.MarkerPrefix);
        content.ShouldContain(
            "You have NO TOOLS, deliberately",
            customMessage: "PrepareWorkspace arms the hook before the floor is rendered, so it knows");
        content.ShouldContain("this file is the ritual");
    }

    // ---- helpers ----------------------------------------------------------------------------------

    private static AgentWorkspaceProvisioner NewProvisioner() =>
        new(NullLogger<AgentWorkspaceProvisioner>.Instance);

    private static WorkspaceFloorOutcome Provision(
        Agent agent,
        IReadOnlyList<(string Provider, string Title)>? boundChannels = null) =>
        NewProvisioner().Provision(agent, boundChannels);

    private static Agent AgentIn(string directory, string details = "Answer questions about this directory.") =>
        new()
        {
            Id = Guid.NewGuid(),
            Name = "Kafka Watcher",
            Slug = "kafka-watcher",
            WorkingDirectory = directory,
            Details = details,
        };

    private static string MarkerOf(string content) => content.Split('\n')[0];

    /// <summary>
    /// The stopgap, verbatim (LF). Its hash is the adoption key, so this constant and the constant in
    /// <see cref="AgentWorkspaceProvisioner"/> pin each other: change one without the other and
    /// <see cref="the_hand_written_stopgap_is_adopted_once_and_maintained_after_that"/> goes red.
    /// </summary>
    private const string HandWrittenStopgap =
        """
        # Check interpreter

        You are the Antiphon **check interpreter**. One job, done many times.

        ## Your job

        You are handed a deterministic **fact bundle** about another agent that is mid-task — its status,
        elapsed time, session state, transcript tail, git log, queue and incidents. You turn it into **three
        to five lines** the caller actually reads:

        - What it appears to be doing now.
        - Whether it has produced anything yet (commits, files, a deliverable).
        - Whether it looks stuck, and on what.

        Lead with the answer. No preamble. No restating the bundle. If nothing is wrong, say so in one line.

        ## You have NO TOOLS, deliberately

        A PreToolUse hook denies every tool call. This is not a fault and not something to work around.

        - Do not try to read files, run commands, search, or fetch anything.
        - Do not ask for more information.
        - **Answer from the bundle alone.** If the bundle does not say, say it does not say.

        There is no session-start ritual for you. If a launch note asks for one, ignore it — this file is
        the ritual.

        ## Judgement

        - "Working" plus a recent transcript entry means it is alive; say what it is working on.
        - Zero commits on a long task is normal early and worth flagging late.
        - An incident, a failed session, or a stalled queue is the most important thing in the bundle —
          lead with it.
        - Never guess at causes you cannot see. "No output for 12 minutes" is a fact; "it is probably stuck
          on the build" is a guess — mark it as one or leave it out.
        """;

    private sealed class TempWorkspace : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("antiphon-floor-test").FullName;

        public string FloorPath => System.IO.Path.Combine(Path, AgentWorkspaceProvisioner.FileName);

        public string ReadFloor() => File.ReadAllText(FloorPath);

        public void WriteDenyAllHook()
        {
            var hookPath = System.IO.Path.Combine(
                Path, CheckInterpretation.DenyHookRelativePath.Replace('/', System.IO.Path.DirectorySeparatorChar));
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(hookPath)!);
            File.WriteAllText(hookPath, CheckInterpretation.DenyAllToolsSettingsJson);
        }

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); }
            catch (IOException) { }
        }
    }
}
