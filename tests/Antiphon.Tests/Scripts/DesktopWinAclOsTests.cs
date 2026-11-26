using Antiphon.Nightly;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-1744 DW-3: the production Windows ACL seam of desktop-win-phase1-secrets (DesktopWinAclOs, unit 51 of
/// docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md) on files in a temporary root: a fresh file is not owner-only, the
/// owner-only ACL is protected with one full-control rule for the user, a saved ACL comes back, and the owner reads as Get-Acl's.
/// Windows only (the helper's Windows branch); nothing outside the temporary root is touched.
/// </summary>
[Category("Unit")]
public sealed class DesktopWinAclOsTests
{
    [Test]
    public void The_owner_only_acl_is_applied_checked_and_restored_on_a_temporary_file()
    {
        if (!OperatingSystem.IsWindows()) Skip.Test("CARD-1744: the helper's ACL branch runs on Windows only");
        using var world = new OperatorToolWorld("dw3-acl");
        var file = world.PathOf("secret.dw3part");
        File.WriteAllText(file, "");
        var who = Environment.UserDomainName + "\\" + Environment.UserName;
        var acl = new DesktopWinAclOs();

        acl.IsOwnerOnly(file, who).ShouldBeFalse("a new file inherits its directory's rules");
        var saved = acl.Save(file);
        acl.ProtectOwnerOnly(file, who);
        acl.IsOwnerOnly(file, who).ShouldBeTrue();
        acl.IsOwnerOnly(file, "FIXTURE\\someone-else").ShouldBeFalse();
        File.WriteAllText(file, "still writable by its owner");

        acl.Restore(file, saved);
        acl.IsOwnerOnly(file, who).ShouldBeFalse("the saved rules are back");

        // The user, or BUILTIN\Administrators when an elevated member of that group made the directory (the helper refuses that).
        var owner = acl.Owner(world.Root);
        owner.ShouldNotBeNullOrWhiteSpace();
        owner[(owner.LastIndexOf('\\') + 1)..].ShouldBeOneOf([Environment.UserName, "Administrators"], owner);
    }
}
