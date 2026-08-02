using Aegis.Domain.Identity;
using Aegis.Domain.Identity.Events;
using Aegis.Domain.Identity.ValueObjects;

namespace Aegis.Domain.UnitTests.Identity;

public sealed class UserTests
{
    private static readonly Guid Organization = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateTimeOffset Now = new(2026, 8, 2, 12, 0, 0, TimeSpan.Zero);

    private static User Register(string email = "alice@utility.gov") =>
        User.Register(
            Organization,
            EmailAddress.Create(email).Value,
            PasswordHash.FromEncoded("pbkdf2$600000$salt$hash"),
            "Alice",
            "Nkemelu");

    private static User Active()
    {
        var user = Register();
        user.ConfirmEmail();
        user.ClearDomainEvents();
        return user;
    }

    [Fact]
    public void A_new_user_starts_pending_and_cannot_sign_in()
    {
        var user = Register();

        user.Status.ShouldBe(UserStatus.Pending);
        user.EmailConfirmed.ShouldBeFalse();
        user.CanSignIn(Now).ShouldBeFalse();
    }

    [Fact]
    public void Registration_raises_an_event_carrying_the_address_as_it_was()
    {
        var user = Register();

        var raised = user.DomainEvents.OfType<UserRegistered>().ShouldHaveSingleItem();
        raised.UserId.ShouldBe(user.Id);
        raised.OrganizationId.ShouldBe(Organization);
        raised.Email.ShouldBe("alice@utility.gov");
        raised.DisplayName.ShouldBe("Alice Nkemelu");
    }

    [Fact]
    public void Confirming_the_email_activates_a_pending_account()
    {
        var user = Register();

        user.ConfirmEmail().IsSuccess.ShouldBeTrue();

        user.Status.ShouldBe(UserStatus.Active);
        user.CanSignIn(Now).ShouldBeTrue();
    }

    [Fact]
    public void Confirming_an_already_confirmed_email_is_rejected()
    {
        var user = Active();

        var result = user.ConfirmEmail();

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("User.EmailAlreadyConfirmed");
    }

    // ---- Lockout ----

    [Fact]
    public void Failed_attempts_below_the_threshold_do_not_lock_the_account()
    {
        var user = Active();

        user.RecordFailedSignIn(Now, maxAttempts: 5, TimeSpan.FromMinutes(15));
        user.RecordFailedSignIn(Now, maxAttempts: 5, TimeSpan.FromMinutes(15));

        user.FailedSignInAttempts.ShouldBe(2);
        user.IsLockedOut(Now).ShouldBeFalse();
        user.CanSignIn(Now).ShouldBeTrue();
    }

    [Fact]
    public void Reaching_the_threshold_locks_the_account_and_raises_an_event()
    {
        var user = Active();

        for (var i = 0; i < 5; i++)
        {
            user.RecordFailedSignIn(Now, maxAttempts: 5, TimeSpan.FromMinutes(15));
        }

        user.IsLockedOut(Now).ShouldBeTrue();
        user.CanSignIn(Now).ShouldBeFalse();

        var raised = user.DomainEvents.OfType<UserLockedOut>().ShouldHaveSingleItem();
        raised.FailedAttempts.ShouldBe(5);
        raised.LockoutEndsOnUtc.ShouldBe(Now.AddMinutes(15));
    }

    [Fact]
    public void The_lockout_expires_on_its_own()
    {
        // Temporary rather than permanent on purpose: a permanent lock turns an attacker's failed
        // guesses into a denial-of-service against the legitimate user.
        var user = Active();

        for (var i = 0; i < 5; i++)
        {
            user.RecordFailedSignIn(Now, maxAttempts: 5, TimeSpan.FromMinutes(15));
        }

        user.IsLockedOut(Now.AddMinutes(14)).ShouldBeTrue();
        user.IsLockedOut(Now.AddMinutes(16)).ShouldBeFalse();
        user.CanSignIn(Now.AddMinutes(16)).ShouldBeTrue();
    }

    [Fact]
    public void A_failure_after_a_lockout_expires_starts_a_fresh_count()
    {
        // Otherwise a single typo after the lockout lifts would immediately re-lock the account,
        // because the old counter was still sitting at the threshold.
        var user = Active();

        for (var i = 0; i < 5; i++)
        {
            user.RecordFailedSignIn(Now, maxAttempts: 5, TimeSpan.FromMinutes(15));
        }

        user.RecordFailedSignIn(Now.AddMinutes(20), maxAttempts: 5, TimeSpan.FromMinutes(15));

        user.FailedSignInAttempts.ShouldBe(1);
        user.IsLockedOut(Now.AddMinutes(20)).ShouldBeFalse();
    }

    [Fact]
    public void A_successful_sign_in_clears_the_failure_count()
    {
        var user = Active();
        user.RecordFailedSignIn(Now, maxAttempts: 5, TimeSpan.FromMinutes(15));

        user.RecordSuccessfulSignIn(Now).IsSuccess.ShouldBeTrue();

        user.FailedSignInAttempts.ShouldBe(0);
        user.LockoutEndsOnUtc.ShouldBeNull();
        user.LastSignedInOnUtc.ShouldBe(Now);
    }

    [Fact]
    public void A_locked_out_account_cannot_record_a_successful_sign_in()
    {
        var user = Active();

        for (var i = 0; i < 5; i++)
        {
            user.RecordFailedSignIn(Now, maxAttempts: 5, TimeSpan.FromMinutes(15));
        }

        var result = user.RecordSuccessfulSignIn(Now);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("User.CannotSignIn");
    }

    // ---- Security stamp ----

    [Fact]
    public void Changing_the_password_rotates_the_security_stamp()
    {
        // The stamp is embedded in access tokens, which cannot be recalled. Rotating it is what
        // makes a password change invalidate tokens that are still within their lifetime.
        var user = Active();
        var before = user.SecurityStamp;

        user.ChangePassword(PasswordHash.FromEncoded("pbkdf2$600000$newsalt$newhash"), Now);

        user.SecurityStamp.ShouldNotBe(before);
    }

    [Fact]
    public void Changing_the_password_revokes_every_active_session()
    {
        // The entire point of changing a password after a suspected compromise. Leaving sessions
        // alive means the attacker keeps the access the user believes they just removed.
        var user = Active();
        user.AddRefreshToken(RefreshToken.Issue("hash-a", Now, TimeSpan.FromDays(7), "10.0.0.1"));
        user.AddRefreshToken(RefreshToken.Issue("hash-b", Now, TimeSpan.FromDays(7), "10.0.0.2"));

        user.ChangePassword(PasswordHash.FromEncoded("pbkdf2$600000$s$h"), Now);

        user.RefreshTokens.ShouldAllBe(t => !t.IsActive(Now));
    }

    [Fact]
    public void Changing_the_password_clears_an_active_lockout()
    {
        var user = Active();

        for (var i = 0; i < 5; i++)
        {
            user.RecordFailedSignIn(Now, maxAttempts: 5, TimeSpan.FromMinutes(15));
        }

        user.ChangePassword(PasswordHash.FromEncoded("pbkdf2$600000$s$h"), Now);

        user.IsLockedOut(Now).ShouldBeFalse();
        user.FailedSignInAttempts.ShouldBe(0);
    }

    [Fact]
    public void Assigning_and_removing_a_role_rotates_the_security_stamp()
    {
        // A permission change must invalidate tokens carrying the old permission set, otherwise a
        // revoked capability lingers until the access token expires.
        var user = Active();
        var roleId = Guid.CreateVersion7();

        var afterRegister = user.SecurityStamp;
        user.AssignRole(roleId);
        var afterAssign = user.SecurityStamp;
        user.RemoveRole(roleId);

        afterAssign.ShouldNotBe(afterRegister);
        user.SecurityStamp.ShouldNotBe(afterAssign);
    }

    // ---- Roles ----

    [Fact]
    public void A_role_cannot_be_assigned_twice()
    {
        var user = Active();
        var roleId = Guid.CreateVersion7();

        user.AssignRole(roleId).IsSuccess.ShouldBeTrue();
        var second = user.AssignRole(roleId);

        second.IsFailure.ShouldBeTrue();
        second.Error.Code.ShouldBe("User.RoleAlreadyAssigned");
        user.RoleIds.Count.ShouldBe(1);
    }

    [Fact]
    public void Removing_a_role_the_user_does_not_hold_is_rejected()
    {
        var result = Active().RemoveRole(Guid.CreateVersion7());

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("User.RoleNotAssigned");
    }

    // ---- Lifecycle ----

    [Fact]
    public void Deactivation_ends_every_session_and_blocks_sign_in()
    {
        var user = Active();
        user.AddRefreshToken(RefreshToken.Issue("hash-a", Now, TimeSpan.FromDays(7), null));

        user.Deactivate(Now, "Left the organization").IsSuccess.ShouldBeTrue();

        user.Status.ShouldBe(UserStatus.Deactivated);
        user.CanSignIn(Now).ShouldBeFalse();
        user.RefreshTokens.ShouldAllBe(t => !t.IsActive(Now));
    }

    [Fact]
    public void Reactivating_an_unconfirmed_account_returns_it_to_pending()
    {
        // Reactivation must not be a way to skip the confirmation the account never completed.
        var user = Register();
        user.Deactivate(Now);

        user.Reactivate().IsSuccess.ShouldBeTrue();

        user.Status.ShouldBe(UserStatus.Pending);
        user.CanSignIn(Now).ShouldBeFalse();
    }

    [Fact]
    public void Reactivating_a_confirmed_account_restores_it_to_active()
    {
        var user = Active();
        user.Deactivate(Now);

        user.Reactivate().IsSuccess.ShouldBeTrue();

        user.Status.ShouldBe(UserStatus.Active);
        user.CanSignIn(Now).ShouldBeTrue();
    }

    [Fact]
    public void A_soft_deleted_user_cannot_sign_in()
    {
        var user = Active();

        user.IsDeleted = true;

        user.CanSignIn(Now).ShouldBeFalse();
    }
}
