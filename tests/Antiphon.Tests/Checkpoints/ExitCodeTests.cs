using Antiphon.Checkpoints;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Checkpoints;

[Category("Unit")]
public sealed class ExitCodeTests
{
    [Test]
    [Arguments(2, 1, 2)]
    [Arguments(6, 2, 6)]
    [Arguments(4, 6, 6)]
    [Arguments(5, 4, 4)]
    [Arguments(1, 5, 5)]
    [Arguments(3, 1, 1)]
    [Arguments(0, 3, 3)]
    public async Task run_exit_is_the_highest_precedence_row_state(int left, int right, int expected)
    {
        ExitCodes.FromRowStates([left, right]).ShouldBe(expected);
        await Task.CompletedTask;
    }
}
