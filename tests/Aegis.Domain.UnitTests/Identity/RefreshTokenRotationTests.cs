using Aegis.Domain.Identity;
using Aegis.Domain.Identity.Events;
using Aegis.Domain.Identity.ValueObjects;

namespace Aegis.Domain.UnitTests.Identity;

/// <summary>
/// Covers refresh token rotation and reuse detection.
/// </summary>
/// <remarks>
/// The behaviour under test is what bounds the damage of a leaked refresh token. Without rotation,
/// a stolen token is a renewable session for its full lifetime; without reuse detection, the theft
/// is never noticed at all.
/// </remarks>
public sealed class RefreshTokenRotationTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 2, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Lifetime = TimeSpan.FromDays(7);

    private static User ActiveUser()
    {
        var user = User.Register(
            Guid.CreateVersion7(),
            EmailAddress.Create("tech@utility.gov").Value,
            PasswordHash.FromEncoded("pbkdf2$600000$salt$hash"),
            "Sam",
            "Okoye");

        user.ConfirmEmail();
        user.ClearDomainEvents();

        return user;
    }

    [Fact]
    public void An_issued_token_is_active_until_it_expires()
    {
        var token = RefreshToken.Issue("hash-1", Now, Lifetime, "10.0.0.1");

        token.IsActive(Now).ShouldBeTrue();
        token.IsActive(Now.AddDays(6)).ShouldBeTrue();
        token.IsExpired(Now.AddDays(8)).ShouldBeTrue();
        token.IsActive(Now.AddDays(8)).ShouldBeFalse();
    }

    [Fact]
    public void Issuing_a_token_with_a_non_positive_lifetime_is_a_programming_error()
    {
        Should.Throw<Domain.Common.DomainException>(
            () => RefreshToken.Issue("hash", Now, TimeSpan.Zero, null));
    }

    [Fact]
    public void Rotation_issues_a_replacement_and_retires_the_presented_token()
    {
        var user = ActiveUser();
        user.AddRefreshToken(RefreshToken.Issue("hash-1", Now, Lifetime, "10.0.0.1"));

        var result = user.RotateRefreshToken("hash-1", "hash-2", Now.AddHours(1), Lifetime, "10.0.0.1");

        result.IsSuccess.ShouldBeTrue();
        result.Value.TokenHash.ShouldBe("hash-2");

        var original = user.RefreshTokens.Single(t => t.TokenHash == "hash-1");
        original.IsActive(Now.AddHours(1)).ShouldBeFalse();
        original.WasRotated.ShouldBeTrue();
        original.ReplacedByTokenHash.ShouldBe("hash-2");
    }

    [Fact]
    public void An_unknown_token_is_rejected()
    {
        var user = ActiveUser();

        var result = user.RotateRefreshToken("never-issued", "hash-2", Now, Lifetime, null);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("RefreshToken.NotFound");
    }

    [Fact]
    public void An_expired_token_is_rejected()
    {
        var user = ActiveUser();
        user.AddRefreshToken(RefreshToken.Issue("hash-1", Now, Lifetime, null));

        var result = user.RotateRefreshToken("hash-1", "hash-2", Now.AddDays(8), Lifetime, null);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("RefreshToken.Inactive");
    }

    [Fact]
    public void Replaying_a_rotated_token_revokes_the_entire_chain()
    {
        // The core security behaviour. A correctly behaving client discards a token the instant it
        // exchanges it, so seeing it again means two parties hold it.
        var user = ActiveUser();
        user.AddRefreshToken(RefreshToken.Issue("hash-1", Now, Lifetime, "10.0.0.1"));

        user.RotateRefreshToken("hash-1", "hash-2", Now.AddHours(1), Lifetime, "10.0.0.1");
        user.RotateRefreshToken("hash-2", "hash-3", Now.AddHours(2), Lifetime, "10.0.0.1");
        user.ClearDomainEvents();

        // The attacker replays the stolen, already-rotated first token.
        var result = user.RotateRefreshToken("hash-1", "hash-4", Now.AddHours(3), Lifetime, "203.0.113.9");

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("RefreshToken.Reused");

        // Both parties are signed out. Inconveniencing the legitimate user beats leaving an
        // attacker with an indefinitely renewable session.
        user.RefreshTokens.ShouldAllBe(t => !t.IsActive(Now.AddHours(3)));
    }

    [Fact]
    public void Reuse_detection_raises_an_event_carrying_the_attacker_address()
    {
        var user = ActiveUser();
        user.AddRefreshToken(RefreshToken.Issue("hash-1", Now, Lifetime, "10.0.0.1"));
        user.RotateRefreshToken("hash-1", "hash-2", Now.AddHours(1), Lifetime, "10.0.0.1");
        user.ClearDomainEvents();

        user.RotateRefreshToken("hash-1", "hash-9", Now.AddHours(2), Lifetime, "203.0.113.9");

        var raised = user.DomainEvents.OfType<RefreshTokenReuseDetected>().ShouldHaveSingleItem();
        raised.UserId.ShouldBe(user.Id);
        raised.DetectedFromIpAddress.ShouldBe("203.0.113.9");
        raised.TokensRevoked.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void An_explicitly_revoked_token_is_not_treated_as_a_reuse_attempt()
    {
        // Distinguishes theft from a normal sign-out. Conflating them would raise a security alert
        // every time a user logs out and their client retries with a stale token.
        var user = ActiveUser();
        user.AddRefreshToken(RefreshToken.Issue("hash-1", Now, Lifetime, null));
        user.RevokeAllRefreshTokens("Signed out", Now.AddMinutes(5));
        user.ClearDomainEvents();

        var result = user.RotateRefreshToken("hash-1", "hash-2", Now.AddMinutes(10), Lifetime, null);

        result.Error.Code.ShouldBe("RefreshToken.Inactive");
        user.DomainEvents.OfType<RefreshTokenReuseDetected>().ShouldBeEmpty();
    }

    [Fact]
    public void A_deactivated_user_cannot_refresh_an_otherwise_valid_token()
    {
        // Deactivation must take effect immediately, not when the refresh token happens to expire.
        var user = ActiveUser();
        user.AddRefreshToken(RefreshToken.Issue("hash-1", Now, Lifetime, null));
        user.Deactivate(Now.AddMinutes(1));

        var result = user.RotateRefreshToken("hash-1", "hash-2", Now.AddMinutes(2), Lifetime, null);

        result.IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void Revocation_preserves_the_original_timestamp_when_applied_twice()
    {
        // Overwriting it would destroy the forensic record of when a session actually ended.
        var token = RefreshToken.Issue("hash-1", Now, Lifetime, null);
        var user = ActiveUser();
        user.AddRefreshToken(token);

        user.RevokeAllRefreshTokens("First", Now.AddMinutes(1));
        user.RevokeAllRefreshTokens("Second", Now.AddMinutes(5));

        token.RevokedOnUtc.ShouldBe(Now.AddMinutes(1));
        token.RevocationReason.ShouldBe("First");
    }

    [Fact]
    public void Pruning_removes_only_inactive_tokens_beyond_the_retention_window()
    {
        // Without pruning, an account used daily for two years loads a growing token collection on
        // every sign-in and eventually makes its own authentication slow.
        var user = ActiveUser();

        user.AddRefreshToken(RefreshToken.Issue("old", Now.AddDays(-90), Lifetime, null));
        user.AddRefreshToken(RefreshToken.Issue("recent-expired", Now.AddDays(-10), Lifetime, null));
        user.AddRefreshToken(RefreshToken.Issue("active", Now, Lifetime, null));

        var pruned = user.PruneRefreshTokens(Now, TimeSpan.FromDays(30));

        pruned.ShouldBe(1);
        user.RefreshTokens.Select(t => t.TokenHash).ShouldBe(["recent-expired", "active"], ignoreOrder: true);
    }
}
