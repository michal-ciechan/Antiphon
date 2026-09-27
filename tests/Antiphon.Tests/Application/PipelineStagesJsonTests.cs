using System.Security.Cryptography;
using System.Text;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Domain.ValueObjects;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Unit")]
public sealed class PipelineStagesJsonTests
{
    [Test]
    public void Serialise_is_canonical_and_hash_is_eight_lowercase_hex()
    {
        PipelineStageSpec[] first = [new(AgentTaskRole.Code, "stage-code", ["review", "CODE", "decide"])];
        PipelineStageSpec[] second = [new(AgentTaskRole.Code, "stage-code", ["decide", "code", "REVIEW"])];
        var json = PipelineStagesJson.Serialise(first);
        json.ShouldBe(PipelineStagesJson.Serialise(second));
        json.ShouldContain("\"role\":\"Code\"");
        json.ShouldContain("\"allowedNext\":[\"code\",\"decide\",\"review\"]");
        var hash = PipelineStagesJson.ContentHash(json);
        hash.ShouldBe(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)))[..8].ToLowerInvariant());
        PipelineStagesJson.Parse(json)[0].Role.ShouldBe(AgentTaskRole.Code);
    }

    [Test]
    [Arguments("[{\"role\":\"Ship\",\"bundleKey\":\"stage-code\",\"allowedNext\":[]}]")]
    [Arguments("{}")]
    [Arguments("")]
    public void Parse_rejects_unknown_role_member_and_non_array(string json)
    {
        Should.Throw<ValidationException>(() => PipelineStagesJson.Parse(json)).Code
            .ShouldBe("pipeline_stages_invalid");
    }
}
