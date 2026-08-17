using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// The read-only projection over the plan files git already holds (mobile-thread spec slice T1).
///
/// <para>Two properties carry the whole design and each has its own group below.</para>
///
/// <list type="number">
/// <item><b>Tolerance.</b> The 23 specs in this repo follow no enforced header format — four
/// distinct shapes are in use and one file has no header at all. Every shape is pinned here from
/// the real corpus, and so is the file that parses to almost nothing: it must still be LISTED, with
/// what could be read. A catalog that dropped unparseable files would be a catalog of the plans
/// written after it shipped, which is the opposite of a projection that is retroactive by
/// construction.</item>
/// <item><b>Refusal.</b> The content route serves file contents by name, so it is the one thing
/// here that can be turned into a read-any-file endpoint. Every escape shape is refused as a
/// validation error rather than a 404 — a 404 would tell a prober that a different path might have
/// worked — and the refusal is proved against a file that really exists outside the roots, so a
/// passing test cannot be an accident of the target being missing.</item>
/// </list>
///
/// <para>No database: this is a projection over a directory, and every test builds its own.</para>
/// </summary>
[Category("Unit")]
public class PlanCatalogServiceTests
{
    // ---- 1. the header shapes the corpus actually uses -------------------------------------------

    [Test]
    public async Task The_bulleted_header_shape_yields_title_status_date_and_card()
    {
        // 2026-08-16-card-0035-stuck-work-view.md's shape.
        using var repo = new TempRepo();
        repo.WriteSpec("2026-08-16-card-0035-stuck-work-view.md", """
            # CARD-0035 — A diagnostic view for work that is stuck

            - **Status**: Planned (this document is the plan; nothing here is implemented)
            - **Card**: CARD-0035 (`43635fab-de31-4f12-87b8-df2f9af21bd5`) — "UX: a diagnostic view"
            - **Date**: 2026-08-16

            ## 0. What exists today
            """);

        var plan = (await repo.ListAsync()).Plans.Single();

        plan.Title.ShouldBe("CARD-0035 — A diagnostic view for work that is stuck");
        plan.Status.ShouldBe("Planned (this document is the plan; nothing here is implemented)");
        plan.Date.ShouldBe(new DateOnly(2026, 8, 16));
        plan.Cards.ShouldBe(["CARD-0035"]);
        plan.Kind.ShouldBe(PlanKind.Spec);
        plan.RelativePath.ShouldBe("docs/superpowers/specs/2026-08-16-card-0035-stuck-work-view.md");
    }

    [Test]
    public async Task Three_fields_on_one_bold_line_are_read_as_three_fields()
    {
        // 2026-08-11-card-0019-card-correction.md's shape: **Status:** x. **Card:** y. **Date:** z.
        // Reading the line as one field would put "**Card:** CARD-0019 …" inside the status text.
        using var repo = new TempRepo();
        repo.WriteSpec("2026-08-11-card-0019-card-correction.md", """
            # CARD-0019: Card correction — edit with history, archive, and the 4000-char ceiling

            **Status:** planned, not implemented. **Card:** CARD-0019 (priority 0, `bug/api/cards`).
            **Date:** 2026-08-11.

            ## Problem
            """);

        var plan = (await repo.ListAsync()).Plans.Single();

        plan.Status.ShouldBe("planned, not implemented");
        plan.Cards.ShouldBe(["CARD-0019"]);
        plan.Date.ShouldBe(new DateOnly(2026, 8, 11));
    }

    [Test]
    public async Task A_plain_header_whose_value_is_bold_still_yields_the_status()
    {
        // 2026-07-19-pty-host-split.md: `Status: **Implemented** (2026-07-19, slices 1–6 …)`.
        // Read as a bold field this names the VALUE and loses the label, which is why the plain
        // form is tried first.
        using var repo = new TempRepo();
        repo.WriteSpec("2026-07-19-pty-host-split.md", """
            # Spec: PTY-Host Split — Sessions Survive Runner Restarts

            Date: 2026-07-19
            Status: **Implemented** (2026-07-19, slices 1–6: skeleton `62c5cb8`, adoption `b148bec`)

            ## Problem
            """);

        var plan = (await repo.ListAsync()).Plans.Single();

        plan.Status.ShouldStartWith("Implemented (2026-07-19");
        plan.Date.ShouldBe(new DateOnly(2026, 7, 19));
    }

    [Test]
    public async Task A_file_with_no_header_block_at_all_is_still_listed()
    {
        // 2026-05-18-agent-queues-design.md: a title, then straight into prose. The tolerance case
        // — it appears with what could be read and null where nothing could.
        using var repo = new TempRepo();
        repo.WriteSpec("2026-05-18-agent-queues-design.md", """
            # Agent Queues Design

            ## Summary

            Antiphon will add a top-level Agents area where users define persistent agents.
            """);

        var plan = (await repo.ListAsync()).Plans.Single();

        plan.Title.ShouldBe("Agent Queues Design");
        plan.Status.ShouldBeNull("nothing said what the status was — inventing one would be worse");
        plan.Date.ShouldBe(new DateOnly(2026, 5, 18), "the filename still dates it");
        plan.Cards.ShouldBeEmpty();
    }

    [Test]
    public async Task A_title_that_carries_the_identifier_is_enough_to_correlate()
    {
        // 2026-08-09-transcript-adoption-safety.md — undated-by-card filename, identifier in the
        // title only. Without the title scan this plan would belong to no card at all.
        using var repo = new TempRepo();
        repo.WriteSpec("2026-08-09-transcript-adoption-safety.md", """
            # Transcript adoption safety (CARD-0006)

            **Status: slice 1 implemented 2026-08-10**

            ## Problem
            """);

        (await repo.ListAsync()).Plans.Single().Cards.ShouldBe(["CARD-0006"]);
    }

    [Test]
    public async Task A_multi_card_header_field_yields_every_identifier_it_names()
    {
        using var repo = new TempRepo();
        repo.WriteSpec("2026-08-17-mobile-thread-and-plan-surfacing.md", """
            # The work thread on a phone

            - **Status**: Proposed (planning only)
            - **Date**: 2026-08-17
            - **Cards reconciled** (no new card filed): CARD-0002, CARD-0031, CARD-0033

            ## 0. What exists today
            """);

        (await repo.ListAsync()).Plans.Single().Cards
            .ShouldBe(["CARD-0002", "CARD-0031", "CARD-0033"]);
    }

    [Test]
    public async Task A_header_field_that_wraps_keeps_what_is_on_the_continuation_line()
    {
        // Not an edge case: half the specs here carry a Card(s) line long enough to wrap, and
        // reading only its first line drops most of what it names. The blank line below the field
        // is what stops the fold — prose under a header is not part of the header.
        using var repo = new TempRepo();
        repo.WriteSpec("2026-08-17-mobile-thread-and-plan-surfacing.md", """
            # The work thread on a phone

            - **Status**: Proposed (planning only)
            - **Cards reconciled** (no new card filed, per the brief): CARD-0002, CARD-0031, CARD-0032,
              CARD-0033, CARD-0034, CARD-0035, CARD-0036.
            - **Date**: 2026-08-17

            CARD-0099 is prose below the header, not a subject.

            ## 0. What exists today
            """);

        var plan = (await repo.ListAsync()).Plans.Single();

        plan.Cards.ShouldBe([
            "CARD-0002", "CARD-0031", "CARD-0032", "CARD-0033", "CARD-0034", "CARD-0035", "CARD-0036",
        ]);
        plan.MentionedCards.ShouldBe(["CARD-0099"]);
        plan.Date.ShouldBe(new DateOnly(2026, 8, 17), "the field after the wrap is still its own field");
    }

    [Test]
    public async Task A_neighbours_citation_is_mentioned_not_owned()
    {
        // Most specs cite four or five neighbours under "Relates to". Folding those into Cards
        // would put every plan on every neighbouring card's thread, which is how a correlated view
        // stops being worth opening.
        using var repo = new TempRepo();
        repo.WriteSpec("2026-08-16-card-0048-da1-answer.md", """
            # CARD-0048 — Answer OpenConsole's DA1 query

            **Status:** Plan (not implemented). 2026-08-16.
            **Card:** CARD-0048 "Modern pty: a child is silent for 2-5s".
            **Relates to:** CARD-0037 (modern backend), CARD-0045 (backend test equivalence).

            ## Problem

            CARD-0049 closed as its duplicate.
            """);

        var plan = (await repo.ListAsync()).Plans.Single();

        plan.Cards.ShouldBe(["CARD-0048"]);
        plan.MentionedCards.ShouldBe(["CARD-0037", "CARD-0045", "CARD-0049"]);
    }

    [Test]
    public void An_identifier_is_never_read_out_of_a_longer_one()
    {
        // The guard the thread depends on: CARD-0006 must not be found inside CARD-00670, and #5
        // must not be found inside #51. A substring match here would attach plans to cards they
        // never mention, and there is no foreign key anywhere to catch it.
        PlanCatalogService.CardsIn("CARD-00670 and CARD-0006").ShouldBe(["CARD-0006"]);
        PlanCatalogService.CardsIn("card-0035 lowercase counts").ShouldBe(["CARD-0035"]);
        PlanCatalogService.CardsIn("SUPERCARD-0035 does not").ShouldBeEmpty();
    }

    [Test]
    public async Task A_feature_proposal_is_catalogued_alongside_the_specs()
    {
        using var repo = new TempRepo();
        repo.WriteSpec("2026-08-16-card-0035-stuck-work-view.md", "# CARD-0035 — stuck work\n");
        repo.WriteProposal("010-home-tasks-section", """
            # Home: Tasks section (CARD-0002)

            **Status:** proposed

            ## Problem
            """);

        var plans = (await repo.ListAsync()).Plans;

        var proposal = plans.Single(p => p.Kind == PlanKind.Proposal);
        proposal.RelativePath.ShouldBe("docs/features/010-home-tasks-section/proposal.md");
        proposal.Cards.ShouldBe(["CARD-0002"]);
        proposal.Date.ShouldBeNull("a proposal carries no date and inventing one would sort it wrong");
        plans.Count.ShouldBe(2, "one list, one reader — the kind is a label, not a second endpoint");
    }

    [Test]
    public async Task Plans_are_newest_first_with_the_undated_ones_last()
    {
        using var repo = new TempRepo();
        repo.WriteSpec("2026-05-18-agent-queues-design.md", "# Agent Queues Design\n");
        repo.WriteSpec("2026-08-16-card-0035-stuck-work-view.md", "# CARD-0035\n");
        repo.WriteProposal("010-home-tasks-section", "# Home tasks\n");

        var order = (await repo.ListAsync()).Plans.Select(p => p.FileName).ToList();

        order.ShouldBe([
            "2026-08-16-card-0035-stuck-work-view.md",
            "2026-05-18-agent-queues-design.md",
            "proposal.md",
        ]);
    }

    // ---- 2. content, and the refusals that make serving it safe ---------------------------------

    [Test]
    public async Task Content_comes_back_verbatim_with_the_summary_that_was_parsed_from_it()
    {
        using var repo = new TempRepo();
        var body = "# CARD-0035 — stuck work\n\n- **Status**: Planned\n\n## Body\n\nText.\n";
        repo.WriteSpec("2026-08-16-card-0035-stuck-work-view.md", body);

        var read = await repo.Service.ReadAsync(
            repo.Root, "docs/superpowers/specs/2026-08-16-card-0035-stuck-work-view.md", CancellationToken.None);

        read.Content.ShouldBe(body);
        read.Plan.Cards.ShouldBe(["CARD-0035"]);
    }

    [Test]
    [Arguments("../secret.md")]
    [Arguments("../../secret.md")]
    [Arguments("docs/superpowers/specs/../../../secret.md")]
    [Arguments("docs/superpowers/specs/../../../../secret.md")]
    public async Task A_path_that_climbs_out_of_the_plan_roots_is_refused(string escape)
    {
        // The target really exists, so a pass here cannot be an accident of the file being missing.
        using var repo = new TempRepo();
        repo.WriteSpec("2026-08-16-card-0035-stuck-work-view.md", "# CARD-0035\n");
        var secret = repo.WriteOutsideRoots("secret.md", "credentials");
        File.Exists(secret).ShouldBeTrue();

        var thrown = await Should.ThrowAsync<ValidationException>(
            () => repo.Service.ReadAsync(repo.Root, escape, CancellationToken.None));

        thrown.StatusCode.ShouldBe(422, "a 404 would tell a prober another path might have worked");
    }

    [Test]
    public async Task An_absolute_path_is_refused_even_when_it_names_a_real_plan()
    {
        using var repo = new TempRepo();
        var real = repo.WriteSpec("2026-08-16-card-0035-stuck-work-view.md", "# CARD-0035\n");

        await Should.ThrowAsync<ValidationException>(
            () => repo.Service.ReadAsync(repo.Root, real, CancellationToken.None));
    }

    [Test]
    public async Task A_file_inside_the_repo_but_outside_the_plan_roots_is_refused()
    {
        // The roots are the boundary, not the repo: this endpoint is not a file browser.
        using var repo = new TempRepo();
        repo.WriteSpec("2026-08-16-card-0035-stuck-work-view.md", "# CARD-0035\n");
        repo.WriteInsideRepo("docs/adr/0002-modern-conpty-backend.md", "# ADR\n");

        await Should.ThrowAsync<ValidationException>(
            () => repo.Service.ReadAsync(repo.Root, "docs/adr/0002-modern-conpty-backend.md", CancellationToken.None));
    }

    [Test]
    public async Task A_non_markdown_file_inside_the_roots_is_refused()
    {
        using var repo = new TempRepo();
        repo.WriteSpec("2026-08-16-card-0035-stuck-work-view.md", "# CARD-0035\n");
        repo.WriteInsideRepo("docs/superpowers/specs/notes.env", "TOKEN=1");

        await Should.ThrowAsync<ValidationException>(
            () => repo.Service.ReadAsync(repo.Root, "docs/superpowers/specs/notes.env", CancellationToken.None));
    }

    [Test]
    public async Task A_well_formed_name_with_no_file_behind_it_is_a_not_found()
    {
        // The other half of the refusal contract: inside the roots, a missing plan is honestly 404.
        using var repo = new TempRepo();
        repo.WriteSpec("2026-08-16-card-0035-stuck-work-view.md", "# CARD-0035\n");

        await Should.ThrowAsync<NotFoundException>(
            () => repo.Service.ReadAsync(repo.Root, "docs/superpowers/specs/never-written.md", CancellationToken.None));
    }

    // ---- 3. root resolution and caching ---------------------------------------------------------

    [Test]
    public async Task A_root_that_holds_no_plans_is_reported_absent_rather_than_empty()
    {
        // The runnerConsulted distinction: "this repo has no plans" and "nobody found the repo" are
        // different answers, and a client that collapses them shows a confident empty state over a
        // broken lookup.
        using var empty = new TempRepo(createPlanRoots: false);

        var catalog = await empty.ListAsync();

        catalog.RootResolved.ShouldBeFalse();
        catalog.Root.ShouldBeNull();
        catalog.Plans.ShouldBeEmpty();
    }

    [Test]
    public async Task A_subdirectory_resolves_to_the_checkout_that_holds_the_plans()
    {
        using var repo = new TempRepo();
        repo.WriteSpec("2026-08-16-card-0035-stuck-work-view.md", "# CARD-0035\n");
        var deep = Path.Combine(repo.Root, "server", "Application", "Services");
        Directory.CreateDirectory(deep);

        var catalog = await repo.Service.ListAsync(deep, CancellationToken.None);

        catalog.RootResolved.ShouldBeTrue();
        catalog.Root.ShouldBe(repo.Root);
        catalog.Plans.Count.ShouldBe(1);
    }

    [Test]
    public async Task The_catalog_is_cached_per_root_and_clear_reopens_it()
    {
        // A phone polling a thread must not stat two dozen files per tap; Clear is what the shared
        // test factory calls between tests so one test's catalog never leaks into the next.
        using var repo = new TempRepo();
        repo.WriteSpec("2026-08-16-card-0035-stuck-work-view.md", "# CARD-0035\n");

        (await repo.ListAsync()).Plans.Count.ShouldBe(1);
        repo.WriteSpec("2026-08-17-card-0067-channel-replies.md", "# CARD-0067\n");
        (await repo.ListAsync()).Plans.Count.ShouldBe(1, "inside the TTL the answer is the cached one");

        repo.Service.Clear();

        (await repo.ListAsync()).Plans.Count.ShouldBe(2);
    }

    // ---- 4. the retroactivity claim, against this repo's own corpus -----------------------------

    [Test]
    public async Task Every_plan_already_written_in_this_repo_is_in_the_catalog()
    {
        // The claim the whole design rests on: the projection is retroactive BY CONSTRUCTION, so
        // the plans written long before it shipped are in it because they are on disk. Measured
        // 2026-08-17: 29 files (24 specs, 5 proposals), none dropped, 25 dated, 24 with a status.
        //
        // Self-calibrating on purpose — it enumerates the files itself rather than asserting a
        // count, so adding a spec cannot fail it, but a parser that started dropping one will.
        // The root resolves by walking up from the test binary, which lives inside the checkout.
        var service = new PlanCatalogService(TimeProvider.System, NullLogger<PlanCatalogService>.Instance);

        var catalog = await service.ListAsync(null, CancellationToken.None);

        catalog.RootResolved.ShouldBeTrue("the test binary lives inside a checkout that holds plans");
        var onDisk = Directory
            .EnumerateFiles(Path.Combine(catalog.Root!, "docs", "superpowers", "specs"), "*.md")
            .Concat(Directory.EnumerateFiles(
                Path.Combine(catalog.Root!, "docs", "features"), "proposal.md", SearchOption.AllDirectories))
            .Select(f => Path.GetRelativePath(catalog.Root!, f).Replace('\\', '/'))
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();

        catalog.Plans.Select(p => p.RelativePath).OrderBy(f => f, StringComparer.Ordinal).ToList()
            .ShouldBe(onDisk, "a file it cannot fully parse must still appear with what it could read");
        catalog.Plans.ShouldAllBe(p => p.Title.Length > 0, "a plan with no heading falls back to its filename");
    }

    // ---- harness ---------------------------------------------------------------------------------

    /// <summary>A throwaway checkout with the two plan roots, plus a file outside them to aim at.</summary>
    private sealed class TempRepo : IDisposable
    {
        public string Root { get; }
        public PlanCatalogService Service { get; }

        private readonly string _base;

        public TempRepo(bool createPlanRoots = true)
        {
            // Two levels: the escape tests need somewhere real ABOVE the root to try to reach.
            _base = Path.Combine(Path.GetTempPath(), $"antiphon-plans-{Guid.NewGuid():N}");
            Root = Path.Combine(_base, "checkout");
            Directory.CreateDirectory(Root);
            if (createPlanRoots)
            {
                Directory.CreateDirectory(Path.Combine(Root, "docs", "superpowers", "specs"));
                Directory.CreateDirectory(Path.Combine(Root, "docs", "features"));
            }

            Service = new PlanCatalogService(TimeProvider.System, NullLogger<PlanCatalogService>.Instance);
        }

        public Task<PlanCatalogDto> ListAsync() => Service.ListAsync(Root, CancellationToken.None);

        public string WriteSpec(string fileName, string content) =>
            Write(Path.Combine(Root, "docs", "superpowers", "specs", fileName), content);

        public string WriteProposal(string folder, string content) =>
            Write(Path.Combine(Root, "docs", "features", folder, "proposal.md"), content);

        public string WriteInsideRepo(string relativePath, string content) =>
            Write(Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar)), content);

        /// <summary>Above the checkout entirely — what a traversal is trying to reach.</summary>
        public string WriteOutsideRoots(string fileName, string content) =>
            Write(Path.Combine(_base, fileName), content);

        private static string Write(string path, string content)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
            return path;
        }

        public void Dispose()
        {
            try { Directory.Delete(_base, recursive: true); }
            catch (IOException) { /* a temp dir that outlives the test costs nothing */ }
            catch (UnauthorizedAccessException) { }
        }
    }
}
