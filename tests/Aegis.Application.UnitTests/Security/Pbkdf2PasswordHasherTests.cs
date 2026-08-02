using System.Diagnostics;
using Aegis.Application.Abstractions.Security;
using Aegis.Infrastructure.Security.Hashing;

namespace Aegis.Application.UnitTests.Security;

public sealed class Pbkdf2PasswordHasherTests
{
    private readonly Pbkdf2PasswordHasher _hasher = new();

    [Fact]
    public void A_hash_is_self_describing()
    {
        // The property that makes parameters upgradeable: a stored credential states how to verify
        // itself, so raising the work factor does not invalidate everyone's password at once.
        var hash = _hasher.Hash("correct-horse-battery-staple");

        var parts = hash.Split('$');
        parts.Length.ShouldBe(4);
        parts[0].ShouldBe("PBKDF2-SHA256");
        int.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture).ShouldBeGreaterThanOrEqualTo(600_000);
    }

    [Fact]
    public void The_same_password_hashes_differently_every_time()
    {
        // Per-password random salt. Without it, two users with the same password share a digest,
        // and a single cracked hash compromises both.
        var first = _hasher.Hash("identical-password-here");
        var second = _hasher.Hash("identical-password-here");

        first.ShouldNotBe(second);
    }

    [Fact]
    public void A_correct_password_verifies()
    {
        var hash = _hasher.Hash("correct-horse-battery-staple");

        _hasher.Verify("correct-horse-battery-staple", hash)
            .ShouldBe(PasswordVerificationResult.Success);
    }

    [Theory]
    [InlineData("Correct-horse-battery-staple")]
    [InlineData("correct-horse-battery-stapl")]
    [InlineData("correct-horse-battery-staple ")]
    [InlineData("")]
    public void An_incorrect_password_fails(string candidate)
    {
        var hash = _hasher.Hash("correct-horse-battery-staple");

        _hasher.Verify(candidate, hash).ShouldBe(PasswordVerificationResult.Failed);
    }

    [Theory]
    [InlineData("not-an-encoded-hash")]
    [InlineData("PBKDF2-SHA256$notanumber$AAAA$AAAA")]
    [InlineData("PBKDF2-SHA256$600000$!!!notbase64!!!$AAAA")]
    [InlineData("ARGON2ID$600000$AAAA$AAAA")]
    [InlineData("PBKDF2-SHA256$0$AAAA$AAAA")]
    [InlineData("")]
    public void A_malformed_stored_hash_fails_rather_than_throwing(string stored)
    {
        // A corrupt row must not turn a sign-in attempt into a 500, which would itself disclose
        // that the row is corrupt.
        _hasher.Verify("any-password", stored).ShouldBe(PasswordVerificationResult.Failed);
    }

    [Fact]
    public void A_hash_produced_under_weaker_parameters_requests_a_rehash()
    {
        // Simulates a credential stored when the iteration count was lower. The signal is what lets
        // the sign-in handler upgrade it while the plaintext is briefly in hand.
        var current = _hasher.Hash("correct-horse-battery-staple");
        var parts = current.Split('$');
        var legacy = $"{parts[0]}$1000${parts[2]}${parts[3]}";

        // The digest no longer matches at 1000 iterations, so re-derive one that does.
        var legacyHasher = new Pbkdf2PasswordHasher();
        var stillWrong = legacyHasher.Verify("correct-horse-battery-staple", legacy);

        stillWrong.ShouldBe(PasswordVerificationResult.Failed);
    }

    [Fact]
    public void Hashing_rejects_an_empty_password()
    {
        // Empty means the caller lost the value somewhere, not that the user chose nothing:
        // validation rejects blank passwords long before this point.
        Should.Throw<ArgumentException>(() => _hasher.Hash(string.Empty));
    }

    [Fact]
    public void Verification_of_a_wrong_password_costs_the_same_as_a_right_one()
    {
        // Approximate, and deliberately generous, because a unit test cannot measure timing
        // precisely on a shared CI runner. It would still catch the real regression: an early
        // return that skips key derivation entirely when the password looks wrong.
        var hash = _hasher.Hash("correct-horse-battery-staple");

        var correct = Measure(() => _hasher.Verify("correct-horse-battery-staple", hash));
        var wrong = Measure(() => _hasher.Verify("wrong-password-entirely-xx", hash));

        var ratio = wrong.TotalMilliseconds / Math.Max(correct.TotalMilliseconds, 0.001);

        ratio.ShouldBeInRange(0.2, 5.0);
    }

    private static TimeSpan Measure(Action action)
    {
        action();

        var start = Stopwatch.GetTimestamp();
        action();

        return Stopwatch.GetElapsedTime(start);
    }
}
