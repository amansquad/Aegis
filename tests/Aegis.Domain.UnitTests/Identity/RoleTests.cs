using Aegis.Domain.Identity;

namespace Aegis.Domain.UnitTests.Identity;

public sealed class RoleTests
{
    private static readonly Guid Organization = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public void A_role_normalises_its_name_for_lookup()
    {
        var role = Role.Create(Organization, "  Field Supervisor  ").Value;

        role.Name.ShouldBe("Field Supervisor");
        role.NormalizedName.ShouldBe("FIELD SUPERVISOR");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_role_requires_a_name(string? name)
    {
        Role.Create(Organization, name).Error.Code.ShouldBe("Role.NameRequired");
    }

    [Fact]
    public void Granting_an_unrecognised_permission_is_rejected()
    {
        // A permission no code checks produces a role that appears to confer access and does not,
        // which becomes a support ticket nobody can reproduce.
        var role = Role.Create(Organization, "Custom").Value;

        var result = role.Grant("assets.deleteEverything");

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Role.UnknownPermission");
        role.Permissions.ShouldBeEmpty();
    }

    [Fact]
    public void Granting_a_recognised_permission_succeeds_and_is_idempotent()
    {
        var role = Role.Create(Organization, "Custom").Value;

        role.Grant(Permissions.Assets.View).IsSuccess.ShouldBeTrue();
        role.Grant(Permissions.Assets.View).IsSuccess.ShouldBeTrue();

        role.Permissions.Count.ShouldBe(1);
        role.HasPermission(Permissions.Assets.View).ShouldBeTrue();
    }

    [Fact]
    public void Revoking_a_permission_the_role_lacks_is_rejected()
    {
        var role = Role.Create(Organization, "Custom").Value;

        role.Revoke(Permissions.Assets.View).Error.Code.ShouldBe("Role.PermissionNotGranted");
    }

    [Fact]
    public void Replacing_the_permission_set_is_all_or_nothing()
    {
        // A request containing one bad name must leave the role untouched. A half-applied
        // permission change is worse than a rejected one: nobody knows which half took effect.
        var role = Role.Create(Organization, "Custom").Value;
        role.Grant(Permissions.Assets.View);

        var result = role.SetPermissions([Permissions.WorkOrders.Assign, "not.a.permission"]);

        result.IsFailure.ShouldBeTrue();
        role.Permissions.ShouldBe([Permissions.Assets.View]);
    }

    [Fact]
    public void Replacing_the_permission_set_with_valid_names_succeeds()
    {
        var role = Role.Create(Organization, "Custom").Value;
        role.Grant(Permissions.Assets.View);

        var result = role.SetPermissions([Permissions.WorkOrders.Assign, Permissions.Incidents.Triage]);

        result.IsSuccess.ShouldBeTrue();
        role.Permissions.ShouldBe(
            [Permissions.WorkOrders.Assign, Permissions.Incidents.Triage],
            ignoreOrder: true);
    }

    [Fact]
    public void A_system_role_is_seeded_with_its_default_permissions()
    {
        var administrator = Role.CreateSystemRole(Organization, SystemRoles.Administrator).Value;

        administrator.IsSystemRole.ShouldBeTrue();
        administrator.Permissions.ShouldBe(Permissions.All, ignoreOrder: true);
    }

    [Fact]
    public void A_technician_role_carries_the_least_authority_that_still_works()
    {
        // A field technician's device is the one most likely to be lost, stolen, or used on an
        // untrusted network, so its default authority is deliberately narrow.
        var technician = Role.CreateSystemRole(Organization, SystemRoles.Technician).Value;

        technician.HasPermission(Permissions.WorkOrders.Complete).ShouldBeTrue();
        technician.HasPermission(Permissions.Assets.View).ShouldBeTrue();

        technician.HasPermission(Permissions.Users.Create).ShouldBeFalse();
        technician.HasPermission(Permissions.Assets.Decommission).ShouldBeFalse();
        technician.HasPermission(Permissions.WorkOrders.Approve).ShouldBeFalse();
        technician.HasPermission(Permissions.Analytics.ViewExecutive).ShouldBeFalse();
    }

    [Fact]
    public void Every_seeded_role_grants_only_recognised_permissions()
    {
        // Guards the catalogue against drift: renaming a permission constant without updating the
        // seed data would otherwise create roles granting names nothing checks.
        foreach (var (roleName, permissions) in SystemRoles.DefaultPermissions)
        {
            foreach (var permission in permissions)
            {
                Permissions.IsDefined(permission).ShouldBeTrue(
                    $"Role '{roleName}' grants unrecognised permission '{permission}'.");
            }
        }
    }

    [Theory]
    [InlineData("assets.view", true)]
    [InlineData("workorders.approve", true)]
    [InlineData("assets.nope", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void The_catalogue_recognises_exactly_the_defined_permissions(string? candidate, bool expected)
    {
        Permissions.IsDefined(candidate).ShouldBe(expected);
    }
}
