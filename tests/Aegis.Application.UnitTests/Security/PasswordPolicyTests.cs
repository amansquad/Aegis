using Aegis.Infrastructure.Security;

namespace Aegis.Application.UnitTests.Security;

public sealed class PasswordPolicyTests
{
    private readonly PasswordPolicy _policy = new();

    [Fact]
    public void A_long_unremarkable_passphrase_is_accepted()
    {
        _policy.Screen("thistle marmalade quiet lantern").IsAcceptable.ShouldBeTrue();
    }

    [Theory]
    [InlineData("short")]
    [InlineData("elevenchars")]
    public void A_password_below_the_minimum_length_is_rejected(string candidate)
    {
        var result = _policy.Screen(candidate);

        result.IsAcceptable.ShouldBeFalse();
        result.Reason.ShouldNotBeNull().ShouldContain("12 characters");
    }

    [Theory]
    [InlineData("password1234")]
    [InlineData("Password1234")]
    [InlineData("PASSWORD1234")]
    [InlineData("qwerty123456")]
    [InlineData("welcome123456")]
    [InlineData("administrator")]
    [InlineData("correcthorsebatterystaple")]
    [InlineData("scadaoperator1")]
    [InlineData("maintenance123")]
    public void A_known_breached_password_is_rejected_regardless_of_case(string candidate)
    {
        // Case-insensitive on purpose: Password123 is not meaningfully stronger than password123,
        // and treating them differently lets a single capitalisation defeat the entire list.
        _policy.Screen(candidate).IsAcceptable.ShouldBeFalse();
    }

    [Fact]
    public void Every_banned_entry_worth_having_is_at_least_the_minimum_length()
    {
        // Guards the flaw that writing this list surfaced: with a twelve-character minimum, shorter
        // banned entries are already refused by the length rule and screen nothing. A list made
        // entirely of six-character classics would look thorough and do no work at all.
        //
        // Asserts that a meaningful number of entries are long enough to matter, rather than that
        // every entry is — the short ones are deliberately retained in case the minimum is lowered.
        var longEnough = new[]
        {
            "password1234", "qwerty123456", "administrator", "correcthorsebatterystaple",
            "scadaoperator1", "watertreatment1", "infrastructure1", "changeme1234",
        };

        foreach (var candidate in longEnough)
        {
            candidate.Length.ShouldBeGreaterThanOrEqualTo(_policy.MinimumLength);
            _policy.Screen(candidate).IsAcceptable.ShouldBeFalse(
                $"'{candidate}' is long enough to pass the length rule, so the banned list is the " +
                "only thing that can reject it.");
        }
    }

    [Theory]
    [InlineData("p-a-s-s-w-o-r-d-1-2-3")]
    [InlineData("q.w.e.r.t.y.u.i.o.p")]
    public void A_separator_disguised_breached_password_is_rejected(string candidate)
    {
        // Otherwise the list is defeated by punctuation, which is the first thing anyone tries when
        // a password is refused.
        _policy.Screen(candidate).IsAcceptable.ShouldBeFalse();
    }

    [Fact]
    public void A_single_repeated_character_is_rejected()
    {
        _policy.Screen("aaaaaaaaaaaaaaaa").IsAcceptable.ShouldBeFalse();
    }

    [Fact]
    public void A_password_containing_the_user_email_is_rejected()
    {
        // Strong-looking but personal. Anyone targeting this specific user already knows it.
        var result = _policy.Screen(
            "ada.osei-is-here-2025",
            "ada.osei@northern-water.gov");

        result.IsAcceptable.ShouldBeFalse();
        result.Reason.ShouldNotBeNull().ShouldContain("name, email address or organization");
    }

    [Fact]
    public void A_password_containing_the_organization_name_is_rejected()
    {
        _policy.Screen("northern water forever", "Northern Water").IsAcceptable.ShouldBeFalse();
    }

    [Fact]
    public void A_password_containing_the_email_domain_is_rejected()
    {
        // The address is split on separators so the domain is screened independently, rather than
        // only matching the whole address nobody would ever type verbatim.
        _policy.Screen("mysecret-northern-pass", "ada@northern.gov").IsAcceptable.ShouldBeFalse();
    }

    [Fact]
    public void Short_context_fragments_are_ignored()
    {
        // Rejecting fragments under four characters would fail a password for containing a common
        // short word from an organization's name, frustrating users without denying an attacker
        // anything.
        _policy.Screen("thistle marmalade quiet", "A B Co", "Jo", "Li").IsAcceptable.ShouldBeTrue();
    }

    [Fact]
    public void An_absurdly_long_password_is_rejected()
    {
        // PBKDF2 cost scales with input length, so an unbounded password is a cheap way to make the
        // server do expensive work on the caller's behalf.
        _policy.Screen(new string('x', 500)).IsAcceptable.ShouldBeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_password_is_rejected(string? candidate)
    {
        _policy.Screen(candidate!).IsAcceptable.ShouldBeFalse();
    }

    [Fact]
    public void Null_context_entries_are_tolerated()
    {
        _policy.Screen("thistle marmalade quiet", null, null).IsAcceptable.ShouldBeTrue();
    }
}
