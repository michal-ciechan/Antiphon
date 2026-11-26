using Shouldly;
using TUnit.Core;

namespace Antiphon.Agents.Pty.Tests;

/// <summary>CARD-0050 S5: process-spawning classes share a 1-wide lane.</summary>
[Category("Unit")]
public class ProcessSpawnLimitTests
{
    [Test]
    public void Caps_concurrent_process_spawning_tests_at_one()
    {
        new ProcessSpawnLimit().Limit.ShouldBe(1);
    }
}
