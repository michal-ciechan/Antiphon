using System.Text.Json;
using System.Text.Json.Serialization;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Shouldly;
using TUnit.Core;
using Antiphon.Tests.Application;

namespace Antiphon.Tests.Invariants;

[Category("Unit")]
[Category("RepositoryInvariant")]
public sealed class InstructionFileStampInvariantTests
{
    [Test]
    public void Shipped_instruction_files_match_the_ordered_plan_list()
    {
        PolicyRefreshSettings.DefaultInstructionFiles.ShouldBe([
            "CLAUDE.md",
            "AGENTS.md",
            "docs/orchestration-loop.md",
            "docs/agent-card-lifecycle.md",
            "docs/ops-http.md",
            ".claude/skills/antiphon-delegate/SKILL.md",
        ]);
    }
}
