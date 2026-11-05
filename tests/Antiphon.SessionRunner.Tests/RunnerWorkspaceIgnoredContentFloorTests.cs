using Shouldly;
using TUnit.Core;

namespace Antiphon.SessionRunner.Tests;

/// <summary>
/// CARD-1718 A-D3 round 2 (review 47d31366 B-2). The runner classifier must protect the same
/// secret floor the server defaults name, including descendants of a secret-named directory,
/// and a disposable override cannot release them.
/// </summary>
[Category("Unit")]
public sealed class RunnerWorkspaceIgnoredContentFloorTests
{
    [Test]
    [Arguments("bin/appsettings.Development.json")]
    [Arguments("bin/appsettings.Development.json/token")]
    [Arguments("bin/tls.pem")]
    [Arguments("bin/tls.key")]
    [Arguments("bin/id_rsa")]
    [Arguments("bin/cert.pfx")]
    [Arguments("bin/.npmrc")]
    [Arguments("bin/.netrc")]
    [Arguments("bin/Credentials.JSON")]
    [Arguments("bin/.aws/credentials")]
    [Arguments("bin/credentials/token")]
    [Arguments("bin/tls.pem/token")]
    [Arguments("bin/id_rsa/x")]
    [Arguments("bin/id_ed25519")]
    [Arguments("bin/store.p12")]
    [Arguments("bin/store.jks")]
    [Arguments("bin/vault.kdbx")]
    [Arguments("bin/.git-credentials")]
    [Arguments("bin/.pgpass")]
    [Arguments("bin/secrets.json")]
    [Arguments("bin/kubeconfig")]
    [Arguments("bin/.docker/config.json")]
    public void Disposable_override_cannot_release_a_secret_shaped_name(string path)
    {
        new RunnerWorkspaceIgnoredContent(["**/*"]).Classify(path).ShouldBe(RunnerIgnoredContentKind.Protected, path);
        new RunnerWorkspaceIgnoredContent().Classify(path).ShouldBe(RunnerIgnoredContentKind.Protected, path);
    }

    [Test]
    [Arguments("bin/notes.json")]
    [Arguments("bin/design.token")]
    [Arguments("bin/cache.dat")]
    public void Ordinary_ignored_build_output_stays_disposable(string path) =>
        new RunnerWorkspaceIgnoredContent().Classify(path).ShouldBe(RunnerIgnoredContentKind.Disposable, path);
}
