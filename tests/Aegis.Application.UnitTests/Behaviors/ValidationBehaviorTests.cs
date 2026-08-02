using Aegis.Application.Behaviors;
using Aegis.Application.Messaging;
using Aegis.Domain.Common;
using FluentValidation;

namespace Aegis.Application.UnitTests.Behaviors;

public sealed class ValidationBehaviorTests
{
    private sealed record CreateAssetCommand(string Name, int Capacity) : ICommand<Guid>;

    private sealed class NameValidator : AbstractValidator<CreateAssetCommand>
    {
        public NameValidator() =>
            RuleFor(c => c.Name).NotEmpty().WithMessage("Name is required.");
    }

    private sealed class CapacityValidator : AbstractValidator<CreateAssetCommand>
    {
        public CapacityValidator() =>
            RuleFor(c => c.Capacity).GreaterThan(0).WithMessage("Capacity must be positive.");
    }

    private static ValidationBehavior<CreateAssetCommand, Result<Guid>> Behavior(
        params IValidator<CreateAssetCommand>[] validators) => new(validators);

    [Fact]
    public async Task Passes_through_when_no_validators_are_registered()
    {
        var expected = Guid.CreateVersion7();
        var handlerRan = false;

        var result = await Behavior().Handle(
            new CreateAssetCommand("", -1),
            () =>
            {
                handlerRan = true;
                return Task.FromResult(Result.Success(expected));
            },
            CancellationToken.None);

        handlerRan.ShouldBeTrue();
        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(expected);
    }

    [Fact]
    public async Task Invokes_the_handler_when_validation_passes()
    {
        var handlerRan = false;

        var result = await Behavior(new NameValidator(), new CapacityValidator()).Handle(
            new CreateAssetCommand("Pump A", 500),
            () =>
            {
                handlerRan = true;
                return Task.FromResult(Result.Success(Guid.CreateVersion7()));
            },
            CancellationToken.None);

        handlerRan.ShouldBeTrue();
        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task Short_circuits_without_invoking_the_handler_when_validation_fails()
    {
        var handlerRan = false;

        var result = await Behavior(new NameValidator()).Handle(
            new CreateAssetCommand("", 500),
            () =>
            {
                handlerRan = true;
                return Task.FromResult(Result.Success(Guid.CreateVersion7()));
            },
            CancellationToken.None);

        // The point of validating in the pipeline: an invalid command never reaches the handler,
        // so no transaction is opened and no partial work is done.
        handlerRan.ShouldBeFalse();
        result.IsFailure.ShouldBeTrue();
    }

    [Fact]
    public async Task Returns_a_failure_rather_than_throwing()
    {
        // The design choice under test. A thrown ValidationException would make every invalid
        // request pay a stack unwind and would hide the failure from the handler's signature.
        var result = await Behavior(new NameValidator()).Handle(
            new CreateAssetCommand("", 500),
            () => Task.FromResult(Result.Success(Guid.CreateVersion7())),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Type.ShouldBe(ErrorType.Validation);
        result.Error.Code.ShouldBe("Validation.Failed");
    }

    [Fact]
    public async Task Aggregates_failures_from_every_validator_rather_than_stopping_at_the_first()
    {
        // A form with two bad fields should report both, not force the user through two round trips.
        var result = await Behavior(new NameValidator(), new CapacityValidator()).Handle(
            new CreateAssetCommand("", -5),
            () => Task.FromResult(Result.Success(Guid.CreateVersion7())),
            CancellationToken.None);

        result.Error.Details.ShouldNotBeNull();
        result.Error.Details.Count.ShouldBe(2);
        result.Error.Details.Keys.ShouldContain(nameof(CreateAssetCommand.Name));
        result.Error.Details.Keys.ShouldContain(nameof(CreateAssetCommand.Capacity));
        result.Error.Details[nameof(CreateAssetCommand.Name)].ShouldContain("Name is required.");
        result.Error.Details[nameof(CreateAssetCommand.Capacity)].ShouldContain("Capacity must be positive.");
    }

    [Fact]
    public async Task Groups_multiple_messages_for_one_property_under_a_single_key()
    {
        var strict = new InlineValidator<CreateAssetCommand>();
        strict.RuleFor(c => c.Name).NotEmpty().WithMessage("Name is required.");
        strict.RuleFor(c => c.Name).MinimumLength(3).WithMessage("Name is too short.");

        var result = await Behavior(strict).Handle(
            new CreateAssetCommand("", 10),
            () => Task.FromResult(Result.Success(Guid.CreateVersion7())),
            CancellationToken.None);

        result.Error.Details.ShouldNotBeNull();
        result.Error.Details[nameof(CreateAssetCommand.Name)].Length.ShouldBe(2);
    }
}
