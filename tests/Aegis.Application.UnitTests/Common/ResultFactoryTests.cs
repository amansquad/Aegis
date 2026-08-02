using Aegis.Application.Common;
using Aegis.Domain.Common;

namespace Aegis.Application.UnitTests.Common;

public sealed class ResultFactoryTests
{
    private sealed record AssetDto(Guid Id, string Name);

    [Fact]
    public void Creates_a_failed_plain_result()
    {
        var error = Error.NotFound("Asset.NotFound", "Asset was not found.");

        var result = ResultFactory.Failure<Result>(error);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(error);
        result.ShouldBeOfType<Result>();
    }

    [Fact]
    public void Creates_a_failed_value_result_of_the_correct_closed_type()
    {
        var error = Error.Conflict("Asset.DuplicateSerial", "Serial number already registered.");

        var result = ResultFactory.Failure<Result<AssetDto>>(error);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(error);

        // The pipeline behaviours know the response only as `TResponse : Result`, so producing the
        // correct closed generic type is exactly what this class exists to guarantee. Returning a
        // plain Result here would fail MediatR's cast at the call site.
        result.ShouldBeOfType<Result<AssetDto>>();
    }

    [Fact]
    public void Creates_a_successful_value_result_from_a_boxed_value()
    {
        var dto = new AssetDto(Guid.CreateVersion7(), "Pump A");

        var result = ResultFactory.Success<Result<AssetDto>>(dto);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(dto);
    }

    [Fact]
    public void Resolves_the_value_type_of_a_generic_result()
    {
        ResultFactory.GetValueType(typeof(Result<AssetDto>)).ShouldBe(typeof(AssetDto));
        ResultFactory.GetValueType(typeof(Result<int>)).ShouldBe(typeof(int));
    }

    [Fact]
    public void Reports_no_value_type_for_a_plain_result()
    {
        // The caching behaviour relies on this to skip requests that carry nothing worth caching.
        ResultFactory.GetValueType(typeof(Result)).ShouldBeNull();
    }

    [Fact]
    public void Producing_a_value_result_for_a_plain_result_type_throws()
    {
        Should.Throw<InvalidOperationException>(() => ResultFactory.Success<Result>(42));
    }

    [Fact]
    public void Repeated_calls_return_equivalent_results_from_the_cached_factory()
    {
        // Guards the compiled-delegate cache: a stale or cross-wired entry would surface here as
        // the wrong type or a stale error.
        var first = ResultFactory.Failure<Result<AssetDto>>(Error.NotFound("A", "a"));
        var second = ResultFactory.Failure<Result<AssetDto>>(Error.Conflict("B", "b"));

        first.Error.Code.ShouldBe("A");
        second.Error.Code.ShouldBe("B");
        second.ShouldBeOfType<Result<AssetDto>>();
    }
}
