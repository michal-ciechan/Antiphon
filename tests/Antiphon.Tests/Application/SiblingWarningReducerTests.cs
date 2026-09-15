using Antiphon.Server.Application.Services;
using Shouldly;
using TUnit.Core;
using Candidate = Antiphon.Server.Application.Services.SiblingWarningReducer.Candidate;

namespace Antiphon.Tests.Application;

[Category("Unit")]
public class SiblingWarningReducerTests
{
    private static Candidate Tip(int id, char sha, int age = 0, string? branch = null) =>
        new(new Guid(id, 0, 0, new byte[8]), branch ?? $"branch-{id}",
            new DateTime(2026, 9, 15, 0, 0, 0, DateTimeKind.Utc).AddMinutes(age), new string(sha, 40));

    [Test]
    public void C540_IdenticalTipsCollapse()
    {
        var a = Tip(1, 'a'); var b = Tip(2, 'a', 1);
        var group = SiblingWarningReducer.Reduce([a, b], []).ShouldHaveSingleItem();
        group.Representative.ShouldBe(b);
        group.Members.Select(c => c.TaskId).Order().ShouldBe(new[] { a.TaskId, b.TaskId }.Order());
    }

    [Test]
    public void C540_RepresentativeOrderIsStable()
    {
        var a = Tip(1, 'a', 0); var b = Tip(2, 'a', 1, "Z");
        var c = Tip(3, 'a', 1, "A"); var d = Tip(4, 'a', 1, "A");
        var divergent = Tip(5, 'b', 2);
        foreach (var permutation in Permutations(new[] { a, b, c, d, divergent }))
        {
            var reduced = SiblingWarningReducer.Reduce(permutation, []);
            reduced.Select(g => g.Representative.TaskId).ShouldBe(new[] { divergent.TaskId, c.TaskId });
            reduced[1].Members.Count.ShouldBe(4);
        }
    }

    [Test]
    public void C540_AncestorChainCollapses()
    {
        var a = Tip(1, 'a', 3); var b = Tip(2, 'b', 2);
        var alias = Tip(3, 'b', 1); var c = Tip(4, 'c');
        // Deliberately omit A->C: proven reachability must supply it.
        var edges = new[] { (a.FullTipSha!, b.FullTipSha!), (b.FullTipSha!, c.FullTipSha!) };
        foreach (var input in Permutations(new[] { a, b, alias, c }))
        {
            var group = SiblingWarningReducer.Reduce(input, edges).ShouldHaveSingleItem();
            group.Representative.ShouldBe(c); group.Members.Count.ShouldBe(4);
        }
    }

    [Test]
    public void C540_DivergentTipsRemainVisible()
    {
        var a = Tip(1, 'a'); var b = Tip(2, 'b');
        SiblingWarningReducer.Reduce([a, b], []).Count.ShouldBe(2);
        // Display-prefix equality is not full object identity.
        b = b with { FullTipSha = "aaaaaaaa" + new string('b', 32) };
        SiblingWarningReducer.Reduce([a, b], []).Count.ShouldBe(2);
    }

    [Test]
    public void C540_ForkKeepsBothTips()
    {
        var a = Tip(1, 'a', 3); var b = Tip(2, 'b', 1); var c = Tip(3, 'c', 2); var d = Tip(4, 'd');
        var edges = new List<(string, string)> { (a.FullTipSha!, b.FullTipSha!), (a.FullTipSha!, c.FullTipSha!) };
        var fork = SiblingWarningReducer.Reduce([a, b, c], edges);
        fork.Select(g => g.Representative).ShouldBe(new[] { c, b });
        fork[0].Members.Select(m => m.TaskId).ShouldContain(a.TaskId);
        fork[1].Members.ShouldHaveSingleItem().ShouldBe(b);
        fork.SelectMany(g => g.Members).Select(m => m.TaskId).Distinct().Count().ShouldBe(3);
        edges.Add((b.FullTipSha!, d.FullTipSha!)); edges.Add((c.FullTipSha!, d.FullTipSha!));
        var merge = SiblingWarningReducer.Reduce([a, b, c, d], edges).ShouldHaveSingleItem();
        merge.Representative.ShouldBe(d); merge.Members.Count.ShouldBe(4);
    }

    [Test]
    public void C540_UnknownTipsRemainVisible()
    {
        var rows = new[] { Tip(1, 'a') with { FullTipSha = null }, Tip(2, 'a') with { FullTipSha = null },
            Tip(3, 'a') with { FullTipSha = "unknown" }, Tip(4, 'a') with { FullTipSha = "deadbeef" } };
        SiblingWarningReducer.Reduce(rows, [("unknown", "deadbeef")]).Count.ShouldBe(4);
        var sha256 = Tip(5, 'a') with { FullTipSha = new string('A', 64) };
        SiblingWarningReducer.Reduce([sha256, sha256 with { TaskId = Guid.NewGuid(), FullTipSha = new string('a', 64) }], [])
            .ShouldHaveSingleItem().Members.Count.ShouldBe(2);
    }

    private static IEnumerable<T[]> Permutations<T>(T[] values) => values.Length == 0
        ? [Array.Empty<T>()]
        : values.SelectMany((head, index) => Permutations(values.Where((_, i) => i != index).ToArray())
            .Select(tail => new[] { head }.Concat(tail).ToArray()));
}
