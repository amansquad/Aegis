using Aegis.Domain.Identity.ValueObjects;

namespace Aegis.Domain.UnitTests.Identity;

public sealed class EmailAddressTests
{
    [Theory]
    [InlineData("alice@utility.gov")]
    [InlineData("a.b.c@sub.domain.co.uk")]
    [InlineData("first+tag@example.com")]
    [InlineData("dispatch_01@northern-water.org")]
    public void Accepts_well_formed_addresses(string candidate)
    {
        EmailAddress.Create(candidate).IsSuccess.ShouldBeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("no-at-sign")]
    [InlineData("@nolocal.com")]
    [InlineData("trailing@")]
    [InlineData("two@@at.com")]
    [InlineData("spaces in@example.com")]
    [InlineData("no-tld@localhost")]
    public void Rejects_malformed_addresses(string? candidate)
    {
        var result = EmailAddress.Create(candidate);

        result.IsFailure.ShouldBeTrue();
        result.Error.Type.ShouldBe(Domain.Common.ErrorType.Validation);
    }

    [Fact]
    public void Rejects_an_address_beyond_the_maximum_length()
    {
        var overlong = new string('a', 250) + "@example.com";

        var result = EmailAddress.Create(overlong);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Email.TooLong");
    }

    [Fact]
    public void Normalises_case_and_surrounding_whitespace()
    {
        var result = EmailAddress.Create("  Alice@Utility.GOV  ");

        result.IsSuccess.ShouldBeTrue();
        result.Value.Value.ShouldBe("alice@utility.gov");
    }

    [Fact]
    public void Addresses_differing_only_in_case_are_the_same_address()
    {
        // The property that makes the unique index behave. Without normalisation the same person
        // can create two accounts and then wonder why their permissions disappeared.
        var lower = EmailAddress.Create("alice@utility.gov").Value;
        var mixed = EmailAddress.Create("ALICE@Utility.Gov").Value;

        lower.ShouldBe(mixed);
        lower.GetHashCode().ShouldBe(mixed.GetHashCode());
    }

    [Fact]
    public void Exposes_local_and_domain_parts()
    {
        var email = EmailAddress.Create("dispatch@northern-water.org").Value;

        email.LocalPart.ShouldBe("dispatch");
        email.Domain.ShouldBe("northern-water.org");
    }

    [Fact]
    public void Rehydration_bypasses_validation()
    {
        // A row written under yesterday's rules must stay loadable when the rules tighten,
        // otherwise a stricter regex makes existing accounts unusable rather than un-creatable.
        var rehydrated = EmailAddress.FromTrustedSource("legacy_address@internal");

        rehydrated.Value.ShouldBe("legacy_address@internal");
    }

    [Fact]
    public void A_pathological_input_does_not_hang_the_matcher()
    {
        // Guards against catastrophic backtracking. The regex carries a 250 ms timeout; this input
        // is the classic nested-repetition shape that kills naive email patterns.
        var hostile = new string('a', 200) + "!";

        Should.CompleteIn(
            () => EmailAddress.Create(hostile).IsFailure.ShouldBeTrue(),
            TimeSpan.FromSeconds(2));
    }
}
