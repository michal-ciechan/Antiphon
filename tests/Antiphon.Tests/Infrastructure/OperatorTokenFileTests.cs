using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using Antiphon.Server.Infrastructure.Security;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Infrastructure;

/// <summary>CARD-0653: the force-release credential is a random, owner-only file compared in constant time.</summary>
[Category("Unit")]
public class OperatorTokenFileTests
{
    [Test]
    public async Task First_use_creates_one_owner_only_random_token_and_later_reads_return_it()
    {
        var root = Path.Combine(Path.GetTempPath(), "antiphon-operator-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(root, "nested", "operator-token");
        try
        {
            var first = OperatorTokenFile.ReadOrCreate(path);
            first.Length.ShouldBe(64);
            first.ShouldMatch("^[0-9a-f]{64}$");
            OperatorTokenFile.ReadOrCreate(path).ShouldBe(first);
            File.ReadAllText(path).ShouldBe(first);

            var other = OperatorTokenFile.ReadOrCreate(Path.Combine(root, "other-token"));
            other.ShouldNotBe(first);

            if (OperatingSystem.IsWindows())
                AssertOwnerOnly(path);
            else
                File.GetUnixFileMode(path).ShouldBe(UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }

        await Task.CompletedTask;
    }

    [Test]
    public async Task Only_the_exact_token_matches()
    {
        OperatorTokenFile.Matches("abc123", "abc123").ShouldBeTrue();
        OperatorTokenFile.Matches("abc123", "abc124").ShouldBeFalse();
        OperatorTokenFile.Matches("abc123", "abc1234").ShouldBeFalse();
        OperatorTokenFile.Matches("abc123", "").ShouldBeFalse();
        OperatorTokenFile.Matches("abc123", null).ShouldBeFalse();
        OperatorTokenFile.Matches("", "").ShouldBeFalse();
        await Task.CompletedTask;
    }

    [SupportedOSPlatform("windows")]
    private static void AssertOwnerOnly(string path)
    {
        using var identity = WindowsIdentity.GetCurrent();
        var user = identity.User.ShouldNotBeNull();
        var security = new FileInfo(path).GetAccessControl(AccessControlSections.Access | AccessControlSections.Owner);
        security.AreAccessRulesProtected.ShouldBeTrue();
        security.GetOwner(typeof(SecurityIdentifier)).ShouldBe(user);
        var rules = security.GetAccessRules(includeExplicit: true, includeInherited: true, typeof(SecurityIdentifier))
            .Cast<FileSystemAccessRule>()
            .ToArray();
        rules.ShouldNotBeEmpty();
        rules.ShouldAllBe(rule => rule.AccessControlType == AccessControlType.Allow && rule.IdentityReference == user);
    }
}
