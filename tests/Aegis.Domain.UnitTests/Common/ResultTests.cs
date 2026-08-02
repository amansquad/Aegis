using Aegis.Domain.Common;

namespace Aegis.Domain.UnitTests.Common;

public sealed class ResultTests
{
    [Fact]
    public void Success_should_report_success_and_carry_no_error()
    {
        var result = Result.Success();

        result.IsSuccess.ShouldBeTrue();
        result.IsFailure.ShouldBeFalse();
        result.Error.ShouldBe(Error.None);
    }

    [Fact]
    public void Failure_should_report_failure_and_carry_the_error()
    {
        var error = Error.NotFound("Asset.NotFound", "Asset was not found.");

        var result = Result.Failure(error);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(error);
        result.Error.Type.ShouldBe(ErrorType.NotFound);
    }

    [Fact]
    public void Success_with_value_should_expose_the_value()
    {
        var result = Result.Success("PUMP-0431");

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe("PUMP-0431");
    }

    [Fact]
    public void Accessing_value_of_a_failed_result_should_throw()
    {
        var result = Result.Failure<string>(Error.NotFound("Asset.NotFound", "Not found."));

        // Reading Value off a failure is a programming error — the caller skipped the IsSuccess
        // check. Returning default would let a null propagate silently instead of failing loudly.
        var exception = Should.Throw<InvalidOperationException>(() => _ = result.Value);
        exception.Message.ShouldContain("Asset.NotFound");
    }

    [Fact]
    public void Implicit_conversion_should_lift_a_value_into_a_successful_result()
    {
        Result<string> result = "PUMP-0431";

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe("PUMP-0431");
    }

    [Fact]
    public void Implicit_conversion_of_null_should_produce_a_failure_not_a_null_success()
    {
        // Guards the case where a handler does `return null!` and would otherwise hand the caller
        // a "successful" result whose Value is null.
        Result<string> result = (string?)null;

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Value.Null");
    }

    [Fact]
    public void Ensure_should_convert_a_null_lookup_into_the_supplied_error()
    {
        var error = Error.NotFound("Asset.NotFound", "Asset was not found.");

        Result.Ensure((string?)null, error).Error.ShouldBe(error);
        Result.Ensure("found", error).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void Constructing_an_inconsistent_result_should_throw()
    {
        // A success carrying an error, or a failure carrying none, means the factory methods were
        // bypassed. Failing at construction keeps that inconsistency from reaching a caller.
        Should.Throw<InvalidOperationException>(() => Result.Failure(Error.None));
    }
}
