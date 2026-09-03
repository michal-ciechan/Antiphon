using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.AgentTui;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0058 slice 6 — a bundle attached to a SPECIFIC agent, and the drift check that says when a
/// running session's instructions have been overtaken by the repo.
///
/// <para>The card this closes is narrow and worth naming: <c>board-api</c> is attached to no role at
/// all, so a delegate working the card API never received it, and widening the role map would have
/// handed it to every delegate of that role. An attachment is the answer that fits — this agent,
/// this bundle — and this file pins that it composes, that it composes ONCE alongside a role
/// default, and that it never becomes a stored blob of composed text.</para>
///
/// <para>Everything about versions leans on what slice 1 already built: a bundle's version IS the
/// hash of its content, stamped into the composed output as <c>[bundle:key vhash8]</c>. The drift
/// comparison is therefore a string match against a freshly recomputed composition, and there is no
/// second versioning scheme anywhere for it to disagree with.</para>
/// </summary>
public class AgentBundleAttachmentTests
{
    // ---- what may be attached --------------------------------------------------------------------

    [Test]
    public void every_bundle_except_the_reply_styles_can_be_attached()
    {
        var attachable = InstructionBundles.Attachable.Select(b => b.Key).ToList();

        attachable.ShouldBe(["board-api", "check-interpreter", "delegate-basics", "diagnose", "orchestrator"]);
        attachable.ShouldContain(
            InstructionBundles.BoardApi,
            "the whole point of the slice: board-api is on no role, so an attachment is the only way "
            + "an agent that works the card API can carry it");
        InstructionBundles.All.Keys.Where(InstructionBundles.IsStyle).ShouldNotBeEmpty();
        attachable.ShouldNotContain(k => InstructionBundles.IsStyle(k));
    }

    [Test]
    public void a_style_is_rejected_by_name_rather_than_treated_as_a_typo()
    {
        // It IS a real bundle, so "unknown key" would be a lie and would send an operator looking for
        // a spelling mistake they did not make. The reason is that ReplyStyle already picks one.
        var ex = Should.Throw<ValidationException>(
            () => AgentBundleAttachments.Validate(["board-api", "style-caveman"]));

        ex.StatusCode.ShouldBe(422);
        ex.Errors.Values.SelectMany(e => e).ShouldContain(e => e.Contains("two voices"));
    }

    [Test]
    public void a_key_that_names_nothing_is_rejected_with_the_list_of_ones_that_do()
    {
        var ex = Should.Throw<ValidationException>(
            () => AgentBundleAttachments.Validate(["board-apis"]));

        ex.Errors.Values.SelectMany(e => e).ShouldContain(e => e.Contains("board-api"));
    }

    [Test]
    public void submitted_keys_are_trimmed_deduped_and_keep_the_order_they_arrived_in()
    {
        // Order is composition order, so first-occurrence-wins is the rule that keeps a resubmitted
        // list from quietly reordering an agent's instructions.
        AgentBundleAttachments.Validate([" board-api ", "orchestrator", "board-api", "  "])
            .ShouldBe(["board-api", "orchestrator"]);
    }

    // ---- composition ------------------------------------------------------------------------------

    [Test]
    public void an_attachment_composes_ahead_of_the_style_and_the_agents_own_contract()
    {
        const string own = "You are the board keeper.";

        var composed = InstructionBundleComposer.Compose(
            [InstructionBundles.BoardApi],
            AgentReplyStyles.ComposedKey(AgentReplyStyle.Terse),
            own);

        composed.Bundles.Select(b => b.Key).ShouldBe(["board-api", "style-terse"]);
        composed.Text.ShouldStartWith("[bundle:board-api v");
        composed.Text.ShouldEndWith(
            own, customMessage: "the agent's own contract still keeps the last word");
    }

    [Test]
    public void a_role_default_and_an_attachment_naming_the_same_bundle_compose_it_once()
    {
        // The dedup that makes attaching harmless: an operator who attaches delegate-basics to an
        // agent that already gets it by role must not make the agent read it twice.
        var keys = InstructionBundles.ForDelegate(
            AgentTaskKind.Worker, AgentTaskRole.Code, [InstructionBundles.DelegateBasics, InstructionBundles.BoardApi]);

        var composed = InstructionBundleComposer.Compose(keys);

        composed.Bundles.Select(b => b.Key).ShouldBe(["delegate-basics", "board-api"]);
        composed.Text.IndexOf("[bundle:delegate-basics", StringComparison.Ordinal).ShouldBe(0);
        composed.Text.IndexOf(
                "[bundle:delegate-basics",
                composed.Text.IndexOf("[bundle:delegate-basics", StringComparison.Ordinal) + 1,
                StringComparison.Ordinal)
            .ShouldBe(-1, "exactly once");
    }

    [Test]
    public void an_attachment_never_reopens_the_check_carve_out()
    {
        // The check interpreter is the agent MOST likely to be pinned and therefore most likely to
        // have an attachment. It has no tools and a deny-all hook: the carve-out is about what it can
        // OBEY, not about which map the instruction arrived through.
        InstructionBundles.ForDelegate(
                AgentTaskKind.Worker, AgentTaskRole.Check, [InstructionBundles.BoardApi])
            .ShouldBeEmpty();
    }

    [Test]
    public void composing_nothing_at_all_still_returns_the_append_byte_for_byte()
    {
        // Slice 1's property, re-pinned from this side because slice 6 added a parameter to the call
        // every launch path makes. No attachments, Normal style: the append must be the agent's own
        // text with nothing added, or every pre-existing agent's launch arguments changed.
        const string own = "You are Antiphon-Opus. Channels: {channels}.\r\n\r\nTrailing space kept. ";

        var composed = InstructionBundleComposer.Compose(
            [], AgentReplyStyles.ComposedKey(AgentReplyStyle.Normal), own);

        composed.Text.ShouldBe(own);
        composed.Bundles.ShouldBeEmpty();
        composed.StampLine.ShouldBeEmpty();
    }

    // ---- the drift comparison ---------------------------------------------------------------------

    [Test]
    public void the_stamp_line_is_the_content_hashes_and_nothing_else()
    {
        var composed = InstructionBundleComposer.Compose([InstructionBundles.BoardApi]);
        var bundle = InstructionBundles.Get(InstructionBundles.BoardApi);

        composed.StampLine.ShouldBe($"board-api v{bundle.Version}");
        composed.StampLine.ShouldNotContain(bundle.Text, customMessage:
            "stamps, never the composed text — a stored composition is the drift this card removes");
    }

    [Test]
    public void a_session_launched_with_what_the_repo_still_says_is_not_out_of_date()
    {
        var current = InstructionBundleComposer.Compose([InstructionBundles.BoardApi]);

        InstructionBundleComposer.IsOutOfDate(current.StampLine, current).ShouldBeFalse();
    }

    [Test]
    public void a_session_launched_before_the_column_existed_is_never_out_of_date()
    {
        // Null is NO EVIDENCE, and no evidence must never raise a badge: every session that predates
        // the migration has null here, and the migration deliberately does not backfill it.
        InstructionBundleComposer.IsOutOfDate(null, InstructionBundleComposer.Compose([InstructionBundles.BoardApi]))
            .ShouldBeFalse();
    }

    [Test]
    public void a_session_that_launched_carrying_nothing_IS_out_of_date_once_a_bundle_is_attached()
    {
        // The empty string is a real answer and this is why it has to be: "" says the launch composed
        // nothing, which an attachment made afterwards genuinely contradicts. Were it stored as null,
        // attaching a first bundle to a running agent would show no drift at all.
        InstructionBundleComposer.IsOutOfDate(
                string.Empty, InstructionBundleComposer.Compose([InstructionBundles.BoardApi]))
            .ShouldBeTrue();
    }

    [Test]
    public void an_edited_bundle_file_shows_as_drift_with_no_version_to_bump()
    {
        // Simulating the PR that edits a bundle: the version is the content hash, so the stamp the
        // session recorded simply stops matching. Nothing in the system had to be told the edit
        // happened, which is the property the whole scheme rests on.
        var current = InstructionBundleComposer.Compose([InstructionBundles.BoardApi]);
        var beforeTheEdit = $"{InstructionBundles.BoardApi} v0000dead";

        InstructionBundleComposer.IsOutOfDate(beforeTheEdit, current).ShouldBeTrue();
    }

    [Test]
    public void a_changed_reply_style_also_shows_as_drift()
    {
        // One comparison covers all three ways instructions can move: an edited file, an attachment
        // added or removed, and a style changed. They all end up as a different stamp line.
        var terse = InstructionBundleComposer.Compose(
            [], AgentReplyStyles.ComposedKey(AgentReplyStyle.Terse));
        var caveman = InstructionBundleComposer.Compose(
            [], AgentReplyStyles.ComposedKey(AgentReplyStyle.Caveman));

        InstructionBundleComposer.IsOutOfDate(terse.StampLine, caveman).ShouldBeTrue();
    }

    // ---- the rows -------------------------------------------------------------------------------

    [Test]
    [Category("Integration")]
    public async Task attachments_round_trip_and_keep_their_submitted_order()
    {
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions());
        var agent = await AddAgentAsync(db);

        (await AgentBundleAttachments.SetAsync(
            db, agent, [InstructionBundles.BoardApi, InstructionBundles.Orchestrator], DateTime.UtcNow, default))
            .ShouldBeTrue();
        await db.SaveChangesAsync();

        await using (var read = new AppDbContext(TestDbFixture.CreateDbContextOptions()))
        {
            (await AgentBundleAttachments.LoadAsync(read, agent.Id, null, default))
                .ShouldBe(["board-api", "orchestrator"]);
        }

        // Reordered: the same two keys, the other way round, must come back the other way round —
        // composition order is meaningful and the drift stamp is an ordered string.
        await using (var reorder = new AppDbContext(TestDbFixture.CreateDbContextOptions()))
        {
            var tracked = await reorder.Agents.SingleAsync(a => a.Id == agent.Id);
            (await AgentBundleAttachments.SetAsync(
                reorder, tracked, [InstructionBundles.Orchestrator, InstructionBundles.BoardApi], DateTime.UtcNow, default))
                .ShouldBeTrue();
            await reorder.SaveChangesAsync();
        }

        await using var verify = new AppDbContext(TestDbFixture.CreateDbContextOptions());
        (await AgentBundleAttachments.LoadAsync(verify, agent.Id, null, default))
            .ShouldBe(["orchestrator", "board-api"]);
    }

    [Test]
    [Category("Integration")]
    public async Task submitting_the_same_set_again_changes_nothing()
    {
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions());
        var agent = await AddAgentAsync(db);
        await AgentBundleAttachments.SetAsync(db, agent, [InstructionBundles.BoardApi], DateTime.UtcNow, default);
        await db.SaveChangesAsync();

        await using var again = new AppDbContext(TestDbFixture.CreateDbContextOptions());
        var tracked = await again.Agents.SingleAsync(a => a.Id == agent.Id);

        (await AgentBundleAttachments.SetAsync(
            again, tracked, [InstructionBundles.BoardApi], DateTime.UtcNow, default)).ShouldBeFalse();
    }

    [Test]
    [Category("Integration")]
    public async Task an_empty_list_detaches_everything_and_a_null_never_reaches_here()
    {
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions());
        var agent = await AddAgentAsync(db);
        await AgentBundleAttachments.SetAsync(db, agent, [InstructionBundles.BoardApi], DateTime.UtcNow, default);
        await db.SaveChangesAsync();

        await using (var clear = new AppDbContext(TestDbFixture.CreateDbContextOptions()))
        {
            var tracked = await clear.Agents.SingleAsync(a => a.Id == agent.Id);
            (await AgentBundleAttachments.SetAsync(clear, tracked, [], DateTime.UtcNow, default)).ShouldBeTrue();
            await clear.SaveChangesAsync();
        }

        await using var verify = new AppDbContext(TestDbFixture.CreateDbContextOptions());
        (await AgentBundleAttachments.LoadAsync(verify, agent.Id, null, default)).ShouldBeEmpty();
        // Null on the request means "leave alone", which is why UpdateAsync gates on it rather than
        // passing it through — an older client PATCHing an agent must not detach its bundles.
        new UpdateAgentRequest("A", "C:\\tmp", null, null, AgentAssignmentPolicy.AutoPick)
            .BundleKeys.ShouldBeNull();
    }

    [Test]
    [Category("Integration")]
    public async Task a_stored_key_whose_bundle_file_was_renamed_is_dropped_rather_than_failing_the_launch()
    {
        // The one case the composer's throw-on-miss would turn into an always-on agent that cannot
        // start. Attachment state is DATA and outlives the code it points at; a bundle renamed in a
        // later PR must cost that agent an optional block, not its process. The drop is a Warning and
        // the composition the UI shows stops listing it, so it is not silent.
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions());
        var agent = await AddAgentAsync(db);
        db.AgentBundleAttachments.AddRange(
            new AgentBundleAttachment
            {
                AgentId = agent.Id, BundleKey = "board-api", Position = 0, CreatedAt = DateTime.UtcNow,
            },
            new AgentBundleAttachment
            {
                AgentId = agent.Id, BundleKey = "renamed-away", Position = 1, CreatedAt = DateTime.UtcNow,
            });
        await db.SaveChangesAsync();

        await using var read = new AppDbContext(TestDbFixture.CreateDbContextOptions());
        var keys = await AgentBundleAttachments.LoadAsync(read, agent.Id, null, default);

        keys.ShouldBe(["board-api"]);
        Should.NotThrow(() => InstructionBundleComposer.Compose(keys));
    }

    [Test]
    [Category("Integration")]
    public async Task deleting_an_agent_takes_its_attachments_with_it()
    {
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions());
        var agent = await AddAgentAsync(db);
        await AgentBundleAttachments.SetAsync(db, agent, [InstructionBundles.BoardApi], DateTime.UtcNow, default);
        await db.SaveChangesAsync();

        await using (var delete = new AppDbContext(TestDbFixture.CreateDbContextOptions()))
        {
            delete.Agents.Remove(await delete.Agents.SingleAsync(a => a.Id == agent.Id));
            await delete.SaveChangesAsync();
        }

        await using var verify = new AppDbContext(TestDbFixture.CreateDbContextOptions());
        // Scoped to THIS agent's rows: the test database is shared by the whole assembly run.
        (await verify.AgentBundleAttachments.CountAsync(a => a.AgentId == agent.Id)).ShouldBe(0);
    }

    // ---- CARD-0247 S2 launch env ----------------------------------------------------------------

    [Test]
    [Category("Integration")]
    public async Task an_agent_with_the_orchestrator_bundle_launches_with_ANTIPHON_ORCHESTRATOR_1()
    {
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions());
        var agent = await AddAgentAsync(db);
        await AgentBundleAttachments.SetAsync(
            db, agent, [InstructionBundles.Orchestrator], DateTime.UtcNow, default);
        await db.SaveChangesAsync();

        var composition = await ComposeAsync(db, agent);

        composition.ExtraEnv["ANTIPHON_ORCHESTRATOR"].ShouldBe("1");
        composition.ExtraEnv.ShouldContainKey("ANTIPHON_AGENT_ID");
        composition.ExtraEnv.ShouldNotContainKey("ANTIPHON_TASK_ID");
    }

    [Test]
    [Category("Integration")]
    public async Task an_agent_without_the_orchestrator_bundle_does_not_set_ANTIPHON_ORCHESTRATOR()
    {
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions());
        var agent = await AddAgentAsync(db);
        await AgentBundleAttachments.SetAsync(
            db, agent, [InstructionBundles.BoardApi], DateTime.UtcNow, default);
        await db.SaveChangesAsync();

        var composition = await ComposeAsync(db, agent);

        composition.ExtraEnv.ShouldNotContainKey("ANTIPHON_ORCHESTRATOR");
    }

    [Test]
    [Category("Integration")]
    public async Task ComposeForAgentAsync_stamps_instruction_files_that_exist_under_cwd()
    {
        var cwd = Path.Combine(Path.GetTempPath(), $"antiphon-compose-stamp-{Guid.NewGuid():N}");
        Directory.CreateDirectory(cwd);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(cwd, "AGENTS.md"), "You are the floor.\n");
            await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions());
            var agent = await AddAgentAsync(db, cwd);

            var composition = await ComposeAsync(db, agent);

            var expected = InstructionFileStamps.Compute(cwd, PolicyRefreshSettings.DefaultInstructionFiles);
            composition.InstructionFileStamp.ShouldBe(expected.StampLine);
            composition.InstructionFileStamp.ShouldNotBeNullOrEmpty();
            composition.InstructionFileStamp!.ShouldContain("AGENTS.md v");
        }
        finally
        {
            Directory.Delete(cwd, recursive: true);
        }
    }

    private static Task<AgentLaunchComposition> ComposeAsync(AppDbContext db, Agent agent)
    {
        var registry = new AgentRegistry(new OptionsMonitorStub<AgentRegistrySettings>(new AgentRegistrySettings
        {
            DefaultDefinition = "claude",
            Definitions =
            {
                ["claude"] = new AgentDefinition { Kind = "ClaudeCode", Exe = "claude" },
            },
        }));
        var composer = new AgentSessionLaunchComposer(
            db,
            Options.Create(new DelegationSettings()),
            registry,
            NullLogger<AgentSessionLaunchComposer>.Instance);
        return composer.ComposeForAgentAsync(agent, CancellationToken.None);
    }

    private static async Task<Agent> AddAgentAsync(AppDbContext db, string? workingDirectory = null)
    {
        var name = $"bundle-{Guid.NewGuid():N}"[..20];
        var agent = new Agent
        {
            Id = Guid.NewGuid(),
            Name = name,
            Slug = name,
            WorkingDirectory = workingDirectory ?? "C:\\tmp",
            Details = string.Empty,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        db.Agents.Add(agent);
        await db.SaveChangesAsync();
        return agent;
    }
}
