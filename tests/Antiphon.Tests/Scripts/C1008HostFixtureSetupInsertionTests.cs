using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-1673 (CARD-1665 M65-X). <see cref="C1008HostFixture.InsertSetupAtEntry"/> puts the fixture's setup before the
/// script's one column-0 <c>trap 'ec=$?;</c> line and nowhere else. No such line, or a second one, throws naming the
/// count: without the guard the setup would land inside a function (a misleading WrongLane diagnosis) or beside the
/// wrong trap (silently).
/// </summary>
[Timeout(60_000)]
[Category("Unit")]
public sealed class C1008HostFixtureSetupInsertionTests
{
    private const string Setup = "SETUP_MARKER=1";

    [Test]
    public void One_column_zero_trap_gets_the_setup_once_and_indented_traps_none()
    {
        var source = """
            #!/bin/bash
            audit() {
                trap 'ec=$?; inner' EXIT
            }
            trap 'ec=$?; outer' EXIT
            main
            """.ReplaceLineEndings("\n");

        var result = C1008HostFixture.InsertSetupAtEntry(source, Setup);

        result.Split('\n').Count(line => line == Setup).ShouldBe(1);
        result.ShouldContain(Setup + "\ntrap 'ec=$?; outer' EXIT");
        result.ShouldContain("{\n    trap 'ec=$?; inner' EXIT\n}");
        result.Replace(Setup + "\n", "").ShouldBe(source);
    }

    [Test]
    public void No_column_zero_trap_throws_naming_zero()
    {
        var source = """
            audit() {
                trap 'ec=$?; inner' EXIT
            }
            main
            """.ReplaceLineEndings("\n");

        var thrown = Should.Throw<InvalidOperationException>(() => C1008HostFixture.InsertSetupAtEntry(source, Setup));
        thrown.Message.ShouldEndWith("found 0");
    }

    [Test]
    public void Two_column_zero_traps_throw_naming_two()
    {
        var source = """
            audit() {
            trap 'ec=$?; dedented' EXIT
            }
            trap 'ec=$?; outer' EXIT
            main
            """.ReplaceLineEndings("\n");

        var thrown = Should.Throw<InvalidOperationException>(() => C1008HostFixture.InsertSetupAtEntry(source, Setup));
        thrown.Message.ShouldEndWith("found 2");
    }
}
